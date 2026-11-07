namespace Timberborn.TerrainGenerator.Generators.Wfc;

// The N x N x N voxel patterns learned from source maps (overlapping model), how often each occurs at each height
// layer, and which may be placed next to each other in each direction.
// Patterns are height-aware, softly: every pattern may appear at every layer, but its weight there falls off by the
// height penalty for each layer away from where it was seen in the sources. Without this, small patterns cannot tell
// how high they are, and cliff edges stack into endless spires.
public sealed class WfcPatternSet
{
    // directions: +x, -x, +y, -y, +z, -z; the opposite of direction d is d ^ 1
    internal static readonly (int X, int Y, int Z)[] Directions =
    {
        (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)
    };

    private WfcPatternSet(int size, ulong[] codes, double[][] layerWeights)
    {
        Size = size;
        Codes = codes;
        LayerWeights = layerWeights;
        Words = (codes.Length + 63) / 64;
        Propagator = new ulong[codes.Length * Directions.Length][];
    }

    public int Size { get; }
    public int Count => Codes.Length;
    public int LayerCount => LayerWeights.Length;
    internal int Words { get; }

    // pattern voxels as bits, bit (dz * Size + dy) * Size + dx
    internal ulong[] Codes { get; }

    // [layer][pattern]: how likely the pattern is chosen at that height layer (always above 0)
    internal double[][] LayerWeights { get; }

    // [pattern * 6 + direction]: bitset of patterns that may be placed next to the pattern in that direction
    internal ulong[][] Propagator { get; }

    internal static int Bit(int size, int dx, int dy, int dz)
    {
        return (dz * size + dy) * size + dx;
    }

    // output cells above the highest source layer use the source's top layer
    internal int LayerOf(int z)
    {
        return Math.Min(z, LayerCount - 1);
    }

    // symmetry adds rotations around the vertical axis and mirror images; patterns are never turned upside down;
    // heightPenalty (0..1) multiplies a pattern's weight for each layer away from where it was seen;
    // coverage (0..1] keeps only the most frequent patterns that together make up that share of all occurrences, as
    // rare patterns are most of the count but little of the terrain, and the solver's cost grows with the count
    public static WfcPatternSet Learn(IEnumerable<VoxelMap> sources, int size, bool symmetry, bool periodicInput,
        double heightPenalty, double coverage, Action<float>? progress = null)
    {
        var layerCounts = CountPatterns(sources, size, symmetry, periodicInput,
            fraction => progress?.Invoke(0.5f * fraction));
        var layers = layerCounts.Values.First().Length;
        var byFrequency = layerCounts.Select(pair => (Code: pair.Key, Counts: pair.Value, Total: pair.Value.Sum()))
            .OrderByDescending(pattern => pattern.Total).ThenBy(pattern => pattern.Code).ToArray();
        var occurrences = byFrequency.Sum(pattern => (long)pattern.Total);
        var kept = 0;
        for (long covered = 0; kept < byFrequency.Length && covered < coverage * occurrences; kept++)
            covered += byFrequency[kept].Total;
        var ordered = byFrequency.Take(Math.Max(1, kept))
            .Select(pattern => new KeyValuePair<ulong, int[]>(pattern.Code, pattern.Counts)).ToArray();
        var codes = ordered.Select(pair => pair.Key).ToArray();
        var layerWeights = new double[layers][];
        for (var layer = 0; layer < layers; layer++)
        {
            layerWeights[layer] = new double[codes.Length];
            for (var p = 0; p < codes.Length; p++)
            {
                var counts = ordered[p].Value;
                var weight = 0.0;
                for (var seenAt = 0; seenAt < layers; seenAt++)
                    if (counts[seenAt] > 0)
                        weight += counts[seenAt] * Math.Pow(heightPenalty, Math.Abs(layer - seenAt));
                // keep every pattern possible everywhere, however unlikely
                layerWeights[layer][p] = Math.Max(weight, 1e-12);
            }
        }

        var patterns = new WfcPatternSet(size, codes, layerWeights);
        patterns.BuildPropagator(fraction => progress?.Invoke(0.5f + 0.5f * fraction));
        return patterns;
    }

    // how often each pattern occurs at each height layer of the sources (all sliding-window positions, including
    // rotations and mirror images when symmetry is on)
    internal static Dictionary<ulong, int[]> CountPatterns(IEnumerable<VoxelMap> sources, int size, bool symmetry,
        bool periodicInput, Action<float>? progress = null)
    {
        if (size < 2 || size > 3)
            throw new ArgumentOutOfRangeException(nameof(size), "Pattern size must be 2 or 3");

        var sourceList = sources.Where(map => map.SizeZ >= size).ToList();
        if (sourceList.Count == 0)
            throw new InvalidOperationException("No source map is high enough for the pattern size");
        var layers = sourceList.Max(map => map.SizeZ - size + 1);
        var layerCounts = new Dictionary<ulong, int[]>();
        for (var s = 0; s < sourceList.Count; s++)
        {
            var map = sourceList[s];
            var maxX = periodicInput ? map.SizeX : map.SizeX - size + 1;
            var maxY = periodicInput ? map.SizeY : map.SizeY - size + 1;
            for (var z = 0; z <= map.SizeZ - size; z++)
            {
                for (var y = 0; y < maxY; y++)
                for (var x = 0; x < maxX; x++)
                {
                    var code = Extract(map, x, y, z, size);
                    foreach (var variant in Variants(code, size, symmetry))
                    {
                        if (!layerCounts.TryGetValue(variant, out var counts))
                            layerCounts[variant] = counts = new int[layers];
                        counts[z]++;
                    }
                }

                progress?.Invoke((s + (z + 1f) / (map.SizeZ - size + 1)) / sourceList.Count);
            }
        }

        return layerCounts;
    }

    // patterns a and b are compatible in a direction when the part of a that overlaps b equals the part of b that
    // overlaps a; grouping patterns by those overlap signatures avoids comparing every pair
    private void BuildPropagator(Action<float> progress)
    {
        for (var d = 0; d < Directions.Length; d++)
        {
            var shift = Directions[d];
            var byOverlap = new Dictionary<ulong, ulong[]>();
            for (var q = 0; q < Count; q++)
            {
                var signature = OverlapOfNeighbour(Codes[q], shift);
                if (!byOverlap.TryGetValue(signature, out var group))
                    byOverlap[signature] = group = new ulong[Words];
                group[q >> 6] |= 1UL << (q & 63);
            }

            var none = new ulong[Words];
            for (var p = 0; p < Count; p++)
            {
                var compatible = byOverlap.TryGetValue(OverlapSeenFrom(Codes[p], shift), out var group)
                    ? group
                    : none;
                Propagator[p * Directions.Length + d] = compatible;
            }

            progress((d + 1f) / Directions.Length);
        }
    }

    // the voxels of pattern a that overlap a neighbour placed at shift, in the neighbour's coordinates
    private ulong OverlapSeenFrom(ulong a, (int X, int Y, int Z) shift)
    {
        ulong signature = 0;
        for (var dz = 0; dz < Size; dz++)
        for (var dy = 0; dy < Size; dy++)
        for (var dx = 0; dx < Size; dx++)
        {
            int bx = dx - shift.X, by = dy - shift.Y, bz = dz - shift.Z;
            if (bx < 0 || by < 0 || bz < 0 || bx >= Size || by >= Size || bz >= Size)
                continue;
            if (((a >> Bit(Size, dx, dy, dz)) & 1) != 0)
                signature |= 1UL << Bit(Size, bx, by, bz);
        }

        return signature;
    }

    // the voxels of neighbour b (placed at shift) that overlap the pattern it neighbours, in b's coordinates
    private ulong OverlapOfNeighbour(ulong b, (int X, int Y, int Z) shift)
    {
        ulong signature = 0;
        for (var bz = 0; bz < Size; bz++)
        for (var by = 0; by < Size; by++)
        for (var bx = 0; bx < Size; bx++)
        {
            int dx = bx + shift.X, dy = by + shift.Y, dz = bz + shift.Z;
            if (dx < 0 || dy < 0 || dz < 0 || dx >= Size || dy >= Size || dz >= Size)
                continue;
            signature |= ((b >> Bit(Size, bx, by, bz)) & 1) << Bit(Size, bx, by, bz);
        }

        return signature;
    }

    private static ulong Extract(VoxelMap map, int x, int y, int z, int size)
    {
        ulong code = 0;
        for (var dz = 0; dz < size; dz++)
        for (var dy = 0; dy < size; dy++)
        for (var dx = 0; dx < size; dx++)
            if (map[(x + dx) % map.SizeX, (y + dy) % map.SizeY, z + dz])
                code |= 1UL << Bit(size, dx, dy, dz);
        return code;
    }

    private static IEnumerable<ulong> Variants(ulong code, int size, bool symmetry)
    {
        if (!symmetry)
        {
            yield return code;
            yield break;
        }

        var seen = new HashSet<ulong>();
        var current = code;
        for (var mirror = 0; mirror < 2; mirror++)
        {
            for (var rotation = 0; rotation < 4; rotation++)
            {
                if (seen.Add(current))
                    yield return current;
                current = Transform(current, size, (dx, dy) => (size - 1 - dy, dx));
            }

            current = Transform(code, size, (dx, dy) => (size - 1 - dx, dy));
        }
    }

    private static ulong Transform(ulong code, int size, Func<int, int, (int X, int Y)> map)
    {
        ulong result = 0;
        for (var dz = 0; dz < size; dz++)
        for (var dy = 0; dy < size; dy++)
        for (var dx = 0; dx < size; dx++)
        {
            if (((code >> Bit(size, dx, dy, dz)) & 1) == 0)
                continue;
            var (tx, ty) = map(dx, dy);
            result |= 1UL << Bit(size, tx, ty, dz);
        }

        return result;
    }
}

using System.Text;
using Timberborn.TerrainGenerator.Core.Common;

namespace Timberborn.TerrainGenerator.Core.Wfc;

// The patterns of the source maps, deduplicated up to rotation about the vertical axis, with their per-height counts
// and one face table per direction (docs/wfc-face-lookup.md, section 3). Read-only once built.
// An oriented id is 4 * pattern + rotation; only rotations below the pattern's distinct rotation count are valid.
public sealed class WfcPatternIndex
{
    public const string TooSmallKey = "Ximsa.TerrainGenerator.Wfc.MapsTooSmall";

    // canonical patterns, numbered by id
    private readonly WfcPatternStore store;

    // [pattern * Bands + band]: occurrences of the pattern in the sources at that height band
    private readonly int[] bandCounts;

    // per direction: face slots .. slot + 1 give the range of ids[direction] with that face on that side
    private readonly int[][] offsets;
    private readonly int[][] ids;

    // [oriented id * 6 + direction]: the face slot in table direction ^ 1 that matches the id's face on side
    // direction, so the candidates of the neighbour in that direction; -1 if no pattern has a matching face
    private readonly int[] neighbourSlots;

    private readonly int[] faceValues = new int[6];

    private WfcPatternIndex(WfcPatternShape shape, WfcPatternStore store, int[] bandCounts, int bands, long windows)
    {
        Shape = shape;
        this.store = store;
        this.bandCounts = bandCounts;
        Bands = bands;
        Windows = windows;
        var patterns = store.Count;
        Rotations = new byte[patterns];
        var oriented = new List<int>();
        var pattern = shape.NewPattern();
        for (var p = 0; p < patterns; p++)
        {
            store.Get(p, pattern);
            Rotations[p] = (byte)shape.DistinctRotations(pattern);
            for (var r = 0; r < Rotations[p]; r++)
                oriented.Add(4 * p + r);
        }

        AllIds = oriented.ToArray();

        // first each id's own face slot per direction (face values are interned per direction), giving the tables;
        // then, in the same array, the slot its faces match on the other side
        var slots = new int[patterns * 4 * 6];
        var faceSlots = new Dictionary<(ulong, ulong), int>[6];
        for (var d = 0; d < 6; d++)
            faceSlots[d] = new Dictionary<(ulong, ulong), int>();
        ForEachFace((id, d, face) =>
        {
            if (!faceSlots[d].TryGetValue(face, out var slot))
                faceSlots[d][face] = slot = faceSlots[d].Count;
            slots[id * 6 + d] = slot;
        });

        offsets = new int[6][];
        ids = new int[6][];
        for (var d = 0; d < 6; d++)
        {
            faceValues[d] = faceSlots[d].Count;
            var counts = new int[faceValues[d] + 1];
            foreach (var id in AllIds)
                counts[slots[id * 6 + d] + 1]++;
            for (var v = 0; v < faceValues[d]; v++)
                counts[v + 1] += counts[v];
            var fill = (int[])counts.Clone();
            var list = new int[AllIds.Length];
            // ids go in ascending, so every list is sorted
            foreach (var id in AllIds)
                list[fill[slots[id * 6 + d]]++] = id;
            offsets[d] = counts;
            ids[d] = list;
        }

        neighbourSlots = slots;
        ForEachFace((id, d, face) =>
            neighbourSlots[id * 6 + d] = faceSlots[d ^ 1].TryGetValue(face, out var slot) ? slot : -1);
    }

    public WfcPatternShape Shape { get; }
    public long Windows { get; }

    // height bands of the sources: a window starting at height z belongs to the nearest cell layer
    public int Bands { get; }
    public int PatternCount => Rotations.Length;
    public int OrientedCount => AllIds.Length;

    // every valid oriented id, ascending
    internal int[] AllIds { get; }

    // per pattern: distinct rotations (1, 2 or 4)
    internal byte[] Rotations { get; }

    public ulong[] OrientedCode(int id)
    {
        var pattern = Shape.NewPattern();
        store.Get(id >> 2, pattern);
        Shape.Rotate(pattern, id & 3);
        return pattern;
    }

    // the sorted oriented ids that fit next to id in the given direction
    internal IdRange NeighbourCandidates(int id, int direction)
    {
        var slot = neighbourSlots[id * 6 + direction];
        if (slot < 0)
            return IdRange.Empty;
        var table = offsets[direction ^ 1];
        return new IdRange(ids[direction ^ 1], table[slot], table[slot + 1] - table[slot]);
    }

    // [cell layer][pattern]: how likely each orientation of a pattern is chosen in that layer. Every occurrence in the
    // sources counts, by heightPenalty for each layer between where it was seen and the cell layer; 1 ignores
    // heights. A pattern's weight is split evenly across its distinct orientations.
    internal float[][] LayerWeights(int layers, double heightPenalty)
    {
        var falloff = new double[Math.Max(layers, Bands)];
        for (var i = 0; i < falloff.Length; i++)
            falloff[i] = Math.Pow(heightPenalty, i);
        var weights = new float[layers][];
        for (var layer = 0; layer < layers; layer++)
        {
            var row = weights[layer] = new float[Rotations.Length];
            for (var p = 0; p < Rotations.Length; p++)
            {
                var weight = 0.0;
                for (var band = 0; band < Bands; band++)
                    weight += bandCounts[p * Bands + band] * falloff[Math.Abs(layer - band)];
                // never quite zero, so heights alone cannot cause contradictions
                row[p] = (float)Math.Max(weight / Rotations[p], 1e-30);
            }
        }

        return weights;
    }

    public bool ContainsCanonical(ulong[] canonical)
    {
        return store.Find(canonical) >= 0;
    }

    // every window of every source, stride 1, no wrapping
    public static WfcPatternIndex Learn(WfcPatternShape shape, IEnumerable<VoxelMap> sources,
        Action<float>? progress = null)
    {
        var maps = sources.Where(map =>
            map.SizeX >= shape.Width && map.SizeY >= shape.Width && map.SizeZ >= shape.Height).ToList();
        if (maps.Count == 0)
            throw new GenerationFailedException(TooSmallKey, $"No source map is at least {shape}");
        var store = new WfcPatternStore(shape);
        var bands = maps.Max(map => (map.SizeZ - shape.Height + shape.StrideUp / 2) / shape.StrideUp) + 1;
        var bandCounts = new List<int>();
        long windows = 0;
        for (var s = 0; s < maps.Count; s++)
        {
            ForEachWindow(shape, maps[s], (z, canonical) =>
            {
                var p = store.AddOrFind(canonical, out var added);
                if (added)
                    for (var b = 0; b < bands; b++)
                        bandCounts.Add(0);
                bandCounts[p * bands + (z + shape.StrideUp / 2) / shape.StrideUp]++;
                windows++;
            }, fraction => progress?.Invoke(0.8f * (s + fraction) / maps.Count));
        }

        store.Trim();
        var index = new WfcPatternIndex(shape, store, bandCounts.ToArray(), bands, windows);
        progress?.Invoke(1);
        return index;
    }

    // calls visit with the height and canonical pattern of every window of the map, stride 1; the pattern array is
    // reused between calls
    public static void ForEachWindow(WfcPatternShape shape, VoxelMap map, Action<int, ulong[]> visit,
        Action<float>? progress = null)
    {
        int w = shape.Width, h = shape.Height;
        int across = map.SizeX - w + 1, deep = map.SizeY - w + 1;
        // per layer: the w x w patch starting at each (x, y), as bits x + w * y in two words, built from rows of w
        var low = new ulong[map.SizeZ][];
        var high = new ulong[map.SizeZ][];
        var rows = new ulong[across * map.SizeY];
        for (var z = 0; z < map.SizeZ; z++)
        {
            for (var y = 0; y < map.SizeY; y++)
            for (var x = 0; x < across; x++)
            {
                ulong row = 0;
                for (var dx = 0; dx < w; dx++)
                    if (map[x + dx, y, z])
                        row |= 1UL << dx;
                rows[y * across + x] = row;
            }

            low[z] = new ulong[across * deep];
            high[z] = new ulong[across * deep];
            for (var y = 0; y < deep; y++)
            for (var x = 0; x < across; x++)
            {
                ulong patchLow = 0, patchHigh = 0;
                for (var dy = 0; dy < w; dy++)
                {
                    var row = rows[(y + dy) * across + x];
                    var shift = w * dy;
                    if (shift >= 64)
                        patchHigh |= row << (shift - 64);
                    else
                    {
                        patchLow |= row << shift;
                        if (shift > 0 && shift + w > 64)
                            patchHigh |= row >> (64 - shift);
                    }
                }

                low[z][y * across + x] = patchLow;
                high[z][y * across + x] = patchHigh;
            }
        }

        var pattern = shape.NewPattern();
        var scratch = shape.NewPattern();
        for (var z = 0; z + h <= map.SizeZ; z++)
        {
            for (var i = 0; i < across * deep; i++)
            {
                for (var dz = 0; dz < h; dz++)
                {
                    pattern[2 * dz] = low[z + dz][i];
                    pattern[2 * dz + 1] = high[z + dz][i];
                }

                shape.Canonicalize(pattern, scratch);
                visit(z, pattern);
            }

            progress?.Invoke((z + 1f) / (map.SizeZ - h + 1));
        }
    }

    private void ForEachFace(Action<int, int, (ulong, ulong)> visit)
    {
        var pattern = Shape.NewPattern();
        for (var p = 0; p < Rotations.Length; p++)
        {
            store.Get(p, pattern);
            for (var r = 0; r < Rotations[p]; r++, Shape.Rotate(pattern))
                for (var d = 0; d < 6; d++)
                    visit(4 * p + r, d, Shape.Face(pattern, d));
        }
    }

    // the measurements of section 3.5
    public string Describe()
    {
        var text = new StringBuilder();
        var symmetry = new int[5];
        foreach (var rotations in Rotations)
            symmetry[rotations]++;
        text.AppendLine($"{Shape} patterns: windows {Windows:N0}, patterns {PatternCount:N0}, " +
                        $"oriented {OrientedCount:N0}, rotations 1/2/4: {symmetry[1]:N0}/{symmetry[2]:N0}/{symmetry[4]:N0}");
        string[] names = { "+x", "-x", "+y", "-y", "+z", "-z" };
        for (var d = 0; d < 6; d++)
        {
            var longest = 0;
            for (var v = 0; v < faceValues[d]; v++)
                longest = Math.Max(longest, offsets[d][v + 1] - offsets[d][v]);
            text.AppendLine($"  {names[d]}: {faceValues[d]:N0} face values, " +
                            $"mean list {(double)OrientedCount / faceValues[d]:F1}, longest {longest:N0}");
        }

        var bytes = store.Bytes + bandCounts.LongLength * 4 + Rotations.LongLength + neighbourSlots.LongLength * 4 +
                    6L * OrientedCount * 4 + faceValues.Sum(count => count + 1L) * 4;
        text.Append($"  index memory about {bytes / (1024.0 * 1024.0):F0} MB");
        return text.ToString();
    }
}

// a range of a sorted id array, without copying it
internal readonly struct IdRange
{
    public static readonly IdRange Empty = new(Array.Empty<int>(), 0, 0);

    public IdRange(int[] items, int start, int count)
    {
        Items = items;
        Start = start;
        Count = count;
    }

    public int[] Items { get; }
    public int Start { get; }
    public int Count { get; }

    public int this[int i] => Items[Start + i];
}

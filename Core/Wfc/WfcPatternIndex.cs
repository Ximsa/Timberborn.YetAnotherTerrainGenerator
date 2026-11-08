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
    private readonly int[][] offsets = new int[6][];
    private readonly int[][] ids = new int[6][];

    // [oriented id * 6 + direction]: the face slot in table direction ^ 1 that matches the id's face on side
    // direction, so the candidates of the neighbour in that direction; -1 if no pattern has a matching face
    private readonly int[] neighbourSlots;

    private readonly int[] faceValues = new int[6];

    // the steps run on several threads; progress is reported between them, from the calling thread
    private WfcPatternIndex(WfcPatternShape shape, WfcPatternStore store, int[] bandCounts, int bands, long windows,
        Action<float> progress)
    {
        Shape = shape;
        this.store = store;
        this.bandCounts = bandCounts;
        Bands = bands;
        Windows = windows;
        var patterns = store.Count;
        Rotations = new byte[patterns];
        Parallelism.ForRanges(patterns, (start, end) =>
        {
            var pattern = shape.NewPattern();
            for (var p = start; p < end; p++)
            {
                store.Get(p, pattern);
                Rotations[p] = (byte)shape.DistinctRotations(pattern);
            }
        });
        var oriented = new List<int>();
        for (var p = 0; p < patterns; p++)
            for (var r = 0; r < Rotations[p]; r++)
                oriented.Add(4 * p + r);
        AllIds = oriented.ToArray();
        progress(0.2f);

        // each id's own face slot per direction, with face values interned per direction, one thread per direction;
        // slots are numbered in id order, as on one thread. Then the tables from them.
        var slots = new int[patterns * 4 * 6];
        var faceSlots = new Dictionary<(ulong, ulong), int>[6];
        Parallelism.For(6, d =>
        {
            var interned = faceSlots[d] = new Dictionary<(ulong, ulong), int>();
            ForEachFace(0, patterns, d, (id, face) =>
            {
                if (!interned.TryGetValue(face, out var slot))
                    interned[face] = slot = interned.Count;
                slots[id * 6 + d] = slot;
            });
        });
        progress(0.6f);
        Parallelism.For(6, d =>
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
        });
        progress(0.8f);

        // in the same array, the slot each face matches on the other side; the dictionaries are only read now
        neighbourSlots = slots;
        Parallelism.ForRanges(patterns, (start, end) =>
        {
            for (var d = 0; d < 6; d++)
            {
                var matching = faceSlots[d ^ 1];
                var direction = d;
                ForEachFace(start, end, d, (id, face) =>
                    neighbourSlots[id * 6 + direction] = matching.TryGetValue(face, out var slot) ? slot : -1);
            }
        });
        progress(1);
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

    // every window of every source, stride 1, no wrapping. The heights of each map are scanned in parallel into
    // stores of their own, which are merged in height order, so patterns are numbered as on one thread.
    public static WfcPatternIndex Learn(WfcPatternShape shape, IEnumerable<VoxelMap> sources,
        Action<float>? progress = null)
    {
        var maps = sources.Where(map =>
            map.SizeX >= shape.Width && map.SizeY >= shape.Width && map.SizeZ >= shape.Height).ToList();
        if (maps.Count == 0)
            throw new GenerationFailedException(TooSmallKey, $"No source map is at least {shape}");
        var store = new WfcPatternStore(shape);
        var bands = maps.Max(map => BandOf(shape, map.SizeZ - shape.Height)) + 1;
        var bandCounts = new List<int>();
        long windows = 0;
        var pattern = shape.NewPattern();
        for (var s = 0; s < maps.Count; s++)
        {
            var patches = WindowPatches.Build(shape, maps[s]);
            var heights = maps[s].SizeZ - shape.Height + 1;
            var slices = new (WfcPatternStore Store, List<int> Counts)[heights];
            Parallelism.For(heights, z =>
            {
                var slice = new WfcPatternStore(shape);
                var counts = new List<int>();
                patches.ForEach(z, canonical =>
                {
                    var id = slice.AddOrFind(canonical, out var added);
                    if (added)
                        counts.Add(0);
                    counts[id]++;
                });
                slices[z] = (slice, counts);
            });

            for (var z = 0; z < heights; z++)
            {
                var (slice, counts) = slices[z];
                var band = BandOf(shape, z);
                for (var id = 0; id < slice.Count; id++)
                {
                    slice.Get(id, pattern);
                    var p = store.AddOrFind(pattern, out var added);
                    if (added)
                        for (var b = 0; b < bands; b++)
                            bandCounts.Add(0);
                    bandCounts[p * bands + band] += counts[id];
                    windows += counts[id];
                }

                slices[z] = default;
            }

            progress?.Invoke(0.6f * (s + 1) / maps.Count);
        }

        store.Trim();
        return new WfcPatternIndex(shape, store, bandCounts.ToArray(), bands, windows,
            fraction => progress?.Invoke(0.6f + 0.4f * fraction));
    }

    private static int BandOf(WfcPatternShape shape, int z)
    {
        return (z + shape.StrideUp / 2) / shape.StrideUp;
    }

    // calls visit with the height and canonical pattern of every window of the map, stride 1, on this thread; the
    // pattern array is reused between calls
    public static void ForEachWindow(WfcPatternShape shape, VoxelMap map, Action<int, ulong[]> visit)
    {
        var patches = WindowPatches.Build(shape, map);
        for (var z = 0; z + shape.Height <= map.SizeZ; z++)
        {
            var height = z;
            patches.ForEach(z, canonical => visit(height, canonical));
        }
    }

    // calls visit with each oriented id of patterns start .. end - 1 and its face on side direction
    private void ForEachFace(int start, int end, int direction, Action<int, (ulong, ulong)> visit)
    {
        var pattern = Shape.NewPattern();
        for (var p = start; p < end; p++)
        {
            store.Get(p, pattern);
            for (var r = 0; r < Rotations[p]; r++)
            {
                if (r > 0)
                    Shape.Rotate(pattern);
                visit(4 * p + r, Shape.Face(pattern, direction));
            }
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

    // the Width x Width patch of each layer of a map at each (x, y), as bits x + Width * y in two words, so a window
    // is just Height patches stacked
    private sealed class WindowPatches
    {
        private readonly WfcPatternShape shape;
        private readonly int count;
        private readonly ulong[][] low;
        private readonly ulong[][] high;

        private WindowPatches(WfcPatternShape shape, int count, ulong[][] low, ulong[][] high)
        {
            this.shape = shape;
            this.count = count;
            this.low = low;
            this.high = high;
        }

        // built from rows of Width voxels, layers in parallel
        public static WindowPatches Build(WfcPatternShape shape, VoxelMap map)
        {
            var w = shape.Width;
            int across = map.SizeX - w + 1, deep = map.SizeY - w + 1;
            var low = new ulong[map.SizeZ][];
            var high = new ulong[map.SizeZ][];
            Parallelism.For(map.SizeZ, z =>
            {
                var rows = new ulong[across * map.SizeY];
                for (var y = 0; y < map.SizeY; y++)
                for (var x = 0; x < across; x++)
                {
                    ulong row = 0;
                    for (var dx = 0; dx < w; dx++)
                        if (map[x + dx, y, z])
                            row |= 1UL << dx;
                    rows[y * across + x] = row;
                }

                var layerLow = low[z] = new ulong[across * deep];
                var layerHigh = high[z] = new ulong[across * deep];
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

                    layerLow[y * across + x] = patchLow;
                    layerHigh[y * across + x] = patchHigh;
                }
            });
            return new WindowPatches(shape, across * deep, low, high);
        }

        // visits the canonical pattern of every window starting at height z; the array is reused between calls
        public void ForEach(int z, Action<ulong[]> visit)
        {
            var pattern = shape.NewPattern();
            var scratch = shape.NewPattern();
            for (var i = 0; i < count; i++)
            {
                for (var dz = 0; dz < shape.Height; dz++)
                {
                    pattern[2 * dz] = low[z + dz][i];
                    pattern[2 * dz + 1] = high[z + dz][i];
                }

                shape.Canonicalize(pattern, scratch);
                visit(pattern);
            }
        }
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

using Timberborn.TerrainGenerator.Core.Common;

namespace Timberborn.TerrainGenerator.Core.CliffSharpening;

public sealed class CliffSharpenerSettings
{
    // blur radius in tiles before segmenting; larger values give larger, calmer regions
    public int RegionSize = 4;

    // regions smaller than this many tiles are merged into the neighbour with the closest height
    public int MinRegionArea = 32;

    // smallest height difference between neighbouring plateaus, i.e. the lowest cliff; plateaus can be at any height
    public int StepSize = 2;

    // 0 keeps the original surface, 1 flattens fully to the plateau heights
    public double Strength = 0.5;
}

// Splits a surface into flat plateaus with cliffs between them, like typical Timberborn maps: watershed regions of
// the smoothed surface's slope (so region borders run along the steepest ground), small regions merged into their
// closest-height neighbour, each region flattened to its median height, and neighbouring plateaus closer in height
// than the step size merged, so plateaus can lie at any height but every cliff is at least one step high.
public sealed class CliffSharpener
{
    private static readonly (int X, int Y)[] Neighbours =
        { (-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1) };

    private readonly CliffSharpenerSettings settings;
    private readonly int sizeX;
    private readonly int sizeY;

    public CliffSharpener(CliffSharpenerSettings settings, int sizeX, int sizeY)
    {
        if (settings.StepSize < 1)
            throw new ArgumentOutOfRangeException(nameof(settings.StepSize), "Must be at least 1");
        this.settings = settings;
        this.sizeX = sizeX;
        this.sizeY = sizeY;
    }

    public int RegionCount { get; private set; }

    // heights indexed y * sizeX + x; returns the sharpened heights
    public int[] Sharpen(int[] heights, Action<float>? progress = null)
    {
        var smoothed = Blur(heights.Select(h => (float)h).ToArray(), settings.RegionSize);
        progress?.Invoke(0.2f);
        // bands follow a lightly smoothed surface, so narrow ridges keep their height
        var labels = SplitIntoBands(Watershed(Slope(smoothed)), Blur(heights.Select(h => (float)h).ToArray(), 1));
        progress?.Invoke(0.6f);
        var regions = MergeSmallRegions(labels, heights);
        progress?.Invoke(0.8f);
        var plateauHeights = PlateauHeights(regions, heights);
        var result = new int[heights.Length];
        for (var i = 0; i < heights.Length; i++)
            result[i] = (int)Math.Round(heights[i] + (plateauHeights[regions[i]] - heights[i]) * settings.Strength);
        progress?.Invoke(1);
        return result;
    }

    // three box blurs approximate a Gaussian; edges are clamped
    private float[] Blur(float[] values, int radius)
    {
        if (radius < 1) return values;
        for (var pass = 0; pass < 3; pass++)
        {
            values = BoxBlur(values, radius, 1, 0);
            values = BoxBlur(values, radius, 0, 1);
        }

        return values;
    }

    private float[] BoxBlur(float[] values, int radius, int stepX, int stepY)
    {
        var result = new float[values.Length];
        for (var y = 0; y < sizeY; y++)
        for (var x = 0; x < sizeX; x++)
        {
            var sum = 0f;
            for (var d = -radius; d <= radius; d++)
            {
                var nx = Math.Clamp(x + d * stepX, 0, sizeX - 1);
                var ny = Math.Clamp(y + d * stepY, 0, sizeY - 1);
                sum += values[ny * sizeX + nx];
            }

            result[y * sizeX + x] = sum / (2 * radius + 1);
        }

        return result;
    }

    private float[] Slope(float[] surface)
    {
        var slope = new float[surface.Length];
        for (var y = 0; y < sizeY; y++)
        for (var x = 0; x < sizeX; x++)
        {
            var dx = surface[y * sizeX + Math.Min(x + 1, sizeX - 1)] - surface[y * sizeX + Math.Max(x - 1, 0)];
            var dy = surface[Math.Min(y + 1, sizeY - 1) * sizeX + x] - surface[Math.Max(y - 1, 0) * sizeX + x];
            slope[y * sizeX + x] = MathF.Sqrt(dx * dx + dy * dy);
        }

        return slope;
    }

    // priority flood from the slope's local minima: every tile joins the region that reaches it over the
    // gentlest ground, so borders end up along slope ridges (the steepest ground between flat areas)
    private int[] Watershed(float[] slope)
    {
        var labels = new int[slope.Length];
        Array.Fill(labels, -1);
        var queue = new TileQueue();
        var regionCount = 0;
        for (var i = 0; i < slope.Length; i++)
        {
            if (labels[i] >= 0 || !IsLocalMinimum(slope, i)) continue;
            // connected minima of equal slope form one seed
            var seed = new Stack<int>();
            seed.Push(i);
            labels[i] = regionCount;
            while (seed.Count > 0)
            {
                var tile = seed.Pop();
                queue.Push(tile, slope[tile]);
                foreach (var neighbour in NeighboursOf(tile))
                    if (labels[neighbour] < 0 && slope[neighbour] == slope[tile] && IsLocalMinimum(slope, neighbour))
                    {
                        labels[neighbour] = regionCount;
                        seed.Push(neighbour);
                    }
            }

            regionCount++;
        }

        while (queue.TryPop(out var tile, out _))
            foreach (var neighbour in NeighboursOf(tile))
                if (labels[neighbour] < 0)
                {
                    labels[neighbour] = labels[tile];
                    queue.Push(neighbour, slope[neighbour]);
                }

        return labels;
    }

    // a long even slope has no slope minima inside it, so it would end up split between the regions above and
    // below; splitting every region into connected bands of one step of smoothed height turns it into terraces
    private int[] SplitIntoBands(int[] labels, float[] smoothed)
    {
        var band = smoothed.Select(height => (int)Math.Round(height / settings.StepSize)).ToArray();
        var result = new int[labels.Length];
        Array.Fill(result, -1);
        var count = 0;
        var stack = new Stack<int>();
        for (var i = 0; i < labels.Length; i++)
        {
            if (result[i] >= 0) continue;
            result[i] = count;
            stack.Push(i);
            while (stack.Count > 0)
            {
                var tile = stack.Pop();
                foreach (var neighbour in NeighboursOf(tile))
                    if (result[neighbour] < 0 && labels[neighbour] == labels[tile] && band[neighbour] == band[tile])
                    {
                        result[neighbour] = count;
                        stack.Push(neighbour);
                    }
            }

            count++;
        }

        return result;
    }

    private bool IsLocalMinimum(float[] slope, int tile)
    {
        foreach (var neighbour in NeighboursOf(tile))
            if (slope[neighbour] < slope[tile])
                return false;
        return true;
    }

    private IEnumerable<int> NeighboursOf(int tile)
    {
        int x = tile % sizeX, y = tile / sizeX;
        foreach (var (dx, dy) in Neighbours)
        {
            int nx = x + dx, ny = y + dy;
            if (nx >= 0 && nx < sizeX && ny >= 0 && ny < sizeY)
                yield return ny * sizeX + nx;
        }
    }

    // returns, per tile, the index of its final region (0 .. RegionCount - 1)
    private int[] MergeSmallRegions(int[] labels, int[] heights)
    {
        var count = labels.Max() + 1;
        var parent = Enumerable.Range(0, count).ToArray();
        var area = new int[count];
        var heightSum = new double[count];
        for (var i = 0; i < labels.Length; i++)
        {
            area[labels[i]]++;
            heightSum[labels[i]] += heights[i];
        }

        bool merged;
        do
        {
            merged = false;
            // neighbouring roots of every small root, from the 4-connected borders between tiles
            var adjacency = new Dictionary<int, HashSet<int>>();
            for (var y = 0; y < sizeY; y++)
            for (var x = 0; x < sizeX; x++)
            {
                var a = Find(labels[y * sizeX + x]);
                if (x + 1 < sizeX) Connect(a, Find(labels[y * sizeX + x + 1]));
                if (y + 1 < sizeY) Connect(a, Find(labels[(y + 1) * sizeX + x]));
            }

            foreach (var root in adjacency.Keys.OrderBy(root => area[root]).ToList())
            {
                var small = Find(root);
                if (small != root || area[small] >= settings.MinRegionArea) continue;
                var mean = heightSum[small] / area[small];
                var target = adjacency[root].Select(Find).Where(other => other != small).Distinct()
                    .OrderBy(other => Math.Abs(heightSum[other] / area[other] - mean)).DefaultIfEmpty(-1).First();
                // a small region clearly above or below all its neighbours is a real peak or pit and stays, unless
                // it is only a speck
                if (target < 0 || (Math.Abs(heightSum[target] / area[target] - mean) >= settings.StepSize &&
                                   area[small] * 4 >= settings.MinRegionArea)) continue;
                parent[small] = target;
                area[target] += area[small];
                heightSum[target] += heightSum[small];
                merged = true;
            }

            void Connect(int a, int b)
            {
                if (a == b) return;
                if (area[a] < settings.MinRegionArea) Add(a, b);
                if (area[b] < settings.MinRegionArea) Add(b, a);
            }

            void Add(int from, int to)
            {
                if (!adjacency.TryGetValue(from, out var set))
                    adjacency[from] = set = new HashSet<int>();
                set.Add(to);
            }
        } while (merged);

        var index = new Dictionary<int, int>();
        var regions = new int[labels.Length];
        for (var i = 0; i < labels.Length; i++)
        {
            var root = Find(labels[i]);
            if (!index.TryGetValue(root, out var region))
                index[root] = region = index.Count;
            regions[i] = region;
        }

        RegionCount = index.Count;
        return regions;

        int Find(int label)
        {
            while (parent[label] != label)
                label = parent[label] = parent[parent[label]];
            return label;
        }
    }

    // each region gets the median of its original heights, even or odd; neighbouring regions whose heights differ by
    // less than the step size are merged (closest heights first, the merged region taking the median of both), so every
    // cliff is at least one step high. Returns the plateau height per region of the input.
    private int[] PlateauHeights(int[] regions, int[] heights)
    {
        var count = RegionCount;
        var maxHeight = heights.Max();
        var histograms = new int[count][];
        var areas = new int[count];
        for (var region = 0; region < count; region++)
            histograms[region] = new int[maxHeight + 1];
        for (var i = 0; i < regions.Length; i++)
        {
            histograms[regions[i]][heights[i]]++;
            areas[regions[i]]++;
        }

        // neighbouring regions along 4-connected tile borders; kept only for regions that were not merged away
        var neighbours = new HashSet<int>?[count];
        for (var region = 0; region < count; region++)
            neighbours[region] = new HashSet<int>();
        for (var y = 0; y < sizeY; y++)
        for (var x = 0; x < sizeX; x++)
        {
            var a = regions[y * sizeX + x];
            if (x + 1 < sizeX) Connect(a, regions[y * sizeX + x + 1]);
            if (y + 1 < sizeY) Connect(a, regions[(y + 1) * sizeX + x]);
        }

        var parent = Enumerable.Range(0, count).ToArray();
        var plateau = Enumerable.Range(0, count).Select(Median).ToArray();
        while (true)
        {
            int bestA = -1, bestB = -1, bestDifference = int.MaxValue;
            for (var a = 0; a < count; a++)
            {
                if (neighbours[a] == null) continue;
                foreach (var b in neighbours[a]!)
                {
                    var difference = Math.Abs(plateau[a] - plateau[b]);
                    if (b <= a || difference == 0 || difference >= settings.StepSize || difference >= bestDifference)
                        continue;
                    (bestA, bestB, bestDifference) = (a, b, difference);
                }
            }

            if (bestA < 0) break;
            // the smaller region joins the larger one
            var (keep, gone) = areas[bestA] >= areas[bestB] ? (bestA, bestB) : (bestB, bestA);
            for (var height = 0; height <= maxHeight; height++)
                histograms[keep][height] += histograms[gone][height];
            areas[keep] += areas[gone];
            parent[gone] = keep;
            foreach (var other in neighbours[gone]!)
            {
                neighbours[other]!.Remove(gone);
                if (other == keep) continue;
                neighbours[other]!.Add(keep);
                neighbours[keep]!.Add(other);
            }

            neighbours[gone] = null;
            plateau[keep] = Median(keep);
        }

        RegionCount = neighbours.Count(set => set != null);
        return Enumerable.Range(0, count).Select(region => plateau[Find(region)]).ToArray();

        void Connect(int a, int b)
        {
            if (a == b) return;
            neighbours[a]!.Add(b);
            neighbours[b]!.Add(a);
        }

        int Median(int region)
        {
            for (int height = 0, seen = 0; height <= maxHeight; height++)
            {
                seen += histograms[region][height];
                if (seen * 2 >= areas[region])
                    return Math.Max(1, height);
            }

            return 1;
        }

        int Find(int region)
        {
            while (parent[region] != region)
                region = parent[region];
            return region;
        }
    }
}

using Timberborn.TerrainGenerator.Core.Common;

namespace Timberborn.TerrainGenerator.Core.Rivers;

public sealed class RiverCarverSettings
{
    public int Rivers = 3;

    // bed width in tiles
    public int Width = 3;

    // a basin whose area below its spill level reaches this many tiles becomes a lake that ends the river
    public int LakeSize = 150;

    // strength of the water source at each river head
    public float SourceStrength = 1.5f;

    public int Seed = 1;

    public RiverCarverSettings Clone()
    {
        return (RiverCarverSettings)MemberwiseClone();
    }
}

// a river head: where its water source goes, on top of the carved bed
public readonly record struct RiverHead(int X, int Y, int Z);

// Carves rivers into a surface. Each river starts at a random tile, climbs to the nearest top, and has its head half
// way up in height. From the head it runs down the steepest descent; at a pit it floods the basin lowest first until
// the water spills over the rim (filling the pit, unless the basin is big enough to become a lake). The river
// ends at a map edge, in a lake, or where it joins an earlier river. Rivers never cut more than one block deep, so
// they follow the terrain and the water fills pits on its way rather than running through cut rims.
public sealed class RiverCarver
{
    private static readonly (int X, int Y)[] Neighbours =
        { (-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1) };

    private const int HeadAttempts = 50;

    // a river lowers the terrain by at most this many blocks, anywhere (bed, width and lakes)
    private const int MaxCut = 1;

    private readonly RiverCarverSettings settings;
    private readonly int sizeX;
    private readonly int sizeY;

    public RiverCarver(RiverCarverSettings settings, int sizeX, int sizeY)
    {
        if (settings.Width < 1)
            throw new ArgumentOutOfRangeException(nameof(settings.Width), "Must be at least 1");
        this.settings = settings;
        this.sizeX = sizeX;
        this.sizeY = sizeY;
    }

    public List<RiverHead> Heads { get; } = new();

    // tiles covered by carved river beds and lakes, indexed y * sizeX + x
    public bool[] Water { get; private set; } = Array.Empty<bool>();

    // heights indexed y * sizeX + x; returns the carved heights
    public int[] Carve(int[] heights, Action<float>? progress = null)
    {
        var random = new Random(settings.Seed);
        var result = (int[])heights.Clone();
        Water = new bool[heights.Length];
        // tiles on an earlier river's course; a later river that reaches one joins it
        var courses = new bool[heights.Length];
        var climbSurface = Smooth(heights);
        for (var river = 0; river < settings.Rivers; river++)
        {
            var head = FindHead(random, result, climbSurface);
            if (head < 0) break;
            var (course, lake) = TraceCourse(head, result, courses);
            var bed = CarveCourse(course, heights, result);
            if (lake != null)
                CarveLake(lake, bed, heights, result);
            foreach (var tile in course) courses[tile] = true;
            Heads.Add(new RiverHead(head % sizeX, head / sizeX, result[head]));
            progress?.Invoke((river + 1f) / settings.Rivers);
        }

        return result;
    }

    // climbing works on a lightly smoothed surface, so it does not stall on the flat tops of terraces
    private float[] Smooth(int[] heights)
    {
        var smoothed = new float[heights.Length];
        for (var y = 0; y < sizeY; y++)
        for (var x = 0; x < sizeX; x++)
        {
            float sum = 0;
            var count = 0;
            for (var dy = -2; dy <= 2; dy++)
            for (var dx = -2; dx <= 2; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= sizeX || ny >= sizeY) continue;
                sum += heights[ny * sizeX + nx];
                count++;
            }

            // a little of the exact height breaks ties on flats in favour of higher tiles
            smoothed[y * sizeX + x] = sum / count + heights[y * sizeX + x] * 0.01f;
        }

        return smoothed;
    }

    // random start, steepest ascent to a top, head at the first point of the climb at least half way up in height.
    // Of several random starts the one with the biggest climb wins, so rivers do not start on small bumps in low
    // ground; heads near the map edge or near earlier heads, and starts with nothing to climb, are skipped
    private int FindHead(Random random, int[] heights, float[] climbSurface)
    {
        var minDistance = Math.Max(8, Math.Min(sizeX, sizeY) / (2 * Math.Max(1, settings.Rivers)));
        // heads close to the map edge would just run straight off it
        var edgeMargin = Math.Max(8, Math.Min(sizeX, sizeY) / 6);
        var best = -1;
        var bestClimb = 0;
        for (var attempt = 0; attempt < HeadAttempts; attempt++)
        {
            var start = random.Next(heights.Length);
            var path = new List<int> { start };
            var tile = start;
            while (true)
            {
                var next = tile;
                foreach (var neighbour in NeighboursOf(tile))
                    if (climbSurface[neighbour] > climbSurface[next])
                        next = neighbour;
                if (next == tile) break;
                path.Add(next);
                tile = next;
            }

            var startHeight = heights[start];
            var topHeight = heights[tile];
            var climb = topHeight - startHeight;
            if (climb < 2 || climb <= bestClimb) continue;
            var halfHeight = (startHeight + topHeight) / 2.0;
            var head = path.First(t => heights[t] >= halfHeight);
            if (EdgeDistance(head) < edgeMargin || Heads.Any(h => Distance(h.X, h.Y, head) < minDistance)) continue;
            best = head;
            bestClimb = climb;
        }

        return best;
    }

    private int EdgeDistance(int tile)
    {
        int x = tile % sizeX, y = tile / sizeX;
        return Math.Min(Math.Min(x, y), Math.Min(sizeX - 1 - x, sizeY - 1 - y));
    }

    // the river's tiles from head to mouth, and the lake it ends in (null if it ends at an edge or another river)
    private (List<int> Course, List<int>? Lake) TraceCourse(int head, int[] heights, bool[] courses)
    {
        var course = new List<int> { head };
        var onCourse = new HashSet<int> { head };
        var tile = head;
        while (!IsEdge(tile) && !courses[tile])
        {
            var next = tile;
            foreach (var neighbour in NeighboursOf(tile))
                if (heights[neighbour] < heights[next] && !onCourse.Contains(neighbour))
                    next = neighbour;
            if (next != tile)
            {
                course.Add(next);
                onCourse.Add(next);
                tile = next;
                continue;
            }

            // a pit or a flat: flood it to find where it spills over
            var (spillPath, lake) = Flood(tile, heights, onCourse, courses);
            if (lake != null) return (course, lake);
            foreach (var step in spillPath)
            {
                course.Add(step);
                onCourse.Add(step);
            }

            tile = course[course.Count - 1];
        }

        return (course, null);
    }

    // floods from start lowest first; returns the path to the first tile past the rim (or at an edge or on another
    // river), or the basin as a lake when its area below the water level reaches the lake size
    private (List<int> SpillPath, List<int>? Lake) Flood(int start, int[] heights, HashSet<int> onCourse,
        bool[] courses)
    {
        var parent = new Dictionary<int, int> { [start] = start };
        // steps from start; at equal height nearer tiles flood first, so paths across flats are shortest
        var steps = new Dictionary<int, int> { [start] = 0 };
        var queue = new TileQueue();
        queue.Push(start, Priority(heights[start], 0));
        var level = heights[start];
        // flooded tiles per height, to count the area below the water level
        var flooded = new SortedDictionary<int, int>();
        var visited = new List<int>();
        while (queue.TryPop(out var tile, out _))
        {
            var height = heights[tile];
            if (height < level || (tile != start && (IsEdge(tile) || courses[tile])))
                return (PathTo(tile, parent), null);
            level = height;
            visited.Add(tile);
            flooded[height] = flooded.TryGetValue(height, out var count) ? count + 1 : 1;
            if (flooded.Where(entry => entry.Key < level).Sum(entry => entry.Value) >= settings.LakeSize)
                return (new List<int>(), visited.Where(t => heights[t] < level).ToList());
            foreach (var neighbour in NeighboursOf(tile))
                if (!parent.ContainsKey(neighbour) && !onCourse.Contains(neighbour))
                {
                    parent[neighbour] = tile;
                    steps[neighbour] = steps[tile] + 1;
                    queue.Push(neighbour, Priority(heights[neighbour], steps[neighbour]));
                }
        }

        // nowhere to spill (the whole map was flooded): end here as a lake
        return (new List<int>(), visited);
    }

    private static float Priority(int height, int steps)
    {
        // stays below one height step for any path within a 256 x 256 map
        return height + steps * 1e-5f;
    }

    private static List<int> PathTo(int tile, Dictionary<int, int> parent)
    {
        var path = new List<int>();
        while (parent[tile] != tile)
        {
            path.Add(tile);
            tile = parent[tile];
        }

        path.Reverse();
        return path;
    }

    // cuts a bed across the river's width that never rises from head to mouth where it can, but never lowers a tile
    // more than MaxCut below its original height; returns the bed height at the mouth
    private int CarveCourse(List<int> course, int[] original, int[] heights)
    {
        var radius = (settings.Width - 1) / 2.0;
        var reach = (int)Math.Ceiling(radius);
        var bed = int.MaxValue;
        foreach (var tile in course)
        {
            bed = Math.Max(1, Math.Min(bed, heights[tile] - MaxCut));
            int cx = tile % sizeX, cy = tile / sizeX;
            for (var dy = -reach; dy <= reach; dy++)
            for (var dx = -reach; dx <= reach; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if (x < 0 || y < 0 || x >= sizeX || y >= sizeY || dx * dx + dy * dy > (radius + 0.5) * (radius + 0.5))
                    continue;
                Lower(y * sizeX + x, bed, original, heights);
            }
        }

        return bed;
    }

    private void CarveLake(List<int> lake, int bed, int[] original, int[] heights)
    {
        foreach (var tile in lake)
            Lower(tile, bed, original, heights);
    }

    private void Lower(int tile, int target, int[] original, int[] heights)
    {
        heights[tile] = Math.Max(Math.Max(1, original[tile] - MaxCut), Math.Min(heights[tile], target));
        Water[tile] = true;
    }

    private bool IsEdge(int tile)
    {
        int x = tile % sizeX, y = tile / sizeX;
        return x == 0 || y == 0 || x == sizeX - 1 || y == sizeY - 1;
    }

    private double Distance(int x, int y, int tile)
    {
        double dx = x - tile % sizeX, dy = y - tile / sizeX;
        return Math.Sqrt(dx * dx + dy * dy);
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
}

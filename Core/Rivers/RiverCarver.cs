using Timberborn.TerrainGenerator.Core.Common;

namespace Timberborn.TerrainGenerator.Core.Rivers;

public sealed class RiverCarverSettings
{
    public int Rivers = 3;

    // bed width in tiles
    public int Width = 3;

    // the head sits this many tiles before the peak, along the climb from the random start
    public int HeadDistance = 4;

    // basins up to this many tiles below their rim are filled and the river flows on over the rim; larger ones are
    // tunnelled through towards the nearest map edge
    public int MaxBasinArea = 500;

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

// Carves rivers into a surface. Each river starts at a random tile, climbs to the nearest top, and has its head a
// few tiles before the peak. From the head it runs down the steepest descent. At a pit it floods the basin lowest first:
// the water fills it up to the rim, and the river flows on from where it spills over. A basin too big to fill is
// tunnelled through instead, straight towards the nearest map edge, until the tunnel comes out into the open. A river
// ends at a map edge or where it joins an earlier river. River beds lower the terrain by at most one block; only
// tunnels cut deeper.
public sealed class RiverCarver
{
    // height in voxels of a tunnel's passage
    public const int TunnelHeight = 2;

    private static readonly (int X, int Y)[] Neighbours =
        { (-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1) };

    private const int HeadAttempts = 50;

    // a river bed lowers the terrain by at most this many blocks (bed and width); tunnels are the exception
    private const int MaxCut = 1;

    private readonly RiverCarverSettings settings;
    private readonly int sizeX;
    private readonly int sizeY;
    private readonly List<(int X, int Y, int Z)> tunnels = new();

    public RiverCarver(RiverCarverSettings settings, int sizeX, int sizeY)
    {
        if (settings.Width < 1)
            throw new ArgumentOutOfRangeException(nameof(settings.Width), "Must be at least 1");
        this.settings = settings;
        this.sizeX = sizeX;
        this.sizeY = sizeY;
    }

    public List<RiverHead> Heads { get; } = new();

    // tiles covered by river beds, filled basins and tunnels, indexed y * sizeX + x
    public bool[] Water { get; private set; } = Array.Empty<bool>();

    // tunnel passages: TunnelHeight voxels from (X, Y, Z) up are cleared below the surface (see CarveTunnels)
    public IReadOnlyList<(int X, int Y, int Z)> Tunnels => tunnels;

    // heights indexed y * sizeX + x; returns the carved heights. Tunnels are not part of the heights: apply them to
    // the voxels with CarveTunnels after applying the heights
    public int[] Carve(int[] heights, Action<float>? progress = null)
    {
        var random = new Random(settings.Seed);
        var result = (int[])heights.Clone();
        Water = new bool[heights.Length];
        tunnels.Clear();
        // tiles on an earlier river's course; a later river that reaches one joins it
        var courses = new bool[heights.Length];
        var climbSurface = Smooth(heights);
        for (var river = 0; river < settings.Rivers; river++)
        {
            var head = FindHead(random, result, climbSurface);
            if (head < 0) break;
            var (course, tunnelled, basins) = TraceCourse(head, result, courses);
            CarveCourse(course, tunnelled, heights, result);
            foreach (var tile in basins) Water[tile] = true;
            foreach (var tile in course) courses[tile] = true;
            Heads.Add(new RiverHead(head % sizeX, head / sizeX, result[head]));
            progress?.Invoke((river + 1f) / settings.Rivers);
        }

        return result;
    }

    // clears the tunnel passages in a map whose surface already has the carved heights
    public void CarveTunnels(VoxelMap map)
    {
        foreach (var (x, y, z) in tunnels)
            for (var dz = 0; dz < TunnelHeight && z + dz < map.SizeZ; dz++)
                map[x, y, z + dz] = false;
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

    // random start, steepest ascent to a top, head HeadDistance tiles before the top along the climb. Of several
    // random starts the one with the biggest climb wins, so rivers do not start on small bumps in low ground; heads
    // near the map edge or near earlier heads, and starts with nothing to climb, are skipped
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

            var climb = heights[tile] - heights[start];
            if (climb < 2 || climb <= bestClimb) continue;
            var head = path[Math.Max(0, path.Count - 1 - settings.HeadDistance)];
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

    // the river's tiles from head to mouth, which of them are tunnelled, and the tiles of the basins it fills
    private (List<int> Course, HashSet<int> Tunnelled, List<int> Basins) TraceCourse(int head, int[] heights,
        bool[] courses)
    {
        var course = new List<int> { head };
        var onCourse = new HashSet<int> { head };
        var tunnelled = new HashSet<int>();
        var basins = new List<int>();
        var tile = head;
        while (!IsEdge(tile) && !courses[tile])
        {
            var next = tile;
            foreach (var neighbour in NeighboursOf(tile))
                if (heights[neighbour] < heights[next] && !onCourse.Contains(neighbour))
                    next = neighbour;
            if (next != tile)
            {
                Add(next);
                tile = next;
                continue;
            }

            // a pit or a flat: fill the basin up to its rim and flow on from there, or tunnel out if it is too big
            var (spillPath, basin) = Flood(tile, heights, onCourse, courses);
            if (spillPath != null)
            {
                basins.AddRange(basin);
                foreach (var step in spillPath) Add(step);
            }
            else
            {
                var floor = heights[tile] - MaxCut;
                foreach (var step in Tunnel(tile, floor, heights))
                {
                    Add(step);
                    if (heights[step] > floor)
                        tunnelled.Add(step);
                }

                // the tunnel ends at the first tile in the open, unless it ran to the map edge
                var exit = course[course.Count - 1];
                if (tunnelled.Contains(exit)) break;
            }

            tile = course[course.Count - 1];
        }

        return (course, tunnelled, basins);

        void Add(int step)
        {
            course.Add(step);
            onCourse.Add(step);
        }
    }

    // floods from start lowest first. Returns the path to the first tile past the rim (or at an edge or on another
    // river) and the basin's tiles below the water level, or no path when the basin's area below the water level
    // reaches the largest basin to fill
    private (List<int>? SpillPath, List<int> Basin) Flood(int start, int[] heights, HashSet<int> onCourse,
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
                return (PathTo(tile, parent), visited.Where(t => heights[t] < level).ToList());
            level = height;
            visited.Add(tile);
            flooded[height] = flooded.TryGetValue(height, out var count) ? count + 1 : 1;
            if (flooded.Where(entry => entry.Key < level).Sum(entry => entry.Value) >= settings.MaxBasinArea)
                return (null, new List<int>());
            foreach (var neighbour in NeighboursOf(tile))
                if (!parent.ContainsKey(neighbour) && !onCourse.Contains(neighbour))
                {
                    parent[neighbour] = tile;
                    steps[neighbour] = steps[tile] + 1;
                    queue.Push(neighbour, Priority(heights[neighbour], steps[neighbour]));
                }
        }

        // nowhere to spill: tunnel out
        return (null, new List<int>());
    }

    // tiles from start straight towards the nearest map edge, at floor height: up to and including the first tile
    // whose ground is at or below the floor (the tunnel comes out there), or to the map edge
    private List<int> Tunnel(int start, int floor, int[] heights)
    {
        int x = start % sizeX, y = start / sizeX;
        var (dx, dy) = new[] { (x, (-1, 0)), (sizeX - 1 - x, (1, 0)), (y, (0, -1)), (sizeY - 1 - y, (0, 1)) }
            .OrderBy(edge => edge.Item1).First().Item2;
        var path = new List<int>();
        while (true)
        {
            x += dx;
            y += dy;
            if (x < 0 || y < 0 || x >= sizeX || y >= sizeY) break;
            var tile = y * sizeX + x;
            path.Add(tile);
            if (heights[tile] <= floor || IsEdge(tile)) break;
        }

        return path;
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
    // more than MaxCut below its original height. Tunnelled tiles keep the bed height as the tunnel floor: under
    // enough ground (at least one block of roof) a passage is cleared, elsewhere the ground is cut open to the floor
    private void CarveCourse(List<int> course, HashSet<int> tunnelled, int[] original, int[] heights)
    {
        var radius = (settings.Width - 1) / 2.0;
        var reach = (int)Math.Ceiling(radius);
        var bed = int.MaxValue;
        foreach (var tile in course)
        {
            var tunnel = tunnelled.Contains(tile);
            if (!tunnel)
                bed = Math.Max(1, Math.Min(bed, heights[tile] - MaxCut));
            int cx = tile % sizeX, cy = tile / sizeX;
            for (var dy = -reach; dy <= reach; dy++)
            for (var dx = -reach; dx <= reach; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if (x < 0 || y < 0 || x >= sizeX || y >= sizeY || dx * dx + dy * dy > (radius + 0.5) * (radius + 0.5))
                    continue;
                var index = y * sizeX + x;
                Water[index] = true;
                if (!tunnel)
                    heights[index] = Math.Max(Math.Max(1, original[index] - MaxCut), Math.Min(heights[index], bed));
                else if (heights[index] > bed + TunnelHeight)
                    tunnels.Add((x, y, bed));
                else
                    heights[index] = Math.Min(heights[index], bed);
            }
        }
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

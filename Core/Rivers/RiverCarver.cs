using Timberborn.TerrainGenerator.Core.Common;

namespace Timberborn.TerrainGenerator.Core.Rivers;

public sealed class RiverCarverSettings
{
    public int Rivers = 1;

    // bed width in tiles
    public int Width = 3;

    // the head sits this many tiles before the peak, along the climb from the random start
    public int HeadDistance = 4;

    // a basin fills with water (rising past low rims into neighbouring low ground) until it holds this many voxels of
    // water; then the river tunnels out towards the nearest map edge or lower ground
    public int MaxBasinVolume = 500;

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
// few tiles before the peak. From the head it runs down the steepest descent. At a pit the water rises, spreading over
// low rims into neighbouring low ground, until the basin holds the largest volume; then the river tunnels out,
// winding towards the nearest way out (a map edge or lower ground, whichever is closer). Rising water that reaches a
// map edge or another river drains there instead. A river ends at a map edge or where it joins an earlier river.
// River beds lower the terrain by at most one block; only tunnels cut deeper.
public sealed class RiverCarver
{
    // height in voxels of a tunnel's passage
    public const int TunnelHeight = 2;

    private static readonly (int X, int Y)[] Neighbours =
        { (-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1) };

    private const int HeadAttempts = 50;

    // contrast of the tunnel cost: a tile costs e^(TunnelWinding * (noise - 0.5)), so tunnels follow the noise's low
    // valleys; higher values make them wind more
    private const float TunnelWinding = 8;

    // a river bed lowers the terrain by at most this many blocks (bed and width); tunnels are the exception
    private const int MaxCut = 1;

    private readonly RiverCarverSettings settings;
    private readonly int sizeX;
    private readonly int sizeY;
    private readonly List<(int X, int Y, int Z)> tunnels = new();
    private float[] tunnelNoise = Array.Empty<float>();

    public RiverCarver(RiverCarverSettings settings, int sizeX, int sizeY)
    {
        if (settings.Width < 1)
            throw new ArgumentOutOfRangeException(nameof(settings.Width), "Must be at least 1");
        this.settings = settings;
        this.sizeX = sizeX;
        this.sizeY = sizeY;
    }

    public List<RiverHead> Heads { get; } = new();

    // per river: its head and then every tile downstream outside tunnels, where the water source can go if the head
    // turns out unsuitable (the game decides whether a source fits)
    public List<IReadOnlyList<RiverHead>> SourceSpots { get; } = new();

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
        var openCourses = new List<(List<int> Course, HashSet<int> Tunnelled)>();
        // tiles on an earlier river's course; a later river that reaches one joins it
        var courses = new bool[heights.Length];
        var climbSurface = Smooth(heights);
        tunnelNoise = SmoothNoise(random, 3);
        for (var river = 0; river < settings.Rivers; river++)
        {
            var head = FindHead(random, result, climbSurface);
            if (head < 0) break;
            var (course, tunnelled, basins) = TraceCourse(head, result, courses);
            CarveCourse(course, tunnelled, heights, result);
            foreach (var tile in basins) Water[tile] = true;
            foreach (var tile in course) courses[tile] = true;
            Heads.Add(new RiverHead(head % sizeX, head / sizeX, result[head]));
            openCourses.Add((course, tunnelled));
            progress?.Invoke((river + 1f) / settings.Rivers);
        }

        // the spots where each river's water source may go, on the final surface
        foreach (var (course, tunnelled) in openCourses)
            SourceSpots.Add(course.Where(tile => !tunnelled.Contains(tile))
                .Select(tile => new RiverHead(tile % sizeX, tile / sizeX, result[tile])).ToList());
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

            // a pit or a flat: the water rises until the basin holds the largest volume, then tunnels out, unless it
            // reaches a map edge or another river first
            var (drainPath, basin) = Flood(tile, heights, onCourse, courses);
            basins.AddRange(basin);
            if (drainPath != null)
            {
                foreach (var step in drainPath) Add(step);
            }
            else
            {
                var floor = heights[tile] - MaxCut;
                var tunnel = Tunnel(tile, floor, heights, onCourse);
                // no way out at all (the river's own course walls it in): the river ends in this basin
                if (tunnel.Count == 0) break;
                foreach (var step in tunnel)
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

    // floods from start lowest first: the water keeps rising, spreading over low rims into neighbouring low ground,
    // until it holds the largest basin volume. Returns the path to where it drains if it reaches a map edge or another
    // river first, and the tiles under water; no path means the basin is full and the river has to tunnel out
    private (List<int>? DrainPath, List<int> Basin) Flood(int start, int[] heights, HashSet<int> onCourse,
        bool[] courses)
    {
        var parent = new Dictionary<int, int> { [start] = start };
        // steps from start; at equal height nearer tiles flood first, so paths across flats are shortest
        var steps = new Dictionary<int, int> { [start] = 0 };
        var queue = new TileQueue();
        queue.Push(start, Priority(heights[start], 0));
        var level = heights[start];
        var flooded = new List<int>();
        // voxels of water: the depth below the level, summed over the flooded tiles
        long volume = 0;
        while (queue.TryPop(out var tile, out _))
        {
            var height = heights[tile];
            if (tile != start && (IsEdge(tile) || courses[tile]))
                return (PathTo(tile, parent), Underwater());
            if (height > level)
            {
                // rising to the next tile's height deepens the water on every flooded tile
                volume += (long)flooded.Count * (height - level);
                level = height;
            }
            else
            {
                // lower ground beyond a rim fills up to the level
                volume += level - height;
            }

            flooded.Add(tile);
            if (volume >= settings.MaxBasinVolume)
                return (null, Underwater());
            foreach (var neighbour in NeighboursOf(tile))
                if (!parent.ContainsKey(neighbour) && !onCourse.Contains(neighbour))
                {
                    parent[neighbour] = tile;
                    steps[neighbour] = steps[tile] + 1;
                    queue.Push(neighbour, Priority(heights[neighbour], steps[neighbour]));
                }
        }

        // nowhere left to flood: tunnel out
        return (null, Underwater());

        List<int> Underwater()
        {
            return flooded.Where(t => heights[t] < level).ToList();
        }
    }

    // the tunnel's tiles from start (excluded) to the nearest way out: a map edge tile or a tile whose ground is at or
    // below the floor (the tunnel comes out there), whichever the cheapest path reaches first, so both count equally.
    // Each tile costs 1 plus a smooth random amount, which makes the tunnel wind instead of running straight
    private List<int> Tunnel(int start, int floor, int[] heights, HashSet<int> onCourse)
    {
        var parent = new Dictionary<int, int> { [start] = start };
        var cost = new Dictionary<int, float> { [start] = 0 };
        var queue = new TileQueue();
        queue.Push(start, 0);
        while (queue.TryPop(out var tile, out var tileCost))
        {
            if (tileCost > cost[tile]) continue;
            if (tile != start && (heights[tile] <= floor || IsEdge(tile)))
                return PathTo(tile, parent);
            int x = tile % sizeX, y = tile / sizeX;
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= sizeX || ny >= sizeY) continue;
                var neighbour = ny * sizeX + nx;
                if (onCourse.Contains(neighbour)) continue;
                var neighbourCost = tileCost + (float)Math.Exp(TunnelWinding * (tunnelNoise[neighbour] - 0.5));
                if (cost.TryGetValue(neighbour, out var known) && known <= neighbourCost) continue;
                cost[neighbour] = neighbourCost;
                parent[neighbour] = tile;
                queue.Push(neighbour, neighbourCost);
            }
        }

        return new List<int>();
    }

    // smooth random values in 0..1, one per tile: blurred white noise, stretched to the full range
    private float[] SmoothNoise(Random random, int radius)
    {
        var values = Enumerable.Range(0, sizeX * sizeY).Select(_ => (float)random.NextDouble()).ToArray();
        for (var pass = 0; pass < 2; pass++)
        {
            var blurred = new float[values.Length];
            for (var y = 0; y < sizeY; y++)
            for (var x = 0; x < sizeX; x++)
            {
                float sum = 0;
                var count = 0;
                for (var dy = -radius; dy <= radius; dy++)
                for (var dx = -radius; dx <= radius; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= sizeX || ny >= sizeY) continue;
                    sum += values[ny * sizeX + nx];
                    count++;
                }

                blurred[y * sizeX + x] = sum / count;
            }

            values = blurred;
        }

        float min = values.Min(), range = Math.Max(1e-6f, values.Max() - min);
        return values.Select(value => (value - min) / range).ToArray();
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

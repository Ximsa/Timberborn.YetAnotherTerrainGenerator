using Timberborn.TerrainGenerator.Core.Noise3D;

namespace Timberborn.TerrainGenerator.Core.Trees;

public sealed class TreePlacerSettings
{
    // share of the plantable land inside forest patches
    public double ForestCoverage = 0.2;

    // share of the plantable land inside bush patches (outside forests)
    public double BushCoverage = 0.12;

    // typical width of a patch in tiles
    public int PatchSize = 32;

    // typical width of a single-species clump inside a patch in tiles
    public int ClumpSize = 6;

    // chance of a plant on a tile inside a patch, far from water
    public double Density = 0.35;

    // added to the density right next to water, fading out over WaterReach tiles
    public double WaterBonus = 0.3;
    public int WaterReach = 10;

    // relative share of each tree and bush species; zero leaves a species out
    public double[] TreeWeights = Array.Empty<double>();
    public double[] BushWeights = Array.Empty<double>();

    public int Seed = 1;

    public TreePlacerSettings Clone()
    {
        var clone = (TreePlacerSettings)MemberwiseClone();
        clone.TreeWeights = (double[])TreeWeights.Clone();
        clone.BushWeights = (double[])BushWeights.Clone();
        return clone;
    }
}

public enum PlantKind
{
    Tree,
    Bush
}

// one plant on a tile; Species indexes TreeWeights or BushWeights, Z is the surface height it stands on
public readonly record struct Plant(int X, int Y, int Z, PlantKind Kind, int Species);

// Places forests and bush patches with noise: one noise field picks forest tiles, another bush tiles outside forests,
// each thresholded to its coverage. Inside a patch, species come in clumps (cells of a noise-warped grid, each with one
// weighted random species), so trees only mix with trees and bushes with bushes. Plants get denser near water and stay
// off water and cliff edges.
public sealed class TreePlacer
{
    private readonly TreePlacerSettings settings;
    private readonly int sizeX;
    private readonly int sizeY;

    public TreePlacer(TreePlacerSettings settings, int sizeX, int sizeY)
    {
        this.settings = settings;
        this.sizeX = sizeX;
        this.sizeY = sizeY;
    }

    // heights and water indexed y * sizeX + x
    public List<Plant> Place(int[] heights, bool[] water, Action<float>? progress = null)
    {
        var random = new Random(settings.Seed);
        var plantable = Enumerable.Range(0, heights.Length).Select(tile => IsPlantable(tile, heights, water)).ToArray();
        var forestNoise = Noise(random, settings.PatchSize);
        var bushNoise = Noise(random, settings.PatchSize * 0.6);
        var warpX = Noise(random, settings.ClumpSize * 2.0);
        var warpY = Noise(random, settings.ClumpSize * 2.0);
        progress?.Invoke(0.3f);
        var forestThreshold = Threshold(forestNoise, plantable, settings.ForestCoverage);
        var bushThreshold = Threshold(bushNoise, plantable, settings.BushCoverage / Math.Max(1e-6, 1 - settings.ForestCoverage));
        var waterDistance = DistanceToWater(water);
        progress?.Invoke(0.5f);

        var plants = new List<Plant>();
        var clumpSalt = random.Next();
        for (var tile = 0; tile < heights.Length; tile++)
        {
            if (!plantable[tile]) continue;
            PlantKind kind;
            double[] weights;
            if (forestNoise[tile] >= forestThreshold)
                (kind, weights) = (PlantKind.Tree, settings.TreeWeights);
            else if (bushNoise[tile] >= bushThreshold)
                (kind, weights) = (PlantKind.Bush, settings.BushWeights);
            else
                continue;
            var nearWater = Math.Max(0, 1 - (double)waterDistance[tile] / Math.Max(1, settings.WaterReach));
            if (random.NextDouble() >= settings.Density + settings.WaterBonus * nearWater) continue;
            int x = tile % sizeX, y = tile / sizeX;
            // the clump cell this tile falls into, with the grid bent by noise so clumps are not square
            var warp = settings.ClumpSize * 0.75;
            var cellX = (int)Math.Floor((x + warpX[tile] * warp) / settings.ClumpSize);
            var cellY = (int)Math.Floor((y + warpY[tile] * warp) / settings.ClumpSize);
            var species = PickWeighted(weights, Hash(cellX, cellY, clumpSalt + (int)kind));
            if (species >= 0)
                plants.Add(new Plant(x, y, heights[tile], kind, species));
        }

        progress?.Invoke(1);
        return plants;
    }

    // flat-ish land: not water, and no neighbour more than one tile higher or lower (keeps plants off cliff edges)
    private bool IsPlantable(int tile, int[] heights, bool[] water)
    {
        if (water[tile]) return false;
        int x = tile % sizeX, y = tile / sizeX;
        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            int nx = x + dx, ny = y + dy;
            if (nx < 0 || ny < 0 || nx >= sizeX || ny >= sizeY) continue;
            if (Math.Abs(heights[ny * sizeX + nx] - heights[tile]) > 1) return false;
        }

        return true;
    }

    // octave noise with features about featureSize tiles wide, indexed y * sizeX + x
    private double[] Noise(Random random, double featureSize)
    {
        var noise = new Noise3DOctaveNoise(new Random(random.Next()), 4);
        var scale = 1 / Math.Max(1, featureSize);
        // Generate2D indexes x * sizeZ + z; the second axis is the map's y
        var values = noise.Generate2D(random.Next(10000), random.Next(10000), sizeX, sizeY, scale, scale);
        var result = new double[sizeX * sizeY];
        for (var x = 0; x < sizeX; x++)
        for (var y = 0; y < sizeY; y++)
            result[y * sizeX + x] = values[x * sizeY + y];
        // the octaves sum to roughly -2 .. 2
        return result.Select(value => value / 2).ToArray();
    }

    // the noise value above which the given share of plantable tiles lies
    private static double Threshold(double[] noise, bool[] plantable, double coverage)
    {
        var values = noise.Where((_, tile) => plantable[tile]).OrderByDescending(value => value).ToArray();
        if (coverage <= 0 || values.Length == 0) return double.PositiveInfinity;
        if (coverage >= 1) return double.NegativeInfinity;
        return values[(int)(coverage * (values.Length - 1))];
    }

    // 8-connected steps to the nearest water tile, capped at the water reach
    private int[] DistanceToWater(bool[] water)
    {
        var cap = Math.Max(1, settings.WaterReach);
        var distance = Enumerable.Repeat(cap, water.Length).ToArray();
        var queue = new Queue<int>();
        for (var tile = 0; tile < water.Length; tile++)
            if (water[tile])
            {
                distance[tile] = 0;
                queue.Enqueue(tile);
            }

        while (queue.Count > 0)
        {
            var tile = queue.Dequeue();
            if (distance[tile] + 1 >= cap) continue;
            int x = tile % sizeX, y = tile / sizeX;
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= sizeX || ny >= sizeY) continue;
                var neighbour = ny * sizeX + nx;
                if (distance[neighbour] <= distance[tile] + 1) continue;
                distance[neighbour] = distance[tile] + 1;
                queue.Enqueue(neighbour);
            }
        }

        return distance;
    }

    // index chosen by weight with a uniform value in [0, 1), or -1 when all weights are zero
    private static int PickWeighted(double[] weights, double value)
    {
        var total = weights.Sum();
        if (total <= 0) return -1;
        var target = value * total;
        for (var i = 0; i < weights.Length; i++)
        {
            target -= weights[i];
            if (target < 0 && weights[i] > 0) return i;
        }

        return Array.FindLastIndex(weights, weight => weight > 0);
    }

    // deterministic uniform value in [0, 1) for a clump cell
    private static double Hash(int x, int y, int salt)
    {
        unchecked
        {
            var h = (uint)(x * 73856093 ^ y * 19349663 ^ salt * 83492791);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (h & 0xFFFFFF) / (double)0x1000000;
        }
    }
}

using Timberborn.TerrainGenerator.Core.Common;

namespace Timberborn.TerrainGenerator.Core.Wfc;

public sealed class WfcSettings
{
    // pattern size in voxels: width across (x and y), height up
    public int PatternWidth = 7;
    public int PatternHeight = 5;

    // removed placements per attempt before starting over
    public int MaxDeletions = 2000;

    // attempts with different random choices before giving up
    public int MaxAttempts = 30;

    // reject candidates that would leave a free neighbour without candidates
    public bool Lookahead = true;

    // repairs of the same cell before the placements in a box around it are cleared; 0 never clears
    public int ClearEvery = 8;

    // factor (0..1) on a pattern's weight for each cell layer (pattern height - 1 voxels) away from where it was seen
    // in the sources; 1 ignores heights
    public double HeightPenalty = 0.25;

    public int Seed;

    public WfcSettings Clone()
    {
        return (WfcSettings)MemberwiseClone();
    }
}

public readonly struct WfcAttempt
{
    public WfcAttempt(bool solved, int placements, int repairs, int deletions, int lookaheadRejections, long ms)
    {
        Solved = solved;
        Placements = placements;
        Repairs = repairs;
        Deletions = deletions;
        LookaheadRejections = lookaheadRejections;
        Milliseconds = ms;
    }

    public bool Solved { get; }
    public int Placements { get; }
    public int Repairs { get; }
    public int Deletions { get; }
    public int LookaheadRejections { get; }
    public long Milliseconds { get; }

    public override string ToString()
    {
        return $"{(Solved ? "solved" : "failed")} in {Milliseconds} ms: {Placements} placements, {Repairs} repairs, " +
               $"{Deletions} deletions, {LookaheadRejections} lookahead rejections";
    }
}

// Generates terrain with face-lookup wave function collapse (docs/wfc-face-lookup.md): voxel patterns from the source
// maps are placed on a grid whose cells lie one less than the pattern size apart, so neighbours share one voxel layer
// and fit exactly when that face is equal. The grid covers the map rounded up to whole cells and is cropped afterwards.
public sealed class WfcTerrainGenerator
{
    public const string NoSolutionKey = "Ximsa.TerrainGenerator.Wfc.NoSolution";

    private readonly WfcSettings settings;
    private WfcPatternIndex? index;

    public WfcTerrainGenerator(WfcSettings settings)
    {
        this.settings = settings;
    }

    public WfcPatternIndex Index => index ?? throw new InvalidOperationException("Learn must run first");

    // share of solid voxels per height, averaged over the sources that reach that height
    public double[] SourceProfile { get; private set; } = Array.Empty<double>();

    public List<WfcAttempt> Attempts { get; } = new();

    public void Learn(IReadOnlyList<VoxelMap> sources, Action<float>? progress = null)
    {
        index = WfcPatternIndex.Learn(new WfcPatternShape(settings.PatternWidth, settings.PatternHeight), sources,
            progress);
        var height = sources.Max(map => map.SizeZ);
        var sums = new double[height];
        var counts = new int[height];
        foreach (var map in sources)
        {
            var profile = FillProfile(map);
            for (var z = 0; z < profile.Length; z++)
            {
                sums[z] += profile[z];
                counts[z]++;
            }
        }

        SourceProfile = sums.Select((sum, z) => sum / counts[z]).ToArray();
    }

    // sizes are the Timberborn map size: x and y across, z up; the bottom layer is always solid
    public VoxelMap Generate(int sizeX, int sizeY, int sizeZ, Action<float>? progress = null)
    {
        var patterns = Index;
        var shape = patterns.Shape;
        Attempts.Clear();
        int cellsX = Cells(sizeX, shape.StrideAcross), cellsY = Cells(sizeY, shape.StrideAcross),
            cellsZ = Cells(sizeZ, shape.StrideUp);
        var layerWeights = patterns.LayerWeights(cellsZ, settings.HeightPenalty);
        for (var attempt = 0; attempt < settings.MaxAttempts; attempt++)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var solver = new WfcSolver(patterns, cellsX, cellsY, cellsZ, new Random(settings.Seed + attempt * 7919),
                settings.MaxDeletions, settings.Lookahead, settings.ClearEvery, layerWeights);
            var solved = solver.Run(progress);
            Attempts.Add(new WfcAttempt(solved, solver.Placements, solver.Repairs, solver.Deletions,
                solver.LookaheadRejections, stopwatch.ElapsedMilliseconds));
            if (solved)
                return Materialise(solver, sizeX, sizeY, sizeZ);
        }

        throw new GenerationFailedException(NoSolutionKey,
            $"Wave function collapse did not finish in {settings.MaxAttempts} attempts; try other source maps");
    }

    // cells stride apart needed to cover size voxels (each cell covers stride + 1)
    private static int Cells(int size, int stride)
    {
        return Math.Max(1, (size - 1 + stride - 1) / stride);
    }

    private VoxelMap Materialise(WfcSolver solver, int sizeX, int sizeY, int sizeZ)
    {
        var shape = Index.Shape;
        var map = new VoxelMap(sizeX, sizeY, sizeZ, new bool[sizeX * sizeY * sizeZ]);
        var written = new bool[map.Voxels.Length];
        var mismatches = 0;
        for (var cz = 0; cz < solver.SizeZ; cz++)
        for (var cy = 0; cy < solver.SizeY; cy++)
        for (var cx = 0; cx < solver.SizeX; cx++)
        {
            var code = Index.OrientedCode(solver.PatternAt(cx, cy, cz));
            for (var dz = 0; dz < shape.Height; dz++)
            for (var dy = 0; dy < shape.Width; dy++)
            for (var dx = 0; dx < shape.Width; dx++)
            {
                int x = cx * shape.StrideAcross + dx, y = cy * shape.StrideAcross + dy, z = cz * shape.StrideUp + dz;
                if (x >= sizeX || y >= sizeY || z >= sizeZ)
                    continue;
                var i = (z * sizeY + y) * sizeX + x;
                var solid = shape.Voxel(code, dx, dy, dz);
                // shared layers are written twice and must agree
                if (written[i] && map.Voxels[i] != solid)
                    mismatches++;
                written[i] = true;
                map.Voxels[i] = solid;
            }
        }

        if (mismatches > 0)
            throw new InvalidOperationException($"{mismatches} voxels differ between neighbouring patterns");
        for (var i = 0; i < sizeX * sizeY; i++)
            map.Voxels[i] = true;
        return map;
    }

    // share of pattern-sized windows (stride 1) of the map that never occur in the sources
    public double Novelty(VoxelMap map)
    {
        long total = 0, unseen = 0;
        WfcPatternIndex.ForEachWindow(Index.Shape, map, (_, canonical) =>
        {
            total++;
            if (!Index.ContainsCanonical(canonical))
                unseen++;
        });
        return total == 0 ? 0 : (double)unseen / total;
    }

    // share of solid voxels per height
    public static double[] FillProfile(VoxelMap map)
    {
        var area = map.SizeX * map.SizeY;
        var profile = new double[map.SizeZ];
        for (var z = 0; z < map.SizeZ; z++)
        {
            var solid = 0;
            for (var i = 0; i < area; i++)
                if (map.Voxels[z * area + i])
                    solid++;
            profile[z] = (double)solid / area;
        }

        return profile;
    }
}

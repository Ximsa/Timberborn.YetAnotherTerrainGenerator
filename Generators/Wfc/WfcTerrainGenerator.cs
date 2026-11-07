namespace Timberborn.TerrainGenerator.Generators.Wfc;

public sealed class WfcSettings
{
    // edge length of the patterns in coarse blocks (2 or 3); larger patterns copy bigger structures but are slower
    public int PatternSize = 3;

    // add rotations around the vertical axis and mirror images of the source patterns
    public bool Symmetry = true;

    // treat the source maps as wrapping around at their edges
    public bool PeriodicInput;

    // factor (0..1) on a pattern's probability for each height layer away from where it was seen in the sources;
    // small values strongly prefer the sources' heights without forbidding others
    public double HeightPenalty = 0.5;

    // share of a 4 x 4 x 4 block's voxels that must be solid for the block to count as solid; low values keep thin
    // details (ridges, pillars, overhangs), high values give chunkier terrain
    public double SolidThreshold = 0.25;

    // keep the most frequent patterns until they cover this share of all occurrences; drops rare patterns, which are
    // most of the patterns of large source sets but little of their terrain
    public double PatternCoverage = 0.95;

    // choices undone after contradictions (per attempt) before starting over with other random choices
    public int MaxBacktracks = 2000;

    // attempts with different random choices before giving up after contradictions
    public int Attempts = 5;

    public int Seed;

    public WfcSettings Clone()
    {
        return (WfcSettings)MemberwiseClone();
    }
}

// Generates terrain with 3D overlapping-model wave function collapse, learning voxel patterns from source maps.
// WFC runs on a coarse grid of 4 x 4 x 4 voxel blocks: patterns of single voxels are far too small to capture
// Timberborn's terraces and cliffs (they degenerate into pillars), while coarse patterns span 8-12 tiles and keep the
// number of distinct patterns small. Blocks are scaled back up afterwards, so heights come in steps of 4.
public sealed class WfcTerrainGenerator
{
    public const int BlockSize = 4;

    private readonly WfcSettings settings;
    private WfcPatternSet? patterns;

    public WfcTerrainGenerator(WfcSettings settings)
    {
        this.settings = settings;
    }

    public int PatternCount => patterns?.Count ?? 0;

    public int AttemptsUsed { get; private set; }

    public int Backtracks { get; private set; }

    public void Learn(IEnumerable<VoxelMap> sources, Action<float>? progress = null)
    {
        if (settings.PatternCoverage <= 0 || settings.PatternCoverage > 1)
            throw new ArgumentOutOfRangeException(nameof(settings.PatternCoverage), "Must be above 0 and at most 1");
        if (settings.SolidThreshold < 0 || settings.SolidThreshold > 1)
            throw new ArgumentOutOfRangeException(nameof(settings.SolidThreshold), "Must be between 0 and 1");
        patterns = WfcPatternSet.Learn(sources.Select(map => Downsample(map, settings.SolidThreshold)),
            settings.PatternSize, settings.Symmetry, settings.PeriodicInput, settings.HeightPenalty,
            settings.PatternCoverage, progress);
    }

    // sizes are the Timberborn map size: x and y across, z up; the bottom layer is always solid
    public VoxelMap Generate(int sizeX, int sizeY, int sizeZ, Action<float>? progress = null)
    {
        if (patterns == null)
            throw new InvalidOperationException("Learn must run first");
        var n = patterns.Size;
        int coarseX = Blocks(sizeX), coarseY = Blocks(sizeY), coarseZ = Blocks(sizeZ);
        if (coarseX < n || coarseY < n || coarseZ < n)
            throw new InvalidOperationException("The map is too small for the pattern size");
        for (var attempt = 0; attempt < settings.Attempts; attempt++)
        {
            AttemptsUsed = attempt + 1;
            var solver = new WfcSolver(patterns, coarseX - n + 1, coarseY - n + 1, coarseZ - n + 1,
                new Random(settings.Seed + attempt * 7919), settings.MaxBacktracks);
            var solved = solver.Run(progress);
            Backtracks += solver.Backtracks;
            if (solved)
                return Upsample(ToCoarseVoxels(solver, coarseX, coarseY, coarseZ), sizeX, sizeY, sizeZ);
        }

        throw new InvalidOperationException(
            $"Wave function collapse ran into contradictions {settings.Attempts} times; " +
            "try another seed, a smaller pattern size or other source maps");
    }

    private static int Blocks(int size)
    {
        return (size + BlockSize - 1) / BlockSize;
    }

    // a block is solid when at least the given share of its voxels (within the map) is solid
    public static VoxelMap Downsample(VoxelMap map, double solidThreshold)
    {
        int coarseX = Blocks(map.SizeX), coarseY = Blocks(map.SizeY), coarseZ = Blocks(map.SizeZ);
        var coarse = new VoxelMap(coarseX, coarseY, coarseZ, new bool[coarseX * coarseY * coarseZ]);
        for (var cz = 0; cz < coarseZ; cz++)
        for (var cy = 0; cy < coarseY; cy++)
        for (var cx = 0; cx < coarseX; cx++)
        {
            int solid = 0, total = 0;
            for (var z = cz * BlockSize; z < Math.Min(map.SizeZ, (cz + 1) * BlockSize); z++)
            for (var y = cy * BlockSize; y < Math.Min(map.SizeY, (cy + 1) * BlockSize); y++)
            for (var x = cx * BlockSize; x < Math.Min(map.SizeX, (cx + 1) * BlockSize); x++)
            {
                total++;
                if (map[x, y, z])
                    solid++;
            }

            coarse[cx, cy, cz] = solid >= solidThreshold * total;
        }

        return coarse;
    }

    public static VoxelMap Upsample(VoxelMap coarse, int sizeX, int sizeY, int sizeZ)
    {
        var voxels = new VoxelMap(sizeX, sizeY, sizeZ, new bool[sizeX * sizeY * sizeZ]);
        for (var z = 0; z < sizeZ; z++)
        for (var y = 0; y < sizeY; y++)
        for (var x = 0; x < sizeX; x++)
            voxels[x, y, z] = z == 0 || coarse[x / BlockSize, y / BlockSize, z / BlockSize];
        return voxels;
    }

    // each coarse voxel comes from the pattern of the nearest cell, at its offset within that pattern
    private VoxelMap ToCoarseVoxels(WfcSolver solver, int sizeX, int sizeY, int sizeZ)
    {
        var n = patterns!.Size;
        var voxels = new VoxelMap(sizeX, sizeY, sizeZ, new bool[sizeX * sizeY * sizeZ]);
        for (var z = 0; z < sizeZ; z++)
        for (var y = 0; y < sizeY; y++)
        for (var x = 0; x < sizeX; x++)
        {
            int cx = Math.Min(x, solver.SizeX - 1), cy = Math.Min(y, solver.SizeY - 1),
                cz = Math.Min(z, solver.SizeZ - 1);
            var code = patterns.Codes[solver.PatternAt(cx, cy, cz)];
            voxels[x, y, z] = ((code >> WfcPatternSet.Bit(n, x - cx, y - cy, z - cz)) & 1) != 0;
        }

        return voxels;
    }
}

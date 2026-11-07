using Timberborn.TerrainGenerator.Core.Common;

namespace Timberborn.TerrainGenerator.Core.Wfc;

// how the solver picks the next cell to collapse; ties are broken randomly
public enum WfcCellSelection
{
    // the cell whose remaining patterns are least uncertain, weighted by their probabilities (classic WFC)
    LowestEntropy,

    // the cell with the fewest remaining patterns, ignoring their weights
    FewestPatterns,

    // lowest entropy, but each height layer is finished before the next, from the ground up
    LayerByLayer,

    // a fixed order: the bottom layer first, row by row
    Scanline,

    // the undecided cell nearest the map centre (lowest entropy among equals), so terrain grows outwards
    CentreOutward,

    // a random undecided cell
    RandomOrder
}

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

    // edge length in voxels of the coarse blocks WFC works on; heights come in steps of this size
    public int BlockSize = WfcTerrainGenerator.DefaultBlockSize;

    // share of a block's voxels that must be solid for the block to count as solid; low values keep thin
    // details (ridges, pillars, overhangs), high values give chunkier terrain
    public double SolidThreshold = 0.25;

    // keep the most frequent patterns until they cover this share of all occurrences; drops rare patterns, which are
    // most of the patterns of large source sets but little of their terrain
    public double PatternCoverage = 0.95;

    // choices undone after contradictions (per attempt) before starting over with other random choices
    public int MaxBacktracks = 2000;

    // attempts with different random choices before giving up after contradictions
    public int Attempts = 5;

    public WfcCellSelection CellSelection = WfcCellSelection.LowestEntropy;

    public int Seed;

    public WfcSettings Clone()
    {
        return (WfcSettings)MemberwiseClone();
    }
}

// Generates terrain with 3D overlapping-model wave function collapse, learning voxel patterns from source maps.
// WFC runs on a coarse grid of voxel blocks (4 x 4 x 4 by default): patterns of single voxels are far too small to
// capture Timberborn's terraces and cliffs (they degenerate into pillars), while coarse patterns span many tiles and
// keep the number of distinct patterns small. Blocks are scaled back up afterwards, so heights come in steps of the
// block size.
public sealed class WfcTerrainGenerator
{
    public const int DefaultBlockSize = 4;

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
        if (settings.BlockSize < 1)
            throw new ArgumentOutOfRangeException(nameof(settings.BlockSize), "Must be at least 1");
        if (settings.PatternCoverage <= 0 || settings.PatternCoverage > 1)
            throw new ArgumentOutOfRangeException(nameof(settings.PatternCoverage), "Must be above 0 and at most 1");
        if (settings.SolidThreshold < 0 || settings.SolidThreshold > 1)
            throw new ArgumentOutOfRangeException(nameof(settings.SolidThreshold), "Must be between 0 and 1");
        var coarseSources = sources.Select(map => Downsample(map, settings.SolidThreshold, settings.BlockSize));
        patterns = WfcPatternSet.Learn(coarseSources,
            settings.PatternSize, settings.Symmetry, settings.PeriodicInput, settings.HeightPenalty,
            settings.PatternCoverage, progress);
    }

    // sizes are the Timberborn map size: x and y across, z up; the bottom layer is always solid
    public VoxelMap Generate(int sizeX, int sizeY, int sizeZ, Action<float>? progress = null)
    {
        if (patterns == null)
            throw new InvalidOperationException("Learn must run first");
        var n = patterns.Size;
        var blockSize = settings.BlockSize;
        int coarseX = Blocks(sizeX, blockSize), coarseY = Blocks(sizeY, blockSize), coarseZ = Blocks(sizeZ, blockSize);
        if (coarseX < n || coarseY < n || coarseZ < n)
            throw new InvalidOperationException("The map is too small for the pattern size");
        for (var attempt = 0; attempt < settings.Attempts; attempt++)
        {
            AttemptsUsed = attempt + 1;
            var solver = new WfcSolver(patterns, coarseX - n + 1, coarseY - n + 1, coarseZ - n + 1,
                new Random(settings.Seed + attempt * 7919), settings.MaxBacktracks, settings.CellSelection);
            var solved = solver.Run(progress);
            Backtracks += solver.Backtracks;
            if (solved)
                return Upsample(ToCoarseVoxels(solver, coarseX, coarseY, coarseZ), sizeX, sizeY, sizeZ, blockSize);
        }

        throw new InvalidOperationException(
            $"Wave function collapse ran into contradictions {settings.Attempts} times; " +
            "try another seed, a smaller pattern size or other source maps");
    }

    private static int Blocks(int size, int blockSize)
    {
        return (size + blockSize - 1) / blockSize;
    }

    // a block is solid when at least the given share of its voxels (within the map) is solid
    public static VoxelMap Downsample(VoxelMap map, double solidThreshold, int blockSize = DefaultBlockSize)
    {
        int coarseX = Blocks(map.SizeX, blockSize), coarseY = Blocks(map.SizeY, blockSize),
            coarseZ = Blocks(map.SizeZ, blockSize);
        var coarse = new VoxelMap(coarseX, coarseY, coarseZ, new bool[coarseX * coarseY * coarseZ]);
        for (var cz = 0; cz < coarseZ; cz++)
        for (var cy = 0; cy < coarseY; cy++)
        for (var cx = 0; cx < coarseX; cx++)
        {
            int solid = 0, total = 0;
            for (var z = cz * blockSize; z < Math.Min(map.SizeZ, (cz + 1) * blockSize); z++)
            for (var y = cy * blockSize; y < Math.Min(map.SizeY, (cy + 1) * blockSize); y++)
            for (var x = cx * blockSize; x < Math.Min(map.SizeX, (cx + 1) * blockSize); x++)
            {
                total++;
                if (map[x, y, z])
                    solid++;
            }

            coarse[cx, cy, cz] = solid >= solidThreshold * total;
        }

        return coarse;
    }

    public static VoxelMap Upsample(VoxelMap coarse, int sizeX, int sizeY, int sizeZ,
        int blockSize = DefaultBlockSize)
    {
        var voxels = new VoxelMap(sizeX, sizeY, sizeZ, new bool[sizeX * sizeY * sizeZ]);
        for (var z = 0; z < sizeZ; z++)
        for (var y = 0; y < sizeY; y++)
        for (var x = 0; x < sizeX; x++)
            voxels[x, y, z] = z == 0 || coarse[x / blockSize, y / blockSize, z / blockSize];
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

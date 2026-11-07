namespace Timberborn.TerrainGenerator.Generators.Noise3D;

public enum Noise3DClimate
{
    // rainforest everywhere: hot and wet, so the hill noise applies in full (the hilliest climate)
    Rainforest,

    // varying biome climate (temperature and humidity noise), including flat dry areas
    VaryingBiomes
}

public sealed class Noise3DSettings
{
    public const int WorldHeight = 128;
    public const int SeaLevel = 64;

    public int Seed;

    public Noise3DClimate Climate = Noise3DClimate.Rainforest;

    // 3D Noise height that becomes Timberborn height 0 (just below sea level, so oceans become shallow basins)
    public double BaseLevel = SeaLevel - 8;

    // 3D Noise blocks per Timberborn tile; 0 means "fit base level .. world top into the map height".
    // 2 keeps the 3D Noise hills steep enough for Timberborn's ~23 tiles, only cutting off the rare peaks above 102
    public double VerticalScale = 2;

    // 3D Noise blocks per Timberborn tile across the map; 0 means "same as the vertical scale"
    public double HorizontalScale;

    // coordinate scale of the 3D limit noises (684.412)
    public double CoordinateScale = 684.412;

    // coordinate scale of the 3D limit noises in height (684.412)
    public double HeightScale = 684.412;

    public double ResolveVerticalScale(int mapHeight)
    {
        return VerticalScale > 0 ? VerticalScale : (WorldHeight - BaseLevel) / mapHeight;
    }

    public double ResolveHorizontalScale(int mapHeight)
    {
        return HorizontalScale > 0 ? HorizontalScale : ResolveVerticalScale(mapHeight);
    }

    public Noise3DSettings Clone()
    {
        return (Noise3DSettings)MemberwiseClone();
    }
}

// The 3D Noise generator's terrain shape (based on Minecraft beta 1.7.3's ChunkProviderGenerate), without caves, ores,
// surface blocks or decorations. 3D Noise computes a density on a coarse grid (every 4 blocks across, 8 blocks up),
// interpolates it trilinearly, and makes every block with a positive density solid.
// Timberborn tiles are mapped onto a vertical slice of the 3D Noise world, see Noise3DSettings.
public sealed class Noise3DTerrainGenerator
{
    private const int CellWidth = 4;
    private const int CellHeight = 8;
    private const int GridHeight = Noise3DSettings.WorldHeight / CellHeight + 1;

    private readonly double baseLevel;
    private readonly double horizontalScale;
    private readonly int gridX;
    private readonly int gridZ;
    private readonly Noise3DSettings settings;
    private readonly int sizeX;
    private readonly int sizeY;
    private readonly int sizeZ;
    private readonly double verticalScale;

    private double[]? density;
    private double[]? humidity;
    private double[]? temperature;

    // sizes are the Timberborn map size: x and y across, z up
    public Noise3DTerrainGenerator(Noise3DSettings settings, int sizeX, int sizeY, int sizeZ)
    {
        this.settings = settings;
        this.sizeX = sizeX;
        this.sizeY = sizeY;
        this.sizeZ = sizeZ;
        baseLevel = settings.BaseLevel;
        verticalScale = settings.ResolveVerticalScale(sizeZ);
        horizontalScale = settings.ResolveHorizontalScale(sizeZ);
        // grid points needed to cover the map; Timberborn y runs along 3D Noise z
        gridX = (int)Math.Ceiling(sizeX * horizontalScale / CellWidth) + 2;
        gridZ = (int)Math.Ceiling(sizeY * horizontalScale / CellWidth) + 2;
    }

    // biome temperature and humidity at every grid column (the original's WorldChunkManager)
    public void ComputeClimate()
    {
        if (settings.Climate == Noise3DClimate.Rainforest)
        {
            // rainforest needs temperature >= 0.97 and humidity * temperature >= 0.9; use its hottest, wettest
            temperature = Enumerable.Repeat(1.0, gridX * gridZ).ToArray();
            humidity = Enumerable.Repeat(1.0, gridX * gridZ).ToArray();
            return;
        }

        long seed = settings.Seed;
        var temperatureNoise = new Noise3DOctaveSimplexNoise(new Random(SeedFrom(seed * 9871)), 4);
        var humidityNoise = new Noise3DOctaveSimplexNoise(new Random(SeedFrom(seed * 39811)), 4);
        var detailNoise = new Noise3DOctaveSimplexNoise(new Random(SeedFrom(seed * 543321)), 2);
        // 3D Noise samples climate per block; grid columns are CellWidth blocks apart, so the scale is stretched
        temperature = temperatureNoise.Generate(0, 0, gridX, gridZ, 0.025 * CellWidth, 0.025 * CellWidth, 0.25);
        humidity = humidityNoise.Generate(0, 0, gridX, gridZ, 0.05 * CellWidth, 0.05 * CellWidth, 1 / 3.0);
        var detail = detailNoise.Generate(0, 0, gridX, gridZ, 0.25 * CellWidth, 0.25 * CellWidth, 1 / 1.7);
        for (var i = 0; i < temperature.Length; i++)
        {
            var detailValue = detail[i] * 1.1 + 0.5;
            var t = (temperature[i] * 0.15 + 0.7) * 0.99 + detailValue * 0.01;
            var h = (humidity[i] * 0.15 + 0.5) * 0.998 + detailValue * 0.002;
            t = 1 - (1 - t) * (1 - t);
            temperature[i] = Clamp01(t);
            humidity[i] = Clamp01(h);
        }
    }

    // 3D Noise density on the coarse grid, indexed (x * gridZ + z) * GridHeight + y; progress is 0..1
    public void ComputeDensity(Action<float>? progress = null)
    {
        if (temperature == null || humidity == null)
            throw new InvalidOperationException("ComputeClimate must run first");

        var random = new Random(settings.Seed);
        var minLimitNoise = new Noise3DOctaveNoise(random, 16);
        var maxLimitNoise = new Noise3DOctaveNoise(random, 16);
        var mainNoise = new Noise3DOctaveNoise(random, 8);
        // the original creates two more noises here (sand/gravel and stone depth); they are only used for surface blocks,
        // but still consume random numbers
        _ = new Noise3DOctaveNoise(random, 4);
        _ = new Noise3DOctaveNoise(random, 4);
        var scaleNoise = new Noise3DOctaveNoise(random, 10);
        var depthNoise = new Noise3DOctaveNoise(random, 16);

        var horizontal = settings.CoordinateScale;
        var vertical = settings.HeightScale;
        var scale = scaleNoise.Generate2D(0, 0, gridX, gridZ, 1.121, 1.121);
        var depth = depthNoise.Generate2D(0, 0, gridX, gridZ, 200, 200);
        progress?.Invoke(0.05f);
        var main = mainNoise.Generate(0, 0, 0, gridX, GridHeight, gridZ, horizontal / 80, vertical / 160,
            horizontal / 80, fraction => progress?.Invoke(0.05f + 0.15f * fraction));
        var minLimit = minLimitNoise.Generate(0, 0, 0, gridX, GridHeight, gridZ, horizontal, vertical, horizontal,
            fraction => progress?.Invoke(0.2f + 0.4f * fraction));
        var maxLimit = maxLimitNoise.Generate(0, 0, 0, gridX, GridHeight, gridZ, horizontal, vertical, horizontal,
            fraction => progress?.Invoke(0.6f + 0.4f * fraction));

        density = new double[gridX * gridZ * GridHeight];
        var index = 0;
        var column = 0;
        for (var x = 0; x < gridX; x++)
        for (var z = 0; z < gridZ; z++)
        {
            var dryness = 1 - humidity[column] * temperature[column];
            dryness *= dryness;
            dryness *= dryness;
            var climate = 1 - dryness;

            var columnScale = Math.Min(1, (scale[column] + 256) / 512 * climate);
            var columnDepth = depth[column] / 8000;
            if (columnDepth < 0)
                columnDepth = -columnDepth * 0.3;
            columnDepth = columnDepth * 3 - 2;
            if (columnDepth < 0)
            {
                columnDepth /= 2;
                if (columnDepth < -1)
                    columnDepth = -1;
                columnDepth /= 1.4;
                columnDepth /= 2;
                columnScale = 0;
            }
            else
            {
                if (columnDepth > 1)
                    columnDepth = 1;
                columnDepth /= 8;
            }

            if (columnScale < 0)
                columnScale = 0;
            columnScale += 0.5;
            columnDepth = columnDepth * GridHeight / 16;
            var groundLevel = GridHeight / 2.0 + columnDepth * 4;
            column++;

            for (var y = 0; y < GridHeight; y++)
            {
                var falloff = (y - groundLevel) * 12 / columnScale;
                if (falloff < 0)
                    falloff *= 4;
                var low = minLimit[index] / 512;
                var high = maxLimit[index] / 512;
                var selector = (main[index] / 10 + 1) / 2;
                var value = selector < 0 ? low : selector > 1 ? high : low + (high - low) * selector;
                value -= falloff;
                // fade to air at the top of the world
                if (y > GridHeight - 4)
                {
                    var fade = (y - (GridHeight - 4)) / 3.0;
                    value = value * (1 - fade) - 10 * fade;
                }

                density[index++] = value;
            }
        }
    }

    // samples the interpolated density at every Timberborn tile; the bottom layer is always solid
    public VoxelMap BuildVoxels(Action<float>? progress = null)
    {
        if (density == null)
            throw new InvalidOperationException("ComputeDensity must run first");

        var voxels = new VoxelMap(sizeX, sizeY, sizeZ, new bool[sizeX * sizeY * sizeZ]);
        for (var y = 0; y < sizeY; y++)
        {
            var cellZ = (y + 0.5) * horizontalScale / CellWidth;
            for (var x = 0; x < sizeX; x++)
            {
                var cellX = (x + 0.5) * horizontalScale / CellWidth;
                voxels[x, y, 0] = true;
                for (var z = 1; z < sizeZ; z++)
                {
                    var cellY = (baseLevel + (z + 0.5) * verticalScale) / CellHeight;
                    voxels[x, y, z] = SampleDensity(cellX, cellY, cellZ) > 0;
                }
            }

            progress?.Invoke((y + 1f) / sizeY);
        }

        return voxels;
    }

    private double SampleDensity(double x, double y, double z)
    {
        y = Math.Max(0, Math.Min(GridHeight - 1.000001, y));
        var x0 = (int)x;
        var y0 = (int)y;
        var z0 = (int)z;
        var fx = x - x0;
        var fy = y - y0;
        var fz = z - z0;
        var c00 = Lerp(fx, Density(x0, y0, z0), Density(x0 + 1, y0, z0));
        var c01 = Lerp(fx, Density(x0, y0, z0 + 1), Density(x0 + 1, y0, z0 + 1));
        var c10 = Lerp(fx, Density(x0, y0 + 1, z0), Density(x0 + 1, y0 + 1, z0));
        var c11 = Lerp(fx, Density(x0, y0 + 1, z0 + 1), Density(x0 + 1, y0 + 1, z0 + 1));
        return Lerp(fy, Lerp(fz, c00, c01), Lerp(fz, c10, c11));
    }

    private double Density(int x, int y, int z)
    {
        return density![(x * gridZ + z) * GridHeight + y];
    }

    private static double Lerp(double t, double a, double b)
    {
        return a + t * (b - a);
    }

    private static double Clamp01(double value)
    {
        return value < 0 ? 0 : value > 1 ? 1 : value;
    }

    // System.Random takes an int seed; fold the 64-bit seed into one
    private static int SeedFrom(long seed)
    {
        return unchecked((int)(seed ^ (seed >> 32)));
    }
}

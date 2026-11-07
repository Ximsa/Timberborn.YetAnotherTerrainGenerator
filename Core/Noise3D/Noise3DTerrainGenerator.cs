using Timberborn.TerrainGenerator.Core.Common;

namespace Timberborn.TerrainGenerator.Core.Noise3D;

public sealed class Noise3DSettings
{
    public const int WorldHeight = 128;
    public const int SeaLevel = 64;

    public long Seed;

    // 0 is nearly flat, 1 the hilliest terrain
    public double Hilliness = 1;

    // 0 gives rounded hills and peaks, 1 rugged ones; 0.5 keeps the noise as it is
    public double Ruggedness = 0.5;

    // stretch the generated terrain vertically so that its highest completely solid layer becomes the map's bottom
    // and the lowest completely empty layer above it the map's top; overrides base level and vertical scale
    public bool Normalize;

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

// The 3D Noise generator's terrain shape. It computes a density on a coarse grid (every 4 blocks across, 8 blocks
// up) from octave noises, interpolates it trilinearly, and makes every block with a positive density solid.
// Timberborn tiles are mapped onto a vertical slice of the 3D Noise world, see Noise3DSettings.
public sealed class Noise3DTerrainGenerator
{
    private const int CellWidth = 4;
    private const int CellHeight = 8;
    private const int GridHeight = Noise3DSettings.WorldHeight / CellHeight + 1;

    private double baseLevel;
    private readonly double horizontalScale;
    private readonly int gridX;
    private readonly int gridZ;
    private readonly Noise3DSettings settings;
    private readonly int sizeX;
    private readonly int sizeY;
    private readonly int sizeZ;
    private double verticalScale;

    private double[]? density;

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

    // 3D Noise density on the coarse grid, indexed (x * gridZ + z) * GridHeight + y; progress is 0..1
    public void ComputeDensity(Action<float>? progress = null)
    {
        var hilliness = Clamp01(settings.Hilliness);
        // fine noise octaves: 0 at ruggedness 0 (rounded), unchanged at 0.5, doubled at 1 (rugged)
        var detailWeight = 2 * Clamp01(settings.Ruggedness);

        var random = new Random(SeedFrom(settings.Seed));
        var minLimitNoise = new Noise3DOctaveNoise(random, 16);
        var maxLimitNoise = new Noise3DOctaveNoise(random, 16);
        var mainNoise = new Noise3DOctaveNoise(random, 8);
        var scaleNoise = new Noise3DOctaveNoise(random, 10);
        var depthNoise = new Noise3DOctaveNoise(random, 16);

        var horizontal = settings.CoordinateScale;
        var vertical = settings.HeightScale;
        var scale = scaleNoise.Generate2D(0, 0, gridX, gridZ, 1.121, 1.121);
        var depth = depthNoise.Generate2D(0, 0, gridX, gridZ, 200, 200);
        progress?.Invoke(0.05f);
        var main = mainNoise.Generate(0, 0, 0, gridX, GridHeight, gridZ, horizontal / 80, vertical / 160,
            horizontal / 80, fraction => progress?.Invoke(0.05f + 0.15f * fraction), detailWeight);
        var minLimit = minLimitNoise.Generate(0, 0, 0, gridX, GridHeight, gridZ, horizontal, vertical, horizontal,
            fraction => progress?.Invoke(0.2f + 0.4f * fraction), detailWeight);
        var maxLimit = maxLimitNoise.Generate(0, 0, 0, gridX, GridHeight, gridZ, horizontal, vertical, horizontal,
            fraction => progress?.Invoke(0.6f + 0.4f * fraction), detailWeight);

        density = new double[gridX * gridZ * GridHeight];
        var index = 0;
        var column = 0;
        for (var x = 0; x < gridX; x++)
        for (var z = 0; z < gridZ; z++)
        {
            var columnScale = Math.Min(1, (scale[column] + 256) / 512 * hilliness);
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
            // low hilliness also steepens the falloff around the ground level, so the terrain gets close to flat
            columnScale *= 0.25 + 0.75 * hilliness;
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

        if (settings.Normalize)
            FitTerrainToMapHeight();

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

    // chooses the vertical slice of the world so the terrain fills the map: the highest world layer that is solid in
    // every column lands just above the map's bottom layer, and the lowest layer above it that is empty in every column
    // lands just above the map's top. Measured in whole world blocks over the full world height, so peaks and islands
    // the default slice would cut off are included.
    private void FitTerrainToMapHeight()
    {
        var lowestAir = double.MaxValue;
        var highestSolid = double.MinValue;
        for (var y = 0; y < sizeY; y++)
        {
            var cellZ = (y + 0.5) * horizontalScale / CellWidth;
            for (var x = 0; x < sizeX; x++)
            {
                var cellX = (x + 0.5) * horizontalScale / CellWidth;
                // first empty block from the bottom: everything below is solid in this column
                var air = 0;
                while (air < Noise3DSettings.WorldHeight && Solid(air))
                    air++;
                lowestAir = Math.Min(lowestAir, air);
                // highest solid block of the column
                var top = Noise3DSettings.WorldHeight - 1;
                while (top >= 0 && !Solid(top))
                    top--;
                highestSolid = Math.Max(highestSolid, top + 1);

                bool Solid(int worldY)
                {
                    return SampleDensity(cellX, (worldY + 0.5) / CellHeight, cellZ) > 0;
                }
            }
        }

        if (highestSolid <= lowestAir)
            return;
        // tile z covers world heights baseLevel + z * verticalScale .. + verticalScale: lowestAir is the top of tile 0,
        // highestSolid the top of the highest tile
        verticalScale = (highestSolid - lowestAir) / (sizeZ - 1);
        baseLevel = lowestAir - verticalScale;
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

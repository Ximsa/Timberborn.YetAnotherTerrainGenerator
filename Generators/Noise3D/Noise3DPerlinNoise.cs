namespace Timberborn.TerrainGenerator.Generators.Noise3D;

// Improved Perlin noise for the 3D Noise generator, seeded from System.Random.
// Values are added into a flat array indexed (x * sizeZ + z) * sizeY + y.
internal sealed class Noise3DPerlinNoise
{
    private readonly int[] permutations = new int[512];
    private readonly double xOffset;
    private readonly double yOffset;
    private readonly double zOffset;

    public Noise3DPerlinNoise(Random random)
    {
        xOffset = random.NextDouble() * 256;
        yOffset = random.NextDouble() * 256;
        zOffset = random.NextDouble() * 256;
        for (var i = 0; i < 256; i++)
            permutations[i] = i;
        for (var i = 0; i < 256; i++)
        {
            var j = random.Next(256 - i) + i;
            (permutations[i], permutations[j]) = (permutations[j], permutations[i]);
            permutations[i + 256] = permutations[i];
        }
    }

    // adds the noise divided by octaveScale; a size of 1 in y selects the cheaper 2D variant
    public void Add(double[] values, double x, double y, double z, int sizeX, int sizeY, int sizeZ,
        double scaleX, double scaleY, double scaleZ, double octaveScale)
    {
        var amplitude = 1.0 / octaveScale;
        if (sizeY == 1)
        {
            Add2D(values, x, z, sizeX, sizeZ, scaleX, scaleZ, amplitude);
            return;
        }

        var index = 0;
        var lastY = -1;
        double x0Y0 = 0, x0Y1 = 0, x1Y0 = 0, x1Y1 = 0;
        for (var ix = 0; ix < sizeX; ix++)
        {
            var px = (x + ix) * scaleX + xOffset;
            var cellX = Floor(px);
            var hashX = cellX & 255;
            px -= cellX;
            var fadeX = Fade(px);
            for (var iz = 0; iz < sizeZ; iz++)
            {
                var pz = (z + iz) * scaleZ + zOffset;
                var cellZ = Floor(pz);
                var hashZ = cellZ & 255;
                pz -= cellZ;
                var fadeZ = Fade(pz);
                for (var iy = 0; iy < sizeY; iy++)
                {
                    var py = (y + iy) * scaleY + yOffset;
                    var cellY = Floor(py);
                    var hashY = cellY & 255;
                    py -= cellY;
                    var fadeY = Fade(py);
                    // 3D Noise only recomputes the corner gradients when the y cell changes (or a column starts)
                    if (iy == 0 || hashY != lastY)
                    {
                        lastY = hashY;
                        var a = permutations[hashX] + hashY;
                        var aa = permutations[a] + hashZ;
                        var ab = permutations[a + 1] + hashZ;
                        var b = permutations[hashX + 1] + hashY;
                        var ba = permutations[b] + hashZ;
                        var bb = permutations[b + 1] + hashZ;
                        x0Y0 = Lerp(fadeX, Grad(permutations[aa], px, py, pz),
                            Grad(permutations[ba], px - 1, py, pz));
                        x0Y1 = Lerp(fadeX, Grad(permutations[ab], px, py - 1, pz),
                            Grad(permutations[bb], px - 1, py - 1, pz));
                        x1Y0 = Lerp(fadeX, Grad(permutations[aa + 1], px, py, pz - 1),
                            Grad(permutations[ba + 1], px - 1, py, pz - 1));
                        x1Y1 = Lerp(fadeX, Grad(permutations[ab + 1], px, py - 1, pz - 1),
                            Grad(permutations[bb + 1], px - 1, py - 1, pz - 1));
                    }

                    var near = Lerp(fadeY, x0Y0, x0Y1);
                    var far = Lerp(fadeY, x1Y0, x1Y1);
                    values[index++] += Lerp(fadeZ, near, far) * amplitude;
                }
            }
        }
    }

    private void Add2D(double[] values, double x, double z, int sizeX, int sizeZ, double scaleX, double scaleZ,
        double amplitude)
    {
        var index = 0;
        for (var ix = 0; ix < sizeX; ix++)
        {
            var px = (x + ix) * scaleX + xOffset;
            var cellX = Floor(px);
            var hashX = cellX & 255;
            px -= cellX;
            var fadeX = Fade(px);
            for (var iz = 0; iz < sizeZ; iz++)
            {
                var pz = (z + iz) * scaleZ + zOffset;
                var cellZ = Floor(pz);
                var hashZ = cellZ & 255;
                pz -= cellZ;
                var fadeZ = Fade(pz);
                var a = permutations[hashX];
                var aa = permutations[a] + hashZ;
                var b = permutations[hashX + 1];
                var ba = permutations[b] + hashZ;
                var near = Lerp(fadeX, Grad2D(permutations[aa], px, pz), Grad(permutations[ba], px - 1, 0, pz));
                var far = Lerp(fadeX, Grad(permutations[aa + 1], px, 0, pz - 1),
                    Grad(permutations[ba + 1], px - 1, 0, pz - 1));
                values[index++] += Lerp(fadeZ, near, far) * amplitude;
            }
        }
    }

    private static int Floor(double value)
    {
        var floor = (int)value;
        return value < floor ? floor - 1 : floor;
    }

    private static double Fade(double t)
    {
        return t * t * t * (t * (t * 6 - 15) + 10);
    }

    private static double Lerp(double t, double a, double b)
    {
        return a + t * (b - a);
    }

    private static double Grad(int hash, double x, double y, double z)
    {
        var h = hash & 15;
        var u = h < 8 ? x : y;
        var v = h < 4 ? y : h == 12 || h == 14 ? x : z;
        return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
    }

    private static double Grad2D(int hash, double x, double z)
    {
        var h = hash & 15;
        var u = (1 - ((h & 8) >> 3)) * x;
        var v = h < 4 ? 0 : h == 12 || h == 14 ? x : z;
        return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
    }
}

namespace Timberborn.TerrainGenerator.Generators.Noise3D;

// Port of Minecraft beta 1.7.3's 2D simplex noise (NoiseGenerator2) and its octave stack (NoiseGeneratorOctaves2),
// which beta uses for the biome climate (temperature and humidity).
internal sealed class BetaSimplexNoise
{
    private static readonly int[,] Gradients =
    {
        { 1, 1 }, { -1, 1 }, { 1, -1 }, { -1, -1 }, { 1, 0 }, { -1, 0 }, { 1, 0 }, { -1, 0 },
        { 0, 1 }, { 0, -1 }, { 0, 1 }, { 0, -1 }
    };

    private static readonly double Skew = 0.5 * (Math.Sqrt(3) - 1);
    private static readonly double Unskew = (3 - Math.Sqrt(3)) / 6;

    private readonly int[] permutations = new int[512];
    private readonly double xOffset;
    private readonly double yOffset;

    public BetaSimplexNoise(Random random)
    {
        xOffset = random.NextDouble() * 256;
        yOffset = random.NextDouble() * 256;
        // beta draws a third offset it never uses; kept so the random sequence matches the original's structure
        random.NextDouble();
        for (var i = 0; i < 256; i++)
            permutations[i] = i;
        for (var i = 0; i < 256; i++)
        {
            var j = random.Next(256 - i) + i;
            (permutations[i], permutations[j]) = (permutations[j], permutations[i]);
            permutations[i + 256] = permutations[i];
        }
    }

    // adds the noise times amplitude into values indexed x * sizeZ + z
    public void Add(double[] values, double x, double z, int sizeX, int sizeZ, double scaleX, double scaleZ,
        double amplitude)
    {
        var index = 0;
        for (var ix = 0; ix < sizeX; ix++)
        {
            var px = (x + ix) * scaleX + xOffset;
            for (var iz = 0; iz < sizeZ; iz++)
            {
                var pz = (z + iz) * scaleZ + yOffset;
                var skew = (px + pz) * Skew;
                var cellX = Floor(px + skew);
                var cellZ = Floor(pz + skew);
                var unskew = (cellX + cellZ) * Unskew;
                var x0 = px - (cellX - unskew);
                var z0 = pz - (cellZ - unskew);
                int stepX, stepZ;
                if (x0 > z0)
                {
                    stepX = 1;
                    stepZ = 0;
                }
                else
                {
                    stepX = 0;
                    stepZ = 1;
                }

                var x1 = x0 - stepX + Unskew;
                var z1 = z0 - stepZ + Unskew;
                var x2 = x0 - 1 + 2 * Unskew;
                var z2 = z0 - 1 + 2 * Unskew;
                var hashX = cellX & 255;
                var hashZ = cellZ & 255;
                var gradient0 = permutations[hashX + permutations[hashZ]] % 12;
                var gradient1 = permutations[hashX + stepX + permutations[hashZ + stepZ]] % 12;
                var gradient2 = permutations[hashX + 1 + permutations[hashZ + 1]] % 12;
                values[index++] += 70 * (Corner(gradient0, x0, z0) + Corner(gradient1, x1, z1) +
                                         Corner(gradient2, x2, z2)) * amplitude;
            }
        }
    }

    private static double Corner(int gradient, double x, double z)
    {
        var t = 0.5 - x * x - z * z;
        if (t < 0)
            return 0;
        t *= t;
        return t * t * (Gradients[gradient, 0] * x + Gradients[gradient, 1] * z);
    }

    private static int Floor(double value)
    {
        return value > 0 ? (int)value : (int)value - 1;
    }
}

internal sealed class BetaOctaveSimplexNoise
{
    private readonly BetaSimplexNoise[] octaves;

    public BetaOctaveSimplexNoise(Random random, int octaveCount)
    {
        octaves = new BetaSimplexNoise[octaveCount];
        for (var i = 0; i < octaveCount; i++)
            octaves[i] = new BetaSimplexNoise(random);
    }

    // like beta: each octave multiplies the frequency by frequencyFactor and doubles the amplitude
    public double[] Generate(double x, double z, int sizeX, int sizeZ, double scaleX, double scaleZ,
        double frequencyFactor)
    {
        scaleX /= 1.5;
        scaleZ /= 1.5;
        var values = new double[sizeX * sizeZ];
        var amplitudeDivisor = 1.0;
        var frequency = 1.0;
        foreach (var octave in octaves)
        {
            octave.Add(values, x, z, sizeX, sizeZ, scaleX * frequency, scaleZ * frequency, 0.55 / amplitudeDivisor);
            frequency *= frequencyFactor;
            amplitudeDivisor *= 0.5;
        }

        return values;
    }
}

namespace Timberborn.TerrainGenerator.Generators.Noise3D;

// Port of Minecraft beta 1.7.3's NoiseGeneratorOctaves: each octave doubles the frequency and halves the amplitude.
internal sealed class BetaOctaveNoise
{
    private readonly BetaPerlinNoise[] octaves;

    public BetaOctaveNoise(Random random, int octaveCount)
    {
        octaves = new BetaPerlinNoise[octaveCount];
        for (var i = 0; i < octaveCount; i++)
            octaves[i] = new BetaPerlinNoise(random);
    }

    // indexed (x * sizeZ + z) * sizeY + y; progress is reported once per octave
    public double[] Generate(double x, double y, double z, int sizeX, int sizeY, int sizeZ,
        double scaleX, double scaleY, double scaleZ, Action<float>? progress = null)
    {
        var values = new double[sizeX * sizeY * sizeZ];
        var octaveScale = 1.0;
        for (var i = 0; i < octaves.Length; i++)
        {
            octaves[i].Add(values, x, y, z, sizeX, sizeY, sizeZ,
                scaleX * octaveScale, scaleY * octaveScale, scaleZ * octaveScale, octaveScale);
            octaveScale /= 2;
            progress?.Invoke((i + 1f) / octaves.Length);
        }

        return values;
    }

    // beta's 2D sampling: a single layer at y = 10, indexed x * sizeZ + z
    public double[] Generate2D(double x, double z, int sizeX, int sizeZ, double scaleX, double scaleZ,
        Action<float>? progress = null)
    {
        return Generate(x, 10, z, sizeX, 1, sizeZ, scaleX, 1, scaleZ, progress);
    }
}

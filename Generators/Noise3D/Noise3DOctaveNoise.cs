namespace Timberborn.TerrainGenerator.Generators.Noise3D;

// Octave noise for the 3D Noise generator: each octave doubles the frequency and halves the amplitude.
internal sealed class Noise3DOctaveNoise
{
    private readonly Noise3DPerlinNoise[] octaves;

    public Noise3DOctaveNoise(Random random, int octaveCount)
    {
        octaves = new Noise3DPerlinNoise[octaveCount];
        for (var i = 0; i < octaveCount; i++)
            octaves[i] = new Noise3DPerlinNoise(random);
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

    // the 3D Noise generator's 2D sampling: a single layer at y = 10, indexed x * sizeZ + z
    public double[] Generate2D(double x, double z, int sizeX, int sizeZ, double scaleX, double scaleZ,
        Action<float>? progress = null)
    {
        return Generate(x, 10, z, sizeX, 1, sizeZ, scaleX, 1, scaleZ, progress);
    }
}

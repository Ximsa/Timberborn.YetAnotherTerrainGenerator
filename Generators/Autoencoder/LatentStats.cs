using System.Globalization;

namespace Timberborn.TerrainGenerator.Generators.Autoencoder;

// Mean and standard deviation of each latent number over the built-in maps (Models/LatentStats.txt, written by
// tools/AutoencoderTuner). The generator samples new maps from this spread, so every latent number varies about as
// much as it does between real maps.
public sealed class LatentStats
{
    public LatentStats(float[] mean, float[] std)
    {
        if (mean.Length != std.Length)
            throw new ArgumentException($"{mean.Length} means but {std.Length} standard deviations");
        Mean = mean;
        Std = std;
    }

    public float[] Mean { get; }
    public float[] Std { get; }

    public static LatentStats From(IReadOnlyList<float[]> latents)
    {
        var size = latents[0].Length;
        var mean = new float[size];
        var std = new float[size];
        for (var d = 0; d < size; d++)
        {
            mean[d] = latents.Average(latent => latent[d]);
            std[d] = MathF.Sqrt(latents.Average(latent => (latent[d] - mean[d]) * (latent[d] - mean[d])));
        }

        return new LatentStats(mean, std);
    }

    // two lines: "mean" and "std", each followed by one number per latent
    public static LatentStats Load(string path)
    {
        var lines = File.ReadAllLines(path).Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToDictionary(parts => parts[0], parts => parts.Skip(1)
                .Select(value => float.Parse(value, CultureInfo.InvariantCulture)).ToArray());
        return new LatentStats(lines["mean"], lines["std"]);
    }

    public void Save(string path)
    {
        File.WriteAllLines(path, new[] { Line("mean", Mean), Line("std", Std) });

        static string Line(string name, float[] values)
        {
            return name + " " + string.Join(" ", values.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
        }
    }

    // keep blends in the current map's latent (0 = ignore it, 1 = keep it); variety is the noise in standard
    // deviations of the built-in maps
    public float[] Sample(float[] current, float keep, float variety, Random random)
    {
        var result = new float[Mean.Length];
        for (var d = 0; d < result.Length; d++)
            result[d] = Mean[d] + keep * (current[d] - Mean[d]) + variety * Std[d] * (float)Gaussian(random);
        return result;
    }

    private static double Gaussian(Random random)
    {
        return Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
    }
}

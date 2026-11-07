namespace Timberborn.TerrainGenerator.Generators.Autoencoder;

// Separable Gaussian blur with clamped edges. The autoencoder generator runs it on its enlarged output to round off
// the square blocks that enlarging by nearest neighbour leaves.
public static class GaussianBlur
{
    // standard deviation in tiles used by the generator
    public const double DefaultSigma = 1.5;

    // values indexed y * width + x
    public static float[] Apply(float[] values, int width, int height, double sigma)
    {
        if (sigma <= 0) return values;
        var radius = (int)Math.Ceiling(sigma * 3);
        var kernel = new double[2 * radius + 1];
        for (var d = -radius; d <= radius; d++)
            kernel[d + radius] = Math.Exp(-d * d / (2 * sigma * sigma));
        var total = kernel.Sum();
        for (var i = 0; i < kernel.Length; i++)
            kernel[i] /= total;

        var horizontal = new float[values.Length];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            double sum = 0;
            for (var d = -radius; d <= radius; d++)
                sum += kernel[d + radius] * values[y * width + Math.Max(0, Math.Min(width - 1, x + d))];
            horizontal[y * width + x] = (float)sum;
        }

        var result = new float[values.Length];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            double sum = 0;
            for (var d = -radius; d <= radius; d++)
                sum += kernel[d + radius] * horizontal[Math.Max(0, Math.Min(height - 1, y + d)) * width + x];
            result[y * width + x] = (float)sum;
        }

        return result;
    }
}

using Timberborn.TerrainGenerator.Generators.Autoencoder;

namespace AutoencoderTuner;

// The autoencoder generator's pipeline outside the game (TerrainGeneratorDialog.Pipeline2D), on plain arrays.
public static class Pipeline
{
    public const int InputSize = 128;

    // heights scaled to side x side by nearest neighbour (like NumSharpExtensions.Scale2D) and divided by the maximum
    public static float[] Normalize(int[] heights, int sizeX, int sizeY, int side)
    {
        var result = new float[side * side];
        for (var i = 0; i < side; i++)
        for (var j = 0; j < side; j++)
            result[i * side + j] = heights[(int)(i * (float)sizeY / side) * sizeX + (int)(j * (float)sizeX / side)];
        var max = Math.Max(1e-6f, result.Max());
        return result.Select(h => h / max).ToArray();
    }

    // decoded 128 x 128 output -> scaled to the map, zoomed, randomly cropped, Gaussian blurred (like the generator),
    // optionally median filtered (the generator used to), normalised to 0..1
    public static float[] Finish(float[] decoded, int side, float zoom, int medianRadius, int medianPasses, double sigma,
        Random random)
    {
        var map = Scale(decoded, InputSize, side);
        var zoomed = (int)(side * zoom);
        map = Scale(map, side, zoomed);
        int offsetY = random.Next(zoomed - side + 1), offsetX = random.Next(zoomed - side + 1);
        var cropped = new float[side * side];
        for (var y = 0; y < side; y++)
        for (var x = 0; x < side; x++)
            cropped[y * side + x] = map[(y + offsetY) * zoomed + x + offsetX];
        cropped = GaussianBlur.Apply(cropped, side, side, sigma);
        for (var pass = 0; pass < medianPasses; pass++)
            cropped = Median(cropped, side, medianRadius);
        float min = cropped.Min(), max = cropped.Max();
        return cropped.Select(h => (h - min) / Math.Max(1e-6f, max - min)).ToArray();
    }

    private static float[] Scale(float[] map, int from, int to)
    {
        var result = new float[to * to];
        for (var i = 0; i < to; i++)
        for (var j = 0; j < to; j++)
            result[i * to + j] = map[(int)(i * (float)from / to) * from + (int)(j * (float)from / to)];
        return result;
    }

    // median over a square window that shrinks at the edges, like NumSharpExtensions.MedianBlur
    private static float[] Median(float[] map, int side, int radius)
    {
        var result = new float[map.Length];
        Parallel.For(0, side, y =>
        {
            var window = new float[(2 * radius + 1) * (2 * radius + 1)];
            for (var x = 0; x < side; x++)
            {
                var n = 0;
                for (var dy = -radius; dy <= radius; dy++)
                for (var dx = -radius; dx <= radius; dx++)
                {
                    int ny = y + dy, nx = x + dx;
                    if (ny >= 0 && nx >= 0 && ny < side && nx < side)
                        window[n++] = map[ny * side + nx];
                }
                Array.Sort(window, 0, n);
                result[y * side + x] = window[n / 2];
            }
        });
        return result;
    }
}

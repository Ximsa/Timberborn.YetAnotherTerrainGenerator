using NumSharp;

namespace Timberborn.TerrainGenerator.Core.Smoothing;

// Median filter on a heightmap: each value becomes the median of the square window around it (smaller at the edges),
// which smooths noise while keeping cliffs sharper than an average would.
public static class MedianBlurExtensions
{
    public static NDArray MedianBlur(this NDArray input, int radius, int passes, Action<float>? progress = null)
    {
        var window = new float[(2 * radius + 1) * (2 * radius + 1)];
        for (var pass = 0; pass < passes; pass++)
        {
            input = input.astype(np.float32);
            var original = input.Clone();
            for (var i = 0; i < input.Shape[0]; i++)
            {
                MedianBlurRow(input, original, window, radius, i);
                progress?.Invoke((pass * input.Shape[0] + i + 1f) / (passes * input.Shape[0]));
            }
        }

        return input;
    }

    private static void MedianBlurRow(NDArray input, NDArray original, float[] window, int radius, int i)
    {
        for (var j = 0; j < input.Shape[1]; j++)
        {
            // select iteration window for kernel
            var windowIndex = 0;
            var iMin = Math.Max(i - radius, 0);
            var iMax = Math.Min(i + radius, input.Shape[0]);
            var jMin = Math.Max(j - radius, 0);
            var jMax = Math.Min(j + radius, input.Shape[1]);
            for (var di = iMin; di < iMax; di++)
            for (var dj = jMin; dj < jMax; dj++)
            {
                // build up (insertion-)sorted list for median computation
                var value = (float)original[di, dj];
                var insertIndex = windowIndex;
                while (insertIndex > 0 && value < window[insertIndex - 1])
                {
                    window[insertIndex] = window[insertIndex - 1];
                    insertIndex--;
                }

                window[insertIndex] = value;
                windowIndex++;
            }

            var median = window[windowIndex / 2];
            input[i, j] = median;
        }
    }
}

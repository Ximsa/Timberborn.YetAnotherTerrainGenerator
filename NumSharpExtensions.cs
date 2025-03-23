using NumSharp;

namespace Timberborn.TerrainGenerator;

public static class NumSharpExtensions
{
    public static NDArray Scale2D(this NDArray input, Shape target)
    {
        var result = np.zeros(target, np.float32);
        var resize0 = (float)input.Shape[0] / target[0];
        var resize1 = (float)input.Shape[1] / target[1];
        for (var i = 0; i < target[0]; i++)
        for (var j = 0; j < target[1]; j++)
            result[i, j] = input[(int)(i * resize0), (int)(j * resize1)];

        return result;
    }

    public static NDArray MedianBlur(this NDArray input, int radius, int passes)
    {
        var window = new float[(2 * radius + 1) * (2 * radius + 1)];
        for (var pass = 0; pass < passes; pass++)
        {
            input = input.astype(np.float32);
            var original = input.Clone();
            for (var i = 0; i < input.Shape[0]; i++)
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

        return input;
    }
}
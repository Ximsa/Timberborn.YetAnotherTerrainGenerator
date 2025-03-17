using NumSharp;

namespace Timberborn.TerrainGenerator;

public static class NumSharpExtensions
{
    public static NDArray Scale2D(this NDArray input, Shape target)
    {
        var result = np.zeros(target, dtype: np.float32);
        var resize0 = (float)input.Shape[0] / target[0];
        var resize1 = (float)input.Shape[1] / target[1];
        for (var i = 0; i < target[0]; i++)
        for (var j = 0; j < target[1]; j++)
            result[i, j] = input[(int)(i * resize0), (int)(j * resize1)];

        return result;
    }

    public static NDArray MedianBlur(this NDArray input, int radius, int passes)
    {
        for (var pass = 0; pass < passes; pass++)
        {
            input = input.astype(np.float32);
            var original = input.Clone();
            var window = new float[(2 * radius + 1) * (2 * radius + 1)];
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
                    var value = (float)original[di, dj]; // insert 
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

    public static NDArray GaussKernel1D(int length, float sigma)
    {
        var ax = np.linspace(-(length - 1) / 2.0, (length - 1) / 2.0, length, true, NPTypeCode.Float);
        var gauss = np.exp(-0.5 * np.square(ax) / np.square(sigma), NPTypeCode.Float);
        var sum = gauss.Cast<float>().Sum();
        return (gauss / sum).astype(np.float32);
    }

    public static NDArray Filter2D(this NDArray input, NDArray kernel, int passes = 1)
    {
        var old = input.copy().astype(np.float32);
        for (var dim0 = 0; dim0 < input.Shape[0]; dim0++)
        {
            input[$"{dim0},:"] = Convolve1D(old[$"{dim0},:"], kernel);
        }

        input = input.T;
        old = input.copy();
        for (var dim0 = 0; dim0 < input.Shape[0]; dim0++)
        {
            input[$"{dim0},:"] = Convolve1D(old[$"{dim0},:"], kernel);
        }

        return input.T.astype(np.float32);
    }

    public static NDArray Convolve1D(this NDArray input, NDArray kernel)
    {
        // from https://github.com/SciSharp/NumSharp/pull/7/commits/0c52da50f20e895f76ac6232f1832c59bb939398
        var nf = input.Shape[0];
        var ng = kernel.Shape[0];
        var n = nf + ng - 1;

        var result = new float[n];

        for (var idx = 0; idx < n; ++idx)
        {
            var jmn = (idx >= ng - 1) ? (idx - (ng - 1)) : 0;
            var jmx = (idx < nf - 1) ? idx : nf - 1;

            for (var jdx = jmn; jdx <= jmx; ++jdx)
            {
                result[idx] += (input[jdx] * kernel[idx - jdx]);
            }
        }

        return np.array(result)[$"{kernel.Shape[0] / 2}:{input.Shape[0] + kernel.Shape[0] / 2}"];
    }
}
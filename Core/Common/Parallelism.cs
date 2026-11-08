using System.Runtime.ExceptionServices;

namespace Timberborn.TerrainGenerator.Core.Common;

// Parallel loops for the background computations: all cores but one, so the game keeps one for itself, and the first
// exception is rethrown as it was (not wrapped), so callers catch it like on one thread.
public static class Parallelism
{
    public static int Workers => Math.Max(1, Environment.ProcessorCount - 1);

    public static void For(int count, Action<int> body)
    {
        try
        {
            Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = Workers }, body);
        }
        catch (AggregateException exception)
        {
            ExceptionDispatchInfo.Capture(exception.Flatten().InnerExceptions[0]).Throw();
        }
    }

    // body gets ranges [start, end) that together cover 0 .. count, a few per worker, so it can keep buffers per range
    public static void ForRanges(int count, Action<int, int> body)
    {
        var ranges = Math.Max(1, Math.Min(count, Workers * 4));
        For(ranges, range => body((int)((long)count * range / ranges), (int)((long)count * (range + 1) / ranges)));
    }
}

using System.Diagnostics;
using Debug = UnityEngine.Debug;

namespace Timberborn.TerrainGenerator;

// Thread-safe progress shared between a background job and the UI thread.
// Stages are identified by localization keys, which the UI translates when displaying them.
public class ProgressReporter
{
    private readonly object sync = new();
    private float fraction;
    private string stageKey = string.Empty;

    public (string StageKey, float Fraction) Current
    {
        get
        {
            lock (sync)
            {
                return (stageKey, fraction);
            }
        }
    }

    public void Report(string stageKey, float fraction)
    {
        lock (sync)
        {
            this.stageKey = stageKey;
            this.fraction = fraction;
        }
    }

    // runs one stage of a job, mapping the stage's own 0..1 progress into [start, end] of the whole job
    public T Stage<T>(string stageKey, float start, float end, Func<Action<float>, T> work)
    {
        Report(stageKey, start);
        var stopwatch = Stopwatch.StartNew();
        var result = work(stageFraction => Report(stageKey, start + (end - start) * stageFraction));
        Debug.Log($"[TerrainGenerator] {stageKey} took {stopwatch.ElapsedMilliseconds} ms");
        Report(stageKey, end);
        return result;
    }
}

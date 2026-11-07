namespace Timberborn.TerrainGenerator.Core.Wfc;

// Wave function collapse over a 3D grid of cells. Each cell keeps the set of patterns it may still take as a bitset;
// the next cell (by default the lowest-entropy one, see WfcCellSelection) is collapsed to one weighted-random pattern
// and the choice is propagated to neighbours,
// until every cell is decided. On a contradiction (a cell with no pattern left) the solver backtracks: it undoes the
// last choice, bans that pattern in that cell and propagates again, stepping further back if that fails too.
internal sealed class WfcSolver
{
    private readonly ulong[] candidates;
    private readonly int cells;
    private readonly int[] decidedPattern;
    private readonly Stack<Decision> decisions = new();
    // per cell: a random value in [0, 1) that breaks ties, or orders the cells for RandomOrder
    private readonly double[] order;
    private readonly MinHeap heap = new();
    private readonly int maxBacktracks;
    private readonly WfcCellSelection selection;
    private readonly WfcPatternSet patterns;
    private readonly bool[] queued;
    private readonly Random random;
    private readonly int[] remaining;

    // undo log: every change of remaining counts, with the previous value
    private readonly List<(int Cell, int Old)> remainingTrail = new();

    // per cell: patterns lost since the cell was last propagated
    private readonly ulong[] removed;
    private readonly Stack<int> stack = new();
    private readonly int[] version;
    private readonly ulong[] wave;

    // undo log: every change of a wave word, with the previous value
    private readonly List<(int Index, ulong Old)> waveTrail = new();

    // [layer][pattern]: weight * log(weight), precomputed for the entropy
    private readonly double[][] weightLogsByLayer;
    private readonly int words;
    private int decided;

    public WfcSolver(WfcPatternSet patterns, int sizeX, int sizeY, int sizeZ, Random random, int maxBacktracks,
        WfcCellSelection selection = WfcCellSelection.LowestEntropy)
    {
        this.patterns = patterns;
        this.random = random;
        this.maxBacktracks = maxBacktracks;
        this.selection = selection;
        SizeX = sizeX;
        SizeY = sizeY;
        SizeZ = sizeZ;
        cells = sizeX * sizeY * sizeZ;
        words = patterns.Words;
        wave = new ulong[cells * words];
        removed = new ulong[cells * words];
        queued = new bool[cells];
        candidates = new ulong[words];
        remaining = new int[cells];
        version = new int[cells];
        decidedPattern = new int[cells];
        order = new double[cells];
        weightLogsByLayer = patterns.LayerWeights
            .Select(weights => weights.Select(weight => weight * Math.Log(weight)).ToArray()).ToArray();
    }

    public int SizeX { get; }
    public int SizeY { get; }
    public int SizeZ { get; }
    public int Backtracks { get; private set; }

    // the pattern a cell collapsed to; only valid after Run returned true
    public int PatternAt(int x, int y, int z)
    {
        return decidedPattern[Index(x, y, z)];
    }

    // false if the contradictions could not be resolved within the backtracking limit
    public bool Run(Action<float>? progress = null)
    {
        Initialize();
        var lastReported = 0f;
        while (true)
        {
            var cell = NextCell();
            if (cell < 0)
                break;
            var chosen = Choose(cell);
            decisions.Push(new Decision(cell, chosen, waveTrail.Count, remainingTrail.Count));
            Collapse(cell, chosen);
            while (!Propagate())
                if (!Backtrack())
                    return false;

            var fraction = (float)decided / cells;
            if (fraction - lastReported >= 0.005f)
            {
                lastReported = fraction;
                progress?.Invoke(fraction);
            }
        }

        for (var cell = 0; cell < cells; cell++)
            decidedPattern[cell] = FirstPattern(cell);
        return true;
    }

    private void Initialize()
    {
        var full = new ulong[words];
        for (var p = 0; p < patterns.Count; p++)
            full[p >> 6] |= 1UL << (p & 63);
        for (var cell = 0; cell < cells; cell++)
        {
            Array.Copy(full, 0, wave, cell * words, words);
            remaining[cell] = patterns.Count;
            order[cell] = random.NextDouble();
            if (patterns.Count > 1)
                Push(cell);
            else
                decided++;
        }
    }

    // the undecided cell with the lowest priority, or -1 when all cells are decided
    private int NextCell()
    {
        while (heap.Count > 0)
        {
            var (cell, cellVersion) = heap.Pop();
            if (remaining[cell] > 1 && cellVersion == version[cell])
                return cell;
        }

        return -1;
    }

    // a weighted-random pattern among the cell's remaining ones, using the weights at the cell's height
    private int Choose(int cell)
    {
        var offset = cell * words;
        var weights = WeightsOf(cell);
        var total = 0.0;
        for (var w = 0; w < words; w++)
            for (var bits = wave[offset + w]; bits != 0; bits &= bits - 1)
                total += weights[w * 64 + Bits.TrailingZeros(bits)];
        var pick = random.NextDouble() * total;
        var last = -1;
        for (var w = 0; w < words; w++)
            for (var bits = wave[offset + w]; bits != 0; bits &= bits - 1)
            {
                last = w * 64 + Bits.TrailingZeros(bits);
                pick -= weights[last];
                if (pick <= 0)
                    return last;
            }

        return last;
    }

    private void Collapse(int cell, int chosen)
    {
        var offset = cell * words;
        for (var w = 0; w < words; w++)
        {
            var keep = w == chosen >> 6 ? 1UL << (chosen & 63) : 0;
            removed[offset + w] |= wave[offset + w] & ~keep;
            SetWord(offset + w, keep);
        }

        ApplyChange(cell, 1);
    }

    // undoes decisions until one can be replaced: the pattern it chose is banned from its cell and the result
    // propagates without contradiction. False when out of decisions or over the backtracking limit.
    private bool Backtrack()
    {
        while (decisions.Count > 0)
        {
            ClearPropagation();
            if (++Backtracks > maxBacktracks)
                return false;
            var decision = decisions.Pop();
            Undo(decision.WaveMark, decision.RemainingMark);
            // the ban is logged as part of the previous decision, so stepping further back also lifts it
            if (Ban(decision.Cell, decision.Pattern) && Propagate())
                return true;
        }

        ClearPropagation();
        return false;
    }

    private bool Ban(int cell, int pattern)
    {
        var index = cell * words + (pattern >> 6);
        var bit = 1UL << (pattern & 63);
        if ((wave[index] & bit) == 0)
            return true;
        SetWord(index, wave[index] & ~bit);
        removed[index] |= bit;
        return ApplyChange(cell, remaining[cell] - 1);
    }

    private void Undo(int waveMark, int remainingMark)
    {
        for (var i = waveTrail.Count - 1; i >= waveMark; i--)
            wave[waveTrail[i].Index] = waveTrail[i].Old;
        waveTrail.RemoveRange(waveMark, waveTrail.Count - waveMark);

        for (var i = remainingTrail.Count - 1; i >= remainingMark; i--)
        {
            var (cell, old) = remainingTrail[i];
            if (remaining[cell] == 1 && old > 1)
                decided--;
            remaining[cell] = old;
            if (old > 1)
                Push(cell);
        }

        remainingTrail.RemoveRange(remainingMark, remainingTrail.Count - remainingMark);
    }

    // drops pending propagation after a contradiction
    private void ClearPropagation()
    {
        stack.Clear();
        Array.Clear(queued, 0, queued.Length);
        Array.Clear(removed, 0, removed.Length);
    }

    // processes cells that lost patterns: only neighbour patterns that one of the lost patterns could support may
    // have lost their last support, and the direction tables (Propagator) list exactly those, so only they are
    // re-checked. False on contradiction.
    private bool Propagate()
    {
        while (stack.Count > 0)
        {
            var cell = stack.Pop();
            queued[cell] = false;
            var z = cell / (SizeX * SizeY);
            var y = cell / SizeX % SizeY;
            var x = cell % SizeX;
            for (var d = 0; d < WfcPatternSet.Directions.Length; d++)
            {
                var (dx, dy, dz) = WfcPatternSet.Directions[d];
                int nx = x + dx, ny = y + dy, nz = z + dz;
                if (nx < 0 || ny < 0 || nz < 0 || nx >= SizeX || ny >= SizeY || nz >= SizeZ)
                    continue;
                if (!RemoveUnsupported(Index(nx, ny, nz), cell, d))
                    return false;
            }

            Array.Clear(removed, cell * words, words);
        }

        return true;
    }

    // the neighbour lies in direction d from cell; removes its patterns that lost their last support in cell.
    // False on contradiction.
    private bool RemoveUnsupported(int neighbour, int cell, int d)
    {
        var neighbourOffset = neighbour * words;
        var cellOffset = cell * words;

        // candidates: neighbour patterns that a pattern the cell lost could have supported
        Array.Clear(candidates, 0, words);
        for (var w = 0; w < words; w++)
            for (var lost = removed[cellOffset + w]; lost != 0; lost &= lost - 1)
            {
                var supportedByLost = patterns.Propagator[
                    (w * 64 + Bits.TrailingZeros(lost)) * WfcPatternSet.Directions.Length + d];
                for (var i = 0; i < words; i++)
                    candidates[i] |= supportedByLost[i];
            }

        // a candidate keeps its place if some pattern still left in the cell supports it
        var towardsCell = d ^ 1;
        var removedAny = false;
        for (var w = 0; w < words; w++)
        {
            var kept = wave[neighbourOffset + w];
            for (var bits = candidates[w] & kept; bits != 0; bits &= bits - 1)
            {
                var q = w * 64 + Bits.TrailingZeros(bits);
                var compatible = patterns.Propagator[q * WfcPatternSet.Directions.Length + towardsCell];
                var supported = false;
                for (var i = 0; i < words; i++)
                    if ((compatible[i] & wave[cellOffset + i]) != 0)
                    {
                        supported = true;
                        break;
                    }

                if (!supported)
                    kept &= ~(1UL << (q & 63));
            }

            var lostHere = wave[neighbourOffset + w] & ~kept;
            if (lostHere == 0)
                continue;
            removed[neighbourOffset + w] |= lostHere;
            SetWord(neighbourOffset + w, kept);
            removedAny = true;
        }

        if (!removedAny)
            return true;
        var count = 0;
        for (var w = 0; w < words; w++)
            count += Bits.PopCount(wave[neighbourOffset + w]);
        return ApplyChange(neighbour, count);
    }

    // after a cell lost patterns: records how many remain, requeues it with its new priority and schedules it for
    // propagation; false on contradiction
    private bool ApplyChange(int cell, int count)
    {
        if (count == 0)
            return false;
        remainingTrail.Add((cell, remaining[cell]));
        if (remaining[cell] > 1 && count == 1)
            decided++;
        remaining[cell] = count;
        if (count > 1)
            Push(cell);
        if (!queued[cell])
        {
            queued[cell] = true;
            stack.Push(cell);
        }

        return true;
    }

    private void SetWord(int index, ulong value)
    {
        if (wave[index] == value)
            return;
        waveTrail.Add((index, wave[index]));
        wave[index] = value;
    }

    private void Push(int cell)
    {
        version[cell]++;
        heap.Push(Priority(cell), cell, version[cell]);
    }

    // lower is collapsed first; the random order value breaks ties
    private double Priority(int cell)
    {
        const double tie = 1e-6;
        var layer = cell / (SizeX * SizeY);
        switch (selection)
        {
            case WfcCellSelection.FewestPatterns:
                return remaining[cell] + order[cell] * 0.5;
            case WfcCellSelection.LayerByLayer:
                // entropy stays far below 1000 (it is at most the log of the pattern count)
                return layer * 1000 + Entropy(cell) + order[cell] * tie;
            case WfcCellSelection.Scanline:
                return cell;
            case WfcCellSelection.CentreOutward:
            {
                double dx = cell % SizeX - (SizeX - 1) / 2.0, dy = cell / SizeX % SizeY - (SizeY - 1) / 2.0;
                return Math.Round(Math.Sqrt(dx * dx + dy * dy)) * 1000 + Entropy(cell) + order[cell] * tie;
            }
            case WfcCellSelection.RandomOrder:
                return order[cell];
            default:
                return Entropy(cell) + order[cell] * tie;
        }
    }

    // Shannon entropy of the cell's remaining patterns, weighted by their probability at the cell's height
    private double Entropy(int cell)
    {
        var sum = 0.0;
        var sumLog = 0.0;
        var layer = patterns.LayerOf(cell / (SizeX * SizeY));
        var weights = patterns.LayerWeights[layer];
        var weightLogs = weightLogsByLayer[layer];
        var offset = cell * words;
        for (var w = 0; w < words; w++)
            for (var bits = wave[offset + w]; bits != 0; bits &= bits - 1)
            {
                var p = w * 64 + Bits.TrailingZeros(bits);
                sum += weights[p];
                sumLog += weightLogs[p];
            }

        return Math.Log(sum) - sumLog / sum;
    }

    private double[] WeightsOf(int cell)
    {
        return patterns.LayerWeights[patterns.LayerOf(cell / (SizeX * SizeY))];
    }

    private int FirstPattern(int cell)
    {
        var offset = cell * words;
        for (var w = 0; w < words; w++)
            if (wave[offset + w] != 0)
                return w * 64 + Bits.TrailingZeros(wave[offset + w]);
        return -1;
    }

    private int Index(int x, int y, int z)
    {
        return (z * SizeY + y) * SizeX + x;
    }

    private readonly struct Decision
    {
        public Decision(int cell, int pattern, int waveMark, int remainingMark)
        {
            Cell = cell;
            Pattern = pattern;
            WaveMark = waveMark;
            RemainingMark = remainingMark;
        }

        public int Cell { get; }
        public int Pattern { get; }
        public int WaveMark { get; }
        public int RemainingMark { get; }
    }

    // binary min-heap of (priority, cell, version); stale entries are skipped when popped
    private sealed class MinHeap
    {
        private readonly List<(double Priority, int Cell, int Version)> items = new();

        public int Count => items.Count;

        public void Push(double priority, int cell, int cellVersion)
        {
            items.Add((priority, cell, cellVersion));
            var i = items.Count - 1;
            while (i > 0)
            {
                var parent = (i - 1) / 2;
                if (items[parent].Priority <= items[i].Priority)
                    break;
                (items[parent], items[i]) = (items[i], items[parent]);
                i = parent;
            }
        }

        public (int Cell, int Version) Pop()
        {
            var top = items[0];
            var last = items[items.Count - 1];
            items.RemoveAt(items.Count - 1);
            if (items.Count > 0)
            {
                items[0] = last;
                var i = 0;
                while (true)
                {
                    int left = 2 * i + 1, right = left + 1, smallest = i;
                    if (left < items.Count && items[left].Priority < items[smallest].Priority)
                        smallest = left;
                    if (right < items.Count && items[right].Priority < items[smallest].Priority)
                        smallest = right;
                    if (smallest == i)
                        break;
                    (items[smallest], items[i]) = (items[i], items[smallest]);
                    i = smallest;
                }
            }

            return (top.Cell, top.Version);
        }
    }
}

internal static class Bits
{
    private static readonly int[] DeBruijn =
    {
        0, 1, 48, 2, 57, 49, 28, 3, 61, 58, 50, 42, 38, 29, 17, 4, 62, 55, 59, 36, 53, 51, 43, 22, 45, 39, 33, 30,
        24, 18, 12, 5, 63, 47, 56, 27, 60, 41, 37, 16, 54, 35, 52, 21, 44, 32, 23, 11, 46, 26, 40, 15, 34, 20, 31,
        10, 25, 14, 19, 9, 13, 8, 7, 6
    };

    public static int PopCount(ulong value)
    {
        value -= (value >> 1) & 0x5555555555555555UL;
        value = (value & 0x3333333333333333UL) + ((value >> 2) & 0x3333333333333333UL);
        value = (value + (value >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
        return (int)((value * 0x0101010101010101UL) >> 56);
    }

    // value must not be 0
    public static int TrailingZeros(ulong value)
    {
        return DeBruijn[((value & (ulong)-(long)value) * 0x03F79D71B4CB0A89UL) >> 58];
    }
}

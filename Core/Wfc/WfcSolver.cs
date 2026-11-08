namespace Timberborn.TerrainGenerator.Core.Wfc;

// One attempt at filling a grid of cells with oriented patterns whose shared faces match
// (docs/wfc-face-lookup.md, section 5). A cell's domain (its candidates) is a pure function of its placed neighbours
// and its blacklist, so it is recomputed instead of propagated, and removing a placement needs no undo log.
// The next cell is the one with the fewest candidates among cells next to placed ones. When a cell has no candidate
// left, one of its placed neighbours is removed and that choice is blacklisted there; past the deletion limit the
// attempt fails and the caller restarts.
internal sealed class WfcSolver
{
    // weighted draws with rejection before ordering all remaining candidates at once
    private const int QuickTries = 8;

    private const int MaxClearRadius = 3;

    private readonly int[] allIds;
    private readonly int[] assigned;
    private readonly HashSet<int>?[] blacklist;
    private readonly int cells;
    private readonly int clearEvery;
    private readonly Stack<int> contradictions = new();
    private readonly IdRange[] domains;
    private readonly MinHeap heap = new();
    private readonly WfcPatternIndex index;
    private readonly bool lookahead;

    // [cell layer][pattern]
    private readonly float[][] layerWeights;
    private readonly int maxDeletions;

    // [cell * 6 + direction]: the neighbouring cell, or -1 outside the grid
    private readonly int[] neighbours;
    private readonly Random random;
    private readonly List<int> rejected = new();

    // per cell: repairs started from it in this attempt
    private readonly int[] repairsAt;
    private readonly IdRange[] constraints = new IdRange[6];

    // shared buffer for intersections, copied out at the exact length
    private int[] scratch = new int[1024];
    private readonly int[] version;
    private int assignedCount;

    // clearEvery: repairs of the same cell before a box around it is cleared; 0 never clears
    public WfcSolver(WfcPatternIndex index, int sizeX, int sizeY, int sizeZ, Random random, int maxDeletions,
        bool lookahead, int clearEvery, float[][] layerWeights)
    {
        this.index = index;
        this.random = random;
        this.maxDeletions = maxDeletions;
        this.lookahead = lookahead;
        this.clearEvery = clearEvery;
        this.layerWeights = layerWeights;
        SizeX = sizeX;
        SizeY = sizeY;
        SizeZ = sizeZ;
        cells = sizeX * sizeY * sizeZ;
        repairsAt = new int[cells];
        allIds = index.AllIds;
        assigned = new int[cells];
        blacklist = new HashSet<int>?[cells];
        domains = new IdRange[cells];
        version = new int[cells];
        neighbours = new int[cells * 6];
        for (var cell = 0; cell < cells; cell++)
        {
            assigned[cell] = -1;
            domains[cell] = new IdRange(allIds, 0, allIds.Length);
            int x = cell % sizeX, y = cell / sizeX % sizeY, z = cell / (sizeX * sizeY);
            for (var d = 0; d < 6; d++)
            {
                var (dx, dy, dz) = WfcPatternShape.Directions[d];
                int nx = x + dx, ny = y + dy, nz = z + dz;
                neighbours[cell * 6 + d] = nx < 0 || ny < 0 || nz < 0 || nx >= sizeX || ny >= sizeY || nz >= sizeZ
                    ? -1
                    : (nz * sizeY + ny) * sizeX + nx;
            }
        }
    }

    public int SizeX { get; }
    public int SizeY { get; }
    public int SizeZ { get; }
    public int Placements { get; private set; }
    public int Repairs { get; private set; }
    public int Deletions { get; private set; }
    public int LookaheadRejections { get; private set; }

    // the oriented id placed at a cell; valid after Run returned true
    public int PatternAt(int x, int y, int z)
    {
        return assigned[(z * SizeY + y) * SizeX + x];
    }

    // false when the attempt ran past the deletion limit or got stuck, and should restart
    public bool Run(Action<float>? progress = null)
    {
        var steps = 0;
        var reported = 0f;
        while (assignedCount < cells)
        {
            if (++steps % 64 == 0)
            {
                // repairs can lower the fraction; the bar only moves forward
                reported = Math.Max(reported, (float)assignedCount / cells);
                progress?.Invoke(reported);
            }

            if (TryPopContradiction(out var broken))
            {
                if (!Repair(broken))
                    return false;
                continue;
            }

            // without placed cells (at the start, or if repairs removed them all) a random free cell seeds the grid
            var cell = NextCell();
            if (cell < 0)
                cell = RandomFreeCell();
            var chosen = Choose(cell);
            if (chosen < 0)
            {
                if (!Repair(cell))
                    return false;
            }
            else
                Place(cell, chosen);
        }

        return true;
    }

    private bool TryPopContradiction(out int cell)
    {
        while (contradictions.Count > 0)
        {
            cell = contradictions.Pop();
            if (assigned[cell] < 0 && domains[cell].Count == 0)
                return true;
        }

        cell = -1;
        return false;
    }

    // the free cell with the fewest candidates among cells next to placed ones (ties random), or -1
    private int NextCell()
    {
        while (heap.Count > 0)
        {
            var (cell, cellVersion) = heap.Pop();
            if (assigned[cell] < 0 && cellVersion == version[cell] && domains[cell].Count > 0)
                return cell;
        }

        return -1;
    }

    private int RandomFreeCell()
    {
        var start = random.Next(cells);
        for (var i = 0; i < cells; i++)
        {
            var cell = (start + i) % cells;
            if (assigned[cell] < 0)
                return cell;
        }

        throw new InvalidOperationException("No free cell left");
    }

    // a weighted-random candidate that leaves every free neighbour at least one candidate, or -1. Candidates are
    // tested in weighted-random order and the first that passes wins, which picks with the same odds as filtering
    // the whole domain first, but usually tests only one.
    private int Choose(int cell)
    {
        var domain = domains[cell];
        if (domain.Count == 0)
            return -1;
        var weights = layerWeights[cell / (SizeX * SizeY)];
        var total = 0.0;
        for (var i = 0; i < domain.Count; i++)
            total += weights[domain[i] >> 2];
        rejected.Clear();
        for (var tries = 0; tries < QuickTries && rejected.Count < domain.Count; tries++)
        {
            var i = WeightedPick(domain, weights, total);
            var id = domain[i];
            if (!lookahead || Fits(cell, id))
                return id;
            LookaheadRejections++;
            rejected.Add(i);
            total -= weights[id >> 2];
        }

        if (rejected.Count == domain.Count)
            return -1;
        // all others in one weighted-random order (exponential keys, smallest first)
        var order = new List<(double Key, int Id)>(domain.Count - rejected.Count);
        for (var i = 0; i < domain.Count; i++)
            if (!rejected.Contains(i))
                order.Add((-Math.Log(1 - random.NextDouble()) / weights[domain[i] >> 2], domain[i]));
        order.Sort((a, b) => a.Key.CompareTo(b.Key));
        foreach (var (_, id) in order)
        {
            if (Fits(cell, id))
                return id;
            LookaheadRejections++;
        }

        return -1;
    }

    // a weighted-random position in the domain, skipping rejected positions; total is their weight sum
    private int WeightedPick(IdRange domain, float[] weights, double total)
    {
        var pick = random.NextDouble() * total;
        var last = -1;
        for (var i = 0; i < domain.Count; i++)
        {
            if (rejected.Count > 0 && rejected.Contains(i))
                continue;
            last = i;
            pick -= weights[domain[i] >> 2];
            if (pick <= 0)
                return i;
        }

        return last;
    }

    // one-step lookahead: every free neighbour keeps a candidate that fits the shared face
    private bool Fits(int cell, int id)
    {
        for (var d = 0; d < 6; d++)
        {
            var neighbour = neighbours[cell * 6 + d];
            if (neighbour < 0 || assigned[neighbour] >= 0)
                continue;
            if (!Intersects(domains[neighbour], index.NeighbourCandidates(id, d)))
                return false;
        }

        return true;
    }

    private void Place(int cell, int id)
    {
        assigned[cell] = id;
        assignedCount++;
        Placements++;
        // a new constraint only narrows the neighbours' domains, so intersecting with it equals recomputing them
        for (var d = 0; d < 6; d++)
        {
            var neighbour = neighbours[cell * 6 + d];
            if (neighbour < 0 || assigned[neighbour] >= 0)
                continue;
            var fitting = index.NeighbourCandidates(id, d);
            SetDomain(neighbour, Intersect(domains[neighbour], fitting), true);
        }
    }

    // removes the placed neighbour whose constraint frees the cell up most, blacklisting its choice there;
    // false once the attempt should restart
    private bool Repair(int cell)
    {
        if (Deletions >= maxDeletions)
            return false;
        Repairs++;
        // a cell that keeps failing sits in a trap that removing single neighbours does not open, as the conflict is
        // often with a free neighbour's other neighbours; clear a box around it instead, growing with each repeat
        if (clearEvery > 0 && ++repairsAt[cell] % clearEvery == 0)
        {
            ClearAround(cell, Math.Min(MaxClearRadius, repairsAt[cell] / clearEvery));
            return true;
        }

        var best = -1;
        var bestGain = 0;
        var ties = 0;
        var placed = 0;
        for (var d = 0; d < 6; d++)
        {
            var neighbour = neighbours[cell * 6 + d];
            if (neighbour < 0 || assigned[neighbour] < 0)
                continue;
            placed++;
            var gain = ComputeDomain(cell, d, out _).Count;
            // the largest gain wins, ties (and all-zero gains) broken at random
            if (gain > bestGain)
            {
                best = neighbour;
                bestGain = gain;
                ties = 1;
            }
            else if (gain == bestGain && random.Next(++ties) == 0)
                best = neighbour;
        }

        if (placed == 0)
            return false;
        Unassign(best);
        return true;
    }

    // removes every placement within radius cells (a box) and forgets the blacklists there, as they were learned
    // around placements that are now gone
    private void ClearAround(int cell, int radius)
    {
        int x = cell % SizeX, y = cell / SizeX % SizeY, z = cell / (SizeX * SizeY);
        for (var pass = 0; pass < 2; pass++)
        for (var cz = Math.Max(0, z - radius - 1); cz <= Math.Min(SizeZ - 1, z + radius + 1); cz++)
        for (var cy = Math.Max(0, y - radius - 1); cy <= Math.Min(SizeY - 1, y + radius + 1); cy++)
        for (var cx = Math.Max(0, x - radius - 1); cx <= Math.Min(SizeX - 1, x + radius + 1); cx++)
        {
            var inside = Math.Abs(cx - x) <= radius && Math.Abs(cy - y) <= radius && Math.Abs(cz - z) <= radius;
            var target = (cz * SizeY + cy) * SizeX + cx;
            if (pass == 0 && inside)
            {
                // first remove, then recompute the box and its rim once all removals are done
                blacklist[target] = null;
                if (assigned[target] < 0)
                    continue;
                assigned[target] = -1;
                assignedCount--;
                Deletions++;
            }
            else if (pass == 1 && assigned[target] < 0)
                Recompute(target);
        }
    }

    private void Unassign(int cell)
    {
        (blacklist[cell] ??= new HashSet<int>()).Add(assigned[cell]);
        assigned[cell] = -1;
        assignedCount--;
        Deletions++;
        Recompute(cell);
        for (var d = 0; d < 6; d++)
        {
            var neighbour = neighbours[cell * 6 + d];
            if (neighbour >= 0 && assigned[neighbour] < 0)
                Recompute(neighbour);
        }
    }

    private void Recompute(int cell)
    {
        var domain = ComputeDomain(cell, -1, out var constrained);
        SetDomain(cell, domain, constrained);
    }

    // constrained: the cell has a placed neighbour, so it may be picked next
    private void SetDomain(int cell, IdRange domain, bool constrained)
    {
        domains[cell] = domain;
        version[cell]++;
        if (domain.Count == 0)
            contradictions.Push(cell);
        else if (constrained)
            heap.Push(domain.Count + random.NextDouble(), cell, version[cell]);
    }

    // the intersection of the candidate lists of all placed neighbours (except the one in direction skip), shortest
    // first, minus the blacklist; all ids if no neighbour is placed
    private IdRange ComputeDomain(int cell, int skip, out bool constrained)
    {
        var count = 0;
        for (var d = 0; d < 6; d++)
        {
            var neighbour = neighbours[cell * 6 + d];
            if (d == skip || neighbour < 0 || assigned[neighbour] < 0)
                continue;
            constraints[count++] = index.NeighbourCandidates(assigned[neighbour], d ^ 1);
        }

        constrained = count > 0;
        IdRange domain;
        if (count == 0)
            domain = new IdRange(allIds, 0, allIds.Length);
        else
        {
            Array.Sort(constraints, 0, count, IdRangeByCount.Instance);
            domain = constraints[0];
            for (var i = 1; i < count && domain.Count > 0; i++)
                domain = Intersect(domain, constraints[i]);
        }

        return WithoutBlacklist(cell, domain);
    }

    private IdRange WithoutBlacklist(int cell, IdRange domain)
    {
        var banned = blacklist[cell];
        if (banned == null)
            return domain;
        var hits = 0;
        for (var i = 0; i < domain.Count; i++)
            if (banned.Contains(domain[i]))
                hits++;
        if (hits == 0)
            return domain;
        var kept = new int[domain.Count - hits];
        var n = 0;
        for (var i = 0; i < domain.Count; i++)
            if (!banned.Contains(domain[i]))
                kept[n++] = domain[i];
        return new IdRange(kept, 0, n);
    }

    private bool IsAll(IdRange range)
    {
        return range.Items == allIds && range.Count == allIds.Length;
    }

    // sorted-list intersection: a linear merge, or binary searches in the longer list when lengths differ a lot
    private IdRange Intersect(IdRange a, IdRange b)
    {
        if (IsAll(a))
            return b;
        if (IsAll(b))
            return a;
        if (a.Count > b.Count)
            (a, b) = (b, a);
        if (a.Count == 0)
            return IdRange.Empty;
        if (scratch.Length < a.Count)
            scratch = new int[Math.Max(a.Count, scratch.Length * 2)];
        var n = 0;
        int ai = a.Start, aEnd = a.Start + a.Count, bi = b.Start, bEnd = b.Start + b.Count;
        if (b.Count > 8 * a.Count)
            for (; ai < aEnd && bi < bEnd; ai++)
            {
                bi = LowerBound(b.Items, bi, bEnd, a.Items[ai]);
                if (bi < bEnd && b.Items[bi] == a.Items[ai])
                    scratch[n++] = b.Items[bi++];
            }
        else
            while (ai < aEnd && bi < bEnd)
            {
                int x = a.Items[ai], y = b.Items[bi];
                if (x < y)
                    ai++;
                else if (x > y)
                    bi++;
                else
                {
                    scratch[n++] = x;
                    ai++;
                    bi++;
                }
            }

        if (n == 0)
            return IdRange.Empty;
        var result = new int[n];
        Array.Copy(scratch, result, n);
        return new IdRange(result, 0, n);
    }

    private bool Intersects(IdRange a, IdRange b)
    {
        if (IsAll(a))
            return b.Count > 0;
        if (IsAll(b))
            return a.Count > 0;
        if (a.Count > b.Count)
            (a, b) = (b, a);
        int ai = a.Start, aEnd = a.Start + a.Count, bi = b.Start, bEnd = b.Start + b.Count;
        if (b.Count > 8 * a.Count)
        {
            for (; ai < aEnd && bi < bEnd; ai++)
            {
                bi = LowerBound(b.Items, bi, bEnd, a.Items[ai]);
                if (bi < bEnd && b.Items[bi] == a.Items[ai])
                    return true;
            }

            return false;
        }

        while (ai < aEnd && bi < bEnd)
        {
            int x = a.Items[ai], y = b.Items[bi];
            if (x == y)
                return true;
            if (x < y)
                ai++;
            else
                bi++;
        }

        return false;
    }

    // the first position in [start, end) whose value is at least value
    private static int LowerBound(int[] items, int start, int end, int value)
    {
        while (start < end)
        {
            var middle = (start + end) >> 1;
            if (items[middle] < value)
                start = middle + 1;
            else
                end = middle;
        }

        return start;
    }

    private sealed class IdRangeByCount : IComparer<IdRange>
    {
        public static readonly IdRangeByCount Instance = new();

        public int Compare(IdRange a, IdRange b)
        {
            return a.Count.CompareTo(b.Count);
        }
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

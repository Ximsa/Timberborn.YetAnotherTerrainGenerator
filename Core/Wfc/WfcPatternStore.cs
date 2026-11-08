namespace Timberborn.TerrainGenerator.Core.Wfc;

// A set of patterns kept in one pool of words (LayerWords per layer), numbered in the order they were added.
// One shared array with an open-addressing table keeps large pattern sets far smaller than a dictionary of arrays.
internal sealed class WfcPatternStore
{
    private const int Empty = -1;

    private readonly WfcPatternShape shape;
    private readonly int words;
    private ulong[] pool;

    // pattern ids by hash, linear probing; at most half full
    private int[] table;

    public WfcPatternStore(WfcPatternShape shape)
    {
        this.shape = shape;
        words = shape.Height * shape.LayerWords;
        pool = new ulong[words * 1024];
        table = NewTable(2048);
    }

    public int Count { get; private set; }
    public long Bytes => pool.LongLength * 8 + table.LongLength * 4;

    // the pattern's id, or -1
    public int Find(ulong[] pattern)
    {
        var mask = table.Length - 1;
        for (var slot = (int)Hash(pattern) & mask;; slot = (slot + 1) & mask)
        {
            var id = table[slot];
            if (id == Empty)
                return -1;
            if (Matches(id, pattern))
                return id;
        }
    }

    public int AddOrFind(ulong[] pattern, out bool added)
    {
        var mask = table.Length - 1;
        var slot = (int)Hash(pattern) & mask;
        for (;; slot = (slot + 1) & mask)
        {
            var id = table[slot];
            if (id == Empty)
                break;
            if (Matches(id, pattern))
            {
                added = false;
                return id;
            }
        }

        if ((long)(Count + 1) * words > pool.LongLength)
            Array.Resize(ref pool, pool.Length * 2);
        var start = Count * words;
        for (var z = 0; z < shape.Height; z++)
        for (var w = 0; w < shape.LayerWords; w++)
            pool[start++] = pattern[2 * z + w];
        table[slot] = Count;
        added = true;
        if (++Count * 2 > table.Length)
            Grow();
        return Count - 1;
    }

    public void Get(int id, ulong[] pattern)
    {
        Array.Clear(pattern, 0, pattern.Length);
        var start = id * words;
        for (var z = 0; z < shape.Height; z++)
        for (var w = 0; w < shape.LayerWords; w++)
            pattern[2 * z + w] = pool[start++];
    }

    // the pool can stay larger than needed while adding; trims it once all patterns are in
    public void Trim()
    {
        Array.Resize(ref pool, Count * words);
    }

    private bool Matches(int id, ulong[] pattern)
    {
        var start = id * words;
        for (var z = 0; z < shape.Height; z++)
        for (var w = 0; w < shape.LayerWords; w++)
            if (pool[start++] != pattern[2 * z + w])
                return false;
        return true;
    }

    private ulong Hash(ulong[] pattern)
    {
        var hash = 0xCBF29CE484222325UL;
        for (var z = 0; z < shape.Height; z++)
        for (var w = 0; w < shape.LayerWords; w++)
            hash = Mix(hash ^ pattern[2 * z + w]);
        return hash;
    }

    private ulong HashOfStored(int id)
    {
        var hash = 0xCBF29CE484222325UL;
        var start = id * words;
        for (var i = 0; i < words; i++)
            hash = Mix(hash ^ pool[start + i]);
        return hash;
    }

    private static ulong Mix(ulong value)
    {
        value *= 0x9E3779B97F4A7C15UL;
        return value ^ (value >> 31);
    }

    private void Grow()
    {
        table = NewTable(table.Length * 2);
        var mask = table.Length - 1;
        for (var id = 0; id < Count; id++)
        {
            var slot = (int)HashOfStored(id) & mask;
            while (table[slot] != Empty)
                slot = (slot + 1) & mask;
            table[slot] = id;
        }
    }

    private static int[] NewTable(int size)
    {
        var result = new int[size];
        for (var i = 0; i < size; i++)
            result[i] = Empty;
        return result;
    }
}

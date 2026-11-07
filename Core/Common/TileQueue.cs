namespace Timberborn.TerrainGenerator.Core.Common;

// binary min-heap of tiles by priority, since netstandard2.1 has no PriorityQueue
public sealed class TileQueue
{
    private readonly List<(float Priority, int Tile)> items = new();

    public int Count => items.Count;

    public void Push(int tile, float priority)
    {
        items.Add((priority, tile));
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

    public bool TryPop(out int tile, out float priority)
    {
        if (items.Count == 0)
        {
            tile = 0;
            priority = 0;
            return false;
        }

        (priority, tile) = items[0];
        var last = items[items.Count - 1];
        items.RemoveAt(items.Count - 1);
        if (items.Count == 0) return true;
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

        return true;
    }
}

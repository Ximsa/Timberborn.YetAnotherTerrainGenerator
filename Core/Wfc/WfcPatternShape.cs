namespace Timberborn.TerrainGenerator.Core.Wfc;

// The shape of the patterns: Width x Width voxels across, Height up (docs/wfc-face-lookup.md, section 2, generalised).
// Placed patterns share their last voxel layer with the next one along each axis, so cells lie Width - 1 apart across
// and Height - 1 apart vertically. Coordinates follow VoxelMap: x and y across, z up, so the spec's Y is z here.
// A pattern is held as two words per layer, ulong[2 * Height]: layer z's bits x + Width * y, the low 64 at 2z and the
// rest at 2z + 1. Patterns are ordered as big numbers, top layer and high word first.
public sealed class WfcPatternShape
{
    public const int MaxWidth = 10;

    // directions: +x, -x, +y, -y, +z (up), -z (down); the opposite of direction d is d ^ 1
    public static readonly (int X, int Y, int Z)[] Directions =
    {
        (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)
    };

    // [row y][bits of that row]: where the row's voxels go in a layer turned once about the vertical axis
    private readonly ulong[][] rotatedLow;
    private readonly ulong[][] rotatedHigh;

    public WfcPatternShape(int width, int height)
    {
        if (width < 2 || width > MaxWidth || height < 2)
            throw new ArgumentOutOfRangeException(nameof(width),
                $"Patterns are 2 to {MaxWidth} wide and at least 2 high");
        if (width * height > 64)
            throw new ArgumentOutOfRangeException(nameof(height), "A pattern's side holds at most 64 voxels");
        Width = width;
        Height = height;
        LayerBits = width * width;
        LayerWords = LayerBits > 64 ? 2 : 1;
        rotatedLow = new ulong[width][];
        rotatedHigh = new ulong[width][];
        for (var y = 0; y < width; y++)
        {
            rotatedLow[y] = new ulong[1 << width];
            rotatedHigh[y] = new ulong[1 << width];
            for (var row = 0; row < 1 << width; row++)
            for (var x = 0; x < width; x++)
            {
                if ((row & (1 << x)) == 0)
                    continue;
                // (x, y) -> (w - 1 - y, x)
                var bit = width - 1 - y + width * x;
                if (bit < 64)
                    rotatedLow[y][row] |= 1UL << bit;
                else
                    rotatedHigh[y][row] |= 1UL << (bit - 64);
            }
        }
    }

    public int Width { get; }
    public int Height { get; }
    public int LayerBits { get; }

    // words a layer needs when stored: 1, or 2 for layers over 64 voxels
    public int LayerWords { get; }
    public int StrideAcross => Width - 1;
    public int StrideUp => Height - 1;

    public override string ToString()
    {
        return $"{Width}x{Width}x{Height}";
    }

    public ulong[] NewPattern()
    {
        return new ulong[2 * Height];
    }

    public bool Voxel(ulong[] pattern, int x, int y, int z)
    {
        var bit = x + Width * y;
        return ((pattern[2 * z + (bit >> 6)] >> (bit & 63)) & 1) != 0;
    }

    public void SetVoxel(ulong[] pattern, int x, int y, int z)
    {
        var bit = x + Width * y;
        pattern[2 * z + (bit >> 6)] |= 1UL << (bit & 63);
    }

    // one turn about the vertical axis, (x, y, z) -> (w - 1 - y, x, z), in place
    public void Rotate(ulong[] pattern, int turns = 1)
    {
        for (var turn = 0; turn < turns; turn++)
            for (var z = 0; z < Height; z++)
                RotateLayer(ref pattern[2 * z], ref pattern[2 * z + 1]);
    }

    // turns the pattern in place to the smallest of its four rotations
    public void Canonicalize(ulong[] pattern, ulong[] scratch)
    {
        Array.Copy(pattern, scratch, pattern.Length);
        for (var turn = 1; turn < 4; turn++)
        {
            Rotate(scratch);
            if (Compare(scratch, pattern) < 0)
                Array.Copy(scratch, pattern, pattern.Length);
        }
    }

    public int Compare(ulong[] a, ulong[] b)
    {
        for (var i = a.Length - 1; i >= 0; i--)
            if (a[i] != b[i])
                return a[i] < b[i] ? -1 : 1;
        return 0;
    }

    // how many of the four rotations differ (1, 2 or 4); rotations 0 .. count - 1 are the distinct ones
    public int DistinctRotations(ulong[] pattern)
    {
        var turned = (ulong[])pattern.Clone();
        Rotate(turned);
        if (Compare(turned, pattern) == 0)
            return 1;
        Rotate(turned);
        return Compare(turned, pattern) == 0 ? 2 : 4;
    }

    // faces are read in the world frame, so neighbours fit exactly when face(a, d) == face(b, d ^ 1):
    // x faces bit w * z + y, y faces bit w * z + x, z faces bit w * y + x (a whole layer, up to two words)
    public (ulong Low, ulong High) Face(ulong[] pattern, int direction)
    {
        switch (direction)
        {
            case 4:
                return (pattern[2 * (Height - 1)], pattern[2 * Height - 1]);
            case 5:
                return (pattern[0], pattern[1]);
        }

        var fixedAt = direction % 2 == 0 ? Width - 1 : 0;
        ulong face = 0;
        for (var z = 0; z < Height; z++)
        for (var i = 0; i < Width; i++)
        {
            var solid = direction < 2 ? Voxel(pattern, fixedAt, i, z) : Voxel(pattern, i, fixedAt, z);
            if (solid)
                face |= 1UL << (Width * z + i);
        }

        return (face, 0);
    }

    private void RotateLayer(ref ulong low, ref ulong high)
    {
        ulong newLow = 0, newHigh = 0;
        for (var y = 0; y < Width; y++)
        {
            var row = Bits(low, high, Width * y, Width);
            newLow |= rotatedLow[y][row];
            newHigh |= rotatedHigh[y][row];
        }

        low = newLow;
        high = newHigh;
    }

    // count (at most 64) bits starting at start of the 128-bit number high:low
    internal static ulong Bits(ulong low, ulong high, int start, int count)
    {
        ulong value;
        if (start >= 64)
            value = high >> (start - 64);
        else
        {
            value = low >> start;
            if (start > 0 && start + count > 64)
                value |= high << (64 - start);
        }

        return count == 64 ? value : value & ((1UL << count) - 1);
    }
}

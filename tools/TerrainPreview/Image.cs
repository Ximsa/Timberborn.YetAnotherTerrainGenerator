using System.IO.Compression;

namespace TerrainPreview;

public readonly record struct Rgb(byte R, byte G, byte B)
{
    public Rgb Scale(double factor)
    {
        return new Rgb(Clamp(R * factor), Clamp(G * factor), Clamp(B * factor));
    }

    public Rgb Mix(Rgb other, double amount)
    {
        return new Rgb(Clamp(R + (other.R - R) * amount), Clamp(G + (other.G - G) * amount),
            Clamp(B + (other.B - B) * amount));
    }

    private static byte Clamp(double value)
    {
        return (byte)Math.Max(0, Math.Min(255, Math.Round(value)));
    }
}

// A minimal RGB image that can be saved as PNG without extra dependencies.
public sealed class Image
{
    private readonly byte[] pixels;

    public Image(int width, int height, Rgb background)
    {
        Width = width;
        Height = height;
        pixels = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            this[x, y] = background;
    }

    public int Width { get; }
    public int Height { get; }

    public Rgb this[int x, int y]
    {
        get
        {
            var i = (y * Width + x) * 3;
            return new Rgb(pixels[i], pixels[i + 1], pixels[i + 2]);
        }
        set
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height)
                return;
            var i = (y * Width + x) * 3;
            pixels[i] = value.R;
            pixels[i + 1] = value.G;
            pixels[i + 2] = value.B;
        }
    }

    public void FillRect(int x, int y, int width, int height, Rgb color)
    {
        for (var py = y; py < y + height; py++)
        for (var px = x; px < x + width; px++)
            this[px, py] = color;
    }

    // 3x5 pixel glyphs for labels; rows top to bottom, bits left to right
    private static readonly Dictionary<char, int[]> Glyphs = new()
    {
        ['0'] = new[] { 7, 5, 5, 5, 7 }, ['1'] = new[] { 2, 6, 2, 2, 7 }, ['2'] = new[] { 7, 1, 7, 4, 7 },
        ['3'] = new[] { 7, 1, 7, 1, 7 }, ['4'] = new[] { 5, 5, 7, 1, 1 }, ['5'] = new[] { 7, 4, 7, 1, 7 },
        ['6'] = new[] { 7, 4, 7, 5, 7 }, ['7'] = new[] { 7, 1, 1, 1, 1 }, ['8'] = new[] { 7, 5, 7, 5, 7 },
        ['9'] = new[] { 7, 5, 7, 1, 7 }, ['%'] = new[] { 5, 1, 2, 4, 5 }, ['.'] = new[] { 0, 0, 0, 0, 2 },
        [' '] = new[] { 0, 0, 0, 0, 0 }
    };

    public void DrawText(string text, int x, int y, int scale, Rgb color)
    {
        foreach (var character in text)
        {
            if (Glyphs.TryGetValue(character, out var rows))
                for (var row = 0; row < 5; row++)
                for (var column = 0; column < 3; column++)
                    if ((rows[row] & (4 >> column)) != 0)
                        FillRect(x + column * scale, y + row * scale, scale, scale, color);
            x += 4 * scale;
        }
    }

    public void Blit(Image source, int x, int y)
    {
        for (var py = 0; py < source.Height; py++)
        for (var px = 0; px < source.Width; px++)
            this[x + px, y + py] = source[px, py];
    }

    public void SavePng(string path)
    {
        using var file = File.Create(path);
        file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

        var header = new byte[13];
        WriteInt(header, 0, Width);
        WriteInt(header, 4, Height);
        header[8] = 8; // bit depth
        header[9] = 2; // RGB
        WriteChunk(file, "IHDR", header);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true))
            for (var y = 0; y < Height; y++)
            {
                zlib.WriteByte(0); // no filter
                zlib.Write(pixels, y * Width * 3, Width * 3);
            }

        WriteChunk(file, "IDAT", compressed.ToArray());
        WriteChunk(file, "IEND", Array.Empty<byte>());
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        WriteInt(length, 0, data.Length);
        stream.Write(length);
        var typeAndData = new byte[4 + data.Length];
        for (var i = 0; i < 4; i++)
            typeAndData[i] = (byte)type[i];
        data.CopyTo(typeAndData, 4);
        stream.Write(typeAndData);
        var crc = new byte[4];
        WriteInt(crc, 0, (int)Crc32(typeAndData));
        stream.Write(crc);
    }

    private static void WriteInt(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }

        return ~crc;
    }
}

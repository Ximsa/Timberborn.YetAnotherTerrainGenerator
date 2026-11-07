using Timberborn.TerrainGenerator.Core.Common;

namespace TerrainPreview;

// Renders a VoxelMap as one preview image: an isometric 3D view on top, and below it a top-down height map
// (columns with overhangs or floating terrain tinted magenta) next to a vertical cross-section through the middle.
// An optional overlay colours the top surface of single columns (water, plants, markers).
public static class VoxelRenderer
{
    private static readonly Rgb Background = new(32, 34, 40);
    private static readonly Rgb Sky = new(170, 200, 230);
    private static readonly Rgb Overhang = new(220, 60, 200);

    private static readonly (double At, Rgb Color)[] HeightColors =
    {
        (0.0, new Rgb(70, 110, 60)),
        (0.25, new Rgb(110, 150, 70)),
        (0.5, new Rgb(150, 130, 90)),
        (0.75, new Rgb(130, 128, 125)),
        (1.0, new Rgb(240, 240, 240))
    };

    public static Image Render(VoxelMap map, Func<int, int, Rgb?>? overlay = null)
    {
        var iso = RenderIsometric(map, overlay);
        var heights = RenderHeightMap(map, overlay);
        var section = RenderSection(map);
        const int gap = 16;
        var width = Math.Max(iso.Width, heights.Width + gap + section.Width) + 2 * gap;
        var height = iso.Height + gap + Math.Max(heights.Height, section.Height) + 2 * gap;
        var image = new Image(width, height, Background);
        image.Blit(iso, (width - iso.Width) / 2, gap);
        image.Blit(heights, gap, gap + iso.Height + gap);
        image.Blit(section, gap + heights.Width + gap, gap + iso.Height + gap);
        return image;
    }

    private static Rgb ColorFor(int z, int sizeZ)
    {
        var t = sizeZ <= 1 ? 0 : (double)z / (sizeZ - 1);
        for (var i = 1; i < HeightColors.Length; i++)
            if (t <= HeightColors[i].At)
            {
                var (fromAt, from) = HeightColors[i - 1];
                var (toAt, to) = HeightColors[i];
                return from.Mix(to, (t - fromAt) / (toAt - fromAt));
            }

        return HeightColors[^1].Color;
    }

    public static Image RenderIsometric(VoxelMap map, Func<int, int, Rgb?>? overlay = null)
    {
        // tile half width in pixels, and pixels per voxel of height
        var tile = Math.Max(1, Math.Min(4, 640 / (map.SizeX + map.SizeY)));
        double half = tile;
        double voxelHeight = tile * 1.5;
        var width = (int)((map.SizeX + map.SizeY) * half) + 2;
        var height = (int)((map.SizeX + map.SizeY) * half / 2 + map.SizeZ * voxelHeight) + 2;
        var image = new Image(width, height, Background);
        var originX = map.SizeY * half;
        var originY = map.SizeZ * voxelHeight;

        (double X, double Y) Project(double x, double y, double z)
        {
            return (originX + (x - y) * half, originY + (x + y) * half / 2 - z * voxelHeight);
        }

        // the topmost solid voxel of its column
        bool IsTop(int x, int y, int z)
        {
            for (var above = z + 1; above < map.SizeZ; above++)
                if (map[x, y, above])
                    return false;
            return true;
        }

        bool Solid(int x, int y, int z)
        {
            return x >= 0 && y >= 0 && z >= 0 && x < map.SizeX && y < map.SizeY && z < map.SizeZ && map[x, y, z];
        }

        // painter's order: far (small x, y) and low first
        for (var x = 0; x < map.SizeX; x++)
        for (var y = 0; y < map.SizeY; y++)
        for (var z = 0; z < map.SizeZ; z++)
        {
            if (!map[x, y, z])
                continue;
            var color = ColorFor(z, map.SizeZ);
            if (!Solid(x, y, z + 1) && overlay?.Invoke(x, y) is { } tint && IsTop(x, y, z))
                color = tint;
            if (!Solid(x, y, z + 1))
                FillQuad(image, color, Project(x, y, z + 1), Project(x + 1, y, z + 1), Project(x + 1, y + 1, z + 1),
                    Project(x, y + 1, z + 1));
            if (!Solid(x + 1, y, z))
                FillQuad(image, color.Scale(0.75), Project(x + 1, y, z + 1), Project(x + 1, y + 1, z + 1),
                    Project(x + 1, y + 1, z), Project(x + 1, y, z));
            if (!Solid(x, y + 1, z))
                FillQuad(image, color.Scale(0.58), Project(x, y + 1, z + 1), Project(x + 1, y + 1, z + 1),
                    Project(x + 1, y + 1, z), Project(x, y + 1, z));
        }

        return image;
    }

    private static void FillQuad(Image image, Rgb color, params (double X, double Y)[] corners)
    {
        var minX = (int)Math.Floor(corners.Min(c => c.X));
        var maxX = (int)Math.Ceiling(corners.Max(c => c.X));
        var minY = (int)Math.Floor(corners.Min(c => c.Y));
        var maxY = (int)Math.Ceiling(corners.Max(c => c.Y));
        for (var py = minY; py <= maxY; py++)
        for (var px = minX; px <= maxX; px++)
            if (Inside(corners, px + 0.5, py + 0.5))
                image[px, py] = color;
    }

    private static bool Inside((double X, double Y)[] polygon, double x, double y)
    {
        bool? sign = null;
        for (var i = 0; i < polygon.Length; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Length];
            var cross = (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);
            if (Math.Abs(cross) < 1e-9)
                continue;
            if (sign == null)
                sign = cross > 0;
            else if (sign != cross > 0)
                return false;
        }

        return true;
    }

    private static Image RenderHeightMap(VoxelMap map, Func<int, int, Rgb?>? overlay = null)
    {
        var scale = Math.Max(1, Math.Min(4, 320 / Math.Max(map.SizeX, map.SizeY)));
        var image = new Image(map.SizeX * scale, map.SizeY * scale, Background);
        for (var y = 0; y < map.SizeY; y++)
        for (var x = 0; x < map.SizeX; x++)
        {
            var top = -1;
            var hasGap = false;
            for (var z = 0; z < map.SizeZ; z++)
                if (map[x, y, z])
                {
                    if (top >= 0 && top < z - 1)
                        hasGap = true;
                    top = z;
                }

            var color = top < 0 ? Sky : ColorFor(top, map.SizeZ);
            if (hasGap)
                color = color.Mix(Overhang, 0.6);
            if (overlay?.Invoke(x, y) is { } tint)
                color = tint;
            image.FillRect(x * scale, y * scale, scale, scale, color);
        }

        return image;
    }

    // cross-section through the row with the most overhangs (the middle row if there are none)
    private static Image RenderSection(VoxelMap map)
    {
        var scale = Math.Max(1, Math.Min(6, 480 / map.SizeX));
        var y = map.SizeY / 2;
        var mostGaps = 0;
        for (var row = 0; row < map.SizeY; row++)
        {
            var gaps = 0;
            for (var x = 0; x < map.SizeX; x++)
            for (var z = 1; z < map.SizeZ; z++)
                if (map[x, row, z] && !map[x, row, z - 1])
                    gaps++;
            if (gaps > mostGaps)
            {
                mostGaps = gaps;
                y = row;
            }
        }

        var image = new Image(map.SizeX * scale, map.SizeZ * scale, Sky);
        for (var x = 0; x < map.SizeX; x++)
        for (var z = 0; z < map.SizeZ; z++)
            if (map[x, y, z])
                image.FillRect(x * scale, (map.SizeZ - 1 - z) * scale, scale, scale, ColorFor(z, map.SizeZ));
        return image;
    }
}

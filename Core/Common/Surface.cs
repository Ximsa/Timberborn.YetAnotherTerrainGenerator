namespace Timberborn.TerrainGenerator.Core.Common;

// The top surface of a voxel map, as a heightmap the terrain tools can work on. Writing a changed surface back only
// touches voxels above the lower of the old and new surface, so overhang undersides, gaps and floating terrain below
// the surface are kept.
public static class Surface
{
    // one above each column's topmost solid voxel (0 for an empty column), indexed y * SizeX + x
    public static int[] Heights(VoxelMap map)
    {
        var heights = new int[map.SizeX * map.SizeY];
        for (var y = 0; y < map.SizeY; y++)
        for (var x = 0; x < map.SizeX; x++)
        {
            var z = map.SizeZ;
            while (z > 0 && !map[x, y, z - 1])
                z--;
            heights[y * map.SizeX + x] = z;
        }

        return heights;
    }

    // a copy of map whose surface is lowered or raised to heights (clamped to at least 1 voxel and the map height)
    public static VoxelMap WithHeights(VoxelMap map, int[] heights)
    {
        if (heights.Length != map.SizeX * map.SizeY)
            throw new ArgumentException($"Expected {map.SizeX * map.SizeY} heights, got {heights.Length}");
        var result = new VoxelMap(map.SizeX, map.SizeY, map.SizeZ, (bool[])map.Voxels.Clone());
        var oldHeights = Heights(map);
        for (var y = 0; y < map.SizeY; y++)
        for (var x = 0; x < map.SizeX; x++)
        {
            var oldHeight = oldHeights[y * map.SizeX + x];
            var newHeight = Math.Max(1, Math.Min(map.SizeZ, heights[y * map.SizeX + x]));
            // raising fills air above the old surface; lowering removes everything down to the new surface
            for (var z = Math.Min(oldHeight, newHeight); z < Math.Max(oldHeight, newHeight); z++)
                result[x, y, z] = newHeight > oldHeight;
        }

        return result;
    }

    // columns (x, y) in which any voxel differs between the two maps
    public static List<(int X, int Y)> ChangedColumns(VoxelMap before, VoxelMap after)
    {
        var changed = new List<(int X, int Y)>();
        for (var y = 0; y < before.SizeY; y++)
        for (var x = 0; x < before.SizeX; x++)
        for (var z = 0; z < Math.Max(before.SizeZ, after.SizeZ); z++)
        {
            var wasSolid = z < before.SizeZ && before[x, y, z];
            var isSolid = z < after.SizeZ && after[x, y, z];
            if (wasSolid == isSolid) continue;
            changed.Add((x, y));
            break;
        }

        return changed;
    }
}

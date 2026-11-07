using NumSharp;

namespace Timberborn.TerrainGenerator.Core.Common;

// Terrain voxels of a whole map, independent of the map currently loaded in the game.
public sealed class VoxelMap
{
    public VoxelMap(int sizeX, int sizeY, int sizeZ, bool[] voxels)
    {
        if (voxels.Length != sizeX * sizeY * sizeZ)
            throw new ArgumentException(
                $"Expected {sizeX * sizeY * sizeZ} voxels for {sizeX}x{sizeY}x{sizeZ}, got {voxels.Length}");
        SizeX = sizeX;
        SizeY = sizeY;
        SizeZ = sizeZ;
        Voxels = voxels;
    }

    public int SizeX { get; }
    public int SizeY { get; }
    public int SizeZ { get; }

    // stored layer by layer from the bottom, x fastest, like the game's packed "Voxels" list
    public bool[] Voxels { get; }

    public bool this[int x, int y, int z]
    {
        get => Voxels[(z * SizeY + y) * SizeX + x];
        set => Voxels[(z * SizeY + y) * SizeX + x] = value;
    }

    public int CountSolid()
    {
        return Voxels.Count(voxel => voxel);
    }

    // terrain as returned by MapEditorService.GetTerrain: bools shaped (z, y, x)
    public static VoxelMap FromTerrain(NDArray terrain)
    {
        return new VoxelMap(terrain.Shape[2], terrain.Shape[1], terrain.Shape[0],
            terrain.flatten().ToArray<bool>());
    }

    // heightmap shaped (y, x); each column becomes solid from the bottom up to its height, at least 1 voxel
    public static VoxelMap FromHeightmap(NDArray heightmap, int sizeZ)
    {
        var sizeY = heightmap.Shape[0];
        var sizeX = heightmap.Shape[1];
        var heights = heightmap.astype(np.int32).flatten().ToArray<int>();
        var map = new VoxelMap(sizeX, sizeY, sizeZ, new bool[sizeX * sizeY * sizeZ]);
        for (var y = 0; y < sizeY; y++)
        for (var x = 0; x < sizeX; x++)
        {
            var height = Math.Max(1, Math.Min(sizeZ, heights[y * sizeX + x]));
            for (var z = 0; z < height; z++)
                map[x, y, z] = true;
        }

        return map;
    }
}

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

    // solid voxels per column, indexed y * SizeX + x
    public float[] SolidPerColumn()
    {
        var counts = new float[SizeX * SizeY];
        for (var z = 0; z < SizeZ; z++)
        for (var i = 0; i < counts.Length; i++)
            if (Voxels[z * counts.Length + i])
                counts[i]++;
        return counts;
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

    // heights indexed y * sizeX + x, cut to whole voxels; each column becomes solid from the bottom up to its height,
    // at least 1 voxel
    public static VoxelMap FromHeightmap(float[] heights, int sizeX, int sizeY, int sizeZ)
    {
        var map = new VoxelMap(sizeX, sizeY, sizeZ, new bool[sizeX * sizeY * sizeZ]);
        for (var y = 0; y < sizeY; y++)
        for (var x = 0; x < sizeX; x++)
        {
            var height = Math.Max(1, Math.Min(sizeZ, (int)heights[y * sizeX + x]));
            for (var z = 0; z < height; z++)
                map[x, y, z] = true;
        }

        return map;
    }
}

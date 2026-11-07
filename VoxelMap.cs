namespace Timberborn.TerrainGenerator;

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

    public bool this[int x, int y, int z] => Voxels[(z * SizeY + y) * SizeX + x];

    public int CountSolid()
    {
        return Voxels.Count(voxel => voxel);
    }
}

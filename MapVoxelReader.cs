using System.IO.Compression;
using Newtonsoft.Json;

namespace Timberborn.TerrainGenerator;

// Reads the terrain voxels from a .timber map file (a zip containing world.json), without loading the map
// into the game. Using the game's MapDeserializer instead would also replace the game's current world data.
public static class MapVoxelReader
{
    private const string WorldEntryName = "world.json";
    private const string SizeXPath = "Singletons.MapSize.Size.X";
    private const string SizeYPath = "Singletons.MapSize.Size.Y";
    private const string VoxelsPath = "Singletons.TerrainMap.Voxels.Array";

    public static VoxelMap Read(Stream timberFile)
    {
        using var archive = new ZipArchive(timberFile, ZipArchiveMode.Read, true);
        var entry = archive.GetEntry(WorldEntryName) ??
                    throw new InvalidDataException($"Map file has no {WorldEntryName}");
        using var reader = new JsonTextReader(new StreamReader(entry.Open()));

        int? sizeX = null;
        int? sizeY = null;
        string? packedVoxels = null;
        while (reader.Read())
            switch (reader.Path)
            {
                case SizeXPath when reader.TokenType == JsonToken.Integer:
                    sizeX = Convert.ToInt32(reader.Value);
                    break;
                case SizeYPath when reader.TokenType == JsonToken.Integer:
                    sizeY = Convert.ToInt32(reader.Value);
                    break;
                case VoxelsPath when reader.TokenType == JsonToken.String:
                    packedVoxels = (string)reader.Value!;
                    break;
            }

        if (sizeX == null || sizeY == null || packedVoxels == null)
            throw new InvalidDataException("Map file has no map size or terrain voxels");

        var voxels = ParsePackedBools(packedVoxels);
        var layerSize = sizeX.Value * sizeY.Value;
        if (voxels.Length % layerSize != 0)
            throw new InvalidDataException(
                $"{voxels.Length} voxels do not fill whole {sizeX}x{sizeY} layers");
        return new VoxelMap(sizeX.Value, sizeY.Value, voxels.Length / layerSize, voxels);
    }

    // the game packs bool lists as space-separated 0/1 values
    private static bool[] ParsePackedBools(string packed)
    {
        var count = 0;
        foreach (var character in packed)
            if (character != ' ')
                count++;

        var result = new bool[count];
        var index = 0;
        foreach (var character in packed)
            if (character != ' ')
                result[index++] = character == '1';
        return result;
    }
}

using NumSharp;
using Timberborn.BlockSystem;
using Timberborn.Common;
using Timberborn.EntitySystem;
using Timberborn.MapEditorPersistenceUI;
using Timberborn.MapEditorSceneLoading;
using Timberborn.MapIndexSystem;
using Timberborn.MapRepositorySystem;
using Timberborn.TerrainSystem;
using UnityEngine;

namespace Timberborn.TerrainGenerator;

public class MapEditorService
{
    private readonly BlockService blockService;
    private readonly EntityService entityService;
    private readonly MapEditorSceneLoader mapEditorSceneLoader;
    private readonly MapIndexService mapIndexService;
    private readonly MapPersistenceController mapPersistenceController;
    private readonly TerrainMap terrainMap;
    private readonly ITerrainService terrainService;

    public MapEditorService(BlockService blockService, EntityService entityService,
        MapEditorSceneLoader mapEditorSceneLoader, MapIndexService mapIndexService,
        MapPersistenceController mapPersistenceController, TerrainMap terrainMap, ITerrainService terrainService)
    {
        this.blockService = blockService;
        this.entityService = entityService;
        this.mapEditorSceneLoader = mapEditorSceneLoader;
        this.mapIndexService = mapIndexService;
        this.mapPersistenceController = mapPersistenceController;
        this.terrainMap = terrainMap;
        this.terrainService = terrainService;
    }

    public Vector3Int MapSize => terrainService.Size;

    public void RemoveAllEntityComponents()
    {
        var worldBlocks = blockService.GetFieldValue<Array3D<WorldBlock>>("_blocks");
        var worldBlocksArray = worldBlocks!.GetFieldValue<WorldBlock[,,]>("_values");
        foreach (var worldBlock in worldBlocksArray!)
        {
            var blockObjects = worldBlock.BlockObjects.Where(x => x.GetComponentFast<EntityComponent>() != null)
                .ToArray();
            foreach (var block in blockObjects) entityService.Delete(block);
        }
    }

    public NDArray GetTerrain()
    {
        var terrainSize = terrainService.Size;
        var rawTerrain = terrainMap.GetFieldValue<bool[]>("_terrainVoxels")!;
        return np
            .array(rawTerrain)
            .reshape(terrainSize.z, terrainSize.y + 2, terrainSize.x + 2)
            [$":,1:{terrainSize.y + 1},1:{terrainSize.x + 1}"];
    }

    public void SetTerrain(Vector3Int position, bool value)
    {
        var offset = mapIndexService.CoordinatesToIndex3D(position);
        var terrain = terrainMap.GetFieldValue<bool[]>("_terrainVoxels")!;
        terrain[offset] = value;
    }

    public void ClearTerrain()
    {
        var terrain = terrainMap.GetFieldValue<bool[]>("_terrainVoxels")!;
        for (var i = 0; i < terrain.Length; i++) terrain[i] = false;
    }

    public void Apply()
    {
        mapPersistenceController.Call("ForceSaveAs", "generated", () => { Debug.Log("generated map"); }, false);
        mapEditorSceneLoader.LoadMap(MapFileReference.FromUserFolder("generated"));
    }

    public void Set2DTerrain(NDArray heightmap)
    {
        heightmap = heightmap.astype(np.int32);
        for (var i = 0; i < MapSize.y; i++)
        for (var j = 0; j < MapSize.x; j++)
        {
            var height = terrainService.CellHeight(new Vector2Int(j, i));
            var targetHeight = (int)heightmap[i, j];
            if (height > targetHeight)
                terrainService.UnsetTerrain(new Vector3Int(j, i, height), height - targetHeight + 1);
            else
                terrainService.SetTerrain(new Vector3Int(j, i, height), targetHeight - height);
        }
    }
}
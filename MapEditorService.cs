using NumSharp;
using Timberborn.BlockSystem;
using Timberborn.Common;
using Timberborn.EntitySystem;
using Timberborn.MapEditorPersistenceUI;
using Timberborn.MapEditorSceneLoading;
using Timberborn.MapIndexSystem;
using Timberborn.MapRepositorySystem;
using Timberborn.TerrainSystem;
using Timberborn.UndoSystem;
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
    private readonly IUndoRegistry undoRegistry;

    public MapEditorService(BlockService blockService, EntityService entityService,
        MapEditorSceneLoader mapEditorSceneLoader, MapIndexService mapIndexService,
        MapPersistenceController mapPersistenceController, TerrainMap terrainMap, ITerrainService terrainService,
        IUndoRegistry undoRegistry)
    {
        this.undoRegistry = undoRegistry;
        this.blockService = blockService;
        this.entityService = entityService;
        this.mapEditorSceneLoader = mapEditorSceneLoader;
        this.mapIndexService = mapIndexService;
        this.mapPersistenceController = mapPersistenceController;
        this.terrainMap = terrainMap;
        this.terrainService = terrainService;
    }

    public Vector3Int MapSize => terrainService.Size;

    // the game records every terrain and entity change as a pending undo entry; committing closes the pending
    // entries into one step of the map editor's undo (Ctrl+Z), like the game's brushes do after each stroke
    public void CommitUndoStep()
    {
        undoRegistry.CommitStack();
    }

    public void RemoveAllEntityComponents()
    {
        RemoveEntities(_ => true);
    }

    // removes every entity on the map that matches the filter
    public void RemoveEntities(Func<BlockObject, bool> filter)
    {
        var worldBlocks = blockService.GetFieldValue<Array3D<WorldBlock>>("_blocks");
        var worldBlocksArray = worldBlocks!.GetFieldValue<WorldBlock[,,]>("_values");
        foreach (var worldBlock in worldBlocksArray!)
        {
            var blockObjects = worldBlock.BlockObjects.Where(x => x.HasComponent<EntityComponent>() && filter(x))
                .ToArray();
            foreach (var block in blockObjects) entityService.Delete(block);
        }
    }

    // removes every entity with a block in one of the columns, at any height
    public void RemoveEntitiesInColumns(IEnumerable<(int X, int Y)> columns)
    {
        var entities = new HashSet<BlockObject>();
        var sizeZ = blockService.Size.z;
        foreach (var (x, y) in columns)
        for (var z = 0; z < sizeZ; z++)
            foreach (var blockObject in blockService.GetObjectsAt(new Vector3Int(x, y, z)))
                if (blockObject.HasComponent<EntityComponent>())
                    entities.Add(blockObject);
        foreach (var entity in entities) entityService.Delete(entity);
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

    // makes the map's terrain match target, one map row per step, yielding the fraction of rows done so callers
    // can spread it over frames; only voxels that differ are written, through ITerrainService so the game redraws
    public IEnumerable<float> SetVoxelsByRow(VoxelMap target)
    {
        var size = terrainService.Size;
        if (target.SizeX != size.x || target.SizeY != size.y || target.SizeZ > size.z)
            throw new ArgumentException(
                $"Terrain of {target.SizeX}x{target.SizeY}x{target.SizeZ} does not fit the map ({size})");
        for (var y = 0; y < size.y; y++)
        {
            for (var x = 0; x < size.x; x++)
                SetColumn(target, x, y, size.z);
            yield return (y + 1f) / size.y;
        }
    }

    private void SetColumn(VoxelMap target, int x, int y, int sizeZ)
    {
        var z = 0;
        while (z < sizeZ)
        {
            var wanted = z < target.SizeZ && target[x, y, z];
            if (terrainMap.IsTerrainVoxel(new Vector3Int(x, y, z)) == wanted)
            {
                z++;
                continue;
            }

            // a run of voxels that all need the same change
            var start = z;
            while (z < sizeZ && (z < target.SizeZ && target[x, y, z]) == wanted &&
                   terrainMap.IsTerrainVoxel(new Vector3Int(x, y, z)) != wanted)
                z++;
            if (wanted)
                // SetTerrain fills upwards from the given voxel
                terrainService.SetTerrain(new Vector3Int(x, y, start), z - start);
            else
                // UnsetTerrain removes downwards from the given voxel
                terrainService.UnsetTerrain(new Vector3Int(x, y, z - 1), z - start);
        }
    }
}

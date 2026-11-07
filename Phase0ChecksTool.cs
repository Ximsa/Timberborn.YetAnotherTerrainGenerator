using System.Diagnostics;
using System.Text;
using NumSharp;
using Timberborn.MapRepositorySystem;
using Timberborn.TerrainSystem;
using Timberborn.ToolSystem;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Timberborn.TerrainGenerator;

// TEMPORARY (Phase 0 of docs/terrain-generators-plan.md): checks assumptions in game and logs the results.
// First use writes test shapes at the map corner; later uses (e.g. after saving and reloading) only log them.
public class Phase0ChecksTool : ITool
{
    private const string Log = "[Phase0]";
    private const int TestRow = 2;

    private static bool shapesWritten;

    private readonly MapEditorService mapEditorService;
    private readonly MapRepository mapRepository;
    private readonly TerrainMap terrainMap;
    private readonly ITerrainService terrainService;
    private readonly ToolService toolService;

    public Phase0ChecksTool(ITerrainService terrainService, TerrainMap terrainMap, MapRepository mapRepository,
        MapEditorService mapEditorService, ToolService toolService)
    {
        this.terrainService = terrainService;
        this.terrainMap = terrainMap;
        this.mapRepository = mapRepository;
        this.mapEditorService = mapEditorService;
        this.toolService = toolService;
    }

    public void Enter()
    {
        Debug.Log($"{Log} tool entered (check 3)");
        Run("current map", LogCurrentMap);
        Run("3D writes", CheckVoxelWrites);
        Run("other maps", CheckOtherMaps);
        toolService.SwitchToDefaultTool();
        Debug.Log($"{Log} switched back to default tool (check 3)");
    }

    public void Exit()
    {
    }

    private static void Run(string name, Action check)
    {
        try
        {
            check();
        }
        catch (Exception exception)
        {
            Debug.LogError($"{Log} {name} failed: {exception}");
        }
    }

    private void LogCurrentMap()
    {
        var terrain = mapEditorService.GetTerrain();
        var solid = (int)(float)terrain.astype(np.float32).sum();
        Debug.Log($"{Log} current map: size {terrainService.Size}, voxel grid {terrain.Shape}, solid {solid}");
    }

    private void CheckVoxelWrites()
    {
        if (!shapesWritten)
        {
            Debug.Log($"{Log} writing test shapes in row y={TestRow}, columns x=2..10");
            Write(2, "service SetTerrain floating run (z=top+3, height 2)",
                top => terrainService.SetTerrain(new Vector3Int(2, TestRow, top + 3), 2));
            Write(4, "service UnsetTerrain under the surface (z=1, height 1)",
                _ => terrainService.UnsetTerrain(new Vector3Int(4, TestRow, 1), 1));
            Write(6, "service SetTerrain on top (z=top, height 3), control",
                top => terrainService.SetTerrain(new Vector3Int(6, TestRow, top), 3));
            Write(8, "TerrainMap.SetTerrain single floating voxel (z=top+3)",
                top => terrainMap.SetTerrain(new Vector3Int(8, TestRow, top + 3)));
            Write(10, "service UnsetTerrain top voxel (z=top-1, height 1), control",
                top => terrainService.UnsetTerrain(new Vector3Int(10, TestRow, top - 1), 1));
            shapesWritten = true;
        }

        for (var x = 2; x <= 10; x += 2)
            Debug.Log($"{Log} column ({x},{TestRow}) now: {Column(x, TestRow)}");
    }

    private void Write(int x, string description, Action<int> write)
    {
        var before = Column(x, TestRow);
        try
        {
            write(Top(x, TestRow));
            Debug.Log($"{Log} ({x},{TestRow}) {description}\n  before: {before}\n  after:  {Column(x, TestRow)}");
        }
        catch (Exception exception)
        {
            Debug.LogError($"{Log} ({x},{TestRow}) {description} threw: {exception}");
        }
    }

    // column from bottom to top, 1 = terrain
    private string Column(int x, int y)
    {
        var builder = new StringBuilder();
        for (var z = 0; z < terrainService.Size.z; z++)
            builder.Append(terrainMap.IsTerrainVoxel(new Vector3Int(x, y, z)) ? '1' : '0');
        return builder.ToString();
    }

    private int Top(int x, int y)
    {
        for (var z = terrainService.Size.z - 1; z >= 0; z--)
            if (terrainMap.IsTerrainVoxel(new Vector3Int(x, y, z)))
                return z + 1;
        return 0;
    }

    private void CheckOtherMaps()
    {
        var builtinNames = mapRepository.GetBuiltinMapNames().ToList();
        var userNames = mapRepository.GetUserMapNames().ToList();
        Debug.Log($"{Log} built-in maps: {string.Join(", ", builtinNames)}");
        Debug.Log($"{Log} user maps: {string.Join(", ", userNames)}");
        foreach (var name in builtinNames)
            ReadMap($"built-in '{name}'", MapFileReference.FromResource(name));
        foreach (var name in userNames)
            ReadMap($"user '{name}'", MapFileReference.FromUserFolder(name));
    }

    private void ReadMap(string label, MapFileReference reference)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            using var stream = mapRepository.OpenMap(reference);
            var map = MapVoxelReader.Read(stream);
            Debug.Log($"{Log} {label}: {map.SizeX}x{map.SizeY}x{map.SizeZ}, solid {map.CountSolid()}, " +
                      $"read in {stopwatch.ElapsedMilliseconds} ms");
        }
        catch (Exception exception)
        {
            Debug.LogError($"{Log} {label} could not be read: {exception}");
        }
    }
}

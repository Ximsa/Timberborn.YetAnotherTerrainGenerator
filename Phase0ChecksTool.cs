using System.Diagnostics;
using System.Text;
using NumSharp;
using Timberborn.MapRepositorySystem;
using Timberborn.TerrainSystem;
using Timberborn.ToolSystem;
using Timberborn.ToolSystemUI;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Timberborn.TerrainGenerator;

// TEMPORARY (Phase 0 of docs/terrain-generators-plan.md): checks assumptions in game and logs the results.
// Every use first logs probe columns of the test shapes (to check they survived a save and reload),
// then (re)writes the test shapes around the map centre.
public class Phase0ChecksTool : ITool, IToolDescriptor
{
    private const string Log = "[Phase0]";

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

    public ToolDescription DescribeTool()
    {
        return new ToolDescription.Builder("Phase 0 checks (temporary)")
            .AddSection("Writes test shapes around the map centre and logs the results to Player.log.").Build();
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
        Debug.Log($"{Log} current map: size {terrainService.Size}, voxel grid {terrain.Shape}, " +
                  $"solid {terrain.astype(np.int32).sum()}");
    }

    private void CheckVoxelWrites()
    {
        var size = terrainService.Size;
        var cx = size.x / 2;
        var cy = size.y / 2;
        if (size.x < 20 || size.y < 16)
        {
            Debug.LogWarning($"{Log} map too small for the test shapes");
            return;
        }

        // probe columns, logged before writing so a second use after save and reload shows what survived
        var probes = new (string Name, int X, int Y)[]
        {
            ("A service platform centre", cx - 5, cy),
            ("B direct platform centre", cx + 5, cy),
            ("C arch opening centre", cx, cy + 6),
            ("C arch pillar", cx - 4, cy + 6)
        };
        foreach (var probe in probes)
            Debug.Log($"{Log} before: {probe.Name} ({probe.X},{probe.Y}): {Column(probe.X, probe.Y)}");

        // floating platforms 3 voxels above the highest ground in their area
        var platformZ = 0;
        for (var x = cx - 8; x <= cx + 8; x++)
        for (var y = cy - 3; y <= cy + 2; y++)
            platformZ = Math.Max(platformZ, GroundTop(x, y) + 3);
        platformZ = Math.Min(platformZ, size.z - 2);

        // A: through ITerrainService, which raises the game's terrain change events
        for (var x = cx - 8; x <= cx - 3; x++)
        for (var y = cy - 3; y <= cy + 2; y++)
            terrainService.SetTerrain(new Vector3Int(x, y, platformZ), 2);
        LogBox("A service floating platform (left of centre)", cx - 8, cx - 3, cy - 3, cy + 2, platformZ, 2);

        // B: directly through TerrainMap, voxel by voxel, bypassing the service
        for (var x = cx + 3; x <= cx + 8; x++)
        for (var y = cy - 3; y <= cy + 2; y++)
        {
            terrainMap.SetTerrain(new Vector3Int(x, y, platformZ));
            terrainMap.SetTerrain(new Vector3Int(x, y, platformZ + 1));
        }

        LogBox("B direct floating platform (right of centre)", cx + 3, cx + 8, cy - 3, cy + 2, platformZ, 2);

        // C: an arch through ITerrainService: a wall 4 high, then a 5 wide, 2 high opening at its bottom
        var archY = cy + 6;
        var groundTops = new Dictionary<int, int>();
        for (var x = cx - 5; x <= cx + 5; x++)
        {
            groundTops[x] = GroundTop(x, archY);
            terrainService.SetTerrain(new Vector3Int(x, archY, groundTops[x]), 4);
        }

        for (var x = cx - 2; x <= cx + 2; x++)
            terrainService.UnsetTerrain(new Vector3Int(x, archY, groundTops[x]), 2);
        Debug.Log($"{Log} C service arch (below centre) at x {cx - 5}..{cx + 5}, y {archY}, " +
                  $"opening x {cx - 2}..{cx + 2}");

        foreach (var probe in probes)
            Debug.Log($"{Log} after: {probe.Name} ({probe.X},{probe.Y}): {Column(probe.X, probe.Y)}");
    }

    private void LogBox(string name, int x0, int x1, int y0, int y1, int z0, int height)
    {
        var solid = 0;
        for (var x = x0; x <= x1; x++)
        for (var y = y0; y <= y1; y++)
        for (var z = z0; z < z0 + height; z++)
            if (terrainMap.IsTerrainVoxel(new Vector3Int(x, y, z)))
                solid++;
        var total = (x1 - x0 + 1) * (y1 - y0 + 1) * height;
        Debug.Log($"{Log} {name} at x {x0}..{x1}, y {y0}..{y1}, z {z0}..{z0 + height - 1}: {solid}/{total} solid");
    }

    // column from bottom to top, 1 = terrain
    private string Column(int x, int y)
    {
        var builder = new StringBuilder();
        for (var z = 0; z < terrainService.Size.z; z++)
            builder.Append(terrainMap.IsTerrainVoxel(new Vector3Int(x, y, z)) ? '1' : '0');
        return builder.ToString();
    }

    // height of the solid run starting at the bottom, ignoring anything floating above it
    private int GroundTop(int x, int y)
    {
        var z = 0;
        while (z < terrainService.Size.z && terrainMap.IsTerrainVoxel(new Vector3Int(x, y, z)))
            z++;
        return z;
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

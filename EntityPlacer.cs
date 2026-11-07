using Timberborn.BlockSystem;
using Timberborn.Coordinates;
using Timberborn.EntitySystem;
using Timberborn.Forestry;
using Timberborn.MapEditorNaturalResources;
using Timberborn.NaturalResources;
using Timberborn.TemplateSystem;
using Timberborn.TerrainGenerator.TerrainTools;
using Timberborn.WaterSourceSystem;
using Timberborn.WaterSystem;
using UnityEngine;

namespace Timberborn.TerrainGenerator;

// a tree or bush species: its template id and the localization key of its name
public readonly record struct Species(string Id, string NameLocKey);

// Spawns the terrain tools' trees, bushes and water sources through the same game services as the map editor's
// own brushes, and reads where water is.
public class EntityPlacer
{
    // share of mature bushes that start with their yield ready, like the editor brush's default
    private const float GatherablePercent = 0.5f;

    private readonly BlockObjectFactory blockObjectFactory;
    private readonly MapEditorService mapEditorService;
    private readonly NaturalResourceSpawner naturalResourceSpawner;
    private readonly TemplateService templateService;
    private readonly IThreadSafeWaterMap waterMap;

    public EntityPlacer(BlockObjectFactory blockObjectFactory, MapEditorService mapEditorService,
        NaturalResourceSpawner naturalResourceSpawner, TemplateService templateService, IThreadSafeWaterMap waterMap)
    {
        this.blockObjectFactory = blockObjectFactory;
        this.mapEditorService = mapEditorService;
        this.naturalResourceSpawner = naturalResourceSpawner;
        this.templateService = templateService;
        this.waterMap = waterMap;
    }

    // the species the editor's natural resource brush offers, in its order
    public IReadOnlyList<Species> TreeSpecies => SpeciesWith(spec => spec.HasSpec<TreeComponentSpec>());
    public IReadOnlyList<Species> BushSpecies => SpeciesWith(spec => spec.HasSpec<BushSpec>());

    // tiles with water at any height, indexed y * sizeX + x
    public bool[] WaterTiles()
    {
        var size = mapEditorService.MapSize;
        var water = new bool[size.x * size.y];
        for (var y = 0; y < size.y; y++)
        for (var x = 0; x < size.x; x++)
            water[y * size.x + x] = waterMap.IsWaterOnAnyHeight(new Vector2Int(x, y));
        return water;
    }

    public void RemoveAllNaturalResources()
    {
        mapEditorService.RemoveEntities(blockObject => blockObject.HasComponent<NaturalResource>());
    }

    // removes all natural resources, then spawns the plants a few at a time, yielding the fraction done
    public IEnumerable<float> ReplacePlants(IReadOnlyList<Plant> plants, IReadOnlyList<Species> trees,
        IReadOnlyList<Species> bushes)
    {
        RemoveAllNaturalResources();
        for (var i = 0; i < plants.Count; i++)
        {
            var plant = plants[i];
            var species = plant.Kind == PlantKind.Tree ? trees[plant.Species] : bushes[plant.Species];
            // skips tiles that are already occupied, like the brush
            naturalResourceSpawner.Spawn(new[] { new SpawnableResource(species.Id, false) },
                new Vector3Int(plant.X, plant.Y, plant.Z), GatherablePercent);
            if (i % 50 == 0)
                yield return (i + 1f) / plants.Count;
        }

        yield return 1;
    }

    // places the smallest clean water source with its corner on the head, standing on the surface
    public void PlaceWaterSource(RiverHead head, float strength)
    {
        var template = WaterSourceTemplate();
        var blockObject = blockObjectFactory.CreateFinished(new EntitySetup.Builder(template.Blueprint),
            new Placement(new Vector3Int(head.X, head.Y, head.Z)));
        blockObject.GetComponent<WaterSource>().SetSpecifiedStrength(strength);
    }

    private BlockObjectSpec WaterSourceTemplate()
    {
        var source = templateService.GetAll<WaterSourceSpec>()
            .Where(spec => !spec.HasSpec<WaterSourceRegulatorSpec>() && !spec.HasSpec<UndergroundWaterSourceSpec>())
            .Where(spec => !spec.HasSpec<WaterSourceContaminationSpec>() ||
                           spec.GetSpec<WaterSourceContaminationSpec>().DefaultContamination <= 0)
            .OrderBy(spec => spec.Coordinates.Length)
            .FirstOrDefault();
        if (source == null)
            throw new InvalidOperationException("The game has no clean water source template");
        return source.GetSpec<BlockObjectSpec>();
    }

    private IReadOnlyList<Species> SpeciesWith(Func<NaturalResourceSpec, bool> filter)
    {
        return templateService.GetAll<NaturalResourceSpec>()
            .Where(spec => spec.UsableWithCurrentFeatureToggles && filter(spec))
            .OrderBy(spec => spec.Order)
            .Select(spec => new Species(spec.GetSpec<TemplateSpec>().TemplateName,
                spec.GetSpec<LabeledEntitySpec>().DisplayNameLocKey))
            .ToList();
    }
}

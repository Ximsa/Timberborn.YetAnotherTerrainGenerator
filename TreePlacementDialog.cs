using Timberborn.Localization;
using Timberborn.TerrainGenerator.TerrainTools;
using TimberUi.CommonUi;
using UnityEngine.UIElements;
using Random = System.Random;

namespace Timberborn.TerrainGenerator;

public class TreePlacementDialog : TerrainJobDialog
{
    private const string TitleKey = "Ximsa.TerrainGenerator.Tool.TreePlacement";
    private const string ForestCoverageKey = "Ximsa.TerrainGenerator.TreePlacement.ForestCoverage";
    private const string BushCoverageKey = "Ximsa.TerrainGenerator.TreePlacement.BushCoverage";
    private const string PatchSizeKey = "Ximsa.TerrainGenerator.TreePlacement.PatchSize";
    private const string DensityKey = "Ximsa.TerrainGenerator.TreePlacement.Density";
    private const string TreeSpeciesKey = "Ximsa.TerrainGenerator.TreePlacement.TreeSpecies";
    private const string BushSpeciesKey = "Ximsa.TerrainGenerator.TreePlacement.BushSpecies";
    private const string SeedKey = "Ximsa.TerrainGenerator.Noise3D.Seed";
    private const string RandomSeedKey = "Ximsa.TerrainGenerator.Noise3D.RandomSeed";
    private const string ApplyKey = "Ximsa.TerrainGenerator.Apply";
    private const string ChoosingKey = "Ximsa.TerrainGenerator.Progress.ChoosingPlants";

    private static readonly Random SeedRandom = new();

    private readonly VisualElement applyButton;
    private readonly IReadOnlyList<Species> bushes;
    private readonly EntityPlacer entityPlacer;
    private readonly TextField seedField;
    private readonly TreePlacerSettings settings = new() { Seed = SeedRandom.Next(100000) };
    private readonly IReadOnlyList<Species> trees;

    public TreePlacementDialog(ILoc loc, MapEditorService mapEditorService, EntityPlacer entityPlacer) :
        base(loc, mapEditorService)
    {
        this.entityPlacer = entityPlacer;
        trees = entityPlacer.TreeSpecies;
        bushes = entityPlacer.BushSpecies;
        settings.TreeWeights = trees.Select(_ => 1.0).ToArray();
        settings.BushWeights = bushes.Select(_ => 1.0).ToArray();
        SetTitle(loc.T(TitleKey));
        Content.AddLabel(loc.T(SeedKey), SeedKey);
        seedField = Content.AddTextField(SeedKey, OnSeedChanged);
        seedField.SetValueWithoutNotify(settings.Seed.ToString());
        Content.AddButton(loc.T(RandomSeedKey), RandomSeedKey, () =>
        {
            settings.Seed = SeedRandom.Next(100000);
            seedField.SetValueWithoutNotify(settings.Seed.ToString());
        });
        AddPercentSlider(ForestCoverageKey, settings.ForestCoverage, value => settings.ForestCoverage = value);
        AddPercentSlider(BushCoverageKey, settings.BushCoverage, value => settings.BushCoverage = value);
        Content.Add(new GameSliderInt()
            .SetLabel(loc.T(PatchSizeKey))
            .SetHorizontalSlider(new SliderValues<int>(8, 96, settings.PatchSize))
            .RegisterChange(patchSize => settings.PatchSize = patchSize)
            .AddEndLabel(value => $"{value}"));
        AddPercentSlider(DensityKey, settings.Density, value => settings.Density = value);
        AddWeightSliders(TreeSpeciesKey, trees, settings.TreeWeights);
        AddWeightSliders(BushSpeciesKey, bushes, settings.BushWeights);
        applyButton = Content.AddButton(loc.T(ApplyKey), ApplyKey, OnApply);
        AddProgressBar();
        AddCloseButton();
    }

    private void AddPercentSlider(string labelKey, double value, Action<double> onChange)
    {
        Content.Add(new GameSlider()
            .SetLabel(loc.T(labelKey))
            .SetHorizontalSlider(new SliderValues<float>(0, 1, (float)value))
            .RegisterChange(changed => onChange(changed))
            .AddEndLabel(changed => $"{changed * 100:F0}%"));
    }

    // one slider per species for its share; 0 leaves the species out
    private void AddWeightSliders(string titleKey, IReadOnlyList<Species> species, double[] weights)
    {
        Content.AddLabel(loc.T(titleKey), titleKey);
        for (var i = 0; i < species.Count; i++)
        {
            var index = i;
            Content.Add(new GameSlider()
                .SetLabel(loc.T(species[i].NameLocKey))
                .SetHorizontalSlider(new SliderValues<float>(0, 1, (float)weights[i]))
                .RegisterChange(weight => weights[index] = weight)
                .AddEndLabel(value => $"{value * 100:F0}%"));
        }
    }

    private void OnSeedChanged(string text)
    {
        if (int.TryParse(text, out var seed))
            settings.Seed = seed;
    }

    private void OnApply()
    {
        // snapshot, so changing settings while the job runs does not affect it; water is read on the main thread
        var jobSettings = settings.Clone();
        var water = entityPlacer.WaterTiles();
        IReadOnlyList<Plant> plants = Array.Empty<Plant>();
        RunJob(applyButton, (terrain, mapSize, progress) =>
        {
            // the terrain stays as it is; only the plants are computed here
            var voxels = VoxelMap.FromTerrain(terrain);
            var placer = new TreePlacer(jobSettings, mapSize.x, mapSize.y);
            plants = progress.Stage(ChoosingKey, 0, 1, report => placer.Place(Surface.Heights(voxels), water, report));
            return voxels;
        }, removeAllEntities: false, placeEntities: () => entityPlacer.ReplacePlants(plants, trees, bushes));
    }
}

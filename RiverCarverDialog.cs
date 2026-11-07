using Timberborn.Localization;
using Timberborn.TerrainGenerator.TerrainTools;
using TimberUi.CommonUi;
using UnityEngine.UIElements;
using Random = System.Random;

namespace Timberborn.TerrainGenerator;

public class RiverCarverDialog : TerrainJobDialog
{
    private const string TitleKey = "Ximsa.TerrainGenerator.Tool.RiverCarver";
    private const string RiversKey = "Ximsa.TerrainGenerator.RiverCarver.Rivers";
    private const string WidthKey = "Ximsa.TerrainGenerator.RiverCarver.Width";
    private const string LakeSizeKey = "Ximsa.TerrainGenerator.RiverCarver.LakeSize";
    private const string SourceStrengthKey = "Ximsa.TerrainGenerator.RiverCarver.SourceStrength";
    private const string SeedKey = "Ximsa.TerrainGenerator.Noise3D.Seed";
    private const string RandomSeedKey = "Ximsa.TerrainGenerator.Noise3D.RandomSeed";
    private const string ApplyKey = "Ximsa.TerrainGenerator.Apply";
    private const string CarvingKey = "Ximsa.TerrainGenerator.Progress.Carving";

    private static readonly Random SeedRandom = new();

    private readonly VisualElement applyButton;
    private readonly EntityPlacer entityPlacer;
    private readonly TextField seedField;
    private readonly RiverCarverSettings settings = new() { Seed = SeedRandom.Next(100000) };

    public RiverCarverDialog(ILoc loc, MapEditorService mapEditorService, EntityPlacer entityPlacer) :
        base(loc, mapEditorService)
    {
        this.entityPlacer = entityPlacer;
        SetTitle(loc.T(TitleKey));
        Content.AddLabel(loc.T(SeedKey), SeedKey);
        seedField = Content.AddTextField(SeedKey, OnSeedChanged);
        seedField.SetValueWithoutNotify(settings.Seed.ToString());
        Content.AddButton(loc.T(RandomSeedKey), RandomSeedKey, () =>
        {
            settings.Seed = SeedRandom.Next(100000);
            seedField.SetValueWithoutNotify(settings.Seed.ToString());
        });
        Content.Add(new GameSliderInt()
            .SetLabel(loc.T(RiversKey))
            .SetHorizontalSlider(new SliderValues<int>(1, 10, settings.Rivers))
            .RegisterChange(rivers => settings.Rivers = rivers)
            .AddEndLabel(value => $"{value}"));
        Content.Add(new GameSliderInt()
            .SetLabel(loc.T(WidthKey))
            .SetHorizontalSlider(new SliderValues<int>(1, 7, settings.Width))
            .RegisterChange(width => settings.Width = width)
            .AddEndLabel(value => $"{value}"));
        Content.Add(new GameSliderInt()
            .SetLabel(loc.T(LakeSizeKey))
            .SetHorizontalSlider(new SliderValues<int>(20, 1000, settings.LakeSize))
            .RegisterChange(lakeSize => settings.LakeSize = lakeSize)
            .AddEndLabel(value => $"{value}"));
        Content.Add(new GameSlider()
            .SetLabel(loc.T(SourceStrengthKey))
            .SetHorizontalSlider(new SliderValues<float>(0.5f, 5, settings.SourceStrength))
            .RegisterChange(strength => settings.SourceStrength = strength)
            .AddEndLabel(value => $"{value:F1}"));
        applyButton = Content.AddButton(loc.T(ApplyKey), ApplyKey, OnApply);
        AddProgressBar();
        AddCloseButton();
    }

    private void OnSeedChanged(string text)
    {
        if (int.TryParse(text, out var seed))
            settings.Seed = seed;
    }

    private void OnApply()
    {
        // snapshot, so changing settings while the job runs does not affect it
        var jobSettings = settings.Clone();
        IReadOnlyList<RiverHead> heads = Array.Empty<RiverHead>();
        RunJob(applyButton, (terrain, mapSize, progress) =>
        {
            var voxels = VoxelMap.FromTerrain(terrain);
            var carver = new RiverCarver(jobSettings, mapSize.x, mapSize.y);
            var heights = progress.Stage(CarvingKey, 0, 1, report => carver.Carve(Surface.Heights(voxels), report));
            heads = carver.Heads;
            return Surface.WithHeights(voxels, heights);
        }, removeAllEntities: false, placeEntities: () => PlaceWaterSources(heads, jobSettings.SourceStrength));
    }

    private IEnumerable<float> PlaceWaterSources(IReadOnlyList<RiverHead> heads, float strength)
    {
        for (var i = 0; i < heads.Count; i++)
        {
            entityPlacer.PlaceWaterSource(heads[i], strength);
            yield return (i + 1f) / heads.Count;
        }
    }
}

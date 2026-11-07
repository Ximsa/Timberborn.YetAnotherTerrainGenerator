using Timberborn.Localization;
using Timberborn.TerrainGenerator.Generators.Noise3D;
using TimberUi.CommonUi;
using UnityEngine.UIElements;
using Random = System.Random;

namespace Timberborn.TerrainGenerator;

public class Noise3DGeneratorDialog : TerrainJobDialog
{
    private const string TitleKey = "Ximsa.TerrainGenerator.Tool.Noise3D";
    private const string SeedKey = "Ximsa.TerrainGenerator.Noise3D.Seed";
    private const string RandomSeedKey = "Ximsa.TerrainGenerator.Noise3D.RandomSeed";
    private const string BaseLevelKey = "Ximsa.TerrainGenerator.Noise3D.BaseLevel";
    private const string VerticalScaleKey = "Ximsa.TerrainGenerator.Noise3D.VerticalScale";
    private const string HorizontalScaleKey = "Ximsa.TerrainGenerator.Noise3D.HorizontalScale";
    private const string VaryingBiomesKey = "Ximsa.TerrainGenerator.Noise3D.VaryingBiomes";
    private const string GenerateKey = "Ximsa.TerrainGenerator.Generate";
    private const string ClimateKey = "Ximsa.TerrainGenerator.Progress.Climate";
    private const string NoiseKey = "Ximsa.TerrainGenerator.Progress.Noise";
    private const string ShapingKey = "Ximsa.TerrainGenerator.Progress.Shaping";

    private static readonly Random SeedRandom = new();

    private readonly VisualElement generateButton;
    private readonly TextField seedField;
    private readonly Noise3DSettings settings = new() { Seed = SeedRandom.Next(100000) };

    public Noise3DGeneratorDialog(ILoc loc, MapEditorService mapEditorService) : base(loc, mapEditorService)
    {
        SetTitle(loc.T(TitleKey));
        Content.AddLabel(loc.T(SeedKey), SeedKey);
        seedField = Content.AddTextField(SeedKey, OnSeedChanged);
        seedField.SetValueWithoutNotify(settings.Seed.ToString());
        Content.AddButton(loc.T(RandomSeedKey), RandomSeedKey, () =>
        {
            settings.Seed = SeedRandom.Next(100000);
            seedField.SetValueWithoutNotify(settings.Seed.ToString());
        });
        Content.Add(new GameSlider()
            .SetLabel(loc.T(BaseLevelKey))
            .SetHorizontalSlider(new SliderValues<float>(32, 80, (float)settings.BaseLevel))
            .RegisterChange(baseLevel => settings.BaseLevel = baseLevel)
            .AddEndLabel(value => $"{value:F0}"));
        Content.Add(new GameSlider()
            .SetLabel(loc.T(VerticalScaleKey))
            .SetHorizontalSlider(new SliderValues<float>(1, 6, (float)settings.VerticalScale))
            .RegisterChange(verticalScale => settings.VerticalScale = verticalScale)
            .AddEndLabel(value => $"{value:F2}"));
        settings.HorizontalScale = settings.VerticalScale;
        Content.Add(new GameSlider()
            .SetLabel(loc.T(HorizontalScaleKey))
            .SetHorizontalSlider(new SliderValues<float>(1, 6, (float)settings.HorizontalScale))
            .RegisterChange(horizontalScale => settings.HorizontalScale = horizontalScale)
            .AddEndLabel(value => $"{value:F2}"));
        Content.AddGamePanelToggle(loc.T(VaryingBiomesKey), varyingBiomes =>
            settings.Climate = varyingBiomes ? Noise3DClimate.VaryingBiomes : Noise3DClimate.Rainforest);
        generateButton = Content.AddButton(loc.T(GenerateKey), GenerateKey, OnGenerate);
        AddProgressBar();
        AddCloseButton();
    }

    private void OnSeedChanged(string text)
    {
        if (int.TryParse(text, out var seed))
            settings.Seed = seed;
    }

    private void OnGenerate()
    {
        // snapshot, so changing settings while the job runs does not affect it
        var jobSettings = settings.Clone();
        RunJob(generateButton, (_, mapSize, progress) =>
        {
            var generator = new Noise3DTerrainGenerator(jobSettings, mapSize.x, mapSize.y, mapSize.z);
            progress.Stage(ClimateKey, 0f, 0.02f, _ =>
            {
                generator.ComputeClimate();
                return true;
            });
            progress.Stage(NoiseKey, 0.02f, 0.9f, report =>
            {
                generator.ComputeDensity(report);
                return true;
            });
            return progress.Stage(ShapingKey, 0.9f, 1f, report => generator.BuildVoxels(report));
        });
    }
}

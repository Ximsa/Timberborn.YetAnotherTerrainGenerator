using Timberborn.Localization;
using Timberborn.TerrainGenerator.Core.Noise3D;
using Timberborn.TerrainGenerator.Game.Common;
using TimberUi.CommonUi;
using UnityEngine.UIElements;

namespace Timberborn.TerrainGenerator.Game.Noise3D;

public class Noise3DGeneratorDialog : TerrainJobDialog
{
    private const string TitleKey = "Ximsa.TerrainGenerator.Tool.Noise3D";
    private const string RuggednessKey = "Ximsa.TerrainGenerator.Noise3D.Ruggedness";
    private const string NormalizeKey = "Ximsa.TerrainGenerator.Normalize";
    private const string HillinessKey = "Ximsa.TerrainGenerator.Noise3D.Hilliness";
    private const string BaseLevelKey = "Ximsa.TerrainGenerator.Noise3D.BaseLevel";
    private const string VerticalScaleKey = "Ximsa.TerrainGenerator.Noise3D.VerticalScale";
    private const string HorizontalScaleKey = "Ximsa.TerrainGenerator.Noise3D.HorizontalScale";
    private const string GenerateKey = "Ximsa.TerrainGenerator.Generate";
    private const string NoiseKey = "Ximsa.TerrainGenerator.Progress.Noise";
    private const string ShapingKey = "Ximsa.TerrainGenerator.Progress.Shaping";

    private readonly VisualElement generateButton;
    private readonly Noise3DSettings settings = new();

    public Noise3DGeneratorDialog(ILoc loc, MapEditorService mapEditorService) : base(loc, mapEditorService)
    {
        SetTitle(loc.T(TitleKey));
        Content.Add(WithTooltip(HillinessKey, new GameSlider()
            .SetLabel(loc.T(HillinessKey))
            .SetHorizontalSlider(new SliderValues<float>(0, 1, (float)settings.Hilliness))
            .RegisterChange(hilliness => settings.Hilliness = hilliness)
            .AddEndLabel(value => $"{value:F2}")));
        Content.Add(WithTooltip(RuggednessKey, new GameSlider()
            .SetLabel(loc.T(RuggednessKey))
            .SetHorizontalSlider(new SliderValues<float>(0, 1, (float)settings.Ruggedness))
            .RegisterChange(ruggedness => settings.Ruggedness = ruggedness)
            .AddEndLabel(value => $"{value:F2}")));
        Content.AddGamePanelToggle(loc.T(NormalizeKey), normalize => settings.Normalize = normalize)
            .SetValueWithoutNotify(settings.Normalize);
        Content.Add(WithTooltip(BaseLevelKey, new GameSlider()
            .SetLabel(loc.T(BaseLevelKey))
            .SetHorizontalSlider(new SliderValues<float>(32, 80, (float)settings.BaseLevel))
            .RegisterChange(baseLevel => settings.BaseLevel = baseLevel)
            .AddEndLabel(value => $"{value:F0}")));
        Content.Add(WithTooltip(VerticalScaleKey, new GameSlider()
            .SetLabel(loc.T(VerticalScaleKey))
            .SetHorizontalSlider(new SliderValues<float>(1, 6, (float)settings.VerticalScale))
            .RegisterChange(verticalScale => settings.VerticalScale = verticalScale)
            .AddEndLabel(value => $"{value:F2}")));
        settings.HorizontalScale = settings.VerticalScale;
        Content.Add(WithTooltip(HorizontalScaleKey, new GameSlider()
            .SetLabel(loc.T(HorizontalScaleKey))
            .SetHorizontalSlider(new SliderValues<float>(1, 6, (float)settings.HorizontalScale))
            .RegisterChange(horizontalScale => settings.HorizontalScale = horizontalScale)
            .AddEndLabel(value => $"{value:F2}")));
        generateButton = Content.AddButton(loc.T(GenerateKey), GenerateKey, OnGenerate);
        AddProgressBar();
        AddCloseButton();
    }

    private void OnGenerate()
    {
        // snapshot, so changing settings while the job runs does not affect it; every run gets a new seed
        var jobSettings = settings.Clone();
        jobSettings.Seed = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        RunJob(generateButton, (_, mapSize, progress) =>
        {
            var generator = new Noise3DTerrainGenerator(jobSettings, mapSize.x, mapSize.y, mapSize.z);
            progress.Stage(NoiseKey, 0f, 0.9f, report =>
            {
                generator.ComputeDensity(report);
                return true;
            });
            return progress.Stage(ShapingKey, 0.9f, 1f, report => generator.BuildVoxels(report));
        });
    }
}

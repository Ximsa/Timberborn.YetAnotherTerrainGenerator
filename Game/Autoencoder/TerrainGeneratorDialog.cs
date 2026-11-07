using NumSharp;
using Timberborn.Localization;
using Timberborn.TerrainGenerator.Core.Autoencoder;
using Timberborn.TerrainGenerator.Core.Common;
using Timberborn.TerrainGenerator.Game.Common;
using TimberUi.CommonUi;
using UnityEngine.UIElements;
using Random = System.Random;

namespace Timberborn.TerrainGenerator.Game.Autoencoder;

public class TerrainGeneratorDialog : TerrainJobDialog
{
    private const string TitleKey = "Ximsa.TerrainGenerator.Title";
    private const string EncoderKey = "Ximsa.TerrainGenerator.Encoder";
    private const string VarietyKey = "Ximsa.TerrainGenerator.Variety";
    private const string GenerateKey = "Ximsa.TerrainGenerator.Generate";
    private const string ZoomFactorKey = "Ximsa.TerrainGenerator.ZoomFactor";
    private const string MaxHeightKey = "Ximsa.TerrainGenerator.MaxHeight";
    private const string NormalizeKey = "Ximsa.TerrainGenerator.Normalize";

    private readonly VisualElement generateButton;
    private readonly AutoencoderSettings settings = new();
    private int maxTerrainHeight;

    public TerrainGeneratorDialog(
        ILoc loc,
        MapEditorService mapEditorService) : base(loc, mapEditorService)
    {
        SetTitle(loc.T(TitleKey));
        Content.Add(new GameSlider()
            .SetLabel($"{loc.T(EncoderKey)}")
            .SetHorizontalSlider(new SliderValues<float>(0, 1, settings.KeepCurrent))
            .RegisterChange(keepCurrent => settings.KeepCurrent = keepCurrent)
            .AddEndLabel(value => $"{value * 100:F0}%"));
        Content.Add(new GameSlider()
            .SetLabel($"{loc.T(VarietyKey)}")
            .SetHorizontalSlider(new SliderValues<float>(0, 3, settings.Variety))
            .RegisterChange(variety => settings.Variety = variety)
            .AddEndLabel(value => $"{value:F2}"));
        Content.Add(new GameSlider()
            .SetLabel($"{loc.T(ZoomFactorKey)}")
            .SetHorizontalSlider(new SliderValues<float>(1, 8, settings.ZoomFactor))
            .RegisterChange(zoomFactor => settings.ZoomFactor = zoomFactor)
            .AddEndLabel(value => $"{value:G3}"));
        Content.AddGamePanelToggle(loc.T(NormalizeKey), normalize => settings.Normalize = normalize)
            .SetValueWithoutNotify(settings.Normalize);
        // the height normalizing stretches to
        maxTerrainHeight = mapEditorService.MapSize.z;
        Content.Add(new GameSliderInt()
            .SetLabel($"{loc.T(MaxHeightKey)}")
            .SetHorizontalSlider(new SliderValues<int>(1, mapEditorService.MapSize.z, mapEditorService.MapSize.z))
            .RegisterChange(maxHeight => maxTerrainHeight = maxHeight)
            .AddEndLabel(value => $"{value}"));
        generateButton = Content.AddButton(loc.T(GenerateKey), GenerateKey, OnGenerate);
        AddProgressBar();
        AddCloseButton();
    }

    private void OnGenerate()
    {
        // snapshot, so moving sliders while the job runs does not affect it
        var jobSettings = settings.Clone();
        var maxHeight = maxTerrainHeight;
        RunJob(generateButton, (terrain, mapSize, progress) =>
        {
            var generator = progress.Stage(LoadingModelsKey, 0f, 0.05f,
                _ => AutoencoderGenerator.Load(Path.Combine(ModStarter.OriginPath, "Models")));
            var random = new Random();
            // the current map as solid voxels per column
            var current = terrain.astype(np.float32).sum(0).flatten().ToArray<float>();
            var decoded = progress.Stage(DecodingKey, 0.05f, 0.15f, _ =>
            {
                var latent = generator.Sample(generator.Encode(current, mapSize.x, mapSize.y), jobSettings, random);
                return generator.Decode(latent);
            });
            var heights = progress.Stage(ScalingKey, 0.15f, 1f,
                report => generator.Finish(decoded, mapSize.x, mapSize.y, jobSettings, random, report));
            // normalized: from the bottom layer (the only full one) to the maximum height; otherwise on the current
            // map's scale, where 1 is its highest column
            var columns = jobSettings.Normalize
                ? heights.Select(height => 1 + height * (maxHeight - 1f))
                : heights.Select(height => height * current.Max());
            return VoxelMap.FromHeightmap(columns.ToArray(), mapSize.x, mapSize.y, mapSize.z);
        });
    }
}

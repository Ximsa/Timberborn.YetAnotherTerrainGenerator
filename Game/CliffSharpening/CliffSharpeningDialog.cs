using Timberborn.Localization;
using Timberborn.TerrainGenerator.Core.CliffSharpening;
using Timberborn.TerrainGenerator.Core.Common;
using Timberborn.TerrainGenerator.Game.Common;
using TimberUi.CommonUi;
using UnityEngine.UIElements;

namespace Timberborn.TerrainGenerator.Game.CliffSharpening;

public class CliffSharpeningDialog : TerrainJobDialog
{
    private const string TitleKey = "Ximsa.TerrainGenerator.Tool.CliffSharpening";
    private const string RegionSizeKey = "Ximsa.TerrainGenerator.CliffSharpening.RegionSize";
    private const string MinRegionAreaKey = "Ximsa.TerrainGenerator.CliffSharpening.MinRegionArea";
    private const string StepSizeKey = "Ximsa.TerrainGenerator.CliffSharpening.StepSize";
    private const string StrengthKey = "Ximsa.TerrainGenerator.CliffSharpening.Strength";
    private const string ApplyKey = "Ximsa.TerrainGenerator.Apply";
    private const string SharpeningKey = "Ximsa.TerrainGenerator.Progress.Sharpening";

    private readonly VisualElement applyButton;
    private readonly CliffSharpenerSettings settings = new();

    public CliffSharpeningDialog(ILoc loc, MapEditorService mapEditorService) : base(loc, mapEditorService)
    {
        SetTitle(loc.T(TitleKey));
        Content.Add(new GameSliderInt()
            .SetLabel(loc.T(RegionSizeKey))
            .SetHorizontalSlider(new SliderValues<int>(2, 8, settings.RegionSize))
            .RegisterChange(regionSize => settings.RegionSize = regionSize)
            .AddEndLabel(value => $"{value}"));
        Content.Add(new GameSliderInt()
            .SetLabel(loc.T(MinRegionAreaKey))
            .SetHorizontalSlider(new SliderValues<int>(16, 128, settings.MinRegionArea))
            .RegisterChange(minRegionArea => settings.MinRegionArea = minRegionArea)
            .AddEndLabel(value => $"{value}"));
        Content.Add(new GameSliderInt()
            .SetLabel(loc.T(StepSizeKey))
            .SetHorizontalSlider(new SliderValues<int>(1, 6, settings.StepSize))
            .RegisterChange(stepSize => settings.StepSize = stepSize)
            .AddEndLabel(value => $"{value}"));
        Content.Add(new GameSlider()
            .SetLabel(loc.T(StrengthKey))
            .SetHorizontalSlider(new SliderValues<float>(0, 1, (float)settings.Strength))
            .RegisterChange(strength => settings.Strength = strength)
            .AddEndLabel(value => $"{value * 100:F0}%"));
        applyButton = Content.AddButton(loc.T(ApplyKey), ApplyKey, OnApply);
        AddProgressBar();
        AddCloseButton();
    }

    private void OnApply()
    {
        // snapshot, so changing settings while the job runs does not affect it
        var jobSettings = new CliffSharpenerSettings
        {
            RegionSize = settings.RegionSize, MinRegionArea = settings.MinRegionArea,
            StepSize = settings.StepSize, Strength = settings.Strength
        };
        RunJob(applyButton, (terrain, mapSize, progress) =>
        {
            var voxels = VoxelMap.FromTerrain(terrain);
            var sharpener = new CliffSharpener(jobSettings, mapSize.x, mapSize.y);
            var heights = progress.Stage(SharpeningKey, 0, 1,
                report => sharpener.Sharpen(Surface.Heights(voxels), report));
            return Surface.WithHeights(voxels, heights);
        }, removeAllEntities: false);
    }
}

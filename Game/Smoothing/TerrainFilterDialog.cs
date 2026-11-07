using NumSharp;
using Timberborn.Localization;
using Timberborn.TerrainGenerator.Core.Common;
using Timberborn.TerrainGenerator.Core.Smoothing;
using Timberborn.TerrainGenerator.Game.Common;
using TimberUi.CommonUi;
using UnityEngine.UIElements;

namespace Timberborn.TerrainGenerator.Game.Smoothing;

public class TerrainFilterDialog : TerrainJobDialog
{
    private const string FilterTitleKey = "Ximsa.TerrainGenerator.FilterTitle";
    private const string PassesKey = "Ximsa.TerrainGenerator.Passes";
    private const string RadiusKey = "Ximsa.TerrainGenerator.Radius";
    private const string ApplyKey = "Ximsa.TerrainGenerator.Apply";

    private readonly VisualElement applyButton;

    private int passes = 1;
    private int radius = 4;

    public TerrainFilterDialog(
        ILoc loc,
        MapEditorService mapEditorService) : base(loc, mapEditorService)
    {
        SetTitle(loc.T(FilterTitleKey));
        Content.Add(new GameSliderInt()
            .SetLabel($"{loc.T(PassesKey)}")
            .SetHorizontalSlider(new SliderValues<int>(0, 12, passes))
            .RegisterChange(passes => this.passes = passes)
            .AddEndLabel(value => $"{value}"));
        Content.Add(new GameSliderInt()
            .SetLabel($"{loc.T(RadiusKey)}")
            .SetHorizontalSlider(new SliderValues<int>(1, 12, radius))
            .RegisterChange(radius => this.radius = radius)
            .AddEndLabel(value => $"{value}"));
        applyButton = Content.AddButton(loc.T(ApplyKey), ApplyKey, OnApply);
        AddProgressBar();
        AddCloseButton();
    }

    private void OnApply()
    {
        var passes = this.passes;
        var radius = this.radius;
        RunJob(applyButton, (terrain, mapSize, progress) =>
        {
            var voxels = VoxelMap.FromTerrain(terrain);
            var surface = np.array(Surface.Heights(voxels)).reshape(mapSize.y, mapSize.x).astype(np.float32);
            float originalMin = surface.min();
            float originalMax = surface.max();
            surface = progress.Stage(FilteringKey, 0, 1, report => surface.MedianBlur(radius, passes, report));
            // stretch the smoothed surface back to the original height range
            float smoothedMin = surface.min();
            var smoothedRange = Math.Max(1e-6f, (float)surface.max() - smoothedMin);
            surface = (surface - smoothedMin) / smoothedRange * (originalMax - originalMin) + originalMin;
            var heights = np.round_(surface).astype(np.int32).flatten().ToArray<int>();
            return Surface.WithHeights(voxels, heights);
        }, removeAllEntities: false);
    }
}

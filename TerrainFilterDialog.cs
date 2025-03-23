using NumSharp;
using Timberborn.Localization;
using TimberUi.CommonUi;
using UiBuilder.CommonUi;
using UnityEngine.UIElements;

namespace Timberborn.TerrainGenerator;

public class TerrainFilterDialog : DialogBoxElement
{
    private const string MedianBlurTitleKey = "Ximsa.TerrainGenerator.MedianBlurTitle";
    private const string MedianBlurPassesKey = "Ximsa.TerrainGenerator.MedianBlurPasses";
    private const string MedianBlurRadiusKey = "Ximsa.TerrainGenerator.MedianBlurRadius";
    private const string ApplyKey = "Ximsa.TerrainGenerator.Apply";

    private readonly MapEditorService mapEditorService;
    private int medianBlurPasses = 1;
    private int medianBlurRadius = 4;

    public TerrainFilterDialog(
        ILoc loc,
        MapEditorService mapEditorService)
    {
        this.mapEditorService = mapEditorService;
        SetTitle(loc.T(MedianBlurTitleKey));
        Content.Add(new GameSliderInt()
            .SetLabel($"{loc.T(MedianBlurPassesKey)}")
            .SetHorizontalSlider(new SliderValues<int>(0, 12, medianBlurPasses))
            .RegisterChange(medianBlurPasses => this.medianBlurPasses = medianBlurPasses)
            .AddEndLabel(value => $"{value}"));
        Content.Add(new GameSliderInt()
            .SetLabel($"{loc.T(MedianBlurRadiusKey)}")
            .SetHorizontalSlider(new SliderValues<int>(1, 12, medianBlurRadius))
            .RegisterChange(medianBlurRadius => this.medianBlurRadius = medianBlurRadius)
            .AddEndLabel(value => $"{value}"));
        Content.AddButton(loc.T(ApplyKey), ApplyKey, OnApply);
        AddCloseButton();
    }

    private void OnApply()
    {
        mapEditorService.RemoveAllEntityComponents();
        var terrain = mapEditorService.GetTerrain();
        terrain = terrain.astype(np.float32).sum(0).MedianBlur(medianBlurRadius, medianBlurPasses);
        mapEditorService.Set2DTerrain(terrain);
    }
}
using NumSharp;
using Timberborn.Localization;
using TimberUi.CommonUi;
using UnityEngine.UIElements;

namespace Timberborn.TerrainGenerator;

public class TerrainFilterDialog : DialogBoxElement
{
    private const string FilterTitleKey = "Ximsa.TerrainGenerator.FilterTitle";
    private const string PassesKey = "Ximsa.TerrainGenerator.Passes";
    private const string RadiusKey = "Ximsa.TerrainGenerator.Radius";
    private const string ApplyKey = "Ximsa.TerrainGenerator.Apply";

    private readonly MapEditorService mapEditorService;

    private int passes = 1;
    private int radius = 4;

    public TerrainFilterDialog(
        ILoc loc,
        MapEditorService mapEditorService)
    {
        this.mapEditorService = mapEditorService;
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
        Content.AddButton(loc.T(ApplyKey), ApplyKey, OnApply);
        AddCloseButton();
    }

    private void OnApply()
    {
        mapEditorService.RemoveAllEntityComponents();
        var terrain = mapEditorService.GetTerrain().astype(np.float32).sum(0);
        var originalMax = terrain.max();
        var originalMin = terrain.min();
        terrain = terrain.MedianBlur(radius, passes);
        terrain -= terrain.min() + originalMin;
        terrain = terrain / terrain.max() * originalMax;
        mapEditorService.Set2DTerrain(terrain);
    }
}
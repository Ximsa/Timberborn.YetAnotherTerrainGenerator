using NumSharp;
using Timberborn.Localization;
using TimberUi.CommonUi;
using UiBuilder.CommonUi;
using UnityEngine;
using UnityEngine.UIElements;

namespace Timberborn.TerrainGenerator;

public class TerrainFilterDialog : DialogBoxElement
{
    private const string MedianBlurTitleKey = "Ximsa.TerrainGenerator.MedianBlurTitle";
    private const string MedianBlurPassesKey = "Ximsa.TerrainGenerator.MedianBlurPasses";
    private const string MedianBlurRadiusKey = "Ximsa.TerrainGenerator.MedianBlurRadius";
    private const string ApplyKey = "Ximsa.TerrainGenerator.Apply";

    private readonly MapEditorService mapEditorService;
    private int medianBlurRadius = 1;
    private int medianBlurPasses = 1;

    public TerrainFilterDialog(
        ILoc loc,
        MapEditorService mapEditorService)
    {
        this.mapEditorService = mapEditorService;
        SetTitle(loc.T(MedianBlurTitleKey));
        Content.Add(new GameSliderInt()
            .SetLabel($"{loc.T(MedianBlurPassesKey)} {0}-{12}")
            .SetHorizontalSlider(new SliderValues<int>(0, 12, medianBlurPasses))
            .RegisterChange(medianBlurPasses => this.medianBlurPasses = medianBlurPasses));
        Content.Add(new GameSliderInt()
            .SetLabel($"{loc.T(MedianBlurRadiusKey)} {1}-{12}")
            .SetHorizontalSlider(new SliderValues<int>(1, 12, medianBlurRadius))
            .RegisterChange(medianBlurRadius => this.medianBlurRadius = medianBlurRadius));
        Content.AddButton(loc.T(ApplyKey), ApplyKey, OnApply);
        AddCloseButton();
    }

    private void OnApply()
    {
        mapEditorService.RemoveAllEntityComponents();
        var terrain = mapEditorService.GetTerrain();
        this.mapEditorService.ClearTerrain();
        terrain = terrain.astype(np.float32).sum(0).MedianBlur(this.medianBlurRadius, this.medianBlurPasses);
        for (var x = 0; x < mapEditorService.MapSize.x; x++)
        for (var y = 0; y < mapEditorService.MapSize.y; y++)
        {
            var terrainHeight = (float)terrain[y, x].astype(np.float32);
            for (var z = 0; z < terrainHeight; z++)
                mapEditorService.SetTerrain(new Vector3Int(x, y, z), true);
        }

        this.mapEditorService.Apply();
    }
}
using Timberborn.AssetSystem;
using Timberborn.CoreUI;
using Timberborn.Localization;
using TimberUi.CommonUi;

namespace Timberborn.TerrainGenerator;

public class TerrainFilterButton : GrouplessBottomBarButton
{
    public TerrainFilterButton(
        VisualElementInitializer visualElementInitializer,
        PanelStack panelStack,
        VisualElementLoader visualElementLoader,
        IAssetLoader assetLoader,
        MapEditorService mapEditorService,
        ILoc loc
    ) :
        base(visualElementLoader, assetLoader)
    {
        SpritePath = "Sprites/terrain-smoothing-button";
        Click = () =>
            new TerrainFilterDialog(loc, mapEditorService).Show(
                visualElementInitializer,
                panelStack);
        BottomText = "Global Terrain Filter";
    }
}
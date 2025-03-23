using Timberborn.AssetSystem;
using Timberborn.CoreUI;
using Timberborn.Localization;
using TimberUi.CommonUi;

namespace Timberborn.TerrainGenerator;

public class TerrainGeneratorButton : GrouplessBottomBarButton
{
    public TerrainGeneratorButton(
        VisualElementInitializer visualElementInitializer,
        PanelStack panelStack,
        VisualElementLoader visualElementLoader,
        IAssetLoader assetLoader,
        MapEditorService mapEditorService,
        ILoc loc
    ) :
        base(visualElementLoader, assetLoader)
    {
        SpritePath = "Sprites/terrain-generator-button";
        Click = () =>
            new TerrainGeneratorDialog(loc, mapEditorService).Show(
                visualElementInitializer,
                panelStack);
        BottomText = "Terrain Generator";
    }
}
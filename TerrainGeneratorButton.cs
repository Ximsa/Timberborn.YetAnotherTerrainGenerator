using Timberborn.AssetSystem;
using Timberborn.BlockSystem;
using Timberborn.CoreUI;
using Timberborn.EntitySystem;
using Timberborn.Localization;
using Timberborn.MapEditorPersistenceUI;
using Timberborn.MapEditorSceneLoading;
using Timberborn.MapIndexSystem;
using Timberborn.TerrainSystem;
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
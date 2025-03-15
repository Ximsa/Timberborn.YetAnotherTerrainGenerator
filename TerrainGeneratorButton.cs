using Timberborn.AssetSystem;
using Timberborn.CoreUI;
using Timberborn.Debugging;
using TimberUi.CommonUi;

namespace Timberborn.TerrainGenerator
{
    public class TerrainGeneratorButton : GrouplessBottomBarButton
    {
        public TerrainGeneratorButton(VisualElementLoader veLoader, IAssetLoader assetLoader, DevModeManager devMode) :
            base(
                veLoader, assetLoader)
        {
            SpritePath = "Sprites/timberui-dev";
            Click = TerrainGeneratorButtonClicked;
            BottomText = "Dev Mode";
        }

        void TerrainGeneratorButtonClicked()
        {
        }
    }
}
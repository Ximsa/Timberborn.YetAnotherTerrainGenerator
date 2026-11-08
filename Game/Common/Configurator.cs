using Bindito.Core;
using Timberborn.BottomBarSystem;
using Timberborn.TerrainGenerator.Game.Autoencoder;
using Timberborn.TerrainGenerator.Game.CliffSharpening;
using Timberborn.TerrainGenerator.Game.Noise3D;
using Timberborn.TerrainGenerator.Game.Smoothing;
using Timberborn.TerrainGenerator.Game.Trees;
using Timberborn.TerrainGenerator.Game.Wfc;
using TimberUi.CommonProviders;

namespace Timberborn.TerrainGenerator.Game.Common;

[Context("MainMenu")]
public class MainMenuConfigurator : Configurator
{
    protected override void Configure()
    {
    }
}

[Context("Game")]
public class GameConfigurator : Configurator
{
    protected override void Configure()
    {
    }
}

[Context("MapEditor")]
public class MapEditorConfigurator : Configurator
{
    protected override void Configure()
    {
        Bind<MapEditorService>().AsSingleton();

        Bind<AutoencoderGeneratorTool>().AsSingleton();
        Bind<Noise3DGeneratorTool>().AsSingleton();
        Bind<WfcGeneratorTool>().AsSingleton();
        Bind<TerrainGeneratorGroupButton>().AsSingleton();
        MultiBind<BottomBarModule>().ToProvider<BottomBarModuleProvider<TerrainGeneratorGroupButton>>().AsSingleton();

        Bind<TerrainSmoothingTool>().AsSingleton();
        Bind<CliffSharpeningTool>().AsSingleton();
        Bind<EntityPlacer>().AsSingleton();
        Bind<TreePlacementTool>().AsSingleton();
        Bind<TerrainToolsGroupButton>().AsSingleton();
        MultiBind<BottomBarModule>().ToProvider<BottomBarModuleProvider<TerrainToolsGroupButton>>().AsSingleton();
    }
}

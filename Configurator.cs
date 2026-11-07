using Bindito.Core;
using Timberborn.BottomBarSystem;
using TimberUi.CommonProviders;

namespace Timberborn.TerrainGenerator;

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

        Bind<TerrainSmoothingTool>().AsSingleton();
        Bind<TerrainFilterButton>().AsSingleton();
        MultiBind<BottomBarModule>().ToProvider<BottomBarModuleProvider<TerrainFilterButton>>().AsSingleton();

        Bind<AutoencoderGeneratorTool>().AsSingleton();
        Bind<Noise3DGeneratorTool>().AsSingleton();
        Bind<WfcGeneratorTool>().AsSingleton();
        Bind<TerrainGeneratorGroupButton>().AsSingleton();
        MultiBind<BottomBarModule>().ToProvider<BottomBarModuleProvider<TerrainGeneratorGroupButton>>().AsSingleton();
    }
}
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
        Bind<TerrainGeneratorButton>().AsTransient();
        Bind<TerrainFilterButton>().AsTransient();
        Bind<MapEditorService>().AsSingleton();
        MultiBind<BottomBarModule>().ToProvider<BottomBarModuleProvider<TerrainGeneratorButton>>().AsSingleton();
        MultiBind<BottomBarModule>().ToProvider<BottomBarModuleProvider<TerrainFilterButton>>().AsSingleton();
    }
}
using Bindito.Core;
using Timberborn.BottomBarSystem;
using TimberUi.CommonProviders;

namespace Timberborn.TerrainGenerator;

[Context("MainMenu")]
public class MainMenuConfigurator : Configurator
{
    public override void Configure()
    {
    }
}

[Context("Game")]
public class GameConfigurator : Configurator
{
    public override void Configure()
    {
    }
}

[Context("MapEditor")]
public class MapEditorConfigurator : Configurator
{
    public override void Configure()
    {
        Bind<TerrainGeneratorButton>().AsTransient();
        Bind<TerrainFilterButton>().AsTransient();
        Bind<MapEditorService>().AsSingleton();
        MultiBind<BottomBarModule>().ToProvider<BottomBarModuleProvider<TerrainGeneratorButton>>().AsSingleton();
        MultiBind<BottomBarModule>().ToProvider<BottomBarModuleProvider<TerrainFilterButton>>().AsSingleton();
    }
}
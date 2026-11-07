using Timberborn.AssetSystem;
using Timberborn.ToolButtonSystem;
using Timberborn.ToolSystem;

namespace Timberborn.TerrainGenerator;

public class TerrainToolsGroupButton : ToolGroupBottomBarButton
{
    private readonly TerrainSmoothingTool terrainSmoothingTool;

    public TerrainToolsGroupButton(ToolGroupService toolGroupService,
        ToolGroupButtonFactory toolGroupButtonFactory, ToolButtonFactory toolButtonFactory,
        IAssetLoader assetLoader, TerrainSmoothingTool terrainSmoothingTool) :
        base(toolGroupService, toolGroupButtonFactory, toolButtonFactory, assetLoader)
    {
        this.terrainSmoothingTool = terrainSmoothingTool;
    }

    protected override string GroupId => "TerrainTools";

    protected override IEnumerable<(ITool Tool, string SpritePath)> Tools => new (ITool, string)[]
    {
        (terrainSmoothingTool, "Sprites/terrain-smoothing-button")
    };
}

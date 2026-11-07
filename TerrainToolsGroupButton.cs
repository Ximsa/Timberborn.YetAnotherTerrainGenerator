using Timberborn.AssetSystem;
using Timberborn.ToolButtonSystem;
using Timberborn.ToolSystem;

namespace Timberborn.TerrainGenerator;

public class TerrainToolsGroupButton : ToolGroupBottomBarButton
{
    private readonly CliffSharpeningTool cliffSharpeningTool;
    private readonly TerrainSmoothingTool terrainSmoothingTool;

    public TerrainToolsGroupButton(ToolGroupService toolGroupService,
        ToolGroupButtonFactory toolGroupButtonFactory, ToolButtonFactory toolButtonFactory,
        IAssetLoader assetLoader, TerrainSmoothingTool terrainSmoothingTool, CliffSharpeningTool cliffSharpeningTool) :
        base(toolGroupService, toolGroupButtonFactory, toolButtonFactory, assetLoader)
    {
        this.terrainSmoothingTool = terrainSmoothingTool;
        this.cliffSharpeningTool = cliffSharpeningTool;
    }

    protected override string GroupId => "TerrainTools";

    protected override IEnumerable<(ITool Tool, string SpritePath)> Tools => new (ITool, string)[]
    {
        (terrainSmoothingTool, "Sprites/terrain-smoothing-button"),
        (cliffSharpeningTool, "Sprites/terrain-sharpen-button")
    };
}

using Timberborn.AssetSystem;
using Timberborn.TerrainGenerator.Game.CliffSharpening;
using Timberborn.TerrainGenerator.Game.Smoothing;
using Timberborn.TerrainGenerator.Game.Trees;
using Timberborn.ToolButtonSystem;
using Timberborn.ToolSystem;

namespace Timberborn.TerrainGenerator.Game.Common;

public class TerrainToolsGroupButton : ToolGroupBottomBarButton
{
    private readonly CliffSharpeningTool cliffSharpeningTool;
    private readonly TerrainSmoothingTool terrainSmoothingTool;
    private readonly TreePlacementTool treePlacementTool;

    public TerrainToolsGroupButton(ToolGroupService toolGroupService,
        ToolGroupButtonFactory toolGroupButtonFactory, ToolButtonFactory toolButtonFactory,
        IAssetLoader assetLoader, TerrainSmoothingTool terrainSmoothingTool, CliffSharpeningTool cliffSharpeningTool,
        TreePlacementTool treePlacementTool) :
        base(toolGroupService, toolGroupButtonFactory, toolButtonFactory, assetLoader)
    {
        this.terrainSmoothingTool = terrainSmoothingTool;
        this.cliffSharpeningTool = cliffSharpeningTool;
        this.treePlacementTool = treePlacementTool;
    }

    protected override string GroupId => "TerrainTools";

    protected override IEnumerable<(ITool Tool, string SpritePath)> Tools => new (ITool, string)[]
    {
        (terrainSmoothingTool, "Sprites/terrain-smoothing-button"),
        (cliffSharpeningTool, "Sprites/terrain-sharpen-button"),
        (treePlacementTool, "Sprites/tree-placement-button")
    };
}

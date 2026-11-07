using Timberborn.AssetSystem;
using Timberborn.BottomBarSystem;
using Timberborn.Localization;
using Timberborn.ToolButtonSystem;
using UnityEngine;

namespace Timberborn.TerrainGenerator;

// Standalone bottom bar button for terrain smoothing, built like the game's own groupless tool buttons.
public class TerrainFilterButton : IBottomBarElementsProvider
{
    private const string SpritePath = "Sprites/terrain-smoothing-button";

    private readonly IAssetLoader assetLoader;
    private readonly ILoc loc;
    private readonly TerrainSmoothingTool terrainSmoothingTool;
    private readonly ToolButtonFactory toolButtonFactory;

    public TerrainFilterButton(ToolButtonFactory toolButtonFactory, IAssetLoader assetLoader, ILoc loc,
        TerrainSmoothingTool terrainSmoothingTool)
    {
        this.toolButtonFactory = toolButtonFactory;
        this.assetLoader = assetLoader;
        this.loc = loc;
        this.terrainSmoothingTool = terrainSmoothingTool;
    }

    public IEnumerable<BottomBarElement> GetElements()
    {
        var button = toolButtonFactory.CreateGrouplessGreen(terrainSmoothingTool,
            assetLoader.Load<Sprite>(SpritePath));
        button.InitializeTooltip(loc.T(terrainSmoothingTool.NameLocKey));
        yield return BottomBarElement.CreateSingleLevel(button.Root);
    }
}

using Timberborn.AssetSystem;
using Timberborn.BottomBarSystem;
using Timberborn.Localization;
using Timberborn.ToolButtonSystem;
using Timberborn.ToolSystem;
using UnityEngine;

namespace Timberborn.TerrainGenerator;

// A bottom bar button group built with the game's own tool group system, like the game's Demolishing group.
// The group itself is defined by a ToolGroupSpec blueprint in the mod's ToolGroups folder.
public abstract class ToolGroupBottomBarButton : IBottomBarElementsProvider
{
    private readonly IAssetLoader assetLoader;
    private readonly ILoc loc;
    private readonly ToolButtonFactory toolButtonFactory;
    private readonly ToolGroupButtonFactory toolGroupButtonFactory;
    private readonly ToolGroupService toolGroupService;

    protected ToolGroupBottomBarButton(ToolGroupService toolGroupService,
        ToolGroupButtonFactory toolGroupButtonFactory, ToolButtonFactory toolButtonFactory,
        IAssetLoader assetLoader, ILoc loc)
    {
        this.toolGroupService = toolGroupService;
        this.toolGroupButtonFactory = toolGroupButtonFactory;
        this.toolButtonFactory = toolButtonFactory;
        this.assetLoader = assetLoader;
        this.loc = loc;
    }

    protected abstract string GroupId { get; }

    protected abstract IEnumerable<(ITool Tool, string SpritePath, string TooltipLocKey)> Tools { get; }

    public IEnumerable<BottomBarElement> GetElements()
    {
        var group = toolGroupService.GetGroup(GroupId);
        var groupButton = toolGroupButtonFactory.CreateGreen(group);
        foreach (var (tool, spritePath, tooltipLocKey) in Tools)
        {
            var toolButton = toolButtonFactory.Create(tool, assetLoader.Load<Sprite>(spritePath),
                groupButton.ToolButtonsElement);
            toolButton.InitializeTooltip(loc.T(tooltipLocKey));
            groupButton.AddTool(toolButton);
            toolGroupService.AssignToGroup(group, tool);
        }

        yield return BottomBarElement.CreateMultiLevel(groupButton.Root, groupButton.ToolButtonsElement);
    }
}

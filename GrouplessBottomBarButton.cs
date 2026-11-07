using Timberborn.AssetSystem;
using Timberborn.BottomBarSystem;
using Timberborn.CoreUI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Timberborn.TerrainGenerator;

// Replacement for TimberUI 0.7's GrouplessBottomBarButton, which TimberUI 1.x no longer provides.
// Builds the button the same way the game's own ShowOptionsButton does.
public abstract class GrouplessBottomBarButton : IBottomBarElementsProvider
{
    private readonly IAssetLoader assetLoader;
    private readonly VisualElementLoader visualElementLoader;

    protected GrouplessBottomBarButton(VisualElementLoader visualElementLoader, IAssetLoader assetLoader)
    {
        this.visualElementLoader = visualElementLoader;
        this.assetLoader = assetLoader;
    }

    protected string SpritePath { get; set; } = string.Empty;
    protected string TooltipText { get; set; } = string.Empty;
    protected Action Click { get; set; } = () => { };

    public IEnumerable<BottomBarElement> GetElements()
    {
        var root = visualElementLoader.LoadVisualElement("Common/BottomBar/GrouplessToolButton");
        root.AddToClassList("bottom-bar-button--red");
        root.Q<VisualElement>("ToolImage").style.backgroundImage =
            new StyleBackground(assetLoader.Load<Sprite>(SpritePath));
        root.Q<Button>("ToolButton").RegisterCallback<ClickEvent>(_ => Click());
        root.Q<TextElement>("BottomText").ToggleDisplayStyle(false);

        // shown only while hovering, like the game's own bottom bar buttons
        var tooltip = root.Q<Label>("Tooltip");
        tooltip.text = TooltipText;
        tooltip.ToggleDisplayStyle(false);
        root.RegisterCallback<MouseOverEvent>(_ => tooltip.ToggleDisplayStyle(true));
        root.RegisterCallback<MouseOutEvent>(_ => tooltip.ToggleDisplayStyle(false));
        yield return BottomBarElement.CreateSingleLevel(root);
    }
}

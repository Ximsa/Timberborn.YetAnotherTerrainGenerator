using Timberborn.CoreUI;
using Timberborn.Localization;
using Timberborn.ToolSystem;
using Timberborn.ToolSystemUI;
using TimberUi.CommonUi;

namespace Timberborn.TerrainGenerator;

// A tool whose button opens a dialog instead of changing what clicks on the map do.
// Its name and description show in the game's description panel while hovering its button.
public abstract class DialogTool : ITool, IToolDescriptor
{
    private readonly PanelStack panelStack;
    private readonly ToolService toolService;
    private readonly VisualElementInitializer visualElementInitializer;
    protected readonly ILoc loc;

    protected DialogTool(VisualElementInitializer visualElementInitializer, PanelStack panelStack,
        ToolService toolService, ILoc loc)
    {
        this.visualElementInitializer = visualElementInitializer;
        this.panelStack = panelStack;
        this.toolService = toolService;
        this.loc = loc;
    }

    public abstract string NameLocKey { get; }
    protected abstract string DescriptionLocKey { get; }

    public void Enter()
    {
        CreateDialog().Show(visualElementInitializer, panelStack);
        // the tool only opens a dialog, so hand control straight back to the default tool
        toolService.SwitchToDefaultTool();
    }

    public void Exit()
    {
    }

    public ToolDescription DescribeTool()
    {
        return new ToolDescription.Builder(loc.T(NameLocKey)).AddSection(loc.T(DescriptionLocKey)).Build();
    }

    protected abstract DialogBoxElement CreateDialog();
}

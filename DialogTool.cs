using Timberborn.CoreUI;
using Timberborn.ToolSystem;
using TimberUi.CommonUi;

namespace Timberborn.TerrainGenerator;

// A tool whose button opens a dialog instead of changing what clicks on the map do.
public abstract class DialogTool : ITool
{
    private readonly PanelStack panelStack;
    private readonly ToolService toolService;
    private readonly VisualElementInitializer visualElementInitializer;

    protected DialogTool(VisualElementInitializer visualElementInitializer, PanelStack panelStack,
        ToolService toolService)
    {
        this.visualElementInitializer = visualElementInitializer;
        this.panelStack = panelStack;
        this.toolService = toolService;
    }

    public void Enter()
    {
        CreateDialog().Show(visualElementInitializer, panelStack);
        // the tool only opens a dialog, so hand control straight back to the default tool
        toolService.SwitchToDefaultTool();
    }

    public void Exit()
    {
    }

    protected abstract DialogBoxElement CreateDialog();
}

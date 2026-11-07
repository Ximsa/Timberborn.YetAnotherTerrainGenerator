using Timberborn.CoreUI;
using Timberborn.Localization;
using Timberborn.ToolSystem;
using TimberUi.CommonUi;

namespace Timberborn.TerrainGenerator;

public class CliffSharpeningTool : DialogTool
{
    private readonly MapEditorService mapEditorService;

    public CliffSharpeningTool(VisualElementInitializer visualElementInitializer, PanelStack panelStack,
        ToolService toolService, ILoc loc, MapEditorService mapEditorService) :
        base(visualElementInitializer, panelStack, toolService, loc)
    {
        this.mapEditorService = mapEditorService;
    }

    public override string NameLocKey => "Ximsa.TerrainGenerator.Tool.CliffSharpening";
    protected override string DescriptionLocKey => "Ximsa.TerrainGenerator.Tool.CliffSharpening.Description";

    protected override DialogBoxElement CreateDialog()
    {
        return new CliffSharpeningDialog(loc, mapEditorService);
    }
}

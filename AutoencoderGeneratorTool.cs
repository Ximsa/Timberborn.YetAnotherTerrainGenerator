using Timberborn.CoreUI;
using Timberborn.Localization;
using Timberborn.ToolSystem;
using TimberUi.CommonUi;

namespace Timberborn.TerrainGenerator;

public class AutoencoderGeneratorTool : DialogTool
{
    private readonly ILoc loc;
    private readonly MapEditorService mapEditorService;

    public AutoencoderGeneratorTool(VisualElementInitializer visualElementInitializer, PanelStack panelStack,
        ToolService toolService, ILoc loc, MapEditorService mapEditorService) :
        base(visualElementInitializer, panelStack, toolService)
    {
        this.loc = loc;
        this.mapEditorService = mapEditorService;
    }

    protected override DialogBoxElement CreateDialog()
    {
        return new TerrainGeneratorDialog(loc, mapEditorService);
    }
}

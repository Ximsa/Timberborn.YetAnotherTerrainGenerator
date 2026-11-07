using Timberborn.CoreUI;
using Timberborn.Localization;
using Timberborn.MapRepositorySystem;
using Timberborn.ToolSystem;
using TimberUi.CommonUi;

namespace Timberborn.TerrainGenerator;

public class WfcGeneratorTool : DialogTool
{
    private readonly MapEditorService mapEditorService;
    private readonly MapRepository mapRepository;

    public WfcGeneratorTool(VisualElementInitializer visualElementInitializer, PanelStack panelStack,
        ToolService toolService, ILoc loc, MapEditorService mapEditorService, MapRepository mapRepository) :
        base(visualElementInitializer, panelStack, toolService, loc)
    {
        this.mapEditorService = mapEditorService;
        this.mapRepository = mapRepository;
    }

    public override string NameLocKey => "Ximsa.TerrainGenerator.Tool.Wfc";
    protected override string DescriptionLocKey => "Ximsa.TerrainGenerator.Tool.Wfc.Description";

    protected override DialogBoxElement CreateDialog()
    {
        return new WfcGeneratorDialog(loc, mapEditorService, mapRepository);
    }
}

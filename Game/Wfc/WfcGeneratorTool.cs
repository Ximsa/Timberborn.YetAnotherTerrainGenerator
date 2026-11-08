using Timberborn.CoreUI;
using Timberborn.Localization;
using Timberborn.MapRepositorySystem;
using Timberborn.TerrainGenerator.Game.Common;
using Timberborn.ToolSystem;
using TimberUi.CommonUi;

namespace Timberborn.TerrainGenerator.Game.Wfc;

public class WfcGeneratorTool : DialogTool
{
    private readonly DialogBoxShower dialogBoxShower;
    private readonly MapEditorService mapEditorService;
    private readonly MapRepository mapRepository;

    public WfcGeneratorTool(VisualElementInitializer visualElementInitializer, PanelStack panelStack,
        ToolService toolService, ILoc loc, MapEditorService mapEditorService, MapRepository mapRepository,
        DialogBoxShower dialogBoxShower) :
        base(visualElementInitializer, panelStack, toolService, loc)
    {
        this.mapEditorService = mapEditorService;
        this.mapRepository = mapRepository;
        this.dialogBoxShower = dialogBoxShower;
    }

    public override string NameLocKey => "Ximsa.TerrainGenerator.Tool.Wfc";
    protected override string DescriptionLocKey => "Ximsa.TerrainGenerator.Tool.Wfc.Description";

    protected override DialogBoxElement CreateDialog()
    {
        return new WfcGeneratorDialog(loc, mapEditorService, mapRepository, dialogBoxShower);
    }
}

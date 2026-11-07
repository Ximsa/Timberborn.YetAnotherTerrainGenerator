using Timberborn.CoreUI;
using Timberborn.DropdownSystem;
using Timberborn.Localization;
using Timberborn.MapRepositorySystem;
using Timberborn.TerrainGenerator.Game.Common;
using Timberborn.ToolSystem;
using TimberUi.CommonUi;

namespace Timberborn.TerrainGenerator.Game.Wfc;

public class WfcGeneratorTool : DialogTool
{
    private readonly DropdownItemsSetter dropdownItemsSetter;
    private readonly MapEditorService mapEditorService;
    private readonly MapRepository mapRepository;
    private readonly VisualElementInitializer visualElementInitializer;

    public WfcGeneratorTool(VisualElementInitializer visualElementInitializer, PanelStack panelStack,
        ToolService toolService, ILoc loc, MapEditorService mapEditorService, MapRepository mapRepository,
        DropdownItemsSetter dropdownItemsSetter) :
        base(visualElementInitializer, panelStack, toolService, loc)
    {
        this.mapEditorService = mapEditorService;
        this.mapRepository = mapRepository;
        this.visualElementInitializer = visualElementInitializer;
        this.dropdownItemsSetter = dropdownItemsSetter;
    }

    public override string NameLocKey => "Ximsa.TerrainGenerator.Tool.Wfc";
    protected override string DescriptionLocKey => "Ximsa.TerrainGenerator.Tool.Wfc.Description";

    protected override DialogBoxElement CreateDialog()
    {
        return new WfcGeneratorDialog(loc, mapEditorService, mapRepository, visualElementInitializer,
            dropdownItemsSetter);
    }
}

using Timberborn.CoreUI;
using Timberborn.Localization;
using Timberborn.ToolSystem;
using TimberUi.CommonUi;

namespace Timberborn.TerrainGenerator;

public class TreePlacementTool : DialogTool
{
    private readonly EntityPlacer entityPlacer;
    private readonly MapEditorService mapEditorService;

    public TreePlacementTool(VisualElementInitializer visualElementInitializer, PanelStack panelStack,
        ToolService toolService, ILoc loc, MapEditorService mapEditorService, EntityPlacer entityPlacer) :
        base(visualElementInitializer, panelStack, toolService, loc)
    {
        this.mapEditorService = mapEditorService;
        this.entityPlacer = entityPlacer;
    }

    public override string NameLocKey => "Ximsa.TerrainGenerator.Tool.TreePlacement";
    protected override string DescriptionLocKey => "Ximsa.TerrainGenerator.Tool.TreePlacement.Description";

    protected override DialogBoxElement CreateDialog()
    {
        return new TreePlacementDialog(loc, mapEditorService, entityPlacer);
    }
}

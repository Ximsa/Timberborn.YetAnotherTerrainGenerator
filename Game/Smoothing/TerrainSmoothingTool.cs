using Timberborn.CoreUI;
using Timberborn.Localization;
using Timberborn.TerrainGenerator.Game.Common;
using Timberborn.ToolSystem;
using TimberUi.CommonUi;

namespace Timberborn.TerrainGenerator.Game.Smoothing;

public class TerrainSmoothingTool : DialogTool
{
    private readonly MapEditorService mapEditorService;

    public TerrainSmoothingTool(VisualElementInitializer visualElementInitializer, PanelStack panelStack,
        ToolService toolService, ILoc loc, MapEditorService mapEditorService) :
        base(visualElementInitializer, panelStack, toolService, loc)
    {
        this.mapEditorService = mapEditorService;
    }

    public override string NameLocKey => "Ximsa.TerrainGenerator.Tool.Smoothing";
    protected override string DescriptionLocKey => "Ximsa.TerrainGenerator.Tool.Smoothing.Description";

    protected override DialogBoxElement CreateDialog()
    {
        return new TerrainFilterDialog(loc, mapEditorService);
    }
}

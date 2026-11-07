using Timberborn.CoreUI;
using Timberborn.Localization;
using Timberborn.ToolSystem;
using TimberUi.CommonUi;

namespace Timberborn.TerrainGenerator;

public class Noise3DGeneratorTool : DialogTool
{
    private readonly MapEditorService mapEditorService;

    public Noise3DGeneratorTool(VisualElementInitializer visualElementInitializer, PanelStack panelStack,
        ToolService toolService, ILoc loc, MapEditorService mapEditorService) :
        base(visualElementInitializer, panelStack, toolService, loc)
    {
        this.mapEditorService = mapEditorService;
    }

    public override string NameLocKey => "Ximsa.TerrainGenerator.Tool.Noise3D";
    protected override string DescriptionLocKey => "Ximsa.TerrainGenerator.Tool.Noise3D.Description";

    protected override DialogBoxElement CreateDialog()
    {
        return new Noise3DGeneratorDialog(loc, mapEditorService);
    }
}

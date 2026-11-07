using Timberborn.CoreUI;
using Timberborn.Localization;
using Timberborn.ToolSystem;
using TimberUi.CommonUi;

namespace Timberborn.TerrainGenerator;

public class AutoencoderGeneratorTool : DialogTool
{
    private readonly MapEditorService mapEditorService;

    public AutoencoderGeneratorTool(VisualElementInitializer visualElementInitializer, PanelStack panelStack,
        ToolService toolService, ILoc loc, MapEditorService mapEditorService) :
        base(visualElementInitializer, panelStack, toolService, loc)
    {
        this.mapEditorService = mapEditorService;
    }

    public override string NameLocKey => "Ximsa.TerrainGenerator.Tool.Autoencoder";
    protected override string DescriptionLocKey => "Ximsa.TerrainGenerator.Tool.Autoencoder.Description";

    protected override DialogBoxElement CreateDialog()
    {
        return new TerrainGeneratorDialog(loc, mapEditorService);
    }
}

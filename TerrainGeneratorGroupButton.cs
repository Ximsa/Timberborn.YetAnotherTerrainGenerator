using Timberborn.AssetSystem;
using Timberborn.Localization;
using Timberborn.ToolButtonSystem;
using Timberborn.ToolSystem;

namespace Timberborn.TerrainGenerator;

public class TerrainGeneratorGroupButton : ToolGroupBottomBarButton
{
    private const string AutoencoderTooltipKey = "Ximsa.TerrainGenerator.Tool.Autoencoder";
    private const string Phase0TooltipKey = "Ximsa.TerrainGenerator.Tool.Phase0Checks";

    private readonly AutoencoderGeneratorTool autoencoderGeneratorTool;
    private readonly Phase0ChecksTool phase0ChecksTool;

    public TerrainGeneratorGroupButton(ToolGroupService toolGroupService,
        ToolGroupButtonFactory toolGroupButtonFactory, ToolButtonFactory toolButtonFactory,
        IAssetLoader assetLoader, ILoc loc, AutoencoderGeneratorTool autoencoderGeneratorTool,
        Phase0ChecksTool phase0ChecksTool) :
        base(toolGroupService, toolGroupButtonFactory, toolButtonFactory, assetLoader, loc)
    {
        this.autoencoderGeneratorTool = autoencoderGeneratorTool;
        this.phase0ChecksTool = phase0ChecksTool;
    }

    protected override string GroupId => "TerrainGenerators";

    protected override IEnumerable<(ITool Tool, string SpritePath, string TooltipLocKey)> Tools => new (ITool, string, string)[]
    {
        (autoencoderGeneratorTool, "Sprites/terrain-generator-button", AutoencoderTooltipKey),
        (phase0ChecksTool, "Sprites/terrain-sharpen-button", Phase0TooltipKey)
    };
}

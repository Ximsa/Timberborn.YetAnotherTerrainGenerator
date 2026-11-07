using Timberborn.AssetSystem;
using Timberborn.ToolButtonSystem;
using Timberborn.ToolSystem;

namespace Timberborn.TerrainGenerator;

public class TerrainGeneratorGroupButton : ToolGroupBottomBarButton
{
    private readonly AutoencoderGeneratorTool autoencoderGeneratorTool;
    private readonly Phase0ChecksTool phase0ChecksTool;

    public TerrainGeneratorGroupButton(ToolGroupService toolGroupService,
        ToolGroupButtonFactory toolGroupButtonFactory, ToolButtonFactory toolButtonFactory,
        IAssetLoader assetLoader, AutoencoderGeneratorTool autoencoderGeneratorTool,
        Phase0ChecksTool phase0ChecksTool) :
        base(toolGroupService, toolGroupButtonFactory, toolButtonFactory, assetLoader)
    {
        this.autoencoderGeneratorTool = autoencoderGeneratorTool;
        this.phase0ChecksTool = phase0ChecksTool;
    }

    protected override string GroupId => "TerrainGenerators";

    protected override IEnumerable<(ITool Tool, string SpritePath)> Tools => new (ITool, string)[]
    {
        (autoencoderGeneratorTool, "Sprites/terrain-generator-button"),
        (phase0ChecksTool, "Sprites/terrain-sharpen-button")
    };
}

using Timberborn.AssetSystem;
using Timberborn.ToolButtonSystem;
using Timberborn.ToolSystem;

namespace Timberborn.TerrainGenerator;

public class TerrainGeneratorGroupButton : ToolGroupBottomBarButton
{
    private readonly AutoencoderGeneratorTool autoencoderGeneratorTool;

    public TerrainGeneratorGroupButton(ToolGroupService toolGroupService,
        ToolGroupButtonFactory toolGroupButtonFactory, ToolButtonFactory toolButtonFactory,
        IAssetLoader assetLoader, AutoencoderGeneratorTool autoencoderGeneratorTool) :
        base(toolGroupService, toolGroupButtonFactory, toolButtonFactory, assetLoader)
    {
        this.autoencoderGeneratorTool = autoencoderGeneratorTool;
    }

    protected override string GroupId => "TerrainGenerators";

    protected override IEnumerable<(ITool Tool, string SpritePath)> Tools => new (ITool, string)[]
    {
        (autoencoderGeneratorTool, "Sprites/terrain-generator-button")
    };
}

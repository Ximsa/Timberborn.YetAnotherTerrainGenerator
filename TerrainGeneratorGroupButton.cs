using Timberborn.AssetSystem;
using Timberborn.ToolButtonSystem;
using Timberborn.ToolSystem;

namespace Timberborn.TerrainGenerator;

public class TerrainGeneratorGroupButton : ToolGroupBottomBarButton
{
    private readonly AutoencoderGeneratorTool autoencoderGeneratorTool;
    private readonly Noise3DGeneratorTool noise3DGeneratorTool;

    public TerrainGeneratorGroupButton(ToolGroupService toolGroupService,
        ToolGroupButtonFactory toolGroupButtonFactory, ToolButtonFactory toolButtonFactory,
        IAssetLoader assetLoader, AutoencoderGeneratorTool autoencoderGeneratorTool,
        Noise3DGeneratorTool noise3DGeneratorTool) :
        base(toolGroupService, toolGroupButtonFactory, toolButtonFactory, assetLoader)
    {
        this.autoencoderGeneratorTool = autoencoderGeneratorTool;
        this.noise3DGeneratorTool = noise3DGeneratorTool;
    }

    protected override string GroupId => "TerrainGenerators";

    protected override IEnumerable<(ITool Tool, string SpritePath)> Tools => new (ITool, string)[]
    {
        (autoencoderGeneratorTool, "Sprites/terrain-generator-button"),
        (noise3DGeneratorTool, "Sprites/noise3d-generator-button")
    };
}

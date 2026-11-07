using Timberborn.AssetSystem;
using Timberborn.TerrainGenerator.Game.Autoencoder;
using Timberborn.TerrainGenerator.Game.Noise3D;
using Timberborn.TerrainGenerator.Game.Wfc;
using Timberborn.ToolButtonSystem;
using Timberborn.ToolSystem;

namespace Timberborn.TerrainGenerator.Game.Common;

public class TerrainGeneratorGroupButton : ToolGroupBottomBarButton
{
    private readonly AutoencoderGeneratorTool autoencoderGeneratorTool;
    private readonly Noise3DGeneratorTool noise3DGeneratorTool;
    private readonly WfcGeneratorTool wfcGeneratorTool;

    public TerrainGeneratorGroupButton(ToolGroupService toolGroupService,
        ToolGroupButtonFactory toolGroupButtonFactory, ToolButtonFactory toolButtonFactory,
        IAssetLoader assetLoader, AutoencoderGeneratorTool autoencoderGeneratorTool,
        Noise3DGeneratorTool noise3DGeneratorTool, WfcGeneratorTool wfcGeneratorTool) :
        base(toolGroupService, toolGroupButtonFactory, toolButtonFactory, assetLoader)
    {
        this.autoencoderGeneratorTool = autoencoderGeneratorTool;
        this.noise3DGeneratorTool = noise3DGeneratorTool;
        this.wfcGeneratorTool = wfcGeneratorTool;
    }

    protected override string GroupId => "TerrainGenerators";

    protected override IEnumerable<(ITool Tool, string SpritePath)> Tools => new (ITool, string)[]
    {
        (autoencoderGeneratorTool, "Sprites/terrain-generator-button"),
        (noise3DGeneratorTool, "Sprites/noise3d-generator-button"),
        (wfcGeneratorTool, "Sprites/wfc-generator-button")
    };
}

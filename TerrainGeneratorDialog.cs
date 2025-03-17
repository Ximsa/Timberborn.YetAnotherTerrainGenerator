using NumSharp;
using Timberborn.Localization;
using TimberUi.CommonUi;
using UiBuilder.CommonUi;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

namespace Timberborn.TerrainGenerator;

public class TerrainGeneratorDialog : DialogBoxElement
{
    private const string TitleKey = "Ximsa.TerrainGenerator.Title";
    private const string HeightmapKey = "Ximsa.TerrainGenerator.Heightmap";
    private const string EncoderKey = "Ximsa.TerrainGenerator.Encoder";
    private const string NoiseKey = "Ximsa.TerrainGenerator.Noise";
    private const string NoiseStdKey = "Ximsa.TerrainGenerator.NoiseStd";
    private const string NoiseMeanKey = "Ximsa.TerrainGenerator.NoiseMean";
    private const string GenerateKey = "Ximsa.TerrainGenerator.Generate";
    private const string MedianBlurPassesKey = "Ximsa.TerrainGenerator.MedianBlurPasses";
    private const string MedianBlurRadiusKey = "Ximsa.TerrainGenerator.MedianBlurRadius";
    private const string MedianBlurTitleKey = "Ximsa.TerrainGenerator.MedianBlurTitle";

    private float encoderBias = 1;
    private int medianBlurPasses = 1;
    private int medianBlurRadius = 5;
    private float noiseMean;
    private float noiseStd = 1;
    private bool useHeightmap;
    private readonly MapEditorService mapEditorService;


    public TerrainGeneratorDialog(
        ILoc loc,
        MapEditorService mapEditorService)
    {
        this.mapEditorService = mapEditorService;
        SetTitle(loc.T(TitleKey));
        Content.Add(new GameSlider()
            .SetLabel($"{loc.T(EncoderKey)} {-12}-{12}")
            .SetHorizontalSlider(new SliderValues<float>(-12, 12, encoderBias))
            .RegisterChange(encoderBias => this.encoderBias = encoderBias));
        Content.AddLabel(loc.T(NoiseKey), NoiseKey);
        Content.Add(new GameSlider()
            .SetLabel($"{loc.T(NoiseMeanKey)} {-12}-{12}")
            .SetHorizontalSlider(new SliderValues<float>(-12, 12, noiseMean))
            .RegisterChange(noiseMean => this.noiseMean = noiseMean));
        Content.Add(new GameSlider()
            .SetLabel($"{loc.T(NoiseStdKey)} {0}-{12}")
            .SetHorizontalSlider(new SliderValues<float>(0, 12, noiseStd))
            .RegisterChange(noiseStd => this.noiseStd = noiseStd));
        Content.AddLabel(loc.T(MedianBlurTitleKey), MedianBlurTitleKey);
        Content.Add(new GameSliderInt()
            .SetLabel($"{loc.T(MedianBlurPassesKey)} {0}-{12}")
            .SetHorizontalSlider(new SliderValues<int>(0, 12, medianBlurPasses))
            .RegisterChange(medianBlurPasses => this.medianBlurPasses = medianBlurPasses));
        Content.Add(new GameSliderInt()
            .SetLabel($"{loc.T(MedianBlurRadiusKey)} {1}-{12}")
            .SetHorizontalSlider(new SliderValues<int>(1, 12, medianBlurRadius))
            .RegisterChange(medianBlurRadius => this.medianBlurRadius = medianBlurRadius));
        Content.AddButton(loc.T(GenerateKey), GenerateKey, OnGenerate);
        AddCloseButton();
    }

    private void OnGenerate()
    {
        mapEditorService.RemoveAllEntityComponents();
        mapEditorService.ClearTerrain();
        Pipeline2D();
        mapEditorService.Apply();
    }

    // encode current map, add noise, decode map
    private void Pipeline2D()
    {
        // load models
        var encoder = LoadModel("Encoder2D.npz");
        var decoder = LoadModel("Decoder2D.npz");

        var inputDim = (int)Math.Sqrt(encoder.weight1.Shape[1]);
        var encodedSize = decoder.weight1.Shape[1];

        // normalize input
        var terrain = mapEditorService.GetTerrain();
        terrain = terrain.astype(np.float32);
        terrain = terrain.sum(0);
        terrain = terrain.Scale2D(new Shape(inputDim, inputDim));
        terrain /= np.max(terrain);
        Debug.Log(terrain.Shape);
        // encode
        Debug.Log("encode");
        var encoded = RunModel(terrain.reshape(1, inputDim * inputDim), encoder);

        // generate noise
        Debug.Log("noise");
        var noise = ((np.random.rand(1, encodedSize) - 0.5) * noiseStd + noiseMean).astype(np.float32);
        encoded = encoded * encoderBias + noise;

        // decode
        Debug.Log("decode");
        var decoded = RunModel(encoded, decoder);
        Debug.Log($"Min {decoded.min()} Max {decoded.max()} Mean {decoded.mean()} Std {decoded.std()}");

        // Filter
        decoded = np.tanh(decoded, NPTypeCode.Float) + 0.25f * decoded;
        decoded = decoded.reshape(inputDim, inputDim);
        Debug.Log($"filter {medianBlurRadius} {medianBlurPasses}");
        decoded = decoded.MedianBlur(medianBlurRadius, medianBlurPasses);
        // scale when needed
        if (decoded.Shape[0] < mapEditorService.MapSize.y || decoded.Shape[1] < mapEditorService.MapSize.x)
        {
            Debug.Log("scale");
            var targetSize = math.max(mapEditorService.MapSize.x, mapEditorService.MapSize.y);
            decoded = decoded.Scale2D(new Shape(targetSize, targetSize));
        }

        decoded -= decoded.min();
        decoded = decoded / decoded.max() * (mapEditorService.MapSize.z - 1.0f);

        // set terrain
        Debug.Log("save");
        for (var x = 0; x < mapEditorService.MapSize.x; x++)
        for (var y = 0; y < mapEditorService.MapSize.y; y++)
        {
            var terrainHeight = (float)decoded[y, x].astype(np.float32);
            for (var z = 0; z < terrainHeight; z++)
                mapEditorService.SetTerrain(new Vector3Int(x, y, z), true);
        }
    }

    private static (NDArray weight1, NDArray bias1, NDArray weight2, NDArray bias2, NDArray weight3, NDArray bias3)
        LoadModel(string model)
    {
        var path = ModStarter.OriginPath + "/Models/" + model;
        var weightDict = np.Load_Npz<float[,]>(path);
        var w1 = weightDict["1.weight.npy"];
        var w2 = weightDict["2.weight.npy"];
        var w3 = weightDict["3.weight.npy"];
        weightDict.Dispose();
        var biasDict = np.Load_Npz<float[]>(path);
        var b1 = biasDict["1.bias.npy"];
        var b2 = biasDict["2.bias.npy"];
        var b3 = biasDict["3.bias.npy"];
        biasDict.Dispose();
        return (w1, b1, w2, b2, w3, b3);
    }

    private static NDArray RunModel(NDArray input, (
        NDArray weight1,
        NDArray bias1,
        NDArray weight2,
        NDArray bias2,
        NDArray weight3,
        NDArray bias3) model)
    {
        var (weight1, bias1, weight2, bias2, weight3, bias3) = model;
        var layer1 = np.matmul(input, weight1.T);
        layer1 += bias1;
        layer1 = np.maximum(0.0f, layer1, NPTypeCode.Float);
        var layer2 = np.matmul(layer1, weight2.T);
        layer2 += bias2;
        layer2 = np.maximum(0.0f, layer2, NPTypeCode.Float);
        var layer3 = np.matmul(layer2, weight3.T);
        layer3 += bias3;
        return layer3;
    }
}
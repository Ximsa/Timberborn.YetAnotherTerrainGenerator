using NumSharp;
using Timberborn.Localization;
using TimberUi.CommonUi;
using UiBuilder.CommonUi;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;
using Random = System.Random;

namespace Timberborn.TerrainGenerator;

public class TerrainGeneratorDialog : DialogBoxElement
{
    private const string TitleKey = "Ximsa.TerrainGenerator.Title";
    private const string EncoderKey = "Ximsa.TerrainGenerator.Encoder";
    private const string NoiseKey = "Ximsa.TerrainGenerator.Noise";
    private const string NoiseStdKey = "Ximsa.TerrainGenerator.NoiseStd";
    private const string NoiseMeanKey = "Ximsa.TerrainGenerator.NoiseMean";
    private const string GenerateKey = "Ximsa.TerrainGenerator.Generate";
    private const string MedianBlurPassesKey = "Ximsa.TerrainGenerator.MedianBlurPasses";
    private const string MedianBlurRadiusKey = "Ximsa.TerrainGenerator.MedianBlurRadius";
    private const string MedianBlurTitleKey = "Ximsa.TerrainGenerator.MedianBlurTitle";
    private const string ZoomFactorKey = "Ximsa.TerrainGenerator.ZoomFactor";
    private const string MaxHeightKey = "Ximsa.TerrainGenerator.MaxHeight";

    private readonly MapEditorService mapEditorService;

    private float encoderBias = -1;
    private int medianBlurPasses = 1;
    private int medianBlurRadius = 4;
    private float noiseMean;
    private float noiseStd = 1;
    private float zoomFactor = 1.25f;
    private int maxHeight = 1;


    public TerrainGeneratorDialog(
        ILoc loc,
        MapEditorService mapEditorService)
    {
        this.mapEditorService = mapEditorService;
        SetTitle(loc.T(TitleKey));
        Content.Add(new GameSlider()
            .SetLabel($"{loc.T(EncoderKey)}")
            .SetHorizontalSlider(new SliderValues<float>(-12, 12, encoderBias))
            .RegisterChange(encoderBias => this.encoderBias = encoderBias)
            .AddEndLabel(value => $"{value:G3}"));
        Content.AddLabel(loc.T(NoiseKey), NoiseKey);
        Content.Add(new GameSlider()
            .SetLabel($"{loc.T(NoiseMeanKey)}")
            .SetHorizontalSlider(new SliderValues<float>(-12, 12, noiseMean))
            .RegisterChange(noiseMean => this.noiseMean = noiseMean)
            .AddEndLabel(value => $"{value:G3}"));
        Content.Add(new GameSlider()
            .SetLabel($"{loc.T(NoiseStdKey)}")
            .SetHorizontalSlider(new SliderValues<float>(0, 12, noiseStd))
            .RegisterChange(noiseStd => this.noiseStd = noiseStd)
            .AddEndLabel(value => $"{value:G3}"));
        Content.AddLabel(loc.T(MedianBlurTitleKey), MedianBlurTitleKey);
        Content.Add(new GameSliderInt()
            .SetLabel($"{loc.T(MedianBlurPassesKey)}")
            .SetHorizontalSlider(new SliderValues<int>(0, 8, medianBlurPasses))
            .RegisterChange(medianBlurPasses => this.medianBlurPasses = medianBlurPasses)
            .AddEndLabel(value => $"{value}"));
        Content.Add(new GameSliderInt()
            .SetLabel($"{loc.T(MedianBlurRadiusKey)}")
            .SetHorizontalSlider(new SliderValues<int>(1, 8, medianBlurRadius))
            .RegisterChange(medianBlurRadius => this.medianBlurRadius = medianBlurRadius)
            .AddEndLabel(value => $"{value}"));
        Content.Add(new GameSlider()
            .SetLabel($"{loc.T(ZoomFactorKey)}")
            .SetHorizontalSlider(new SliderValues<float>(1, 8, zoomFactor))
            .RegisterChange(zoomFactor => this.zoomFactor = zoomFactor)
            .AddEndLabel(value => $"{value:G3}"));
        this.maxHeight = mapEditorService.MapSize.z;
        Content.Add(new GameSliderInt()
            .SetLabel($"{loc.T(MaxHeightKey)}")
            .SetHorizontalSlider(new SliderValues<int>(1, mapEditorService.MapSize.z, mapEditorService.MapSize.z))
            .RegisterChange(maxHeight => this.maxHeight = maxHeight)
            .AddEndLabel(value => $"{value}"));
        Content.AddButton(loc.T(GenerateKey), GenerateKey, OnGenerate);
        AddCloseButton();
    }

    private void OnGenerate()
    {
        mapEditorService.RemoveAllEntityComponents();
        var terrain = Pipeline2D();
        mapEditorService.Set2DTerrain(terrain);
    }

    // encode current map, add noise, decode map
    private NDArray Pipeline2D()
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

        // soft "clamp"
        //decoded = np.tanh(decoded, NPTypeCode.Float) + 0.25f * decoded;
        decoded = decoded.reshape(inputDim, inputDim);

        Debug.Log("zoom");
        if (decoded.Shape[0] < mapEditorService.MapSize.y || decoded.Shape[1] < mapEditorService.MapSize.x)
        {
            var targetSize = math.max(mapEditorService.MapSize.x, mapEditorService.MapSize.y);
            decoded = decoded.Scale2D(new Shape(targetSize, targetSize));
        }

        decoded = decoded.Scale2D(
            new Shape((int)(decoded.Shape[0] * zoomFactor), (int)(decoded.Shape[1] * zoomFactor)));

        // trim
        Debug.Log("trim");
        var rand = new Random();
        var maxDim0 = decoded.Shape[0] - mapEditorService.MapSize.y;
        var maxDim1 = decoded.Shape[1] - mapEditorService.MapSize.x;
        var offsetDim0 = rand.Next(0, maxDim0 + 1);
        var offsetDim1 = rand.Next(0, maxDim1 + 1);
        decoded = decoded[
            $"{offsetDim0}:{mapEditorService.MapSize.y + offsetDim0},{offsetDim1}:{mapEditorService.MapSize.x + offsetDim1}"];

        // filter
        Debug.Log($"filter {medianBlurRadius} {medianBlurPasses}");
        decoded = decoded.MedianBlur(medianBlurRadius, medianBlurPasses);

        // normalize
        decoded -= decoded.min();
        decoded = decoded / decoded.max() * (maxHeight - 1.0f);

        // set terrain
        return decoded;
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
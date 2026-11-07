using NumSharp;
using Timberborn.Localization;
using TimberUi.CommonUi;
using Unity.Mathematics;
using UnityEngine;
using Timberborn.TerrainGenerator.Generators.Autoencoder;
using UnityEngine.UIElements;
using Random = System.Random;

namespace Timberborn.TerrainGenerator;

public class TerrainGeneratorDialog : TerrainJobDialog
{
    private const string TitleKey = "Ximsa.TerrainGenerator.Title";
    private const string EncoderKey = "Ximsa.TerrainGenerator.Encoder";
    private const string VarietyKey = "Ximsa.TerrainGenerator.Variety";
    private const string GenerateKey = "Ximsa.TerrainGenerator.Generate";
    private const string ZoomFactorKey = "Ximsa.TerrainGenerator.ZoomFactor";
    private const string MaxHeightKey = "Ximsa.TerrainGenerator.MaxHeight";

    private readonly VisualElement generateButton;
    private Settings settings = new();

    public TerrainGeneratorDialog(
        ILoc loc,
        MapEditorService mapEditorService) : base(loc, mapEditorService)
    {
        SetTitle(loc.T(TitleKey));
        Content.Add(new GameSlider()
            .SetLabel($"{loc.T(EncoderKey)}")
            .SetHorizontalSlider(new SliderValues<float>(0, 1, settings.KeepCurrent))
            .RegisterChange(keepCurrent => settings.KeepCurrent = keepCurrent)
            .AddEndLabel(value => $"{value * 100:F0}%"));
        Content.Add(new GameSlider()
            .SetLabel($"{loc.T(VarietyKey)}")
            .SetHorizontalSlider(new SliderValues<float>(0, 3, settings.Variety))
            .RegisterChange(variety => settings.Variety = variety)
            .AddEndLabel(value => $"{value:F2}"));
        Content.Add(new GameSlider()
            .SetLabel($"{loc.T(ZoomFactorKey)}")
            .SetHorizontalSlider(new SliderValues<float>(1, 8, settings.ZoomFactor))
            .RegisterChange(zoomFactor => settings.ZoomFactor = zoomFactor)
            .AddEndLabel(value => $"{value:G3}"));
        settings.MaxTerrainHeight = mapEditorService.MapSize.z;
        Content.Add(new GameSliderInt()
            .SetLabel($"{loc.T(MaxHeightKey)}")
            .SetHorizontalSlider(new SliderValues<int>(1, mapEditorService.MapSize.z, mapEditorService.MapSize.z))
            .RegisterChange(maxHeight => settings.MaxTerrainHeight = maxHeight)
            .AddEndLabel(value => $"{value}"));
        generateButton = Content.AddButton(loc.T(GenerateKey), GenerateKey, OnGenerate);
        AddProgressBar();
        AddCloseButton();
    }

    private void OnGenerate()
    {
        // snapshot, so moving sliders while the job runs does not affect it
        var jobSettings = settings.Clone();
        RunJob(generateButton, (terrain, mapSize, progress) =>
            VoxelMap.FromHeightmap(Pipeline2D(terrain, mapSize, jobSettings, progress), mapSize.z));
    }

    // encode current map, add noise, decode map; runs on a background thread
    private static NDArray Pipeline2D(NDArray terrain, Vector3Int mapSize, Settings settings,
        ProgressReporter progress)
    {
        // load models
        var (encoder, decoder, latentStats) = progress.Stage(LoadingModelsKey, 0f, 0.05f,
            _ => (LoadModel("Encoder2D.npz"), LoadModel("Decoder2D.npz"),
                LatentStats.Load(ModStarter.OriginPath + "/Models/LatentStats.txt")));

        var inputDim = (int)Math.Sqrt(encoder.weight1.Shape[1]);
        var encodedSize = decoder.weight1.Shape[1];

        var encoded = progress.Stage(EncodingKey, 0.05f, 0.1f, report =>
        {
            // normalize input
            terrain = terrain.astype(np.float32);
            terrain = terrain.sum(0);
            terrain = terrain.Scale2D(new Shape(inputDim, inputDim), report);
            terrain /= np.max(terrain);
            Debug.Log(terrain.Shape);
            // encode
            return RunModel(terrain.reshape(1, inputDim * inputDim), encoder);
        });

        var decoded = progress.Stage(DecodingKey, 0.1f, 0.15f, _ =>
        {
            // sample around the built-in maps' latents, blending in the current map's
            var sampled = latentStats.Sample(encoded.flatten().ToArray<float>(), settings.KeepCurrent,
                settings.Variety, new Random());
            encoded = np.array(sampled).reshape(1, encodedSize);

            // decode
            var result = RunModel(encoded, decoder);
            Debug.Log($"Min {result.min()} Max {result.max()} Mean {result.mean()} Std {result.std()}");
            return result;
        });

        // soft "clamp"
        //decoded = np.tanh(decoded, NPTypeCode.Float) + 0.25f * decoded;
        decoded = decoded.reshape(inputDim, inputDim);

        decoded = progress.Stage(ScalingKey, 0.15f, 0.9f, report =>
        {
            // zoom
            if (decoded.Shape[0] < mapSize.y || decoded.Shape[1] < mapSize.x)
            {
                var targetSize = math.max(mapSize.x, mapSize.y);
                decoded = decoded.Scale2D(new Shape(targetSize, targetSize), fraction => report(fraction / 2));
            }

            decoded = decoded.Scale2D(
                new Shape((int)(decoded.Shape[0] * settings.ZoomFactor), (int)(decoded.Shape[1] * settings.ZoomFactor)),
                fraction => report(0.5f + fraction / 2));

            // trim
            var rand = new Random();
            var maxDim0 = decoded.Shape[0] - mapSize.y;
            var maxDim1 = decoded.Shape[1] - mapSize.x;
            var offsetDim0 = rand.Next(0, maxDim0 + 1);
            var offsetDim1 = rand.Next(0, maxDim1 + 1);
            return decoded[
                $"{offsetDim0}:{mapSize.y + offsetDim0},{offsetDim1}:{mapSize.x + offsetDim1}"];
        });

        // round off the square blocks that enlarging by nearest neighbour leaves
        decoded = progress.Stage(FilteringKey, 0.9f, 1f, _ =>
        {
            var blurred = GaussianBlur.Apply(decoded.astype(np.float32).flatten().ToArray<float>(), mapSize.x,
                mapSize.y, GaussianBlur.DefaultSigma);
            return np.array(blurred).reshape(mapSize.y, mapSize.x);
        });

        // stretch to the map's height
        decoded -= decoded.min();
        return decoded / decoded.max() * (settings.MaxTerrainHeight - 1.0f);
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

    private sealed class Settings
    {
        // 0 ignores the current map, 1 keeps its latent and only adds variety
        public float KeepCurrent;
        public int MaxTerrainHeight;

        // noise in standard deviations of the built-in maps' latents
        public float Variety = 1.5f;
        public float ZoomFactor = 1.25f;

        public Settings Clone()
        {
            return (Settings)MemberwiseClone();
        }
    }
}

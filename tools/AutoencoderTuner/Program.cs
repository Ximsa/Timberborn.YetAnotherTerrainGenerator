using System.Diagnostics;
using System.Globalization;
using AutoencoderTuner;
using Timberborn.TerrainGenerator;
using Timberborn.TerrainGenerator.Generators.Autoencoder;
using TerrainPreview;

// Tunes the autoencoder generator's models without retraining.
// Usage:
//   AutoencoderTuner stats --maps <dir of .timber> [--encoder Models/Encoder2D.npz] --out Models/LatentStats.txt
//       the mean and standard deviation of each latent number over the maps, which the generator samples from
//   AutoencoderTuner sharpen --in <decoder.npz> --out <decoder.npz> [--amount 1] [--sigma 2]
//       bakes an unsharp mask into the decoder's last layer: every output map is sharpened exactly as if the filter
//       ran on it afterwards, since the layer is linear
//   AutoencoderTuner preview --maps <dir> [--current Diorama] [--decoder ...] [--stats ...] [--variety 1.5] [--keep 0]
//       [--legacy-multiplier -1 --legacy-noise 1] [--samples 8] [--size 256] [--seed 1] --out preview.png
//       generates like the mod's pipeline and prints diversity (how different samples are) and relief
var options = ParseOptions(args.Skip(1));
var models = options.GetValueOrDefault("models", "Models");
var stopwatch = Stopwatch.StartNew();
switch (args.FirstOrDefault())
{
    case "stats":
    {
        var encoder = Mlp.Load(options.GetValueOrDefault("encoder", Path.Combine(models, "Encoder2D.npz")));
        var latents = ReadMaps(options["maps"]).Select(map => encoder.Run(Pipeline.Normalize(map.Heights,
            map.SizeX, map.SizeY, Pipeline.InputSize))).ToList();
        var stats = LatentStats.From(latents);
        stats.Save(options["out"]);
        Console.WriteLine($"{latents.Count} maps; mean {Join(stats.Mean)}");
        Console.WriteLine($"std {Join(stats.Std)}");
        break;
    }
    case "sharpen":
    {
        var decoder = Mlp.Load(options["in"]);
        var amount = Number("amount", "1");
        var sigma = Number("sigma", "2");
        Sharpen(decoder, (float)amount, sigma);
        decoder.Save(options["out"]);
        Console.WriteLine($"sharpened by {amount} (sigma {sigma}) in {stopwatch.ElapsedMilliseconds} ms");
        break;
    }
    case "preview":
    {
        var encoder = Mlp.Load(options.GetValueOrDefault("encoder", Path.Combine(models, "Encoder2D.npz")));
        var decoder = Mlp.Load(options.GetValueOrDefault("decoder", Path.Combine(models, "Decoder2D.npz")));
        var maps = ReadMaps(options["maps"]);
        var size = int.Parse(options.GetValueOrDefault("size", "256"));
        var current = maps.First(m => m.Name == options.GetValueOrDefault("current", "Diorama"));
        var random = new Random(int.Parse(options.GetValueOrDefault("seed", "1")));
        var latent = encoder.Run(Pipeline.Normalize(current.Heights, current.SizeX, current.SizeY,
            Pipeline.InputSize));
        Func<float[]> sample;
        if (options.TryGetValue("legacy-multiplier", out var legacyMultiplier))
        {
            // the generator before tuning: latent * multiplier + uniform noise
            var multiplier = float.Parse(legacyMultiplier, CultureInfo.InvariantCulture);
            var noise = Number("legacy-noise", "1");
            sample = () => latent.Select(v => v * multiplier + (float)((random.NextDouble() - 0.5) * noise)).ToArray();
        }
        else
        {
            var stats = LatentStats.Load(options.GetValueOrDefault("stats", Path.Combine(models, "LatentStats.txt")));
            var variety = (float)Number("variety", "1.5");
            var keep = (float)Number("keep", "0");
            sample = () => stats.Sample(latent, keep, variety, random);
        }

        var samples = Enumerable.Range(0, int.Parse(options.GetValueOrDefault("samples", "8")))
            .Select(_ => Pipeline.Finish(decoder.Run(sample()), size, 1.25f, 4, 1, random))
            .ToList();
        var real = maps.Select(m => Pipeline.Normalize(m.Heights, m.SizeX, m.SizeY, size)).ToList();
        Console.WriteLine($"samples: diversity {Diversity(samples):F3}, relief {Relief(samples, size):F3} | " +
                          $"built-in maps: diversity {Diversity(real):F3}, relief {Relief(real, size):F3}");
        Sheet(samples, size).SavePng(options["out"]);
        Console.WriteLine($"wrote {options["out"]} after {stopwatch.ElapsedMilliseconds} ms");
        break;
    }
    default:
        Console.WriteLine("usage: AutoencoderTuner stats|sharpen|preview ...");
        return 1;
}

return 0;

double Number(string key, string fallback)
{
    return double.Parse(options.GetValueOrDefault(key, fallback), CultureInfo.InvariantCulture);
}

static string Join(float[] values)
{
    return string.Join(" ", values.Select(v => v.ToString("F2", CultureInfo.InvariantCulture)));
}

// surface heights of every map in a folder
static List<(string Name, int[] Heights, int SizeX, int SizeY)> ReadMaps(string folder)
{
    return Directory.GetFiles(folder, "*.timber").OrderBy(f => f).Select(path =>
    {
        using var file = File.OpenRead(path);
        var map = MapVoxelReader.Read(file);
        return (Path.GetFileNameWithoutExtension(path),
            Timberborn.TerrainGenerator.TerrainTools.Surface.Heights(map), map.SizeX, map.SizeY);
    }).ToList();
}

// unsharp mask on each of the last layer's output maps: W' = W + amount * (W - blur(W)), same for the bias
static void Sharpen(Mlp decoder, float amount, double sigma)
{
    var last = Mlp.Layers - 1;
    var outputs = decoder.Outputs[last];
    var side = (int)Math.Sqrt(outputs);
    var inputs = decoder.Inputs[last];
    var weights = decoder.Weights[last];
    // one column of the weight matrix is the map that one hidden unit adds to the output
    Parallel.For(0, inputs + 1, column =>
    {
        var map = new float[outputs];
        for (var p = 0; p < outputs; p++)
            map[p] = column < inputs ? weights[p * inputs + column] : decoder.Biases[last][p];
        var blurred = Blur(map, side, sigma);
        for (var p = 0; p < outputs; p++)
        {
            var sharpened = map[p] + amount * (map[p] - blurred[p]);
            if (column < inputs) weights[p * inputs + column] = sharpened;
            else decoder.Biases[last][p] = sharpened;
        }
    });
}

// separable Gaussian blur with clamped edges
static float[] Blur(float[] map, int side, double sigma)
{
    var radius = (int)Math.Ceiling(sigma * 3);
    var kernel = Enumerable.Range(-radius, 2 * radius + 1).Select(d => Math.Exp(-d * d / (2 * sigma * sigma))).ToArray();
    var total = kernel.Sum();
    var horizontal = new float[map.Length];
    var result = new float[map.Length];
    for (var y = 0; y < side; y++)
    for (var x = 0; x < side; x++)
    {
        double sum = 0;
        for (var d = -radius; d <= radius; d++)
            sum += kernel[d + radius] * map[y * side + Math.Clamp(x + d, 0, side - 1)];
        horizontal[y * side + x] = (float)(sum / total);
    }

    for (var y = 0; y < side; y++)
    for (var x = 0; x < side; x++)
    {
        double sum = 0;
        for (var d = -radius; d <= radius; d++)
            sum += kernel[d + radius] * horizontal[Math.Clamp(y + d, 0, side - 1) * side + x];
        result[y * side + x] = (float)(sum / total);
    }

    return result;
}

// mean per-pixel standard deviation across maps (each normalised to 0..1): how different the maps are
static double Diversity(List<float[]> maps)
{
    double total = 0;
    var length = maps[0].Length;
    for (var p = 0; p < length; p++)
    {
        var mean = maps.Average(m => m[p]);
        total += Math.Sqrt(maps.Average(m => (m[p] - mean) * (m[p] - mean)));
    }

    return total / length;
}

// mean height step between neighbouring tiles, in tiles of a 22 tile high map: how rugged the maps are
static double Relief(List<float[]> maps, int side)
{
    return maps.Average(m =>
    {
        double sum = 0;
        for (var y = 0; y < side - 1; y++)
        for (var x = 0; x < side - 1; x++)
        {
            var h = m[y * side + x];
            sum += Math.Abs(m[y * side + x + 1] - h) + Math.Abs(m[(y + 1) * side + x] - h);
        }

        return sum / (2.0 * (side - 1) * (side - 1)) * 22;
    });
}

static Image Sheet(List<float[]> maps, int side)
{
    const int columns = 4, gap = 8;
    var rows = (maps.Count + columns - 1) / columns;
    var sheet = new Image(columns * (side + gap) + gap, rows * (side + gap) + gap, new Rgb(32, 34, 40));
    for (var i = 0; i < maps.Count; i++)
    {
        int ox = gap + i % columns * (side + gap), oy = gap + i / columns * (side + gap);
        for (var y = 0; y < side; y++)
        for (var x = 0; x < side; x++)
        {
            // quantised to 22 levels like the game, with light from the top left so cliffs show
            var level = (int)(maps[i][y * side + x] * 21.99f);
            var left = x > 0 ? (int)(maps[i][y * side + x - 1] * 21.99f) : level;
            var up = y > 0 ? (int)(maps[i][(y - 1) * side + x] * 21.99f) : level;
            var shade = Math.Clamp(1 + 0.12 * (level - left + level - up), 0.55, 1.35);
            var t = level / 21.0;
            sheet[ox + x, oy + y] = new Rgb((byte)Math.Clamp((70 + 150 * t) * shade, 0, 255),
                (byte)Math.Clamp((110 + 110 * t) * shade, 0, 255), (byte)Math.Clamp((55 + 140 * t) * shade, 0, 255));
        }
    }

    return sheet;
}

static Dictionary<string, string> ParseOptions(IEnumerable<string> arguments)
{
    var result = new Dictionary<string, string>();
    string? key = null;
    foreach (var argument in arguments)
        if (argument.StartsWith("--"))
            key = argument[2..];
        else if (key != null)
        {
            result[key] = argument;
            key = null;
        }

    return result;
}

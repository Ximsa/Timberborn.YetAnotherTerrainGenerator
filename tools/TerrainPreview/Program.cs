using System.Diagnostics;
using Timberborn.TerrainGenerator;
using Timberborn.TerrainGenerator.Generators.Noise3D;
using Timberborn.TerrainGenerator.Generators.Wfc;
using TerrainPreview;

// Usage:
//   TerrainPreview noise3d --size 128x128x23 --seed 1 [--climate rainforest|betabiomes] [--base 56] [--vscale 3]
//       [--hscale 3] --out preview.png
//   TerrainPreview wfc --sources a.timber,b.timber --size 64x64x23 --seed 1 [--n 3] [--symmetry true]
//       [--periodic false] [--penalty 0.5] [--threshold 0.25] [--coverage 0.95] [--block 4] [--seeds 1-10] --out preview.png
//   TerrainPreview threshold-sweep --sources a.timber,b.timber [--size 128x128x23] [--seed 1] [--start 0] --out-dir <dir>
//   TerrainPreview map <file.timber> --out preview.png
if (args.Length == 0)
{
    Console.WriteLine("usage: TerrainPreview noise3d|map ... --out <file.png>");
    return 1;
}

var options = ParseOptions(args.Skip(1));
var output = options.GetValueOrDefault("out", "preview.png");
var stopwatch = Stopwatch.StartNew();
VoxelMap map;
switch (args[0])
{
    case "noise3d":
    {
        var size = ParseSize(options.GetValueOrDefault("size", "128x128x23"));
        var settings = new Noise3DSettings
        {
            Seed = int.Parse(options.GetValueOrDefault("seed", "1")),
            BaseLevel = double.Parse(options.GetValueOrDefault("base", "56")),
            VerticalScale = double.Parse(options.GetValueOrDefault("vscale", "2")),
            HorizontalScale = double.Parse(options.GetValueOrDefault("hscale", "0")),
            Climate = Enum.Parse<Noise3DClimate>(options.GetValueOrDefault("climate", "Rainforest"), true)
        };
        var generator = new Noise3DTerrainGenerator(settings, size.X, size.Y, size.Z);
        generator.ComputeClimate();
        Console.WriteLine($"climate: {stopwatch.ElapsedMilliseconds} ms");
        generator.ComputeDensity();
        Console.WriteLine($"density: {stopwatch.ElapsedMilliseconds} ms");
        map = generator.BuildVoxels();
        Console.WriteLine($"voxels:  {stopwatch.ElapsedMilliseconds} ms " +
                          $"(vertical scale {settings.ResolveVerticalScale(size.Z):F2}, " +
                          $"horizontal scale {settings.ResolveHorizontalScale(size.Z):F2})");
        break;
    }
    case "wfc":
    {
        var size = ParseSize(options.GetValueOrDefault("size", "64x64x23"));
        var settings = new WfcSettings
        {
            Seed = int.Parse(options.GetValueOrDefault("seed", "1")),
            PatternSize = int.Parse(options.GetValueOrDefault("n", "3")),
            Symmetry = options.GetValueOrDefault("symmetry", "true") == "true",
            PeriodicInput = options.GetValueOrDefault("periodic", "false") == "true",
            HeightPenalty = double.Parse(options.GetValueOrDefault("penalty", "0.5"), System.Globalization.CultureInfo.InvariantCulture),
            SolidThreshold = double.Parse(options.GetValueOrDefault("threshold", "0.25"), System.Globalization.CultureInfo.InvariantCulture),
            PatternCoverage = double.Parse(options.GetValueOrDefault("coverage", "0.95"), System.Globalization.CultureInfo.InvariantCulture),
            BlockSize = int.Parse(options.GetValueOrDefault("block", "4"))
        };
        var sources = options["sources"].Split(',').Select(path =>
        {
            using var file = File.OpenRead(path);
            return MapVoxelReader.Read(file);
        }).ToList();
        var generator = new WfcTerrainGenerator(settings);
        generator.Learn(sources);
        Console.WriteLine($"learned {generator.PatternCount} patterns in {stopwatch.ElapsedMilliseconds} ms");
        if (options.TryGetValue("seeds", out var seedRange))
        {
            // one contact sheet with a tile per seed, learning only once
            var bounds = seedRange.Split('-').Select(int.Parse).ToArray();
            var tiles = new List<(string Label, Image Tile)>();
            for (var seed = bounds[0]; seed <= bounds[^1]; seed++)
            {
                var run = Stopwatch.StartNew();
                settings.Seed = seed;
                var backtracksBefore = generator.Backtracks;
                var result = generator.Generate(size.X, size.Y, size.Z);
                Console.Write($"seed {seed,2}: {run.ElapsedMilliseconds,6} ms, {generator.AttemptsUsed} attempt(s), " +
                              $"{generator.Backtracks - backtracksBefore} backtracks | ");
                PrintStats(result);
                tiles.Add((seed.ToString(), VoxelRenderer.RenderIsometric(result)));
            }

            Sheet(tiles).SavePng(output);
            Console.WriteLine($"wrote {output} after {stopwatch.ElapsedMilliseconds} ms");
            return 0;
        }

        map = generator.Generate(size.X, size.Y, size.Z);
        Console.WriteLine($"generated in {stopwatch.ElapsedMilliseconds} ms after {generator.AttemptsUsed} attempt(s), {generator.Backtracks} backtracks");
        break;
    }
    case "threshold-sweep":
    {
        // the coarse round trip of the first source, and WFC results, for solid thresholds 0% .. 100%
        var size = ParseSize(options.GetValueOrDefault("size", "128x128x23"));
        var seed = int.Parse(options.GetValueOrDefault("seed", "1"));
        var outDir = options.GetValueOrDefault("out-dir", ".");
        var sources = options["sources"].Split(',').Select(path =>
        {
            using var file = File.OpenRead(path);
            return MapVoxelReader.Read(file);
        }).ToList();
        var roundTrips = new List<(string Label, Image Tile)>();
        var results = new List<(string Label, Image Tile)>();
        var startPercent = int.Parse(options.GetValueOrDefault("start", "0"));
        for (var percent = startPercent; percent <= 100; percent += 10)
        {
            var threshold = percent / 100.0;
            var label = $"{percent}%";
            var first = sources[0];
            var roundTrip = WfcTerrainGenerator.Upsample(WfcTerrainGenerator.Downsample(first, threshold),
                first.SizeX, first.SizeY, first.SizeZ);
            roundTrips.Add((label, VoxelRenderer.RenderIsometric(roundTrip)));
            Console.Write($"{label,4}: round trip solid {roundTrip.CountSolid(),6} (source {first.CountSolid()}) | ");

            var run = Stopwatch.StartNew();
            var generator = new WfcTerrainGenerator(new WfcSettings { Seed = seed, SolidThreshold = threshold });
            try
            {
                generator.Learn(sources);
                var result = generator.Generate(size.X, size.Y, size.Z);
                results.Add((label, VoxelRenderer.RenderIsometric(result)));
                Console.Write($"WFC {generator.PatternCount,5} patterns, {run.ElapsedMilliseconds / 1000.0,5:F1} s, " +
                              $"{generator.Backtracks} backtracks | ");
                PrintStats(result);
            }
            catch (Exception exception)
            {
                results.Add((label, new Image(64, 64, new Rgb(90, 30, 30))));
                Console.WriteLine($"WFC failed: {exception.Message}");
            }
        }

        Sheet(roundTrips).SavePng(Path.Combine(outDir, "threshold-roundtrip.png"));
        Sheet(results).SavePng(Path.Combine(outDir, "threshold-wfc.png"));
        Console.WriteLine($"wrote sheets to {outDir} after {stopwatch.ElapsedMilliseconds / 1000} s");
        return 0;
    }
    case "map":
    {
        using var file = File.OpenRead(args[1]);
        map = MapVoxelReader.Read(file);
        break;
    }
    default:
        Console.WriteLine($"unknown generator {args[0]}");
        return 1;
}

PrintStats(map);
VoxelRenderer.Render(map).SavePng(output);
Console.WriteLine($"wrote {output} after {stopwatch.ElapsedMilliseconds} ms");
return 0;

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

static (int X, int Y, int Z) ParseSize(string size)
{
    var parts = size.Split('x').Select(int.Parse).ToArray();
    return (parts[0], parts[1], parts[2]);
}

static void PrintStats(VoxelMap map)
{
    var columnsWithGaps = 0;
    var topHeights = new List<int>();
    for (var y = 0; y < map.SizeY; y++)
    for (var x = 0; x < map.SizeX; x++)
    {
        var top = -1;
        var gap = false;
        for (var z = 0; z < map.SizeZ; z++)
            if (map[x, y, z])
            {
                if (top >= 0 && top < z - 1)
                    gap = true;
                top = z;
            }

        topHeights.Add(top + 1);
        if (gap)
            columnsWithGaps++;
    }

    topHeights.Sort();
    Console.WriteLine($"{map.SizeX}x{map.SizeY}x{map.SizeZ}: solid {map.CountSolid()}, " +
                      $"surface height min {topHeights[0]} / median {topHeights[topHeights.Count / 2]} / " +
                      $"max {topHeights[^1]}, columns with overhangs {columnsWithGaps}");
}

// labelled tiles in a grid of 4 columns
static Image Sheet(List<(string Label, Image Tile)> tiles)
{
    const int columns = 4, gap = 12, labelHeight = 26;
    var tileWidth = tiles.Max(t => t.Tile.Width);
    var tileHeight = tiles.Max(t => t.Tile.Height);
    var rows = (tiles.Count + columns - 1) / columns;
    var sheet = new Image(columns * (tileWidth + gap) + gap, rows * (tileHeight + labelHeight + gap) + gap,
        new Rgb(32, 34, 40));
    for (var i = 0; i < tiles.Count; i++)
    {
        var x = gap + i % columns * (tileWidth + gap);
        var y = gap + i / columns * (tileHeight + labelHeight + gap);
        sheet.DrawText(tiles[i].Label, x, y, 4, new Rgb(235, 235, 235));
        sheet.Blit(tiles[i].Tile, x, y + labelHeight);
    }

    return sheet;
}

using System.Diagnostics;
using Timberborn.TerrainGenerator.Core.CliffSharpening;
using Timberborn.TerrainGenerator.Core.Common;
using Timberborn.TerrainGenerator.Core.Noise3D;
using Timberborn.TerrainGenerator.Core.Trees;
using Timberborn.TerrainGenerator.Core.Wfc;
using TerrainPreview;

// Usage:
//   TerrainPreview noise3d --size 128x128x23 --seed 1 [--hilliness 1] [--ruggedness 0.5] [--normalize false] [--base 56] [--vscale 3]
//       [--hscale 3] --out preview.png
//   TerrainPreview wfc --sources a.timber,b.timber|<folder> [--size 128x128x16] [--seed 1] [--seeds 1-8]
//       [--deletions 2000] [--attempts 10] [--lookahead true] [--clear-every 8] [--penalty 0.25] --out preview.png
//   TerrainPreview wfc-test --sources a.timber   (checks the pattern encoding)
//   (wfc: --width 6 --height 4 set the pattern size)
//   TerrainPreview map <file.timber> --out preview.png
//   TerrainPreview sharpen <file.timber> [--region 4] [--min-area 32] [--step 2] [--strength 0.5] --out preview.png
//   TerrainPreview trees <file.timber> [--forest 0.2] [--bush 0.12] [--patch 32] [--clump 6]
//       [--density 0.35] [--seed 1] [--sharpen false] --out preview.png
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
            Hilliness = double.Parse(options.GetValueOrDefault("hilliness", "1"), System.Globalization.CultureInfo.InvariantCulture),
            Ruggedness = double.Parse(options.GetValueOrDefault("ruggedness", "0.5"), System.Globalization.CultureInfo.InvariantCulture),
            Normalize = options.GetValueOrDefault("normalize", "false") == "true"
        };
        var generator = new Noise3DTerrainGenerator(settings, size.X, size.Y, size.Z);
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
        var size = ParseSize(options.GetValueOrDefault("size", "128x128x16"));
        var settings = new WfcSettings
        {
            Seed = int.Parse(options.GetValueOrDefault("seed", "1")),
            MaxDeletions = int.Parse(options.GetValueOrDefault("deletions", "2000")),
            MaxAttempts = int.Parse(options.GetValueOrDefault("attempts", "10")),
            Lookahead = options.GetValueOrDefault("lookahead", "true") == "true",
            PatternWidth = int.Parse(options.GetValueOrDefault("width", "6")),
            PatternHeight = int.Parse(options.GetValueOrDefault("height", "4")),
            ClearEvery = int.Parse(options.GetValueOrDefault("clear-every", "8")),
            HeightPenalty = double.Parse(options.GetValueOrDefault("penalty", "0.25"), System.Globalization.CultureInfo.InvariantCulture)
        };
        var sources = ReadSources(options["sources"]);
        var generator = new WfcTerrainGenerator(settings);
        generator.Learn(sources);
        Console.WriteLine($"learned from {sources.Count} maps in {stopwatch.ElapsedMilliseconds} ms");
        Console.WriteLine(generator.Index.Describe());
        var seeds = options.TryGetValue("seeds", out var seedRange)
            ? seedRange.Split('-').Select(int.Parse).ToArray()
            : new[] { settings.Seed };
        var tiles = new List<(string Label, Image Tile)>();
        var profiles = new List<double[]>();
        VoxelMap? generated = null;
        for (var seed = seeds[0]; seed <= seeds[^1]; seed++)
        {
            var run = Stopwatch.StartNew();
            settings.Seed = seed;
            try
            {
                generated = generator.Generate(size.X, size.Y, size.Z);
            }
            catch (GenerationFailedException exception)
            {
                Console.WriteLine($"seed {seed}: {exception.Message}");
                tiles.Add(($"{seed} failed", new Image(64, 64, new Rgb(90, 30, 30))));
                continue;
            }

            Console.WriteLine($"seed {seed}: {run.ElapsedMilliseconds} ms, {generator.Attempts.Count} attempt(s), " +
                              $"novelty {generator.Novelty(generated) * 100:F1}%");
            foreach (var attempt in generator.Attempts)
                Console.WriteLine($"  {attempt}");
            Console.Write("  ");
            PrintStats(generated);
            profiles.Add(WfcTerrainGenerator.FillProfile(generated));
            tiles.Add((seed.ToString(), VoxelRenderer.RenderIsometric(generated)));
        }

        // fill ratio per height: the generated maps' mean against the sources
        Console.WriteLine("height  sources  generated");
        for (var z = 0; z < size.Z; z++)
            Console.WriteLine($"{z,6}  {(z < generator.SourceProfile.Length ? generator.SourceProfile[z] : 0),7:P0}  " +
                              $"{(profiles.Count > 0 ? profiles.Average(p => p[z]) : 0),9:P0}");
        if (seeds.Length > 1)
        {
            Sheet(tiles).SavePng(output);
            Console.WriteLine($"wrote {output} after {stopwatch.ElapsedMilliseconds} ms");
            return 0;
        }

        if (generated == null)
            return 1;
        map = generated;
        break;
    }
    case "wfc-test":
    {
        // checks of the pattern encoding for several shapes (docs/wfc-face-lookup.md, section 9 step 1)
        var failures = 0;
        var random = new Random(1);
        var source = ReadSources(options["sources"])[0];
        foreach (var (width, height) in new[] { (4, 4), (6, 4), (8, 5), (9, 6), (10, 6) })
        {
            var shape = new WfcPatternShape(width, height);
            for (var i = 0; i < 1000; i++)
            {
                var code = shape.NewPattern();
                for (var z = 0; z < height; z++)
                for (var y = 0; y < width; y++)
                for (var x = 0; x < width; x++)
                    if (random.Next(2) == 1)
                        shape.SetVoxel(code, x, y, z);
                var turned = (ulong[])code.Clone();
                shape.Rotate(turned, 4);
                if (!turned.SequenceEqual(code))
                    failures += Fail($"{shape}: four turns changed a pattern");
                var canonical = Canonical(shape, code);
                shape.Rotate(turned);
                if (!Canonical(shape, turned).SequenceEqual(canonical))
                    failures += Fail($"{shape}: canonical form differs between rotations");
                // a turn maps (x, y, z) to (w - 1 - y, x, z), so the turned +x face is the original -y face
                for (var z = 0; z < height; z++)
                for (var x = 0; x < width; x++)
                    if (shape.Voxel(turned, width - 1, x, z) != shape.Voxel(code, x, 0, z))
                        failures += Fail($"{shape}: a turn moved voxel ({x}, 0, {z}) wrongly");
            }

            // windows one stride apart in a source share their face exactly, also after turning both
            int[] turnedDirection = { 2, 3, 1, 0, 4, 5 };
            for (var i = 0; i < 1000; i++)
            {
                int x = random.Next(source.SizeX - 2 * width), y = random.Next(source.SizeY - 2 * width),
                    z = random.Next(source.SizeZ - 2 * height);
                var a = Window(shape, source, x, y, z);
                for (var d = 0; d < 6; d += 2)
                {
                    var (dx, dy, dz) = WfcPatternShape.Directions[d];
                    var b = Window(shape, source, x + shape.StrideAcross * dx, y + shape.StrideAcross * dy,
                        z + shape.StrideUp * dz);
                    if (shape.Face(a, d) != shape.Face(b, d + 1))
                        failures += Fail($"{shape}: faces differ at ({x}, {y}, {z}) direction {d}");
                    var t = turnedDirection[d];
                    var turnedA = (ulong[])a.Clone();
                    var turnedB = (ulong[])b.Clone();
                    shape.Rotate(turnedA);
                    shape.Rotate(turnedB);
                    if (shape.Face(turnedA, t) != shape.Face(turnedB, t ^ 1))
                        failures += Fail($"{shape}: turned faces differ at ({x}, {y}, {z}) direction {d}");
                }
            }

            // the sliding-window extraction gives the same canonical patterns as reading windows one by one
            var expected = new List<ulong[]>();
            for (var z = 0; z + height <= Math.Min(source.SizeZ, height + 1); z++)
            for (var y = 0; y + width <= source.SizeY; y++)
            for (var x = 0; x + width <= source.SizeX; x++)
                expected.Add(Canonical(shape, Window(shape, source, x, y, z)));
            var seen = 0;
            WfcPatternIndex.ForEachWindow(shape, source, (_, canonical) =>
            {
                if (seen < expected.Count && !canonical.SequenceEqual(expected[seen++]))
                    failures += Fail($"{shape}: window {seen - 1} differs from the sliding extraction");
            });
        }

        Console.WriteLine(failures == 0 ? "all checks passed" : $"{failures} checks failed");
        return failures == 0 ? 0 : 1;

        static int Fail(string message)
        {
            Console.WriteLine(message);
            return 1;
        }

        static ulong[] Canonical(WfcPatternShape shape, ulong[] pattern)
        {
            var result = (ulong[])pattern.Clone();
            shape.Canonicalize(result, shape.NewPattern());
            return result;
        }

        static ulong[] Window(WfcPatternShape shape, VoxelMap map, int x, int y, int z)
        {
            var pattern = shape.NewPattern();
            for (var dz = 0; dz < shape.Height; dz++)
            for (var dy = 0; dy < shape.Width; dy++)
            for (var dx = 0; dx < shape.Width; dx++)
                if (map[x + dx, y + dy, z + dz])
                    shape.SetVoxel(pattern, dx, dy, dz);
            return pattern;
        }
    }
    case "map":
    {
        using var file = File.OpenRead(args[1]);
        map = MapVoxelReader.Read(file);
        break;
    }
    case "sharpen":
    {
        // the map before and after cliff sharpening, side by side
        using var file = File.OpenRead(args[1]);
        var before = MapVoxelReader.Read(file);
        var settings = new CliffSharpenerSettings
        {
            RegionSize = int.Parse(options.GetValueOrDefault("region", "4")),
            MinRegionArea = int.Parse(options.GetValueOrDefault("min-area", "32")),
            StepSize = int.Parse(options.GetValueOrDefault("step", "2")),
            Strength = double.Parse(options.GetValueOrDefault("strength", "0.5"), System.Globalization.CultureInfo.InvariantCulture)
        };
        var sharpener = new CliffSharpener(settings, before.SizeX, before.SizeY);
        var after = Surface.WithHeights(before, sharpener.Sharpen(Surface.Heights(before)));
        Console.WriteLine($"sharpened into {sharpener.RegionCount} regions in {stopwatch.ElapsedMilliseconds} ms");
        PrintStats(before);
        PrintStats(after);
        Sheet(new List<(string, Image)>
        {
            ("before", VoxelRenderer.Render(before)), ("after", VoxelRenderer.Render(after))
        }).SavePng(output);
        Console.WriteLine($"wrote {output}");
        return 0;
    }
    case "trees":
    {
        // optional sharpening, then the plants (dark green trees in shades per species, yellow-green bushes); map
        // files are read without their water, so no tile counts as water
        using var file = File.OpenRead(args[1]);
        var before = MapVoxelReader.Read(file);
        var heights = Surface.Heights(before);
        if (options.GetValueOrDefault("sharpen", "false") == "true")
            heights = new CliffSharpener(new CliffSharpenerSettings(), before.SizeX, before.SizeY).Sharpen(heights);
        var defaults = new TreePlacerSettings();
        var treeSettings = new TreePlacerSettings
        {
            ForestCoverage = Double("forest", defaults.ForestCoverage),
            BushCoverage = Double("bush", defaults.BushCoverage),
            PatchSize = int.Parse(options.GetValueOrDefault("patch", defaults.PatchSize.ToString())),
            ClumpSize = int.Parse(options.GetValueOrDefault("clump", defaults.ClumpSize.ToString())),
            Density = Double("density", defaults.Density),
            // stand-ins for the game's species: four trees and two bushes
            TreeWeights = new[] { 1.0, 1.0, 1.0, 0.5 },
            BushWeights = new[] { 1.0, 1.0 },
            Seed = int.Parse(options.GetValueOrDefault("seed", "1"))
        };
        var placer = new TreePlacer(treeSettings, before.SizeX, before.SizeY);
        var plants = placer.Place(heights, new bool[heights.Length]);
        var trees = plants.Count(p => p.Kind == PlantKind.Tree);
        var bushes = plants.Count(p => p.Kind == PlantKind.Bush);
        Console.WriteLine($"placed {trees} trees ({trees * 100.0 / heights.Length:F1}% of tiles) and {bushes} bushes " +
                          $"({bushes * 100.0 / heights.Length:F1}%) in {stopwatch.ElapsedMilliseconds} ms");
        var colors = new Dictionary<(int, int), Rgb>();
        Rgb[] treeColors = { new(20, 70, 30), new(40, 95, 35), new(15, 55, 60), new(80, 60, 30) };
        Rgb[] bushColors = { new(180, 200, 60), new(220, 120, 160) };
        foreach (var plant in plants)
            colors[(plant.X, plant.Y)] = plant.Kind == PlantKind.Tree
                ? treeColors[plant.Species]
                : bushColors[plant.Species];
        var after = Surface.WithHeights(before, heights);
        Sheet(new List<(string, Image)>
        {
            ("before", VoxelRenderer.Render(before)),
            ("after", VoxelRenderer.Render(after, (x, y) => colors.TryGetValue((x, y), out var c) ? c : null))
        }).SavePng(output);
        Console.WriteLine($"wrote {output}");
        return 0;

        double Double(string key, double fallback)
        {
            return options.TryGetValue(key, out var value)
                ? double.Parse(value, System.Globalization.CultureInfo.InvariantCulture)
                : fallback;
        }
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

// comma-separated .timber files or folders of them
static List<VoxelMap> ReadSources(string paths)
{
    return paths.Split(',')
        .SelectMany(path => Directory.Exists(path)
            ? Directory.GetFiles(path, "*.timber").OrderBy(file => file).ToArray()
            : new[] { path })
        .Select(path =>
        {
            using var file = File.OpenRead(path);
            return MapVoxelReader.Read(file);
        }).ToList();
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

# Architecture

One mod project (`Timberborn.TerrainGenerator.csproj`, netstandard2.1, C# 10) with two halves, each arranged by
feature, plus offline tools that reuse one half.

```
Core/                    Timberborn-independent: algorithms on plain arrays and VoxelMap (no Unity, no game types)
  Common/                VoxelMap, MapVoxelReader, Surface, TileQueue
  Autoencoder/           AutoencoderGenerator, Mlp, LatentStats, GaussianBlur
  Noise3D/               Noise3DTerrainGenerator (+ settings), Noise3DOctaveNoise, Noise3DPerlinNoise
  Wfc/                   WfcTerrainGenerator (+ settings, cell selection), WfcPatternSet, WfcSolver
  Smoothing/             MedianBlur
  CliffSharpening/       CliffSharpener
  Rivers/                RiverCarver
  Trees/                 TreePlacer (uses Noise3DOctaveNoise for patches)
Game/                    Timberborn-specific: the map editor integration
  Common/                ModStarter, Configurator, MapEditorService, EntityPlacer, TerrainJobDialog,
                         ProgressReporter, DialogTool, ToolGroupBottomBarButton, TerrainGeneratorGroupButton,
                         TerrainToolsGroupButton, TerrainPhysicsPatches, ReflectionExtensions
  <Feature>/             one tool (a DialogTool) and one dialog (a TerrainJobDialog) per feature
tools/
  TerrainPreview/        runs Core generators and tools on .timber maps, renders PNG previews
  AutoencoderTuner/      latent statistics, decoder sharpening, autoencoder previews
```

Namespaces follow the folders: `Timberborn.TerrainGenerator.Core.<Feature>` and
`Timberborn.TerrainGenerator.Game.<Feature>`.

## The boundary

- **Core never references Game, Unity or Timberborn.** The offline tools compile all of `Core/` (net9.0) without any
  game assemblies, so a Timberborn or Unity type in Core breaks their build. NumSharp, Newtonsoft.Json and
  System.IO.Compression are allowed.
- **Game turns map editor state into Core inputs and Core results back into map editor changes.** Algorithms take
  heights or voxels and settings, and return heights, voxels or placement lists; they report progress as
  `Action<float>` and never touch game services.
- Core code must still run in the game's Mono runtime (netstandard2.1): no newer BCL APIs (e.g. `PriorityQueue`,
  `StringSplitOptions.TrimEntries`), and zip files through `ZipArchive` on a stream.

## Data

| Type | What | Where it comes from |
|---|---|---|
| `VoxelMap` | Solid voxels of a whole map, layer by layer from the bottom, x fastest | `VoxelMap.FromTerrain` (the editor's terrain), `MapVoxelReader` (`.timber` files), generators |
| Surface heights `int[]` / `float[]` | One per column, indexed `y * sizeX + x` | `Surface.Heights` (one above each column's topmost solid voxel) |
| `Surface.WithHeights` | Applies new surface heights to a `VoxelMap`, only touching voxels above the lower of old and new surface | used by all terrain tools, so 3D terrain below the surface survives |

## Flow of a tool action

```
bottom bar button ─▶ DialogTool.Enter ─▶ dialog (sliders) ─▶ Generate/Apply
                                                               │
TerrainJobDialog.RunJob ◀──────────────────────────────────────┘
  main thread:  read terrain (MapEditorService.GetTerrain), water, maps; snapshot settings
  background:   compute(terrain, mapSize, progress) ─▶ Core algorithm ─▶ new VoxelMap   (cancellable)
  main thread:  commit pending undo entries
                remove entities: all (generators) or only in changed columns (terrain tools)
                MapEditorService.SetVoxelsByRow (through ITerrainService), spread over frames
                placeEntities (optional): EntityPlacer spawns trees, bushes, water sources, over frames
                commit undo entries: the whole action is one Ctrl+Z step
```

- `ProgressReporter` carries stage names (localization keys) and fractions from the background task to the progress
  bar, and throws on cancellation at the next report.
- Terrain is only written through `ITerrainService` (the game redraws and records undo entries); entities only
  through `EntityService`, `NaturalResourceSpawner` and `BlockObjectFactory`.
- Seeds come from the current Unix timestamp in the dialogs; Core takes them as settings, so the offline tools can
  reproduce a run.

## Game wiring

- `ModStarter` (the game's `IModStarter`) records the mod paths and applies the Harmony patches
  (`TerrainPhysicsPatches`: keep floating terrain on load).
- `Configurator` binds everything in the `MapEditor` context: `MapEditorService`, `EntityPlacer`, one tool per
  feature, and the two bottom-bar groups.
- `ToolGroupBottomBarButton` builds a native tool group from a `ToolGroups/*.blueprint.json` spec;
  `TerrainGeneratorGroupButton` (Autoencoder, Noise3D, Wfc) and `TerrainToolsGroupButton` (Smoothing, CliffSharpening,
  Rivers, Trees) list their tools and icons.
- Assets: `Localizations/enUS.csv` (all texts), `Sprites/` (icons), `ToolGroups/` (group specs), `Models/`
  (autoencoder networks and latent statistics, read from `ModStarter.OriginPath/Models`).

## Features

| Feature | Core | Game |
|---|---|---|
| Autoencoder | `AutoencoderGenerator`: normalise input, encode (`Mlp`), sample around `LatentStats`, decode, enlarge, zoom, crop, `GaussianBlur`, normalise | `TerrainGeneratorDialog`: stages, heights to voxels |
| Noise3D | `Noise3DTerrainGenerator`: octave noise density on a coarse grid, trilinear sampling into voxels, optional fit to map height | `Noise3DGeneratorDialog` |
| Wfc | `WfcTerrainGenerator`: learn 3D patterns from maps (`WfcPatternSet`), solve with backtracking (`WfcSolver`, selectable cell order) | `WfcGeneratorDialog`: map checklist (`MapRepository`), cell selection dropdown |
| Smoothing | `MedianBlur` on the surface | `TerrainFilterDialog` |
| CliffSharpening | `CliffSharpener`: watershed on slope, height bands, small-region merging, median plateaus, minimum cliff merging | `CliffSharpeningDialog` |
| Rivers | `RiverCarver`: heads a few tiles below a climbed peak, steepest descent, basins filled up to their rim, tunnels (3D) towards the nearest edge through basins too big to fill, beds at most one block deep | `RiverCarverDialog`: tunnels carved into the voxels, water sources at heads (`EntityPlacer`) |
| Trees | `TreePlacer`: noise patches, species clumps, density near water | `TreePlacementDialog`: species from the game, water from `IThreadSafeWaterMap`, spawning (`EntityPlacer`) |

## Build and deploy

The csproj compiles `Core/` and `Game/` (not `tools/`) against the game's publicized assemblies. After every build,
its `DeployMod` target copies the DLL, texts, icons, blueprints and models into `release/` and the game's Mods
folder. See `AGENTS.md` for paths and testing, `DESIGN.md` for goals, and `docs/` for plans and decisions.

# AGENTS.md

Yet Another Terrain Generator (mod id `Ximsa.TerrainGenerator`) is a Timberborn mod for the **map editor**. It adds
two bottom-bar tool groups:

- **Terrain Generators:** Autoencoder Generator, 3D Noise Generator, WFC Generator.
- **Terrain Tools:** Terrain Smoothing, Cliff Sharpening, River Carver, Tree Placement.

Every tool opens a dialog with settings; nothing reacts to clicks on the map.

## Layout

| Path | What |
|---|---|
| `Core/<Feature>/` | Timberborn-independent algorithms (also compiled by the offline tools): `Common` (`VoxelMap`, `MapVoxelReader`, `Surface`, `TileQueue`), `Autoencoder`, `Noise3D`, `Wfc`, `Smoothing`, `CliffSharpening`, `Rivers`, `Trees` |
| `Game/<Feature>/` | Map editor code: a tool (`*Tool.cs`) and a dialog (`*Dialog.cs`) per feature; `Game/Common` has `ModStarter`, `Configurator` (Bindito bindings, `MapEditor` context), `MapEditorService` (terrain, entities, undo), `EntityPlacer` (trees, bushes, water sources), `TerrainJobDialog`, group buttons |
| `Localizations/enUS.csv` | All UI texts |
| `ToolGroups/` | `ToolGroupSpec` blueprints, one per bottom-bar group |
| `Sprites/` | 256 x 256 button icons (line drawings: light grey fill, dark outline, transparent background) |
| `Models/` | Autoencoder weights (`*.npz`) and `LatentStats.txt` |
| `tools/TerrainPreview` | Runs generators and tools outside the game and renders PNG previews |
| `tools/AutoencoderTuner` | Latent statistics, decoder sharpening and previews for the autoencoder |
| `docs/` | Plans and design notes, with status tables and decisions |

## Build

- Mod: `dotnet build Timberborn.TerrainGenerator.csproj` (Debug output in `bin/Debug/netstandard2.1`).
  It references the game's assemblies from `F:\GOG\Timberborn\Timberborn_Data\Managed` (publicized, so internal game
  types are usable) and TimberUi and Harmony from the user's Mods folder. Paths are set in the csproj.
- The mod targets **netstandard2.1, C# 10**: no `PriorityQueue`, no newer BCL APIs (use `Core/Common/TileQueue`).
- Offline tools target net9.0 and compile all of `Core/`. Keep `Core/` free of Unity, Timberborn and `Game/` references
  so they keep compiling there (see `ARCHITECTURE.md`).

## Deploy

The game loads the mod from `C:\Users\monik\OneDrive\Dokumente\Timberborn\Mods\TerrainGen`, **not** from this repo.
`release/` (gitignored) mirrors that layout: `version-1.1/` (DLL, `manifest.json`, `Localizations`, `Sprites`,
`ToolGroups`, `Imports`) and `Models/` at the mod root (`ModStarter.OriginPath`).

Every build of the mod runs the `DeployMod` target in the csproj, which copies the DLL, texts, icons, tool group
blueprints and models into both `release/` and the Mods folder (unchanged files are skipped). If the game is running,
the locked DLL only causes a warning: close the game and build again. Skip it with `-p:DeployMod=false`.
`manifest.json` (with the version) and `Imports/` are not tracked and are not copied; edit them in place in both
folders. Restart the game after deploying.

## Testing

- There is no test project. Check algorithms offline with the preview tools, then in game.
  - `dotnet run --project tools/TerrainPreview -- noise3d|wfc|map|sharpen|rivers|trees ... --out preview.png`
    (usage at the top of its `Program.cs`; `--rivers N` works after any map-producing command).
  - `dotnet run --project tools/AutoencoderTuner -c Release -- preview|stats|sharpen ...`
    (see `docs/autoencoder-tuning.md`).
  - Built-in maps are not files on disk; they come from the game (`MapRepository`). For offline work, use user maps
    from `C:\Users\monik\OneDrive\Dokumente\Timberborn\Maps` or extracted copies.
- In game, the log at `%USERPROFILE%\AppData\LocalLow\Mechanistry\Timberborn\Player.log` shows which build ran (each
  job stage logs `[TerrainGenerator] <stage key> took N ms`) and any exceptions.
- Say plainly what was only previewed offline and what was tested in game; `docs/terrain-tools-plan.md` has an
  in-game test checklist.

## Game rules learned the hard way

- Write terrain only through `ITerrainService` (`MapEditorService.SetVoxelsByRow`). Writing `TerrainMap` directly
  changes the data without redrawing.
- `SetTerrain(z, n)` fills upwards from z; `UnsetTerrain(z, n)` removes downwards from z.
- `TerrainPhysicsPatches` skips the game's load-time check that deletes floating terrain; keep it, as the generators
  make overhangs and floating islands.
- Terrain tools edit only the top surface of each column (`Core/Common/Surface`), so 3D terrain below survives.
- Long work runs in `TerrainJobDialog.RunJob`: compute on a background thread (cancellable, reports progress through
  `ProgressReporter` with localization keys as stage names), then apply on the main thread spread over frames.
  Generators remove all entities; terrain tools pass `removeAllEntities: false` to remove only entities in changed
  columns. Spawning trees or water sources goes in `placeEntities`, which also runs over frames.
- Undo: the game records every terrain change made through `ITerrainService` and every entity created or deleted
  as pending undo entries; `RunJob` commits them (`MapEditorService.CommitUndoStep`) before and after applying, so
  each tool action is one Ctrl+Z step. Do not write terrain or entities around these services, or undo misses it.
- Read game state (terrain, water, maps through `MapRepository`) on the main thread before handing off to a
  background task.
- Grouped tool buttons show their name through `IToolDescriptor`; hover tooltips only work on groupless buttons.
- To learn a game API, decompile the assembly from the Managed folder (e.g. `ilspycmd` installed into a scratch
  folder, run with `dotnet --roll-forward Major`), and check that the service is bound in the `MapEditor` context.

## Conventions

- Match the surrounding code: file-scoped namespaces, `readonly` fields without underscores, comments that explain
  why in lowercase sentences, LF line endings.
- Dialogs snapshot their settings (`settings.Clone()`) before starting a job.
- Every run of a generator or tool is seeded with the current Unix timestamp; dialogs have no seed fields. The
  offline tools keep `--seed` for reproducible previews.
- Texts: keys `Ximsa.TerrainGenerator.<Area>.<Name>` in `Localizations/enUS.csv`, one per line as
  `Key,"Text",-`. No trailing spaces inside texts (the game warns about them). Remove keys that are no longer used.
- The 3D Noise generator used an old Minecraft version's generator as a baseline only: do not mention Minecraft or
  "beta" in code or texts, and name things after what they do (e.g. Hilliness, Ruggedness).
- River beds lower the terrain by at most one block; only tunnels (through basins too big to fill) cut deeper.
- Keep the plan documents in `docs/` current: update status tables and record decisions and their reasons.
- Commit only when asked, on the current feature branch, with a short summary line and a body explaining why.

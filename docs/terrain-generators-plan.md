# Two new terrain generators and a generator button group

## Goal

Add two terrain generators next to the existing autoencoder generator:

1. **3D Noise Generator**: a port of Minecraft beta 1.7.3's overworld noise generator (3D density).
2. **WFC Generator**: 3D overlapping-model Wave Function Collapse that learns from a voxel map.

All three generators (**Autoencoder Generator**, **3D Noise Generator**, **WFC Generator**) live in one
bottom-bar button group that expands into three buttons.

## Status (2026-11-07)

| Phase | State |
|---|---|
| 0 Spikes | Done, all four checks confirmed in game |
| 1 Button group | Done, tested in game (Autoencoder Generator) |
| 2 3D apply path, Cancel | Built; not yet tested in game |
| 3 3D Noise Generator | Built and previewed offline; not yet tested in game |
| 4 WFC Generator | Built and previewed offline; not yet tested in game |
| 5 Polish and release | 1.2.0 in `release/version-1.1`, placeholder icons; in-game testing still to do |

Offline previews: `tools/TerrainPreview` renders the generators and any `.timber` map as PNG.

## Decisions

| Topic | Decision |
|---|---|
| Beta terrain shape | Full 3D density noise, so overhangs and arches appear |
| Beta faithfulness | Port beta's noise generator and density code, but use .NET `System.Random` instead of Java's `Random` (seeds will not match Minecraft) |
| Beta focus | Overworld above (and just below) sea level; skip caves, ores and other underground features |
| Beta biome | Rainforest climate everywhere by default (hot and wet, so beta's hill noise applies in full); beta's varying biome climate stays available as an option |
| Floating terrain | Keep floating chunks, like beta |
| Extras | Terrain only: no water sources, trees or bushes |
| WFC model | Overlapping model on **3D voxels**, learning from one or more source maps |
| WFC sources | A checklist of maps: the current map and Diorama are checked by default; the other built-in maps (Cliffside and the rest) and your own saved maps appear when the list is expanded |
| Button group | Native game tool group (`ToolGroupSpec` blueprint plus a tool per generator) |
| Smoothing button | Stays outside the generator group; in the next step it moves into a second group of terrain tools |
| Icons | Placeholder PNGs for now, to be replaced later |

## Height scale

Timberborn maps are about 24 tiles high, while beta worlds are 128 blocks high with sea level at 64.
The 3D Noise Generator only keeps a vertical slice around and above sea level:

- **Base level** (default: sea level - 8 = 56): the beta height that becomes Timberborn height 0.
  Ocean floors end up as shallow basins that can be flooded in the editor.
- **Vertical scale** (default: (128 - 56) / map height, about 3 blocks per tile): beta blocks per
  Timberborn tile.
- **Horizontal scale** (default: same as vertical, to keep slopes in proportion): beta blocks per
  Timberborn tile across the map.

The noise is sampled at the scaled coordinates, rather than generating a full beta world and shrinking it.

## Phases

### Phase 0: Spikes (verify assumptions before building on them)

1. **Writing 3D voxels.** Check that the map editor accepts arbitrary voxel columns, including overhangs
   and floating chunks, either through `ITerrainService.SetTerrain`/`UnsetTerrain` per column segment or
   by writing `TerrainMap._terrainVoxels` and reloading. Check that saving and reloading keeps them.
2. **Native tool group from a mod.** Find the blueprint format for a `ToolGroupSpec` (id, display name
   key, icon) in a mod's `Blueprints` folder, and check that `ToolGroupButtonFactory` can be used from
   mod code to create the group button and add tool buttons to it.
3. **Tool that opens a dialog.** An `ITool` whose `Enter` opens the generator dialog and then switches
   back to the default tool, so clicking a group button behaves like a normal button.
4. **Reading other maps.** Load built-in maps (e.g. Diorama, Cliffside) and user maps through `MapRepository`/`MapDeserializer`
   and decode their terrain voxels (the `TerrainMap` entry of the serialized world).

#### Phase 0 results (2026-11-07)

1. **3D voxels.** Terrain is stored as a full 3D voxel grid (`TerrainMap`, saved as `Voxels`), so
   overhangs and floating chunks can be stored. Writes must go through `ITerrainService`:
   - `SetTerrain(z, n)` fills voxels z .. z+n-1 upwards; `UnsetTerrain(z, n)` removes z, z-1 .. z-n+1
     downwards. Floating platforms, arches and holes under the surface all work and render.
   - Writing directly into `TerrainMap.SetTerrain` changes the data but does not redraw the terrain,
     because it skips the service's change events. Do not use it.
   - On load, the game's `TerrainPhysicsPostLoader.Validate` deletes terrain (and buildings) that is not
     connected to the ground. The mod skips it with a Harmony patch (`TerrainPhysicsPatches.cs`), in the
     map editor and in the game. Confirmed: floating terrain now survives saving and reloading.
     Live terrain physics (collapsing when support is removed) is unchanged.
   - Grouped tool buttons show their name through `IToolDescriptor` (the game's description panel);
     the hover label (`InitializeTooltip`) only works on groupless tool buttons.
2. **Native tool group.** Works. `ToolGroups/ToolGroups.<Name>.blueprint.json` in the mod registers a
   `ToolGroupSpec`; the group is built with `ToolGroupService.GetGroup`, `ToolGroupButtonFactory.CreateGreen`,
   `ToolButtonFactory.Create`, `ToolButton.InitializeTooltip`, `ToolGroupButton.AddTool`,
   `ToolGroupService.AssignToGroup` and `BottomBarElement.CreateMultiLevel`. Buttons are icon-only with
   the name as hover tooltip.
3. **Tool that opens a dialog.** `Enter` followed by `ToolService.SwitchToDefaultTool()` works without
   errors. Confirmed with the Autoencoder Generator dialog.
4. **Reading other maps.** Works for all 22 built-in maps and the user's maps (8-700 ms each) through
   `MapRepository.OpenMap` and `MapVoxelReader` (zip + `world.json`: `MapSize.Size` and
   `TerrainMap.Voxels.Array`, layer by layer from the bottom, x fastest). `MapDeserializer` is avoided
   because it also replaces the game's current world data. Built-in names starting with `_`
   (`_Mini`, `_TerrainTest`, `_WaterPerformanceTest`) are dev maps and are hidden from the WFC checklist.
   Map heights differ (23 for built-in maps, up to 71 with Quadruple Terrain Bounds).

### Phase 1: Button group

- Add the `ToolGroupSpec` blueprint, group icon and display name.
- Turn the existing autoencoder generator into a tool inside the group, named "Autoencoder Generator".
- Build the group code so a second group can reuse it (see "Next step": a terrain tools group).
- Keep the smoothing button as it is for now.
- Check in game: the group opens and closes like the game's groups, and the existing generator still works.

### Phase 2: 3D apply path

- `MapEditorService`: add a row-by-row 3D apply (`Set3DTerrainByRow(bool[,,])`), like `Set2DTerrainByRow`.
- `TerrainJobDialog`: let a job return either a 2D heightmap or a 3D voxel grid.
- Add cancellation (a Cancel button using a `CancellationToken`), since WFC runs can take a while.

### Phase 3: 3D Noise Generator

- Port the noise classes: improved Perlin noise, octave noise, and the octave simplex noise used for
  biome temperature and humidity.
- Port the overworld density function: low/high limit noise, main (selector) noise, scale and depth
  noise, biome influence, top fade-out, density grid with trilinear interpolation.
- Leave out caves, ores, surface blocks and decorations.
- Map to Timberborn with base level, vertical scale and horizontal scale.
- Dialog settings: seed (with a randomize button), base level, vertical scale, horizontal scale,
  and beta's main constants (coordinate scales) as advanced sliders with beta's values as defaults.
- Progress stages: biome noise, density noise, interpolation, applying.

### Phase 4: WFC Generator

Adjusted after the first previews (single-voxel 2x2x2 patterns degenerate into pillars; 3x3x3 patterns at full
resolution give thousands of patterns and are far too slow):

- **Coarse grid:** WFC runs on blocks of 4 x 4 x 4 voxels (fixed). A block is solid when at least the solid threshold
  (setting, default 25%) of its voxels is; the result is scaled back up, so heights come in steps of 4 (terraces and cliffs).
- **Patterns:** 3 x 3 x 3 coarse blocks (12 x 12 x 12 tiles) by default, sliding window with stride 1, deduplicated by
  exact content, rotations and mirror images folded in. Never turned upside down.
- **Height-aware, softly:** every pattern may appear at every height; its weight halves for each coarse layer away from
  where it was seen (height penalty setting, default 0.5). No hard height limits.
- **Pruning:** only the most frequent patterns that together cover 95% of all occurrences are kept (pattern coverage
  setting). Rare patterns are most of the count for large source sets (all 19 built-in maps: 24,646 -> 4,763) but
  little of their terrain.
- **Compatibility:** built during learning with one hashmap per direction, from overlap signature to the bitset of
  patterns with that overlap.
- **Propagation:** each cell remembers the patterns it lost since it was last processed; only neighbour patterns those
  lost patterns supported (from the direction tables) are re-checked. 128 x 128 with Diorama + Cliffside: about 12 s
  outside the game; 256 x 256: about 30 s per attempt.
- On contradiction: backtracking with an undo log (undo the last choice, ban it, propagate again; step further back if
  needed), up to 2,000 undone choices per attempt, then restart with a new seed (up to 5 attempts).
- Source checklist: "Current map" and "Diorama" are checked by default. An expandable "More maps"
  section lists, unchecked, the other built-in maps (from `MapRepository.GetBuiltinMapNames()`, hiding names that
  start with `_`) and the user's own saved maps (from `MapRepository.GetUserMapNames()`), in two labelled sub-lists.
  Patterns and frequencies from all checked maps are merged. Generate is disabled while nothing is checked.
- Built-in maps are loaded at runtime from the game itself, through
  `MapRepository.OpenMap(MapFileReference.FromResource(name))` and `MapVoxelReader`. Nothing is copied into the mod.
- Progress: fraction of decided cells.
- Dialog settings: source checklist, pattern size (2 or 3), seed, symmetry, periodic input, height tolerance.
- Open: variety. With few small sources, their features repeat across big maps; more sources help.

### Phase 5: Polish and release

- Placeholder icons for the group, 3D Noise Generator and WFC Generator buttons (same size and style as existing sprites).
- Localization keys for all new texts.
- Optional: a small test project for the noise and WFC code, which does not depend on the game.
- Bump the mod version to 1.2.0 and update the release folder.

## Next step (not part of this plan): terrain tools group

A second button group, built with the same group code as the generator group, holding:

- **Terrain smoothing** (the existing Global Terrain Filter, moved into the group)
- **River carver** (new)
- **Tree placement** (new)
- **Cliff sharpening** (new): a post-generation filter that segregates terrain into flatter terraces with
  steeper cliffs between them, like typical Timberborn maps, for easier building placement

To be planned in detail after this plan is done. Note that tree placement will need to spawn natural
resources, which this plan's generators deliberately leave out.

## Risks

- **WFC on 3D voxels is heavy.** A 256×256×24 map has about 1.5 million cells. With hundreds of
  patterns, memory is fine (bitsets), but runtime could be minutes and contradictions more likely.
  Mitigations: N = 2 by default, ground and sky constraints, restarts, block-wise fallback, Cancel.
- **The game may not like floating terrain or some overhang shapes.** Phase 0 checks this first.
- **The native group needs blueprint and spec details** not yet confirmed for 1.x mods. Phase 0 checks
  this; the fallback is a custom group built from the game's `ToolGroupButton` template.

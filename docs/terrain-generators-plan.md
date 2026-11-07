# Two new terrain generators and a generator button group

## Goal

Add two terrain generators next to the existing autoencoder generator:

1. **3D Noise Generator**: a port of Minecraft beta 1.7.3's overworld noise generator (3D density).
2. **WFC Generator**: 3D overlapping-model Wave Function Collapse that learns from a voxel map.

All three generators (**Autoencoder Generator**, **3D Noise Generator**, **WFC Generator**) live in one
bottom-bar button group that expands into three buttons.

## Decisions

| Topic | Decision |
|---|---|
| Beta terrain shape | Full 3D density noise, so overhangs and arches appear |
| Beta faithfulness | Port beta's noise generator and density code, but use .NET `System.Random` instead of Java's `Random` (seeds will not match Minecraft) |
| Beta focus | Overworld above (and just below) sea level; skip caves, ores and other underground features |
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

- Learn N×N×N voxel patterns (default N = 2, optional 3) with their frequencies from all checked source maps combined.
- Constrain the bottom layer to solid and the top layer to air to cut down contradictions.
- Lowest-entropy cell selection, weighted by pattern frequency, with seeded random choices.
- Propagation using precomputed pattern compatibility per direction; bitset storage per cell.
- On contradiction: restart with a new seed, up to a retry limit. If full-map runs prove too slow or
  fail too often, fall back to generating the map in overlapping blocks.
- Source checklist: "Current map" and "Diorama" are checked by default. An expandable "More maps"
  section lists, unchecked, the other built-in maps (from `MapRepository.GetBuiltinMapNames()`) and
  the user's own saved maps (from `MapRepository.GetUserMapNames()`, loaded with
  `MapFileReference.FromUserFolder(name)`), in two labelled sub-lists.
  Patterns and frequencies from all checked maps are merged. Generate is disabled while nothing is checked.
- Built-in maps are loaded at runtime from the game itself, through
  `MapRepository.OpenMap(MapFileReference.FromResource(name))` and `MapDeserializer.Load`, and
  their terrain voxels are read from the serialized `TerrainMap`. Nothing is copied into the mod, so
  no game content is redistributed. Phase 0 checks how to decode the voxels from the serialized map.
- Progress: fraction of collapsed cells.
- Dialog settings: source checklist, pattern size, seed, symmetry (rotations/reflections on or off),
  periodic input on or off, retry limit.

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

To be planned in detail after this plan is done. Note that tree placement will need to spawn natural
resources, which this plan's generators deliberately leave out.

## Risks

- **WFC on 3D voxels is heavy.** A 256×256×24 map has about 1.5 million cells. With hundreds of
  patterns, memory is fine (bitsets), but runtime could be minutes and contradictions more likely.
  Mitigations: N = 2 by default, ground and sky constraints, restarts, block-wise fallback, Cancel.
- **The game may not like floating terrain or some overhang shapes.** Phase 0 checks this first.
- **The native group needs blueprint and spec details** not yet confirmed for 1.x mods. Phase 0 checks
  this; the fallback is a custom group built from the game's `ToolGroupButton` template.

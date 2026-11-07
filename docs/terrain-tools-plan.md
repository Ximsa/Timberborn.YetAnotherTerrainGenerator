# Terrain tools group

## Goal

A second bottom-bar button group, **Terrain Tools**, built with the same group code as the generator group
(`ToolGroupBottomBarButton`), holding four tools that post-process a generated map:

1. **Terrain Smoothing**: the existing Global Terrain Filter, moved into the group.
2. **Cliff Sharpening**: splits the terrain into flat plateaus with cliffs between them.
3. **River Carver**: carves river beds and places a water source at each river's head.
4. **Tree Placement**: places forests of trees and patches of bushes.

## Status (2026-11-07)

| Phase | State |
|---|---|
| 0 Spikes | API found in game code (see findings); not yet confirmed in game |
| 1 Group, smoothing, surface editing | Built; not yet tested in game |
| 2 Cliff Sharpening | Built and previewed offline; not yet tested in game |
| 3 River Carver | Built (dialog, water sources) and previewed offline; not yet tested in game |
| 4 Tree Placement | Built (dialog, spawning) and previewed offline; not yet tested in game |
| 5 Polish and release | Placeholder icons for all tools, texts done, release folder updated; version bump still to do |

## Decisions

| Topic | Decision |
|---|---|
| Order | Group and smoothing first, then Cliff Sharpening, River Carver, Tree Placement |
| 3D terrain | Tools only change the top surface of each column; voxels below the topmost solid run (overhang undersides, caves, floating chunks under it) are kept |
| Entities | Terrain tools remove only entities on columns whose surface changed; the generators keep wiping everything |
| Cliff Sharpening | Region-based plateaus: watershed regions on the smoothed height, each region flattened to its median height snapped to a step size |
| River placement | Pick a random point, hill-climb to the top, put the river head half way between the point and the top |
| River course | Steepest descent from the head, filling pits on the way, until a map edge, another river or a large basin, which becomes a lake |
| River depth | A river lowers the terrain by at most 1 block anywhere |
| River water | A water source entity at each river head |
| Trees | Noise-driven forest patches mixing tree species, and separate noise-driven patches mixing bush species; trees and bushes never share a patch |
| Existing trees | Tree Placement removes all natural resources first, then places new ones |
| Mode | All four tools are dialog tools (like the generators): settings, a seed, Apply |

## Surface editing

The terrain tools work on a heightmap, but must not destroy 3D terrain:

- **Surface height** of a column: one above the top voxel of its topmost solid run (0 for an empty column).
- A tool computes a new surface height per column. Raising fills voxels from the old surface up to the new one;
  lowering removes voxels from the old surface down to the new one. Only the topmost run changes; anything under it
  (gaps, overhang undersides) is kept unless lowering cuts into it.
- Implemented as `SurfaceEdit` (old and new heightmap, plus the current `VoxelMap`), producing the target
  `VoxelMap` for `MapEditorService.SetVoxelsByRow`. Smoothing switches from `sum(0)` (which flattened the map) to this.
- `TerrainJobDialog` gets a flag for whether a job wipes all entities (generators) or only those on changed columns
  (tools). Changed columns come from comparing old and new terrain.

## Phases

### Phase 0: Spikes

1. **Spawning natural resources in the map editor.** Find how the editor's natural resource brush places trees and
   bushes (`Timberborn.MapEditorNaturalResources`, `MapEditorPlacementRandomizing`), and spawn one tree and one bush
   from mod code at a given tile. List the available tree and bush templates (including faction-specific ones) and how
   to tell trees from bushes. Check that they survive saving and reloading, and whether they need to start fully grown.
2. **Spawning water sources.** Spawn a water source entity (`Timberborn.WaterSourceSystem`) at a tile from mod code,
   set its strength, check that water flows when simulation runs and survives saving.
3. **Entities per column.** Find all entities whose blocks touch a set of columns (`BlockService`), to remove only the
   affected ones. Check that removing natural resources this way is clean (no leftover reservations or models).

#### Phase 0 findings from the game code (2026-11-07, still to confirm in game)

1. **Natural resources.** The editor brush uses `NaturalResourceSpawner.Spawn(spawnables, coordinates,
   gatherablePercent)` (`Timberborn.MapEditorNaturalResources`): it skips occupied tiles, spawns through
   `NaturalResourceFactory.SpawnIgnoringConstraintsAndRandomizePosition` and grows the resource (fully when not a
   seedling, plus a random share of gatherable yield). The brush panel lists species with
   `TemplateService.GetAll<NaturalResourceSpec>()` filtered by `UsableWithCurrentFeatureToggles`, ordered by `Order`,
   id = `TemplateSpec.TemplateName`. Trees have a `TreeComponentSpec`, bushes a `BushSpec` (`Timberborn.Forestry`);
   anything with neither (crops) is ignored.
2. **Water sources.** `WaterSource` (with internal `WaterSourceSpec`: footprint `Coordinates`, `DefaultStrength`) is a
   block object; strength is set with `WaterSource.SetSpecifiedStrength`. Spawn by finding templates with a
   `WaterSourceSpec` and creating them with `BlockObjectFactory.CreateFinished(new EntitySetup.Builder(blueprint),
   new Placement(coordinates))`, as `NaturalResourceFactory` does; skip badwater sources (contamination above 0).

### Phase 1: Group, smoothing, surface editing

- Add `ToolGroups.TerrainTools.blueprint.json`, a group icon (placeholder) and display name.
- `TerrainToolsGroupButton : ToolGroupBottomBarButton` with Terrain Smoothing as its first tool; remove
  `TerrainFilterButton`.
- Add `SurfaceEdit` and the per-column entity removal; switch smoothing to surface editing.
- Check in game: the group sits next to the generator group, smoothing works and keeps overhangs.

### Phase 2: Cliff Sharpening

- Smooth the surface heightmap (Gaussian, radius from the region size setting).
- Watershed segmentation of the smoothed surface's slope (priority flood from the slope's local minima), so region
  borders run along the steepest ground.
- Each watershed region is split into connected bands one step of height apart (on a lightly smoothed surface):
  a long even slope has no slope minima inside it, and without this it ended up flattened into the regions above
  and below; with it, slopes become stacked terraces and narrow ridges keep their height.
- Regions smaller than the minimum region area merge into the neighbour with the closest mean height, but only when
  that neighbour is less than one step away (real small peaks and pits stay, specks under a quarter of the minimum
  area always merge).
- Each region gets the median of its original surface heights, snapped to the step size (halves round up).
- No ramps: beavers need stairs for any terrain step anyway.
- Settings: region size, minimum region area, step size (default 2), strength (blend between original and plateau
  height, default full). One progress stage.
- Offline preview: `TerrainPreview sharpen <file.timber>`. On ThousandIslands (256 x 256) it takes about 0.7 s and
  keeps every height level (e.g. tiles at 16 stay 16, at 12 stay 12 on average).

### Phase 3: River Carver

- For each river: pick random columns (seeded), hill-climb each (steepest ascent on a lightly smoothed surface, so
  terrace flats do not stall it) to a local top, and put the head on the climbed path at the first point half way up
  in height. Of 50 random starts the one with the biggest climb wins (heads on small bumps gave rivers of a few
  tiles); heads within a sixth of the map from the edge, or near earlier heads, are skipped.
- Course: from the head, follow steepest descent; at a pit or flat, flood lowest first (ties broken by distance, so
  paths across flats are straight) until the water spills over the rim, then continue from there. Stop at a map
  edge, on an earlier river, or when the flooded area below the water level reaches the lake size; that basin
  becomes a lake.
- Carve: **at most 1 block below the original terrain, anywhere**: the bed follows a line that never
  rises from head to mouth where the 1-block limit allows, across the width setting; lakes too. No banks, no depth
  setting. Rims are not cut through; the water fills pits on its way, as Timberborn water does.
- Place a water source at the head.
- Settings: number of rivers, width, lake size, water source strength, seed. One progress stage.
- Offline preview: `TerrainPreview rivers <file.timber>`, or `--rivers N` after any map-producing command (e.g.
  `noise3d`). Water is drawn blue, heads red.

### Phase 4: Tree Placement

- Remove all natural resources on the map.
- Two independent seeded noise fields (octave Perlin from the noise generator): one for forests, one for bushes.
  A tile is forest where the forest noise is above the forest threshold, bush where the bush noise is above its
  threshold and the tile is not forest.
- Inside a patch, a second, small-scale noise picks the species, so species come in clumps; species weights decide
  each species' share. Trees only pick tree species, bushes only bush species.
- Density inside patches, higher near water (distance to water or carved river beds), none on tiles where the
  surface slope is steep, at cliff edges, or on water.
- Settings: forest coverage, bush coverage, patch size, density, tree species weights, bush species weights, seed.
- Progress stages: removing, choosing tiles, placing.

### Phase 5: Polish and release

- Placeholder icons for the group and the three new tools.
- Localization keys for all new texts.
- Version bump and release folder update.

## Risks

- **Spawning entities from mod code** may need internals of the editor's brush (template lookup, placement
  validation). Phase 0 checks this; the fallback is to reuse the editor's randomized placement service directly.
- **Surface editing and floating terrain:** lowering a surface can cut into a floating chunk's top; that is
  intended, but can leave thin shelves.
- **Watershed over-segmentation** on noisy maps: the smoothing radius and minimum region area control it.
- **Water sources on carved beds** may flood more than the river when the course is shallow; depth and strength
  settings, plus testing on several generated maps.

## In-game test checklist

Built against the game's assemblies but not run in game yet (2026-11-07). In the map editor:

1. The Terrain Tools group shows four tools; each opens its dialog and the hover text names it.
2. Terrain Smoothing and Cliff Sharpening keep overhangs, and only remove entities in changed columns.
3. River Carver: rivers are at most one block deep; a water source appears at each head with the chosen strength;
   water flows when the simulation runs; the water sources survive saving and reloading. `EntityPlacer` picks the
   smallest clean, unregulated water source template; check it is the expected one.
4. Tree Placement: the species sliders list the faction's trees and bushes by name; old trees and bushes are
   removed; new ones are grown, survive saving and reloading, and stay off water (water is read from the game's
   water map, so it must already exist, e.g. after running the simulation once).

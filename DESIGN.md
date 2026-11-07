# Design

## Goal

Make good Timberborn maps quickly in the map editor: generate varied terrain, then turn it into terrain that plays
like a Timberborn map, with flat ground to build on, cliffs between levels, rivers with water and forests, in a few
clicks instead of hours of brushing.

## Workflow

1. **Generate** terrain with one of three generators, each with its own character.
2. **Shape** it with the terrain tools, in any order and as often as wanted.
3. **Finish** by hand with the game's own editor tools.

| Step | Tool | What it is for |
|---|---|---|
| Generate | Autoencoder Generator | Heightmaps in the style of existing maps, from a small neural network trained on maps; can blend in the current map |
| Generate | 3D Noise Generator | 3D noise terrain with overhangs, arches and floating islands; Hilliness and Ruggedness set its character |
| Generate | WFC Generator | Terrain assembled from 3D patterns learned from chosen maps (the current one, built-in ones, the user's own) |
| Shape | Terrain Smoothing | Calms rough terrain |
| Shape | Cliff Sharpening | Splits terrain into flat plateaus with cliffs between them, so buildings fit |
| Shape | River Carver | Rivers from half way up hills down to the map edge or a lake, with a water source at each head |
| Shape | Tree Placement | Forests and bush patches, denser near water, off cliff edges |

## Principles

- **Timberborn first.** Results are judged by how they play in Timberborn: buildable flat areas, readable cliffs,
  water that flows. Generators are baselines to borrow from, not things to reproduce; the 3D Noise generator started
  from an old Minecraft terrain generator, but its controls and names describe Timberborn terrain, and the code does
  not refer to its origin.
- **Variety.** Each run should give a different map. Runs are seeded with the current time, with no seed fields to
  manage. Settings change the character of the terrain, not the dice.
- **3D terrain is welcome.** Overhangs and floating terrain are part of what the generators make; the mod keeps the
  game from deleting floating terrain on load, and the terrain tools only change the top surface of each column so
  what lies below survives.
- **Gentle tools.** Tools change what they need and leave the rest: terrain tools only remove entities in columns
  whose terrain changed, rivers lower the ground by at most one block, Tree Placement only replaces trees and bushes.
- **Simple controls.** Every tool is one dialog with a few sliders whose names say what they do (Hilliness,
  Ruggedness, Variety, Width), with defaults that give a good result on their own.
- **Responsive editor.** Long computations run in the background with progress and Cancel; changes to the map are
  spread over frames, and the map is never left half changed.
- **Previewable.** Algorithms are independent of the game, so they can be run and judged offline on real maps with
  the preview tools before anyone starts Timberborn.

## Scope

- **In:** map editor tools for terrain, water sources at river heads, trees and bushes.
- **Out:** the game itself (no in-game tools), caves and underground features, resources other than trees and
  bushes, buildings, hand-placed details.

## Open directions

- In-game testing of everything built since the generator group (see the checklist in `docs/terrain-tools-plan.md`).
- Final icons for all tools.
- More variety from the Autoencoder Generator: fine-tuning on the built-in maps (see `docs/autoencoder-tuning.md`).
- WFC speed on large maps with many patterns (one in-game run took about 6.5 minutes).

Details, decisions and their reasons are in the plans in `docs/`; how the code is organised is in `ARCHITECTURE.md`, how
to work in the repo in `AGENTS.md`.

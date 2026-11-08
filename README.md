# Yet Another Terrain Generator

A Timberborn mod (`Ximsa.TerrainGenerator`) that adds terrain generators and terrain tools to the **map editor**:

- **Terrain Generators:** Autoencoder Generator, 3D Noise Generator, WFC Generator.
- **Terrain Tools:** Terrain Smoothing, Cliff Sharpening, Tree Placement.

Each tool opens a dialog with a few settings; every action can be undone in one step with Ctrl+Z.

## Documentation

- [DESIGN.md](DESIGN.md): goals, workflow, principles and scope.
- [ARCHITECTURE.md](ARCHITECTURE.md): how the code is organised (`Core/` vs `Game/`, by feature) and how a tool action
  flows through the mod.
- [AGENTS.md](AGENTS.md): building, deploying, testing and conventions for working in the repo.
- [docs/](docs): plans, decisions and tuning notes.

## Agentic coding

Since commit `c02795a` ("Add 3D voxel apply path and cancellable terrain jobs", 2026-11-07), this project is developed
with agentic coding (Claude Code). Those commits carry a `Co-Authored-By: Claude` trailer.

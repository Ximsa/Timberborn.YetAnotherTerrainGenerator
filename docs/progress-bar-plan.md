# Progress bar for the terrain generator

## Problem

`TerrainGeneratorDialog.OnGenerate` runs the whole pipeline synchronously on Unity's main thread.
The UI cannot redraw until it returns, so the game freezes and a progress bar would never update.

## Plan

1. **Background computation**
   - Read the current terrain on the main thread (`MapEditorService.GetTerrain` touches game state).
   - Run the rest of `Pipeline2D` with `Task.Run`: load models, encode, noise, decode, zoom, trim,
     median filter, normalize. This is pure NumSharp maths and safe off the main thread.

2. **Progress reporting**
   - A small thread-safe progress object holds a fraction (0..1) and a stage label.
   - Each pipeline stage updates it. The median filter reports once per pass/row for a smooth bar.
   - Stage weights are based on measured timings (each stage logs its duration).

3. **UI updates**
   - The dialog shows a progress bar with a stage label (TimberUI progress bar helpers).
   - A scheduled callback on the dialog (`schedule.Execute(...).Every(100)`) copies the progress
     into the bar.

4. **Applying the result on the main thread**
   - `RemoveAllEntityComponents` and `Set2DTerrain` change game state, so they stay on the main thread.
   - `Set2DTerrain` is split across frames (a few rows per frame) and reports its own progress.

5. **Robustness**
   - Disable the Generate button while running so it cannot be started twice.
   - Show an error in the dialog (and log it) if a stage fails.
   - Stop updating if the dialog is closed mid-run.

6. **Terrain smoothing**
   - Apply the same approach to `TerrainFilterDialog` (the "Global Terrain Filter" smoothing tool).
   - Shared code lives in a common dialog base class (`TerrainJobDialog`) and a `ProgressReporter`,
     so both dialogs only describe their computation stages.

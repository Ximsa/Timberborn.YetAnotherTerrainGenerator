using System.Diagnostics;
using NumSharp;
using Timberborn.Localization;
using TimberUi.CommonUi;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace Timberborn.TerrainGenerator;

// Dialog that computes a new heightmap on a background thread and then applies it to the map
// over several frames, showing a progress bar for both phases.
public abstract class TerrainJobDialog : DialogBoxElement
{
    protected const string LoadingModelsKey = "Ximsa.TerrainGenerator.Progress.LoadingModels";
    protected const string EncodingKey = "Ximsa.TerrainGenerator.Progress.Encoding";
    protected const string DecodingKey = "Ximsa.TerrainGenerator.Progress.Decoding";
    protected const string ScalingKey = "Ximsa.TerrainGenerator.Progress.Scaling";
    protected const string FilteringKey = "Ximsa.TerrainGenerator.Progress.Filtering";
    private const string ApplyingKey = "Ximsa.TerrainGenerator.Progress.Applying";
    private const string DoneKey = "Ximsa.TerrainGenerator.Progress.Done";
    private const string FailedKey = "Ximsa.TerrainGenerator.Progress.Failed";

    // share of the progress bar used by the background computation; the rest is for applying the terrain
    private const float ComputeShare = 0.7f;
    private const int FrameBudgetMs = 15;

    protected readonly ILoc loc;
    protected readonly MapEditorService mapEditorService;

    private readonly ProgressReporter progress = new();
    private bool applying;
    private bool closed;
    private ProgressBarWithLabel progressBar;
    private VisualElement progressContainer = null!;

    protected TerrainJobDialog(ILoc loc, MapEditorService mapEditorService)
    {
        this.loc = loc;
        this.mapEditorService = mapEditorService;
        RegisterCallback<AttachToPanelEvent>(_ => closed = false);
        RegisterCallback<DetachFromPanelEvent>(_ => closed = true);
    }

    protected void AddProgressBar()
    {
        progressContainer = new VisualElement();
        progressBar = progressContainer.AddProgressBarWithLabel(ProgressBarColor.Green);
        progressContainer.SetDisplay(false);
        Content.Add(progressContainer);
    }

    // compute receives the current terrain voxels and the map size, and runs on a background thread
    protected async void RunJob(VisualElement startButton,
        Func<NDArray, Vector3Int, ProgressReporter, NDArray> compute)
    {
        startButton.SetEnabled(false);
        progressContainer.SetDisplay(true);
        applying = false;
        progress.Report(LoadingModelsKey, 0);
        var refresh = schedule.Execute(UpdateProgressBar).Every(100);
        try
        {
            // game state is read (and copied, as it may share memory with the game) on the main thread
            // before handing off to the background
            var terrain = mapEditorService.GetTerrain().Clone();
            var mapSize = mapEditorService.MapSize;
            var heightmap = await Task.Run(() => compute(terrain, mapSize, progress));
            if (closed)
            {
                Debug.Log("[TerrainGenerator] dialog closed before the terrain was applied, discarding result");
                return;
            }

            applying = true;
            mapEditorService.RemoveAllEntityComponents();
            var stopwatch = Stopwatch.StartNew();
            var frame = Stopwatch.StartNew();
            foreach (var rowFraction in mapEditorService.Set2DTerrainByRow(heightmap))
            {
                progress.Report(ApplyingKey, rowFraction);
                if (frame.ElapsedMilliseconds < FrameBudgetMs) continue;
                UpdateProgressBar();
                // resumes on the main thread next frame, so the UI can redraw in between
                await Task.Yield();
                frame.Restart();
            }

            Debug.Log($"[TerrainGenerator] {ApplyingKey} took {stopwatch.ElapsedMilliseconds} ms");
            progress.Report(DoneKey, 1);
            UpdateProgressBar();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            progressBar.SetProgress(0, $"{loc.T(FailedKey)}: {exception.Message}");
        }
        finally
        {
            refresh.Pause();
            startButton.SetEnabled(true);
        }
    }

    private void UpdateProgressBar()
    {
        var (stageKey, fraction) = progress.Current;
        var overall = applying ? ComputeShare + fraction * (1 - ComputeShare) : fraction * ComputeShare;
        progressBar.SetProgress(overall, $"{loc.T(stageKey)} ({overall * 100:F0}%)");
    }
}

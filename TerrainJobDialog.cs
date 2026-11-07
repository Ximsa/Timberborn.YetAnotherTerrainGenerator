using System.Diagnostics;
using NumSharp;
using Timberborn.Localization;
using TimberUi.CommonUi;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace Timberborn.TerrainGenerator;

// Dialog that computes new terrain on a background thread and then applies it to the map
// over several frames, showing a progress bar for both phases. The computation can be cancelled;
// applying cannot, so the map is never left half changed.
public abstract class TerrainJobDialog : DialogBoxElement
{
    protected const string LoadingModelsKey = "Ximsa.TerrainGenerator.Progress.LoadingModels";
    protected const string EncodingKey = "Ximsa.TerrainGenerator.Progress.Encoding";
    protected const string DecodingKey = "Ximsa.TerrainGenerator.Progress.Decoding";
    protected const string ScalingKey = "Ximsa.TerrainGenerator.Progress.Scaling";
    protected const string FilteringKey = "Ximsa.TerrainGenerator.Progress.Filtering";
    private const string StartingKey = "Ximsa.TerrainGenerator.Progress.Starting";
    private const string ApplyingKey = "Ximsa.TerrainGenerator.Progress.Applying";
    private const string DoneKey = "Ximsa.TerrainGenerator.Progress.Done";
    private const string FailedKey = "Ximsa.TerrainGenerator.Progress.Failed";
    private const string CancelledKey = "Ximsa.TerrainGenerator.Progress.Cancelled";
    private const string CancelKey = "Ximsa.TerrainGenerator.Cancel";

    // share of the progress bar used by the background computation; the rest is for applying the terrain
    private const float ComputeShare = 0.7f;
    private const int FrameBudgetMs = 15;

    protected readonly ILoc loc;
    protected readonly MapEditorService mapEditorService;

    private bool applying;
    private VisualElement cancelButton = null!;
    private CancellationTokenSource? cancellation;
    private ProgressReporter progress = new();
    private ProgressBarWithLabel progressBar;
    private VisualElement progressContainer = null!;

    protected TerrainJobDialog(ILoc loc, MapEditorService mapEditorService)
    {
        this.loc = loc;
        this.mapEditorService = mapEditorService;
        // closing the dialog cancels a running computation
        RegisterCallback<DetachFromPanelEvent>(_ => cancellation?.Cancel());
    }

    protected void AddProgressBar()
    {
        progressContainer = new VisualElement();
        progressBar = progressContainer.AddProgressBarWithLabel(ProgressBarColor.Green);
        cancelButton = progressContainer.AddButton(loc.T(CancelKey), CancelKey, () => cancellation?.Cancel());
        progressContainer.SetDisplay(false);
        Content.Add(progressContainer);
    }

    // compute receives the current terrain voxels (shaped z, y, x) and the map size, runs on a background thread
    // and returns the complete new terrain of the map
    protected async void RunJob(VisualElement startButton,
        Func<NDArray, Vector3Int, ProgressReporter, VoxelMap> compute)
    {
        startButton.SetEnabled(false);
        progressContainer.SetDisplay(true);
        cancelButton.SetDisplay(true);
        applying = false;
        cancellation = new CancellationTokenSource();
        progress = new ProgressReporter(cancellation.Token);
        progress.Report(StartingKey, 0);
        var refresh = schedule.Execute(UpdateProgressBar).Every(100);
        try
        {
            // game state is read (and copied, as it may share memory with the game) on the main thread
            // before handing off to the background
            var terrain = mapEditorService.GetTerrain().Clone();
            var mapSize = mapEditorService.MapSize;
            var jobProgress = progress;
            var voxels = await Task.Run(() => compute(terrain, mapSize, jobProgress), cancellation.Token);

            // applying cannot be cancelled, so it gets a reporter without the cancellation token
            applying = true;
            cancelButton.SetDisplay(false);
            progress = new ProgressReporter();
            mapEditorService.RemoveAllEntityComponents();
            var stopwatch = Stopwatch.StartNew();
            var frame = Stopwatch.StartNew();
            foreach (var rowFraction in mapEditorService.SetVoxelsByRow(voxels))
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
        catch (OperationCanceledException)
        {
            Debug.Log("[TerrainGenerator] job cancelled, terrain left unchanged");
            progressBar.SetProgress(0, loc.T(CancelledKey));
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            progressBar.SetProgress(0, $"{loc.T(FailedKey)}: {exception.Message}");
        }
        finally
        {
            refresh.Pause();
            cancelButton.SetDisplay(false);
            cancellation.Dispose();
            cancellation = null;
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

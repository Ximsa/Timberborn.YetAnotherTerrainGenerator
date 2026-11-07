using Timberborn.Localization;
using Timberborn.TerrainGenerator.Core.Common;
using Timberborn.TerrainGenerator.Core.Rivers;
using Timberborn.TerrainGenerator.Game.Common;
using TimberUi.CommonUi;
using UnityEngine;
using UnityEngine.UIElements;

namespace Timberborn.TerrainGenerator.Game.Rivers;

public class RiverCarverDialog : TerrainJobDialog
{
    private const string TitleKey = "Ximsa.TerrainGenerator.Tool.RiverCarver";
    private const string RiversKey = "Ximsa.TerrainGenerator.RiverCarver.Rivers";
    private const string WidthKey = "Ximsa.TerrainGenerator.RiverCarver.Width";
    private const string HeadDistanceKey = "Ximsa.TerrainGenerator.RiverCarver.HeadDistance";
    private const string MaxBasinVolumeKey = "Ximsa.TerrainGenerator.RiverCarver.MaxBasinVolume";
    private const string FillDipAreaKey = "Ximsa.TerrainGenerator.RiverCarver.FillDipArea";
    private const string SourceStrengthKey = "Ximsa.TerrainGenerator.RiverCarver.SourceStrength";
    private const string ApplyKey = "Ximsa.TerrainGenerator.Apply";
    private const string CarvingKey = "Ximsa.TerrainGenerator.Progress.Carving";

    private readonly VisualElement applyButton;
    private readonly EntityPlacer entityPlacer;
    private readonly RiverCarverSettings settings = new();

    public RiverCarverDialog(ILoc loc, MapEditorService mapEditorService, EntityPlacer entityPlacer) :
        base(loc, mapEditorService)
    {
        this.entityPlacer = entityPlacer;
        SetTitle(loc.T(TitleKey));
        Content.Add(new GameSliderInt()
            .SetLabel(loc.T(RiversKey))
            .SetHorizontalSlider(new SliderValues<int>(1, 4, settings.Rivers))
            .RegisterChange(rivers => settings.Rivers = rivers)
            .AddEndLabel(value => $"{value}"));
        Content.Add(new GameSliderInt()
            .SetLabel(loc.T(WidthKey))
            .SetHorizontalSlider(new SliderValues<int>(1, 7, settings.Width))
            .RegisterChange(width => settings.Width = width)
            .AddEndLabel(value => $"{value}"));
        Content.Add(new GameSliderInt()
            .SetLabel(loc.T(HeadDistanceKey))
            .SetHorizontalSlider(new SliderValues<int>(0, 16, settings.HeadDistance))
            .RegisterChange(headDistance => settings.HeadDistance = headDistance)
            .AddEndLabel(value => $"{value}"));
        Content.Add(new GameSliderInt()
            .SetLabel(loc.T(MaxBasinVolumeKey))
            .SetHorizontalSlider(new SliderValues<int>(50, 5000, settings.MaxBasinVolume))
            .RegisterChange(maxBasinVolume => settings.MaxBasinVolume = maxBasinVolume)
            .AddEndLabel(value => $"{value}"));
        Content.Add(new GameSliderInt()
            .SetLabel(loc.T(FillDipAreaKey))
            .SetHorizontalSlider(new SliderValues<int>(0, 256, settings.FillDipArea))
            .RegisterChange(fillDipArea => settings.FillDipArea = fillDipArea)
            .AddEndLabel(value => $"{value}"));
        Content.Add(new GameSlider()
            .SetLabel(loc.T(SourceStrengthKey))
            .SetHorizontalSlider(new SliderValues<float>(0.5f, 5, settings.SourceStrength))
            .RegisterChange(strength => settings.SourceStrength = strength)
            .AddEndLabel(value => $"{value:F1}"));
        applyButton = Content.AddButton(loc.T(ApplyKey), ApplyKey, OnApply);
        AddProgressBar();
        AddCloseButton();
    }

    private void OnApply()
    {
        // snapshot, so changing settings while the job runs does not affect it; every run gets a new seed
        var jobSettings = settings.Clone();
        jobSettings.Seed = unchecked((int)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        IReadOnlyList<IReadOnlyList<RiverHead>> sourceSpots = Array.Empty<IReadOnlyList<RiverHead>>();
        RunJob(applyButton, (terrain, mapSize, progress) =>
        {
            var voxels = VoxelMap.FromTerrain(terrain);
            var carver = new RiverCarver(jobSettings, mapSize.x, mapSize.y);
            var heights = progress.Stage(CarvingKey, 0, 1, report => carver.Carve(Surface.Heights(voxels), report));
            sourceSpots = carver.SourceSpots;
            var carved = Surface.WithHeights(voxels, heights);
            carver.CarveTunnels(carved);
            return carved;
        }, removeAllEntities: false, placeEntities: () => PlaceWaterSources(sourceSpots, jobSettings.SourceStrength));
    }

    // one water source per river, at its head or, where the game does not accept it there, as close downstream as
    // possible
    private IEnumerable<float> PlaceWaterSources(IReadOnlyList<IReadOnlyList<RiverHead>> sourceSpots, float strength)
    {
        for (var i = 0; i < sourceSpots.Count; i++)
        {
            var placed = entityPlacer.PlaceWaterSource(sourceSpots[i], strength);
            if (placed == null)
                Debug.LogWarning($"[TerrainGenerator] no place for river {i + 1}'s water source along its course");
            else if (placed != sourceSpots[i][0])
                Debug.Log($"[TerrainGenerator] river {i + 1}'s water source moved from its head {sourceSpots[i][0]} " +
                          $"to {placed}");
            yield return (i + 1f) / sourceSpots.Count;
        }
    }
}

using Timberborn.Localization;
using Timberborn.TerrainGenerator.Core.Common;
using Timberborn.TerrainGenerator.Core.Rivers;
using Timberborn.TerrainGenerator.Game.Common;
using TimberUi.CommonUi;
using UnityEngine.UIElements;

namespace Timberborn.TerrainGenerator.Game.Rivers;

public class RiverCarverDialog : TerrainJobDialog
{
    private const string TitleKey = "Ximsa.TerrainGenerator.Tool.RiverCarver";
    private const string RiversKey = "Ximsa.TerrainGenerator.RiverCarver.Rivers";
    private const string WidthKey = "Ximsa.TerrainGenerator.RiverCarver.Width";
    private const string HeadDistanceKey = "Ximsa.TerrainGenerator.RiverCarver.HeadDistance";
    private const string MaxBasinAreaKey = "Ximsa.TerrainGenerator.RiverCarver.MaxBasinArea";
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
            .SetHorizontalSlider(new SliderValues<int>(1, 10, settings.Rivers))
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
            .SetLabel(loc.T(MaxBasinAreaKey))
            .SetHorizontalSlider(new SliderValues<int>(20, 1000, settings.MaxBasinArea))
            .RegisterChange(maxBasinArea => settings.MaxBasinArea = maxBasinArea)
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
        IReadOnlyList<RiverHead> heads = Array.Empty<RiverHead>();
        RunJob(applyButton, (terrain, mapSize, progress) =>
        {
            var voxels = VoxelMap.FromTerrain(terrain);
            var carver = new RiverCarver(jobSettings, mapSize.x, mapSize.y);
            var heights = progress.Stage(CarvingKey, 0, 1, report => carver.Carve(Surface.Heights(voxels), report));
            heads = carver.Heads;
            var carved = Surface.WithHeights(voxels, heights);
            carver.CarveTunnels(carved);
            return carved;
        }, removeAllEntities: false, placeEntities: () => PlaceWaterSources(heads, jobSettings.SourceStrength));
    }

    private IEnumerable<float> PlaceWaterSources(IReadOnlyList<RiverHead> heads, float strength)
    {
        for (var i = 0; i < heads.Count; i++)
        {
            entityPlacer.PlaceWaterSource(heads[i], strength);
            yield return (i + 1f) / heads.Count;
        }
    }
}

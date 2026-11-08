using Timberborn.CoreUI;
using Timberborn.Localization;
using Timberborn.MapRepositorySystem;
using Timberborn.TerrainGenerator.Core.Common;
using Timberborn.TerrainGenerator.Core.Wfc;
using Timberborn.TerrainGenerator.Game.Common;
using TimberUi.CommonUi;
using UnityEngine;
using UnityEngine.UIElements;

namespace Timberborn.TerrainGenerator.Game.Wfc;

public class WfcGeneratorDialog : TerrainJobDialog
{
    private const string TitleKey = "Ximsa.TerrainGenerator.Tool.Wfc";
    private const string SourcesKey = "Ximsa.TerrainGenerator.Wfc.Sources";
    private const string CurrentMapKey = "Ximsa.TerrainGenerator.Wfc.CurrentMap";
    private const string MoreMapsKey = "Ximsa.TerrainGenerator.Wfc.MoreMaps";
    private const string BuiltInMapsKey = "Ximsa.TerrainGenerator.Wfc.BuiltInMaps";
    private const string UserMapsKey = "Ximsa.TerrainGenerator.Wfc.UserMaps";
    private const string HeightPenaltyKey = "Ximsa.TerrainGenerator.Wfc.HeightPenalty";
    private const string PatternWidthKey = "Ximsa.TerrainGenerator.Wfc.PatternWidth";
    private const string PatternHeightKey = "Ximsa.TerrainGenerator.Wfc.PatternHeight";
    private const string GenerateKey = "Ximsa.TerrainGenerator.Generate";
    private const string ReadingMapsKey = "Ximsa.TerrainGenerator.Progress.ReadingMaps";
    private const string LearningKey = "Ximsa.TerrainGenerator.Progress.Learning";
    private const string SolvingKey = "Ximsa.TerrainGenerator.Progress.Solving";

    private readonly VisualElement generateButton;
    private readonly DialogBoxShower dialogBoxShower;
    private readonly MapRepository mapRepository;
    private readonly HashSet<MapFileReference> selectedMaps = new();
    private readonly WfcSettings settings = new();
    private bool useCurrentMap = true;

    public WfcGeneratorDialog(ILoc loc, MapEditorService mapEditorService, MapRepository mapRepository,
        DialogBoxShower dialogBoxShower) :
        base(loc, mapEditorService)
    {
        this.mapRepository = mapRepository;
        this.dialogBoxShower = dialogBoxShower;
        SetTitle(loc.T(TitleKey));

        // sources: the current map up front, all other maps behind "More maps"
        Content.AddLabel(loc.T(SourcesKey), SourcesKey);
        var currentMap = Content.AddGamePanelToggle(loc.T(CurrentMapKey), selected =>
        {
            useCurrentMap = selected;
            UpdateGenerateButton();
        });
        currentMap.SetValueWithoutNotify(true);
        AlignLeft(currentMap);

        var moreMaps = new VisualElement();
        // full width, so its checkboxes can sit on the left like the one above
        moreMaps.style.alignSelf = Align.Stretch;
        moreMaps.AddLabel(loc.T(BuiltInMapsKey), BuiltInMapsKey);
        foreach (var name in mapRepository.GetBuiltinMapNames().Where(name => !name.StartsWith("_")))
            AddMapToggle(moreMaps, name, MapFileReference.FromResource(name));
        moreMaps.AddLabel(loc.T(UserMapsKey), UserMapsKey);
        foreach (var name in mapRepository.GetUserMapNames())
            AddMapToggle(moreMaps, name, MapFileReference.FromUserFolder(name));
        moreMaps.SetDisplay(false);
        var moreMapsShown = false;
        Content.AddButton(loc.T(MoreMapsKey), MoreMapsKey, () => moreMaps.SetDisplay(moreMapsShown = !moreMapsShown));
        Content.Add(moreMaps);

        Content.Add(WithTooltip(PatternWidthKey, new GameSliderInt()
            .SetLabel(loc.T(PatternWidthKey))
            .SetHorizontalSlider(new SliderValues<int>(4, WfcPatternShape.MaxWidth, settings.PatternWidth))
            .RegisterChange(width => settings.PatternWidth = width)
            .AddEndLabel(value => $"{value}")));
        Content.Add(WithTooltip(PatternHeightKey, new GameSliderInt()
            .SetLabel(loc.T(PatternHeightKey))
            .SetHorizontalSlider(new SliderValues<int>(4, 6, settings.PatternHeight))
            .RegisterChange(height => settings.PatternHeight = height)
            .AddEndLabel(value => $"{value}")));
        Content.Add(WithTooltip(HeightPenaltyKey, new GameSlider()
            .SetLabel(loc.T(HeightPenaltyKey))
            .SetHorizontalSlider(new SliderValues<float>(0.05f, 1, (float)settings.HeightPenalty))
            .RegisterChange(penalty => settings.HeightPenalty = penalty)
            .AddEndLabel(value => $"{value:F2}")));
        generateButton = Content.AddButton(loc.T(GenerateKey), GenerateKey, OnGenerate);
        AddProgressBar();
        AddCloseButton();
        UpdateGenerateButton();
    }

    private void AddMapToggle(VisualElement parent, string name, MapFileReference reference)
    {
        var toggle = parent.AddGamePanelToggle(name, isSelected =>
        {
            if (isSelected)
                selectedMaps.Add(reference);
            else
                selectedMaps.Remove(reference);
            UpdateGenerateButton();
        });
        AlignLeft(toggle);
    }

    // the dialog centres its content; checkboxes read better in a left-aligned column
    private static void AlignLeft(VisualElement element)
    {
        element.style.alignSelf = Align.FlexStart;
    }

    protected override void ShowFailure(string message)
    {
        dialogBoxShower.Create().SetMessage(message).SetConfirmButton(() => { }).Show();
    }

    private void UpdateGenerateButton()
    {
        generateButton?.SetEnabled(useCurrentMap || selectedMaps.Count > 0);
    }

    private void OnGenerate()
    {
        // snapshot, so changing settings while the job runs does not affect it; every run gets a new seed
        var jobSettings = settings.Clone();
        jobSettings.Seed = unchecked((int)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var useCurrent = useCurrentMap;
        // maps are read through the game's MapRepository (Unity assets), so on the main thread;
        // their bytes are parsed in the background
        var mapFiles = new List<byte[]>();
        foreach (var reference in selectedMaps)
            try
            {
                using var stream = mapRepository.OpenMap(reference);
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                mapFiles.Add(copy.ToArray());
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[TerrainGenerator] could not open map {reference.Name}: {exception.Message}");
            }

        RunJob(generateButton, (terrain, mapSize, progress) =>
        {
            var sources = progress.Stage(ReadingMapsKey, 0f, 0.05f, report =>
            {
                var maps = new List<VoxelMap>();
                if (useCurrent)
                    maps.Add(VoxelMap.FromTerrain(terrain));
                for (var i = 0; i < mapFiles.Count; i++)
                {
                    using var file = new MemoryStream(mapFiles[i]);
                    maps.Add(MapVoxelReader.Read(file));
                    report((i + 1f) / mapFiles.Count);
                }

                return maps;
            });
            var generator = new WfcTerrainGenerator(jobSettings);
            progress.Stage(LearningKey, 0.05f, 0.15f, report =>
            {
                generator.Learn(sources, report);
                Debug.Log($"[TerrainGenerator] WFC learned from {sources.Count} maps: {generator.Index.Describe()}");
                return true;
            });
            var voxels = progress.Stage(SolvingKey, 0.15f, 1f,
                report => generator.Generate(mapSize.x, mapSize.y, mapSize.z, report));
            foreach (var attempt in generator.Attempts)
                Debug.Log($"[TerrainGenerator] WFC attempt {attempt}");
            return voxels;
        });
    }
}

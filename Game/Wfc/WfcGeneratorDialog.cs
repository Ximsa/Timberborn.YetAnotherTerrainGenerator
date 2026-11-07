using Timberborn.CoreUI;
using Timberborn.DropdownSystem;
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
    private const string SolidThresholdKey = "Ximsa.TerrainGenerator.Wfc.SolidThreshold";
    private const string BlockSizeKey = "Ximsa.TerrainGenerator.Wfc.BlockSize";
    private const string PatternSizeKey = "Ximsa.TerrainGenerator.Wfc.PatternSize";
    private const string HeightPenaltyKey = "Ximsa.TerrainGenerator.Wfc.HeightPenalty";
    private const string CoverageKey = "Ximsa.TerrainGenerator.Wfc.Coverage";
    private const string CellSelectionKey = "Ximsa.TerrainGenerator.Wfc.CellSelection";
    private const string GenerateKey = "Ximsa.TerrainGenerator.Generate";
    private const string ReadingMapsKey = "Ximsa.TerrainGenerator.Progress.ReadingMaps";
    private const string LearningKey = "Ximsa.TerrainGenerator.Progress.Learning";
    private const string SolvingKey = "Ximsa.TerrainGenerator.Progress.Solving";

    // checked by default, besides the current map
    private const string DefaultBuiltInMap = "Diorama";

    private readonly VisualElement generateButton;
    private readonly MapRepository mapRepository;
    private readonly HashSet<MapFileReference> selectedMaps = new();
    private readonly WfcSettings settings = new();
    private bool useCurrentMap = true;

    public WfcGeneratorDialog(ILoc loc, MapEditorService mapEditorService, MapRepository mapRepository,
        VisualElementInitializer visualElementInitializer, DropdownItemsSetter dropdownItemsSetter) :
        base(loc, mapEditorService)
    {
        this.mapRepository = mapRepository;
        SetTitle(loc.T(TitleKey));

        // sources: the current map and Diorama up front, all other maps behind "More maps"
        Content.AddLabel(loc.T(SourcesKey), SourcesKey);
        Content.AddGamePanelToggle(loc.T(CurrentMapKey), selected =>
        {
            useCurrentMap = selected;
            UpdateGenerateButton();
        }).SetValueWithoutNotify(true);
        var builtInNames = mapRepository.GetBuiltinMapNames().Where(name => !name.StartsWith("_")).ToList();
        if (builtInNames.Contains(DefaultBuiltInMap))
            AddMapToggle(Content, DefaultBuiltInMap, MapFileReference.FromResource(DefaultBuiltInMap), true);

        var moreMaps = new VisualElement();
        moreMaps.AddLabel(loc.T(BuiltInMapsKey), BuiltInMapsKey);
        foreach (var name in builtInNames.Where(name => name != DefaultBuiltInMap))
            AddMapToggle(moreMaps, name, MapFileReference.FromResource(name), false);
        moreMaps.AddLabel(loc.T(UserMapsKey), UserMapsKey);
        foreach (var name in mapRepository.GetUserMapNames())
            AddMapToggle(moreMaps, name, MapFileReference.FromUserFolder(name), false);
        moreMaps.SetDisplay(false);
        var moreMapsShown = false;
        Content.AddButton(loc.T(MoreMapsKey), MoreMapsKey, () => moreMaps.SetDisplay(moreMapsShown = !moreMapsShown));
        Content.Add(moreMaps);

        Content.Add(new GameSlider()
            .SetLabel(loc.T(SolidThresholdKey))
            .SetHorizontalSlider(new SliderValues<float>(0.05f, 1, (float)settings.SolidThreshold))
            .RegisterChange(threshold => settings.SolidThreshold = threshold)
            .AddEndLabel(value => $"{value * 100:F0}%"));
        Content.Add(new GameSliderInt()
            .SetLabel(loc.T(BlockSizeKey))
            .SetHorizontalSlider(new SliderValues<int>(2, 6, settings.BlockSize))
            .RegisterChange(blockSize => settings.BlockSize = blockSize)
            .AddEndLabel(value => $"{value}"));
        Content.Add(new GameSliderInt()
            .SetLabel(loc.T(PatternSizeKey))
            .SetHorizontalSlider(new SliderValues<int>(2, 3, settings.PatternSize))
            .RegisterChange(patternSize => settings.PatternSize = patternSize)
            .AddEndLabel(value => $"{value}"));
        Content.Add(new GameSlider()
            .SetLabel(loc.T(HeightPenaltyKey))
            .SetHorizontalSlider(new SliderValues<float>(0.01f, 1, (float)settings.HeightPenalty))
            .RegisterChange(penalty => settings.HeightPenalty = penalty)
            .AddEndLabel(value => $"{value:F2}"));
        Content.Add(new GameSlider()
            .SetLabel(loc.T(CoverageKey))
            .SetHorizontalSlider(new SliderValues<float>(0.5f, 1, (float)settings.PatternCoverage))
            .RegisterChange(coverage => settings.PatternCoverage = coverage)
            .AddEndLabel(value => $"{value * 100:F0}%"));
        Content.AddDropdownRow(Enum.GetValues(typeof(WfcCellSelection)).Cast<WfcCellSelection>(),
                selection => loc.T(CellSelectionKey + "." + selection), visualElementInitializer, dropdownItemsSetter,
                loc.T(CellSelectionKey), changed => settings.CellSelection = changed.Item.Value)
            .SetSelectedValue(settings.CellSelection);
        generateButton = Content.AddButton(loc.T(GenerateKey), GenerateKey, OnGenerate);
        AddProgressBar();
        AddCloseButton();
        UpdateGenerateButton();
    }

    private void AddMapToggle(VisualElement parent, string name, MapFileReference reference, bool selected)
    {
        if (selected)
            selectedMaps.Add(reference);
        parent.AddGamePanelToggle(name, isSelected =>
        {
            if (isSelected)
                selectedMaps.Add(reference);
            else
                selectedMaps.Remove(reference);
            UpdateGenerateButton();
        }).SetValueWithoutNotify(selected);
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
                Debug.Log($"[TerrainGenerator] WFC learned {generator.PatternCount} patterns from {sources.Count} maps");
                return true;
            });
            var voxels = progress.Stage(SolvingKey, 0.15f, 1f,
                report => generator.Generate(mapSize.x, mapSize.y, mapSize.z, report));
            Debug.Log($"[TerrainGenerator] WFC solved after {generator.AttemptsUsed} attempt(s), " +
                      $"{generator.Backtracks} backtracks");
            return voxels;
        });
    }
}

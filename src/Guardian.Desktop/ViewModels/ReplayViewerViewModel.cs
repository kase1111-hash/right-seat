using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Guardian.Common;
using Guardian.Core;
using Guardian.Detection;
using Guardian.Priority;
using Guardian.Replay;

namespace Guardian.Desktop.ViewModels;

/// <summary>
/// ViewModel for the side-by-side crash replay viewer.
/// Shows a recorded flight timeline with Guardian alerts overlaid,
/// compared against expected results.
/// </summary>
public partial class ReplayViewerViewModel : ObservableObject
{
    [ObservableProperty] private string _scenarioName = "No scenario loaded";
    [ObservableProperty] private string _profileName = "---";
    [ObservableProperty] private double _playbackProgress;
    [ObservableProperty] private string _playbackTime = "00:00";
    [ObservableProperty] private string _totalTime = "00:00";
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private string _validationSummary = "";
    [ObservableProperty] private int _matchedCount;
    [ObservableProperty] private int _missingCount;
    [ObservableProperty] private int _unexpectedCount;
    [ObservableProperty] private int _forbiddenCount;

    public ObservableCollection<TimelineEntryViewModel> TimelineEntries { get; } = new();
    public ObservableCollection<ReplayAlertEntryViewModel> DeliveredAlerts { get; } = new();
    public ObservableCollection<ExpectedAlertEntryViewModel> ExpectedAlerts { get; } = new();

    private ReplayResult? _lastResult;
    private ValidationReport? _lastValidation;

    [RelayCommand]
    private async Task LoadAndReplay(string scenarioCsvPath)
    {
        if (!File.Exists(scenarioCsvPath)) return;

        ScenarioName = Path.GetFileNameWithoutExtension(scenarioCsvPath);
        TimelineEntries.Clear();
        DeliveredAlerts.Clear();
        ExpectedAlerts.Clear();

        // Load scenario
        var snapshots = ScenarioCsvReader.ReadCsv(scenarioCsvPath);
        if (snapshots.Count == 0) return;

        var duration = snapshots[^1].Timestamp - snapshots[0].Timestamp;
        TotalTime = FormatDuration(duration);

        // Load profile
        var profilesPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "config", "profiles");
        var profileLoader = new ProfileLoader();
        profileLoader.LoadProfiles(profilesPath);

        // Determine engine count from first snapshot
        var first = snapshots[0];
        var engineCount = (int)(first.Get(SimVarId.NumberOfEngines) ?? 1);
        var engineType = ((int)(first.Get(SimVarId.EngineType) ?? 0)) == 0 ? "piston" : "turboprop";
        var profile = profileLoader.MatchProfile("", engineCount, engineType);

        if (profile is null) return;
        ProfileName = profile.DisplayName;

        // Run replay
        var config = new GuardianConfig();
        var engine = new ScenarioReplayEngine(config, profile);
        IsPlaying = true;

        _lastResult = await Task.Run(() => engine.Replay(snapshots, playbackSpeed: 100.0));
        IsPlaying = false;

        // Populate timeline
        var startTime = snapshots[0].Timestamp;
        foreach (var alert in _lastResult.DeliveredAlerts)
        {
            var offset = alert.DeliveredAt - startTime;
            DeliveredAlerts.Add(new ReplayAlertEntryViewModel
            {
                Time = FormatDuration(offset),
                Severity = alert.Alert.Severity.ToString(),
                RuleId = alert.Alert.RuleId,
                Text = alert.Alert.FormatText(),
                PositionPct = duration.TotalSeconds > 0
                    ? (offset.TotalSeconds / duration.TotalSeconds * 100)
                    : 0,
            });

            TimelineEntries.Add(new TimelineEntryViewModel
            {
                Time = FormatDuration(offset),
                PositionPct = duration.TotalSeconds > 0
                    ? (offset.TotalSeconds / duration.TotalSeconds * 100)
                    : 0,
                Label = $"[{alert.Alert.Severity}] {alert.Alert.RuleId}",
                SeverityColor = GetSeverityColor(alert.Alert.Severity),
                IsExpected = false,
            });
        }

        // Load and validate expected results
        var expectedPath = Path.Combine(
            Path.GetDirectoryName(scenarioCsvPath) ?? ".",
            "expected",
            Path.GetFileNameWithoutExtension(scenarioCsvPath) + ".json");

        if (File.Exists(expectedPath))
        {
            var expectedResults = ExpectedResults.LoadFromFile(expectedPath);
            _lastValidation = ScenarioValidator.Validate(_lastResult, expectedResults, startTime);

            MatchedCount = _lastValidation.Matched.Count;
            MissingCount = _lastValidation.Missing.Count;
            UnexpectedCount = _lastValidation.Unexpected.Count;
            ForbiddenCount = _lastValidation.Forbidden.Count;

            ValidationSummary = _lastValidation.Passed
                ? "PASS — all expected alerts matched"
                : $"FAIL — {MissingCount} missing, {ForbiddenCount} forbidden, {UnexpectedCount} unexpected";

            // Show expected alerts in the list
            foreach (var expected in expectedResults.ExpectedAlerts)
            {
                var matched = _lastValidation.Matched.Any(m => m.RuleId == expected.RuleId);
                ExpectedAlerts.Add(new ExpectedAlertEntryViewModel
                {
                    RuleId = expected.RuleId,
                    Severity = expected.Severity,
                    TimeWindow = $"{expected.MinTimeSec:F0}s – {expected.MaxTimeSec:F0}s",
                    Matched = matched,
                });
            }
        }
        else
        {
            ValidationSummary = "No expected results file found";
        }

        PlaybackProgress = 100;
        PlaybackTime = TotalTime;
    }

    private static string FormatDuration(TimeSpan ts)
    {
        return ts.TotalMinutes >= 1
            ? $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}"
            : $"0:{(int)ts.TotalSeconds:D2}";
    }

    private static string GetSeverityColor(AlertSeverity severity) => severity switch
    {
        AlertSeverity.Critical => "#FF4444",
        AlertSeverity.Warning => "#FFAA00",
        AlertSeverity.Advisory => "#00AADD",
        _ => "#66AA66",
    };
}

public class TimelineEntryViewModel
{
    public string Time { get; set; } = "";
    public double PositionPct { get; set; }
    public string Label { get; set; } = "";
    public string SeverityColor { get; set; } = "#FFFFFF";
    public bool IsExpected { get; set; }
}

public class ReplayAlertEntryViewModel
{
    public string Time { get; set; } = "";
    public string Severity { get; set; } = "";
    public string RuleId { get; set; } = "";
    public string Text { get; set; } = "";
    public double PositionPct { get; set; }
}

public class ExpectedAlertEntryViewModel
{
    public string RuleId { get; set; } = "";
    public string Severity { get; set; } = "";
    public string TimeWindow { get; set; } = "";
    public bool Matched { get; set; }
}

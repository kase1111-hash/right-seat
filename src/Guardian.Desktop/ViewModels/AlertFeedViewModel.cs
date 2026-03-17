using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Guardian.Core;
using Guardian.Priority;

namespace Guardian.Desktop.ViewModels;

/// <summary>
/// ViewModel for the chronological alert feed panel.
/// </summary>
public partial class AlertFeedViewModel : ObservableObject
{
    public ObservableCollection<AlertEntryViewModel> Alerts { get; } = new();

    [ObservableProperty] private AlertEntryViewModel? _selectedAlert;
    [ObservableProperty] private AlertSeverity _minimumSeverityFilter = AlertSeverity.Info;
    [ObservableProperty] private int _totalAlertCount;
    [ObservableProperty] private int _criticalCount;
    [ObservableProperty] private int _warningCount;
    [ObservableProperty] private int _advisoryCount;

    private const int MaxAlerts = 500;

    public void AddDeliveredAlert(DeliveredAlert delivered)
    {
        var entry = new AlertEntryViewModel
        {
            Timestamp = delivered.Alert.Timestamp,
            Severity = delivered.Alert.Severity,
            RuleId = delivered.Alert.RuleId,
            TextKey = delivered.Alert.TextKey,
            FormattedText = delivered.Alert.FormatText(),
            FlightPhase = delivered.Alert.FlightPhase.ToString(),
            WasDeferredFromSterile = delivered.WasDeferredFromSterile,
            TelemetrySnapshot = delivered.Alert.TelemetrySnapshot,
            SeverityClass = GetSeverityClass(delivered.Alert.Severity),
        };

        Alerts.Insert(0, entry); // Newest first

        if (Alerts.Count > MaxAlerts)
            Alerts.RemoveAt(Alerts.Count - 1);

        UpdateCounts(delivered.Alert.Severity, +1);
    }

    public void AddInfoAlert(Alert info)
    {
        var entry = new AlertEntryViewModel
        {
            Timestamp = info.Timestamp,
            Severity = AlertSeverity.Info,
            RuleId = info.RuleId,
            TextKey = info.TextKey,
            FormattedText = info.FormatText(),
            FlightPhase = info.FlightPhase.ToString(),
            SeverityClass = "severity-info",
        };

        Alerts.Insert(0, entry);

        if (Alerts.Count > MaxAlerts)
            Alerts.RemoveAt(Alerts.Count - 1);

        TotalAlertCount++;
    }

    public void Clear()
    {
        Alerts.Clear();
        TotalAlertCount = 0;
        CriticalCount = 0;
        WarningCount = 0;
        AdvisoryCount = 0;
    }

    private void UpdateCounts(AlertSeverity severity, int delta)
    {
        TotalAlertCount += delta;
        switch (severity)
        {
            case AlertSeverity.Critical: CriticalCount += delta; break;
            case AlertSeverity.Warning: WarningCount += delta; break;
            case AlertSeverity.Advisory: AdvisoryCount += delta; break;
        }
    }

    private static string GetSeverityClass(AlertSeverity severity) => severity switch
    {
        AlertSeverity.Critical => "severity-critical",
        AlertSeverity.Warning => "severity-warning",
        AlertSeverity.Advisory => "severity-advisory",
        _ => "severity-info",
    };
}

public partial class AlertEntryViewModel : ObservableObject
{
    [ObservableProperty] private DateTime _timestamp;
    [ObservableProperty] private AlertSeverity _severity;
    [ObservableProperty] private string _ruleId = "";
    [ObservableProperty] private string _textKey = "";
    [ObservableProperty] private string _formattedText = "";
    [ObservableProperty] private string _flightPhase = "";
    [ObservableProperty] private bool _wasDeferredFromSterile;
    [ObservableProperty] private string _severityClass = "";
    [ObservableProperty] private Dictionary<string, double> _telemetrySnapshot = new();

    public string TimestampDisplay => Timestamp.ToString("HH:mm:ss");
    public string SeverityDisplay => Severity.ToString().ToUpperInvariant();
}

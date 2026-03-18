using Guardian.Common;
using Guardian.Core;
using Guardian.Detection;
using Guardian.Priority;
using Serilog;

namespace Guardian.Efb.Api;

/// <summary>
/// Provides state data to the EFB HTTP API.
/// Connects to the Guardian pipeline events and maintains a session alert history.
/// </summary>
public sealed class EfbStateProvider
{
    private static readonly ILogger Log = Serilog.Log.ForContext<EfbStateProvider>();

    private readonly GuardianConfig _config;
    private readonly AlertPipeline _pipeline;
    private readonly Func<IReadOnlyList<(string RuleId, string Name, RuleState State)>> _getRuleStates;
    private readonly Func<FlightPhase> _getPhase;
    private readonly Func<AircraftProfile?> _getProfile;

    private readonly List<AlertDto> _alertHistory = new();
    private readonly object _lock = new();
    private readonly DateTime _startTime = DateTime.UtcNow;

    private bool _connected;
    private int _criticalCount;
    private int _warningCount;
    private int _advisoryCount;

    public EfbStateProvider(
        GuardianConfig config,
        AlertPipeline pipeline,
        Func<IReadOnlyList<(string RuleId, string Name, RuleState State)>> getRuleStates,
        Func<FlightPhase> getPhase,
        Func<AircraftProfile?> getProfile)
    {
        _config = config;
        _pipeline = pipeline;
        _getRuleStates = getRuleStates;
        _getPhase = getPhase;
        _getProfile = getProfile;

        // Wire to pipeline events
        pipeline.OnAlertDelivered += OnAlertDelivered;
        pipeline.OnInfoLogged += OnInfoLogged;
    }

    public void SetConnected(bool connected) => _connected = connected;

    private void OnAlertDelivered(DeliveredAlert delivered)
    {
        var dto = ToDto(delivered.Alert, delivered.WasDeferredFromSterile);

        lock (_lock)
        {
            _alertHistory.Insert(0, dto);
            if (_alertHistory.Count > 500) _alertHistory.RemoveAt(_alertHistory.Count - 1);

            switch (delivered.Alert.Severity)
            {
                case AlertSeverity.Critical: _criticalCount++; break;
                case AlertSeverity.Warning: _warningCount++; break;
                case AlertSeverity.Advisory: _advisoryCount++; break;
            }
        }
    }

    private void OnInfoLogged(Alert info)
    {
        var dto = ToDto(info, false);
        lock (_lock)
        {
            _alertHistory.Insert(0, dto);
            if (_alertHistory.Count > 500) _alertHistory.RemoveAt(_alertHistory.Count - 1);
        }
    }

    public StatusDto GetStatus()
    {
        var profile = _getProfile();
        var rules = _getRuleStates();

        return new StatusDto
        {
            Connected = _connected,
            FlightPhase = _getPhase().ToString(),
            SterileCockpit = _pipeline.SterileCockpit.IsSterile,
            AircraftId = profile?.AircraftId ?? "",
            AircraftName = profile?.DisplayName ?? "Unknown",
            AlertCounts = new AlertCountDto
            {
                Critical = _criticalCount,
                Warning = _warningCount,
                Advisory = _advisoryCount,
                Total = _criticalCount + _warningCount + _advisoryCount,
            },
            Rules = rules.Select(r => new RuleStatusDto
            {
                RuleId = r.RuleId,
                Name = r.Name,
                State = r.State.ToString(),
            }).ToList(),
            UptimeSec = (DateTime.UtcNow - _startTime).TotalSeconds,
        };
    }

    public List<AlertDto> GetAllAlerts()
    {
        lock (_lock) return _alertHistory.ToList();
    }

    public List<AlertDto> GetActiveAlerts()
    {
        lock (_lock)
        {
            return _alertHistory
                .Where(a => a.Severity != "Info")
                .Take(20)
                .ToList();
        }
    }

    public void ApplySettings(SettingsUpdateDto settings)
    {
        if (settings.SterileCockpitManual is not null)
        {
            _pipeline.SterileCockpit.SetManualOverride(settings.SterileCockpitManual.Value);
            Log.Information("EFB: Sterile cockpit manual override set to {Value}", settings.SterileCockpitManual.Value);
        }

        if (settings.AudioEnabled is not null)
        {
            _config.AudioEnabled = settings.AudioEnabled.Value;
            Log.Information("EFB: Audio enabled set to {Value}", settings.AudioEnabled.Value);
        }

        if (settings.Sensitivity is not null)
        {
            _config.Sensitivity = settings.Sensitivity;
            Log.Information("EFB: Sensitivity set to {Value}", settings.Sensitivity);
        }
    }

    public void SilenceCriticalAlarm()
    {
        _pipeline.Audio.SilenceCriticalAlarm();
        Log.Information("EFB: Critical alarm silenced");
    }

    private static AlertDto ToDto(Alert alert, bool deferredFromSterile)
    {
        return new AlertDto
        {
            Id = alert.Id.ToString(),
            RuleId = alert.RuleId,
            Severity = alert.Severity.ToString(),
            TextKey = alert.TextKey,
            Text = alert.FormatText(),
            Timestamp = alert.Timestamp.ToString("O"),
            FlightPhase = alert.FlightPhase.ToString(),
            Parameters = alert.TextParameters,
            Telemetry = alert.TelemetrySnapshot,
            DeferredFromSterile = deferredFromSterile,
        };
    }
}

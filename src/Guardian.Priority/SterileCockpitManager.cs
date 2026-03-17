using Guardian.Common;
using Guardian.Core;
using Serilog;

namespace Guardian.Priority;

/// <summary>
/// Manages sterile cockpit mode — suppresses WARNING and ADVISORY alerts
/// during critical flight phases (takeoff, approach, landing).
///
/// When sterile mode deactivates:
///   - Queued alerts are released in severity order with configurable spacing
///   - Alerts whose conditions resolved during sterile mode are downgraded to INFO
/// </summary>
public sealed class SterileCockpitManager
{
    private static readonly ILogger Log = Serilog.Log.ForContext<SterileCockpitManager>();

    private readonly bool _enabled;
    private bool _manualOverride;
    private bool _wasSterile;

    /// <summary>Raised when sterile mode transitions. Bool = new sterile state.</summary>
    public event Action<bool>? OnSterileStateChanged;

    public SterileCockpitManager(GuardianConfig config)
    {
        _enabled = config.SterileCockpitEnabled;
    }

    /// <summary>
    /// Whether sterile cockpit mode is currently active.
    /// </summary>
    public bool IsSterile { get; private set; }

    /// <summary>
    /// Updates sterile state based on the current flight phase.
    /// Returns true if a sterile → non-sterile transition just occurred
    /// (meaning queued alerts should be released).
    /// </summary>
    public bool Update(FlightPhase phase)
    {
        if (!_enabled && !_manualOverride)
        {
            IsSterile = false;
            return false;
        }

        bool shouldBeSterile = _manualOverride || phase.IsSterile();
        bool wasJustSterile = IsSterile;

        if (shouldBeSterile != IsSterile)
        {
            IsSterile = shouldBeSterile;
            Log.Information("Sterile cockpit mode {State} (phase={Phase}, manual={Manual})",
                IsSterile ? "ACTIVATED" : "DEACTIVATED", phase, _manualOverride);
            OnSterileStateChanged?.Invoke(IsSterile);
        }

        // Detect sterile → non-sterile transition
        bool justExitedSterile = wasJustSterile && !IsSterile;
        _wasSterile = IsSterile;

        return justExitedSterile;
    }

    /// <summary>
    /// Returns true if an alert should be suppressed (queued) in the current sterile state.
    /// CRITICAL alerts are never suppressed.
    /// </summary>
    public bool ShouldSuppress(Alert alert)
    {
        if (!IsSterile) return false;
        return alert.Severity < AlertSeverity.Critical;
    }

    /// <summary>
    /// Manually toggle sterile cockpit mode.
    /// </summary>
    public void ToggleManual()
    {
        _manualOverride = !_manualOverride;
        Log.Information("Sterile cockpit manual override {State}", _manualOverride ? "ON" : "OFF");
    }

    /// <summary>Set manual override to a specific state.</summary>
    public void SetManualOverride(bool enabled)
    {
        _manualOverride = enabled;
    }
}

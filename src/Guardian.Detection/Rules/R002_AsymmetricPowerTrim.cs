using Guardian.Core;
using Serilog;

namespace Guardian.Detection.Rules;

/// <summary>
/// R002: Asymmetric Power/Trim Disagreement
///
/// Detects when a multi-engine aircraft has significant thrust asymmetry
/// (throttle, RPM, or manifold pressure differential between engines) but
/// the trim settings do not reflect compensating input (rudder/aileron trim).
///
/// Also detects significant trim offset with no corresponding power differential,
/// which may indicate a stuck or mis-set trim condition.
///
/// Escalation:
///   ADVISORY — trim/power mismatch detected, may be intentional
///   WARNING — mismatch persists for 60+ seconds
///
/// Applicable only to multi-engine aircraft.
/// </summary>
public sealed class R002_AsymmetricPowerTrim : IDetectionRule
{
    private static readonly ILogger Log = Serilog.Log.ForContext<R002_AsymmetricPowerTrim>();

    public string RuleId => "R002";
    public string Name => "Asymmetric Power/Trim Disagreement";
    public string Description => "Detects thrust asymmetry without compensating trim input on multi-engine aircraft.";
    public TimeSpan EvaluationInterval => TimeSpan.FromSeconds(5);

    // Power differential thresholds (percent of lever range)
    private const double ThrottleDiffThreshold = 10.0; // >10% difference = asymmetric
    private const double RpmDiffThreshold = 100.0;     // >100 RPM difference

    // Trim thresholds
    private const double TrimNeutralZone = 5.0; // ±5% considered neutral

    // Persistence for escalation
    private DateTime _mismatchStart = DateTime.MinValue;
    private static readonly TimeSpan EscalationDuration = TimeSpan.FromSeconds(60);

    public bool IsApplicable(AircraftProfile profile, FlightPhase phase)
    {
        return profile.EngineCount >= 2 && phase != FlightPhase.Ground;
    }

    public Alert? Evaluate(
        TelemetrySnapshot current,
        ITelemetryBuffer buffer,
        AircraftProfile profile,
        FlightPhase phase)
    {
        var engineCount = profile.EngineCount;

        // Get throttle positions and RPMs for all engines
        var throttles = new double[engineCount];
        var rpms = new double[engineCount];
        var combustion = new bool[engineCount];

        for (int i = 1; i <= engineCount; i++)
        {
            throttles[i - 1] = current.Get(SimVarId.ThrottleLeverPosition, i) ?? 0;
            rpms[i - 1] = current.Get(SimVarId.GeneralEngRpm, i) ?? 0;
            combustion[i - 1] = (current.Get(SimVarId.GeneralEngCombustion, i) ?? 0) > 0.5;
        }

        // Need at least 2 engines running
        if (combustion.Count(c => c) < 2)
            return null;

        // Calculate power differential
        double maxThrottle = throttles.Where((_, i) => combustion[i]).Max();
        double minThrottle = throttles.Where((_, i) => combustion[i]).Min();
        double throttleDiff = maxThrottle - minThrottle;

        double maxRpm = rpms.Where((_, i) => combustion[i]).Max();
        double minRpm = rpms.Where((_, i) => combustion[i]).Min();
        double rpmDiff = maxRpm - minRpm;

        bool powerAsymmetric = throttleDiff > ThrottleDiffThreshold || rpmDiff > RpmDiffThreshold;

        // Get trim positions
        double rudderTrim = current.Get(SimVarId.RudderTrimPct) ?? 0;
        double aileronTrim = current.Get(SimVarId.AileronTrimPct) ?? 0;

        bool trimNeutral = Math.Abs(rudderTrim) < TrimNeutralZone &&
                           Math.Abs(aileronTrim) < TrimNeutralZone;

        double trimMagnitude = Math.Max(Math.Abs(rudderTrim), Math.Abs(aileronTrim));
        bool trimSignificant = trimMagnitude > profile.Trim.AsymmetricThresholdPct;

        // Case 1: Asymmetric power with neutral trim — pilot may not be compensating
        if (powerAsymmetric && trimNeutral)
        {
            return MakeAlert(current, phase, throttleDiff, rpmDiff, rudderTrim, aileronTrim,
                "R002_POWER_ASYMMETRY_NO_TRIM");
        }

        // Case 2: Significant trim with symmetric power — trim may be stuck or mis-set
        if (!powerAsymmetric && trimSignificant)
        {
            return MakeAlert(current, phase, throttleDiff, rpmDiff, rudderTrim, aileronTrim,
                "R002_TRIM_OFFSET_NO_POWER_DIFF");
        }

        // No mismatch — reset persistence timer
        _mismatchStart = DateTime.MinValue;
        return null;
    }

    private Alert MakeAlert(
        TelemetrySnapshot current, FlightPhase phase,
        double throttleDiff, double rpmDiff,
        double rudderTrim, double aileronTrim,
        string textKey)
    {
        var now = current.Timestamp;

        // Track persistence for escalation
        if (_mismatchStart == DateTime.MinValue)
            _mismatchStart = now;

        var severity = (now - _mismatchStart) >= EscalationDuration
            ? AlertSeverity.Warning
            : AlertSeverity.Advisory;

        return new Alert
        {
            RuleId = RuleId,
            Severity = severity,
            TextKey = textKey,
            TextParameters = new Dictionary<string, string>
            {
                ["throttle_diff_pct"] = throttleDiff.ToString("F1"),
                ["rpm_diff"] = rpmDiff.ToString("F0"),
                ["rudder_trim_pct"] = rudderTrim.ToString("F1"),
                ["aileron_trim_pct"] = aileronTrim.ToString("F1"),
                ["duration_sec"] = ((now - _mismatchStart).TotalSeconds).ToString("F0"),
            },
            FlightPhase = phase,
            TelemetrySnapshot = new Dictionary<string, double>
            {
                ["ThrottleDiff"] = throttleDiff,
                ["RpmDiff"] = rpmDiff,
                ["RudderTrimPct"] = rudderTrim,
                ["AileronTrimPct"] = aileronTrim,
            },
        };
    }
}

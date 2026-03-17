using Guardian.Core;
using Serilog;

namespace Guardian.Detection.Rules;

/// <summary>
/// R006: Icing Conditions
///
/// Detects when the aircraft is in an icing envelope (OAT in range + visible moisture)
/// and monitors pitot heat state and structural ice accumulation.
///
/// Escalation:
///   ADVISORY — OAT in icing range + in cloud/precip, pitot heat OFF
///   WARNING — structural ice accumulating (> 5%)
///   CRITICAL — structural ice > 25% or pitot ice > 25%
/// </summary>
public sealed class R006_IcingConditions : IDetectionRule
{
    private static readonly ILogger Log = Serilog.Log.ForContext<R006_IcingConditions>();

    public string RuleId => "R006";
    public string Name => "Icing Conditions";
    public string Description => "Detects icing envelope conditions and monitors ice accumulation.";
    public TimeSpan EvaluationInterval => TimeSpan.FromSeconds(10);

    private const double StructuralIceWarningPct = 5.0;
    private const double StructuralIceCriticalPct = 25.0;
    private const double PitotIceCriticalPct = 25.0;

    public bool IsApplicable(AircraftProfile profile, FlightPhase phase)
    {
        // Only applicable when airborne
        return phase != FlightPhase.Ground && phase != FlightPhase.Landing;
    }

    public Alert? Evaluate(
        TelemetrySnapshot current,
        ITelemetryBuffer buffer,
        AircraftProfile profile,
        FlightPhase phase)
    {
        var structuralIce = current.Get(SimVarId.StructuralIcePct) ?? 0;
        var pitotIce = current.Get(SimVarId.PitotIcePct) ?? 0;
        var oat = current.Get(SimVarId.AmbientTemperature) ?? 15; // celsius
        var inCloud = (current.Get(SimVarId.AmbientInCloud) ?? 0) > 0.5;
        var precip = (current.Get(SimVarId.AmbientPrecipState) ?? 0) > 0.5;

        var icing = profile.Icing;
        bool inIcingTempRange = oat >= icing.IcingOatRangeC[0] && oat <= icing.IcingOatRangeC[1];
        bool moisturePresent = inCloud || precip;

        // CRITICAL — significant ice accumulation
        if (structuralIce >= StructuralIceCriticalPct)
        {
            return new Alert
            {
                RuleId = RuleId,
                Severity = AlertSeverity.Critical,
                TextKey = "R006_STRUCTURAL_ICE_CRITICAL",
                TextParameters = new Dictionary<string, string>
                {
                    ["structural_ice_pct"] = structuralIce.ToString("F0"),
                    ["oat_c"] = oat.ToString("F1"),
                },
                FlightPhase = phase,
                TelemetrySnapshot = BuildSnapshot(structuralIce, pitotIce, oat),
            };
        }

        if (pitotIce >= PitotIceCriticalPct)
        {
            return new Alert
            {
                RuleId = RuleId,
                Severity = AlertSeverity.Critical,
                TextKey = "R006_PITOT_ICE_CRITICAL",
                TextParameters = new Dictionary<string, string>
                {
                    ["pitot_ice_pct"] = pitotIce.ToString("F0"),
                    ["oat_c"] = oat.ToString("F1"),
                },
                FlightPhase = phase,
                TelemetrySnapshot = BuildSnapshot(structuralIce, pitotIce, oat),
            };
        }

        // WARNING — structural ice accumulating
        if (structuralIce >= StructuralIceWarningPct)
        {
            return new Alert
            {
                RuleId = RuleId,
                Severity = AlertSeverity.Warning,
                TextKey = "R006_STRUCTURAL_ICE_WARNING",
                TextParameters = new Dictionary<string, string>
                {
                    ["structural_ice_pct"] = structuralIce.ToString("F0"),
                    ["oat_c"] = oat.ToString("F1"),
                },
                FlightPhase = phase,
                TelemetrySnapshot = BuildSnapshot(structuralIce, pitotIce, oat),
            };
        }

        // ADVISORY — in icing envelope with pitot heat off
        if (inIcingTempRange && moisturePresent && icing.HasPitotHeat)
        {
            // Check if pitot heat should be on — we can infer from pitot ice trend
            // If pitot ice is starting to accumulate, pitot heat is likely off
            if (pitotIce > 0.5)
            {
                return new Alert
                {
                    RuleId = RuleId,
                    Severity = AlertSeverity.Advisory,
                    TextKey = "R006_ICING_CONDITIONS_PITOT_HEAT",
                    TextParameters = new Dictionary<string, string>
                    {
                        ["oat_c"] = oat.ToString("F1"),
                        ["pitot_ice_pct"] = pitotIce.ToString("F1"),
                        ["in_cloud"] = inCloud ? "yes" : "no",
                        ["precipitation"] = precip ? "yes" : "no",
                    },
                    FlightPhase = phase,
                    TelemetrySnapshot = BuildSnapshot(structuralIce, pitotIce, oat),
                };
            }
        }

        return null;
    }

    private static Dictionary<string, double> BuildSnapshot(double structuralIce, double pitotIce, double oat)
    {
        return new Dictionary<string, double>
        {
            ["StructuralIcePct"] = structuralIce,
            ["PitotIcePct"] = pitotIce,
            ["AmbientTemperature"] = oat,
        };
    }
}

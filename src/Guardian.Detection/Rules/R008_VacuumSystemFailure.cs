using Guardian.Core;
using Serilog;

namespace Guardian.Detection.Rules;

/// <summary>
/// R008: Vacuum System Failure
///
/// Monitors suction pressure for gyro instrument reliability.
///
/// Escalation:
///   ADVISORY — suction below normal range (gyros may be sluggish)
///   WARNING — suction below minimum (gyros unreliable)
///
/// Context: In VMC daytime conditions (no cloud, good visibility),
/// the severity is downgraded since the pilot has visual references
/// and gyro failure is less critical.
/// </summary>
public sealed class R008_VacuumSystemFailure : IDetectionRule
{
    private static readonly ILogger Log = Serilog.Log.ForContext<R008_VacuumSystemFailure>();

    public string RuleId => "R008";
    public string Name => "Vacuum System Failure";
    public string Description => "Monitors vacuum suction pressure for gyro instrument reliability.";
    public TimeSpan EvaluationInterval => TimeSpan.FromSeconds(10);

    public bool IsApplicable(AircraftProfile profile, FlightPhase phase)
    {
        // Only relevant when airborne (gyro instruments matter most)
        return phase != FlightPhase.Ground && phase != FlightPhase.Landing;
    }

    public Alert? Evaluate(
        TelemetrySnapshot current,
        ITelemetryBuffer buffer,
        AircraftProfile profile,
        FlightPhase phase)
    {
        var suction = current.Get(SimVarId.SuctionPressure);
        if (suction is null) return null;

        var vac = profile.Vacuum;
        bool isVmc = IsVmc(current);

        // Below minimum — gyros unreliable
        if (suction.Value < vac.SuctionMinimumInhg)
        {
            // Downgrade in VMC conditions
            var severity = isVmc ? AlertSeverity.Advisory : AlertSeverity.Warning;

            return new Alert
            {
                RuleId = RuleId,
                Severity = severity,
                TextKey = "R008_VACUUM_LOW",
                TextParameters = new Dictionary<string, string>
                {
                    ["suction_inhg"] = suction.Value.ToString("F2"),
                    ["minimum_inhg"] = vac.SuctionMinimumInhg.ToString("F1"),
                    ["vmc"] = isVmc ? "yes" : "no",
                },
                FlightPhase = phase,
                TelemetrySnapshot = new Dictionary<string, double>
                {
                    ["SuctionPressure"] = suction.Value,
                },
            };
        }

        // Below normal range — gyros may be sluggish
        if (suction.Value < vac.SuctionNormalInhg[0])
        {
            return new Alert
            {
                RuleId = RuleId,
                Severity = AlertSeverity.Advisory,
                TextKey = "R008_VACUUM_BELOW_NORMAL",
                TextParameters = new Dictionary<string, string>
                {
                    ["suction_inhg"] = suction.Value.ToString("F2"),
                    ["normal_low"] = vac.SuctionNormalInhg[0].ToString("F1"),
                },
                FlightPhase = phase,
                TelemetrySnapshot = new Dictionary<string, double>
                {
                    ["SuctionPressure"] = suction.Value,
                },
            };
        }

        return null;
    }

    /// <summary>
    /// Approximate VMC detection: not in cloud and no precipitation.
    /// In real implementation this would also check visibility distance.
    /// </summary>
    private static bool IsVmc(TelemetrySnapshot current)
    {
        var inCloud = (current.Get(SimVarId.AmbientInCloud) ?? 0) > 0.5;
        var precip = (current.Get(SimVarId.AmbientPrecipState) ?? 0) > 0.5;
        return !inCloud && !precip;
    }
}

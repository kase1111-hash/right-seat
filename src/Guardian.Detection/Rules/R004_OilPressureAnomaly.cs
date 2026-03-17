using Guardian.Core;
using Serilog;

namespace Guardian.Detection.Rules;

/// <summary>
/// R004: Oil Pressure Anomaly
///
/// Monitors oil pressure for:
///   1. Absolute minimum threshold breach → CRITICAL
///   2. Rapid drop rate → WARNING
///   3. Oil temperature rising while pressure falling (cross-correlation) → WARNING
///
/// Context suppression: Low oil pressure at idle on ground is normal and suppressed.
/// </summary>
public sealed class R004_OilPressureAnomaly : IDetectionRule
{
    private static readonly ILogger Log = Serilog.Log.ForContext<R004_OilPressureAnomaly>();

    public string RuleId => "R004";
    public string Name => "Oil Pressure Anomaly";
    public string Description => "Monitors oil pressure for absolute minimums, rapid drops, and temp/pressure divergence.";
    public TimeSpan EvaluationInterval => TimeSpan.FromSeconds(5);

    private static readonly TimeSpan RateWindow = TimeSpan.FromSeconds(30);

    // Ground idle suppression: below this RPM on ground, low oil pressure is expected
    private const double IdleRpmThreshold = 800;

    public bool IsApplicable(AircraftProfile profile, FlightPhase phase)
    {
        // Applicable whenever engines might be running
        return true;
    }

    public Alert? Evaluate(
        TelemetrySnapshot current,
        ITelemetryBuffer buffer,
        AircraftProfile profile,
        FlightPhase phase)
    {
        var engineCount = profile.EngineCount;
        Alert? worstAlert = null;

        for (int eng = 1; eng <= engineCount; eng++)
        {
            var combustion = current.Get(SimVarId.GeneralEngCombustion, eng) ?? 0;
            if (combustion < 0.5) continue;

            var oilPressure = current.Get(SimVarId.GeneralEngOilPressure, eng);
            if (oilPressure is null) continue;

            var oilTemp = current.Get(SimVarId.GeneralEngOilTemperature, eng);
            var rpm = current.Get(SimVarId.GeneralEngRpm, eng) ?? 0;

            // Context suppression: idle on ground
            if (phase == FlightPhase.Ground && rpm < IdleRpmThreshold)
                continue;

            var alert = EvaluateEngine(oilPressure.Value, oilTemp, rpm, eng, buffer, profile, phase);
            if (alert is not null && (worstAlert is null || alert.Severity > worstAlert.Severity))
                worstAlert = alert;
        }

        return worstAlert;
    }

    private Alert? EvaluateEngine(
        double oilPressure, double? oilTempRankine, double rpm,
        int engineIndex, ITelemetryBuffer buffer,
        AircraftProfile profile, FlightPhase phase)
    {
        var eng = profile.Engine;

        // 1. Absolute minimum — CRITICAL
        if (oilPressure < eng.OilPressureMinimumPsi)
        {
            return new Alert
            {
                RuleId = RuleId,
                Severity = AlertSeverity.Critical,
                TextKey = "R004_OIL_PRESSURE_CRITICAL",
                TextParameters = new Dictionary<string, string>
                {
                    ["engine"] = engineIndex.ToString(),
                    ["pressure_psi"] = oilPressure.ToString("F1"),
                    ["minimum_psi"] = eng.OilPressureMinimumPsi.ToString("F1"),
                },
                FlightPhase = phase,
                TelemetrySnapshot = new Dictionary<string, double>
                {
                    [$"GeneralEngOilPressure:{engineIndex}"] = oilPressure,
                },
            };
        }

        // 2. Rapid drop rate — WARNING
        var dropRate = buffer.RateOfChange(SimVarId.GeneralEngOilPressure, RateWindow, engineIndex);
        if (dropRate is not null && dropRate.Value < 0)
        {
            double dropRatePerSec = Math.Abs(dropRate.Value);
            if (dropRatePerSec >= eng.OilPressureDropRateWarningPsiPerSec)
            {
                double dropRatePerMin = UnitsConverter.PerSecondToPerMinute(dropRatePerSec);
                return new Alert
                {
                    RuleId = RuleId,
                    Severity = AlertSeverity.Warning,
                    TextKey = "R004_OIL_PRESSURE_DROP_RATE",
                    TextParameters = new Dictionary<string, string>
                    {
                        ["engine"] = engineIndex.ToString(),
                        ["pressure_psi"] = oilPressure.ToString("F1"),
                        ["drop_rate_psi_per_min"] = dropRatePerMin.ToString("F1"),
                    },
                    FlightPhase = phase,
                    TelemetrySnapshot = new Dictionary<string, double>
                    {
                        [$"GeneralEngOilPressure:{engineIndex}"] = oilPressure,
                    },
                };
            }
        }

        // 3. Cross-correlation: temp rising + pressure falling — WARNING (lubrication concern)
        if (oilTempRankine is not null)
        {
            var tempRate = buffer.RateOfChange(SimVarId.GeneralEngOilTemperature, RateWindow, engineIndex);
            if (dropRate is not null && tempRate is not null &&
                dropRate.Value < 0 && tempRate.Value > 0)
            {
                // Both moving in opposite directions = concerning
                double pressureDropPerMin = UnitsConverter.PerSecondToPerMinute(Math.Abs(dropRate.Value));
                double tempRisePerMin = UnitsConverter.PerSecondToPerMinute(tempRate.Value);

                // Only alert if both rates are meaningful
                if (pressureDropPerMin > 2.0 && tempRisePerMin > 1.0)
                {
                    double oilTempF = UnitsConverter.RankineToFahrenheit(oilTempRankine.Value);
                    return new Alert
                    {
                        RuleId = RuleId,
                        Severity = AlertSeverity.Warning,
                        TextKey = "R004_OIL_TEMP_PRESSURE_DIVERGENCE",
                        TextParameters = new Dictionary<string, string>
                        {
                            ["engine"] = engineIndex.ToString(),
                            ["pressure_psi"] = oilPressure.ToString("F1"),
                            ["oil_temp_f"] = oilTempF.ToString("F0"),
                            ["pressure_drop_per_min"] = pressureDropPerMin.ToString("F1"),
                            ["temp_rise_per_min"] = tempRisePerMin.ToString("F1"),
                        },
                        FlightPhase = phase,
                        TelemetrySnapshot = new Dictionary<string, double>
                        {
                            [$"GeneralEngOilPressure:{engineIndex}"] = oilPressure,
                            [$"GeneralEngOilTemperature:{engineIndex}"] = oilTempRankine.Value,
                        },
                    };
                }
            }
        }

        return null;
    }
}

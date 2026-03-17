using Guardian.Core;
using Serilog;

namespace Guardian.Detection.Rules;

/// <summary>
/// R003: Engine Temperature Trend
///
/// Monitors CHT and EGT rate-of-change over a 60-second sliding window
/// and absolute values against profile-defined redline thresholds.
///
/// Rate-of-change (trend) thresholds:
///   ADVISORY — rising faster than cht_trend_advisory per profile
///   WARNING — rising faster than cht_trend_warning per profile
///
/// Absolute value thresholds:
///   WARNING — ≥ 90% of redline
///   CRITICAL — ≥ 95% of redline
///
/// Flight-phase awareness:
///   During CLIMB, rate-of-change thresholds are relaxed by 50%
///   (higher temps during climb are normal).
/// </summary>
public sealed class R003_EngineTemperatureTrend : IDetectionRule
{
    private static readonly ILogger Log = Serilog.Log.ForContext<R003_EngineTemperatureTrend>();

    public string RuleId => "R003";
    public string Name => "Engine Temperature Trend";
    public string Description => "Monitors CHT/EGT rate-of-change and absolute values against redline thresholds.";
    public TimeSpan EvaluationInterval => TimeSpan.FromSeconds(5);

    private static readonly TimeSpan TrendWindow = TimeSpan.FromSeconds(60);

    // Absolute threshold percentages of redline
    private const double WarningPct = 0.90;
    private const double CriticalPct = 0.95;

    // Climb phase relaxation factor for rate thresholds
    private const double ClimbRelaxationFactor = 1.5;

    public bool IsApplicable(AircraftProfile profile, FlightPhase phase)
    {
        // Always applicable when engines are expected to be running
        return phase != FlightPhase.Ground;
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
            // Skip engines that aren't running
            var combustion = current.Get(SimVarId.GeneralEngCombustion, eng) ?? 0;
            if (combustion < 0.5) continue;

            var chtAlert = EvaluateCht(current, buffer, profile, phase, eng);
            worstAlert = PickWorst(worstAlert, chtAlert);

            var egtAlert = EvaluateEgt(current, buffer, profile, phase, eng);
            worstAlert = PickWorst(worstAlert, egtAlert);
        }

        return worstAlert;
    }

    private Alert? EvaluateCht(
        TelemetrySnapshot current, ITelemetryBuffer buffer,
        AircraftProfile profile, FlightPhase phase, int engineIndex)
    {
        var cht = current.Get(SimVarId.EngCylinderHeadTemperature, engineIndex);
        if (cht is null) return null;

        var eng = profile.Engine;

        // --- Absolute value checks ---
        var absoluteAlert = CheckAbsolute(
            cht.Value, eng.ChtRedlineRankine,
            "CHT", engineIndex, phase,
            cht.Value, SimVarId.EngCylinderHeadTemperature);

        if (absoluteAlert is not null) return absoluteAlert;

        // --- Rate-of-change checks ---
        var rate = buffer.RateOfChange(SimVarId.EngCylinderHeadTemperature, TrendWindow, engineIndex);
        if (rate is null || rate.Value <= 0) return null; // only care about rising temps

        double advisoryThreshold = eng.ChtTrendAdvisoryRankinePerSec;
        double warningThreshold = eng.ChtTrendWarningRankinePerSec;

        // Relax thresholds during climb
        if (phase == FlightPhase.Climb)
        {
            advisoryThreshold *= ClimbRelaxationFactor;
            warningThreshold *= ClimbRelaxationFactor;
        }

        if (rate.Value >= warningThreshold)
        {
            var ratePerMin = UnitsConverter.PerSecondToPerMinute(rate.Value);
            return MakeTrendAlert(AlertSeverity.Warning, "R003_CHT_TREND_WARNING",
                "CHT", engineIndex, ratePerMin, phase, cht.Value);
        }

        if (rate.Value >= advisoryThreshold)
        {
            var ratePerMin = UnitsConverter.PerSecondToPerMinute(rate.Value);
            return MakeTrendAlert(AlertSeverity.Advisory, "R003_CHT_TREND_ADVISORY",
                "CHT", engineIndex, ratePerMin, phase, cht.Value);
        }

        return null;
    }

    private Alert? EvaluateEgt(
        TelemetrySnapshot current, ITelemetryBuffer buffer,
        AircraftProfile profile, FlightPhase phase, int engineIndex)
    {
        var egt = current.Get(SimVarId.EngExhaustGasTemperature, engineIndex);
        if (egt is null) return null;

        var eng = profile.Engine;

        // --- Absolute value checks only for EGT ---
        return CheckAbsolute(
            egt.Value, eng.EgtRedlineRankine,
            "EGT", engineIndex, phase,
            egt.Value, SimVarId.EngExhaustGasTemperature);
    }

    private Alert? CheckAbsolute(
        double currentValue, double redlineValue,
        string tempType, int engineIndex, FlightPhase phase,
        double rawRankine, SimVarId simVarId)
    {
        if (redlineValue <= 0) return null; // no redline defined

        double pctOfRedline = currentValue / redlineValue;
        double tempF = UnitsConverter.RankineToFahrenheit(rawRankine);
        double redlineF = UnitsConverter.RankineToFahrenheit(redlineValue);

        if (pctOfRedline >= CriticalPct)
        {
            return new Alert
            {
                RuleId = RuleId,
                Severity = AlertSeverity.Critical,
                TextKey = $"R003_{tempType}_REDLINE_CRITICAL",
                TextParameters = new Dictionary<string, string>
                {
                    ["temp_type"] = tempType,
                    ["engine"] = engineIndex.ToString(),
                    ["current_f"] = tempF.ToString("F0"),
                    ["redline_f"] = redlineF.ToString("F0"),
                    ["pct"] = (pctOfRedline * 100).ToString("F0"),
                },
                FlightPhase = phase,
                TelemetrySnapshot = new Dictionary<string, double>
                {
                    [$"{simVarId}:{engineIndex}"] = rawRankine,
                },
            };
        }

        if (pctOfRedline >= WarningPct)
        {
            return new Alert
            {
                RuleId = RuleId,
                Severity = AlertSeverity.Warning,
                TextKey = $"R003_{tempType}_REDLINE_WARNING",
                TextParameters = new Dictionary<string, string>
                {
                    ["temp_type"] = tempType,
                    ["engine"] = engineIndex.ToString(),
                    ["current_f"] = tempF.ToString("F0"),
                    ["redline_f"] = redlineF.ToString("F0"),
                    ["pct"] = (pctOfRedline * 100).ToString("F0"),
                },
                FlightPhase = phase,
                TelemetrySnapshot = new Dictionary<string, double>
                {
                    [$"{simVarId}:{engineIndex}"] = rawRankine,
                },
            };
        }

        return null;
    }

    private Alert MakeTrendAlert(
        AlertSeverity severity, string textKey,
        string tempType, int engineIndex, double ratePerMin,
        FlightPhase phase, double currentRankine)
    {
        double tempF = UnitsConverter.RankineToFahrenheit(currentRankine);

        return new Alert
        {
            RuleId = RuleId,
            Severity = severity,
            TextKey = textKey,
            TextParameters = new Dictionary<string, string>
            {
                ["temp_type"] = tempType,
                ["engine"] = engineIndex.ToString(),
                ["rate_f_per_min"] = ratePerMin.ToString("F1"),
                ["current_f"] = tempF.ToString("F0"),
            },
            FlightPhase = phase,
            TelemetrySnapshot = new Dictionary<string, double>
            {
                [$"EngCylinderHeadTemperature:{engineIndex}"] = currentRankine,
            },
        };
    }

    private static Alert? PickWorst(Alert? current, Alert? candidate)
    {
        if (candidate is null) return current;
        if (current is null) return candidate;
        return candidate.Severity > current.Severity ? candidate : current;
    }
}

using Guardian.Core;
using Serilog;

namespace Guardian.Detection.Rules;

/// <summary>
/// R007: Electrical Degradation
///
/// Monitors main bus and battery bus voltage for:
///   1. Absolute minimum threshold breach → CRITICAL
///   2. Sustained decrease trend over 120 seconds → WARNING
///   3. Bus voltage below normal range → ADVISORY
/// </summary>
public sealed class R007_ElectricalDegradation : IDetectionRule
{
    private static readonly ILogger Log = Serilog.Log.ForContext<R007_ElectricalDegradation>();

    public string RuleId => "R007";
    public string Name => "Electrical Degradation";
    public string Description => "Monitors bus voltage for absolute minimums and sustained decrease trends.";
    public TimeSpan EvaluationInterval => TimeSpan.FromSeconds(10);

    private static readonly TimeSpan TrendWindow = TimeSpan.FromSeconds(120);

    // Minimum voltage drop rate to consider a trend (volts per second)
    private const double MinTrendRateVPerSec = 0.001; // ~0.06V/min

    public bool IsApplicable(AircraftProfile profile, FlightPhase phase)
    {
        // Always applicable — electrical monitoring is relevant in all phases
        return true;
    }

    public Alert? Evaluate(
        TelemetrySnapshot current,
        ITelemetryBuffer buffer,
        AircraftProfile profile,
        FlightPhase phase)
    {
        var elec = profile.Electrical;

        var mainBusV = current.Get(SimVarId.ElectricalMainBusVoltage);
        var batteryBusV = current.Get(SimVarId.ElectricalBatteryBusVoltage);

        // Check main bus voltage
        if (mainBusV is not null)
        {
            // CRITICAL — below absolute minimum
            if (mainBusV.Value < elec.MainBusMinimumV)
            {
                return new Alert
                {
                    RuleId = RuleId,
                    Severity = AlertSeverity.Critical,
                    TextKey = "R007_MAIN_BUS_CRITICAL",
                    TextParameters = new Dictionary<string, string>
                    {
                        ["voltage"] = mainBusV.Value.ToString("F1"),
                        ["minimum_v"] = elec.MainBusMinimumV.ToString("F1"),
                    },
                    FlightPhase = phase,
                    TelemetrySnapshot = BuildSnapshot(mainBusV, batteryBusV),
                };
            }

            // WARNING — sustained decrease trend
            var mainRate = buffer.RateOfChange(SimVarId.ElectricalMainBusVoltage, TrendWindow);
            if (mainRate is not null && mainRate.Value < -MinTrendRateVPerSec)
            {
                double dropPerMin = UnitsConverter.PerSecondToPerMinute(Math.Abs(mainRate.Value));
                return new Alert
                {
                    RuleId = RuleId,
                    Severity = AlertSeverity.Warning,
                    TextKey = "R007_MAIN_BUS_TREND_WARNING",
                    TextParameters = new Dictionary<string, string>
                    {
                        ["voltage"] = mainBusV.Value.ToString("F1"),
                        ["drop_rate_v_per_min"] = dropPerMin.ToString("F2"),
                    },
                    FlightPhase = phase,
                    TelemetrySnapshot = BuildSnapshot(mainBusV, batteryBusV),
                };
            }

            // ADVISORY — below normal range
            if (mainBusV.Value < elec.MainBusNormalV[0])
            {
                return new Alert
                {
                    RuleId = RuleId,
                    Severity = AlertSeverity.Advisory,
                    TextKey = "R007_MAIN_BUS_LOW_ADVISORY",
                    TextParameters = new Dictionary<string, string>
                    {
                        ["voltage"] = mainBusV.Value.ToString("F1"),
                        ["normal_low"] = elec.MainBusNormalV[0].ToString("F1"),
                    },
                    FlightPhase = phase,
                    TelemetrySnapshot = BuildSnapshot(mainBusV, batteryBusV),
                };
            }
        }

        // Check battery bus
        if (batteryBusV is not null && batteryBusV.Value < elec.BatteryBusMinimumV)
        {
            return new Alert
            {
                RuleId = RuleId,
                Severity = AlertSeverity.Critical,
                TextKey = "R007_BATTERY_BUS_CRITICAL",
                TextParameters = new Dictionary<string, string>
                {
                    ["voltage"] = batteryBusV.Value.ToString("F1"),
                    ["minimum_v"] = elec.BatteryBusMinimumV.ToString("F1"),
                },
                FlightPhase = phase,
                TelemetrySnapshot = BuildSnapshot(mainBusV, batteryBusV),
            };
        }

        return null;
    }

    private static Dictionary<string, double> BuildSnapshot(double? mainBusV, double? batteryBusV)
    {
        var snap = new Dictionary<string, double>();
        if (mainBusV is not null) snap["ElectricalMainBusVoltage"] = mainBusV.Value;
        if (batteryBusV is not null) snap["ElectricalBatteryBusVoltage"] = batteryBusV.Value;
        return snap;
    }
}

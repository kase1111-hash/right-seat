using Guardian.Core;
using Serilog;

namespace Guardian.Detection.Rules;

/// <summary>
/// R001: Fuel Cross-Feed Mismatch
///
/// Detects when multiple engines are drawing from the same fuel tank while other
/// tanks still have usable fuel. Calculates time-to-exhaustion and escalates
/// based on remaining time at current combined burn rate.
///
/// Also detects sensor/selector disagreement: fuel flow registered on an engine
/// whose fuel selector is set to OFF or to a different engine's tank.
///
/// Applicable only to multi-engine aircraft (engine_count > 1).
///
/// Escalation:
///   WARNING — both engines on same tank, > 30 min to exhaustion
///   CRITICAL — both engines on same tank, ≤ 30 min to exhaustion
///   CRITICAL (urgent) — ≤ 15 min to exhaustion
/// </summary>
public sealed class R001_FuelCrossFeedMismatch : IDetectionRule
{
    private static readonly ILogger Log = Serilog.Log.ForContext<R001_FuelCrossFeedMismatch>();

    public string RuleId => "R001";
    public string Name => "Fuel Cross-Feed Mismatch";
    public string Description => "Detects multiple engines drawing from the same tank while other tanks have fuel.";
    public TimeSpan EvaluationInterval => TimeSpan.FromSeconds(5);

    // Time-to-exhaustion thresholds (minutes)
    private const double CriticalUrgentMinutes = 15;
    private const double CriticalMinutes = 30;

    public bool IsApplicable(AircraftProfile profile, FlightPhase phase)
    {
        // Only applicable to multi-engine aircraft
        if (profile.EngineCount < 2)
            return false;

        // Only when engines are running (not on ground with engines off)
        // The actual combustion check is done in Evaluate
        return true;
    }

    public Alert? Evaluate(
        TelemetrySnapshot current,
        ITelemetryBuffer buffer,
        AircraftProfile profile,
        FlightPhase phase)
    {
        var engineCount = profile.EngineCount;

        // Get fuel selectors and fuel flow for each engine
        var selectors = new double[engineCount];
        var fuelFlows = new double[engineCount];
        var combustion = new bool[engineCount];

        for (int i = 1; i <= engineCount; i++)
        {
            selectors[i - 1] = current.Get(SimVarId.FuelTankSelector, i) ?? -1;
            fuelFlows[i - 1] = current.Get(SimVarId.GeneralEngFuelFlow, i) ?? 0;
            combustion[i - 1] = (current.Get(SimVarId.GeneralEngCombustion, i) ?? 0) > 0.5;
        }

        // If no engines running, skip
        if (!combustion.Any(c => c))
            return null;

        // Check for sensor/selector disagreement
        var disagreementAlert = CheckSelectorDisagreement(
            selectors, fuelFlows, combustion, engineCount, phase);
        if (disagreementAlert is not null)
            return disagreementAlert;

        // Check for cross-feed: multiple engines on same tank
        var crossFeedAlert = CheckCrossFeed(
            current, selectors, fuelFlows, combustion, engineCount, profile, phase);

        return crossFeedAlert;
    }

    private Alert? CheckSelectorDisagreement(
        double[] selectors, double[] fuelFlows, bool[] combustion,
        int engineCount, FlightPhase phase)
    {
        for (int i = 0; i < engineCount; i++)
        {
            // Fuel flow > 0 but selector is OFF (value 0)
            if (fuelFlows[i] > 0.5 && selectors[i] < 0.5 && combustion[i])
            {
                return new Alert
                {
                    RuleId = RuleId,
                    Severity = AlertSeverity.Warning,
                    TextKey = "R001_SELECTOR_DISAGREEMENT",
                    TextParameters = new Dictionary<string, string>
                    {
                        ["engine"] = (i + 1).ToString(),
                        ["fuel_flow_gph"] = fuelFlows[i].ToString("F1"),
                        ["selector"] = "OFF",
                    },
                    FlightPhase = phase,
                    TelemetrySnapshot = BuildSnapshot(selectors, fuelFlows, combustion),
                };
            }
        }
        return null;
    }

    private Alert? CheckCrossFeed(
        TelemetrySnapshot current,
        double[] selectors, double[] fuelFlows, bool[] combustion,
        int engineCount, AircraftProfile profile, FlightPhase phase)
    {
        // Group engines by their fuel tank selector
        var enginesByTank = new Dictionary<int, List<int>>();
        for (int i = 0; i < engineCount; i++)
        {
            if (!combustion[i]) continue;

            var tankId = (int)selectors[i];
            if (tankId < 0) continue; // unknown selector

            if (!enginesByTank.ContainsKey(tankId))
                enginesByTank[tankId] = new List<int>();
            enginesByTank[tankId].Add(i);
        }

        // Find tanks feeding multiple engines
        foreach (var (tankId, engines) in enginesByTank)
        {
            if (engines.Count < 2) continue;

            // Multiple engines on same tank — check if other tanks have fuel
            double combinedBurnRate = engines.Sum(e => fuelFlows[e]);
            if (combinedBurnRate < 0.1) continue; // negligible flow

            // Get tank quantity (approximate — use total fuel divided by tank count as fallback)
            double? tankQty = current.Get(SimVarId.FuelSystemTankQuantity, tankId);
            double? totalFuel = current.Get(SimVarId.FuelTotalQuantity);

            // Check other tanks for unused fuel
            double otherTanksFuel = 0;
            for (int t = 0; t < profile.Fuel.TankCount; t++)
            {
                if (t == tankId) continue;
                var qty = current.Get(SimVarId.FuelSystemTankQuantity, t) ?? 0;
                otherTanksFuel += qty;
            }

            if (otherTanksFuel < 1.0) continue; // other tanks empty, cross-feed is appropriate

            // Calculate time to exhaustion on current tank
            double currentTankQty = tankQty ?? (totalFuel ?? 0) / Math.Max(profile.Fuel.TankCount, 1);
            double timeToExhaustionHours = currentTankQty / combinedBurnRate;
            double timeToExhaustionMinutes = timeToExhaustionHours * 60;

            // Determine severity based on time remaining
            AlertSeverity severity;
            string textKey;

            if (timeToExhaustionMinutes <= CriticalUrgentMinutes)
            {
                severity = AlertSeverity.Critical;
                textKey = "R001_CROSSFEED_CRITICAL_URGENT";
            }
            else if (timeToExhaustionMinutes <= CriticalMinutes)
            {
                severity = AlertSeverity.Critical;
                textKey = "R001_CROSSFEED_CRITICAL";
            }
            else
            {
                severity = AlertSeverity.Warning;
                textKey = "R001_CROSSFEED_WARNING";
            }

            var tankName = tankId < profile.Fuel.TankNames.Count
                ? profile.Fuel.TankNames[tankId]
                : $"tank_{tankId}";

            return new Alert
            {
                RuleId = RuleId,
                Severity = severity,
                TextKey = textKey,
                TextParameters = new Dictionary<string, string>
                {
                    ["tank"] = tankName,
                    ["engine_count"] = engines.Count.ToString(),
                    ["engines"] = string.Join(", ", engines.Select(e => (e + 1).ToString())),
                    ["time_min"] = timeToExhaustionMinutes.ToString("F0"),
                    ["burn_rate_gph"] = combinedBurnRate.ToString("F1"),
                    ["unused_gal"] = otherTanksFuel.ToString("F0"),
                },
                FlightPhase = phase,
                TelemetrySnapshot = BuildSnapshot(selectors, fuelFlows, combustion),
            };
        }

        return null;
    }

    private static Dictionary<string, double> BuildSnapshot(
        double[] selectors, double[] fuelFlows, bool[] combustion)
    {
        var snap = new Dictionary<string, double>();
        for (int i = 0; i < selectors.Length; i++)
        {
            snap[$"FuelTankSelector:{i + 1}"] = selectors[i];
            snap[$"GeneralEngFuelFlow:{i + 1}"] = fuelFlows[i];
            snap[$"GeneralEngCombustion:{i + 1}"] = combustion[i] ? 1.0 : 0.0;
        }
        return snap;
    }
}

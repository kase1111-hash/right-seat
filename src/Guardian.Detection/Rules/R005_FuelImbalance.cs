using Guardian.Core;
using Serilog;

namespace Guardian.Detection.Rules;

/// <summary>
/// R005: Fuel Imbalance
///
/// Monitors left/right tank quantity divergence as a percentage of total fuel.
///
/// Escalation:
///   ADVISORY — imbalance exceeds advisory threshold AND trend is growing
///   WARNING — imbalance exceeds warning threshold
///   CRITICAL — any single tank below minimum fuel quantity
///
/// Trend-aware: at ADVISORY level, only alerts if imbalance is increasing
/// (a stable imbalance may be intentional fuel management).
/// </summary>
public sealed class R005_FuelImbalance : IDetectionRule
{
    private static readonly ILogger Log = Serilog.Log.ForContext<R005_FuelImbalance>();

    public string RuleId => "R005";
    public string Name => "Fuel Imbalance";
    public string Description => "Monitors left/right tank quantity divergence and minimum fuel levels.";
    public TimeSpan EvaluationInterval => TimeSpan.FromSeconds(10);

    private static readonly TimeSpan TrendWindow = TimeSpan.FromSeconds(120);

    public bool IsApplicable(AircraftProfile profile, FlightPhase phase)
    {
        // Need at least 2 tanks and engines running
        return profile.Fuel.TankCount >= 2;
    }

    public Alert? Evaluate(
        TelemetrySnapshot current,
        ITelemetryBuffer buffer,
        AircraftProfile profile,
        FlightPhase phase)
    {
        var fuel = profile.Fuel;

        // Get tank quantities (use first two tanks as left/right)
        var tankQtys = new double[fuel.TankCount];
        double totalFuel = 0;

        for (int t = 0; t < fuel.TankCount; t++)
        {
            tankQtys[t] = current.Get(SimVarId.FuelSystemTankQuantity, t) ?? 0;
            totalFuel += tankQtys[t];
        }

        if (totalFuel < 1.0) return null; // effectively empty, nothing to balance

        // Check minimum fuel per tank — CRITICAL
        for (int t = 0; t < fuel.TankCount; t++)
        {
            if (tankQtys[t] < fuel.MinimumFuelWarningGal && totalFuel > fuel.MinimumFuelWarningGal * 2)
            {
                // One tank critically low but other tanks have fuel
                var tankName = t < fuel.TankNames.Count ? fuel.TankNames[t] : $"tank_{t}";
                return new Alert
                {
                    RuleId = RuleId,
                    Severity = AlertSeverity.Critical,
                    TextKey = "R005_TANK_MINIMUM_FUEL",
                    TextParameters = new Dictionary<string, string>
                    {
                        ["tank"] = tankName,
                        ["quantity_gal"] = tankQtys[t].ToString("F1"),
                        ["minimum_gal"] = fuel.MinimumFuelWarningGal.ToString("F1"),
                    },
                    FlightPhase = phase,
                    TelemetrySnapshot = BuildFuelSnapshot(tankQtys, fuel),
                };
            }
        }

        // Calculate imbalance between primary tank pairs (index 0 = left, index 1 = right)
        if (fuel.TankCount < 2) return null;

        double leftQty = tankQtys[0];
        double rightQty = tankQtys[1];
        double pairTotal = leftQty + rightQty;

        if (pairTotal < 2.0) return null;

        double imbalancePct = Math.Abs(leftQty - rightQty) / pairTotal * 100.0;

        // WARNING threshold
        if (imbalancePct >= fuel.ImbalanceWarningPct)
        {
            var heavySide = leftQty > rightQty ? "left" : "right";
            return new Alert
            {
                RuleId = RuleId,
                Severity = AlertSeverity.Warning,
                TextKey = "R005_FUEL_IMBALANCE_WARNING",
                TextParameters = new Dictionary<string, string>
                {
                    ["imbalance_pct"] = imbalancePct.ToString("F0"),
                    ["heavy_side"] = heavySide,
                    ["left_gal"] = leftQty.ToString("F1"),
                    ["right_gal"] = rightQty.ToString("F1"),
                },
                FlightPhase = phase,
                TelemetrySnapshot = BuildFuelSnapshot(tankQtys, fuel),
            };
        }

        // ADVISORY threshold — only if trend is growing
        if (imbalancePct >= fuel.ImbalanceAdvisoryPct)
        {
            // Check if imbalance is growing by comparing tank rate-of-change
            var leftRate = buffer.RateOfChange(SimVarId.FuelSystemTankQuantity, TrendWindow, 0);
            var rightRate = buffer.RateOfChange(SimVarId.FuelSystemTankQuantity, TrendWindow, 1);

            bool imbalanceGrowing = false;
            if (leftRate is not null && rightRate is not null)
            {
                // If the lighter side is draining faster, imbalance is growing
                if (leftQty < rightQty && leftRate.Value < rightRate.Value)
                    imbalanceGrowing = true;
                else if (rightQty < leftQty && rightRate.Value < leftRate.Value)
                    imbalanceGrowing = true;
            }
            else
            {
                // No trend data — alert anyway at advisory level
                imbalanceGrowing = true;
            }

            if (imbalanceGrowing)
            {
                var heavySide = leftQty > rightQty ? "left" : "right";
                return new Alert
                {
                    RuleId = RuleId,
                    Severity = AlertSeverity.Advisory,
                    TextKey = "R005_FUEL_IMBALANCE_ADVISORY",
                    TextParameters = new Dictionary<string, string>
                    {
                        ["imbalance_pct"] = imbalancePct.ToString("F0"),
                        ["heavy_side"] = heavySide,
                        ["left_gal"] = leftQty.ToString("F1"),
                        ["right_gal"] = rightQty.ToString("F1"),
                    },
                    FlightPhase = phase,
                    TelemetrySnapshot = BuildFuelSnapshot(tankQtys, fuel),
                };
            }
        }

        return null;
    }

    private static Dictionary<string, double> BuildFuelSnapshot(double[] tankQtys, FuelProfile fuel)
    {
        var snap = new Dictionary<string, double>();
        for (int t = 0; t < tankQtys.Length; t++)
        {
            var name = t < fuel.TankNames.Count ? fuel.TankNames[t] : $"tank_{t}";
            snap[$"FuelSystemTankQuantity:{name}"] = tankQtys[t];
        }
        return snap;
    }
}

using Guardian.Core;
using Guardian.Detection.Rules;
using Xunit;

namespace Guardian.Detection.Tests;

public class R005_FuelImbalanceTests
{
    private readonly R005_FuelImbalance _rule = new();

    private static AircraftProfile MakeProfile() => new()
    {
        AircraftId = "c172sp",
        EngineCount = 1,
        Fuel = new FuelProfile
        {
            TankCount = 2,
            TankNames = new List<string> { "left", "right" },
            TotalCapacityGal = 56,
            UsableCapacityGal = 53,
            ImbalanceAdvisoryPct = 10,
            ImbalanceWarningPct = 20,
            MinimumFuelWarningGal = 8,
        },
    };

    [Fact]
    public void BalancedTanks_NoAlert()
    {
        var snap = MakeFuelSnapshot(left: 25, right: 25);
        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), MakeProfile(), FlightPhase.Cruise);
        Assert.Null(alert);
    }

    [Fact]
    public void SlightImbalance_BelowAdvisory_NoAlert()
    {
        // 4% imbalance (below 10% threshold)
        var snap = MakeFuelSnapshot(left: 26, right: 24);
        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), MakeProfile(), FlightPhase.Cruise);
        Assert.Null(alert);
    }

    [Fact]
    public void ImbalanceAboveAdvisory_Growing_Advisory()
    {
        // 15% imbalance (above 10%, below 20%)
        var snap = MakeFuelSnapshot(left: 28, right: 20); // diff=8, total=48, 16.7%

        var buffer = new MockTelemetryBuffer();
        // Right side draining faster — imbalance growing
        buffer.SetRate(SimVarId.FuelSystemTankQuantity, -0.001, 0); // left draining slowly
        buffer.SetRate(SimVarId.FuelSystemTankQuantity, -0.005, 1); // right draining faster

        var alert = _rule.Evaluate(snap, buffer, MakeProfile(), FlightPhase.Cruise);

        Assert.NotNull(alert);
        Assert.Equal("R005", alert!.RuleId);
        Assert.Equal(AlertSeverity.Advisory, alert.Severity);
        Assert.Contains("ADVISORY", alert.TextKey);
        Assert.Equal("left", alert.TextParameters["heavy_side"]);
    }

    [Fact]
    public void ImbalanceAboveWarning_Warning()
    {
        // 25% imbalance (above 20%)
        var snap = MakeFuelSnapshot(left: 30, right: 18); // diff=12, total=48, 25%

        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), MakeProfile(), FlightPhase.Cruise);

        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.Warning, alert!.Severity);
        Assert.Contains("WARNING", alert.TextKey);
    }

    [Fact]
    public void TankBelowMinimum_OtherHasFuel_Critical()
    {
        // Left tank at 5 gal (below 8 gal minimum), right has 30 gal
        var snap = MakeFuelSnapshot(left: 5, right: 30);

        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), MakeProfile(), FlightPhase.Cruise);

        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.Critical, alert!.Severity);
        Assert.Contains("MINIMUM", alert.TextKey);
        Assert.Equal("left", alert.TextParameters["tank"]);
    }

    [Fact]
    public void BothTanksLow_NoMinimumAlert()
    {
        // Both tanks low — not an imbalance issue, it's a total fuel issue
        // (totalFuel <= minWarning * 2, so minimum check is skipped)
        var snap = MakeFuelSnapshot(left: 5, right: 5);

        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), MakeProfile(), FlightPhase.Cruise);

        // Should not trigger minimum alert when both tanks are equally low
        if (alert is not null)
            Assert.NotEqual("R005_TANK_MINIMUM_FUEL", alert.TextKey);
    }

    [Fact]
    public void EmptyTanks_NoAlert()
    {
        var snap = MakeFuelSnapshot(left: 0.3, right: 0.3);
        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), MakeProfile(), FlightPhase.Cruise);
        Assert.Null(alert); // total < 1 gal, nothing to balance
    }

    [Fact]
    public void SingleTank_NotApplicable()
    {
        var profile = new AircraftProfile
        {
            Fuel = new FuelProfile { TankCount = 1 },
        };
        Assert.False(_rule.IsApplicable(profile, FlightPhase.Cruise));
    }

    private static TelemetrySnapshot MakeFuelSnapshot(double left, double right)
    {
        var snap = new TelemetrySnapshot();
        snap.Set(SimVarId.FuelSystemTankQuantity, left, 0);
        snap.Set(SimVarId.FuelSystemTankQuantity, right, 1);
        snap.Set(SimVarId.FuelTotalQuantity, left + right);
        return snap;
    }
}

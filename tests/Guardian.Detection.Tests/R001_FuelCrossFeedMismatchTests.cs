using Guardian.Core;
using Guardian.Detection.Rules;
using Xunit;

namespace Guardian.Detection.Tests;

public class R001_FuelCrossFeedMismatchTests
{
    private readonly R001_FuelCrossFeedMismatch _rule = new();

    private static AircraftProfile TwinProfile => new()
    {
        AircraftId = "be58_baron",
        EngineCount = 2,
        Fuel = new FuelProfile
        {
            TankCount = 2,
            TankNames = new List<string> { "left", "right" },
            TotalCapacityGal = 166,
            UsableCapacityGal = 162,
        },
    };

    private static AircraftProfile SingleProfile => new()
    {
        AircraftId = "c172sp",
        EngineCount = 1,
    };

    [Fact]
    public void IsApplicable_SingleEngine_ReturnsFalse()
    {
        Assert.False(_rule.IsApplicable(SingleProfile, FlightPhase.Cruise));
    }

    [Fact]
    public void IsApplicable_TwinEngine_ReturnsTrue()
    {
        Assert.True(_rule.IsApplicable(TwinProfile, FlightPhase.Cruise));
    }

    [Fact]
    public void NormalOps_BothEnginesDifferentTanks_NoAlert()
    {
        var snap = new TelemetrySnapshot();
        // Engine 1 on left tank (selector=0), Engine 2 on right tank (selector=1)
        snap.Set(SimVarId.FuelTankSelector, 0, index: 1);
        snap.Set(SimVarId.FuelTankSelector, 1, index: 2);
        snap.Set(SimVarId.GeneralEngFuelFlow, 14.0, index: 1);
        snap.Set(SimVarId.GeneralEngFuelFlow, 14.0, index: 2);
        snap.Set(SimVarId.GeneralEngCombustion, 1.0, index: 1);
        snap.Set(SimVarId.GeneralEngCombustion, 1.0, index: 2);
        snap.Set(SimVarId.FuelSystemTankQuantity, 40.0, index: 0);
        snap.Set(SimVarId.FuelSystemTankQuantity, 40.0, index: 1);

        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), TwinProfile, FlightPhase.Cruise);
        Assert.Null(alert);
    }

    [Fact]
    public void BothEnginesSameTank_OtherTankHasFuel_Warning()
    {
        var snap = MakeCrossFeedSnapshot(
            bothOnTank: 0,
            fuelFlowPerEngine: 14.0,
            activeTankQty: 50.0,    // ~107 min at 28 gph combined
            otherTankQty: 40.0);

        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), TwinProfile, FlightPhase.Cruise);

        Assert.NotNull(alert);
        Assert.Equal("R001", alert!.RuleId);
        Assert.Equal(AlertSeverity.Warning, alert.Severity);
        Assert.Contains("CROSSFEED_WARNING", alert.TextKey);
    }

    [Fact]
    public void BothEnginesSameTank_30MinToExhaustion_Critical()
    {
        // 28 gph combined, need ≤30 min = 14 gal tank
        var snap = MakeCrossFeedSnapshot(
            bothOnTank: 0,
            fuelFlowPerEngine: 14.0,
            activeTankQty: 12.0,    // ~25.7 min → CRITICAL
            otherTankQty: 40.0);

        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), TwinProfile, FlightPhase.Cruise);

        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.Critical, alert!.Severity);
        Assert.Contains("CRITICAL", alert.TextKey);
    }

    [Fact]
    public void BothEnginesSameTank_15MinToExhaustion_CriticalUrgent()
    {
        // 28 gph combined, need ≤15 min = 7 gal tank
        var snap = MakeCrossFeedSnapshot(
            bothOnTank: 0,
            fuelFlowPerEngine: 14.0,
            activeTankQty: 5.0,     // ~10.7 min → CRITICAL URGENT
            otherTankQty: 40.0);

        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), TwinProfile, FlightPhase.Cruise);

        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.Critical, alert!.Severity);
        Assert.Contains("URGENT", alert.TextKey);
    }

    [Fact]
    public void BothEnginesSameTank_OtherTankEmpty_NoAlert()
    {
        // Other tank is empty — cross-feed is appropriate
        var snap = MakeCrossFeedSnapshot(
            bothOnTank: 0,
            fuelFlowPerEngine: 14.0,
            activeTankQty: 50.0,
            otherTankQty: 0.5);     // less than 1 gal = effectively empty

        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), TwinProfile, FlightPhase.Cruise);
        Assert.Null(alert);
    }

    [Fact]
    public void EnginesNotRunning_NoAlert()
    {
        var snap = new TelemetrySnapshot();
        snap.Set(SimVarId.FuelTankSelector, 0, index: 1);
        snap.Set(SimVarId.FuelTankSelector, 0, index: 2);
        snap.Set(SimVarId.GeneralEngFuelFlow, 0, index: 1);
        snap.Set(SimVarId.GeneralEngFuelFlow, 0, index: 2);
        snap.Set(SimVarId.GeneralEngCombustion, 0, index: 1);
        snap.Set(SimVarId.GeneralEngCombustion, 0, index: 2);

        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), TwinProfile, FlightPhase.Ground);
        Assert.Null(alert);
    }

    [Fact]
    public void SelectorDisagreement_FuelFlowWithSelectorOff_Warning()
    {
        var snap = new TelemetrySnapshot();
        // Engine 1: selector OFF (0) but fuel flowing
        snap.Set(SimVarId.FuelTankSelector, 0, index: 1);
        snap.Set(SimVarId.GeneralEngFuelFlow, 14.0, index: 1);
        snap.Set(SimVarId.GeneralEngCombustion, 1.0, index: 1);
        // Engine 2: normal
        snap.Set(SimVarId.FuelTankSelector, 1, index: 2);
        snap.Set(SimVarId.GeneralEngFuelFlow, 14.0, index: 2);
        snap.Set(SimVarId.GeneralEngCombustion, 1.0, index: 2);

        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), TwinProfile, FlightPhase.Cruise);

        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.Warning, alert!.Severity);
        Assert.Contains("DISAGREEMENT", alert.TextKey);
    }

    [Fact]
    public void AlertParameters_IncludeTankName()
    {
        var snap = MakeCrossFeedSnapshot(
            bothOnTank: 0,
            fuelFlowPerEngine: 14.0,
            activeTankQty: 50.0,
            otherTankQty: 40.0);

        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), TwinProfile, FlightPhase.Cruise);

        Assert.NotNull(alert);
        Assert.Equal("left", alert!.TextParameters["tank"]);
        Assert.Contains("2", alert.TextParameters["engine_count"]);
    }

    private static TelemetrySnapshot MakeCrossFeedSnapshot(
        int bothOnTank, double fuelFlowPerEngine, double activeTankQty, double otherTankQty)
    {
        var snap = new TelemetrySnapshot();
        // Both engines on same tank
        snap.Set(SimVarId.FuelTankSelector, bothOnTank, index: 1);
        snap.Set(SimVarId.FuelTankSelector, bothOnTank, index: 2);
        snap.Set(SimVarId.GeneralEngFuelFlow, fuelFlowPerEngine, index: 1);
        snap.Set(SimVarId.GeneralEngFuelFlow, fuelFlowPerEngine, index: 2);
        snap.Set(SimVarId.GeneralEngCombustion, 1.0, index: 1);
        snap.Set(SimVarId.GeneralEngCombustion, 1.0, index: 2);
        // Tank quantities
        snap.Set(SimVarId.FuelSystemTankQuantity, activeTankQty, index: bothOnTank);
        int otherTank = bothOnTank == 0 ? 1 : 0;
        snap.Set(SimVarId.FuelSystemTankQuantity, otherTankQty, index: otherTank);
        snap.Set(SimVarId.FuelTotalQuantity, activeTankQty + otherTankQty);

        return snap;
    }
}

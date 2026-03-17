using Guardian.Core;
using Guardian.Detection.Rules;
using Xunit;

namespace Guardian.Detection.Tests;

public class R002_AsymmetricPowerTrimTests
{
    private static AircraftProfile TwinProfile => new()
    {
        AircraftId = "be58",
        EngineCount = 2,
        Trim = new TrimProfile { AsymmetricThresholdPct = 15 },
    };

    private static AircraftProfile SingleProfile => new()
    {
        AircraftId = "c172sp",
        EngineCount = 1,
    };

    [Fact]
    public void IsApplicable_SingleEngine_False()
    {
        var rule = new R002_AsymmetricPowerTrim();
        Assert.False(rule.IsApplicable(SingleProfile, FlightPhase.Cruise));
    }

    [Fact]
    public void IsApplicable_TwinOnGround_False()
    {
        var rule = new R002_AsymmetricPowerTrim();
        Assert.False(rule.IsApplicable(TwinProfile, FlightPhase.Ground));
    }

    [Fact]
    public void IsApplicable_TwinInCruise_True()
    {
        var rule = new R002_AsymmetricPowerTrim();
        Assert.True(rule.IsApplicable(TwinProfile, FlightPhase.Cruise));
    }

    [Fact]
    public void SymmetricPower_NeutralTrim_NoAlert()
    {
        var rule = new R002_AsymmetricPowerTrim();
        var snap = MakeSnapshot(throttle1: 75, throttle2: 75, rpm1: 2400, rpm2: 2400,
            rudderTrim: 0, aileronTrim: 0);

        var alert = rule.Evaluate(snap, new MockTelemetryBuffer(), TwinProfile, FlightPhase.Cruise);
        Assert.Null(alert);
    }

    [Fact]
    public void AsymmetricPower_NeutralTrim_Advisory()
    {
        var rule = new R002_AsymmetricPowerTrim();
        var snap = MakeSnapshot(throttle1: 80, throttle2: 60, rpm1: 2500, rpm2: 2200,
            rudderTrim: 0, aileronTrim: 0);

        var alert = rule.Evaluate(snap, new MockTelemetryBuffer(), TwinProfile, FlightPhase.Cruise);

        Assert.NotNull(alert);
        Assert.Equal("R002", alert!.RuleId);
        Assert.Equal(AlertSeverity.Advisory, alert.Severity);
        Assert.Contains("NO_TRIM", alert.TextKey);
    }

    [Fact]
    public void SymmetricPower_SignificantTrim_Advisory()
    {
        var rule = new R002_AsymmetricPowerTrim();
        var snap = MakeSnapshot(throttle1: 75, throttle2: 75, rpm1: 2400, rpm2: 2400,
            rudderTrim: 20, aileronTrim: 0); // 20% > 15% threshold

        var alert = rule.Evaluate(snap, new MockTelemetryBuffer(), TwinProfile, FlightPhase.Cruise);

        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.Advisory, alert!.Severity);
        Assert.Contains("TRIM_OFFSET", alert.TextKey);
    }

    [Fact]
    public void AsymmetricPower_PersistsOver60s_EscalatesToWarning()
    {
        var rule = new R002_AsymmetricPowerTrim();
        var now = DateTime.UtcNow;

        // First evaluation — advisory
        var snap1 = MakeSnapshot(throttle1: 80, throttle2: 60, rpm1: 2500, rpm2: 2200,
            rudderTrim: 0, aileronTrim: 0, time: now);
        var alert1 = rule.Evaluate(snap1, new MockTelemetryBuffer(), TwinProfile, FlightPhase.Cruise);
        Assert.Equal(AlertSeverity.Advisory, alert1!.Severity);

        // 61 seconds later — should escalate to warning
        var snap2 = MakeSnapshot(throttle1: 80, throttle2: 60, rpm1: 2500, rpm2: 2200,
            rudderTrim: 0, aileronTrim: 0, time: now.AddSeconds(61));
        var alert2 = rule.Evaluate(snap2, new MockTelemetryBuffer(), TwinProfile, FlightPhase.Cruise);
        Assert.Equal(AlertSeverity.Warning, alert2!.Severity);
    }

    [Fact]
    public void OneEngineOff_NoAlert()
    {
        var rule = new R002_AsymmetricPowerTrim();
        var snap = new TelemetrySnapshot();
        snap.Set(SimVarId.ThrottleLeverPosition, 80, 1);
        snap.Set(SimVarId.ThrottleLeverPosition, 0, 2);
        snap.Set(SimVarId.GeneralEngRpm, 2400, 1);
        snap.Set(SimVarId.GeneralEngRpm, 0, 2);
        snap.Set(SimVarId.GeneralEngCombustion, 1.0, 1);
        snap.Set(SimVarId.GeneralEngCombustion, 0.0, 2); // engine off
        snap.Set(SimVarId.RudderTrimPct, 0);
        snap.Set(SimVarId.AileronTrimPct, 0);

        var alert = rule.Evaluate(snap, new MockTelemetryBuffer(), TwinProfile, FlightPhase.Cruise);
        Assert.Null(alert);
    }

    [Fact]
    public void MismatchClears_TimerResets()
    {
        var rule = new R002_AsymmetricPowerTrim();
        var now = DateTime.UtcNow;

        // First: mismatch
        var snap1 = MakeSnapshot(throttle1: 80, throttle2: 60, rpm1: 2500, rpm2: 2200,
            rudderTrim: 0, aileronTrim: 0, time: now);
        rule.Evaluate(snap1, new MockTelemetryBuffer(), TwinProfile, FlightPhase.Cruise);

        // Clear: symmetric power
        var snap2 = MakeSnapshot(throttle1: 75, throttle2: 75, rpm1: 2400, rpm2: 2400,
            rudderTrim: 0, aileronTrim: 0, time: now.AddSeconds(30));
        var alertClear = rule.Evaluate(snap2, new MockTelemetryBuffer(), TwinProfile, FlightPhase.Cruise);
        Assert.Null(alertClear);

        // Mismatch again — should start fresh as advisory, not warning
        var snap3 = MakeSnapshot(throttle1: 80, throttle2: 60, rpm1: 2500, rpm2: 2200,
            rudderTrim: 0, aileronTrim: 0, time: now.AddSeconds(92)); // >60s from original start
        var alertNew = rule.Evaluate(snap3, new MockTelemetryBuffer(), TwinProfile, FlightPhase.Cruise);
        Assert.Equal(AlertSeverity.Advisory, alertNew!.Severity);
    }

    private static TelemetrySnapshot MakeSnapshot(
        double throttle1, double throttle2,
        double rpm1, double rpm2,
        double rudderTrim, double aileronTrim,
        DateTime? time = null)
    {
        var snap = new TelemetrySnapshot { Timestamp = time ?? DateTime.UtcNow };
        snap.Set(SimVarId.ThrottleLeverPosition, throttle1, 1);
        snap.Set(SimVarId.ThrottleLeverPosition, throttle2, 2);
        snap.Set(SimVarId.GeneralEngRpm, rpm1, 1);
        snap.Set(SimVarId.GeneralEngRpm, rpm2, 2);
        snap.Set(SimVarId.GeneralEngCombustion, 1.0, 1);
        snap.Set(SimVarId.GeneralEngCombustion, 1.0, 2);
        snap.Set(SimVarId.RudderTrimPct, rudderTrim);
        snap.Set(SimVarId.AileronTrimPct, aileronTrim);
        return snap;
    }
}

using Guardian.Core;
using Guardian.Detection.Rules;
using Xunit;

namespace Guardian.Detection.Tests;

public class R007_ElectricalDegradationTests
{
    private readonly R007_ElectricalDegradation _rule = new();

    private static AircraftProfile MakeProfile() => new()
    {
        AircraftId = "c172sp",
        Electrical = new ElectricalProfile
        {
            MainBusNormalV = [13.5, 14.5],
            MainBusMinimumV = 12.0,
            BatteryBusMinimumV = 11.0,
        },
    };

    [Fact]
    public void NormalVoltage_NoAlert()
    {
        var snap = MakeElecSnapshot(mainBusV: 14.0, battBusV: 12.5);
        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), MakeProfile(), FlightPhase.Cruise);
        Assert.Null(alert);
    }

    [Fact]
    public void MainBusBelowMinimum_Critical()
    {
        var snap = MakeElecSnapshot(mainBusV: 11.5);

        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), MakeProfile(), FlightPhase.Cruise);

        Assert.NotNull(alert);
        Assert.Equal("R007", alert!.RuleId);
        Assert.Equal(AlertSeverity.Critical, alert.Severity);
        Assert.Contains("MAIN_BUS_CRITICAL", alert.TextKey);
    }

    [Fact]
    public void MainBusTrendDecreasing_Warning()
    {
        var snap = MakeElecSnapshot(mainBusV: 13.8);

        var buffer = new MockTelemetryBuffer();
        buffer.SetRate(SimVarId.ElectricalMainBusVoltage, -0.002); // sustained drop

        var alert = _rule.Evaluate(snap, buffer, MakeProfile(), FlightPhase.Cruise);

        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.Warning, alert!.Severity);
        Assert.Contains("TREND", alert.TextKey);
    }

    [Fact]
    public void MainBusBelowNormal_Advisory()
    {
        var snap = MakeElecSnapshot(mainBusV: 13.0); // below 13.5 normal

        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), MakeProfile(), FlightPhase.Cruise);

        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.Advisory, alert!.Severity);
        Assert.Contains("LOW_ADVISORY", alert.TextKey);
    }

    [Fact]
    public void BatteryBusBelowMinimum_Critical()
    {
        var snap = MakeElecSnapshot(mainBusV: 14.0, battBusV: 10.5);

        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), MakeProfile(), FlightPhase.Cruise);

        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.Critical, alert!.Severity);
        Assert.Contains("BATTERY_BUS_CRITICAL", alert.TextKey);
    }

    [Fact]
    public void MainBus_StableInNormalRange_NoAlert()
    {
        var snap = MakeElecSnapshot(mainBusV: 14.2);

        var buffer = new MockTelemetryBuffer();
        buffer.SetRate(SimVarId.ElectricalMainBusVoltage, 0.0001); // essentially stable

        var alert = _rule.Evaluate(snap, buffer, MakeProfile(), FlightPhase.Cruise);
        Assert.Null(alert);
    }

    [Fact]
    public void NoVoltageData_NoAlert()
    {
        var snap = new TelemetrySnapshot(); // no electrical data
        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), MakeProfile(), FlightPhase.Cruise);
        Assert.Null(alert);
    }

    private static TelemetrySnapshot MakeElecSnapshot(double? mainBusV = null, double? battBusV = null)
    {
        var snap = new TelemetrySnapshot();
        if (mainBusV is not null) snap.Set(SimVarId.ElectricalMainBusVoltage, mainBusV.Value);
        if (battBusV is not null) snap.Set(SimVarId.ElectricalBatteryBusVoltage, battBusV.Value);
        return snap;
    }
}

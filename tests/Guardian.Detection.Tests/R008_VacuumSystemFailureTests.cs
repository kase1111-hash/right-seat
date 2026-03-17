using Guardian.Core;
using Guardian.Detection.Rules;
using Xunit;

namespace Guardian.Detection.Tests;

public class R008_VacuumSystemFailureTests
{
    private readonly R008_VacuumSystemFailure _rule = new();

    private static AircraftProfile MakeProfile() => new()
    {
        AircraftId = "c172sp",
        Vacuum = new VacuumProfile
        {
            SuctionNormalInhg = [4.5, 5.5],
            SuctionMinimumInhg = 3.5,
        },
    };

    [Fact]
    public void IsApplicable_OnGround_False()
    {
        Assert.False(_rule.IsApplicable(MakeProfile(), FlightPhase.Ground));
    }

    [Fact]
    public void IsApplicable_InCruise_True()
    {
        Assert.True(_rule.IsApplicable(MakeProfile(), FlightPhase.Cruise));
    }

    [Fact]
    public void NormalSuction_NoAlert()
    {
        var snap = MakeVacSnapshot(suction: 5.0, inCloud: false, precip: false);
        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), MakeProfile(), FlightPhase.Cruise);
        Assert.Null(alert);
    }

    [Fact]
    public void BelowNormal_Advisory()
    {
        var snap = MakeVacSnapshot(suction: 4.0, inCloud: false, precip: false); // below 4.5

        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), MakeProfile(), FlightPhase.Cruise);

        Assert.NotNull(alert);
        Assert.Equal("R008", alert!.RuleId);
        Assert.Equal(AlertSeverity.Advisory, alert.Severity);
        Assert.Contains("BELOW_NORMAL", alert.TextKey);
    }

    [Fact]
    public void BelowMinimum_IMC_Warning()
    {
        // In cloud → IMC → Warning severity
        var snap = MakeVacSnapshot(suction: 3.0, inCloud: true, precip: false);

        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), MakeProfile(), FlightPhase.Cruise);

        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.Warning, alert!.Severity);
        Assert.Contains("VACUUM_LOW", alert.TextKey);
        Assert.Equal("no", alert.TextParameters["vmc"]);
    }

    [Fact]
    public void BelowMinimum_VMC_DowngradedToAdvisory()
    {
        // Clear sky → VMC → downgraded to Advisory
        var snap = MakeVacSnapshot(suction: 3.0, inCloud: false, precip: false);

        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), MakeProfile(), FlightPhase.Cruise);

        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.Advisory, alert!.Severity);
        Assert.Contains("VACUUM_LOW", alert.TextKey);
        Assert.Equal("yes", alert.TextParameters["vmc"]);
    }

    [Fact]
    public void NoSuctionData_NoAlert()
    {
        var snap = new TelemetrySnapshot(); // no vacuum data
        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), MakeProfile(), FlightPhase.Cruise);
        Assert.Null(alert);
    }

    [Fact]
    public void BelowMinimum_InPrecip_Warning()
    {
        // Precipitation → not VMC → Warning
        var snap = MakeVacSnapshot(suction: 3.0, inCloud: false, precip: true);

        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), MakeProfile(), FlightPhase.Cruise);

        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.Warning, alert!.Severity);
    }

    private static TelemetrySnapshot MakeVacSnapshot(double suction, bool inCloud, bool precip)
    {
        var snap = new TelemetrySnapshot();
        snap.Set(SimVarId.SuctionPressure, suction);
        snap.Set(SimVarId.AmbientInCloud, inCloud ? 1.0 : 0.0);
        snap.Set(SimVarId.AmbientPrecipState, precip ? 1.0 : 0.0);
        return snap;
    }
}

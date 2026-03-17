using Guardian.Core;
using Guardian.Detection.Rules;
using Xunit;

namespace Guardian.Detection.Tests;

public class R006_IcingConditionsTests
{
    private readonly R006_IcingConditions _rule = new();

    private static AircraftProfile MakeProfile() => new()
    {
        AircraftId = "c172sp",
        Icing = new IcingProfile
        {
            HasAntiIce = false,
            HasPitotHeat = true,
            IcingOatRangeC = [-20, 2],
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
    public void WarmDay_NoIce_NoAlert()
    {
        var snap = MakeWeatherSnapshot(oatC: 15, inCloud: false, precip: false, structIce: 0, pitotIce: 0);
        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), MakeProfile(), FlightPhase.Cruise);
        Assert.Null(alert);
    }

    [Fact]
    public void IcingTemp_InCloud_PitotIceAccumulating_Advisory()
    {
        var snap = MakeWeatherSnapshot(oatC: -5, inCloud: true, precip: false, structIce: 0, pitotIce: 2);

        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), MakeProfile(), FlightPhase.Cruise);

        Assert.NotNull(alert);
        Assert.Equal("R006", alert!.RuleId);
        Assert.Equal(AlertSeverity.Advisory, alert.Severity);
        Assert.Contains("PITOT_HEAT", alert.TextKey);
    }

    [Fact]
    public void StructuralIce_5Pct_Warning()
    {
        var snap = MakeWeatherSnapshot(oatC: -5, inCloud: true, precip: false, structIce: 6, pitotIce: 0);

        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), MakeProfile(), FlightPhase.Cruise);

        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.Warning, alert!.Severity);
        Assert.Contains("STRUCTURAL_ICE_WARNING", alert.TextKey);
    }

    [Fact]
    public void StructuralIce_25Pct_Critical()
    {
        var snap = MakeWeatherSnapshot(oatC: -10, inCloud: true, precip: true, structIce: 30, pitotIce: 10);

        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), MakeProfile(), FlightPhase.Cruise);

        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.Critical, alert!.Severity);
        Assert.Contains("STRUCTURAL_ICE_CRITICAL", alert.TextKey);
    }

    [Fact]
    public void PitotIce_25Pct_Critical()
    {
        var snap = MakeWeatherSnapshot(oatC: -5, inCloud: false, precip: true, structIce: 0, pitotIce: 30);

        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), MakeProfile(), FlightPhase.Cruise);

        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.Critical, alert!.Severity);
        Assert.Contains("PITOT_ICE_CRITICAL", alert.TextKey);
    }

    [Fact]
    public void IcingTemp_NoPrecip_NoCloud_NoAlert()
    {
        // Cold but no moisture — no icing
        var snap = MakeWeatherSnapshot(oatC: -5, inCloud: false, precip: false, structIce: 0, pitotIce: 0);
        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), MakeProfile(), FlightPhase.Cruise);
        Assert.Null(alert);
    }

    [Fact]
    public void OatOutsideIcingRange_NoPitotIce_NoAlert()
    {
        // -25°C is below the icing range [-20, 2]
        var snap = MakeWeatherSnapshot(oatC: -25, inCloud: true, precip: true, structIce: 0, pitotIce: 0);
        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), MakeProfile(), FlightPhase.Cruise);
        Assert.Null(alert);
    }

    private static TelemetrySnapshot MakeWeatherSnapshot(
        double oatC, bool inCloud, bool precip, double structIce, double pitotIce)
    {
        var snap = new TelemetrySnapshot();
        snap.Set(SimVarId.AmbientTemperature, oatC);
        snap.Set(SimVarId.AmbientInCloud, inCloud ? 1.0 : 0.0);
        snap.Set(SimVarId.AmbientPrecipState, precip ? 1.0 : 0.0);
        snap.Set(SimVarId.StructuralIcePct, structIce);
        snap.Set(SimVarId.PitotIcePct, pitotIce);
        return snap;
    }
}

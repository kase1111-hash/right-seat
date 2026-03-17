using Guardian.Core;
using Guardian.Detection.Rules;
using Xunit;

namespace Guardian.Detection.Tests;

public class R004_OilPressureAnomalyTests
{
    private readonly R004_OilPressureAnomaly _rule = new();

    private static AircraftProfile MakeProfile()
    {
        var profile = new AircraftProfile
        {
            AircraftId = "c172sp",
            EngineCount = 1,
            Engine = new EngineProfile
            {
                OilPressureNormalPsi = [60, 90],
                OilPressureMinimumPsi = 25,
                OilPressureRedlinePsi = 115,
                OilPressureDropRateWarningPsiPerMin = 10,
                OilTempNormalRangeF = [100, 245],
                OilTempRedlineF = 250,
            },
        };
        Guardian.Common.ProfileLoader.ConvertUnits(profile);
        return profile;
    }

    [Fact]
    public void NormalOilPressure_NoAlert()
    {
        var snap = MakeEngineSnapshot(oilPressure: 70);
        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), MakeProfile(), FlightPhase.Cruise);
        Assert.Null(alert);
    }

    [Fact]
    public void BelowMinimum_Critical()
    {
        var snap = MakeEngineSnapshot(oilPressure: 20);
        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), MakeProfile(), FlightPhase.Cruise);

        Assert.NotNull(alert);
        Assert.Equal("R004", alert!.RuleId);
        Assert.Equal(AlertSeverity.Critical, alert.Severity);
        Assert.Contains("CRITICAL", alert.TextKey);
    }

    [Fact]
    public void RapidDropRate_Warning()
    {
        var profile = MakeProfile();
        var snap = MakeEngineSnapshot(oilPressure: 50);

        var buffer = new MockTelemetryBuffer();
        // Drop rate: -0.2 psi/sec = -12 psi/min (above 10 psi/min threshold)
        buffer.SetRate(SimVarId.GeneralEngOilPressure, -0.20, index: 1);

        var alert = _rule.Evaluate(snap, buffer, profile, FlightPhase.Cruise);

        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.Warning, alert!.Severity);
        Assert.Contains("DROP_RATE", alert.TextKey);
    }

    [Fact]
    public void TempRising_PressureFalling_Divergence_Warning()
    {
        var profile = MakeProfile();
        var oilTempRankine = UnitsConverter.FahrenheitToRankine(220);
        var snap = MakeEngineSnapshot(oilPressure: 50, oilTempRankine: oilTempRankine);

        var buffer = new MockTelemetryBuffer();
        // Pressure falling: -0.05 psi/sec = -3 psi/min
        buffer.SetRate(SimVarId.GeneralEngOilPressure, -0.05, index: 1);
        // Temp rising: +0.03 R/sec ≈ 1.8 °R/min
        buffer.SetRate(SimVarId.GeneralEngOilTemperature, 0.03, index: 1);

        var alert = _rule.Evaluate(snap, buffer, profile, FlightPhase.Cruise);

        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.Warning, alert!.Severity);
        Assert.Contains("DIVERGENCE", alert.TextKey);
    }

    [Fact]
    public void IdleOnGround_LowPressure_Suppressed()
    {
        var snap = new TelemetrySnapshot();
        snap.Set(SimVarId.GeneralEngOilPressure, 20, 1); // below minimum
        snap.Set(SimVarId.GeneralEngRpm, 600, 1);         // idle RPM
        snap.Set(SimVarId.GeneralEngCombustion, 1.0, 1);

        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), MakeProfile(), FlightPhase.Ground);
        Assert.Null(alert);
    }

    [Fact]
    public void EngineNotRunning_NoAlert()
    {
        var snap = new TelemetrySnapshot();
        snap.Set(SimVarId.GeneralEngOilPressure, 0, 1);
        snap.Set(SimVarId.GeneralEngCombustion, 0.0, 1);

        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), MakeProfile(), FlightPhase.Cruise);
        Assert.Null(alert);
    }

    [Fact]
    public void ModerateDropRate_NoAlert()
    {
        var profile = MakeProfile();
        var snap = MakeEngineSnapshot(oilPressure: 60);

        var buffer = new MockTelemetryBuffer();
        // Slow drop: -0.05 psi/sec = -3 psi/min (below 10 psi/min threshold)
        buffer.SetRate(SimVarId.GeneralEngOilPressure, -0.05, index: 1);

        var alert = _rule.Evaluate(snap, buffer, profile, FlightPhase.Cruise);
        Assert.Null(alert);
    }

    private static TelemetrySnapshot MakeEngineSnapshot(double oilPressure, double? oilTempRankine = null)
    {
        var snap = new TelemetrySnapshot();
        snap.Set(SimVarId.GeneralEngOilPressure, oilPressure, 1);
        snap.Set(SimVarId.GeneralEngRpm, 2400, 1);
        snap.Set(SimVarId.GeneralEngCombustion, 1.0, 1);
        if (oilTempRankine is not null)
            snap.Set(SimVarId.GeneralEngOilTemperature, oilTempRankine.Value, 1);
        return snap;
    }
}

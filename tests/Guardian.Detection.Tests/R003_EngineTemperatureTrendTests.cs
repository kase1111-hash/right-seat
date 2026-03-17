using Guardian.Core;
using Guardian.Detection.Rules;
using Xunit;

namespace Guardian.Detection.Tests;

public class R003_EngineTemperatureTrendTests
{
    private readonly R003_EngineTemperatureTrend _rule = new();

    private static AircraftProfile MakeProfile()
    {
        var profile = new AircraftProfile
        {
            AircraftId = "c172sp",
            EngineCount = 1,
            Engine = new EngineProfile
            {
                ChtRedlineF = 500,
                ChtNormalRangeF = new[] { 200.0, 450.0 },
                ChtTrendAdvisoryFPerMin = 5,
                ChtTrendWarningFPerMin = 10,
                EgtRedlineF = 1600,
                EgtNormalRangeF = new[] { 1100.0, 1500.0 },
            }
        };

        // Simulate profile loader unit conversion
        Guardian.Common.ProfileLoader.ConvertUnits(profile);
        return profile;
    }

    private static TelemetrySnapshot MakeSnapshot(double chtRankine, double egtRankine, int engine = 1)
    {
        var snap = new TelemetrySnapshot();
        snap.Set(SimVarId.EngCylinderHeadTemperature, chtRankine, engine);
        snap.Set(SimVarId.EngExhaustGasTemperature, egtRankine, engine);
        snap.Set(SimVarId.GeneralEngCombustion, 1.0, engine);
        return snap;
    }

    [Fact]
    public void IsApplicable_OnGround_ReturnsFalse()
    {
        Assert.False(_rule.IsApplicable(MakeProfile(), FlightPhase.Ground));
    }

    [Fact]
    public void IsApplicable_InCruise_ReturnsTrue()
    {
        Assert.True(_rule.IsApplicable(MakeProfile(), FlightPhase.Cruise));
    }

    // ── Absolute CHT threshold tests ──

    [Fact]
    public void ChtNormal_NoAlert()
    {
        var profile = MakeProfile();
        // 350°F = normal, well below 90% of 500°F redline
        var chtRankine = UnitsConverter.FahrenheitToRankine(350);
        var egtRankine = UnitsConverter.FahrenheitToRankine(1200);
        var snap = MakeSnapshot(chtRankine, egtRankine);

        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), profile, FlightPhase.Cruise);
        Assert.Null(alert);
    }

    [Fact]
    public void Cht90PctRedline_Warning()
    {
        var profile = MakeProfile();
        // 90% of 500°F = 450°F → WARNING
        var chtRankine = UnitsConverter.FahrenheitToRankine(451);
        var egtRankine = UnitsConverter.FahrenheitToRankine(1200);
        var snap = MakeSnapshot(chtRankine, egtRankine);

        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), profile, FlightPhase.Cruise);

        Assert.NotNull(alert);
        Assert.Equal("R003", alert!.RuleId);
        Assert.Equal(AlertSeverity.Warning, alert.Severity);
        Assert.Contains("CHT", alert.TextKey);
        Assert.Contains("REDLINE_WARNING", alert.TextKey);
    }

    [Fact]
    public void Cht95PctRedline_Critical()
    {
        var profile = MakeProfile();
        // 95% of 500°F = 475°F → CRITICAL
        var chtRankine = UnitsConverter.FahrenheitToRankine(476);
        var egtRankine = UnitsConverter.FahrenheitToRankine(1200);
        var snap = MakeSnapshot(chtRankine, egtRankine);

        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), profile, FlightPhase.Cruise);

        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.Critical, alert!.Severity);
        Assert.Contains("CRITICAL", alert.TextKey);
    }

    // ── Absolute EGT threshold tests ──

    [Fact]
    public void Egt90PctRedline_Warning()
    {
        var profile = MakeProfile();
        // 90% of 1600°F = 1440°F → WARNING
        var chtRankine = UnitsConverter.FahrenheitToRankine(300); // normal CHT
        var egtRankine = UnitsConverter.FahrenheitToRankine(1441);
        var snap = MakeSnapshot(chtRankine, egtRankine);

        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), profile, FlightPhase.Cruise);

        Assert.NotNull(alert);
        Assert.Contains("EGT", alert!.TextKey);
        Assert.Equal(AlertSeverity.Warning, alert.Severity);
    }

    [Fact]
    public void Egt95PctRedline_Critical()
    {
        var profile = MakeProfile();
        // 95% of 1600°F = 1520°F → CRITICAL
        var chtRankine = UnitsConverter.FahrenheitToRankine(300);
        var egtRankine = UnitsConverter.FahrenheitToRankine(1521);
        var snap = MakeSnapshot(chtRankine, egtRankine);

        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), profile, FlightPhase.Cruise);

        Assert.NotNull(alert);
        Assert.Contains("EGT", alert!.TextKey);
        Assert.Equal(AlertSeverity.Critical, alert.Severity);
    }

    // ── CHT rate-of-change (trend) tests ──

    [Fact]
    public void ChtTrend_AdvisoryRate_Advisory()
    {
        var profile = MakeProfile();
        // 5°F/min = 5/60 °F/sec. Since F and R have same scale factor, rate is same in R/sec.
        var advisoryRatePerSec = profile.Engine.ChtTrendAdvisoryRankinePerSec;

        var buffer = new MockTelemetryBuffer();
        buffer.SetRate(SimVarId.EngCylinderHeadTemperature, advisoryRatePerSec + 0.001, index: 1);

        var chtRankine = UnitsConverter.FahrenheitToRankine(350); // normal absolute
        var snap = MakeSnapshot(chtRankine, UnitsConverter.FahrenheitToRankine(1200));

        var alert = _rule.Evaluate(snap, buffer, profile, FlightPhase.Cruise);

        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.Advisory, alert!.Severity);
        Assert.Contains("TREND_ADVISORY", alert.TextKey);
    }

    [Fact]
    public void ChtTrend_WarningRate_Warning()
    {
        var profile = MakeProfile();
        var warningRatePerSec = profile.Engine.ChtTrendWarningRankinePerSec;

        var buffer = new MockTelemetryBuffer();
        buffer.SetRate(SimVarId.EngCylinderHeadTemperature, warningRatePerSec + 0.001, index: 1);

        var chtRankine = UnitsConverter.FahrenheitToRankine(350);
        var snap = MakeSnapshot(chtRankine, UnitsConverter.FahrenheitToRankine(1200));

        var alert = _rule.Evaluate(snap, buffer, profile, FlightPhase.Cruise);

        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.Warning, alert!.Severity);
        Assert.Contains("TREND_WARNING", alert.TextKey);
    }

    [Fact]
    public void ChtTrend_Decreasing_NoAlert()
    {
        var profile = MakeProfile();

        var buffer = new MockTelemetryBuffer();
        buffer.SetRate(SimVarId.EngCylinderHeadTemperature, -0.5, index: 1); // cooling

        var chtRankine = UnitsConverter.FahrenheitToRankine(350);
        var snap = MakeSnapshot(chtRankine, UnitsConverter.FahrenheitToRankine(1200));

        var alert = _rule.Evaluate(snap, buffer, profile, FlightPhase.Cruise);
        Assert.Null(alert);
    }

    // ── Climb phase relaxation ──

    [Fact]
    public void ChtTrend_DuringClimb_ThresholdsRelaxed()
    {
        var profile = MakeProfile();
        // Advisory rate in cruise = 5°F/min. During climb, threshold is 7.5°F/min.
        // So a rate of 6°F/min should trigger advisory in cruise but not in climb.
        var advisoryRatePerSec = profile.Engine.ChtTrendAdvisoryRankinePerSec;
        var climbSubThresholdRate = advisoryRatePerSec * 1.2; // above cruise threshold, below climb threshold (1.5x)

        var buffer = new MockTelemetryBuffer();
        buffer.SetRate(SimVarId.EngCylinderHeadTemperature, climbSubThresholdRate, index: 1);

        var chtRankine = UnitsConverter.FahrenheitToRankine(350);
        var snap = MakeSnapshot(chtRankine, UnitsConverter.FahrenheitToRankine(1200));

        // In cruise — should trigger
        var cruiseAlert = _rule.Evaluate(snap, buffer, profile, FlightPhase.Cruise);
        Assert.NotNull(cruiseAlert);

        // In climb — should NOT trigger (relaxed by 1.5x)
        var climbAlert = _rule.Evaluate(snap, buffer, profile, FlightPhase.Climb);
        Assert.Null(climbAlert);
    }

    // ── Engine not running ──

    [Fact]
    public void EngineNotRunning_NoAlert()
    {
        var profile = MakeProfile();
        var snap = new TelemetrySnapshot();
        snap.Set(SimVarId.EngCylinderHeadTemperature, UnitsConverter.FahrenheitToRankine(600), 1);
        snap.Set(SimVarId.EngExhaustGasTemperature, UnitsConverter.FahrenheitToRankine(2000), 1);
        snap.Set(SimVarId.GeneralEngCombustion, 0.0, 1); // Not running

        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), profile, FlightPhase.Cruise);
        Assert.Null(alert);
    }

    // ── Multi-engine: worst alert wins ──

    [Fact]
    public void MultiEngine_ReturnsWorstAlert()
    {
        var profile = new AircraftProfile
        {
            AircraftId = "be58",
            EngineCount = 2,
            Engine = new EngineProfile
            {
                ChtRedlineF = 460,
                ChtNormalRangeF = new[] { 200.0, 420.0 },
                ChtTrendAdvisoryFPerMin = 5,
                ChtTrendWarningFPerMin = 10,
                EgtRedlineF = 1650,
                EgtNormalRangeF = new[] { 1100.0, 1500.0 },
            }
        };
        Guardian.Common.ProfileLoader.ConvertUnits(profile);

        var snap = new TelemetrySnapshot();
        // Engine 1: 90% of redline → WARNING
        snap.Set(SimVarId.EngCylinderHeadTemperature, UnitsConverter.FahrenheitToRankine(415), 1);
        snap.Set(SimVarId.EngExhaustGasTemperature, UnitsConverter.FahrenheitToRankine(1200), 1);
        snap.Set(SimVarId.GeneralEngCombustion, 1.0, 1);
        // Engine 2: 95% of redline → CRITICAL
        snap.Set(SimVarId.EngCylinderHeadTemperature, UnitsConverter.FahrenheitToRankine(440), 2);
        snap.Set(SimVarId.EngExhaustGasTemperature, UnitsConverter.FahrenheitToRankine(1200), 2);
        snap.Set(SimVarId.GeneralEngCombustion, 1.0, 2);

        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), profile, FlightPhase.Cruise);

        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.Critical, alert!.Severity);
    }

    // ── Alert parameters ──

    [Fact]
    public void AlertParameters_IncludeTemperatureValues()
    {
        var profile = MakeProfile();
        var chtRankine = UnitsConverter.FahrenheitToRankine(476);
        var snap = MakeSnapshot(chtRankine, UnitsConverter.FahrenheitToRankine(1200));

        var alert = _rule.Evaluate(snap, new MockTelemetryBuffer(), profile, FlightPhase.Cruise);

        Assert.NotNull(alert);
        Assert.True(alert!.TextParameters.ContainsKey("current_f"));
        Assert.True(alert.TextParameters.ContainsKey("redline_f"));
        Assert.True(alert.TextParameters.ContainsKey("engine"));
    }
}

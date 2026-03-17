using Guardian.Core;
using Guardian.Detection;
using Xunit;

namespace Guardian.Detection.Tests;

/// <summary>
/// A mock rule that returns a configurable alert on evaluation.
/// </summary>
internal class MockRule : IDetectionRule
{
    public string RuleId { get; set; } = "TEST";
    public string Name { get; set; } = "Mock Rule";
    public string Description { get; set; } = "A mock rule for testing.";
    public TimeSpan EvaluationInterval { get; set; } = TimeSpan.Zero;

    public Alert? AlertToReturn { get; set; }
    public bool ThrowOnEvaluate { get; set; }
    public int EvaluateCallCount { get; private set; }

    public Func<AircraftProfile, FlightPhase, bool>? IsApplicableFunc { get; set; }

    public bool IsApplicable(AircraftProfile profile, FlightPhase phase) =>
        IsApplicableFunc?.Invoke(profile, phase) ?? true;

    public Alert? Evaluate(TelemetrySnapshot current, ITelemetryBuffer buffer, AircraftProfile profile, FlightPhase phase)
    {
        EvaluateCallCount++;
        if (ThrowOnEvaluate)
            throw new InvalidOperationException("Mock rule crash");
        return AlertToReturn;
    }
}

public class DetectionEngineTests
{
    private static AircraftProfile DefaultProfile => new() { AircraftId = "test", EngineCount = 1 };
    private static TelemetrySnapshot DefaultSnapshot => new();
    private static MockTelemetryBuffer DefaultBuffer => new();

    [Fact]
    public void Register_RuleIsEnabled_WhenInEnabledList()
    {
        var engine = new DetectionEngine(new[] { "R001" });
        var rule = new MockRule { RuleId = "R001" };

        engine.Register(rule);

        var states = engine.GetRuleStates();
        Assert.Single(states);
        Assert.Equal(RuleState.Enabled, states[0].State);
    }

    [Fact]
    public void Register_RuleIsDisabled_WhenNotInEnabledList()
    {
        var engine = new DetectionEngine(new[] { "R001" });
        var rule = new MockRule { RuleId = "R999" };

        engine.Register(rule);

        var states = engine.GetRuleStates();
        Assert.Equal(RuleState.DisabledByConfig, states[0].State);
    }

    [Fact]
    public void Evaluate_CallsEnabledRule()
    {
        var engine = new DetectionEngine();
        var rule = new MockRule { RuleId = "R001" };
        engine.Register(rule);

        engine.Evaluate(DefaultSnapshot, DefaultBuffer, DefaultProfile, FlightPhase.Cruise);

        Assert.Equal(1, rule.EvaluateCallCount);
    }

    [Fact]
    public void Evaluate_SkipsDisabledRule()
    {
        var engine = new DetectionEngine(new[] { "R001" });
        var rule = new MockRule { RuleId = "R999" };
        engine.Register(rule);

        engine.Evaluate(DefaultSnapshot, DefaultBuffer, DefaultProfile, FlightPhase.Cruise);

        Assert.Equal(0, rule.EvaluateCallCount);
    }

    [Fact]
    public void Evaluate_SkipsNotApplicableRule()
    {
        var engine = new DetectionEngine();
        var rule = new MockRule
        {
            RuleId = "R001",
            IsApplicableFunc = (_, _) => false,
        };
        engine.Register(rule);

        engine.Evaluate(DefaultSnapshot, DefaultBuffer, DefaultProfile, FlightPhase.Cruise);

        Assert.Equal(0, rule.EvaluateCallCount);
    }

    [Fact]
    public void Evaluate_EmitsAlert_WhenRuleReturnsOne()
    {
        var engine = new DetectionEngine();
        var expectedAlert = new Alert
        {
            RuleId = "R001",
            Severity = AlertSeverity.Warning,
            TextKey = "TEST_WARNING",
        };
        var rule = new MockRule { RuleId = "R001", AlertToReturn = expectedAlert };
        engine.Register(rule);

        Alert? received = null;
        engine.OnAlert += a => received = a;

        engine.Evaluate(DefaultSnapshot, DefaultBuffer, DefaultProfile, FlightPhase.Cruise);

        Assert.NotNull(received);
        Assert.Equal("R001", received!.RuleId);
    }

    [Fact]
    public void Evaluate_NoAlert_WhenRuleReturnsNull()
    {
        var engine = new DetectionEngine();
        var rule = new MockRule { RuleId = "R001", AlertToReturn = null };
        engine.Register(rule);

        Alert? received = null;
        engine.OnAlert += a => received = a;

        engine.Evaluate(DefaultSnapshot, DefaultBuffer, DefaultProfile, FlightPhase.Cruise);

        Assert.Null(received);
    }

    [Fact]
    public void Evaluate_DisablesRule_After3ConsecutiveErrors()
    {
        var engine = new DetectionEngine();
        var rule = new MockRule { RuleId = "R001", ThrowOnEvaluate = true };
        engine.Register(rule);

        RuleState? lastState = null;
        engine.OnRuleStateChanged += (_, state) => lastState = state;

        // Three evaluations, each throws
        for (int i = 0; i < 3; i++)
            engine.Evaluate(DefaultSnapshot, DefaultBuffer, DefaultProfile, FlightPhase.Cruise);

        Assert.Equal(RuleState.DisabledCrashed, lastState);

        // Fourth evaluation should not call rule
        rule.EvaluateCallCount = 0;
        engine.Evaluate(DefaultSnapshot, DefaultBuffer, DefaultProfile, FlightPhase.Cruise);
        Assert.Equal(0, rule.EvaluateCallCount);
    }

    [Fact]
    public void Evaluate_RespectsEvaluationInterval()
    {
        var engine = new DetectionEngine();
        var rule = new MockRule
        {
            RuleId = "R001",
            EvaluationInterval = TimeSpan.FromSeconds(10),
        };
        engine.Register(rule);

        var now = DateTime.UtcNow;
        var snap1 = new TelemetrySnapshot { Timestamp = now };
        var snap2 = new TelemetrySnapshot { Timestamp = now.AddSeconds(5) };
        var snap3 = new TelemetrySnapshot { Timestamp = now.AddSeconds(11) };

        engine.Evaluate(snap1, DefaultBuffer, DefaultProfile, FlightPhase.Cruise);
        Assert.Equal(1, rule.EvaluateCallCount);

        engine.Evaluate(snap2, DefaultBuffer, DefaultProfile, FlightPhase.Cruise);
        Assert.Equal(1, rule.EvaluateCallCount); // Too soon

        engine.Evaluate(snap3, DefaultBuffer, DefaultProfile, FlightPhase.Cruise);
        Assert.Equal(2, rule.EvaluateCallCount); // Enough time passed
    }

    [Fact]
    public void DisableRule_PreventsEvaluation()
    {
        var engine = new DetectionEngine();
        var rule = new MockRule { RuleId = "R001" };
        engine.Register(rule);

        engine.DisableRule("R001", RuleState.DisabledMissingSimVars);
        engine.Evaluate(DefaultSnapshot, DefaultBuffer, DefaultProfile, FlightPhase.Cruise);

        Assert.Equal(0, rule.EvaluateCallCount);
    }

    [Fact]
    public void EnableRule_AllowsEvaluationAgain()
    {
        var engine = new DetectionEngine();
        var rule = new MockRule { RuleId = "R001" };
        engine.Register(rule);

        engine.DisableRule("R001", RuleState.DisabledMissingSimVars);
        engine.EnableRule("R001");
        engine.Evaluate(DefaultSnapshot, DefaultBuffer, DefaultProfile, FlightPhase.Cruise);

        Assert.Equal(1, rule.EvaluateCallCount);
    }
}

/// <summary>
/// Minimal mock telemetry buffer for tests that don't need buffer data.
/// </summary>
internal class MockTelemetryBuffer : ITelemetryBuffer
{
    private readonly Dictionary<(SimVarId, int), double> _values = new();
    private readonly Dictionary<(SimVarId, int), double> _rates = new();

    public void SetValue(SimVarId id, double value, int index = 0) =>
        _values[(id, index)] = value;

    public void SetRate(SimVarId id, double ratePerSec, int index = 0) =>
        _rates[(id, index)] = ratePerSec;

    public double? Latest(SimVarId id, int index = 0) =>
        _values.TryGetValue((id, index), out var v) ? v : null;

    public IReadOnlyList<SimVarValue> Window(SimVarId id, TimeSpan duration, int index = 0) =>
        Array.Empty<SimVarValue>();

    public double? RateOfChange(SimVarId id, TimeSpan window, int index = 0) =>
        _rates.TryGetValue((id, index), out var r) ? r : null;

    public double? Delta(SimVarId id, DateTime referenceTime, int index = 0) => null;

    public TelemetrySnapshot? LatestSnapshot => null;
}

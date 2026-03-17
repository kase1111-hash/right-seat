using Guardian.Core;
using Serilog;

namespace Guardian.Detection;

/// <summary>
/// Tracks the state of a registered detection rule within the engine.
/// </summary>
public enum RuleState
{
    Enabled,
    DisabledMissingSimVars,
    DisabledCrashed,
    DisabledByConfig,
}

/// <summary>
/// Internal tracking record for a rule registered with the detection engine.
/// </summary>
internal sealed class RuleRegistration
{
    public required IDetectionRule Rule { get; init; }
    public RuleState State { get; set; } = RuleState.Enabled;
    public DateTime LastEvaluated { get; set; } = DateTime.MinValue;
    public DateTime LastTriggered { get; set; } = DateTime.MinValue;
    public int ConsecutiveErrors { get; set; }
    public const int MaxConsecutiveErrors = 3;
}

/// <summary>
/// Manages registered detection rules, evaluates them on their configured intervals,
/// and emits alerts. Each rule evaluation is wrapped in error handling — a crashing
/// rule is disabled for the session rather than bringing down the whole system.
/// </summary>
public sealed class DetectionEngine
{
    private static readonly ILogger Log = Serilog.Log.ForContext<DetectionEngine>();

    private readonly List<RuleRegistration> _rules = new();
    private readonly HashSet<string> _enabledRuleIds;

    /// <summary>Fired when a rule emits an alert.</summary>
    public event Action<Alert>? OnAlert;

    /// <summary>Fired when a rule's state changes.</summary>
    public event Action<string, RuleState>? OnRuleStateChanged;

    public DetectionEngine(IEnumerable<string>? enabledRuleIds = null)
    {
        _enabledRuleIds = enabledRuleIds is not null
            ? new HashSet<string>(enabledRuleIds, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Registers a detection rule. If the rule's ID is not in the enabled set, it is disabled by config.
    /// </summary>
    public void Register(IDetectionRule rule)
    {
        var state = _enabledRuleIds.Count == 0 || _enabledRuleIds.Contains(rule.RuleId)
            ? RuleState.Enabled
            : RuleState.DisabledByConfig;

        var registration = new RuleRegistration { Rule = rule, State = state };
        _rules.Add(registration);

        Log.Information("Rule registered: {RuleId} ({Name}) — {State}", rule.RuleId, rule.Name, state);
    }

    /// <summary>
    /// Evaluates all enabled rules against the current telemetry state.
    /// Called from the main evaluation loop (typically 1 Hz).
    /// </summary>
    public void Evaluate(
        TelemetrySnapshot current,
        ITelemetryBuffer buffer,
        AircraftProfile profile,
        FlightPhase phase)
    {
        var now = current.Timestamp;

        foreach (var reg in _rules)
        {
            if (reg.State != RuleState.Enabled)
                continue;

            // Check evaluation interval
            if ((now - reg.LastEvaluated) < reg.Rule.EvaluationInterval)
                continue;

            reg.LastEvaluated = now;

            // Check applicability
            if (!reg.Rule.IsApplicable(profile, phase))
            {
                Log.Verbose("Rule {RuleId} not applicable (phase={Phase}, aircraft={Aircraft})",
                    reg.Rule.RuleId, phase, profile.AircraftId);
                continue;
            }

            try
            {
                var alert = reg.Rule.Evaluate(current, buffer, profile, phase);
                reg.ConsecutiveErrors = 0;

                if (alert is not null)
                {
                    reg.LastTriggered = now;
                    Log.Information("Rule {RuleId} triggered: {Alert}", reg.Rule.RuleId, alert);
                    OnAlert?.Invoke(alert);
                }
            }
            catch (Exception ex)
            {
                reg.ConsecutiveErrors++;
                Log.Error(ex, "Rule {RuleId} threw exception (error {Count}/{Max})",
                    reg.Rule.RuleId, reg.ConsecutiveErrors, RuleRegistration.MaxConsecutiveErrors);

                if (reg.ConsecutiveErrors >= RuleRegistration.MaxConsecutiveErrors)
                {
                    reg.State = RuleState.DisabledCrashed;
                    Log.Error("Rule {RuleId} disabled after {Max} consecutive errors",
                        reg.Rule.RuleId, RuleRegistration.MaxConsecutiveErrors);
                    OnRuleStateChanged?.Invoke(reg.Rule.RuleId, RuleState.DisabledCrashed);
                }
            }
        }
    }

    /// <summary>
    /// Returns the current state of all registered rules.
    /// </summary>
    public IReadOnlyList<(string RuleId, string Name, RuleState State)> GetRuleStates()
    {
        return _rules.Select(r => (r.Rule.RuleId, r.Rule.Name, r.State)).ToList();
    }

    /// <summary>
    /// Disables a specific rule by ID (e.g., due to missing SimVars).
    /// </summary>
    public void DisableRule(string ruleId, RuleState reason)
    {
        var reg = _rules.FirstOrDefault(r => r.Rule.RuleId == ruleId);
        if (reg is null) return;

        reg.State = reason;
        Log.Warning("Rule {RuleId} manually disabled: {Reason}", ruleId, reason);
        OnRuleStateChanged?.Invoke(ruleId, reason);
    }

    /// <summary>
    /// Re-enables a previously disabled rule.
    /// </summary>
    public void EnableRule(string ruleId)
    {
        var reg = _rules.FirstOrDefault(r => r.Rule.RuleId == ruleId);
        if (reg is null) return;

        reg.State = RuleState.Enabled;
        reg.ConsecutiveErrors = 0;
        Log.Information("Rule {RuleId} re-enabled", ruleId);
        OnRuleStateChanged?.Invoke(ruleId, RuleState.Enabled);
    }
}

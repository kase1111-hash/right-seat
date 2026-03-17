namespace Guardian.Core;

/// <summary>
/// Interface for all detection rules. Each rule is a self-contained analysis unit
/// that evaluates current telemetry state and history and may emit an alert.
///
/// Rules are registered at startup and evaluated by the detection engine on their
/// configured interval. A rule returns null if no anomaly is detected, or an Alert if one is.
/// </summary>
public interface IDetectionRule
{
    /// <summary>Rule identifier, e.g., "R001".</summary>
    string RuleId { get; }

    /// <summary>Human-readable name, e.g., "Fuel Cross-Feed Mismatch".</summary>
    string Name { get; }

    /// <summary>Description of what this rule detects.</summary>
    string Description { get; }

    /// <summary>How often this rule should be evaluated.</summary>
    TimeSpan EvaluationInterval { get; }

    /// <summary>
    /// Returns true if this rule is applicable given the current aircraft profile and flight phase.
    /// Rules that are not applicable are skipped during evaluation (not an error).
    /// </summary>
    bool IsApplicable(AircraftProfile profile, FlightPhase phase);

    /// <summary>
    /// Evaluates the current telemetry state and history.
    /// Returns an Alert if an anomaly is detected, or null if conditions are normal.
    /// </summary>
    /// <param name="current">Current point-in-time telemetry values.</param>
    /// <param name="buffer">Historical telemetry buffer for trend analysis.</param>
    /// <param name="profile">Aircraft-specific normal operating ranges.</param>
    /// <param name="phase">Current flight phase for context-aware analysis.</param>
    Alert? Evaluate(
        TelemetrySnapshot current,
        ITelemetryBuffer buffer,
        AircraftProfile profile,
        FlightPhase phase);
}

namespace Guardian.Core;

/// <summary>
/// Severity levels for detection alerts.
/// Determines delivery timing, audio behavior, and sterile cockpit filtering.
/// </summary>
public enum AlertSeverity
{
    /// <summary>Informational. Logged but not actively presented.</summary>
    Info,

    /// <summary>Condition worth noting, no immediate action needed.</summary>
    Advisory,

    /// <summary>Developing problem requiring pilot attention.</summary>
    Warning,

    /// <summary>Immediate threat to flight safety.</summary>
    Critical,
}

/// <summary>
/// A structured alert emitted by a detection rule.
/// Contains severity, source rule, parameterized text, and a telemetry snapshot.
/// </summary>
public sealed class Alert
{
    /// <summary>Unique identifier for this specific alert instance.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The rule that generated this alert (e.g., "R001").</summary>
    public required string RuleId { get; init; }

    /// <summary>Severity level controlling delivery behavior.</summary>
    public required AlertSeverity Severity { get; init; }

    /// <summary>
    /// Localizable string key for the alert text template.
    /// Example: "R001_BOTH_ENGINES_SAME_TANK"
    /// </summary>
    public required string TextKey { get; init; }

    /// <summary>
    /// Parameter values to fill into the text template.
    /// Example: { "tank" = "left", "time_min" = "22", "unused_gal" = "18" }
    /// </summary>
    public Dictionary<string, string> TextParameters { get; init; } = new();

    /// <summary>UTC timestamp when the alert was generated.</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Snapshot of relevant telemetry values at the time of alert generation.
    /// Keys are SimVarId names with optional engine/tank index suffix.
    /// </summary>
    public Dictionary<string, double> TelemetrySnapshot { get; init; } = new();

    /// <summary>The flight phase at the time of alert generation.</summary>
    public FlightPhase FlightPhase { get; init; }

    /// <summary>
    /// Formats the alert text using the template and parameters.
    /// Falls back to key + raw parameters if no template is found.
    /// </summary>
    public string FormatText(Func<string, string?>? templateLookup = null)
    {
        var template = templateLookup?.Invoke(TextKey);
        if (template is null)
        {
            // Fallback: return key with parameters inline
            if (TextParameters.Count == 0)
                return TextKey;

            var paramStr = string.Join(", ", TextParameters.Select(kv => $"{kv.Key}={kv.Value}"));
            return $"{TextKey} [{paramStr}]";
        }

        var result = template;
        foreach (var (key, value) in TextParameters)
        {
            result = result.Replace($"{{{key}}}", value);
        }
        return result;
    }

    public override string ToString() =>
        $"[{Severity}] {RuleId} @ {Timestamp:HH:mm:ss} — {FormatText()}";
}

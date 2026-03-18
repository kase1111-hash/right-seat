using System.Text.Json;
using System.Text.Json.Serialization;
using Guardian.Core;

namespace Guardian.Replay;

/// <summary>
/// Expected alert results for a scenario, loaded from JSON.
///
/// Format:
/// {
///   "scenario_id": "R001_BothEnginesSameTank",
///   "description": "Both engines on left tank, right tank has fuel",
///   "expected_alerts": [
///     {
///       "rule_id": "R001",
///       "severity": "Warning",
///       "text_key": "R001_BOTH_ENGINES_SAME_TANK",
///       "earliest_sec": 5,
///       "latest_sec": 30,
///       "required": true
///     }
///   ],
///   "forbidden_alerts": [
///     { "rule_id": "R003" }
///   ]
/// }
/// </summary>
public sealed class ExpectedResults
{
    [JsonPropertyName("scenario_id")]
    public string ScenarioId { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("expected_alerts")]
    public List<ExpectedAlert> ExpectedAlerts { get; set; } = new();

    [JsonPropertyName("forbidden_alerts")]
    public List<ForbiddenAlert> ForbiddenAlerts { get; set; } = new();

    public static ExpectedResults Load(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<ExpectedResults>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new FormatException($"Failed to parse expected results: {filePath}");
    }
}

public sealed class ExpectedAlert
{
    [JsonPropertyName("rule_id")]
    public string RuleId { get; set; } = "";

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "";

    [JsonPropertyName("text_key")]
    public string? TextKey { get; set; }

    /// <summary>Earliest acceptable detection time (seconds from scenario start).</summary>
    [JsonPropertyName("earliest_sec")]
    public double EarliestSec { get; set; }

    /// <summary>Latest acceptable detection time (seconds from scenario start).</summary>
    [JsonPropertyName("latest_sec")]
    public double LatestSec { get; set; } = double.MaxValue;

    /// <summary>If true, this alert MUST appear for the scenario to pass.</summary>
    [JsonPropertyName("required")]
    public bool Required { get; set; } = true;

    public AlertSeverity ParsedSeverity =>
        Enum.TryParse<AlertSeverity>(Severity, ignoreCase: true, out var sev) ? sev : AlertSeverity.Info;
}

public sealed class ForbiddenAlert
{
    [JsonPropertyName("rule_id")]
    public string RuleId { get; set; } = "";

    [JsonPropertyName("text_key")]
    public string? TextKey { get; set; }
}

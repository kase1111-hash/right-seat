using System.Text.Json.Serialization;

namespace Guardian.Efb.Api;

/// <summary>
/// JSON DTO for an alert delivered to the EFB.
/// </summary>
public sealed class AlertDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("rule_id")]
    public string RuleId { get; set; } = "";

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "";

    [JsonPropertyName("text_key")]
    public string TextKey { get; set; } = "";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = "";

    [JsonPropertyName("flight_phase")]
    public string FlightPhase { get; set; } = "";

    [JsonPropertyName("parameters")]
    public Dictionary<string, string> Parameters { get; set; } = new();

    [JsonPropertyName("telemetry")]
    public Dictionary<string, double> Telemetry { get; set; } = new();

    [JsonPropertyName("deferred_from_sterile")]
    public bool DeferredFromSterile { get; set; }
}

/// <summary>
/// JSON DTO for overall Guardian status.
/// </summary>
public sealed class StatusDto
{
    [JsonPropertyName("connected")]
    public bool Connected { get; set; }

    [JsonPropertyName("flight_phase")]
    public string FlightPhase { get; set; } = "";

    [JsonPropertyName("sterile_cockpit")]
    public bool SterileCockpit { get; set; }

    [JsonPropertyName("aircraft_id")]
    public string AircraftId { get; set; } = "";

    [JsonPropertyName("aircraft_name")]
    public string AircraftName { get; set; } = "";

    [JsonPropertyName("active_alert_count")]
    public AlertCountDto AlertCounts { get; set; } = new();

    [JsonPropertyName("rules")]
    public List<RuleStatusDto> Rules { get; set; } = new();

    [JsonPropertyName("uptime_sec")]
    public double UptimeSec { get; set; }
}

public sealed class AlertCountDto
{
    [JsonPropertyName("critical")]
    public int Critical { get; set; }

    [JsonPropertyName("warning")]
    public int Warning { get; set; }

    [JsonPropertyName("advisory")]
    public int Advisory { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

public sealed class RuleStatusDto
{
    [JsonPropertyName("rule_id")]
    public string RuleId { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("state")]
    public string State { get; set; } = "";
}

/// <summary>
/// JSON DTO for settings updates from the EFB.
/// </summary>
public sealed class SettingsUpdateDto
{
    [JsonPropertyName("sterile_cockpit_manual")]
    public bool? SterileCockpitManual { get; set; }

    [JsonPropertyName("audio_enabled")]
    public bool? AudioEnabled { get; set; }

    [JsonPropertyName("sensitivity")]
    public string? Sensitivity { get; set; }
}

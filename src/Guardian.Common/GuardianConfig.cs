using Tomlyn;
using Tomlyn.Model;

namespace Guardian.Common;

/// <summary>
/// Runtime configuration loaded from guardian.toml.
/// </summary>
public sealed class GuardianConfig
{
    // [connection]
    public int SimConnectRetryIntervalSec { get; set; } = 5;
    public int SimConnectMaxRetries { get; set; } = 0;

    // [polling]
    public int GroupAIntervalMs { get; set; } = 250;
    public int GroupBIntervalMs { get; set; } = 1000;
    public int GroupCIntervalMs { get; set; } = 4000;

    // [buffer]
    public int HistoryDepthSec { get; set; } = 600;
    public int TrendWindowSec { get; set; } = 60;
    public int RateOfChangeWindowSec { get; set; } = 30;

    // [detection]
    public List<string> EnabledRules { get; set; } = new() { "R001", "R002", "R003", "R004", "R005", "R006", "R007", "R008" };
    public string Sensitivity { get; set; } = "standard";

    // [sterile_cockpit]
    public bool SterileCockpitEnabled { get; set; } = true;
    public List<string> SterileCockpitPhases { get; set; } = new() { "TAKEOFF", "APPROACH", "LANDING" };
    public string ManualToggleKey { get; set; } = "Ctrl+Shift+S";

    // [alerts]
    public bool AudioEnabled { get; set; } = true;
    public bool TtsEnabled { get; set; }
    public string TtsVoice { get; set; } = "default";
    public int CriticalRepeatIntervalSec { get; set; } = 30;
    public int WarningCooldownSec { get; set; } = 60;
    public int AdvisoryCooldownSec { get; set; } = 180;

    // [recording]
    public bool AutoRecord { get; set; }
    public string OutputDirectory { get; set; } = "./recordings";
    public string RecordingFormat { get; set; } = "csv";

    // [efb]
    public string CommunicationMode { get; set; } = "http";
    public int HttpPort { get; set; } = 9847;

    // [ui]
    public string Theme { get; set; } = "dark";
    public bool ShowSparklines { get; set; } = true;
    public bool TelemetryPanelVisible { get; set; } = true;

    /// <summary>
    /// Loads configuration from a TOML file. Returns defaults if the file doesn't exist.
    /// </summary>
    public static GuardianConfig Load(string path)
    {
        var config = new GuardianConfig();

        if (!File.Exists(path))
            return config;

        var tomlContent = File.ReadAllText(path);
        var model = Toml.ToModel(tomlContent);

        if (model.TryGetValue("connection", out var connObj) && connObj is TomlTable conn)
        {
            config.SimConnectRetryIntervalSec = GetInt(conn, "simconnect_retry_interval_sec", config.SimConnectRetryIntervalSec);
            config.SimConnectMaxRetries = GetInt(conn, "simconnect_max_retries", config.SimConnectMaxRetries);
        }

        if (model.TryGetValue("polling", out var pollObj) && pollObj is TomlTable poll)
        {
            config.GroupAIntervalMs = GetInt(poll, "group_a_interval_ms", config.GroupAIntervalMs);
            config.GroupBIntervalMs = GetInt(poll, "group_b_interval_ms", config.GroupBIntervalMs);
            config.GroupCIntervalMs = GetInt(poll, "group_c_interval_ms", config.GroupCIntervalMs);
        }

        if (model.TryGetValue("buffer", out var bufObj) && bufObj is TomlTable buf)
        {
            config.HistoryDepthSec = GetInt(buf, "history_depth_sec", config.HistoryDepthSec);
            config.TrendWindowSec = GetInt(buf, "trend_window_sec", config.TrendWindowSec);
            config.RateOfChangeWindowSec = GetInt(buf, "rate_of_change_window_sec", config.RateOfChangeWindowSec);
        }

        if (model.TryGetValue("detection", out var detObj) && detObj is TomlTable det)
        {
            if (det.TryGetValue("enabled_rules", out var rulesObj) && rulesObj is TomlArray rulesArr)
            {
                config.EnabledRules = rulesArr.OfType<string>().ToList();
            }
            config.Sensitivity = GetString(det, "sensitivity", config.Sensitivity);
        }

        if (model.TryGetValue("sterile_cockpit", out var sterObj) && sterObj is TomlTable ster)
        {
            config.SterileCockpitEnabled = GetBool(ster, "enabled", config.SterileCockpitEnabled);
            if (ster.TryGetValue("phases", out var phasesObj) && phasesObj is TomlArray phasesArr)
            {
                config.SterileCockpitPhases = phasesArr.OfType<string>().ToList();
            }
            config.ManualToggleKey = GetString(ster, "manual_toggle_key", config.ManualToggleKey);
        }

        if (model.TryGetValue("alerts", out var alertObj) && alertObj is TomlTable alert)
        {
            config.AudioEnabled = GetBool(alert, "audio_enabled", config.AudioEnabled);
            config.TtsEnabled = GetBool(alert, "tts_enabled", config.TtsEnabled);
            config.TtsVoice = GetString(alert, "tts_voice", config.TtsVoice);
            config.CriticalRepeatIntervalSec = GetInt(alert, "critical_repeat_interval_sec", config.CriticalRepeatIntervalSec);
            config.WarningCooldownSec = GetInt(alert, "warning_cooldown_sec", config.WarningCooldownSec);
            config.AdvisoryCooldownSec = GetInt(alert, "advisory_cooldown_sec", config.AdvisoryCooldownSec);
        }

        if (model.TryGetValue("recording", out var recObj) && recObj is TomlTable rec)
        {
            config.AutoRecord = GetBool(rec, "auto_record", config.AutoRecord);
            config.OutputDirectory = GetString(rec, "output_directory", config.OutputDirectory);
            config.RecordingFormat = GetString(rec, "format", config.RecordingFormat);
        }

        if (model.TryGetValue("efb", out var efbObj) && efbObj is TomlTable efb)
        {
            config.CommunicationMode = GetString(efb, "communication_mode", config.CommunicationMode);
            config.HttpPort = GetInt(efb, "http_port", config.HttpPort);
        }

        if (model.TryGetValue("ui", out var uiObj) && uiObj is TomlTable ui)
        {
            config.Theme = GetString(ui, "theme", config.Theme);
            config.ShowSparklines = GetBool(ui, "show_sparklines", config.ShowSparklines);
            config.TelemetryPanelVisible = GetBool(ui, "telemetry_panel_visible", config.TelemetryPanelVisible);
        }

        return config;
    }

    private static int GetInt(TomlTable table, string key, int defaultValue)
    {
        if (table.TryGetValue(key, out var val) && val is long l)
            return (int)l;
        return defaultValue;
    }

    private static string GetString(TomlTable table, string key, string defaultValue)
    {
        if (table.TryGetValue(key, out var val) && val is string s)
            return s;
        return defaultValue;
    }

    private static bool GetBool(TomlTable table, string key, bool defaultValue)
    {
        if (table.TryGetValue(key, out var val) && val is bool b)
            return b;
        return defaultValue;
    }
}

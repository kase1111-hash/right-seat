using Guardian.Common;
using Guardian.Core;
using Serilog;

namespace Guardian.Priority;

/// <summary>
/// Tracks per-rule alert cooldowns and deduplication.
///
/// - Suppresses duplicate alerts during configurable cooldown periods
/// - Allows escalation: if a new alert from the same rule has higher severity, it passes through
/// - Tracks active conditions so "resolved" info messages can be emitted when conditions clear
/// </summary>
public sealed class AlertCooldownTracker
{
    private static readonly ILogger Log = Serilog.Log.ForContext<AlertCooldownTracker>();

    private readonly Dictionary<string, CooldownEntry> _entries = new();
    private readonly GuardianConfig _config;

    public AlertCooldownTracker(GuardianConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Checks whether an alert should be delivered or suppressed by cooldown.
    /// </summary>
    /// <returns>True if the alert should be delivered; false if in cooldown.</returns>
    public bool ShouldDeliver(Alert alert, DateTime now)
    {
        if (!_entries.TryGetValue(alert.RuleId, out var entry))
        {
            // First time seeing this rule — deliver and start tracking
            _entries[alert.RuleId] = new CooldownEntry
            {
                LastDelivered = now,
                LastSeverity = alert.Severity,
                TextKey = alert.TextKey,
                IsActive = true,
            };
            return true;
        }

        // Severity escalation always passes through
        if (alert.Severity > entry.LastSeverity)
        {
            Log.Information("Alert {RuleId} escalated from {Old} to {New}, bypassing cooldown",
                alert.RuleId, entry.LastSeverity, alert.Severity);
            entry.LastDelivered = now;
            entry.LastSeverity = alert.Severity;
            entry.TextKey = alert.TextKey;
            entry.IsActive = true;
            return true;
        }

        // Check cooldown period based on severity
        var cooldown = GetCooldown(alert.Severity);
        if ((now - entry.LastDelivered) >= cooldown)
        {
            entry.LastDelivered = now;
            entry.LastSeverity = alert.Severity;
            entry.TextKey = alert.TextKey;
            entry.IsActive = true;
            return true;
        }

        Log.Verbose("Alert {RuleId} suppressed by cooldown ({Remaining:F0}s remaining)",
            alert.RuleId, (cooldown - (now - entry.LastDelivered)).TotalSeconds);
        return false;
    }

    /// <summary>
    /// Marks a rule's condition as resolved. Returns a resolution info alert
    /// if the rule was previously active and in cooldown.
    /// </summary>
    public Alert? MarkResolved(string ruleId, DateTime now, FlightPhase phase)
    {
        if (!_entries.TryGetValue(ruleId, out var entry))
            return null;

        if (!entry.IsActive)
            return null;

        entry.IsActive = false;

        Log.Information("Rule {RuleId} condition resolved", ruleId);

        return new Alert
        {
            RuleId = ruleId,
            Severity = AlertSeverity.Info,
            TextKey = $"{entry.TextKey}_RESOLVED",
            TextParameters = new Dictionary<string, string>
            {
                ["original_severity"] = entry.LastSeverity.ToString(),
            },
            FlightPhase = phase,
        };
    }

    /// <summary>
    /// Returns whether a rule currently has an active (unresolved) condition.
    /// </summary>
    public bool IsActive(string ruleId)
    {
        return _entries.TryGetValue(ruleId, out var entry) && entry.IsActive;
    }

    /// <summary>
    /// Resets all tracking state.
    /// </summary>
    public void Reset()
    {
        _entries.Clear();
    }

    private TimeSpan GetCooldown(AlertSeverity severity) => severity switch
    {
        AlertSeverity.Critical => TimeSpan.FromSeconds(_config.CriticalRepeatIntervalSec),
        AlertSeverity.Warning => TimeSpan.FromSeconds(_config.WarningCooldownSec),
        AlertSeverity.Advisory => TimeSpan.FromSeconds(_config.AdvisoryCooldownSec),
        _ => TimeSpan.FromSeconds(60),
    };

    private sealed class CooldownEntry
    {
        public DateTime LastDelivered { get; set; }
        public AlertSeverity LastSeverity { get; set; }
        public string TextKey { get; set; } = "";
        public bool IsActive { get; set; }
    }
}

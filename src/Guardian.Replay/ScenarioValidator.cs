using Guardian.Core;
using Serilog;

namespace Guardian.Replay;

/// <summary>
/// Validates a replay result against expected results.
/// Produces a diff report of matched, missing, unexpected, and forbidden alerts.
/// </summary>
public sealed class ScenarioValidator
{
    private static readonly ILogger Log = Serilog.Log.ForContext<ScenarioValidator>();

    /// <summary>
    /// Validates replay results against expected results specification.
    /// </summary>
    public ValidationReport Validate(ReplayResult result, ExpectedResults expected)
    {
        var report = new ValidationReport
        {
            ScenarioId = expected.ScenarioId,
            Description = expected.Description,
            TotalSnapshots = result.Snapshots,
            Duration = result.Duration,
        };

        var scenarioStart = result.StartTime;

        // Check expected alerts
        foreach (var exp in expected.ExpectedAlerts)
        {
            var matching = result.DeliveredAlerts
                .Where(d => d.Alert.RuleId == exp.RuleId)
                .Where(d => exp.TextKey is null || d.Alert.TextKey == exp.TextKey)
                .Where(d =>
                {
                    var offsetSec = (d.Alert.Timestamp - scenarioStart).TotalSeconds;
                    return offsetSec >= exp.EarliestSec && offsetSec <= exp.LatestSec;
                })
                .ToList();

            if (matching.Count > 0)
            {
                var first = matching[0];
                var offsetSec = (first.Alert.Timestamp - scenarioStart).TotalSeconds;
                bool severityMatch = first.Alert.Severity == exp.ParsedSeverity;

                report.Matched.Add(new MatchedAlert
                {
                    ExpectedRuleId = exp.RuleId,
                    ExpectedSeverity = exp.Severity,
                    ActualSeverity = first.Alert.Severity.ToString(),
                    DetectionTimeSec = offsetSec,
                    SeverityCorrect = severityMatch,
                    TextKey = first.Alert.TextKey,
                });
            }
            else if (exp.Required)
            {
                report.Missing.Add(new MissingAlert
                {
                    RuleId = exp.RuleId,
                    ExpectedSeverity = exp.Severity,
                    TextKey = exp.TextKey,
                    WindowStart = exp.EarliestSec,
                    WindowEnd = exp.LatestSec,
                });
            }
        }

        // Check forbidden alerts
        foreach (var forbidden in expected.ForbiddenAlerts)
        {
            var violations = result.DeliveredAlerts
                .Where(d => d.Alert.RuleId == forbidden.RuleId)
                .Where(d => forbidden.TextKey is null || d.Alert.TextKey == forbidden.TextKey)
                .ToList();

            foreach (var v in violations)
            {
                report.Forbidden.Add(new ForbiddenAlertViolation
                {
                    RuleId = forbidden.RuleId,
                    TextKey = v.Alert.TextKey,
                    DetectionTimeSec = (v.Alert.Timestamp - scenarioStart).TotalSeconds,
                    Severity = v.Alert.Severity.ToString(),
                });
            }
        }

        // Find unexpected alerts (delivered but not in expected or forbidden list)
        var expectedRuleIds = expected.ExpectedAlerts.Select(e => e.RuleId).ToHashSet();
        var forbiddenRuleIds = expected.ForbiddenAlerts.Select(f => f.RuleId).ToHashSet();

        foreach (var delivered in result.DeliveredAlerts)
        {
            if (!expectedRuleIds.Contains(delivered.Alert.RuleId) &&
                !forbiddenRuleIds.Contains(delivered.Alert.RuleId))
            {
                report.Unexpected.Add(new UnexpectedAlert
                {
                    RuleId = delivered.Alert.RuleId,
                    Severity = delivered.Alert.Severity.ToString(),
                    TextKey = delivered.Alert.TextKey,
                    DetectionTimeSec = (delivered.Alert.Timestamp - scenarioStart).TotalSeconds,
                });
            }
        }

        report.Passed = report.Missing.Count == 0 && report.Forbidden.Count == 0;

        return report;
    }
}

/// <summary>
/// Full validation report for a scenario replay.
/// </summary>
public sealed class ValidationReport
{
    public string ScenarioId { get; set; } = "";
    public string Description { get; set; } = "";
    public int TotalSnapshots { get; set; }
    public TimeSpan Duration { get; set; }
    public bool Passed { get; set; }

    public List<MatchedAlert> Matched { get; set; } = new();
    public List<MissingAlert> Missing { get; set; } = new();
    public List<ForbiddenAlertViolation> Forbidden { get; set; } = new();
    public List<UnexpectedAlert> Unexpected { get; set; } = new();

    public string Summary =>
        $"{ScenarioId}: {(Passed ? "PASS" : "FAIL")} — " +
        $"{Matched.Count} matched, {Missing.Count} missing, " +
        $"{Forbidden.Count} forbidden, {Unexpected.Count} unexpected";
}

public sealed class MatchedAlert
{
    public string ExpectedRuleId { get; set; } = "";
    public string ExpectedSeverity { get; set; } = "";
    public string ActualSeverity { get; set; } = "";
    public double DetectionTimeSec { get; set; }
    public bool SeverityCorrect { get; set; }
    public string TextKey { get; set; } = "";
}

public sealed class MissingAlert
{
    public string RuleId { get; set; } = "";
    public string ExpectedSeverity { get; set; } = "";
    public string? TextKey { get; set; }
    public double WindowStart { get; set; }
    public double WindowEnd { get; set; }
}

public sealed class ForbiddenAlertViolation
{
    public string RuleId { get; set; } = "";
    public string TextKey { get; set; } = "";
    public double DetectionTimeSec { get; set; }
    public string Severity { get; set; } = "";
}

public sealed class UnexpectedAlert
{
    public string RuleId { get; set; } = "";
    public string Severity { get; set; } = "";
    public string TextKey { get; set; } = "";
    public double DetectionTimeSec { get; set; }
}

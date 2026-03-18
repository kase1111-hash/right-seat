using System.Text.Json;
using System.Text.Json.Serialization;

namespace Guardian.Replay;

/// <summary>
/// Computes and stores metrics across all scenario validations:
/// - Detection latency (mean, p50, p95)
/// - False positive rate
/// - Missed detection rate
/// - Severity accuracy
/// - Escalation timing accuracy
/// </summary>
public sealed class Scorecard
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("total_scenarios")]
    public int TotalScenarios { get; set; }

    [JsonPropertyName("passed")]
    public int Passed { get; set; }

    [JsonPropertyName("failed")]
    public int Failed { get; set; }

    [JsonPropertyName("detection_latency_mean_sec")]
    public double DetectionLatencyMeanSec { get; set; }

    [JsonPropertyName("detection_latency_p50_sec")]
    public double DetectionLatencyP50Sec { get; set; }

    [JsonPropertyName("detection_latency_p95_sec")]
    public double DetectionLatencyP95Sec { get; set; }

    [JsonPropertyName("false_positive_count")]
    public int FalsePositiveCount { get; set; }

    [JsonPropertyName("missed_detection_count")]
    public int MissedDetectionCount { get; set; }

    [JsonPropertyName("severity_accuracy_pct")]
    public double SeverityAccuracyPct { get; set; }

    [JsonPropertyName("total_alerts_generated")]
    public int TotalAlertsGenerated { get; set; }

    [JsonPropertyName("scenario_results")]
    public List<ScenarioSummary> ScenarioResults { get; set; } = new();

    /// <summary>
    /// Computes a scorecard from a list of validation reports.
    /// </summary>
    public static Scorecard Compute(IReadOnlyList<ValidationReport> reports)
    {
        var card = new Scorecard
        {
            TotalScenarios = reports.Count,
            Passed = reports.Count(r => r.Passed),
            Failed = reports.Count(r => !r.Passed),
        };

        var allLatencies = new List<double>();
        int severityCorrect = 0;
        int severityTotal = 0;

        foreach (var report in reports)
        {
            card.MissedDetectionCount += report.Missing.Count;
            card.FalsePositiveCount += report.Forbidden.Count;

            foreach (var matched in report.Matched)
            {
                allLatencies.Add(matched.DetectionTimeSec);
                severityTotal++;
                if (matched.SeverityCorrect) severityCorrect++;
            }

            card.TotalAlertsGenerated += report.Matched.Count + report.Unexpected.Count;

            card.ScenarioResults.Add(new ScenarioSummary
            {
                ScenarioId = report.ScenarioId,
                Passed = report.Passed,
                Matched = report.Matched.Count,
                Missing = report.Missing.Count,
                Forbidden = report.Forbidden.Count,
                Unexpected = report.Unexpected.Count,
            });
        }

        if (allLatencies.Count > 0)
        {
            allLatencies.Sort();
            card.DetectionLatencyMeanSec = allLatencies.Average();
            card.DetectionLatencyP50Sec = Percentile(allLatencies, 50);
            card.DetectionLatencyP95Sec = Percentile(allLatencies, 95);
        }

        card.SeverityAccuracyPct = severityTotal > 0
            ? (double)severityCorrect / severityTotal * 100.0
            : 100.0;

        return card;
    }

    /// <summary>
    /// Serializes the scorecard to JSON.
    /// </summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
    }

    /// <summary>
    /// Returns a human-readable summary.
    /// </summary>
    public string ToSummary()
    {
        return $"""
            ═══ Flight Guardian Scorecard ═══
            Timestamp:           {Timestamp:yyyy-MM-dd HH:mm:ss} UTC
            Scenarios:           {Passed}/{TotalScenarios} passed ({Failed} failed)
            Detection Latency:   mean={DetectionLatencyMeanSec:F1}s  p50={DetectionLatencyP50Sec:F1}s  p95={DetectionLatencyP95Sec:F1}s
            Severity Accuracy:   {SeverityAccuracyPct:F0}%
            False Positives:     {FalsePositiveCount}
            Missed Detections:   {MissedDetectionCount}
            Total Alerts:        {TotalAlertsGenerated}
            """;
    }

    private static double Percentile(List<double> sorted, int percentile)
    {
        if (sorted.Count == 0) return 0;
        double index = (percentile / 100.0) * (sorted.Count - 1);
        int lower = (int)Math.Floor(index);
        int upper = (int)Math.Ceiling(index);
        if (lower == upper) return sorted[lower];
        double weight = index - lower;
        return sorted[lower] * (1 - weight) + sorted[upper] * weight;
    }
}

public sealed class ScenarioSummary
{
    [JsonPropertyName("scenario_id")]
    public string ScenarioId { get; set; } = "";

    [JsonPropertyName("passed")]
    public bool Passed { get; set; }

    [JsonPropertyName("matched")]
    public int Matched { get; set; }

    [JsonPropertyName("missing")]
    public int Missing { get; set; }

    [JsonPropertyName("forbidden")]
    public int Forbidden { get; set; }

    [JsonPropertyName("unexpected")]
    public int Unexpected { get; set; }
}

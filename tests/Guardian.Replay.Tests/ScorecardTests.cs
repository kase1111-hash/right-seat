using Guardian.Replay;
using Xunit;

namespace Guardian.Replay.Tests;

public class ScorecardTests
{
    [Fact]
    public void Compute_AllPassing_CorrectCounts()
    {
        var reports = new List<ValidationReport>
        {
            new() { ScenarioId = "S1", Passed = true,
                Matched = new() { new MatchedAlert { DetectionTimeSec = 5, SeverityCorrect = true } } },
            new() { ScenarioId = "S2", Passed = true,
                Matched = new() { new MatchedAlert { DetectionTimeSec = 10, SeverityCorrect = true } } },
        };

        var card = Scorecard.Compute(reports);

        Assert.Equal(2, card.TotalScenarios);
        Assert.Equal(2, card.Passed);
        Assert.Equal(0, card.Failed);
        Assert.Equal(0, card.MissedDetectionCount);
        Assert.Equal(0, card.FalsePositiveCount);
        Assert.Equal(100.0, card.SeverityAccuracyPct);
    }

    [Fact]
    public void Compute_WithFailures_CorrectCounts()
    {
        var reports = new List<ValidationReport>
        {
            new() { ScenarioId = "S1", Passed = true,
                Matched = new() { new MatchedAlert { DetectionTimeSec = 5, SeverityCorrect = true } } },
            new() { ScenarioId = "S2", Passed = false,
                Missing = new() { new MissingAlert { RuleId = "R001" } },
                Forbidden = new() { new ForbiddenAlertViolation { RuleId = "R003" } } },
        };

        var card = Scorecard.Compute(reports);

        Assert.Equal(1, card.Failed);
        Assert.Equal(1, card.MissedDetectionCount);
        Assert.Equal(1, card.FalsePositiveCount);
    }

    [Fact]
    public void Compute_LatencyPercentiles()
    {
        var matched = Enumerable.Range(1, 100)
            .Select(i => new MatchedAlert { DetectionTimeSec = i, SeverityCorrect = true })
            .ToList();

        var reports = new List<ValidationReport>
        {
            new() { ScenarioId = "S1", Passed = true, Matched = matched },
        };

        var card = Scorecard.Compute(reports);

        Assert.Equal(50.5, card.DetectionLatencyMeanSec, precision: 1);
        Assert.True(card.DetectionLatencyP50Sec > 49 && card.DetectionLatencyP50Sec < 52);
        Assert.True(card.DetectionLatencyP95Sec > 94 && card.DetectionLatencyP95Sec < 97);
    }

    [Fact]
    public void Compute_SeverityAccuracy()
    {
        var reports = new List<ValidationReport>
        {
            new() { ScenarioId = "S1", Passed = true,
                Matched = new()
                {
                    new MatchedAlert { DetectionTimeSec = 5, SeverityCorrect = true },
                    new MatchedAlert { DetectionTimeSec = 10, SeverityCorrect = false },
                    new MatchedAlert { DetectionTimeSec = 15, SeverityCorrect = true },
                } },
        };

        var card = Scorecard.Compute(reports);

        // 2 out of 3 correct = 66.7%
        Assert.True(card.SeverityAccuracyPct > 66 && card.SeverityAccuracyPct < 67);
    }

    [Fact]
    public void ToJson_ValidJson()
    {
        var card = Scorecard.Compute(new List<ValidationReport>
        {
            new() { ScenarioId = "S1", Passed = true },
        });

        var json = card.ToJson();
        Assert.Contains("\"total_scenarios\"", json);
        Assert.Contains("\"passed\"", json);
    }

    [Fact]
    public void ToSummary_ContainsKeyInfo()
    {
        var card = Scorecard.Compute(new List<ValidationReport>
        {
            new() { ScenarioId = "S1", Passed = true },
        });

        var summary = card.ToSummary();
        Assert.Contains("Scorecard", summary);
        Assert.Contains("1/1 passed", summary);
    }
}

using Guardian.Core;
using Guardian.Priority;
using Guardian.Replay;
using Xunit;

namespace Guardian.Replay.Tests;

public class ScenarioValidatorTests
{
    private readonly ScenarioValidator _validator = new();

    [Fact]
    public void MatchedAlert_ReportsCorrectly()
    {
        var result = MakeResult(new[]
        {
            MakeDelivered("R001", AlertSeverity.Warning, "R001_TEST", 10),
        });

        var expected = new ExpectedResults
        {
            ScenarioId = "test",
            ExpectedAlerts = new()
            {
                new ExpectedAlert { RuleId = "R001", Severity = "Warning", EarliestSec = 5, LatestSec = 20 },
            },
        };

        var report = _validator.Validate(result, expected);

        Assert.True(report.Passed);
        Assert.Single(report.Matched);
        Assert.Empty(report.Missing);
        Assert.Equal("R001", report.Matched[0].ExpectedRuleId);
        Assert.True(report.Matched[0].SeverityCorrect);
    }

    [Fact]
    public void MissingRequiredAlert_Fails()
    {
        var result = MakeResult(Array.Empty<DeliveredAlert>());

        var expected = new ExpectedResults
        {
            ScenarioId = "test",
            ExpectedAlerts = new()
            {
                new ExpectedAlert { RuleId = "R001", Severity = "Warning", Required = true },
            },
        };

        var report = _validator.Validate(result, expected);

        Assert.False(report.Passed);
        Assert.Single(report.Missing);
        Assert.Equal("R001", report.Missing[0].RuleId);
    }

    [Fact]
    public void ForbiddenAlert_Fails()
    {
        var result = MakeResult(new[]
        {
            MakeDelivered("R003", AlertSeverity.Warning, "R003_TEST", 10),
        });

        var expected = new ExpectedResults
        {
            ScenarioId = "test",
            ForbiddenAlerts = new()
            {
                new ForbiddenAlert { RuleId = "R003" },
            },
        };

        var report = _validator.Validate(result, expected);

        Assert.False(report.Passed);
        Assert.Single(report.Forbidden);
    }

    [Fact]
    public void UnexpectedAlert_Tracked()
    {
        var result = MakeResult(new[]
        {
            MakeDelivered("R005", AlertSeverity.Advisory, "R005_TEST", 15),
        });

        var expected = new ExpectedResults { ScenarioId = "test" };

        var report = _validator.Validate(result, expected);

        Assert.True(report.Passed); // Unexpected don't fail, just tracked
        Assert.Single(report.Unexpected);
        Assert.Equal("R005", report.Unexpected[0].RuleId);
    }

    [Fact]
    public void AlertOutsideTimeWindow_NotMatched()
    {
        var result = MakeResult(new[]
        {
            MakeDelivered("R001", AlertSeverity.Warning, "R001_TEST", 50), // at 50s
        });

        var expected = new ExpectedResults
        {
            ScenarioId = "test",
            ExpectedAlerts = new()
            {
                new ExpectedAlert { RuleId = "R001", Severity = "Warning", EarliestSec = 5, LatestSec = 30, Required = true },
            },
        };

        var report = _validator.Validate(result, expected);

        Assert.False(report.Passed);
        Assert.Empty(report.Matched);
        Assert.Single(report.Missing);
    }

    [Fact]
    public void SeverityMismatch_MatchedButFlagged()
    {
        var result = MakeResult(new[]
        {
            MakeDelivered("R001", AlertSeverity.Critical, "R001_TEST", 10), // Critical instead of Warning
        });

        var expected = new ExpectedResults
        {
            ScenarioId = "test",
            ExpectedAlerts = new()
            {
                new ExpectedAlert { RuleId = "R001", Severity = "Warning", EarliestSec = 5, LatestSec = 20 },
            },
        };

        var report = _validator.Validate(result, expected);

        Assert.True(report.Passed); // Matched, but severity wrong
        Assert.Single(report.Matched);
        Assert.False(report.Matched[0].SeverityCorrect);
        Assert.Equal("Critical", report.Matched[0].ActualSeverity);
    }

    private static readonly DateTime ScenarioStart = new(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    private static DeliveredAlert MakeDelivered(string ruleId, AlertSeverity severity, string textKey, double offsetSec)
    {
        return new DeliveredAlert
        {
            Alert = new Alert
            {
                RuleId = ruleId,
                Severity = severity,
                TextKey = textKey,
                Timestamp = ScenarioStart.AddSeconds(offsetSec),
                FlightPhase = FlightPhase.Cruise,
            },
            DeliveredAt = ScenarioStart.AddSeconds(offsetSec),
        };
    }

    private static ReplayResult MakeResult(IEnumerable<DeliveredAlert> alerts)
    {
        return new ReplayResult
        {
            Snapshots = 10,
            Duration = TimeSpan.FromSeconds(60),
            StartTime = ScenarioStart,
            EndTime = ScenarioStart.AddSeconds(60),
            DeliveredAlerts = alerts.ToList(),
        };
    }
}

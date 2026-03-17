using Guardian.Common;
using Guardian.Core;
using Guardian.Priority;
using Xunit;

namespace Guardian.Priority.Tests;

public class AlertCooldownTrackerTests
{
    private static GuardianConfig DefaultConfig => new()
    {
        CriticalRepeatIntervalSec = 30,
        WarningCooldownSec = 60,
        AdvisoryCooldownSec = 180,
    };

    [Fact]
    public void FirstAlert_AlwaysDelivers()
    {
        var tracker = new AlertCooldownTracker(DefaultConfig);
        var now = DateTime.UtcNow;

        var alert = MakeAlert(AlertSeverity.Warning, "R001");
        Assert.True(tracker.ShouldDeliver(alert, now));
    }

    [Fact]
    public void SameAlertWithinCooldown_Suppressed()
    {
        var tracker = new AlertCooldownTracker(DefaultConfig);
        var now = DateTime.UtcNow;

        var alert1 = MakeAlert(AlertSeverity.Warning, "R001");
        tracker.ShouldDeliver(alert1, now);

        var alert2 = MakeAlert(AlertSeverity.Warning, "R001");
        Assert.False(tracker.ShouldDeliver(alert2, now.AddSeconds(30))); // within 60s cooldown
    }

    [Fact]
    public void SameAlertAfterCooldown_Delivers()
    {
        var tracker = new AlertCooldownTracker(DefaultConfig);
        var now = DateTime.UtcNow;

        tracker.ShouldDeliver(MakeAlert(AlertSeverity.Warning, "R001"), now);

        Assert.True(tracker.ShouldDeliver(
            MakeAlert(AlertSeverity.Warning, "R001"),
            now.AddSeconds(61))); // after 60s cooldown
    }

    [Fact]
    public void SeverityEscalation_BypassesCooldown()
    {
        var tracker = new AlertCooldownTracker(DefaultConfig);
        var now = DateTime.UtcNow;

        // Start with advisory
        tracker.ShouldDeliver(MakeAlert(AlertSeverity.Advisory, "R001"), now);

        // Escalate to warning within cooldown — should deliver
        Assert.True(tracker.ShouldDeliver(
            MakeAlert(AlertSeverity.Warning, "R001"),
            now.AddSeconds(5)));
    }

    [Fact]
    public void SeverityDowngrade_RespectsCooldown()
    {
        var tracker = new AlertCooldownTracker(DefaultConfig);
        var now = DateTime.UtcNow;

        // Start with warning
        tracker.ShouldDeliver(MakeAlert(AlertSeverity.Warning, "R001"), now);

        // Downgrade to advisory within cooldown — suppressed
        Assert.False(tracker.ShouldDeliver(
            MakeAlert(AlertSeverity.Advisory, "R001"),
            now.AddSeconds(5)));
    }

    [Fact]
    public void MarkResolved_GeneratesInfoAlert()
    {
        var tracker = new AlertCooldownTracker(DefaultConfig);
        var now = DateTime.UtcNow;

        tracker.ShouldDeliver(MakeAlert(AlertSeverity.Warning, "R001", "R001_OIL_LOW"), now);

        var resolved = tracker.MarkResolved("R001", now.AddSeconds(30), FlightPhase.Cruise);

        Assert.NotNull(resolved);
        Assert.Equal(AlertSeverity.Info, resolved!.Severity);
        Assert.Contains("RESOLVED", resolved.TextKey);
        Assert.Equal("R001", resolved.RuleId);
    }

    [Fact]
    public void MarkResolved_WhenNotActive_ReturnsNull()
    {
        var tracker = new AlertCooldownTracker(DefaultConfig);
        var now = DateTime.UtcNow;

        var resolved = tracker.MarkResolved("R001", now, FlightPhase.Cruise);
        Assert.Null(resolved);
    }

    [Fact]
    public void MarkResolved_TwiceForSameRule_SecondReturnsNull()
    {
        var tracker = new AlertCooldownTracker(DefaultConfig);
        var now = DateTime.UtcNow;

        tracker.ShouldDeliver(MakeAlert(AlertSeverity.Warning, "R001"), now);
        tracker.MarkResolved("R001", now.AddSeconds(10), FlightPhase.Cruise);

        var second = tracker.MarkResolved("R001", now.AddSeconds(20), FlightPhase.Cruise);
        Assert.Null(second);
    }

    [Fact]
    public void IsActive_TrueAfterDelivery_FalseAfterResolved()
    {
        var tracker = new AlertCooldownTracker(DefaultConfig);
        var now = DateTime.UtcNow;

        Assert.False(tracker.IsActive("R001"));

        tracker.ShouldDeliver(MakeAlert(AlertSeverity.Warning, "R001"), now);
        Assert.True(tracker.IsActive("R001"));

        tracker.MarkResolved("R001", now.AddSeconds(10), FlightPhase.Cruise);
        Assert.False(tracker.IsActive("R001"));
    }

    [Fact]
    public void DifferentRules_IndependentCooldowns()
    {
        var tracker = new AlertCooldownTracker(DefaultConfig);
        var now = DateTime.UtcNow;

        tracker.ShouldDeliver(MakeAlert(AlertSeverity.Warning, "R001"), now);

        // Different rule at same time — should deliver
        Assert.True(tracker.ShouldDeliver(
            MakeAlert(AlertSeverity.Warning, "R002"), now));
    }

    [Fact]
    public void Reset_ClearsAllState()
    {
        var tracker = new AlertCooldownTracker(DefaultConfig);
        var now = DateTime.UtcNow;

        tracker.ShouldDeliver(MakeAlert(AlertSeverity.Warning, "R001"), now);
        Assert.True(tracker.IsActive("R001"));

        tracker.Reset();

        Assert.False(tracker.IsActive("R001"));
        Assert.True(tracker.ShouldDeliver(
            MakeAlert(AlertSeverity.Warning, "R001"), now)); // Delivers again
    }

    [Fact]
    public void CriticalCooldown_ShorterThanWarning()
    {
        var tracker = new AlertCooldownTracker(DefaultConfig);
        var now = DateTime.UtcNow;

        tracker.ShouldDeliver(MakeAlert(AlertSeverity.Critical, "R001"), now);

        // After 31 seconds (critical cooldown = 30s)
        Assert.True(tracker.ShouldDeliver(
            MakeAlert(AlertSeverity.Critical, "R001"),
            now.AddSeconds(31)));
    }

    private static Alert MakeAlert(AlertSeverity severity, string ruleId, string? textKey = null) => new()
    {
        RuleId = ruleId,
        Severity = severity,
        TextKey = textKey ?? $"{ruleId}_TEST",
        FlightPhase = FlightPhase.Cruise,
    };
}

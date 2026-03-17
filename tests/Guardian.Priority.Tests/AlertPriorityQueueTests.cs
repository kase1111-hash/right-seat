using Guardian.Core;
using Guardian.Priority;
using Xunit;

namespace Guardian.Priority.Tests;

public class AlertPriorityQueueTests
{
    private readonly AlertPriorityQueue _queue = new();

    [Fact]
    public void CriticalAlert_BypassesQueue_DeliveredImmediately()
    {
        var alert = MakeAlert(AlertSeverity.Critical, "R001");

        var immediate = _queue.Enqueue(alert, out var delivery);

        Assert.True(immediate);
        Assert.NotNull(delivery);
        Assert.Equal("R001", delivery!.RuleId);
        Assert.Equal(0, _queue.Count); // Not in queue
    }

    [Fact]
    public void InfoAlert_NeverQueued()
    {
        var alert = MakeAlert(AlertSeverity.Info, "R001");

        var immediate = _queue.Enqueue(alert, out var delivery);

        Assert.False(immediate);
        Assert.Null(delivery);
        Assert.Equal(0, _queue.Count);
    }

    [Fact]
    public void WarningAlert_Queued()
    {
        var alert = MakeAlert(AlertSeverity.Warning, "R001");

        _queue.Enqueue(alert, out _);

        Assert.Equal(1, _queue.Count);
    }

    [Fact]
    public void Dequeue_RespectsDeliverySpacing()
    {
        var now = DateTime.UtcNow;
        _queue.DeliverySpacing = TimeSpan.FromSeconds(3);

        _queue.Enqueue(MakeAlert(AlertSeverity.Warning, "R001"), out _);
        _queue.Enqueue(MakeAlert(AlertSeverity.Warning, "R002"), out _);

        // First dequeue works
        var first = _queue.Dequeue(now, FlightPhase.Cruise);
        Assert.NotNull(first);

        // Second dequeue too soon — null
        var second = _queue.Dequeue(now.AddSeconds(1), FlightPhase.Cruise);
        Assert.Null(second);

        // After spacing — delivers
        var third = _queue.Dequeue(now.AddSeconds(4), FlightPhase.Cruise);
        Assert.NotNull(third);
    }

    [Fact]
    public void Dequeue_PrioritizesBySeverity()
    {
        var now = DateTime.UtcNow;
        _queue.DeliverySpacing = TimeSpan.Zero;

        _queue.Enqueue(MakeAlert(AlertSeverity.Advisory, "R003"), out _);
        _queue.Enqueue(MakeAlert(AlertSeverity.Warning, "R001"), out _);

        var first = _queue.Dequeue(now, FlightPhase.Cruise);
        Assert.Equal("R001", first!.RuleId); // Warning first

        var second = _queue.Dequeue(now.AddSeconds(1), FlightPhase.Cruise);
        Assert.Equal("R003", second!.RuleId); // Advisory second
    }

    [Fact]
    public void Advisory_OnlyDuringCruiseOrGround()
    {
        var now = DateTime.UtcNow;
        _queue.DeliverySpacing = TimeSpan.Zero;

        _queue.Enqueue(MakeAlert(AlertSeverity.Advisory, "R003"), out _);

        // During climb — not delivered
        var result = _queue.Dequeue(now, FlightPhase.Climb);
        Assert.Null(result);

        // During cruise — delivered
        result = _queue.Dequeue(now.AddSeconds(1), FlightPhase.Cruise);
        Assert.NotNull(result);
    }

    [Fact]
    public void Advisory_NotDelivered_WhenHigherPriorityPending()
    {
        var now = DateTime.UtcNow;
        _queue.DeliverySpacing = TimeSpan.Zero;

        _queue.Enqueue(MakeAlert(AlertSeverity.Advisory, "R003"), out _);
        _queue.Enqueue(MakeAlert(AlertSeverity.Warning, "R001"), out _);

        // The warning should come first, not the advisory
        var first = _queue.Dequeue(now, FlightPhase.Cruise);
        Assert.Equal(AlertSeverity.Warning, first!.Severity);
    }

    [Fact]
    public void DrainAll_ReturnsSortedBySeverity()
    {
        _queue.Enqueue(MakeAlert(AlertSeverity.Advisory, "R003"), out _);
        _queue.Enqueue(MakeAlert(AlertSeverity.Warning, "R001"), out _);
        _queue.Enqueue(MakeAlert(AlertSeverity.Advisory, "R005"), out _);

        var drained = _queue.DrainAll();

        Assert.Equal(3, drained.Count);
        Assert.Equal(AlertSeverity.Warning, drained[0].Severity);
        Assert.Equal(AlertSeverity.Advisory, drained[1].Severity);
        Assert.Equal(0, _queue.Count);
    }

    [Fact]
    public void RemoveByRule_RemovesCorrectAlerts()
    {
        _queue.Enqueue(MakeAlert(AlertSeverity.Warning, "R001"), out _);
        _queue.Enqueue(MakeAlert(AlertSeverity.Advisory, "R002"), out _);
        _queue.Enqueue(MakeAlert(AlertSeverity.Warning, "R001"), out _);

        var removed = _queue.RemoveByRule("R001");

        Assert.Equal(2, removed.Count);
        Assert.Equal(1, _queue.Count); // Only R002 remains
    }

    private static Alert MakeAlert(AlertSeverity severity, string ruleId) => new()
    {
        RuleId = ruleId,
        Severity = severity,
        TextKey = $"{ruleId}_TEST",
        FlightPhase = FlightPhase.Cruise,
    };
}

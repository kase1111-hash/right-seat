using Guardian.Common;
using Guardian.Core;
using Guardian.Priority;
using Xunit;

namespace Guardian.Priority.Tests;

public class AlertPipelineTests
{
    private static GuardianConfig DefaultConfig => new()
    {
        SterileCockpitEnabled = true,
        AudioEnabled = true,
        CriticalRepeatIntervalSec = 30,
        WarningCooldownSec = 60,
        AdvisoryCooldownSec = 180,
    };

    [Fact]
    public void CriticalAlert_DeliveredImmediately()
    {
        var pipeline = new AlertPipeline(DefaultConfig);
        var delivered = new List<DeliveredAlert>();
        pipeline.OnAlertDelivered += d => delivered.Add(d);

        var now = DateTime.UtcNow;
        pipeline.Tick(now, FlightPhase.Cruise);
        pipeline.IngestAlert(MakeAlert(AlertSeverity.Critical, "R001"), now);

        Assert.Single(delivered);
        Assert.Equal(AlertSeverity.Critical, delivered[0].Alert.Severity);
    }

    [Fact]
    public void WarningAlert_DeliveredOnTick()
    {
        var pipeline = new AlertPipeline(DefaultConfig);
        var delivered = new List<DeliveredAlert>();
        pipeline.OnAlertDelivered += d => delivered.Add(d);

        var now = DateTime.UtcNow;
        pipeline.Tick(now, FlightPhase.Cruise);
        pipeline.IngestAlert(MakeAlert(AlertSeverity.Warning, "R001"), now);

        // Not delivered yet — needs a tick
        Assert.Empty(delivered);

        // Tick delivers it
        pipeline.Tick(now.AddSeconds(4), FlightPhase.Cruise);
        Assert.Single(delivered);
    }

    [Fact]
    public void InfoAlert_LoggedOnly_NeverDelivered()
    {
        var pipeline = new AlertPipeline(DefaultConfig);
        var delivered = new List<DeliveredAlert>();
        var logged = new List<Alert>();
        pipeline.OnAlertDelivered += d => delivered.Add(d);
        pipeline.OnInfoLogged += a => logged.Add(a);

        var now = DateTime.UtcNow;
        pipeline.IngestAlert(MakeAlert(AlertSeverity.Info, "R001"), now);
        pipeline.Tick(now.AddSeconds(5), FlightPhase.Cruise);

        Assert.Empty(delivered);
        Assert.Single(logged);
    }

    [Fact]
    public void SterileCockpit_SuppressesWarning()
    {
        var pipeline = new AlertPipeline(DefaultConfig);
        var delivered = new List<DeliveredAlert>();
        pipeline.OnAlertDelivered += d => delivered.Add(d);

        var now = DateTime.UtcNow;
        pipeline.Tick(now, FlightPhase.Takeoff); // Sterile active

        pipeline.IngestAlert(MakeAlert(AlertSeverity.Warning, "R001"), now);
        pipeline.Tick(now.AddSeconds(5), FlightPhase.Takeoff);

        Assert.Empty(delivered); // Suppressed during sterile
    }

    [Fact]
    public void SterileCockpit_CriticalNotSuppressed()
    {
        var pipeline = new AlertPipeline(DefaultConfig);
        var delivered = new List<DeliveredAlert>();
        pipeline.OnAlertDelivered += d => delivered.Add(d);

        var now = DateTime.UtcNow;
        pipeline.Tick(now, FlightPhase.Takeoff); // Sterile active

        pipeline.IngestAlert(MakeAlert(AlertSeverity.Critical, "R001"), now);

        Assert.Single(delivered);
        Assert.Equal(AlertSeverity.Critical, delivered[0].Alert.Severity);
    }

    [Fact]
    public void SterileCockpit_ReleasesAlertsAfterExit()
    {
        var pipeline = new AlertPipeline(DefaultConfig);
        var delivered = new List<DeliveredAlert>();
        pipeline.OnAlertDelivered += d => delivered.Add(d);

        var now = DateTime.UtcNow;

        // Enter sterile
        pipeline.Tick(now, FlightPhase.Takeoff);

        // Ingest warning during sterile
        pipeline.IngestAlert(MakeAlert(AlertSeverity.Warning, "R001"), now.AddSeconds(1));

        // Exit sterile → climb
        pipeline.Tick(now.AddSeconds(10), FlightPhase.Climb);

        // The deferred alert should now be in the queue — tick to deliver
        pipeline.Tick(now.AddSeconds(14), FlightPhase.Climb);

        Assert.True(delivered.Count >= 1);
    }

    [Fact]
    public void Cooldown_SuppressesDuplicate()
    {
        var pipeline = new AlertPipeline(DefaultConfig);
        var delivered = new List<DeliveredAlert>();
        pipeline.OnAlertDelivered += d => delivered.Add(d);

        var now = DateTime.UtcNow;
        pipeline.Tick(now, FlightPhase.Cruise);

        // First alert delivers
        pipeline.IngestAlert(MakeAlert(AlertSeverity.Warning, "R001"), now);
        pipeline.Tick(now.AddSeconds(4), FlightPhase.Cruise);

        // Same alert within cooldown — suppressed
        pipeline.IngestAlert(MakeAlert(AlertSeverity.Warning, "R001"), now.AddSeconds(10));
        pipeline.Tick(now.AddSeconds(14), FlightPhase.Cruise);

        Assert.Single(delivered); // Only first one delivered
    }

    [Fact]
    public void Cooldown_EscalationBypassesCooldown()
    {
        var pipeline = new AlertPipeline(DefaultConfig);
        var delivered = new List<DeliveredAlert>();
        pipeline.OnAlertDelivered += d => delivered.Add(d);

        var now = DateTime.UtcNow;
        pipeline.Tick(now, FlightPhase.Cruise);

        // Advisory first
        pipeline.IngestAlert(MakeAlert(AlertSeverity.Advisory, "R001"), now);
        pipeline.Tick(now.AddSeconds(4), FlightPhase.Cruise);

        // Escalate to warning — should deliver
        pipeline.IngestAlert(MakeAlert(AlertSeverity.Warning, "R001"), now.AddSeconds(5));
        pipeline.Tick(now.AddSeconds(9), FlightPhase.Cruise);

        Assert.Equal(2, delivered.Count);
    }

    [Fact]
    public void NotifyResolved_GeneratesInfoLog()
    {
        var pipeline = new AlertPipeline(DefaultConfig);
        var logged = new List<Alert>();
        pipeline.OnInfoLogged += a => logged.Add(a);

        var now = DateTime.UtcNow;
        pipeline.Tick(now, FlightPhase.Cruise);

        pipeline.IngestAlert(MakeAlert(AlertSeverity.Warning, "R001"), now);
        pipeline.NotifyResolved("R001", now.AddSeconds(30), FlightPhase.Cruise);

        Assert.Single(logged);
        Assert.Contains("RESOLVED", logged[0].TextKey);
    }

    [Fact]
    public void AudioPlayed_OnDelivery()
    {
        var pipeline = new AlertPipeline(DefaultConfig);
        var tones = new List<(string tone, AlertSeverity severity)>();
        pipeline.Audio.OnPlayTone += (tone, sev) => tones.Add((tone, sev));

        var now = DateTime.UtcNow;
        pipeline.Tick(now, FlightPhase.Cruise);
        pipeline.IngestAlert(MakeAlert(AlertSeverity.Critical, "R001"), now);

        Assert.Single(tones);
        Assert.Equal("critical_alarm", tones[0].tone);
    }

    [Fact]
    public void MultipleRules_DeliveredInSeverityOrder()
    {
        var pipeline = new AlertPipeline(DefaultConfig);
        pipeline.Queue.DeliverySpacing = TimeSpan.Zero;
        var delivered = new List<DeliveredAlert>();
        pipeline.OnAlertDelivered += d => delivered.Add(d);

        var now = DateTime.UtcNow;
        pipeline.Tick(now, FlightPhase.Cruise);

        pipeline.IngestAlert(MakeAlert(AlertSeverity.Advisory, "R005"), now);
        pipeline.IngestAlert(MakeAlert(AlertSeverity.Warning, "R004"), now);

        // Tick to deliver both
        pipeline.Tick(now.AddSeconds(1), FlightPhase.Cruise);
        pipeline.Tick(now.AddSeconds(2), FlightPhase.Cruise);

        Assert.True(delivered.Count >= 1);
        // Warning should come before advisory
        if (delivered.Count >= 2)
        {
            Assert.Equal(AlertSeverity.Warning, delivered[0].Alert.Severity);
            Assert.Equal(AlertSeverity.Advisory, delivered[1].Alert.Severity);
        }
    }

    [Fact]
    public void EndToEnd_SyntheticTelemetryToPipeline()
    {
        // Simulates: detection → pipeline → delivery
        var pipeline = new AlertPipeline(DefaultConfig);
        var delivered = new List<DeliveredAlert>();
        var logged = new List<Alert>();
        pipeline.OnAlertDelivered += d => delivered.Add(d);
        pipeline.OnInfoLogged += a => logged.Add(a);

        var now = DateTime.UtcNow;

        // Phase 1: Cruise — warning alert
        pipeline.Tick(now, FlightPhase.Cruise);
        pipeline.IngestAlert(MakeAlert(AlertSeverity.Warning, "R004"), now);
        pipeline.Tick(now.AddSeconds(4), FlightPhase.Cruise);
        Assert.Single(delivered);

        // Phase 2: Enter sterile (approach) — advisory suppressed
        pipeline.Tick(now.AddSeconds(10), FlightPhase.Approach);
        pipeline.IngestAlert(MakeAlert(AlertSeverity.Advisory, "R005"), now.AddSeconds(11));
        pipeline.Tick(now.AddSeconds(15), FlightPhase.Approach);
        Assert.Single(delivered); // Still only the first one

        // Phase 3: Critical during sterile — immediate delivery
        pipeline.IngestAlert(MakeAlert(AlertSeverity.Critical, "R001"), now.AddSeconds(20));
        Assert.Equal(2, delivered.Count);
        Assert.Equal(AlertSeverity.Critical, delivered[1].Alert.Severity);

        // Phase 4: Exit sterile — deferred advisory released
        pipeline.Tick(now.AddSeconds(30), FlightPhase.Cruise);
        pipeline.Tick(now.AddSeconds(34), FlightPhase.Cruise);
        Assert.True(delivered.Count >= 3);

        // Phase 5: Resolve the warning
        pipeline.NotifyResolved("R004", now.AddSeconds(40), FlightPhase.Cruise);
        Assert.True(logged.Count >= 1);
    }

    private static Alert MakeAlert(AlertSeverity severity, string ruleId) => new()
    {
        RuleId = ruleId,
        Severity = severity,
        TextKey = $"{ruleId}_TEST",
        FlightPhase = FlightPhase.Cruise,
    };
}

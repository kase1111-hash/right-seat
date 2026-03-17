using Guardian.Common;
using Guardian.Core;
using Serilog;

namespace Guardian.Priority;

/// <summary>
/// Orchestrates the complete alert delivery pipeline:
///   DetectionEngine.OnAlert → Cooldown check → Sterile cockpit filter
///     → Priority queue → Timed delivery → Audio feedback
///
/// This is the single integration point connecting detection to delivery.
/// </summary>
public sealed class AlertPipeline
{
    private static readonly ILogger Log = Serilog.Log.ForContext<AlertPipeline>();

    private readonly AlertPriorityQueue _queue;
    private readonly SterileCockpitManager _sterile;
    private readonly AlertCooldownTracker _cooldown;
    private readonly AudioAlertService _audio;

    private readonly List<Alert> _sterileDeferredAlerts = new();

    /// <summary>Raised when an alert is delivered to the pilot.</summary>
    public event Action<DeliveredAlert>? OnAlertDelivered;

    /// <summary>Raised when an info/resolved alert is logged.</summary>
    public event Action<Alert>? OnInfoLogged;

    public AlertPipeline(GuardianConfig config)
    {
        _queue = new AlertPriorityQueue();
        _sterile = new SterileCockpitManager(config);
        _cooldown = new AlertCooldownTracker(config);
        _audio = new AudioAlertService(config);
    }

    /// <summary>
    /// Constructor for testing — allows injecting individual components.
    /// </summary>
    public AlertPipeline(
        AlertPriorityQueue queue,
        SterileCockpitManager sterile,
        AlertCooldownTracker cooldown,
        AudioAlertService audio)
    {
        _queue = queue;
        _sterile = sterile;
        _cooldown = cooldown;
        _audio = audio;
    }

    /// <summary>Access to the sterile cockpit manager for manual toggle.</summary>
    public SterileCockpitManager SterileCockpit => _sterile;

    /// <summary>Access to the audio service for silencing alarms.</summary>
    public AudioAlertService Audio => _audio;

    /// <summary>Access to the queue for inspection.</summary>
    public AlertPriorityQueue Queue => _queue;

    /// <summary>
    /// Ingests a raw alert from the detection engine.
    /// Applies cooldown, sterile filtering, and queuing.
    /// </summary>
    public void IngestAlert(Alert alert, DateTime now)
    {
        // 1. Info alerts — log only
        if (alert.Severity == AlertSeverity.Info)
        {
            Log.Debug("INFO alert {RuleId}: {TextKey}", alert.RuleId, alert.TextKey);
            OnInfoLogged?.Invoke(alert);
            return;
        }

        // 2. Cooldown check — suppress duplicates
        if (!_cooldown.ShouldDeliver(alert, now))
            return;

        // 3. Sterile cockpit suppression
        if (_sterile.ShouldSuppress(alert))
        {
            Log.Information("Alert {RuleId} ({Severity}) suppressed during sterile cockpit",
                alert.RuleId, alert.Severity);
            _sterileDeferredAlerts.Add(alert);
            return;
        }

        // 4. Enqueue (CRITICAL bypasses queue)
        if (_queue.Enqueue(alert, out var immediate) && immediate is not null)
        {
            Deliver(immediate, now, wasDeferredFromSterile: false);
        }
    }

    /// <summary>
    /// Called when a rule that was previously alerting no longer returns an alert.
    /// Generates a resolution info message.
    /// </summary>
    public void NotifyResolved(string ruleId, DateTime now, FlightPhase phase)
    {
        // Remove any pending alerts for this rule from the queue
        var removed = _queue.RemoveByRule(ruleId);
        if (removed.Count > 0)
        {
            Log.Debug("Removed {Count} pending alert(s) for resolved rule {RuleId}", removed.Count, ruleId);
        }

        // Remove from sterile deferred list
        _sterileDeferredAlerts.RemoveAll(a => a.RuleId == ruleId);

        // Generate resolution info
        var resolved = _cooldown.MarkResolved(ruleId, now, phase);
        if (resolved is not null)
        {
            OnInfoLogged?.Invoke(resolved);
        }
    }

    /// <summary>
    /// Updates the pipeline for the current tick. Call once per evaluation cycle.
    /// Handles:
    ///   - Sterile cockpit transitions (release queued alerts)
    ///   - Timed delivery from the priority queue
    /// </summary>
    public void Tick(DateTime now, FlightPhase currentPhase)
    {
        // Update sterile cockpit state
        bool justExitedSterile = _sterile.Update(currentPhase);

        // On sterile exit, release deferred alerts
        if (justExitedSterile)
        {
            ReleaseSterileAlerts(now, currentPhase);
        }

        // Try to deliver from queue
        var next = _queue.Dequeue(now, currentPhase);
        if (next is not null)
        {
            Deliver(next, now, wasDeferredFromSterile: false);
        }
    }

    private void ReleaseSterileAlerts(DateTime now, FlightPhase phase)
    {
        if (_sterileDeferredAlerts.Count == 0) return;

        Log.Information("Releasing {Count} alerts deferred during sterile cockpit",
            _sterileDeferredAlerts.Count);

        // Sort by severity descending, then timestamp
        var sorted = _sterileDeferredAlerts
            .OrderByDescending(a => a.Severity)
            .ThenBy(a => a.Timestamp)
            .ToList();

        _sterileDeferredAlerts.Clear();

        foreach (var alert in sorted)
        {
            // Check if condition resolved during sterile mode
            if (!_cooldown.IsActive(alert.RuleId))
            {
                // Condition resolved during sterile — deliver as INFO
                var infoAlert = new Alert
                {
                    RuleId = alert.RuleId,
                    Severity = AlertSeverity.Info,
                    TextKey = alert.TextKey + "_RESOLVED_DURING_STERILE",
                    TextParameters = alert.TextParameters,
                    FlightPhase = phase,
                };
                OnInfoLogged?.Invoke(infoAlert);
                continue;
            }

            // Re-enqueue for timed delivery (respecting spacing)
            _queue.Enqueue(alert, out var immediate);
            if (immediate is not null)
            {
                Deliver(immediate, now, wasDeferredFromSterile: true);
            }
        }
    }

    private void Deliver(Alert alert, DateTime now, bool wasDeferredFromSterile)
    {
        _queue.RecordDelivery(now);

        var delivered = new DeliveredAlert
        {
            Alert = alert,
            DeliveredAt = now,
            WasDeferredFromSterile = wasDeferredFromSterile,
        };

        Log.Information("DELIVERED: [{Severity}] {RuleId} — {TextKey} (deferred={Deferred})",
            alert.Severity, alert.RuleId, alert.TextKey, wasDeferredFromSterile);

        // Audio feedback
        _audio.PlayAlert(alert, now);

        OnAlertDelivered?.Invoke(delivered);
    }
}

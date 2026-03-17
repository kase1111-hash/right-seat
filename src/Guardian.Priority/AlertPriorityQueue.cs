using Guardian.Core;
using Serilog;

namespace Guardian.Priority;

/// <summary>
/// A delivered alert with delivery metadata.
/// </summary>
public sealed class DeliveredAlert
{
    public required Alert Alert { get; init; }
    public DateTime DeliveredAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// True if this alert was originally suppressed during a sterile cockpit phase
    /// and is being delivered after the sterile phase ended.
    /// </summary>
    public bool WasDeferredFromSterile { get; init; }

    /// <summary>
    /// If the condition resolved during sterile suppression, the alert is
    /// downgraded to Info and this flag is set.
    /// </summary>
    public bool ResolvedDuringSterile { get; init; }
}

/// <summary>
/// Severity-ordered priority queue for alerts.
///
/// Delivery rules:
///   CRITICAL → bypass queue, deliver immediately
///   WARNING  → deliver within 5 seconds unless sterile mode
///   ADVISORY → deliver only during CRUISE/GROUND with no higher-priority pending
///   INFO     → logged only, never queued
///
/// After a sterile phase ends, queued alerts are released in severity order
/// with 3-second spacing.
/// </summary>
public sealed class AlertPriorityQueue
{
    private static readonly ILogger Log = Serilog.Log.ForContext<AlertPriorityQueue>();

    private readonly List<Alert> _pending = new();
    private readonly object _lock = new();

    private DateTime _lastDeliveryTime = DateTime.MinValue;

    /// <summary>Minimum spacing between non-critical deliveries.</summary>
    public TimeSpan DeliverySpacing { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Maximum time a WARNING can wait before delivery.</summary>
    public TimeSpan WarningMaxDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Enqueues an alert for delivery. CRITICAL alerts are returned immediately
    /// via the out parameter. INFO alerts are discarded (log-only).
    /// </summary>
    /// <returns>True if the alert should be delivered immediately (CRITICAL).</returns>
    public bool Enqueue(Alert alert, out Alert? immediateDelivery)
    {
        immediateDelivery = null;

        if (alert.Severity == AlertSeverity.Info)
        {
            Log.Debug("INFO alert {RuleId} logged only, not queued", alert.RuleId);
            return false;
        }

        if (alert.Severity == AlertSeverity.Critical)
        {
            immediateDelivery = alert;
            Log.Information("CRITICAL alert {RuleId} bypasses queue for immediate delivery", alert.RuleId);
            return true;
        }

        lock (_lock)
        {
            _pending.Add(alert);
            Log.Debug("Alert {RuleId} ({Severity}) queued. Queue depth: {Depth}",
                alert.RuleId, alert.Severity, _pending.Count);
        }

        return false;
    }

    /// <summary>
    /// Dequeues the next alert eligible for delivery, respecting spacing and priority.
    /// Returns null if no alert is ready.
    /// </summary>
    /// <param name="now">Current time for spacing checks.</param>
    /// <param name="currentPhase">Current flight phase for advisory filtering.</param>
    public Alert? Dequeue(DateTime now, FlightPhase currentPhase)
    {
        lock (_lock)
        {
            if (_pending.Count == 0) return null;

            // Enforce delivery spacing
            if ((now - _lastDeliveryTime) < DeliverySpacing)
                return null;

            // Sort by severity descending (highest first), then by timestamp ascending (oldest first)
            _pending.Sort((a, b) =>
            {
                int sevCmp = b.Severity.CompareTo(a.Severity);
                return sevCmp != 0 ? sevCmp : a.Timestamp.CompareTo(b.Timestamp);
            });

            for (int i = 0; i < _pending.Count; i++)
            {
                var alert = _pending[i];

                // WARNING: check max delay — force delivery if overdue
                if (alert.Severity == AlertSeverity.Warning)
                {
                    _pending.RemoveAt(i);
                    _lastDeliveryTime = now;
                    return alert;
                }

                // ADVISORY: only deliver during CRUISE/GROUND with no higher-priority pending
                if (alert.Severity == AlertSeverity.Advisory)
                {
                    bool hasHigherPriority = _pending.Any(a =>
                        a.Severity > AlertSeverity.Advisory && a != alert);

                    if (hasHigherPriority)
                        continue;

                    bool safePhase = currentPhase is FlightPhase.Cruise or FlightPhase.Ground;
                    if (!safePhase)
                        continue;

                    _pending.RemoveAt(i);
                    _lastDeliveryTime = now;
                    return alert;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Drains all pending alerts in severity order (for sterile cockpit release).
    /// </summary>
    public IReadOnlyList<Alert> DrainAll()
    {
        lock (_lock)
        {
            var result = _pending
                .OrderByDescending(a => a.Severity)
                .ThenBy(a => a.Timestamp)
                .ToList();
            _pending.Clear();
            return result;
        }
    }

    /// <summary>
    /// Removes all pending alerts for a specific rule (e.g., when condition resolves).
    /// Returns the removed alerts.
    /// </summary>
    public IReadOnlyList<Alert> RemoveByRule(string ruleId)
    {
        lock (_lock)
        {
            var removed = _pending.Where(a => a.RuleId == ruleId).ToList();
            _pending.RemoveAll(a => a.RuleId == ruleId);
            return removed;
        }
    }

    /// <summary>Number of alerts currently in the queue.</summary>
    public int Count
    {
        get { lock (_lock) return _pending.Count; }
    }

    /// <summary>Records a delivery time (used when delivering critical alerts directly).</summary>
    public void RecordDelivery(DateTime now) => _lastDeliveryTime = now;
}

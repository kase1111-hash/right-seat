using Guardian.Common;
using Guardian.Core;
using Guardian.Detection;
using Guardian.Detection.Rules;
using Guardian.Priority;
using Guardian.SimConnect;
using Serilog;

namespace Guardian.Replay;

/// <summary>
/// Replays recorded scenario CSV data through the full Guardian pipeline
/// (buffer → flight phase → detection → priority queue) without SimConnect.
///
/// Supports variable playback speed (1x = real-time, 0 = as-fast-as-possible).
/// Collects all generated alerts for comparison against expected results.
/// </summary>
public sealed class ScenarioReplayEngine
{
    private static readonly ILogger Log = Serilog.Log.ForContext<ScenarioReplayEngine>();

    private readonly GuardianConfig _config;
    private readonly AircraftProfile _profile;

    public ScenarioReplayEngine(GuardianConfig config, AircraftProfile profile)
    {
        _config = config;
        _profile = profile;
    }

    /// <summary>
    /// Replays a scenario and returns all alerts that were generated.
    /// </summary>
    /// <param name="snapshots">Time-ordered telemetry snapshots from CSV.</param>
    /// <param name="playbackSpeed">Speed multiplier (0 = instant, 1.0 = real-time).</param>
    public ReplayResult Replay(IReadOnlyList<TelemetrySnapshot> snapshots, double playbackSpeed = 0)
    {
        if (snapshots.Count == 0)
            return new ReplayResult { Snapshots = 0 };

        var buffer = new TelemetryBuffer(TimeSpan.FromSeconds(_config.HistoryDepthSec));
        var phaseTracker = new FlightPhaseTracker();
        var detection = new DetectionEngine(_config.EnabledRules);
        var pipeline = new AlertPipeline(_config);

        // Register all rules
        detection.Register(new R001_FuelCrossFeedMismatch());
        detection.Register(new R002_AsymmetricPowerTrim());
        detection.Register(new R003_EngineTemperatureTrend());
        detection.Register(new R004_OilPressureAnomaly());
        detection.Register(new R005_FuelImbalance());
        detection.Register(new R006_IcingConditions());
        detection.Register(new R007_ElectricalDegradation());
        detection.Register(new R008_VacuumSystemFailure());

        // Collect results
        var deliveredAlerts = new List<DeliveredAlert>();
        var infoAlerts = new List<Alert>();
        var phaseTransitions = new List<(DateTime Time, FlightPhase From, FlightPhase To)>();

        detection.OnAlert += alert =>
        {
            pipeline.IngestAlert(alert, alert.Timestamp);
        };

        pipeline.OnAlertDelivered += delivered =>
        {
            deliveredAlerts.Add(delivered);
        };

        pipeline.OnInfoLogged += info =>
        {
            infoAlerts.Add(info);
        };

        phaseTracker.OnPhaseChanged += (old, @new) =>
        {
            phaseTransitions.Add((snapshots[0].Timestamp, old, @new));
        };

        var startTime = snapshots[0].Timestamp;
        var wallStart = DateTime.UtcNow;

        // Replay each snapshot
        for (int i = 0; i < snapshots.Count; i++)
        {
            var snapshot = snapshots[i];

            // Playback speed throttling
            if (playbackSpeed > 0 && i > 0)
            {
                var scenarioElapsed = snapshot.Timestamp - snapshots[i - 1].Timestamp;
                var targetDelay = scenarioElapsed / playbackSpeed;
                var actualElapsed = DateTime.UtcNow - wallStart;
                var remaining = targetDelay - actualElapsed;
                if (remaining > TimeSpan.Zero)
                    Thread.Sleep(remaining);
                wallStart = DateTime.UtcNow;
            }

            // Feed through pipeline
            buffer.Record(snapshot);
            phaseTracker.Update(snapshot, buffer);
            detection.Evaluate(snapshot, buffer, _profile, phaseTracker.CurrentPhase);
            pipeline.Tick(snapshot.Timestamp, phaseTracker.CurrentPhase);
        }

        var endTime = snapshots[^1].Timestamp;

        return new ReplayResult
        {
            Snapshots = snapshots.Count,
            Duration = endTime - startTime,
            DeliveredAlerts = deliveredAlerts,
            InfoAlerts = infoAlerts,
            PhaseTransitions = phaseTransitions,
            StartTime = startTime,
            EndTime = endTime,
        };
    }
}

/// <summary>
/// Result of a scenario replay containing all generated alerts and metadata.
/// </summary>
public sealed class ReplayResult
{
    public int Snapshots { get; init; }
    public TimeSpan Duration { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public List<DeliveredAlert> DeliveredAlerts { get; init; } = new();
    public List<Alert> InfoAlerts { get; init; } = new();
    public List<(DateTime Time, FlightPhase From, FlightPhase To)> PhaseTransitions { get; init; } = new();
}

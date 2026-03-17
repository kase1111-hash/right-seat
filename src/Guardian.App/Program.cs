using Guardian.Common;
using Guardian.Core;
using Guardian.Detection;
using Guardian.Detection.Rules;
using Guardian.Priority;
using Guardian.SimConnect;
using Serilog;
using Serilog.Events;

namespace Guardian.App;

public static class Program
{
    public static async Task Main(string[] args)
    {
        // Initialize logging
        var logLevel = args.Contains("--debug") ? LogEventLevel.Debug : LogEventLevel.Information;
        Logging.Initialize(logLevel);
        Log.Information("Flight Guardian starting...");

        // Load configuration
        var configPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "config", "guardian.toml");
        if (args.Length > 0 && args[0] == "--config")
            configPath = args[1];

        var config = GuardianConfig.Load(configPath);
        Log.Information("Configuration loaded. Sensitivity: {Sensitivity}, Rules: {Rules}",
            config.Sensitivity, string.Join(", ", config.EnabledRules));

        // Load aircraft profiles
        var profilesPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "config", "profiles");
        var profileLoader = new ProfileLoader();
        profileLoader.LoadProfiles(profilesPath);
        Log.Information("Loaded {Count} aircraft profiles", profileLoader.Profiles.Count);

        // Initialize telemetry buffer
        var buffer = new TelemetryBuffer(TimeSpan.FromSeconds(config.HistoryDepthSec));

        // Initialize flight phase tracker
        var phaseTracker = new FlightPhaseTracker();
        phaseTracker.OnPhaseChanged += (old, @new) =>
        {
            Log.Information("Flight phase: {Old} → {New}", old, @new);
        };

        // Initialize detection engine
        var detectionEngine = new DetectionEngine(config.EnabledRules);
        detectionEngine.Register(new R001_FuelCrossFeedMismatch());
        detectionEngine.Register(new R002_AsymmetricPowerTrim());
        detectionEngine.Register(new R003_EngineTemperatureTrend());
        detectionEngine.Register(new R004_OilPressureAnomaly());
        detectionEngine.Register(new R005_FuelImbalance());
        detectionEngine.Register(new R006_IcingConditions());
        detectionEngine.Register(new R007_ElectricalDegradation());
        detectionEngine.Register(new R008_VacuumSystemFailure());
        // Initialize alert pipeline
        var alertPipeline = new AlertPipeline(config);
        alertPipeline.OnAlertDelivered += delivered =>
        {
            Log.Warning("DELIVERED: {Alert} (deferred={Deferred})",
                delivered.Alert, delivered.WasDeferredFromSterile);
        };
        alertPipeline.OnInfoLogged += info =>
        {
            Log.Information("INFO: {Alert}", info);
        };

        // Wire detection engine to alert pipeline
        detectionEngine.OnAlert += alert =>
        {
            alertPipeline.IngestAlert(alert, DateTime.UtcNow);
        };
        detectionEngine.OnRuleStateChanged += (ruleId, state) =>
        {
            Log.Warning("Rule {RuleId} state changed: {State}", ruleId, state);
        };

        // Use generic profile until aircraft is identified
        AircraftProfile? activeProfile = null;

        // Initialize SimConnect client
        using var simConnect = new SimConnectClient(
            retryIntervalMs: config.SimConnectRetryIntervalSec * 1000,
            maxRetries: config.SimConnectMaxRetries,
            groupAIntervalMs: config.GroupAIntervalMs,
            groupBIntervalMs: config.GroupBIntervalMs,
            groupCIntervalMs: config.GroupCIntervalMs);

        simConnect.OnSnapshot += snapshot =>
        {
            buffer.Record(snapshot);

            // Update flight phase
            phaseTracker.Update(snapshot, buffer);

            // Match profile on first snapshot if not yet matched
            if (activeProfile is null)
            {
                var engineCount = (int)(snapshot.Get(SimVarId.NumberOfEngines) ?? 1);
                var engineType = ((int)(snapshot.Get(SimVarId.EngineType) ?? 0)) == 0 ? "piston" : "turboprop";
                activeProfile = profileLoader.MatchProfile("", engineCount, engineType);
                if (activeProfile is not null)
                    Log.Information("Active profile: {Id} ({Name})", activeProfile.AircraftId, activeProfile.DisplayName);
            }

            // Evaluate detection rules
            if (activeProfile is not null)
            {
                detectionEngine.Evaluate(snapshot, buffer, activeProfile, phaseTracker.CurrentPhase);
            }

            // Tick alert pipeline (sterile cockpit transitions + timed delivery)
            alertPipeline.Tick(snapshot.Timestamp, phaseTracker.CurrentPhase);

            Log.Verbose("Snapshot recorded: {Count} values at {Time}", snapshot.Keys.Count, snapshot.Timestamp);
        };

        simConnect.OnStateChanged += state =>
        {
            Log.Information("SimConnect state changed: {State}", state);
        };

        // Start SimConnect connection loop
        Log.Information("Starting SimConnect client...");
        simConnect.Start();

        // Wait for shutdown signal
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            Log.Information("Shutdown requested (Ctrl+C)");
        };

        Log.Information("Flight Guardian running. Press Ctrl+C to stop.");

        try
        {
            await Task.Delay(Timeout.Infinite, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }

        Log.Information("Flight Guardian shutting down.");
        Log.CloseAndFlush();
    }
}

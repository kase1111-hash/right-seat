using Guardian.Common;
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
            Log.Verbose("Snapshot recorded: {Count} values at {Time}",
                snapshot.Keys.Count, snapshot.Timestamp);
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

using Guardian.Common;
using Guardian.Core;
using Guardian.Replay;
using Serilog;

/// <summary>
/// CLI regression test runner.
///
/// Usage:
///   guardian-replay [scenarios-dir] [--profile profile-id] [--config guardian.toml] [--speed N]
///
/// Runs all .csv scenario files found in the directory, validates against
/// matching .json expected-results files, and outputs a scorecard.
///
/// Exit code: 0 = all pass, 1 = regression detected.
/// </summary>

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    var scenariosDir = args.Length > 0 && !args[0].StartsWith("--")
        ? args[0]
        : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "training", "scenarios");

    var configPath = GetArg(args, "--config")
        ?? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "config", "guardian.toml");

    var profileId = GetArg(args, "--profile") ?? "generic_single_piston";
    var speed = double.TryParse(GetArg(args, "--speed"), out var s) ? s : 0;

    if (!Directory.Exists(scenariosDir))
    {
        Log.Error("Scenarios directory not found: {Dir}", scenariosDir);
        return 1;
    }

    var config = GuardianConfig.Load(configPath);
    var profileLoader = new ProfileLoader();
    var profilesDir = Path.Combine(Path.GetDirectoryName(configPath)!, "profiles");
    if (Directory.Exists(profilesDir))
        profileLoader.LoadProfiles(profilesDir);

    var profile = profileLoader.Profiles.FirstOrDefault(p => p.AircraftId == profileId)
        ?? new AircraftProfile { AircraftId = profileId, DisplayName = profileId };

    ProfileLoader.ConvertUnits(profile);

    Log.Information("Scenarios dir: {Dir}", scenariosDir);
    Log.Information("Profile: {Id} ({Name})", profile.AircraftId, profile.DisplayName);
    Log.Information("Playback speed: {Speed}x", speed == 0 ? "max" : speed);

    // Find scenario files
    var csvFiles = Directory.GetFiles(scenariosDir, "*.csv")
        .OrderBy(f => f)
        .ToArray();

    if (csvFiles.Length == 0)
    {
        Log.Warning("No .csv scenario files found in {Dir}", scenariosDir);
        return 0;
    }

    var expectedDir = Path.Combine(scenariosDir, "expected");
    var validator = new ScenarioValidator();
    var reports = new List<ValidationReport>();

    Log.Information("Found {Count} scenario files", csvFiles.Length);
    Console.WriteLine();

    foreach (var csvFile in csvFiles)
    {
        var scenarioName = Path.GetFileNameWithoutExtension(csvFile);
        var expectedFile = Path.Combine(expectedDir, $"{scenarioName}.json");

        Log.Information("═══ Running: {Scenario} ═══", scenarioName);

        // Load and replay
        var snapshots = ScenarioCsvReader.ReadCsv(csvFile);
        if (snapshots.Count == 0)
        {
            Log.Warning("  Empty scenario, skipping");
            continue;
        }

        var engine = new ScenarioReplayEngine(config, profile);
        var result = engine.Replay(snapshots, speed);

        Log.Information("  {Count} snapshots, {Duration:F0}s duration, {Alerts} alerts delivered",
            result.Snapshots, result.Duration.TotalSeconds, result.DeliveredAlerts.Count);

        foreach (var alert in result.DeliveredAlerts)
        {
            var offset = (alert.Alert.Timestamp - result.StartTime).TotalSeconds;
            Log.Information("  [{Severity}] {RuleId} @ {Offset:F1}s — {TextKey}",
                alert.Alert.Severity, alert.Alert.RuleId, offset, alert.Alert.TextKey);
        }

        // Validate against expected results (if available)
        if (File.Exists(expectedFile))
        {
            var expected = ExpectedResults.Load(expectedFile);
            var report = validator.Validate(result, expected);
            reports.Add(report);

            var status = report.Passed ? "PASS" : "FAIL";
            Log.Information("  Result: {Status} — {Summary}", status, report.Summary);

            foreach (var missing in report.Missing)
                Log.Error("    MISSING: {RuleId} ({Severity}) expected in [{Start:F0}s-{End:F0}s]",
                    missing.RuleId, missing.ExpectedSeverity, missing.WindowStart, missing.WindowEnd);

            foreach (var forbidden in report.Forbidden)
                Log.Error("    FORBIDDEN: {RuleId} ({Severity}) detected at {Time:F1}s",
                    forbidden.RuleId, forbidden.Severity, forbidden.DetectionTimeSec);
        }
        else
        {
            Log.Information("  No expected-results file, replay only (no validation)");
        }

        Console.WriteLine();
    }

    // Generate scorecard
    if (reports.Count > 0)
    {
        var scorecard = Scorecard.Compute(reports);
        Console.WriteLine(scorecard.ToSummary());

        // Write scorecard JSON
        var scorecardPath = Path.Combine(scenariosDir, "scorecard.json");
        File.WriteAllText(scorecardPath, scorecard.ToJson());
        Log.Information("Scorecard written to {Path}", scorecardPath);

        return scorecard.Failed > 0 ? 1 : 0;
    }

    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Unhandled exception in replay runner");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

static string? GetArg(string[] args, string flag)
{
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (args[i] == flag) return args[i + 1];
    }
    return null;
}

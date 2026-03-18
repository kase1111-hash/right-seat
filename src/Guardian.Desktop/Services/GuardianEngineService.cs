using Guardian.Common;
using Guardian.Core;
using Guardian.Detection;
using Guardian.Detection.Rules;
using Guardian.Efb.Api;
using Guardian.Priority;
using Guardian.SimConnect;
using Serilog;

namespace Guardian.Desktop.Services;

/// <summary>
/// Manages the complete Guardian backend pipeline (SimConnect → Buffer → Detection → Priority)
/// and exposes observable events for the UI layer.
/// </summary>
public sealed class GuardianEngineService : IDisposable
{
    private static readonly ILogger Log = Serilog.Log.ForContext<GuardianEngineService>();

    private readonly GuardianConfig _config;
    private readonly TelemetryBuffer _buffer;
    private readonly FlightPhaseTracker _phaseTracker;
    private readonly DetectionEngine _detection;
    private readonly AlertPipeline _pipeline;
    private readonly ProfileLoader _profileLoader;
    private SimConnectClient? _simConnect;
    private EfbHttpServer? _efbServer;
    private EfbStateProvider? _efbState;

    private AircraftProfile? _activeProfile;
    private bool _isRecording;
    private StreamWriter? _recordingWriter;

    // Events for UI binding
    public event Action<TelemetrySnapshot>? OnTelemetryUpdated;
    public event Action<FlightPhase, FlightPhase>? OnPhaseChanged;
    public event Action<DeliveredAlert>? OnAlertDelivered;
    public event Action<Alert>? OnInfoLogged;
    public event Action<string, RuleState>? OnRuleStateChanged;
    public event Action<string>? OnConnectionStateChanged;
    public event Action<AircraftProfile>? OnProfileMatched;

    public GuardianConfig Config => _config;
    public FlightPhase CurrentPhase => _phaseTracker.CurrentPhase;
    public AircraftProfile? ActiveProfile => _activeProfile;
    public AlertPipeline Pipeline => _pipeline;
    public TelemetryBuffer Buffer => _buffer;
    public DetectionEngine Detection => _detection;
    public bool IsRecording => _isRecording;

    public GuardianEngineService(GuardianConfig config)
    {
        _config = config;
        _buffer = new TelemetryBuffer(TimeSpan.FromSeconds(config.HistoryDepthSec));
        _phaseTracker = new FlightPhaseTracker();
        _detection = new DetectionEngine(config.EnabledRules);
        _pipeline = new AlertPipeline(config);
        _profileLoader = new ProfileLoader();

        // Register all detection rules
        _detection.Register(new R001_FuelCrossFeedMismatch());
        _detection.Register(new R002_AsymmetricPowerTrim());
        _detection.Register(new R003_EngineTemperatureTrend());
        _detection.Register(new R004_OilPressureAnomaly());
        _detection.Register(new R005_FuelImbalance());
        _detection.Register(new R006_IcingConditions());
        _detection.Register(new R007_ElectricalDegradation());
        _detection.Register(new R008_VacuumSystemFailure());

        // Wire internal events
        _phaseTracker.OnPhaseChanged += (old, @new) =>
        {
            OnPhaseChanged?.Invoke(old, @new);
        };

        _detection.OnAlert += alert =>
        {
            _pipeline.IngestAlert(alert, DateTime.UtcNow);
        };

        _detection.OnRuleStateChanged += (ruleId, state) =>
        {
            OnRuleStateChanged?.Invoke(ruleId, state);
        };

        _pipeline.OnAlertDelivered += delivered =>
        {
            OnAlertDelivered?.Invoke(delivered);
        };

        _pipeline.OnInfoLogged += info =>
        {
            OnInfoLogged?.Invoke(info);
        };
    }

    public void Start()
    {
        // Load profiles
        var profilesPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "config", "profiles");
        _profileLoader.LoadProfiles(profilesPath);
        Log.Information("Loaded {Count} aircraft profiles", _profileLoader.Profiles.Count);

        // Start SimConnect
        _simConnect = new SimConnectClient(
            retryIntervalMs: _config.SimConnectRetryIntervalSec * 1000,
            maxRetries: _config.SimConnectMaxRetries,
            groupAIntervalMs: _config.GroupAIntervalMs,
            groupBIntervalMs: _config.GroupBIntervalMs,
            groupCIntervalMs: _config.GroupCIntervalMs);

        _simConnect.OnSnapshot += HandleSnapshot;
        _simConnect.OnStateChanged += state =>
        {
            OnConnectionStateChanged?.Invoke(state);
            _efbState?.SetConnected(state == "Connected");
        };

        _simConnect.Start();

        // Start EFB HTTP server
        _efbState = new EfbStateProvider(
            _config,
            _pipeline,
            () => _detection.GetRuleStates(),
            () => _phaseTracker.CurrentPhase,
            () => _activeProfile);

        _efbServer = new EfbHttpServer(_config, _efbState);
        try
        {
            _efbServer.Start();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to start EFB HTTP server — EFB features disabled");
            _efbServer = null;
        }

        Log.Information("Guardian engine started");
    }

    public void Stop()
    {
        StopRecording();
        _efbServer?.Dispose();
        _efbServer = null;
        _simConnect?.Dispose();
        _simConnect = null;
        Log.Information("Guardian engine stopped");
    }

    private void HandleSnapshot(TelemetrySnapshot snapshot)
    {
        try
        {
            _buffer.Record(snapshot);
            _phaseTracker.Update(snapshot, _buffer);

            // Profile matching
            if (_activeProfile is null)
            {
                var engineCount = (int)(snapshot.Get(SimVarId.NumberOfEngines) ?? 1);
                var engineType = ((int)(snapshot.Get(SimVarId.EngineType) ?? 0)) == 0 ? "piston" : "turboprop";
                _activeProfile = _profileLoader.MatchProfile("", engineCount, engineType);
                if (_activeProfile is not null)
                {
                    Log.Information("Active profile: {Id} ({Name})", _activeProfile.AircraftId, _activeProfile.DisplayName);
                    OnProfileMatched?.Invoke(_activeProfile);
                }
            }

            // Detection
            if (_activeProfile is not null)
            {
                _detection.Evaluate(snapshot, _buffer, _activeProfile, _phaseTracker.CurrentPhase);
            }

            // Alert pipeline tick
            _pipeline.Tick(snapshot.Timestamp, _phaseTracker.CurrentPhase);

            // Recording
            if (_isRecording && _recordingWriter is not null)
            {
                WriteRecordingRow(snapshot);
            }

            // Notify UI
            OnTelemetryUpdated?.Invoke(snapshot);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error processing telemetry snapshot — skipping frame");
        }
    }

    // ── Recording ──

    public void StartRecording(string filePath)
    {
        if (_isRecording) return;

        _recordingWriter = new StreamWriter(filePath, append: false);
        _recordingWriter.WriteLine("timestamp,simvar_id,index,value");
        _isRecording = true;
        Log.Information("Recording started: {Path}", filePath);
    }

    public void StopRecording()
    {
        if (!_isRecording) return;

        _recordingWriter?.Flush();
        _recordingWriter?.Dispose();
        _recordingWriter = null;
        _isRecording = false;
        Log.Information("Recording stopped");
    }

    private void WriteRecordingRow(TelemetrySnapshot snapshot)
    {
        foreach (var key in snapshot.Keys)
        {
            var value = snapshot.Get(key.SimVarId, key.Index);
            if (value is not null)
            {
                _recordingWriter?.WriteLine($"{snapshot.Timestamp:O},{key.SimVarId},{key.Index},{value.Value:F6}");
            }
        }
    }

    /// <summary>Returns current state of all registered rules.</summary>
    public IReadOnlyList<(string RuleId, string Name, RuleState State)> GetRuleStates()
        => _detection.GetRuleStates();

    public void Dispose()
    {
        Stop();
    }
}

using Guardian.Core;
using Serilog;

namespace Guardian.SimConnect;

/// <summary>
/// Connection state for the SimConnect client.
/// </summary>
public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
}

/// <summary>
/// Wraps the MSFS SimConnect managed SDK. Handles connection lifecycle,
/// data definition registration, polling, and reconnection with backoff.
///
/// When built with the MSFS SDK present (SIMCONNECT_SDK, see the csproj),
/// the client drives a LiveSimConnectSession against the running sim.
/// Without the SDK (e.g., Linux dev environment or CI) it operates in stub
/// mode — it logs what it would do but produces no data. Replay and EFB
/// features work in both modes.
/// </summary>
public sealed class SimConnectClient : IDisposable
{
    private static readonly ILogger Log = Serilog.Log.ForContext<SimConnectClient>();

    private readonly int _retryIntervalMs;
    private readonly int _maxRetries;
    private readonly int _groupAIntervalMs;
    private readonly int _groupBIntervalMs;
    private readonly int _groupCIntervalMs;

    private ConnectionState _state = ConnectionState.Disconnected;
    private CancellationTokenSource? _cts;
    private readonly HashSet<SimVarId> _unavailableSimVars = new();

    /// <summary>Current connection state.</summary>
    public ConnectionState State => _state;

    /// <summary>SimVars that are not available for the current aircraft.</summary>
    public IReadOnlySet<SimVarId> UnavailableSimVars => _unavailableSimVars;

    /// <summary>Fired when a new telemetry snapshot is received from SimConnect.</summary>
    public event Action<TelemetrySnapshot>? OnSnapshot;

    /// <summary>Fired when connection state changes.</summary>
    public event Action<ConnectionState>? OnStateChanged;

    /// <summary>Fired once per connection with the loaded aircraft's TITLE, for profile matching.</summary>
    public event Action<string>? OnAircraftTitle;

    public SimConnectClient(
        int retryIntervalMs = 5000,
        int maxRetries = 0,
        int groupAIntervalMs = 250,
        int groupBIntervalMs = 1000,
        int groupCIntervalMs = 4000)
    {
        _retryIntervalMs = retryIntervalMs;
        _maxRetries = maxRetries;
        _groupAIntervalMs = groupAIntervalMs;
        _groupBIntervalMs = groupBIntervalMs;
        _groupCIntervalMs = groupCIntervalMs;
    }

    /// <summary>
    /// Begins the connection and polling loop. Non-blocking — runs on background tasks.
    /// </summary>
    public void Start()
    {
        if (_cts is not null) return;

        _cts = new CancellationTokenSource();
        _ = ConnectionLoopAsync(_cts.Token);
    }

    /// <summary>
    /// Stops polling and disconnects.
    /// </summary>
    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        SetState(ConnectionState.Disconnected);
    }

    private async Task ConnectionLoopAsync(CancellationToken ct)
    {
        int attempts = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                SetState(ConnectionState.Connecting);
                Log.Information("Attempting SimConnect connection (attempt {Attempt})...", attempts + 1);

#if SIMCONNECT_SDK
                using (var session = new LiveSimConnectSession(
                    _groupAIntervalMs,
                    snapshot => OnSnapshot?.Invoke(snapshot),
                    title => OnAircraftTitle?.Invoke(title),
                    MarkUnavailable))
                {
                    // Throws COMException when the sim is not running.
                    session.Connect();
                    SetState(ConnectionState.Connected);
                    attempts = 0;

                    // Runs until the sim quits, the receive loop faults,
                    // or we are cancelled.
                    await session.Closed.WaitAsync(ct);
                }

                if (ct.IsCancellationRequested)
                    break;

                Log.Information("SimConnect session ended — will reconnect");
                SetState(ConnectionState.Reconnecting);
#else
                // Built without the MSFS SDK (see Guardian.SimConnect.csproj).
                // Stub mode keeps replay, EFB, and UI development working on
                // any platform — it just produces no live telemetry.
                Log.Warning("SimConnect SDK not linked. Running in stub mode — no live telemetry.");
                SetState(ConnectionState.Disconnected);
#endif

                attempts++;
                if (_maxRetries > 0 && attempts >= _maxRetries)
                {
                    Log.Error("Max SimConnect connection retries ({Max}) reached. Giving up.", _maxRetries);
                    break;
                }

                await Task.Delay(_retryIntervalMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SimConnect connection error");
                SetState(ConnectionState.Reconnecting);
                attempts++;
                if (_maxRetries > 0 && attempts >= _maxRetries)
                {
                    Log.Error("Max SimConnect connection retries ({Max}) reached. Giving up.", _maxRetries);
                    break;
                }
                await Task.Delay(_retryIntervalMs, ct);
            }
        }
    }

    /// <summary>
    /// Marks a SimVar as unavailable for the current aircraft.
    /// Rules depending on it will be disabled with an INFO log.
    /// </summary>
    public void MarkUnavailable(SimVarId id)
    {
        if (_unavailableSimVars.Add(id))
        {
            Log.Information("SimVar unavailable for current aircraft: {Id}. Dependent rules will be disabled.", id);
        }
    }

    /// <summary>
    /// Called when SimConnect delivers a data snapshot. Builds a TelemetrySnapshot
    /// and fires the OnSnapshot event. Also used by tests and replay tooling.
    /// </summary>
    internal void HandleDataReceived(Dictionary<SimVarKey, double> values, DateTime timestamp)
    {
        var snapshot = new TelemetrySnapshot { Timestamp = timestamp };
        foreach (var (key, value) in values)
        {
            snapshot.Set(key.Id, value, key.Index);
        }

        OnSnapshot?.Invoke(snapshot);
    }

    private void SetState(ConnectionState newState)
    {
        if (_state == newState) return;
        var old = _state;
        _state = newState;
        Log.Information("SimConnect state: {Old} → {New}", old, newState);
        OnStateChanged?.Invoke(newState);
    }

    public void Dispose()
    {
        Stop();
    }
}

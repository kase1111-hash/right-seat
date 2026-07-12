#if SIMCONNECT_SDK
using System.Runtime.InteropServices;
using Guardian.Core;
using Microsoft.FlightSimulator.SimConnect;
using Serilog;
using MsfsSimConnect = Microsoft.FlightSimulator.SimConnect.SimConnect;

namespace Guardian.SimConnect;

/// <summary>
/// Live SimConnect session against the MSFS SDK managed library.
/// Only compiled when the MSFS SDK is present (SIMCONNECT_SDK constant,
/// see Guardian.SimConnect.csproj).
///
/// Design:
/// - Each SimVar (and each engine/tank index of indexed vars) gets its own
///   data definition and request, all typed FLOAT64. This avoids fragile
///   big-struct marshalling and lets exceptions disable a single var.
/// - Received values land in a latest-value dictionary; a timer assembles
///   a TelemetrySnapshot every Group-A interval and hands it to the client.
/// - The aircraft TITLE is requested once per connection for profile matching.
/// - SimConnect exceptions for unknown datums map back through the sent
///   packet id, so unsupported vars on a given aircraft are marked
///   unavailable instead of killing the session.
/// </summary>
internal sealed class LiveSimConnectSession : IDisposable
{
    private static readonly ILogger Log = Serilog.Log.ForContext<LiveSimConnectSession>();

    private const int MaxEngines = 4;
    private const int MaxTanks = 4;
    private const uint TitleRequestId = 100_000;
    private const uint TitleDefineId = 100_000;

    private readonly int _snapshotIntervalMs;
    private readonly Action<TelemetrySnapshot> _onSnapshot;
    private readonly Action<string> _onAircraftTitle;
    private readonly Action<SimVarId> _onSimVarUnavailable;

    private readonly EventWaitHandle _recvEvent = new(false, EventResetMode.AutoReset);
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private MsfsSimConnect? _simConnect;
    private Thread? _receiveThread;
    private Timer? _snapshotTimer;
    private volatile bool _disposed;
    private volatile bool _opened;

    // requestId → simvar key, and sendId → simvar (for exception mapping)
    private readonly Dictionary<uint, SimVarKey> _requestMap = new();
    private readonly Dictionary<uint, SimVarKey> _sendIdMap = new();
    private readonly Dictionary<SimVarKey, double> _latest = new();
    private readonly object _latestLock = new();
    private volatile bool _hasData;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct DoubleValue
    {
        public double Value;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    private struct String256
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Value;
    }

    // The managed SimConnect API takes Enum-typed ids; any enum value works.
    private enum Ids : uint { }

    public LiveSimConnectSession(
        int snapshotIntervalMs,
        Action<TelemetrySnapshot> onSnapshot,
        Action<string> onAircraftTitle,
        Action<SimVarId> onSimVarUnavailable)
    {
        _snapshotIntervalMs = snapshotIntervalMs;
        _onSnapshot = onSnapshot;
        _onAircraftTitle = onAircraftTitle;
        _onSimVarUnavailable = onSimVarUnavailable;
    }

    /// <summary>Task that completes when the sim closes the connection.</summary>
    public Task Closed => _closed.Task;

    /// <summary>
    /// Opens the SimConnect connection. Throws COMException when the sim
    /// is not running — the caller owns retry/backoff.
    /// </summary>
    public void Connect()
    {
        _simConnect = new MsfsSimConnect("Flight Guardian", IntPtr.Zero, 0, _recvEvent, 0);
        _simConnect.OnRecvOpen += HandleRecvOpen;
        _simConnect.OnRecvQuit += HandleRecvQuit;
        _simConnect.OnRecvException += HandleRecvException;
        _simConnect.OnRecvSimobjectData += HandleRecvSimobjectData;

        _receiveThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "SimConnect.Receive" };
        _receiveThread.Start();
    }

    private void ReceiveLoop()
    {
        while (!_disposed)
        {
            try
            {
                if (_recvEvent.WaitOne(1000))
                {
                    _simConnect?.ReceiveMessage();
                }
            }
            catch (Exception ex) when (!_disposed)
            {
                Log.Error(ex, "SimConnect receive error — closing session");
                _closed.TrySetResult();
                return;
            }
        }
    }

    private void HandleRecvOpen(MsfsSimConnect sender, SIMCONNECT_RECV_OPEN data)
    {
        Log.Information("SimConnect open: {App} {Major}.{Minor}",
            data.szApplicationName, data.dwApplicationVersionMajor, data.dwApplicationVersionMinor);
        _opened = true;

        RegisterAllDataDefinitions();
        RequestAircraftTitle();

        _snapshotTimer = new Timer(_ => EmitSnapshot(), null, _snapshotIntervalMs, _snapshotIntervalMs);
    }

    private void HandleRecvQuit(MsfsSimConnect sender, SIMCONNECT_RECV data)
    {
        Log.Information("SimConnect quit received — sim is shutting down");
        _closed.TrySetResult();
    }

    private void HandleRecvException(MsfsSimConnect sender, SIMCONNECT_RECV_EXCEPTION data)
    {
        var exception = (SIMCONNECT_EXCEPTION)data.dwException;

        if (_sendIdMap.TryGetValue(data.dwSendID, out var key))
        {
            Log.Warning("SimConnect exception {Exception} for {SimVar} — marking unavailable",
                exception, key);
            _onSimVarUnavailable(key.Id);
            return;
        }

        Log.Warning("SimConnect exception: {Exception} (sendId={SendId})", exception, data.dwSendID);
    }

    private void HandleRecvSimobjectData(MsfsSimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA data)
    {
        if (data.dwRequestID == TitleRequestId)
        {
            if (data.dwData is { Length: > 0 } && data.dwData[0] is String256 title)
            {
                Log.Information("Aircraft title: {Title}", title.Value);
                _onAircraftTitle(title.Value);
            }
            return;
        }

        if (!_requestMap.TryGetValue(data.dwRequestID, out var key))
            return;

        if (data.dwData is { Length: > 0 } && data.dwData[0] is DoubleValue value)
        {
            lock (_latestLock)
            {
                _latest[key] = value.Value;
            }
            _hasData = true;
        }
    }

    private void RegisterAllDataDefinitions()
    {
        if (_simConnect is null) return;

        uint nextId = 1;
        foreach (SimVarId id in Enum.GetValues<SimVarId>())
        {
            var name = SimVarMetadata.GetSimConnectName(id);
            var unit = SimVarMetadata.GetSimConnectUnit(id);
            var group = SimVarMetadata.GetGroup(id);

            if (SimVarMetadata.IsIndexed(id))
            {
                // Engine-indexed vars use 1-based indices both internally and
                // in SimConnect. Tank quantity is 0-based internally but
                // 1-based in SimConnect ("FUELSYSTEM TANK QUANTITY:1").
                bool isTankIndexed = id == SimVarId.FuelSystemTankQuantity;
                int count = isTankIndexed ? MaxTanks : MaxEngines;

                for (int simIndex = 1; simIndex <= count; simIndex++)
                {
                    int internalIndex = isTankIndexed ? simIndex - 1 : simIndex;
                    RegisterOne(nextId++, new SimVarKey(id, internalIndex), $"{name}:{simIndex}", unit, group);
                }
            }
            else
            {
                RegisterOne(nextId++, new SimVarKey(id), name, unit, group);
            }
        }

        Log.Information("Registered {Count} SimConnect data definitions", nextId - 1);
    }

    private void RegisterOne(uint id, SimVarKey key, string datumName, string unit, PollingGroup group)
    {
        if (_simConnect is null) return;

        _simConnect.AddToDataDefinition(
            (Ids)id, datumName, unit,
            SIMCONNECT_DATATYPE.FLOAT64, 0f, MsfsSimConnect.SIMCONNECT_UNUSED);
        TrackSendId(key);

        _simConnect.RegisterDataDefineStruct<DoubleValue>((Ids)id);

        // Group A and D update whenever the value changes (per sim frame);
        // the snapshot timer coalesces to the configured cadence.
        // Groups B and C poll at 1s / 4s.
        var (period, interval) = group switch
        {
            PollingGroup.GroupA => (SIMCONNECT_PERIOD.SIM_FRAME, 0u),
            PollingGroup.GroupB => (SIMCONNECT_PERIOD.SECOND, 0u),
            PollingGroup.GroupC => (SIMCONNECT_PERIOD.SECOND, 4u),
            _ => (SIMCONNECT_PERIOD.SIM_FRAME, 0u),
        };

        _simConnect.RequestDataOnSimObject(
            (Ids)id, (Ids)id, MsfsSimConnect.SIMCONNECT_OBJECT_ID_USER,
            period, SIMCONNECT_DATA_REQUEST_FLAG.CHANGED, 0, interval, 0);
        TrackSendId(key);

        _requestMap[id] = key;
    }

    private void RequestAircraftTitle()
    {
        if (_simConnect is null) return;

        _simConnect.AddToDataDefinition(
            (Ids)TitleDefineId, "TITLE", null,
            SIMCONNECT_DATATYPE.STRING256, 0f, MsfsSimConnect.SIMCONNECT_UNUSED);
        _simConnect.RegisterDataDefineStruct<String256>((Ids)TitleDefineId);
        _simConnect.RequestDataOnSimObject(
            (Ids)TitleRequestId, (Ids)TitleDefineId, MsfsSimConnect.SIMCONNECT_OBJECT_ID_USER,
            SIMCONNECT_PERIOD.ONCE, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);
    }

    private void TrackSendId(SimVarKey key)
    {
        try
        {
            _simConnect!.GetLastSentPacketID(out uint sendId);
            _sendIdMap[sendId] = key;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "GetLastSentPacketID failed — exception mapping degraded");
        }
    }

    private void EmitSnapshot()
    {
        if (!_opened || !_hasData || _disposed) return;

        var snapshot = new TelemetrySnapshot { Timestamp = DateTime.UtcNow };
        lock (_latestLock)
        {
            foreach (var (key, value) in _latest)
            {
                snapshot.Set(key.Id, value, key.Index);
            }
        }

        try
        {
            _onSnapshot(snapshot);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Snapshot handler threw");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _snapshotTimer?.Dispose();
        _snapshotTimer = null;

        try
        {
            _simConnect?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "SimConnect dispose error");
        }
        _simConnect = null;

        _recvEvent.Set(); // wake receive thread so it can observe _disposed
        _receiveThread?.Join(TimeSpan.FromSeconds(2));
        _recvEvent.Dispose();

        _closed.TrySetResult();
    }
}
#endif

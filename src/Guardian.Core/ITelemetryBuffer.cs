namespace Guardian.Core;

/// <summary>
/// Read-only interface for the telemetry ring buffer.
/// Detection rules consume this interface — they never access SimConnect directly.
/// </summary>
public interface ITelemetryBuffer
{
    /// <summary>
    /// Returns the most recent value for a SimVar, or null if never recorded.
    /// </summary>
    double? Latest(SimVarId id, int index = 0);

    /// <summary>
    /// Returns all recorded values for a SimVar within the specified time window,
    /// ordered oldest to newest.
    /// </summary>
    IReadOnlyList<SimVarValue> Window(SimVarId id, TimeSpan duration, int index = 0);

    /// <summary>
    /// Computes the rate of change (derivative) for a SimVar over the specified window.
    /// Returns the value in units-per-second. Returns null if insufficient data.
    /// </summary>
    double? RateOfChange(SimVarId id, TimeSpan window, int index = 0);

    /// <summary>
    /// Returns the change in value from the specified reference time to now.
    /// Returns null if no data exists at or before the reference time.
    /// </summary>
    double? Delta(SimVarId id, DateTime referenceTime, int index = 0);

    /// <summary>
    /// Returns the most recent complete telemetry snapshot, or null if no data yet.
    /// </summary>
    TelemetrySnapshot? LatestSnapshot { get; }
}

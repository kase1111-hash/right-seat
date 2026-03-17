namespace Guardian.Core;

/// <summary>
/// A key identifying a specific SimVar reading, optionally for a specific engine or tank index.
/// </summary>
public readonly record struct SimVarKey(SimVarId Id, int Index = 0)
{
    public override string ToString() =>
        Index == 0 ? Id.ToString() : $"{Id}:{Index}";
}

/// <summary>
/// A single timestamped value for a SimVar.
/// </summary>
public readonly record struct SimVarValue(
    double Value,
    DateTime Timestamp
);

/// <summary>
/// A point-in-time collection of all current SimVar values.
/// Produced by the SimConnect polling loop and stored in the ring buffer.
/// </summary>
public sealed class TelemetrySnapshot
{
    /// <summary>UTC timestamp when this snapshot was captured.</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    private readonly Dictionary<SimVarKey, double> _values = new();

    /// <summary>
    /// Sets a value in this snapshot.
    /// </summary>
    public void Set(SimVarId id, double value, int index = 0)
    {
        _values[new SimVarKey(id, index)] = value;
    }

    /// <summary>
    /// Gets a value from this snapshot. Returns null if the SimVar is not present.
    /// </summary>
    public double? Get(SimVarId id, int index = 0)
    {
        return _values.TryGetValue(new SimVarKey(id, index), out var val) ? val : null;
    }

    /// <summary>
    /// Gets a value, throwing if the SimVar is not present.
    /// Only use when you have already confirmed the SimVar exists via IsAvailable.
    /// </summary>
    public double GetRequired(SimVarId id, int index = 0)
    {
        if (_values.TryGetValue(new SimVarKey(id, index), out var val))
            return val;
        throw new KeyNotFoundException($"SimVar {id} (index {index}) not present in snapshot.");
    }

    /// <summary>
    /// Returns true if the specified SimVar is available in this snapshot.
    /// </summary>
    public bool IsAvailable(SimVarId id, int index = 0) =>
        _values.ContainsKey(new SimVarKey(id, index));

    /// <summary>
    /// Returns all SimVar keys present in this snapshot.
    /// </summary>
    public IReadOnlyCollection<SimVarKey> Keys => _values.Keys;

    /// <summary>
    /// Returns all values as a dictionary (for alert telemetry snapshots).
    /// </summary>
    public IReadOnlyDictionary<SimVarKey, double> All => _values;
}

using System.Collections.Concurrent;
using Guardian.Core;

namespace Guardian.SimConnect;

/// <summary>
/// Thread-safe ring buffer storing historical telemetry data.
/// SimConnect writes from the polling thread; detection rules read concurrently.
/// Configurable depth (default 10 minutes of history).
/// </summary>
public sealed class TelemetryBuffer : ITelemetryBuffer
{
    private readonly TimeSpan _maxAge;
    private readonly ConcurrentDictionary<SimVarKey, TimeSeries> _series = new();
    private TelemetrySnapshot? _latestSnapshot;

    public TelemetryBuffer(TimeSpan? maxAge = null)
    {
        _maxAge = maxAge ?? TimeSpan.FromSeconds(600);
    }

    /// <summary>
    /// Records a full telemetry snapshot into the buffer.
    /// Called by the SimConnect polling loop on each data receipt.
    /// </summary>
    public void Record(TelemetrySnapshot snapshot)
    {
        _latestSnapshot = snapshot;

        foreach (var key in snapshot.Keys)
        {
            var value = snapshot.Get(key.Id, key.Index);
            if (value is null) continue;

            var series = _series.GetOrAdd(key, _ => new TimeSeries(_maxAge));
            series.Add(snapshot.Timestamp, value.Value);
        }
    }

    /// <inheritdoc />
    public double? Latest(SimVarId id, int index = 0)
    {
        var key = new SimVarKey(id, index);
        if (_series.TryGetValue(key, out var series))
            return series.Latest();
        return null;
    }

    /// <inheritdoc />
    public IReadOnlyList<SimVarValue> Window(SimVarId id, TimeSpan duration, int index = 0)
    {
        var key = new SimVarKey(id, index);
        if (_series.TryGetValue(key, out var series))
            return series.Window(duration);
        return Array.Empty<SimVarValue>();
    }

    /// <inheritdoc />
    public double? RateOfChange(SimVarId id, TimeSpan window, int index = 0)
    {
        var key = new SimVarKey(id, index);
        if (!_series.TryGetValue(key, out var series))
            return null;

        return series.RateOfChange(window);
    }

    /// <inheritdoc />
    public double? Delta(SimVarId id, DateTime referenceTime, int index = 0)
    {
        var key = new SimVarKey(id, index);
        if (!_series.TryGetValue(key, out var series))
            return null;

        return series.Delta(referenceTime);
    }

    /// <inheritdoc />
    public TelemetrySnapshot? LatestSnapshot => _latestSnapshot;

    /// <summary>
    /// Removes expired entries from all time series. Called periodically.
    /// </summary>
    public void Prune()
    {
        foreach (var series in _series.Values)
        {
            series.Prune();
        }
    }
}

/// <summary>
/// A time series of double values for a single SimVar, backed by a list
/// with automatic pruning of entries older than maxAge.
/// Thread-safe for single-writer, multiple-reader access.
/// </summary>
internal sealed class TimeSeries
{
    private readonly TimeSpan _maxAge;
    private readonly object _lock = new();
    private readonly List<(DateTime Timestamp, double Value)> _entries = new();

    public TimeSeries(TimeSpan maxAge)
    {
        _maxAge = maxAge;
    }

    public void Add(DateTime timestamp, double value)
    {
        lock (_lock)
        {
            _entries.Add((timestamp, value));
        }
    }

    public double? Latest()
    {
        lock (_lock)
        {
            return _entries.Count > 0 ? _entries[^1].Value : null;
        }
    }

    public IReadOnlyList<SimVarValue> Window(TimeSpan duration)
    {
        lock (_lock)
        {
            if (_entries.Count == 0) return Array.Empty<SimVarValue>();

            var cutoff = _entries[^1].Timestamp - duration;
            var result = new List<SimVarValue>();

            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].Timestamp < cutoff) break;
                result.Add(new SimVarValue(_entries[i].Value, _entries[i].Timestamp));
            }

            result.Reverse();
            return result;
        }
    }

    /// <summary>
    /// Computes rate of change in units-per-second using linear regression
    /// over the specified window. Returns null if insufficient data (< 2 points).
    /// </summary>
    public double? RateOfChange(TimeSpan window)
    {
        lock (_lock)
        {
            if (_entries.Count < 2) return null;

            var cutoff = _entries[^1].Timestamp - window;
            var referenceTime = _entries[^1].Timestamp;

            // Collect points within window
            double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;
            int n = 0;

            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].Timestamp < cutoff) break;

                // x = seconds from most recent point (negative going back)
                double x = (_entries[i].Timestamp - referenceTime).TotalSeconds;
                double y = _entries[i].Value;

                sumX += x;
                sumY += y;
                sumXY += x * y;
                sumXX += x * x;
                n++;
            }

            if (n < 2) return null;

            // Linear regression slope = (n*sumXY - sumX*sumY) / (n*sumXX - sumX*sumX)
            double denominator = n * sumXX - sumX * sumX;
            if (Math.Abs(denominator) < 1e-10) return 0.0;

            return (n * sumXY - sumX * sumY) / denominator; // units per second
        }
    }

    /// <summary>
    /// Returns the change from the value at/before referenceTime to the latest value.
    /// </summary>
    public double? Delta(DateTime referenceTime)
    {
        lock (_lock)
        {
            if (_entries.Count == 0) return null;

            var latestValue = _entries[^1].Value;

            // Find the entry at or just before the reference time
            double? refValue = null;
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].Timestamp <= referenceTime)
                {
                    refValue = _entries[i].Value;
                    break;
                }
            }

            if (refValue is null) return null;
            return latestValue - refValue.Value;
        }
    }

    /// <summary>
    /// Removes entries older than maxAge relative to the most recent entry.
    /// </summary>
    public void Prune()
    {
        lock (_lock)
        {
            if (_entries.Count == 0) return;

            var cutoff = _entries[^1].Timestamp - _maxAge;
            int removeCount = 0;

            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Timestamp >= cutoff) break;
                removeCount++;
            }

            if (removeCount > 0)
            {
                _entries.RemoveRange(0, removeCount);
            }
        }
    }
}

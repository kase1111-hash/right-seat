using Guardian.Core;
using Serilog;

namespace Guardian.Replay;

/// <summary>
/// Reads scenario CSV files and produces a time-ordered sequence of TelemetrySnapshots.
///
/// CSV format:
///   timestamp,simvar_id,index,value
///   2024-01-15T10:00:00.000Z,GeneralEngRpm,1,2400
///   2024-01-15T10:00:00.000Z,GeneralEngOilPressure,1,72
///   ...
///
/// All rows sharing the same timestamp are grouped into a single TelemetrySnapshot.
/// </summary>
public static class ScenarioCsvReader
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(ScenarioCsvReader));

    /// <summary>
    /// Reads a CSV scenario file and returns a list of TelemetrySnapshots in time order.
    /// </summary>
    public static List<TelemetrySnapshot> ReadCsv(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        if (lines.Length < 2)
            return new List<TelemetrySnapshot>();

        // Parse header
        var header = lines[0].Split(',');
        if (header.Length < 4 || header[0] != "timestamp")
        {
            throw new FormatException(
                $"Invalid CSV header. Expected: timestamp,simvar_id,index,value. Got: {lines[0]}");
        }

        // Parse rows grouped by timestamp
        var groups = new SortedDictionary<DateTime, TelemetrySnapshot>();

        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var parts = line.Split(',');
            if (parts.Length < 4)
            {
                Log.Warning("Skipping malformed line {LineNum}: {Line}", i + 1, line);
                continue;
            }

            if (!DateTime.TryParse(parts[0], null, System.Globalization.DateTimeStyles.RoundtripKind, out var timestamp))
            {
                Log.Warning("Skipping line {LineNum}: invalid timestamp '{Ts}'", i + 1, parts[0]);
                continue;
            }

            if (!Enum.TryParse<SimVarId>(parts[1], ignoreCase: true, out var simVarId))
            {
                Log.Warning("Skipping line {LineNum}: unknown SimVar '{Var}'", i + 1, parts[1]);
                continue;
            }

            if (!int.TryParse(parts[2], out var index))
            {
                Log.Warning("Skipping line {LineNum}: invalid index '{Idx}'", i + 1, parts[2]);
                continue;
            }

            if (!double.TryParse(parts[3], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                Log.Warning("Skipping line {LineNum}: invalid value '{Val}'", i + 1, parts[3]);
                continue;
            }

            if (!groups.TryGetValue(timestamp, out var snapshot))
            {
                snapshot = new TelemetrySnapshot { Timestamp = timestamp };
                groups[timestamp] = snapshot;
            }

            snapshot.Set(simVarId, value, index);
        }

        Log.Information("Read {Count} snapshots from {File}", groups.Count, Path.GetFileName(filePath));
        return groups.Values.ToList();
    }

    /// <summary>
    /// Writes a sequence of snapshots to CSV (for recording).
    /// </summary>
    public static void WriteCsv(string filePath, IEnumerable<TelemetrySnapshot> snapshots)
    {
        using var writer = new StreamWriter(filePath);
        writer.WriteLine("timestamp,simvar_id,index,value");

        foreach (var snapshot in snapshots)
        {
            foreach (var key in snapshot.Keys)
            {
                var value = snapshot.Get(key.Id, key.Index);
                if (value is not null)
                {
                    writer.WriteLine(string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "{0:O},{1},{2},{3:F6}",
                        snapshot.Timestamp, key.Id, key.Index, value.Value));
                }
            }
        }
    }
}

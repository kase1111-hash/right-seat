using Guardian.Core;
using Guardian.Replay;
using Xunit;

namespace Guardian.Replay.Tests;

public class ScenarioCsvReaderTests
{
    [Fact]
    public void ReadCsv_ValidFile_ParsesSnapshots()
    {
        var path = WriteTempCsv("""
            timestamp,simvar_id,index,value
            2024-01-15T10:00:00.000Z,GeneralEngRpm,1,2400
            2024-01-15T10:00:00.000Z,GeneralEngOilPressure,1,72
            2024-01-15T10:00:05.000Z,GeneralEngRpm,1,2450
            2024-01-15T10:00:05.000Z,GeneralEngOilPressure,1,71
            """);

        var snapshots = ScenarioCsvReader.ReadCsv(path);

        Assert.Equal(2, snapshots.Count);
        Assert.Equal(2400, snapshots[0].Get(SimVarId.GeneralEngRpm, 1));
        Assert.Equal(72, snapshots[0].Get(SimVarId.GeneralEngOilPressure, 1));
        Assert.Equal(2450, snapshots[1].Get(SimVarId.GeneralEngRpm, 1));
    }

    [Fact]
    public void ReadCsv_TimestampsOrdered()
    {
        var path = WriteTempCsv("""
            timestamp,simvar_id,index,value
            2024-01-15T10:00:10.000Z,GeneralEngRpm,1,2400
            2024-01-15T10:00:00.000Z,GeneralEngRpm,1,2300
            2024-01-15T10:00:05.000Z,GeneralEngRpm,1,2350
            """);

        var snapshots = ScenarioCsvReader.ReadCsv(path);

        Assert.Equal(3, snapshots.Count);
        Assert.True(snapshots[0].Timestamp < snapshots[1].Timestamp);
        Assert.True(snapshots[1].Timestamp < snapshots[2].Timestamp);
    }

    [Fact]
    public void ReadCsv_SkipsMalformedLines()
    {
        var path = WriteTempCsv("""
            timestamp,simvar_id,index,value
            2024-01-15T10:00:00.000Z,GeneralEngRpm,1,2400
            bad_timestamp,GeneralEngRpm,1,2400
            2024-01-15T10:00:05.000Z,UnknownSimVar,1,99
            2024-01-15T10:00:10.000Z,GeneralEngRpm,1,2450
            """);

        var snapshots = ScenarioCsvReader.ReadCsv(path);

        Assert.Equal(2, snapshots.Count); // Only the two valid rows
    }

    [Fact]
    public void ReadCsv_EmptyFile_ReturnsEmpty()
    {
        var path = WriteTempCsv("timestamp,simvar_id,index,value\n");
        var snapshots = ScenarioCsvReader.ReadCsv(path);
        Assert.Empty(snapshots);
    }

    [Fact]
    public void WriteCsv_RoundTrips()
    {
        var snap1 = new TelemetrySnapshot { Timestamp = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc) };
        snap1.Set(SimVarId.GeneralEngRpm, 2400, 1);
        snap1.Set(SimVarId.GeneralEngOilPressure, 72, 1);

        var snap2 = new TelemetrySnapshot { Timestamp = new DateTime(2024, 1, 15, 10, 0, 5, DateTimeKind.Utc) };
        snap2.Set(SimVarId.GeneralEngRpm, 2450, 1);

        var path = Path.GetTempFileName();
        ScenarioCsvReader.WriteCsv(path, new[] { snap1, snap2 });

        var read = ScenarioCsvReader.ReadCsv(path);
        Assert.Equal(2, read.Count);
        Assert.Equal(2400, read[0].Get(SimVarId.GeneralEngRpm, 1));

        File.Delete(path);
    }

    private static string WriteTempCsv(string content)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, content);
        return path;
    }
}

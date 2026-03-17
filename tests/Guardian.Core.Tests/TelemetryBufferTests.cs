using Guardian.Core;
using Guardian.SimConnect;
using Xunit;

namespace Guardian.Core.Tests;

public class TelemetryBufferTests
{
    private static TelemetrySnapshot MakeSnapshot(DateTime time, SimVarId id, double value, int index = 0)
    {
        var snap = new TelemetrySnapshot { Timestamp = time };
        snap.Set(id, value, index);
        return snap;
    }

    [Fact]
    public void Latest_ReturnsLastRecordedValue()
    {
        var buffer = new TelemetryBuffer();
        var now = DateTime.UtcNow;

        buffer.Record(MakeSnapshot(now, SimVarId.AirspeedIndicated, 100.0));
        buffer.Record(MakeSnapshot(now.AddSeconds(1), SimVarId.AirspeedIndicated, 110.0));
        buffer.Record(MakeSnapshot(now.AddSeconds(2), SimVarId.AirspeedIndicated, 120.0));

        Assert.Equal(120.0, buffer.Latest(SimVarId.AirspeedIndicated));
    }

    [Fact]
    public void Latest_NoData_ReturnsNull()
    {
        var buffer = new TelemetryBuffer();

        Assert.Null(buffer.Latest(SimVarId.AirspeedIndicated));
    }

    [Fact]
    public void Window_ReturnsValuesWithinDuration()
    {
        var buffer = new TelemetryBuffer();
        var now = DateTime.UtcNow;

        // Record 10 seconds of data, one per second
        for (int i = 0; i < 10; i++)
        {
            buffer.Record(MakeSnapshot(now.AddSeconds(i), SimVarId.AirspeedIndicated, 100.0 + i));
        }

        // Request last 5 seconds
        var window = buffer.Window(SimVarId.AirspeedIndicated, TimeSpan.FromSeconds(5));

        Assert.True(window.Count >= 5);
        Assert.Equal(109.0, window[^1].Value); // most recent
    }

    [Fact]
    public void Window_NoData_ReturnsEmpty()
    {
        var buffer = new TelemetryBuffer();
        var result = buffer.Window(SimVarId.AirspeedIndicated, TimeSpan.FromSeconds(10));

        Assert.Empty(result);
    }

    [Fact]
    public void RateOfChange_ConstantIncrease_ReturnsPositiveRate()
    {
        var buffer = new TelemetryBuffer();
        var now = DateTime.UtcNow;

        // CHT increasing 1 Rankine per second for 30 seconds
        for (int i = 0; i <= 30; i++)
        {
            buffer.Record(MakeSnapshot(
                now.AddSeconds(i),
                SimVarId.EngCylinderHeadTemperature,
                900.0 + i, // Rankine
                index: 1));
        }

        var rate = buffer.RateOfChange(SimVarId.EngCylinderHeadTemperature, TimeSpan.FromSeconds(30), index: 1);

        Assert.NotNull(rate);
        Assert.InRange(rate.Value, 0.9, 1.1); // ~1.0 Rankine/sec
    }

    [Fact]
    public void RateOfChange_Constant_ReturnsZero()
    {
        var buffer = new TelemetryBuffer();
        var now = DateTime.UtcNow;

        for (int i = 0; i <= 10; i++)
        {
            buffer.Record(MakeSnapshot(
                now.AddSeconds(i),
                SimVarId.EngCylinderHeadTemperature,
                900.0, // constant
                index: 1));
        }

        var rate = buffer.RateOfChange(SimVarId.EngCylinderHeadTemperature, TimeSpan.FromSeconds(10), index: 1);

        Assert.NotNull(rate);
        Assert.InRange(rate.Value, -0.01, 0.01);
    }

    [Fact]
    public void RateOfChange_InsufficientData_ReturnsNull()
    {
        var buffer = new TelemetryBuffer();
        var now = DateTime.UtcNow;

        buffer.Record(MakeSnapshot(now, SimVarId.EngCylinderHeadTemperature, 900.0, index: 1));

        var rate = buffer.RateOfChange(SimVarId.EngCylinderHeadTemperature, TimeSpan.FromSeconds(10), index: 1);

        Assert.Null(rate);
    }

    [Fact]
    public void Delta_ReturnsChangeFromReference()
    {
        var buffer = new TelemetryBuffer();
        var now = DateTime.UtcNow;

        buffer.Record(MakeSnapshot(now, SimVarId.GeneralEngOilPressure, 80.0, index: 1));
        buffer.Record(MakeSnapshot(now.AddSeconds(30), SimVarId.GeneralEngOilPressure, 70.0, index: 1));
        buffer.Record(MakeSnapshot(now.AddSeconds(60), SimVarId.GeneralEngOilPressure, 55.0, index: 1));

        var delta = buffer.Delta(SimVarId.GeneralEngOilPressure, now, index: 1);

        Assert.NotNull(delta);
        Assert.Equal(-25.0, delta.Value); // 55 - 80 = -25
    }

    [Fact]
    public void Delta_NoReferenceData_ReturnsNull()
    {
        var buffer = new TelemetryBuffer();
        var now = DateTime.UtcNow;

        buffer.Record(MakeSnapshot(now.AddSeconds(10), SimVarId.GeneralEngOilPressure, 70.0));

        // Reference time is before any data
        var delta = buffer.Delta(SimVarId.GeneralEngOilPressure, now);

        Assert.Null(delta);
    }

    [Fact]
    public void Indexed_SimVars_Stored_Independently()
    {
        var buffer = new TelemetryBuffer();
        var now = DateTime.UtcNow;

        buffer.Record(MakeSnapshot(now, SimVarId.GeneralEngRpm, 2400.0, index: 1));
        buffer.Record(MakeSnapshot(now, SimVarId.GeneralEngRpm, 2350.0, index: 2));

        Assert.Equal(2400.0, buffer.Latest(SimVarId.GeneralEngRpm, index: 1));
        Assert.Equal(2350.0, buffer.Latest(SimVarId.GeneralEngRpm, index: 2));
    }

    [Fact]
    public void Prune_RemovesOldEntries()
    {
        var buffer = new TelemetryBuffer(maxAge: TimeSpan.FromSeconds(5));
        var now = DateTime.UtcNow;

        // Record data spanning 10 seconds
        for (int i = 0; i < 10; i++)
        {
            buffer.Record(MakeSnapshot(now.AddSeconds(i), SimVarId.AirspeedIndicated, 100.0 + i));
        }

        buffer.Prune();

        // Window requesting all data should only return ~5 seconds worth
        var window = buffer.Window(SimVarId.AirspeedIndicated, TimeSpan.FromSeconds(20));
        Assert.True(window.Count <= 6); // 5 seconds + 1 for boundary
    }

    [Fact]
    public void LatestSnapshot_ReturnsLastRecorded()
    {
        var buffer = new TelemetryBuffer();
        var now = DateTime.UtcNow;

        var snap1 = MakeSnapshot(now, SimVarId.AirspeedIndicated, 100.0);
        var snap2 = MakeSnapshot(now.AddSeconds(1), SimVarId.AirspeedIndicated, 110.0);

        buffer.Record(snap1);
        buffer.Record(snap2);

        Assert.NotNull(buffer.LatestSnapshot);
        Assert.Equal(snap2.Timestamp, buffer.LatestSnapshot!.Timestamp);
    }
}

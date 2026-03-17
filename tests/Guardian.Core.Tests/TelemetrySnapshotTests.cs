using Guardian.Core;
using Xunit;

namespace Guardian.Core.Tests;

public class TelemetrySnapshotTests
{
    [Fact]
    public void Set_And_Get_ReturnsValue()
    {
        var snapshot = new TelemetrySnapshot();
        snapshot.Set(SimVarId.GeneralEngRpm, 2400.0, index: 1);

        Assert.Equal(2400.0, snapshot.Get(SimVarId.GeneralEngRpm, index: 1));
    }

    [Fact]
    public void Get_MissingSimVar_ReturnsNull()
    {
        var snapshot = new TelemetrySnapshot();

        Assert.Null(snapshot.Get(SimVarId.GeneralEngRpm));
    }

    [Fact]
    public void GetRequired_MissingSimVar_Throws()
    {
        var snapshot = new TelemetrySnapshot();

        Assert.Throws<KeyNotFoundException>(() =>
            snapshot.GetRequired(SimVarId.GeneralEngRpm));
    }

    [Fact]
    public void IsAvailable_SetVar_ReturnsTrue()
    {
        var snapshot = new TelemetrySnapshot();
        snapshot.Set(SimVarId.AirspeedIndicated, 120.0);

        Assert.True(snapshot.IsAvailable(SimVarId.AirspeedIndicated));
    }

    [Fact]
    public void IsAvailable_UnsetVar_ReturnsFalse()
    {
        var snapshot = new TelemetrySnapshot();

        Assert.False(snapshot.IsAvailable(SimVarId.AirspeedIndicated));
    }

    [Fact]
    public void Indexed_SimVars_Are_Independent()
    {
        var snapshot = new TelemetrySnapshot();
        snapshot.Set(SimVarId.GeneralEngRpm, 2400.0, index: 1);
        snapshot.Set(SimVarId.GeneralEngRpm, 2350.0, index: 2);

        Assert.Equal(2400.0, snapshot.Get(SimVarId.GeneralEngRpm, 1));
        Assert.Equal(2350.0, snapshot.Get(SimVarId.GeneralEngRpm, 2));
        Assert.Null(snapshot.Get(SimVarId.GeneralEngRpm, 3));
    }

    [Fact]
    public void Keys_ReturnsAllSetKeys()
    {
        var snapshot = new TelemetrySnapshot();
        snapshot.Set(SimVarId.AirspeedIndicated, 120.0);
        snapshot.Set(SimVarId.IndicatedAltitude, 5000.0);
        snapshot.Set(SimVarId.GeneralEngRpm, 2400.0, index: 1);

        Assert.Equal(3, snapshot.Keys.Count);
    }
}

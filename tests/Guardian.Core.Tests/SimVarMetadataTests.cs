using Guardian.Core;
using Xunit;

namespace Guardian.Core.Tests;

public class SimVarMetadataTests
{
    [Theory]
    [InlineData(SimVarId.GeneralEngRpm, PollingGroup.GroupA)]
    [InlineData(SimVarId.EngCylinderHeadTemperature, PollingGroup.GroupA)]
    [InlineData(SimVarId.FuelTankSelector, PollingGroup.GroupB)]
    [InlineData(SimVarId.AirspeedIndicated, PollingGroup.GroupB)]
    [InlineData(SimVarId.AmbientTemperature, PollingGroup.GroupC)]
    [InlineData(SimVarId.SimOnGround, PollingGroup.GroupC)]
    [InlineData(SimVarId.GeneralEngCombustion, PollingGroup.GroupD)]
    [InlineData(SimVarId.AutopilotMaster, PollingGroup.GroupD)]
    public void GetGroup_ReturnsCorrectGroup(SimVarId id, PollingGroup expected)
    {
        Assert.Equal(expected, SimVarMetadata.GetGroup(id));
    }

    [Theory]
    [InlineData(SimVarId.GeneralEngRpm, true)]
    [InlineData(SimVarId.FuelSystemTankQuantity, true)]
    [InlineData(SimVarId.ThrottleLeverPosition, true)]
    [InlineData(SimVarId.AirspeedIndicated, false)]
    [InlineData(SimVarId.AmbientTemperature, false)]
    [InlineData(SimVarId.SimOnGround, false)]
    public void IsIndexed_ReturnsCorrectly(SimVarId id, bool expected)
    {
        Assert.Equal(expected, SimVarMetadata.IsIndexed(id));
    }

    [Fact]
    public void GetSimConnectName_AllIdsHaveNames()
    {
        foreach (SimVarId id in Enum.GetValues<SimVarId>())
        {
            var name = SimVarMetadata.GetSimConnectName(id);
            Assert.False(string.IsNullOrWhiteSpace(name), $"SimVarId {id} has no SimConnect name");
        }
    }

    [Fact]
    public void GetSimConnectUnit_AllIdsHaveUnits()
    {
        foreach (SimVarId id in Enum.GetValues<SimVarId>())
        {
            var unit = SimVarMetadata.GetSimConnectUnit(id);
            Assert.False(string.IsNullOrWhiteSpace(unit), $"SimVarId {id} has no SimConnect unit");
        }
    }
}

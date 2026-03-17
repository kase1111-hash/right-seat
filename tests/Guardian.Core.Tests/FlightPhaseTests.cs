using Guardian.Core;
using Xunit;

namespace Guardian.Core.Tests;

public class FlightPhaseTests
{
    [Theory]
    [InlineData(FlightPhase.Takeoff, true)]
    [InlineData(FlightPhase.Approach, true)]
    [InlineData(FlightPhase.Landing, true)]
    [InlineData(FlightPhase.Ground, false)]
    [InlineData(FlightPhase.Climb, false)]
    [InlineData(FlightPhase.Cruise, false)]
    [InlineData(FlightPhase.Descent, false)]
    public void IsSterile_CorrectForAllPhases(FlightPhase phase, bool expected)
    {
        Assert.Equal(expected, phase.IsSterile());
    }
}

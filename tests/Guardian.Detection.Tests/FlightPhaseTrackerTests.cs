using Guardian.Core;
using Guardian.Detection;
using Guardian.SimConnect;
using Xunit;

namespace Guardian.Detection.Tests;

public class FlightPhaseTrackerTests
{
    private readonly FlightPhaseTracker _tracker = new();

    private static TelemetrySnapshot MakeSnapshot(
        DateTime time,
        bool onGround = false,
        double verticalSpeed = 0,
        double altitude = 5000,
        double gearDown = 0,
        double flaps = 0)
    {
        var snap = new TelemetrySnapshot { Timestamp = time };
        snap.Set(SimVarId.SimOnGround, onGround ? 1.0 : 0.0);
        snap.Set(SimVarId.VerticalSpeed, verticalSpeed);
        snap.Set(SimVarId.IndicatedAltitude, altitude);
        snap.Set(SimVarId.GearHandlePosition, gearDown);
        snap.Set(SimVarId.FlapsHandlePercent, flaps);
        return snap;
    }

    private static TelemetryBuffer MakeBuffer() => new();

    private void SustainPhase(DateTime start, int seconds, Func<DateTime, TelemetrySnapshot> makeSnap)
    {
        var buffer = MakeBuffer();
        for (int i = 0; i <= seconds; i++)
        {
            var snap = makeSnap(start.AddSeconds(i));
            buffer.Record(snap);
            _tracker.Update(snap, buffer);
        }
    }

    [Fact]
    public void InitialState_IsGround()
    {
        Assert.Equal(FlightPhase.Ground, _tracker.CurrentPhase);
    }

    [Fact]
    public void Ground_ToTakeoff_WhenAirborneWithClimb()
    {
        var now = DateTime.UtcNow;

        // Start on ground
        SustainPhase(now, 3, t => MakeSnapshot(t, onGround: true));
        Assert.Equal(FlightPhase.Ground, _tracker.CurrentPhase);

        // Lift off with positive climb
        SustainPhase(now.AddSeconds(4), 8, t =>
            MakeSnapshot(t, onGround: false, verticalSpeed: 500, altitude: 200));

        Assert.Equal(FlightPhase.Takeoff, _tracker.CurrentPhase);
    }

    [Fact]
    public void Takeoff_ToClimb_WhenSustainedClimb()
    {
        var now = DateTime.UtcNow;

        // Get to takeoff
        SustainPhase(now, 3, t => MakeSnapshot(t, onGround: true));
        SustainPhase(now.AddSeconds(4), 8, t =>
            MakeSnapshot(t, onGround: false, verticalSpeed: 500, altitude: 200));
        Assert.Equal(FlightPhase.Takeoff, _tracker.CurrentPhase);

        // Continue climbing at higher altitude — should transition to Climb
        SustainPhase(now.AddSeconds(13), 8, t =>
            MakeSnapshot(t, onGround: false, verticalSpeed: 700, altitude: 2000));

        Assert.Equal(FlightPhase.Climb, _tracker.CurrentPhase);
    }

    [Fact]
    public void Climb_ToCruise_WhenLevelFlight()
    {
        var now = DateTime.UtcNow;

        // Fast-track to Climb phase
        _tracker.Reset();
        SustainPhase(now, 3, t => MakeSnapshot(t, onGround: true));
        SustainPhase(now.AddSeconds(4), 8, t =>
            MakeSnapshot(t, onGround: false, verticalSpeed: 500, altitude: 200));
        SustainPhase(now.AddSeconds(13), 8, t =>
            MakeSnapshot(t, onGround: false, verticalSpeed: 700, altitude: 2000));
        Assert.Equal(FlightPhase.Climb, _tracker.CurrentPhase);

        // Level off
        SustainPhase(now.AddSeconds(22), 8, t =>
            MakeSnapshot(t, onGround: false, verticalSpeed: 0, altitude: 8000));

        Assert.Equal(FlightPhase.Cruise, _tracker.CurrentPhase);
    }

    [Fact]
    public void Cruise_ToDescent_WhenDescending()
    {
        var now = DateTime.UtcNow;

        // Fast-track to Cruise
        SustainPhase(now, 3, t => MakeSnapshot(t, onGround: true));
        SustainPhase(now.AddSeconds(4), 8, t =>
            MakeSnapshot(t, onGround: false, verticalSpeed: 500, altitude: 200));
        SustainPhase(now.AddSeconds(13), 8, t =>
            MakeSnapshot(t, onGround: false, verticalSpeed: 700, altitude: 2000));
        SustainPhase(now.AddSeconds(22), 8, t =>
            MakeSnapshot(t, onGround: false, verticalSpeed: 0, altitude: 8000));
        Assert.Equal(FlightPhase.Cruise, _tracker.CurrentPhase);

        // Begin descent
        SustainPhase(now.AddSeconds(31), 8, t =>
            MakeSnapshot(t, onGround: false, verticalSpeed: -500, altitude: 6000));

        Assert.Equal(FlightPhase.Descent, _tracker.CurrentPhase);
    }

    [Fact]
    public void Descent_ToApproach_WhenLowAltitudeWithConfig()
    {
        var now = DateTime.UtcNow;

        // Fast-track to Descent
        SustainPhase(now, 3, t => MakeSnapshot(t, onGround: true));
        SustainPhase(now.AddSeconds(4), 8, t =>
            MakeSnapshot(t, onGround: false, verticalSpeed: 500, altitude: 200));
        SustainPhase(now.AddSeconds(13), 8, t =>
            MakeSnapshot(t, onGround: false, verticalSpeed: 700, altitude: 2000));
        SustainPhase(now.AddSeconds(22), 8, t =>
            MakeSnapshot(t, onGround: false, verticalSpeed: 0, altitude: 8000));
        SustainPhase(now.AddSeconds(31), 8, t =>
            MakeSnapshot(t, onGround: false, verticalSpeed: -500, altitude: 6000));
        Assert.Equal(FlightPhase.Descent, _tracker.CurrentPhase);

        // Low altitude, gear down, flaps deployed
        SustainPhase(now.AddSeconds(40), 8, t =>
            MakeSnapshot(t, onGround: false, verticalSpeed: -500, altitude: 2000,
                gearDown: 1.0, flaps: 30.0));

        Assert.Equal(FlightPhase.Approach, _tracker.CurrentPhase);
    }

    [Fact]
    public void Approach_ToLanding_WhenOnGround()
    {
        var now = DateTime.UtcNow;

        // Fast-track to Approach
        SustainPhase(now, 3, t => MakeSnapshot(t, onGround: true));
        SustainPhase(now.AddSeconds(4), 8, t =>
            MakeSnapshot(t, onGround: false, verticalSpeed: 500, altitude: 200));
        SustainPhase(now.AddSeconds(13), 8, t =>
            MakeSnapshot(t, onGround: false, verticalSpeed: 700, altitude: 2000));
        SustainPhase(now.AddSeconds(22), 8, t =>
            MakeSnapshot(t, onGround: false, verticalSpeed: 0, altitude: 8000));
        SustainPhase(now.AddSeconds(31), 8, t =>
            MakeSnapshot(t, onGround: false, verticalSpeed: -500, altitude: 6000));
        SustainPhase(now.AddSeconds(40), 8, t =>
            MakeSnapshot(t, onGround: false, verticalSpeed: -500, altitude: 2000,
                gearDown: 1.0, flaps: 30.0));
        Assert.Equal(FlightPhase.Approach, _tracker.CurrentPhase);

        // Touchdown
        SustainPhase(now.AddSeconds(49), 8, t =>
            MakeSnapshot(t, onGround: true, verticalSpeed: 0, altitude: 100));

        Assert.Equal(FlightPhase.Landing, _tracker.CurrentPhase);
    }

    [Fact]
    public void SustainedConditionRequired_NoInstantTransition()
    {
        var now = DateTime.UtcNow;
        var buffer = MakeBuffer();

        // Start on ground
        for (int i = 0; i < 5; i++)
        {
            var snap = MakeSnapshot(now.AddSeconds(i), onGround: true);
            buffer.Record(snap);
            _tracker.Update(snap, buffer);
        }
        Assert.Equal(FlightPhase.Ground, _tracker.CurrentPhase);

        // Single airborne snapshot should not trigger transition
        var airborneSnap = MakeSnapshot(now.AddSeconds(5), onGround: false, verticalSpeed: 500);
        buffer.Record(airborneSnap);
        _tracker.Update(airborneSnap, buffer);

        // Back to ground immediately
        var groundSnap = MakeSnapshot(now.AddSeconds(6), onGround: true);
        buffer.Record(groundSnap);
        _tracker.Update(groundSnap, buffer);

        Assert.Equal(FlightPhase.Ground, _tracker.CurrentPhase);
    }

    [Fact]
    public void PhaseChanged_EventFired()
    {
        var now = DateTime.UtcNow;
        FlightPhase? oldPhase = null;
        FlightPhase? newPhase = null;

        _tracker.OnPhaseChanged += (old, @new) =>
        {
            oldPhase = old;
            newPhase = @new;
        };

        SustainPhase(now, 3, t => MakeSnapshot(t, onGround: true));
        SustainPhase(now.AddSeconds(4), 8, t =>
            MakeSnapshot(t, onGround: false, verticalSpeed: 500, altitude: 200));

        Assert.Equal(FlightPhase.Ground, oldPhase);
        Assert.Equal(FlightPhase.Takeoff, newPhase);
    }

    [Fact]
    public void Reset_ReturnsToGround()
    {
        var now = DateTime.UtcNow;
        SustainPhase(now, 3, t => MakeSnapshot(t, onGround: true));
        SustainPhase(now.AddSeconds(4), 8, t =>
            MakeSnapshot(t, onGround: false, verticalSpeed: 500, altitude: 200));
        Assert.Equal(FlightPhase.Takeoff, _tracker.CurrentPhase);

        _tracker.Reset();
        Assert.Equal(FlightPhase.Ground, _tracker.CurrentPhase);
    }
}

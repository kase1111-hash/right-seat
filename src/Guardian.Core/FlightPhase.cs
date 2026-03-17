namespace Guardian.Core;

/// <summary>
/// The current phase of flight as determined by the state tracker.
/// Used by detection rules to contextualize their analysis.
/// </summary>
public enum FlightPhase
{
    /// <summary>On the ground, ground speed below 30 kts.</summary>
    Ground,

    /// <summary>Takeoff roll and initial climb until 200 ft AGL with positive climb.</summary>
    Takeoff,

    /// <summary>Airborne with sustained positive vertical speed > 200 fpm.</summary>
    Climb,

    /// <summary>Airborne with vertical speed within +/-200 fpm.</summary>
    Cruise,

    /// <summary>Airborne with sustained negative vertical speed < -200 fpm.</summary>
    Descent,

    /// <summary>Below 3000 ft AGL in descent with gear down or flaps extended.</summary>
    Approach,

    /// <summary>On the ground after approach phase, decelerating.</summary>
    Landing,
}

public static class FlightPhaseExtensions
{
    /// <summary>
    /// Returns true if this phase is a sterile cockpit phase where non-critical alerts are suppressed.
    /// </summary>
    public static bool IsSterile(this FlightPhase phase) =>
        phase is FlightPhase.Takeoff or FlightPhase.Approach or FlightPhase.Landing;
}

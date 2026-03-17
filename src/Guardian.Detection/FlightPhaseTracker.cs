using Guardian.Core;
using Serilog;

namespace Guardian.Detection;

/// <summary>
/// Determines the current flight phase from telemetry data using a state machine
/// with sustained-condition timers to prevent rapid oscillation.
/// </summary>
public sealed class FlightPhaseTracker
{
    private static readonly ILogger Log = Serilog.Log.ForContext<FlightPhaseTracker>();

    // Sustained-condition timer thresholds
    private static readonly TimeSpan SustainedDuration = TimeSpan.FromSeconds(5);

    // Thresholds
    private const double VerticalSpeedClimbThreshold = 200.0;   // fpm
    private const double VerticalSpeedDescentThreshold = -200.0; // fpm
    private const double ApproachAltitudeAgl = 3000.0;          // feet (approximated)
    private const double OnGroundThreshold = 0.5;                // SimOnGround bool

    private FlightPhase _currentPhase = FlightPhase.Ground;
    private FlightPhase _candidatePhase = FlightPhase.Ground;
    private DateTime _candidateStart = DateTime.MinValue;

    /// <summary>Current flight phase.</summary>
    public FlightPhase CurrentPhase => _currentPhase;

    /// <summary>Fired when the flight phase changes.</summary>
    public event Action<FlightPhase, FlightPhase>? OnPhaseChanged;

    /// <summary>
    /// Updates the flight phase based on the current telemetry snapshot and buffer.
    /// Should be called on each telemetry update (Group B frequency = 1 Hz).
    /// </summary>
    public void Update(TelemetrySnapshot snapshot, ITelemetryBuffer buffer)
    {
        var now = snapshot.Timestamp;
        var determined = DeterminePhase(snapshot);

        if (determined == _currentPhase)
        {
            // Already in this phase — reset candidate
            _candidatePhase = _currentPhase;
            _candidateStart = DateTime.MinValue;
            return;
        }

        if (determined != _candidatePhase)
        {
            // New candidate — start timer
            _candidatePhase = determined;
            _candidateStart = now;
            return;
        }

        // Same candidate sustained — check duration
        if (_candidateStart != DateTime.MinValue && (now - _candidateStart) >= SustainedDuration)
        {
            TransitionTo(determined);
        }
    }

    private FlightPhase DeterminePhase(TelemetrySnapshot snapshot)
    {
        var onGround = snapshot.Get(SimVarId.SimOnGround) ?? 1.0;
        var verticalSpeed = snapshot.Get(SimVarId.VerticalSpeed) ?? 0.0;
        var altitude = snapshot.Get(SimVarId.IndicatedAltitude) ?? 0.0;
        var gearDown = snapshot.Get(SimVarId.GearHandlePosition) ?? 1.0;
        var flaps = snapshot.Get(SimVarId.FlapsHandlePercent) ?? 0.0;

        // On ground?
        if (onGround > OnGroundThreshold)
        {
            return _currentPhase switch
            {
                FlightPhase.Approach or FlightPhase.Landing => FlightPhase.Landing,
                _ => FlightPhase.Ground,
            };
        }

        // Airborne — determine phase from vertical speed and configuration
        if (verticalSpeed > VerticalSpeedClimbThreshold)
        {
            // Could be takeoff (low altitude, recently on ground) or climb
            if (_currentPhase == FlightPhase.Ground || _currentPhase == FlightPhase.Takeoff)
                return FlightPhase.Takeoff;

            return FlightPhase.Climb;
        }

        if (verticalSpeed < VerticalSpeedDescentThreshold)
        {
            // Descending — check if approach configuration
            if (altitude < ApproachAltitudeAgl && (gearDown > 0.5 || flaps > 10.0))
                return FlightPhase.Approach;

            return FlightPhase.Descent;
        }

        // Level flight (vertical speed within +/- threshold)
        // If we were in takeoff/climb, transition to cruise
        // If we were in approach, stay in approach (leveled briefly on approach)
        if (_currentPhase == FlightPhase.Approach)
            return FlightPhase.Approach;

        return FlightPhase.Cruise;
    }

    private void TransitionTo(FlightPhase newPhase)
    {
        var old = _currentPhase;
        _currentPhase = newPhase;
        _candidatePhase = newPhase;
        _candidateStart = DateTime.MinValue;

        Log.Information("Flight phase: {Old} → {New}", old, newPhase);
        OnPhaseChanged?.Invoke(old, newPhase);
    }

    /// <summary>
    /// Resets the tracker to Ground phase. Used at session start.
    /// </summary>
    public void Reset()
    {
        _currentPhase = FlightPhase.Ground;
        _candidatePhase = FlightPhase.Ground;
        _candidateStart = DateTime.MinValue;
    }
}

namespace Guardian.Core;

/// <summary>
/// All monitored SimConnect variables, organized by polling group.
/// Groups determine polling frequency: A=4Hz, B=1Hz, C=0.25Hz, D=on-change.
/// </summary>
public enum SimVarId
{
    // ── Group A: High Frequency (4 Hz / 250ms) ──
    // Engine parameters where rapid change matters.

    GeneralEngRpm,                  // rpm, indexed per engine
    GeneralEngFuelFlow,             // gallons per hour, indexed per engine
    GeneralEngOilPressure,          // psi, indexed per engine
    GeneralEngOilTemperature,       // rankine, indexed per engine
    EngCylinderHeadTemperature,     // rankine, indexed per engine
    EngExhaustGasTemperature,       // rankine, indexed per engine
    RecipEngManifoldPressure,       // psi, indexed per engine
    TurbEngItt,                     // rankine, indexed per engine
    TurbEngN1,                      // percent, indexed per engine
    TurbEngN2,                      // percent, indexed per engine

    // ── Group B: Standard Frequency (1 Hz / 1000ms) ──
    // Flight state and configuration.

    FuelTankSelector,               // enum, indexed per engine
    FuelSystemTankQuantity,         // gallons, indexed per tank
    FuelTotalQuantity,              // gallons
    IndicatedAltitude,              // feet
    AirspeedIndicated,              // knots
    VerticalSpeed,                  // feet per minute
    PlanePitchDegrees,              // degrees
    PlaneBankDegrees,               // degrees
    HeadingIndicator,               // degrees
    ElevatorTrimPosition,           // radians
    AileronTrimPct,                 // percent
    RudderTrimPct,                  // percent
    ThrottleLeverPosition,          // percent, indexed per engine
    MixtureLeverPosition,           // percent, indexed per engine
    PropLeverPosition,              // percent, indexed per engine
    FlapsHandlePercent,             // percent
    GearHandlePosition,             // bool (0/1)

    // ── Group C: Low Frequency (0.25 Hz / 4000ms) ──
    // Environmental and system data.

    AmbientTemperature,             // celsius
    AmbientWindVelocity,            // knots
    AmbientWindDirection,           // degrees
    AmbientPressure,                // inHg
    AmbientPrecipState,             // enum
    AmbientInCloud,                 // bool (0/1)
    StructuralIcePct,               // percent
    PitotIcePct,                    // percent
    ElectricalMainBusVoltage,       // volts
    ElectricalBatteryBusVoltage,    // volts
    SuctionPressure,                // inHg
    SimOnGround,                    // bool (0/1)
    NumberOfEngines,                // number (read once)
    EngineType,                     // enum (read once)

    // ── Group D: On Change Only ──
    // Discrete state changes triggering immediate re-evaluation.

    GeneralEngCombustion,           // bool, indexed per engine
    AutopilotMaster,                // bool
}

/// <summary>
/// SimVar polling groups controlling update frequency.
/// </summary>
public enum PollingGroup
{
    /// <summary>4 Hz (250ms) — critical engine parameters.</summary>
    GroupA,
    /// <summary>1 Hz (1000ms) — flight state and configuration.</summary>
    GroupB,
    /// <summary>0.25 Hz (4000ms) — environmental and system data.</summary>
    GroupC,
    /// <summary>On change only — discrete state transitions.</summary>
    GroupD,
}

/// <summary>
/// Metadata for a SimVar: its polling group, native unit, and whether it's indexed per engine/tank.
/// </summary>
public static class SimVarMetadata
{
    public static PollingGroup GetGroup(SimVarId id) => id switch
    {
        >= SimVarId.GeneralEngRpm and <= SimVarId.TurbEngN2 => PollingGroup.GroupA,
        >= SimVarId.FuelTankSelector and <= SimVarId.GearHandlePosition => PollingGroup.GroupB,
        >= SimVarId.AmbientTemperature and <= SimVarId.EngineType => PollingGroup.GroupC,
        >= SimVarId.GeneralEngCombustion and <= SimVarId.AutopilotMaster => PollingGroup.GroupD,
        _ => PollingGroup.GroupC,
    };

    /// <summary>
    /// Returns true if this SimVar is indexed per engine or per tank.
    /// </summary>
    public static bool IsIndexed(SimVarId id) => id switch
    {
        SimVarId.GeneralEngRpm => true,
        SimVarId.GeneralEngFuelFlow => true,
        SimVarId.GeneralEngOilPressure => true,
        SimVarId.GeneralEngOilTemperature => true,
        SimVarId.EngCylinderHeadTemperature => true,
        SimVarId.EngExhaustGasTemperature => true,
        SimVarId.RecipEngManifoldPressure => true,
        SimVarId.TurbEngItt => true,
        SimVarId.TurbEngN1 => true,
        SimVarId.TurbEngN2 => true,
        SimVarId.FuelTankSelector => true,
        SimVarId.FuelSystemTankQuantity => true,
        SimVarId.ThrottleLeverPosition => true,
        SimVarId.MixtureLeverPosition => true,
        SimVarId.PropLeverPosition => true,
        SimVarId.GeneralEngCombustion => true,
        _ => false,
    };

    /// <summary>
    /// The SimConnect variable name string used for data definition registration.
    /// </summary>
    public static string GetSimConnectName(SimVarId id) => id switch
    {
        SimVarId.GeneralEngRpm => "GENERAL ENG RPM",
        SimVarId.GeneralEngFuelFlow => "GENERAL ENG FUEL FLOW",
        SimVarId.GeneralEngOilPressure => "GENERAL ENG OIL PRESSURE",
        SimVarId.GeneralEngOilTemperature => "GENERAL ENG OIL TEMPERATURE",
        SimVarId.EngCylinderHeadTemperature => "ENG CYLINDER HEAD TEMPERATURE",
        SimVarId.EngExhaustGasTemperature => "ENG EXHAUST GAS TEMPERATURE",
        SimVarId.RecipEngManifoldPressure => "RECIP ENG MANIFOLD PRESSURE",
        SimVarId.TurbEngItt => "TURB ENG ITT",
        SimVarId.TurbEngN1 => "TURB ENG N1",
        SimVarId.TurbEngN2 => "TURB ENG N2",
        SimVarId.FuelTankSelector => "FUEL TANK SELECTOR",
        SimVarId.FuelSystemTankQuantity => "FUELSYSTEM TANK QUANTITY",
        SimVarId.FuelTotalQuantity => "FUEL TOTAL QUANTITY",
        SimVarId.IndicatedAltitude => "INDICATED ALTITUDE",
        SimVarId.AirspeedIndicated => "AIRSPEED INDICATED",
        SimVarId.VerticalSpeed => "VERTICAL SPEED",
        SimVarId.PlanePitchDegrees => "PLANE PITCH DEGREES",
        SimVarId.PlaneBankDegrees => "PLANE BANK DEGREES",
        SimVarId.HeadingIndicator => "HEADING INDICATOR",
        SimVarId.ElevatorTrimPosition => "ELEVATOR TRIM POSITION",
        SimVarId.AileronTrimPct => "AILERON TRIM PCT",
        SimVarId.RudderTrimPct => "RUDDER TRIM PCT",
        SimVarId.ThrottleLeverPosition => "THROTTLE LEVER POSITION",
        SimVarId.MixtureLeverPosition => "MIXTURE LEVER POSITION",
        SimVarId.PropLeverPosition => "PROP LEVER POSITION",
        SimVarId.FlapsHandlePercent => "FLAPS HANDLE PERCENT",
        SimVarId.GearHandlePosition => "GEAR HANDLE POSITION",
        SimVarId.AmbientTemperature => "AMBIENT TEMPERATURE",
        SimVarId.AmbientWindVelocity => "AMBIENT WIND VELOCITY",
        SimVarId.AmbientWindDirection => "AMBIENT WIND DIRECTION",
        SimVarId.AmbientPressure => "AMBIENT PRESSURE",
        SimVarId.AmbientPrecipState => "AMBIENT PRECIP STATE",
        SimVarId.AmbientInCloud => "AMBIENT IN CLOUD",
        SimVarId.StructuralIcePct => "STRUCTURAL ICE PCT",
        SimVarId.PitotIcePct => "PITOT ICE PCT",
        SimVarId.ElectricalMainBusVoltage => "ELECTRICAL MAIN BUS VOLTAGE",
        SimVarId.ElectricalBatteryBusVoltage => "ELECTRICAL BATTERY BUS VOLTAGE",
        SimVarId.SuctionPressure => "SUCTION PRESSURE",
        SimVarId.SimOnGround => "SIM ON GROUND",
        SimVarId.NumberOfEngines => "NUMBER OF ENGINES",
        SimVarId.EngineType => "ENGINE TYPE",
        SimVarId.GeneralEngCombustion => "GENERAL ENG COMBUSTION",
        SimVarId.AutopilotMaster => "AUTOPILOT MASTER",
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown SimVarId"),
    };

    /// <summary>
    /// The SimConnect unit string for data definition registration.
    /// </summary>
    public static string GetSimConnectUnit(SimVarId id) => id switch
    {
        SimVarId.GeneralEngRpm => "rpm",
        SimVarId.GeneralEngFuelFlow => "gallons per hour",
        SimVarId.GeneralEngOilPressure => "psi",
        SimVarId.GeneralEngOilTemperature => "rankine",
        SimVarId.EngCylinderHeadTemperature => "rankine",
        SimVarId.EngExhaustGasTemperature => "rankine",
        SimVarId.RecipEngManifoldPressure => "psi",
        SimVarId.TurbEngItt => "rankine",
        SimVarId.TurbEngN1 => "percent",
        SimVarId.TurbEngN2 => "percent",
        SimVarId.FuelTankSelector => "enum",
        SimVarId.FuelSystemTankQuantity => "gallons",
        SimVarId.FuelTotalQuantity => "gallons",
        SimVarId.IndicatedAltitude => "feet",
        SimVarId.AirspeedIndicated => "knots",
        SimVarId.VerticalSpeed => "feet per minute",
        SimVarId.PlanePitchDegrees => "degrees",
        SimVarId.PlaneBankDegrees => "degrees",
        SimVarId.HeadingIndicator => "degrees",
        SimVarId.ElevatorTrimPosition => "radians",
        SimVarId.AileronTrimPct => "percent",
        SimVarId.RudderTrimPct => "percent",
        SimVarId.ThrottleLeverPosition => "percent",
        SimVarId.MixtureLeverPosition => "percent",
        SimVarId.PropLeverPosition => "percent",
        SimVarId.FlapsHandlePercent => "percent",
        SimVarId.GearHandlePosition => "bool",
        SimVarId.AmbientTemperature => "celsius",
        SimVarId.AmbientWindVelocity => "knots",
        SimVarId.AmbientWindDirection => "degrees",
        SimVarId.AmbientPressure => "inHg",
        SimVarId.AmbientPrecipState => "enum",
        SimVarId.AmbientInCloud => "bool",
        SimVarId.StructuralIcePct => "percent",
        SimVarId.PitotIcePct => "percent",
        SimVarId.ElectricalMainBusVoltage => "volts",
        SimVarId.ElectricalBatteryBusVoltage => "volts",
        SimVarId.SuctionPressure => "inHg",
        SimVarId.SimOnGround => "bool",
        SimVarId.NumberOfEngines => "number",
        SimVarId.EngineType => "enum",
        SimVarId.GeneralEngCombustion => "bool",
        SimVarId.AutopilotMaster => "bool",
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown SimVarId"),
    };
}

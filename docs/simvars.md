# SimVar Reference

Flight Guardian monitors SimConnect variables organized into four polling groups by update frequency.

## Group A — High Frequency (4 Hz / 250ms)

Critical engine parameters where rapid change detection matters.

| SimVarId | SimConnect Name | Unit | Indexed | Used By |
|----------|----------------|------|---------|---------|
| GeneralEngRpm | GENERAL ENG RPM | rpm | per engine | R002, R004 |
| GeneralEngFuelFlow | GENERAL ENG FUEL FLOW | gallons/hour | per engine | R001 |
| GeneralEngOilPressure | GENERAL ENG OIL PRESSURE | psi | per engine | R004 |
| GeneralEngOilTemperature | GENERAL ENG OIL TEMPERATURE | rankine | per engine | R004 |
| EngCylinderHeadTemperature | ENG CYLINDER HEAD TEMPERATURE | rankine | per engine | R003 |
| EngExhaustGasTemperature | ENG EXHAUST GAS TEMPERATURE | rankine | per engine | R003 |
| RecipEngManifoldPressure | RECIP ENG MANIFOLD PRESSURE | psi | per engine | Telemetry display |
| TurbEngItt | TURB ENG ITT | rankine | per engine | R003 (turboprop) |
| TurbEngN1 | TURB ENG N1 | percent | per engine | Telemetry display |
| TurbEngN2 | TURB ENG N2 | percent | per engine | Telemetry display |

## Group B — Standard Frequency (1 Hz / 1000ms)

Flight state and configuration variables.

| SimVarId | SimConnect Name | Unit | Indexed | Used By |
|----------|----------------|------|---------|---------|
| FuelTankSelector | FUEL TANK SELECTOR | enum | per engine | R001 |
| FuelSystemTankQuantity | FUELSYSTEM TANK QUANTITY | gallons | per tank | R001, R005 |
| FuelTotalQuantity | FUEL TOTAL QUANTITY | gallons | no | R001 |
| IndicatedAltitude | INDICATED ALTITUDE | feet | no | Phase tracker |
| AirspeedIndicated | AIRSPEED INDICATED | knots | no | Phase tracker |
| VerticalSpeed | VERTICAL SPEED | feet/min | no | Phase tracker |
| PlanePitchDegrees | PLANE PITCH DEGREES | degrees | no | Phase tracker |
| PlaneBankDegrees | PLANE BANK DEGREES | degrees | no | Telemetry display |
| HeadingIndicator | HEADING INDICATOR | degrees | no | Telemetry display |
| ElevatorTrimPosition | ELEVATOR TRIM POSITION | radians | no | Telemetry display |
| AileronTrimPct | AILERON TRIM PCT | percent | no | R002 |
| RudderTrimPct | RUDDER TRIM PCT | percent | no | R002 |
| ThrottleLeverPosition | THROTTLE LEVER POSITION | percent | per engine | R002 |
| MixtureLeverPosition | MIXTURE LEVER POSITION | percent | per engine | Telemetry display |
| PropLeverPosition | PROP LEVER POSITION | percent | per engine | Telemetry display |
| FlapsHandlePercent | FLAPS HANDLE PERCENT | percent | no | Phase tracker |
| GearHandlePosition | GEAR HANDLE POSITION | bool (0/1) | no | Phase tracker |

## Group C — Low Frequency (0.25 Hz / 4000ms)

Environmental and system data that changes slowly.

| SimVarId | SimConnect Name | Unit | Indexed | Used By |
|----------|----------------|------|---------|---------|
| AmbientTemperature | AMBIENT TEMPERATURE | celsius | no | R006 |
| AmbientWindVelocity | AMBIENT WIND VELOCITY | knots | no | Telemetry display |
| AmbientWindDirection | AMBIENT WIND DIRECTION | degrees | no | Telemetry display |
| AmbientPressure | AMBIENT PRESSURE | inHg | no | Telemetry display |
| AmbientPrecipState | AMBIENT PRECIP STATE | enum | no | R006, R008 |
| AmbientInCloud | AMBIENT IN CLOUD | bool (0/1) | no | R006, R008 |
| StructuralIcePct | STRUCTURAL ICE PCT | percent | no | R006 |
| PitotIcePct | PITOT ICE PCT | percent | no | R006 |
| ElectricalMainBusVoltage | ELECTRICAL MAIN BUS VOLTAGE | volts | no | R007 |
| ElectricalBatteryBusVoltage | ELECTRICAL BATTERY BUS VOLTAGE | volts | no | R007 |
| SuctionPressure | SUCTION PRESSURE | inHg | no | R008 |
| SimOnGround | SIM ON GROUND | bool (0/1) | no | Phase tracker |
| NumberOfEngines | NUMBER OF ENGINES | number | no | Profile matching |
| EngineType | ENGINE TYPE | enum | no | Profile matching |

## Group D — On Change Only

Discrete state changes that trigger immediate re-evaluation.

| SimVarId | SimConnect Name | Unit | Indexed | Used By |
|----------|----------------|------|---------|---------|
| GeneralEngCombustion | GENERAL ENG COMBUSTION | bool | per engine | R001-R004, Phase tracker |
| AutopilotMaster | AUTOPILOT MASTER | bool | no | Telemetry display |

## Unit Conversions

SimConnect returns some values in native units that differ from pilot-familiar units:

| Native (SimConnect) | Display (Pilot) | Conversion |
|--------------------|--------------------|------------|
| Rankine | Fahrenheit | F = R - 459.67 |
| Radians | Degrees | deg = rad * (180/pi) |
| Feet per minute | Feet per minute | (no conversion) |
| PSI (manifold) | inHg | inHg = PSI * 2.036 |

Aircraft profiles store thresholds in pilot-friendly units (Fahrenheit, degrees). These are converted to native SimConnect units on load.

## Indexed Variables

Indexed variables have per-engine or per-tank instances. The `TelemetrySnapshot` stores these with a `(SimVarId, int index)` key. Index 0 corresponds to the first engine/tank, index 1 to the second, etc.

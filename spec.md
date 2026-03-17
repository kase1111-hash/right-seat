# Flight Guardian — Technical Specification

Version: 0.1.0-draft
Status: Pre-implementation design document

---

## 1. System Overview

Flight Guardian is a passive aircraft monitoring system that reads real-time telemetry from Microsoft Flight Simulator 2024 via the SimConnect API, analyzes it for anomalies and developing problems, and delivers prioritized alerts to the pilot through an in-cockpit EFB tablet app and/or an external companion window.

The system is strictly read-only. It never writes SimVars, never sends key events, and never modifies any simulation state. It is an observer and advisor.

### 1.1 Design Constraints

- **Advisory only**: No control authority. No SimVar writes. No autopilot interaction.
- **Non-blocking**: Detection engine must never stall the SimConnect polling loop.
- **Graceful degradation**: If any SimVar is unavailable for a given aircraft, detection rules that depend on it are silently disabled — they do not error or produce false alerts.
- **Configurable sensitivity**: Every detection threshold is exposed in configuration. Pilots can tune aggressiveness to their preference and experience level.
- **Profile-driven**: Normal operating ranges are defined per aircraft type, not hardcoded. Adding a new aircraft means adding a JSON file, not changing code.

### 1.2 Key Abstractions

| Concept | Definition |
|---------|-----------|
| **SimVar Stream** | The continuous flow of polled simulation variables from SimConnect, timestamped and buffered |
| **Aircraft Profile** | A JSON document defining normal operating ranges for a specific airframe type |
| **Flight Phase** | The current phase of flight (ground, takeoff, climb, cruise, descent, approach, landing) as determined by the state tracker |
| **Detection Rule** | A named, self-contained analysis unit that evaluates the current state and history and may emit an alert |
| **Alert** | A structured message with severity, text, rule ID, and associated telemetry snapshot |
| **Priority Queue** | The ordered list of pending alerts awaiting delivery, filtered by sterile cockpit mode |

---

## 2. SimConnect Integration

### 2.1 Connection Model

The application runs as an out-of-process SimConnect client (a standalone .exe), connecting to the running MSFS instance. This is the MSFS SDK recommended approach: out-of-process clients are more stable, don't crash the sim on failure, and support managed (.NET) code.

Connection lifecycle:
1. Attempt connection on startup with 5-second retry interval
2. On connection, register data definitions for all monitored SimVars
3. Begin polling loop at configured frequency
4. On disconnect, enter reconnection loop with backoff
5. All detection state is preserved across reconnections; the system resumes monitoring seamlessly

### 2.2 SimVar Watch List

SimVars are organized into groups by polling frequency. Critical engine parameters poll faster than environmental data.

#### Group A — High Frequency (4 Hz / 250ms)

These are the parameters where rapid change matters — engine failures develop in seconds.

| SimVar Name | Units | Index | Purpose |
|-------------|-------|-------|---------|
| GENERAL ENG RPM | rpm | 1–4 | Engine speed per engine |
| GENERAL ENG FUEL FLOW | gallons per hour | 1–4 | Fuel consumption rate per engine |
| GENERAL ENG OIL PRESSURE | psi | 1–4 | Oil pressure per engine |
| GENERAL ENG OIL TEMPERATURE | rankine | 1–4 | Oil temperature per engine |
| ENG CYLINDER HEAD TEMPERATURE | rankine | 1–4 | CHT per engine (recip) |
| ENG EXHAUST GAS TEMPERATURE | rankine | 1–4 | EGT per engine |
| RECIP ENG MANIFOLD PRESSURE | psi | 1–4 | Manifold pressure (recip) |
| TURB ENG ITT | rankine | 1–4 | Interstage turbine temp (turbine) |
| TURB ENG N1 | percent | 1–4 | N1 spool speed (turbine) |
| TURB ENG N2 | percent | 1–4 | N2 spool speed (turbine) |

#### Group B — Standard Frequency (1 Hz / 1000ms)

Flight state and configuration parameters that change at human timescales.

| SimVar Name | Units | Index | Purpose |
|-------------|-------|-------|---------|
| FUEL TANK SELECTOR | enum | 1–4 | Fuel selector position per engine |
| FUELSYSTEM TANK QUANTITY | gallons | 1–N | Fuel quantity per tank |
| FUEL TOTAL QUANTITY | gallons | — | Total fuel on board |
| INDICATED ALTITUDE | feet | — | Current altitude |
| AIRSPEED INDICATED | knots | — | Indicated airspeed |
| VERTICAL SPEED | feet per minute | — | Rate of climb/descent |
| PLANE PITCH DEGREES | degrees | — | Pitch attitude |
| PLANE BANK DEGREES | degrees | — | Bank angle |
| HEADING INDICATOR | degrees | — | Current heading |
| ELEVATOR TRIM POSITION | radians | — | Elevator trim deflection |
| AILERON TRIM PCT | percent | — | Aileron trim percent |
| RUDDER TRIM PCT | percent | — | Rudder trim percent |
| THROTTLE LEVER POSITION | percent | 1–4 | Throttle position per engine |
| MIXTURE LEVER POSITION | percent | 1–4 | Mixture position per engine |
| PROP LEVER POSITION | percent | 1–4 | Prop RPM lever per engine |
| FLAPS HANDLE PERCENT | percent | — | Flap extension |
| GEAR HANDLE POSITION | bool | — | Gear up/down command |

#### Group C — Low Frequency (0.25 Hz / 4000ms)

Environmental and system data that changes slowly.

| SimVar Name | Units | Index | Purpose |
|-------------|-------|-------|---------|
| AMBIENT TEMPERATURE | celsius | — | Outside air temperature |
| AMBIENT WIND VELOCITY | knots | — | Wind speed |
| AMBIENT WIND DIRECTION | degrees | — | Wind direction |
| AMBIENT PRESSURE | inHg | — | Barometric pressure |
| AMBIENT PRECIP STATE | enum | — | Precipitation type |
| AMBIENT IN CLOUD | bool | — | In visible moisture |
| STRUCTURAL ICE PCT | percent | — | Structural ice accumulation |
| PITOT ICE PCT | percent | — | Pitot ice accumulation |
| ELECTRICAL MAIN BUS VOLTAGE | volts | — | Main bus voltage |
| ELECTRICAL BATTERY BUS VOLTAGE | volts | — | Battery bus voltage |
| SUCTION PRESSURE | inHg | — | Vacuum system pressure |
| SIM ON GROUND | bool | — | Weight on wheels |
| NUMBER OF ENGINES | number | — | Engine count (read once) |
| ENGINE TYPE | enum | — | Piston/turboprop/jet (read once) |

#### Group D — On Change Only

Discrete state changes that should trigger immediate rule re-evaluation.

| SimVar Name | Purpose |
|-------------|---------|
| FUEL TANK SELECTOR (any) | Fuel configuration changed — re-evaluate R001 immediately |
| GENERAL ENG COMBUSTION (any) | Engine start/stop — update state model |
| GEAR HANDLE POSITION | Gear state change — update flight phase model |
| FLAPS HANDLE PERCENT | Flap change — update flight phase model |
| AUTOPILOT MASTER | AP engagement — update state model |

### 2.3 Data Buffer

All polled SimVars are stored in a ring buffer with configurable depth. Default: 10 minutes of history at native polling rate per group. The buffer supports:

- **Snapshot**: Current values of all monitored SimVars
- **Window query**: All values of a specific SimVar within a time window (e.g., last 60 seconds of CHT for engine 1)
- **Rate of change**: Computed derivative over a configurable window (default 30 seconds)
- **Delta**: Change from a reference point (e.g., value at start of current flight phase)

The buffer is the primary data structure consumed by the detection engine. Rules never poll SimConnect directly.

---

## 3. Aircraft Profiles

Each supported aircraft type has a JSON profile defining its normal operating envelope. Rules reference these values instead of hardcoding thresholds.

### 3.1 Profile Schema

```json
{
  "aircraft_id": "c172sp",
  "display_name": "Cessna 172SP Skyhawk",
  "engine_type": "piston",
  "engine_count": 1,
  "fuel": {
    "tank_count": 2,
    "tank_names": ["left", "right"],
    "total_capacity_gal": 56,
    "usable_capacity_gal": 53,
    "selector_positions": ["OFF", "LEFT", "RIGHT", "BOTH"],
    "normal_cruise_selector": "BOTH",
    "imbalance_advisory_pct": 10,
    "imbalance_warning_pct": 20,
    "minimum_fuel_warning_gal": 8
  },
  "engine": {
    "rpm_range": [600, 2700],
    "rpm_cruise_typical": [2200, 2500],
    "manifold_pressure_range_inhg": [12, 30],
    "cht_normal_range_f": [200, 450],
    "cht_redline_f": 500,
    "cht_trend_advisory_f_per_min": 5,
    "cht_trend_warning_f_per_min": 10,
    "egt_normal_range_f": [1100, 1500],
    "egt_redline_f": 1600,
    "oil_pressure_normal_psi": [60, 90],
    "oil_pressure_minimum_psi": 25,
    "oil_pressure_redline_psi": 115,
    "oil_pressure_drop_rate_warning_psi_per_min": 10,
    "oil_temp_normal_range_f": [100, 245],
    "oil_temp_redline_f": 250,
    "fuel_flow_cruise_gph": [8, 12]
  },
  "electrical": {
    "main_bus_normal_v": [13.5, 14.5],
    "main_bus_minimum_v": 12.0,
    "battery_bus_minimum_v": 11.0
  },
  "vacuum": {
    "suction_normal_inhg": [4.5, 5.5],
    "suction_minimum_inhg": 3.5
  },
  "performance": {
    "vne_kias": 163,
    "vno_kias": 129,
    "vs0_kias": 40,
    "vs1_kias": 48,
    "vfe_kias": 110,
    "normal_approach_kias": [65, 85],
    "normal_climb_fpm": [500, 800]
  },
  "trim": {
    "asymmetric_threshold_pct": 15
  },
  "icing": {
    "has_anti_ice": false,
    "has_pitot_heat": true,
    "icing_oat_range_c": [-20, 2]
  }
}
```

### 3.2 Profile Matching

On connection, the system reads the aircraft title from MSFS and attempts to match it to a profile. Matching order:

1. Exact match on MSFS `TITLE` SimVar
2. Partial match (normalized string comparison)
3. Fallback to a `generic_single_piston.json` or `generic_twin_piston.json` based on engine count and type
4. If no match at all, run in limited mode with only rules that don't require profile thresholds (e.g., cross-correlation rules still work because they compare engines against each other, not against absolute values)

---

## 4. Flight Phase Detection

The state tracker maintains a flight phase model derived from SimVar data. Detection rules use flight phase to contextualize their analysis.

### 4.1 Phase Definitions

| Phase | Entry Conditions | Exit Conditions |
|-------|-----------------|-----------------|
| **GROUND** | SIM_ON_GROUND = true AND ground speed < 30 kts | Ground speed > 30 kts with takeoff power set |
| **TAKEOFF** | Ground speed > 30 kts AND throttle > 90% AND SIM_ON_GROUND | SIM_ON_GROUND = false AND altitude > field elevation + 200 ft AND positive climb sustained 5 sec |
| **CLIMB** | Airborne AND vertical speed > +200 fpm sustained 15 sec | Vertical speed within ±200 fpm sustained 30 sec OR descent detected |
| **CRUISE** | Airborne AND vertical speed within ±200 fpm sustained 30 sec | Vertical speed < -200 fpm sustained 15 sec OR vertical speed > +200 fpm sustained 15 sec |
| **DESCENT** | Airborne AND vertical speed < -200 fpm sustained 15 sec | Vertical speed within ±200 fpm sustained 15 sec OR approach conditions met |
| **APPROACH** | Descent AND altitude < 3000 ft AGL AND gear down OR flaps > 10° | SIM_ON_GROUND = true |
| **LANDING** | SIM_ON_GROUND = true AND was in APPROACH | Ground speed < 30 kts |

### 4.2 Sterile Cockpit Mode

During TAKEOFF, APPROACH, and LANDING phases, non-critical alerts are suppressed. Only CRITICAL severity alerts are delivered during sterile phases. All suppressed alerts are queued and delivered when the phase transitions to a non-sterile phase (CLIMB, CRUISE, DESCENT), unless they have been superseded or resolved.

Sterile mode can also be manually toggled by the pilot via the EFB app or a configurable key binding.

---

## 5. Detection Rules

### 5.1 Rule Architecture

Each rule is a self-contained class implementing the `IDetectionRule` interface:

```
interface IDetectionRule {
    string RuleId { get; }             // e.g., "R001"
    string Name { get; }               // Human-readable name
    string Description { get; }        // What this rule detects
    TimeSpan EvaluationInterval { get; } // How often to run
    bool IsApplicable(AircraftProfile profile, FlightPhase phase);
    Alert? Evaluate(TelemetrySnapshot current, TelemetryBuffer history, AircraftProfile profile, FlightPhase phase);
}
```

Rules are registered at startup and evaluated by the detection engine on their configured interval. A rule returns null if no anomaly is detected, or an Alert if one is.

### 5.2 Alert Severity Levels

| Level | Meaning | Delivery Behavior |
|-------|---------|-------------------|
| **CRITICAL** | Immediate threat to flight safety | Delivered immediately, even during sterile cockpit. Accompanied by audio tone. Repeats at 30-second intervals until acknowledged or condition resolves. |
| **WARNING** | Developing problem requiring pilot attention | Delivered at next appropriate moment. During sterile cockpit, queued. Accompanied by single audio chime. |
| **ADVISORY** | Condition worth noting, no immediate action needed | Queued for delivery during low-workload periods. No audio. |
| **INFO** | Informational, logged but not actively presented | Written to log. Visible in alert history on request. Never interrupts. |

### 5.3 Alert Escalation

Rules may escalate severity over time. Escalation is defined in the rule logic:

- Time-based: "If condition persists for X minutes, escalate from ADVISORY to WARNING"
- Value-based: "If parameter exceeds Y threshold, escalate from WARNING to CRITICAL"
- Compound: "If this condition AND that condition, escalate"

### 5.4 Alert Cooldown and Deduplication

Once an alert is delivered for a specific rule, that rule enters a cooldown period (configurable per rule, default 60 seconds for WARNING, 30 seconds for CRITICAL). During cooldown, the same rule will not produce a duplicate alert unless the severity escalates.

If the underlying condition resolves during cooldown, the rule emits an INFO-level "resolved" message.

### 5.5 Rule Definitions

---

#### R001 — Fuel Cross-Feed Mismatch

**What it detects**: Both engines drawing fuel from the same tank due to fuel selector misconfiguration, leaving one or more tanks unused while fuel depletes.

**Applicable**: Multi-engine aircraft only (engine_count > 1)

**Monitored SimVars**:
- FUEL TANK SELECTOR (per engine)
- GENERAL ENG FUEL FLOW (per engine)
- FUELSYSTEM TANK QUANTITY (per tank)

**Logic**:
1. Read fuel selector position for each engine
2. Determine which physical tank each engine is drawing from (cross-feed means drawing from opposite tank)
3. If two or more engines are drawing from the same tank AND another tank with fuel is being ignored:
   - Calculate time-to-exhaustion of the shared tank at current combined burn rate
   - If time-to-exhaustion > 30 min: emit WARNING
   - If time-to-exhaustion ≤ 30 min: emit CRITICAL
   - If time-to-exhaustion ≤ 15 min: emit CRITICAL with "FUEL STARVATION IMMINENT" text
4. Cross-reference: if fuel flow is detected on an engine whose selector is OFF, emit CRITICAL (sensor/selector disagreement)

**Alert text examples**:
- WARNING: "Both engines drawing from left tank. Right tank has 18 gal unused. Estimated 47 min at current burn rate."
- CRITICAL: "Both engines drawing from left tank — 22 min to exhaustion. Right tank has 18 gal. Consider fuel selector adjustment."

**Escalation**: WARNING → CRITICAL as time-to-exhaustion decreases below 30-minute threshold.

**Cooldown**: 120 sec for WARNING, 30 sec for CRITICAL.

---

#### R002 — Asymmetric Power/Trim Disagreement

**What it detects**: Aircraft trimmed for asymmetric conditions without a corresponding power differential between engines, or significant power differential without appropriate trim. Both indicate a configuration the pilot may not be aware of.

**Applicable**: Multi-engine aircraft only

**Monitored SimVars**:
- GENERAL ENG RPM (per engine)
- RECIP ENG MANIFOLD PRESSURE (per engine) or TURB ENG N1 (per engine)
- RUDDER TRIM PCT
- AILERON TRIM PCT
- ELEVATOR TRIM POSITION

**Logic**:
1. Compute power differential between engines (RPM delta, MP delta, or N1 delta as applicable)
2. Read trim offsets (rudder, aileron)
3. If trim offset exceeds `profile.trim.asymmetric_threshold_pct` AND power differential is below 5%:
   - Emit ADVISORY: "Rudder trim offset X% with symmetric power. Verify configuration."
   - If persists > 2 min: escalate to WARNING
4. If power differential exceeds 10% AND trim is centered (within 5%):
   - Emit ADVISORY: "Engine power differential detected (L: X RPM, R: Y RPM) with neutral trim."
   - If persists > 1 min: escalate to WARNING

**Alert text example**:
- WARNING: "Rudder trim 18% left with engines matched at 2,350 RPM. Trim may not reflect current configuration."

**Cooldown**: 120 sec.

---

#### R003 — Engine Temperature Trend

**What it detects**: Cylinder head temperature or exhaust gas temperature climbing at an abnormal rate, or approaching redline values.

**Applicable**: All aircraft with engine temperature instrumentation

**Monitored SimVars**:
- ENG CYLINDER HEAD TEMPERATURE (per engine)
- ENG EXHAUST GAS TEMPERATURE (per engine)
- TURB ENG ITT (per engine, turbine aircraft)

**Logic**:
1. Compute rate of change over a 60-second sliding window
2. Compare against profile thresholds:
   - If CHT rate > `cht_trend_advisory_f_per_min`: emit ADVISORY
   - If CHT rate > `cht_trend_warning_f_per_min`: emit WARNING
3. Compare absolute values against profile limits:
   - If CHT > 90% of `cht_redline_f`: emit WARNING with value and trend direction
   - If CHT > 95% of `cht_redline_f`: emit CRITICAL
4. Same logic applies to EGT and ITT with their respective profile values
5. Context-aware: higher rates are expected during climb. The state tracker provides flight phase — rate thresholds are relaxed by 50% during CLIMB phase.

**Alert text example**:
- ADVISORY: "Engine 1 CHT climbing 6°F/min for last 3 minutes. Currently 412°F."
- CRITICAL: "Engine 1 CHT at 478°F (95% of redline). Rate: +8°F/min."

**Escalation**: ADVISORY → WARNING → CRITICAL as values approach redline.

**Cooldown**: 60 sec for ADVISORY, 30 sec for WARNING/CRITICAL.

---

#### R004 — Oil Pressure Anomaly

**What it detects**: Oil pressure dropping below safe operating range, dropping rapidly, or diverging from oil temperature (temperature rising while pressure falls indicates potential lubrication failure).

**Applicable**: All aircraft with oil instrumentation

**Monitored SimVars**:
- GENERAL ENG OIL PRESSURE (per engine)
- GENERAL ENG OIL TEMPERATURE (per engine)
- GENERAL ENG RPM (per engine)

**Logic**:
1. Check absolute oil pressure against profile minimum:
   - Below `oil_pressure_minimum_psi`: emit CRITICAL
2. Check rate of change:
   - Dropping faster than `oil_pressure_drop_rate_warning_psi_per_min`: emit WARNING
3. Check temperature-pressure correlation:
   - If oil temp is rising AND oil pressure is falling simultaneously over 60-second window: emit WARNING ("Oil temperature/pressure divergence on engine X — possible lubrication issue")
4. Context: Low oil pressure is normal at idle RPM. Suppress if RPM < 800 AND SIM_ON_GROUND = true.

**Alert text example**:
- CRITICAL: "Engine 2 oil pressure 22 PSI (minimum 25). Dropping 12 PSI/min."
- WARNING: "Engine 1 oil temp rising (+8°F/min) while oil pressure falling (-4 PSI/min). Monitor closely."

**Escalation**: WARNING → CRITICAL on continued divergence or breach of minimum.

**Cooldown**: 30 sec for WARNING/CRITICAL.

---

#### R005 — Fuel Imbalance

**What it detects**: Fuel quantities in left and right tanks diverging beyond safe operational margins, which can cause asymmetric flight characteristics and eventual fuel starvation on one side.

**Applicable**: Aircraft with 2+ fuel tanks in symmetric configuration

**Monitored SimVars**:
- FUELSYSTEM TANK QUANTITY (per tank)
- FUEL TOTAL QUANTITY

**Logic**:
1. Compute imbalance as percentage: `abs(left - right) / (left + right) * 100`
2. Check trend: is the imbalance growing or shrinking?
3. If imbalance > `profile.fuel.imbalance_advisory_pct` AND growing: emit ADVISORY
4. If imbalance > `profile.fuel.imbalance_warning_pct`: emit WARNING regardless of trend
5. Also check absolute minimums: if any tank < `profile.fuel.minimum_fuel_warning_gal`: emit WARNING

**Alert text example**:
- ADVISORY: "Fuel imbalance growing: left 22 gal, right 16 gal (16% difference). Consider balancing."

**Cooldown**: 180 sec for ADVISORY, 60 sec for WARNING.

---

#### R006 — Icing Conditions

**What it detects**: Aircraft operating in conditions conducive to structural or induction icing without appropriate anti-ice or de-ice systems activated.

**Applicable**: All aircraft

**Monitored SimVars**:
- AMBIENT TEMPERATURE
- AMBIENT IN CLOUD
- AMBIENT PRECIP STATE
- STRUCTURAL ICE PCT
- PITOT ICE PCT
- Anti-ice system states (aircraft-specific L:vars where available)

**Logic**:
1. Determine if icing conditions exist:
   - OAT within `profile.icing.icing_oat_range_c` AND (AMBIENT_IN_CLOUD = true OR AMBIENT_PRECIP_STATE indicates visible moisture)
2. If icing conditions exist AND pitot heat is off: emit WARNING
3. If STRUCTURAL_ICE_PCT > 0 AND increasing: emit WARNING
4. If STRUCTURAL_ICE_PCT > 15%: emit CRITICAL
5. For aircraft with anti-ice (`profile.icing.has_anti_ice`): advise activation when conditions detected

**Alert text example**:
- WARNING: "Icing conditions: OAT -3°C in visible moisture. Structural ice accumulating (4%). Pitot heat is off."

**Cooldown**: 120 sec.

---

#### R007 — Electrical System Degradation

**What it detects**: Bus voltage trending below minimums, indicating alternator/generator failure or excessive electrical load.

**Applicable**: All aircraft

**Monitored SimVars**:
- ELECTRICAL MAIN BUS VOLTAGE
- ELECTRICAL BATTERY BUS VOLTAGE

**Logic**:
1. If main bus voltage below `profile.electrical.main_bus_minimum_v`: emit WARNING
2. If main bus voltage trending downward over 120-second window (sustained decrease > 0.5V): emit ADVISORY
3. If battery bus below `profile.electrical.battery_bus_minimum_v`: emit WARNING

**Alert text example**:
- ADVISORY: "Main bus voltage trending down: 13.1V, was 14.2V five minutes ago."

**Cooldown**: 120 sec.

---

#### R008 — Vacuum System Failure

**What it detects**: Vacuum suction pressure below the threshold needed for reliable gyroscopic instrument operation. Critical for IFR flight.

**Applicable**: Aircraft with vacuum-driven gyros

**Monitored SimVars**:
- SUCTION PRESSURE

**Logic**:
1. If suction < `profile.vacuum.suction_minimum_inhg`: emit WARNING
2. Context: Only relevant if flight conditions suggest instrument reliance (IMC or night). If VMC daytime, downgrade to ADVISORY.

**Alert text example**:
- WARNING: "Vacuum suction 3.1 inHg (minimum 3.5). Attitude and heading indicators may be unreliable."

**Cooldown**: 120 sec.

---

## 6. Priority Queue and Delivery

### 6.1 Queue Behavior

Alerts are not delivered the instant they are generated. They enter a priority queue that manages delivery timing:

1. CRITICAL alerts skip the queue and deliver immediately
2. WARNING alerts deliver within 5 seconds unless sterile cockpit mode is active
3. ADVISORY alerts deliver only when no WARNING or CRITICAL alerts are pending AND the flight phase is CRUISE or GROUND
4. INFO alerts never enter the queue — they go straight to the log

### 6.2 Sterile Cockpit Enforcement

During sterile phases (TAKEOFF, APPROACH, LANDING):
- Only CRITICAL alerts are delivered
- WARNING and ADVISORY alerts are held in queue
- On phase transition to non-sterile, queued alerts are delivered in severity order with 3-second spacing
- Alerts that resolved during the sterile phase are delivered as INFO ("Resolved: ...") rather than their original severity

### 6.3 Delivery Channels

| Channel | Description | Alert Levels |
|---------|-------------|-------------|
| EFB banner | Persistent banner at top of EFB tablet screen | CRITICAL, WARNING |
| EFB notification | Dismissible notification card | WARNING, ADVISORY |
| EFB history | Scrollable alert log | All levels |
| External window | Desktop companion alert panel | All levels |
| Audio tone | Distinct tones per severity | CRITICAL (repeating), WARNING (single chime) |
| Text-to-speech | Spoken alert text (optional, configurable) | CRITICAL, WARNING |

---

## 7. EFB Application

### 7.1 Technology

Built using the MSFS 2024 EFB SDK. The app is a JavaScript/JSX application using the provided `efb_api` framework. It renders on the in-cockpit tablet device present in all MSFS 2024 aircraft.

### 7.2 Communication Architecture

The EFB app cannot use SimConnect directly (it runs in the sim's embedded browser context). Communication between the detection engine and the EFB app uses one of:

**Option A — Coherent DataBindings (preferred)**:
MSFS uses the Coherent GT UI framework internally. The detection engine (running as a WASM module or via SimConnect L:var bridge) writes alert state to Local variables (L:vars) that the EFB JavaScript reads via `SimVar.GetSimVarValue()`.

**Option B — Local HTTP server**:
The out-of-process detection engine runs a lightweight HTTP server on localhost. The EFB app fetches alert state via periodic `fetch()` calls to `http://localhost:{PORT}/api/alerts`. This is simpler to implement but depends on the EFB sandbox allowing localhost fetch (to be validated during development).

**Option C — Hybrid via L:vars**:
The out-of-process client writes a small set of L:vars (current alert severity, alert text chunks, alert count). The EFB reads these L:vars. Limited by L:var string length constraints but requires no HTTP.

Decision point: validate Option B first during Phase 1. Fall back to Option C if sandbox restrictions prevent localhost access.

### 7.3 EFB UI Screens

**Main screen**: Status dashboard showing:
- Connection status (green dot = connected to detection engine)
- Current flight phase
- Active alert count by severity
- Most recent alert in banner format

**Alert history screen**: Scrollable list of all alerts from current session, newest first. Each entry shows timestamp, severity badge, rule ID, and alert text. Tapping an entry shows the telemetry snapshot at the time of the alert.

**Settings screen**:
- Sterile cockpit mode toggle (manual override)
- Audio alerts on/off
- Sensitivity preset (conservative / standard / sensitive)
- Aircraft profile override (if auto-detection picks wrong type)

---

## 8. External Companion Application

### 8.1 Purpose

The external desktop window serves three roles:
1. **Development tool**: Full diagnostic view during rule development and testing
2. **Secondary display**: For sim pilots with multi-monitor setups
3. **Phase 1 deliverable**: Working product before EFB integration is complete

### 8.2 Panels

- **Live telemetry**: Real-time gauges/values for all monitored SimVars
- **Alert feed**: Chronological alert stream with filtering by severity
- **Rule status**: List of all detection rules showing enabled/disabled/triggered state
- **State model**: Current flight phase, aircraft profile, fuel configuration, engine states
- **Buffer visualization**: Sparkline graphs of key parameters over the buffer window (last 10 min)
- **Recording controls**: Start/stop scenario recording, load and replay recorded sessions

---

## 9. Training and Validation Pipeline

### 9.1 Scenario Recording

During any live session or replay, the system can record all polled SimVar data to a timestamped file (CSV or Parquet). This file contains:

- Timestamp (milliseconds since session start)
- All SimVar values at that timestamp
- Current flight phase
- Any alerts generated

### 9.2 Scenario Replay

The replay tool feeds a recorded scenario through the detection engine, replacing the live SimConnect connection with the recorded data stream. This enables:

- Offline testing without running MSFS
- Automated regression testing (run all scenarios, compare alert output to expected results)
- Threshold tuning (adjust parameters, re-run, compare)
- CI/CD integration for detection rule changes

### 9.3 NTSB Scenario Construction

For validated training data, we reconstruct accident scenarios:

1. Read the NTSB final report for a fuel/engine/systems-related accident
2. Note the aircraft type, phase of flight, and the timeline of events
3. In MSFS, load the appropriate aircraft type and set up matching conditions
4. Manually reproduce the failure chain (misset fuel selectors, fail an engine, etc.)
5. Record the session
6. Annotate the recording with the expected alert timeline (when *should* the system have spoken up?)
7. Run the detection engine against the recording
8. Generate a scorecard

### 9.4 Scorecard Metrics

| Metric | Definition | Target |
|--------|-----------|--------|
| Detection latency | Time from anomaly onset to first alert | < 60 sec for CRITICAL, < 120 sec for WARNING |
| False positive rate | Alerts generated when no anomaly exists | < 1 per hour of normal flying |
| Missed detection rate | Known anomalies that produced no alert | 0% for CRITICAL scenarios |
| Severity accuracy | Alert severity matches the actual risk level | > 90% |
| Escalation timing | Time from initial alert to correct final severity | Within 2 minutes |

---

## 10. Configuration

All runtime configuration lives in `config/guardian.toml`.

```toml
[connection]
simconnect_retry_interval_sec = 5
simconnect_max_retries = 0  # 0 = infinite

[polling]
group_a_interval_ms = 250
group_b_interval_ms = 1000
group_c_interval_ms = 4000

[buffer]
history_depth_sec = 600  # 10 minutes
trend_window_sec = 60
rate_of_change_window_sec = 30

[detection]
enabled_rules = ["R001", "R002", "R003", "R004", "R005", "R006", "R007", "R008"]
sensitivity = "standard"  # conservative, standard, sensitive

[sterile_cockpit]
enabled = true
phases = ["TAKEOFF", "APPROACH", "LANDING"]
manual_toggle_key = "Ctrl+Shift+S"

[alerts]
audio_enabled = true
tts_enabled = false
tts_voice = "default"
critical_repeat_interval_sec = 30
warning_cooldown_sec = 60
advisory_cooldown_sec = 180

[recording]
auto_record = false
output_directory = "./recordings"
format = "csv"  # csv or parquet

[efb]
communication_mode = "http"  # http, lvar, or coherent
http_port = 9847

[ui]
theme = "dark"
show_sparklines = true
telemetry_panel_visible = true
```

---

## 11. Future: Real Aircraft Path

The detection engine is designed for portability. The SimConnect client is an input adapter; the rules, state tracker, priority queue, and alert delivery are all independent of the data source. Migrating to real aircraft requires:

1. **New input adapter**: Replace SimConnect client with an adapter reading from real engine monitors (JPI EDM-930, Electronics International CGR-30P, Garmin G3X, etc.). These devices output serial data streams with the same parameters we're monitoring in the sim.
2. **Certified profiles**: Validate aircraft profiles against the aircraft's actual POH data, not MSFS approximations.
3. **Hardware platform**: Raspberry Pi or equivalent running the detection engine, connected to the engine monitor data bus and outputting to a cockpit-mounted tablet.
4. **Regulatory path**: As an advisory-only device with no control authority, this falls under the lightest FAA certification category. Comparable to a tablet running ForeFlight — it provides information but doesn't connect to any aircraft system.

The MSFS development cycle de-risks this path. Every detection rule validated in simulation transfers directly. The math is identical. The thresholds need real-world calibration, but the logic is proven.

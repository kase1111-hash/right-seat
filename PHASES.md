# Flight Guardian — Coding Phase Plan

This document breaks the full Flight Guardian implementation into discrete, buildable phases. Each phase produces working, testable software. Later phases build on earlier ones but each phase stands on its own as a deliverable milestone.

---

## Phase 1: Foundation & Data Layer

**Goal**: Establish the project structure, core abstractions, SimConnect data ingestion, and ring buffer — the plumbing everything else depends on.

### 1A — Project Scaffolding
- [ ] Create .NET 8 solution with project structure: `Guardian.Core`, `Guardian.SimConnect`, `Guardian.Detection`, `Guardian.Priority`, `Guardian.App`, `Guardian.Common`
- [ ] Set up project references (Core has no dependencies; others reference Core)
- [ ] Add Serilog for structured logging with the level conventions from CLAUDE.md
- [ ] Create `config/guardian.toml` with the configuration schema from spec §10
- [ ] Add TOML parser (e.g., Tomlyn) and configuration loader (`GuardianConfig` class)
- [ ] Set up xUnit test projects mirroring the source structure
- [ ] Add `.editorconfig` and code style settings

### 1B — Core Types & Interfaces
- [ ] Define `SimVarId` enum covering all SimVars from spec §2.2 (Groups A–D)
- [ ] Define `SimVarValue` with timestamp, value, and unit metadata
- [ ] Define `TelemetrySnapshot` — a point-in-time collection of all current SimVar values
- [ ] Define `AircraftProfile` class matching the JSON schema (spec §3.1)
- [ ] Define `FlightPhase` enum (Ground, Takeoff, Climb, Cruise, Descent, Approach, Landing)
- [ ] Define `AlertSeverity` enum (Critical, Warning, Advisory, Info)
- [ ] Define `Alert` class (severity, rule ID, text key, parameters, telemetry snapshot, timestamp)
- [ ] Define `IDetectionRule` interface (spec §5.1)
- [ ] Define `UnitsConverter` static class (Rankine↔Fahrenheit, radians↔degrees, etc.)
- [ ] Write unit tests for `UnitsConverter`

### 1C — Aircraft Profile System
- [ ] Implement JSON profile loader with deserialization into `AircraftProfile`
- [ ] Implement unit conversion on load (pilot-friendly → native SimConnect units)
- [ ] Create `c172sp.json` profile (Cessna 172SP — single engine baseline)
- [ ] Create `be58_baron.json` profile (Beechcraft Baron — twin engine baseline)
- [ ] Create `generic_single_piston.json` fallback profile
- [ ] Create `generic_twin_piston.json` fallback profile
- [ ] Implement profile matching logic (exact title → partial match → generic fallback)
- [ ] Write unit tests for profile loading, conversion, and matching

### 1D — SimConnect Client
- [ ] Implement `SimConnectClient` class wrapping the MSFS SimConnect managed SDK
- [ ] Implement connection lifecycle (connect, register data definitions, disconnect, reconnect with backoff)
- [ ] Register data definitions for Groups A–D with correct polling frequencies
- [ ] Implement polling loop that receives data and emits `TelemetrySnapshot` events
- [ ] Implement on-change subscription for Group D variables
- [ ] Add structured logging for connection state transitions
- [ ] Handle graceful degradation when SimVars are unavailable (log INFO, skip)

### 1E — Ring Buffer
- [ ] Implement `TelemetryBuffer` (ring buffer, configurable depth, default 10 min)
- [ ] Support `Latest(simVar)` — current value
- [ ] Support `Window(simVar, timeSpan)` — all values in a time window
- [ ] Support `RateOfChange(simVar, windowSeconds)` — computed derivative
- [ ] Support `Delta(simVar, referenceTimestamp)` — change from a reference point
- [ ] Write unit tests with synthetic data for all query methods
- [ ] Ensure thread safety (SimConnect writes from polling thread, detection reads)

**Deliverable**: Application connects to MSFS, polls SimVars, stores them in a buffer, and logs to console. No detection, no UI. Can be verified by running with MSFS open and checking logs.

---

## Phase 2: Flight State & Detection Engine Framework

**Goal**: Build the flight phase tracker, the detection engine that evaluates rules, and implement the first two detection rules (R001, R003) as proof that the architecture works end-to-end.

### 2A — Flight Phase Tracker
- [ ] Implement `FlightPhaseTracker` with state machine logic per spec §4.1
- [ ] Phase transitions: Ground → Takeoff → Climb → Cruise → Descent → Approach → Landing
- [ ] Use sustained-condition timers (e.g., vertical speed > 200 fpm for 15 sec → CLIMB)
- [ ] Expose current phase and phase-transition events
- [ ] Write unit tests using synthetic telemetry sequences that exercise all transitions

### 2B — Detection Engine
- [ ] Implement `DetectionEngine` class that manages registered rules
- [ ] Rule registration at startup (scan assembly or explicit registration)
- [ ] Evaluation loop: for each rule, check `IsApplicable`, then `Evaluate` on its interval
- [ ] Wrap each rule evaluation in try/catch — a crashing rule is logged and disabled for the session
- [ ] Track rule state: enabled, disabled (missing SimVars), disabled (crashed), triggered
- [ ] Emit detected `Alert` objects to subscribers (event-based)
- [ ] Write tests with mock rules verifying engine behavior (scheduling, error handling, disabling)

### 2C — Rule R001: Fuel Cross-Feed Mismatch
- [ ] Implement `R001_FuelCrossFeedMismatch` per spec §5.5
- [ ] Logic: detect multiple engines drawing from same tank while other tanks have fuel
- [ ] Calculate time-to-exhaustion at current combined burn rate
- [ ] Escalation: WARNING (>30 min) → CRITICAL (≤30 min) → CRITICAL urgent (≤15 min)
- [ ] Sensor/selector disagreement detection (fuel flow on engine with selector OFF)
- [ ] Applicable only to multi-engine aircraft
- [ ] Alert text uses string keys with parameter placeholders (for future localization)
- [ ] Cooldown: 120 sec WARNING, 30 sec CRITICAL
- [ ] Write unit tests: normal ops (no alert), cross-feed detected (warning), time-critical (critical)
- [ ] Create test scenario files: `R001_BothEnginesSameTank_Warning.csv`, `R001_15MinToExhaustion_Critical.csv`

### 2D — Rule R003: Engine Temperature Trend
- [ ] Implement `R003_EngineTemperatureTrend` per spec §5.5
- [ ] Rate-of-change computation over 60-second sliding window (use buffer's `RateOfChange`)
- [ ] Threshold checks against profile values (CHT, EGT, ITT)
- [ ] Absolute value checks: 90% redline → WARNING, 95% redline → CRITICAL
- [ ] Flight-phase awareness: relax rate thresholds by 50% during CLIMB
- [ ] Cooldown: 60 sec ADVISORY, 30 sec WARNING/CRITICAL
- [ ] Write unit tests for all severity levels, escalation, climb-phase relaxation
- [ ] Create test scenario files

**Deliverable**: Application connects, tracks flight phase, and can detect fuel cross-feed and temperature trend issues. Alerts are emitted as structured objects (logged to console). R001 validates multi-engine detection; R003 validates trend analysis.

---

## Phase 3: Remaining Detection Rules (R002, R004–R008)

**Goal**: Complete the full v1 detection rule suite. Each rule follows the same pattern established in Phase 2.

### 3A — Rule R002: Asymmetric Power/Trim Disagreement
- [ ] Implement per spec §5.5 — trim offset vs power differential mismatch
- [ ] Time-based escalation (ADVISORY → WARNING after 1–2 min persistence)
- [ ] Multi-engine only
- [ ] Unit tests + scenario files

### 3B — Rule R004: Oil Pressure Anomaly
- [ ] Implement per spec §5.5 — absolute minimum, drop rate, temp/pressure divergence
- [ ] Context suppression: low oil pressure at idle on ground is normal
- [ ] Cross-correlation: temp rising + pressure falling = lubrication concern
- [ ] Unit tests + scenario files

### 3C — Rule R005: Fuel Imbalance
- [ ] Implement per spec §5.5 — left/right tank percentage divergence
- [ ] Trend-aware: only alert on growing imbalance at ADVISORY level
- [ ] Absolute minimum tank quantity check
- [ ] Unit tests + scenario files

### 3D — Rule R006: Icing Conditions
- [ ] Implement per spec §5.5 — OAT + moisture envelope detection
- [ ] Check pitot heat state, structural ice accumulation
- [ ] Escalation based on structural ice percentage
- [ ] Unit tests + scenario files

### 3E — Rule R007: Electrical Degradation
- [ ] Implement per spec §5.5 — bus voltage trending and absolute minimums
- [ ] 120-second trend window for sustained decrease
- [ ] Unit tests + scenario files

### 3F — Rule R008: Vacuum System Failure
- [ ] Implement per spec §5.5 — suction pressure below gyro reliability
- [ ] Context: downgrade to ADVISORY in VMC daytime conditions
- [ ] Unit tests + scenario files

**Deliverable**: All 8 detection rules implemented and tested. Full detection suite running against the telemetry buffer. Each rule has unit tests and at least one positive + one negative scenario file.

---

## Phase 4: Priority Queue & Alert Delivery

**Goal**: Build the alert triage system that controls *when* and *how* alerts reach the pilot, including sterile cockpit enforcement.

### 4A — Priority Queue
- [ ] Implement `AlertPriorityQueue` per spec §6.1
- [ ] CRITICAL: bypass queue, deliver immediately
- [ ] WARNING: deliver within 5 sec unless sterile mode
- [ ] ADVISORY: deliver only during CRUISE/GROUND with no higher-priority alerts pending
- [ ] INFO: straight to log, never queued
- [ ] Severity-ordered delivery with 3-second spacing after sterile phase ends

### 4B — Sterile Cockpit Mode
- [ ] Automatic activation during TAKEOFF, APPROACH, LANDING phases
- [ ] Suppress WARNING/ADVISORY during sterile phases; queue them
- [ ] On phase transition to non-sterile: deliver queued alerts in severity order
- [ ] Resolved alerts during sterile phase delivered as INFO instead of original severity
- [ ] Manual toggle support (from config key binding or future EFB input)

### 4C — Cooldown & Deduplication
- [ ] Per-rule cooldown tracking (configurable per rule + per severity)
- [ ] Suppress duplicate alerts during cooldown unless severity escalates
- [ ] Emit INFO "resolved" message when condition clears during cooldown

### 4D — Audio Alerts
- [ ] Implement audio tone system: distinct tones for CRITICAL (repeating) and WARNING (single chime)
- [ ] Respect `alerts.audio_enabled` config
- [ ] TTS stub (opt-in, configurable voice) — basic implementation, can be refined later

### 4E — Integration Testing
- [ ] End-to-end test: synthetic telemetry → buffer → detection → priority queue → delivered alerts
- [ ] Test sterile cockpit suppression and release
- [ ] Test cooldown and escalation interactions
- [ ] Test multi-rule scenarios (simultaneous alerts from different rules)

**Deliverable**: Complete alert pipeline from detection to timed delivery. Alerts are triaged, deduplicated, and delivered respecting sterile cockpit rules. The full backend pipeline is complete.

---

## Phase 5: External Desktop Application

**Goal**: Build the WPF/Avalonia companion window that serves as the primary development UI and first user-facing output.

### 5A — Application Shell
- [ ] Create WPF or Avalonia desktop app in `Guardian.App`
- [ ] Dark theme (consistent with EFB design)
- [ ] Main window with panel layout: telemetry, alerts, rule status, state model
- [ ] Wire up to `GuardianConfig` for runtime settings

### 5B — Live Telemetry Panel
- [ ] Real-time gauges/values for monitored SimVars
- [ ] Sparkline graphs for key parameters over buffer window (last 10 min)
- [ ] Color-coded values (normal = green/white, warning range = amber, redline = red)

### 5C — Alert Feed Panel
- [ ] Chronological alert stream with severity badges and color coding
- [ ] Filter by severity level
- [ ] Alert detail view showing telemetry snapshot at time of alert

### 5D — Diagnostic Panels
- [ ] Rule status panel: all rules with enabled/disabled/triggered state
- [ ] State model panel: current flight phase, aircraft profile, fuel config, engine states
- [ ] Connection status indicator

### 5E — Recording Controls
- [ ] Start/stop scenario recording to CSV
- [ ] Load and replay recorded sessions through the detection engine
- [ ] Display replay results alongside expected results for comparison

**Deliverable**: Fully functional desktop companion window showing live telemetry, alert feed, rule status, and recording/replay controls. This is the Phase 1 "product" from the README roadmap.

---

## Phase 6: Scenario Replay & Training Pipeline

**Goal**: Build the offline testing and validation infrastructure for tuning detection rules against recorded and NTSB-derived scenarios.

### 6A — Replay Engine
- [ ] Implement `ScenarioReplayEngine` that replaces SimConnect with recorded data
- [ ] Feed timestamped CSV/Parquet data through the full pipeline (buffer → detection → priority)
- [ ] Support variable playback speed
- [ ] Output: list of alerts generated with timestamps

### 6B — Scenario File Format
- [ ] Define CSV schema: timestamp, SimVar columns
- [ ] Optional Parquet support for large scenarios
- [ ] Expected-results JSON format: list of expected alerts with timestamp windows and severity

### 6C — Regression Test Runner
- [ ] CLI tool: run all scenarios in `training/scenarios/`, compare output to `training/scenarios/expected/`
- [ ] Diff report: new alerts, missing alerts, severity mismatches, timing drift
- [ ] Exit code: 0 = all pass, nonzero = regression detected
- [ ] CI/CD integration (can run in GitHub Actions without MSFS)

### 6D — Scorecard Generation
- [ ] Compute metrics per spec §9.4: detection latency, false positive rate, missed detection rate, severity accuracy, escalation timing
- [ ] Output scorecard as JSON + human-readable summary
- [ ] Track scorecard history for trend analysis across rule changes

### 6E — NTSB Scenario Construction Guide
- [ ] Document the process for reconstructing accident scenarios (spec §9.3)
- [ ] Create first NTSB-derived scenario (based on the origin story: twin-engine fuel starvation)
- [ ] Validate R001 against this scenario

**Deliverable**: Offline testing pipeline that can validate all detection rules against recorded scenarios without MSFS. Regression testing is automated. At least one NTSB scenario is reconstructed and validated.

---

## Phase 7: EFB Integration

**Goal**: Build the in-cockpit tablet app for MSFS 2024 using the EFB SDK.

### 7A — Communication Layer
- [ ] Implement HTTP server in the backend (Option B from spec §7.2): `GET /api/alerts`, `GET /api/status`
- [ ] JSON API: current alerts, flight phase, connection status, alert history
- [ ] Configurable port (default 9847)
- [ ] Validate `fetch()` works from EFB sandbox to localhost
- [ ] If blocked: implement L:var fallback (Option C)

### 7B — EFB App Shell
- [ ] Set up EFB project in `efb/GuardianApp/` using MSFS EFB SDK
- [ ] Dark theme, minimum 16px body text
- [ ] Navigation between screens: Main, Alert History, Settings

### 7C — Main Dashboard Screen
- [ ] Connection status indicator (green dot)
- [ ] Current flight phase display
- [ ] Active alert count by severity
- [ ] Most recent alert in banner format (no scrolling for current alerts)

### 7D — Alert History Screen
- [ ] Scrollable list of all session alerts, newest first
- [ ] Severity badge, rule ID, timestamp, alert text per entry
- [ ] Tap for detail view with telemetry snapshot

### 7E — Settings Screen
- [ ] Sterile cockpit mode manual toggle
- [ ] Audio alerts on/off
- [ ] Sensitivity preset selector (conservative / standard / sensitive)
- [ ] Aircraft profile override dropdown

### 7F — In-Sim Testing
- [ ] Test in MSFS 2024 Coherent GT browser engine (not just Chrome)
- [ ] Validate glanceability: pilot understands state in under 2 seconds
- [ ] Test across multiple aircraft types

**Deliverable**: Working EFB tablet app displaying alerts in the cockpit. Pilot can see current status, review alert history, and adjust settings from the in-sim tablet.

---

## Phase 8: Polish, Additional Profiles & Documentation

**Goal**: Harden the system, expand aircraft coverage, and prepare for community use.

### 8A — Additional Aircraft Profiles
- [ ] `pa28_warrior.json` (Piper Warrior — single piston)
- [ ] `pa44_seminole.json` (Piper Seminole — twin piston)
- [ ] `c182t.json` (Cessna 182T — single piston, higher performance)
- [ ] `da62.json` (Diamond DA62 — twin turbodiesel, different engine parameters)
- [ ] Validate each profile against the aircraft's POH values

### 8B — Rule Documentation
- [ ] Write `docs/rules/R001.md` through `R008.md`
- [ ] Each doc: real-world failure mode, how the rule works, thresholds, limitations
- [ ] Write `docs/simvars.md` — complete SimVar reference
- [ ] Write `docs/architecture.md` — detailed system architecture

### 8C — False Positive Tuning
- [ ] Record extended normal flight sessions across multiple aircraft types
- [ ] Run all rules against normal flights — target < 1 false positive per hour
- [ ] Tune thresholds where needed, update profiles
- [ ] Document tuning decisions in profiles

### 8D — Error Handling Hardening
- [ ] Audit all error paths for proper logging
- [ ] Verify no swallowed exceptions in detection rules
- [ ] Test graceful degradation: disconnect mid-flight, reconnect, resume
- [ ] Test with aircraft that have missing SimVars (rules disable cleanly)

### 8E — Demonstration Tools
- [ ] Side-by-side crash replay viewer (spec Phase 5)
- [ ] Show recorded flight with and without Guardian alerts on a timeline
- [ ] Export for video/article content

**Deliverable**: Production-quality system with 5+ aircraft profiles, comprehensive documentation, tuned false-positive rates, and demonstration tooling for community adoption.

---

## Phase Dependency Summary

```
Phase 1 (Foundation)
  └──► Phase 2 (Flight State + First Rules)
         ├──► Phase 3 (All Rules)
         │      └──► Phase 4 (Priority Queue)
         │             ├──► Phase 5 (Desktop App)
         │             │      └──► Phase 6 (Replay Pipeline)
         │             └──► Phase 7 (EFB App)
         │
         └──► Phase 6 can start partially in parallel with Phase 3
                (replay engine only needs buffer + detection engine)

Phase 8 (Polish) runs after Phases 5–7 are complete.
```

---

## Estimated Scope Per Phase

| Phase | Key Output | Test Coverage |
|-------|-----------|---------------|
| 1 | SimConnect → Buffer pipeline | Unit tests for types, converter, buffer, profile loading |
| 2 | Flight phase + 2 rules working | Unit + scenario tests for R001, R003 |
| 3 | Full 8-rule detection suite | Unit + scenario tests for all rules |
| 4 | Alert triage pipeline | Integration tests for full pipeline |
| 5 | Desktop companion window | Manual testing + recorded replay verification |
| 6 | Offline replay + regression CI | Automated regression suite, NTSB scenario |
| 7 | In-cockpit EFB app | In-sim testing across aircraft types |
| 8 | Polished, documented, demonstrated | False positive validation, extended flight testing |

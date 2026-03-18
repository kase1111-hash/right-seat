# Flight Guardian — System Architecture

## Overview

Flight Guardian is a real-time flight safety monitoring system for Microsoft Flight Simulator 2024. It ingests telemetry from SimConnect, applies detection rules based on aircraft-specific thresholds, manages alert delivery through a priority pipeline, and presents results via both a desktop companion app and an in-cockpit EFB tablet.

## Solution Structure

```
FlightGuardian.sln
├── src/
│   ├── Guardian.Core          # Core types, interfaces, no external dependencies
│   ├── Guardian.Common         # Configuration, profiles, shared utilities
│   ├── Guardian.SimConnect     # SimConnect client, telemetry polling
│   ├── Guardian.Detection      # Detection engine + all 8 rules
│   ├── Guardian.Priority       # Alert pipeline, sterile cockpit, cooldown, audio
│   ├── Guardian.App            # Headless console application
│   ├── Guardian.Desktop        # Avalonia 11 desktop companion window
│   ├── Guardian.Efb            # HTTP API server for EFB integration
│   └── Guardian.Replay         # Scenario replay engine + CLI runner
├── tests/
│   ├── Guardian.Core.Tests
│   ├── Guardian.Detection.Tests
│   ├── Guardian.Priority.Tests
│   └── Guardian.Replay.Tests
├── config/
│   ├── guardian.toml           # Runtime configuration
│   └── profiles/               # Aircraft-specific JSON profiles
├── training/
│   └── scenarios/              # Replay CSV files + expected results
├── efb/
│   └── GuardianApp/            # EFB web app (HTML/CSS/JS)
└── docs/                       # Documentation
```

## Data Flow

```
SimConnect (MSFS 2024)
        │
        ▼
 SimConnectClient          ← Polls SimVars at configured frequencies
        │
        ▼
 TelemetrySnapshot         ← Point-in-time collection of all SimVar values
        │
        ├──► TelemetryBuffer    ← Ring buffer (10 min), provides Window/RateOfChange/Delta
        │
        ├──► FlightPhaseTracker ← State machine: Ground→Takeoff→Climb→Cruise→Descent→Approach→Landing
        │
        └──► DetectionEngine    ← Evaluates all registered rules
                │
                ▼
             Alert              ← Structured alert: rule_id, severity, text, telemetry snapshot
                │
                ▼
          AlertPipeline
           ├── AlertCooldownTracker   ← Deduplication, severity escalation bypass
           ├── SterileCockpitManager  ← Suppress non-critical during sterile phases
           ├── AlertPriorityQueue     ← Severity-based delivery timing
           └── AudioAlertService      ← Tone generation for warnings/criticals
                │
                ▼
         DeliveredAlert        ← Alert with delivery metadata
                │
                ├──► Desktop UI (Avalonia MVVM)
                └──► EFB HTTP API (port 9847) ──► EFB Tablet (MSFS Coherent GT)
```

## Key Components

### Guardian.Core

Contains all type definitions with zero external dependencies:

- **SimVarId** — Enum of all monitored SimConnect variables with metadata (group, unit, name)
- **TelemetrySnapshot** — Immutable point-in-time SimVar collection, keyed by `(SimVarId, index)`
- **Alert** — Structured alert with severity, rule ID, localized text key, parameters, and telemetry snapshot
- **FlightPhase** — Enum with `IsSterile()` extension for sterile cockpit determination
- **IDetectionRule** — Interface: `IsApplicable(profile, phase)` + `Evaluate(snapshot, buffer, profile, phase)` → `Alert?`
- **AircraftProfile** — JSON-deserializable aircraft configuration with nested fuel, engine, electrical, vacuum, performance, trim, and icing profiles

### Guardian.Common

- **GuardianConfig** — TOML configuration loader covering connection, polling, buffer, detection, sterile cockpit, alerts, recording, EFB, and UI settings
- **ProfileLoader** — Loads and matches aircraft profiles (exact title → partial match → generic fallback)
- **UnitsConverter** — Rankine↔Fahrenheit, radians↔degrees, PSI↔inHg

### Guardian.SimConnect

- **SimConnectClient** — Wraps the MSFS managed SDK. Handles connection lifecycle with retry/backoff, registers data definitions for Groups A-D at configured frequencies, and emits `TelemetrySnapshot` events.

### Guardian.Detection

- **DetectionEngine** — Manages rule registration and evaluation. Rules are wrapped in try/catch; 3 consecutive errors disable a rule for the session. Tracks rule state (Enabled, DisabledMissingSimVars, DisabledCrashed, DisabledByConfig).
- **Rules R001-R008** — See `docs/rules/` for individual rule documentation.

### Guardian.Priority

- **AlertPipeline** — Orchestrator connecting cooldown, sterile cockpit, priority queue, and audio services.
- **AlertCooldownTracker** — Per-rule cooldown (30s critical, 60s warning, 180s advisory). Severity escalation bypasses cooldown. Emits INFO on resolution.
- **SterileCockpitManager** — Auto-activates during TAKEOFF/APPROACH/LANDING. Suppresses non-critical alerts. Releases queued alerts on phase transition.
- **AlertPriorityQueue** — CRITICAL bypasses queue. WARNING delivered within 5s. ADVISORY only during CRUISE/GROUND. 3s delivery spacing.
- **AudioAlertService** — Stub with events for tone generation. CRITICAL repeating alarm, WARNING single chime.

### Guardian.Desktop

- **GuardianEngineService** — Wraps entire backend pipeline. Exposes events for UI binding. Manages SimConnect lifecycle, EFB server, and CSV recording.
- **ViewModels** — MVVM with CommunityToolkit.Mvvm: MainWindow (3-column layout), TelemetryPanel (real-time gauges), AlertFeed (chronological stream), RuleStatus (rule state grid), StateModel (phase/profile info).
- **Avalonia UI** — Dark theme, severity-colored alerts, panel-based layout.

### Guardian.Efb

- **EfbHttpServer** — HttpListener on port 9847 with CORS. REST endpoints: GET /api/status, GET /api/alerts, POST /api/settings, POST /api/silence.
- **EfbStateProvider** — Bridges pipeline events to JSON DTOs. Maintains capped alert history (500 entries).

### Guardian.Replay

- **ScenarioCsvReader** — Reads timestamped CSV telemetry files. Groups rows by timestamp into TelemetrySnapshot sequences.
- **ScenarioReplayEngine** — Feeds snapshots through the full pipeline (Buffer → PhaseTracker → DetectionEngine → AlertPipeline) at variable speed.
- **ScenarioValidator** — Compares replay results against expected results JSON (matched, missing, forbidden, unexpected alerts).
- **Scorecard** — Metrics: detection latency (mean/p50/p95), false positive count, missed detections, severity accuracy.
- **CLI** — `guardian-replay [dir] --profile --config --speed` with CI-friendly exit codes.

## Detection Rule Pattern

All rules implement `IDetectionRule`:

```csharp
public interface IDetectionRule
{
    string RuleId { get; }
    string Name { get; }
    TimeSpan EvaluationInterval { get; }
    bool IsApplicable(AircraftProfile profile, FlightPhase phase);
    Alert? Evaluate(TelemetrySnapshot snapshot, ITelemetryBuffer buffer,
                    AircraftProfile profile, FlightPhase phase);
}
```

Rules are stateless where possible, using the telemetry buffer's `RateOfChange` and `Window` methods for trend analysis. Rules that require internal state (R002's escalation timer) track it via private fields reset on construction.

## Aircraft Profile System

JSON profiles define aircraft-specific thresholds:

```
config/profiles/
├── c172sp.json             # Cessna 172SP Skyhawk
├── be58_baron.json         # Beechcraft Baron 58
├── pa28_warrior.json       # Piper PA-28-161 Warrior II
├── pa44_seminole.json      # Piper PA-44-180 Seminole
├── c182t.json              # Cessna 182T Skylane
├── da62.json               # Diamond DA62 (turbodiesel, 28V electrical)
├── generic_single_piston.json
└── generic_twin_piston.json
```

Profile matching: exact MSFS aircraft title → partial match → engine count + type fallback → generic profile.

## Configuration

`guardian.toml` controls all runtime behavior:

- **[connection]** — SimConnect retry interval and max retries
- **[polling]** — Group A/B/C polling intervals
- **[buffer]** — History depth, trend and rate-of-change windows
- **[detection]** — Enabled rules list, sensitivity preset
- **[sterile_cockpit]** — Auto-enable, phase list, manual toggle key
- **[alerts]** — Audio/TTS settings, cooldown intervals per severity
- **[recording]** — Auto-record, output directory, format
- **[efb]** — Communication mode, HTTP port
- **[ui]** — Theme, sparklines, panel visibility

## Flight Phases

```
Ground ──► Takeoff ──► Climb ──► Cruise ──► Descent ──► Approach ──► Landing
  ▲                                                                      │
  └──────────────────────────────────────────────────────────────────────┘
```

Sterile phases (Takeoff, Approach, Landing) suppress non-critical alerts. Transitions are based on sustained telemetry conditions (e.g., vertical speed > 200 fpm for 15 seconds → CLIMB).

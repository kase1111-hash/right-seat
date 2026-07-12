# Flight Guardian

**Passive AI copilot for general aviation safety monitoring**

Flight Guardian is a non-intrusive monitoring system that watches aircraft telemetry in real time, detects developing problems before they trigger warning lights, and delivers prioritized alerts to the pilot at the right moment. It runs first as a Microsoft Flight Simulator 2024 addon — using the sim as both development platform and validation environment — with a long-term path toward certified advisory hardware for real aircraft.

## Why This Exists

General aviation accidents are overwhelmingly caused by things a second set of eyes would catch. Fuel mismanagement, unnoticed temperature trends, configuration errors, icing conditions building while the pilot is heads-down on navigation. Single-pilot operations have no redundancy for attention.

Current aircraft warning systems are binary threshold alerts — the oil pressure light comes on when you already have a problem. Flight Guardian watches for the *trend toward* a problem: a cylinder head temp climbing 3°/min for eight minutes, a fuel imbalance growing silently, a cross-feed configuration that doesn't match what the engines are actually burning.

The system is advisory only. It never touches the controls. It watches, it thinks, and it speaks up — like a good copilot who knows when to talk and when to stay quiet.

## Origin

This project was born from analysis of a real accident: a multi-engine aircraft lost both engines in flight because the pilot had one engine on cross-feed and the other on normal fuel selection, starving both from the same tank while a full tank sat unused. The aircraft was also trimmed for asymmetric thrust — a condition that should have prompted immediate investigation. Every piece of information needed to prevent that crash was available in the cockpit instruments. Nothing was watching.

## Architecture Overview

```
┌─────────────────────────────────────────────────────────┐
│                    DATA SOURCES                         │
│  SimConnect API  │  Aircraft Profiles  │  NTSB Records  │
└────────┬─────────┴──────────┬──────────┴───────┬────────┘
         │                    │                  │
         ▼                    ▼                  ▼
┌─────────────────────────────────────────────────────────┐
│                  PROCESSING ENGINE                      │
│                                                         │
│  State Tracker ──► Anomaly Detector ──► Priority Queue  │
│  (flight phase,    (rules engine +      (triage,        │
│   configuration)    trend analysis)      timing,        │
│                                          sterile mode)  │
└────────────────────────┬────────────────────────────────┘
                         │
              ┌──────────┴──────────┐
              ▼                     ▼
     ┌────────────────┐   ┌─────────────────┐
     │  External App  │   │  EFB Tablet App  │
     │  (dev + debug) │   │  (in-cockpit)    │
     └────────────────┘   └─────────────────┘
```

**Five phases of development:**

1. **Data layer** — SimConnect client streaming live SimVars into a rolling buffer
2. **Detection engine** — Rules for known bad states + trend analysis for developing problems
3. **Output interfaces** — External companion window first, then in-cockpit EFB app
4. **Training pipeline** — Replay NTSB scenarios through the detector, tune thresholds
5. **Demonstration tools** — Side-by-side crash replay viewer for pilot adoption

## Technology Stack

| Component | Technology | Rationale |
|-----------|-----------|-----------|
| SimConnect client | C# / .NET | MSFS SDK recommended path for out-of-process addons; managed code, rich SimConnect support |
| Detection engine | C# | Runs in-process with the client; rules + math, no heavy dependencies |
| External UI | WPF or Avalonia | Native Windows desktop, binds naturally to the C# backend |
| EFB app | JavaScript / JSX | Required by MSFS 2024 EFB API; runs on the in-cockpit tablet |
| Aircraft profiles | JSON | One file per airframe type; human-readable, easy to contribute |
| Scenario recordings | CSV / Parquet | Timestamped SimVar captures for replay and training |
| Configuration | TOML | Single config file for all runtime settings |

## Project Structure

```
right-seat/
├── README.md                   # This file
├── spec.md                     # Full technical specification
├── LICENSE                     # MIT
├── FlightGuardian.sln
├── build.bat                   # One-step restore/build/test/publish (Windows)
├── scripts/
│   └── package_release.py      # Assembles the distributable release zip
├── config/
│   ├── guardian.toml           # Runtime configuration
│   └── profiles/               # Aircraft-specific normal operating ranges
├── src/
│   ├── Guardian.Core/          # Shared types, interfaces, configuration
│   ├── Guardian.Common/        # Logging, profile loading, audio, utilities
│   ├── Guardian.SimConnect/    # SimConnect client (live when MSFS SDK present)
│   ├── Guardian.Detection/     # State tracker, anomaly detector, rules engine
│   ├── Guardian.Priority/      # Alert priority queue and sterile cockpit logic
│   ├── Guardian.Efb/           # HTTP API serving the EFB tablet app
│   ├── Guardian.Replay/        # Scenario replay CLI (regression harness)
│   ├── Guardian.App/           # Headless console monitor
│   └── Guardian.Desktop/       # Avalonia desktop companion app
├── efb/
│   └── GuardianApp/            # EFB tablet app source (HTML/JS)
├── training/
│   ├── scenarios/              # Recorded + synthetic validation scenarios
│   └── tools/                  # Scenario generator
├── docs/
│   ├── rules/                  # Detailed documentation per detection rule
│   └── simvars.md              # Complete SimVar reference and polling config
└── tests/                      # xUnit test suites (226 tests)
```

## Getting Started

### Prerequisites

- Windows 10/11 (SimConnect is Windows-only)
- Microsoft Flight Simulator 2024
- MSFS 2024 SDK (install via Developer Mode → Help → SDK Installer)
- .NET 8.0 SDK or later
- Visual Studio 2022 or JetBrains Rider (recommended)
- Node.js 18+ (for EFB app development)

### For Simmers (no build tools needed)

1. Download `FlightGuardian-win-x64.zip` from the
   [latest release](https://github.com/kase1111-hash/right-seat/releases)
2. Unzip anywhere and run `Guardian.Desktop\Guardian.Desktop.exe`
3. (Optional) Copy `community\flight-guardian-efb` into your MSFS
   `Community` folder to get the in-cockpit EFB tablet app
4. Launch MSFS 2024 and load a flight — Guardian connects automatically
   and starts monitoring

Audio chimes and voice callouts are built in (toggle in `config/guardian.toml`).

### For Developers

```bash
# Clone the repository
git clone https://github.com/kase1111-hash/right-seat.git
cd right-seat

# Build everything, run tests, publish executables (Windows)
build.bat

# Or manually on any platform (runs in stub/replay mode without MSFS):
dotnet build FlightGuardian.sln
dotnet test FlightGuardian.sln
dotnet run --project src/Guardian.Replay -- training/scenarios
dotnet run --project src/Guardian.App
```

Live SimConnect telemetry requires Windows with the MSFS SDK installed.
The build detects the SDK automatically through the `MSFS_SDK` environment
variable (set by the SDK installer) and compiles in the live client; without
it, everything still builds and runs against recorded scenarios.

### Development Workflow

1. Launch MSFS 2024 and load a flight
2. Run Flight Guardian in debug mode
3. Introduce a failure condition (e.g., switch one engine to cross-feed)
4. Observe detection and alert behavior
5. Tune rules and thresholds
6. Record the scenario for the test suite

## Detection Rules (v1 Targets)

| ID | Name | What It Catches | Severity |
|----|------|----------------|----------|
| R001 | Fuel cross-feed mismatch | Both engines drawing from same tank due to selector misconfiguration | WARNING → CRITICAL |
| R002 | Asymmetric power/trim disagreement | Trim offset that doesn't match engine power differential, or vice versa | ADVISORY → WARNING |
| R003 | Engine temperature trend | CHT or EGT climbing at abnormal rate or approaching redline | ADVISORY → CRITICAL |
| R004 | Oil pressure anomaly | Oil pressure drop, divergence from temperature, or below-minimum for power setting | WARNING → CRITICAL |
| R005 | Fuel imbalance | Left/right tank quantities diverging beyond safe margin | ADVISORY → WARNING |
| R006 | Icing conditions | OAT + moisture in icing envelope with anti-ice/de-ice systems off | WARNING |
| R007 | Electrical degradation | Bus voltage trending below instrument reliability minimums | ADVISORY → WARNING |
| R008 | Vacuum system failure | Suction pressure below gyro instrument reliability threshold | WARNING |

## Roadmap

### Phase 1: Proof of Concept
- [x] SimConnect client reading core engine and fuel SimVars
- [x] Rule R001 (fuel cross-feed) implemented and tested
- [x] External window displaying live state and alerts
- [x] Record/replay capability for captured sessions

### Phase 2: Core Detection Suite
- [x] All v1 detection rules (R001–R008)
- [x] Flight phase detection (ground through landing)
- [x] Priority queue with sterile cockpit mode
- [x] Trend analysis engine with configurable windows
- [x] Aircraft profile system with 8 airframes

### Phase 3: EFB Integration
- [x] MSFS 2024 EFB app displaying alerts on cockpit tablet
- [x] Alert history and dismissal
- [x] Configuration accessible from EFB settings

### Phase 4: Training Pipeline
- [x] NTSB scenario parser and ingestion tools
- [x] Automated scenario replay through detection engine
- [x] Scorecard generation (detection time, false positive rate)
- [x] Validation scenarios covering every detection rule (runs in CI)
- [ ] Parameter optimization feedback loop

### Phase 5: Demonstration
- [ ] Side-by-side crash replay viewer
- [ ] Pilot response time simulator
- [ ] Publishable video/article content for community adoption

### Future: Real Aircraft Path
- [ ] Hardware integration specification (engine monitor data feeds)
- [ ] FAA advisory device regulatory assessment
- [ ] Partnership with engine monitor manufacturers (JPI, EI, Garmin)
- [ ] Portable advisory tablet prototype

## Contributing

This is an open-source safety project. Contributions are welcome in these areas:

- **Aircraft profiles**: If you fly a type we don't have a profile for, contribute one
- **Detection rules**: Propose new rules based on accident analysis or flight experience
- **NTSB scenarios**: Help recreate known accident chains in MSFS for validation
- **Testing**: Fly with Guardian running and report false positives or missed detections

## Philosophy

Flight Guardian follows the "sovereignty with oversight" principle. The pilot is always in command. The system never acts, never overrides, never takes control. It watches, analyzes, and advises — and it knows when to be quiet. Its job is to give the pilot information early enough that even an imperfect response produces a survivable outcome.

The MSFS implementation is not a toy. It is a full-fidelity development and validation platform that produces a system directly transferable to real aircraft sensor data. Every detection rule validated in the simulator works on real telemetry with the same math — only the input source changes.

## License

MIT — because safety tools should be available to everyone.

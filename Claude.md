# CLAUDE.md — Flight Guardian Development Guide

This document governs how AI assistants (Claude) should work on the Flight Guardian project. It is a constitutional document: follow it before following instinct.

---

## Project Identity

Flight Guardian is a passive aircraft monitoring system. It watches telemetry, detects developing problems, and advises the pilot. It never controls anything.

**This is a safety-critical project.** Every design decision, every line of code, every threshold value has the potential to affect whether a pilot gets a timely warning about a life-threatening condition. That means:

- No guessing at aerospace values. If you don't know a threshold, flag it for human research.
- No silent failures. Every error path must produce a log entry. A swallowed exception in a detection rule could mean a missed alert.
- No cleverness over clarity. The person maintaining this might be reading detection logic at 2 AM trying to understand why a rule fired (or didn't). Write for that reader.
- Test assertions are not optional. Every detection rule ships with a recorded scenario that exercises it.

---

## Architecture Rules

### Separation of Concerns

The system is layered. Respect the boundaries:

```
SimConnect Client  →  Data Buffer  →  Detection Engine  →  Priority Queue  →  Presentation
(input adapter)       (ring buffer)    (rules + trends)     (triage)          (EFB / desktop)
```

- **The detection engine never talks to SimConnect.** It reads from the buffer. This is what makes replay and testing possible. If you find yourself importing SimConnect types in a detection rule, stop.
- **Detection rules never format UI output.** They return structured Alert objects. The presentation layer decides how to render them.
- **The priority queue never evaluates telemetry.** It receives alerts and decides when to deliver them based on severity and flight phase. It doesn't know what CHT means.
- **The EFB app never runs detection logic.** It displays what the backend tells it. All intelligence lives in the C# backend.

### Data Flow

All telemetry flows in one direction: SimConnect → Buffer → Detection → Priority → Display. There are no callbacks from display to detection, no "re-check this because the pilot dismissed it." If a condition resolves, the detection rule notices on its next evaluation cycle because the buffer data has changed.

The one exception: manual sterile cockpit toggle flows from presentation back to the priority queue (not to detection — rules keep running regardless of display suppression).

### Aircraft Profiles Are Data, Not Code

If you find yourself writing `if (aircraftType == "C172") { threshold = 450; }` you are doing it wrong. All aircraft-specific values live in JSON profile files. Detection rules read from the profile object. Adding support for a new aircraft type must never require a code change.

---

## Coding Standards

### Language: C# (.NET 8+)

The core application is C# because the MSFS SimConnect SDK has first-class .NET support, it's the recommended out-of-process language, and it provides strong typing that helps catch unit mismatches (a major source of aerospace bugs).

### Naming

- Detection rules: `R001_FuelCrossFeedMismatch.cs`, `R002_AsymmetricPowerTrim.cs`
- Test scenarios: `R001_BothEnginesSameTank_Warning.csv`, `R001_TimeToExhaustion15Min_Critical.csv`
- Aircraft profiles: `c172sp.json`, `be58_baron.json`, `pa44_seminole.json`
- Configuration keys: `snake_case` in TOML, `PascalCase` in C# classes

### Unit Handling

SimConnect returns values in specific units (often Rankine for temperature, radians for angles). Convert to pilot-friendly units (Fahrenheit, degrees) at the *presentation layer*, not in detection rules. Detection rules work in whatever units the SimVar provides natively to avoid conversion errors in safety-critical math.

**Exception**: Aircraft profiles store values in pilot-friendly units (°F, PSI, knots) because they are human-authored. The profile loader converts to native units on load. This conversion is tested.

Document units in comments on every variable that holds a physical quantity:

```csharp
// Good
double chtRankine = buffer.Latest(SimVar.EngCylinderHeadTemp, engineIndex); // Rankine
double chtFahrenheit = UnitsConverter.RankineToFahrenheit(chtRankine);      // °F

// Bad
double cht = buffer.Latest(SimVar.EngCylinderHeadTemp, engineIndex);
```

### Error Handling

- Detection rules must never throw unhandled exceptions. Wrap rule evaluation in try/catch at the engine level. A crashing rule is logged and disabled for the remainder of the session — it does not take down other rules.
- SimConnect disconnections are expected events, not errors. The system enters reconnection mode cleanly.
- Missing SimVars (aircraft doesn't expose a variable) are detected during the first poll cycle. Rules that depend on missing variables are disabled with an INFO log message. No warnings, no errors — this is normal for aircraft that lack certain instrumentation.

### Logging

Use structured logging (Serilog or equivalent) with these levels:

| Level | Use |
|-------|-----|
| Fatal | Application cannot continue (SimConnect SDK not found, corrupt config) |
| Error | Something failed but the system continues (rule threw exception, EFB HTTP server failed to start) |
| Warning | Unexpected but handled (SimVar returned unusual value, profile match was fuzzy) |
| Information | Normal operational events (connected, disconnected, rule enabled/disabled, alert generated) |
| Debug | Detailed operational flow (SimVar poll results, rule evaluation timing, queue state) |
| Verbose | Everything (raw SimConnect packets, individual SimVar values per tick) |

Default level: Information. Debug and Verbose are for development only and will impact polling performance if left on.

---

## Detection Rule Development

### Writing a New Rule

1. **Start with the accident.** Find an NTSB report or known failure mode. Understand the causal chain. Identify which SimVars would have shown the problem developing.

2. **Define the rule in SPEC.md first.** Write the full specification — monitored SimVars, logic, alert text templates, escalation behavior, cooldown — before writing code. Get the design reviewed.

3. **Create test scenarios.** Before implementing the rule:
   - Record a "normal flight" scenario where the rule should NOT fire
   - Record (or construct) at least one scenario where it SHOULD fire
   - Define expected alert timing and severity for each scenario

4. **Implement the rule.** Follow the `IDetectionRule` interface. Keep it focused — one rule detects one category of problem. If your rule is checking both fuel AND temperature, it's two rules.

5. **Validate against scenarios.** Run the replay tool with your scenarios. The rule must:
   - Detect every anomaly in the positive scenarios
   - Produce zero alerts in the normal scenarios
   - Fire within the detection latency targets (< 60s for CRITICAL, < 120s for WARNING)

6. **Document the rule.** Add a detailed entry to `docs/rules/R0XX.md` explaining the real-world failure mode, how the rule works, what thresholds mean, and known limitations.

### Rule Implementation Principles

- **Prefer simple threshold checks over ML.** A rule that fires when oil pressure drops below 25 PSI is easy to understand, easy to test, and easy to trust. Save statistical methods for trend detection where thresholds alone aren't sufficient.

- **Use the buffer, not instantaneous values.** A single anomalous reading is noise. Use sliding windows. A CHT spike for one polling cycle is meaningless. A CHT that has been climbing for 3 minutes is a trend.

- **Flight phase matters.** High CHT during climb is expected. High CHT in cruise at reduced power is not. Rules must account for what phase of flight the aircraft is in. Use the profile's phase-specific thresholds when available.

- **Cross-correlation catches what absolute thresholds miss.** Oil temperature rising while oil pressure falls. Both values might be within normal ranges individually. Together they tell a story. Design rules that look at relationships between parameters, not just individual values.

- **Alert text must be actionable.** Don't just say "CHT HIGH." Say "Engine 1 CHT 478°F, climbing 8°F/min. Currently at 95% of redline (500°F)." Give the pilot the number, the trend, and the context. They know what to do with that information.

- **Never say "PULL POWER" or "LAND IMMEDIATELY."** The system is advisory. It presents information and context. It does not issue commands. Phrasing like "consider" and "monitor closely" is appropriate. Phrasing like "you must" and "take immediate action" is not. The pilot commands the aircraft.

---

## Testing

### Unit Tests

Every detection rule has unit tests covering:
- Normal conditions (rule returns null)
- Anomaly detection (rule returns correct alert)
- Severity escalation (alert severity increases as condition worsens)
- Cooldown behavior (duplicate alerts are suppressed during cooldown)
- Flight phase interaction (sterile cockpit suppression, phase-specific thresholds)
- Missing data (rule gracefully handles unavailable SimVars)
- Edge cases (values at exactly the threshold, oscillating around threshold)

### Scenario Tests

Integration tests using recorded or constructed SimVar data files:
- Each scenario file is a timestamped series of SimVar snapshots
- The test harness feeds the scenario through the full pipeline (buffer → detection → priority)
- Assertions verify: what alerts were generated, at what timestamps, with what severity

### Replay Regression Suite

Before merging any detection rule change:
1. Run all existing scenarios
2. Compare output to baseline (expected alerts per scenario)
3. Any change in alert output requires explicit justification

---

## EFB Development

The EFB app lives in `efb/GuardianApp/` and is built with the MSFS 2024 EFB SDK.

### Constraints

- The EFB runs inside the sim's Coherent GT browser engine. It is NOT a full browser. Test thoroughly in-sim, not just in Chrome.
- No Node.js runtime in the EFB. No npm packages that require Node APIs. Pure browser JavaScript.
- The EFB sandbox may restrict `fetch()` to certain origins. Validate localhost access early.
- The EFB screen is physically small (tablet-sized in the cockpit). Design for glanceability. A pilot should understand the current state in under 2 seconds of looking at the screen.

### Design Principles

- **Color = severity.** Red = critical, amber = warning, blue = advisory. No other meaning for these colors.
- **Large text.** Minimum 16px for body text in the EFB. The pilot is reading this on a small screen from an arm's length away in a vibrating cockpit.
- **No scrolling for current alerts.** The most important information must be visible without scrolling. Alert history can scroll.
- **Dark theme only.** Bright screens in a cockpit at night destroy night vision. The EFB app uses dark backgrounds with muted text.

---

## File and Folder Conventions

```
src/Guardian.Detection/Rules/       # One file per rule: R001_FuelCrossFeedMismatch.cs
src/Guardian.Detection/Trends/      # Trend analysis utilities (rate of change, sliding window)
config/profiles/                    # One JSON file per aircraft type
training/scenarios/                 # Recorded SimVar data files
training/scenarios/expected/        # Expected alert output per scenario (for regression)
docs/rules/                         # One markdown file per rule: R001.md
tests/Guardian.Detection.Tests/     # Test files mirror rule files: R001_Tests.cs
```

### Naming Scenarios

```
{RuleID}_{Condition}_{ExpectedSeverity}.csv
```

Examples:
- `R001_BothEnginesSameTank_Warning.csv`
- `R001_15MinToExhaustion_Critical.csv`
- `R003_CHTClimbing6PerMin_Advisory.csv`
- `R003_CHTAt95PctRedline_Critical.csv`
- `NORMAL_C172_60MinCruise.csv`

---

## What Not To Do

- **Don't add ML models in v1.** The first version is rules and thresholds. They're transparent, testable, and trustworthy. ML-based anomaly detection is a Phase 6+ consideration, after the rule-based system has proven itself.
- **Don't write to SimVars.** Ever. Not even L:vars for "convenience." The system is read-only by design and by principle. The one exception is L:vars used solely for EFB communication (Option C in spec), and even those carry only alert display data, never control inputs.
- **Don't make the system mandatory.** It must be trivially easy to turn off, close, or ignore. A pilot who doesn't want it running should never feel coerced.
- **Don't add voice callouts by default.** Text-to-speech is opt-in. An unexpected voice in the cockpit during a critical phase could be more dangerous than the problem it's warning about. Audio tones (short, distinct, non-startling) are appropriate. Full voice callouts are a user choice.
- **Don't optimize for demo impressions over real utility.** A system that fires 10 dramatic alerts during a normal flight to look impressive is worse than useless. False positives erode trust. Tune for precision. A pilot who trusts the system because it only speaks up when something is actually wrong will listen when it does.
- **Don't hardcode English strings in detection rules.** Alert text templates should support future localization. Use string keys that map to templates, with the template containing parameter placeholders. The presentation layer fills in the localized template with the values.

---

## Build and Development Workflow

### Prerequisites
- .NET 8.0 SDK
- MSFS 2024 + SDK (for EFB development and in-sim testing)
- Visual Studio 2022 or Rider

### Build Commands
```bash
dotnet build                                    # Build all projects
dotnet test                                     # Run all tests
dotnet run --project src/Guardian.App            # Launch the desktop app
```

### EFB Build
```bash
cd efb/GuardianApp
npm install
npm run build
# Copy dist/ contents to MSFS Community package
```

### Recording a Test Scenario
```bash
dotnet run --project src/Guardian.App -- --record --output training/scenarios/my_test.csv
# Fly the scenario in MSFS, then Ctrl+C to stop recording
```

### Running Replay Validation
```bash
dotnet run --project training/replay -- --scenario training/scenarios/R001_BothEnginesSameTank_Warning.csv --expected training/scenarios/expected/R001_BothEnginesSameTank_Warning.json
```

---

## Commit Message Convention

```
[component] Brief description

Components: simconnect, detection, priority, efb, app, training, profiles, docs, config
```

Examples:
```
[detection] Add R003 engine temperature trend rule
[profiles] Add Beechcraft Baron BE58 profile
[training] Record fuel crossfeed scenario for R001 validation
[efb] Implement alert banner display
[docs] Document R004 oil pressure rule logic
```

---

## Decision Log

Record significant design decisions here as the project evolves.

| Date | Decision | Rationale |
|------|----------|-----------|
| 2026-03-17 | System is advisory-only, no SimVar writes | Core safety principle — never interfere with pilot authority |
| 2026-03-17 | C# for core application | MSFS SDK recommended; managed code; strong typing for physical units |
| 2026-03-17 | Rules-based detection in v1, no ML | Transparency, testability, trustworthiness. ML is a future layer on top |
| 2026-03-17 | External app first, EFB second | Faster iteration; EFB sandbox unknowns need investigation |
| 2026-03-17 | Alert text must never issue commands | Advisory role — inform the pilot, never instruct them |
| 2026-03-17 | Dark theme only for EFB | Night vision preservation in cockpit environment |

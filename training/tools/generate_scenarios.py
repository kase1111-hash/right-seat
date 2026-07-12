#!/usr/bin/env python3
"""Generates Flight Guardian validation scenarios (CSV + expected JSON).

Each scenario is a timestamped SimVar stream at 5-second cadence that either
recreates a specific failure mode (one per detection rule) or a normal flight
that must stay silent. Regenerate with:

    python3 training/tools/generate_scenarios.py

Conventions (matching docs/simvars.md and the detection rules):
  - Engine-indexed SimVars use 1-based indices (GeneralEngRpm:1..N)
  - Tank quantities use 0-based indices (FuelSystemTankQuantity:0..N-1)
  - Fuel selector values are 1-based tank numbers, 0 = OFF
  - Temperatures are Rankine in the stream, Fahrenheit in this script
"""

import json
import os

BASE = "2024-01-15T10:{:02d}:{:02d}Z"
OUT = os.path.join(os.path.dirname(__file__), "..", "scenarios")


def f_to_r(f):
    return f + 459.67


def ts(sec):
    return BASE.format(sec // 60, sec % 60)


class Scenario:
    def __init__(self, name, duration_sec, step=5):
        self.name = name
        self.rows = []
        self.duration = duration_sec
        self.step = step

    def emit(self, t, var, index, value):
        self.rows.append((t, var, index, round(float(value), 6)))

    def write(self):
        path = os.path.join(OUT, f"{self.name}.csv")
        with open(path, "w") as f:
            f.write("timestamp,simvar_id,index,value\n")
            for t, var, index, value in self.rows:
                f.write(f"{ts(t)},{var},{index},{value}\n")
        print(f"wrote {path} ({len(self.rows)} rows, {self.duration}s)")


def write_expected(name, description, profile, expected, forbidden):
    path = os.path.join(OUT, "expected", f"{name}.json")
    doc = {
        "scenario_id": name,
        "description": description,
        "profile": profile,
        "expected_alerts": expected,
        "forbidden_alerts": [{"rule_id": r} for r in forbidden],
    }
    with open(path, "w") as f:
        json.dump(doc, f, indent=2)
        f.write("\n")
    print(f"wrote {path}")


def c172_baseline(s, t, cht_f=380, egt_f=1350, oil_psi=72, oil_temp_f=180,
                  fuel_l=24, fuel_r=22, volts=14.1, suction=5.0, oat=10,
                  in_cloud=0, precip=0, struct_ice=0, pitot_ice=0,
                  rpm=2350, throttle=65, fuel_flow=9.5):
    e = s.emit
    e(t, "SimOnGround", 0, 0)
    e(t, "VerticalSpeed", 0, 0)
    e(t, "IndicatedAltitude", 0, 5500)
    e(t, "AirspeedIndicated", 0, 110)
    e(t, "NumberOfEngines", 0, 1)
    e(t, "EngineType", 0, 0)
    e(t, "GeneralEngCombustion", 1, 1)
    e(t, "GeneralEngRpm", 1, rpm)
    e(t, "ThrottleLeverPosition", 1, throttle)
    e(t, "GeneralEngFuelFlow", 1, fuel_flow)
    e(t, "EngCylinderHeadTemperature", 1, f_to_r(cht_f))
    e(t, "EngExhaustGasTemperature", 1, f_to_r(egt_f))
    e(t, "GeneralEngOilPressure", 1, oil_psi)
    e(t, "GeneralEngOilTemperature", 1, f_to_r(oil_temp_f))
    e(t, "FuelTankSelector", 1, 3)  # BOTH
    e(t, "FuelSystemTankQuantity", 0, fuel_l)
    e(t, "FuelSystemTankQuantity", 1, fuel_r)
    e(t, "FuelTotalQuantity", 0, fuel_l + fuel_r)
    e(t, "ElectricalMainBusVoltage", 0, volts)
    e(t, "ElectricalBatteryBusVoltage", 0, 13.8)
    e(t, "SuctionPressure", 0, suction)
    e(t, "AmbientTemperature", 0, oat)
    e(t, "AmbientInCloud", 0, in_cloud)
    e(t, "AmbientPrecipState", 0, precip)
    e(t, "StructuralIcePct", 0, struct_ice)
    e(t, "PitotIcePct", 0, pitot_ice)
    e(t, "RudderTrimPct", 0, 0)
    e(t, "AileronTrimPct", 0, 0)


def baron_baseline(s, t, throttle2=75, rpm2=2400, rudder_trim=0,
                   sel1=1, sel2=2, fuel_l=30, fuel_r=30,
                   cht_f=380, egt_f=1420, oil_psi=72):
    e = s.emit
    e(t, "SimOnGround", 0, 0)
    e(t, "VerticalSpeed", 0, 0)
    e(t, "IndicatedAltitude", 0, 7500)
    e(t, "AirspeedIndicated", 0, 165)
    e(t, "NumberOfEngines", 0, 2)
    e(t, "EngineType", 0, 0)
    for eng, (thr, rpm) in enumerate([(75, 2400), (throttle2, rpm2)], start=1):
        e(t, "GeneralEngCombustion", eng, 1)
        e(t, "GeneralEngRpm", eng, rpm)
        e(t, "ThrottleLeverPosition", eng, thr)
        e(t, "GeneralEngFuelFlow", eng, 13.5)
        e(t, "EngCylinderHeadTemperature", eng, f_to_r(cht_f))
        e(t, "EngExhaustGasTemperature", eng, f_to_r(egt_f))
        e(t, "GeneralEngOilPressure", eng, oil_psi)
        e(t, "GeneralEngOilTemperature", eng, f_to_r(185))
    e(t, "FuelTankSelector", 1, sel1)
    e(t, "FuelTankSelector", 2, sel2)
    e(t, "FuelSystemTankQuantity", 0, fuel_l)
    e(t, "FuelSystemTankQuantity", 1, fuel_r)
    e(t, "FuelTotalQuantity", 0, fuel_l + fuel_r)
    e(t, "ElectricalMainBusVoltage", 0, 27.6)
    e(t, "ElectricalBatteryBusVoltage", 0, 24.5)
    e(t, "SuctionPressure", 0, 5.0)
    e(t, "AmbientTemperature", 0, 8)
    e(t, "AmbientInCloud", 0, 0)
    e(t, "AmbientPrecipState", 0, 0)
    e(t, "StructuralIcePct", 0, 0)
    e(t, "PitotIcePct", 0, 0)
    e(t, "RudderTrimPct", 0, rudder_trim)
    e(t, "AileronTrimPct", 0, 0)


ALL_RULES = ["R001", "R002", "R003", "R004", "R005", "R006", "R007", "R008"]


def ramp(t, start_t, base, rate_per_min, floor=None, ceil=None):
    """Value ramping linearly after start_t."""
    if t <= start_t:
        return base
    v = base + rate_per_min * (t - start_t) / 60.0
    if floor is not None:
        v = max(v, floor)
    if ceil is not None:
        v = min(v, ceil)
    return v


def normal_c172():
    s = Scenario("NormalFlight_C172SP_Cruise", 300)
    for t in range(0, s.duration + 1, s.step):
        # gentle realistic wander, all well inside limits
        cht = 380 + 3 * (t % 60) / 60
        volts = 14.1 - 0.05 * ((t // 30) % 2)
        fuel = 24 - t * (9.5 / 3600 / 2)  # both tanks drain evenly at 9.5 gph
        c172_baseline(s, t, cht_f=cht, volts=volts,
                      fuel_l=fuel, fuel_r=fuel - 1.5)
    s.write()
    write_expected(
        s.name,
        "Normal C172SP cruise, 5 minutes — all parameters within limits. "
        "No alerts may fire.",
        "c172sp", [], ALL_RULES)


def normal_baron():
    s = Scenario("NormalFlight_Baron58_Cruise", 300)
    for t in range(0, s.duration + 1, s.step):
        baron_baseline(s, t, fuel_l=30 - t * 0.001, fuel_r=30 - t * 0.001)
    s.write()
    write_expected(
        s.name,
        "Normal Baron 58 twin cruise, 5 minutes — engines on separate tanks, "
        "balanced fuel, all systems nominal. No alerts may fire.",
        "be58_baron", [], ALL_RULES)


def r002_power_trim():
    s = Scenario("R002_PowerAsymmetry_Baron", 240)
    for t in range(0, s.duration + 1, s.step):
        if t < 30:
            baron_baseline(s, t)
        else:
            # Engine 2 power pulled back, no compensating trim applied
            baron_baseline(s, t, throttle2=40, rpm2=1900)
    s.write()
    write_expected(
        s.name,
        "Baron 58: engine 2 throttle pulled to 40% at T+30s with no rudder "
        "trim compensation. Advisory immediately, escalates to warning after "
        "60s persistence.",
        "be58_baron",
        [
            {"rule_id": "R002", "severity": "Advisory",
             "text_key": "R002_POWER_ASYMMETRY_NO_TRIM",
             "earliest_sec": 30, "latest_sec": 90, "required": True},
            {"rule_id": "R002", "severity": "Warning",
             "text_key": "R002_POWER_ASYMMETRY_NO_TRIM",
             "earliest_sec": 90, "latest_sec": 200, "required": True},
        ],
        ["R001", "R003", "R004", "R005", "R006", "R007", "R008"])


def r003_cht_trend():
    s = Scenario("R003_ChtTrendClimb_C172", 240)
    for t in range(0, s.duration + 1, s.step):
        # Cooling airflow blockage: CHT climbs 12 F/min from T+30
        cht = ramp(t, 30, 380, 12, ceil=444)  # stays below 90% of 500F redline
        c172_baseline(s, t, cht_f=cht)
    s.write()
    write_expected(
        s.name,
        "C172SP: CHT rising at 12 F/min from T+30s (cooling airflow "
        "blockage). Trend warning once the 60s regression window confirms "
        "the rate; absolute redline never reached.",
        "c172sp",
        [
            {"rule_id": "R003", "severity": "Warning",
             "text_key": "R003_CHT_TREND_WARNING",
             "earliest_sec": 45, "latest_sec": 150, "required": True},
        ],
        ["R001", "R002", "R004", "R005", "R006", "R007", "R008"])


def r004_oil_pressure():
    s = Scenario("R004_OilPressureDrop_C172", 180)
    for t in range(0, s.duration + 1, s.step):
        # Oil leak: pressure falls 15 psi/min from T+30
        oil = ramp(t, 30, 72, -15, floor=20)
        c172_baseline(s, t, oil_psi=oil)
    s.write()
    write_expected(
        s.name,
        "C172SP: oil pressure dropping 15 psi/min from T+30s (oil leak). "
        "Drop-rate warning first, critical when pressure passes the 25 psi "
        "minimum around T+218s... capped at T+180s so warning only.",
        "c172sp",
        [
            {"rule_id": "R004", "severity": "Warning",
             "text_key": "R004_OIL_PRESSURE_DROP_RATE",
             "earliest_sec": 40, "latest_sec": 140, "required": True},
        ],
        ["R001", "R002", "R003", "R005", "R006", "R007", "R008"])


def r005_fuel_imbalance():
    s = Scenario("R005_FuelImbalance_C172", 300)
    for t in range(0, s.duration + 1, s.step):
        # Right tank leaking 2 gal/min; left feeds normally
        right = ramp(t, 30, 22, -2, floor=0)
        c172_baseline(s, t, fuel_l=24, fuel_r=right)
    s.write()
    write_expected(
        s.name,
        "C172SP: right tank leaking 2 gal/min from T+30s. Imbalance grows "
        "through the 10% advisory threshold to the 20% warning threshold.",
        "c172sp",
        [
            {"rule_id": "R005", "severity": "Warning",
             "text_key": "R005_FUEL_IMBALANCE_WARNING",
             "earliest_sec": 100, "latest_sec": 260, "required": True},
        ],
        ["R001", "R002", "R003", "R004", "R006", "R007", "R008"])


def r006_icing():
    s = Scenario("R006_IcingEncounter_C172", 240)
    for t in range(0, s.duration + 1, s.step):
        in_cloud = 1 if t >= 30 else 0
        ice = ramp(t, 60, 0, 6, ceil=20)       # 6%/min accumulation
        pitot = ramp(t, 60, 0, 2, ceil=10)
        c172_baseline(s, t, oat=-5, in_cloud=in_cloud,
                      struct_ice=ice, pitot_ice=pitot)
    s.write()
    write_expected(
        s.name,
        "C172SP: enters cloud at -5C at T+30s, structural ice accumulating "
        "6%/min from T+60s. Structural ice warning at the 5% threshold.",
        "c172sp",
        [
            {"rule_id": "R006", "severity": "Warning",
             "text_key": "R006_STRUCTURAL_ICE_WARNING",
             "earliest_sec": 100, "latest_sec": 180, "required": True},
        ],
        ["R001", "R002", "R003", "R004", "R005", "R007", "R008"])


def r007_electrical():
    s = Scenario("R007_ElectricalFailure_C172", 300)
    for t in range(0, s.duration + 1, s.step):
        # Alternator failure: bus voltage decays 0.5 V/min from T+30
        volts = ramp(t, 30, 14.1, -0.5, floor=11.5)
        c172_baseline(s, t, volts=volts)
    s.write()
    write_expected(
        s.name,
        "C172SP: alternator failure at T+30s, main bus decaying 0.5 V/min. "
        "Trend warning once the 120s window confirms the decay; critical "
        "when the bus passes the 12.0V minimum (~T+282s).",
        "c172sp",
        [
            {"rule_id": "R007", "severity": "Warning",
             "text_key": "R007_MAIN_BUS_TREND_WARNING",
             "earliest_sec": 100, "latest_sec": 240, "required": True},
            {"rule_id": "R007", "severity": "Critical",
             "text_key": "R007_MAIN_BUS_CRITICAL",
             "earliest_sec": 250, "latest_sec": 300, "required": True},
        ],
        ["R001", "R002", "R003", "R004", "R005", "R006", "R008"])


def r008_vacuum():
    s = Scenario("R008_VacuumFailure_C172", 180)
    for t in range(0, s.duration + 1, s.step):
        # Vacuum pump failing in IMC: suction bleeds off from T+30
        suction = ramp(t, 30, 5.0, -1.2, floor=1.5)
        c172_baseline(s, t, suction=suction, in_cloud=1, oat=8)
    s.write()
    write_expected(
        s.name,
        "C172SP in IMC: vacuum pump failing from T+30s, suction bleeding "
        "off at 1.2 inHg/min. Advisory below the normal range, warning when "
        "below the 3.5 inHg gyro-reliability minimum (IMC keeps severity "
        "at warning).",
        "c172sp",
        [
            {"rule_id": "R008", "severity": "Warning",
             "text_key": "R008_VACUUM_LOW",
             "earliest_sec": 95, "latest_sec": 180, "required": True},
        ],
        ["R001", "R002", "R003", "R004", "R005", "R006", "R007"])


if __name__ == "__main__":
    os.makedirs(os.path.join(OUT, "expected"), exist_ok=True)
    normal_c172()
    normal_baron()
    r002_power_trim()
    r003_cht_trend()
    r004_oil_pressure()
    r005_fuel_imbalance()
    r006_icing()
    r007_electrical()
    r008_vacuum()
    print("done")

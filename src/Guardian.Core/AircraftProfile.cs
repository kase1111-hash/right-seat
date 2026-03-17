using System.Text.Json.Serialization;

namespace Guardian.Core;

/// <summary>
/// Defines normal operating ranges for a specific aircraft type.
/// Loaded from JSON profile files. All values in pilot-friendly units
/// as authored; the profile loader converts to native SimConnect units on load.
/// </summary>
public sealed class AircraftProfile
{
    [JsonPropertyName("aircraft_id")]
    public string AircraftId { get; set; } = "";

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("engine_type")]
    public string EngineType { get; set; } = "piston";

    [JsonPropertyName("engine_count")]
    public int EngineCount { get; set; } = 1;

    [JsonPropertyName("fuel")]
    public FuelProfile Fuel { get; set; } = new();

    [JsonPropertyName("engine")]
    public EngineProfile Engine { get; set; } = new();

    [JsonPropertyName("electrical")]
    public ElectricalProfile Electrical { get; set; } = new();

    [JsonPropertyName("vacuum")]
    public VacuumProfile Vacuum { get; set; } = new();

    [JsonPropertyName("performance")]
    public PerformanceProfile Performance { get; set; } = new();

    [JsonPropertyName("trim")]
    public TrimProfile Trim { get; set; } = new();

    [JsonPropertyName("icing")]
    public IcingProfile Icing { get; set; } = new();

    /// <summary>
    /// Optional list of exact MSFS aircraft title strings that match this profile.
    /// Used for profile matching when connecting to the sim.
    /// </summary>
    [JsonPropertyName("msfs_titles")]
    public List<string> MsfsTitles { get; set; } = new();
}

public sealed class FuelProfile
{
    [JsonPropertyName("tank_count")]
    public int TankCount { get; set; } = 2;

    [JsonPropertyName("tank_names")]
    public List<string> TankNames { get; set; } = new();

    [JsonPropertyName("total_capacity_gal")]
    public double TotalCapacityGal { get; set; }

    [JsonPropertyName("usable_capacity_gal")]
    public double UsableCapacityGal { get; set; }

    [JsonPropertyName("selector_positions")]
    public List<string> SelectorPositions { get; set; } = new();

    [JsonPropertyName("normal_cruise_selector")]
    public string NormalCruiseSelector { get; set; } = "";

    [JsonPropertyName("imbalance_advisory_pct")]
    public double ImbalanceAdvisoryPct { get; set; } = 10;

    [JsonPropertyName("imbalance_warning_pct")]
    public double ImbalanceWarningPct { get; set; } = 20;

    [JsonPropertyName("minimum_fuel_warning_gal")]
    public double MinimumFuelWarningGal { get; set; } = 8;
}

public sealed class EngineProfile
{
    [JsonPropertyName("rpm_range")]
    public double[] RpmRange { get; set; } = [600, 2700];

    [JsonPropertyName("rpm_cruise_typical")]
    public double[] RpmCruiseTypical { get; set; } = [2200, 2500];

    [JsonPropertyName("manifold_pressure_range_inhg")]
    public double[] ManifoldPressureRangeInhg { get; set; } = [12, 30];

    // ── CHT (stored in Fahrenheit in JSON, converted to Rankine on load) ──

    [JsonPropertyName("cht_normal_range_f")]
    public double[] ChtNormalRangeF { get; set; } = [200, 450];

    [JsonPropertyName("cht_redline_f")]
    public double ChtRedlineF { get; set; } = 500;

    [JsonPropertyName("cht_trend_advisory_f_per_min")]
    public double ChtTrendAdvisoryFPerMin { get; set; } = 5;

    [JsonPropertyName("cht_trend_warning_f_per_min")]
    public double ChtTrendWarningFPerMin { get; set; } = 10;

    // ── EGT ──

    [JsonPropertyName("egt_normal_range_f")]
    public double[] EgtNormalRangeF { get; set; } = [1100, 1500];

    [JsonPropertyName("egt_redline_f")]
    public double EgtRedlineF { get; set; } = 1600;

    // ── Oil ──

    [JsonPropertyName("oil_pressure_normal_psi")]
    public double[] OilPressureNormalPsi { get; set; } = [60, 90];

    [JsonPropertyName("oil_pressure_minimum_psi")]
    public double OilPressureMinimumPsi { get; set; } = 25;

    [JsonPropertyName("oil_pressure_redline_psi")]
    public double OilPressureRedlinePsi { get; set; } = 115;

    [JsonPropertyName("oil_pressure_drop_rate_warning_psi_per_min")]
    public double OilPressureDropRateWarningPsiPerMin { get; set; } = 10;

    [JsonPropertyName("oil_temp_normal_range_f")]
    public double[] OilTempNormalRangeF { get; set; } = [100, 245];

    [JsonPropertyName("oil_temp_redline_f")]
    public double OilTempRedlineF { get; set; } = 250;

    // ── Fuel flow ──

    [JsonPropertyName("fuel_flow_cruise_gph")]
    public double[] FuelFlowCruiseGph { get; set; } = [8, 12];

    // ── Converted values (populated by profile loader, not serialized) ──

    [JsonIgnore] public double ChtRedlineRankine { get; set; }
    [JsonIgnore] public double[] ChtNormalRangeRankine { get; set; } = new double[2];
    [JsonIgnore] public double EgtRedlineRankine { get; set; }
    [JsonIgnore] public double[] EgtNormalRangeRankine { get; set; } = new double[2];
    [JsonIgnore] public double[] OilTempNormalRangeRankine { get; set; } = new double[2];
    [JsonIgnore] public double OilTempRedlineRankine { get; set; }

    /// <summary>
    /// Rate thresholds converted to Rankine-per-second for direct comparison with buffer rate-of-change.
    /// </summary>
    [JsonIgnore] public double ChtTrendAdvisoryRankinePerSec { get; set; }
    [JsonIgnore] public double ChtTrendWarningRankinePerSec { get; set; }
    [JsonIgnore] public double OilPressureDropRateWarningPsiPerSec { get; set; }
}

public sealed class ElectricalProfile
{
    [JsonPropertyName("main_bus_normal_v")]
    public double[] MainBusNormalV { get; set; } = [13.5, 14.5];

    [JsonPropertyName("main_bus_minimum_v")]
    public double MainBusMinimumV { get; set; } = 12.0;

    [JsonPropertyName("battery_bus_minimum_v")]
    public double BatteryBusMinimumV { get; set; } = 11.0;
}

public sealed class VacuumProfile
{
    [JsonPropertyName("suction_normal_inhg")]
    public double[] SuctionNormalInhg { get; set; } = [4.5, 5.5];

    [JsonPropertyName("suction_minimum_inhg")]
    public double SuctionMinimumInhg { get; set; } = 3.5;
}

public sealed class PerformanceProfile
{
    [JsonPropertyName("vne_kias")]
    public double VneKias { get; set; }

    [JsonPropertyName("vno_kias")]
    public double VnoKias { get; set; }

    [JsonPropertyName("vs0_kias")]
    public double Vs0Kias { get; set; }

    [JsonPropertyName("vs1_kias")]
    public double Vs1Kias { get; set; }

    [JsonPropertyName("vfe_kias")]
    public double VfeKias { get; set; }

    [JsonPropertyName("normal_approach_kias")]
    public double[] NormalApproachKias { get; set; } = [65, 85];

    [JsonPropertyName("normal_climb_fpm")]
    public double[] NormalClimbFpm { get; set; } = [500, 800];
}

public sealed class TrimProfile
{
    [JsonPropertyName("asymmetric_threshold_pct")]
    public double AsymmetricThresholdPct { get; set; } = 15;
}

public sealed class IcingProfile
{
    [JsonPropertyName("has_anti_ice")]
    public bool HasAntiIce { get; set; }

    [JsonPropertyName("has_pitot_heat")]
    public bool HasPitotHeat { get; set; } = true;

    [JsonPropertyName("icing_oat_range_c")]
    public double[] IcingOatRangeC { get; set; } = [-20, 2];
}

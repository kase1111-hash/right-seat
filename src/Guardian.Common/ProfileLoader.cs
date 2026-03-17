using System.Text.Json;
using Guardian.Core;
using Serilog;

namespace Guardian.Common;

/// <summary>
/// Loads aircraft profiles from JSON files and performs unit conversions
/// from pilot-friendly units to native SimConnect units.
/// </summary>
public sealed class ProfileLoader
{
    private static readonly ILogger Log = Serilog.Log.ForContext<ProfileLoader>();

    private readonly Dictionary<string, AircraftProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AircraftProfile> _titleIndex = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Loads all .json profile files from the specified directory.
    /// </summary>
    public void LoadProfiles(string profilesDirectory)
    {
        if (!Directory.Exists(profilesDirectory))
        {
            Log.Warning("Profiles directory not found: {Directory}", profilesDirectory);
            return;
        }

        var files = Directory.GetFiles(profilesDirectory, "*.json");
        Log.Information("Loading {Count} aircraft profiles from {Directory}", files.Length, profilesDirectory);

        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var profile = JsonSerializer.Deserialize<AircraftProfile>(json);
                if (profile is null)
                {
                    Log.Warning("Failed to deserialize profile: {File}", file);
                    continue;
                }

                ConvertUnits(profile);
                _profiles[profile.AircraftId] = profile;

                foreach (var title in profile.MsfsTitles)
                {
                    _titleIndex[title] = profile;
                }

                Log.Information("Loaded profile: {Id} ({Name})", profile.AircraftId, profile.DisplayName);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load profile: {File}", file);
            }
        }
    }

    /// <summary>
    /// Matches an MSFS aircraft title to a loaded profile.
    /// Tries: exact title match → partial match → generic fallback by engine count/type.
    /// </summary>
    public AircraftProfile? MatchProfile(string msfsTitle, int engineCount, string engineType)
    {
        // 1. Exact match on MSFS title
        if (_titleIndex.TryGetValue(msfsTitle, out var exact))
        {
            Log.Information("Profile matched (exact): {Title} → {Id}", msfsTitle, exact.AircraftId);
            return exact;
        }

        // 2. Partial match (normalized string comparison)
        var normalizedTitle = msfsTitle.ToLowerInvariant();
        foreach (var (title, profile) in _titleIndex)
        {
            if (normalizedTitle.Contains(title.ToLowerInvariant()) ||
                title.ToLowerInvariant().Contains(normalizedTitle))
            {
                Log.Information("Profile matched (partial): {Title} → {Id}", msfsTitle, profile.AircraftId);
                return profile;
            }
        }

        // Also try matching against aircraft_id and display_name
        foreach (var profile in _profiles.Values)
        {
            if (normalizedTitle.Contains(profile.AircraftId.ToLowerInvariant()) ||
                normalizedTitle.Contains(profile.DisplayName.ToLowerInvariant()))
            {
                Log.Information("Profile matched (fuzzy): {Title} → {Id}", msfsTitle, profile.AircraftId);
                return profile;
            }
        }

        // 3. Generic fallback by engine count and type
        var genericId = engineCount > 1
            ? "generic_twin_piston"
            : "generic_single_piston";

        if (_profiles.TryGetValue(genericId, out var generic))
        {
            Log.Warning("No specific profile for {Title}. Using generic: {Id}", msfsTitle, genericId);
            return generic;
        }

        // 4. No match at all
        Log.Warning("No profile match for {Title}. Running in limited mode.", msfsTitle);
        return null;
    }

    /// <summary>
    /// Converts pilot-friendly units in the profile to native SimConnect units.
    /// Fahrenheit → Rankine for temperatures, per-minute rates → per-second.
    /// </summary>
    internal static void ConvertUnits(AircraftProfile profile)
    {
        var eng = profile.Engine;

        // CHT: Fahrenheit → Rankine
        eng.ChtRedlineRankine = UnitsConverter.FahrenheitToRankine(eng.ChtRedlineF);
        eng.ChtNormalRangeRankine = new[]
        {
            UnitsConverter.FahrenheitToRankine(eng.ChtNormalRangeF[0]),
            UnitsConverter.FahrenheitToRankine(eng.ChtNormalRangeF[1]),
        };

        // EGT: Fahrenheit → Rankine
        eng.EgtRedlineRankine = UnitsConverter.FahrenheitToRankine(eng.EgtRedlineF);
        eng.EgtNormalRangeRankine = new[]
        {
            UnitsConverter.FahrenheitToRankine(eng.EgtNormalRangeF[0]),
            UnitsConverter.FahrenheitToRankine(eng.EgtNormalRangeF[1]),
        };

        // Oil temp: Fahrenheit → Rankine
        eng.OilTempRedlineRankine = UnitsConverter.FahrenheitToRankine(eng.OilTempRedlineF);
        eng.OilTempNormalRangeRankine = new[]
        {
            UnitsConverter.FahrenheitToRankine(eng.OilTempNormalRangeF[0]),
            UnitsConverter.FahrenheitToRankine(eng.OilTempNormalRangeF[1]),
        };

        // Rate thresholds: per-minute → per-second
        // CHT trend rates are in F/min. Since Rankine and Fahrenheit have the same scale factor
        // (a delta of 1°F = a delta of 1°R), we just convert per-min to per-sec.
        eng.ChtTrendAdvisoryRankinePerSec = UnitsConverter.PerMinuteToPerSecond(eng.ChtTrendAdvisoryFPerMin);
        eng.ChtTrendWarningRankinePerSec = UnitsConverter.PerMinuteToPerSecond(eng.ChtTrendWarningFPerMin);

        // Oil pressure drop rate: PSI/min → PSI/sec
        eng.OilPressureDropRateWarningPsiPerSec = UnitsConverter.PerMinuteToPerSecond(eng.OilPressureDropRateWarningPsiPerMin);
    }

    public IReadOnlyDictionary<string, AircraftProfile> Profiles => _profiles;
}

using Guardian.Common;
using Guardian.Core;
using Xunit;

namespace Guardian.Core.Tests;

public class ProfileLoaderTests
{
    [Fact]
    public void ConvertUnits_ChtRedline_FahrenheitToRankine()
    {
        var profile = new AircraftProfile
        {
            Engine = new EngineProfile { ChtRedlineF = 500 }
        };

        ProfileLoader.ConvertUnits(profile);

        // 500°F = 959.67°R
        Assert.Equal(959.67, profile.Engine.ChtRedlineRankine, 0.01);
    }

    [Fact]
    public void ConvertUnits_ChtNormalRange_ConvertsBothBounds()
    {
        var profile = new AircraftProfile
        {
            Engine = new EngineProfile { ChtNormalRangeF = [200, 450] }
        };

        ProfileLoader.ConvertUnits(profile);

        // 200°F = 659.67°R, 450°F = 909.67°R
        Assert.Equal(659.67, profile.Engine.ChtNormalRangeRankine[0], 0.01);
        Assert.Equal(909.67, profile.Engine.ChtNormalRangeRankine[1], 0.01);
    }

    [Fact]
    public void ConvertUnits_RateThresholds_PerMinToPerSec()
    {
        var profile = new AircraftProfile
        {
            Engine = new EngineProfile
            {
                ChtTrendAdvisoryFPerMin = 5,
                ChtTrendWarningFPerMin = 10,
                OilPressureDropRateWarningPsiPerMin = 10,
            }
        };

        ProfileLoader.ConvertUnits(profile);

        Assert.Equal(5.0 / 60.0, profile.Engine.ChtTrendAdvisoryRankinePerSec, 0.001);
        Assert.Equal(10.0 / 60.0, profile.Engine.ChtTrendWarningRankinePerSec, 0.001);
        Assert.Equal(10.0 / 60.0, profile.Engine.OilPressureDropRateWarningPsiPerSec, 0.001);
    }

    [Fact]
    public void ConvertUnits_EgtRedline_Converted()
    {
        var profile = new AircraftProfile
        {
            Engine = new EngineProfile { EgtRedlineF = 1600 }
        };

        ProfileLoader.ConvertUnits(profile);

        Assert.Equal(UnitsConverter.FahrenheitToRankine(1600), profile.Engine.EgtRedlineRankine, 0.01);
    }

    [Fact]
    public void ConvertUnits_OilTempRedline_Converted()
    {
        var profile = new AircraftProfile
        {
            Engine = new EngineProfile { OilTempRedlineF = 250 }
        };

        ProfileLoader.ConvertUnits(profile);

        Assert.Equal(UnitsConverter.FahrenheitToRankine(250), profile.Engine.OilTempRedlineRankine, 0.01);
    }
}

using Guardian.Core;
using Xunit;

namespace Guardian.Core.Tests;

public class UnitsConverterTests
{
    private const double Tolerance = 0.01;

    // ── Temperature ──

    [Fact]
    public void RankineToFahrenheit_FreezingPoint()
    {
        // 32°F = 491.67°R
        Assert.Equal(32.0, UnitsConverter.RankineToFahrenheit(491.67), Tolerance);
    }

    [Fact]
    public void RankineToFahrenheit_BoilingPoint()
    {
        // 212°F = 671.67°R
        Assert.Equal(212.0, UnitsConverter.RankineToFahrenheit(671.67), Tolerance);
    }

    [Fact]
    public void FahrenheitToRankine_RoundTrip()
    {
        double tempF = 450.0; // typical CHT value
        double rankine = UnitsConverter.FahrenheitToRankine(tempF);
        double backToF = UnitsConverter.RankineToFahrenheit(rankine);
        Assert.Equal(tempF, backToF, Tolerance);
    }

    [Fact]
    public void FahrenheitToRankine_AbsoluteZero()
    {
        // -459.67°F = 0°R
        Assert.Equal(0.0, UnitsConverter.FahrenheitToRankine(-459.67), Tolerance);
    }

    [Fact]
    public void CelsiusToFahrenheit_Freezing()
    {
        Assert.Equal(32.0, UnitsConverter.CelsiusToFahrenheit(0.0), Tolerance);
    }

    [Fact]
    public void CelsiusToFahrenheit_Boiling()
    {
        Assert.Equal(212.0, UnitsConverter.CelsiusToFahrenheit(100.0), Tolerance);
    }

    [Fact]
    public void FahrenheitToCelsius_RoundTrip()
    {
        double tempC = -15.0; // icing condition
        double f = UnitsConverter.CelsiusToFahrenheit(tempC);
        double backToC = UnitsConverter.FahrenheitToCelsius(f);
        Assert.Equal(tempC, backToC, Tolerance);
    }

    [Fact]
    public void CelsiusToRankine_Freezing()
    {
        // 0°C = 273.15K = 491.67°R
        Assert.Equal(491.67, UnitsConverter.CelsiusToRankine(0.0), Tolerance);
    }

    [Fact]
    public void RankineToCelsius_RoundTrip()
    {
        double tempC = 25.0;
        double rankine = UnitsConverter.CelsiusToRankine(tempC);
        double backToC = UnitsConverter.RankineToCelsius(rankine);
        Assert.Equal(tempC, backToC, Tolerance);
    }

    // ── Angles ──

    [Fact]
    public void RadiansToDegrees_Pi()
    {
        Assert.Equal(180.0, UnitsConverter.RadiansToDegrees(Math.PI), Tolerance);
    }

    [Fact]
    public void RadiansToDegrees_HalfPi()
    {
        Assert.Equal(90.0, UnitsConverter.RadiansToDegrees(Math.PI / 2.0), Tolerance);
    }

    [Fact]
    public void DegreesToRadians_RoundTrip()
    {
        double degrees = 45.0;
        double radians = UnitsConverter.DegreesToRadians(degrees);
        double backToDeg = UnitsConverter.RadiansToDegrees(radians);
        Assert.Equal(degrees, backToDeg, Tolerance);
    }

    // ── Rate conversions ──

    [Fact]
    public void PerSecondToPerMinute()
    {
        // 5°F/min = 5/60 °F/sec
        double perSec = 5.0 / 60.0;
        Assert.Equal(5.0, UnitsConverter.PerSecondToPerMinute(perSec), Tolerance);
    }

    [Fact]
    public void PerMinuteToPerSecond()
    {
        Assert.Equal(10.0 / 60.0, UnitsConverter.PerMinuteToPerSecond(10.0), Tolerance);
    }

    // ── Pressure ──

    [Fact]
    public void InHgToPsi_StandardAtmosphere()
    {
        // 29.92 inHg ≈ 14.696 PSI
        Assert.Equal(14.696, UnitsConverter.InHgToPsi(29.92), 0.05);
    }

    [Fact]
    public void PsiToInHg_RoundTrip()
    {
        double psi = 60.0;
        double inHg = UnitsConverter.PsiToInHg(psi);
        double backToPsi = UnitsConverter.InHgToPsi(inHg);
        Assert.Equal(psi, backToPsi, Tolerance);
    }
}

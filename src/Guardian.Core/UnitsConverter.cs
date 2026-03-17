namespace Guardian.Core;

/// <summary>
/// Conversion utilities between SimConnect native units and pilot-friendly units.
/// Detection rules work in native SimConnect units. The presentation layer and
/// aircraft profiles use pilot-friendly units. This converter bridges the gap.
/// </summary>
public static class UnitsConverter
{
    // ── Temperature ──

    /// <summary>Convert Rankine to Fahrenheit. SimConnect reports temps in Rankine.</summary>
    public static double RankineToFahrenheit(double rankine) => rankine - 459.67;

    /// <summary>Convert Fahrenheit to Rankine (for profile loading).</summary>
    public static double FahrenheitToRankine(double fahrenheit) => fahrenheit + 459.67;

    /// <summary>Convert Celsius to Fahrenheit.</summary>
    public static double CelsiusToFahrenheit(double celsius) => celsius * 9.0 / 5.0 + 32.0;

    /// <summary>Convert Fahrenheit to Celsius.</summary>
    public static double FahrenheitToCelsius(double fahrenheit) => (fahrenheit - 32.0) * 5.0 / 9.0;

    /// <summary>Convert Celsius to Rankine.</summary>
    public static double CelsiusToRankine(double celsius) => (celsius + 273.15) * 9.0 / 5.0;

    /// <summary>Convert Rankine to Celsius.</summary>
    public static double RankineToCelsius(double rankine) => rankine * 5.0 / 9.0 - 273.15;

    // ── Angles ──

    /// <summary>Convert radians to degrees.</summary>
    public static double RadiansToDegrees(double radians) => radians * (180.0 / Math.PI);

    /// <summary>Convert degrees to radians.</summary>
    public static double DegreesToRadians(double degrees) => degrees * (Math.PI / 180.0);

    // ── Rate conversions ──

    /// <summary>Convert a per-second rate to per-minute rate.</summary>
    public static double PerSecondToPerMinute(double perSecond) => perSecond * 60.0;

    /// <summary>Convert a per-minute rate to per-second rate.</summary>
    public static double PerMinuteToPerSecond(double perMinute) => perMinute / 60.0;

    // ── Pressure ──

    /// <summary>Convert inHg to PSI.</summary>
    public static double InHgToPsi(double inHg) => inHg * 0.491154;

    /// <summary>Convert PSI to inHg.</summary>
    public static double PsiToInHg(double psi) => psi / 0.491154;
}

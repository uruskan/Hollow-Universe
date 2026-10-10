using System;

namespace Hollow.Core;

/// <summary>
/// Universe time is real UTC (ARCHITECTURE.md §6). Orbits are expressed relative to the J2000 epoch.
/// This helper reads the clock, so use it for presentation only; simulation receives time as a parameter.
/// </summary>
public static class UniverseTime
{
    /// <summary>The J2000.0 epoch (2000-01-01 12:00 TT) expressed in UTC.</summary>
    public static readonly DateTime J2000 = new(2000, 1, 1, 11, 58, 55, 816, DateTimeKind.Utc);

    /// <summary>Seconds since J2000 for a UTC instant.</summary>
    public static double SecondsSinceJ2000(DateTime utc) => (utc.ToUniversalTime() - J2000).TotalSeconds;

    /// <summary>Seconds since J2000 right now.</summary>
    public static double Now => SecondsSinceJ2000(DateTime.UtcNow);

    /// <summary>UTC instant for a number of seconds since J2000.</summary>
    public static DateTime ToUtc(double secondsSinceJ2000) => J2000.AddSeconds(secondsSinceJ2000);
}

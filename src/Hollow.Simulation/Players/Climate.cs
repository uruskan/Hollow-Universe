using System;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Surface;

namespace Hollow.Simulation.Players;

/// <summary>
/// The air temperature at a place and time (GROUND.md §6): <see cref="PlanetClimate"/> on a real surface,
/// <see cref="StandInClimate"/> on the stand-in.
/// </summary>
public interface IClimate
{
    /// <summary>Air temperature in °C at a zone-frame point, at a time in Unix seconds.</summary>
    float TemperatureAt(Vector3 position, double unixSeconds);
}

/// <summary>
/// The real planet's air (GROUND.md §6.1): the planet's annual mean temperature at the place (latitude and height,
/// <see cref="PlanetSurface.MeanTemperature"/>) and a day-night swing that follows the zone's sun at play speed
/// (<see cref="ZoneSpec.CreateSky"/>): warmest at two in the afternoon, coldest at two at night, local solar time. So it
/// is cold when it looks dark, on the server's clock as on every client's.
/// </summary>
public sealed class PlanetClimate : IClimate
{
    /// <summary>Unix seconds at J2000 (2000-01-01 11:58:55.816 UTC), the zero of universe time.</summary>
    public const double UnixJ2000 = 946_727_935.816;

    private readonly SurvivalTuning _tuning;
    private readonly PlanetSurface _surface;
    private readonly ZoneFrame _frame;
    private readonly SkyEphemeris _sky;
    private readonly int _sun;

    public PlanetClimate(SurvivalTuning tuning, PlanetSurface surface, ZoneFrame frame, SkyEphemeris sky)
    {
        _tuning = tuning;
        _surface = surface;
        _frame = frame;
        _sky = sky;
        _sun = sky.Sun.Index;
    }

    public float TemperatureAt(Vector3 position, double unixSeconds)
    {
        Vector3D body = _frame.ToBody(position);
        Vector3D direction = body.Normalized;
        double mean = _surface.MeanTemperature(direction, Math.Max(0, body.Length - _surface.Radius));
        double hours = _sky.LocalSolarHours(direction, _sun, unixSeconds - UnixJ2000);
        return (float)(mean + _tuning.ClimateDaySwing * DMath.Cos(DMath.TwoPi * (hours - 14) / 24));
    }
}

/// <summary>
/// The stand-in ground's climate in short (GROUND.md §3; survival.json, <c>climateStandIn</c>): a mean temperature, a
/// day-night swing over the compressed day, and air cooling with height (6.5 °C a kilometre).
/// </summary>
public sealed class StandInClimate : IClimate
{
    private readonly SurvivalTuning _tuning;
    private readonly ZoneFrame _frame;
    private readonly double _radius;

    public StandInClimate(SurvivalTuning tuning, ZoneFrame frame, double radius)
    {
        _tuning = tuning;
        _frame = frame;
        _radius = radius;
    }

    public float TemperatureAt(Vector3 position, double unixSeconds)
    {
        double day = Math.Max(60, _tuning.DayLengthSeconds);
        double phase = (unixSeconds % day) / day;
        // Warmest a little after noon (phase 0.5), coldest before dawn.
        double swing = -DMath.Cos(DMath.TwoPi * (phase - 0.08));
        double height = _frame.ToBody(position).Length - _radius;
        return (float)(_tuning.ClimateMeanCelsius + _tuning.ClimateDaySwing * swing - _tuning.ClimateLapsePerKilometre * height / 1000);
    }
}

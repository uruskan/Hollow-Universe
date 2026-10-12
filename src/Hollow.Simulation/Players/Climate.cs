using System;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Surface;

namespace Hollow.Simulation.Players;

/// <summary>
/// The air temperature at a place and time (GROUND.md §6): the planet package's climate will provide it from the
/// planet state (latitude, height, season, day); until then <see cref="StandInClimate"/> does.
/// </summary>
public interface IClimate
{
    /// <summary>Air temperature in °C at a zone-frame point, at a time in Unix seconds.</summary>
    float TemperatureAt(Vector3 position, double unixSeconds);
}

/// <summary>
/// The first world's climate in short (GROUND.md §3; survival.json, <c>climateStandIn</c>): a mean temperature, a
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

using System;
using Hollow.Core;
using Hollow.Galaxy.Systems;

namespace Hollow.Surface;

/// <summary>
/// Where everything in a planet's sky is at a universe time (GROUND.md §4.6): the planet's spin turns its body frame
/// within the system frame, and the existing orbit code (<see cref="StarSystem.PositionOf"/>) places the suns, moons
/// and planets. Deterministic (DMath), so the server can use it for day and night too.
/// </summary>
/// <remarks>
/// <para>Orientation: the spin axis leans from the system's +Y by the axial tilt, toward −X (a rotation about +Z, as
/// the system map draws it), and stays fixed in the system frame, so the seasons follow the orbit. The body frame
/// turns about that axis by 2π · t / <see cref="Body.RotationPeriod"/> (negative periods turn backwards), and at
/// J2000 its prime meridian lies in the plane of the axis and +X.</para>
/// <para><see cref="RotationTimeScale"/> speeds up the spin alone (1 = real); orbits and seasons stay real. It exists
/// for the open question of day length (GROUND.md §11) and is a client setting; changing it, or jumping the clock,
/// never makes the sky jump.</para>
/// </remarks>
public sealed class SkyEphemeris
{
    private readonly double _cosTilt;
    private readonly double _sinTilt;

    public SkyEphemeris(StarSystem system, Body planet)
    {
        System = system;
        Planet = planet;
        _cosTilt = DMath.Cos(planet.AxialTilt);
        _sinTilt = DMath.Sin(planet.AxialTilt);
    }

    public StarSystem System { get; }

    public Body Planet { get; }

    // The spin as turns since J2000: turns(t) = baseTurns + (t − baseTime) · scale / |period|. With the defaults
    // (0, 0, 1) that is t / |period|, the real rotation.
    private double _baseTime;
    private double _baseTurns;
    private double _scale = 1;

    /// <summary>How much faster than real the planet spins (1 = its real rotation).</summary>
    public double RotationTimeScale => _scale;

    /// <summary>
    /// Spins the planet <paramref name="scale"/> times faster than real from time <paramref name="now"/> on, keeping
    /// its orientation at that moment, so the sky doesn't jump when the scale changes.
    /// </summary>
    public void SetRotationTimeScale(double scale, double now)
    {
        if (!(scale > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }
        _baseTurns = Turns(now);
        _baseTime = now;
        _scale = scale;
    }

    /// <summary>
    /// The spin as it is set: turns since J2000 at <c>Time</c> (seconds since J2000), and how many times faster than
    /// real it turns from there (GROUND.md §8.2: the zone's clock, which the server sends to its clients).
    /// </summary>
    public (double Time, double Turns, double Scale) Spin => (_baseTime, _baseTurns, _scale);

    /// <summary>Turns since J2000 at a time (seconds since J2000), as the spin is set.</summary>
    public double TurnsAt(double t) => Turns(t);

    /// <summary>
    /// Sets the spin outright (a server's zone clock, GROUND.md §8.2): <paramref name="turns"/> since J2000 at
    /// <paramref name="time"/>, turning <paramref name="scale"/> times faster than real from there; 0 stops it.
    /// </summary>
    public void SetSpin(double time, double turns, double scale)
    {
        if (!(scale >= 0) || double.IsInfinity(scale) || !double.IsFinite(time) || !double.IsFinite(turns))
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }
        _baseTime = time;
        _baseTurns = turns;
        _scale = scale;
    }

    /// <summary>
    /// Turns the planet, from time <paramref name="t"/> on and at the speed it has, so that it is
    /// <paramref name="hours"/> o'clock local solar time at a direction (an admin's <c>time</c>, GROUND.md §8.2). Only
    /// the spin moves: the orbits, the seasons and the universe's time stay as they are.
    /// </summary>
    public void SetLocalSolarHours(Vector3D surfaceDirection, int sunIndex, double t, double hours)
    {
        double turns = Turns(t), scale = _scale;
        // How far an hour of the planet's clock turns it, measured: a solar day is not quite a turn, and a retrograde
        // spin runs the other way.
        double before = LocalSolarHours(surfaceDirection, sunIndex, t);
        SetSpin(t, turns + 1.0 / 24, scale);
        double perTurn = WrapHours(LocalSolarHours(surfaceDirection, sunIndex, t) - before) * 24;
        if (Math.Abs(perTurn) < 1)
        {
            perTurn = Planet.RotationPeriod < 0 ? -24 : 24;
        }
        for (int i = 0; i < 3; i++)
        {
            SetSpin(t, turns, scale);
            double error = WrapHours(hours - LocalSolarHours(surfaceDirection, sunIndex, t));
            if (Math.Abs(error) < 1e-7)
            {
                return;
            }
            turns += error / perTurn;
        }
        SetSpin(t, turns, scale);
    }

    // An hour difference in [−12, 12).
    private static double WrapHours(double h) => h - 24 * Math.Floor((h + 12) / 24);

    /// <summary>
    /// Keeps the spin in step with a jump of the clock by <paramref name="delta"/> seconds: the planet turns by that
    /// much real time whatever the scale, so a jump of an hour is an hour on its clock.
    /// </summary>
    public void JumpClock(double delta)
    {
        double period = Math.Abs(Planet.RotationPeriod);
        if (period > 0)
        {
            _baseTurns += delta * (1 - _scale) / period;
        }
    }

    private double Turns(double t)
    {
        double period = Math.Abs(Planet.RotationPeriod);
        return period == 0 ? 0 : _baseTurns + (t - _baseTime) * _scale / period;
    }

    /// <summary>The spin angle at a time (seconds since J2000), radians in [0, 2π) (or (−2π, 0] when retrograde).</summary>
    public double RotationAngle(double t)
    {
        double period = Planet.RotationPeriod;
        if (period == 0)
        {
            return 0;
        }
        double turns = Turns(t);
        double fraction = turns - Math.Floor(turns);
        return (period > 0 ? 1 : -1) * DMath.TwoPi * fraction;
    }

    /// <summary>A body-frame vector in the system frame at a time.</summary>
    public Vector3D ToSystem(Vector3D v, double t)
    {
        double a = RotationAngle(t), c = DMath.Cos(a), s = DMath.Sin(a);
        // Spin about +Y, then tilt about +Z.
        double x = c * v.X + s * v.Z, y = v.Y, z = -s * v.X + c * v.Z;
        return new Vector3D(_cosTilt * x - _sinTilt * y, _sinTilt * x + _cosTilt * y, z);
    }

    /// <summary>A system-frame vector in the body frame at a time.</summary>
    public Vector3D ToBody(Vector3D v, double t)
    {
        double a = RotationAngle(t), c = DMath.Cos(a), s = DMath.Sin(a);
        double x = _cosTilt * v.X + _sinTilt * v.Y, y = -_sinTilt * v.X + _cosTilt * v.Y, z = v.Z;
        return new Vector3D(c * x - s * z, y, s * x + c * z);
    }

    /// <summary>The planet's north pole in the system frame.</summary>
    public Vector3D NorthPole => new(-_sinTilt, _cosTilt, 0);

    /// <summary>A body's position relative to the planet's centre, in the planet's body frame (metres).</summary>
    public Vector3D Position(int bodyIndex, double t)
    {
        Vector3D relative = System.PositionOf(bodyIndex, t) - System.PositionOf(Planet.Index, t);
        return ToBody(relative, t);
    }

    /// <summary>The star the planet orbits (its nearest ancestor that is a star), or the system's primary.</summary>
    public Body Sun
    {
        get
        {
            int i = Planet.Parent;
            while (i >= 0)
            {
                if (System.Bodies[i].Kind == BodyKind.Star)
                {
                    return System.Bodies[i];
                }
                i = System.Bodies[i].Parent;
            }
            return System.PrimaryStar;
        }
    }

    /// <summary>
    /// Local solar time at a body-frame direction on the surface, in hours from midnight on the planet's own clock
    /// (one solar day = 24 of these hours), for the given sun.
    /// </summary>
    public double LocalSolarHours(Vector3D surfaceDirection, int sunIndex, double t)
    {
        Vector3D sun = Position(sunIndex, t);
        double sunLongitude = DMath.Atan2(-sun.Z, sun.X);
        double longitude = DMath.Atan2(-surfaceDirection.Z, surfaceDirection.X);
        double hourAngle = DMath.WrapAngle(longitude - sunLongitude);
        if (Planet.RotationPeriod < 0)
        {
            hourAngle = -hourAngle;
        }
        double hours = 12 + hourAngle / DMath.TwoPi * 24;
        return hours - 24 * Math.Floor(hours / 24);
    }

    /// <summary>Elevation of a body above the horizon (radians) for an observer at a body-frame point.</summary>
    public double Elevation(int bodyIndex, Vector3D observer, double t)
    {
        Vector3D toBody = Position(bodyIndex, t) - observer;
        double up = Vector3D.Dot(toBody.Normalized, observer.Normalized);
        return DMath.Asin(Math.Clamp(up, -1, 1));
    }
}

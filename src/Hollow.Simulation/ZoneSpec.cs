using System;
using System.Globalization;
using Hollow.Galaxy.Systems;
using Hollow.Surface;

namespace Hollow.Simulation;

/// <summary>
/// Which surface zone this is (GROUND.md §4.2): the body, what the server and every client need to build the same
/// ground, and where on the body the zone's origin sits. The server sends it to each client on connect.
/// </summary>
public sealed record ZoneSpec
{
    /// <summary>The body: its system's address and its body path (ARCHITECTURE.md §7).</summary>
    public ulong SystemAddress { get; init; }

    public uint BodyPath { get; init; }

    /// <summary>Mean radius in metres.</summary>
    public double Radius { get; init; }

    /// <summary>Surface gravity in g.</summary>
    public double SurfaceGravity { get; init; }

    /// <summary>Seed of the surface generator for this body.</summary>
    public ulong SurfaceSeed { get; init; }

    /// <summary>The zone's origin, in degrees.</summary>
    public double LatitudeDegrees { get; init; }

    public double LongitudeDegrees { get; init; }

    /// <summary>
    /// The air (G6, ships' drag): its density at the body's mean radius in kg/m³ and its scale height in metres. A body
    /// without air has 0. Earth's by default (tests and stand-ins).
    /// </summary>
    public double AirDensity { get; init; } = 1.225;

    public double AirScaleHeight { get; init; } = 8500;

    /// <summary>
    /// The air of a body with a surface pressure (atmospheres), a mean temperature (K) and gravity (g), as an ideal gas
    /// of air's molar mass (nitrogen and oxygen; heavier air is denser, which waits for its first world).
    /// </summary>
    public static (double Density, double ScaleHeight) Air(double pressureAtmospheres, double temperatureKelvin, double gravityG)
    {
        const double molarMass = 0.029, gasConstant = 8.314462618;
        double temperature = Math.Max(temperatureKelvin, 30);
        double density = Math.Max(pressureAtmospheres, 0) * 101_325 * molarMass / (gasConstant * temperature);
        double scaleHeight = gasConstant * temperature / (molarMass * Math.Max(gravityG, 0.01) * 9.80665);
        return (density, scaleHeight);
    }

    /// <summary>
    /// The stand-in ground (a hilly sphere without a sea, and stand-in natural objects) instead of the body's real
    /// surface: for tests and benchmarks that want a plain, predictable world. Sent to clients with the zone.
    /// </summary>
    public bool StandIn { get; init; }

    /// <summary>
    /// How much faster than real the planet turns for play (GROUND.md §1: 24, so the first world's 34.9-hour day
    /// lasts 87 minutes), counted from J2000 so that the server and every client agree on the time of day. Orbits and
    /// seasons stay real. 1 is the planet's real rotation.
    /// </summary>
    public double RotationTimeScale { get; init; } = DefaultRotationTimeScale;

    /// <summary>The compressed day of play (GROUND.md §1).</summary>
    public const double DefaultRotationTimeScale = 24;

    /// <summary>
    /// The planet's sky for this zone at play speed: <see cref="RotationTimeScale"/> from J2000, so a universe time
    /// gives the same time of day on the server and every client. Null for bodies the universe doesn't know.
    /// </summary>
    public SkyEphemeris? CreateSky()
    {
        if (PlanetSurfaces.Find(SystemAddress, BodyPath) is not { } found)
        {
            return null;
        }
        var sky = new SkyEphemeris(found.System, found.Body);
        sky.SetRotationTimeScale(RotationTimeScale, 0);
        return sky;
    }

    /// <summary>
    /// The first world (GROUND.md §3) at a latitude and longitude (the server's options; by default the landing site,
    /// <see cref="Hollow.Surface.FirstWorld.LandingLatitude"/>), or null if the generator no longer makes it.
    /// </summary>
    public static ZoneSpec? FirstWorld(Galaxy.Galaxy galaxy, double latitudeDegrees, double longitudeDegrees)
    {
        (StarSystem System, Body Planet)? found = Hollow.Surface.FirstWorld.Find(galaxy);
        if (found == null)
        {
            return null;
        }
        (StarSystem system, Body planet) = found.Value;
        (double density, double scaleHeight) = Air(planet.SurfacePressure, planet.Temperature, planet.SurfaceGravity);
        return new ZoneSpec
        {
            AirDensity = density,
            AirScaleHeight = scaleHeight,
            SystemAddress = system.Address.Value,
            BodyPath = planet.Path,
            Radius = planet.Radius,
            SurfaceGravity = planet.SurfaceGravity,
            SurfaceSeed = Core.Hash64.Of(system.Address.Value, planet.Path),
            LatitudeDegrees = latitudeDegrees,
            LongitudeDegrees = longitudeDegrees,
        };
    }

    /// <summary>
    /// The ground of this zone's body. <b>The one place that picks the surface</b> (GROUND.md §4.6): the body's real
    /// surface (<see cref="PlanetSurface"/>, shared within the process), or the stand-in sphere for
    /// <see cref="StandIn"/> zones and bodies the universe doesn't know.
    /// </summary>
    public ISurface CreateSurface() =>
        !StandIn && PlanetSurfaces.Find(SystemAddress, BodyPath) is { } real ? real.Surface : new SphereSurface(Radius, SurfaceSeed);

    /// <summary>
    /// The natural objects of this zone's ground (trees, outcrops, pickups, bushes). <b>The one place that picks
    /// them</b> (GROUND.md §6.1): the planet's own scatter on a real surface, the stand-in placement on the stand-in.
    /// </summary>
    public Nature.INaturalObjects CreateNature(ISurface surface, ZoneFrame frame, Content.GameContent content) =>
        surface is PlanetSurface planet
            ? new Nature.ScatterNaturalObjects(planet, frame, content)
            : new Nature.StandInNaturalObjects(surface, frame, content, Core.Hash64.Of(SurfaceSeed, 0x7A7));

    /// <summary>The zone's frame on a surface built by <see cref="CreateSurface"/>.</summary>
    public ZoneFrame CreateFrame(ISurface surface) =>
        ZoneFrame.At(surface, LatitudeDegrees * Math.PI / 180, LongitudeDegrees * Math.PI / 180);

    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"body {SystemAddress:X16}/{BodyPath:X8}, radius {Radius / 1000:0} km, {SurfaceGravity:0.00} g, at {LatitudeDegrees:0.####}°, {LongitudeDegrees:0.####}°, {(StandIn ? "stand-in ground" : "day x" + RotationTimeScale.ToString("0.##", CultureInfo.InvariantCulture))}");
}

using System;
using System.Globalization;
using Hollow.Galaxy.Systems;
using Hollow.Surface;

namespace Hollow.Simulation;

/// <summary>Which ground a zone stands on (<see cref="ZoneSpec.Ground"/>; on the wire, a byte: append only).</summary>
public enum GroundKind : byte
{
    /// <summary>The stand-in world of S3 (docs/WHOLE_WORLD.md §4): the default until the owner approves S4.</summary>
    StandIn = 0,

    /// <summary>The walking-scale ground of S4 (docs/WALKING_GROUND.md): the server's <c>--ground walking</c>.</summary>
    Walking = 1,
}

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
    /// Marks a zone on the stand-in ground (<see cref="StandInWorld"/>: continents, a sea and climate bands, with stand-in
    /// natural objects). Until S4 makes the real ground at walking scale every zone stands on it
    /// (<see cref="CreateSurface"/>), whatever this says; it is sent to clients with the zone.
    /// </summary>
    public bool StandIn { get; init; }

    /// <summary>
    /// The ground the zone stands on: the stand-in by default; the walking-scale ground (docs/WALKING_GROUND.md) when the
    /// server says so (<c>--ground walking</c>), sent to clients with the zone so they build the same.
    /// </summary>
    public GroundKind Ground { get; init; }

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
            // Real-equivalent heights (docs/WHOLE_WORLD.md §6.5): the climb to the leaving height thins the air as the
            // climb to the old 15 km ceiling did.
            AirScaleHeight = scaleHeight * (LeavingRadii * FirstWorldRadius / RealCeiling),
            SystemAddress = system.Address.Value,
            BodyPath = planet.Path,
            Radius = FirstWorldRadius,
            SurfaceGravity = planet.SurfaceGravity,
            SurfaceSeed = Core.Hash64.Of(system.Address.Value, planet.Path),
            LatitudeDegrees = latitudeDegrees,
            LongitudeDegrees = longitudeDegrees,
            StandIn = true,
        };
    }

    /// <summary>
    /// The first world's game radius in metres (SCALE.md §3.1, docs/WHOLE_WORLD.md §1): a constant until S1's
    /// <c>GameScale</c> gives every body its own.
    /// </summary>
    public const double FirstWorldRadius = 2410;

    /// <summary>A ship leaves the world this many world radii above the ground (docs/WHOLE_WORLD.md decision 4).</summary>
    public const double LeavingRadii = 2;

    /// <summary>The ceiling ships had on the real-size first world, in metres: what real-equivalent heights compare with.</summary>
    public const double RealCeiling = 15_000;

    /// <summary>The unit direction of the spec's latitude and longitude (the landing site) in the body frame.</summary>
    public Core.Vector3D LandingDirection => ZoneFrame.Direction(LatitudeDegrees * Math.PI / 180, LongitudeDegrees * Math.PI / 180);

    /// <summary>
    /// The ground of this zone's body. <b>The one place that picks the surface</b> (GROUND.md §4.6). By default the
    /// stand-in world (docs/WHOLE_WORLD.md §4); with <see cref="GroundKind.Walking"/> the body's walking-scale ground
    /// (docs/WALKING_GROUND.md), shared by everything in the process (<see cref="WalkingWorlds"/>), or the stand-in if the
    /// universe doesn't know the body. The real <see cref="PlanetSurface"/> is built at its real radius, which a zone
    /// measured from its centre can't hold.
    /// </summary>
    public ISurface CreateSurface() =>
        Ground == GroundKind.Walking && WalkingWorlds.Find(SystemAddress, BodyPath, Radius) is { } walking
            ? walking
            : new StandInWorld(Radius, SurfaceSeed, LandingDirection);

    /// <summary>
    /// The natural objects of this zone's ground (trees, outcrops, pickups, bushes). <b>The one place that picks
    /// them</b> (GROUND.md §6.1): the ground's own scatter on a surface that has one (the real planet's, the
    /// walking-scale ground's at its level by size), the stand-in placement on the stand-in.
    /// </summary>
    public Nature.INaturalObjects CreateNature(ISurface surface, ZoneFrame frame, Content.GameContent content) =>
        surface is IScatterField field
            ? new Nature.ScatterNaturalObjects(field, frame, content)
            : new Nature.StandInNaturalObjects(surface, frame, content, Core.Hash64.Of(SurfaceSeed, 0x7A7), LandingDirection);

    /// <summary>
    /// The zone's frame: the body's own (docs/WHOLE_WORLD.md §3.1), origin at the centre, so one zone holds the whole
    /// world. The surface is not needed any more; the parameter stays for callers that build a zone's parts.
    /// </summary>
    public ZoneFrame CreateFrame(ISurface surface) => ZoneFrame.World;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"body {SystemAddress:X16}/{BodyPath:X8}, radius {Radius:0} m, {SurfaceGravity:0.00} g, landing at {LatitudeDegrees:0.####}°, {LongitudeDegrees:0.####}°, {(Ground == GroundKind.Walking ? "walking-scale ground" : StandIn ? "stand-in ground" : "day x" + RotationTimeScale.ToString("0.##", CultureInfo.InvariantCulture))}");
}

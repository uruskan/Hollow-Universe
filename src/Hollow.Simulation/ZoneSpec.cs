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
    /// The first world (GROUND.md §3) at a latitude and longitude, or null if the generator no longer makes it. The
    /// landing site is the planet package's to choose; until then the server takes it from its options.
    /// </summary>
    public static ZoneSpec? FirstWorld(Galaxy.Galaxy galaxy, double latitudeDegrees, double longitudeDegrees)
    {
        (StarSystem System, Body Planet)? found = Hollow.Surface.FirstWorld.Find(galaxy);
        if (found == null)
        {
            return null;
        }
        (StarSystem system, Body planet) = found.Value;
        return new ZoneSpec
        {
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
    /// The ground of this zone's body. <b>The one place that picks the surface:</b> the stand-in sphere until the planet
    /// package's real surface lands, then that (GROUND.md §4.6).
    /// </summary>
    public ISurface CreateSurface() => new SphereSurface(Radius, SurfaceSeed);

    /// <summary>
    /// The natural objects of this zone's ground (trees, outcrops, pickups, bushes). <b>The one place that picks
    /// them:</b> the stand-in placement until the planet package's scatter lands (GROUND.md §4.6).
    /// </summary>
    public Nature.INaturalObjects CreateNature(ISurface surface, ZoneFrame frame, Content.GameContent content) =>
        new Nature.StandInNaturalObjects(surface, frame, content, Core.Hash64.Of(SurfaceSeed, 0x7A7));

    /// <summary>The zone's frame on a surface built by <see cref="CreateSurface"/>.</summary>
    public ZoneFrame CreateFrame(ISurface surface) =>
        ZoneFrame.At(surface, LatitudeDegrees * Math.PI / 180, LongitudeDegrees * Math.PI / 180);

    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"body {SystemAddress:X16}/{BodyPath:X8}, radius {Radius / 1000:0} km, {SurfaceGravity:0.00} g, at {LatitudeDegrees:0.###}°, {LongitudeDegrees:0.###}°");
}

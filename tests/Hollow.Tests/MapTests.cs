using System;
using System.Linq;
using System.Numerics;
using Hollow.Client.Map;
using Hollow.Core;
using Hollow.Simulation;
using Hollow.Surface;

namespace Hollow.Tests;

/// <summary>The map of a whole world (docs/WHOLE_WORLD.md §11): its projection and inverse, its grid's names, its picture.</summary>
public static class MapTests
{
    private const double Radius = ZoneSpec.FirstWorldRadius;

    /// <summary>
    /// The globe becomes a chart and back without losing a point: every visible direction drawn on the screen reads back
    /// as itself, for a globe, Lambert's disc and the flat chart, about the equator and over a pole; the far side of a
    /// globe is hidden and the chart shows it; the land under the view is true to scale for every bend.
    /// </summary>
    [Test]
    public static void MapProjectionRoundTripsAndHidesTheFarSide()
    {
        var rng = new Rng(0x6A9);
        int checkedPoints = 0;
        foreach (Vector3D centre in new[] { new Vector3D(1, 0, 0), new Vector3D(0.3, 0.9, -0.2).Normalized, new Vector3D(0, 1, 0), new Vector3D(-0.5, -0.4, 0.7).Normalized })
        {
            foreach (double bend in new[] { 1.0, 0.8, 0.5, 0.2, 0.0 })
            {
                var p = new GlobeProjection(centre, new Vector3D(0, 1, 0.3), Radius, 0.2, bend);
                for (int i = 0; i < 400; i++)
                {
                    var d = new Vector3D(rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1);
                    if (d.LengthSquared < 0.01 || d.LengthSquared > 1)
                    {
                        continue;
                    }
                    d = d.Normalized;
                    double theta = DMath.Acos(Math.Clamp(Vector3D.Dot(d, p.Centre), -1, 1));
                    bool shown = p.ToScreen(d, out Vector2 screen);
                    Assert.Equal(theta <= p.Reach + 1e-9, shown, "drawn exactly within the map's reach");
                    // Close to the edge of the globe and to the antipode a pixel spans much of the world: test inside them.
                    if (!shown || theta > p.Reach - 0.05 || theta > Math.PI - 0.05)
                    {
                        continue;
                    }
                    Assert.True(p.ToDirection(screen, out Vector3D back), "a drawn point reads back");
                    double off = DMath.Acos(Math.Clamp(Vector3D.Dot(back, d), -1, 1)) * Radius;
                    Assert.True(off < 0.02, $"read back within 2 cm (bend {bend}, {off:0.0000} m off)");
                    checkedPoints++;
                }
                // True to scale at the centre: 10 m of ground is 10 m × the scale on screen.
                Vector3D east = Vector3D.Cross(p.Up, p.Centre);
                Assert.True(p.ToScreen(p.Centre * DMath.Cos(10 / Radius) + east * DMath.Sin(10 / Radius), out Vector2 ten), "10 m off the centre is shown");
                Assert.Near(10 * p.PixelsPerMetre, ten.X, 1e-4, "true to scale at the centre");
            }
        }
        Fixtures.Report($"{checkedPoints} points drawn and read back");

        // The far side: hidden on the globe, shown on the chart; the globe's edge.
        var globe = new GlobeProjection(new Vector3D(1, 0, 0), Vector3D.UnitY, Radius, 0.2, GlobeProjection.BendFor(GlobeCamera.WidestRadii * Radius, Radius));
        Assert.Near(1, globe.Bend, 1e-12, "the whole world is a globe");
        Vector3D near = ZoneFrame.Direction(0, 89 * DMath.DegToRad), far = ZoneFrame.Direction(0, 91 * DMath.DegToRad);
        Assert.True(globe.ToScreen(near, out _), "89° from the centre: on the near side");
        Assert.True(!globe.ToScreen(far, out _) && !globe.ToScreen(new Vector3D(-1, 0, 0), out _), "91° and the antipode: the far side, hidden");
        Assert.True(!globe.ToDirection(new Vector2((float)(1.01 * Radius * 0.2), 0), out _), "beyond the globe's edge is off the world");
        var chart = new GlobeProjection(new Vector3D(1, 0, 0), Vector3D.UnitY, Radius, 0.2, GlobeProjection.BendFor(GlobeCamera.ClosestAcross, Radius));
        Assert.Near(0, chart.Bend, 1e-12, "150 m across is a flat chart");
        Assert.True(chart.ToScreen(far, out _) && chart.ToScreen(new Vector3D(-0.999, 0.04, 0).Normalized, out _), "the chart shows the far side too");
        Assert.Near(Math.PI * Radius * 0.2, chart.EdgePixels, 1e-6, "the chart's edge is the antipode, half the world's girth away");
    }

    /// <summary>
    /// The camera: following puts the point in the middle with local north up; a drag keeps the ground under the pointer
    /// (on the chart and on the globe, over a pole); zooming keeps the ground under the pointer; left alone, north
    /// comes back up away from the poles.
    /// </summary>
    [Test]
    public static void MapCameraFollowsDragsAndZooms()
    {
        Vector3D landing = ZoneFrame.Direction(44.86 * DMath.DegToRad, -170 * DMath.DegToRad);
        var camera = new GlobeCamera(Radius, Vector3D.UnitY, 900);
        camera.Follow(landing * 2412);
        GlobeProjection p = camera.Projection(800);
        Assert.Near(0, (p.Centre - landing).Length, 1e-12, "the player in the middle");
        Vector2 north = p.NorthAt(p.Centre)!.Value;
        Assert.Near(0, north.X, 1e-5, "north up (x)");
        Assert.Near(-1, north.Y, 1e-5, "north up (y)");

        foreach ((double across, Vector2 from, Vector2 to) in new[] { (900.0, new Vector2(120, -40), new Vector2(-30, 80)), (5000.0, new Vector2(100, 50), new Vector2(160, -120)) })
        {
            camera.Across = across;
            p = camera.Projection(800);
            Assert.True(p.ToDirection(from, out Vector3D grabbed), "the pointer is on the world");
            camera.Drag(from, to, 800);
            p = camera.Projection(800);
            Assert.True(p.ToDirection(to, out Vector3D under), "still on the world");
            Assert.True((under - grabbed).Length * Radius < 0.01, "the grabbed ground stays under the pointer");
            Assert.True(!camera.Following, "a drag stops following");
        }
        // Over the north pole: drag the pole from below the middle to above it.
        camera.Across = 3000;
        camera.LookAt(ZoneFrame.Direction(80 * DMath.DegToRad, 0));
        for (int i = 0; i < 40; i++)
        {
            camera.Drag(new Vector2(0, -10), new Vector2(0, 10), 800);
        }
        double latitude = DMath.Asin(camera.Centre.Y) * DMath.RadToDeg;
        Assert.True(latitude < 80, $"dragged over the pole, the map goes on ({latitude:0.0}° now)");

        // Zoom about the pointer.
        camera.LookAt(landing);
        camera.Across = 2000;
        var pointer = new Vector2(150, 90);
        camera.Projection(800).ToDirection(pointer, out Vector3D before);
        camera.Zoom(0.5, pointer, 800);
        camera.Projection(800).ToDirection(pointer, out Vector3D after);
        Assert.True((before - after).Length * Radius < 0.01, "zooming keeps the ground under the pointer");
        Assert.Near(1000, camera.Across, 1e-9, "twice as close");
        camera.Across = 1;
        Assert.Near(GlobeCamera.ClosestAcross, camera.Across, 1e-9, "no closer than 150 m");

        // Turned by a drag at mid latitudes, the map settles back to north up.
        camera.LookAt(ZoneFrame.Direction(30 * DMath.DegToRad, 40 * DMath.DegToRad));
        camera.Across = 4000;
        camera.Drag(new Vector2(300, 300), new Vector2(-300, 200), 800);
        for (int i = 0; i < 120; i++)
        {
            camera.Settle(1 / 60.0);
        }
        north = camera.Projection(800).NorthAt(camera.Centre)!.Value;
        Assert.True(north.Y < -0.999f, $"north back up ({north.X:0.000}, {north.Y:0.000})");
    }

    /// <summary>
    /// The grid (decision 6): six faces A to F of 25 × 25 cells of about 150 m on the first world; known points' names; every
    /// cell's name reads back as itself and names its own middle; untrusted words never throw.
    /// </summary>
    [Test]
    public static void MapGridNamesCells()
    {
        int cells = WorldGrid.CellsPerFace(Radius, 150);
        Assert.Equal(25, cells, "cells a face side on the first world");
        Assert.Equal("A M12", WorldGrid.Name(ZoneFrame.Direction(0, 0), cells), "0°, 0°: face A's middle");
        Assert.Equal("B M12", WorldGrid.Name(ZoneFrame.Direction(0, Math.PI / 2), cells), "0°, 90° E: B");
        Assert.Equal("C M12", WorldGrid.Name(ZoneFrame.Direction(0, Math.PI), cells), "0°, 180°: C");
        Assert.Equal("D M12", WorldGrid.Name(ZoneFrame.Direction(0, -Math.PI / 2), cells), "0°, 90° W: D");
        Assert.Equal("E M12", WorldGrid.Name(Vector3D.UnitY, cells), "the north pole: E");
        Assert.Equal("F M12", WorldGrid.Name(-Vector3D.UnitY, cells), "the south pole: F");
        // On A, letters grow eastward and numbers southward, as on Rust's map: a little north-west of 0°, 0° is L11, south-east N13.
        Assert.Equal("A L11", WorldGrid.Name(ZoneFrame.Direction(3.5 * DMath.DegToRad, -3.5 * DMath.DegToRad), cells), "north-west of A's middle");
        Assert.Equal("A N13", WorldGrid.Name(ZoneFrame.Direction(-3.5 * DMath.DegToRad, 3.5 * DMath.DegToRad), cells), "south-east of it");
        Assert.Equal("A A0", WorldGrid.Name(CubeSphere.Direction(0, -0.99, 0.99), cells), "A's north-west corner");
        Assert.Equal("A Y24", WorldGrid.Name(CubeSphere.Direction(0, 0.99, -0.99), cells), "A's south-east corner");
        Assert.Equal("AZ", WorldGrid.Column(51), "two letters past Z");
        for (int face = 0; face < CubeSphere.FaceCount; face++)
        {
            for (int column = 0; column < cells; column++)
            {
                for (int row = 0; row < cells; row++)
                {
                    var cell = new WorldGrid.Cell(face, column, row);
                    string name = WorldGrid.Name(cell);
                    Assert.True(WorldGrid.TryParse(name, cells, out WorldGrid.Cell read) && read == cell, $"{name} reads back");
                    Assert.Equal(name, WorldGrid.Name(WorldGrid.Centre(cell, cells), cells), "a cell's middle lies in it");
                }
            }
        }
        Assert.True(WorldGrid.TryParse("ck12", cells, out WorldGrid.Cell c) && WorldGrid.Name(c) == "C K12", "CK12, any case, without the space");
        foreach (string bad in new[] { "", "G A1", "A 12", "A Z3", "A A25", "A AAA1", "Ada", "C K12345", "1 2", "B" })
        {
            Assert.True(!WorldGrid.TryParse(bad, cells, out _), $"'{bad}' is no cell");
        }
        // Cells keep about their size everywhere: the largest and smallest edge on the world.
        double shortest = double.MaxValue, longest = 0;
        for (int i = 0; i < cells; i++)
        {
            double edge = (WorldGrid.At(0, i, 0, cells) - WorldGrid.At(0, i + 1, 0, cells)).Length * Radius;
            double middle = (WorldGrid.At(0, i, 12, cells) - WorldGrid.At(0, i + 1, 12, cells)).Length * Radius;
            shortest = Math.Min(shortest, Math.Min(edge, middle));
            longest = Math.Max(longest, Math.Max(edge, middle));
        }
        Fixtures.Report($"cells' edges {shortest:0} … {longest:0} m");
        Assert.InRange(100, 160, shortest, "the shortest cell edge (m): at a face's corners");
        Assert.InRange(100, 160, longest, "the longest cell edge (m)");
    }

    /// <summary>
    /// The picture: six faces rendered from any surface by direction, the same every time; a surface without a sea is
    /// drawn, not black (WHOLE_WORLD.md §12); the cache key changes with the surface and its radius.
    /// </summary>
    [Test]
    public static void MapPictureIsTheWholeWorld()
    {
        var spec = new ZoneSpec { SystemAddress = 1, BodyPath = 2, Radius = Radius, SurfaceSeed = 77, LatitudeDegrees = 44.86, LongitudeDegrees = -170 };
        // This map-key fixture intentionally has a fabricated address; its surface is explicitly borrowed.
        ISurface standIn = new StandInWorld(spec.Radius, spec.SurfaceSeed, spec.LandingDirection);
        var world = new Simulation.Content.WorldData();
        var map = new ZoneMap(spec, standIn, ZoneFrame.World, world);
        Assert.Equal(25, map.Cells, "25 cells a face");
        MapPicture a = ZoneMapImage.Render(standIn, 48), b = ZoneMapImage.Render(standIn, 48);
        Assert.Equal(48 * 48 * 6 * 4, a.Rgba.Length, "six faces of 48 px");
        Assert.True(a.Rgba.AsSpan().SequenceEqual(b.Rgba), "the same picture every time");
        int sea = Count(a, (r, g, bl) => bl > r + 10), land = Count(a, (r, g, bl) => r > bl + 30);
        Fixtures.Report($"stand-in: {sea} sea and {land} land pixels of {48 * 48 * 6}");
        Assert.True(sea > 48 * 48 && land > 48 * 48, "sea and land on the stand-in world");

        var dry = new SphereSurface(Radius, 5);
        MapPicture plain = ZoneMapImage.Render(dry, 32);
        int dark = Count(plain, (r, g, bl) => r + g + bl < 120);
        Assert.Equal(0, dark, "a world without a sea is drawn, not black");
        var otherSpec = spec with { Radius = 3000 };
        Assert.True(new ZoneMap(otherSpec, new StandInWorld(otherSpec.Radius, otherSpec.SurfaceSeed, otherSpec.LandingDirection), ZoneFrame.World, world).CacheKey != map.CacheKey, "another radius, another picture");
        Assert.True(new ZoneMap(spec, dry, ZoneFrame.World, world).CacheKey != map.CacheKey, "another surface, another picture");
        Assert.Equal(map.CacheKey, new ZoneMap(spec, new StandInWorld(spec.Radius, spec.SurfaceSeed, spec.LandingDirection), ZoneFrame.World, world).CacheKey, "the same world, the same key");
        Vector3 ground = map.Ground(spec.LandingDirection);
        Assert.Near(Radius + standIn.HeightAt(spec.LandingDirection), ground.Length(), 0.01, "a marker lies on the ground");
        Assert.Equal(WorldGrid.Name(spec.LandingDirection, 25), map.GridName(ground), "the map names the cell of a point");
    }

    private static int Count(MapPicture picture, Func<int, int, int, bool> what)
    {
        int n = 0;
        for (int i = 0; i < picture.Rgba.Length; i += 4)
        {
            n += what(picture.Rgba[i], picture.Rgba[i + 1], picture.Rgba[i + 2]) ? 1 : 0;
        }
        return n;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Tests;

public static class CliffFormationTests
{
    [Test]
    public static void CliffFormationsAreDeterministicAndBounded()
    {
        var surface = new SlopingSurface(1.1);
        var allScales = new HashSet<double>();
        int groups = 0, parts = 0;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var placements = new CliffPlacements(surface, seed);
            var independent = new CliffPlacements(surface, seed);
            TileKey tile = TileKey.At(Vector3D.UnitX, placements.Level);
            IReadOnlyList<CliffPlacement> group = placements.FormationAt(tile);
            Assert.True(group.SequenceEqual(independent.FormationAt(tile)), "server/client generation agrees on every transform and stable part key");
            if (group.Count == 0) continue;
            groups++;
            parts += group.Count;
            Assert.True(group.Count <= CliffPlacements.MaximumParts, "a cell's formations have a bounded cost");
            Assert.Equal(group.Count, group.Select(p => p.Key).Distinct().Count(), "different members never alias a streaming/collision key");
            const int FaceParts = CliffPlacements.MaximumColumns * CliffPlacements.MaximumTiers;
            foreach (var face in group.GroupBy(p => p.Part / FaceParts))
                Assert.Equal(1, face.Select(p => CliffGeometry.All[p.Model].MaterialFamily).Distinct().Count(),
                    "a face's members belong to one compatible material family");
            Vector3D cell = tile.Centre * (surface.Radius + surface.HeightAt(tile.Centre));
            foreach (CliffPlacement piece in group)
            {
                allScales.Add(piece.Scale);
                Assert.Near(1, Vector3D.Dot(Vector3D.Cross(piece.Right, piece.Up), piece.Forward), 1e-10,
                    "authored facing preserves a right-handed basis");
                Assert.True(CliffGeometry.All[piece.Model].Positions.All(p => p.Length * piece.Scale <= CliffPlacements.MaximumExtent),
                    "every authoritative vertex remains inside the streaming radius");
                Assert.True((cell.Normalized - piece.Position.Normalized).Length * surface.Radius <= CliffPlacements.MaximumOffset,
                    "members stay within their cell's discovery reach across the ground");
            }
        }
        Assert.Equal(40, groups, "a continuous dry slope is dressed under every seed: no random cell is left bare");
        Assert.True(allScales.Count > 10, "composition varies physical size across stable seeded formations");
        Fixtures.Report($"{groups} synthetic fitted formations, {parts} parts, {allScales.Count} scales");
    }

    [Test]
    public static void CliffFormationSweepBuildsTheCachedFormationWithoutTheCache()
    {
        var surface = new SlopingSurface(1.1);
        var placements = new CliffPlacements(surface, 7);
        TileKey tile = TileKey.At(Vector3D.UnitX, placements.Level);
        CliffPlacement[] swept = placements.Generate(tile);
        Assert.Equal(0, placements.CacheDiagnostics.Entries, "the far layer's sweep keeps its own results, never the shared cache's");
        Assert.True(swept.Length > 0, "the slope is dressed");
        Assert.True(swept.SequenceEqual(placements.FormationAt(tile)), "the sweep builds exactly the formation collision and the near layer read");
        Assert.Equal(0, placements.Generate(tile.Parent).Length, "only cells of the formation level hold formations");
        Assert.True(ReferenceEquals(surface, placements.Surface), "the far layer finds the ground its formations stand on");
    }

    [Test]
    public static void CliffFormationsDressANarrowEscarpmentAlongItsLength()
    {
        // A tableland edge as the walking world makes it: 10 m of rise in 3 m, straight through the cell.
        var probe = new CliffPlacements(new SlopingSurface(0), 1);
        TileKey tile = TileKey.At(Vector3D.UnitX, probe.Level);
        Vector3D centre = tile.Centre;
        var surface = new EscarpmentSurface(centre.Z / centre.X);
        int covered = 0, steep = 0, walls = 0, wallsCovered = 0, members = 0;
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var placements = new CliffPlacements(surface, seed);
            members += placements.FormationAt(tile).Count;
            foreach (CliffFaceSample sample in placements.Report(tile))
            {
                steep++;
                if (sample.Covered) covered++;
                if (sample.Wall) { walls++; if (sample.Covered) wallsCovered++; }
                Assert.Equal("mountain-face", sample.Recipe, "a surface without relief fields takes the mountain recipe");
                Assert.Equal(0.0, sample.Rise, "a 10 m step is no wall over the survey's 10 m baseline, and only walls report a rise");
            }
        }
        double share = steep == 0 ? 0 : (double)covered / steep;
        Fixtures.Report($"escarpment: {steep} face samples, {share:P0} inside a scan; {walls} walls, {wallsCovered} covered; {members} members over 8 seeds");
        Assert.True(steep >= 8 * 12, "the survey finds the escarpment all along the cell, not at one random point");
        Assert.True(share >= 0.7, $"most of the escarpment carries rock ({share:P0}); the old single candidate dressed under a fifth of such cells");
    }

    [Test]
    public static void CliffWallsReportTheRiseOfTheirFace()
    {
        TileKey tile = TileKey.At(Vector3D.UnitX, new CliffPlacements(new SlopingSurface(0), 1).Level);
        Vector3D centre = tile.Centre;
        var surface = new EscarpmentSurface(centre.Z / centre.X, rise: 30);
        int walls = 0;
        double rise = 0, covered = 0;
        foreach (CliffFaceSample sample in new CliffPlacements(surface, 5).Report(tile))
        {
            if (!sample.Wall) { Assert.Equal(0.0, sample.Rise, "only walls report a rise"); continue; }
            walls++;
            Assert.True(sample.Rise is >= 24 and <= 30.5, $"a wall reports its face's rise, the step's 30 m less its eased ends ({sample.Rise:F1})");
            Assert.True(sample.RiseCovered >= 0 && sample.RiseCovered <= sample.Rise, "rock covers at most the whole face");
            rise += sample.Rise;
            covered += sample.RiseCovered;
        }
        Fixtures.Report($"30 m step: {walls} wall samples, {covered / Math.Max(1, rise):P0} of their rise with rock in front");
        Assert.True(walls >= 12, "a 30 m step in three metres is a wall all along the cell");
    }

    [Test]
    public static void CliffFormationSightLinesMeetTheScanMesh()
    {
        var surface = new SlopingSurface(1.1);
        var placements = new CliffPlacements(surface, 3);
        CliffPlacement piece = placements.FormationAt(TileKey.At(Vector3D.UnitX, placements.Level))[0];
        CliffGeometry scan = CliffGeometry.All[piece.Model];
        Vector3D middle = piece.Transform((scan.BoundsMin + scan.BoundsMax) * 0.5);
        Vector3D outward = piece.Downslope;
        Assert.True(CliffPlacements.Hits(piece, middle + outward * 30, -outward, 60), "a sight line through the scan's middle meets its mesh");
        Assert.True(!CliffPlacements.Hits(piece, middle + outward * 30, -outward, 5), "a sight line stopping short of it does not");
        Assert.True(!CliffPlacements.Hits(piece, middle + piece.Up * 60 + outward * 30, -outward, 60), "nor one passing far above it");
        int shelves = placements.FormationAt(TileKey.At(Vector3D.UnitX, placements.Level)).Count(placements.IsShelf);
        Assert.Equal(0, shelves, "on an even slope no piece hangs over a drop");
    }

    [Test]
    public static void CliffFormationFitsTheWholeScanAndKeepsSteepFacesEligible()
    {
        foreach (double gradient in new[] { 0.75, 1.2, 2.0, 5.0 })
        {
            var surface = new SlopingSurface(gradient);
            Vector3D origin = Vector3D.UnitX * (surface.Radius + surface.HeightAt(Vector3D.UnitX));
            double measuredGradient = (surface.HeightAt((origin - Vector3D.UnitZ * 5).Normalized) -
                surface.HeightAt((origin + Vector3D.UnitZ * 5).Normalized)) / 10;
            Assert.Near(gradient, measuredGradient, 1e-10,
                "fixture grade is measured over physical metres at ground height, not at the mean planet radius");
            int groups = 0, checkedParts = 0;
            for (ulong seed = 1; seed <= 12; seed++)
            {
                var placements = new CliffPlacements(surface, seed);
                TileKey tile = TileKey.At(Vector3D.UnitX, placements.Level);
                IReadOnlyList<CliffPlacement> formation = placements.FormationAt(tile);
                if (formation.Count == 0) continue;
                groups++;
                foreach (CliffPlacement piece in formation)
                {
                    CliffGeometry geometry = CliffGeometry.All[piece.Model];
                    int buried = 0, visible = 0;
                    double maximumCrest = double.NegativeInfinity;
                    foreach (Vector3D vertex in geometry.Positions)
                    {
                        Vector3D point = piece.Transform(vertex);
                        double clearance = point.Length - surface.Radius - surface.HeightAt(point.Normalized);
                        if (clearance <= 0) buried++;
                        if (clearance > 0.15) visible++;
                        if (vertex.Y > geometry.Height * 0.75) maximumCrest = Math.Max(maximumCrest, clearance);
                    }
                    Assert.True(buried >= geometry.Positions.Length * 0.08,
                        "a meaningful portion of the entire scan intersects terrain, rather than floating above it");
                    Assert.True(visible >= geometry.Positions.Length * 0.15,
                        "fitting retains a face rather than spending geometry on a few exposed tips");
                    Assert.True(maximumCrest <= geometry.Height * piece.Scale * 0.55,
                        "the upper silhouette follows the slope instead of projecting as a tall unsupported spike");
                    Assert.True(Vector3D.Dot(piece.Downslope, -Vector3D.UnitZ) < -0.85,
                        "authored front reversal does not reverse the physical downhill direction");
                    checkedParts++;
                }
            }
            Fixtures.Report($"gradient {gradient:0.00}: {groups} formations / {checkedParts} parts fitted against every shared collision vertex");
            Assert.True(groups >= 4, $"dry gradient {gradient} has fitted geology; steep slopes cannot silently disappear at the old 3.5 cutoff");
        }
    }

    [Test]
    public static void CliffFormationCollisionUsesTheSameFittedTransformsAcrossSourceCells()
    {
        var surface = new SlopingSurface(1.1);
        var placements = new CliffPlacements(surface, 1);
        TileKey tile = TileKey.At(Vector3D.UnitX, placements.Level);
        IReadOnlyList<CliffPlacement> group = placements.FormationAt(tile);
        for (ulong seed = 2; group.Count < 2 && seed <= 20; seed++)
        {
            placements = new CliffPlacements(surface, seed);
            group = placements.FormationAt(tile);
        }
        Assert.True(group.Count >= 2, "the collision scenario contains a multipart formation");
        CliffPlacement sample = group[^1];
        Assert.True(placements.Near(sample.Position, 1).Contains(sample), "offset members are discoverable from their actual location");
        TileKey collisionTile = TileKey.At(sample.Position, TileKey.LevelFor(surface.Radius, 64));
        TriangleMesh terrain = SurfaceGrid.Collision(surface, ZoneFrame.World, collisionTile, 2, SurfaceGrid.DefaultSpacing);
        TriangleMesh collision = placements.Collision(ZoneFrame.World, collisionTile, 2);
        TriangleMesh repeated = placements.Collision(ZoneFrame.World, collisionTile, 2);
        Assert.True(collision.TriangleCount > terrain.TriangleCount, "authoritative collision includes the placed formation");
        Assert.True(collision.Positions.SequenceEqual(repeated.Positions) && collision.Indices.SequenceEqual(repeated.Indices),
            "rebuilding collision preserves every fitted triangle");
        var expected = ZoneFrame.World.ToLocal(sample.Transform(CliffGeometry.All[sample.Model].Positions[0]));
        bool found = false;
        for (int i = terrain.Positions.Length; i < collision.Positions.Length; i += 3)
            if (collision.Positions[i] == expected.X && collision.Positions[i + 1] == expected.Y && collision.Positions[i + 2] == expected.Z)
            { found = true; break; }
        Assert.True(found, "the shared collision contains the exact transform consumed by presentation, including authored front orientation");
    }

    [Test]
    public static void CliffFormationRejectsSubmergedAndFlatGround()
    {
        foreach (ISurface surface in new ISurface[] { new SlopingSurface(0), new SlopingSurface(1.2, submerged: true) })
        {
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var placements = new CliffPlacements(surface, seed);
                Assert.Equal(0, placements.FormationAt(TileKey.At(Vector3D.UnitX, placements.Level)).Count,
                    "water and gentle ground do not acquire detached cliff scans");
            }
        }
    }

    // A straight step along the face's v axis at a chosen tangent: base height below, base plus rise above, the rise
    // over a three-metre band (as WalkingWorld.Tableland's cliff share makes it).
    private sealed class EscarpmentSurface(double stepTangent, double rise = 10) : ISurface
    {
        private const double BaseHeight = 300, Band = 3;
        private readonly double Rise = rise;
        public double Radius => 1200;
        public double SeaRadius => 0;
        public double HeightAt(Vector3D direction)
        {
            if (direction.X <= 0.5) return BaseHeight;
            double metres = (direction.Z / direction.X - stepTangent) * (Radius + BaseHeight);
            double t = Math.Clamp((metres + Band / 2) / Band, 0, 1);
            return BaseHeight + Rise * t * t * (3 - 2 * t);
        }
        public double WaterLevelAt(Vector3D direction) => double.NegativeInfinity;
    }

    private sealed class SlopingSurface(double gradient, bool submerged = false) : ISurface
    {
        private const double BaseHeight = 300;
        public double Radius => 1200;
        public double SeaRadius => 0;
        public double HeightAt(Vector3D direction) => direction.X > 0.5
            ? BaseHeight - gradient * (Radius + BaseHeight) * direction.Z / direction.X
            : BaseHeight;
        public double WaterLevelAt(Vector3D direction) => submerged ? HeightAt(direction) + 1 : double.NegativeInfinity;
    }
}

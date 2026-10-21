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
    public static void CliffWallSkinsDressAWallFromFootToCrest()
    {
        TileKey tile = TileKey.At(Vector3D.UnitX, new CliffPlacements(new SlopingSurface(0), 1).Level);
        Vector3D centre = tile.Centre;
        var surface = new EscarpmentSurface(centre.Z / centre.X, rise: 30);
        var placements = new CliffPlacements(surface, 5);
        CliffWall[] walls = placements.GenerateWalls(tile);
        Assert.Equal(1, walls.Length, "the cell's one wall takes one recipe's textures");
        Assert.Equal("mountain-face", CliffRecipe.All[walls[0].Recipe].Key, "a surface without relief fields takes the mountain recipe");
        CliffWall wall = walls[0];
        int vertices = wall.Positions.Length / 3;
        Assert.True(vertices > 0 && wall.Positions.Length % 3 == 0 && wall.Normals.Length == wall.Positions.Length, "a vertex has a position and a normal");
        Assert.True(wall.Indices.Length % 3 == 0 && wall.Indices.All(i => i >= 0 && i < vertices), "triangles index the skin's vertices");
        Assert.Equal(3, wall.Lods.Length, "three coarser levels of detail");
        double previous = 0;
        int triangles = wall.Indices.Length;
        foreach (CliffWallLod lod in wall.Lods)
        {
            Assert.True(lod.Indices.Length % 3 == 0 && lod.Indices.Length > 0 && lod.Indices.Length < triangles &&
                lod.Indices.All(i => i >= 0 && i < vertices), "each level is fewer triangles over the same vertices");
            Assert.True(lod.Error > previous, "coarser levels admit larger errors");
            (previous, triangles) = (lod.Error, lod.Indices.Length);
        }
        CliffWall again = new CliffPlacements(surface, 5).GenerateWalls(tile)[0];
        Assert.True(wall.Positions.SequenceEqual(again.Positions) && wall.Normals.SequenceEqual(again.Normals) &&
            wall.Indices.SequenceEqual(again.Indices), "every client and server builds the same skin");
        Assert.True(placements.WallsAt(tile)[0].Positions.SequenceEqual(wall.Positions), "the cache keeps what generation builds");
        double low = double.PositiveInfinity, high = double.NegativeInfinity;
        for (int v = 0; v < vertices; v++)
        {
            var normal = new Vector3D(wall.Normals[v * 3], wall.Normals[v * 3 + 1], wall.Normals[v * 3 + 2]);
            Assert.Near(1, normal.Length, 1e-4, "normals are unit length");
            double height = (wall.Anchor + new Vector3D(wall.Positions[v * 3], wall.Positions[v * 3 + 1], wall.Positions[v * 3 + 2])).Length - surface.Radius;
            (low, high) = (Math.Min(low, height), Math.Max(high, height));
        }
        Assert.True(low < 300 && high > 328, $"the skin reaches from below the foot to the crest ({low:F1} to {high:F1} m)");
        double rise = 0, covered = 0;
        foreach (CliffFaceSample sample in placements.Report(tile))
            if (sample.Wall) { rise += sample.Rise; covered += sample.RiseCovered; }
        Fixtures.Report($"30 m step: skin of {vertices} vertices, {wall.Indices.Length / 3} triangles; {covered / rise:P0} of the walls' rise covered");
        Assert.True(covered >= 0.9 * rise, $"the skin covers the wall from foot to crest ({covered / rise:P0})");
    }

    [Test]
    public static void CliffWallSkinsAreSolidWhereTheyAreDrawn()
    {
        TileKey tile = TileKey.At(Vector3D.UnitX, new CliffPlacements(new SlopingSurface(0), 1).Level);
        Vector3D centre = tile.Centre;
        var surface = new EscarpmentSurface(centre.Z / centre.X, rise: 30);
        var placements = new CliffPlacements(surface, 5);
        CliffWall wall = placements.GenerateWalls(tile)[0];
        TileKey collisionTile = TileKey.At(wall.Anchor, TileKey.LevelFor(surface.Radius, 64));
        TriangleMesh ground = SurfaceGrid.Collision(surface, ZoneFrame.World, collisionTile, 2, SurfaceGrid.DefaultSpacing);
        TriangleMesh solid = placements.Collision(ZoneFrame.World, collisionTile, 2);
        Assert.True(solid.TriangleCount >= ground.TriangleCount + wall.Lods[0].Indices.Length / 3,
            "the collision tile holds the skin's first coarser level besides the ground");
        // The skins' collision (it follows the ground's, before the scans' hulls) faces outward, as the ground's faces up:
        // (b - a) x (c - a) leans down the wall.
        double reach = TileKey.EdgeLength(collisionTile.Level, surface.Radius) * 0.95 + 2;
        Vector3D tileGround = collisionTile.Centre * (surface.Radius + surface.HeightAt(collisionTile.Centre));
        int skins = placements.WallsNear(tileGround, reach).Sum(w => w.Lods[0].Indices.Length);
        int first = ground.Indices.Length;
        Vector3D Point(int index) => new(solid.Positions[index * 3], solid.Positions[index * 3 + 1], solid.Positions[index * 3 + 2]);
        int outward = 0, triangles = 0;
        Vector3D downslope = (new Vector3D(1, centre.Y / centre.X, centre.Z / centre.X - 0.01).Normalized - centre.Normalized).Normalized;
        for (int i = first; i + 2 < first + skins; i += 3)
        {
            Vector3D a = Point(solid.Indices[i]), b = Point(solid.Indices[i + 1]), c = Point(solid.Indices[i + 2]);
            Vector3D normal = Vector3D.Cross(b - a, c - a);
            if (normal.LengthSquared < 1e-12) continue;
            triangles++;
            if (Vector3D.Dot(normal.Normalized, downslope) > 0.3) outward++;
        }
        Assert.True(outward > triangles / 2, $"most of the skin's solid triangles face out of the wall ({outward} of {triangles})");
    }

    [Test]
    public static void CliffWallSkinsLeaveNoGapWhereAWallCrossesCells()
    {
        TileKey tile = TileKey.At(Vector3D.UnitX, new CliffPlacements(new SlopingSurface(0), 1).Level);
        Vector3D centre = tile.Centre;
        double tangent = centre.Z / centre.X;
        var surface = new EscarpmentSurface(tangent, rise: 30);
        var placements = new CliffPlacements(surface, 9);
        var cells = new HashSet<TileKey>();
        int lines = 0, hits = 0, gap = 0, longest = 0;
        // Along the step for 150 m across several cells, a level sight line at mid-height from 4 m out every metre.
        for (int k = -75; k <= 75; k++)
        {
            double y = centre.Y / centre.X + k / (surface.Radius + 315);
            Vector3D direction = new Vector3D(1, y, tangent).Normalized;
            Vector3D face = direction * (surface.Radius + 315);
            Vector3D lower = new Vector3D(1, y, tangent - 0.01).Normalized * (surface.Radius + 315);
            Vector3D outward = (lower - face) - direction * Vector3D.Dot(lower - face, direction);
            outward = outward.Normalized;
            cells.Add(TileKey.At(direction, placements.Level));
            lines++;
            bool hit = placements.WallsNear(face, 40).Any(wall => CliffPlacements.Hits(wall, face + outward * 4, -outward, 4.5));
            if (hit) { hits++; gap = 0; } else longest = Math.Max(longest, ++gap);
        }
        Fixtures.Report($"step across {cells.Count} cells: {hits} of {lines} sight lines meet a skin, longest gap {longest} m");
        Assert.True(cells.Count >= 3, "the stretch crosses cells");
        Assert.True(hits >= lines - 2 && longest <= 1, "cells' skins meet along the wall without a gap");
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

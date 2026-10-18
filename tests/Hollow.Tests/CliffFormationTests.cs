using System;
using System.Collections.Generic;
using System.Linq;
using Hollow.Core;
using Hollow.Surface;

namespace Hollow.Tests;

public static class CliffFormationTests
{
    [Test]
    public static void CliffFormationKeepsCandidateStreamAndBoundsComposition()
    {
        var surface = new SlopingSurface(1.1);
        var allScales = new HashSet<double>();
        int groups = 0, parts = 0;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var placements = new CliffPlacements(surface, seed);
            var independent = new CliffPlacements(surface, seed);
            TileKey tile = TileKey.At(Vector3D.UnitX, placements.Level);
            var candidate = new Rng(Hash64.Of(seed, 0x434C4946, tile.Packed));
            bool enabled = candidate.Chance(0.8);
            Vector3D direction = CubeSphere.Direction(tile.Face, tile.U0 + tile.Span * candidate.Range(0.15, 0.85),
                tile.V0 + tile.Span * candidate.Range(0.15, 0.85));
            IReadOnlyList<CliffPlacement> group = placements.FormationAt(tile);
            Assert.True(group.SequenceEqual(independent.FormationAt(tile)), "server/client generation agrees on every transform and stable part key");
            if (!enabled)
            {
                Assert.Equal(0, group.Count, "existing seed rejection remains stable");
                continue;
            }
            if (group.Count == 0) continue;
            groups++;
            parts += group.Count;
            Assert.True(group.Count <= 9, "a formation has a bounded nine-piece cost, rather than a dense wall of repeated scans");
            Assert.Near(0, Vector3D.Distance(direction, group[0].Position.Normalized), 1e-12,
                "first member remains at the original candidate direction");
            Assert.Equal(group.Count, group.Select(p => p.Key).Distinct().Count(), "different members never alias a streaming/collision key");
            Assert.Equal(1, group.Select(p => CliffGeometry.All[p.Model].MaterialFamily).Distinct().Count(),
                "members belong to the same compatible material family");
            foreach (CliffPlacement piece in group)
            {
                allScales.Add(piece.Scale);
                Assert.Near(1, Vector3D.Dot(Vector3D.Cross(piece.Right, piece.Up), piece.Forward), 1e-10,
                    "authored facing preserves a right-handed basis");
                Assert.True(CliffGeometry.All[piece.Model].Positions.All(p => p.Length * piece.Scale <= CliffPlacements.MaximumExtent),
                    "every authoritative vertex remains inside the streaming radius");
                Vector3D anchor = direction * (surface.Radius + surface.HeightAt(direction));
                Assert.True(Vector3D.Distance(anchor, piece.Position) <= CliffPlacements.MaximumOffset,
                    "members cannot escape neighbour discovery");
            }
        }
        Assert.True(groups >= 20, "representative continuous dry slopes retain formations after fitting");
        Assert.True(allScales.Count > 10, "composition varies physical size across stable seeded formations");
        Fixtures.Report($"{groups} synthetic fitted formations, {parts} parts, {allScales.Count} scales; unchanged candidate stream");
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

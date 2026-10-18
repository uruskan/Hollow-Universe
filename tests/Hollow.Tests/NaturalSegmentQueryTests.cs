using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Nature;
using Hollow.Surface;

namespace Hollow.Tests;

public static class NaturalSegmentQueryTests
{
    private static readonly NaturalKind[] Kinds =
    {
        new() { Index = 0, Class = NaturalClass.Tree, Height = 30, Radius = 0.8f },
        new() { Index = 1, Class = NaturalClass.Tree, Height = 1, Radius = 8 },
        new() { Index = 2, Class = NaturalClass.Node, Radius = 8 },
        new() { Index = 3, Class = NaturalClass.Boulder, Radius = 8 },
    };

    [Test]
    public static void SegmentCandidatesPreserveEveryExhaustiveHitAcrossFacesReliefAndFrames()
    {
        var surface = new FlatSurface();
        foreach (ZoneFrame frame in new[] { ZoneFrame.World, ZoneFrame.At(surface, 0.63, -1.2) })
        {
            var nature = new SyntheticNature(surface, frame, 4);
            // Independent exhaustive fixture: every leaf has a root, with all shape classes and both relief extrema.
            foreach (TileKey tile in Tiles(nature.Level))
                nature.Add(tile.Centre, (ushort)((tile.X + tile.Y + tile.Face) % Kinds.Length),
                    (tile.X & 1) == 0 ? -35 : 45, (tile.Y & 1) == 0 ? 0.7f : NaturalShapes.MaximumGeneratedScale);
            // Exact face seams, corners and poles, plus points on each side of the seams.
            Vector3D[] directions = { new(1, 1, 0), new(1, 1, 1), new(-1, 1, -1), Vector3D.UnitY,
                new(1, 0.999999, 0), new(0.999999, 1, 0), new(0, -1, 1), new(-1, 0, -1) };
            var selected = new List<NaturalObject>();
            foreach (Vector3D direction in directions)
                for (ushort kind = 0; kind < Kinds.Length; kind++)
                    selected.Add(nature.Add(direction.Normalized, kind, kind % 2 == 0 ? -35 : 45,
                        NaturalShapes.MaximumGeneratedScale));
            foreach (NaturalObject target in selected)
            {
                Vector3 across = Across(target.Up);
                Vector3 centre = ShapeCentre(target, Kinds[target.Kind]);
                foreach ((float before, float after, float thickness) in new[] { (3f, 37f, 0f), (37f, 3f, 0.5f), (20f, 20f, 2f) })
                    Compare(nature, nature.All, Kinds, centre - across * before, centre + across * after, thickness, target.Id);
            }
        }
    }

    [Test]
    public static void TallTreesAndOffsetRocksOutsideTheOldThreeMetreMarginRemainHittable()
    {
        var surface = new FlatSurface();
        for (ushort kind = 0; kind < Kinds.Length; kind++)
        {
            var nature = new SyntheticNature(surface, ZoneFrame.World, 5);
            NaturalObject target = nature.Add(Vector3D.UnitX, kind, 0, NaturalShapes.MaximumGeneratedScale);
            Vector3 centre = kind switch
            {
                0 => target.Position + target.Up * 60,
                1 => NaturalShapes.Trunk(target, Kinds[kind]).B,
                _ => NaturalShapes.Rock(target, Kinds[kind]).Centre,
            };
            foreach ((float before, float after) in new[] { (0.5f, 3.5f), (3.5f, 0.5f) })
            {
                Vector3 from = centre - Vector3.UnitZ * before, to = centre + Vector3.UnitZ * after;
                var old = new List<NaturalObject>();
                nature.Query((from + to) * 0.5f, Vector3.Distance(from, to) * 0.5f + 3, old);
                Assert.Equal(0, old.Count, "the previous root sphere incorrectly omitted this hit shape");
                Compare(nature, nature.All, Kinds, from, to, 0, target.Id);
            }
        }
    }

    [Test]
    public static void CanonicalOrderKeepsTheFirstEqualDistanceHitAndGeneratesEachTileOnce()
    {
        var nature = new SyntheticNature(new FlatSurface(), ZoneFrame.World, 5);
        NaturalObject first = nature.Add(Vector3D.UnitX, 0, 0, 1);
        _ = nature.Add(Vector3D.UnitX, 0, 0, 1);
        Vector3 centre = first.Position + first.Up * 10;
        List<(ulong Id, float Distance)> hits = Compare(nature, nature.All, Kinds,
            centre - Vector3.UnitZ * 20, centre + Vector3.UnitZ * 20, 0, first.Id);
        Assert.Equal(2, hits.Count, "both coincident trunks reach the narrowphase");
        Assert.Equal(hits[0].Distance, hits[1].Distance, "fixture is an exact distance tie");
        Assert.Equal(first.Id, hits[0].Id, "first canonical object wins the unchanged strict-distance comparison");
    }

    [Test]
    public static void LongOffPlanetSegmentsAreRejectedBeforeGenerationAndCrossingWorkIsFinite()
    {
        var nature = new SyntheticNature(new FlatSurface(), ZoneFrame.World, 6);
        var candidates = new List<NaturalObject>();
        nature.QueryAlong(new Vector3(1e20f, 1e20f, 0), new Vector3(-1e20f, 1e20f, 0), 4, candidates);
        Assert.Equal(0, nature.Visited.Count, "a distant segment never generates a natural tile");
        nature.QueryAlong(new Vector3(-1e20f, 0, 0), new Vector3(1e20f, 0, 0), 4, candidates);
        Assert.True(nature.Visited.Count > 0 && nature.Visited.Count < 256,
            "a planet crossing selects only a bounded corridor at the two sides, irrespective of ray length");
        CheckTileOrder(nature);
        int count = nature.Visited.Count;
        nature.QueryAlong(new Vector3(float.NaN, 0, 0), Vector3.Zero, 4, candidates);
        nature.QueryAlong(Vector3.Zero, Vector3.One, float.PositiveInfinity, candidates);
        Assert.Equal(count, nature.Visited.Count, "invalid inputs do not generate tiles");
    }

    [Test]
    public static void BothGeneratedProvidersAgreeWithExhaustiveWorldObjects()
    {
        var surface = new FlatSurface { Small = true };
        INaturalObjects[] providers =
        {
            new StandInNaturalObjects(surface, ZoneFrame.World, ContentFixture.Content, 107, Vector3D.Zero),
            new ScatterNaturalObjects(surface, ZoneFrame.World, ContentFixture.Content),
        };
        NaturalKind[] kinds = ContentFixture.Content.NaturalKinds.ToArray();
        foreach (INaturalObjects provider in providers)
        {
            NaturalObject[] all = Tiles(provider.Level).OrderBy(t => t.Packed).SelectMany(provider.InTile).ToArray();
            NaturalObject[] targets = all.Where(o => kinds[o.Kind].IsSolid).Take(12).ToArray();
            Assert.True(targets.Length > 0, "the real generator produces solid fixture objects");
            foreach (NaturalObject target in targets)
            {
                Vector3 centre = ShapeCentre(target, kinds[target.Kind]), across = Across(target.Up);
                var candidates = new List<NaturalObject>();
                Vector3 from = centre - across * 30, to = centre + across * 30;
                provider.QueryAlong(from, to, NaturalShapes.MaximumRayRootReach(kinds) + 0.3f, candidates);
                Assert.True(Hits(all, kinds, from, to, 0.3f).SequenceEqual(Hits(candidates, kinds, from, to, 0.3f)),
                    "generated provider keeps every exhaustive narrowphase hit in identical order");
            }
        }
    }

    [Test]
    public static void ContentDerivedReachEnclosesTheExactCapsulesAndOffsetSpheres()
    {
        foreach (NaturalKind kind in Kinds.Concat(ContentFixture.Content.NaturalKinds).Where(k => k.IsSolid))
            foreach (float scale in new[] { 0.25f, 1f, NaturalShapes.MaximumGeneratedScale })
            {
                var item = new NaturalObject { Up = Vector3.UnitY, Scale = scale };
                float reach = NaturalShapes.RayRootReach(kind, scale), required;
                if (kind.Class == NaturalClass.Tree)
                {
                    (Vector3 a, Vector3 b, float radius) = NaturalShapes.Trunk(item, kind);
                    required = MathF.Max(a.Length(), b.Length()) + radius;
                }
                else
                {
                    (Vector3 centre, float radius) = NaturalShapes.Rock(item, kind);
                    required = centre.Length() + radius;
                }
                Assert.True(reach + 0.0001f >= required, "root reach includes both caps or the full shifted sphere");
            }
        Assert.True(NaturalSegmentQuery.EnclosingSphere(new Vector3(2e30f, 0, 0), new Vector3(-2e30f, 0, 0), 3,
            out _, out float bound) && float.IsFinite(bound), "fallback avoids intermediate float-square overflow");
    }

    private static List<(ulong Id, float Distance)> Compare(SyntheticNature nature, IEnumerable<NaturalObject> all,
        NaturalKind[] kinds, Vector3 from, Vector3 to, float thickness, ulong target)
    {
        nature.Visited.Clear();
        var candidates = new List<NaturalObject>();
        nature.QueryAlong(from, to, NaturalShapes.MaximumRayRootReach(kinds) + thickness, candidates);
        var expected = Hits(all, kinds, from, to, thickness);
        Assert.True(expected.Any(hit => hit.Id == target), "exhaustive geometry hits the intended fixture object");
        Assert.True(expected.SequenceEqual(Hits(candidates, kinds, from, to, thickness)),
            "segment broadphase preserves all exhaustive hits, exact distances and canonical ties");
        CheckTileOrder(nature);
        return expected;
    }

    private static List<(ulong Id, float Distance)> Hits(IEnumerable<NaturalObject> objects, NaturalKind[] kinds,
        Vector3 from, Vector3 to, float thickness)
    {
        float length = Vector3.Distance(from, to);
        Vector3 direction = (to - from) / length;
        var hits = new List<(ulong, float)>();
        foreach (NaturalObject item in objects)
        {
            NaturalKind kind = kinds[item.Kind];
            if (!kind.IsSolid) continue;
            float distance;
            if (kind.Class == NaturalClass.Tree)
            {
                (Vector3 a, Vector3 b, float radius) = NaturalShapes.Trunk(item, kind);
                distance = Geometry.RayCapsule(from, direction, length, a, b, radius + thickness);
            }
            else
            {
                (Vector3 centre, float radius) = NaturalShapes.Rock(item, kind);
                distance = Geometry.RaySphere(from, direction, length, centre, radius + thickness);
            }
            if (distance >= 0) hits.Add((item.Id, distance));
        }
        return hits;
    }

    private static Vector3 ShapeCentre(NaturalObject item, NaturalKind kind) => kind.Class == NaturalClass.Tree
        ? item.Position + item.Up * (kind.Height * item.Scale * 0.5f) : NaturalShapes.Rock(item, kind).Centre;
    private static Vector3 Across(Vector3 up) => Vector3.Normalize(Vector3.Cross(up,
        MathF.Abs(up.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitZ));
    private static void CheckTileOrder(SyntheticNature nature)
    {
        Assert.Equal(nature.Visited.Count, nature.Visited.Distinct().Count(), "canonical leaves generated at most once");
        Assert.True(nature.Visited.SequenceEqual(nature.Visited.OrderBy(t => t.Packed)), "tile order matches radius queries");
    }
    private static IEnumerable<TileKey> Tiles(int level)
    {
        int side = 1 << level;
        for (int face = 0; face < 6; face++)
            for (int x = 0; x < side; x++)
                for (int y = 0; y < side; y++) yield return new TileKey(face, level, x, y);
    }
    private sealed class SyntheticNature(FlatSurface surface, ZoneFrame frame, int level) : INaturalObjects
    {
        private readonly Dictionary<TileKey, List<NaturalObject>> _tiles = new();
        private ulong _next;
        public int Level => level;
        public List<TileKey> Visited { get; } = new();
        public IEnumerable<NaturalObject> All => _tiles.OrderBy(pair => pair.Key.Packed).SelectMany(pair => pair.Value);
        public NaturalObject Add(Vector3D direction, ushort kind, double height, float scale)
        {
            var item = new NaturalObject { Id = ++_next, Kind = kind, Position = frame.ToLocal(direction * (surface.Radius + height)),
                Up = Vector3.Normalize(frame.DirectionToLocal(direction)), Scale = scale };
            TileKey tile = TileKey.At(direction, Level);
            if (!_tiles.TryGetValue(tile, out var items)) _tiles.Add(tile, items = new List<NaturalObject>());
            items.Add(item);
            return item;
        }
        public void Query(Vector3 centre, float radius, List<NaturalObject> into) =>
            into.AddRange(All.Where(o => Vector3.DistanceSquared(o.Position, centre) <= radius * radius));
        public void QueryAlong(Vector3 from, Vector3 to, float rootReach, List<NaturalObject> into) =>
            NaturalSegmentQuery.Query(this, surface, frame, from, to, rootReach, into);
        public IReadOnlyList<NaturalObject> InTile(TileKey tile)
        {
            Visited.Add(tile);
            return _tiles.TryGetValue(tile, out var items) ? items : Array.Empty<NaturalObject>();
        }
        public bool TryGet(ulong id, out NaturalObject found) { found = All.FirstOrDefault(o => o.Id == id); return found.Id != 0; }
        public TileKey? TileOf(ulong id) => TryGet(id, out NaturalObject found) ? TileKey.At(frame.ToBody(found.Position), Level) : null;
    }
    private sealed class FlatSurface : ISurface, IScatterField
    {
        public bool Small { get; init; }
        public double Radius => Small ? 120 : 1200;
        public double SeaRadius => 0;
        public (double Low, double High) HeightBounds => (-35, 45);
        public double HeightAt(Vector3D direction) => 0;
        public ulong ScatterSeed => 107;
        public ScatterPlan ScatterPlan => new(2, 1, 1, 1, 1, 1, 1);
        public SurfaceSample Sample(Vector3D direction) => default;
        public ScatterDensity Densities(Vector3D direction, in SurfaceSample sample, double slope) =>
            new(new VegetationSample(1, 0, 0, 0, 0), new double[] { 1, 0, 0, 0 }, 0, 0);
    }
}

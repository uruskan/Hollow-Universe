using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Targets;
using Hollow.Surface;

namespace Hollow.Tests;

public static class NaturalAimSegmentQueryTests
{
    [Test]
    public static void InteractionAndAttackAimMatchExhaustiveShapesForEveryContentKind()
    {
        GameContent content = ContentFixture.Content;
        var surface = new FlatSurface();
        foreach (NaturalKind kind in content.NaturalKinds)
            foreach (float scale in new[] { 0.25f, 1f, NaturalShapes.MaximumGeneratedScale })
                foreach (float? thickness in new float?[] { null, 0, 0.75f })
                {
                    var nature = new SyntheticNature(surface);
                    NaturalObject target = nature.Add(kind, scale);
                    var state = new NaturalState(content, nature);
                    HitShape shape = NaturalShapes.Bounds(target, kind);
                    Vector3 from = shape.Middle - Vector3.UnitZ * 10;
                    var ray = new AimRay(from, Vector3.UnitZ, 20) { AttackRadius = thickness };
                    AimHit expected = Exhaustive(nature.All, content, state, ray, AimHit.None), actual = AimHit.None;
                    NaturalAim.Aim(nature, state, content, ray, ref actual, new List<NaturalObject>());
                    Assert.Equal(expected, actual, $"{kind.Key}/{scale}/{thickness}: same exact hit, point, identity and aim kind");
                    Assert.True(actual.Hit == (thickness == null || kind.IsSolid), "attack filtering is unchanged");
                    Assert.Equal(0, nature.RadiusQueries, "shared aim uses segment candidates rather than a midpoint disk");
                    if (thickness == null || kind.IsSolid)
                    {
                        var largest = target with { Position = Vector3.Zero, Scale = NaturalShapes.MaximumGeneratedScale };
                        HitShape bound = NaturalShapes.Bounds(largest, kind);
                        float required = MathF.Max(bound.A.Length(), bound.B.Length()) + bound.Radius +
                            (thickness ?? (kind.IsPicked ? AimShapes.PickupMargin : 0));
                        Assert.True(nature.LastReach >= required, "reach encloses exact shape offsets and interaction/attack growth");
                    }
                    state.Apply(target.Id, new NaturalOverride { Depleted = true });
                    actual = AimHit.None;
                    NaturalAim.Aim(nature, state, content, ray, ref actual, new List<NaturalObject>());
                    Assert.True(!actual.Hit, "depleted objects remain excluded");
                }
    }

    [Test]
    public static void TallEndpointHitsAndCanonicalTiesSurviveAndEarlierHitsStillWin()
    {
        GameContent content = ContentFixture.Content;
        NaturalKind kind = content.NaturalKinds.First(k => k.Class == NaturalClass.Tree && k.Height >= 10);
        var nature = new SyntheticNature(new FlatSurface());
        NaturalObject first = nature.Add(kind, NaturalShapes.MaximumGeneratedScale);
        _ = nature.Add(kind, NaturalShapes.MaximumGeneratedScale);
        var state = new NaturalState(content, nature);
        Vector3 centre = NaturalShapes.Bounds(first, kind).Middle;
        foreach ((float before, float after) in new[] { (0.5f, 3.5f), (3.5f, 0.5f) })
        {
            var ray = new AimRay(centre - Vector3.UnitZ * before, Vector3.UnitZ, before + after) { AttackRadius = 0 };
            Vector3 midpoint = ray.Origin + ray.Direction * (ray.Length * 0.5f);
            Assert.True(Vector3.Distance(midpoint, first.Position) > ray.Length * 0.5f + 4,
                "old four-metre root margin omitted the tall trunk");
            AimHit expected = Exhaustive(nature.All, content, state, ray, AimHit.None), actual = AimHit.None;
            NaturalAim.Aim(nature, state, content, ray, ref actual, new List<NaturalObject>());
            Assert.Equal(first.Id, actual.Target.Id, "coincident hits keep the first canonical object");
            Assert.Equal(expected, actual, "short endpoint ray preserves the exact hit");
            var prior = new AimHit(TargetRef.Ground, actual.Distance, actual.Point, AimKind.Backdrop);
            actual = prior;
            NaturalAim.Aim(nature, state, content, ray, ref actual, new List<NaturalObject>());
            Assert.Equal(prior, actual, "an equal prior receiver remains the winner");
        }
    }

    [Test]
    public static void RepeatedTwoKilometreStationAimReusesABoundedCorridor()
    {
        var surface = new FlatSurface { Underwater = true };
        var nature = new StandInNaturalObjects(surface, ZoneFrame.World, ContentFixture.Content, 107, Vector3D.Zero);
        var state = new NaturalState(ContentFixture.Content, nature);
        var ray = new AimRay(new Vector3((float)surface.Radius + 10, 0, 0),
            Vector3.Normalize(new Vector3(0.174f, 0, 0.985f)), 2000) { AttackRadius = 0 };
        var scratch = new List<NaturalObject>();
        AimHit best = AimHit.None;
        NaturalAim.Aim(nature, state, ContentFixture.Content, ray, ref best, scratch);
        int generated = nature.Generated;
        Assert.True(generated > 0 && generated < StandInNaturalObjects.MaxTiles, "the complete sky-ray corridor fits the resident budget");
        for (int i = 0; i < 12; i++)
        {
            best = AimHit.None;
            NaturalAim.Aim(nature, state, ContentFixture.Content, ray, ref best, scratch);
            Assert.True(!best.Hit, "underwater fixture has no natural objects");
            Assert.Equal(generated, nature.Generated, "repeated station aiming does not churn the tile cache");
        }
        Assert.Equal(0L, nature.CacheDiagnostics.Evictions, "the repeated corridor never exceeds retained capacity");
    }

    private static AimHit Exhaustive(IEnumerable<NaturalObject> all, GameContent content, NaturalState state,
        in AimRay ray, AimHit best)
    {
        float length = ray.Upto(best);
        foreach (NaturalObject item in all)
        {
            NaturalKind kind = content.Natural(item.Kind)!;
            if (state.IsDepleted(item.Id) || ray.AttackRadius != null && !kind.IsSolid) continue;
            HitShape shape = NaturalShapes.Bounds(item, kind);
            float grow = ray.AttackRadius ?? (kind.IsPicked ? AimShapes.PickupMargin : 0);
            ray.Consider(ref best, shape.Raycast(ray.Origin, ray.Direction, length, grow),
                new TargetRef(TargetFamily.Natural, item.Id), kind.IsPicked ? AimKind.Thing : AimKind.Backdrop);
        }
        return best;
    }

    private sealed class SyntheticNature(FlatSurface surface) : INaturalObjects
    {
        public List<NaturalObject> All { get; } = new();
        public int RadiusQueries { get; private set; }
        public float LastReach { get; private set; }
        public int Level => 5;
        private TileKey Tile => TileKey.At(Vector3D.UnitX, Level);
        public NaturalObject Add(NaturalKind kind, float scale)
        {
            var item = new NaturalObject { Id = (ulong)All.Count + 1, Kind = kind.Index,
                Position = new Vector3((float)surface.Radius, 0, 0), Up = Vector3.UnitX, Scale = scale };
            All.Add(item);
            return item;
        }
        public void Query(Vector3 centre, float radius, List<NaturalObject> into)
        {
            RadiusQueries++;
            into.AddRange(All.Where(o => Vector3.DistanceSquared(centre, o.Position) <= radius * radius));
        }
        public void QueryAlong(Vector3 from, Vector3 to, float rootReach, List<NaturalObject> into)
        {
            LastReach = rootReach;
            NaturalSegmentQuery.Query(this, surface, ZoneFrame.World, from, to, rootReach, into);
        }
        public IReadOnlyList<NaturalObject> InTile(TileKey tile) => tile == Tile ? All : Array.Empty<NaturalObject>();
        public bool TryGet(ulong id, out NaturalObject found) { found = All.FirstOrDefault(o => o.Id == id); return found.Id != 0; }
        public TileKey? TileOf(ulong id) => TryGet(id, out _) ? Tile : null;
    }
    private sealed class FlatSurface : ISurface
    {
        public double Radius => 2410;
        public bool Underwater { get; init; }
        public double SeaRadius => Underwater ? 2500 : 0;
        public double HeightAt(Vector3D direction) => 0;
        public (double Low, double High) HeightBounds => (0, 0);
    }
}

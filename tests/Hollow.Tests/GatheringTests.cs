using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Physics;
using Hollow.Surface;

namespace Hollow.Tests;

public static class GatheringTests
{
    private static GameContent C => ContentFixture.Content;

    private const long Now = 1_800_000_000_000;

    private static (ZoneSpec Spec, ISurface Surface, ZoneFrame Frame, INaturalObjects Objects) World()
    {
        ZoneSpec spec = ZoneSpec.FirstWorld(Fixtures.Galaxy, 28.4, -61.7)! with { StandIn = true };
        ISurface surface = spec.CreateSurface();
        ZoneFrame frame = spec.CreateFrame(surface);
        return (spec, surface, frame, spec.CreateNature(surface, frame, C));
    }

    private static NaturalObject Find(INaturalObjects objects, string kind)
    {
        var found = new List<NaturalObject>();
        objects.Query(-200, -200, 200, 200, found);
        ushort index = C.Natural(kind)!.Index;
        return found.First(o => o.Kind == index);
    }

    // A swing from an eye straight at a point, with a tool.
    private static GatherResult Swing(NaturalState state, in NaturalObject o, string tool, Vector3 eye, Vector3 at)
    {
        ItemDef item = C.ItemByKey(tool);
        return state.Hit(o, new GatherSwing(item, item.Melee!, at, eye, Vector3.Normalize(at - eye), Now, 1));
    }

    // A swing at the weak spot from a little over a metre out in front of it.
    private static GatherResult AtSpot(NaturalState state, in NaturalObject o, NaturalKind kind, string tool)
    {
        (Vector3 spot, Vector3 normal) = state.WeakSpotAt(o, kind)!.Value;
        return Swing(state, o, tool, spot + normal * 1.2f + o.Up * 0.25f, spot);
    }

    // A swing from the east (+X) or the west at a tree's trunk at chest height, or at an outcrop's flank.
    private static GatherResult FromSide(NaturalState state, in NaturalObject o, NaturalKind kind, string tool, float side)
    {
        bool tree = kind.Class == NaturalClass.Tree;
        Vector3 east = Vector3.UnitX * side;
        Vector3 eye = o.Position + east * (tree ? 2f : 2.2f * o.Scale) + o.Up * 1.6f;
        Vector3 at = o.Position + (tree ? o.Up * 1.3f : east * (0.5f * o.Scale) + o.Up * (0.3f * o.Scale));
        return Swing(state, o, tool, eye, at);
    }

    /// <summary>
    /// The stand-in placement (GROUND.md §4.6): the same objects under the same IDs from any instance, every kind
    /// present (berry bushes too), the spawn area clear, and IDs from outside looked up safely.
    /// </summary>
    [Test]
    public static void StandInNatureIsStable()
    {
        (ZoneSpec spec, ISurface surface, ZoneFrame frame, INaturalObjects a) = World();
        INaturalObjects b = spec.CreateNature(surface, frame, C);
        var first = new List<NaturalObject>();
        var second = new List<NaturalObject>();
        a.Query(-320, -320, 320, 320, first);
        b.Query(-320, -320, 320, 320, second);
        Assert.True(first.SequenceEqual(second), "two instances place the same objects");
        Assert.True(first.Count > 500, $"a populated ground ({first.Count} objects in 640 × 640 m)");
        foreach (NaturalKind kind in C.NaturalKinds.Where(k => k.StandInPerTile > 0))
        {
            Assert.True(first.Any(o => o.Kind == kind.Index), $"some {kind.Key}");
        }
        Assert.True(first.All(o => new Vector2(o.Position.X, o.Position.Z).Length() >= StandInNaturalObjects.ClearRadius), "spawn area clear");
        Assert.True(a.TryGet(first[123].Id, out NaturalObject again) && again == first[123], "found by ID");
        Assert.True(!a.TryGet(0, out _) && !a.TryGet(first[123].Id + (1UL << 20), out _) && !a.TryGet(ulong.MaxValue, out _), "made-up IDs aren't");
        Fixtures.Report($"{first.Count} objects; e.g. {C.Natural(first[0].Kind)!.Key} at {first[0].Position}");
    }

    /// <summary>
    /// GROUND.md §6, gathering: yield grows with the tool (a stone hatchet fells a tree in fewer hits than a rock, for
    /// the same total); outcrops go through three stages, then respawn on time; pickups and berry bushes are picked whole
    /// and come back.
    /// </summary>
    [Test]
    public static void GatheringYieldDepletionAndRespawn()
    {
        (_, _, _, INaturalObjects objects) = World();
        NaturalObject tree = Find(objects, "pine-tree");
        NaturalKind pine = C.Natural("pine-tree")!;
        int wood = NaturalShapes.Total(tree, pine, pine.Yield[0]);

        // The first hit from the west puts the weak spot there; the rest come from the east, never on it.
        int HitsToFell(string tool, out int total)
        {
            var state = new NaturalState(C, objects);
            total = FromSide(state, tree, pine, tool, -1).Yield.Sum(y => y.Count);
            int hits = 1;
            while (!state.IsDepleted(tree.Id) && hits < 1000)
            {
                GatherResult r = FromSide(state, tree, pine, tool, 1);
                Assert.True(r.Hit && !r.WeakSpot, "an ordinary hit");
                total += r.Yield.Sum(y => y.Count);
                hits++;
            }
            return hits;
        }
        int rockHits = HitsToFell("rock", out int rockWood);
        int hatchetHits = HitsToFell("stone-hatchet", out int hatchetWood);
        Fixtures.Report($"a {tree.Scale:0.00}-size pine: {wood} wood; rock {rockHits} hits, stone hatchet {hatchetHits}, metal hatchet {HitsToFell("metal-hatchet", out _)}");
        Assert.True(rockWood == wood && hatchetWood == wood, "the whole yield, to the unit");
        Assert.True(hatchetHits * 2 < rockHits, "a hatchet is more than twice as quick");

        // An outcrop through its stages, then gone, then back on time.
        NaturalObject node = Find(objects, "metal-node");
        NaturalKind metal = C.Natural("metal-node")!;
        var rocks = new NaturalState(C, objects);
        var stages = new List<int>();
        var gained = new Dictionary<string, int>();
        while (!rocks.IsDepleted(node.Id))
        {
            stages.Add(rocks.Stage(node, metal));
            GatherResult r = FromSide(rocks, node, metal, "rock", stages.Count == 1 ? -1 : 1);
            foreach (ItemAmount y in r.Yield)
            {
                gained[y.Item.Key] = gained.GetValueOrDefault(y.Item.Key) + y.Count;
            }
        }
        Assert.True(stages.Distinct().SequenceEqual(new[] { 0, 1, 2 }), $"three stages in order: {string.Join(",", stages.Distinct())}");
        Assert.Equal(NaturalShapes.Total(node, metal, metal.Yield[0]), gained["metal-ore"], "all the ore");
        Assert.Equal(NaturalShapes.Total(node, metal, metal.Yield[1]), gained["high-grade-ore"], "and the rich ore");
        Assert.True(rocks.Changed.Contains(node.Id), "a change to send and save");
        Assert.Equal(0, rocks.Respawn(Now + 599_000), "not before ten minutes");
        Assert.Equal(1, rocks.Respawn(Now + 600_000), "back after ten");
        Assert.True(!rocks.IsDepleted(node.Id) && rocks.Health(node, metal) == NaturalShapes.MaxHealth(node, metal), "whole again");

        // Pickups and berry bushes: picked whole with the interact key, then back after their time.
        var plants = new NaturalState(C, objects);
        NaturalObject bush = Find(objects, "red-berry-bush");
        GatherResult berries = plants.Pick(bush, Now);
        Assert.True(berries.Hit && berries.Yield.Single().Item.Key == "red-berries" && berries.Yield[0].Count == 4, "four red berries");
        Assert.True(!plants.Pick(bush, Now + 1000).Hit, "a bare bush gives nothing");
        NaturalObject fibre = Find(objects, "fibre-plant");
        Assert.Equal(10, plants.Pick(fibre, Now).Yield.Single().Count, "ten fibre");
        Assert.True(!plants.Pick(tree, Now).Hit, "trees aren't picked");
        Assert.Equal(2, plants.Respawn(Now + 480_000), "both back after eight minutes");
    }

    /// <summary>
    /// Rust's X on trees (gather.json classes.tree.spot; Rust's November 2017 update): it shows with the first hit,
    /// facing the hitter near the hit's height; hits on it in a row pay and take 1.125, 1.25 … up to 2 times and move
    /// it; a hit elsewhere ends the run and leaves the X where it was.
    /// </summary>
    [Test]
    public static void TreeMarkPaysMoreForEveryHitOnItInARow()
    {
        (_, _, _, INaturalObjects objects) = World();
        NaturalObject tree = Find(objects, "pine-tree");
        NaturalKind pine = C.Natural("pine-tree")!;
        float rate = C.ItemByKey("rock").Melee!.Tree;
        var state = new NaturalState(C, objects);
        Assert.True(state.WeakSpot(tree, pine) == null, "no X before the first hit");
        GatherResult first = FromSide(state, tree, pine, "rock", 1);
        (Vector3 x, _) = state.WeakSpotAt(tree, pine)!.Value;
        float height = Vector3.Dot(x - tree.Position, tree.Up);
        Assert.True(!first.WeakSpot && x.X > tree.Position.X && height is >= 0.7f and <= 2.0f && MathF.Abs(height - 1.3f) <= 0.36f,
            $"the X faces the hitter near the hit's height ({height:0.00} m)");
        var scales = new List<float>();
        int moved = 0;
        for (int i = 0; i < 10; i++)
        {
            float before = state.Health(tree, pine);
            Vector3 was = state.WeakSpot(tree, pine)!.Value;
            GatherResult r = AtSpot(state, tree, pine, "rock");
            Assert.True(r.WeakSpot && r.Streak == i + 1, $"hit {i + 1} on the X");
            Assert.Near(before - rate * r.Scale, state.Health(tree, pine), 1e-3, "it takes as much more as it pays");
            scales.Add(r.Scale);
            moved += Vector3.Distance(was, state.WeakSpot(tree, pine)!.Value) > 0.05f ? 1 : 0;
        }
        Fixtures.Report($"X hits in a row pay {string.Join(", ", scales.Select(s => s.ToString("0.###")))}; the X moved {moved} times of 10");
        Assert.True(scales.SequenceEqual(new[] { 1.125f, 1.25f, 1.375f, 1.5f, 1.625f, 1.75f, 1.875f, 2f, 2f, 2f }), "1.125 more for each, up to twice");
        Assert.True(moved >= 9, "every hit on it moves it");
        Vector3 standing = state.WeakSpot(tree, pine)!.Value;
        GatherResult miss = FromSide(state, tree, pine, "rock", Vector3.Dot(standing - tree.Position, Vector3.UnitX) > 0 ? -1 : 1);
        Assert.True(!miss.WeakSpot && miss.Scale == 1 && state.WeakSpot(tree, pine) == standing, "a miss pays the plain rate and leaves the X");
        Assert.Equal(1.125f, AtSpot(state, tree, pine, "rock").Scale, "and the run starts again");
    }

    /// <summary>
    /// Rust's hotspot on ore nodes (gather.json classes.node.spot; Devblog 170): hits on it in a row pay 150%, 200%,
    /// 250%, at most 300%; a miss resets the run; the node gives the same ore in all, in fewer hits.
    /// </summary>
    [Test]
    public static void OreHotspotBreaksTheNodeInFewerHits()
    {
        (_, _, _, INaturalObjects objects) = World();
        NaturalObject node = Find(objects, "stone-node");
        NaturalKind stone = C.Natural("stone-node")!;
        int total = NaturalShapes.Total(node, stone, stone.Yield[0]);
        int Break(bool onSpot, out List<float> scales)
        {
            var state = new NaturalState(C, objects);
            int ore = FromSide(state, node, stone, "rock", -1).Yield.Sum(y => y.Count), hits = 1;
            scales = new List<float>();
            while (!state.IsDepleted(node.Id) && hits < 1000)
            {
                GatherResult r = onSpot ? AtSpot(state, node, stone, "rock") : FromSide(state, node, stone, "rock", 1);
                Assert.True(r.WeakSpot == onSpot, onSpot ? "on the hotspot" : "off it");
                scales.Add(r.Scale);
                ore += r.Yield.Sum(y => y.Count);
                hits++;
            }
            Assert.Equal(total, ore, "the same stone either way");
            return hits;
        }
        int plain = Break(false, out _), hot = Break(true, out List<float> run);
        Fixtures.Report($"a {node.Scale:0.00}-size stone outcrop: {plain} hits with a rock, {hot} on the hotspot (pays {string.Join(", ", run.Take(5))})");
        Assert.True(run.Take(5).SequenceEqual(new[] { 1.5f, 2f, 2.5f, 3f, 3f }), "150%, 200%, 250%, then 300%");
        Assert.True(hot * 2 < plain, "under half the hits");
        var miss = new NaturalState(C, objects);
        FromSide(miss, node, stone, "rock", -1);
        AtSpot(miss, node, stone, "rock");
        Vector3 spot = miss.WeakSpot(node, stone)!.Value;
        Assert.True(FromSide(miss, node, stone, "rock", 1).Scale == 1 && miss.WeakSpot(node, stone) == spot, "a miss pays the plain rate, the spot stays");
        Assert.Equal(1.5f, AtSpot(miss, node, stone, "rock").Scale, "and the bonus starts again");
    }

    /// <summary>gather.json classes.tree.last (Rust's November 2017 update): a felled tree pays half its wood as it falls.</summary>
    [Test]
    public static void FelledTreePaysHalfItsWood()
    {
        (_, _, _, INaturalObjects objects) = World();
        NaturalObject tree = Find(objects, "broadleaf-tree");
        NaturalKind oak = C.Natural("broadleaf-tree")!;
        var state = new NaturalState(C, objects);
        int wood = NaturalShapes.Total(tree, oak, oak.Yield[0]), byHits = FromSide(state, tree, oak, "stone-hatchet", -1).Yield.Sum(y => y.Count);
        GatherResult r;
        while (!(r = FromSide(state, tree, oak, "stone-hatchet", 1)).Depleted)
        {
            byHits += r.Yield.Sum(y => y.Count);
        }
        int felled = r.Yield.Sum(y => y.Count);
        Fixtures.Report($"a broadleaf tree of {wood} wood: {byHits} from the hits, {felled} as it falls");
        Assert.Equal(wood, byHits + felled, "the whole tree");
        Assert.True(felled >= (int)Math.Round(wood * 0.5) && felled <= wood * 0.6, "half of it when it falls (and the felling hit's own share)");
    }

    /// <summary>gather.json classes.node.last (Devblog 166): the last hit with a pickaxe pays 20% of the node on top; a rock's doesn't.</summary>
    [Test]
    public static void ProperToolFinishesAnOutcropWithABonus()
    {
        (_, _, _, INaturalObjects objects) = World();
        NaturalObject node = Find(objects, "sulphur-node");
        NaturalKind sulphur = C.Natural("sulphur-node")!;
        int total = NaturalShapes.Total(node, sulphur, sulphur.Yield[0]);
        int Break(string tool)
        {
            var state = new NaturalState(C, objects);
            int ore = 0;
            GatherResult r;
            do
            {
                r = FromSide(state, node, sulphur, tool, ore == 0 ? -1 : 1);
                ore += r.Yield.Sum(y => y.Count);
            }
            while (!r.Depleted);
            Assert.True(r.Bonus == (tool != "rock"), $"{tool}: the bonus or not");
            return ore;
        }
        int rock = Break("rock"), pickaxe = Break("stone-pickaxe");
        Fixtures.Report($"a sulphur outcrop of {total} ore: {rock} with a rock, {pickaxe} with a stone pickaxe");
        Assert.Equal(total, rock, "a rock: the node's ore");
        Assert.Equal(total + (int)Math.Round(total * 0.2), pickaxe, "a pickaxe: a fifth more on the last hit");
    }

    /// <summary>
    /// The hotspot lies on the outcrop's drawn rock at every stage (surfaces.json, measured from the stage models), never
    /// in the air beside a broken one, and a swing aimed at it meets the rock there.
    /// </summary>
    [Test]
    public static void OutcropSpotsSitOnTheDrawnRock()
    {
        (_, _, _, INaturalObjects objects) = World();
        int checkedSpots = 0;
        foreach (NaturalKind kind in C.NaturalKinds.Where(k => k.Gather?.Spot.Surface is MeasuredSurface))
        {
            NaturalObject node = Find(objects, kind.Key);
            var surface = (MeasuredSurface)kind.Gather!.Spot.Surface;
            (Vector3 x, Vector3 up, Vector3 z) = SpotFrames.ModelFrame(node);
            float max = NaturalShapes.MaxHealth(node, kind);
            for (int stage = 0; stage < kind.Stages; stage++)
            {
                var state = new NaturalState(C, objects);
                state.Apply(node.Id, new NaturalOverride { Health = max * (1 - (stage + 0.5f) / kind.Stages) });
                FromSide(state, node, kind, "rock", -1);
                Assert.Equal(stage, state.Stage(node, kind), "the stage");
                (Vector3 spot, _) = state.WeakSpotAt(node, kind)!.Value;
                Vector3 local = (spot - node.Position) / node.Scale;
                float lx = Vector3.Dot(local, x), ly = Vector3.Dot(local, up), lz = Vector3.Dot(local, z);
                float rho = MathF.Sqrt(lx * lx + lz * lz);
                int ring = Enumerable.Range(0, surface.Rings.Count).MinBy(r => MathF.Abs(surface.Rings[r] - rho));
                int direction = ((int)MathF.Round(MathF.Atan2(lz, lx) / MathF.Tau * surface.Directions) % surface.Directions + surface.Directions) % surface.Directions;
                Assert.Near(surface.Height(stage, ring, direction), ly, 2e-3, $"{kind.Key} stage {stage}: on the measured top");
                Assert.True(ly >= surface.Low * surface.Top(stage) - 1e-3f, $"{kind.Key} stage {stage}: on its upper part");
                GatherResult hit = AtSpot(state, node, kind, "rock");
                Assert.True(hit.WeakSpot && Vector3.Distance(hit.Point, spot) < 0.08f, $"{kind.Key} stage {stage}: a swing at it meets the rock there");
                checkedSpots++;
            }
        }
        Fixtures.Report($"{checkedSpots} spots on the measured rock");
        Assert.True(checkedSpots >= 9, "three outcrops, three stages each");
    }

    /// <summary>
    /// gather.json's looks (GROUND.md §9.3) name effects fx.json has and sound events sounds.json has, for every stage an
    /// outcrop can reach: a typo would play nothing.
    /// </summary>
    [Test]
    public static void GatherLooksNameRealEffectsAndSounds()
    {
        HashSet<string> Keys(string file, string list) => JsonDocument.Parse(File.ReadAllText(Path.Combine(ContentFixture.Folder, file)))
            .RootElement.GetProperty(list).EnumerateArray().Select(e => e.GetProperty("key").GetString()!).ToHashSet();
        HashSet<string> effects = Keys("fx.json", "effects"), sounds = Keys("sounds.json", "events");
        int named = 0;
        foreach (NaturalKind kind in C.NaturalKinds.Where(k => k.Gather != null))
        {
            GatherLook look = kind.Gather!.Look;
            foreach (GatherCue? cue in new[] { look.Hit, look.SpotHit, look.Missed, look.Stage, look.Depleted })
            {
                if (cue?.Fx is { } fx)
                {
                    Assert.True(effects.Contains(fx), $"{kind.Key}: effect {fx}");
                    named++;
                }
                for (int stage = 1; cue?.Sound is { } sound && stage < kind.Stages + 1; stage++)
                {
                    string key = sound.Replace("{stage}", stage.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
                    Assert.True(sounds.Contains(key), $"{kind.Key}: sound {key}");
                    named++;
                }
            }
            foreach (string? fx in new[] { look.Marker?.Sparkle, look.Fall?.Fx })
            {
                Assert.True(fx == null || effects.Contains(fx), $"{kind.Key}: effect {fx}");
            }
        }
        Fixtures.Report($"{named} effects and sounds named by the looks");
        Assert.True(named >= 20, "every harvested kind has its cues");
    }

    /// <summary>GROUND.md §4.5: trees and outcrops are solid props in the physics while their tile is; a felled one isn't.</summary>
    [Test]
    public static void NaturalObjectsCollide()
    {
        (_, ISurface surface, ZoneFrame frame, INaturalObjects objects) = World();
        var state = new NaturalState(C, objects);
        using var world = new PhysicsWorld(surface, frame);
        _ = new NaturalBodies(world, state);
        NaturalObject tree = Find(objects, "pine-tree");
        world.EnsureGround(tree.Position, radius: 0);
        Assert.True(world.HasProp(PropLayer.Nature, tree.Id), "the tree is solid once its tile exists");
        int solid = world.PropCount;
        MeleeStats axe = C.ItemByKey("metal-hatchet").Melee!;
        while (!state.IsDepleted(tree.Id))
        {
            state.Hit(tree, axe, tree.Position, tree.Position + Vector3.UnitX * 3, 0);
        }
        Assert.True(!world.HasProp(PropLayer.Nature, tree.Id) && world.PropCount == solid - 1, "felled: gone from the physics");
        state.Respawn(long.MaxValue);
        Assert.True(world.HasProp(PropLayer.Nature, tree.Id), "grown back: solid again");
        Fixtures.Report($"{solid} solid natural objects in the tile under a pine");
    }
}

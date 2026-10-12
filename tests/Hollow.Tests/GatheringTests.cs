using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Physics;
using Hollow.Surface;

namespace Hollow.Tests;

public static class GatheringTests
{
    private static GameContent C => ContentFixture.Content;

    private static (ZoneSpec Spec, ISurface Surface, ZoneFrame Frame, INaturalObjects Objects) World()
    {
        ZoneSpec spec = ZoneSpec.FirstWorld(Fixtures.Galaxy, 28.4, -61.7)!;
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
        foreach (NaturalKind kind in C.NaturalKinds)
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
    /// the same total); the weak spot shows after the first hit, facing the hitter, counts double and moves; outcrops go
    /// through three stages, then respawn on time; pickups and berry bushes are picked whole and come back.
    /// </summary>
    [Test]
    public static void GatheringYieldDepletionAndRespawn()
    {
        (_, _, _, INaturalObjects objects) = World();
        NaturalObject tree = Find(objects, "pine-tree");
        NaturalKind pine = C.Natural("pine-tree")!;
        int wood = NaturalShapes.Total(tree, pine, pine.Yield[0]);
        long now = 1_800_000_000_000;

        // Same tree, two tools, hit from the east on its west side: never on the weak spot, which faces the hitter.
        Vector3 east = tree.Position + new Vector3(3, 1.6f, 0), west = tree.Position + new Vector3(-0.6f, 1.2f, 0);
        int HitsToFell(string tool, out int total)
        {
            var state = new NaturalState(C, objects);
            MeleeStats stats = C.ItemByKey(tool).Melee!;
            total = 0;
            int hits = 0;
            while (!state.IsDepleted(tree.Id) && hits < 1000)
            {
                GatherResult r = state.Hit(tree, stats, west, east, now);
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

        // The weak spot: none until the first hit, then on the hitter's side; a hit on it counts double and moves it.
        var weak = new NaturalState(C, objects);
        MeleeStats hatchet = C.ItemByKey("stone-hatchet").Melee!;
        Assert.True(weak.WeakSpot(tree, pine) == null, "no weak spot before the first hit");
        GatherResult first = weak.Hit(tree, hatchet, west, east, now);
        Vector3 before = weak.WeakSpot(tree, pine)!.Value;
        Assert.True(!first.WeakSpot && Vector3.Dot(before - tree.Position, east - tree.Position) > 0, "it shows facing the hitter");
        float health = weak.Health(tree, pine);
        GatherResult onSpot = weak.Hit(tree, hatchet, before, east, now);
        Vector3 after = weak.WeakSpot(tree, pine)!.Value;
        Assert.True(onSpot.WeakSpot && Vector3.Distance(before, after) > 0.05f, "a weak-spot hit moves the spot");
        Assert.Near(health - 2 * hatchet.Tree, weak.Health(tree, pine), 1e-3, "and counts double");
        Assert.True(Vector3.Dot(after - tree.Position, east - tree.Position) > 0, "still on the hitter's side");

        // An outcrop through its stages, then gone, then back on time.
        NaturalObject node = Find(objects, "metal-node");
        NaturalKind metal = C.Natural("metal-node")!;
        var rocks = new NaturalState(C, objects);
        MeleeStats pick = C.ItemByKey("stone-pickaxe").Melee!;
        var stages = new List<int>();
        var gained = new Dictionary<string, int>();
        while (!rocks.IsDepleted(node.Id))
        {
            stages.Add(rocks.Stage(node, metal));
            GatherResult r = rocks.Hit(node, pick, node.Position - node.Up * 5, node.Position + new Vector3(3, 1.6f, 0), now);
            foreach (ItemAmount y in r.Yield)
            {
                gained[y.Item.Key] = gained.GetValueOrDefault(y.Item.Key) + y.Count;
            }
        }
        Assert.True(stages.Distinct().SequenceEqual(new[] { 0, 1, 2 }), $"three stages in order: {string.Join(",", stages.Distinct())}");
        Assert.Equal(NaturalShapes.Total(node, metal, metal.Yield[0]), gained["metal-ore"], "all the ore");
        Assert.Equal(NaturalShapes.Total(node, metal, metal.Yield[1]), gained["high-grade-ore"], "and the rich ore");
        Assert.True(rocks.Changed.Contains(node.Id), "a change to send and save");
        Assert.Equal(0, rocks.Respawn(now + 599_000), "not before ten minutes");
        Assert.Equal(1, rocks.Respawn(now + 600_000), "back after ten");
        Assert.True(!rocks.IsDepleted(node.Id) && rocks.Health(node, metal) == NaturalShapes.MaxHealth(node, metal), "whole again");

        // Pickups and berry bushes: picked whole with the interact key, then back after their time.
        var plants = new NaturalState(C, objects);
        NaturalObject bush = Find(objects, "red-berry-bush");
        GatherResult berries = plants.Pick(bush, now);
        Assert.True(berries.Hit && berries.Yield.Single().Item.Key == "red-berries" && berries.Yield[0].Count == 4, "four red berries");
        Assert.True(!plants.Pick(bush, now + 1000).Hit, "a bare bush gives nothing");
        NaturalObject fibre = Find(objects, "fibre-plant");
        Assert.Equal(10, plants.Pick(fibre, now).Yield.Single().Count, "ten fibre");
        Assert.True(!plants.Pick(tree, now).Hit, "trees aren't picked");
        Assert.Equal(2, plants.Respawn(now + 480_000), "both back after eight minutes");
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

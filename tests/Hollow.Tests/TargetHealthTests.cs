using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Animals;
using Hollow.Simulation.Capabilities;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Players;
using Hollow.Simulation.Raid;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Tests;

public static class TargetHealthTests
{
    [Test]
    public static void HealthQueryIsBoundedAndNeverLooksThroughHealthyOccluders()
    {
        var viewer = new Viewer(new EntityId(90), null, null);
        AimedVitality? Query(params (ulong Id, float Distance, TargetVitality? Health)[] entries) =>
            TargetHealthQuery.Find(new ITargetView[] { new TestView(entries) }, Vector3.Zero, -Vector3.UnitZ, viewer);
        Assert.True(Query((1, 3, new(50, 100))) is { Vitality.Fraction: .5f }, "damaged nearest target");
        Assert.True(Query((1, 3, new(100, 100)), (2, 4, new(20, 100))) == null, "healthy foreground hides damaged target");
        Assert.True(Query((1, 3, null), (2, 4, new(20, 100))) == null, "non-damageable foreground blocks the bar");
        Assert.True(Query((1, 8.01f, new(50, 100))) == null, "beyond eight metres hidden");
        Assert.True(Query((1, 8, new(50, 100))) != null, "at eight metres visible");
        foreach (TargetVitality invalid in new[] { new TargetVitality(0, 100), new(-1, 100), new(100, 100), new(110, 100), new(1, 0), new(double.NaN, 100), new(1, double.PositiveInfinity) })
            Assert.True(Query((1, 2, invalid)) == null, "empty, full and invalid health hidden");
        Assert.True(TargetHealthQuery.Find(new ITargetView[] { new TestView((1, 2, new(1, 2))) }, Vector3.Zero, Vector3.Zero, viewer) == null,
            "zero direction produces no target");
    }

    [Test]
    public static void WorldFamiliesAdvertiseTheSameOptionalVitalityContract()
    {
        using var rig = new GameRig();
        Zone z = rig.Zone;
        Entity ada = rig.Add("Ada", 0, 0);
        var viewer = new Viewer(rig.P(ada).Id, null, null);
        void Half(TargetRef target)
        {
            Assert.True(z.Targets().FamilyOf(target.Family)!.TryFacts(target, viewer, out TargetFacts facts) && facts.Vitality.HasValue, "health advertised: " + target.Family);
            Assert.Near(.5, facts.Vitality!.Value.Fraction, .0001, "half health: " + target.Family);
        }
        Vector3 at = z.GroundAt(5, 5);
        var box = new Deployable(z.NextId(), rig.C.Deployable("small-box")!, at, z.Frame.UpAt(at), 0, viewer.Id);
        z.Things().AddStatic(box); box.Health = box.Def.Health / 2; Half(box.Ref);
        // This checks the target adapter, independent of which loot spots stream near the fixture's landing site.
        Vector3 barrelAt = z.GroundAt(8, 5);
        var barrel = new WorldProp(z.NextId(), rig.C.World.Prop("loot-barrel-1")!, barrelAt, z.Frame.UpAt(barrelAt), 0);
        z.Things().AddStatic(barrel);
        barrel.Health = barrel.Def.Health / 2; Half(barrel.Ref);
        TestBases.Hut hut = Raids.Hut(rig, 20, 20, owner: viewer.Id.Value);
        hut.Walls[1].Health = hut.Walls[1].MaxHealth / 2; Half(hut.Walls[1].Ref);
        hut.Door!.Health = hut.Door.Def.Health / 2; Half(hut.Door.Ref);
        hut.Claim!.Health = rig.C.Building.ClaimHealth / 2; Half(hut.Claim.Ref);
        Animal animal = AnimalRig.Herd(rig, "deer", 50, 50, 1).Members[0];
        animal.Health = animal.Species.Health / 2; Half(animal.Ref);
        animal.State = AnimalState.Dead;
        z.Targets().FamilyOf(TargetFamily.Animal)!.TryFacts(animal.Ref, viewer, out TargetFacts dead);
        Assert.True(dead.Vitality == null, "dead animal harvest is not presented as living health");
        var naturals = new List<NaturalObject>(); z.Nature!.Query(z.Landing, 280, naturals);
        NaturalObject node = naturals.First(o => rig.C.Natural(o.Kind) is { IsPicked: false });
        NaturalKind kind = rig.C.Natural(node.Kind)!;
        z.Natural!.Apply(node.Id, new NaturalOverride { Health = NaturalShapes.MaxHealth(node, kind) / 2 });
        Half(new TargetRef(TargetFamily.Natural, node.Id));
        z.Players().Die(ada, rig.P(ada), "test", EntityId.None, "");
        Corpse corpse = z.Things().Statics().OfType<Corpse>().Single(c => c.Of == viewer.Id);
        corpse.Life!.Restore(CorpseStage.Body, 100, corpse.Life.Tuning.HarvestHealth / 2, z.Tick);
        Half(corpse.Ref);
        corpse.Life.Restore(CorpseStage.Bag, 0, 0, z.Tick);
        z.Targets().FamilyOf(TargetFamily.Thing)!.TryFacts(corpse.Ref, viewer, out TargetFacts bag);
        Assert.True(bag.Vitality == null, "converted lootbag has no harvest bar");
        using var ships = new ShipRig(content: true);
        var ship = ships.Add(); ship.State.Health = ship.Def.Health / 2;
        Assert.True(ships.Zone.Targets().FamilyOf(TargetFamily.Ship)!.TryFacts(ship.Ref, viewer, out TargetFacts hull), "ship facts");
        Assert.Near(.5, hull.Vitality!.Value.Fraction, .0001, "ship hull shares health contract");
        var wake = ships.Zone.Ships().SpawnShip(rig.C.Ship("iron-wake")!, ships.Zone.GroundAt(60, 60), 0, viewer.Id, 100);
        foreach (int index in Enumerable.Range(1, wake.Def.Parts.Count - 1).Where(i => ships.Zone.Ships().PartHealthOf(wake, i).Def != null))
        {
            var part = wake.Def.Parts[index];
            var condition = ships.Zone.Ships().PartHealthOf(wake, index);
            if (part.Door >= 0) wake.Doors[part.Door].Health = condition.Max / 2;
            else wake.PartHealth[index] = condition.Max / 2;
            ships.Zone.Targets().FamilyOf(TargetFamily.Ship)!.TryFacts(wake.Ref with { Part = (byte)index }, viewer, out TargetFacts component);
            Assert.Near(.5, component.Vitality!.Value.Fraction, .0001, "ship part: " + part.Name);
        }
    }

    [Test]
    public static void ReplicatedDamageAppearsAndRepairRemovesTheBar()
    {
        using var rig = new LoopbackRig();
        GameClient client = rig.Join("Inspector"); rig.WaitUntilPlaying();
        (EntityId id, Vector3 centre, Vector3 stand) = rig.Server.Inspect(server =>
        {
            Vector3 at = server.Zone.GroundAt(0, -2);
            var box = new Deployable(server.Zone.NextId(), server.Content.Deployable("small-box")!, at,
                server.Zone.Frame.UpAt(at), 0, client.EntityId);
            box.Health = box.Def.Health / 2; server.Zone.Things().AddStatic(box);
            return (box.Id, box.Centre, server.Zone.GroundAt(0, 0));
        });
        rig.Teleport("Inspector", stand);
        void Until(Func<bool> ready, string what)
        {
            var watch = Stopwatch.StartNew();
            while (!ready()) { Assert.True(watch.Elapsed.TotalSeconds < 8, what); rig.Run(.05, (_, _) => default); }
        }
        Until(() => client.Survival().ThingOf(id) != null && Vector3.Distance(client.Character.Position, stand) < .2f, "replicated damaged box and player position");
        Vector3 eye = client.Character.Position + client.Frame.UpAt(client.Character.Position) * 1.5f;
        AimedVitality? visible = client.Targets().AimedHealth(eye, centre - eye);
        Assert.True(visible is { } hit && hit.Target.Id == id.Value && Math.Abs(hit.Vitality.Fraction - .5) < .005, "client aims at the replicated half-health box");
        rig.Server.Inspect(server => { var box = (Deployable)server.Zone.Things().StaticOf(id)!; box.Health = box.Def.Health; box.Revision++; return 0; });
        Until(() => client.Survival().ThingOf(id)!.Health == 1, "repair replication");
        Assert.True(client.Targets().AimedHealth(eye, centre - eye) == null, "repaired target no longer shows a bar");
    }

    private sealed class TestView(params (ulong Id, float Distance, TargetVitality? Health)[] entries) : ITargetView
    {
        public TargetFamily Family => TargetFamily.Thing;
        public void Aim(in AimRay ray, ref AimHit best)
        {
            foreach (var entry in entries) ray.Consider(ref best, entry.Distance, new TargetRef(Family, entry.Id));
        }
        public bool TryFacts(in TargetRef target, in Viewer viewer, out TargetFacts facts)
        {
            foreach (var entry in entries) if (entry.Id == target.Id) { facts = new TargetFacts { Name = "Test target", Vitality = entry.Health }; return true; }
            facts = default; return false;
        }
        public bool TryPose(in TargetRef target, out TargetPose pose) { pose = default; return false; }
        public bool TryNearest(in TargetRef target, Vector3 from, out Vector3 point) { point = default; return false; }
    }
}

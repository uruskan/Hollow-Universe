using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Players;
using Hollow.Simulation.Raid;
using Hollow.Simulation.World;
using Hollow.Simulation.Targets;

namespace Hollow.Tests;

/// <summary>The damage pipeline's Destroyed event (GROUND.md §6.4.2), in the zone and over the network.</summary>
public static class DamageTests
{
    private static List<ZoneEvent> Destroyed(GameRig rig) => rig.Zone.GameEvents.Where(e => e.Kind == ZoneEventKind.Destroyed).ToList();

    /// <summary>
    /// GROUND.md §6.4.2: what breaks is reported once, from the one place, saying what it was, where it stood and why: a
    /// wooden door blown off its stone doorway by a timed charge (an explosion, the blow's direction, whose and with what;
    /// the doorway only scorched), a box broken by a blow, a roof demolished with the hammer, and a foundation broken under
    /// the hut (a blow): the claim device on it goes with it, the walls and doorway come down after it (collapse).
    /// </summary>
    [Test]
    public static void DestroyedEventsSayWhatBrokeHowAndWhere()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        Entity ada = rig.Add("Ada", 0, -5); // behind the hut, out of the blast at its door
        Player a = rig.P(ada);
        Raids.Belt(rig, ada, 0, "hammer", 1);
        TestBases.Hut hut = Raids.Hut(rig, 0, 0, grade: 2, owner: a.Id.Value, door: "wooden-door");
        Door door = hut.Door!;

        // A timed charge on the door.
        (Vector3 frameAt, Quaternion frameTurn) = door.Frame.ZoneTransform;
        Vector3 outward = Vector3.Transform(Vector3.UnitZ, frameTurn);
        (Vector3 hinge, Quaternion leafTurn) = door.Leaf(0, false);
        Vector3 size = door.Def.Leaves[0].Size;
        Vector3 face = hinge + Vector3.Transform(new Vector3(size.X / 2, 1.1f, MathF.Abs(size.Z) / 2), leafTurn);
        ushort charge = c.ItemByKey("timed-charge").Id;
        rig.Zone.GameEvents.Clear();
        rig.Zone.Damage().Blast(c.Blast("timed-charge")!, face + outward * ExplosiveSystem.StuckOffset, outward, door.Ref, TargetFamily.Door, a.Id, a.Name, charge);
        List<ZoneEvent> blown = Destroyed(rig);
        Assert.True(door.Gone && !hut.Doorway.Gone, "the door is blown off, the stone doorway stands");
        Assert.Equal(1, blown.Count, $"one thing destroyed ({string.Join(", ", blown.Select(e => e.Text))})");
        ZoneEvent d = blown[0];
        Assert.True(d.Target == door.Ref && d.Other == door.Id && d.Text == "wooden-door", $"the door, by its ID and key ({d.Target} {d.Text})");
        Assert.True(d.Cause == DestroyCause.Explosion && d.Player == a.Id && d.Item == charge, $"by Ada's timed charge ({d.Cause})");
        Assert.True(Vector3.Distance(d.Position, frameAt) < 1e-4f && Quaternion.Dot(d.Rotation, frameTurn) > 0.9999f && !d.Flag,
            "where its frame stands, shut");
        Assert.True(Vector3.Dot(d.Velocity, -outward) > 0.999f, "the blow travelling into it");

        // A box broken by a blow.
        Vector3 boxAt = rig.Zone.GroundAt(4, 4);
        var box = new Deployable(rig.Zone.NextId(), c.Deployable("large-box")!, boxAt, rig.Zone.Frame.UpAt(boxAt), 0.7f, EntityId.None);
        rig.Zone.Things().AddStatic(box);
        rig.Zone.GameEvents.Clear();
        rig.Zone.Things().DamageDeployable(box, 10_000);
        ZoneEvent b = Destroyed(rig).Single();
        Vector3 up = rig.Zone.Frame.UpAt(boxAt);
        (Vector3 right, Vector3 forward) = Geometry.Axes(up, 0.7f);
        Assert.True(b.Target == box.Ref && b.Other == box.Id && b.Text == "large-box" && b.Cause == DestroyCause.Melee,
            $"the box, by a blow ({b.Target} {b.Text} {b.Cause})");
        Assert.True(Vector3.Distance(b.Position, boxAt) < 1e-5f && Vector3.Distance(Vector3.Transform(Vector3.UnitY, b.Rotation), up) < 1e-4f &&
            Vector3.Distance(Vector3.Transform(-Vector3.UnitZ, b.Rotation), forward) < 1e-4f && Vector3.Distance(Vector3.Transform(Vector3.UnitX, b.Rotation), right) < 1e-4f,
            "standing where it stood, turned as it was drawn");

        // The roof demolished with the hammer, within its edit window.
        Piece roof = hut.Roof!;
        rig.Zone.GameEvents.Clear();
        Outcome demolished = rig.Zone.Building().Hammer(ada, HammerAction.Demolish, roof.Id, 0);
        Assert.True(demolished.Ok, $"Ada demolishes the roof ({demolished})");
        ZoneEvent r = Destroyed(rig).Single();
        Assert.True(r.Target == roof.Ref && r.Other == roof.Id && r.Text == "floor" && r.Grade == 2 && r.Cause == DestroyCause.Demolish &&
            r.Player == a.Id && r.Velocity == Vector3.Zero, $"the roof, demolished by Ada ({r.Text} {r.Cause})");

        // The foundation broken by a blow: the claim device on it goes with it; the walls come down after it.
        ClaimDevice claim = hut.Claim!;
        rig.Zone.GameEvents.Clear();
        var seen = new List<ZoneEvent>();
        rig.Zone.Building().DamageBuilding(hut.Foundation, -up, 1_000_000, projectile: false);
        seen.AddRange(Destroyed(rig));
        rig.Zone.GameEvents.Clear();
        int fell = Raids.StepUntil(rig, () =>
        {
            seen.AddRange(Destroyed(rig));
            rig.Zone.GameEvents.Clear();
            return hut.Walls.All(w => w.Gone) && rig.Zone.Building().World!.Collapsing == 0;
        }, 90, "the walls coming down");
        Assert.True(seen.Any(e => e.Other == hut.Foundation.Id && e.Cause == DestroyCause.Melee && e.Text == "foundation"), "the foundation, by the blow");
        Assert.True(seen.Any(e => e.Target == claim.Ref && e.Cause == DestroyCause.Melee),
            "the claim device with its piece, by the same blow");
        foreach (Piece wall in hut.Walls)
        {
            ZoneEvent w = seen.Single(e => e.Other == wall.Id);
            Assert.True(w.Cause == DestroyCause.Collapse && w.Velocity == Vector3.Zero && w.Text == wall.Def.Key, $"{wall.Def.Key}: brought down ({w.Cause})");
        }
        Assert.Equal(1 + 1 + hut.Walls.Length, seen.Count, "each once");
        Assert.True(seen.All(e => float.IsFinite(e.Position.X) && MathF.Abs(e.Rotation.Length() - 1) < 1e-4f), "every one placed");
        Fixtures.Report($"door: {d.Cause}; box: {b.Cause}; roof: {r.Cause}; foundation and claim device: {DestroyCause.Melee}; " +
            $"{hut.Walls.Length} walls brought down within {fell} ticks");
    }

    /// <summary>
    /// GROUND.md §6.4.2 over the network: a barrel broken next to a player reaches their client as a <see cref="DestroyedEvent"/>
    /// (a world prop, by its ID and model key, where it stood, how it broke).
    /// </summary>
    [Test]
    public static void DestroyedReachesClientsInRange()
    {
        using var rig = new LoopbackRig();
        GameClient ada = rig.Join("Ada");
        rig.WaitUntilPlaying();
        (EntityId barrel, Vector3 at) = rig.Server.Inspect(sv =>
        {
            WorldProp p = sv.Zone.World().Props.Where(x => x.Prop.Kind == WorldPropKind.Barrel).OrderBy(x => x.Position.Length()).First();
            return (p.Id, p.Position);
        });
        Vector3 stand = at + Vector3.Normalize(new Vector3(-at.X, 0, -at.Z)) * 2f;
        rig.Server.Execute(FormattableString.Invariant($"teleport Ada {stand.X} {stand.Z}"));
        RunUntil(rig, () => ada.ThingOf(barrel) != null, "the barrel in range");
        string key = rig.Server.Inspect(sv =>
        {
            var prop = (WorldProp)sv.Zone.Things().StaticOf(barrel)!;
            sv.Zone.Things().DamageDeployable(prop, 1_000);
            return prop.Prop.Key;
        });
        RunUntil(rig, () => ada.Destructions.Any(e => e.Target.Id == barrel.Value), "the barrel's breaking");
        DestroyedEvent e = ada.Destructions.Single(x => x.Target.Id == barrel.Value);
        Assert.True(e.Target == new TargetRef(TargetFamily.Thing, barrel.Value) && e.Key == key && e.Cause == DestroyCause.Melee,
            $"a prop, by its key ({e.Target} {e.Key} {e.Cause})");
        Assert.True(Vector3.Distance(e.Position, at) < 1e-3f && MathF.Abs(e.Rotation.Length() - 1) < 1e-4f, "where it stood");
        Fixtures.Report($"{e.Key} broken {Vector3.Distance(e.Position, ada.Character.Position):0.0} m from Ada reached her client");
    }

    private static void RunUntil(LoopbackRig rig, Func<bool> done, string what, double seconds = 6)
    {
        var watch = Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(watch.Elapsed.TotalSeconds < seconds, $"timed out waiting for {what}");
            rig.Run(0.05, (_, _) => default);
        }
    }
}

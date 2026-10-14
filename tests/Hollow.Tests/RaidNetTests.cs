using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Server;
using Hollow.Server.Notifications;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Players;
using Hollow.Simulation.Raid;
using Hollow.Simulation.World;
using Hollow.Simulation.Targets;

namespace Hollow.Tests;

public static class RaidNetTests
{
    private static void RunUntil(LoopbackRig rig, Func<bool> done, string what, double seconds, Func<GameClient, double, InputFrame>? input = null)
    {
        var watch = Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(watch.Elapsed.TotalSeconds < seconds, $"timed out waiting for {what}");
            rig.Run(0.05, input ?? ((_, _) => default));
        }
    }

    private static (float Yaw, float Pitch) LookAt(Vector3 eye, Vector3 at, Vector3 up)
    {
        Vector3 d = at - eye;
        Vector3 flat = d - up * Vector3.Dot(d, up);
        return (MathF.Atan2(-flat.X, -flat.Z), MathF.Atan2(Vector3.Dot(d, up), flat.Length()));
    }

    /// <summary>
    /// GROUND.md §6.6 over the network: Bo throws a tin grenade at a stone hut listing Ada (the intent: it leaves his belt
    /// at once); both clients see it fly on its arc and come to rest, its fuse burning; if it fizzles, Bo lights it again with
    /// the interact key; when it goes off both clients get the detonation (where, how big, of what), and Ada, playing, gets a
    /// raid alert for her base in the game.
    /// </summary>
    [Test]
    public static void RaidingOverTheNetwork()
    {
        using var rig = new LoopbackRig();
        GameContent c = ContentFixture.Content;
        GameClient ada = rig.Join("Ada");
        GameClient bo = rig.Join("Bo");
        rig.WaitUntilPlaying();
        rig.Server.Execute("teleport Ada 0 -12");
        rig.Server.Execute("teleport Bo 0 6");
        rig.Server.Execute("give Bo tin-grenade 1");
        RunUntil(rig, () => bo.Local.Inventory.Count(c.ItemByKey("tin-grenade")) == 1, "the grenade", 5);
        (Vector3 face, Vector3 outward) = rig.Server.Inspect(sv =>
        {
            Assert.True(sv.Zone.Players().TryFindPlayer(ada.EntityId, out _, out Player owner), "Ada's character");
            TestBases.Hut hut = TestBases.Build(sv.Zone, sv.Zone.GroundAt(0, 0), 0, 2, owner.Id.Value)!;
            return hut.Face(hut.Doorway, height: 0.6);
        });
        var seen = new List<ClientExplosive>();
        var blasts = new List<DetonationEvent>();
        var alerts = new List<RaidAlertView>();
        ada.Raid().ExplosiveChanged += e => seen.Add(e);
        ada.Raid().Detonated += blasts.Add;
        bo.Raid().Detonated += blasts.Add;
        ada.Local.RaidAlertReceived += alerts.Add;

        // Bo, 4.5 m from the doorway's face, moves the grenade to his belt and throws it at the door's foot.
        int slot = Enumerable.Range(0, PlayerInventory.MainSlots).First(i => bo.Local.Inventory.Main[i].Item == c.ItemByKey("tin-grenade").Id);
        bo.Local.MoveItem(ContainerRef.Main, slot, ContainerRef.Belt, 2);
        rig.Server.Execute($"teleport Bo {face.X + outward.X * 4.5f} {face.Z + outward.Z * 4.5f}");
        rig.Run(0.6, (_, _) => default);
        Vector3 feet = bo.Character.Position;
        Vector3 up = bo.Frame.UpAt(feet);
        (float yaw, float pitch) = LookAt(feet + up * bo.EyeHeight, face, up);
        InputFrame Aim(GameClient client, double t) => client == bo ? new InputFrame { Yaw = yaw, Pitch = pitch } : default;
        rig.Run(0.3, Aim);
        Assert.True(bo.Local.Throw(2, yaw, pitch) != 0, "thrown");
        Assert.True(bo.Local.Inventory.Belt[2].IsEmpty, "it left the belt at once");
        RunUntil(rig, () => ada.Raid().Explosives.Count == 1 && bo.Raid().Explosives.Count == 1, "the grenade on both clients", 3, Aim);
        ClientExplosive grenade = ada.Raid().Explosives.Single();
        Assert.True(grenade.Item.Key == "tin-grenade" && grenade.Burning && grenade.FuseTicks is >= 100 and <= 140, $"a lit tin grenade ({grenade})");
        RunUntil(rig, () => grenade.State == ExplosiveState.Resting || blasts.Count > 0, "the grenade coming to rest", 4, Aim);
        int bounces = seen.Count(e => e == grenade) - 1;

        // It goes off (or fizzles, and Bo lights it again with the interact key).
        int lit = 0;
        while (blasts.Count == 0)
        {
            RunUntil(rig, () => blasts.Count > 0 || bo.Raid().ExplosiveOf(grenade.Id)?.Dud == true, "the blast or a dud", 8, Aim);
            if (blasts.Count > 0)
            {
                break;
            }
            Assert.True(lit++ < 6, "it goes off in the end");
            Vector3 at = bo.Raid().ExplosiveOf(grenade.Id)!.PositionAt(bo.ServerTickNow);
            rig.Server.Execute($"teleport Bo {at.X + outward.X * 1.5f} {at.Z + outward.Z * 1.5f}");
            rig.Run(0.5, (_, _) => default);
            Vector3 eye = bo.Character.Position + up * bo.EyeHeight;
            InteractAim? target = bo.Interaction().AimInteraction(eye, Vector3.Normalize(at - eye));
            Assert.True(target is { Primary: >= 0 } && target.Target.Id == grenade.Id.Value &&
                bo.Content.Interactions.Verb(target.Options[target.Primary].Verb)?.Key == "relight", $"the dud is the interact key's target ({target?.Target})");
            bo.Local.Interact(target!.Target, target.Options[target.Primary].Verb);
            RunUntil(rig, () => bo.Raid().ExplosiveOf(grenade.Id) is { Dud: false, Burning: true }, "the grenade lit again", 3);
            rig.Server.Execute($"teleport Bo {at.X + outward.X * 9} {at.Z + outward.Z * 9}");
        }
        RunUntil(rig, () => blasts.Count >= 2, "the blast on both clients", 2);
        DetonationEvent blast = blasts[0];
        Assert.True(blast.Item?.Key == "tin-grenade" && blast.Explosive == grenade.Id && Math.Abs(blast.Radius - 3.5f) < 0.05f, $"what went off ({blast.Item?.Key}, {blast.Radius} m)");
        Assert.True(ada.Raid().Explosives.Count == 0 && bo.Raid().Explosives.Count == 0, "and it is gone");

        // Ada, playing, hears of the raid on her hut.
        RunUntil(rig, () => alerts.Count > 0, "Ada's raid alert", 3);
        RaidAlertView alert = alerts[0];
        Assert.True(!alert.WhileAway && alert.Item?.Key == "tin-grenade" && alert.What.Length > 0, $"the alert: {alert.What} with {alert.Item?.Key}");
        Fixtures.Report($"the grenade bounced {bounces} time(s), was lit again {lit} time(s), went off; Ada's alert: \"{alert.What}\" " +
            $"({alert.Hits} hits) at {alert.Last:HH:mm:ss}; {rig.Server.Inspect(sv => sv.Raid().Notifications.Counters.ToString())}");
    }

    /// <summary>
    /// GROUND.md §6.6, persistence: charges stuck in the world come back after a restart with the fuse they had left (world
    /// time: it paused while the server was down) and stuck to what they were on, a fizzling one still fizzling; a raid
    /// alert queued for Ada while she was away waits in the database and comes to her at login, with the base, the time
    /// and what was hit.
    /// </summary>
    [Test]
    public static void ChargesAndAlertsSurviveARestart()
    {
        using var file = new TempDatabase();
        GameContent c = ContentFixture.Content;
        EntityId adaId, chargeId, bagId, boxId;
        long chargeLeft;
        using (var rig = new LoopbackRig(file.Path))
        {
            GameClient ada = rig.Join("Ada");
            rig.WaitUntilPlaying();
            adaId = ada.EntityId;
            rig.Server.Execute("teleport Ada 0 -12");
            rig.Run(0.5, (_, _) => default);
            ada.Disconnect();
            rig.Clients.Remove(ada);
            ada.Dispose();
            rig.Run(0.5, (_, _) => default);
            (chargeId, bagId, boxId, chargeLeft) = rig.Server.Inspect(sv =>
            {
                Zone z = sv.Zone;
                // A box with a bag charge stuck to it (one that will fizzle), and a timed charge on the ground.
                Vector3 boxAt = z.GroundAt(6, -6);
                var box = new Deployable(z.NextId(), c.Deployable("small-box")!, boxAt, z.Frame.UpAt(boxAt), 0, EntityId.None);
                z.Things().AddStatic(box);
                LiveExplosive bag = z.Explosives().StickExplosive(c.ItemByKey("bag-charge").Throwable!, box.Centre + new Vector3(0, 2, 0), -Vector3.UnitY, 3, EntityId.None, "Bo", dud: true)!;
                Vector3 ground = z.GroundAt(-6, -6);
                LiveExplosive charge = z.Explosives().StickExplosive(c.ItemByKey("timed-charge").Throwable!, ground + z.Frame.UpAt(ground) * 2, -z.Frame.UpAt(ground), 3,
                    EntityId.None, "Bo")!;
                Assert.True(bag.Link.Support.Whole == box.Ref && charge.Link.Support.Family == TargetFamily.Ground, "stuck to the box and the ground");
                // A raid on Ada's hut while she is away.
                Assert.True(z.Players().TryFindPlayer(adaId, out _, out Player owner) && owner.Sleeping, "Ada asleep");
                TestBases.Hut hut = TestBases.Build(z, z.GroundAt(0, 6), 0, 2, owner.Id.Value)!;
                (Vector3 face, Vector3 outward) = hut.Face(hut.Walls[1]);
                z.Damage().Blast(c.Blast("timed-charge")!, face + outward * ExplosiveSystem.StuckOffset, outward, hut.Walls[1].Ref, TargetFamily.Piece, EntityId.None, "Bo",
                    c.ItemByKey("timed-charge").Id);
                return (charge.Id, bag.Id, box.Id, charge.FuseEndTick - z.Tick);
            });
            rig.Run(0.3, (_, _) => default);
            Assert.Equal(1L, rig.Server.Inspect(sv => sv.Raid().Notifications.Counters.Queued), "the alert queued for Ada");
        }

        using (var rig = new LoopbackRig(file.Path))
        {
            string report = rig.Server.Inspect(sv =>
            {
                Zone z = sv.Zone;
                LiveExplosive? charge = z.Explosives().ExplosiveOf(chargeId), bag = z.Explosives().ExplosiveOf(bagId);
                Assert.True(charge != null && bag != null, "both charges are back");
                long left = charge!.FuseEndTick - z.Tick;
                Assert.InRange(chargeLeft - 60, chargeLeft, left, "the timed charge's fuse goes on from where it was");
                Assert.True(charge.State == ExplosiveState.Stuck && charge.Link.Support.Family == TargetFamily.Ground && charge.Burning, "stuck to the ground, burning");
                Assert.True(bag!.State == ExplosiveState.Stuck && bag.Link.Support == new TargetRef(TargetFamily.Thing, boxId.Value), "the bag charge on its box");
                Assert.True(bag.WillDud && bag.Burning, "still going to fizzle");
                return $"timed charge {left / 30.0:0.0} s left (was {chargeLeft / 30.0:0.0} s)";
            });
            GameClient ada = rig.Join("Ada");
            rig.WaitUntilPlaying();
            RunUntil(rig, () => ada.Local.RaidAlerts.Count > 0 && ada.Raid().Explosives.Count >= 2, "Ada's waiting alert and the charges", 5);
            RaidAlertView alert = ada.Local.RaidAlerts.Single();
            Assert.True(alert.WhileAway && alert.What.Contains("Stone wall", StringComparison.Ordinal) && alert.Item?.Key == "timed-charge",
                $"what was hit and with what: {alert.What}");
            Assert.True((DateTimeOffset.UtcNow - alert.Last).TotalMinutes < 5, "and when");
            Assert.True(ada.Raid().Explosives.Any(e => e.Id == chargeId && e.Burning), "Ada's client sees the burning charge");
            Fixtures.Report($"after the restart: {report}; Ada's alert at login: \"{alert.What}\" ({alert.Last:HH:mm:ss})");
        }
    }
}

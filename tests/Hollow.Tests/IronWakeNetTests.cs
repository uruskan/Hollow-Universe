using System;
using System.Diagnostics;
using System.Numerics;
using Hollow.Client;
using Hollow.Server;
using Hollow.Simulation;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;

namespace Hollow.Tests;

/// <summary>The Iron Wake over the network (IRON_WAKE.md §4.8, §4.11): prediction aboard, and sleepers aboard across a restart.</summary>
public static class IronWakeNetTests
{
    internal readonly record struct ShipFacts(ulong Id, ShipPose Pose, bool Parked, float Height);

    // The server's Iron Wake: its ID, where it is, parked or not, its pads' height over the ground.
    internal static ShipFacts Facts(LoopbackRig rig) => rig.Server.Inspect(sv =>
    {
        foreach (Ship ship in sv.Zone.Ships().All)
        {
            if (ship.Def.Interior != null)
            {
                return new ShipFacts(ship.Id.Value, ShipFrame.PoseOf(ship.State, ship.Def.CentreOfMass), ship.ParkedBody != 0, sv.Zone.HeightAboveGround(ship.Origin));
            }
        }
        return default;
    });

    // Gives a player an Iron Wake (the admin's command) and waits until it stands parked on its pads.
    internal static ShipFacts Spawn(LoopbackRig rig, string owner)
    {
        rig.Server.Execute($"ship {owner} iron-wake");
        var wait = Stopwatch.StartNew();
        ShipFacts ship;
        do
        {
            rig.Run(0.25, (_, _) => default);
            ship = Facts(rig);
        }
        while (!ship.Parked && wait.Elapsed.TotalSeconds < 20);
        Assert.True(ship.Parked, $"the Iron Wake parks beside {owner}");
        return ship;
    }

    // A player's character on the server: its state (in its frame).
    internal static CharacterState ServerState(LoopbackRig rig, EntityId id) => rig.Server.Inspect(sv =>
        sv.Zone.Entities.TryFind(id, out Entity e) ? sv.Zone.Entities.Get<Character>(e).State : default);

    // Full health and vitals for a player, so nothing the prediction doesn't know of changes it (healing, hunger).
    internal static void Heal(LoopbackRig rig, EntityId id) => rig.Server.Inspect(sv =>
    {
        Assert.True(sv.Zone.Entities.TryFind(id, out Entity e), "the character is in the zone");
        Hollow.Simulation.Players.Player p = sv.Zone.Players().PlayerOf(e)!;
        p.Vitals.Health = 100;
        p.Vitals.PendingHeal = 0;
        p.Vitals.Calories = 500;
        p.Vitals.Hydration = 250;
        sv.Zone.Entities.Get<Character>(e).State.Health = 100;
        return true;
    });

    // Puts a player's character aboard on the server, standing at a point of the ship's frame (as a teleport would).
    internal static void PutAboard(LoopbackRig rig, EntityId id, ulong ship, Vector3 local) => rig.Server.Inspect(sv =>
    {
        Assert.True(sv.Zone.Entities.TryFind(id, out Entity e), "the character is in the zone");
        ref Character c = ref sv.Zone.Entities.Get<Character>(e);
        CharacterState state = sv.Zone.NewCharacter(local + Vector3.UnitY * 0.02f);
        state.Frame = ship;
        c.State = state;
        c.Input = new MoveInput { View = Vector3.UnitZ, Frame = ship };
        return true;
    });

    /// <summary>
    /// IRON_WAKE.md §4.8, §14: over the real network a player walks up the parked Iron Wake's ramp (predicted through the
    /// hand-over), and another takes the helm and flies it for 30 s: up, forward, round and to a stop. The passenger walks
    /// up and down Deck A all the while; their prediction is never corrected (the ship's flight never enters it).
    /// </summary>
    [Test]
    public static void APassengerIsPredictedExactlyWhileAnotherFlies()
    {
        using var rig = new LoopbackRig();
        GameClient ada = rig.Join("Ada");
        GameClient bo = rig.Join("Bo");
        rig.WaitUntilPlaying();
        ShipFacts ship = Spawn(rig, "Bo");

        // Full health (no healing the prediction doesn't know of), then Ada behind the ramp, and up it: her view in
        // whichever frame her client has her in.
        Heal(rig, ada.EntityId);
        rig.Teleport("Ada", ship.Pose.ToWorld(new Vector3(0, 0, -30)));
        rig.Run(2.0, (_, _) => default);
        Vector3 forward = ship.Pose.DirectionToWorld(Vector3.UnitZ);
        long walkCorrections = ada.Stats.Corrections, walkReplayed = ada.Prediction!.Replayed;
        rig.Run(6.5, (c, _) => c == ada
            ? new InputFrame { Forward = 1, View = ada.OwnFrame == 0 ? forward : Vector3.UnitZ, ViewFrame = ada.OwnFrame }
            : default);
        rig.Run(0.5, (_, _) => default);
        CharacterState aboard = ServerState(rig, ada.EntityId);
        Assert.True(aboard.Frame == ship.Id && ada.OwnFrame != 0, "Ada walked aboard, on the server and in her client");
        Fixtures.Report($"up the ramp: {ada.Stats.Corrections - walkCorrections} corrections, {ada.Prediction.Replayed - walkReplayed} ticks replayed; " +
            $"on the server {aboard}");

        // Bo onto the bridge and into the helm's seat.
        PutAboard(rig, bo.EntityId, ship.Id, new Vector3(-1.75f, 9.1f, 13.9f));
        rig.Run(0.8, (_, _) => default);
        bo.Local.Interact(new TargetRef(TargetFamily.Ship, ship.Id, (byte)ContentFixture.Content.Ship("iron-wake")!.PartIndex("helm")), "take-helm");
        rig.Run(0.8, (_, _) => default);
        Assert.True(bo.Ships().Piloting, "Bo at the helm, his client predicting the ship");

        // 30 s of flight; Ada walks up and down Deck A.
        long corrections = ada.Stats.Corrections, replayed = ada.Prediction.Replayed, reconciled = ada.Stats.Reconciliations;
        long shipCorrections = bo.Stats.ShipCorrections;
        float highest = 0;
        rig.Run(30.0, (c, t) =>
        {
            if (c == bo)
            {
                return new InputFrame
                {
                    Ship = t < 5 ? new ShipInput { Vertical = 1 } : t < 20 ? new ShipInput { Vertical = 0.2f, Throttle = 0.35f, Yaw = 0.6f } : default,
                };
            }
            if (c == ada)
            {
                highest = MathF.Max(highest, ada.Ships().ShipByNetId(ada.OwnFrame)?.Position.Length() ?? 0);
                return new InputFrame { Forward = (int)(t / 1.5) % 2 == 0 ? 1 : -1, View = Vector3.UnitZ, ViewFrame = ada.OwnFrame };
            }
            return default;
        });
        ShipFacts after = Facts(rig);
        CharacterState end = ServerState(rig, ada.EntityId);
        float climbed = after.Pose.Position.Length() - ship.Pose.Position.Length(), travelled = Vector3.Distance(after.Pose.Position, ship.Pose.Position);
        Fixtures.Report(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"the Iron Wake climbed {climbed:0.0} m and went {travelled:0.0} m; Ada aboard {end.Frame == ship.Id} at {end.Position}; " +
            $"her client reconciled {ada.Stats.Reconciliations - reconciled} times: {ada.Stats.Corrections - corrections} corrections, " +
            $"{ada.Prediction.Replayed - replayed} ticks replayed; Bo's ship corrections {bo.Stats.ShipCorrections - shipCorrections}"));
        Assert.True(climbed > 20 && travelled > 40, "Bo flew the ship up and away");
        Assert.True(end.Frame == ship.Id && ada.OwnFrame != 0, "Ada stayed aboard");
        Assert.True(ada.Stats.Reconciliations - reconciled > 400, "her client checked its prediction against the server all along");
        Assert.Equal(0L, ada.Stats.Corrections - corrections, "Ada's prediction aboard was never corrected");
        Assert.Equal(0L, ada.Prediction.Replayed - replayed, "not a single tick replayed");
    }

    /// <summary>
    /// IRON_WAKE.md §4.11, §14: a player who logs off aboard a parked Iron Wake sleeps aboard, in the ship's frame; after a
    /// restart the ship comes back and the sleeper with it, at the same place on the deck; logging in, they wake there and
    /// their client predicts them aboard.
    /// </summary>
    [Test]
    public static void ASleeperAboardComesBackAboard()
    {
        using var file = new TempDatabase();
        ShipFacts ship;
        EntityId adaId;
        CharacterState slept;
        using (var rig = new LoopbackRig(file.Path))
        {
            GameClient ada = rig.Join("Ada");
            rig.WaitUntilPlaying();
            adaId = ada.EntityId;
            ship = Spawn(rig, "Ada");
            PutAboard(rig, adaId, ship.Id, new Vector3(1.0f, 7.4f, 3.0f));
            rig.Run(1.5, (_, _) => default);
            Assert.True(ada.OwnFrame != 0, "Ada aboard on Deck B, in her client too");
            ada.Disconnect();
            rig.Clients.Remove(ada);
            ada.Dispose();
            rig.Run(1.0, (_, _) => default);
            slept = ServerState(rig, adaId);
            Assert.True(slept.Frame == ship.Id, "logged off, she sleeps aboard");
            rig.Server.Inspect(sv => sv.SaveAll()).Wait(TimeSpan.FromSeconds(10));
        }

        using (var rig = new LoopbackRig(file.Path))
        {
            CharacterState back = ServerState(rig, adaId);
            ShipFacts again = Facts(rig);
            Fixtures.Report($"slept at {slept.Position} aboard {slept.Frame:X}; after the restart {back.Position} aboard {back.Frame:X} (the ship {again.Id:X}, parked {again.Parked})");
            Assert.True(again.Id == ship.Id, "the ship came back");
            Assert.True(back.Frame == ship.Id && Vector3.Distance(back.Position, slept.Position) < 0.05f, "and the sleeper aboard it, where she lay down");

            GameClient ada = rig.Join("Ada");
            rig.WaitUntilPlaying();
            rig.Run(1.0, (_, _) => default);
            CharacterState woke = ServerState(rig, adaId);
            Fixtures.Report($"woke at {woke.Position} aboard {woke.Frame:X}; her client in frame {ada.OwnFrame}, at {ada.Character.Position}");
            Assert.True(woke.Frame == ship.Id && ada.OwnFrame != 0, "she wakes aboard, and her client has her there");
            Assert.True(Vector3.Distance(ada.Character.Position, woke.Position) < 0.01f, "predicted where the server has her");
        }
    }
}

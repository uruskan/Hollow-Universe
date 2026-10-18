using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Client;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;
using Hollow.Net.Replication;
using Hollow.Net.Transport;
using Hollow.Simulation;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;

namespace Hollow.Tests;

public static class ShipAttitudeNetTests
{
    [Test]
    public static void ShipAttitudeTakeoverPredictionAndRewindRemainExact()
    {
        using var rig = new ShipRig(content: true, worldSpec: ZoneSpec.StartingWorld(10, 20));
        (Ship ship, Entity pilot) = ShipAttitudeTests.Crewed(rig);
        Assert.True(rig.Zone.Ships().Release(pilot, SeatRelease.Moved), "release captures");
        rig.Step(20);
        Assert.True(ship.State.AttitudeHoldActive, "unattended authority holds");
        Assert.True(rig.Zone.Ships().Reseat(pilot, ship, ship.Def.Helm.Index), "awake takeover");
        ShipState own = ShipConvert.FromOwn(ShipConvert.ToOwn(ship.State, 1, ship.Def.Id, ship.Input));
        Assert.True(!own.AttitudeHoldActive && own.Equals(ship.State), "unchanged own wire carries complete inactive pilot state");
        using var inputs = new Predictor(rig.Zone.Spec, rig.Zone.Surface, null);
        var predictor = new ShipPredictor(inputs, () => rig.Zone.Ships().ShipContext);
        uint start = (uint)rig.Zone.Tick;
        predictor.Reset(ship.Def, 1, start, own, start);
        var commands = new Dictionary<uint, InputCommand>();
        ShipState correction = default;
        for (uint tick = start + 1; tick <= start + 90; tick++)
        {
            InputCommand command = NetConvert.ToCommand(new MoveInput { View = Vector3.UnitZ }, new ActionInput { HeldSlot = ActionInput.NoSlot });
            command.Ship = new[] { ShipConvert.ToControls(new ShipInput { Yaw = 0.3f, Pitch = tick % 3 == 0 ? 0.2f : 0, Throttle = 0.2f }) };
            commands.Add(tick, command);
            inputs.RecordInput(tick, command);
            predictor.Predict(tick);
            ship.Input = ShipConvert.ToShipInput(command);
            rig.Step();
            Assert.True(predictor.Latest.Equals(ship.State), $"takeover prediction matches exactly at {tick}");
            if (tick == start + 82) correction = ship.State;
        }
        correction.Rotation = Quaternion.Normalize(correction.Rotation * Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.025f));
        ShipState expected = correction;
        for (uint tick = start + 83; tick <= start + 90; tick++)
        {
            ShipInput input = ShipConvert.ToShipInput(commands[tick]);
            input.Engines = true;
            ShipMotor.Step(ref expected, input, ship.Def, rig.Zone.Ships().ShipContext);
        }
        predictor.Reconcile(ship.Def, 1, start + 82, correction, start + 90);
        Assert.Equal(8L, predictor.Replayed, "eight newer inputs actually replayed");
        Assert.True(predictor.Latest.Equals(expected), "rewind/replay agrees to the bit, not a positional tolerance");
    }

    [Test]
    public static void ShipAttitudeLateSnapshotsCannotUndoOwnershipTransitions()
    {
        var sender = new SnapshotSender();
        var receiver = new SnapshotReceiver();
        var writer = new BitWriter();
        var reader = new BitReader();
        var decoded = new List<(uint NetId, CharacterNetState State)>();
        ShipState inactive = ShipAttitudeTests.Airborne();
        OwnShipState own = ShipConvert.ToOwn(inactive, 1, ShipAttitudeTests.Carrier.Id);
        byte[] Packet(uint tick, bool occupied)
        {
            writer.Reset();
            sender.Write(writer, new SnapshotHeader { ServerTick = tick, OwnTick = tick,
                OwnShip = occupied ? new[] { own } : Array.Empty<OwnShipState>() }, Array.Empty<SnapshotCandidate>(), 1200);
            return writer.ToArray();
        }
        bool Read(byte[] packet, out SnapshotHeader header)
        {
            reader.Reset(packet, 0, packet.Length);
            reader.ReadBits(8);
            return receiver.TryRead(reader, out header, decoded);
        }
        byte[] piloted = Packet(1, true), released = Packet(2, false), retaken = Packet(3, true);
        Assert.True(Read(released, out SnapshotHeader empty) && empty.OwnShip.Length == 0, "new release header stops own prediction");
        Assert.True(!Read(piloted, out _), "late old pilot header cannot restart prediction");
        Assert.True(Read(retaken, out SnapshotHeader occupied) && occupied.OwnShip.Length == 1, "takeover header starts inactive own state");
        Assert.True(!ShipConvert.FromOwn(occupied.OwnShip[0]).AttitudeHoldActive, "wire never invents a hold target");
        Assert.True(!Read(released, out _), "late release header cannot stop the new pilot");
    }

    [Test]
    public static void ShipAttitudeTwoClientsObserveHoldAndActualHelmTakeover()
    {
        using var rig = new LoopbackRig();
        GameClient ada = rig.Join("Ada");
        GameClient bo = rig.Join("Bo", new NetConditions { LatencyMs = 100, JitterMs = 35 });
        rig.WaitUntilPlaying();
        EntityId id = rig.Server.Inspect(sv =>
        {
            Assert.True(sv.Zone.Entities.TryFind(ada.EntityId, out Entity pilot), "host entity");
            Assert.True(sv.Zone.Entities.TryFind(bo.EntityId, out Entity gunner), "guest entity");
            Vector3 at = sv.Zone.GroundAt(0, 0);
            Ship ship = sv.Zone.Ships().SpawnShip(ShipAttitudeTests.Carrier, at + sv.Zone.Frame.UpAt(at) * 350, 0, ada.EntityId, 500);
            Assert.True(sv.Zone.Ships().Reseat(pilot, ship, ship.Def.Helm.Index), "host helm");
            Assert.True(sv.Zone.Ships().Reseat(gunner, ship, ship.Def.Stations[0].Seat), "guest gunner");
            return ship.Id;
        });
        rig.Run(1.5, (_, _) => default);
        Assert.True(ada.Ships().Piloting && bo.Ships().Seated && !bo.Ships().Piloting, "real own header goes only to helmsman");
        rig.Run(0.8, (c, _) => c == ada ? new InputFrame { Ship = new ShipInput { Pitch = 0.6f, Roll = 0.5f } } : default);
        ada.Local.LeaveSeat();
        rig.Run(1.0, (_, _) => default);
        ShipState released = rig.Server.Inspect(sv => sv.Zone.Ships().ShipOf(id)!.State);
        Assert.True(released.AttitudeHoldActive && !ada.Ships().Piloting && !bo.Ships().Piloting, "actual release stops own predictor and activates only server hold");
        rig.Run(5.0, (_, _) => default);
        ShipState held = rig.Server.Inspect(sv => sv.Zone.Ships().ShipOf(id)!.State);
        Assert.True(held.AttitudeHoldActive && ShipAttitudeTests.Degrees(held.Rotation, held.AttitudeHoldRotation) < 0.2, "server returns to release target");
        foreach (GameClient client in new[] { ada, bo })
        {
            RemoteShip remote = client.Ships().ShipOf(id)!;
            Assert.True(remote != null && ShipAttitudeTests.Degrees(remote.Rotation, held.Rotation) < 1, "both remote snapshots follow held attitude despite delayed guest packets");
        }
        IronWakeNetTests.PutAboard(rig, ada.EntityId, id.Value, new Vector3(-1.75f, 9.1f, 13.9f));
        rig.Run(0.5, (_, _) => default);
        ada.Local.Interact(new TargetRef(TargetFamily.Ship, id.Value, ShipAttitudeTests.Carrier.HelmPart()), "take-helm");
        rig.Run(1.0, (_, _) => default);
        Assert.True(ada.Ships().Piloting && !bo.Ships().Piloting, "actual helm interaction resumes host prediction only");
        Assert.True(!ada.Ships().OwnShip.AttitudeHoldActive && !rig.Server.Inspect(sv => sv.Zone.Ships().ShipOf(id)!.State.AttitudeHoldActive),
            "awake takeover clears before its first own motor path");
        long corrections = ada.Stats.ShipCorrections;
        rig.Run(2.0, (c, _) => c == ada ? new InputFrame { Ship = new ShipInput { Yaw = 0.3f, Throttle = 0.2f, Vertical = 0.2f } } : default);
        Assert.Equal(0L, ada.Stats.ShipCorrections - corrections, "normal pilot replay remains exact on the clean host line");
    }
}

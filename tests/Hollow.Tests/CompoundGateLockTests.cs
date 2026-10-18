using System;
using System.Linq;
using System.Numerics;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Placement;
using Hollow.Simulation.World;

namespace Hollow.Tests;

public static class CompoundGateLockTests
{
    [Test]
    public static void CompoundGateCodeLockUsesPlacementAndSharedInteractions()
    {
        using var rig = new GameRig();
        Zone zone = rig.Zone;
        Entity owner = rig.Add("GateOwner", 20, 20), guest = rig.Add("GateGuest", 21, 20);
        Deployable gate = zone.Players().SpawnDeployable(rig.C.Deployable("wood-compound-gate")!, zone.GroundAt(20, 24), 0, rig.P(owner).Id);
        Quaternion rotation = Geometry.Upright(gate.Up, gate.Yaw);
        void Stand(Entity who, float x) => Raids.Stand(rig, who,
            zone.Settle(gate.Position + Vector3.Transform(new Vector3(x, 0, 1.6f), rotation)));
        Stand(owner, -.3f);
        Stand(guest, .5f);
        Assert.True(LockMounts.TryCompoundPose(rig.C, gate.Def, false, gate.Position, gate.Up, gate.Yaw, out var mount), "production gate has an authored lock socket");
        ItemDef lockItem = rig.C.ItemByKey("code-lock");
        rig.Give(owner, lockItem.Key, 1);
        int slot = Enumerable.Range(0, rig.P(owner).Inventory.Main.Size).First(i => rig.P(owner).Inventory.Main[i].Item == lockItem.Id);
        PlacementCandidate? ghost = zone.Placement().Aim(owner, lockItem.Places!, mount.Position - zone.EyeOf(rig.Ch(owner).State));
        Assert.True(ghost is { Valid: true } && ghost.Support.Whole == gate.Ref, $"lock ghost uses compound gate mount: {ghost}");
        Assert.True(zone.Placement().Place(owner, ContainerRef.Main, slot, ghost!).Ok, "ordinary lock item placement attaches to a gate");
        Assert.Equal(0, rig.P(owner).Inventory.Count(lockItem), "one lock item consumed");
        Assert.True(gate.CapabilitiesIn(rig.C.Capabilities).Has(rig.C.Capabilities.Index("code-lock")), "lock interactions are added dynamically");
        Assert.Equal(OutcomeCode.Done, zone.Interaction().Interact(owner, gate.Ref, "set-code", code: "2580").Code, "existing set-code handler works on a gate");
        Assert.Equal(OutcomeCode.Locked, zone.Interaction().Interact(guest, gate.Ref, "gate-open").Code, "stranger cannot open the locked gate");
        Assert.Equal(OutcomeCode.RemoveLockFirst, zone.Interaction().Interact(owner, gate.Ref, "retrieve").Code, "pickup cannot bypass the attached lock");
        Assert.Equal(OutcomeCode.WrongCode, zone.Interaction().Interact(guest, gate.Ref, "enter-code", code: "0000").Code, "wrong code is rejected");
        Assert.Equal(OutcomeCode.WaitForCode, zone.Interaction().Interact(guest, gate.Ref, "enter-code", code: "2580").Code, "existing retry throttle applies");
        zone.UnixMilliseconds += 31_000;
        Assert.Equal(OutcomeCode.Done, zone.Interaction().Interact(guest, gate.Ref, "enter-code", code: "2580").Code, "correct code authorises guest");
        Assert.Equal(OutcomeCode.Done, zone.Interaction().Interact(guest, gate.Ref, "gate-open").Code, "authorised guest opens through normal interaction");
        Assert.True(gate.Open, "gate really opened");
        Assert.True(zone.Require<CompoundInteractionSystem>().SetOpen(rig.P(owner).Id.Value, gate, false).Ok, "owner closes through same access rule");
        Assert.Equal(OutcomeCode.Done, zone.Interaction().Interact(owner, gate.Ref, "code-unlock").Code, "existing unlock handler works");
        Assert.Equal(OutcomeCode.Done, zone.Interaction().Interact(owner, gate.Ref, "code-remove").Code, "existing remove handler returns lock");
        Assert.True(gate.Lock == null && !gate.CapabilitiesIn(rig.C.Capabilities).Has(rig.C.Capabilities.Index("code-lock")), "removal clears dynamic lock capabilities");
        Assert.Equal(1, rig.P(owner).Inventory.Count(lockItem), "removed lock returned once");
        Assert.True(zone.Require<CompoundInteractionSystem>().SetOpen(999, gate, true).Ok, "unlocked gate is publicly operable");
    }

    [Test]
    public static void CompoundGateKeyLockSharesAllocatorKeysAndMovingSocket()
    {
        using var rig = new GameRig();
        Zone zone = rig.Zone;
        BuildingWorld world = zone.Building().World!;
        DeployableDef def = rig.C.Deployable("stone-compound-gate")!;
        var restored = new Deployable(zone.NextId(), def, zone.GroundAt(50, 50), zone.Frame.UpAt(zone.GroundAt(50, 50)), .4f, new EntityId(17));
        ((ILockHost)restored).Lock = LockState.RestoreKey(zone.NextId(), 17, 900, true);
        zone.Things().AddStatic(restored);
        var next = new Deployable(zone.NextId(), def, zone.GroundAt(70, 50), zone.Frame.UpAt(zone.GroundAt(70, 50)), .4f, new EntityId(17));
        Assert.True(world.AttachKeyLock(17, next, out KeyLock? key).Ok && key!.KeyCode == 901, "restored gate keys advance the one building key allocator");
        Assert.True(world.SetLocked(17, next, true).Ok, "owner locks without a carried key");
        Assert.True(!BuildingWorld.CanUse(19, next) && BuildingWorld.CanUse(19, next, new[] { key!.KeyCode }), "matching physical key admits another player");
        Assert.True(world.SetLocked(19, next, false, new[] { key!.KeyCode }).Ok, "key holder can unlock");
        Assert.Equal(OutcomeCode.NotOnLock, world.RemoveLock(19, next).Code, "key holder cannot steal the lock");
        Assert.True(world.RemoveLock(17, next).Ok, "owner removes the unlocked key lock");
        Assert.True(LockMounts.TryCompoundPose(rig.C, def, false, next.Position, next.Up, next.Yaw, out var shut) &&
            LockMounts.TryCompoundPose(rig.C, def, true, next.Position, next.Up, next.Yaw, out _), "both mount poses resolve");
        LockMounts.TryCompoundPose(rig.C, def, true, next.Position, next.Up, next.Yaw, out var open);
        var socket = def.Gate!.LockSocket!;
        Vector3 pivot = next.Position + Vector3.Transform(rig.C.Compound.Find(def.Assembly)!.Part(socket.Part)!.Pivot, Geometry.Upright(next.Up, next.Yaw));
        Assert.True(Vector3.Distance(shut.Position, open.Position) > 1, "socket moves with leaf, not gate bounds");
        Assert.Near(Vector3.Distance(shut.Position, pivot), Vector3.Distance(open.Position, pivot), .0002, "socket keeps its hinge radius on rotated curved ground");
    }
}

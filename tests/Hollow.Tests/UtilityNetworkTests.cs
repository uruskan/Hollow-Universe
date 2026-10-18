using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Hollow.Client;
using Hollow.Core;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Authorization;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Electricity;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Tests;

public static class UtilityNetworkTests
{
    private static void Until(LoopbackRig rig, Func<bool> ready, string message)
    {
        var watch = Stopwatch.StartNew();
        while (!ready())
        {
            Assert.True(watch.Elapsed.TotalSeconds < 8, "waiting for " + message);
            rig.Run(.05, (_, _) => default);
        }
    }

    private static void WaitForWireTool(LoopbackRig rig, GameClient client)
    {
        // The reliable intent and tick-input stream are independent. Inventory arrival does not acknowledge equip.
        Until(rig, () => rig.Server.Inspect(server =>
            server.Zone.Players().TryFindPlayer(client.EntityId, out _, out var player) &&
            player.Held(server.Content) is { Held: HeldUse.Wiring, Wiring: not null }), "authoritative Wire Tool equip");
    }

    [Test]
    public static void UtilityAuthorizationMenusAndLiveCupboardsReachBothClients()
    {
        using var rig = new LoopbackRig();
        GameClient owner = rig.Join("UtilityOwner"), friend = rig.Join("UtilityFriend");
        rig.WaitUntilPlaying();
        (EntityId id, EntityId cupboard, Vector3 at) = rig.Server.Inspect(server =>
        {
            Zone zone = server.Zone;
            Piece floor = BuildingNetTests.Foundation(zone, zone.GroundAt(-6, -6), owner.EntityId.Value, grade: 2);
            BuildingWorld building = zone.Building().World;
            Assert.True(building.PlaceClaim(owner.EntityId.Value, floor, Vector3D.Zero, 0, out ClaimDevice? claim).Ok,
                "place the governing cupboard before the device");
            Assert.True(building.Authorise(friend.EntityId.Value, claim!).Ok, "friend has building privilege");
            Deployable device = zone.Players().SpawnDeployable(server.Content.Deployable("auto-turret")!,
                zone.GroundAt(0, -4), 0, owner.EntityId);
            Assert.True(zone.DeviceAuthorization().IsFriendly(device, friend.EntityId), "default protection comes from live cupboard");
            return (device.Id, claim!.Id, device.Position);
        });
        rig.Teleport(owner.Name, at, south: 1.8f);
        rig.Teleport(friend.Name, at, east: 1, south: 1.8f);
        RemoteThing View(GameClient client) => client.Survival().ThingOf(id)!;
        Until(rig, () => owner.Survival().ThingOf(id) is { } a && friend.Survival().ThingOf(id) is { } b &&
            a.DeviceAccess.Friendly && b.DeviceAccess.Friendly && a.DeviceAccess.CanManage && b.DeviceAccess.CanManage,
            "separate protected/manager facts for both viewers");
        rig.Run(.15, (_, _) => default);
        var answers = new Dictionary<(string, uint), IntentAnswer>();
        owner.Local.IntentAnswered += answer => answers[(owner.Name, answer.Seq)] = answer;
        friend.Local.IntentAnswered += answer => answers[(friend.Name, answer.Seq)] = answer;
        void Ask(GameClient player, string verb, OutcomeCode expected = OutcomeCode.Done)
        {
            uint seq = player.Local.Interact(new TargetRef(TargetFamily.Thing, id.Value), verb);
            Until(rig, () => answers.ContainsKey((player.Name, seq)), verb);
            Assert.Equal(expected, answers[(player.Name, seq)].Code, verb + " authoritative result");
        }
        Ask(friend, "defense-deauthorise");
        Until(rig, () => !View(friend).DeviceAccess.Friendly && View(owner).DeviceAccess.Friendly,
            "self-clear overrides inherited protection only for its actor");
        Assert.True(View(friend).DeviceAccess.CanManage, "clearing friendship does not remove cupboard management");
        Ask(owner, "defense-clear");
        Until(rig, () => !View(owner).DeviceAccess.Friendly && !View(friend).DeviceAccess.Friendly &&
            !View(owner).DeviceAccess.FollowsCupboard && !View(friend).DeviceAccess.FollowsCupboard,
            "clear-all persists manual-only policy on both clients");
        Ask(owner, "defense-authorise");
        Until(rig, () => View(owner).DeviceAccess.Friendly && !View(friend).DeviceAccess.Friendly,
            "self-add after clear-all restores only the actor");
        Ask(owner, "defense-off");
        Until(rig, () => !View(owner).DeviceAccess.Enabled && !View(friend).DeviceAccess.Enabled,
            "one intentional disabled state reaches both clients");
        Ask(owner, "defense-on");
        Until(rig, () => View(owner).DeviceAccess.Enabled && View(friend).DeviceAccess.Enabled, "enable reaches both clients");
        rig.Server.Inspect(server =>
        {
            BuildingWorld building = server.Zone.Building().World;
            building.Deauthorise(friend.EntityId.Value, building.ClaimOf(cupboard)!);
            return true;
        });
        Until(rig, () => !View(friend).DeviceAccess.CanManage, "live TC revocation updates a connected viewer without device edits");
        Ask(friend, "defense-authorise", OutcomeCode.NotAuthorised);
        Assert.True(!View(friend).DeviceAccess.Friendly, "a refused self-authorize cannot grant immunity");
    }

    [Test]
    public static void UtilityCableRoutesReplicateAsDeltasAndSurviveServerRestart()
    {
        using var file = new TempDatabase();
        EntityId sourceId, targetId;
        ulong cableId;
        Vector3 routePoint, inputPoint;
        ushort inputPort;
        using (var rig = new LoopbackRig(file.Path))
        {
            GameClient owner = rig.Join("CableOwner"), observer = rig.Join("CableObserver");
            rig.WaitUntilPlaying();
            ushort outputPort;
            Vector3 outputPoint;
            (sourceId, targetId, routePoint, outputPort, inputPort, outputPoint, inputPoint) = rig.Server.Inspect(server =>
            {
                Zone zone = server.Zone;
                Deployable source = zone.Players().SpawnDeployable(server.Content.Deployable("windmill")!,
                    zone.GroundAt(-5, -5), 0, owner.EntityId);
                Deployable target = zone.Players().SpawnDeployable(server.Content.Deployable("auto-turret")!,
                    zone.GroundAt(5, -5), 0, owner.EntityId);
                Vector3 point = zone.GroundAt(0, 25);
                Assert.True(zone.DeviceAuthorization().ClearAll(target, owner.EntityId).Ok, "explicitly clear the unclaimed owner grant");
                PowerPort output = source.Def.Power!.Output!, input = target.Def.Power!.Input!;
                return (source.Id, target.Id, point, output.Id, input.Id, PowerPorts.Position(source, output), PowerPorts.Position(target, input));
            });
            var wireAnswers = new Dictionary<uint, IntentAnswer>();
            owner.Local.IntentAnswered += answer => wireAnswers[answer.Seq] = answer;
            void Wire(WireOperation operation, TargetRef target, ushort port, Vector3 point, OutcomeCode expected = OutcomeCode.Done)
            {
                uint seq = owner.Local.Wire(operation, target, port, point);
                Until(rig, () => wireAnswers.ContainsKey(seq), "Wire Tool " + operation);
                Assert.Equal(expected, wireAnswers[seq].Code, "Wire Tool " + operation);
            }
            Wire(WireOperation.Begin, new TargetRef(TargetFamily.Thing, sourceId.Value), outputPort, outputPoint, OutcomeCode.NoTool);
            rig.Server.Inspect(server =>
            {
                Assert.True(server.Zone.Players().TryFindPlayer(owner.EntityId, out _, out var player), "find wire user");
                player.Inventory.Belt.Set(5, ItemStack.Of(server.Content.ItemByKey("wire-tool"), 1));
                return true;
            });
            Until(rig, () => owner.Local.Inventory.Belt[5].Item == owner.Content.ItemByKey("wire-tool").Id, "Wire Tool inventory");
            owner.Local.HeldSlot = 5;
            rig.Teleport(owner.Name, outputPoint, east: -1.3f, south: 1.3f);
            rig.Run(.3, (_, _) => default);
            WaitForWireTool(rig, owner);
            Wire(WireOperation.Begin, new TargetRef(TargetFamily.Thing, sourceId.Value), outputPort, outputPoint);
            rig.Teleport(owner.Name, routePoint, south: 1);
            rig.Run(.3, (_, _) => default);
            Wire(WireOperation.Anchor, TargetRef.Ground, 0, routePoint);
            rig.Teleport(owner.Name, inputPoint, east: -1.3f, south: 1.3f);
            rig.Run(.3, (_, _) => default);
            Wire(WireOperation.Complete, new TargetRef(TargetFamily.Thing, targetId.Value), inputPort, inputPoint);
            cableId = rig.Server.Inspect(server => ((Deployable)server.Zone.Things().StaticOf(targetId)!).PowerCable!.Id);
            RemoteThing View(GameClient client) => client.Survival().ThingOf(targetId)!;
            Until(rig, () => owner.Survival().ThingOf(targetId)?.PowerCable?.CableId == cableId &&
                observer.Survival().ThingOf(targetId)?.PowerCable?.CableId == cableId, "same cable identity on both clients");
            foreach (GameClient client in new[] { owner, observer })
            {
                PowerCableNet cable = View(client).PowerCable!.Value;
                Assert.Equal(sourceId.Value, cable.OutputEntity, "source endpoint identity");
                Assert.Equal(targetId.Value, cable.InputEntity, "destination endpoint identity");
                Assert.True(Vector3.Distance(new Vector3(cable.OutputX, cable.OutputY, cable.OutputZ), outputPoint) < .001f,
                    "physical output world position is included even before the source streams in");
                Assert.True(cable.Anchors.Length == 1 && cable.Anchors[0].Support.Family == TargetFamily.Ground &&
                    Vector3.Distance(new Vector3(cable.Anchors[0].X, cable.Anchors[0].Y, cable.Anchors[0].Z), routePoint) < .05f,
                    "support-local route data crosses the actual network");
                Assert.True(Vector3.Distance(new Vector3(cable.Anchors[0].WorldX, cable.Anchors[0].WorldY, cable.Anchors[0].WorldZ), routePoint) < .05f,
                    "resolved static anchor fallback crosses the actual network");
                Assert.True(!View(client).DeviceAccess.Friendly && !View(client).DeviceAccess.FollowsCupboard,
                    "manual-only state does not become owner immunity in snapshots");
            }
            PowerCableNet[] retained = View(owner).Utility.Cables;
            rig.Server.Inspect(server =>
            {
                Assert.True(server.Zone.Power().SetEnabled(owner.EntityId, (Deployable)server.Zone.Things().StaticOf(sourceId)!, false).Ok,
                    "disable source through shared power controls");
                return true;
            });
            Until(rig, () => !View(owner).Utility.Powered && !View(observer).Utility.Powered, "source loss reaches both clients");
            Assert.True(ReferenceEquals(retained, View(owner).Utility.Cables),
                "unchanged route is retained across power updates instead of being resent");
            rig.Server.SaveAll().Wait();
        }
        using (var rig = new LoopbackRig(file.Path))
        {
            rig.Server.Inspect(server =>
            {
                Deployable target = (Deployable)server.Zone.Things().StaticOf(targetId)!;
                Assert.True(target.PowerCable is { } cable && cable.Id == cableId && Vector3.Distance(cable.Anchors[0].Position, routePoint) < .05f,
                    "explicit ports and route survive a genuine database/server restart");
                Assert.True(target.Authorization.Initialized && target.Authorization.Mode == DeviceAuthorizationMode.ManualOnly &&
                    target.Authorization.Grants.Count == 0 && target.Authorization.Exclusions.Count == 0,
                    "clear-all survives restore without seeding owner immunity");
                return true;
            });
            GameClient owner = rig.Join("CableOwner"), observer = rig.Join("CableObserver");
            rig.WaitUntilPlaying();
            Until(rig, () => owner.Survival().ThingOf(targetId)?.PowerCable?.CableId == cableId &&
                observer.Survival().ThingOf(targetId)?.PowerCable?.CableId == cableId, "reconnect baselines include the route");
            owner.Local.HeldSlot = 5;
            rig.Teleport(owner.Name, inputPoint, east: -1.3f, south: 1.3f);
            rig.Run(.3, (_, _) => default);
            WaitForWireTool(rig, owner);
            var answers = new Dictionary<uint, IntentAnswer>();
            owner.Local.IntentAnswered += answer => answers[answer.Seq] = answer;
            uint disconnect = owner.Local.Wire(WireOperation.Disconnect, new TargetRef(TargetFamily.Thing, targetId.Value), inputPort, inputPoint);
            Until(rig, () => answers.ContainsKey(disconnect), "Wire Tool disconnect after restart");
            Assert.Equal(OutcomeCode.Done, answers[disconnect].Code, "owner retains management after clearing friendship");
            Until(rig, () => owner.Survival().ThingOf(targetId)!.PowerCable == null &&
                observer.Survival().ThingOf(targetId)!.PowerCable == null, "explicit empty delta removes cable on both clients");
        }
    }
}

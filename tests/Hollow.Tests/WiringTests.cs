using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Capabilities;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Electricity;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Tests;

/// <summary>Untrusted held-tool intents through the authoritative transaction and actual physics/target surfaces.</summary>
public static class WiringTests
{
    private static Entity User(GameRig rig, string name = "WireUser", float x = 0, float z = 2)
    {
        Entity user = rig.Add(name, x, z);
        Equip(rig, user);
        return user;
    }

    private static void Equip(GameRig rig, Entity user)
    {
        ItemDef tool = rig.C.ItemByKey("wire-tool");
        rig.P(user).Inventory.Belt.Set(0, ItemStack.Of(tool, 1));
        rig.P(user).HeldSlot = 0;
        rig.P(user).HeldItem = tool.Id;
        rig.Ch(user).Actions = new ActionInput { HeldSlot = 0 };
    }

    private static Deployable Node(GameRig rig, EntityId owner, bool generator, float x, float z = 0, Vector3? socket = null)
    {
        var input = new PowerPort { Id = 1, Key = "input", Direction = PowerPortDirection.Input,
            Position = socket ?? new Vector3(0, .6f, 0) };
        var output = new PowerPort { Id = generator ? (ushort)1 : (ushort)2, Key = "output", Direction = PowerPortDirection.Output,
            Position = socket ?? new Vector3(0, .6f, 0) };
        var power = new PowerStats
        {
            Transfer = generator ? PowerTransfer.Generator : PowerTransfer.Consumer,
            Generation = generator ? 100 : 0, Demand = generator ? 0 : 10,
            Ports = generator ? new[] { output } : new[] { input, output },
        };
        return rig.Zone.Players().SpawnDeployable(new DeployableDef
        {
            Key = "wiring-test-node", Item = rig.C.ItemByKey("small-box"), Health = 100,
            HalfExtents = new Vector3(.25f), Power = power,
        }, rig.Zone.GroundAt(x, z), 0, owner);
    }

    private static Deployable Surface(GameRig rig, EntityId owner, float x, float z, Vector3? size = null) =>
        rig.Zone.Players().SpawnDeployable(new DeployableDef
        {
            Key = "wiring-test-support", Item = rig.C.ItemByKey("small-box"), Health = 100,
            HalfExtents = size ?? new Vector3(.5f),
        }, rig.Zone.GroundAt(x, z), 0, owner);

    private static Vector3 Port(Deployable node, ushort port = 1) => PowerPorts.Position(node, node.Def.Power!.Port(port)!);

    private static void AtPort(GameRig rig, Entity user, Deployable node, ushort port = 1)
    {
        Vector3 at = Port(node, port), up = node.Up;
        Vector3 side = Vector3.Transform(Vector3.UnitZ, Geometry.Upright(up, node.Yaw));
        rig.Zone.Teleport(user, at + side * 2 - up * .6f);
        rig.Clear(rig.Zone.EyeOf(rig.Ch(user).State), at, 2);
    }

    private static Outcome Act(GameRig rig, Entity user, WireOperation op, Deployable node, ushort port = 1, uint revision = 0) =>
        rig.Zone.Wiring().Execute(user, op, node.Ref, port, Port(node, port), revision);

    private static AimHit SurfaceHit(GameRig rig, Entity user, Deployable support)
    {
        Vector3 up = support.Up, side = Vector3.Transform(Vector3.UnitZ, Geometry.Upright(up, support.Yaw));
        rig.Zone.Teleport(user, support.Position + side * 2);
        Vector3 eye = rig.Zone.EyeOf(rig.Ch(user).State), toward = support.Position + up * .2f;
        rig.Clear(eye, toward, 2);
        AimHit hit = Targeting.Aim(rig.Zone.Targets().TargetViews,
            new AimRay(eye, Vector3.Normalize(toward - eye), 4, rig.P(user).Id.Value));
        Assert.True(hit.Hit && hit.Target.Whole == support.Ref, "fixture aims at the actual visible support surface");
        return hit;
    }

    private static Vector3 Anchor(GameRig rig, Entity user, Deployable support)
    {
        AimHit hit = SurfaceHit(rig, user, support);
        Assert.True(rig.Zone.Wiring().Execute(user, WireOperation.Anchor, hit.Target, 0, hit.Point).Ok,
            "visible support accepts a decorative anchor");
        return hit.Point;
    }

    private static ClaimDevice Claim(GameRig rig, EntityId owner, params EntityId[] members)
    {
        Vector3 point = rig.Zone.GroundAt(0, -5), up = rig.Zone.Frame.UpAt(point);
        var building = rig.Zone.Building().World;
        Piece floor = building.AddFirst(BuildingFrame.At(new Vector3D(point.X, point.Y, point.Z),
            new Vector3D(up.X, up.Y, up.Z), 0), rig.C.Building.Piece("foundation")!, 0, owner.Value, 0);
        Assert.True(building.PlaceClaim(owner.Value, floor, new Vector3D(.4, 0, .4), 0, out ClaimDevice? claim).Ok, "fixture claim placed");
        foreach (EntityId member in members) building.Authorise(member.Value, claim!);
        return claim!;
    }

    [Test]
    public static void WiringToolRequiresHeldCapabilityAndRejectsForgedOrRemotePorts()
    {
        using var rig = new GameRig();
        Entity user = User(rig);
        Deployable generator = Node(rig, rig.P(user).Id, true, 0);
        ItemDef tool = rig.C.ItemByKey("wire-tool");
        Assert.True(tool.Held == HeldUse.Wiring && tool.Wiring is { Reach: 4, MaxAnchors: 32 },
            "tool behavior is supplied by shared item capability data");
        AtPort(rig, user, generator);
        rig.P(user).HeldSlot = ActionInput.NoSlot;
        Assert.Equal(OutcomeCode.NoTool, Act(rig, user, WireOperation.Begin, generator).Code, "inventory possession is insufficient without holding tool");
        Equip(rig, user);
        Assert.Equal(OutcomeCode.Invalid, rig.Zone.Wiring().Execute(user, WireOperation.Begin, generator.Ref, 1,
            new Vector3(float.NaN, 0, 0)).Code, "nonfinite client geometry rejected");
        Assert.Equal(OutcomeCode.NotFound, rig.Zone.Wiring().Execute(user, WireOperation.Begin,
            new TargetRef(TargetFamily.Character, generator.Id.Value), 1, Port(generator)).Code, "wrong target family cannot reuse node ID");
        Assert.Equal(OutcomeCode.NotFound, rig.Zone.Wiring().Execute(user, WireOperation.Begin, generator.Ref,
            65535, Port(generator)).Code, "unknown socket rejected");
        Assert.Equal(OutcomeCode.Stale, Act(rig, user, WireOperation.Begin, generator, revision: generator.Revision + 1).Code,
            "stale endpoint revision rejected");
        rig.Zone.Teleport(user, rig.Zone.GroundAt(10, 0));
        Assert.True(!rig.Zone.Wiring().Execute(user, WireOperation.Begin, generator.Ref,
            1, rig.Zone.EyeOf(rig.Ch(user).State)).Ok, "forged near click cannot hide authoritative port distance");
        Assert.Equal(OutcomeCode.OutOfReach, Act(rig, user, WireOperation.Begin, generator).Code,
            "even a truthful port point requires physical actor reach");
        Assert.Equal(0, rig.Zone.Wiring().DraftCount, "failed requests create no draft");
    }

    [Test]
    public static void WiringToolRejectsOccludedPortAndUnauthorizedEndpoint()
    {
        using var rig = new GameRig();
        Entity user = User(rig);
        Deployable generator = Node(rig, rig.P(user).Id, true, 0);
        AtPort(rig, user, generator);
        Vector3 eye = rig.Zone.EyeOf(rig.Ch(user).State), midpoint = (eye + Port(generator)) * .5f;
        Deployable wall = Surface(rig, rig.P(user).Id, 0, 1, new Vector3(1, 2, .2f));
        // Put the opaque collider directly on the established sight segment, independent of planetary axes.
        rig.Zone.Things().RemoveStatic(wall);
        wall = rig.Zone.Players().SpawnDeployable(wall.Def, midpoint, 0, rig.P(user).Id);
        Vector3 delta = Port(generator) - eye;
        AimHit obstruction = Targeting.Aim(rig.Zone.Targets().TargetViews,
            new AimRay(eye, Vector3.Normalize(delta), delta.Length(), rig.P(user).Id.Value) { AttackRadius = 0 });
        Assert.True(obstruction.Hit && obstruction.Target.Whole == wall.Ref, "fixture wall actually intercepts the socket sight ray");
        Assert.Equal(OutcomeCode.OutOfSight, Act(rig, user, WireOperation.Begin, generator).Code, "wall blocks a physically nearby port");
        rig.Zone.Things().RemoveStatic(wall);
        generator.Owner = new EntityId(987654);
        Assert.Equal(OutcomeCode.NotOwner, Act(rig, user, WireOperation.Begin, generator).Code, "stranger cannot edit unclaimed device");
        Assert.Equal(0, rig.Zone.Wiring().DraftCount, "neither refusal starts a draft");
    }

    [Test]
    public static void WiringDecorativeRouteMayExceedTwentyMetresWithNearbyEndpoints()
    {
        using var rig = new GameRig();
        Entity user = User(rig);
        EntityId owner = rig.P(user).Id;
        Deployable generator = Node(rig, owner, true, 0), consumer = Node(rig, owner, false, 6);
        Deployable first = Surface(rig, owner, 0, 15), second = Surface(rig, owner, 6, 15);
        AtPort(rig, user, generator);
        Assert.True(Act(rig, user, WireOperation.Begin, generator).Ok, "output starts transaction");
        Vector3 a = Anchor(rig, user, first), b = Anchor(rig, user, second);
        float routed = Vector3.Distance(Port(generator), a) + Vector3.Distance(a, b) + Vector3.Distance(b, Port(consumer));
        Assert.True(routed > 20 && Vector3.Distance(Port(generator), Port(consumer)) < 20,
            "fixture distinguishes decorative route length from electrical endpoint span");
        AtPort(rig, user, consumer);
        Assert.True(Act(rig, user, WireOperation.Complete, consumer).Ok, "long visual route commits within endpoint range");
        Assert.True(consumer.PowerCable is { Anchors.Length: 2 } && consumer.Powered && rig.Zone.Power().Graph.CableCount == 1,
            "one electrical edge stores both decorative anchors and powers the load");
        Assert.Equal(0, rig.Zone.Wiring().DraftCount, "successful commit ends the transaction");
        rig.Zone.Things().RemoveStatic(first);
        Assert.True(consumer.PowerCable is { Anchors.Length: 1 } && consumer.Powered,
            "removing decorative support removes only its anchor and keeps electrical supply");
        rig.Zone.Things().RemoveStatic(generator);
        Assert.True(consumer.PowerCable == null && !consumer.Powered && rig.Zone.Power().Graph.CableCount == 0,
            "removing an endpoint disconnects the circuit immediately");
    }

    [Test]
    public static void WiringFailedCommitKeepsDraftAndRechecksBothEndpointRights()
    {
        using var rig = new GameRig();
        Entity user = User(rig);
        EntityId owner = rig.P(user).Id;
        Deployable generator = Node(rig, owner, true, 0), far = Node(rig, owner, false, 25), near = Node(rig, owner, false, 6);
        AtPort(rig, user, generator);
        Assert.True(Act(rig, user, WireOperation.Begin, generator).Ok, "begin");
        AtPort(rig, user, far);
        Assert.Equal(OutcomeCode.OutOfReach, Act(rig, user, WireOperation.Complete, far).Code, "endpoint span over twenty metres rejected");
        Assert.True(rig.Zone.Wiring().DraftCount == 1 && rig.Zone.Power().Graph.CableCount == 0, "rejected edit changes neither draft nor graph");
        generator.Owner = new EntityId(987654);
        AtPort(rig, user, near);
        Assert.Equal(OutcomeCode.NotOwner, Act(rig, user, WireOperation.Complete, near).Code, "source permission is rechecked at commit");
        Assert.Equal(1, rig.Zone.Wiring().DraftCount, "permission refusal retains valid draft for retry");
        generator.Owner = owner;
        Assert.True(Act(rig, user, WireOperation.Complete, near).Ok && near.Powered, "same draft can complete after recoverable failure");
    }

    [Test]
    public static void WiringConcurrentDraftsCannotDoubleOccupyOutput()
    {
        using var rig = new GameRig();
        Entity first = User(rig, "First"), second = User(rig, "Second", 3, 2);
        EntityId owner = rig.P(first).Id;
        Claim(rig, owner, rig.P(second).Id);
        Deployable generator = Node(rig, owner, true, 0), a = Node(rig, owner, false, 5), b = Node(rig, owner, false, 10);
        AtPort(rig, first, generator);
        Assert.True(Act(rig, first, WireOperation.Begin, generator).Ok, "first begins");
        rig.Zone.Teleport(first, rig.Zone.GroundAt(-5, 5));
        AtPort(rig, second, generator);
        Assert.True(Act(rig, second, WireOperation.Begin, generator).Ok, "second begins before first commits");
        rig.Zone.Teleport(second, rig.Zone.GroundAt(-5, 5));
        AtPort(rig, first, a);
        Assert.True(Act(rig, first, WireOperation.Complete, a).Ok, "first transaction wins output");
        rig.Zone.Teleport(first, rig.Zone.GroundAt(-5, 5));
        AtPort(rig, second, b);
        Assert.Equal(OutcomeCode.NoSpace, Act(rig, second, WireOperation.Complete, b).Code, "second transaction cannot steal occupied output");
        Assert.True(a.PowerCable != null && b.PowerCable == null && rig.Zone.Power().Graph.CableCount == 1 && rig.Zone.Wiring().DraftCount == 1,
            "loser keeps draft and winner's wire survives unchanged");
    }

    [Test]
    public static void WiringCancellationToolSwitchAndDeathNeverPublishDrafts()
    {
        using var rig = new GameRig();
        Entity user = User(rig);
        Deployable generator = Node(rig, rig.P(user).Id, true, 0);
        AtPort(rig, user, generator);
        Assert.True(Act(rig, user, WireOperation.Begin, generator).Ok, "begin before cancellation");
        rig.P(user).HeldSlot = ActionInput.NoSlot;
        Assert.True(rig.Zone.Wiring().Execute(user, WireOperation.Cancel, TargetRef.None, 0, default).Ok &&
            rig.Zone.Wiring().DraftCount == 0, "cancel remains available after tool is put away");
        Equip(rig, user);
        Assert.True(Act(rig, user, WireOperation.Begin, generator).Ok, "begin before tool change");
        rig.P(user).Inventory.Belt.Set(0, ItemStack.Of(rig.C.ItemByKey("rock"), 1));
        rig.Step();
        Assert.Equal(0, rig.Zone.Wiring().DraftCount, "switching held item cancels during authoritative step");
        Equip(rig, user);
        AtPort(rig, user, generator);
        Assert.True(Act(rig, user, WireOperation.Begin, generator).Ok, "begin before death");
        rig.P(user).Dead = true;
        Assert.Equal(OutcomeCode.NotAllowed, Act(rig, user, WireOperation.Complete, generator).Code, "dead actor cannot complete");
        Assert.True(rig.Zone.Wiring().DraftCount == 0 && rig.Zone.Power().Graph.CableCount == 0, "dead actor draft is discarded without a wire");
    }

    [Test]
    public static void WiringSpanAndToolReachUseSocketPositionsInsteadOfModelOrigins()
    {
        using var rig = new GameRig();
        Entity user = User(rig);
        EntityId owner = rig.P(user).Id;
        Deployable high = Node(rig, owner, true, 0, socket: new Vector3(0, 8, 0));
        rig.Zone.Teleport(user, rig.Zone.GroundAt(0, 1));
        rig.Clear(rig.Zone.EyeOf(rig.Ch(user).State), Port(high), 2);
        Assert.Equal(OutcomeCode.OutOfReach, Act(rig, user, WireOperation.Begin, high).Code,
            "standing beside model origin does not reach a high output socket");
        AtPort(rig, user, high);
        Assert.True(Act(rig, user, WireOperation.Begin, high).Ok, "moving close to actual socket allows begin");
        Deployable low = Node(rig, owner, false, 19);
        Assert.True(Vector3.Distance(high.Position, low.Position) < 20 && Vector3.Distance(Port(high), Port(low)) > 20,
            "origins fit while sockets exceed cable span");
        AtPort(rig, user, low);
        Assert.Equal(OutcomeCode.OutOfReach, Act(rig, user, WireOperation.Complete, low).Code, "authoritative endpoint span rejects high-to-low shortcut");
    }

    [Test]
    public static void WiringAnchorsRequireRealOwnedSurfacesAndRevalidateOnCommit()
    {
        using var rig = new GameRig();
        Entity user = User(rig);
        EntityId owner = rig.P(user).Id;
        Deployable generator = Node(rig, owner, true, 0), consumer = Node(rig, owner, false, 8);
        Deployable support = Surface(rig, owner, 4, 4);
        AtPort(rig, user, generator);
        Assert.True(Act(rig, user, WireOperation.Begin, generator).Ok, "begin");
        AimHit hit = SurfaceHit(rig, user, support);
        Assert.Equal(OutcomeCode.OutOfSight, rig.Zone.Wiring().Execute(user, WireOperation.Anchor, consumer.Ref, 0, hit.Point).Code,
            "surface ID cannot be forged for a different ray hit");
        Assert.Equal(OutcomeCode.Invalid, rig.Zone.Wiring().Execute(user, WireOperation.Anchor, support.Ref, 0,
            new Vector3(float.PositiveInfinity, 0, 0)).Code, "anchor coordinates must be finite");
        support.Owner = new EntityId(987654);
        Assert.Equal(OutcomeCode.NotOwner, rig.Zone.Wiring().Execute(user, WireOperation.Anchor, support.Ref, 0, hit.Point).Code,
            "visual cable routing cannot edit another player's unclaimed support");
        support.Owner = owner;
        Assert.True(rig.Zone.Wiring().Execute(user, WireOperation.Anchor, support.Ref, 0, hit.Point).Ok, "valid anchor accepted");
        support.Owner = new EntityId(987654);
        AtPort(rig, user, consumer);
        Assert.Equal(OutcomeCode.NotOwner, Act(rig, user, WireOperation.Complete, consumer).Code,
            "permission changed after anchoring is checked again before commit");
        Assert.True(rig.Zone.Power().Graph.CableCount == 0 && rig.Zone.Wiring().DraftCount == 1, "refusal remains atomic");
        support.Owner = owner;
        rig.Zone.Things().RemoveStatic(support);
        Assert.Equal(OutcomeCode.Stale, Act(rig, user, WireOperation.Complete, consumer).Code,
            "removed draft support cannot become a dangling saved anchor");
    }

    [Test]
    public static void WiringDisconnectRequiresCurrentRightsAtBothEnds()
    {
        using var rig = new GameRig();
        Entity user = User(rig);
        EntityId owner = rig.P(user).Id;
        Deployable generator = Node(rig, owner, true, 0), consumer = Node(rig, owner, false, 6);
        AtPort(rig, user, generator);
        Assert.True(Act(rig, user, WireOperation.Begin, generator).Ok, "begin");
        AtPort(rig, user, consumer);
        Assert.True(Act(rig, user, WireOperation.Complete, consumer).Ok, "complete");
        generator.Owner = new EntityId(987654);
        Assert.Equal(OutcomeCode.NotOwner, Act(rig, user, WireOperation.Disconnect, consumer).Code,
            "editing one endpoint does not grant rights over the other endpoint");
        Assert.True(consumer.PowerCable != null && consumer.Powered, "refused disconnect keeps live connection");
        generator.Owner = owner;
        Assert.True(Act(rig, user, WireOperation.Disconnect, consumer).Ok, "current rights at both endpoints permit removal");
        Assert.True(consumer.PowerCable == null && !consumer.Powered && rig.Zone.Power().Graph.CableCount == 0,
            "disconnect releases both sockets and removes delivered electricity");
    }
}

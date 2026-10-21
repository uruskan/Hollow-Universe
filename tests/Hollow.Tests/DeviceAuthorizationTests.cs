using System.Numerics;
using Hollow.Core;
using Hollow.Simulation;
using Hollow.Simulation.Authorization;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Interaction;
using Hollow.Simulation.Ships;
using Hollow.Simulation.World;

namespace Hollow.Tests;

public static class DeviceAuthorizationTests
{
    private static Deployable Device(GameRig rig, EntityId owner, float x = 5, float z = 0) =>
        rig.Zone.Players().SpawnDeployable(new DeployableDef
        {
            Key = "authorization-test", Item = rig.C.ItemByKey("small-box"), Health = 100, Slots = 4,
            HalfExtents = new Vector3(.3f), Defense = new DefenseStats { Targets = DefenseTargets.Players, WeaponSlot = 0 },
            Power = new PowerStats { Demand = 10 },
        }, rig.Zone.GroundAt(x, z), 0, owner);

    private static ClaimDevice Claim(GameRig rig, EntityId owner, float x = 0, float z = 0)
    {
        Vector3 point = rig.Zone.GroundAt(x, z), up = rig.Zone.Frame.UpAt(point);
        Piece floor = rig.Zone.Building().World.AddFirst(BuildingFrame.At(
            new Vector3D(point.X, point.Y, point.Z), new Vector3D(up.X, up.Y, up.Z), 0),
            rig.C.Building.Piece("foundation")!, 0, owner.Value, 0);
        Assert.True(rig.Zone.Building().World.PlaceClaim(owner.Value, floor, new Vector3D(.4, 0, .4), 0,
            out ClaimDevice? claim).Ok, "test cupboard placed");
        return claim!;
    }

    [Test]
    public static void DeviceAuthorizationInheritedMembershipIsLiveAndClearOverridesIt()
    {
        using var rig = new GameRig();
        EntityId owner = rig.P(rig.Add("Owner", 0, 8)).Id, guest = rig.P(rig.Add("Guest", 0, 9)).Id;
        ClaimDevice cupboard = Claim(rig, owner);
        var building = rig.Zone.Building().World;
        building.Authorise(guest.Value, cupboard);
        Deployable device = Device(rig, owner);
        DeviceAuthorizationSystem auth = rig.Zone.DeviceAuthorization();
        Assert.True(device.Authorization.Initialized && device.Authorization.Grants.Count == 0,
            "claimed placement inherits without secretly retaining owner grant");
        Assert.True(auth.IsFriendly(device, owner) && auth.IsFriendly(device, guest), "cupboard membership protects both");
        Assert.True(auth.ClearSelf(device, guest).Ok && !auth.IsFriendly(device, guest), "self-clear excludes inherited member");
        Assert.True(auth.CanManage(device, guest), "self-clear retains independent building administration");
        building.Deauthorise(guest.Value, cupboard);
        building.Authorise(guest.Value, cupboard);
        Assert.True(!auth.IsFriendly(device, guest), "later cupboard edits do not erase exclusion");
        Assert.True(auth.AuthorizeSelf(device, guest).Ok && auth.IsFriendly(device, guest), "self-authorise replaces exclusion with grant");
        building.Deauthorise(owner.Value, cupboard);
        Assert.True(!auth.IsFriendly(device, owner) && !auth.CanManage(device, owner), "creator has no owner bypass inside claimed base");
        Assert.True(auth.IsFriendly(device, guest), "explicit guest grant remains distinct from inherited membership");
    }

    [Test]
    public static void DeviceAuthorizationClearAllPersistsManualModeAcrossRebinding()
    {
        using var rig = new GameRig();
        EntityId owner = rig.P(rig.Add("Owner", 0, 8)).Id, guest = rig.P(rig.Add("Guest", 0, 9)).Id;
        ClaimDevice cupboard = Claim(rig, owner);
        Deployable device = Device(rig, owner);
        DeviceAuthorizationSystem auth = rig.Zone.DeviceAuthorization();
        Assert.True(auth.ClearAll(device, owner).Ok, "manager clears all");
        uint revision = device.Authorization.Revision;
        Assert.True(device.Authorization.Mode == DeviceAuthorizationMode.ManualOnly && device.Authorization.Grants.Count == 0 &&
            device.Authorization.Exclusions.Count == 0 && !auth.IsFriendly(device, owner), "clear all atomically suspends inheritance");
        Assert.True(auth.CanManage(device, owner), "clear all does not lock out control");
        rig.Zone.Building().World.Authorise(guest.Value, cupboard);
        auth.Unregister(device);
        auth.Register(device);
        Assert.True(device.Authorization.Revision == revision && !auth.IsFriendly(device, owner) && !auth.IsFriendly(device, guest),
            "re-registering initialized state and new cupboard members do not repopulate authorization");
        Assert.True(auth.AuthorizeSelf(device, owner).Ok && auth.IsFriendly(device, owner) && !auth.IsFriendly(device, guest) &&
            device.Authorization.Mode == DeviceAuthorizationMode.ManualOnly, "authorise self never silently restores inheritance");
        Assert.True(auth.ClearAll(device, owner).Ok && auth.ClearAll(device, owner).Ok, "clear all is repeatable");
    }

    [Test]
    public static void DeviceAuthorizationUnclaimedGrantAndControlRemainIndependent()
    {
        using var rig = new GameRig();
        EntityId owner = rig.P(rig.Add("Owner", 0, 8)).Id, friend = rig.P(rig.Add("Friend", 0, 9)).Id;
        rig.Zone.World().Teams.Restore(42, owner, new[] { owner, friend });
        Deployable device = Device(rig, owner);
        DeviceAuthorizationSystem auth = rig.Zone.DeviceAuthorization();
        Assert.True(device.Authorization.Grants.Contains(owner.Value) && auth.IsFriendly(device, owner), "unclaimed placer gets explicit grant");
        Assert.True(!auth.IsFriendly(device, friend) && !auth.CanManage(device, friend), "team membership grants neither immunity nor control");
        Assert.True(auth.AuthorizeSelf(device, friend) == OutcomeCode.NotAuthorised &&
            auth.ClearAll(device, friend) == OutcomeCode.NotAuthorised, "stranger cannot acquire immunity or clear protection");
        Assert.True(auth.ClearSelf(device, owner).Ok && !auth.IsFriendly(device, owner) && auth.CanManage(device, owner),
            "placer can revoke own immunity and retain recovery controls");
        Claim(rig, friend);
        Assert.True(!auth.CanManage(device, owner) && auth.CanManage(device, friend), "new covering claim overrides historical ownership");
        Assert.True(!auth.IsFriendly(device, owner), "self exclusion survives covering claim changes");
    }

    [Test]
    public static void DeviceAuthorizationOverlappingClaimsRequireEveryMembership()
    {
        using var rig = new GameRig();
        EntityId owner = rig.P(rig.Add("Owner", 0, 8)).Id, other = rig.P(rig.Add("Other", 0, 9)).Id;
        ClaimDevice first = Claim(rig, owner, -10), second = Claim(rig, other, 10);
        Deployable device = Device(rig, owner, 0);
        DeviceAuthorizationSystem auth = rig.Zone.DeviceAuthorization();
        Assert.True(!auth.IsFriendly(device, owner) && !auth.CanManage(device, owner), "one blocked overlapping claim wins");
        rig.Zone.Building().World.Authorise(owner.Value, second);
        Assert.True(auth.IsFriendly(device, owner) && auth.CanManage(device, owner), "membership on all covering claims grants access");
        rig.Zone.Building().World.Deauthorise(owner.Value, first);
        Assert.True(!auth.IsFriendly(device, owner) && !auth.CanManage(device, owner), "list removal is visible immediately without a tick");
    }

    [Test]
    public static void DeviceAuthorizationPolicyDenyWinsAndSubjectsFollowPilot()
    {
        var state = new DeviceAuthorizationState { Initialized = true };
        state.Grants.Add(7);
        state.Exclusions.Add(7);
        Assert.True(!DeviceAuthorizationPolicies.IsFriendly(DeviceAuthorizationPolicies.CupboardDevice, state, 7, true),
            "explicit denial wins over local and inherited grants");
        Assert.True(!DeviceAuthorizationPolicies.IsFriendly("unknown", state, 8, true), "unknown policy grants no immunity");
        using var rig = new GameRig();
        EntityId owner = rig.P(rig.Add("Owner", 0, 8)).Id, pilot = rig.P(rig.Add("Pilot", 0, 9)).Id;
        Ship ship = rig.Zone.Ships().SpawnShip(rig.C.Ship("tin-finch")!, rig.Zone.GroundAt(30, 0), 0, owner, 0);
        Assert.Equal(owner, AuthorizationSubjects.Of(ship), "unpiloted subject is current ship owner");
        ship.Occupants[ship.Def.Helm.Index] = pilot;
        Assert.Equal(pilot, AuthorizationSubjects.Of(ship), "controlling pilot overrides ship ownership");
        ship.Occupants[ship.Def.Helm.Index] = EntityId.None;
        if (ship.Occupants.Length > 1) ship.Occupants[1] = pilot;
        Assert.Equal(owner, AuthorizationSubjects.Of(ship), "passenger is never the controlling subject");
    }

    [Test]
    public static void DeviceAuthorizationCupboardLossReplacementAndRemovalInvalidateImmediately()
    {
        using var rig = new GameRig();
        EntityId owner = rig.P(rig.Add("Owner", 0, 8)).Id, successor = rig.P(rig.Add("Successor", 0, 9)).Id;
        ClaimDevice original = Claim(rig, owner);
        Deployable device = Device(rig, owner);
        var auth = rig.Zone.DeviceAuthorization();
        var building = rig.Zone.Building().World;
        Assert.True(auth.IsFriendly(device, owner), "original live membership protects owner");
        Assert.True(building.PickUpClaim(owner.Value, original).Ok, "remove original cupboard");
        Assert.True(!auth.IsFriendly(device, owner) && auth.CanManage(device, owner),
            "claim loss immediately removes inherited protection but preserves unclaimed recovery controls");
        Assert.True(building.PlaceClaim(successor.Value, original.Support, new Vector3D(.4, 0, .4), 0,
            out ClaimDevice? replacement).Ok, "replacement cupboard placed");
        Assert.True(auth.IsFriendly(device, successor) && !auth.IsFriendly(device, owner) && !auth.CanManage(device, owner),
            "replacement binds new membership without retaining destroyed-cupboard immunity");
        Assert.True(auth.ClearAll(device, successor).Ok, "new manager selects manual mode");
        Assert.True(building.PickUpClaim(successor.Value, replacement!).Ok, "replacement removed");
        Assert.True(device.Authorization.Mode == DeviceAuthorizationMode.ManualOnly && !auth.IsFriendly(device, owner),
            "another claim transition cannot restore inheritance after clear all");
        rig.Zone.Things().RemoveStatic(device);
        Assert.True(!auth.Facts(device, owner).Present && !auth.CanManage(device, owner) && !auth.IsFriendly(device, owner),
            "removed device is absent from access registry immediately");
    }
}

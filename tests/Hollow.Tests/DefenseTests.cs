using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Ships;
using Hollow.Simulation.World;

namespace Hollow.Tests;

public static class DefenseTests
{
    private static Deployable Place(GameRig rig, DefenseStats stats, EntityId owner, float x, float z) =>
        rig.Zone.Players().SpawnDeployable(new DeployableDef
        {
            Key = "defense-regression", Item = rig.C.ItemByKey("small-box"), Health = 500, HalfExtents = new Vector3(.6f, .8f, .6f),
            Slots = 4, Defense = stats, Power = new PowerStats { Demand = 10 },
        }, rig.Zone.GroundAt(x, z), 0, owner);

    private static void Supply(GameRig rig, Deployable mount)
    {
        Deployable source = rig.Zone.Players().SpawnDeployable(new DeployableDef
        {
            Key = "defense-test-supply", Item = rig.C.ItemByKey("small-box"), Health = 100, HalfExtents = new Vector3(.2f),
            Power = new PowerStats { Generation = 100, Outputs = 4 }, Solid = false,
        }, mount.Position + mount.Up * 4, 0, mount.Owner);
        Assert.True(rig.Zone.Require<PowerSystem>().Connect(mount.Owner, source, mount).Ok, "actual electrical connection supplies mount");
    }

    [Test]
    public static void DefenseInstalledGunRequiresPowerAmmoRightsAndSightThenUsesOrdinaryDamage()
    {
        using var rig = new GameRig();
        var owner = rig.Add("Owner", 0, 5);
        var friend = rig.Add("Friend", 4, -17);
        var enemy = rig.Add("Enemy", 0, -20);
        rig.Zone.World().Teams.Restore(42, rig.P(owner).Id, new[] { rig.P(owner).Id, rig.P(friend).Id });
        Deployable mount = Place(rig, new DefenseStats { Targets = DefenseTargets.Players, WeaponSlot = 0, Range = 35 }, rig.P(owner).Id, 0, -5);
        Assert.True(!rig.Zone.DeviceAuthorization().IsFriendly(mount, rig.P(friend).Id), "team membership alone grants no device immunity");
        mount.Authorization.Grants.Add(rig.P(friend).Id.Value);
        Container store = mount.Storage!;
        ItemStack pistol = ItemStack.Of(rig.C.ItemByKey("pistol"), 1), rounds = ItemStack.Of(rig.C.ItemByKey("pistol-ammo"), 80);
        Assert.True(store.Accepts(rig.C, 0, pistol), "the gunless mount accepts a firearm in its weapon slot");
        store.Set(0, pistol);
        Assert.True(store.Accepts(rig.C, 1, rounds) && !store.Accepts(rig.C, 1, ItemStack.Of(rig.C.ItemByKey("rifle-ammo"), 1)) &&
            !store.Accepts(rig.C, 0, rounds), "ammunition must match the installed weapon and cannot replace it");
        store.Set(1, rounds);
        rig.Clear(mount.Position, rig.Zone.WorldOf(rig.Ch(enemy).State), 7);
        rig.Step(40);
        Assert.True(mount.DefenseLastFireTick < 0, "no power means no shots");
        Assert.True(rig.Zone.Defenses().SetEnabled(mount, rig.P(enemy).Id, false) == OutcomeCode.NotAuthorised, "stranger cannot disable defense");
        rig.P(enemy).Looting = mount.Id;
        Assert.True(rig.Zone.Actions().ResolveContainer(rig.P(enemy), ContainerRef.Loot) == null, "forged or stale loot session cannot steal its gun or ammo");
        store.Set(0, default);
        Supply(rig, mount);
        rig.Step(40);
        Assert.True(mount.DefenseLastFireTick < 0, "powered gunless mount cannot fire");
        store.Set(0, pistol);
        Deployable wall = rig.Zone.Players().SpawnDeployable(new DeployableDef
        {
            Key = "defense-test-wall", Item = rig.C.ItemByKey("small-box"), Health = 10000, HalfExtents = new Vector3(3, 4, 1),
        }, rig.Zone.GroundAt(0, -12), 0, rig.P(enemy).Id);
        rig.Step(140);
        Assert.True(mount.DefenseLastFireTick < 0 && rig.P(enemy).Vitals.Health == 100, "opaque obstruction prevents firing");
        rig.Zone.Things().RemoveStatic(wall);
        for (int i = 0; i < 150 && rig.P(enemy).Vitals.Health == 100; i++) rig.Step();
        Assert.True(mount.DefenseLastFireTick >= 0 && rig.P(enemy).Vitals.Health < 100, "reloaded gun consumes ammunition and hits through common damage pipeline");
        Assert.True(store[0].Loaded + store[1].Count < 80, "firing spends stored ammunition");
        Assert.True(rig.P(owner).Vitals.Health == 100 && rig.P(friend).Vitals.Health == 100, "explicitly granted players are excluded");
        Assert.True(rig.Zone.Defenses().SetEnabled(mount, rig.P(friend).Id, false) == OutcomeCode.NotAuthorised,
            "friendship does not grant device administration");
        Assert.True(rig.Zone.Defenses().SetEnabled(mount, rig.P(owner).Id, false).Ok, "unclaimed owner can disable it");
        long last = mount.DefenseLastFireTick;
        rig.Step(40);
        Assert.Equal(last, mount.DefenseLastFireTick, "disabled mount stops firing");
    }

    [Test]
    public static void DefenseSamConsumesItsRocketAgainstHostileAirborneShip()
    {
        using var rig = new GameRig();
        var owner = rig.Add("Owner", 0, 5);
        ItemDef rocket = rig.C.ItemByKey("rocket-hv");
        Deployable sam = Place(rig, new DefenseStats
        {
            Targets = DefenseTargets.Aircraft, WeaponSlot = -1, Ammo = new HashSet<ushort> { rocket.Id },
            Range = 100, ProjectileSpeed = 100, MuzzleHeight = 2, IntervalSeconds = 1,
        }, rig.P(owner).Id, 0, -5);
        sam.Storage!.Set(0, ItemStack.Of(rocket, 5));
        Supply(rig, sam);
        Vector3 ground = rig.Zone.GroundAt(0, -35);
        Vector3 aloft = ground + rig.Zone.Frame.UpAt(ground) * 18;
        Ship ship = rig.Zone.Ships().SpawnShip(rig.C.Ship("tin-finch")!, aloft, 0, rig.P(owner).Id, 0);
        rig.Clear(sam.Position, aloft, 7);
        rig.Step(12);
        Assert.True(sam.DefenseLastFireTick < 0, "friendly airborne ship is excluded");
        ship.Owner = new EntityId(987654);
        // Ownership change also clears the prior authorised crew in Ship.Owner's shared setter.
        ship.RestoreCrew(new[] { ship.Owner.Value });
        float health = ship.State.Health;
        rig.Step(35);
        Assert.True(sam.DefenseLastFireTick >= 0 && sam.Storage[0].Count < 5, "hostile airborne ship consumes actual SAM ammunition");
        Assert.True(ship.State.Health < health, "ordinary rocket impact damages its hull");
    }
}

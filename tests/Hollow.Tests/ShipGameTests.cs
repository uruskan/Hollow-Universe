using System;
using System.Linq;
using System.Numerics;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Ships;
using Hollow.Simulation.World;
using Hollow.Simulation.Targets;

namespace Hollow.Tests;

/// <summary>Ships in the survival game (GROUND.md §6.7): built at the vehicle workbench, boarded, flown, left, locked, hurt.</summary>
public static class ShipGameTests
{
    private static Entity AddPlayer(ShipRig rig, string name, Vector3 at, float yaw = 0)
    {
        Zone zone = rig.Zone;
        Vector3 ground = zone.Settle(at);
        Entity e = zone.AddPlayer(zone.NextId(), name, zone.NewCharacter(ground + zone.Frame.UpAt(ground) * 0.02f));
        Player p = zone.PlayerOf(e)!;
        p.Inventory.Clear();
        p.Vitals.Health = 100;
        zone.Entities.Get<Character>(e).Input.Yaw = yaw;
        return e;
    }

    private static ref Character Ch(ShipRig rig, Entity e) => ref rig.Zone.Entities.Get<Character>(e);

    /// <summary>Turns a character to look at a point.</summary>
    private static void Look(ShipRig rig, Entity e, Vector3 at)
    {
        ref Character c = ref Ch(rig, e);
        Vector3 eye = rig.Zone.EyeOf(c.State);
        Vector3 up = rig.Zone.Frame.UpAt(c.State.Position);
        Vector3 d = at - eye;
        Vector3 flat = d - up * Vector3.Dot(d, up);
        c.Input.Yaw = MathF.Atan2(-flat.X, -flat.Z);
        c.Input.Pitch = MathF.Atan2(Vector3.Dot(d, up), flat.Length());
    }

    /// <summary>GROUND.md §6.7: the hatch, boarding, the seat carrying the pilot, stepping out to a safe point, the lock.</summary>
    [Test]
    public static void BoardingAndExit()
    {
        using var rig = new ShipRig(content: true);
        Zone zone = rig.Zone;
        Ship ship = rig.Add(yaw: 0);
        rig.Step(45); // settle and sleep
        Assert.True(ship.State.Asleep && ship.ParkedBody != 0, "a parked ship sleeps on its skids, a static body");

        // Ada stands at the hatch (on the ship's right: east, the nose facing north).
        Vector3 hatch = ship.PointOf(ShipRig.Finch.Hatch);
        Entity ada = AddPlayer(rig, "Ada", hatch + new Vector3(1.6f, 0, 0));
        ship.Owner = zone.PlayerOf(ada)!.Id;
        Look(rig, ada, hatch);
        TargetRef atHatch = new(TargetFamily.Ship, ship.Id.Value, ShipParts.Hatch), atCap = atHatch with { Part = ShipParts.FuelCap },
            atCargo = atHatch with { Part = ShipParts.Cargo };
        Assert.Equal(OutcomeCode.HatchShut, zone.Interact(ada, atHatch, "board"), "the hatch is shut");
        Assert.Equal(OutcomeCode.Done, zone.Interact(ada, atHatch, "hatch-open"), "opens the hatch");
        rig.Step(Ship.HatchTicks);
        Assert.Equal(Ship.HatchTicks, ship.HatchProgress, "the hatch swung open in a second");
        Entity bo = AddPlayer(rig, "Bo", hatch + new Vector3(1.4f, 0, 0.8f));
        Assert.Equal(OutcomeCode.Done, zone.Interact(ada, atHatch, "board"), "Ada boards");
        Assert.Equal(OutcomeCode.SeatTaken, zone.Interact(bo, atHatch, "board"), "the seat is taken");
        Assert.True(zone.SeatOf(ada) == ship && ship.Pilot == zone.PlayerOf(ada)!.Id, "Ada is in the seat");
        Assert.Equal(Stance.Seated, Ch(rig, ada).State.Stance, "seated");
        Assert.True(Vector3.Distance(Ch(rig, ada).State.Position, ship.PointOf(ShipRig.Finch.Seat)) < 1e-3f, "at the seat");

        // She takes off: the seat carries her; she can't step out in the air.
        ship.Input = new ShipInput { Vertical = 1 };
        rig.Step(60);
        float height = rig.Height(ship);
        Assert.True(height > 8, $"lifted off ({height:0.0} m)");
        Assert.True(Vector3.Distance(Ch(rig, ada).State.Position, ship.PointOf(ShipRig.Finch.Seat)) < 1e-3f, "the seat carries the pilot");
        Assert.Equal(OutcomeCode.NotLanded, zone.ExitShip(ada), "no stepping out in the air");
        Assert.True(!ship.HatchOpen && ship.HatchProgress == 0, "the hatch shut behind her");

        // Down again, and out to a safe point by the hatch.
        ship.Input = new ShipInput { Vertical = -1 };
        for (int i = 0; i < 30 * 30 && !ship.State.Has(ShipFlags.Landed); i++)
        {
            rig.Step();
        }
        Assert.True(ship.State.Has(ShipFlags.Landed), "landed again");
        Assert.Equal(OutcomeCode.Done, zone.ExitShip(ada), "steps out");
        Vector3 feet = Ch(rig, ada).State.Position;
        Fixtures.Report(FormattableString.Invariant($"stepped out {Vector3.Distance(feet, ship.PointOf(ShipRig.Finch.Exits[0])):0.00} m from the first exit, {zone.HeightAboveGround(feet):0.00} m over the ground"));
        Assert.True(zone.SeatOf(ada) == null && ship.Pilot.IsNone && Ch(rig, ada).State.Stance == Stance.Standing, "out of the seat");
        Assert.True(Vector3.Distance(feet, ship.PointOf(ShipRig.Finch.Exits[0])) < 1.5f, "at the hatch's exit");
        Assert.True(ship.HatchOpen, "the hatch opens to let her out");
        rig.Step(20);
        Assert.True(Vector3.Distance(Ch(rig, ada).State.Position, feet) < 0.1f, "standing on the ground, clear of the hull");

        // The lock: only the owner gets in.
        Assert.Equal(OutcomeCode.NotUsable, zone.Interact(bo, atHatch, "ship-lock"), "only the owner is offered the lock");
        Look(rig, ada, ship.PointOf(ShipRig.Finch.Hatch));
        Assert.Equal(OutcomeCode.Done, zone.Interact(ada, atHatch, "ship-lock"), "Ada locks it");
        Look(rig, bo, ship.PointOf(ShipRig.Finch.Hatch));
        Assert.Equal(OutcomeCode.Locked, zone.Interact(bo, atHatch, "board"), "locked to Bo");
        Assert.Equal(OutcomeCode.Locked, zone.Interact(bo, atCargo, "open"), "its cargo too");
        Assert.Equal(OutcomeCode.Done, zone.Interact(ada, atHatch, "hatch-close"), "Ada shuts the hatch");
        rig.Step(Ship.HatchTicks);
        Assert.Equal(OutcomeCode.Locked, zone.Interact(bo, atHatch, "hatch-open"), "and Bo can't open it");
        Assert.Equal(OutcomeCode.Done, zone.Interact(ada, atHatch, "hatch-open"), "Ada can");
        rig.Step(Ship.HatchTicks);

        // The cargo: a box behind the open hatch.
        Assert.Equal(OutcomeCode.Done, zone.Interact(ada, atCargo, "open"), "Ada opens the cargo");
        Player pa = zone.PlayerOf(ada)!;
        Assert.Equal(LootKind.Box, zone.LootKindOf(pa), "a box");
        pa.Inventory.Give(ContentFixture.Content, ContentFixture.Item("wood"), 100);
        Assert.Equal(OutcomeCode.Done, zone.QuickMove(ada, ContainerRef.Main, 0, ContentFixture.Item("wood").Id), "puts wood in");
        Assert.Equal(100, ship.Cargo[0].Count, "the wood is in the cargo");

        // Refuelling from lamp oil.
        float fuel = ship.State.Fuel;
        pa.Inventory.Give(ContentFixture.Content, ContentFixture.Item("lamp-oil"), 30);
        ship.State.Fuel = 480;
        Assert.Equal(OutcomeCode.Done, zone.Interact(ada, atCap, "refuel"), "refuels");
        Assert.Near(500, ship.State.Fuel, 1e-3, "filled to the brim");
        Assert.Equal(10, pa.Inventory.Count(ContentFixture.Item("lamp-oil")), "20 units went in");
        _ = fuel;
    }

    /// <summary>GROUND.md §6.7: the vehicle workbench builds a Tin Finch, which rolls out onto its bay when that is clear.</summary>
    [Test]
    public static void CraftAtTheVehicleWorkbench()
    {
        using var rig = new ShipRig(content: true);
        Zone zone = rig.Zone;
        GameContent c = ContentFixture.Content;
        Recipe recipe = c.Recipe(c.ItemByKey("tin-finch").Id)!;
        Entity ada = AddPlayer(rig, "Ada", new Vector3(0, 0, 2));
        Player p = zone.PlayerOf(ada)!;
        foreach (ItemAmount a in recipe.Ingredients)
        {
            p.Inventory.Give(c, a.Item, a.Count);
        }
        Assert.Equal(OutcomeCode.NeedsStation, zone.Craft(ada, recipe.Id, 1, out _), "no vehicle workbench in reach");

        // The bench, facing north; its bay is in front of it.
        DeployableDef bench = c.Deployable("vehicle-workbench")!;
        Vector3 at = zone.Settle(Vector3.Zero);
        var station = new Deployable(zone.NextId(), bench, at, zone.Frame.UpAt(at), 0, p.Id);
        zone.AddStatic(station);
        // Something in the way on the bay first: a box.
        DeployableDef boxDef = c.Deployable("large-box")!;
        Vector3 bay = zone.Settle(new Vector3(0, 0, -bench.Bay!.At.Z));
        var box = new Deployable(zone.NextId(), boxDef, bay, zone.Frame.UpAt(bay), 0, p.Id);
        zone.AddStatic(box);
        Assert.Equal(OutcomeCode.Done, zone.Craft(ada, recipe.Id, 1, out _), "starts the Tin Finch at the bench");
        int notices = 0;
        int ticks = recipe.UnitTicks + 2 * CharacterMotor.TickRate;
        for (int i = 0; i < ticks; i++)
        {
            rig.Step();
            notices += zone.GameEvents.Count(e => e.Kind == ZoneEventKind.Notice);
            zone.GameEvents.Clear();
        }
        Assert.Equal(0, zone.Ships.Count, "no ship while the bay is blocked");
        Assert.Equal(1, notices, "told once to clear the bay");
        Assert.True(p.Crafting.Jobs.Count == 1, "the job waits");
        zone.RemoveStatic(box);
        rig.Step(CharacterMotor.TickRate + 1);
        Assert.Equal(1, zone.Ships.Count, "the ship rolled out");
        Ship ship = zone.Ships[0];
        Fixtures.Report(FormattableString.Invariant($"built in {recipe.Seconds:0} s; rolled out {Vector3.Distance(ship.Origin, bay):0.00} m from the bay's centre, {ship.State.Fuel:0} fuel, owner {ship.Owner}"));
        Assert.True(Vector3.Distance(ship.Origin, bay) < 0.2f, "on the bay");
        Assert.Equal(p.Id, ship.Owner, "Ada's ship");
        Assert.Near(50, ship.State.Fuel, 1e-3, "the recipe's lamp oil is its first tank");
        Assert.True(p.Crafting.Jobs.Count == 0 && p.Inventory.Count(c.ItemByKey("tin-finch")) == 0, "the job is done and nothing went into the inventory");
        rig.Step(45);
        Assert.True(ship.State.Asleep && ship.State.Has(ShipFlags.Landed), "it stands on its skids");
    }

    /// <summary>GROUND.md §6.7: weapons hurt the hull through the zone's damage hooks; when it goes, the pilot dies and a wreck holds the cargo.</summary>
    [Test]
    public static void WeaponsAndTheWreck()
    {
        using var rig = new ShipRig(content: true);
        Zone zone = rig.Zone;
        GameContent c = ContentFixture.Content;
        Ship ship = rig.Add(yaw: 0);
        rig.Step(45);
        ship.Cargo.Set(0, ItemStack.Of(c.ItemByKey("wood"), 500));

        // A melee hit through the zone's trace, as a swing lands.
        Vector3 side = ship.PointOf(new Vector3(-0.8f, 1.4f, 0.4f));
        Entity bo = AddPlayer(rig, "Bo", side + new Vector3(1.5f, -1.4f, 0));
        Player pb = zone.PlayerOf(bo)!;
        pb.Inventory.Give(c, c.ItemByKey("stone-hatchet"), 1);
        Look(rig, bo, side);
        ref Character cb = ref Ch(rig, bo);
        Vector3 eye = zone.EyeOf(cb.State);
        TraceHit hit = zone.Trace(eye, Vector3.Normalize(side - eye), 2.5f, 0.15f, pb.Id, zone.Tick);
        Assert.Equal(ship.Ref, hit.Target, "a swing at the hull meets the ship");
        float lost = zone.DamageShip(ship, 15, ShipDamage.Melee, pb);
        Assert.Near(15 * ShipRig.Finch.MeleeDamage, lost, 1e-4, "melee does its share");

        // Bullets and explosions take the rest.
        zone.DamageShip(ship, 35, ShipDamage.Bullet, pb);
        Assert.Near(ShipRig.Finch.Health - 15 * ShipRig.Finch.MeleeDamage - 35 * ShipRig.Finch.BulletDamage, ship.State.Health, 1e-3, "a rifle round");
        Assert.True(!ship.State.Asleep, "a hit wakes it");

        // Ada in the seat when it goes.
        Vector3 hatch = ship.PointOf(ShipRig.Finch.Hatch);
        Entity ada = AddPlayer(rig, "Ada", hatch + new Vector3(1.6f, 0, 0));
        Look(rig, ada, hatch);
        var atHatch = new TargetRef(TargetFamily.Ship, ship.Id.Value, ShipParts.Hatch);
        zone.Interact(ada, atHatch, "hatch-open");
        rig.Step(Ship.HatchTicks);
        Assert.Equal(OutcomeCode.Done, zone.Interact(ada, atHatch, "board"), "boards");
        Player pa = zone.PlayerOf(ada)!;

        // G5's blasts: a rocket going off a metre and a half from the nose takes its falloff's share; the pilot inside
        // is the cabin's to protect. Then charges stuck to the hull, whole.
        BlastStats rocket = c.Blast("rocket")!;
        Vector3 at = ship.PointOf(new Vector3(0, 1.4f, 3.6f));
        float distance = Vector3.Distance(Zone.ShipClosestPoint(ship, at), at);
        float hull = ship.State.Health, health = pa.Vitals.Health;
        zone.Blast(rocket, at, Vector3.UnitZ, TargetRef.None, TargetFamily.None, pb.Id, "Bo", c.ItemByKey("rocket").Id);
        float expected = (float)(rocket.Deployable * rocket.Falloff(distance)) * ShipRig.Finch.ExplosiveDamage;
        Fixtures.Report(FormattableString.Invariant($"a rocket {distance:0.00} m from the hull: {hull - ship.State.Health:0.0} hull lost"));
        Assert.True(expected > 0, "in the blast's reach");
        Assert.Near(expected, hull - ship.State.Health, 1e-2, "a rocket's blast hurts the hull");
        Assert.Equal(health, pa.Vitals.Health, "the pilot is sheltered");
        BlastStats charge = c.Blast("timed-charge")!;
        Vector3 skin = Zone.ShipClosestPoint(ship, ship.PointOf(new Vector3(-2, 1.4f, 0.4f)));
        for (int i = 0; i < 2 && !ship.Destroyed; i++)
        {
            zone.Blast(charge, skin, -Vector3.UnitX, ship.Ref, TargetFamily.Ship, pb.Id, "Bo", c.ItemByKey("timed-charge").Id);
        }
        Assert.True(ship.Destroyed, "two timed charges stuck to it destroy it");
        Assert.True(pa.Dead && pa.KilledBy == "Bo", "the pilot went down with it, killed by Bo");
        Corpse? wreck = zone.Statics().OfType<Corpse>().FirstOrDefault(k => k.Of == ship.Id);
        Assert.True(wreck != null, "a wreck");
        Assert.True(wreck!.Loot.Size >= 3 && Enumerable.Range(0, wreck.Loot.Size).Any(i => wreck.Loot[i].Item == c.ItemByKey("wood").Id && wreck.Loot[i].Count == 500),
            "the cargo is in the wreck");
        rig.Step(Ship.WreckTicks + 1);
        Assert.Equal(0, zone.Ships.Count, "the burning hull is gone a moment later");
    }
}

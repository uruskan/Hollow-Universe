using System;
using System.Linq;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Placement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;
using Hollow.Simulation.Ships;
using Hollow.Surface;

namespace Hollow.Tests;

/// <summary>
/// A test coast: dry land 1.5 m above the sea to the west of the landing site, the sea to the east of it (a step down to
/// <paramref name="depth"/> under the water at the landing site's meridian). A lake, when asked: no sea at all, but water
/// standing <paramref name="depth"/> deep over the ground everywhere east of it.
/// </summary>
public sealed class CoastSurface : ISurface
{
    private readonly Vector3D _east;
    private readonly double _depth;
    private readonly bool _lake;

    public CoastSurface(double radius, Vector3D east, double depth, bool lake = false)
    {
        Radius = radius;
        _east = east;
        _depth = depth;
        _lake = lake;
    }

    public double Radius { get; }

    public double SeaRadius => _lake ? 0 : Radius;

    private bool Wet(Vector3D direction) => Vector3D.Dot(direction, _east) > 0;

    public double HeightAt(Vector3D direction) => Wet(direction) ? -_depth : 1.5;

    public double WaterLevelAt(Vector3D direction) =>
        _lake ? Wet(direction) ? 0 : double.NegativeInfinity : 0;
}

/// <summary>The motorboat (docs/MOTORBOAT.md): it floats on its hull, drives on its propeller, is put on the water and crafted at a workbench.</summary>
public static class MotorboatTests
{
    private static ShipDef Boat => ContentFixture.Content.Ship("motorboat")!;

    private static ShipRig Coast(double depth = 6, bool lake = false)
    {
        ZoneFrame frame = ZoneFrame.At(new LevelSurface(ShipRig.Earthlike), 10 * Math.PI / 180, 20 * Math.PI / 180);
        return new ShipRig(new CoastSurface(ShipRig.Earthlike, frame.East, depth, lake), content: true);
    }

    /// <summary>The water's surface over a point of the tangent plane (metres east and south of the landing site).</summary>
    private static Vector3 Water(Zone zone, float east, float south)
    {
        Vector3 bed = zone.GroundAt(east, south);
        Vector3D body = zone.Frame.ToBody(bed);
        double level = zone.Surface.Radius + zone.Surface.WaterLevelAt(body / body.Length);
        return bed + zone.Frame.UpAt(bed) * (float)(level - body.Length);
    }

    private static Ship Launch(ShipRig rig, float east = 20, float south = 0, float yaw = 0, float fuel = 100) =>
        rig.Zone.Ships().SpawnShip(Boat, Water(rig.Zone, east, south) + rig.Zone.Frame.UpAt(Water(rig.Zone, east, south)) * 0.02f, yaw,
            EntityId.None, fuel);

    /// <summary>How far the keel (the model's pivot) is under the water, metres.</summary>
    private static float Draft(Zone zone, Ship ship)
    {
        Vector3D origin = zone.Frame.ToBody(ship.Origin);
        return (float)(zone.Surface.Radius + zone.Surface.WaterLevelAt(origin / origin.Length) - origin.Length);
    }

    /// <summary>The boat's tilt from level (radians): its up against the world's.</summary>
    private static float Tilt(ShipRig rig, Ship ship) =>
        MathF.Acos(Math.Clamp(Vector3.Dot(ship.State.Up, rig.Up(ship)), -1f, 1f));

    /// <summary>The nose's pitch above the horizon (radians).</summary>
    private static float NoseUp(ShipRig rig, Ship ship) =>
        MathF.Asin(Math.Clamp(Vector3.Dot(ship.State.Forward, rig.Up(ship)), -1f, 1f));

    private static float Heading(ShipRig rig, Ship ship) => ShipMotor.YawOf(ship.State.Rotation, rig.Up(ship));

    /// <summary>How far the nose swung right (clockwise seen from above) between two headings, within ±π.</summary>
    private static float Right(float before, float after) => MathF.IEEERemainder(before - after, MathF.Tau);

    /// <summary>The motorboat's data: a propeller, a hull that floats, the boat's controls, two open seats, a crate, on the water.</summary>
    [Test]
    public static void ItsKindIsABoat()
    {
        ShipDef boat = Boat;
        Assert.True(boat.Drive is ShipPropeller, "it drives on a propeller");
        Assert.True(boat.Buoyancy is { Points.Count: 6 }, "it floats on its hull");
        Assert.Equal("boat", boat.Controls, "the boat's controls");
        Assert.Equal(2, boat.Seats.Count, "a helm and a bench");
        Assert.True(boat.Seats.All(s => s.Exposed && s.Door < 0), "both in the open, no door");
        Assert.True(boat.CargoPart > 0 && boat.CargoSlots == 8, "a crate of eight");
        Assert.True(!boat.NavLights, "no flight glows");
        Placeable placeable = ContentFixture.Content.ItemByKey("motorboat").Places!;
        Assert.Equal(PlacementSurfaces.Water, placeable.Rule.Surfaces, "put on the water alone");
        Assert.True(placeable.Rule.Water, "standing in it");
        Assert.True(ContentFixture.Content.Ship("tin-finch")!.Drive is ShipThrusters, "the Tin Finch keeps its thrusters");
    }

    /// <summary>Put on the water, it settles on its hull at its draft, level, and rests (asleep, afloat).</summary>
    [Test]
    public static void FloatsLevelAtItsDraft()
    {
        using ShipRig rig = Coast();
        Ship ship = Launch(rig);
        rig.Step(10 * CharacterMotor.TickRate);
        float draft = Draft(rig.Zone, ship);
        Fixtures.Report(FormattableString.Invariant($"draft {draft:0.000} m, tilt {Tilt(rig, ship) * 180 / MathF.PI:0.00}°, {ship.State}"));
        Assert.True(draft is > 0.06f and < 0.3f, $"the keel 6 to 30 cm under ({draft:0.000} m)");
        Assert.True(Tilt(rig, ship) < 2.5f * MathF.PI / 180, "level within 2.5°");
        Assert.True(ship.State.Has(ShipFlags.Wet), "in the water");
        Assert.True(ship.State.Asleep, "at rest");
        Assert.True(!ship.State.Has(ShipFlags.Landed), "afloat, not landed");
    }

    /// <summary>Heeled 25° and trimmed 10° bow down, it rights itself within a few seconds (its columns' lift where they push).</summary>
    [Test]
    public static void RightsItselfFromAHeel()
    {
        using ShipRig rig = Coast();
        Ship ship = Launch(rig);
        rig.Step(CharacterMotor.TickRate);
        Quaternion heel = Quaternion.CreateFromAxisAngle(ship.State.Forward, 25 * MathF.PI / 180) *
            Quaternion.CreateFromAxisAngle(-ship.State.Right, -10 * MathF.PI / 180);
        ship.State.Rotation = Quaternion.Normalize(heel * ship.State.Rotation);
        ship.State.RestTicks = 0;
        float start = Tilt(rig, ship);
        rig.Step(6 * CharacterMotor.TickRate);
        Fixtures.Report(FormattableString.Invariant($"tilt {start * 180 / MathF.PI:0.0}° → {Tilt(rig, ship) * 180 / MathF.PI:0.00}°"));
        Assert.True(start > 20 * MathF.PI / 180, "heeled over");
        Assert.True(Tilt(rig, ship) < 2.5f * MathF.PI / 180, "back to level");
    }

    /// <summary>
    /// Full ahead: up to its 8 m/s, the bow a little up, the hull tracking (little slip); the wheel over: it turns;
    /// let go: it coasts to a stop; the motor burns lamp oil.
    /// </summary>
    [Test]
    public static void DrivesTurnsAndCoasts()
    {
        using ShipRig rig = Coast();
        Ship ship = Launch(rig);
        rig.Step(3 * CharacterMotor.TickRate);
        float fuel = ship.State.Fuel;
        rig.Fly(ship, new ShipInput { Throttle = 1 }, 10 * CharacterMotor.TickRate);
        float speed = ship.State.Velocity.Length();
        float slip = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(ship.State.Velocity), ship.State.Forward), -1f, 1f));
        float bow = NoseUp(rig, ship);
        Fixtures.Report(FormattableString.Invariant($"full ahead: {speed:0.00} m/s, slip {slip * 180 / MathF.PI:0.0}°, bow {bow * 180 / MathF.PI:0.00}°, draft {Draft(rig.Zone, ship):0.000} m"));
        Assert.True(speed is > 7f and < 8.6f, $"about 8 m/s ({speed:0.00})");
        Assert.True(slip < 3 * MathF.PI / 180, "straight along its nose");
        Assert.True(bow is > 0.1f * MathF.PI / 180 and < 8 * MathF.PI / 180, "the bow a little up under power");
        Assert.True(ship.State.Fuel < fuel, "lamp oil burnt");

        float before = Heading(rig, ship);
        rig.Fly(ship, new ShipInput { Throttle = 1, Yaw = 1 }, 3 * CharacterMotor.TickRate);
        float turned = Right(before, Heading(rig, ship));
        slip = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(ship.State.Velocity), ship.State.Forward), -1f, 1f));
        Fixtures.Report(FormattableString.Invariant($"wheel over 3 s: turned {turned * 180 / MathF.PI:0} degrees, slip {slip * 180 / MathF.PI:0.0}°, heel {Tilt(rig, ship) * 180 / MathF.PI:0.0}°"));
        Assert.True(turned > 60 * MathF.PI / 180, "turned right (east of north) more than 60°");
        Assert.True(slip < 25 * MathF.PI / 180, "its keel holds it to its turn");

        rig.Fly(ship, new ShipInput(), 20 * CharacterMotor.TickRate);
        Fixtures.Report(FormattableString.Invariant($"coasting 20 s: {ship.State.Velocity.Length():0.00} m/s"));
        Assert.True(ship.State.Velocity.Length() < 1f, "coasts to a stop");
    }

    /// <summary>Astern: slower, and the wheel turns it the other way.</summary>
    [Test]
    public static void AsternTheWheelTurnsTheOtherWay()
    {
        using ShipRig rig = Coast();
        Ship ship = Launch(rig);
        rig.Step(CharacterMotor.TickRate);
        rig.Fly(ship, new ShipInput { Throttle = -1 }, 6 * CharacterMotor.TickRate);
        float ahead = Vector3.Dot(ship.State.Velocity, ship.State.Forward);
        Assert.True(ahead is < -1.5f and > -2.8f, $"astern at about 2.5 m/s ({ahead:0.00})");
        float before = Heading(rig, ship);
        rig.Fly(ship, new ShipInput { Throttle = -1, Yaw = 1 }, 2 * CharacterMotor.TickRate);
        Assert.True(Right(before, Heading(rig, ship)) < -10 * MathF.PI / 180, "the wheel to the right swings the nose left going astern");
    }

    /// <summary>On dry land its propeller bites nothing: the motor runs, the boat stays where it lies, on its keel.</summary>
    [Test]
    public static void NoWayOnDryLand()
    {
        using ShipRig rig = Coast();
        Vector3 beach = rig.Zone.GroundAt(-12, 0);
        Ship ship = rig.Zone.Ships().SpawnShip(Boat, beach + rig.Zone.Frame.UpAt(beach) * 0.05f, 0, EntityId.None, 100);
        rig.Step(2 * CharacterMotor.TickRate);
        Vector3 lay = ship.State.Position;
        rig.Fly(ship, new ShipInput { Throttle = 1, Yaw = 1 }, 3 * CharacterMotor.TickRate);
        Fixtures.Report(FormattableString.Invariant($"ashore at full throttle: moved {Vector3.Distance(lay, ship.State.Position):0.000} m, {ship.State}"));
        Assert.True(Vector3.Distance(lay, ship.State.Position) < 0.15f, "it goes nowhere");
        Assert.True(!ship.State.Has(ShipFlags.Wet), "dry");
        Assert.True(ship.State.Has(ShipFlags.Touching), "on its keel");
    }

    /// <summary>A lake floats it as the sea does: the water is the ground's (ISurface.WaterLevelAt), not the sea's alone.</summary>
    [Test]
    public static void FloatsOnALake()
    {
        using ShipRig rig = Coast(depth: 3, lake: true);
        Assert.True(rig.Zone.Surface.SeaRadius == 0, "no sea");
        Ship ship = Launch(rig);
        rig.Step(8 * CharacterMotor.TickRate);
        float draft = Draft(rig.Zone, ship);
        Assert.True(draft is > 0.06f and < 0.3f, $"afloat at its draft ({draft:0.000} m)");
        Assert.True(ship.State.Has(ShipFlags.Wet) && ship.State.Asleep, "in the lake, at rest");
        rig.Fly(ship, new ShipInput { Throttle = 1 }, 4 * CharacterMotor.TickRate);
        Assert.True(ship.State.Velocity.Length() > 5, "and drives on it");
    }

    /// <summary>
    /// From its item: put on the water within reach (its ghost on the water's surface), never on land ("Put it on the
    /// water"); placed, it is Ada's with its 20 of lamp oil and floats.
    /// </summary>
    [Test]
    public static void PutOnTheWaterNotOnLand()
    {
        using var rig = new GameRig(spec => new CoastSurface(spec.Radius,
            ZoneFrame.At(new LevelSurface(spec.Radius), spec.LatitudeDegrees * Math.PI / 180, spec.LongitudeDegrees * Math.PI / 180).East, 4));
        Entity ada = rig.Add("Ada", -2.5f, 0);
        rig.Give(ada, "motorboat", 1);
        Placeable boat = rig.C.ItemByKey("motorboat").Places!;
        Vector3 eye = rig.Zone.EyeOf(rig.Ch(ada).State);
        PlacementCandidate? onLand = rig.Zone.Placement().Aim(ada, boat, rig.Zone.GroundAt(-6, 0.3f) - eye, 0);
        Assert.Equal(OutcomeCode.NotOnWater, onLand?.Outcome.Code, "not on the beach");
        PlacementCandidate? ghost = rig.Zone.Placement().Aim(ada, boat, Water(rig.Zone, 4.5f, 0.3f) - eye, 0);
        Assert.True(ghost is { Valid: true, Surface: PlacementSurfaces.Water }, $"its ghost on the water ({ghost})");
        Assert.Equal(OutcomeCode.Done,
            rig.Zone.Placement().Place(ada, ContainerRef.Main, Enumerable.Range(0, rig.P(ada).Inventory.Main.Size)
                .First(i => rig.P(ada).Inventory.Main[i].Item == boat.SlotItem!.Id), ghost!).Code, "placed");
        Ship ship = rig.Zone.Ships().All.Single();
        Assert.True(ship.Owner == rig.P(ada).Id && ship.Def.Key == "motorboat", "Ada's motorboat");
        Assert.Near(20, ship.State.Fuel, 1e-3, "with its first can of lamp oil");
        Assert.Equal(0, rig.P(ada).Inventory.Count(rig.C.ItemByKey("motorboat")), "the item used up");
        rig.Step(8 * CharacterMotor.TickRate);
        Assert.True(ship.State.Has(ShipFlags.Wet) && ship.State.Asleep, "afloat and at rest");
    }

    /// <summary>
    /// Crafted at a level 1 workbench once learnt, it goes into the inventory as its item (nothing rolls out: there is no
    /// bay to a boat).
    /// </summary>
    [Test]
    public static void CraftedAtAWorkbenchAsItsItem()
    {
        using var rig = new GameRig();
        Zone zone = rig.Zone;
        GameContent c = rig.C;
        Entity ada = rig.Add("Ada", 0, 0);
        Player p = rig.P(ada);
        Recipe recipe = c.Recipe(c.ItemByKey("motorboat").Id)!;
        Assert.True(recipe.Station == null && recipe.Workbench == 1, "a level 1 workbench, no station");
        foreach (ItemAmount a in recipe.Ingredients)
        {
            p.Inventory.Give(c, a.Item, a.Count);
        }
        Assert.Equal(OutcomeCode.UnknownRecipe, zone.Actions().Craft(ada, recipe.Id, 1, out _).Code, "learnt first (the level 1 tree)");
        p.Blueprints.Learn(recipe);
        Assert.Equal(OutcomeCode.NeedsWorkbench, zone.Actions().Craft(ada, recipe.Id, 1, out _).Code, "needs the workbench");
        Vector3 at = zone.GroundAt(1.5f, 0);
        zone.Things().AddStatic(new Deployable(zone.NextId(), c.Deployable("workbench-1")!, at, zone.Frame.UpAt(at), 0, p.Id));
        Assert.Equal(OutcomeCode.Done, zone.Actions().Craft(ada, recipe.Id, 1, out _).Code, "crafting");
        rig.Step(recipe.UnitTicks + 2);
        Assert.Equal(1, p.Inventory.Count(c.ItemByKey("motorboat")), "the boat's item in the inventory");
        Assert.Equal(0, zone.Ships().All.Count, "nothing rolled out");
    }

    /// <summary>
    /// A driver and a passenger: the helm and the open bench are sat in from the water beside the boat (no door), the
    /// bench carries its passenger as the boat drives, and leaving over deep water puts them in it beside the boat.
    /// </summary>
    [Test]
    public static void ADriverAndAPassenger()
    {
        using ShipRig rig = Coast(depth: 6);
        Zone zone = rig.Zone;
        Ship ship = Launch(rig, yaw: 0);
        rig.Step(4 * CharacterMotor.TickRate);
        Entity Swimmer(string name, Vector3 local)
        {
            Vector3 at = ship.PointOf(local);
            Vector3 up = zone.Frame.UpAt(at);
            Entity e = zone.Players().AddPlayer(zone.NextId(), name, zone.NewCharacter(at - up * 1.2f));
            Player pl = zone.Players().PlayerOf(e)!;
            pl.Inventory.Clear();
            pl.Vitals.Health = 100;
            return e;
        }
        ShipDef def = ship.Def;
        int helmPart = def.PartIndex("helm"), benchPart = def.PartIndex("bench");
        Entity ada = Swimmer("Ada", new Vector3(-1.4f, 0, -1.2f)), bo = Swimmer("Bo", new Vector3(1.4f, 0, 0.2f));
        ship.Owner = zone.Players().PlayerOf(ada)!.Id;
        void LookAt(Entity e, int part)
        {
            ref Character c = ref zone.Entities.Get<Character>(e);
            c.Input.View = Vector3.Normalize(ship.PointOf(def.PartPoint(part)) - zone.EyeOf(c.State));
        }
        rig.Step(2);
        LookAt(ada, helmPart);
        LookAt(bo, benchPart);
        Assert.Equal(OutcomeCode.Done, zone.Interaction().Interact(ada, new TargetRef(TargetFamily.Ship, ship.Id.Value, (byte)helmPart), "take-helm").Code,
            "Ada takes the helm from the water");
        Assert.Equal(OutcomeCode.Done, zone.Interaction().Interact(bo, new TargetRef(TargetFamily.Ship, ship.Id.Value, (byte)benchPart), "sit").Code,
            "Bo sits on the bench (no door to it)");
        ship.Input = new ShipInput { Throttle = 1 };
        Vector3 start = ship.State.Position;
        rig.Step(4 * CharacterMotor.TickRate);
        ref Character boc = ref zone.Entities.Get<Character>(bo);
        Vector3 bench = ship.PointOf(def.Seats[def.SeatIndex("bench")].At.Position);
        Fixtures.Report(FormattableString.Invariant($"drove {Vector3.Distance(start, ship.State.Position):0.0} m; Bo {Vector3.Distance(boc.State.Position, bench):0.000} m from the bench"));
        Assert.True(Vector3.Distance(start, ship.State.Position) > 8, "the boat drove off with them");
        Assert.True(boc.State.Stance == Stance.Seated && Vector3.Distance(boc.State.Position, bench) < 0.3f, "Bo rides on the bench");

        ship.Input = new ShipInput();
        rig.Step(15 * CharacterMotor.TickRate);
        Assert.Equal(OutcomeCode.Done, zone.Ships().LeaveSeat(bo).Code, "Bo gets out over deep water");
        rig.Step(1);
        ref Character left = ref zone.Entities.Get<Character>(bo);
        float fromBoat = Vector3.Distance(left.State.Position, ship.State.Position);
        Assert.True(left.State.Stance != Stance.Seated && fromBoat is > 0.8f and < 3.5f, $"beside the boat ({fromBoat:0.00} m)");
        rig.Step(2 * CharacterMotor.TickRate);
        float overWater = zone.Ground.HeightAboveWater(zone.Entities.Get<Character>(bo).State.Position);
        Fixtures.Report(FormattableString.Invariant($"Bo's feet {overWater:0.00} m from the surface two seconds later"));
        Assert.True(overWater is > -2.5f and < 0.3f, "in the water, swimming, not on the bottom 6 m down");
    }
}

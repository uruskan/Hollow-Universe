using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using Hollow.Simulation;
using Hollow.Simulation.Interaction;
using Hollow.Simulation.Building;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Raid;
using Hollow.Simulation.World;
using Hollow.Simulation.Targets;

namespace Hollow.Tests;

/// <summary>Raiding in a <see cref="GameRig"/>: huts built through the building rules, explosives through the zone (GROUND.md §6.6).</summary>
public static class Raids
{
    public static ThrowableStats Throwable(GameRig rig, string key) => rig.C.ItemByKey(key).Throwable!;

    /// <summary>A stone (or other grade) hut on the ground at a point of the zone, its doorway facing +Z, listing <paramref name="owner"/>.</summary>
    public static TestBases.Hut Hut(GameRig rig, float x, float z, int grade = 2, ulong owner = 1, string? door = "sheet-metal-door") =>
        TestBases.Build(rig.Zone, rig.Zone.GroundAt(x, z), 0, grade, owner, door) ?? throw new TestFailure("no hut");

    /// <summary>Puts a character on a point (feet), at rest.</summary>
    public static void Stand(GameRig rig, Entity e, Vector3 feet) => rig.Zone.Teleport(e, feet + rig.Zone.Frame.UpAt(feet) * 0.02f);

    /// <summary>Steps until a condition holds; fails after <paramref name="most"/> ticks. Returns the ticks stepped.</summary>
    public static int StepUntil(GameRig rig, Func<bool> done, int most, string what)
    {
        for (int i = 0; i <= most; i++)
        {
            if (done())
            {
                return i;
            }
            rig.Step();
        }
        throw new TestFailure($"timed out ({most} ticks) waiting for {what}");
    }

    /// <summary>Gives a player items into a belt slot.</summary>
    public static void Belt(GameRig rig, Entity e, int slot, string key, int count) =>
        rig.P(e).Inventory.Belt.Set(slot, ItemStack.Of(rig.C.ItemByKey(key), count));

    public static double Health(object part) => part switch
    {
        Piece p => p.Health,
        Door d => d.Health,
        Deployable d => d.Health,
        _ => double.NaN,
    };
}

public static class RaidTests
{
    /// <summary>
    /// GROUND.md §6.6, the raid-cost table: how many of each explosive break each grade of wall, hit on its hard side
    /// and on its soft side, and each door. Rust's counts (the owner allows its raid tables) come out exactly on our hit
    /// points: timed charges 1/2/4/8 for wood, stone, metal and armoured walls, rockets 2/4/8/15, bag charges 3/10/23/46,
    /// tin grenades 13/46/112/223, explosive rounds 49/185/400/799; doors as in the table. Explosives hurt both sides alike
    /// (Rust); the soft side counts for blows (a stone wall takes 5% of a pickaxe on its hard side, all of it on its soft side).
    /// </summary>
    [Test]
    public static void RaidCostsAreRustsOnEveryGradeAndSide()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        BuildingData data = c.Building;
        BuildingWorld world = rig.Zone.Building!;
        var walls = new (string Key, int[] Counts)[]
        {
            ("timed-charge", new[] { 1, 1, 2, 4, 8 }),
            ("rocket", new[] { 1, 2, 4, 8, 15 }),
            ("bag-charge", new[] { 1, 3, 10, 23, 46 }),
            ("tin-grenade", new[] { 1, 13, 46, 112, 223 }),
            ("rifle-ammo-explosive", new[] { 1, 49, 185, 400, 799 }),
            ("rocket-hv", new[] { 1, 4, 12, 23, 45 }),
        };
        // Wooden, sheet metal, garage and armoured doors.
        string[] doorKeys = { "wooden-door", "sheet-metal-door", "garage-door", "armoured-door" };
        var doors = new Dictionary<string, int[]>
        {
            ["timed-charge"] = new[] { 1, 1, 2, 2 },
            ["rocket"] = new[] { 1, 2, 3, 4 },
            ["bag-charge"] = new[] { 2, 4, 9, 12 },
            ["tin-grenade"] = new[] { 10, 18, 43, 56 },
            ["rifle-ammo-explosive"] = new[] { 19, 63, 150, 200 },
            ["rocket-hv"] = new[] { 3, 4, 9, 12 },
        };
        var table = new StringBuilder("\n      hits to break        twig  wood stone metal armd | soft side            | wooden metal garage armd door");
        int spot = 0;
        foreach ((string key, int[] counts) in walls)
        {
            BlastStats blast = c.Blast(key)!;
            ushort item = c.ItemByKey(key).Id;
            var hard = new int[counts.Length];
            var soft = new int[counts.Length];
            // A fresh armoured foundation for each explosive: the splash it takes adds up over the trials.
            (float x, float z, double heading) = TestBases.Ring(spot++, walls.Length, 5.5f);
            Piece foundation = world.AddFirst(TestBases.FrameOver(rig.Zone, data, data.Piece("foundation")!, Ground(rig, x, z), heading), data.Piece("foundation")!, 4, 1, 0);
            for (int g = 0; g < data.Grades.Count; g++)
            {
                for (int side = 0; side < 2; side++)
                {
                    Piece wall = On(world, foundation, new SocketId(SocketKind.Edge, 0), "wall", g);
                    var hut = new TestBases.Hut { Foundation = foundation, Walls = new[] { wall, wall, wall, wall } };
                    (Vector3 face, Vector3 normal) = hut.Face(wall, height: 2.6, inside: side == 1);
                    int n = Count(rig, blast, item, wall, face + normal * Zone.StuckOffset, normal, () => wall.Gone);
                    (side == 0 ? hard : soft)[g] = n;
                    Assert.True(foundation is { Gone: false }, $"{key}: the foundation stands");
                }
                Assert.Equal(counts[g], hard[g], $"{key} against a {data.Grades[g].Key} wall's hard side");
                Assert.Equal(counts[g], soft[g], $"{key} against a {data.Grades[g].Key} wall's soft side (explosives ignore the side)");
            }
            // Doors in a doorway (and the garage door in a wall frame) on the same foundation.
            var doorCounts = new int[doorKeys.Length];
            for (int d = 0; d < doorKeys.Length; d++)
            {
                DoorDef def = data.Door(doorKeys[d])!;
                Piece frame = On(world, foundation, new SocketId(SocketKind.Edge, 0), def.Opening == Opening.Double ? "wall-frame" : "doorway", 4);
                (Vector3 position, Quaternion rotation) = frame.ZoneTransform;
                Assert.True(world.PlaceDoor(1, def, frame, 0, position + Vector3.Transform(new Vector3(0, 1.6f, 2), rotation), out Door? door).Ok,
                    $"{def.Key} fitted");
                Vector3 outward = Vector3.Transform(Vector3.UnitZ, rotation);
                (Vector3 hinge, Quaternion leafTurn) = door!.Leaf(0, false);
                LeafDef leaf = def.Leaves[0];
                Vector3 face = hinge + Vector3.Transform(new Vector3(leaf.Size.X / 2, 1.1f, MathF.Abs(leaf.Size.Z) / 2), leafTurn);
                doorCounts[d] = Count(rig, blast, item, door, face + outward * Zone.StuckOffset, outward, () => door.Gone);
                Assert.Equal(doors[key][d], doorCounts[d], $"{key} against a {def.Key}");
                world.Remove(frame, rig.Zone.BuildingNow, immediate: true);
            }
            table.Append(CultureInfo.InvariantCulture, $"\n      {key,-20} {string.Join(" ", hard.Select(h => $"{h,5}"))} | ")
                .Append(string.Join(" ", soft.Select(h => $"{h,3}"))).Append(" | ").Append(string.Join(" ", doorCounts.Select(h => $"{h,5}")));
        }
        Fixtures.Report(table.ToString());

        // Blows care about the side: a stone wall takes 5% of a melee hit on its hard side and all of it on its soft side.
        (float fx, float fz, double fh) = TestBases.Ring(0, 1, 0);
        Piece stone = On(world, world.AddFirst(TestBases.FrameOver(rig.Zone, data, data.Piece("foundation")!, Ground(rig, fx, fz), fh), data.Piece("foundation")!, 2, 1, 0),
            new SocketId(SocketKind.Edge, 0), "wall", 2);
        Vector3 inward = -Vector3.Transform(Vector3.UnitZ, stone.ZoneTransform.Rotation);
        Assert.Equal(1f, rig.Zone.DamageBuilding(stone, inward, 10, projectile: false), "a 10-point blow on the hard side does ⌈0.5⌉");
        Assert.Equal(10f, rig.Zone.DamageBuilding(stone, -inward, 10, projectile: false), "and all 10 on the soft side");
    }

    private static Vector3 Ground(GameRig rig, float x, float z)
    {
        Vector3 at = rig.Zone.GroundAt(x, z);
        rig.Zone.Physics.EnsureGround(at);
        return at;
    }

    private static Piece On(BuildingWorld world, Piece parent, SocketId socket, string key, int grade)
    {
        PieceDef def = world.Data.Piece(key)!;
        PlacementPlan plan = world.FromIntent(def, parent, socket, 0) ?? throw new TestFailure($"{key} doesn't fit");
        return world.Add(plan.Structure!, def, grade, plan.Pose, 1, 0);
    }

    // Blasts of one kind on a target (stuck to it) until it breaks.
    private static int Count(GameRig rig, BlastStats blast, ushort item, ITargetable target, Vector3 at, Vector3 normal, Func<bool> gone)
    {
        for (int n = 1; n <= 1000; n++)
        {
            rig.Zone.Blast(blast, at, normal, target.Ref, target.Ref.Family, EntityId.None, "", item);
            if (gone())
            {
                return n;
            }
        }
        return -1;
    }

    /// <summary>
    /// GROUND.md §6.6, throwing: a tin grenade thrown at a box bounces off it and comes to rest on the ground (not hanging in
    /// the air beside the box); a timed charge thrown at the box sticks to it, and its blast breaks the box.
    /// </summary>
    [Test]
    public static void ThrownExplosivesBounceRestAndStick()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        Vector3 boxAt = rig.Zone.GroundAt(0, 0);
        var box = new Deployable(rig.Zone.NextId(), c.Deployable("large-box")!, boxAt, rig.Zone.Frame.UpAt(boxAt), 0, EntityId.None);
        rig.Zone.AddStatic(box);
        Entity ada = rig.Add("Ada", 0, 4.5f);
        Raids.Belt(rig, ada, 0, "tin-grenade", 1);
        Raids.Belt(rig, ada, 1, "timed-charge", 1);
        rig.Step(2);
        // Aimed a little high: it drops on the way (0.4 m over 4.5 m at 15 m/s).
        Vector3 up = rig.Zone.Frame.UpAt(box.Centre);
        rig.Look(ada, box.Centre + up * 0.45f);
        ref Character ch = ref rig.Ch(ada);
        Assert.Equal(OutcomeCode.Done, rig.Zone.ThrowExplosive(ada, 0, c.ItemByKey("tin-grenade").Id, ch.Input.Yaw, ch.Input.Pitch, lob: false), "the grenade");
        LiveExplosive grenade = rig.Zone.Explosives.Single();
        Raids.StepUntil(rig, () => grenade.State != ExplosiveState.Flying, 90, "the grenade coming to rest");
        float height = rig.Zone.HeightAboveGround(grenade.Position);
        Assert.True(grenade.Bounces >= 2, $"it bounced off the box and the ground ({grenade.Bounces})");
        Assert.True(grenade.State == ExplosiveState.Resting && (height < 0.3f || grenade.Link.Support.Whole == box.Ref),
            $"resting on the ground or the box ({grenade.State} on {grenade.Link.Support}, {height:0.00} m up)");
        Fixtures.Report($"the grenade bounced {grenade.Bounces} time(s) and lies {Vector3.Distance(grenade.Position, box.Centre):0.0} m from the box, {height:0.00} m up");

        rig.Step(30);
        rig.Look(ada, box.Centre + up * 1.1f);
        ch = ref rig.Ch(ada);
        Assert.Equal(OutcomeCode.Done, rig.Zone.ThrowExplosive(ada, 1, c.ItemByKey("timed-charge").Id, ch.Input.Yaw, ch.Input.Pitch, lob: false), "the charge");
        LiveExplosive charge = rig.Zone.Explosives.Single(e => e.Item.Key == "timed-charge");
        Raids.StepUntil(rig, () => charge.State != ExplosiveState.Flying, 30, "the charge landing");
        Assert.True(charge.State == ExplosiveState.Stuck && charge.Link.Support.Whole == box.Ref, $"stuck to the box ({charge.State} on {charge.Link.Support})");
        Raids.Stand(rig, ada, rig.Zone.GroundAt(0, 15));
        Raids.StepUntil(rig, () => charge.Gone, 320, "the charge going off");
        Assert.True(box.Health <= 0, "the box broke");
    }

    /// <summary>
    /// GROUND.md §6.6, line of sight: a timed charge placed on a hut's north wall from outside breaks a share of it; a box
    /// and a player outside within the blast are hurt (with the falloff: the farther, the less); the box and the sleeper
    /// inside, the walls behind and the door across the room take nothing: the wall stands between. A grenade thrown in
    /// through the door hurts the sleeper.
    /// </summary>
    [Test]
    public static void WallsShieldWhatIsBehindThem()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        TestBases.Hut hut = Raids.Hut(rig, 0, 0);
        Piece north = hut.Walls[2];
        Vector3 up = rig.Zone.Frame.UpAt(hut.Inside);
        DeployableDef boxDef = c.Deployable("small-box")!;
        var inBox = new Deployable(rig.Zone.NextId(), boxDef, hut.ToZone(new(-0.7, 0, -0.9)), up, 0, EntityId.None);
        rig.Zone.AddStatic(inBox);
        Entity sleeper = rig.Add("Sleeper", 0, 0);
        Raids.Stand(rig, sleeper, hut.ToZone(new(-0.6, 0, 0)));
        rig.Zone.Sleep(sleeper);
        Entity ada = rig.Add("Ada", 0, -4);
        (Vector3 face, Vector3 outward) = hut.Face(north, height: 1.4);
        Vector3 ground = rig.Zone.GroundAt(face.X + outward.X * 1.2f, face.Z + outward.Z * 1.2f);
        Raids.Stand(rig, ada, ground);
        Raids.Belt(rig, ada, 0, "timed-charge", 1);
        var outBox = new Deployable(rig.Zone.NextId(), boxDef, rig.Zone.GroundAt(face.X + outward.X * 1.0f + 1.5f, face.Z + outward.Z * 1.0f), up, 0, EntityId.None);
        rig.Zone.AddStatic(outBox);
        Entity bo = rig.Add("Bo", face.X + outward.X * 3.8f, face.Z + outward.Z * 3.8f);
        rig.Step(2);

        // Ada places the charge on the wall in front of her.
        rig.Look(ada, face);
        ref Character ch = ref rig.Ch(ada);
        Assert.Equal(OutcomeCode.Done, rig.Zone.PlaceCharge(ada, 0, c.ItemByKey("timed-charge").Id, ch.Input.Yaw, ch.Input.Pitch), "placed");
        LiveExplosive charge = rig.Zone.Explosives.Single();
        Assert.True(charge.State == ExplosiveState.Stuck && charge.Link.Support.Whole == north.Ref, $"stuck to the north wall ({charge.State}, {charge.Link.Support})");
        Assert.True(rig.P(ada).Inventory.Belt[0].IsEmpty, "the charge left the belt");
        Assert.Equal(OutcomeCode.Stale, rig.Zone.PlaceCharge(ada, 0, c.ItemByKey("timed-charge").Id, ch.Input.Yaw, ch.Input.Pitch), "nothing more to place");
        Assert.Equal(300, charge.FuseTicks, "a ten-second fuse");
        // She runs off (out of the blast); everyone else stays.
        Raids.Stand(rig, ada, rig.Zone.GroundAt(face.X + outward.X * 9, face.Z + outward.Z * 9));
        double northBefore = north.Health, boHealth = rig.P(bo).Vitals.Health;
        var walls = hut.Walls.ToDictionary(w => w, w => w.Health);
        int ticks = Raids.StepUntil(rig, () => charge.Gone, 320, "the charge going off");
        Assert.InRange(295, 301, ticks, "it went off on time");

        Assert.Equal(northBefore - 275, north.Health, "the wall took the charge whole (275)");
        Assert.True(outBox.Health < boxDef.Health, $"the box outside took damage ({outBox.Health} of {boxDef.Health})");
        double boTook = boHealth - rig.P(bo).Vitals.Health;
        Assert.InRange(1, 80, boTook, "Bo, 3.8 m out, took a little (the falloff)");
        Assert.Equal(boxDef.Health, inBox.Health, "the box inside took nothing");
        Assert.Equal(100f, rig.P(sleeper).Vitals.Health, "the sleeper inside took nothing");
        foreach (Piece w in hut.Walls.Where(w => w != north))
        {
            Assert.Equal(walls[w], w.Health, $"the {w.Def.Key} behind took nothing");
        }
        Assert.Equal(hut.Door!.Def.Health, hut.Door.Health, "the door across the room took nothing");
        Fixtures.Report($"charge on the north wall: wall {northBefore} → {north.Health}, box outside {boxDef.Health} → {outBox.Health:0}, " +
            $"Bo (3.8 m) −{boTook:0.0}; inside: box {inBox.Health}, sleeper {rig.P(sleeper).Vitals.Health}");

        // A grenade that lands inside (dropped by the door) hurts the sleeper: nothing stands between them.
        LiveExplosive grenade = rig.Zone.SpawnExplosive(c.ItemByKey("tin-grenade").Throwable!, hut.ToZone(new(0.4, 0.3, 0.6)), Vector3.Zero,
            EntityId.None, "", dud: false);
        Raids.StepUntil(rig, () => grenade.State != ExplosiveState.Flying, 30, "the grenade landing");
        Assert.True(grenade.State == ExplosiveState.Resting && grenade.Link.Support.Whole == hut.Foundation.Ref, $"resting on the floor ({grenade.State} on {grenade.Link.Support})");
        float before = rig.P(sleeper).Vitals.Health;
        Raids.StepUntil(rig, () => grenade.Gone, 200, "the grenade going off");
        Assert.True(rig.P(sleeper).Vitals.Health < before, $"the sleeper inside was hurt ({before} → {rig.P(sleeper).Vitals.Health})");
    }

    /// <summary>
    /// GROUND.md §6.6, duds: a bag charge thrown at a wall sticks to it, its fuse 6 to 12 seconds; one whose fuse goes out
    /// lies there harmless, can't be lit from afar, and the interact key lights it again with a new fuse (which may go out
    /// too), until it goes off. A charge that is burning can't be "lit again".
    /// </summary>
    [Test]
    public static void BagChargesDudAndAreLitAgain()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        TestBases.Hut hut = Raids.Hut(rig, 0, 0);
        Piece east = hut.Walls[1];
        (Vector3 face, Vector3 outward) = hut.Face(east, height: 1.4);
        Entity ada = rig.Add("Ada", 0, 0);
        Raids.Stand(rig, ada, rig.Zone.GroundAt(face.X + outward.X * 4, face.Z + outward.Z * 4));
        Raids.Belt(rig, ada, 0, "bag-charge", 2);
        rig.Step(2);
        ushort bag = c.ItemByKey("bag-charge").Id;

        // Thrown at the wall, it flies and sticks to it.
        rig.Look(ada, face);
        ref Character ch = ref rig.Ch(ada);
        Assert.Equal(OutcomeCode.Done, rig.Zone.ThrowExplosive(ada, 0, bag, ch.Input.Yaw, ch.Input.Pitch, lob: false), "thrown");
        Assert.Equal(OutcomeCode.Cooldown, rig.Zone.ThrowExplosive(ada, 0, bag, ch.Input.Yaw, ch.Input.Pitch, lob: false), "one a second");
        LiveExplosive thrown = rig.Zone.Explosives.Single();
        Raids.StepUntil(rig, () => thrown.State != ExplosiveState.Flying, 30, "the bag charge landing");
        Assert.True(thrown.State == ExplosiveState.Stuck && thrown.Link.Support.Whole == east.Ref, $"stuck to the east wall ({thrown.State} on {thrown.Link.Support})");
        Assert.InRange(6 * 30, 12 * 30, thrown.FuseTicks, "a 6–12 s fuse");
        var offered = new List<InteractOption>();
        Assert.True(rig.Zone.Options(ada, thrown.Ref, offered) && InteractionRules.Find(offered, c.Interactions.Index("relight")) == null,
            "a burning fuse isn't offered to be lit again");
        Raids.StepUntil(rig, () => thrown.Gone || thrown.Dud, 12 * 30 + 2, "the thrown charge");
        Fixtures.Report($"thrown bag charge: {(thrown.Dud ? "a dud" : $"went off, the stone wall at {east.Health}")}");

        // One that goes out: it lies there, harmless.
        double wall = east.Health;
        (Vector3 spot, _) = hut.Face(east, height: 1.0, along: 0.8);
        LiveExplosive dud = rig.Zone.StickExplosive(c.ItemByKey("bag-charge").Throwable!, spot + outward * 2, -outward, 3, rig.P(ada).Id, "Ada", dud: true)!;
        Assert.True(dud.State == ExplosiveState.Stuck, "stuck");
        Raids.StepUntil(rig, () => dud.Dud, 12 * 30 + 2, "the fuse going out");
        rig.Step(30);
        Assert.True(!dud.Gone && dud.Dud && !dud.Burning, "a dud lies there");
        Assert.Equal(wall, east.Health, "harmless");
        Assert.Equal(1L, rig.Zone.RaidStats.Duds - (thrown.Dud ? 1 : 0), "counted");

        // Lit again: not from afar; by the interact key within reach; it may go out again, until it goes off.
        Assert.Equal(OutcomeCode.OutOfReach, rig.Zone.Interact(ada, dud.Ref, "relight"), "too far to reach");
        Raids.Stand(rig, ada, rig.Zone.GroundAt(face.X + outward.X * 1.0f, face.Z + outward.Z * 1.0f));
        rig.Step(2);
        int lit = 0;
        while (!dud.Gone)
        {
            Assert.True(lit < 10, "it goes off in the end");
            Assert.Equal(OutcomeCode.Done, rig.Zone.Interact(ada, dud.Ref, "relight"), "lit again");
            lit++;
            Assert.True(dud.Burning && !dud.Dud, "burning again");
            Raids.Stand(rig, ada, rig.Zone.GroundAt(face.X + outward.X * 10, face.Z + outward.Z * 10));
            Raids.StepUntil(rig, () => dud.Gone || dud.Dud, 12 * 30 + 2, "the relit charge");
            if (dud.Dud)
            {
                Raids.Stand(rig, ada, rig.Zone.GroundAt(face.X + outward.X * 1.0f, face.Z + outward.Z * 1.0f));
                rig.Step(2);
            }
        }
        Assert.True(east.Gone || east.Health < wall, $"the relit charge hurt the wall ({wall} → {east.Health})");
        Assert.Equal(OutcomeCode.NotFound, rig.Zone.Interact(ada, dud.Ref, "relight"), "nothing left to light");
        Fixtures.Report($"the dud took {lit} lighting(s) to go off; the wall {wall} → {(east.Gone ? "broken" : east.Health)}");
    }

    /// <summary>
    /// GROUND.md §6.6, rockets: a rocket flies at 18 m/s with a little drop (so 20 m takes 1.1 s), explodes on the wall it
    /// hits (137 or 138 of a stone wall's 500) and hurts someone standing beside the impact; the launcher holds one and
    /// reloads in 5 s; a high-velocity rocket flies faster and flatter but hardly scratches stone.
    /// </summary>
    [Test]
    public static void RocketsFlyDropAndBlastWalls()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        TestBases.Hut hut = Raids.Hut(rig, 0, 0);
        Piece east = hut.Walls[1];
        (Vector3 face, Vector3 outward) = hut.Face(east, height: 1.6);
        Entity ada = rig.Add("Ada", 0, 0);
        Raids.Stand(rig, ada, rig.Zone.GroundAt(face.X + outward.X * 20, face.Z + outward.Z * 20));
        Entity bo = rig.Add("Bo", 0, 0);
        Raids.Stand(rig, bo, rig.Zone.GroundAt(face.X + outward.X * 1.2f, face.Z + outward.Z * 1.2f + 1.5f));
        Guns.Arm(rig, ada, "rocket-launcher", "rocket", 1);
        rig.Give(ada, "rocket-hv", 1);
        rig.Step(20);
        rig.Look(ada, face);
        Assert.Equal(GunResult.Accepted, Guns.Queue(rig, ada, 1), "fired");
        float boBefore = rig.P(bo).Vitals.Health;
        int flight = Raids.StepUntil(rig, () => rig.Zone.RaidStats.Detonations == 1, 90, "the rocket's blast");
        Assert.InRange(31, 40, flight, "20 m at 18 m/s: about 34 ticks");
        Assert.True(east.Health is 362 or 363, $"the wall lost 137 or 138 ({east.Health})");
        Assert.True(rig.P(bo).Vitals.Health < boBefore, "Bo beside the impact was hurt");
        Assert.Equal(GunResult.Accepted, Guns.Queue(rig, ada, 2), "queued");
        rig.Step();
        Assert.Equal(GunResult.Empty, rig.P(ada).GunRefused, "one rocket at a time");

        // Reload with the high-velocity rocket: 5 s; it flies twice as fast and does little to stone.
        Assert.Equal(GunResult.Accepted, Guns.Queue(rig, ada, 3, GunActionKind.Reload, ammo: c.ItemByKey("rocket-hv").Id), "reloading");
        rig.Step(151);
        Assert.Equal(1, (int)rig.P(ada).Inventory.Belt[0].Loaded, "loaded after 5 s");
        double before = east.Health;
        rig.Look(ada, face);
        Assert.Equal(GunResult.Accepted, Guns.Queue(rig, ada, 4), "fired the high-velocity rocket");
        int fast = Raids.StepUntil(rig, () => rig.Zone.RaidStats.Detonations == 2, 60, "the high-velocity rocket's blast");
        Assert.InRange(13, 18, fast, "20 m at 40 m/s: about 15 ticks");
        Assert.Equal(before - 45, east.Health, "45 against stone");
        Fixtures.Report($"rocket 20 m in {flight} ticks (the wall 500 → {before}), high-velocity in {fast} ticks (→ {east.Health}); Bo −{boBefore - rig.P(bo).Vitals.Health:0}");
    }

    /// <summary>
    /// GROUND.md §6.3 and §6.6, collapse: two timed charges stuck to a stone hut's foundation from inside break it; what
    /// it held (walls, doorway and door, the roof, the claim device) comes down through the building rules, layer by layer;
    /// a dud stuck to a wall falls to the ground when its wall goes.
    /// </summary>
    [Test]
    public static void ABrokenFoundationBringsTheHutDown()
    {
        using var rig = new GameRig();
        GameContent c = rig.C;
        TestBases.Hut hut = Raids.Hut(rig, 0, 0);
        ThrowableStats charge = c.ItemByKey("timed-charge").Throwable!;
        Vector3 up = rig.Zone.Frame.UpAt(hut.Inside);
        (Vector3 face, Vector3 outward) = hut.Face(hut.Walls[3], height: 2.0);
        LiveExplosive dud = rig.Zone.StickExplosive(c.ItemByKey("bag-charge").Throwable!, face + outward, -outward, 2, EntityId.None, "", dud: true)!;
        Assert.True(dud.Link.Support.Whole == hut.Walls[3].Ref, "a dud on the west wall");
        for (int i = 0; i < 2; i++)
        {
            rig.Zone.StickExplosive(charge, hut.Inside + up * 1.5f + new Vector3(0.3f * i, 0, 0), -up, 2, EntityId.None, "");
        }
        Assert.True(rig.Zone.Explosives.Count(e => e.Link.Support.Whole == hut.Foundation.Ref) == 2, "two charges on the floor");
        Raids.StepUntil(rig, () => hut.Foundation.Gone, 320, "the foundation breaking");
        int fell = Raids.StepUntil(rig, () => hut.Walls.All(w => w.Gone) && hut.Roof!.Gone && rig.Zone.Building!.Collapsing == 0, 60, "the collapse");
        Assert.True(hut.Door!.Gone && hut.Claim!.Gone, "the door and the claim device went with their pieces");
        Assert.True(fell >= 4, $"layer by layer, not all at once ({fell} ticks)");
        Raids.StepUntil(rig, () => dud.State != ExplosiveState.Flying, 90, "the dud falling");
        Assert.True(dud.Link.Support.Family == TargetFamily.Ground, $"the dud on the ground ({dud.State} on {dud.Link.Support})");
        Assert.True(Vector3.Dot(dud.Position - rig.Zone.Settle(dud.Position), up) < 0.3f, "lying on the ground");
        Fixtures.Report($"the hut came down {fell} ticks after its foundation broke");
    }
}

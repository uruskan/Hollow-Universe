using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Building;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Physics;
using Hollow.Surface;

namespace Hollow.Tests;

/// <summary>GROUND.md §6.3: the building rules, engine-free.</summary>
public static class BuildingTests
{
    private const ulong Ada = 1, Bo = 2;

    private static readonly Lazy<BuildingData> DataLazy = new(() =>
        BuildingData.Load(Path.Combine(Fixtures.Root, "game", "data", "ground", "building.json")));

    private static BuildingData Data => DataLazy.Value;

    private static PieceDef P(string key) => Data.Piece(key) ?? throw new TestFailure("no piece " + key);

    /// <summary>A world without collision, its IDs counted from 1.</summary>
    private static BuildingWorld PureWorld()
    {
        ulong next = 0;
        return new BuildingWorld(Data, null, () => new EntityId(++next));
    }

    private static Piece First(BuildingWorld world, string key = "foundation", int grade = 0, double x = 0, double z = 0, double now = 0) =>
        world.AddFirst(BuildingFrame.At(new Vector3D(x, 0, z), Vector3D.UnitY, 0), P(key), grade, Ada, now);

    /// <summary>Adds a piece on a socket of another, without checks.</summary>
    private static Piece On(BuildingWorld world, Piece parent, SocketId socket, string key, int orientation = 0, int grade = 0, double now = 0)
    {
        PlacementPlan plan = world.FromIntent(P(key), parent, socket, orientation)
            ?? throw new TestFailure($"{key} doesn't fit {parent.Def.Key} {socket} #{orientation}");
        return world.Add(plan.Structure!, plan.Piece, grade, plan.Pose, Ada, now);
    }

    private static SocketId Edge(int i) => new(SocketKind.Edge, i);

    private static readonly SocketId Top = new(SocketKind.Top, 0);
    private static readonly SocketId Centre = new(SocketKind.Centre, 0);

    [Test]
    public static void BuildingDataIsSound()
    {
        Assert.True(Data.Problems.Count == 0, "building.json problems:\n        " + string.Join("\n        ", Data.Problems));
        Assert.Equal(20, Data.Pieces.Count, "pieces");
        Assert.Equal(5, Data.Grades.Count, "grades");
        Assert.Equal(20, Data.Wheel.Count, "wheel");
        Assert.Equal("50,200,300,200,25", string.Join(",", P("foundation").Cost), "foundation costs (RustMVP)");
        Assert.Equal("10,200,500,1000,2000", string.Join(",", Data.Grades.Select(g => g.Health)), "grade health (RustMVP)");
        for (int i = 0; i < Data.Pieces.Count; i++)
        {
            Assert.Equal(i, Data.Pieces[i].Id, "piece ids run in order");
        }
    }

    /// <summary>
    /// RustMVP's snap formulas in this frame: neighbouring slabs close their shared edge exactly, square and triangle
    /// alike, under a turned parent; walls stand on edges and stack; floors sit on wall tops over the inside; foundations
    /// step up and down half a storey; spirals climb and come back to their column.
    /// </summary>
    [Test]
    public static void SocketsSnapPiecesTogether()
    {
        foreach (string parentKey in new[] { "foundation", "foundation-triangle", "floor", "floor-triangle" })
        {
            foreach (string childKey in new[] { "foundation", "foundation-triangle", "floor", "floor-triangle" })
            {
                PieceDef parent = P(parentKey), child = P(childKey);
                foreach (int parentYaw in new[] { 0, 1, 5 })
                {
                    var parentPose = new Pose(new Vector3D(10, 2, -4), parentYaw);
                    for (int e = 0; e < parent.EdgeCount; e++)
                    {
                        int orientations = PieceGeometry.Orientations(parent, Edge(e), child);
                        bool allowed = parent.Family == PieceFamily.Foundation || child.Family == PieceFamily.Floor;
                        Assert.Equal(allowed ? child.EdgeCount : 0, orientations, $"{childKey} on {parentKey} edge {e}");
                        for (int o = 0; o < orientations; o++)
                        {
                            Assert.True(PieceGeometry.Snap(Data, parent, parentPose, Edge(e), child, o, out Pose pose), "snaps");
                            Vector3D[] pc = PieceGeometry.Footprint(parent.Shape, 3), cc = PieceGeometry.Footprint(child.Shape, 3);
                            Vector3D a = PieceGeometry.ToFrame(parentPose, pc[e]), b = PieceGeometry.ToFrame(parentPose, pc[(e + 1) % pc.Length]);
                            Vector3D c = PieceGeometry.ToFrame(pose, cc[o]), d = PieceGeometry.ToFrame(pose, cc[(o + 1) % cc.Length]);
                            Assert.Near(0, (a - d).Length + (b - c).Length, 1e-9, $"{childKey} #{o} on {parentKey} edge {e} yaw {parentYaw}: shared edge");
                        }
                    }
                }
            }
        }
        var origin = new Pose(Vector3D.Zero, 0);
        PieceDef foundation = P("foundation"), wall = P("wall"), floor = P("floor");
        Snap(foundation, origin, Edge(0), wall, 0, new Vector3D(0, 0, 1.5), 0, "wall on the south edge");
        Snap(foundation, origin, Edge(0), wall, 1, new Vector3D(0, 0, 1.5), 6, "the same wall flipped");
        Snap(foundation, origin, Edge(1), wall, 0, new Vector3D(1.5, 0, 0), 3, "wall on the east edge");
        var wallPose = new Pose(new Vector3D(0, 0, 1.5), 0);
        Snap(wall, wallPose, Top, wall, 0, new Vector3D(0, 3, 1.5), 0, "wall on a wall");
        Snap(wall, wallPose, Top, floor, 0, new Vector3D(0, 3, 0), 0, "floor on a wall top, over the inside");
        Snap(wall, wallPose, Top, floor, 4, new Vector3D(0, 3, 3), 6, "floor on a wall top, outside");
        Snap(wall, wallPose, Top, P("roof"), 0, new Vector3D(0, 3, 0), 0, "roof rising inward from a wall");
        Snap(foundation, origin, new SocketId(SocketKind.EdgeUp, 1), foundation, 0, new Vector3D(3, 1.5, 0), 9, "foundation half a storey up");
        Snap(foundation, origin, new SocketId(SocketKind.EdgeDown, 1), foundation, 0, new Vector3D(3, -1.5, 0), 9, "and down");
        Assert.Equal(4, PieceGeometry.Orientations(foundation, Centre, P("stairs-spiral")), "spiral on a centre: 4 ways");
        Assert.Equal(0, PieceGeometry.Orientations(foundation, Centre, P("stairs-spiral-triangle")), "a triangle spiral needs a triangle");
        Assert.Equal(0, PieceGeometry.Orientations(floor, Edge(0), foundation), "a floor can't hold a foundation");
        PieceDef spiral = P("stairs-spiral"), triangleSpiral = P("stairs-spiral-triangle");
        Snap(spiral, origin, Top, spiral, 0, new Vector3D(0, 1.5, 0), 6, "spiral continues, turned half a circle");
        Assert.True(PieceGeometry.Snap(Data, triangleSpiral, origin, Top, triangleSpiral, 0, out Pose second), "triangle spiral continues");
        Vector3D column = new(0, 0, 3 * Math.Sqrt(3) / 6);
        Vector3D secondColumn = PieceGeometry.ToFrame(second, column);
        Assert.Near(0, (new Vector3D(column.X, 1.5, column.Z) - secondColumn).Length, 1e-9, "the next triangle module turns about the same column");
        foreach (int code in new[] { 0, 3, 16, 32, 64, 67, 68, 71 })
        {
            Assert.Equal(code, SocketId.FromCode(code)!.Value.Code, "socket code round trip");
        }
        Assert.True(SocketId.FromCode(17) == null && SocketId.FromCode(-1) == null, "unknown socket codes are refused");
    }

    private static void Snap(PieceDef parent, Pose parentPose, SocketId socket, PieceDef child, int orientation, Vector3D position, int yaw, string what)
    {
        Assert.True(PieceGeometry.Snap(Data, parent, parentPose, socket, child, orientation, out Pose pose), what + ": snaps");
        Assert.Near(0, (pose.Position - position).Length, 1e-9, what + $": position {pose.Position}");
        Assert.Equal(yaw, pose.Yaw, what + ": yaw");
    }

    /// <summary>
    /// The stability of a known small base (RustMVP's examples): foundations and walls 100%, a floor on the walls 90%,
    /// floors reaching out past the walls 90 (on the wall's top), 70, 50, 30 and 10%, and the next one can't be placed;
    /// a spiral on a foundation 90% and a landing on it 80%.
    /// </summary>
    [Test]
    public static void StabilityOfAKnownSmallBase()
    {
        using BuildingWorld world = PureWorld();
        Piece f = First(world);
        Piece[] walls = Enumerable.Range(0, 4).Select(e => On(world, f, Edge(e), "wall")).ToArray();
        Piece ceiling = On(world, walls[0], Top, "floor");
        Piece reach = ceiling;
        var values = new List<int>();
        for (int i = 0; i < 5; i++)
        {
            reach = On(world, reach, Edge(1), "floor", orientation: 3);
            values.Add(reach.Stability);
        }
        PlacementPlan fifth = world.FromIntent(P("floor"), reach, Edge(1), 3)!;
        world.Check(fifth, Ada, ZoneOf(fifth) + new Vector3(0, 1, 0), null);
        Piece spiral = On(world, First(world, x: 30), Centre, "stairs-spiral");
        Piece landing = On(world, spiral, Top, "floor");
        Fixtures.Report($"foundation {f.StabilityPercent}%, walls {walls.Select(w => w.StabilityPercent).Min()}%, ceiling {ceiling.StabilityPercent}%, " +
            $"reaching out {string.Join(" ", values.Select(v => v / 10.0 + "%"))}, then {fifth.Outcome.Text}; spiral {spiral.StabilityPercent}%, landing {landing.StabilityPercent}%");
        Assert.Equal(1000, f.Stability, "foundation");
        Assert.True(walls.All(w => w.Stability == 1000), "walls keep full stability");
        Assert.Equal(900, ceiling.Stability, "floor on the walls");
        Assert.Equal("900,700,500,300,100", string.Join(",", values), "floors reaching out");
        Assert.Equal(Refusal.Unstable, fifth.Outcome.Reason, "the next floor can't stand");
        Assert.Equal(900, spiral.Stability, "spiral on a foundation's centre");
        Assert.Equal(800, landing.Stability, "landing on the spiral");
        Assert.Equal(4, ceiling.Supporters.Count(l => l.Other.Def.Family == PieceFamily.Wall), "the ceiling rests on all four walls");
    }

    private static Vector3 ZoneOf(PlacementPlan plan) => plan.ZoneTransform.Position;

    // The edge of a slab that faces a way in its frame (+X east, +Z south).
    private static SocketId Facing(Piece slab, double x, double z)
    {
        int best = 0;
        double most = double.MinValue;
        for (int e = 0; e < slab.Def.EdgeCount; e++)
        {
            Vector3D middle = PieceGeometry.Rotate(PieceGeometry.Edge(slab.Def.Shape, e, 3).Middle, slab.Pose.Yaw);
            double along = middle.X * x + middle.Z * z;
            if (along > most)
            {
                most = along;
                best = e;
            }
        }
        return Edge(best);
    }

    private static SocketId WestEdge(Piece slab) => Facing(slab, -1, 0);

    /// <summary>
    /// Taking away a support brings down what it held, one layer after another; a second support keeps a piece up.
    /// </summary>
    [Test]
    public static void RemovingSupportCollapsesInSequence()
    {
        using BuildingWorld world = PureWorld();
        Piece f1 = First(world);
        Piece f2 = On(world, f1, Edge(1), "foundation");
        Piece south = On(world, f2, Edge(0), "wall"), north = On(world, f2, Edge(2), "wall");
        Piece roof = On(world, south, Top, "floor");
        Piece out1 = On(world, roof, Edge(1), "floor", orientation: 3);
        Piece out2 = On(world, out1, Edge(1), "floor", orientation: 3);
        Assert.Equal(2, roof.Supporters.Count(l => l.Other.Def.Family == PieceFamily.Wall), "the ceiling rests on two walls");

        world.Remove(south, now: 100);
        Assert.True(!roof.Collapsing && roof.Stability == 900, "the other wall still holds the ceiling");

        // A wall on the west edge with floors reaching out west from its top: a shelf.
        Piece hanging = On(world, f1, Edge(3), "wall");
        Piece shelf = On(world, hanging, Top, "floor", orientation: 4);
        Piece shelf2 = On(world, shelf, WestEdge(shelf), "floor");
        Assert.True(shelf.Stability == 900 && shelf2.Stability == 700, "a shelf outside the west wall");
        var removed = new List<(double, Piece)>();
        double clock = 200;
        world.Changed += e =>
        {
            if (e.Change == BuildingChange.PieceRemoved)
            {
                removed.Add((clock, e.Piece!));
            }
        };
        world.Remove(hanging, now: 200);
        Assert.True(shelf.Collapsing && shelf2.Collapsing, "what the wall held is coming down");
        Assert.Equal(2, world.Collapsing, "two pieces queued");
        for (clock = 200.1; clock < 201; clock += 0.1)
        {
            world.Tick(clock);
        }
        Assert.Equal(3, removed.Count, "the wall, then the shelf, then the next");
        Assert.True(removed[1].Item2 == shelf && removed[2].Item2 == shelf2 && removed[1].Item1 < removed[2].Item1, "in sequence, nearest first");
        Assert.True(!f1.Gone && !roof.Gone && !out2.Gone, "the rest stands");
        Assert.Equal(1, world.Structures.Count, "still one structure");

        // Taking the foundation out brings down everything standing on it.
        world.Remove(f2, now: 300);
        for (clock = 300.1; clock < 302; clock += 0.1)
        {
            world.Tick(clock);
        }
        Assert.True(roof.Gone && out1.Gone && out2.Gone && north.Gone, "everything on the connector came down");
        Assert.True(!f1.Gone, "the other foundation stands");
    }

    /// <summary>
    /// The hammer: upgrades keep the health fraction and only go up; rotate and demolish only within 10 minutes of
    /// placing (the boundary included); repair waits 30 s after damage and costs RustMVP's amount.
    /// </summary>
    [Test]
    public static void HammerKeepsItsWindows()
    {
        using BuildingWorld world = PureWorld();
        var wallet = new Wallet { ["wood"] = 1000, ["stone"] = 1000 };
        Vector3 eye = new(0, 1.6f, 4);
        Piece f = First(world, grade: 2);
        Piece wall = On(world, f, Edge(0), "wall", grade: 1, now: 0);
        wall.Health = 100;
        Assert.True(world.Upgrade(Ada, wall, 2, wallet, eye).Ok, "wood to stone");
        Assert.Near(250, wall.Health, 1e-9, "half health stays half");
        Assert.Equal(700, wallet["stone"], "paid 300 stone");
        Assert.Equal(Refusal.NotHigherGrade, world.Upgrade(Ada, wall, 1, wallet, eye).Reason, "no downgrade");
        Assert.Equal(Refusal.NotEnough, world.Upgrade(Ada, wall, 4, wallet, eye).Reason, "no refined metal");

        Assert.True(world.Rotate(Ada, wall, eye, now: 599).Ok, "rotate inside the window");
        Assert.Equal(6, wall.Pose.Yaw, "the wall turned its soft side round");
        Assert.Equal(Refusal.EditWindowOver, world.Rotate(Ada, wall, eye, now: 600.5).Reason, "too late to rotate");
        Assert.Equal(Refusal.CannotRotate, world.Rotate(Ada, f, eye, now: 1).Reason, "foundations don't rotate");

        Piece side = On(world, f, Edge(1), "wall", now: 0);
        Assert.Equal(Refusal.EditWindowOver, world.Demolish(Ada, side, eye, now: 600.001).Reason, "too late to demolish");
        Piece other = On(world, f, Edge(2), "wall", now: 0);
        Assert.True(world.Demolish(Ada, other, eye, now: 600).Ok, "demolish at exactly 10 minutes");

        wall.Health = 500;
        world.Damage(wall, new DamageHit(new Vector3(0, 0, -1), Explosive: 100), now: 1000);
        Assert.Near(400, wall.Health, 1e-9, "damaged to 400");
        Outcome early = world.Repair(Ada, wall, wallet, eye, now: 1010);
        Assert.Equal(Refusal.RecentlyDamaged, early.Reason, "repair waits");
        Assert.Equal("Recently damaged: repair in 20 s", early.Text, "and says how long");
        int stone = wallet["stone"];
        Assert.True(world.Repair(Ada, wall, wallet, eye, now: 1030).Ok, "repair after 30 s");
        Assert.Near(500, wall.Health, 1e-9, "+100");
        Assert.Equal(12, stone - wallet["stone"], "12 stone (RustMVP)");
        Assert.Equal(Refusal.FullHealth, world.Repair(Ada, wall, wallet, eye, now: 1031).Reason, "nothing to repair");
    }

    /// <summary>
    /// A claim device gives its structure an area where only authorised players build; the area grows with the base and
    /// ends where it ends; authorising lets a player build, clearing the list stops them.
    /// </summary>
    [Test]
    public static void ClaimGivesBuildingRights()
    {
        using BuildingWorld world = PureWorld();
        Piece f = First(world);
        Assert.Equal(Privilege.None, world.PrivilegeAt(Bo, new Vector3(5, 0, 0)), "no device: anyone builds");
        Assert.True(world.PlaceClaim(Ada, f, new Vector3D(0.5, 0, 0.5), 0, out ClaimDevice? device).Ok, "device placed");
        Assert.Equal(Refusal.HasClaim, world.PlaceClaim(Ada, f, Vector3D.Zero, 0, out _).Reason, "one per structure");
        Assert.Equal(Privilege.Allowed, world.PrivilegeAt(Ada, new Vector3(5, 0, 0)), "its owner may build");
        Assert.Equal(Privilege.Blocked, world.PrivilegeAt(Bo, new Vector3(5, 0, 0)), "a stranger may not");

        PlacementPlan wall = world.FromIntent(P("wall"), f, Edge(0), 0)!;
        var wallet = new Wallet { Endless = true };
        world.Check(wall, Bo, new Vector3(0, 1.6f, 5), wallet);
        Assert.Equal(Refusal.BuildingBlocked, wall.Outcome.Reason, "Bo's wall is refused");
        Assert.Equal("Building blocked: someone else's claim", wall.Outcome.Text, "with the reason");
        Assert.Equal(Privilege.None, world.PrivilegeAt(Bo, new Vector3(60, 0, 0)), "far away is free");

        var far = new Vector3(0, 0, -36);
        Assert.Equal(Privilege.None, world.PrivilegeAt(Bo, far), "36 m north is outside");
        Piece reach = f;
        for (int i = 0; i < 7; i++)
        {
            reach = On(world, reach, Edge(2), "foundation");
        }
        Assert.Equal(Privilege.Blocked, world.PrivilegeAt(Bo, far), "the area grew with the base");

        Assert.True(world.Authorise(Bo, device!).Ok, "Bo authorises at the device");
        world.Check(wall, Bo, new Vector3(0, 1.6f, 5), wallet);
        Assert.True(wall.Valid, "now Bo may build: " + wall.Outcome);
        Assert.Equal(Refusal.NotAuthorised, world.ClearAuthorised(3, device!).Reason, "a stranger can't clear the list");
        Assert.True(world.ClearAuthorised(Ada, device!).Ok, "Ada clears the list");
        Assert.Equal(Privilege.Blocked, world.PrivilegeAt(Bo, new Vector3(5, 0, 0)), "Bo is blocked again");
        Assert.Equal(Privilege.Blocked, world.PrivilegeAt(Ada, new Vector3(5, 0, 0)), "and so is Ada, until she authorises again");
    }

    /// <summary>
    /// Upkeep and decay over elapsed time in closed form (ARCHITECTURE.md §10.4) give what live ticking gives: the same
    /// pieces left, the same health within rounding, the same materials taken.
    /// </summary>
    [Test]
    public static void DecayCatchUpMatchesLiveTicking()
    {
        const double hours = 82;
        (BuildingWorld once, Wallet onceStock) = DecayBase();
        (BuildingWorld live, Wallet liveStock) = DecayBase();
        using (once)
        using (live)
        {
            ClaimDevice claim = once.Claims.Single();
            Fixtures.Report($"upkeep a day: {string.Join(", ", once.UpkeepPerPeriod(claim.Structure).Select(u => $"{u.Value:0.#} {u.Key}"))}; " +
                $"protected {once.ProtectedSeconds(claim.Structure) / 3600:0.0} h; {once.PieceCount} pieces");
            var watch = Stopwatch.StartNew();
            once.AdvanceTime(hours * 3600, now: 0);
            double onceMs = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            int steps = (int)(hours * 3600 / 10);
            for (int i = 0; i < steps; i++)
            {
                live.AdvanceTime(10, now: i * 10);
            }
            double liveMs = watch.Elapsed.TotalMilliseconds;
            var a = once.AllPieces.ToDictionary(p => p.Id);
            var b = live.AllPieces.ToDictionary(p => p.Id);
            Fixtures.Report($"after {hours} h: {a.Count} pieces left; catch-up {onceMs:0.00} ms, {steps} live steps {liveMs:0} ms; " +
                $"stock {string.Join(", ", once.Data.Materials.Select(m => $"{onceStock[m]} {m}"))}");
            Assert.Equal(a.Count, b.Count, "pieces left");
            Assert.True(a.Count > 0 && a.Count < 10, "some decayed away, some stood");
            foreach ((EntityId id, Piece p) in a)
            {
                Assert.True(b.ContainsKey(id), $"{p} stands in both");
                Assert.Near(p.Health, b[id].Health, 1e-6 * p.MaxHealth, $"{p} health");
            }
            foreach (string material in once.Data.Materials)
            {
                Assert.Equal(onceStock[material], liveStock[material], material + " left");
            }
            Assert.True(a.Values.Any(p => p.Health < p.MaxHealth), "something decayed");
        }
    }

    // A claimed base: stone foundations, wood walls, a twig floor, a metal doorway; the device holds a little wood and
    // stone, no metal: the metal decays at once, the wood runs out first, the stone lasts.
    private static (BuildingWorld World, Wallet Stock) DecayBase()
    {
        BuildingWorld world = PureWorld();
        Piece f = First(world, grade: 2);
        Piece f2 = On(world, f, Edge(1), "foundation", grade: 2);
        On(world, f, Edge(2), "foundation", grade: 2);
        Piece[] walls = Enumerable.Range(0, 4).Select(e => On(world, f, Edge(e), e == 0 ? "doorway" : "wall", grade: e == 0 ? 3 : 1)).ToArray();
        On(world, walls[1], Top, "floor", grade: 0);
        On(world, f2, Edge(0), "wall", grade: 1);
        On(world, f2, Edge(1), "half-wall", grade: 2);
        world.PlaceClaim(Ada, f, new Vector3D(0.4, 0, -0.4), 0, out ClaimDevice? claim);
        var store = new Wallet { ["wood"] = 60, ["stone"] = 400 };
        claim!.Store = store;
        return (world, store);
    }

    /// <summary>
    /// Doors fit their openings, open and shut on the manifest's hinges, and their locks keep out whom they should: a
    /// key lock lets its owner and key holders through; a code lock those who know the code, with waits after wrong ones.
    /// </summary>
    [Test]
    public static void DoorLocksAndAuthorisation()
    {
        using BuildingWorld world = PureWorld();
        Piece f = First(world);
        Piece doorway = On(world, f, Edge(0), "doorway");
        Piece frame = On(world, f, Edge(1), "wall-frame");
        Piece wall = On(world, f, Edge(2), "wall");
        Vector3 eye = new(0, 1.6f, 4);
        DoorDef wooden = Data.Door("wooden-door")!, doubleDoor = Data.Door("wooden-double-door")!;
        Assert.Equal(Refusal.WrongOpening, world.PlaceDoor(Ada, doubleDoor, doorway, 0, eye, out _).Reason, "a double door needs a wall frame");
        Assert.Equal(Refusal.NotAnOpening, world.PlaceDoor(Ada, wooden, wall, 0, eye, out _).Reason, "not in a plain wall");
        Assert.True(world.PlaceDoor(Ada, wooden, doorway, 0, eye, out Door? door).Ok, "door fitted");
        Assert.True(world.PlaceDoor(Ada, doubleDoor, frame, 1, eye, out Door? wide).Ok, "double door fitted, turned round");
        Assert.Equal(Refusal.HasDoor, world.PlaceDoor(Ada, wooden, doorway, 0, eye, out _).Reason, "one door per opening");

        (Vector3 _, Quaternion shut) = door!.Leaf(0, open: false);
        (Vector3 _, Quaternion open) = door.Leaf(0, open: true);
        double swing = 2 * Math.Acos(Math.Min(1, Math.Abs(Quaternion.Dot(shut, open)))) * 180 / Math.PI;
        Assert.Near(100, swing, 1e-3, "the leaf swings 100° (the kit's manifest)");

        Assert.True(world.Toggle(Bo, door).Ok && door.Open, "no lock: anyone opens it");
        Assert.True(world.Toggle(Bo, door).Ok && !door.Open, "and shuts it");

        Assert.True(world.AttachKeyLock(Ada, door, out KeyLock? key).Ok, "key lock fitted");
        Assert.True(world.SetLocked(Ada, door, true).Ok, "Ada locks it");
        Assert.Equal(Refusal.Locked, world.Toggle(Bo, door).Reason, "Bo is locked out");
        Assert.True(world.Toggle(Bo, door, new[] { key!.KeyCode }).Ok, "with the key Bo gets in");
        Assert.True(key.Locked, "and it stays locked");
        Assert.True(world.Toggle(Ada, door).Ok, "its owner needs no key");
        Assert.Equal(Refusal.Locked, world.Toggle(Bo, door, new[] { key.KeyCode + 1 }).Reason, "another lock's key doesn't fit");
        Assert.Equal(Refusal.UnlockItFirst, world.RemoveLock(Ada, door).Reason, "unlock before taking it off");
        world.SetLocked(Ada, door, false);
        Assert.True(world.RemoveLock(Ada, door).Ok, "key lock off");

        Assert.True(world.AttachCodeLock(Ada, door, out CodeLock? code).Ok, "code lock fitted");
        Assert.Equal(Refusal.NoCode, world.SetLocked(Ada, door, true).Reason, "no code yet");
        Assert.Equal(Refusal.BadCode, world.SetCode(Ada, door, "12a4").Reason, "four digits");
        Assert.True(world.SetCode(Ada, door, "1234").Ok && code!.Locked, "setting the code locks it");
        Assert.Equal(Refusal.Locked, world.Toggle(Bo, door).Reason, "Bo is locked out");
        Assert.Equal(Refusal.WrongCode, world.EnterCode(Bo, door, "0000", now: 10).Reason, "a wrong code");
        Outcome wait = world.EnterCode(Bo, door, "1234", now: 10.5);
        Assert.Equal(Refusal.WaitForCode, wait.Reason, "then a wait: " + wait.Text);
        Assert.True(world.EnterCode(Bo, door, "1234", now: 11).Ok, "the right code after the wait");
        Assert.True(world.Toggle(Bo, door).Ok, "Bo is authorised now");
        Assert.True(world.SetCode(Bo, door, "9999").Reason == Refusal.UnlockItFirst, "changing the code needs it unlocked");
        Assert.True(world.PickUpDoor(Ada, wide!).Reason == Refusal.OpenItFirst, "a shut door can't be picked up");
        world.Toggle(Ada, wide!);
        Assert.True(world.PickUpDoor(Ada, wide!).Ok && frame.Door == null, "open, with no lock: picked up");

        world.Remove(doorway, now: 50);
        Assert.True(door.Gone, "the door goes with its doorway");
    }

    /// <summary>Hits by type through the grade's protection, rounded up once; a wall's inside is its soft side.</summary>
    [Test]
    public static void WallsHaveASoftSide()
    {
        using BuildingWorld world = PureWorld();
        Piece f = First(world, grade: 2);
        Piece wall = On(world, f, Edge(0), "wall", grade: 2);
        // The wall stands on the south edge (z = 1.5): its outside faces +Z.
        var fromOutside = new Vector3(0, 0, -1);
        var fromInside = new Vector3(0, 0, 1);
        DamageResult hard = world.Damage(wall, new DamageHit(fromOutside, Melee: 100), now: 1);
        DamageResult soft = world.Damage(wall, new DamageHit(fromInside, Melee: 100), now: 2);
        Fixtures.Report($"stone wall, 100 melee: {hard.Loss} from outside, {soft.Loss} from inside");
        Assert.True(!hard.SoftSide && soft.SoftSide, "sides told apart");
        Assert.Equal(5.0, hard.Loss, "hard side: 5%");
        Assert.Equal(100.0, soft.Loss, "soft side: all of it");
        Assert.Equal(0.0, world.Damage(wall, new DamageHit(fromInside, Bullet: 35), now: 3).Loss, "bullets don't hurt stone");
        Assert.Equal(1.0, world.Damage(wall, new DamageHit(fromOutside, Melee: 7), now: 4).Loss, "0.35 rounds up to 1");
        double before = wall.Health;
        Assert.Equal(before, world.Damage(wall, new DamageHit(fromOutside, Explosive: 1000), now: 5).Loss, "capped at what's left");
        Assert.True(wall.Gone, "destroyed");
    }

    /// <summary>
    /// Static entities cost nothing per tick (ARCHITECTURE.md §10.2): a world of 1,024 pieces ticks without touching any.
    /// </summary>
    [Test]
    public static void PiecesCostNothingPerTick()
    {
        using BuildingWorld world = PureWorld();
        Piece row = First(world);
        for (int x = 0; x < 32; x++)
        {
            Piece cell = row;
            for (int z = 1; z < 32; z++)
            {
                cell = On(world, cell, Edge(2), "foundation");
            }
            if (x < 31)
            {
                row = On(world, row, Edge(1), "foundation");
            }
        }
        Assert.Equal(1024, world.PieceCount, "pieces");
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < 10_000; i++)
        {
            world.Tick(i / 30.0);
        }
        double perTick = watch.Elapsed.TotalMilliseconds * 1000 / 10_000;
        Fixtures.Report($"{world.PieceCount} pieces, {world.Structures.Count} structure: {perTick:0.000} µs a tick, {world.TickWork} pieces touched");
        Assert.Equal(0L, world.TickWork, "no piece touched");
        Assert.True(perTick < 5, "a tick costs next to nothing");
    }

    // ---------------------------------------------------------------- with collision

    /// <summary>A ground for placement tests: flat, with a 3 m drop east of x = 12 (in the zone's frame at 0°, 0°).</summary>
    private sealed class CliffSurface : ISurface
    {
        private readonly ZoneFrame _frame;

        public CliffSurface(double radius)
        {
            Radius = radius;
            _frame = new ZoneFrame(ZoneFrame.Direction(0, 0) * radius);
        }

        public double Radius { get; }

        public double SeaRadius => 0;

        public double HeightAt(Vector3D direction)
        {
            Vector3 local = _frame.ToLocal(direction * Radius);
            return local.X > 12 ? -3 : 0;
        }

        public TriangleMesh GroundCollision(in ZoneFrame frame, double centreX, double centreZ, double size) =>
            SphereSurface.GridCollision(this, frame, centreX, centreZ, size, spacing: 1.0);
    }

    /// <summary>
    /// Placement with Jolt: the aim snaps a wall to the edge it points at, and every refusal comes with its reason: too
    /// far, out of sight, taken, floating, buried, into another piece, into the ground, a player in the way, no wood.
    /// </summary>
    [Test]
    public static void PlacementIsRefusedWithAReason()
    {
        var surface = new CliffSurface(6_435_000);
        ZoneFrame zone = ZoneFrame.At(surface, 0, 0);
        using var physics = new PhysicsWorld(surface, zone);
        physics.EnsureGround(Vector3.Zero, 1);
        ulong next = 0;
        using var world = new BuildingWorld(Data, physics, () => new EntityId(++next));
        var players = new List<Capsule>();
        world.Characters = () => players;
        var wallet = new Wallet { ["wood"] = 10_000 };
        var refusals = new List<string>();
        void Expect(Refusal reason, PlacementPlan? plan, Vector3 eye, IBuildingWallet? purse = null)
        {
            Assert.True(plan != null, $"a plan for {reason}");
            world.Check(plan!, Ada, eye, purse ?? wallet);
            Assert.Equal(reason, plan!.Outcome.Reason, $"{plan.Piece.Key}: {plan.Outcome.Text}");
            Assert.True(plan.Outcome.Text.Length > 0, "a line for the player");
            refusals.Add(plan.Outcome.Text);
        }

        // A free foundation where the player looks, 3 m ahead on the flat.
        var standing = new Vector3(6, 0, 4);
        Vector3 eye = standing + new Vector3(0, 1.6f, 0);
        PlacementPlan? first = world.Plan(Ada, P("foundation"), eye, new Vector3(0, -1.6f, -3), 0, wallet, now: 0);
        Assert.True(first != null && first.Valid && first.Support == null, "free foundation: " + first);
        Assert.True(world.Place(first!, Ada, eye, wallet, now: 0, out Piece? f).Ok, "placed");
        Assert.Equal(10_000 - 50, wallet["wood"], "paid 50 wood");
        (Vector3 fPosition, _) = f!.ZoneTransform;
        Fixtures.Report($"free foundation at {fPosition}, its top {fPosition.Y:0.000} m above the flat ground");
        Assert.InRange(Data.FoundationClearance, Data.FoundationClearance + 0.05, fPosition.Y, "just above the ground");

        // Aiming at its east edge with a wall plan picks that edge.
        Vector3 east = f.Structure.Frame.ToZone(PieceGeometry.ToFrame(f.Pose, new Vector3D(1.5, 0.6, 0))) is var e ? BuildingWorld_F(e) : default;
        PlacementPlan? wallPlan = world.Plan(Ada, P("wall"), eye, east - eye, 0, wallet, now: 1);
        Assert.True(wallPlan != null && wallPlan.Valid && wallPlan.Socket == Edge(1), "the wall snaps to the edge aimed at: " + wallPlan);
        world.Place(wallPlan!, Ada, eye, wallet, now: 1, out Piece? eastWall);

        Expect(Refusal.Occupied, world.FromIntent(P("wall"), f, Edge(1), 1), eye);
        Expect(Refusal.TooFar, world.FromIntent(P("wall"), f, Edge(3), 0), fPosition + new Vector3(-20, 1.6f, 0));
        Vector3 behind = fPosition + new Vector3(4.5f, 1.6f, 0);
        Expect(Refusal.OutOfSight, world.FromIntent(P("wall"), f, Edge(3), 0), behind);

        // East of x = 12 the ground drops 3 m: a foundation there would float; one half a storey down on the flat would be buried.
        Piece f2 = world.Add(f.Structure, P("foundation"), 0, world.FromIntent(P("foundation"), f, Facing(f, -1, 0), 0)!.Pose, Ada, 2);
        Piece east1 = world.Add(f.Structure, P("foundation"), 0, world.FromIntent(P("foundation"), f, Facing(f, 1, 0), 0)!.Pose, Ada, 2);
        PlacementPlan? overCliff = world.FromIntent(P("foundation"), east1, Facing(east1, 1, 0), 0);
        Expect(Refusal.Floating, overCliff, ZoneOf(overCliff!) + new Vector3(-3, 1.6f, 0));
        PlacementPlan? sunk = world.FromIntent(P("foundation"), f2, new SocketId(SocketKind.EdgeDown, Facing(f2, 0, -1).Edge), 0);
        Expect(Refusal.Buried, sunk, ZoneOf(sunk!) + new Vector3(0, 3, -3));

        // A floor out from a foundation's edge lies in the ground; a foundation of another frame overlapping this one cuts into it.
        PlacementPlan? lowFloor = world.FromIntent(P("floor"), f2, Facing(f2, 0, 1), 0);
        Expect(Refusal.HitsGround, lowFloor, ZoneOf(lowFloor!) + new Vector3(0, 1.6f, 3));
        var skew = new PlacementPlan
        {
            Piece = P("foundation"),
            Frame = BuildingFrame.At(new Vector3D(fPosition.X + 1.5, fPosition.Y, fPosition.Z - 1.5), Vector3D.UnitY, 0.4),
            Pose = new Pose(Vector3D.Zero, 0),
        };
        Expect(Refusal.HitsPiece, skew, eye);

        // Someone standing on the edge where a wall would go.
        PlacementPlan? southWall = world.FromIntent(P("wall"), f, Edge(0), 0);
        players.Add(new Capsule(ZoneOf(southWall!) + new Vector3(0, 0.05f, -0.2f), Vector3.UnitY, 0.32f, 1.76f));
        Expect(Refusal.PlayerInTheWay, southWall, eye);
        players.Clear();
        Expect(Refusal.NotEnough, southWall, eye, new Wallet());
        Assert.Equal("Not enough wood", southWall!.Outcome.Text, "names the material");
        world.Check(southWall, Ada, eye, wallet);
        Assert.True(southWall.Valid, "with the player gone and wood paid, the wall fits: " + southWall.Outcome);
        Assert.True(world.Plan(Ada, P("wall"), eye, new Vector3(0, 1, 0), 0, wallet, now: 3) == null, "a wall aimed at the sky: nothing to place");
        Fixtures.Report(string.Join(" | ", refusals));
        Assert.Equal(eastWall!.Body != 0, true, "pieces have bodies");
    }

    private static Vector3 BuildingWorld_F(Vector3D v) => new((float)v.X, (float)v.Y, (float)v.Z);
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Placement;
using Hollow.Surface;

namespace Hollow.Tests;

/// <summary>GROUND.md §6.3: the building rules, engine-free.</summary>
public static class BuildingTests
{
    private const ulong Ada = 1, Bo = 2;

    // The player's line for an answer (texts.json, GROUND_SYSTEMS.md §3.2).
    private static string Line(Outcome outcome) => ContentFixture.Content.Texts.Line(outcome);

    // building.json as the game reads it: with its materials linked to their items, for the answers' lines.
    private static readonly Lazy<BuildingData> DataLazy = new(() => ContentFixture.Content.Building);

    private static BuildingData Data => DataLazy.Value;

    private static PieceDef P(string key) => Data.Piece(key) ?? throw new TestFailure("no piece " + key);

    /// <summary>A world without collision, its IDs counted from 1.</summary>
    private static BuildingWorld PureWorld()
    {
        ulong next = 0;
        return new BuildingWorld(Data, null, () => new EntityId(++next));
    }

    private static Piece First(BuildingWorld world, string key = "foundation", int grade = 0, double x = 0, double z = 0, double now = 0) =>
        world.AddFirst(BuildingFrame.At(new Vector3D(x, 0, z), Vector3D.UnitY, -Math.PI / 2), P(key), grade, Ada, now);

    /// <summary>Adds a piece on a socket of another, without checks.</summary>
    private static Piece On(BuildingWorld world, Piece parent, SocketId socket, string key, int orientation = 0, int grade = 0, double now = 0)
    {
        PlacementPlan plan = world.FromIntent(P(key), parent, socket, orientation)
            ?? throw new TestFailure($"{key} doesn't fit {parent.Def.Key} {socket} #{orientation}");
        return world.Add(plan.Structure!, plan.Piece, grade, plan.Pose, Ada, now);
    }

    private static SocketId Edge(int i) => new(SocketKind.Edge, i);

    // ---------------------------------------------------------------- the placement pipeline (GROUND_SYSTEMS.md §6)

    /// <summary>A placement world of a building alone (its collision if it has any), on a ground (flat by default), in the world's frame.</summary>
    private static PlacementWorld Placing(BuildingWorld world, ISurface? surface = null)
    {
        ISurface ground = surface ?? new CliffSurface(ZoneSpec.FirstWorldRadius);
        return new PlacementWorld(ContentFixture.Content, ZoneFrame.World, ground, world.Physics?.World, world, new Alone(world));
    }

    // Tests on a ground of their own stand at 0°, 0° of a small world, written in that spot's tangent frame (x east, y up,
    // z south): these turn its points and directions into the world's frame, which every zone uses (docs/WHOLE_WORLD.md).
    private static Vector3 W(ZoneFrame spot, Vector3 local) => BuildingWorld_F(spot.ToBody(local));

    private static Vector3 WD(ZoneFrame spot, Vector3 local) => BuildingWorld_F(spot.ToBody(local) - spot.Origin);

    private static Vector3 L(ZoneFrame spot, Vector3 world) => spot.ToLocal(new Vector3D(world.X, world.Y, world.Z));

    private static PlacementContext Context(PieceDef piece, ulong player, Vector3 eye, IBuildingWallet? wallet) =>
        new(player, eye, ContentFixture.Content.Placement.Of(piece)!, wallet);

    /// <summary>
    /// The pipeline's verdict on a plan the building rules made: reach, sight and building rights are the pipeline's (one
    /// rule for everything placed), the rest the building's own.
    /// </summary>
    private static Outcome Verdict(PlacementWorld placing, PlacementPlan plan, ulong player, Vector3 eye, IBuildingWallet? wallet)
    {
        PlacementContext ctx = Context(plan.Piece, player, eye, wallet);
        PlacementCandidate c = Placer.OfPlan(placing, ctx, plan);
        c.Outcome = Placer.Check(placing, ctx, c);
        return c.Outcome;
    }

    /// <summary>The ghost of a piece where a player aims (the pipeline's socket solver and checks): its plan, or null.</summary>
    private static PlacementCandidate? Ghost(PlacementWorld placing, PieceDef piece, Vector3 eye, Vector3 aim, IBuildingWallet? wallet, int turns = 0) =>
        Placer.Plan(placing, Context(piece, Ada, eye, wallet), new PlacementAim(aim, 0, turns));

    /// <summary>A building's surroundings for placement: its characters, nothing else.</summary>
    private sealed class Alone(BuildingWorld world) : IPlacementSurroundings
    {
        public void ThingsNear(Vector3 point, float radius, List<PlacedThing> into)
        {
        }

        public void CharactersNear(Vector3 point, float radius, List<Capsule> into) => into.AddRange(world.Characters?.Invoke() ?? Array.Empty<Capsule>());

        public void ShipsNear(Vector3 point, float radius, List<PlacedShip> into)
        {
        }

        public int ThingsOn(in Hollow.Simulation.Targets.TargetRef support) => 0;

        public int ThingsOwned(ulong player, Hollow.Simulation.Content.DeployableDef def) => 0;
    }

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
        world.CheckBuilding(fifth, Ada, null);
        Piece spiral = On(world, First(world, x: 30), Centre, "stairs-spiral");
        Piece landing = On(world, spiral, Top, "floor");
        Fixtures.Report($"foundation {f.StabilityPercent}%, walls {walls.Select(w => w.StabilityPercent).Min()}%, ceiling {ceiling.StabilityPercent}%, " +
            $"reaching out {string.Join(" ", values.Select(v => v / 10.0 + "%"))}, then {Line(fifth.Outcome)}; spiral {spiral.StabilityPercent}%, landing {landing.StabilityPercent}%");
        Assert.Equal(1000, f.Stability, "foundation");
        Assert.True(walls.All(w => w.Stability == 1000), "walls keep full stability");
        Assert.Equal(900, ceiling.Stability, "floor on the walls");
        Assert.Equal("900,700,500,300,100", string.Join(",", values), "floors reaching out");
        Assert.Equal(OutcomeCode.Unstable, fifth.Outcome.Code, "the next floor can't stand");
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
        Assert.Equal(OutcomeCode.NotHigherGrade, world.Upgrade(Ada, wall, 1, wallet, eye).Code, "no downgrade");
        Assert.Equal(OutcomeCode.NotEnough, world.Upgrade(Ada, wall, 4, wallet, eye).Code, "no refined metal");

        Assert.True(world.Rotate(Ada, wall, eye, now: 599).Ok, "rotate inside the window");
        Assert.Equal(6, wall.Pose.Yaw, "the wall turned its soft side round");
        Assert.Equal(OutcomeCode.EditWindowOver, world.Rotate(Ada, wall, eye, now: 600.5).Code, "too late to rotate");
        Assert.Equal(OutcomeCode.CannotRotate, world.Rotate(Ada, f, eye, now: 1).Code, "foundations don't rotate");

        Piece side = On(world, f, Edge(1), "wall", now: 0);
        Assert.Equal(OutcomeCode.EditWindowOver, world.Demolish(Ada, side, eye, now: 600.001).Code, "too late to demolish");
        Piece other = On(world, f, Edge(2), "wall", now: 0);
        Assert.True(world.Demolish(Ada, other, eye, now: 600).Ok, "demolish at exactly 10 minutes");

        wall.Health = 500;
        world.Damage(wall, new DamageHit(new Vector3(0, 0, -1), Explosive: 100), now: 1000);
        Assert.Near(400, wall.Health, 1e-9, "damaged to 400");
        Outcome early = world.Repair(Ada, wall, wallet, eye, now: 1010);
        Assert.Equal(OutcomeCode.RecentlyDamaged, early.Code, "repair waits");
        Assert.Equal("Recently damaged: repair in 20 s", Line(early), "and says how long");
        int stone = wallet["stone"];
        Assert.True(world.Repair(Ada, wall, wallet, eye, now: 1030).Ok, "repair after 30 s");
        Assert.Near(500, wall.Health, 1e-9, "+100");
        Assert.Equal(12, stone - wallet["stone"], "12 stone (RustMVP)");
        Assert.Equal(OutcomeCode.FullHealth, world.Repair(Ada, wall, wallet, eye, now: 1031).Code, "nothing to repair");
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
        Assert.Equal(OutcomeCode.HasClaim, world.PlaceClaim(Ada, f, Vector3D.Zero, 0, out _).Code, "one per structure");
        Assert.Equal(Privilege.Allowed, world.PrivilegeAt(Ada, new Vector3(5, 0, 0)), "its owner may build");
        Assert.Equal(Privilege.Blocked, world.PrivilegeAt(Bo, new Vector3(5, 0, 0)), "a stranger may not");

        PlacementPlan wall = world.FromIntent(P("wall"), f, Edge(0), 0)!;
        var wallet = new Wallet { Endless = true };
        PlacementWorld placing = Placing(world);
        Outcome blocked = Verdict(placing, wall, Bo, new Vector3(0, 1.6f, 5), wallet);
        Assert.Equal(OutcomeCode.BuildingBlocked, blocked.Code, "Bo's wall is refused");
        Assert.Equal("Building blocked: someone else's tool cupboard", Line(blocked), "with the reason");
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
        Outcome allowed = Verdict(placing, wall, Bo, new Vector3(0, 1.6f, 5), wallet);
        Assert.True(allowed.Ok, "now Bo may build: " + allowed);
        Assert.Equal(OutcomeCode.NotAuthorised, world.ClearAuthorised(3, device!).Code, "a stranger can't clear the list");
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
        DoorDef wooden = Data.Door("wooden-door")!, doubleDoor = Data.Door("wooden-double-door")!;
        Assert.Equal(OutcomeCode.NeedsWallFrame, world.PlaceDoor(Ada, doubleDoor, doorway, 0, out _).Code, "a double door needs a wall frame");
        Assert.Equal(OutcomeCode.NotAnOpening, world.PlaceDoor(Ada, wooden, wall, 0, out _).Code, "not in a plain wall");
        Assert.True(world.PlaceDoor(Ada, wooden, doorway, 0, out Door? door).Ok, "door fitted");
        Assert.True(world.PlaceDoor(Ada, doubleDoor, frame, 1, out Door? wide).Ok, "double door fitted, turned round");
        Assert.Equal(OutcomeCode.HasDoor, world.PlaceDoor(Ada, wooden, doorway, 0, out _).Code, "one door per opening");

        (Vector3 _, Quaternion shut) = door!.Leaf(0, open: false);
        (Vector3 _, Quaternion open) = door.Leaf(0, open: true);
        double swing = 2 * Math.Acos(Math.Min(1, Math.Abs(Quaternion.Dot(shut, open)))) * 180 / Math.PI;
        Assert.Near(100, swing, 1e-3, "the leaf swings 100° (the kit's manifest)");

        Assert.True(world.Toggle(Bo, door).Ok && door.Open, "no lock: anyone opens it");
        Assert.True(world.Toggle(Bo, door).Ok && !door.Open, "and shuts it");

        Assert.True(world.AttachKeyLock(Ada, door, out KeyLock? key).Ok, "key lock fitted");
        Assert.True(world.SetLocked(Ada, door, true).Ok, "Ada locks it");
        Assert.Equal(OutcomeCode.Locked, world.Toggle(Bo, door).Code, "Bo is locked out");
        Assert.True(world.Toggle(Bo, door, new[] { key!.KeyCode }).Ok, "with the key Bo gets in");
        Assert.True(key.Locked, "and it stays locked");
        Assert.True(world.Toggle(Ada, door).Ok, "its owner needs no key");
        Assert.Equal(OutcomeCode.Locked, world.Toggle(Bo, door, new[] { key.KeyCode + 1 }).Code, "another lock's key doesn't fit");
        Assert.Equal(OutcomeCode.UnlockItFirst, world.RemoveLock(Ada, door).Code, "unlock before taking it off");
        world.SetLocked(Ada, door, false);
        Assert.True(world.RemoveLock(Ada, door).Ok, "key lock off");

        Assert.True(world.AttachCodeLock(Ada, door, out CodeLock? code).Ok, "code lock fitted");
        Assert.Equal(OutcomeCode.NoCode, world.SetLocked(Ada, door, true).Code, "no code yet");
        Assert.Equal(OutcomeCode.BadCode, world.SetCode(Ada, door, "12a4").Code, "four digits");
        Assert.True(world.SetCode(Ada, door, "1234").Ok && code!.Locked, "setting the code locks it");
        Assert.Equal(OutcomeCode.Locked, world.Toggle(Bo, door).Code, "Bo is locked out");
        Assert.Equal(OutcomeCode.WrongCode, world.EnterCode(Bo, door, "0000", now: 10).Code, "a wrong code");
        Outcome wait = world.EnterCode(Bo, door, "1234", now: 10.5);
        Assert.Equal(OutcomeCode.WaitForCode, wait.Code, "then a wait: " + Line(wait));
        Assert.True(world.EnterCode(Bo, door, "1234", now: 11).Ok, "the right code after the wait");
        Assert.True(world.Toggle(Bo, door).Ok, "Bo is authorised now");
        Assert.True(world.SetCode(Bo, door, "9999").Code == OutcomeCode.UnlockItFirst, "changing the code needs it unlocked");
        Assert.True(world.PickUpDoor(Ada, wide!).Code == OutcomeCode.OpenItFirst, "a shut door can't be picked up");
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

    }

    /// <summary>
    /// Placement with Jolt: the aim snaps a wall to the edge it points at, and every refusal comes with its reason: too
    /// far, out of sight, taken, floating, buried, into another piece, into the ground, a player in the way, no wood.
    /// </summary>
    [Test]
    public static void PlacementIsRefusedWithAReason()
    {
        var surface = new CliffSurface(ZoneSpec.FirstWorldRadius);
        ZoneFrame spot = ZoneFrame.At(surface, 0, 0);
        using var physics = new PhysicsWorld(surface, ZoneFrame.World);
        physics.EnsureGroundWithin(W(spot, Vector3.Zero), 96);
        ulong next = 0;
        using var world = new BuildingWorld(Data, physics, () => new EntityId(++next));
        var players = new List<Capsule>();
        world.Characters = () => players;
        var wallet = new Wallet { ["wood"] = 10_000 };
        var refusals = new List<string>();
        PlacementWorld placing = Placing(world, surface);
        void Expect(OutcomeCode reason, PlacementPlan? plan, Vector3 eye, IBuildingWallet? purse = null)
        {
            Assert.True(plan != null, $"a plan for {reason}");
            Outcome verdict = Verdict(placing, plan!, Ada, eye, purse ?? wallet);
            Assert.Equal(reason, verdict.Code, $"{plan!.Piece.Key}: {Line(verdict)}");
            Assert.True(Line(verdict).Length > 0, "a line for the player");
            refusals.Add(Line(verdict));
        }

        // A free foundation where the player looks, 3 m ahead on the flat.
        Vector3 eye = W(spot, new Vector3(6, 1.6f, 4));
        PlacementCandidate? ghost = Ghost(placing, P("foundation"), eye, WD(spot, new Vector3(0, -1.6f, -3)), wallet);
        PlacementPlan? first = ghost?.Detail as PlacementPlan;
        Assert.True(ghost is { Valid: true } && first is { Support: null }, "free foundation: " + ghost);
        Assert.True(world.Place(first!, Ada, wallet, now: 0, out Piece? f).Ok, "placed");
        Assert.Equal(10_000 - 50, wallet["wood"], "paid 50 wood");
        (Vector3 fPosition, _) = f!.ZoneTransform;
        Fixtures.Report($"free foundation at {L(spot, fPosition)}, its top {L(spot, fPosition).Y:0.000} m above the flat ground");
        Assert.InRange(Data.FoundationClearance, Data.FoundationClearance + 0.05, L(spot, fPosition).Y, "just above the ground");

        // Aiming at its east edge with a wall plan picks that edge.
        Vector3 east = f.Structure.Frame.ToZone(PieceGeometry.ToFrame(f.Pose, new Vector3D(1.5, 0.6, 0))) is var e ? BuildingWorld_F(e) : default;
        PlacementCandidate? wallGhost = Ghost(placing, P("wall"), eye, east - eye, wallet);
        PlacementPlan? wallPlan = wallGhost?.Detail as PlacementPlan;
        Assert.True(wallGhost is { Valid: true } && wallPlan!.Socket == Edge(1), "the wall snaps to the edge aimed at: " + wallGhost);
        world.Place(wallPlan!, Ada, wallet, now: 1, out Piece? eastWall);

        Expect(OutcomeCode.Occupied, world.FromIntent(P("wall"), f, Edge(1), 1), eye);
        Expect(OutcomeCode.OutOfReach, world.FromIntent(P("wall"), f, Edge(3), 0), fPosition + WD(spot, new Vector3(-20, 1.6f, 0)));
        Vector3 behind = fPosition + WD(spot, new Vector3(4.5f, 1.6f, 0));
        Expect(OutcomeCode.OutOfSight, world.FromIntent(P("wall"), f, Edge(3), 0), behind);

        // East of x = 12 the ground drops 3 m: a foundation there would float; one half a storey down on the flat would be buried.
        Piece f2 = world.Add(f.Structure, P("foundation"), 0, world.FromIntent(P("foundation"), f, Facing(f, -1, 0), 0)!.Pose, Ada, 2);
        Piece east1 = world.Add(f.Structure, P("foundation"), 0, world.FromIntent(P("foundation"), f, Facing(f, 1, 0), 0)!.Pose, Ada, 2);
        PlacementPlan? overCliff = world.FromIntent(P("foundation"), east1, Facing(east1, 1, 0), 0);
        Expect(OutcomeCode.Floating, overCliff, ZoneOf(overCliff!) + WD(spot, new Vector3(-3, 1.6f, 0)));
        PlacementPlan? sunk = world.FromIntent(P("foundation"), f2, new SocketId(SocketKind.EdgeDown, Facing(f2, 0, -1).Edge), 0);
        Expect(OutcomeCode.Buried, sunk, ZoneOf(sunk!) + WD(spot, new Vector3(0, 3, -3)));

        // A floor out from a foundation's edge lies in the ground; a foundation of another frame overlapping this one cuts into it.
        PlacementPlan? lowFloor = world.FromIntent(P("floor"), f2, Facing(f2, 0, 1), 0);
        Expect(OutcomeCode.HitsGround, lowFloor, ZoneOf(lowFloor!) + WD(spot, new Vector3(0, 1.6f, 3)));
        Vector3 skewAt = fPosition + WD(spot, new Vector3(1.5f, 0, -1.5f));
        var skew = new PlacementPlan
        {
            Piece = P("foundation"),
            Frame = BuildingFrame.At(new Vector3D(skewAt.X, skewAt.Y, skewAt.Z), spot.Up, 0.4),
            Pose = new Pose(Vector3D.Zero, 0),
        };
        Expect(OutcomeCode.HitsPiece, skew, eye);

        // Someone standing on the edge where a wall would go.
        PlacementPlan? southWall = world.FromIntent(P("wall"), f, Edge(0), 0);
        players.Add(new Capsule(ZoneOf(southWall!) + WD(spot, new Vector3(0, 0.05f, -0.2f)), WD(spot, Vector3.UnitY), 0.32f, 1.76f));
        Expect(OutcomeCode.PlayerInTheWay, southWall, eye);
        players.Clear();
        Expect(OutcomeCode.NotEnough, southWall, eye, new Wallet());
        Assert.Equal("Not enough wood", Line(Verdict(placing, southWall!, Ada, eye, new Wallet())), "names the material");
        Outcome fits = Verdict(placing, southWall!, Ada, eye, wallet);
        Assert.True(fits.Ok, "with the player gone and wood paid, the wall fits: " + fits);
        Assert.True(Ghost(placing, P("wall"), eye, WD(spot, new Vector3(0, 1, 0)), wallet) == null, "a wall aimed at the sky: nothing to place");
        Fixtures.Report(string.Join(" | ", refusals));
        Assert.Equal(eastWall!.Body != 0, true, "pieces have bodies");
    }

    /// <summary>A ground that rises <c>slope</c> metres a metre eastward (in the zone's frame at 0°, 0°), level north and south.</summary>
    private sealed class RampSurface : ISurface
    {
        private readonly ZoneFrame _frame;
        private readonly double _slope;

        public RampSurface(double radius, double slope)
        {
            Radius = radius;
            _slope = slope;
            _frame = new ZoneFrame(ZoneFrame.Direction(0, 0) * radius);
        }

        public double Radius { get; }

        public double SeaRadius => 0;

        public double HeightAt(Vector3D direction) => _frame.ToLocal(direction * Radius).X * _slope;

    }

    /// <summary>
    /// RustMVP's free foundation on a slope: aimed at the ground within 3.5 m it sits as low as the ground under its corners
    /// allows; aimed further it stays 3.5 m out and the aim's height there lifts its top, up to its depth above the lowest
    /// ground, always valid. The server's recomputation from the plan's point gives the same foundation.
    /// </summary>
    [Test]
    public static void FreeFoundationRisesWithTheAim()
    {
        const double slope = 0.08;
        var surface = new RampSurface(ZoneSpec.FirstWorldRadius, slope);
        ZoneFrame spot = ZoneFrame.At(surface, 0, 0);
        using var physics = new PhysicsWorld(surface, ZoneFrame.World);
        physics.EnsureGroundWithin(W(spot, Vector3.Zero), 96);
        ulong next = 0;
        using var world = new BuildingWorld(Data, physics, () => new EntityId(++next));
        var wallet = new Wallet { ["wood"] = 10_000 };
        // Standing where the ground is at zero height, looking north along the level line of the slope.
        Vector3 eye = W(spot, new Vector3(0, 1.6f, 4));
        double rise = slope * Data.Width / 2, clearance = Data.FoundationClearance, depth = Data.FoundationDepth;
        double lowest = rise + clearance, highest = depth - rise;
        PlacementWorld placing = Placing(world, surface);
        double Top(Vector3 aim, string what)
        {
            PlacementCandidate? ghost = Ghost(placing, P("foundation"), eye, WD(spot, aim), wallet);
            PlacementPlan? plan = ghost?.Detail as PlacementPlan;
            Assert.True(ghost is { Valid: true } && plan is { Support: null }, $"{what}: a valid free foundation, not {ghost}");
            PlacementPlan again = world.FoundationAt(P("foundation"), plan!.Point, plan.HeadingSteps * BuildingWorld.HeadingStep)!;
            Assert.True(again.Frame.Equals(plan.Frame), $"{what}: the server's foundation from the plan's point is the ghost's");
            double top = L(spot, BuildingWorld_F(plan.Frame.Origin)).Y;
            Fixtures.Report($"{what}: top {top:0.000} m over the ground at its centre");
            return top;
        }

        // At the ground 3 m ahead: as low as the uphill corners allow.
        Assert.InRange(lowest, lowest + 0.03, Top(new Vector3(0, -1.6f, -3), "aimed at the ground"), "the lowest top the slope allows");
        // A little higher: 3.5 m out, the aim 0.5 m over the ground there.
        Assert.Near(0.5 + clearance, Top(new Vector3(0, -1.1f, -3.5f), "aimed a little higher"), 0.01, "the top follows the aim");
        // Higher still, and at the sky: held at the depth over the downhill corners.
        Assert.InRange(highest - 0.03, highest, Top(new Vector3(0, -0.3f, -3.5f), "aimed higher"), "the top stops at the foundation's depth");
        Assert.InRange(highest - 0.03, highest, Top(new Vector3(0, 0.5f, -3.5f), "aimed at the sky"), "the same at the sky");
    }

    private static Vector3 BuildingWorld_F(Vector3D v) => new((float)v.X, (float)v.Y, (float)v.Z);
}

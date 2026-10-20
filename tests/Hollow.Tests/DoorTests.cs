using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Raid;

namespace Hollow.Tests;

/// <summary>docs/DOORS.md: how doors hang and swing, and the airlocks the layouts make possible.</summary>
public static class DoorTests
{
    private const ulong Ada = 1;

    private static BuildingData Data => ContentFixture.Content.Building;

    private static PieceDef P(string key) => Data.Piece(key) ?? throw new TestFailure("no piece " + key);

    private static BuildingWorld PureWorld()
    {
        ulong next = 0;
        return new BuildingWorld(Data, null, () => new EntityId(++next));
    }

    private static Piece On(BuildingWorld world, Piece parent, int edge, string key, int grade = 2)
    {
        PlacementPlan plan = world.FromIntent(P(key), parent, new SocketId(SocketKind.Edge, edge), 0)
            ?? throw new TestFailure($"{key} doesn't fit {parent.Def.Key} edge {edge}");
        return world.Add(plan.Structure!, plan.Piece, grade, plan.Pose, Ada, 0);
    }

    private static Piece Slab(BuildingWorld world, string key = "foundation") =>
        world.AddFirst(BuildingFrame.At(Vector3D.Zero, Vector3D.UnitY, 0.4), P(key), 2, Ada, 0);

    private static Door Fit(BuildingWorld world, string door, Piece frame, int layout)
    {
        Assert.True(world.PlaceDoor(Ada, Data.Door(door)!, frame, layout, out Door? fitted).Ok, $"{door} fits layout {layout}");
        return fitted!;
    }

    // A point in the frame piece's own metres (x right seen from outside, y up, z outside), in the zone and back.
    private static Vector3 ToZone(Piece frame, Vector3 local)
    {
        (Vector3 position, Quaternion rotation) = frame.ZoneTransform;
        return position + Vector3.Transform(local, rotation);
    }

    private static Vector3 ToFrame(Piece frame, Vector3 zone)
    {
        (Vector3 position, Quaternion rotation) = frame.ZoneTransform;
        return Vector3.Transform(zone - position, Quaternion.Inverse(rotation));
    }

    // A point of a leaf (in the leaf's own metres) in the zone where the leaf is at a progress.
    private static Vector3 OnLeaf(Door door, int index, float progress, Vector3 local)
    {
        (Vector3 position, Quaternion rotation) = door.LeafAt(index, progress);
        return position + Vector3.Transform(local, rotation);
    }

    [Test]
    public static void ASingleDoorHasFourLayoutsAndEachHangsOnItsPinAndSwingsToItsSide()
    {
        DoorDef wooden = Data.Door("wooden-door")!;
        Assert.Equal(4, wooden.Layouts, "hinge side and swing side, each chosen");
        Assert.Equal(2, Data.Door("wooden-double-door")!.Layouts, "a double door chooses its swing side");
        Assert.Equal(1, Data.Door("garage-door")!.Layouts, "the garage door chooses nothing");
        Assert.Equal(1, Data.Door("metal-window-bars")!.Layouts, "an insert has no layout");
        for (int layout = 0; layout < 4; layout++)
        {
            using BuildingWorld world = PureWorld();
            Piece slab = Slab(world);
            Piece doorway = On(world, slab, 0, "doorway");
            Door door = Fit(world, "wooden-door", doorway, layout);
            bool right = (layout & 1) != 0, outward = (layout & 2) != 0;
            Assert.True(door.HingeRight == right && door.SwingOut == outward, $"layout {layout}: the bits");
            LeafHinging hinging = wooden.Hinging(0, layout);
            Assert.True(hinging.Pin.X * (right ? 1 : -1) > 0.5f, $"layout {layout}: the pin by the {(right ? "right" : "left")} jamb");
            Assert.Equal(right, door.LeafMesh(0).Contains("Right"), $"layout {layout}: the right-hinged leaf is the mirrored mesh");
            // The pin does not move as the leaf opens (the leaf's own point at the pin).
            Vector3 pin = -hinging.Offset;
            for (float progress = 0.25f; progress <= 1f; progress += 0.25f)
            {
                Assert.Near(0, Vector3.Distance(OnLeaf(door, 0, 0, pin), OnLeaf(door, 0, progress, pin)), 1e-4, $"layout {layout}: the pin holds at {progress}");
            }
            // The free edge swings out of the opening's plane to the layout's side, a leaf's width from the wall.
            Vector3 free = ToFrame(doorway, OnLeaf(door, 0, 1, new Vector3(Data.Door("wooden-door")!.Leaves[0].Size.X * (right ? -1 : 1), 1, 0)));
            Assert.True(outward ? free.Z > 1.0f : free.Z < -1.0f, $"layout {layout}: the open leaf lies on the {(outward ? "outside" : "inside")} (z {free.Z:0.00})");
            Vector3 shut = ToFrame(doorway, OnLeaf(door, 0, 0, new Vector3(Data.Door("wooden-door")!.Leaves[0].Size.X * (right ? -1 : 1), 1, 0)));
            Assert.Near(0, shut.Z, 0.01, $"layout {layout}: shut, the free edge is in the wall's plane");
            // Its solid part hangs the same way: from the pin toward the free edge.
            ConvexPart[] parts = BuildingWorld.LeafParts(door, 0);
            Assert.True(right ? parts[0].Max.X <= 0.001f && parts[0].Min.X < -1f : parts[0].Min.X >= -0.001f && parts[0].Max.X > 1f, $"layout {layout}: the body is on the hinge's side");
        }
    }

    [Test]
    public static void ADoubleDoorSwingsBothLeavesTheSameWayAndTheLayoutsStepOneChoiceAtATime()
    {
        foreach (int layout in new[] { 0, 2 })
        {
            using BuildingWorld world = PureWorld();
            Piece frame = On(world, Slab(world), 0, "wall-frame");
            Door door = Fit(world, "wooden-double-door", frame, layout);
            foreach (int leaf in new[] { 0, 1 })
            {
                Vector3 free = ToFrame(frame, OnLeaf(door, leaf, 1, new Vector3(Data.Door("wooden-double-door")!.Leaves[leaf].Size.X, 1, 0)));
                Assert.True(layout == 2 ? free.Z > 1.0f : free.Z < -1.0f, $"layout {layout}, leaf {leaf}: swings {(layout == 2 ? "out" : "in")} (z {free.Z:0.00})");
            }
            Assert.Equal(layout, door.Layout, "a double door keeps its swing bit");
        }
        using (BuildingWorld world = PureWorld())
        {
            Piece frame = On(world, Slab(world), 0, "wall-frame");
            Assert.Equal(0, Fit(world, "wooden-double-door", frame, 1).Layout, "a bit the door does not choose is dropped");
        }
        DoorDef single = Data.Door("wooden-door")!, wide = Data.Door("wooden-double-door")!, garage = Data.Door("garage-door")!;
        Assert.Equal(0, single.DefaultLayout(fromOutside: true), "from outside: hinged on the viewer's left, swinging in");
        Assert.Equal(3, single.DefaultLayout(fromOutside: false), "from inside: hinged on the viewer's left (the right of the frame), swinging out");
        Assert.Equal(2, wide.DefaultLayout(fromOutside: false), "a double door swings away from the viewer");
        Assert.Equal(0, garage.DefaultLayout(fromOutside: false), "the garage door has the one layout");
        var seen = new List<int>();
        int at = 0;
        for (int press = 0; press < 5; press++)
        {
            seen.Add(at);
            int next = single.Turned(at, 1);
            Assert.Equal(1, Convert.ToString(next ^ at, 2).Count(c => c == '1'), $"press {press}: one choice changes");
            at = next;
        }
        Assert.True(seen.Take(4).Distinct().Count() == 4 && seen[4] == seen[0], "four presses of R visit every layout and come back");
        Assert.Equal(1, wide.Turned(0, 1) >> 1, "a double door's R swaps in and out");
        Assert.Equal(0, garage.Turned(0, 3), "the garage door stays");
    }

    // ---------------------------------------------------------------- airlocks (DOORS.md §7)

    // A solid in the zone: convex parts and the pose they stand at.
    private readonly record struct Solid(ConvexPart[] Parts, Vector3 Position, Quaternion Inverse);

    // Every wall and every leaf where it is now (an open door's leaf open).
    private static List<Solid> Solids(BuildingWorld world)
    {
        var solids = new List<Solid>();
        foreach (Piece piece in world.AllPieces.Where(p => p.Def.Family == PieceFamily.Wall))
        {
            (Vector3 position, Quaternion rotation) = piece.ZoneTransform;
            solids.Add(new Solid(PieceShapes.For(Data, piece.Def), position, Quaternion.Inverse(rotation)));
        }
        foreach (Door door in world.Doors)
        {
            for (int i = 0; i < door.Def.Leaves.Count; i++)
            {
                (Vector3 position, Quaternion rotation) = door.Leaf(i, door.Open);
                solids.Add(new Solid(BuildingWorld.LeafParts(door, i), position, Quaternion.Inverse(rotation)));
            }
        }
        return solids;
    }

    // Whether a standing character (the movement tuning's capsule) could be at a point on the floor (y 0 is the foundations'
    // top) without reaching into a solid: the capsule's axis sampled as the building's own check does.
    private static bool Free(List<Solid> solids, Vector3 feet)
    {
        const float radius = 0.32f, height = 1.76f;
        for (float up = radius; up <= height - radius + 1e-3f; up += 0.2f)
        {
            Vector3 point = feet + new Vector3(0, up, 0);
            foreach (Solid solid in solids)
            {
                Vector3 local = Vector3.Transform(point - solid.Position, solid.Inverse);
                foreach (ConvexPart part in solid.Parts)
                {
                    if (local.X < part.Min.X - radius || local.X > part.Max.X + radius || local.Y < part.Min.Y - radius ||
                        local.Y > part.Max.Y + radius || local.Z < part.Min.Z - radius || local.Z > part.Max.Z + radius)
                    {
                        continue;
                    }
                    if (part.Outside(local) < radius)
                    {
                        return false;
                    }
                }
            }
        }
        return true;
    }

    // Whether a character can walk from outside one doorway to outside another through the room between them: a flood over
    // 5 cm cells of the slab's footprint (widened by 35 cm, to take in the thresholds) and of a 1.3 m corridor outside each
    // doorway, from the middle of one corridor's end to the other's.
    private static bool Walkable(BuildingWorld world, Piece slab, Piece doorwayA, Piece doorwayB)
    {
        Vector3 from = ToZone(doorwayA, new Vector3(0, 0, 1.2f)), to = ToZone(doorwayB, new Vector3(0, 0, 1.2f));
        bool Corridor(Piece doorway, Vector3 p)
        {
            Vector3 local = ToFrame(doorway, p);
            return MathF.Abs(local.X) <= 0.8f && local.Z >= 0 && local.Z <= 1.3f;
        }
        const float step = 0.05f, reach = 3f;
        (Vector3 centre, Quaternion turn) = slab.ZoneTransform;
        (double X, double Z)[] polygon = PieceShapes.Polygon(slab.Def.Shape, Data.Width);
        float inradius = slab.Def.Shape == PieceShape.Triangle ? (float)(Data.Width * Math.Sqrt(3) / 6) : (float)(Data.Width / 2);
        float scale = 1 + 0.35f / inradius;
        Vector3[] corners = polygon.Select(c => centre + Vector3.Transform(new Vector3((float)c.X * scale, 0, (float)c.Z * scale), turn)).ToArray();
        // On the same side of every edge as the footprint's centre.
        bool Inside(Vector3 p)
        {
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 e = corners[(i + 1) % corners.Length] - corners[i];
                float side = e.X * (p.Z - corners[i].Z) - e.Z * (p.X - corners[i].X);
                float inner = e.X * (centre.Z - corners[i].Z) - e.Z * (centre.X - corners[i].X);
                if (side * inner < 0)
                {
                    return Corridor(doorwayA, p) || Corridor(doorwayB, p);
                }
            }
            return true;
        }
        List<Solid> solids = Solids(world);
        int n = (int)(2 * reach / step) + 1;
        (int X, int Z) Cell(Vector3 p) => ((int)MathF.Round((p.X - centre.X + reach) / step), (int)MathF.Round((p.Z - centre.Z + reach) / step));
        Vector3 At((int X, int Z) c) => new(c.X * step - reach + centre.X, 0, c.Z * step - reach + centre.Z);
        var seen = new bool[n, n];
        var queue = new Queue<(int X, int Z)>();
        (int X, int Z) start = Cell(from), goal = Cell(to);
        if (!Free(solids, At(start)) || !Free(solids, At(goal)))
        {
            // A leaf standing in a doorway's threshold shuts the way as surely as a wall.
            return false;
        }
        seen[start.X, start.Z] = true;
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            (int x, int z) = queue.Dequeue();
            if (Math.Abs(x - goal.X) <= 1 && Math.Abs(z - goal.Z) <= 1)
            {
                return true;
            }
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    (int X, int Z) next = (x + dx, z + dz);
                    if ((dx == 0 && dz == 0) || next.X < 0 || next.Z < 0 || next.X >= n || next.Z >= n || seen[next.X, next.Z])
                    {
                        continue;
                    }
                    seen[next.X, next.Z] = true;
                    Vector3 at = At(next);
                    if (Inside(at) && Free(solids, at))
                    {
                        queue.Enqueue(next);
                    }
                }
            }
        }
        return false;
    }

    // A triangle foundation with a doorway on each of two edges, a wall on the third, a wooden door in each doorway, open or
    // shut; and the two doorways.
    // A layout of -1 leaves that doorway empty.
    private static (BuildingWorld World, Piece Slab, Piece A, Piece B) Triangle(int layoutA, int layoutB, bool open)
    {
        BuildingWorld world = PureWorld();
        Piece slab = Slab(world, "foundation-triangle");
        Piece a = On(world, slab, 0, "doorway"), b = On(world, slab, 1, "doorway");
        On(world, slab, 2, "wall");
        foreach ((Piece doorway, int layout) in new[] { (a, layoutA), (b, layoutB) })
        {
            if (layout >= 0)
            {
                Door door = Fit(world, "wooden-door", doorway, layout);
                Assert.True(!open || world.Toggle(Ada, door).Ok, "the door opens");
            }
        }
        return (world, slab, a, b);
    }

    // A square foundation with a wall frame on edge 0 (a wooden double door) and a doorway on edge 1 (a wooden door), walls on
    // the others; -1 leaves a frame empty.
    private static (BuildingWorld World, Piece Slab, Piece Wide, Piece Single) Room(int swingWide, int layoutSingle)
    {
        BuildingWorld world = PureWorld();
        Piece slab = Slab(world);
        Piece wide = On(world, slab, 0, "wall-frame"), single = On(world, slab, 1, "doorway");
        On(world, slab, 2, "wall");
        On(world, slab, 3, "wall");
        if (swingWide >= 0)
        {
            Assert.True(world.Toggle(Ada, Fit(world, "wooden-double-door", wide, swingWide)).Ok, "the double door opens");
        }
        if (layoutSingle >= 0)
        {
            Assert.True(world.Toggle(Ada, Fit(world, "wooden-door", single, layoutSingle)).Ok, "the single door opens");
        }
        return (world, slab, wide, single);
    }

    private static bool RoomWalk(int swingWide, int layoutSingle)
    {
        (BuildingWorld world, Piece slab, Piece wide, Piece single) = Room(swingWide, layoutSingle);
        using (world)
        {
            return Walkable(world, slab, wide, single);
        }
    }

    // Whether a walk through the triangle from one doorway's outside to the other's is free.
    private static bool TriangleWalk(int layoutA, int layoutB)
    {
        (BuildingWorld world, Piece slab, Piece a, Piece b) = Triangle(layoutA, layoutB, open: true);
        using (world)
        {
            return Walkable(world, slab, a, b);
        }
    }

    [Test]
    public static void ATriangleAirlockClosesTheWalkWithBothDoorsOpenAndItsMirrorLayoutDoesNot()
    {
        // Two single doors on a triangle's edges 0 and 1, which meet at a corner. Seen from outside, +X of edge 0's door and
        // −X of edge 1's door point to that corner: a door hinged there (layout 1 for the first, 0 for the second) swinging
        // into the room puts its leaf across the walk to the other doorway.
        Assert.True(!TriangleWalk(1, 2), "the outer door hinged at the shared corner and swinging in, the inner swinging out: closed");
        Assert.True(!TriangleWalk(1, 2) && !TriangleWalk(1, -1), "the outer door alone shuts the way to the other doorway");
        Assert.True(TriangleWalk(-1, 2), "the inner door swinging out never does");
        Assert.True(!TriangleWalk(0, 0) && !TriangleWalk(-1, 0), "likewise the second door hinged at the corner, swinging in");
        Assert.True(TriangleWalk(0, 3), "hinged at the far sides both leaves lie against the walls: the walk is free");
        Assert.True(TriangleWalk(0, 1) && TriangleWalk(2, 2) && TriangleWalk(3, 3), "so do the layouts that swing out or hug the far wall");
        // The test itself: shut, even the free layout cannot be walked.
        (BuildingWorld shut, Piece slab, Piece a, Piece b) = Triangle(0, 3, open: false);
        using (shut)
        {
            Assert.True(!Walkable(shut, slab, a, b), "shut doors stop the walk too");
        }
    }

    // Whether any open leaf of one door passes through any open leaf of another (doors ignore each other, so two leaves that
    // cross would be drawn through one another): the leaves' volumes sampled every 4 cm against the other's convex parts.
    private static bool LeavesCross(Door a, Door b)
    {
        for (int i = 0; i < a.Def.Leaves.Count; i++)
        {
            for (int j = 0; j < b.Def.Leaves.Count; j++)
            {
                (Vector3 pa, Quaternion ra) = a.Leaf(i, a.Open);
                (Vector3 pb, Quaternion rb) = b.Leaf(j, b.Open);
                Quaternion inverseA = Quaternion.Inverse(ra);
                foreach (ConvexPart theirs in BuildingWorld.LeafParts(b, j))
                {
                    for (float x = theirs.Min.X; x <= theirs.Max.X + 1e-4f; x += 0.04f)
                    {
                        for (float y = theirs.Min.Y; y <= theirs.Max.Y + 1e-4f; y += 0.04f)
                        {
                            for (float z = theirs.Min.Z; z <= theirs.Max.Z + 1e-4f; z += 0.02f)
                            {
                                Vector3 inA = Vector3.Transform(pb + Vector3.Transform(new Vector3(x, y, z), rb) - pa, inverseA);
                                foreach (ConvexPart mine in BuildingWorld.LeafParts(a, i))
                                {
                                    if (mine.Outside(inA) < 0)
                                    {
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        return false;
    }

    [Test]
    public static void AnAirlockWithBothDoorsSwingingIntoTheRoomClosesTheWalkWithoutTheLeavesCrossing()
    {
        // The owner's airlock (DOORS.md §7): the outer door swings in and so does the inner one ("the inner door opens toward the
        // outside"): both open leaves end up in the small room, one hung against a wall, the other across the walk. Doors ignore
        // each other, so two hinge sides that put the leaves through one another are no airlock, whatever they block.
        foreach ((int a, int b, bool walkable, bool cross) in new[] { (0, 0, false, false), (1, 1, false, false), (1, 0, false, true), (0, 1, true, false) })
        {
            (BuildingWorld world, Piece slab, Piece pa, Piece pb) = Triangle(a, b, open: true);
            using (world)
            {
                Assert.Equal(walkable, Walkable(world, slab, pa, pb), $"layouts {a}/{b}, both open and swinging in: the walk is " + (walkable ? "free" : "closed"));
                Assert.Equal(cross, LeavesCross(pa.Door!, pb.Door!), $"layouts {a}/{b}: the open leaves " + (cross ? "cross" : "clear one another"));
                Assert.True(pa.Door!.Layout >> 1 == 0 && pb.Door!.Layout >> 1 == 0, $"layouts {a}/{b} both swing in (bit 1 is the swing)");
            }
        }
    }

    [Test]
    public static void ADoubleDoorOpenInwardAndASingleDoorHingedOnTheRightMakeAnAirlockOnlyTogether()
    {
        Assert.True(!RoomWalk(0, 1) && RoomWalk(0, -1) && RoomWalk(-1, 1),
            "a double door opening in and a single door hinged on the right, swinging in: each alone leaves the way free, together they close it");
        Assert.True(!RoomWalk(0, 3) && RoomWalk(-1, 3), "the single door swinging out, hinged on the right, does too");
        Assert.True(RoomWalk(0, 0) && RoomWalk(0, 2), "hinged on the left it does not");
        Assert.True(RoomWalk(2, 0) && RoomWalk(2, 1) && RoomWalk(2, 2) && RoomWalk(2, 3), "a double door swinging out leaves every layout free");
        // Doors ignore each other, so the airlock's two open leaves must not pass through one another.
        foreach (int single in new[] { 1, 3 })
        {
            (BuildingWorld world, _, Piece wide, Piece door) = Room(0, single);
            using (world)
            {
                Assert.True(!LeavesCross(wide.Door!, door.Door!), $"the double door in and the single door at layout {single}: the open leaves clear one another");
            }
        }
    }

    // A piece on a slab's edge as a player would have it (the placement's own plan).
    private static Piece OnEdge(BuildingWorld world, Piece parent, int edge, string key, double now)
    {
        PlacementPlan plan = world.FromIntent(P(key), parent, new SocketId(SocketKind.Edge, edge), 0) ?? throw new TestFailure($"{key} doesn't fit");
        return world.Add(plan.Structure!, plan.Piece, 2, plan.Pose, Ada, now);
    }

    [Test]
    public static void ACharacterWalkingToTheOtherDoorIsStoppedByTheAirlockAndWalksThroughItsMirror()
    {
        foreach ((int a, int b, bool through) in new[] { (1, 2, false), (0, 3, true), (0, 0, false), (1, 1, false), (0, 1, true) })
        {
            using var rig = new GameRig();
            Zone zone = rig.Zone;
            BuildingWorld world = zone.Building().World!;
            BuildingData data = world.Data;
            Vector3 at = zone.GroundAt(8, 8);
            zone.Physics.EnsureGround(at);
            double now = zone.Building().BuildingNow;
            PieceDef triangle = data.Piece("foundation-triangle")!;
            Piece slab = world.AddFirst(TestBases.FrameOver(zone, data, triangle, at, 0), triangle, 2, Ada, now);
            Piece doorwayA = OnEdge(world, slab, 0, "doorway", now), doorwayB = OnEdge(world, slab, 1, "doorway", now);
            OnEdge(world, slab, 2, "wall", now);
            DoorDef wooden = data.Door("wooden-door")!;
            Assert.True(world.RestoreDoor(zone.NextId(), wooden, doorwayA, a, Ada, open: true, wooden.Health) != null, "the outer door, open");
            Assert.True(world.RestoreDoor(zone.NextId(), wooden, doorwayB, b, Ada, open: true, wooden.Health) != null, "the inner door, open");

            // The walk: in at the outer door's doorway, across the room, out at the other.
            Vector3[] route =
            {
                ToZone(doorwayA, new Vector3(0, 0, 1.8f)), ToZone(doorwayA, new Vector3(0, 0, 0.6f)), ToZone(doorwayA, new Vector3(0, 0, -0.6f)),
                ToZone(slab, Vector3.Zero), ToZone(doorwayB, new Vector3(0, 0, -0.6f)), ToZone(doorwayB, new Vector3(0, 0, 0.6f)),
                ToZone(doorwayB, new Vector3(0, 0, 1.8f)),
            };
            Entity walker = zone.Players().AddPlayer(zone.NextId(), "Walker", zone.NewCharacter(zone.Settle(route[0])));
            int next = 1;
            for (int tick = 0; tick < 1200 && next < route.Length; tick++)
            {
                rig.Look(walker, route[next]);
                rig.Ch(walker).Input.Forward = 1;
                rig.Step();
                Vector3 feet = rig.Ch(walker).State.Position;
                Vector3 horizontal = (route[next] - feet) - zone.Frame.UpAt(feet) * Vector3.Dot(route[next] - feet, zone.Frame.UpAt(feet));
                if (horizontal.Length() < 0.3f)
                {
                    next++;
                }
            }
            Assert.Equal(through, next == route.Length, $"layouts {a}/{b}: a character " + (through ? "walks through" : "is stopped") + $" (reached waypoint {next} of {route.Length})");
        }
    }

    // ---------------------------------------------------------------- motion (DOORS.md §4, §5)

    // Whether a standing character's capsule at a point (feet) reaches into a leaf at the pose of a progress.
    private static bool Touches(Door door, int leaf, float progress, Vector3 feet, Vector3 up)
    {
        const float radius = 0.32f, height = 1.76f;
        (Vector3 position, Quaternion rotation) = door.LeafAt(leaf, progress);
        Quaternion inverse = Quaternion.Inverse(rotation);
        for (float along = radius; along <= height - radius + 1e-3f; along += 0.2f)
        {
            Vector3 local = Vector3.Transform(feet + up * along - position, inverse);
            foreach (ConvexPart part in BuildingWorld.LeafParts(door, leaf))
            {
                if (part.Outside(local) < radius)
                {
                    return true;
                }
            }
        }
        return false;
    }

    // A hut's doorway (no door in it), on the rig's ground, and the door the test fits: open, in the layout.
    private static (TestBases.Hut Hut, Door Door) HutWithDoor(GameRig rig, string door, int layout, bool open)
    {
        TestBases.Hut hut = Raids.Hut(rig, 0, 12, door: null);
        BuildingWorld world = rig.Zone.Building().World!;
        DoorDef def = rig.C.Building.Door(door)!;
        Door fitted = world.RestoreDoor(rig.Zone.NextId(), def, hut.Doorway, layout, hut.Owner, open, def.Health) ?? throw new TestFailure("no door");
        return (hut, fitted);
    }

    [Test]
    public static void ADoorTakesItsTravelTimeAndItsBodyIsWhereItsMotionPutsItEveryTick()
    {
        using var rig = new GameRig();
        Zone zone = rig.Zone;
        (TestBases.Hut hut, Door door) = HutWithDoor(rig, "wooden-door", 1, open: false);
        BuildingWorld world = zone.Building().World!;
        Assert.Equal(30, (int)Math.Round(1.0 / door.Def.ProgressPerTick), "a leaf swings in a second: 30 ticks");
        Assert.True(world.Toggle(1, door).Ok && door.Open && door.Moving == 1, "asked to open: it begins to move");
        long began = door.Since;
        Assert.Equal(zone.Tick, began, "from the tick it was asked at");
        for (int tick = 1; tick <= 34; tick++)
        {
            rig.Step();
            float progress = door.ProgressAt(zone.Tick);
            float expected = Math.Min(1f, (float)tick / 30);
            Assert.Near(expected, progress, 1e-4, $"tick {tick}: progress");
            // The leaf's body is where the pose of that progress is: a ray at the leaf's middle from its +Z face hits the body at the core's face.
            (Vector3 position, Quaternion rotation) = door.LeafAt(0, progress);
            Vector3 middle = position + Vector3.Transform(new Vector3(door.HingeRight ? -0.6f : 0.6f, 1.0f, 0f), rotation);
            Vector3 out_ = Vector3.Transform(Vector3.UnitZ, rotation);
            bool hit = world.Physics!.Ray(middle + out_ * 0.5f, -out_, 1f, out RayHit ray, buildingOnly: true);
            Assert.True(hit && world.Physics.OwnerOf(ray.Body) is DoorLeaf, $"tick {tick}: the leaf's body is there");
            Assert.Near(0.5 - door.Def.Leaves[0].Size.Z / 2, ray.Distance, 0.02, $"tick {tick}: its face");
        }
        Assert.True(door.Moving == 0 && door.Progress == 1f && door.From == 1f, "arrived, at rest");
        // A second press mid-way turns it round from where it is.
        Assert.True(world.Toggle(1, door).Ok, "shut");
        rig.Step(10);
        float midway = door.Progress;
        Assert.Near(1f - 10f / 30, midway, 1e-3, "shutting, ten ticks on");
        Assert.True(world.Toggle(1, door).Ok && door.Moving == 1, "asked to open again mid-way: nothing refuses it");
        rig.Step(3);
        Assert.Near(midway + 3f / 30, door.Progress, 1e-3, "it went back the other way from where it was");
    }

    [Test]
    public static void ADoorClosingOnAStandingPlayerShovesThemClearAndNothingStopsIt()
    {
        using var rig = new GameRig();
        Zone zone = rig.Zone;
        (TestBases.Hut hut, Door door) = HutWithDoor(rig, "wooden-door", 0, open: true);
        BuildingWorld world = zone.Building().World!;
        zone.Step();
        Entity ada = rig.Add("Ada", 0, 0);
        // Inside, in the path of the leaf as it closes (it lies along the inside of the wall, hinged on the left).
        Vector3 spot = ToZone(hut.Doorway, new Vector3(0.2f, 0f, -0.45f));
        Raids.Stand(rig, ada, zone.Settle(spot));
        rig.Step(3);
        Vector3 up = zone.Frame.UpAt(rig.Ch(ada).State.Position);
        Assert.True(!Touches(door, 0, 1f, rig.Ch(ada).State.Position, up), "the open leaf is clear of her");
        bool inItsSweep = false;
        for (float progress = 0.05f; progress < 1f; progress += 0.05f)
        {
            inItsSweep |= Touches(door, 0, progress, rig.Ch(ada).State.Position, up);
        }
        Assert.True(inItsSweep, "she stands in the leaf's sweep as it closes");
        Assert.True(world.Toggle(1, door).Ok, "asked to shut with her in its way: nothing refuses it (it used to: swing blocked)");
        // What the character controller's penetration recovery does, found by running it: the leaf sweeps her ahead of it, on the side
        // it is moving to (its +Z face when it shuts), and she is never on its far side while she is beside it: the leaf does not
        // pass through her and she is not pushed through it.
        LeafDef leaf = door.Def.Leaves[0];
        float deepest = float.MaxValue;
        for (int tick = 1; tick <= 45; tick++)
        {
            rig.Step();
            (Vector3 at, Quaternion turned) = door.LeafAt(0, door.Progress);
            Vector3 local = Vector3.Transform(rig.Ch(ada).State.Position - at, Quaternion.Inverse(turned));
            if (local.X >= 0 && local.X <= leaf.Size.X && door.Progress < 0.9f)
            {
                deepest = Math.Min(deepest, local.Z);
                Assert.True(local.Z >= -leaf.Size.Z / 2, $"tick {tick}: she is on the leaf's leading side, not behind or inside it (leaf z {local.Z:0.00})");
            }
        }
        Assert.True(deepest != float.MaxValue, "she was beside the leaf while it closed");
        Assert.True(door.Moving == 0 && door.Progress == 0f && !door.Open, "it shut, all the way");
        Vector3 feet = rig.Ch(ada).State.Position;
        Assert.True(!Touches(door, 0, 0f, feet, zone.Frame.UpAt(feet)), "she is outside the leaf, shoved clear");
        Assert.True(ToFrame(hut.Doorway, feet).Z > 0.04f, $"ahead of the leaf, which swept her out through the doorway (z {ToFrame(hut.Doorway, feet).Z:0.00})");
        Assert.True(!rig.Ch(ada).State.IsDead, "and none the worse");
    }

    [Test]
    public static void ADoorIsNeverRefusedForSightWhereItsDoorwayShowsAndStillNotThroughAWall()
    {
        // The owner's report: closing an open door from a bit far away answered "Something is in the way". That was the interaction's
        // line of sight to the door's nearest point, which for an open leaf lies behind its own jamb as seen from anywhere but the
        // doorway's axis. A door is now seen when any of its leaves' points or its doorway is (TargetSystem.Reach). Open and shut,
        // every layout: from a ring of eyes round the doorway (1.6 m up, 1.5 to 3.3 m out, up to 75 degrees off its axis) and from
        // inside the hut, the answer is Done or, beyond the reach, OutOfReach, never OutOfSight.
        foreach (bool open in new[] { true, false })
        {
            foreach (int layout in new[] { 0, 1, 2, 3 })
            {
                using var rig = new GameRig();
                Zone zone = rig.Zone;
                (TestBases.Hut hut, Door door) = HutWithDoor(rig, "wooden-door", layout, open);
                rig.Step(2);
                var eyes = new List<(string Where, Vector3 Eye)>();
                foreach (float radius in new[] { 1.5f, 2.5f, 3.3f })
                {
                    for (int degrees = -75; degrees <= 75; degrees += 15)
                    {
                        float a = degrees * MathF.PI / 180;
                        eyes.Add(($"outside {radius} m at {degrees} degrees", ToZone(hut.Doorway, new Vector3(MathF.Sin(a) * radius, 1.6f, MathF.Cos(a) * radius))));
                    }
                }
                foreach ((float x, float z) in new[] { (-0.6f, -1.4f), (0f, -2.0f), (-0.8f, -2.2f), (0.2f, -1.2f) })
                {
                    eyes.Add(($"inside at {x}, {z}", ToZone(hut.Doorway, new Vector3(x, 1.6f, z))));
                }
                int reached = 0;
                foreach ((string where, Vector3 eye) in eyes)
                {
                    for (int part = 0; part <= door.Def.Leaves.Count; part++)
                    {
                        Outcome why = zone.Targets().Reach(eye, door.Ref with { Part = (byte)part }, 3.5f);
                        Assert.True(why.Ok || why.Code == OutcomeCode.OutOfReach,
                            $"layout {layout}, {(open ? "open" : "shut")}: from {where} the door's part {part} answers {why.Code}");
                        reached += why.Ok ? 1 : 0;
                    }
                }
                Assert.True(reached > eyes.Count, $"layout {layout}: the door is reached from most of them ({reached})");
                // A solid wall still hides it: beside the hut, behind its side wall, in reach of the door but not in sight of it.
                Vector3 behind = ToZone(hut.Doorway, new Vector3(2.3f, 1.6f, -1.0f));
                Assert.Equal(OutcomeCode.OutOfSight, zone.Targets().Reach(behind, door.Ref with { Part = 1 }, 3.5f).Code,
                    $"layout {layout}: through the hut's side wall the door is not seen");
            }
        }
    }

    [Test]
    public static void AGarageDoorIsNeverRefusedForSightFromWhereItsOpeningShows()
    {
        foreach (bool open in new[] { true, false })
        {
            using var rig = new GameRig();
            Zone zone = rig.Zone;
            (Piece frame, Door door) = GarageOnGround(rig, open);
            for (float radius = 2.0f; radius <= 4.0f; radius += 1.0f)
            {
                for (int degrees = -60; degrees <= 60; degrees += 20)
                {
                    float a = degrees * MathF.PI / 180;
                    Vector3 eye = ToZone(frame, new Vector3(MathF.Sin(a) * radius, 1.6f, MathF.Cos(a) * radius));
                    Outcome why = zone.Targets().Reach(eye, door.Ref with { Part = 1 }, 3.5f);
                    Assert.True(why.Ok || why.Code == OutcomeCode.OutOfReach,
                        $"{(open ? "open" : "shut")} garage door from {radius} m at {degrees} degrees answers {why.Code}");
                }
            }
        }
    }

    [Test]
    public static void ADoorFitsAndShutsWithAPlayerInTheWay()
    {
        using var rig = new GameRig();
        Zone zone = rig.Zone;
        TestBases.Hut hut = Raids.Hut(rig, 0, 12, door: null);
        Entity ada = rig.Add("Ada", 0, 0);
        Raids.Stand(rig, ada, zone.Settle(ToZone(hut.Doorway, new Vector3(0, 0, 0.0f))));
        rig.Step(2);
        BuildingWorld world = zone.Building().World!;
        DoorDef wooden = rig.C.Building.Door("wooden-door")!;
        Assert.True(world.CanFitDoor(wooden, hut.Doorway, 0).Ok, "a door is fitted into a doorway she stands in");
        Assert.True(world.PlaceDoor(1, wooden, hut.Doorway, 0, out Door? door).Ok, "and fits");
        Assert.True(world.Toggle(1, door!).Ok, "and opens, whoever stands in its way");
    }

    // A lone square foundation with a wall frame on its edge 0 and a garage door in it, open or shut.
    private static (Piece Frame, Door Door) GarageOnGround(GameRig rig, bool open)
    {
        Zone zone = rig.Zone;
        BuildingWorld world = zone.Building().World!;
        BuildingData data = world.Data;
        Vector3 at = zone.GroundAt(30, 30);
        zone.Physics.EnsureGround(at);
        double now = zone.Building().BuildingNow;
        PieceDef foundation = data.Piece("foundation")!;
        Piece slab = world.AddFirst(TestBases.FrameOver(zone, data, foundation, at, 0), foundation, 2, Ada, now);
        Piece frame = OnEdge(world, slab, 0, "wall-frame", now);
        DoorDef garage = data.Door("garage-door")!;
        Door door = world.RestoreDoor(zone.NextId(), garage, frame, 0, Ada, open, garage.Health) ?? throw new TestFailure("no garage door");
        rig.Step(2);
        return (frame, door);
    }

    // The tick-by-tick walk of a character at the garage door's opening from 2.4 m outside, for some ticks; the character's
    // distance outward of the opening's plane after it (positive: still outside).
    private static float WalkAtTheGarage(GameRig rig, Piece frame, int ticks)
    {
        Zone zone = rig.Zone;
        Entity walker = zone.Players().AddPlayer(zone.NextId(), "Walker", zone.NewCharacter(zone.Settle(ToZone(frame, new Vector3(0, 0, 2.4f)))));
        Vector3 inside = ToZone(frame, new Vector3(0, 0, -3f));
        for (int tick = 0; tick < ticks; tick++)
        {
            rig.Look(walker, inside);
            rig.Ch(walker).Input.Forward = 1;
            rig.Step();
        }
        return ToFrame(frame, rig.Ch(walker).State.Position).Z;
    }

    // The curtain rolls into its header and has no body above it (DOORS.md §5): through the opening's plane, over the wall frame's
    // top (3 m), the way is clear at any height of the storey above.
    private static void ClearAboveTheHeader(BuildingWorld world, Piece frame, Vector3 along, string when)
    {
        foreach (float height in new[] { 3.2f, 3.8f, 4.5f, 5.2f, 5.6f })
        {
            Assert.True(!world.Physics!.Ray(ToZone(frame, new Vector3(0.3f, height, 1.8f)), -along, 3.6f, out RayHit hit, buildingOnly: true),
                $"{when}: the storey above the garage door ({height:0.0} m) is clear of its body ({(world.Physics.OwnerOf(hit.Body) as DoorLeaf)?.ToString() ?? "-"})");
        }
    }

    [Test]
    public static void AGarageCurtainsBodyRisesAndFallsWithTheKitsClipAndNobodyWalksUnderItWhileItIsLow()
    {
        DoorDef garage = ContentFixture.Content.Building.Door("garage-door")!;
        Assert.Equal(90, (int)Math.Round(1.0 / garage.ProgressPerTick), "the curtain rolls in 3 s: 90 ticks");
        for (int i = 1; i < garage.Lift.Count; i++)
        {
            Assert.True(garage.Lift[i] >= garage.Lift[i - 1] - 1e-3f, "the curtain's lower edge only rises");
        }
        Assert.Near(0, garage.LiftAt(0, 2.85f), 1e-6, "shut: the edge is on the floor");
        Assert.InRange(2.3, 2.6, garage.LiftAt(1, 2.85f), "open: the curtain is curled in the header");

        using (var rig = new GameRig())
        {
            Zone zone = rig.Zone;
            (Piece frame, Door door) = GarageOnGround(rig, open: true);
            BuildingWorld world = zone.Building().World!;
            Vector3 along = Vector3.Transform(Vector3.UnitZ, frame.ZoneTransform.Rotation);
            ClearAboveTheHeader(world, frame, along, "open");
            // Open, the curtain is only what still hangs from the header: its body is 0.42 m, not the curtain's 2.85 m lifted.
            Assert.True(world.Physics!.Ray(ToZone(frame, new Vector3(0.3f, garage.LiftAt(1, 2.85f) + 0.2f, 1.8f)), -along, 3.6f, out RayHit hanging, buildingOnly: true) &&
                world.Physics.OwnerOf(hanging.Body) is DoorLeaf, "open: what still hangs is there");
            Assert.True(world.Toggle(1, door).Ok, "shut, asked");
            for (int tick = 1; tick <= 90; tick += 7)
            {
                rig.Step(tick == 1 ? 1 : 7);
                float progress = door.ProgressAt(zone.Tick);
                float edge = garage.LiftAt(progress, 2.85f);
                Assert.Near(progress, door.Progress, 1e-4, $"tick {tick}: the body is at the tick's progress");
                // Rays through the opening along its depth, just under and just over the curtain's lower edge.
                if (edge > 0.35f)
                {
                    Assert.True(!world.Physics!.Ray(ToZone(frame, new Vector3(0.3f, edge - 0.2f, 1.8f)), -along, 3.6f, out RayHit under, buildingOnly: true),
                        $"tick {tick}: under the curtain's edge ({edge:0.00} m) the way is clear");
                }
                Assert.True(world.Physics!.Ray(ToZone(frame, new Vector3(0.3f, edge + 0.2f, 1.8f)), -along, 3.6f, out RayHit over, buildingOnly: true) &&
                    world.Physics.OwnerOf(over.Body) is DoorLeaf, $"tick {tick}: over its edge the curtain is there");
                ClearAboveTheHeader(world, frame, along, $"tick {tick}");
            }
            rig.Step(10);
            Assert.True(door.Moving == 0 && door.Progress == 0f, "shut at the end");
            ClearAboveTheHeader(world, frame, along, "shut");
        }
        // Nobody walks under it while its edge is lower than they are tall: one walks in when it is open, and not when it is coming down.
        using (var rig = new GameRig())
        {
            (Piece frame, Door door) = GarageOnGround(rig, open: true);
            Assert.True(WalkAtTheGarage(rig, frame, 150) < -0.5f, "through the open garage door: in");
        }
        using (var rig = new GameRig())
        {
            Zone zone = rig.Zone;
            (Piece frame, Door door) = GarageOnGround(rig, open: true);
            Assert.True(zone.Building().World!.Toggle(1, door).Ok, "shut, asked");
            rig.Step(50);
            float edge = garage.LiftAt(door.Progress, 2.85f);
            Assert.True(edge < 1.5f && edge > 0.3f, $"the curtain is part way down ({edge:0.00} m)");
            Assert.True(WalkAtTheGarage(rig, frame, 150) > 0.25f, "under a curtain that is coming down, nobody gets in");
        }
    }
}

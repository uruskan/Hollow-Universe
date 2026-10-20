using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Entities;

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

    [Test]
    public static void ADoubleDoorOpenInwardAndASingleDoorHingedOnTheRightMakeAnAirlockOnlyTogether()
    {
        Assert.True(!RoomWalk(0, 1) && RoomWalk(0, -1) && RoomWalk(-1, 1),
            "a double door opening in and a single door hinged on the right, swinging in: each alone leaves the way free, together they close it");
        Assert.True(!RoomWalk(0, 3) && RoomWalk(-1, 3), "the single door swinging out, hinged on the right, does too");
        Assert.True(RoomWalk(0, 0) && RoomWalk(0, 2), "hinged on the left it does not");
        Assert.True(RoomWalk(2, 0) && RoomWalk(2, 1) && RoomWalk(2, 2) && RoomWalk(2, 3), "a double door swinging out leaves every layout free");
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
        foreach ((int a, int b, bool through) in new[] { (1, 2, false), (0, 3, true) })
        {
            using var rig = new GameRig();
            Zone zone = rig.Zone;
            BuildingWorld world = zone.Building().World!;
            BuildingData data = world.Data;
            Vector3 at = zone.GroundAt(8, 8);
            zone.Physics.EnsureGround(at);
            double now = zone.Building().BuildingNow;
            PieceDef triangle = data.Piece("foundation-triangle")!;
            Piece slab = world.AddFirst(Hollow.Simulation.Raid.TestBases.FrameOver(zone, data, triangle, at, 0), triangle, 2, Ada, now);
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
}

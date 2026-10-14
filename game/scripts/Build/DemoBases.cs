using System;
using System.Collections.Generic;
using Hollow.Core;
using Hollow.Simulation.Building;
using Num = System.Numerics;

namespace HollowUniverse.Build;

/// <summary>
/// Bases the sandbox builds for a look or a screenshot (GROUND.md §6.3), placed through the rules' sockets so every pose
/// is one a player could reach: a two-storey house in mixed grades with doors and windows, a balcony reaching out to show
/// stability, and a 1,060-piece block for the rendering budget.
/// </summary>
public static class DemoBases
{
    private const ulong Owner = 1;

    /// <summary>The edge of a slab facing a way in its structure's frame (+X east, +Z south).</summary>
    public static SocketId Facing(Piece slab, double x, double z)
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
        return new SocketId(SocketKind.Edge, best);
    }

    private static readonly SocketId Top = new(SocketKind.Top, 0);
    private static readonly SocketId Centre = new(SocketKind.Centre, 0);

    /// <summary>Adds a piece on a socket (no checks: the sockets make every pose exact).</summary>
    public static Piece On(BuildingWorld world, Piece parent, SocketId socket, string key, int grade, int orientation = 0)
    {
        PieceDef def = world.Data.Piece(key) ?? throw new ArgumentException("no piece " + key);
        PlacementPlan plan = world.FromIntent(def, parent, socket, orientation)
            ?? throw new InvalidOperationException($"{key} doesn't fit {parent.Def.Key} {socket} #{orientation}");
        return world.Add(plan.Structure!, def, grade, plan.Pose, Owner, 0);
    }

    // The orientation that gives a child an absolute yaw on a socket.
    private static int OrientationFor(BuildingWorld world, Piece parent, SocketId socket, string key, int yaw)
    {
        PieceDef def = world.Data.Piece(key)!;
        int count = PieceGeometry.Orientations(parent.Def, socket, def);
        for (int o = 0; o < count; o++)
        {
            if (PieceGeometry.Snap(world.Data, parent.Def, parent.Pose, socket, def, o, out Pose pose) && pose.Yaw == PieceGeometry.Wrap(yaw))
            {
                return o;
            }
        }
        return 0;
    }

    /// <summary>The house's pieces, for scenes that point at them.</summary>
    public sealed class House
    {
        public Piece F = null!, E = null!, S = null!, SE = null!, Porch = null!;
        public Piece NorthWallF = null!, StairsU = null!, CeilingF = null!;
        public readonly List<Piece> Balcony = new();
        public BuildingFrame Frame => F.Structure.Frame;
    }

    /// <summary>
    /// A two-storey house on four stone foundations with a twig-walled triangle porch: metal and stone walls, a window, a
    /// doorway with a sheet-metal door, a wall frame with a wooden double door; wooden floors over three rooms and a
    /// stairwell with U stairs; wood and armoured walls upstairs; metal roofs meeting in a ridge; a claim device inside.
    /// </summary>
    public static House TwoStorey(BuildingWorld world, PlacementPlan first)
    {
        var h = new House();
        h.F = world.AddFirst(first.Frame, first.Piece, 2, Owner, 0);
        h.E = On(world, h.F, Facing(h.F, 1, 0), "foundation", 2);
        h.S = On(world, h.F, Facing(h.F, 0, 1), "foundation", 2);
        h.SE = On(world, h.E, Facing(h.E, 0, 1), "foundation", 2);
        h.Porch = On(world, h.S, Facing(h.S, 0, 1), "foundation-triangle", 1);

        // Ground floor: outside faces out (orientation 0 puts a wall's soft side toward its slab).
        h.NorthWallF = On(world, h.F, Facing(h.F, 0, -1), "wall", 2);
        Piece northE = On(world, h.E, Facing(h.E, 0, -1), "window", 2);
        Piece eastE = On(world, h.E, Facing(h.E, 1, 0), "wall", 3);
        Piece eastSE = On(world, h.SE, Facing(h.SE, 1, 0), "wall", 3);
        Piece southSE = On(world, h.SE, Facing(h.SE, 0, 1), "wall", 2);
        Piece doorway = On(world, h.S, Facing(h.S, 0, 1), "doorway", 2);
        Piece westS = On(world, h.S, Facing(h.S, -1, 0), "window", 1);
        Piece westF = On(world, h.F, Facing(h.F, -1, 0), "wall-frame", 1);
        // Inner walls between the rooms and the stairwell.
        On(world, h.SE, Facing(h.SE, -1, 0), "wall", 1);

        // Floors over the three rooms; the stairwell stays open.
        h.CeilingF = On(world, h.NorthWallF, Top, "floor", 1);
        On(world, northE, Top, "floor", 1);
        On(world, westS, Top, "floor", 1);
        h.StairsU = On(world, h.SE, Centre, "stairs-u", 2, OrientationFor(world, h.SE, Centre, "stairs-u", 6));

        // Upstairs.
        Piece upNorthF = On(world, h.NorthWallF, Top, "wall", 1);
        Piece upNorthE = On(world, northE, Top, "wall", 1);
        On(world, eastE, Top, "wall", 4);
        On(world, eastSE, Top, "window", 4);
        Piece upSouthSE = On(world, southSE, Top, "window", 1);
        Piece upSouthS = On(world, doorway, Top, "wall", 1);
        On(world, westS, Top, "wall", 1);
        On(world, westF, Top, "window", 1);

        // Roofs rising from the north and south walls to a ridge over the middle.
        On(world, upNorthF, Top, "roof", 3);
        On(world, upNorthE, Top, "roof", 3);
        On(world, upSouthS, Top, "roof", 3);
        On(world, upSouthSE, Top, "roof", 3);

        // The porch's low twig walls.
        On(world, h.Porch, Facing(h.Porch, 1, 0.6), "low-wall", 0);
        On(world, h.Porch, Facing(h.Porch, -1, 0.6), "low-wall", 0);

        // Doors and the claim device.
        world.PlaceDoor(Owner, world.Data.Door("sheet-metal-door")!, doorway, 0, out _);
        world.PlaceDoor(Owner, world.Data.Door("wooden-double-door")!, westF, 1, out _);
        world.PlaceClaim(Owner, h.F, h.F.Pose.Position + new Vector3D(0.6, 0, -0.7), 0, out ClaimDevice? claim);
        if (claim != null)
        {
            claim.Store = new Wallet { ["wood"] = 1500, ["stone"] = 2000, ["metal-fragments"] = 800, ["refined-metal"] = 60 };
        }
        return h;
    }

    /// <summary>A balcony reaching north from the house's first floor: stability falls 90, 70, 50, 30, 10 %.</summary>
    public static void Balcony(BuildingWorld world, House h)
    {
        Piece reach = On(world, h.NorthWallF, Top, "floor", 1, 4);
        h.Balcony.Add(reach);
        for (int i = 0; i < 4; i++)
        {
            reach = On(world, reach, Facing(reach, 0, -1), "floor", i == 3 ? 0 : 1);
            h.Balcony.Add(reach);
        }
    }

    // A point in front of a wall for the reach checks of fitting a door.
    private static Num.Vector3 Eye(BuildingWorld world, Piece wall)
    {
        (Num.Vector3 position, Num.Quaternion rotation) = wall.ZoneTransform;
        return position + Num.Vector3.Transform(new Num.Vector3(0, 1.6f, 2), rotation);
    }

    /// <summary>
    /// A block of 10 × 10 rooms three storeys high (1,060 pieces): stone foundations; metal, armoured and stone outer
    /// walls with windows on top; stone, wood and twig inner walls with doorways; wood, metal and stone floors.
    /// </summary>
    public static int Block(BuildingWorld world, PlacementPlan first, int size = 10)
    {
        var cells = new Piece[size, size];
        cells[0, 0] = world.AddFirst(first.Frame, first.Piece, 2, Owner, 0);
        for (int i = 0; i < size; i++)
        {
            if (i > 0)
            {
                cells[i, 0] = On(world, cells[i - 1, 0], Facing(cells[i - 1, 0], 1, 0), "foundation", 2);
            }
            for (int j = 1; j < size; j++)
            {
                cells[i, j] = On(world, cells[i, j - 1], Facing(cells[i, j - 1], 0, 1), "foundation", 2);
            }
        }
        // Walls on each room's north and west edges, and on the far east and south: every edge once.
        var below = new Dictionary<(int, int, int), Piece>();
        int[] outerGrades = { 3, 4, 2 };
        int[] innerGrades = { 2, 1, 0 };
        for (int storey = 0; storey < 3; storey++)
        {
            var walls = new Dictionary<(int, int, int), Piece>();
            for (int i = 0; i < size; i++)
            {
                for (int j = 0; j < size; j++)
                {
                    foreach ((int dx, int dz, int side) in new[] { (0, -1, 0), (-1, 0, 1), (1, 0, 2), (0, 1, 3) })
                    {
                        bool far = (side == 2 && i == size - 1) || (side == 3 && j == size - 1);
                        if (side >= 2 && !far)
                        {
                            continue;
                        }
                        bool outer = (side == 0 && j == 0) || (side == 1 && i == 0) || far;
                        string key = outer
                            ? storey == 2 && (i + j) % 2 == 0 ? "window" : "wall"
                            : (i * 7 + j * 3 + storey) % 5 == 0 ? "doorway" : "wall";
                        int grade = outer ? outerGrades[storey] : innerGrades[storey];
                        Piece wall = storey == 0
                            ? On(world, cells[i, j], Facing(cells[i, j], dx, dz), key, grade)
                            : On(world, below[(i, j, side)], Top, key, grade);
                        walls[(i, j, side)] = wall;
                    }
                }
            }
            // A floor over every room, resting on the walls' tops.
            int floorGrade = storey switch { 0 => 1, 1 => 3, _ => 2 };
            for (int i = 0; i < size; i++)
            {
                for (int j = 0; j < size; j++)
                {
                    On(world, walls[(i, j, 0)], Top, "floor", floorGrade);
                }
            }
            below = walls;
        }
        return world.PieceCount;
    }
}

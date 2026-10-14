using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Building;

namespace Hollow.Simulation.Raid;

/// <summary>
/// Small claimed bases built straight through the building rules (no player, no wallet, no placement checks), for the raid
/// tests, the admin's <c>raidbase</c> and the raid bots' load test (GROUND.md §6.6): the same pieces players build, in one
/// go. A hut is a foundation with three walls and a doorway with a door, a floor on top for a roof and a claim device
/// inside listing its owner.
/// </summary>
public static class TestBases
{
    private static readonly SocketId Top = new(SocketKind.Top, 0);

    /// <summary>A hut's pieces. The doorway is on the foundation's south edge (its +Z), facing out.</summary>
    public sealed class Hut
    {
        public required Piece Foundation { get; init; }

        /// <summary>South (the doorway), east, north, west.</summary>
        public required Piece[] Walls { get; init; }

        public Piece Doorway => Walls[0];

        public Door? Door { get; internal set; }

        public Piece? Roof { get; internal set; }

        public ClaimDevice? Claim { get; internal set; }

        public int Grade { get; init; }

        public string? DoorKey { get; init; }

        public ulong Owner { get; init; }

        /// <summary>The middle of the floor inside, on the foundation's top (zone frame).</summary>
        public Vector3 Inside => ToZone(new Vector3D(0, 0, 0));

        /// <summary>A point of the hut's frame in the zone.</summary>
        public Vector3 ToZone(Vector3D local)
        {
            Vector3D z = Foundation.Structure.Frame.ToZone(PieceGeometry.ToFrame(Foundation.Pose, local));
            return new Vector3((float)z.X, (float)z.Y, (float)z.Z);
        }

        /// <summary>A direction of the hut's frame in the zone.</summary>
        public Vector3 Direction(Vector3D local)
        {
            Vector3D z = Foundation.Structure.Frame.DirectionToZone(PieceGeometry.Rotate(local, Foundation.Pose.Yaw));
            return Vector3.Normalize(new Vector3((float)z.X, (float)z.Y, (float)z.Z));
        }

        /// <summary>
        /// A point on a wall's outside face (or, <paramref name="inside"/>, its inside face) at a height above the floor,
        /// and the face's normal (pointing away from the wall).
        /// </summary>
        public (Vector3 Point, Vector3 Normal) Face(Piece wall, double height = 1.4, bool inside = false, double along = 0)
        {
            (Vector3 position, Quaternion rotation) = wall.ZoneTransform;
            Vector3 outward = Vector3.Transform(Vector3.UnitZ, rotation) * (inside ? -1 : 1);
            Vector3 right = Vector3.Transform(Vector3.UnitX, rotation);
            Vector3 up = Vector3.Transform(Vector3.UnitY, rotation);
            float half = (float)(wall.Structure.World.Data.WallThickness / 2);
            return (position + up * (float)height + right * (float)along + outward * half, outward);
        }

        /// <summary>Whether every piece, the door and the claim device still stand.</summary>
        public bool Whole => !Foundation.Gone && Array.TrueForAll(Walls, w => !w.Gone) && (Roof == null || !Roof.Gone) &&
            (DoorKey == null || Door is { Gone: false }) && Claim is { Gone: false };
    }

    /// <summary>
    /// Builds a hut over a zone point (its foundation's top just above the ground there), turned by
    /// <paramref name="heading"/> radians from the zone's east, every piece at <paramref name="grade"/>, with a door (a
    /// building.json door key, or null for none) and a claim device listing <paramref name="owner"/>. Null where there is
    /// no ground.
    /// </summary>
    public static Hut? Build(Zone zone, Vector3 at, double heading, int grade, ulong owner, string? door = "sheet-metal-door", bool claim = true)
    {
        BuildingWorld world = zone.Building().World ?? throw new InvalidOperationException("The zone has no building (no game data).");
        BuildingData data = world.Data;
        zone.Physics.EnsureGround(at);
        PieceDef foundationDef = data.Piece("foundation")!;
        double now = zone.Building().BuildingNow;
        Piece foundation = world.AddFirst(FrameOver(zone, data, foundationDef, at, heading), foundationDef, grade, owner, now);
        var walls = new Piece[4];
        string[] keys = { "doorway", "wall", "wall", "wall" };
        for (int e = 0; e < 4; e++)
        {
            walls[e] = On(world, foundation, new SocketId(SocketKind.Edge, e), keys[e], grade, owner, now);
        }
        var hut = new Hut { Foundation = foundation, Walls = walls, Grade = grade, DoorKey = door, Owner = owner };
        hut.Roof = On(world, walls[0], Top, "floor", grade, owner, now);
        if (door != null)
        {
            FitDoor(zone, hut);
        }
        if (claim)
        {
            hut.Claim = PutClaim(zone, foundation, owner, new[] { owner });
        }
        return hut;
    }

    // The claim device in its corner of the foundation, listing whom it lists: put straight in like the pieces, since the
    // placement rules would refuse it under a neighbour's claim (which reaches over the next huts of a ring) or over a
    // character standing there.
    private static ClaimDevice? PutClaim(Zone zone, Piece foundation, ulong owner, IEnumerable<ulong> listed)
    {
        BuildingWorld world = zone.Building().World!;
        return world.RestoreClaim(zone.NextId(), owner, foundation, new Vector3D(0.8, foundation.Pose.Position.Y, -0.8), 0, world.Data.ClaimHealth,
            listed);
    }

    /// <summary>Puts back whatever of a hut is gone (a wall, the door, the roof, the claim device), as new; false when its foundation went (build another).</summary>
    public static bool Restore(Zone zone, Hut hut)
    {
        BuildingWorld world = zone.Building().World!;
        if (hut.Foundation.Gone)
        {
            return false;
        }
        double now = zone.Building().BuildingNow;
        string[] keys = { "doorway", "wall", "wall", "wall" };
        for (int e = 0; e < 4; e++)
        {
            if (hut.Walls[e].Gone)
            {
                hut.Walls[e] = On(world, hut.Foundation, new SocketId(SocketKind.Edge, e), keys[e], hut.Grade, hut.Owner, now);
            }
        }
        if (hut.Roof is { Gone: true })
        {
            hut.Roof = On(world, hut.Walls[0], Top, "floor", hut.Grade, hut.Owner, now);
        }
        if (hut.DoorKey != null && hut.Door is null or { Gone: true })
        {
            FitDoor(zone, hut);
        }
        if (hut.Claim is null or { Gone: true })
        {
            ulong[] listed = hut.Claim != null ? new List<ulong>(hut.Claim.Authorised).ToArray() : new[] { hut.Owner };
            hut.Claim = PutClaim(zone, hut.Foundation, hut.Owner, listed);
        }
        return true;
    }

    /// <summary>
    /// The <paramref name="index"/>-th of <paramref name="count"/> places on a ring of <paramref name="radius"/> metres
    /// round a point of the world (measured along the ground), and the heading that turns a hut's doorway toward it.
    /// </summary>
    public static (Vector3 At, double Heading) Ring(GroundProbe ground, Vector3 centre, int index, int count, float radius)
    {
        double angle = DMath.TwoPi * index / Math.Max(1, count);
        Vector3 at = ground.Offset(centre, (float)(radius * DMath.Cos(angle)), (float)(radius * DMath.Sin(angle)));
        // A hut's +Z (its doorway's outside) is the frame's AxisZ (BuildingFrame.At): pointed at the centre.
        Vector3 up = ground.Frame.UpAt(at);
        Vector3 toward = centre - at;
        toward -= up * Vector3.Dot(toward, up);
        double heading = BuildingFrame.HeadingOf(new Vector3D(toward.X, toward.Y, toward.Z), new Vector3D(up.X, up.Y, up.Z));
        return (at, heading);
    }

    /// <summary>
    /// A foundation's frame over a zone point: up the planet's there, its top just above the highest ground under its
    /// corners and centre (as the placement rules put it), from the surface itself rather than the collision tiles (Jolt's
    /// rays straight down onto the tiles' meshes miss now and then; GROUND.md §11).
    /// </summary>
    public static BuildingFrame FrameOver(Zone zone, BuildingData data, PieceDef foundation, Vector3 at, double heading)
    {
        Vector3 ground = zone.Settle(at);
        Vector3 up = zone.Frame.UpAt(ground);
        var upD = new Vector3D(up.X, up.Y, up.Z);
        BuildingFrame probe = BuildingFrame.At(new Vector3D(ground.X, ground.Y, ground.Z), upD, heading);
        double highest = 0;
        var samples = new List<Vector3D>(PieceGeometry.Footprint(foundation.Shape, data.Width)) { Vector3D.Zero };
        foreach (Vector3D sample in samples)
        {
            Vector3D p = probe.ToZone(sample);
            highest = Math.Max(highest, -zone.HeightAboveGround(new Vector3((float)p.X, (float)p.Y, (float)p.Z)));
        }
        return BuildingFrame.At(probe.Origin + upD * (highest + data.FoundationClearance + 0.02), upD, heading);
    }

    // The door in the doorway, shut and unlocked: put straight in like the claim device (PutClaim).
    private static void FitDoor(Zone zone, Hut hut)
    {
        BuildingWorld world = zone.Building().World!;
        if (world.Data.Door(hut.DoorKey!) is not { } def)
        {
            return;
        }
        hut.Door = world.RestoreDoor(zone.NextId(), def, hut.Doorway, 0, hut.Owner, open: false, def.Health);
    }

    private static Piece On(BuildingWorld world, Piece parent, SocketId socket, string key, int grade, ulong owner, double now)
    {
        PieceDef def = world.Data.Piece(key) ?? throw new ArgumentException("no piece " + key);
        PlacementPlan plan = world.FromIntent(def, parent, socket, 0)
            ?? throw new InvalidOperationException($"{key} doesn't fit {parent.Def.Key} {socket}");
        return world.Add(plan.Structure!, def, grade, plan.Pose, owner, now);
    }
}

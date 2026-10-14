using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Building;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;
using Hollow.Surface;

namespace Hollow.Simulation.Placement;

/// <summary>A deployable or prop as placement sees it: what it is named, its box (on its base, along up, turned by yaw).</summary>
public readonly record struct PlacedThing(TargetRef Ref, DeployableDef Def, Vector3 Position, Vector3 Up, float Yaw)
{
    public Vector3 Centre => Position + Up * Def.HalfExtents.Y;
}

/// <summary>A ship as placement sees it: its kind, its centre of mass and its turn.</summary>
public readonly record struct PlacedShip(ShipDef Def, Vector3 Position, Quaternion Rotation);

/// <summary>What the world along a ray first meets: how far, where, its normal, what it is named and what surface it is.</summary>
public readonly record struct PlacementHit(float Distance, Vector3 Point, Vector3 Normal, TargetRef Target, PlacementSurfaces Surface);

/// <summary>
/// What the placement world knows only from one side (GROUND_SYSTEMS.md §6): the server from its zone, a client from what it
/// was sent. Things with and without bodies (a sleeping bag has none), characters, ships, what stands on what, and whose
/// things are whose.
/// </summary>
public interface IPlacementSurroundings
{
    /// <summary>Deployables and props whose base is within a radius of a point.</summary>
    void ThingsNear(Vector3 point, float radius, List<PlacedThing> into);

    /// <summary>Living characters' standing capsules within a radius of a point.</summary>
    void CharactersNear(Vector3 point, float radius, List<Capsule> into);

    /// <summary>Ships within a radius of a point.</summary>
    void ShipsNear(Vector3 point, float radius, List<PlacedShip> into);

    /// <summary>How many things stand on a support (a piece).</summary>
    int ThingsOn(in TargetRef support);

    /// <summary>How many of a deployable a player owns.</summary>
    int ThingsOwned(ulong player, DeployableDef def);
}

/// <summary>
/// The world a placement is solved and checked in (GROUND_SYSTEMS.md §6), the same code on both sides: the server's zone
/// and a client's replicated world (its prediction physics with the ground tiles, trees, rocks and solid deployables, its
/// mirror of the building, the natural objects as players changed them) give one each, so the client's ghost and the
/// server's verdict come from the same solvers and checks. Its rays and overlaps meet every body; what a body is (the
/// ground, a piece, a door's leaf, a box, a tree) is asked of its owner. Scratch lists are its own: one per zone or
/// client.
/// </summary>
public sealed class PlacementWorld
{
    private readonly List<OverlapHit> _overlaps = new();
    private readonly List<PlacedThing> _things = new();
    private readonly List<NaturalObject> _nature = new();

    /// <summary>The pipeline's scratch: candidates, and the building's plans (one world per thread).</summary>
    internal readonly List<PlacementCandidate> Candidates = new();

    internal readonly List<PlacementPlan> Plans = new();

    public PlacementWorld(GameContent content, ZoneFrame frame, ISurface surface, PhysicsWorld? physics, BuildingWorld? building,
        IPlacementSurroundings surroundings, INaturalObjects? nature = null, NaturalState? natural = null, ShipCollider? ships = null)
    {
        Content = content;
        Frame = frame;
        Physics = physics;
        Building = building;
        Around = surroundings;
        Nature = nature;
        Natural = natural;
        Ships = ships;
        Ground = new GroundProbe(surface, frame);
    }

    public GameContent Content { get; }

    public PlacementData Data => Content.Placement;

    public ZoneFrame Frame { get; }

    public GroundProbe Ground { get; }

    /// <summary>The Jolt world (null: a building without collision, as some tests use).</summary>
    public PhysicsWorld? Physics { get; }

    public BuildingWorld? Building { get; }

    public IPlacementSurroundings Around { get; }

    public INaturalObjects? Nature { get; }

    public NaturalState? Natural { get; }

    /// <summary>Ship hulls' queries (null: no ships here).</summary>
    public ShipCollider? Ships { get; }

    /// <summary>Things near a point (the world's own list, good until the next call).</summary>
    public List<PlacedThing> ThingsNear(Vector3 point, float radius)
    {
        _things.Clear();
        Around.ThingsNear(point, radius, _things);
        return _things;
    }

    // ---------------------------------------------------------------- rays and overlaps

    /// <summary>
    /// The first body along a ray within <paramref name="length"/>: the ground's tiles, building pieces, doors and the claim
    /// device, trees and rocks, solid deployables and props, parked ships; what it is and what surface it offers.
    /// </summary>
    public bool Ray(Vector3 origin, Vector3 direction, float length, out PlacementHit hit)
    {
        hit = default;
        if (Building?.Physics is not { } physics || !physics.Ray(origin, direction, length, out RayHit ray))
        {
            return false;
        }
        (TargetRef target, PlacementSurfaces surface) = Classify(ray.Body);
        hit = new PlacementHit(ray.Distance, ray.Point, Facing(ray.Normal, direction), target, surface);
        return true;
    }

    // The surface each family of world thing offers (a piece's is its own family's, placement.json's pieceSurfaces), and
    // the family each layer of props names its bodies by: lookups, by number.
    private static readonly PlacementSurfaces[] FamilySurfaces = Surfaces();
    private static readonly TargetFamily[] PropFamilies = { TargetFamily.Natural, TargetFamily.Thing };

    private static PlacementSurfaces[] Surfaces()
    {
        var surfaces = new PlacementSurfaces[(int)TargetFamily.Ship + 1];
        surfaces[(int)TargetFamily.Ground] = PlacementSurfaces.Terrain;
        surfaces[(int)TargetFamily.Natural] = PlacementSurfaces.Natural;
        surfaces[(int)TargetFamily.Thing] = PlacementSurfaces.Thing;
        surfaces[(int)TargetFamily.Cupboard] = PlacementSurfaces.Thing;
        surfaces[(int)TargetFamily.Door] = PlacementSurfaces.Door;
        surfaces[(int)TargetFamily.Ship] = PlacementSurfaces.Ship;
        return surfaces;
    }

    /// <summary>What a body is named (its owner's name for it), and the surface it offers.</summary>
    public (TargetRef Target, PlacementSurfaces Surface) Classify(uint body)
    {
        TargetRef target = PhysicsWorld.IsGroundBody(body) ? TargetRef.Ground
            : Building?.Physics?.OwnerOf(body) is ITargetable owner ? owner.Ref
            : Physics != null && Physics.TryPropOf(body, out PropLayer layer, out ulong key) ? new TargetRef(PropFamilies[(int)layer], key)
            : TargetRef.None;
        return (target, SurfaceOf(target));
    }

    /// <summary>The surface a thing offers what is placed on it: a piece by its family (placement.json), the rest by theirs.</summary>
    public PlacementSurfaces SurfaceOf(in TargetRef target) =>
        target.Family == TargetFamily.Piece
            ? Building?.PieceOf(new Entities.EntityId(target.Id)) is { } piece ? Data.SurfaceOf(piece.Def.Family) : PlacementSurfaces.None
            : (uint)target.Family < (uint)FamilySurfaces.Length ? FamilySurfaces[(int)target.Family] : PlacementSurfaces.None;

    /// <summary>Every body but the ground's a box overlaps by more than the building's contact tolerance (the world's own list).</summary>
    public List<OverlapHit> Overlaps(object key, Vector3 half, Vector3 basePoint, Quaternion rotation)
    {
        _overlaps.Clear();
        if (Building?.Physics is not { } physics)
        {
            return _overlaps;
        }
        JoltPhysicsSharp.Shape shape = physics.ShapeFor(key, new[] { ConvexPart.Box(-half.X, half.X, 0, 2 * half.Y, -half.Z, half.Z) });
        physics.Overlaps(shape, basePoint, rotation, (float)Building.Data.ContactTolerance, _overlaps);
        _overlaps.RemoveAll(o => PhysicsWorld.IsGroundBody(o.Body));
        return _overlaps;
    }

    private static Vector3 Facing(Vector3 normal, Vector3 direction)
    {
        if (!float.IsFinite(normal.X) || normal.LengthSquared() < 1e-6f)
        {
            return -direction;
        }
        normal = Vector3.Normalize(normal);
        return Vector3.Dot(normal, direction) > 0 ? -normal : normal;
    }

    // ---------------------------------------------------------------- the sweep (explosives)

    /// <summary>
    /// The first surface a ball of a radius meets from one point to the next (GROUND.md §6.6): building pieces, doors and
    /// the claim device, deployables and props by their boxes, trees and outcrops, the ground (characters are passed
    /// through). A thrown explosive's flight and a charge put on a surface by hand share it, on the server and in a
    /// client's ghost.
    /// </summary>
    public bool Sweep(Vector3 from, Vector3 to, float radius, out PlacementHit best)
    {
        best = new PlacementHit(float.MaxValue, default, default, TargetRef.None, PlacementSurfaces.None);
        Vector3 d = to - from;
        float length = d.Length();
        if (length < 1e-6f)
        {
            return false;
        }
        Vector3 direction = d / length;

        if (Building?.Physics is { } physics && physics.Ray(from, direction, length + radius, out RayHit rh, buildingOnly: true))
        {
            float t = MathF.Max(0, rh.Distance - radius);
            if (t <= length)
            {
                // A piece, a door's leaf (its part), the claim device: whatever owns the body says how it is named.
                (TargetRef target, PlacementSurfaces surface) = Classify(rh.Body);
                best = new PlacementHit(t, rh.Point, Facing(rh.Normal, direction), target, surface);
            }
        }

        float reach = MathF.Min(length, best.Distance);
        foreach (PlacedThing thing in ThingsNear((from + to) / 2, length / 2 + 2f))
        {
            (Vector3 right, Vector3 forward) = Geometry.Axes(thing.Up, thing.Yaw);
            Vector3 half = thing.Def.HalfExtents + new Vector3(radius);
            Vector3 centre = thing.Centre;
            float t = Geometry.RayBox(from, direction, reach, centre, half, right, thing.Up, forward);
            if (t >= 0 && t < best.Distance)
            {
                Vector3 point = from + direction * t;
                Vector3 local = point - centre;
                float x = Vector3.Dot(local, right) / half.X, y = Vector3.Dot(local, thing.Up) / half.Y, z = Vector3.Dot(local, forward) / half.Z;
                Vector3 normal = MathF.Abs(x) >= MathF.Abs(y) && MathF.Abs(x) >= MathF.Abs(z) ? right * MathF.Sign(x)
                    : MathF.Abs(y) >= MathF.Abs(z) ? thing.Up * MathF.Sign(y) : forward * MathF.Sign(z);
                if (t <= 0 && Vector3.Dot(direction, normal) >= 0)
                {
                    continue; // touching it and moving away
                }
                best = new PlacementHit(t, point - normal * radius, normal, thing.Ref, PlacementSurfaces.Thing);
                reach = t;
            }
        }

        if (Nature != null && Natural != null)
        {
            _nature.Clear();
            const float margin = 3f;
            Nature.Query(MathF.Min(from.X, to.X) - margin, MathF.Min(from.Z, to.Z) - margin, MathF.Max(from.X, to.X) + margin,
                MathF.Max(from.Z, to.Z) + margin, _nature);
            foreach (NaturalObject o in _nature)
            {
                NaturalKind? kind = Content.Natural(o.Kind);
                if (kind == null || !kind.IsHarvested || Natural.IsDepleted(o.Id))
                {
                    continue;
                }
                float t;
                Vector3 normal;
                if (kind.Class == NaturalClass.Tree)
                {
                    (Vector3 a, Vector3 b, float r) = NaturalShapes.Trunk(o, kind);
                    t = Geometry.RayCapsule(from, direction, reach, a, b, r + radius);
                    Vector3 point = from + direction * MathF.Max(0, t);
                    Vector3 axis = Vector3.Normalize(b - a);
                    Vector3 onAxis = a + axis * Math.Clamp(Vector3.Dot(point - a, axis), 0, Vector3.Distance(a, b));
                    normal = SafeNormal(point - onAxis, -direction);
                }
                else
                {
                    (Vector3 centre, float r) = NaturalShapes.Rock(o, kind);
                    t = Geometry.RaySphere(from, direction, reach, centre, r + radius);
                    normal = SafeNormal(from + direction * MathF.Max(0, t) - centre, -direction);
                }
                if (t >= 0 && t < best.Distance && !(t <= 0 && Vector3.Dot(direction, normal) >= 0))
                {
                    best = new PlacementHit(t, from + direction * t - normal * radius, normal, new TargetRef(TargetFamily.Natural, o.Id), PlacementSurfaces.Natural);
                    reach = t;
                }
            }
        }

        float ground = Ground.Crossing(from, direction, reach);
        if (ground >= 0 && ground < best.Distance)
        {
            Vector3 point = from + direction * ground;
            best = new PlacementHit(ground, point, Ground.Normal(point).Normal, TargetRef.Ground, PlacementSurfaces.Terrain);
        }
        return best.Distance <= length;
    }

    private static Vector3 SafeNormal(Vector3 v, Vector3 fallback) => v.LengthSquared() > 1e-8f ? Vector3.Normalize(v) : fallback;
}

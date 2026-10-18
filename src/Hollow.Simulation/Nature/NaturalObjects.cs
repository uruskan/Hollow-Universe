using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Hollow.Simulation.Content;

namespace Hollow.Simulation.Nature;

/// <summary>
/// One natural object (GROUND.md §4.6): a tree, an outcrop, a pickup or a bush, generated from the surface, never
/// stored. Its <see cref="Id"/> is stable (ARCHITECTURE.md §7: tile and index), so the server, every client and the
/// database name it the same; only player changes to it are stored (<see cref="NaturalState"/>).
/// </summary>
public readonly record struct NaturalObject
{
    public ulong Id { get; init; }

    /// <summary>The kind's index in gather.json (<see cref="GameContent.NaturalKinds"/>).</summary>
    public ushort Kind { get; init; }

    /// <summary>Its base on the ground, in the zone frame.</summary>
    public Vector3 Position { get; init; }

    /// <summary>Away from the planet's centre at its base.</summary>
    public Vector3 Up { get; init; }

    /// <summary>Size against the kind's: trees and outcrops get this much more health and yield.</summary>
    public float Scale { get; init; }

    /// <summary>Turn about <see cref="Up"/>, in radians.</summary>
    public float Yaw { get; init; }
}

/// <summary>
/// Where natural objects are (GROUND.md §6.1): the planet's scatter (<see cref="ScatterNaturalObjects"/>) on a real
/// surface, <see cref="StandInNaturalObjects"/> on the stand-in. Pure functions of the zone, safe to call from any
/// thread.
/// </summary>
public interface INaturalObjects
{
    /// <summary>
    /// Adds the objects whose base lies within <paramref name="radius"/> metres of <paramref name="centre"/> (a point of
    /// the zone, straight-line distance), always in the same order (docs/WHOLE_WORLD.md §7.1).
    /// </summary>
    void Query(Vector3 centre, float radius, List<NaturalObject> into);

    /// <summary>
    /// Candidates whose roots may lie within <paramref name="rootReach"/> of a segment. The reach must enclose the
    /// full hit shape about its root, including the ray's thickness. Providers with a spherical surface traverse
    /// conservative tile bounds before generating anything. Other providers retain the enclosing-sphere fallback.
    /// Exact hit testing remains the caller's responsibility; candidates keep the provider's canonical tile order.
    /// </summary>
    void QueryAlong(Vector3 from, Vector3 to, float rootReach, List<NaturalObject> into)
    {
        if (NaturalSegmentQuery.EnclosingSphere(from, to, rootReach, out Vector3 centre, out float radius))
            Query(centre, radius, into);
    }

    /// <summary>The object with an ID (from outside: untrusted, so a made-up one is simply not found).</summary>
    bool TryGet(ulong id, out NaturalObject found);

    /// <summary>
    /// The object with an ID if it may lie within <paramref name="radius"/> of a point: an untrusted ID that names a
    /// place far away is refused before any work is done for it (a player's pick-up request).
    /// </summary>
    bool TryGetNear(ulong id, Vector3 point, float radius, out NaturalObject found) =>
        TryGet(id, out found) && Vector3.Distance(found.Position, point) <= radius;

    /// <summary>The cube-sphere level of the tiles the objects are placed by: each object belongs to one tile of it.</summary>
    int Level { get; }

    /// <summary>The objects of one tile of <see cref="Level"/>, always in the same order (drawing streams them by tile).</summary>
    IReadOnlyList<NaturalObject> InTile(Surface.TileKey tile);

    /// <summary>The tile an ID names, without making the tile's objects (untrusted: null when it names none of ours).</summary>
    Surface.TileKey? TileOf(ulong id);
}

/// <summary>All-or-fail collision planning; source pins and complete root-tile coverage belong to the caller.</summary>
public interface IPlannedNaturalCollision : INaturalObjects
{
    NaturalCollisionPlan PrepareCollision(ReadOnlySpan<Surface.TileKey> completeRootTiles, NaturalCollisionLimits limits,
        CancellationToken cancellation = default);
    NaturalCollisionSnapshot CaptureDepletion(NaturalCollisionPlan plan, NaturalState state, CancellationToken cancellation = default);
    bool IsCurrent(NaturalCollisionSnapshot snapshot, NaturalState state);
}

/// <summary>Shapes and derived numbers of natural objects, shared by the server, the client and the physics.</summary>
public static class NaturalShapes
{
    /// <summary>
    /// Largest scale produced by either natural-object generator: scatter rocks reach 2.45; scatter trees 1.3,
    /// other plants 1.4, outcrops 1.25, and stand-in objects at most 1.25. Changing generation must update this bound.
    /// </summary>
    public const float MaximumGeneratedScale = 2.45f;

    /// <summary>
    /// Conservative distance from the root to every point tested by NatureReceiver: a tree's two trunk caps, or
    /// the full offset rock/outcrop sphere. This includes short, wide trunks whose caps extend past their height.
    /// </summary>
    public static float RayRootReach(NaturalKind kind, float scale) => kind.Class == NaturalClass.Tree
        ? System.MathF.Max(kind.Height, 2 * kind.Radius) * scale
        : 1.35f * kind.Radius * scale;

    public static float MaximumRayRootReach(IEnumerable<NaturalKind> kinds)
    {
        float reach = 0;
        foreach (NaturalKind kind in kinds)
            if (kind.IsSolid) reach = System.MathF.Max(reach, RayRootReach(kind, MaximumGeneratedScale));
        return reach;
    }

    /// <summary>A tree's trunk: a vertical capsule from its base up.</summary>
    public static (Vector3 A, Vector3 B, float Radius) Trunk(in NaturalObject o, NaturalKind kind)
        => Trunk(o, kind.Radius, kind.Height);

    internal static (Vector3 A, Vector3 B, float Radius) Trunk(in NaturalObject o, float baseRadius, float baseHeight)
    {
        float radius = baseRadius * o.Scale;
        float height = baseHeight * o.Scale;
        return (o.Position + o.Up * radius, o.Position + o.Up * (height - radius), radius);
    }

    /// <summary>
    /// An outcrop: a sphere a third sunk in the ground. A boulder: a sphere mostly buried (its top 0.65 of its radius
    /// above the ground), as the planet draws its rocks.
    /// </summary>
    public static (Vector3 Centre, float Radius) Rock(in NaturalObject o, NaturalKind kind)
        => Rock(o, kind.Radius, kind.Class == NaturalClass.Boulder);

    internal static (Vector3 Centre, float Radius) Rock(in NaturalObject o, float baseRadius, bool boulder)
    {
        float radius = baseRadius * o.Scale;
        float lift = boulder ? -0.35f : 0.35f;
        return (o.Position + o.Up * (radius * lift), radius);
    }

    /// <summary>How far an object rises above the ground (for overlap checks).</summary>
    public static float Top(in NaturalObject o, NaturalKind kind) => kind.Class switch
    {
        NaturalClass.Tree => kind.Height * o.Scale,
        NaturalClass.Boulder => kind.Radius * o.Scale * 0.65f,
        _ => kind.Radius * o.Scale * 1.35f,
    };

    /// <summary>Where an interaction aims for a pickup or bush (its middle).</summary>
    public static Vector3 Middle(in NaturalObject o, NaturalKind kind) => o.Position + o.Up * (kind.Height * o.Scale * 0.5f);

    /// <summary>
    /// The space an object takes, as one shape (reach is measured to it, the interact key aims at it): a tree's trunk, an
    /// outcrop's or boulder's rock, a pickup's or bush's ball round its middle.
    /// </summary>
    public static Combat.HitShape Bounds(in NaturalObject o, NaturalKind kind)
    {
        if (kind.Class == NaturalClass.Tree)
        {
            (Vector3 a, Vector3 b, float r) = Trunk(o, kind);
            return new Combat.HitShape(a, b, r, Content.HitRegion.Body);
        }
        if (kind.IsHarvested || kind.Class == NaturalClass.Boulder)
        {
            (Vector3 centre, float r) = Rock(o, kind);
            return new Combat.HitShape(centre, centre, r, Content.HitRegion.Body);
        }
        Vector3 middle = Middle(o, kind);
        return new Combat.HitShape(middle, middle, kind.Radius * o.Scale, Content.HitRegion.Body);
    }

    /// <summary>Full health of an object (trees and outcrops).</summary>
    public static float MaxHealth(in NaturalObject o, NaturalKind kind) => kind.Health * o.Scale;

    /// <summary>The whole yield of an object of a size (trees and outcrops), or one pick (pickups and bushes).</summary>
    public static int Total(in NaturalObject o, NaturalKind kind, ItemAmount yield) =>
        kind.IsHarvested ? (int)System.Math.Round(yield.Count * o.Scale) : yield.Count;
}

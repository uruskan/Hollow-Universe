using System.Collections.Generic;
using System.Numerics;
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
    /// Adds the objects whose base lies in [<paramref name="minX"/>, <paramref name="maxX"/>) ×
    /// [<paramref name="minZ"/>, <paramref name="maxZ"/>) of the zone's tangent plane, always in the same order.
    /// </summary>
    void Query(float minX, float minZ, float maxX, float maxZ, List<NaturalObject> into);

    /// <summary>The object with an ID (from outside: untrusted, so a made-up one is simply not found).</summary>
    bool TryGet(ulong id, out NaturalObject found);

    /// <summary>
    /// The object with an ID if it may lie within <paramref name="radius"/> of a point: an untrusted ID that names a
    /// place far away is refused before any work is done for it (a player's pick-up request).
    /// </summary>
    bool TryGetNear(ulong id, Vector3 point, float radius, out NaturalObject found) =>
        TryGet(id, out found) && Vector3.Distance(found.Position, point) <= radius;
}

/// <summary>Shapes and derived numbers of natural objects, shared by the server, the client and the physics.</summary>
public static class NaturalShapes
{
    /// <summary>A tree's trunk: a vertical capsule from its base up.</summary>
    public static (Vector3 A, Vector3 B, float Radius) Trunk(in NaturalObject o, NaturalKind kind)
    {
        float radius = kind.Radius * o.Scale;
        float height = kind.Height * o.Scale;
        return (o.Position + o.Up * radius, o.Position + o.Up * (height - radius), radius);
    }

    /// <summary>
    /// An outcrop: a sphere a third sunk in the ground. A boulder: a sphere mostly buried (its top 0.65 of its radius
    /// above the ground), as the planet draws its rocks.
    /// </summary>
    public static (Vector3 Centre, float Radius) Rock(in NaturalObject o, NaturalKind kind)
    {
        float radius = kind.Radius * o.Scale;
        float lift = kind.Class == NaturalClass.Boulder ? -0.35f : 0.35f;
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

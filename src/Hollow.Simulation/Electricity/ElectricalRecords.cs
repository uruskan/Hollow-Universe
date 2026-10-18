using System;
using System.Numerics;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Simulation.Electricity;

public enum PowerPortDirection : byte { Input, Output }
public enum PowerTransfer : byte { Generator, Consumer, Storage }

/// <summary>Stable authored connection point. The cube and authoritative pick test use the same local bounds.</summary>
public sealed class PowerPort
{
    public ushort Id { get; init; }
    public string Key { get; init; } = "";
    public PowerPortDirection Direction { get; init; }
    public string Socket { get; init; } = "";
    public Vector3 Position { get; init; }
    public float Size { get; init; } = .12f;
}

public readonly record struct PortRef(ulong EntityId, ushort PortId);

/// <summary>Decorative support-local route point; never a circuit junction or an extension of endpoint range.</summary>
public readonly record struct RouteAnchor(TargetRef Support, Vector3 Position);

/// <summary>One durable directed edge. Port IDs are definition identities, not array indexes.</summary>
public sealed record CableRecord(ulong Id, PortRef Output, PortRef Input, RouteAnchor[] Anchors, uint Revision = 1);

public static class PowerPorts
{
    public const float MaximumSpan = 20;
    public const float SpanTolerance = .001f;
    public const int MaximumAnchors = 32;

    public static Vector3 Position(Deployable node, PowerPort port)
    {
        return Position(node.Position, node.Up, node.Yaw, port);
    }
    public static Vector3 Position(Vector3 position, Vector3 up, float yaw, PowerPort port) =>
        position + Vector3.Transform(port.Position, Combat.Geometry.Upright(up, yaw));

    /// <summary>Exact physical cube picking shared by local preview and authoritative tool validation.</summary>
    public static float Ray(Vector3 origin, Vector3 direction, float length, Vector3 position, Vector3 up, float yaw, PowerPort port)
    {
        Quaternion rotation = Combat.Geometry.Upright(up, yaw);
        return Combat.Geometry.RayBox(origin, direction, length, Position(position, up, yaw, port), new Vector3(port.Size * .5f),
            Vector3.Transform(Vector3.UnitX, rotation), Vector3.Transform(Vector3.UnitY, rotation), Vector3.Transform(Vector3.UnitZ, rotation));
    }
    public static float Ray(Vector3 origin, Vector3 direction, float length, Deployable node, PowerPort port) =>
        Ray(origin, direction, length, node.Position, node.Up, node.Yaw, port);

    public static bool Finite(Vector3 point) => float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);

    public static bool ValidAnchors(System.Collections.Generic.IReadOnlyList<RouteAnchor> anchors)
    {
        if (anchors.Count > MaximumAnchors) return false;
        foreach (RouteAnchor anchor in anchors)
            if (!Finite(anchor.Position) || Math.Abs(anchor.Position.X) > 1_000_000 || Math.Abs(anchor.Position.Y) > 1_000_000 || Math.Abs(anchor.Position.Z) > 1_000_000 ||
                anchor.Support.Family is not (TargetFamily.Ground or TargetFamily.Thing or TargetFamily.Piece or TargetFamily.Door or TargetFamily.Cupboard or TargetFamily.Natural) ||
                (anchor.Support.Family == TargetFamily.Ground ? anchor.Support.Id != 0 || anchor.Support.Part != 0 : anchor.Support.Id == 0)) return false;
        return true;
    }
}

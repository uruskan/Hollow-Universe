using System;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Hollow.Simulation.Building;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;

namespace Hollow.Simulation.Physics;

/// <summary>The same authored hulls drive movement and weapon traces, including a gate's open leaves.</summary>
public static class CompoundGeometry
{
    private sealed record Shapes(ConvexPart[] Closed, ConvexPart[] Open);
    private static readonly ConditionalWeakTable<DeployableDef, Shapes> Cache = new();

    public static ConvexPart[] Parts(GameContent content, DeployableDef def, bool open)
    {
        Shapes shapes = Cache.GetValue(def, d =>
        {
            CompoundAssembly? assembly = content.Compound.Find(d.Assembly);
            if (assembly == null) return new Shapes(Array.Empty<ConvexPart>(), Array.Empty<ConvexPart>());
            ConvexPart[] closed = assembly.Hulls.Select(h => h.Shape).ToArray();
            ConvexPart[] opened = assembly.Hulls.Select(h =>
            {
                CompoundGatePart moving = d.Gate?.Parts.FirstOrDefault(p => p.Part == h.Part) ?? default;
                if (moving.OpenYaw == 0 || assembly.Part(h.Part) is not { } part) return h.Shape;
                Quaternion turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, moving.OpenYaw * MathF.PI / 180);
                return new ConvexPart(h.Points.Select(p => part.Pivot + Vector3.Transform(p - part.Pivot, turn)).ToArray());
            }).ToArray();
            return new Shapes(closed, opened);
        });
        return open ? shapes.Open : shapes.Closed;
    }

    public static PropShape Shape(GameContent content, DeployableDef def, Vector3 at, Vector3 up, float yaw, bool open)
    {
        ConvexPart[] parts = Parts(content, def, open);
        return parts.Length > 0 ? PropShape.Compound(at, Geometry.Upright(up, yaw), parts)
            : PropShape.Box(at + up * def.HalfExtents.Y, def.HalfExtents, up, yaw);
    }

    public static float Ray(GameContent content, DeployableDef def, bool open, Vector3 at, Vector3 up, float yaw,
        Vector3 origin, Vector3 direction, float length, float thickness) =>
        Ray(content, def, open, at, up, yaw, origin, direction, length, thickness, out _);

    /// <summary>The contacted authored face, including its outward normal for surface-mounted objects.</summary>
    public static float Ray(GameContent content, DeployableDef def, bool open, Vector3 at, Vector3 up, float yaw,
        Vector3 origin, Vector3 direction, float length, float thickness, out Vector3 hitNormal)
        => Ray(content, def, open, at, up, yaw, origin, direction, length, thickness, out hitNormal, out _);

    public static float Ray(GameContent content, DeployableDef def, bool open, Vector3 at, Vector3 up, float yaw,
        Vector3 origin, Vector3 direction, float length, float thickness, out Vector3 hitNormal, out byte hitPart)
    {
        hitPart = 0;
        hitNormal = -direction;
        ConvexPart[] parts = Parts(content, def, open);
        if (parts.Length == 0)
        {
            (Vector3 right, Vector3 forward) = Geometry.Axes(up, yaw);
            Vector3 half = def.HalfExtents + new Vector3(thickness), centre = at + up * def.HalfExtents.Y;
            float distance = Geometry.RayBox(origin, direction, length, centre, half, right, up, forward);
            if (distance < 0) return distance;
            Vector3 localPoint = origin + direction * distance - centre;
            float x = Vector3.Dot(localPoint, right) / half.X, y = Vector3.Dot(localPoint, up) / half.Y, z = Vector3.Dot(localPoint, forward) / half.Z;
            hitNormal = MathF.Abs(x) >= MathF.Abs(y) && MathF.Abs(x) >= MathF.Abs(z) ? right * MathF.Sign(x)
                : MathF.Abs(y) >= MathF.Abs(z) ? up * MathF.Sign(y) : forward * MathF.Sign(z);
            return distance;
        }
        Quaternion rotation = Geometry.Upright(up, yaw), inverse = Quaternion.Conjugate(rotation);
        Vector3 local = Vector3.Transform(origin - at, inverse), ray = Vector3.Transform(direction, inverse);
        float closest = float.PositiveInfinity;
        for (int hullIndex = 0; hullIndex < parts.Length; hullIndex++)
        {
            ConvexPart part = parts[hullIndex];
            float enter = 0, exit = length;
            Vector3 enterNormal = -ray;
            foreach ((Vector3 normal, float distance) in part.Planes)
            {
                float dot = Vector3.Dot(normal, ray), gap = distance + thickness - Vector3.Dot(normal, local);
                if (MathF.Abs(dot) < 1e-7f) { if (gap < 0) { exit = -1; break; } continue; }
                float t = gap / dot;
                if (dot < 0)
                {
                    if (t > enter) { enter = t; enterNormal = normal; }
                }
                else exit = MathF.Min(exit, t);
                if (enter > exit) break;
            }
            if (enter <= exit && enter < closest)
            {
                closest = enter;
                hitNormal = Vector3.Transform(enterNormal, rotation);
                if (content.Compound.Find(def.Assembly) is { } assembly && hullIndex < assembly.Hulls.Count)
                    for (int index = 0; index < assembly.Parts.Count && index < 255; index++)
                        if (assembly.Parts[index].Name == assembly.Hulls[hullIndex].Part) { hitPart = (byte)(index + 1); break; }
            }
        }
        return float.IsFinite(closest) ? closest : -1;
    }

    /// <summary>A contacted authored part's pivot and orientation, shared by authority and client attachments.</summary>
    public static bool TryPartPose(GameContent content, DeployableDef def, bool open, Vector3 at, Vector3 up, float yaw,
        byte partId, out Targets.TargetPose pose)
    {
        Quaternion root = Geometry.Upright(up, yaw);
        pose = new Targets.TargetPose(at, root);
        if (partId == 0) return true;
        if (content.Compound.Find(def.Assembly) is not { } assembly || partId > assembly.Parts.Count) return false;
        CompoundPart part = assembly.Parts[partId - 1];
        float degrees = open ? def.Gate?.Parts.FirstOrDefault(p => p.Part == part.Name).OpenYaw ?? 0 : 0;
        Quaternion turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, degrees * MathF.PI / 180);
        pose = new Targets.TargetPose(at + Vector3.Transform(part.Pivot, root), Quaternion.Normalize(root * turn));
        return true;
    }

    public static Vector3 Nearest(GameContent content, DeployableDef def, bool open, Vector3 at, Vector3 up, float yaw, Vector3 from)
    {
        ConvexPart[] parts = Parts(content, def, open);
        if (parts.Length == 0) return Geometry.NearestOnBox(at, up, yaw, def.HalfExtents, from);
        Quaternion turn = Geometry.Upright(up, yaw);
        Vector3 local = Vector3.Transform(from - at, Quaternion.Conjugate(turn));
        Vector3 best = local; float distance = float.PositiveInfinity;
        foreach (var part in parts)
        {
            Vector3 candidate = local;
            for (int pass = 0; pass < 12; pass++)
            {
                float moved = 0;
                foreach ((Vector3 normal, float plane) in part.Planes)
                {
                    float outside = Vector3.Dot(normal, candidate) - plane;
                    if (outside <= 0) continue;
                    candidate -= normal * outside;
                    moved = MathF.Max(moved, outside);
                }
                if (moved < .0001f) break;
            }
            float d = Vector3.DistanceSquared(local, candidate);
            if (d < distance) { distance = d; best = candidate; }
        }
        return at + Vector3.Transform(best, turn);
    }
}

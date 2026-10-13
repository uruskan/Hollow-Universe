using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;

namespace Hollow.Simulation.Combat;

/// <summary>How a body lies, for its hit shapes.</summary>
public enum Pose : byte
{
    Standing,
    Crouching,

    /// <summary>Asleep on the ground.</summary>
    Lying,
}

/// <summary>Where a character's body was at one moment: enough to build its hit shapes.</summary>
public struct HitPose
{
    public Vector3 Feet;
    public Vector3 Up;
    public float Yaw;
    public Pose Pose;
}

/// <summary>
/// One of a body's hit shapes: a capsule from <see cref="A"/> to <see cref="B"/> (a sphere when they are the same point),
/// thick <see cref="Radius"/>, on a region of the body. Its index in the body's list is the bone a support link names
/// (GROUND_SYSTEMS.md §3.6): an arrow in a deer's flank rides on the flank's shape.
/// </summary>
public readonly record struct HitShape(Vector3 A, Vector3 B, float Radius, HitRegion Region)
{
    /// <summary>The nearest contact of a ray, thickened, with the shape (negative for a miss).</summary>
    public float Raycast(Vector3 origin, Vector3 direction, float length, float thickness) =>
        A == B ? Geometry.RaySphere(origin, direction, length, A, Radius + thickness) : Geometry.RayCapsule(origin, direction, length, A, B, Radius + thickness);

    /// <summary>The point of the shape's surface nearest to a point (the point itself when it is inside).</summary>
    public Vector3 Nearest(Vector3 point)
    {
        Vector3 ab = B - A;
        float lengthSquared = ab.LengthSquared();
        float t = lengthSquared < 1e-12f ? 0 : Math.Clamp(Vector3.Dot(point - A, ab) / lengthSquared, 0, 1);
        Vector3 axis = A + ab * t;
        Vector3 off = point - axis;
        float d = off.Length();
        return d <= Radius ? point : axis + off * (Radius / d);
    }

    /// <summary>The shape's middle.</summary>
    public Vector3 Middle => (A + B) / 2;
}

/// <summary>
/// A character's hit shapes (GROUND.md §6): a sphere for the head, a capsule for the body, capsules for the legs and
/// arms, from its pose. Rays (a melee sweep, an arrow's path) are tested against them; the nearest contact says where
/// the hit landed, and on which shape (the bone a stuck arrow rides on).
/// </summary>
public static class Hitboxes
{
    private const float StandingHeight = 1.76f;

    /// <summary>Shapes a body has at most.</summary>
    public const int MostShapes = 6;

    /// <summary>
    /// A body's shapes at a pose, in a fixed order (standing or crouching: head, body, right leg, left leg, right arm,
    /// left arm; lying: head, body, legs): how many went into <paramref name="into"/>.
    /// </summary>
    public static int Shapes(in HitPose pose, Span<HitShape> into)
    {
        (Vector3 forward, Vector3 right) = CharacterMotor.HorizontalBasis(pose.Yaw, pose.Up);
        Vector3 up = pose.Up, f = pose.Feet;
        if (pose.Pose == Pose.Lying)
        {
            Vector3 head = f + forward * 0.78f + up * 0.16f;
            into[0] = new HitShape(head, head, 0.13f, HitRegion.Head);
            into[1] = new HitShape(f + forward * -0.15f + up * 0.2f, f + forward * 0.55f + up * 0.2f, 0.22f, HitRegion.Body);
            into[2] = new HitShape(f + forward * -0.95f + up * 0.13f, f + forward * -0.2f + up * 0.13f, 0.16f, HitRegion.Limb);
            return 3;
        }
        float s = pose.Pose == Pose.Crouching ? 1.10f / StandingHeight : 1f;
        Vector3 top = f + up * (1.60f * s);
        into[0] = new HitShape(top, top, 0.13f, HitRegion.Head);
        into[1] = new HitShape(f + up * (0.98f * s), f + up * (1.38f * s), 0.21f, HitRegion.Body);
        into[2] = new HitShape(f + right * 0.11f + up * (0.10f * s), f + right * 0.11f + up * (0.88f * s), 0.11f, HitRegion.Limb);
        into[3] = new HitShape(f - right * 0.11f + up * (0.10f * s), f - right * 0.11f + up * (0.88f * s), 0.11f, HitRegion.Limb);
        into[4] = new HitShape(f + right * 0.30f + up * (0.95f * s), f + right * 0.30f + up * (1.36f * s), 0.07f, HitRegion.Limb);
        into[5] = new HitShape(f - right * 0.30f + up * (0.95f * s), f - right * 0.30f + up * (1.36f * s), 0.07f, HitRegion.Limb);
        return 6;
    }

    /// <summary>
    /// The nearest contact of a ray, thickened by <paramref name="thickness"/>, with a body at a pose: the distance
    /// along the ray (negative for a miss), the region it landed on and the shape (its index in <see cref="Shapes"/>).
    /// </summary>
    public static float Raycast(in HitPose pose, Vector3 origin, Vector3 direction, float length, float thickness, out HitRegion region, out int shape)
    {
        Span<HitShape> shapes = stackalloc HitShape[MostShapes];
        return Nearest(shapes[..Shapes(pose, shapes)], origin, direction, length, thickness, out region, out shape);
    }

    public static float Raycast(in HitPose pose, Vector3 origin, Vector3 direction, float length, float thickness, out HitRegion region) =>
        Raycast(pose, origin, direction, length, thickness, out region, out _);

    /// <summary>The first contact of a ray with any of some shapes: its distance (negative for none), region and index.</summary>
    public static float Nearest(ReadOnlySpan<HitShape> shapes, Vector3 origin, Vector3 direction, float length, float thickness, out HitRegion region,
        out int shape)
    {
        region = HitRegion.None;
        shape = -1;
        float best = -1;
        for (int i = 0; i < shapes.Length; i++)
        {
            float t = shapes[i].Raycast(origin, direction, length, thickness);
            if (t >= 0 && (best < 0 || t < best))
            {
                best = t;
                region = shapes[i].Region;
                shape = i;
            }
        }
        return best;
    }

    /// <summary>The point of any of some shapes nearest to a point.</summary>
    public static Vector3 NearestPoint(ReadOnlySpan<HitShape> shapes, Vector3 point)
    {
        Vector3 best = point;
        float nearest = float.MaxValue;
        foreach (HitShape s in shapes)
        {
            Vector3 p = s.Nearest(point);
            float d = Vector3.DistanceSquared(p, point);
            if (d < nearest)
            {
                nearest = d;
                best = p;
            }
        }
        return best;
    }

    /// <summary>
    /// Where one of a body's shapes (a bone) is: its middle, turned with the body (its yaw about its up). What a support
    /// link to a body part rides on.
    /// </summary>
    public static Targets.TargetPose ShapePose(ReadOnlySpan<HitShape> shapes, int shape, in HitPose pose) =>
        new(shapes[Math.Clamp(shape, 0, shapes.Length - 1)].Middle, Geometry.Upright(pose.Up, pose.Yaw));
}

/// <summary>
/// One second of every character's poses, a sample a tick (ARCHITECTURE.md §11.5): hits are tested against where
/// others were at the moment the attacker saw them, interpolated between ticks as the client draws them. Built to be
/// reused by guns (G4).
/// </summary>
public sealed class HitboxHistory
{
    /// <summary>Samples kept per character: a second and a little more.</summary>
    public const int Length = 36;

    private readonly Dictionary<EntityId, Track> _tracks = new();

    public void Record(EntityId id, long tick, in HitPose pose)
    {
        if (!_tracks.TryGetValue(id, out Track? track))
        {
            track = new Track();
            _tracks.Add(id, track);
        }
        track.Add(tick, pose);
    }

    public void Forget(EntityId id) => _tracks.Remove(id);

    /// <summary>
    /// A character's pose at a (fractional) tick, between the samples on either side; the oldest or newest kept when
    /// outside them. False if the character has no samples.
    /// </summary>
    public bool Sample(EntityId id, double tick, out HitPose pose)
    {
        pose = default;
        return _tracks.TryGetValue(id, out Track? track) && track.Sample(tick, out pose);
    }

    public IEnumerable<EntityId> Characters => _tracks.Keys;

    /// <summary>A character's newest recorded feet (a cheap first test before sampling); false with no samples.</summary>
    public bool TryNewest(EntityId id, out Vector3 feet)
    {
        feet = default;
        if (!_tracks.TryGetValue(id, out Track? track) || !track.TryNewest(out HitPose pose))
        {
            return false;
        }
        feet = pose.Feet;
        return true;
    }

    private sealed class Track
    {
        private readonly long[] _ticks = new long[Length];
        private readonly HitPose[] _poses = new HitPose[Length];
        private int _count;
        private int _newest = -1;

        public void Add(long tick, in HitPose pose)
        {
            if (_count > 0 && tick <= _ticks[_newest])
            {
                _poses[_newest] = pose;
                return;
            }
            _newest = (_newest + 1) % Length;
            _ticks[_newest] = tick;
            _poses[_newest] = pose;
            _count = Math.Min(Length, _count + 1);
        }

        public bool TryNewest(out HitPose pose)
        {
            pose = _count > 0 ? _poses[_newest] : default;
            return _count > 0;
        }

        public bool Sample(double tick, out HitPose pose)
        {
            pose = default;
            if (_count == 0)
            {
                return false;
            }
            int after = -1;
            for (int i = 0; i < _count; i++)
            {
                int index = (_newest - i + Length) % Length;
                if (_ticks[index] <= tick)
                {
                    if (after < 0 || _ticks[index] == tick)
                    {
                        pose = _poses[index];
                        return true;
                    }
                    double t = (tick - _ticks[index]) / (_ticks[after] - _ticks[index]);
                    HitPose a = _poses[index], b = _poses[after];
                    pose = b;
                    pose.Feet = Vector3.Lerp(a.Feet, b.Feet, (float)t);
                    return true;
                }
                after = index;
            }
            pose = _poses[after];
            return true;
        }
    }
}

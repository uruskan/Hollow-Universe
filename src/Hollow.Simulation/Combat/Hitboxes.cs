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
/// A character's hit shapes (GROUND.md §6): a sphere for the head, a capsule for the body, capsules for the legs and
/// arms, from its pose. Rays (a melee sweep, an arrow's path) are tested against them; the nearest contact says where
/// the hit landed.
/// </summary>
public static class Hitboxes
{
    private const float StandingHeight = 1.76f;

    /// <summary>
    /// The nearest contact of a ray, thickened by <paramref name="thickness"/>, with a body at a pose: the distance
    /// along the ray (negative for a miss) and the region it landed on.
    /// </summary>
    public static float Raycast(in HitPose pose, Vector3 origin, Vector3 direction, float length, float thickness, out HitRegion region)
    {
        HitRegion found = HitRegion.None;
        float best = -1;
        (Vector3 forward, Vector3 right) = CharacterMotor.HorizontalBasis(pose.Yaw, pose.Up);
        Vector3 up = pose.Up, f = pose.Feet;
        void Sphere(Vector3 centre, float radius, HitRegion r)
        {
            float t = Geometry.RaySphere(origin, direction, length, centre, radius + thickness);
            if (t >= 0 && (best < 0 || t < best))
            {
                best = t;
                found = r;
            }
        }
        void Capsule(Vector3 a, Vector3 b, float radius, HitRegion r)
        {
            float t = Geometry.RayCapsule(origin, direction, length, a, b, radius + thickness);
            if (t >= 0 && (best < 0 || t < best))
            {
                best = t;
                found = r;
            }
        }
        if (pose.Pose == Pose.Lying)
        {
            Sphere(f + forward * 0.78f + up * 0.16f, 0.13f, HitRegion.Head);
            Capsule(f + forward * -0.15f + up * 0.2f, f + forward * 0.55f + up * 0.2f, 0.22f, HitRegion.Body);
            Capsule(f + forward * -0.95f + up * 0.13f, f + forward * -0.2f + up * 0.13f, 0.16f, HitRegion.Limb);
            region = found;
            return best;
        }
        float s = pose.Pose == Pose.Crouching ? 1.10f / StandingHeight : 1f;
        Sphere(f + up * (1.60f * s), 0.13f, HitRegion.Head);
        Capsule(f + up * (0.98f * s), f + up * (1.38f * s), 0.21f, HitRegion.Body);
        Capsule(f + right * 0.11f + up * (0.10f * s), f + right * 0.11f + up * (0.88f * s), 0.11f, HitRegion.Limb);
        Capsule(f - right * 0.11f + up * (0.10f * s), f - right * 0.11f + up * (0.88f * s), 0.11f, HitRegion.Limb);
        Capsule(f + right * 0.30f + up * (0.95f * s), f + right * 0.30f + up * (1.36f * s), 0.07f, HitRegion.Limb);
        Capsule(f - right * 0.30f + up * (0.95f * s), f - right * 0.30f + up * (1.36f * s), 0.07f, HitRegion.Limb);
        region = found;
        return best;
    }
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

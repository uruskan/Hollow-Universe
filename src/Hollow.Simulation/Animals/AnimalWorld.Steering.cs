using System;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Simulation.Movement;

namespace Hollow.Simulation.Animals;

// Steering (GROUND.md §6.9): a decision becomes a heading and a speed. Context steering: sixteen headings around the one
// wanted are scored by how well they go where the animal wants and how soon they meet water, a cliff, a trunk, a rock,
// a wall or a box (from the navigation grid, and the zone for what is built), and how close they pass other animals.
// A short A* path on the grid takes over when the straight way is blocked. Headings are measured from local north and
// directions laid on the ground at the animal (docs/WHOLE_WORLD.md §7.3).
public sealed partial class AnimalWorld
{
    private const int Headings = 16;

    /// <summary>Steers towards a point at a speed (around obstacles, along a path when the way is blocked).</summary>
    private void Steer(Animal a, Vector3 goal, float speed, bool path = true)
    {
        AnimalSpecies s = a.Species;
        Vector3 up = UpAt(a.Position);
        Vector3 flat = OnGround(goal - a.Position, up);
        float distance = flat.Length();
        if (distance < 0.35f || speed <= 0)
        {
            a.DesiredSpeed = 0;
            return;
        }
        Vector3 aim = goal;
        if (path && Nav.IsReady(a.Position))
        {
            float probe = MathF.Min(distance, 24);
            Vector3 end = a.Position + flat / distance * probe;
            if (!Nav.Clear(a.Position, end, s.MaxSlope))
            {
                if (!a.HasWaypoint || Tick - a.WaypointTick > 20 || OnGround(a.Waypoint - a.Position, up).Length() < 1.5f)
                {
                    a.HasWaypoint = Nav.FindPath(a.Position, goal, s.MaxSlope, out Vector3 next);
                    a.Waypoint = next;
                    a.WaypointTick = Tick;
                }
                if (a.HasWaypoint)
                {
                    aim = a.Waypoint;
                }
            }
            else
            {
                a.HasWaypoint = false;
            }
        }
        Vector3 toward = OnGround(aim - a.Position, up);
        if (toward.LengthSquared() < 1e-4f)
        {
            toward = flat;
        }
        // Slow down arriving.
        float arrive = MathF.Min(speed, MathF.Max(s.WalkSpeed * 0.6f, distance * 1.2f));
        ContextSteer(a, YawOf(toward, up), arrive);
    }

    private void ContextSteer(Animal a, float wanted, float speed)
    {
        if (!Nav.IsReady(a.Position))
        {
            a.DesiredYaw = wanted;
            a.DesiredSpeed = speed;
            return;
        }
        float look = Math.Clamp(speed * 0.9f, 2.5f, 9f);
        bool built = _host.ObstaclesNear(a.Position, look + 4);
        Near(a.Position, 3.5f, _near);
        float bestScore = float.MinValue, bestYaw = wanted, bestDanger = 0;
        for (int i = 0; i < Headings; i++)
        {
            // 0, +1, −1, +2, −2 …: ties go to the heading nearest the one wanted.
            int k = (i + 1) / 2 * (i % 2 == 0 ? -1 : 1);
            float offset = k * (MathF.PI * 2 / Headings);
            float yaw = WrapAngle(wanted + offset);
            float interest = 0.5f + 0.5f * (float)DMath.Cos(offset);
            float danger = Danger(a, yaw, look, built);
            float score = interest - 1.3f * danger;
            if (score > bestScore)
            {
                bestScore = score;
                bestYaw = yaw;
                bestDanger = danger;
            }
        }
        a.DesiredYaw = bestYaw;
        a.DesiredSpeed = speed * Math.Clamp(1.15f - bestDanger, 0.3f, 1f);
    }

    // How soon a heading meets something: 1 right ahead, 0 nothing within the look-ahead; other animals close by along it.
    private float Danger(Animal a, float yaw, float look, bool built)
    {
        AnimalSpecies s = a.Species;
        Vector3 up = UpAt(a.Position);
        (Vector3 direction, _) = Facing.Basis(yaw, up);
        float danger = 0;
        for (float d = 1; d <= look + 1e-3f; d += 1)
        {
            if (!Nav.Walkable(a.Position + direction * d, s.MaxSlope))
            {
                danger = 1 - (d - 1) / look;
                break;
            }
        }
        if (built && danger < 1)
        {
            float hit = _host.ObstacleAlong(a.Position + up * MathF.Min(0.6f, a.Species.Height * 0.5f), direction, look);
            if (hit >= 0)
            {
                danger = MathF.Max(danger, 1 - hit / look);
            }
        }
        foreach (Animal other in _near)
        {
            if (other == a || other.IsDead || other.Removed)
            {
                continue;
            }
            Vector3 d = OnGround(other.Position - a.Position, up);
            float distance = d.Length();
            float room = a.Species.Radius + other.Species.Radius + 0.8f;
            if (distance < 1e-3f || distance > room + 1.5f)
            {
                continue;
            }
            float along = Vector3.Dot(d, direction) / distance;
            if (along > 0)
            {
                float closeness = Math.Clamp(1 - (distance - room) / 1.5f, 0, 1);
                danger = MathF.Max(danger, closeness * (0.25f + 0.6f * along));
            }
        }
        return Math.Clamp(danger, 0, 1);
    }

    // Away from the threat, held towards the herd when it strays, around what is in the way.
    private void FleeSteer(Animal a, Vector3 from)
    {
        AnimalSpecies s = a.Species;
        Vector3 up = UpAt(a.Position);
        Vector3 away = OnGround(a.Position - from, up);
        float length = away.Length();
        if (length < 0.1f)
        {
            away = Facing.Basis(a.Yaw, up).Forward;
        }
        else
        {
            away /= length;
        }
        Vector3 toCentre = OnGround(a.Herd.Centre() - a.Position, up);
        float apart = toCentre.Length();
        if (apart > s.Cohesion * 0.6f)
        {
            away = Vector3.Normalize(away + toCentre / apart * MathF.Min(0.8f, (apart - s.Cohesion * 0.6f) / s.Cohesion));
        }
        // Leaving its home range far behind: bend back towards it.
        Vector3 home = OnGround(a.Herd.Home - a.Position, up);
        float out_ = home.Length();
        if (a.Herd.Range > 0 && out_ > a.Herd.Range * 1.5f)
        {
            away = Vector3.Normalize(away + home / out_ * 0.5f);
        }
        if (a.Stuck > 1.5f)
        {
            // Cornered (water, a cliff): break away to one side.
            float side = (a.Seed & 1) == 0 ? 1.4f : -1.4f;
            away = Facing.Basis(YawOf(away, up) + side, up).Forward;
        }
        float speed = length < s.FleeDistance * 0.6f || a.HealthFraction < 0.5f ? s.RunSpeed : MathF.Max(s.TrotSpeed, s.RunSpeed * 0.8f);
        ContextSteer(a, YawOf(away, up), speed);
    }

    // At the target, leading it a little; a stalking hunter trots in until it is close; a pack mate that must wait its
    // turn circles at a few metres.
    private void ChaseSteer(Animal a, Vector3 at)
    {
        AnimalSpecies s = a.Species;
        AttackStats attack = s.Attack!;
        float distance = Flat(a, at);
        Vector3 velocity = TargetVelocity(a.Target, UpAt(a.Position));
        float lead = MathF.Min(distance / MathF.Max(1, s.RunSpeed), 0.8f);
        Vector3 goal = at + velocity * lead;
        float speed = s.RunSpeed;
        bool provoked = Tick - a.HurtTick < CharacterTicks.From(10);
        if (s.Temperament == Temperament.Hunt && !provoked && distance > 24)
        {
            speed = s.TrotSpeed; // stalking
        }
        if (distance < 6 && !MayAttack(a))
        {
            // Wait its turn: circle the prey.
            float angle = a.Slot * 2.1f + (Tick % 600) * 0.012f;
            (Vector3 north, Vector3 east) = Facing.Compass(UpAt(at));
            goal = at + (east * (float)DMath.Cos(angle) + north * (float)DMath.Sin(angle)) * 4.2f;
            speed = s.TrotSpeed;
        }
        if (distance < attack.Reach * 0.7f + s.Length * 0.4f)
        {
            Face(a, at);
            return;
        }
        Steer(a, goal, speed);
    }

    private void AttackSteer(Animal a, Vector3 at)
    {
        AnimalSpecies s = a.Species;
        float distance = Flat(a, at);
        switch (s.Attack!.Kind)
        {
            case AttackKind.Bite:
                // A lunge.
                if (distance > s.Length * 0.5f + 0.4f)
                {
                    Steer(a, at, MathF.Min(s.TrotSpeed * 1.2f, 4.5f), path: false);
                }
                else
                {
                    Face(a, at);
                }
                break;
            case AttackKind.Charge:
                // Runs on through.
                Steer(a, at + (at - a.Position) * 0.3f, s.RunSpeed, path: false);
                break;
            default:
                // A bear stands its ground and swipes.
                Face(a, at);
                break;
        }
    }

    // A target's velocity laid on the ground at the animal (its up).
    private Vector3 TargetVelocity(AnimalTarget target, Vector3 up)
    {
        if (target.Kind == TargetKind.Player)
        {
            foreach (AnimalObserver o in Observers)
            {
                if (o.Id == target.Id)
                {
                    return OnGround(o.Velocity, up);
                }
            }
        }
        else if (target.Kind == TargetKind.Animal && Find(target.Id) is { } other)
        {
            return Facing.Basis(other.Yaw, UpAt(other.Position)).Forward * other.Speed;
        }
        return Vector3.Zero;
    }

    /// <summary>A vector laid onto the ground (the plane across <paramref name="up"/>).</summary>
    private static Vector3 OnGround(Vector3 v, Vector3 up) => v - up * Vector3.Dot(v, up);

    /// <summary>How far a point is from an animal along its ground.</summary>
    private float Flat(Animal a, Vector3 point) => OnGround(point - a.Position, UpAt(a.Position)).Length();
}

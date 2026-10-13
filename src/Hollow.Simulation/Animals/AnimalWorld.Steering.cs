using System;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Simulation.Movement;

namespace Hollow.Simulation.Animals;

// Steering (GROUND.md §6.9): a decision becomes a heading and a speed. Context steering: sixteen headings around the one
// wanted are scored by how well they go where the animal wants and how soon they meet water, a cliff, a trunk, a rock,
// a wall or a box (from the navigation grid, and the zone for what is built), and how close they pass other animals.
// A short A* path on the grid takes over when the straight way is blocked.
public sealed partial class AnimalWorld
{
    private const int Headings = 16;

    /// <summary>Steers towards a point at a speed (around obstacles, along a path when the way is blocked).</summary>
    private void Steer(Animal a, Vector3 goal, float speed, bool path = true)
    {
        AnimalSpecies s = a.Species;
        float dx = goal.X - a.Position.X, dz = goal.Z - a.Position.Z;
        float distance = MathF.Sqrt(dx * dx + dz * dz);
        if (distance < 0.35f || speed <= 0)
        {
            a.DesiredSpeed = 0;
            return;
        }
        Vector3 aim = goal;
        if (path && Nav.IsReady(a.Position.X, a.Position.Z))
        {
            float probe = MathF.Min(distance, 24);
            Vector3 end = a.Position + new Vector3(dx, 0, dz) / distance * probe;
            if (!Nav.Clear(a.Position, end, s.MaxSlope))
            {
                if (!a.HasWaypoint || Tick - a.WaypointTick > 20 || Flat(a.Waypoint - a.Position) < 1.5f)
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
        Vector3 toward = aim - a.Position;
        if (toward.X * toward.X + toward.Z * toward.Z < 1e-4f)
        {
            toward = new Vector3(dx, 0, dz);
        }
        // Slow down arriving.
        float arrive = MathF.Min(speed, MathF.Max(s.WalkSpeed * 0.6f, distance * 1.2f));
        ContextSteer(a, YawOf(toward), arrive);
    }

    private void ContextSteer(Animal a, float wanted, float speed)
    {
        AnimalSpecies s = a.Species;
        if (!Nav.IsReady(a.Position.X, a.Position.Z))
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
        float sin = (float)DMath.Sin(yaw), cos = (float)DMath.Cos(yaw);
        var direction = new Vector3(-sin, 0, -cos);
        float danger = 0;
        for (float d = 1; d <= look + 1e-3f; d += 1)
        {
            if (!Nav.Walkable(a.Position.X + direction.X * d, a.Position.Z + direction.Z * d, s.MaxSlope))
            {
                danger = 1 - (d - 1) / look;
                break;
            }
        }
        if (built && danger < 1)
        {
            Vector3 up = UpAt(a.Position);
            (Vector3 forward, _) = CharacterMotor.HorizontalBasis(yaw, up);
            float hit = _host.ObstacleAlong(a.Position + up * MathF.Min(0.6f, a.Species.Height * 0.5f), forward, look);
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
            Vector3 d = other.Position - a.Position;
            float distance = MathF.Sqrt(d.X * d.X + d.Z * d.Z);
            float room = a.Species.Radius + other.Species.Radius + 0.8f;
            if (distance < 1e-3f || distance > room + 1.5f)
            {
                continue;
            }
            float along = (d.X * direction.X + d.Z * direction.Z) / distance;
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
        Vector3 away = a.Position - from;
        away.Y = 0;
        float length = away.Length();
        if (length < 0.1f)
        {
            away = new Vector3((float)-DMath.Sin(a.Yaw), 0, (float)-DMath.Cos(a.Yaw));
        }
        else
        {
            away /= length;
        }
        Vector3 centre = a.Herd.Centre();
        Vector3 toCentre = centre - a.Position;
        toCentre.Y = 0;
        float apart = toCentre.Length();
        if (apart > s.Cohesion * 0.6f)
        {
            away = Vector3.Normalize(away + toCentre / apart * MathF.Min(0.8f, (apart - s.Cohesion * 0.6f) / s.Cohesion));
        }
        // Leaving its home range far behind: bend back towards it.
        Vector3 home = a.Herd.Home - a.Position;
        home.Y = 0;
        float out_ = home.Length();
        if (a.Herd.Range > 0 && out_ > a.Herd.Range * 1.5f)
        {
            away = Vector3.Normalize(away + home / out_ * 0.5f);
        }
        if (a.Stuck > 1.5f)
        {
            // Cornered (water, a cliff): break away to one side.
            float side = (a.Seed & 1) == 0 ? 1.4f : -1.4f;
            float yaw = YawOf(away) + side;
            away = new Vector3((float)-DMath.Sin(yaw), 0, (float)-DMath.Cos(yaw));
        }
        float speed = length < s.FleeDistance * 0.6f || a.HealthFraction < 0.5f ? s.RunSpeed : MathF.Max(s.TrotSpeed, s.RunSpeed * 0.8f);
        ContextSteer(a, YawOf(away), speed);
    }

    // At the target, leading it a little; a stalking hunter trots in until it is close; a pack mate that must wait its
    // turn circles at a few metres.
    private void ChaseSteer(Animal a, Vector3 at)
    {
        AnimalSpecies s = a.Species;
        AttackStats attack = s.Attack!;
        float distance = Flat(at - a.Position);
        Vector3 velocity = TargetVelocity(a.Target);
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
            goal = at + new Vector3((float)DMath.Cos(angle), 0, (float)DMath.Sin(angle)) * 4.2f;
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
        float distance = Flat(at - a.Position);
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

    private Vector3 TargetVelocity(AnimalTarget target)
    {
        if (target.Kind == TargetKind.Player)
        {
            foreach (AnimalObserver o in Observers)
            {
                if (o.Id == target.Id)
                {
                    return new Vector3(o.Velocity.X, 0, o.Velocity.Z);
                }
            }
        }
        else if (target.Kind == TargetKind.Animal && Find(target.Id) is { } other)
        {
            return new Vector3((float)-DMath.Sin(other.Yaw), 0, (float)-DMath.Cos(other.Yaw)) * other.Speed;
        }
        return Vector3.Zero;
    }

    private static float Flat(Vector3 d) => MathF.Sqrt(d.X * d.X + d.Z * d.Z);
}

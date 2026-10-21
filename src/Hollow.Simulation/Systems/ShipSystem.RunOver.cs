using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Building;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Players;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

public sealed partial class ShipSystem
{
    /// <summary>A person hit by a vehicle isn't hit by it again for this many ticks (a second).</summary>
    private const int RunOverTicks = CharacterTicks.Rate;

    private readonly Dictionary<(ulong Ship, ulong Player), long> _runOver = new();

    /// <summary>People run over this run, for statistics and tests.</summary>
    public long RunOvers { get; private set; }

    /// <summary>
    /// A vehicle on wheels running into people (VEHICLES.md §6.4): a person whose capsule its hull boxes meet, the box's
    /// point closing on them faster than the vehicle's <see cref="GroundDef.RunOverSpeed"/>, takes the square of what is
    /// over it times its <see cref="GroundDef.RunOverScale"/> through the characters' own receiver (armour, the wounded
    /// state, death), its driver's doing, and is thrown along with it. Nobody seated in it, nobody twice a second.
    /// </summary>
    private void RunOver(Ship ship)
    {
        if (ship.Def.Ground is not { } ground || ship.Destroyed || ship.State.Asleep ||
            ship.State.Velocity.LengthSquared() < ground.RunOverSpeed * ground.RunOverSpeed)
        {
            return;
        }
        ComponentPool<PlayerLink> players = Entities.Pool<PlayerLink>();
        Quaternion q = ship.State.Rotation, qi = Quaternion.Conjugate(q);
        Vector3 spin = Vector3.Transform(ship.State.AngularVelocity, q);
        float reach = ship.Def.BoundingRadius + 2.5f;
        Player? driver = Entities.TryFind(ship.Pilot, out Entity driving) ? PlayerOf(driving) : null;
        for (int i = 0; i < players.Count; i++)
        {
            Player person = players.Values[i].Player;
            Entity entity = Entities.AtSlot(players.Slots[i]);
            if (person.Dead || SeatOf(entity) == ship || _runOver.GetValueOrDefault((ship.Id.Value, person.Id.Value)) > Tick)
            {
                continue;
            }
            ref Character c = ref Entities.Get<Character>(entity);
            Vector3 feet = WorldOf(c.State), up = UpOf(c.State);
            if (Vector3.DistanceSquared(feet, ship.State.Position) > reach * reach)
            {
                continue;
            }
            float radius = Tuning.Radius, height = MathF.Max(radius * 2, Vector3.Dot(EyeOf(c.State) - feet, up) + 0.06f);
            if (!Touches(ship, qi, feet, up, height, radius, out Vector3 body, out Vector3 contact))
            {
                continue;
            }
            Vector3 toward = body - contact;
            toward = toward.LengthSquared() > 1e-6f ? Vector3.Normalize(toward) : Vector3.Normalize(ship.State.Velocity);
            Vector3 point = ship.State.Velocity + Vector3.Cross(spin, contact - ship.State.Position);
            float closing = Vector3.Dot(point, toward);
            if (closing < ground.RunOverSpeed)
            {
                continue;
            }
            _runOver[(ship.Id.Value, person.Id.Value)] = Tick + RunOverTicks;
            float over = closing - ground.RunOverSpeed;
            Damage.Damage(new DamageRequest
            {
                By = driver, Attacker = driver?.Id ?? EntityId.None, AttackerName = driver?.Name ?? ship.Def.Name, Weapon = ship.Def.Item.Id,
                Delivery = DamageDelivery.Melee, Channel = DamageType.Melee,
                Amounts = new DamageAmounts { Flesh = over * over * ground.RunOverScale }, Cause = "Run over", Direct = true,
                Hit = new TraceHit { Target = person.Ref, Region = HitRegion.Body, Point = contact },
                From = contact, Direction = toward, Markers = TargetFamilies.None,
            });
            // Thrown along with it and a little up, whatever is left of them.
            c.State.Velocity = point * 0.7f + up * MathF.Min(4, 1 + over * 0.2f);
            RunOvers++;
        }
        if (Tick % CharacterTicks.Rate == 0 && _runOver.Count > 0)
        {
            foreach ((ulong, ulong) key in new List<(ulong, ulong)>(_runOver.Keys))
            {
                if (_runOver[key] <= Tick)
                {
                    _runOver.Remove(key);
                }
            }
        }
    }

    // A person's capsule (its axis from the feet a radius up to the head a radius down) against a ship's hull boxes: the
    // body's point and the box's nearest point when they are within the radius.
    private static bool Touches(Ship ship, Quaternion qi, Vector3 feet, Vector3 up, float height, float radius, out Vector3 body, out Vector3 contact)
    {
        body = contact = default;
        float best = radius * radius;
        bool found = false;
        Vector3 pivot = ship.Origin;
        for (int sample = 0; sample < 3; sample++)
        {
            Vector3 axis = feet + up * (radius + (height - 2 * radius) * sample / 2f);
            Vector3 local = Vector3.Transform(axis - pivot, qi);
            foreach (HullBox box in ship.Def.Hull)
            {
                Vector3 nearest = Vector3.Clamp(local, box.Centre - box.HalfExtents, box.Centre + box.HalfExtents);
                float d = Vector3.DistanceSquared(nearest, local);
                if (d <= best)
                {
                    best = d;
                    body = axis;
                    contact = pivot + Vector3.Transform(nearest, ship.State.Rotation);
                    found = true;
                }
            }
        }
        return found;
    }
}

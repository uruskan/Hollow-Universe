using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Simulation.Building;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Players;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

/// <summary>Contact hazards share the existing character damage receiver, armour, bleeding and death rules.</summary>
public sealed class ContactHazardSystem : ZoneSystem
{
    private readonly Dictionary<(ulong Hazard, ulong Player), long> _nextHit = new();
    public override int Every => 3;

    protected internal override void Step()
    {
        Deployable[] hazards = Zone.Things().Statics().OfType<Deployable>().Where(d => d.Def.ContactHazard != null && d.Health > 0).ToArray();
        ComponentPool<PlayerLink> players = Entities.Pool<PlayerLink>();
        foreach (Deployable hazard in hazards)
        {
            ContactHazardStats stats = hazard.Def.ContactHazard!;
            ConvexPart[] hulls = CompoundGeometry.Parts(Content, hazard.Def, hazard.Open);
            if (hulls.Length == 0) continue;
            for (int i = 0; i < players.Count; i++)
            {
                Player player = players.Values[i].Player;
                if (player.Dead || _nextHit.GetValueOrDefault((hazard.Id.Value, player.Id.Value)) > Tick) continue;
                ref Character c = ref Entities.Get<Character>(Entities.AtSlot(players.Slots[i]));
                Vector3 feet = WorldOf(c.State), up = UpOf(c.State);
                float height = Math.Max(Tuning.Radius * 2, Vector3.Dot(EyeOf(c.State) - feet, up) + 0.06f);
                float broad = hazard.Def.HalfExtents.Length() * 2 + height + stats.Range;
                if (Vector3.DistanceSquared(feet, hazard.Position) > broad * broad ||
                    !TryContact(hazard, hulls, feet, up, height, Tuning.Radius, stats.Range, out Vector3 body, out Vector3 contact) ||
                    !ContactVisible(body, contact, hazard.Ref)) continue;
                _nextHit[(hazard.Id.Value, player.Id.Value)] = Tick + Math.Max(3, (long)Math.Ceiling(stats.Interval * CharacterTicks.Rate));
                Zone.Players().TryFindPlayer(hazard.Owner, out _, out Player? owner);
                Vector3 toward = body - contact;
                Zone.Damage().Damage(new DamageRequest
                {
                    By = owner, Attacker = hazard.Owner, AttackerName = hazard.Def.Item.Name, Weapon = hazard.Def.Item.Id,
                    Delivery = DamageDelivery.Melee, Channel = DamageType.Melee,
                    Amounts = new DamageAmounts { Flesh = stats.Damage }, Bleed = stats.Bleed, Cause = "Impaled", Direct = true,
                    Hit = new TraceHit { Target = player.Ref, Region = HitRegion.Body, Point = contact, Distance = toward.Length() },
                    From = contact, Direction = toward.LengthSquared() > 1e-8f ? Vector3.Normalize(toward) : up, Markers = TargetFamilies.None,
                });
            }
        }
        if (Tick % CharacterTicks.Rate == 0)
            foreach (var key in _nextHit.Where(p => p.Value < Tick - CharacterTicks.Rate * 60).Select(p => p.Key).ToArray()) _nextHit.Remove(key);
    }

    private bool ContactVisible(Vector3 body, Vector3 contact, TargetRef target)
    {
        if (!Zone.Targets().InSight(body, contact, target)) return false;
        Vector3 ray = contact - body;
        float length = ray.Length();
        // Interaction sight stops 10 cm before its target. Contact damage needs the thin wall immediately beside
        // a spike to count too, so retain only 5 mm of surface tolerance for the building ray.
        return length <= 0.005f || Zone.Building().World.Physics is not { } physics ||
            !physics.Ray(body, ray / length, length - 0.005f, out RayHit hit, buildingOnly: true) ||
            physics.OwnerOf(hit.Body) is ITargetable owner && owner.Ref.Whole == target.Whole;
    }

    /// <summary>
    /// Finds a real point inside an authored convex hull within capsule radius + contact range. Expanded planes only
    /// locate candidates; the final Euclidean test rejects their overly broad corners. Empty assembly gaps stay safe.
    /// </summary>
    public static bool TryContact(Deployable hazard, IReadOnlyList<ConvexPart> hulls, Vector3 feet, Vector3 up,
        float height, float radius, float range, out Vector3 body, out Vector3 contact)
    {
        body = contact = default;
        Quaternion rotation = Geometry.Upright(hazard.Up, hazard.Yaw), inverse = Quaternion.Conjugate(rotation);
        Vector3 local = Vector3.Transform(feet + up * radius - hazard.Position, inverse);
        Vector3 direction = Vector3.Transform(up, inverse);
        float length = Math.Max(0, height - radius * 2), reach = radius + range;
        foreach (ConvexPart part in hulls)
        {
            float enter = 0, exit = length;
            foreach ((Vector3 normal, float distance) in part.Planes)
            {
                float dot = Vector3.Dot(normal, direction), gap = distance + reach - Vector3.Dot(normal, local);
                if (Math.Abs(dot) < 1e-7f) { if (gap < 0) { exit = -1; break; } continue; }
                float t = gap / dot;
                if (dot < 0) enter = Math.Max(enter, t); else exit = Math.Min(exit, t);
                if (enter > exit) break;
            }
            if (enter > exit) continue;
            for (int sample = 0; sample < 3; sample++)
            {
                Vector3 axis = local + direction * (enter + (exit - enter) * sample / 2), nearest = axis;
                for (int pass = 0; pass < 24; pass++)
                {
                    float moved = 0;
                    foreach ((Vector3 normal, float distance) in part.Planes)
                    {
                        float outside = Vector3.Dot(normal, nearest) - distance;
                        if (outside <= 0) continue;
                        nearest -= normal * outside;
                        moved = Math.Max(moved, outside);
                    }
                    if (moved < 0.00001f) break;
                }
                if (part.Outside(nearest) > 0.0001f || Vector3.DistanceSquared(axis, nearest) > reach * reach + 0.00001f) continue;
                body = hazard.Position + Vector3.Transform(axis, rotation);
                contact = hazard.Position + Vector3.Transform(nearest, rotation);
                return true;
            }
        }
        return false;
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System;
using Hollow.Simulation.Capabilities;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Interaction;
using Hollow.Simulation.Items;
using Hollow.Simulation.Players;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

using Hollow.Simulation.Movement;

namespace Hollow.Simulation;

// Spent arrows (GROUND_SYSTEMS.md §5, the owner's): an arrow that stays whole on a hit sticks where it hit, its tip in,
// held by what it hit through a support link (§3.6) — the ground, a piece, a door's leaf, a deployable, a tree, a body's
// bone it rides on as the body moves — and is its own small target, taken back with E (the pickup capability), from a
// living player or animal too. The one lifetime step (§3.7) takes it away after a while; when what holds it goes, it falls
// to the ground. They no longer go into bags. At most survival.json's arrows.most of them: the oldest goes first.
public sealed class ArrowSystem : ZoneSystem
{
    private SpillSystem? _spillSystem;
    private SpillSystem Spill => _spillSystem ??= Zone.Require<SpillSystem>();
    private SupportSystem? _supportsSystem;
    private SupportSystem Supports => _supportsSystem ??= Zone.Require<SupportSystem>();
    private TargetSystem? _targetsSystem;
    private TargetSystem Targets => _targetsSystem ??= Zone.Require<TargetSystem>();
    private ThingSystem? _thingsSystem;
    private ThingSystem Things => _thingsSystem ??= Zone.Require<ThingSystem>();

    internal readonly Dictionary<EntityId, SpentArrow> _arrows = new();
    private readonly Queue<SpentArrow> _arrowOrder = new();
    private long _arrowSerial;

    /// <summary>The spent arrows in the zone.</summary>
    public IReadOnlyCollection<SpentArrow> All => _arrows.Values;

    public SpentArrow? ArrowOf(EntityId id) => _arrows.GetValueOrDefault(id);

    /// <summary>
    /// An arrow that hit something sticks in it (GROUND_SYSTEMS.md §5): its tip survival.json's arrows.depth in along its
    /// flight, held by what it hit where it hit it, relative to that thing (a body's bone as the shooter saw it then).
    /// </summary>
    internal SpentArrow? StickArrow(Projectile pr, in TraceHit hit)
    {
        GameContent content = Content;
        if (hit.Target.IsNone || content.Survival.ArrowsMost <= 0)
        {
            return null;
        }
        Vector3 direction = pr.Velocity.LengthSquared() > 1e-8f ? Vector3.Normalize(pr.Velocity) : Vector3.UnitY;
        var at = new TargetPose(hit.Point + direction * content.Survival.ArrowDepth, Along(direction));
        TargetRef support = hit.Target;
        SupportLink link = Targets.FamilyOf(support.Family) is { } family && family.TryPoseAt(support, Tick - pr.RewindTicks, out TargetPose then)
            ? SupportLink.Of(support, then, at)
            : SupportLink.Of(TargetRef.Ground, new TargetPose(Vector3.Zero, Quaternion.Identity), at);
        return AddArrow(pr.Ammo, link, at);
    }

    // A new spent arrow: held, loose (its lifetime runs), and the oldest gone when there are too many.
    private SpentArrow AddArrow(ItemDef item, SupportLink link, in TargetPose at)
    {
        LifetimeRule lifetime = Content.Survival.ArrowLifetime;
        var arrow = new SpentArrow(NextId(), item, link, lifetime, Tick + lifetime.Ticks, ++_arrowSerial) { LastPose = at };
        _arrows.Add(arrow.Id, arrow);
        _arrowOrder.Enqueue(arrow);
        Supports.Hold(arrow.Ref, link.Support);
        Things.Loose(arrow);
        while (_arrows.Count > Content.Survival.ArrowsMost && _arrowOrder.TryDequeue(out SpentArrow? oldest))
        {
            RemoveArrow(oldest);
        }
        if (_arrowOrder.Count > 2 * _arrows.Count + 64)
        {
            // The order keeps those taken or gone meanwhile until they reach its front: now and then, only the living stay.
            SpentArrow[] living = _arrowOrder.Where(a => !a.Gone).ToArray();
            _arrowOrder.Clear();
            foreach (SpentArrow a in living)
            {
                _arrowOrder.Enqueue(a);
            }
        }
        return arrow;
    }

    /// <summary>Takes a spent arrow out of the world (taken, its time up, too many): it lets go of what held it.</summary>
    internal bool RemoveArrow(SpentArrow arrow)
    {
        if (arrow.Gone || !_arrows.Remove(arrow.Id))
        {
            return false;
        }
        arrow.Gone = true;
        Supports.Release(arrow.Ref);
        Things.Unloose(arrow);
        return true;
    }

    /// <summary>
    /// What held an arrow went (GROUND_SYSTEMS.md §3.6): it falls where it was to the floor or ground below and lies there,
    /// along its flight, held by the ground.
    /// </summary>
    internal void ArrowFalls(SpentArrow arrow)
    {
        TargetPose was = Supports.TryLinkPose(arrow.Link, out TargetPose now) ? now : arrow.LastPose;
        Vector3 rest = Spill.RestingPoint(was.Position);
        Vector3 up = Frame.UpAt(rest);
        Vector3 along = Vector3.Transform(Vector3.UnitX, was.Rotation);
        Vector3 flat = along - up * Vector3.Dot(along, up);
        flat = flat.LengthSquared() > 1e-6f ? Vector3.Normalize(flat) : Vector3.Normalize(Vector3.Cross(up, Vector3.UnitX));
        // Lying on its side: the tip where it came to rest, the shaft along the ground, a hair above it.
        var lying = new TargetPose(rest + up * 0.01f + flat * (SpentArrow.Length / 2), Along(flat));
        arrow.Link = SupportLink.Of(TargetRef.Ground, new TargetPose(Vector3.Zero, Quaternion.Identity), lying);
        arrow.LastPose = lying;
        arrow.Revision++;
    }

    /// <summary>Where every arrow on a moving support is now, kept for those whose support goes before they are seen again.</summary>
    private void NoteArrowPoses()
    {
        foreach (SpentArrow arrow in _arrows.Values)
        {
            if (Supports.Moves(arrow.Link.Support) && Supports.TryLinkPose(arrow.Link, out TargetPose pose))
            {
                arrow.LastPose = pose;
            }
        }
    }

    /// <summary>The turn that points an arrow's shaft (its +X) along a direction.</summary>
    public static Quaternion Along(Vector3 direction)
    {
        Vector3 d = Vector3.Normalize(direction);
        float dot = Vector3.Dot(Vector3.UnitX, d);
        if (dot > 0.99999f)
        {
            return Quaternion.Identity;
        }
        if (dot < -0.99999f)
        {
            return Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);
        }
        Vector3 axis = Vector3.Normalize(Vector3.Cross(Vector3.UnitX, d));
        return Quaternion.CreateFromAxisAngle(axis, MathF.Acos(Math.Clamp(dot, -1, 1)));
    }

    /// <summary>
    /// Spent arrows (GROUND_SYSTEMS.md §5): each found by its shaft, where its support is now (they move with what holds
    /// them); taken whole with E into the taker's slots; taken away when their lifetime ends.
    /// </summary>
    internal sealed class ArrowTargets(Zone zone) : ITargetFamily, IPickupFamily
    {
        public TargetFamily Family => TargetFamily.Arrow;

        public bool Moves => true;

        public bool Exists(ulong id) => zone.Arrows().ArrowOf(new EntityId(id)) != null;

        public object? Resolve(ulong id) => zone.Arrows().ArrowOf(new EntityId(id));

        public bool TryPose(in TargetRef target, out TargetPose pose)
        {
            pose = default;
            if (zone.Arrows().ArrowOf(new EntityId(target.Id)) is not { } arrow || !zone.Supports().TryLinkPose(arrow.Link, out pose))
            {
                return false;
            }
            arrow.LastPose = pose;
            return true;
        }

        public bool TryNearest(in TargetRef target, Vector3 from, out Vector3 point)
        {
            point = from;
            if (!TryPose(target, out TargetPose pose))
            {
                return false;
            }
            (Vector3 nock, Vector3 tip) = SpentArrow.Shaft(pose);
            point = AimShapes.NearestOnCapsule(nock, tip, AimShapes.ArrowRadius, from);
            return true;
        }

        public void Aim(in AimRay ray, ref AimHit best)
        {
            float reach = ray.Upto(best) + SpentArrow.Length;
            foreach (SpentArrow arrow in zone.Arrows()._arrows.Values)
            {
                if (Vector3.DistanceSquared(arrow.LastPose.Position, ray.Origin) > (reach + 2) * (reach + 2) || !zone.Supports().TryLinkPose(arrow.Link, out TargetPose pose))
                {
                    continue;
                }
                (Vector3 nock, Vector3 tip) = SpentArrow.Shaft(pose);
                ray.Consider(ref best, AimShapes.Capsule(ray, nock, tip, AimShapes.ArrowRadius), arrow.Ref);
            }
        }

        public bool TryFacts(in TargetRef target, in Viewer viewer, out TargetFacts facts)
        {
            facts = default;
            if (zone.Arrows().ArrowOf(new EntityId(target.Id)) is not { } arrow || !TryPose(target, out TargetPose pose))
            {
                return false;
            }
            facts = new TargetFacts { Capabilities = zone.Content!.Capabilities.Arrow, Name = arrow.Item.Name, Count = 1, Position = pose.Position };
            return true;
        }

        public string ModelKey(ulong id) => zone.Arrows().ArrowOf(new EntityId(id))?.Item.Key ?? "";

        public CapabilitySet Capabilities(ulong id) => zone.Arrows().ArrowOf(new EntityId(id)) != null ? zone.Content!.Capabilities.Arrow : CapabilitySet.Empty;

        public bool Remove(ulong id) => zone.Arrows().ArrowOf(new EntityId(id)) is { } arrow && zone.Arrows().RemoveArrow(arrow);

        // Taken back into the taker's slots, whole (none taken when there is no room).
        public Outcome PickUp(Entity entity, Player p, in TargetRef target)
        {
            if (zone.Arrows().ArrowOf(new EntityId(target.Id)) is not { } arrow)
            {
                return OutcomeCode.NotFound;
            }
            if (p.Inventory.Give(zone.Content!, ItemStack.Of(arrow.Item, 1)) > 0)
            {
                return OutcomeCode.NoSpace;
            }
            zone.Arrows().RemoveArrow(arrow);
            zone.GameEvents.Add(new ZoneEvent { Kind = ZoneEventKind.ItemsGained, Player = p.Id, Item = arrow.Item.Id, Count = 1, Reason = GainReason.PickedUp });
            return Outcome.Done;
        }
    }

    public override int Every => CharacterMotor.TickRate;

    protected internal override void Step()
    {
        NoteArrowPoses();
    }
}

/// <summary>The accessor on the zone (GROUND_SYSTEMS.md §12.2, K4).</summary>
public static class ArrowSystemAccess
{
    public static ArrowSystem Arrows(this Zone zone) => zone.Require<ArrowSystem>();
}

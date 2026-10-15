using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System;
using Hollow.Simulation.Animals;
using Hollow.Simulation.Building;
using Hollow.Simulation.Capabilities;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Interaction;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Physics;
using Hollow.Simulation.Players;
using Hollow.Simulation.Raid;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;
using Hollow.Simulation.Teams;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

// Naming world things (GROUND_SYSTEMS.md §3.1): a TargetRef routes to its family, each family registered once here, which
// answers for its members: whether one exists, the thing itself, where it (or a part: a door's leaf, a body's bone) is,
// the nearest point of its bounds, what it is drawn as. The four lookups the zone had (the entity store, the building's,
// the animals', a scan of the explosives) are each one family's business now.

// (from TargetSystem)
// Reach and sight (GROUND_SYSTEMS.md §3.4): one rule for every action on a thing. The distance runs from the eye to the
// thing's nearest surface (its family's), within a reach from data (survival.json's reach); one ray from the eye to just
// in front of that point must meet nothing of the building but the thing itself, nor the ground short of the thing. It
// replaces the slack constants each action had (+0.3, +0.5, +1.5, +3, +4 to centres, middles and feet).
public sealed class TargetSystem : ZoneSystem
{
    private BuildingSystem? _buildingSystem;
    private BuildingSystem Building => _buildingSystem ??= Zone.Require<BuildingSystem>();
    private CombatSystem? _combatSystem;
    private CombatSystem Combat => _combatSystem ??= Zone.Require<CombatSystem>();
    private WorldSystem? _worldSystem;
    private WorldSystem World => _worldSystem ??= Zone.Require<WorldSystem>();

    private Registry<ITargetFamily>? _families;
    private Registry<ICapability>? _capabilities;
    internal ITargetView[] _views = Array.Empty<ITargetView>();

    /// <summary>The families of world things, by <see cref="TargetFamily"/> (null in a movement-only zone).</summary>
    public Registry<ITargetFamily> Families => _families ?? throw new InvalidOperationException("a zone without game data names no things");

    private void InitTargets()
    {
        var families = Registry<ITargetFamily>.ForEnum<TargetFamily>("target families", complete: true, TargetFamily.None);
        foreach (ITargetFamily family in new ITargetFamily[]
                 {
                     new GroundTargets(Zone), new CharacterTargets(Zone), new AnimalTargets(Zone), new NaturalTargets(Zone), new ThingTargets(Zone),
                     new PieceTargets(Zone), new DoorTargets(Zone), new CupboardTargets(Zone), new ExplosiveTargets(Zone), new ShipTargets(Zone),
                     new ArrowSystem.ArrowTargets(Zone),
                 })
        {
            families.Add((int)family.Family, family);
        }
        _families = families.Seal();
        _views = _families.Handlers.ToArray<ITargetView>();
        _capabilities = CapabilityHandlers.Create(Content);
    }

    /// <summary>The families as the interact key aims through them (GROUND_SYSTEMS.md §5), in their numbers' order.</summary>
    public IReadOnlyList<ITargetView> TargetViews => _views;

    /// <summary>Every capability's handler, by capabilities.json's index (checked at start: one each).</summary>
    public Registry<ICapability> Capabilities => _capabilities ?? throw new InvalidOperationException("a zone without game data has no capabilities");

    /// <summary>
    /// What a target tells a viewer (GROUND_SYSTEMS.md §3.3): every capability it has gives its share, through the
    /// registry; none for a thing that doesn't exist.
    /// </summary>
    public ViewerFlags FlagsFor(in TargetRef target, in Viewer viewer)
    {
        if (FamilyOf(target.Family) is not { } family || family.Resolve(target.Id) is not { } thing)
        {
            return ViewerFlags.None;
        }
        return Capabilities.Flags(family.Capabilities(target.Id), thing, viewer);
    }

    /// <summary>
    /// A player as a viewer: their ID, their team and the numbers of the keys they carry. The keys are the zone's own
    /// list, good until the next call.
    /// </summary>
    public Viewer ViewerOf(Player p) => new(p.Id, World.Teams.TeamOf(p.Id), Building.KeysOf(p));

    /// <summary>The family a target belongs to, or null (none, or a number from outside that names no family).</summary>
    public ITargetFamily? FamilyOf(TargetFamily family) => _families?.Find((int)family);

    /// <summary>Whether a target is still in the world.</summary>
    public bool Exists(in TargetRef target) => FamilyOf(target.Family)?.Exists(target.Id) == true;

    /// <summary>The thing a target names (its family's object), or null.</summary>
    public object? Resolve(in TargetRef target) => FamilyOf(target.Family)?.Resolve(target.Id);

    /// <summary>Where a target (or its part) is now.</summary>
    public bool TryPose(in TargetRef target, out TargetPose pose)
    {
        pose = default;
        return FamilyOf(target.Family)?.TryPose(target, out pose) == true;
    }

    /// <summary>The nearest point of a target's bounds to a point (GROUND_SYSTEMS.md §3.4).</summary>
    public bool TryNearest(in TargetRef target, Vector3 from, out Vector3 point)
    {
        point = from;
        return FamilyOf(target.Family)?.TryNearest(target, from, out point) == true;
    }

    /// <summary>Takes a thing out of the world through its family (a loose thing's lifetime ended, GROUND_SYSTEMS.md §3.7).</summary>
    public bool RemoveTarget(in TargetRef target) => FamilyOf(target.Family)?.Remove(target.Id) == true;

    // ---------------------------------------------------------------- the families

    /// <summary>The terrain: always there, its pose the zone frame itself (what lies on it keeps its own place).</summary>
    internal sealed class GroundTargets(Zone zone) : ITargetFamily
    {
        public TargetFamily Family => TargetFamily.Ground;

        public bool Moves => false;

        public bool Exists(ulong id) => id == 0;

        public object? Resolve(ulong id) => null;

        public bool TryPose(in TargetRef target, out TargetPose pose)
        {
            pose = new TargetPose(Vector3.Zero, Quaternion.Identity);
            return target.Id == 0;
        }

        public bool TryNearest(in TargetRef target, Vector3 from, out Vector3 point)
        {
            point = zone.Settle(from);
            return target.Id == 0;
        }

        public string ModelKey(ulong id) => "";

        public CapabilitySet Capabilities(ulong id) => CapabilitySet.Empty;

        // The ground hides what is behind it; it offers nothing.
        public void Aim(in AimRay ray, ref AimHit best) =>
            ray.Consider(ref best, zone.Combat().GroundCrossing(ray.Origin, ray.Direction, ray.Upto(best)), TargetRef.Ground, AimKind.Backdrop);

        public bool TryFacts(in TargetRef target, in Viewer viewer, out TargetFacts facts)
        {
            facts = new TargetFacts { Name = "" };
            return target.Id == 0;
        }
    }

    /// <summary>Players' characters, awake, down or asleep (a dead one is gone: its body is a thing); parts are bones.</summary>
    internal sealed class CharacterTargets(Zone zone) : ITargetFamily
    {
        public TargetFamily Family => TargetFamily.Character;

        public bool Moves => true;

        public bool Exists(ulong id) => zone.Players().TryFindPlayer(new EntityId(id), out _, out Player p) && !p.Dead;

        public object? Resolve(ulong id) => zone.Players().TryFindPlayer(new EntityId(id), out _, out Player p) && !p.Dead ? p : null;

        public bool TryPose(in TargetRef target, out TargetPose pose)
        {
            pose = default;
            if (!Body(target.Id, out HitPose body))
            {
                return false;
            }
            if (target.Part == 0)
            {
                pose = new TargetPose(body.Feet, Geometry.Upright(body.Up, body.Yaw));
                return true;
            }
            Span<HitShape> shapes = stackalloc HitShape[Hitboxes.MostShapes];
            pose = Hitboxes.ShapePose(shapes[..Hitboxes.Shapes(body, shapes)], target.Part - 1, body);
            return true;
        }

        public bool TryNearest(in TargetRef target, Vector3 from, out Vector3 point)
        {
            point = from;
            if (!Body(target.Id, out HitPose body))
            {
                return false;
            }
            Span<HitShape> shapes = stackalloc HitShape[Hitboxes.MostShapes];
            Span<HitShape> all = shapes[..Hitboxes.Shapes(body, shapes)];
            point = target.Part == 0 ? Hitboxes.NearestPoint(all, from) : all[Math.Clamp(target.Part - 1, 0, all.Length - 1)].Nearest(from);
            return true;
        }

        public string ModelKey(ulong id) => "";

        public CapabilitySet Capabilities(ulong id) => zone.Players().TryFindPlayer(new EntityId(id), out _, out Player p) && !p.Dead ? Of(p) : CapabilitySet.Empty;

        // Asleep: looted; down: looted and helped up; up and about: invited into a team.
        private CapabilitySet Of(Player p)
        {
            CapabilityData data = zone.Content!.Capabilities;
            return p.Sleeping ? data.Sleeper : p.Wounded ? data.Down : data.Awake;
        }

        // A bone as the attacker saw it (the hit history), for what sticks in it where it hit.
        public bool TryPoseAt(in TargetRef target, double viewTick, out TargetPose pose)
        {
            if (target.Part == 0 || !zone.History.Sample(new EntityId(target.Id), viewTick, out HitPose then))
            {
                return TryPose(target, out pose);
            }
            Span<HitShape> shapes = stackalloc HitShape[Hitboxes.MostShapes];
            pose = Hitboxes.ShapePose(shapes[..Hitboxes.Shapes(then, shapes)], target.Part - 1, then);
            return true;
        }

        // Bodies as they are now, by their hit shapes, past the one looking.
        public void Aim(in AimRay ray, ref AimHit best)
        {
            float length = ray.Upto(best);
            ComponentPool<PlayerLink> players = zone.Entities.Pool<PlayerLink>();
            for (int i = 0; i < players.Count; i++)
            {
                Player p = players.Values[i].Player;
                if (p.Dead || p.Id.Value == ray.Ignore || !Body(p.Id.Value, out HitPose pose) ||
                    Vector3.DistanceSquared(pose.Feet, ray.Origin) > (length + 2.5f) * (length + 2.5f))
                {
                    continue;
                }
                ray.Consider(ref best, Hitboxes.Raycast(pose, ray.Origin, ray.Direction, length, AimShapes.BodyMargin, out _), p.Ref);
            }
        }

        public bool TryFacts(in TargetRef target, in Viewer viewer, out TargetFacts facts)
        {
            facts = default;
            if (!zone.Players().TryFindPlayer(new EntityId(target.Id), out Entity e, out Player p) || p.Dead)
            {
                return false;
            }
            facts = new TargetFacts
            {
                Capabilities = Of(p), Flags = zone.Targets().FlagsFor(target, viewer), Name = p.Name, Body = true,
                Position = zone.WorldOf(zone.Entities.Get<Character>(e).State),
            };
            return true;
        }

        // The body as it is now: standing, crouching, or lying asleep or down; aboard a ship, where it carries it.
        private bool Body(ulong id, out HitPose pose)
        {
            pose = default;
            if (!zone.Players().TryFindPlayer(new EntityId(id), out Entity e, out Player p) || p.Dead)
            {
                return false;
            }
            ref Character c = ref zone.Entities.Get<Character>(e);
            Vector3 up = zone.UpOf(c.State);
            pose = new HitPose
            {
                Feet = zone.WorldOf(c.State),
                Up = up,
                Yaw = Facing.YawOf(zone.ViewOf(c), up),
                Pose = c.State.Stance == Stance.Seated ? Simulation.Combat.Pose.Seated : p.Sleeping || p.Wounded ? Simulation.Combat.Pose.Lying : c.State.Stance == Stance.Crouching ? Simulation.Combat.Pose.Crouching : Simulation.Combat.Pose.Standing,
            };
            return true;
        }
    }

    /// <summary>Animals, alive or a corpse until it goes; parts are bones.</summary>
    internal sealed class AnimalTargets(Zone zone) : ITargetFamily
    {
        public TargetFamily Family => TargetFamily.Animal;

        public bool Moves => true;

        public bool Exists(ulong id) => Find(id) != null;

        public object? Resolve(ulong id) => Find(id);

        public bool TryPose(in TargetRef target, out TargetPose pose)
        {
            pose = default;
            if (Find(target.Id) is not { } a)
            {
                return false;
            }
            HitPose body = zone.Animals().PoseOf(a);
            if (target.Part == 0)
            {
                pose = new TargetPose(body.Feet, Geometry.Upright(body.Up, body.Yaw));
                return true;
            }
            Span<HitShape> shapes = stackalloc HitShape[Hitboxes.MostShapes];
            pose = Hitboxes.ShapePose(shapes[..AnimalHitboxes.Shapes(a.Species, body, shapes)], target.Part - 1, body);
            return true;
        }

        public bool TryNearest(in TargetRef target, Vector3 from, out Vector3 point)
        {
            point = from;
            if (Find(target.Id) is not { } a)
            {
                return false;
            }
            Span<HitShape> shapes = stackalloc HitShape[Hitboxes.MostShapes];
            Span<HitShape> all = shapes[..AnimalHitboxes.Shapes(a.Species, zone.Animals().PoseOf(a), shapes)];
            point = target.Part == 0 ? Hitboxes.NearestPoint(all, from) : all[Math.Clamp(target.Part - 1, 0, all.Length - 1)].Nearest(from);
            return true;
        }

        public string ModelKey(ulong id) => Find(id)?.Species.Key ?? "";

        public CapabilitySet Capabilities(ulong id) => Find(id) is { IsDead: true } ? zone.Content!.Capabilities.Carcass : CapabilitySet.Empty;

        // A bone as the attacker saw it (the animals' hit history), for what sticks in it where it hit.
        public bool TryPoseAt(in TargetRef target, double viewTick, out TargetPose pose)
        {
            if (target.Part == 0 || Find(target.Id) is not { IsDead: false } a || !zone.Animals().World!.History.Sample(a.Id, viewTick, out HitPose then))
            {
                return TryPose(target, out pose);
            }
            Span<HitShape> shapes = stackalloc HitShape[Hitboxes.MostShapes];
            pose = Hitboxes.ShapePose(shapes[..AnimalHitboxes.Shapes(a.Species, then, shapes)], target.Part - 1, then);
            return true;
        }

        // Animals as they are now, by their hit shapes: they hide what is behind them, and offer nothing themselves.
        public void Aim(in AimRay ray, ref AimHit best)
        {
            if (zone.Animals().World is not { } animals)
            {
                return;
            }
            float length = ray.Upto(best);
            animals.Near(ray.Origin + ray.Direction * (length / 2), length / 2 + 4f, zone.Interaction()._aimAnimals);
            foreach (Animal a in zone.Interaction()._aimAnimals)
            {
                if (!a.Removed)
                {
                    ray.Consider(ref best, AnimalHitboxes.Raycast(a.Species, zone.Animals().PoseOf(a), ray.Origin, ray.Direction, length, AimShapes.BodyMargin, out _), a.Ref,
                        AimKind.Backdrop);
                }
            }
        }

        public bool TryFacts(in TargetRef target, in Viewer viewer, out TargetFacts facts)
        {
            facts = default;
            if (Find(target.Id) is not { } a)
            {
                return false;
            }
            facts = new TargetFacts { Capabilities = Capabilities(target.Id), Name = a.Species.Name, Position = a.Position };
            return true;
        }

        public bool Remove(ulong id)
        {
            if (Find(id) is not { } a)
            {
                return false;
            }
            zone.Animals().World!.Remove(a);
            return true;
        }

        private Animal? Find(ulong id) => zone.Animals().World?.Find(new EntityId(id)) is { Removed: false } a ? a : null;
    }

    /// <summary>Trees, outcrops, pickups and bushes (felled, broken or picked ones are gone until they come back).</summary>
    internal sealed class NaturalTargets(Zone zone) : ITargetFamily, IPickupFamily
    {
        public TargetFamily Family => TargetFamily.Natural;

        public bool Moves => false;

        public bool Exists(ulong id) => Find(id, out _, out _);

        public object? Resolve(ulong id) => Find(id, out NaturalObject o, out _) ? o : null;

        public bool TryPose(in TargetRef target, out TargetPose pose)
        {
            pose = default;
            if (!Find(target.Id, out NaturalObject o, out _))
            {
                return false;
            }
            pose = new TargetPose(o.Position, Geometry.Upright(o.Up, o.Yaw));
            return true;
        }

        public bool TryNearest(in TargetRef target, Vector3 from, out Vector3 point)
        {
            point = from;
            if (!Find(target.Id, out NaturalObject o, out NaturalKind? kind))
            {
                return false;
            }
            point = NaturalShapes.Bounds(o, kind!).Nearest(from);
            return true;
        }

        public string ModelKey(ulong id) => Find(id, out _, out NaturalKind? kind) ? kind!.Key : "";

        public CapabilitySet Capabilities(ulong id) => Find(id, out _, out NaturalKind? kind) ? Of(kind!) : CapabilitySet.Empty;

        // Pickups and bushes are taken with E; trees, outcrops and boulders only hide what is behind them.
        private CapabilitySet Of(NaturalKind kind) => kind.IsPicked ? zone.Content!.Capabilities.Pickup : CapabilitySet.Empty;

        public void Aim(in AimRay ray, ref AimHit best)
        {
            if (zone.Nature != null && zone.Natural != null)
            {
                NaturalAim.Aim(zone.Nature, zone.Natural, zone.Content!, ray, ref best, zone.Interaction()._aimNature);
            }
        }

        public bool TryFacts(in TargetRef target, in Viewer viewer, out TargetFacts facts)
        {
            facts = default;
            if (!Find(target.Id, out NaturalObject o, out NaturalKind? kind))
            {
                return false;
            }
            facts = new TargetFacts { Capabilities = Of(kind!), Name = kind!.Name, Stays = kind.Class == NaturalClass.Bush, Count = 1, Position = o.Position };
            return true;
        }

        // A pickup is taken whole (a bush picked bare): its yield into the picker's slots.
        public Outcome PickUp(Entity entity, Player p, in TargetRef target)
        {
            if (!Find(target.Id, out NaturalObject o, out NaturalKind? kind) || !kind!.IsPicked)
            {
                return OutcomeCode.NotFound;
            }
            GatherResult result = zone.Natural!.Pick(o, zone.UnixMilliseconds);
            zone.Combat().Give(entity, p, result.Yield, GainReason.PickedUp);
            return Outcome.Done;
        }

        private bool Find(ulong id, out NaturalObject o, out NaturalKind? kind)
        {
            kind = null;
            if (zone.Nature == null || zone.Natural == null || !zone.Nature.TryGet(id, out o) || zone.Natural.IsDepleted(id))
            {
                o = default;
                return false;
            }
            kind = zone.Content!.Natural(o.Kind);
            return kind != null;
        }
    }

    /// <summary>The static things of the entity store: deployables, world props, bags of dropped items, bodies.</summary>
    internal sealed class ThingTargets(Zone zone) : ITargetFamily, IPickupFamily
    {
        public TargetFamily Family => TargetFamily.Thing;

        public bool Moves => false;

        public bool Exists(ulong id) => zone.Things().StaticOf(new EntityId(id)) != null;

        public object? Resolve(ulong id) => zone.Things().StaticOf(new EntityId(id));

        public bool TryPose(in TargetRef target, out TargetPose pose)
        {
            pose = default;
            if (zone.Things().StaticOf(new EntityId(target.Id)) is not { } thing)
            {
                return false;
            }
            pose = new TargetPose(thing.Position, Geometry.Upright(thing.Up, thing.Yaw));
            return true;
        }

        public bool TryNearest(in TargetRef target, Vector3 from, out Vector3 point)
        {
            point = from;
            if (zone.Things().StaticOf(new EntityId(target.Id)) is not { } thing)
            {
                return false;
            }
            point = thing.Nearest(from);
            return true;
        }

        public string ModelKey(ulong id) => zone.Things().StaticOf(new EntityId(id))?.ModelKey ?? "";

        public CapabilitySet Capabilities(ulong id) => zone.Things().StaticOf(new EntityId(id))?.CapabilitiesIn(zone.Content!.Capabilities) ?? CapabilitySet.Empty;

        // Deployables and props by their boxes (the cell index), bags and bodies by their spheres (the loose things); the
        // tool cupboard's box is the cupboard's (found by its body).
        public void Aim(in AimRay ray, ref AimHit best)
        {
            float length = ray.Upto(best);
            zone.Interaction()._aimThings.Clear();
            zone.Things().DeployablesNear(ray.Origin + ray.Direction * (length / 2), length / 2 + 2f, zone.Interaction()._aimThings);
            foreach (Deployable d in zone.Interaction()._aimThings)
            {
                if (zone.Building().ClaimOf(d) == null)
                {
                    ray.Consider(ref best, AimShapes.Box(ray, d.Position, d.Up, d.Yaw, d.Def.HalfExtents), d.Ref);
                }
            }
            foreach (ILifetime loose in zone.Things()._loose)
            {
                if (loose is DroppedContainer dropped && Vector3.DistanceSquared(dropped.Position, ray.Origin) <= (length + 2) * (length + 2))
                {
                    ray.Consider(ref best, AimShapes.Dropped(ray, dropped.Position, dropped.Up, dropped.Kind), dropped.Ref);
                }
            }
        }

        public bool TryFacts(in TargetRef target, in Viewer viewer, out TargetFacts facts)
        {
            facts = default;
            if (zone.Things().StaticOf(new EntityId(target.Id)) is not { } thing)
            {
                return false;
            }
            DeployableDef? def = (thing as Deployable)?.Def;
            facts = new TargetFacts
            {
                Capabilities = thing.CapabilitiesIn(zone.Content!.Capabilities), Flags = zone.Targets().FlagsFor(target, viewer),
                Name = thing.Name.Length > 0 ? thing.Name : def?.Item.Name ?? "", Count = thing.ShownCount,
                Body = thing.Kind == StaticKind.Corpse, Deployable = def, Position = thing.Position,
            };
            return true;
        }

        // A bag of dropped items is taken whole: everything in it that fits (an emptied bag goes).
        public Outcome PickUp(Entity entity, Player p, in TargetRef target) =>
            zone.Things().StaticOf(new EntityId(target.Id)) is DroppedBag bag ? zone.Interaction().TakeBag(p, bag) : OutcomeCode.NotFound;

        public bool Remove(ulong id)
        {
            if (zone.Things().StaticOf(new EntityId(id)) is not { } thing)
            {
                return false;
            }
            zone.Things().RemoveStatic(thing);
            return true;
        }
    }

    /// <summary>Building pieces (one coming down still stands until it is taken away); the hammer mends them.</summary>
    internal sealed class PieceTargets(Zone zone) : ITargetFamily, IRepairFamily
    {
        public Outcome Repair(Entity entity, Player p, in TargetRef target, IBuildingWallet wallet) =>
            zone.Building().Hammer(entity, HammerAction.Repair, new EntityId(target.Id), 0);

        public TargetFamily Family => TargetFamily.Piece;

        public bool Moves => false;

        public bool Exists(ulong id) => Find(id) != null;

        public object? Resolve(ulong id) => Find(id);

        public bool TryPose(in TargetRef target, out TargetPose pose)
        {
            pose = default;
            if (Find(target.Id) is not { } piece)
            {
                return false;
            }
            (Vector3 position, Quaternion rotation) = piece.ZoneTransform;
            pose = new TargetPose(position, rotation);
            return true;
        }

        public bool TryNearest(in TargetRef target, Vector3 from, out Vector3 point)
        {
            point = from;
            if (Find(target.Id) is not { } piece)
            {
                return false;
            }
            point = BlastSystem.ClosestPoint(zone.Building().World!.Data, piece, from);
            return true;
        }

        public string ModelKey(ulong id) => Find(id)?.Def.Key ?? "";

        public CapabilitySet Capabilities(ulong id) => CapabilitySet.Empty;

        // Every building body in one query (pieces, doors' leaves, the tool cupboard): whatever owns the body names it.
        public void Aim(in AimRay ray, ref AimHit best)
        {
            if (zone.Building().World is { } building)
            {
                BuildingAim.Bodies(building, ray, ref best);
            }
        }

        public bool TryFacts(in TargetRef target, in Viewer viewer, out TargetFacts facts)
        {
            facts = default;
            if (Find(target.Id) is not { } piece)
            {
                return false;
            }
            facts = new TargetFacts { Name = piece.Def.Name, Position = piece.ZoneTransform.Position };
            return true;
        }

        private Piece? Find(ulong id) => zone.Building().World?.PieceOf(new EntityId(id)) is { Gone: false } piece ? piece : null;
    }

    /// <summary>Doors in their frames; a door's parts are its leaves (part i + 1: leaf i), which swing.</summary>
    internal sealed class DoorTargets(Zone zone) : ITargetFamily
    {
        public TargetFamily Family => TargetFamily.Door;

        public bool Moves => true;

        public bool Exists(ulong id) => Find(id) != null;

        public object? Resolve(ulong id) => Find(id);

        public bool TryPose(in TargetRef target, out TargetPose pose)
        {
            pose = default;
            if (Find(target.Id) is not { } door)
            {
                return false;
            }
            (Vector3 position, Quaternion rotation) = target.Part > 0 && target.Part <= door.Def.Leaves.Count
                ? door.Leaf(target.Part - 1, door.Open)
                : door.Frame.ZoneTransform;
            pose = new TargetPose(position, rotation);
            return true;
        }

        // Its leaves, or its opening, whichever is nearer (a door's target includes its opening).
        public bool TryNearest(in TargetRef target, Vector3 from, out Vector3 point)
        {
            point = from;
            if (Find(target.Id) is not { } door)
            {
                return false;
            }
            point = BlastSystem.ClosestPoint(door, from).Point;
            if (target.Part == 0 && AimShapes.NearestInDoorway(zone.Building().World!.Data, door.Frame, from, out Vector3 opening) &&
                Vector3.DistanceSquared(opening, from) < Vector3.DistanceSquared(point, from))
            {
                point = opening;
            }
            return true;
        }

        // The leaves are building bodies (the pieces' query finds them); the openings are aimed at here, so an open door,
        // whose leaves swung or rolled out of the way, is still what the aim through its doorway meets.
        public void Aim(in AimRay ray, ref AimHit best)
        {
            if (zone.Building().World is { } building)
            {
                BuildingAim.Openings(building, ray, ref best, zone.Interaction()._aimPieces);
            }
        }

        public bool TryFacts(in TargetRef target, in Viewer viewer, out TargetFacts facts)
        {
            facts = default;
            if (Find(target.Id) is not { } door)
            {
                return false;
            }
            facts = new TargetFacts
            {
                Capabilities = Capabilities(target.Id), Flags = zone.Targets().FlagsFor(target, viewer), Name = door.Def.Name, Open = door.Open,
                Lock = door.Lock?.Kind ?? LockKind.None, Position = door.Frame.ZoneTransform.Position,
            };
            return true;
        }

        public string ModelKey(ulong id) => Find(id)?.Def.Key ?? "";

        public CapabilitySet Capabilities(ulong id)
        {
            CapabilityData data = zone.Content!.Capabilities;
            return Find(id) is { } door ? door.Lock is { } fitted ? data.Door.With(data.Lock(fitted.Kind)) : data.Door : CapabilitySet.Empty;
        }

        private Door? Find(ulong id) => zone.Building().World?.DoorOf(new EntityId(id)) is { Gone: false } door ? door : null;
    }

    /// <summary>The claim device (the base's cupboard), found by its box; its list is the building rules'.</summary>
    internal sealed class CupboardTargets(Zone zone) : ITargetFamily, IAuthoriseFamily
    {
        public Outcome Authorise(Player p, in TargetRef target) =>
            Find(target.Id) is { } claim ? zone.Building().World!.Authorise(p.Id.Value, claim) : OutcomeCode.NotFound;

        public Outcome Deauthorise(Player p, in TargetRef target) =>
            Find(target.Id) is { } claim ? zone.Building().World!.Deauthorise(p.Id.Value, claim) : OutcomeCode.NotFound;

        public Outcome AuthoriseTeam(Player p, in TargetRef target, List<ulong> team) =>
            Find(target.Id) is { } claim ? zone.World().AuthoriseTeam(p.Id.Value, claim, team) : OutcomeCode.NotFound;

        public Outcome ClearList(Player p, in TargetRef target) =>
            Find(target.Id) is { } claim ? zone.Building().World!.ClearAuthorised(p.Id.Value, claim) : OutcomeCode.NotFound;

        public TargetFamily Family => TargetFamily.Cupboard;

        public bool Moves => false;

        public bool Exists(ulong id) => Find(id) != null;

        public object? Resolve(ulong id) => Find(id);

        public bool TryPose(in TargetRef target, out TargetPose pose)
        {
            pose = default;
            if (Find(target.Id) is not { } claim)
            {
                return false;
            }
            (Vector3 position, Quaternion rotation) = claim.ZoneTransform;
            pose = new TargetPose(position, rotation);
            return true;
        }

        public bool TryNearest(in TargetRef target, Vector3 from, out Vector3 point)
        {
            point = from;
            if (Find(target.Id) is not { } claim)
            {
                return false;
            }
            point = zone.Building().DeployableOf(claim) is { } box ? BlastSystem.ClosestPoint(box, from) : claim.ZoneTransform.Position;
            return true;
        }

        public string ModelKey(ulong id) => Find(id) != null ? zone.Content!.ClaimDeployable.Key : "";

        public CapabilitySet Capabilities(ulong id) => Find(id) != null ? zone.Content!.Capabilities.Cupboard : CapabilitySet.Empty;

        // Its body is a building body: the pieces' query finds it.
        public void Aim(in AimRay ray, ref AimHit best)
        {
        }

        // Its store is its box's (a deployable): what that tells the viewer (empty, theirs) joins the cupboard's own.
        public bool TryFacts(in TargetRef target, in Viewer viewer, out TargetFacts facts)
        {
            facts = default;
            if (Find(target.Id) is not { } claim)
            {
                return false;
            }
            facts = new TargetFacts
            {
                Capabilities = Capabilities(target.Id),
                Flags = zone.Targets().FlagsFor(target, viewer) | zone.Targets().FlagsFor(new TargetRef(TargetFamily.Thing, claim.Id.Value), viewer),
                Name = zone.Content!.ClaimDeployable.Item.Name, Position = claim.ZoneTransform.Position, Deployable = zone.Content!.ClaimDeployable,
            };
            return true;
        }

        private ClaimDevice? Find(ulong id) => zone.Building().World?.ClaimOf(new EntityId(id)) is { Gone: false } claim ? claim : null;
    }

    /// <summary>Explosives in the world, by their IDs (they were a scan of the list).</summary>
    internal sealed class ExplosiveTargets(Zone zone) : ITargetFamily
    {
        public TargetFamily Family => TargetFamily.Explosive;

        public bool Moves => true;

        public bool Exists(ulong id) => zone.Explosives().ExplosiveOf(new EntityId(id)) != null;

        public object? Resolve(ulong id) => zone.Explosives().ExplosiveOf(new EntityId(id));

        public bool TryPose(in TargetRef target, out TargetPose pose)
        {
            pose = default;
            if (zone.Explosives().ExplosiveOf(new EntityId(target.Id)) is not { } e)
            {
                return false;
            }
            pose = new TargetPose(e.Position, Geometry.Upright(e.Normal, 0));
            return true;
        }

        public bool TryNearest(in TargetRef target, Vector3 from, out Vector3 point)
        {
            point = from;
            if (zone.Explosives().ExplosiveOf(new EntityId(target.Id)) is not { } e)
            {
                return false;
            }
            point = new HitShape(e.Position, e.Position, (float)e.Stats.Radius, HitRegion.Body).Nearest(from);
            return true;
        }

        public string ModelKey(ulong id) => zone.Explosives().ExplosiveOf(new EntityId(id))?.Item.Key ?? "";

        public void Aim(in AimRay ray, ref AimHit best)
        {
            float length = ray.Upto(best);
            foreach (LiveExplosive e in zone.Explosives().All)
            {
                if (!e.Gone && Vector3.DistanceSquared(e.Position, ray.Origin) <= (length + 1) * (length + 1))
                {
                    ray.Consider(ref best, AimShapes.Sphere(ray, e.Position, AimShapes.ExplosiveRadius), e.Ref);
                }
            }
        }

        public bool TryFacts(in TargetRef target, in Viewer viewer, out TargetFacts facts)
        {
            facts = default;
            if (zone.Explosives().ExplosiveOf(new EntityId(target.Id)) is not { Gone: false } e)
            {
                return false;
            }
            facts = new TargetFacts { Capabilities = Capabilities(target.Id), Flags = zone.Targets().FlagsFor(target, viewer), Name = e.Item.Name, Position = e.Position };
            return true;
        }

        public CapabilitySet Capabilities(ulong id)
        {
            CapabilityData data = zone.Content!.Capabilities;
            return zone.Explosives().ExplosiveOf(new EntityId(id)) is { } e ? e.Dud ? data.Dud : data.Explosive : CapabilitySet.Empty;
        }

        // Gone at once; the explosives' step lets go of it on its next tick (its link, its ID), as for one that went off.
        public bool Remove(ulong id)
        {
            if (zone.Explosives().ExplosiveOf(new EntityId(id)) is not { Gone: false } e)
            {
                return false;
            }
            e.Gone = true;
            return true;
        }
    }

    /// <summary>
    /// Ships, wrecks included until they go, by their target parts (IRON_WAKE.md §5.1): found by their parts' balls and,
    /// for a ship people walk in, by its space's pieces (a piece names its part: the ramp, a battery; the rest is the hull,
    /// which only stops the view). Their lists (the helm's) and their parts' repair are theirs too.
    /// </summary>
    internal sealed class ShipTargets(Zone zone) : ITargetFamily, IAuthoriseFamily, IRepairFamily
    {
        private readonly List<(int Part, Vector3 Point, float Distance)> _near = new();

        public TargetFamily Family => TargetFamily.Ship;

        public bool Moves => true;

        public bool Exists(ulong id) => zone.Ships().ShipOf(new EntityId(id)) != null;

        public object? Resolve(ulong id) => zone.Ships().ShipOf(new EntityId(id));

        // The ship by its pivot, or a part at its pose (moved by what carries it: the ramp's leaf, a turret).
        public bool TryPose(in TargetRef target, out TargetPose pose)
        {
            pose = default;
            if (zone.Ships().ShipOf(new EntityId(target.Id)) is not { } ship)
            {
                return false;
            }
            pose = target.Part == ShipParts.Hull ? new TargetPose(ship.Origin, ship.State.Rotation)
                : ShipAim.PartPose(ship.Def, target.Part, ShipFrame.PoseOf(ship.State, ship.Def.CentreOfMass), ship.Parts);
            return true;
        }

        // The hull's nearest point, or a part's (its ball's surface, its pieces').
        public bool TryNearest(in TargetRef target, Vector3 from, out Vector3 point)
        {
            point = from;
            if (zone.Ships().ShipOf(new EntityId(target.Id)) is not { } ship)
            {
                return false;
            }
            if (!ShipAim.Nearest(ship.Def, ship.Frame, ShipFrame.PoseOf(ship.State, ship.Def.CentreOfMass), ship.Parts, target.Part, from, _near, out point))
            {
                point = ShipSystem.ShipClosestPoint(ship, from);
            }
            return true;
        }

        public string ModelKey(ulong id) => zone.Ships().ShipOf(new EntityId(id))?.Def.Key ?? "";

        public CapabilitySet Capabilities(ulong id) => zone.Ships().ShipOf(new EntityId(id)) != null ? zone.Content!.Capabilities.Ship : CapabilitySet.Empty;

        // A ship people walk in: its pieces stop the view (its hull a backdrop, a part's pieces that part, IRON_WAKE.md §4.9:
        // the view goes on through its open ramp, not its hull); then every ship's parts' balls.
        public void Aim(in AimRay ray, ref AimHit best)
        {
            if (zone.Frames.Ships.Count > 0 &&
                ShipQueries.Ray(zone.Frames.Ships, ray.Origin, ray.Direction, ray.Upto(best), zone.Tick, out ShipQueryHit hit) &&
                zone.Ships().ShipOf(new EntityId(hit.Key)) is { Destroyed: false, InOrbit: false } walked)
            {
                int part = walked.Def.PartOfPieces(hit.Hit.Part);
                ray.Consider(ref best, hit.Distance, walked.Ref with { Part = (byte)part }, part == 0 ? AimKind.Backdrop : AimKind.Thing);
            }
            foreach (Ship ship in zone.Ships().All)
            {
                if (!ship.Destroyed && !ship.InOrbit)
                {
                    ShipAim.Balls(ship.Id.Value, ship.Def, ShipFrame.PoseOf(ship.State, ship.Def.CentreOfMass), ship.Parts, Ready(ship), ray, ref best);
                }
            }
        }

        // The doors that let one in now (a part reached through one), as bits.
        private uint Ready(Ship ship)
        {
            uint ready = 0;
            for (int d = 0; d < ship.Doors.Length && d < 32; d++)
            {
                if (zone.Ships().DoorOpenEnough(ship, d))
                {
                    ready |= 1u << d;
                }
            }
            return ready;
        }

        // What the rules know of a part (IRON_WAKE.md §5.1): the ship's capabilities and the part's own (a part reached
        // through a door only while that door lets one in and the ship is at rest), its name, its door's state and rule, its
        // seat's, its health.
        public bool TryFacts(in TargetRef target, in Viewer viewer, out TargetFacts facts)
        {
            facts = default;
            if (zone.Ships().ShipOf(new EntityId(target.Id)) is not { Destroyed: false } ship || target.Part >= ship.Def.Parts.Count)
            {
                return false;
            }
            ShipSystem ships = zone.Ships();
            ShipDef def = ship.Def;
            ShipTargetPart part = def.Parts[target.Part];
            bool still = ships.ShipStill(ship);
            CapabilitySet capabilities = zone.Content!.Capabilities.Ship.With(part.Capabilities);
            if (part.Through >= 0 && (!ships.DoorOpenEnough(ship, part.Through) || !still))
            {
                capabilities = zone.Content!.Capabilities.Ship;
            }
            int seatDoor = part.Seat >= 0 ? def.Seats[part.Seat].Door : -1;
            (float health, float max, ShipPartHealth? healthDef) = ships.PartHealthOf(ship, target.Part);
            facts = new TargetFacts
            {
                Capabilities = capabilities, Flags = zone.Targets().FlagsFor(ship.Ref, viewer), Name = target.Part == 0 ? def.Name : part.Name,
                Open = part.Door >= 0 && ship.Doors[part.Door].Opening, Busy = part.Seat >= 0 && !ship.Occupants[part.Seat].IsNone, Still = still,
                Ship = def, ShipPart = target.Part, DoorRule = part.Door >= 0 ? ships.MayOpen(ship, part.Door).Code : OutcomeCode.Done,
                DoorReady = seatDoor >= 0 && ships.DoorOpenEnough(ship, seatDoor), Jammed = part.Door >= 0 && ships.DoorJammed(ship, part.Door),
                Broken = part.Seat >= 0 && ships.SeatDisabled(ship, part.Seat), Health = healthDef != null ? health / max : -1,
                Position = ship.State.Position,
            };
            return true;
        }

        private Ship? ShipAt(in TargetRef target) => zone.Ships().ShipOf(new EntityId(target.Id)) is { Destroyed: false } ship ? ship : null;

        public Outcome Authorise(Player p, in TargetRef target) => ShipAt(target) is { } ship ? zone.Ships().Authorise(p.Id.Value, ship) : OutcomeCode.NotFound;

        public Outcome Deauthorise(Player p, in TargetRef target) => ShipAt(target) is { } ship ? zone.Ships().Deauthorise(p.Id.Value, ship) : OutcomeCode.NotFound;

        public Outcome AuthoriseTeam(Player p, in TargetRef target, List<ulong> team) =>
            ShipAt(target) is { } ship ? zone.Ships().AuthoriseTeam(p.Id.Value, ship, team) : OutcomeCode.NotFound;

        public Outcome ClearList(Player p, in TargetRef target) => ShipAt(target) is { } ship ? zone.Ships().ClearCrew(p.Id.Value, ship) : OutcomeCode.NotFound;

        public Outcome Repair(Entity entity, Player p, in TargetRef target, IBuildingWallet wallet) =>
            ShipAt(target) is { } ship ? zone.Ships().RepairPart(entity, p, ship, target.Part, wallet) : OutcomeCode.NotFound;
    }

    // ---------------------------------------------------------------- what was TargetSystem

    /// <summary>
    /// A line of sight ends this far in front of the thing's nearest point: the eye must see the air before it. What the
    /// thing touches there (a door's jamb beside its leaf, the floor under a box) doesn't hide it, while a wall it is
    /// against from the far side (0.3 m thick) does.
    /// </summary>
    private const float SightShortBy = 0.1f;

    /// <summary>The ground this close to the thing doesn't hide it: what lies on the ground meets it there.</summary>
    private const float SightGroundAtThing = 0.25f;

    /// <summary>
    /// The one reach rule (GROUND_SYSTEMS.md §3.4): whether the eye reaches a thing: its nearest surface within
    /// <paramref name="reach"/> metres, and (unless <paramref name="sight"/> is false: a workbench's level, an open
    /// container staying open) in sight. Done, or NotFound, OutOfReach or OutOfSight.
    /// </summary>
    public Outcome Reach(Vector3 eye, in TargetRef target, float reach, bool sight = true)
    {
        if (!TryNearest(target, eye, out Vector3 point))
        {
            return OutcomeCode.NotFound;
        }
        if (Vector3.DistanceSquared(eye, point) > reach * reach)
        {
            return OutcomeCode.OutOfReach;
        }
        return !sight || InSight(eye, point, target) ? Outcome.Done : OutcomeCode.OutOfSight;
    }

    /// <summary>
    /// Whether the eye sees a thing at one of its points: from the eye to just in front of the point, no building body but
    /// the thing's own (by its ID: the claim device's body is its box's too), no ship's piece (IRON_WAKE.md §4.9: aboard,
    /// the hull hides the shore; ashore, the inside), and no ground short of the thing.
    /// </summary>
    public bool InSight(Vector3 eye, Vector3 point, in TargetRef target)
    {
        Vector3 d = point - eye;
        float distance = d.Length(), length = distance - SightShortBy;
        if (length <= 0)
        {
            return true;
        }
        Vector3 direction = d / distance;
        if (Building.World?.Physics is { } physics && physics.Ray(eye, direction, length, out RayHit hit, buildingOnly: true) &&
            (physics.OwnerOf(hit.Body) is not ITargetable owner || owner.Ref.Id != target.Id))
        {
            return false;
        }
        if (Zone.Frames.Ships.Count > 0 && ShipQueries.Ray(Zone.Frames.Ships, eye, direction, length, Tick, out _))
        {
            return false;
        }
        float ground = Combat.GroundCrossing(eye, direction, length);
        return ground < 0 || ground > distance - SightGroundAtThing;
    }

    public override int InitOrder => 60;

    protected internal override void Init() => InitTargets();
}

/// <summary>The accessor on the zone (GROUND_SYSTEMS.md §12.2, K4).</summary>
public static class TargetSystemAccess
{
    public static TargetSystem Targets(this Zone zone) => zone.Require<TargetSystem>();
}

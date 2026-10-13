using System;
using System.Numerics;
using Hollow.Simulation.Animals;
using Hollow.Simulation.Building;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Players;
using Hollow.Simulation.Raid;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;
using Hollow.Simulation.Capabilities;
using Hollow.Simulation.Teams;
using System.Collections.Generic;

namespace Hollow.Simulation;

// Naming world things (GROUND_SYSTEMS.md §3.1): a TargetRef routes to its family, each family registered once here, which
// answers for its members: whether one exists, the thing itself, where it (or a part: a door's leaf, a body's bone) is,
// the nearest point of its bounds, what it is drawn as. The four lookups the zone had (the entity store, the building's,
// the animals', a scan of the explosives) are each one family's business now.
public sealed partial class Zone
{
    private Registry<ITargetFamily>? _families;
    private Registry<ICapability>? _capabilities;

    /// <summary>The families of world things, by <see cref="TargetFamily"/> (null in a movement-only zone).</summary>
    public Registry<ITargetFamily> Families => _families ?? throw new InvalidOperationException("a zone without game data names no things");

    private void InitTargets()
    {
        var families = Registry<ITargetFamily>.ForEnum<TargetFamily>("target families", complete: true, TargetFamily.None);
        foreach (ITargetFamily family in new ITargetFamily[]
                 {
                     new GroundTargets(this), new CharacterTargets(this), new AnimalTargets(this), new NaturalTargets(this), new ThingTargets(this),
                     new PieceTargets(this), new DoorTargets(this), new CupboardTargets(this), new ExplosiveTargets(this), new ShipTargets(this),
                 })
        {
            families.Add((int)family.Family, family);
        }
        _families = families.Seal();
        _capabilities = CapabilityHandlers.Create(Content!.Capabilities);
    }

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
    public Viewer ViewerOf(Player p) => new(p.Id, Teams.TeamOf(p.Id), KeysOf(p));

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

    // ---------------------------------------------------------------- the families

    /// <summary>The terrain: always there, its pose the zone frame itself (what lies on it keeps its own place).</summary>
    private sealed class GroundTargets(Zone zone) : ITargetFamily
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
    }

    /// <summary>Players' characters, awake, down or asleep (a dead one is gone: its body is a thing); parts are bones.</summary>
    private sealed class CharacterTargets(Zone zone) : ITargetFamily
    {
        public TargetFamily Family => TargetFamily.Character;

        public bool Moves => true;

        public bool Exists(ulong id) => zone.TryFindPlayer(new EntityId(id), out _, out Player p) && !p.Dead;

        public object? Resolve(ulong id) => zone.TryFindPlayer(new EntityId(id), out _, out Player p) && !p.Dead ? p : null;

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

        public CapabilitySet Capabilities(ulong id) => CapabilitySet.Empty;

        // The body as it is now: standing, crouching, or lying asleep or down.
        private bool Body(ulong id, out HitPose pose)
        {
            pose = default;
            if (!zone.TryFindPlayer(new EntityId(id), out Entity e, out Player p) || p.Dead)
            {
                return false;
            }
            ref Character c = ref zone.Entities.Get<Character>(e);
            pose = new HitPose
            {
                Feet = c.State.Position,
                Up = zone.Frame.UpAt(c.State.Position),
                Yaw = c.Input.Yaw,
                Pose = p.Sleeping || p.Wounded ? Combat.Pose.Lying : c.State.Stance == Stance.Crouching ? Combat.Pose.Crouching : Combat.Pose.Standing,
            };
            return true;
        }
    }

    /// <summary>Animals, alive or a corpse until it goes; parts are bones.</summary>
    private sealed class AnimalTargets(Zone zone) : ITargetFamily
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
            HitPose body = zone.PoseOf(a);
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
            Span<HitShape> all = shapes[..AnimalHitboxes.Shapes(a.Species, zone.PoseOf(a), shapes)];
            point = target.Part == 0 ? Hitboxes.NearestPoint(all, from) : all[Math.Clamp(target.Part - 1, 0, all.Length - 1)].Nearest(from);
            return true;
        }

        public string ModelKey(ulong id) => Find(id)?.Species.Key ?? "";

        public CapabilitySet Capabilities(ulong id) => CapabilitySet.Empty;

        private Animal? Find(ulong id) => zone.Animals?.Find(new EntityId(id)) is { Removed: false } a ? a : null;
    }

    /// <summary>Trees, outcrops, pickups and bushes (felled, broken or picked ones are gone until they come back).</summary>
    private sealed class NaturalTargets(Zone zone) : ITargetFamily
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
            HitShape shape;
            if (kind!.Class == NaturalClass.Tree)
            {
                (Vector3 a, Vector3 b, float r) = NaturalShapes.Trunk(o, kind);
                shape = new HitShape(a, b, r, HitRegion.Body);
            }
            else if (kind.IsHarvested || kind.Class == NaturalClass.Boulder)
            {
                (Vector3 centre, float r) = NaturalShapes.Rock(o, kind);
                shape = new HitShape(centre, centre, r, HitRegion.Body);
            }
            else
            {
                Vector3 middle = NaturalShapes.Middle(o, kind);
                shape = new HitShape(middle, middle, kind.Radius * o.Scale, HitRegion.Body);
            }
            point = shape.Nearest(from);
            return true;
        }

        public string ModelKey(ulong id) => Find(id, out _, out NaturalKind? kind) ? kind!.Key : "";

        public CapabilitySet Capabilities(ulong id) => CapabilitySet.Empty;

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
    private sealed class ThingTargets(Zone zone) : ITargetFamily
    {
        public TargetFamily Family => TargetFamily.Thing;

        public bool Moves => false;

        public bool Exists(ulong id) => zone.StaticOf(new EntityId(id)) != null;

        public object? Resolve(ulong id) => zone.StaticOf(new EntityId(id));

        public bool TryPose(in TargetRef target, out TargetPose pose)
        {
            pose = default;
            if (zone.StaticOf(new EntityId(target.Id)) is not { } thing)
            {
                return false;
            }
            pose = new TargetPose(thing.Position, Geometry.Upright(thing.Up, thing.Yaw));
            return true;
        }

        public bool TryNearest(in TargetRef target, Vector3 from, out Vector3 point)
        {
            point = from;
            if (zone.StaticOf(new EntityId(target.Id)) is not { } thing)
            {
                return false;
            }
            point = thing.Nearest(from);
            return true;
        }

        public string ModelKey(ulong id) => zone.StaticOf(new EntityId(id))?.ModelKey ?? "";

        public CapabilitySet Capabilities(ulong id) => zone.StaticOf(new EntityId(id))?.CapabilitiesIn(zone.Content!.Capabilities) ?? CapabilitySet.Empty;
    }

    /// <summary>Building pieces (one coming down still stands until it is taken away).</summary>
    private sealed class PieceTargets(Zone zone) : ITargetFamily
    {
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
            point = ClosestPoint(zone.Building!.Data, piece, from);
            return true;
        }

        public string ModelKey(ulong id) => Find(id)?.Def.Key ?? "";

        public CapabilitySet Capabilities(ulong id) => CapabilitySet.Empty;

        private Piece? Find(ulong id) => zone.Building?.PieceOf(new EntityId(id)) is { Gone: false } piece ? piece : null;
    }

    /// <summary>Doors in their frames; a door's parts are its leaves (part i + 1: leaf i), which swing.</summary>
    private sealed class DoorTargets(Zone zone) : ITargetFamily
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

        public bool TryNearest(in TargetRef target, Vector3 from, out Vector3 point)
        {
            point = from;
            if (Find(target.Id) is not { } door)
            {
                return false;
            }
            point = ClosestPoint(door, from).Point;
            return true;
        }

        public string ModelKey(ulong id) => Find(id)?.Def.Key ?? "";

        public CapabilitySet Capabilities(ulong id)
        {
            CapabilityData data = zone.Content!.Capabilities;
            return Find(id) is { } door ? door.Lock is { } fitted ? data.Door.With(data.Lock(fitted.Kind)) : data.Door : CapabilitySet.Empty;
        }

        private Door? Find(ulong id) => zone.Building?.DoorOf(new EntityId(id)) is { Gone: false } door ? door : null;
    }

    /// <summary>The claim device (the base's cupboard), found by its box.</summary>
    private sealed class CupboardTargets(Zone zone) : ITargetFamily
    {
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
            point = zone.DeployableOf(claim) is { } box ? ClosestPoint(box, from) : claim.ZoneTransform.Position;
            return true;
        }

        public string ModelKey(ulong id) => Find(id) != null ? zone.Content!.ClaimDeployable.Key : "";

        public CapabilitySet Capabilities(ulong id) => Find(id) != null ? zone.Content!.Capabilities.Cupboard : CapabilitySet.Empty;

        private ClaimDevice? Find(ulong id) => zone.Building?.ClaimOf(new EntityId(id)) is { Gone: false } claim ? claim : null;
    }

    /// <summary>Explosives in the world, by their IDs (they were a scan of the list).</summary>
    private sealed class ExplosiveTargets(Zone zone) : ITargetFamily
    {
        public TargetFamily Family => TargetFamily.Explosive;

        public bool Moves => true;

        public bool Exists(ulong id) => zone.ExplosiveOf(new EntityId(id)) != null;

        public object? Resolve(ulong id) => zone.ExplosiveOf(new EntityId(id));

        public bool TryPose(in TargetRef target, out TargetPose pose)
        {
            pose = default;
            if (zone.ExplosiveOf(new EntityId(target.Id)) is not { } e)
            {
                return false;
            }
            pose = new TargetPose(e.Position, Geometry.Upright(e.Normal, 0));
            return true;
        }

        public bool TryNearest(in TargetRef target, Vector3 from, out Vector3 point)
        {
            point = from;
            if (zone.ExplosiveOf(new EntityId(target.Id)) is not { } e)
            {
                return false;
            }
            point = new HitShape(e.Position, e.Position, (float)e.Stats.Radius, HitRegion.Body).Nearest(from);
            return true;
        }

        public string ModelKey(ulong id) => zone.ExplosiveOf(new EntityId(id))?.Item.Key ?? "";

        public CapabilitySet Capabilities(ulong id) => zone.ExplosiveOf(new EntityId(id)) != null ? zone.Content!.Capabilities.Explosive : CapabilitySet.Empty;
    }

    /// <summary>Ships, wrecks included until they go; found by their hulls.</summary>
    private sealed class ShipTargets(Zone zone) : ITargetFamily
    {
        public TargetFamily Family => TargetFamily.Ship;

        public bool Moves => true;

        public bool Exists(ulong id) => zone.ShipOf(new EntityId(id)) != null;

        public object? Resolve(ulong id) => zone.ShipOf(new EntityId(id));

        public bool TryPose(in TargetRef target, out TargetPose pose)
        {
            pose = default;
            if (zone.ShipOf(new EntityId(target.Id)) is not { } ship)
            {
                return false;
            }
            pose = new TargetPose(ship.Origin, ship.State.Rotation);
            return true;
        }

        public bool TryNearest(in TargetRef target, Vector3 from, out Vector3 point)
        {
            point = from;
            if (zone.ShipOf(new EntityId(target.Id)) is not { } ship)
            {
                return false;
            }
            point = ShipClosestPoint(ship, from);
            return true;
        }

        public string ModelKey(ulong id) => zone.ShipOf(new EntityId(id))?.Def.Key ?? "";

        public CapabilitySet Capabilities(ulong id) => zone.ShipOf(new EntityId(id)) != null ? zone.Content!.Capabilities.Ship : CapabilitySet.Empty;
    }
}

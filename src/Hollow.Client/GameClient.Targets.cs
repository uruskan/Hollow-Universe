using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Animals;
using Hollow.Simulation.Building;
using Hollow.Simulation.Capabilities;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Client;

// The world's things as this client knows them, named the server's way (GROUND_SYSTEMS.md §3.1, §5): one view per target
// family over what the server told this client (the things in range, the people and animals as drawn, the natural
// objects it generates itself, the building's mirror, explosives, ships, spent arrows). Each answers where a member (or a
// part: a door's leaf, a body's bone, a ship's hatch) is, its nearest surface, what lies along the interact key's ray, and
// what the interaction rules know of it, with the same shapes and rules the server's families use; so this client aims
// at, measures and offers what the server will.
public sealed partial class GameClient
{
    private Registry<ITargetView>? _targetRegistry;
    private ITargetView[] _targetViews = Array.Empty<ITargetView>();
    private readonly List<NaturalObject> _aimNature = new();
    private readonly List<Piece> _aimPieces = new();

    /// <summary>The families as this client aims through them, in their numbers' order (every family has one: checked).</summary>
    public IReadOnlyList<ITargetView> TargetViews => _targetViews;

    private void StartTargets()
    {
        var registry = Registry<ITargetView>.ForEnum<TargetFamily>("client target families", complete: true, TargetFamily.None);
        foreach (ITargetView view in new ITargetView[]
                 {
                     new GroundView(this), new CharacterView(this), new AnimalView(this), new NaturalView(this), new ThingView(this),
                     new PieceView(this), new DoorView(this), new CupboardView(this), new ExplosiveView(this), new ShipView(this),
                     new ArrowView(this),
                 })
        {
            registry.Add((int)view.Family, view);
        }
        _targetRegistry = registry.Seal();
        _targetViews = new ITargetView[registry.Count];
        int n = 0;
        foreach (ITargetView view in registry.Handlers)
        {
            _targetViews[n++] = view;
        }
        Array.Resize(ref _targetViews, n);
    }

    /// <summary>The view of a family (null for none, or a number from outside that names none).</summary>
    public ITargetView? TargetViewOf(TargetFamily family) => _targetRegistry?.Find((int)family);

    /// <summary>Where a target (or its part) is now, as this client draws it.</summary>
    public bool TryPose(in TargetRef target, out TargetPose pose)
    {
        pose = default;
        return TargetViewOf(target.Family)?.TryPose(target, out pose) == true;
    }

    /// <summary>Where a thing linked to a support is now (a spent arrow on a body's bone).</summary>
    public bool TryPose(in SupportLink link, out TargetPose pose)
    {
        pose = default;
        if (link.IsNone || !TryPose(link.Support, out TargetPose support))
        {
            return false;
        }
        pose = link.In(support);
        return true;
    }

    // A body drawn now, as the server builds its hit shapes (Zone's CharacterTargets): standing, crouching, or lying.
    private HitPose PoseOf(RemoteEntity r) => new()
    {
        Feet = r.Position,
        Up = Frame.UpAt(r.Position),
        Yaw = r.Yaw,
        Pose = r.Sleeping || r.Wounded ? Simulation.Combat.Pose.Lying : r.Stance == Stance.Crouching ? Simulation.Combat.Pose.Crouching : Simulation.Combat.Pose.Standing,
    };

    private HitPose PoseOf(ClientAnimal a) => new()
    {
        Feet = a.Position,
        Up = Frame.UpAt(a.Position),
        Yaw = a.Yaw,
        Pose = a.Dead ? Simulation.Combat.Pose.Lying : Simulation.Combat.Pose.Standing,
    };

    private RemoteEntity? CharacterOf(ulong id)
    {
        foreach (RemoteEntity r in _remotes.Values)
        {
            if (r.Id.Value == id && r.Kind == Net.Protocol.EntityKind.Character && r.Spawned && r.HasState && (r.Flags & MoveFlags.Dead) == 0)
            {
                return r;
            }
        }
        return null;
    }

    private ClientAnimal? AnimalById(ulong id)
    {
        foreach (ClientAnimal a in _animals.Values)
        {
            if (a.Id.Value == id)
            {
                return a;
            }
        }
        return null;
    }

    private RemoteShip? ShipById(ulong id)
    {
        foreach (RemoteShip s in _ships.Values)
        {
            if (s.Id.Value == id && s.Spawned && s.HasState && s.Def != null)
            {
                return s;
            }
        }
        return null;
    }

    // A body part's pose (a bone a spent arrow rides), or the whole body's: as the server's families give them.
    private static TargetPose BonePose(ReadOnlySpan<HitShape> shapes, in HitPose body, byte part) =>
        part == 0 ? new TargetPose(body.Feet, Geometry.Upright(body.Up, body.Yaw)) : Hitboxes.ShapePose(shapes, part - 1, body);

    // ---------------------------------------------------------------- the families

    private sealed class GroundView(GameClient c) : ITargetView
    {
        public TargetFamily Family => TargetFamily.Ground;

        public bool TryPose(in TargetRef target, out TargetPose pose)
        {
            pose = new TargetPose(Vector3.Zero, Quaternion.Identity);
            return target.Id == 0;
        }

        public bool TryNearest(in TargetRef target, Vector3 from, out Vector3 point)
        {
            point = from;
            return target.Id == 0;
        }

        public void Aim(in AimRay ray, ref AimHit best)
        {
            if (c._surface != null && c.Zone != null)
            {
                ray.Consider(ref best, new GroundProbe(c._surface, c.Frame).Crossing(ray.Origin, ray.Direction, ray.Upto(best)), TargetRef.Ground,
                    AimKind.Backdrop);
            }
        }

        public bool TryFacts(in TargetRef target, in Viewer viewer, out TargetFacts facts)
        {
            facts = new TargetFacts { Name = "" };
            return target.Id == 0;
        }
    }

    private sealed class CharacterView(GameClient c) : ITargetView
    {
        public TargetFamily Family => TargetFamily.Character;

        public bool TryPose(in TargetRef target, out TargetPose pose)
        {
            pose = default;
            if (c.CharacterOf(target.Id) is not { } r)
            {
                return false;
            }
            HitPose body = c.PoseOf(r);
            Span<HitShape> shapes = stackalloc HitShape[Hitboxes.MostShapes];
            pose = BonePose(shapes[..Hitboxes.Shapes(body, shapes)], body, target.Part);
            return true;
        }

        public bool TryNearest(in TargetRef target, Vector3 from, out Vector3 point)
        {
            point = from;
            if (c.CharacterOf(target.Id) is not { } r)
            {
                return false;
            }
            Span<HitShape> shapes = stackalloc HitShape[Hitboxes.MostShapes];
            point = Hitboxes.NearestPoint(shapes[..Hitboxes.Shapes(c.PoseOf(r), shapes)], from);
            return true;
        }

        public void Aim(in AimRay ray, ref AimHit best)
        {
            float length = ray.Upto(best);
            foreach (RemoteEntity r in c._remotes.Values)
            {
                if (r.Kind != Net.Protocol.EntityKind.Character || !r.Spawned || !r.HasState || (r.Flags & MoveFlags.Dead) != 0 || r.Id.Value == ray.Ignore ||
                    Vector3.DistanceSquared(r.Position, ray.Origin) > (length + 2.5f) * (length + 2.5f))
                {
                    continue;
                }
                ray.Consider(ref best, Hitboxes.Raycast(c.PoseOf(r), ray.Origin, ray.Direction, length, AimShapes.BodyMargin, out _),
                    new TargetRef(TargetFamily.Character, r.Id.Value));
            }
        }

        public bool TryFacts(in TargetRef target, in Viewer viewer, out TargetFacts facts)
        {
            facts = default;
            if (c.CharacterOf(target.Id) is not { } r)
            {
                return false;
            }
            CapabilityData data = c.Content.Capabilities;
            facts = new TargetFacts
            {
                Capabilities = r.Sleeping ? data.Sleeper : r.Wounded ? data.Down : data.Awake,
                Flags = c.IsTeammate(r.Id) ? ViewerFlags.Teammate : ViewerFlags.None, Name = r.Sleeping ? $"{r.Name} (asleep)" : r.Name, Body = true,
                Position = r.Position,
            };
            return true;
        }
    }

    private sealed class AnimalView(GameClient c) : ITargetView
    {
        public TargetFamily Family => TargetFamily.Animal;

        public bool TryPose(in TargetRef target, out TargetPose pose)
        {
            pose = default;
            if (c.AnimalById(target.Id) is not { Species: { } species } a)
            {
                return false;
            }
            HitPose body = c.PoseOf(a);
            Span<HitShape> shapes = stackalloc HitShape[Hitboxes.MostShapes];
            pose = BonePose(shapes[..AnimalHitboxes.Shapes(species, body, shapes)], body, target.Part);
            return true;
        }

        public bool TryNearest(in TargetRef target, Vector3 from, out Vector3 point)
        {
            point = from;
            if (c.AnimalById(target.Id) is not { Species: { } species } a)
            {
                return false;
            }
            Span<HitShape> shapes = stackalloc HitShape[Hitboxes.MostShapes];
            point = Hitboxes.NearestPoint(shapes[..AnimalHitboxes.Shapes(species, c.PoseOf(a), shapes)], from);
            return true;
        }

        public void Aim(in AimRay ray, ref AimHit best)
        {
            float length = ray.Upto(best);
            foreach (ClientAnimal a in c._animals.Values)
            {
                if (a.Species is { } species && Vector3.DistanceSquared(a.Position, ray.Origin) <= (length + 4) * (length + 4))
                {
                    ray.Consider(ref best, AnimalHitboxes.Raycast(species, c.PoseOf(a), ray.Origin, ray.Direction, length, AimShapes.BodyMargin, out _),
                        new TargetRef(TargetFamily.Animal, a.Id.Value), AimKind.Backdrop);
                }
            }
        }

        public bool TryFacts(in TargetRef target, in Viewer viewer, out TargetFacts facts)
        {
            facts = default;
            if (c.AnimalById(target.Id) is not { } a)
            {
                return false;
            }
            facts = new TargetFacts
            {
                Capabilities = a.Dead ? c.Content.Capabilities.Carcass : CapabilitySet.Empty, Name = a.Species?.Name ?? a.SpeciesKey, Position = a.Position,
            };
            return true;
        }
    }

    private sealed class NaturalView(GameClient c) : ITargetView
    {
        public TargetFamily Family => TargetFamily.Natural;

        private bool Find(ulong id, out NaturalObject o, out NaturalKind? kind)
        {
            kind = null;
            if (c.Nature == null || c.Natural == null || !c.Nature.TryGet(id, out o) || c.Natural.IsDepleted(id))
            {
                o = default;
                return false;
            }
            kind = c.Content.Natural(o.Kind);
            return kind != null;
        }

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

        public void Aim(in AimRay ray, ref AimHit best)
        {
            if (c.Nature != null && c.Natural != null)
            {
                NaturalAim.Aim(c.Nature, c.Natural, c.Content, ray, ref best, c._aimNature);
            }
        }

        public bool TryFacts(in TargetRef target, in Viewer viewer, out TargetFacts facts)
        {
            facts = default;
            if (!Find(target.Id, out NaturalObject o, out NaturalKind? kind))
            {
                return false;
            }
            facts = new TargetFacts
            {
                Capabilities = kind!.IsPicked ? c.Content.Capabilities.Pickup : CapabilitySet.Empty, Name = kind.Name, Stays = kind.Class == NaturalClass.Bush,
                Count = 1, Position = o.Position,
            };
            return true;
        }
    }

    private sealed class ThingView(GameClient c) : ITargetView
    {
        public TargetFamily Family => TargetFamily.Thing;

        // A thing's box (a deployable or prop) or ball (a bag or body), and its up.
        private (DeployableDef? Def, Vector3 Up) Shape(RemoteThing thing) => (thing.Deployable(c.Content), c.Frame.UpAt(thing.Position));

        // How the prompt names it: its own name (a bag, a prop; a body by whose it is), else its item's.
        private static string NameOf(RemoteThing thing, DeployableDef? def) =>
            thing.Kind == StaticKind.Corpse ? $"{thing.Name}'s body" : thing.Name.Length > 0 ? thing.Name : def?.Item.Name ?? "";

        public bool TryPose(in TargetRef target, out TargetPose pose)
        {
            pose = default;
            if (c.ThingOf(new EntityId(target.Id)) is not { } thing)
            {
                return false;
            }
            pose = new TargetPose(thing.Position, Geometry.Upright(c.Frame.UpAt(thing.Position), thing.Yaw));
            return true;
        }

        public bool TryNearest(in TargetRef target, Vector3 from, out Vector3 point)
        {
            point = from;
            if (c.ThingOf(new EntityId(target.Id)) is not { } thing)
            {
                return false;
            }
            (DeployableDef? def, Vector3 up) = Shape(thing);
            point = def != null ? Geometry.NearestOnBox(thing.Position, up, thing.Yaw, def.HalfExtents, from) : AimShapes.NearestOnDropped(thing.Position, up, thing.Kind, from);
            return true;
        }

        // The tool cupboard's box is the cupboard's (its building body is found by the pieces' view).
        public void Aim(in AimRay ray, ref AimHit best)
        {
            float length = ray.Upto(best);
            ushort cupboard = c.Content.ClaimDeployable.Id;
            foreach (RemoteThing thing in c._things.Values)
            {
                if (thing.Look == cupboard && thing.Kind == StaticKind.Deployable ||
                    Vector3.DistanceSquared(thing.Position, ray.Origin) > (length + 2) * (length + 2))
                {
                    continue;
                }
                (DeployableDef? def, Vector3 up) = Shape(thing);
                float t = def != null ? AimShapes.Box(ray, thing.Position, up, thing.Yaw, def.HalfExtents) : AimShapes.Dropped(ray, thing.Position, up, thing.Kind);
                ray.Consider(ref best, t, new TargetRef(TargetFamily.Thing, thing.Id.Value));
            }
        }

        public bool TryFacts(in TargetRef target, in Viewer viewer, out TargetFacts facts)
        {
            facts = default;
            if (c.ThingOf(new EntityId(target.Id)) is not { } thing)
            {
                return false;
            }
            DeployableDef? def = thing.Deployable(c.Content);
            facts = new TargetFacts
            {
                Capabilities = thing.Capabilities, Flags = thing.Flags, Name = NameOf(thing, def), Count = thing.Count,
                Body = thing.Kind == StaticKind.Corpse, Deployable = def, Position = thing.Position,
            };
            return true;
        }
    }

    private sealed class PieceView(GameClient c) : ITargetView
    {
        public TargetFamily Family => TargetFamily.Piece;

        private Piece? Find(ulong id) => c.Building?.PieceOf(new EntityId(id)) is { Gone: false } piece ? piece : null;

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
            if (Find(target.Id) is not { } piece || c.Building is not { } building)
            {
                return false;
            }
            Core.Vector3D local = PieceGeometry.ClosestPoint(building.Data, piece.Def, piece.Pose, piece.Structure.Frame.ToLocal(new Core.Vector3D(from.X, from.Y, from.Z)));
            Core.Vector3D zone = piece.Structure.Frame.ToZone(local);
            point = new Vector3((float)zone.X, (float)zone.Y, (float)zone.Z);
            return true;
        }

        public void Aim(in AimRay ray, ref AimHit best)
        {
            if (c.Building is { } building)
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
    }

    private sealed class DoorView(GameClient c) : ITargetView
    {
        public TargetFamily Family => TargetFamily.Door;

        private Door? Find(ulong id) => c.Building?.DoorOf(new EntityId(id)) is { Gone: false } door ? door : null;

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

        // Its leaves, or its opening, whichever is nearer (a door's target includes its opening): the server's rule.
        public bool TryNearest(in TargetRef target, Vector3 from, out Vector3 point)
        {
            point = from;
            if (Find(target.Id) is not { } door || c.Building is not { } building)
            {
                return false;
            }
            point = DoorShapes.NearestOnLeaves(door, from);
            if (target.Part == 0 && AimShapes.NearestInDoorway(building.Data, door.Frame, from, out Vector3 opening) &&
                Vector3.DistanceSquared(opening, from) < Vector3.DistanceSquared(point, from))
            {
                point = opening;
            }
            return true;
        }

        public void Aim(in AimRay ray, ref AimHit best)
        {
            if (c.Building is { } building)
            {
                BuildingAim.Openings(building, ray, ref best, c._aimPieces);
            }
        }

        public bool TryFacts(in TargetRef target, in Viewer viewer, out TargetFacts facts)
        {
            facts = default;
            if (Find(target.Id) is not { } door || c.BuildingReplica is not { } replica)
            {
                return false;
            }
            DoorState state = replica.StateOf(door);
            CapabilityData data = c.Content.Capabilities;
            facts = new TargetFacts
            {
                Capabilities = data.Door.With(data.Lock(state.Lock)), Flags = state.Flags, Name = door.Def.Name, Open = door.Open, Lock = state.Lock,
                Position = door.Frame.ZoneTransform.Position,
            };
            return true;
        }
    }

    private sealed class CupboardView(GameClient c) : ITargetView
    {
        public TargetFamily Family => TargetFamily.Cupboard;

        private ClaimDevice? Find(ulong id) => c.Building?.ClaimOf(new EntityId(id)) is { Gone: false } claim ? claim : null;

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

        // Its box (the cupboard's deployable, as the server measures it).
        public bool TryNearest(in TargetRef target, Vector3 from, out Vector3 point)
        {
            point = from;
            if (Find(target.Id) is not { } claim)
            {
                return false;
            }
            (Vector3 position, Quaternion rotation) = claim.ZoneTransform;
            Vector3 up = c.Frame.UpAt(position), along = Vector3.Transform(Vector3.UnitZ, rotation);
            point = Geometry.NearestOnBox(position, up, MathF.Atan2(along.X, along.Z), c.Content.ClaimDeployable.HalfExtents, from);
            return true;
        }

        // Its body is a building body: the pieces' view finds it.
        public void Aim(in AimRay ray, ref AimHit best)
        {
        }

        public bool TryFacts(in TargetRef target, in Viewer viewer, out TargetFacts facts)
        {
            facts = default;
            if (Find(target.Id) is not { } claim || c.BuildingReplica is not { } replica)
            {
                return false;
            }
            ViewerFlags box = c.ThingOf(claim.Id)?.Flags ?? ViewerFlags.None;
            facts = new TargetFacts
            {
                Capabilities = c.Content.Capabilities.Cupboard, Flags = replica.StateOf(claim).Flags | box, Name = c.Content.ClaimDeployable.Item.Name,
                Position = claim.ZoneTransform.Position,
            };
            return true;
        }
    }

    private sealed class ExplosiveView(GameClient c) : ITargetView
    {
        public TargetFamily Family => TargetFamily.Explosive;

        public bool TryPose(in TargetRef target, out TargetPose pose)
        {
            pose = default;
            if (c.ExplosiveOf(new EntityId(target.Id)) is not { } e)
            {
                return false;
            }
            pose = new TargetPose(e.PositionAt(c.ServerTickNow), Geometry.Upright(e.Normal, 0));
            return true;
        }

        public bool TryNearest(in TargetRef target, Vector3 from, out Vector3 point)
        {
            point = from;
            if (c.ExplosiveOf(new EntityId(target.Id)) is not { } e)
            {
                return false;
            }
            point = AimShapes.NearestOnSphere(e.PositionAt(c.ServerTickNow), (float)(e.Stats?.Radius ?? AimShapes.ExplosiveRadius), from);
            return true;
        }

        public void Aim(in AimRay ray, ref AimHit best)
        {
            float length = ray.Upto(best);
            foreach (ClientExplosive e in c._explosives.Values)
            {
                Vector3 at = e.PositionAt(c.ServerTickNow);
                if (Vector3.DistanceSquared(at, ray.Origin) <= (length + 1) * (length + 1))
                {
                    ray.Consider(ref best, AimShapes.Sphere(ray, at, AimShapes.ExplosiveRadius), new TargetRef(TargetFamily.Explosive, e.Id.Value));
                }
            }
        }

        public bool TryFacts(in TargetRef target, in Viewer viewer, out TargetFacts facts)
        {
            facts = default;
            if (c.ExplosiveOf(new EntityId(target.Id)) is not { } e)
            {
                return false;
            }
            CapabilityData data = c.Content.Capabilities;
            facts = new TargetFacts { Capabilities = e.Dud ? data.Dud : data.Explosive, Name = e.Item.Name, Position = e.PositionAt(c.ServerTickNow) };
            return true;
        }
    }

    private sealed class ShipView(GameClient c) : ITargetView
    {
        public TargetFamily Family => TargetFamily.Ship;

        public bool TryPose(in TargetRef target, out TargetPose pose)
        {
            pose = default;
            if (c.ShipById(target.Id) is not { Def: { } def } ship)
            {
                return false;
            }
            pose = new TargetPose(target.Part == ShipParts.Hull ? ship.Origin : ship.PointOf(def.PartPoint(target.Part)), ship.Rotation);
            return true;
        }

        public bool TryNearest(in TargetRef target, Vector3 from, out Vector3 point)
        {
            point = from;
            if (c.ShipById(target.Id) is not { Def: { } def } ship)
            {
                return false;
            }
            point = target.Part == ShipParts.Hull ? ShipShapes.NearestOnHull(def, ship.Position, ship.Rotation, from) : ship.PointOf(def.PartPoint(target.Part));
            return true;
        }

        public void Aim(in AimRay ray, ref AimHit best)
        {
            foreach (RemoteShip ship in c._ships.Values)
            {
                if (ship is { Spawned: true, HasState: true, Def: { } def } && !ship.Has(ShipNetFlags.Destroyed) && !ship.Has(ShipNetFlags.InOrbit))
                {
                    ShipAim.Parts(ship.Id.Value, def, ship.Position, ship.Rotation, ship.Has(ShipNetFlags.HatchOpen), ray, ref best);
                }
            }
        }

        // The cargo is a container's while the hatch is open and the ship at rest (this client knows it landed or asleep).
        public bool TryFacts(in TargetRef target, in Viewer viewer, out TargetFacts facts)
        {
            facts = default;
            if (c.ShipById(target.Id) is not { Def: { } def } ship || ship.Has(ShipNetFlags.Destroyed))
            {
                return false;
            }
            CapabilityData data = c.Content.Capabilities;
            bool open = ship.Has(ShipNetFlags.HatchOpen), still = ship.Has(ShipNetFlags.Landed) || ship.Has(ShipNetFlags.Asleep);
            facts = new TargetFacts
            {
                Capabilities = target.Part == ShipParts.Cargo && open && still ? data.Ship : data.ShipPart, Flags = ship.Viewer, Name = def.Name, Open = open,
                Busy = ship.Has(ShipNetFlags.Piloted), Still = still, Position = ship.Position,
            };
            return true;
        }
    }

    private sealed class ArrowView(GameClient c) : ITargetView
    {
        public TargetFamily Family => TargetFamily.Arrow;

        public bool TryPose(in TargetRef target, out TargetPose pose)
        {
            pose = default;
            if (c.ArrowOf(new EntityId(target.Id)) is not { Placed: true } arrow)
            {
                return false;
            }
            pose = arrow.Pose;
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
            foreach (RemoteArrow arrow in c._arrows.Values)
            {
                if (arrow.Placed && Vector3.DistanceSquared(arrow.Pose.Position, ray.Origin) <= (reach + 2) * (reach + 2))
                {
                    (Vector3 nock, Vector3 tip) = SpentArrow.Shaft(arrow.Pose);
                    ray.Consider(ref best, AimShapes.Capsule(ray, nock, tip, AimShapes.ArrowRadius), new TargetRef(TargetFamily.Arrow, arrow.Id.Value));
                }
            }
        }

        public bool TryFacts(in TargetRef target, in Viewer viewer, out TargetFacts facts)
        {
            facts = default;
            if (c.ArrowOf(new EntityId(target.Id)) is not { Placed: true } arrow)
            {
                return false;
            }
            facts = new TargetFacts { Capabilities = c.Content.Capabilities.Arrow, Name = arrow.Item.Name, Count = 1, Position = arrow.Pose.Position };
            return true;
        }
    }
}

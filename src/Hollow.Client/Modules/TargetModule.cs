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
public sealed class TargetModule : ClientModule
{
    private Registry<ITargetView>? _targetRegistry;
    private ITargetView[] _targetViews = Array.Empty<ITargetView>();
    private readonly List<NaturalObject> _aimNature = new();
    private readonly List<Piece> _aimPieces = new();
    private SurvivalModule? _survivalModule;
    private SurvivalModule Survival => _survivalModule ??= Client.Require<SurvivalModule>();
    private WorldModule? _worldModule;
    private WorldModule World => _worldModule ??= Client.Require<WorldModule>();
    private BuildingModule? _buildingModule;
    private BuildingModule Building => _buildingModule ??= Client.Require<BuildingModule>();
    private RaidModule? _raidModule;
    private RaidModule Raid => _raidModule ??= Client.Require<RaidModule>();
    private ShipModule? _shipModule;
    private ShipModule Ships => _shipModule ??= Client.Require<ShipModule>();
    private AnimalModule? _animalModule;
    private AnimalModule Animals => _animalModule ??= Client.Require<AnimalModule>();
    private ArrowModule? _arrowModule;
    private ArrowModule Arrows => _arrowModule ??= Client.Require<ArrowModule>();
    private FireModule? _fireModule;
    private FireModule Fires => _fireModule ??= Client.Require<FireModule>();

    /// <summary>The families as this client aims through them, in their numbers' order (every family has one: checked).</summary>
    public IReadOnlyList<ITargetView> TargetViews => _targetViews;

    public AimedVitality? AimedHealth(Vector3 eye, Vector3 direction) =>
        TargetHealthQuery.Find(_targetViews, eye, direction, new Viewer(EntityId, null, null));

    /// <summary>A non-authoritative contact pose prediction using the world's shared target shapes. It emits no effects or damage.</summary>
    public AimHit PredictMelee(Vector3 origin, Vector3 direction, float reach)
    {
        var ray = new AimRay(origin, Vector3.Normalize(direction), reach, Client.EntityId.Value) { AttackRadius = CombatSystem.SweepRadius };
        AimHit best = AimHit.None;
        foreach (ITargetView view in _targetViews)
        {
            if (view.Family is TargetFamily.Explosive or TargetFamily.Arrow or TargetFamily.Fire) continue;
            view.Aim(ray, ref best);
        }
        if (best.Hit && best.Target.Family == TargetFamily.Natural && Survival.Nature?.TryGet(best.Target.Id, out NaturalObject natural) == true &&
            Content.Natural(natural.Kind) is { Gather: { } gather } kind && Survival.Natural != null)
        {
            Vector3 point = gather.Spot.Surface.Meet(natural, kind, Survival.Natural.Stage(natural, kind), origin, direction, best.Point);
            best = best with { Point = point };
        }
        return best;
    }

    protected internal override void Welcome()
    {
        var registry = Registry<ITargetView>.ForEnum<TargetFamily>("client target families", complete: true, TargetFamily.None);
        foreach (ITargetView view in new ITargetView[]
                 {
                     new GroundView(this), new CharacterView(this), new AnimalView(this), new NaturalView(this), new ThingView(this),
                     new PieceView(this), new DoorView(this), new CupboardView(this), new ExplosiveView(this), new ShipView(this),
                     new ArrowView(this), new FireView(this),
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

    // A body drawn now, as the server builds its hit shapes (Zone's CharacterTargets): standing, crouching, or lying; aboard
    // a ship, upright on its deck as drawn (IRON_WAKE.md §4.8).
    private HitPose PoseOf(RemoteEntity r) => new()
    {
        Feet = r.Position,
        Up = r.Up,
        Yaw = r.Yaw,
        Pose = r.Stance == Stance.Seated ? Simulation.Combat.Pose.Seated : r.Sleeping || r.Wounded ? Simulation.Combat.Pose.Lying : r.Stance == Stance.Crouching ? Simulation.Combat.Pose.Crouching : Simulation.Combat.Pose.Standing,
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
        foreach (RemoteEntity r in Remotes.Values)
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
        foreach (ClientAnimal a in Animals.All)
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
        foreach (RemoteShip s in Ships.Known)
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

    private sealed class GroundView(TargetModule c) : ITargetView
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
            if (c.Surface != null && c.Zone != null)
            {
                ray.Consider(ref best, new GroundProbe(c.Surface, c.Frame).Crossing(ray.Origin, ray.Direction, ray.Upto(best)), TargetRef.Ground,
                    AimKind.Backdrop);
            }
        }

        public bool TryFacts(in TargetRef target, in Viewer viewer, out TargetFacts facts)
        {
            facts = new TargetFacts { Name = "" };
            return target.Id == 0;
        }
    }

    private sealed class CharacterView(TargetModule c) : ITargetView
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
            foreach (RemoteEntity r in c.Remotes.Values)
            {
                if (r.Kind != Net.Protocol.EntityKind.Character || !r.Spawned || !r.HasState || (r.Flags & MoveFlags.Dead) != 0 || r.Id.Value == ray.Ignore ||
                    Vector3.DistanceSquared(r.Position, ray.Origin) > (length + 2.5f) * (length + 2.5f))
                {
                    continue;
                }
                ray.Consider(ref best, Hitboxes.Raycast(c.PoseOf(r), ray.Origin, ray.Direction, length, ray.AttackRadius ?? AimShapes.BodyMargin, out _),
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
                Flags = c.World.IsTeammate(r.Id) ? ViewerFlags.Teammate : ViewerFlags.None, Name = r.Sleeping ? $"{r.Name} (asleep)" : r.Name, Body = true,
                Position = r.Position,
            };
            return true;
        }
    }

    private sealed class AnimalView(TargetModule c) : ITargetView
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
            foreach (ClientAnimal a in c.Animals.All)
            {
                if (a.Species is { } species && Vector3.DistanceSquared(a.Position, ray.Origin) <= (length + 4) * (length + 4))
                {
                    ray.Consider(ref best, AnimalHitboxes.Raycast(species, c.PoseOf(a), ray.Origin, ray.Direction, length, ray.AttackRadius ?? AimShapes.BodyMargin, out _),
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
                Vitality = !a.Dead && a.Species is { } species ? new TargetVitality(a.Health * species.Health, species.Health) : null,
            };
            return true;
        }
    }

    private sealed class NaturalView(TargetModule c) : ITargetView
    {
        public TargetFamily Family => TargetFamily.Natural;

        private bool Find(ulong id, out NaturalObject o, out NaturalKind? kind)
        {
            kind = null;
            if (c.Survival.Nature == null || c.Survival.Natural == null || !c.Survival.Nature.TryGet(id, out o) || c.Survival.Natural.IsDepleted(id))
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
            if (c.Survival.Nature != null && c.Survival.Natural != null)
            {
                NaturalAim.Aim(c.Survival.Nature, c.Survival.Natural, c.Content, ray, ref best, c._aimNature);
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
                Vitality = !kind.IsPicked && c.Survival.Natural != null ? new TargetVitality(c.Survival.Natural.Health(o, kind), NaturalShapes.MaxHealth(o, kind)) : null,
            };
            return true;
        }
    }

    private sealed class ThingView(TargetModule c) : ITargetView
    {
        public TargetFamily Family => TargetFamily.Thing;

        // A thing's box (a deployable or prop) or ball (a bag or body), and its up.
        private (DeployableDef? Def, Vector3 Up) Shape(RemoteThing thing) => (thing.Deployable(c.Content), c.Frame.UpAt(thing.Position));

        // How the prompt names it: its own name (a bag, a prop; a body by whose it is), else its item's.
        private static string NameOf(RemoteThing thing, DeployableDef? def) =>
            thing.Kind == StaticKind.Corpse && thing.CorpseOwner != 0
                ? thing.IsBody ? $"{thing.Name}'s body" : $"{thing.Name}'s backpack"
                : thing.Name.Length > 0 ? thing.Name : def?.Item.Name ?? "";

        public bool TryPose(in TargetRef target, out TargetPose pose)
        {
            pose = default;
            if (c.Survival.ThingOf(new EntityId(target.Id)) is not { } thing)
            {
                return false;
            }
            pose = new TargetPose(thing.Position, Geometry.Upright(c.Frame.UpAt(thing.Position), thing.Yaw));
            return true;
        }

        public bool TryNearest(in TargetRef target, Vector3 from, out Vector3 point)
        {
            point = from;
            if (c.Survival.ThingOf(new EntityId(target.Id)) is not { } thing)
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
            foreach (RemoteThing thing in c.Survival.Things)
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
            if (c.Survival.ThingOf(new EntityId(target.Id)) is not { } thing)
            {
                return false;
            }
            DeployableDef? def = thing.Deployable(c.Content);
            // Aboard a ship (IRON_WAKE.md §8): the ship's list says who may take it up, as the server's facts have it.
            RemoteShip? ship = thing.Aboard ? c.Ships.ShipOf(new EntityId(thing.Support.Id)) : null;
            ViewerFlags crew = ship != null && (ship.Viewer & ViewerFlags.Authorised) != 0 ? ViewerFlags.Authorised : ViewerFlags.None;
            facts = new TargetFacts
            {
                Capabilities = thing.Capabilities, Flags = thing.Flags | crew, Name = NameOf(thing, def), Count = thing.Count,
                Body = thing.Kind == StaticKind.Corpse, Deployable = def, Position = thing.Position, Aboard = thing.Aboard,
                Vitality = def != null ? new TargetVitality(thing.Health * def.Health, def.Health) :
                    thing.IsBody ? new TargetVitality(thing.Health * c.Content.Survival.Corpse.HarvestHealth, c.Content.Survival.Corpse.HarvestHealth) : null,
            };
            return true;
        }
    }

    private sealed class PieceView(TargetModule c) : ITargetView
    {
        public TargetFamily Family => TargetFamily.Piece;

        private Piece? Find(ulong id) => c.Building.World?.PieceOf(new EntityId(id)) is { Gone: false } piece ? piece : null;

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
            if (Find(target.Id) is not { } piece || c.Building.World is not { } building)
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
            if (c.Building.World is { } building)
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
            facts = new TargetFacts { Name = piece.Def.Name, Position = piece.ZoneTransform.Position, Vitality = new TargetVitality(piece.Health, piece.MaxHealth) };
            return true;
        }
    }

    private sealed class DoorView(TargetModule c) : ITargetView
    {
        public TargetFamily Family => TargetFamily.Door;

        private Door? Find(ulong id) => c.Building.World?.DoorOf(new EntityId(id)) is { Gone: false } door ? door : null;

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
            if (Find(target.Id) is not { } door || c.Building.World is not { } building)
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
            if (c.Building.World is { } building)
            {
                BuildingAim.Openings(building, ray, ref best, c._aimPieces);
            }
        }

        public bool TryFacts(in TargetRef target, in Viewer viewer, out TargetFacts facts)
        {
            facts = default;
            if (Find(target.Id) is not { } door || c.Building.Replica is not { } replica)
            {
                return false;
            }
            DoorState state = replica.StateOf(door);
            CapabilityData data = c.Content.Capabilities;
            facts = new TargetFacts
            {
                Capabilities = data.Door.With(data.Lock(state.Lock)), Flags = state.Flags, Name = door.Def.Name, Open = door.Open, Lock = state.Lock,
                Position = door.Frame.ZoneTransform.Position,
                Vitality = new TargetVitality(door.Health, door.Def.Health),
            };
            return true;
        }
    }

    private sealed class CupboardView(TargetModule c) : ITargetView
    {
        public TargetFamily Family => TargetFamily.Cupboard;

        private ClaimDevice? Find(ulong id) => c.Building.World?.ClaimOf(new EntityId(id)) is { Gone: false } claim ? claim : null;

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
            point = Geometry.NearestOnBox(position, up, Facing.YawOf(-along, up), c.Content.ClaimDeployable.HalfExtents, from);
            return true;
        }

        // Its body is a building body: the pieces' view finds it.
        public void Aim(in AimRay ray, ref AimHit best)
        {
        }

        public bool TryFacts(in TargetRef target, in Viewer viewer, out TargetFacts facts)
        {
            facts = default;
            if (Find(target.Id) is not { } claim || c.Building.Replica is not { } replica)
            {
                return false;
            }
            ViewerFlags box = c.Survival.ThingOf(claim.Id)?.Flags ?? ViewerFlags.None;
            facts = new TargetFacts
            {
                Capabilities = c.Content.Capabilities.Cupboard, Flags = replica.StateOf(claim).Flags | box, Name = c.Content.ClaimDeployable.Item.Name,
                Position = claim.ZoneTransform.Position, Deployable = c.Content.ClaimDeployable,
                Vitality = new TargetVitality(claim.Health, c.Content.Building.ClaimHealth),
            };
            return true;
        }
    }

    private sealed class ExplosiveView(TargetModule c) : ITargetView
    {
        public TargetFamily Family => TargetFamily.Explosive;

        public bool TryPose(in TargetRef target, out TargetPose pose)
        {
            pose = default;
            if (c.Raid.ExplosiveOf(new EntityId(target.Id)) is not { } e)
            {
                return false;
            }
            pose = new TargetPose(e.PositionAt(c.ServerTickNow), Geometry.Upright(e.Normal, 0));
            return true;
        }

        public bool TryNearest(in TargetRef target, Vector3 from, out Vector3 point)
        {
            point = from;
            if (c.Raid.ExplosiveOf(new EntityId(target.Id)) is not { } e)
            {
                return false;
            }
            point = AimShapes.NearestOnSphere(e.PositionAt(c.ServerTickNow), (float)(e.Stats?.Radius ?? AimShapes.ExplosiveRadius), from);
            return true;
        }

        public void Aim(in AimRay ray, ref AimHit best)
        {
            float length = ray.Upto(best);
            foreach (ClientExplosive e in c.Raid.Explosives)
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
            if (c.Raid.ExplosiveOf(new EntityId(target.Id)) is not { } e)
            {
                return false;
            }
            CapabilityData data = c.Content.Capabilities;
            facts = new TargetFacts { Capabilities = e.Dud ? data.Dud : data.Explosive, Name = e.Item.Name, Position = e.PositionAt(c.ServerTickNow) };
            return true;
        }
    }

    private sealed class ShipView(TargetModule c) : ITargetView
    {
        public TargetFamily Family => TargetFamily.Ship;

        // A ship as drawn (its pose and its parts' states here: the frame's space as posed, else its details now).
        private (ShipPose Pose, float[] Parts)? Drawn(RemoteShip ship, ShipDef def)
        {
            if (c.Ships.DrawnPose(ship.NetId) is not { } pose)
            {
                return null;
            }
            if (def.Interior is not { } interior)
            {
                return (pose, Array.Empty<float>());
            }
            if (c.Ships.FrameOf(ship.NetId) is { } frame)
            {
                return (pose, frame.States);
            }
            if (_parts.Length != interior.Parts.Count)
            {
                _parts = new float[interior.Parts.Count];
            }
            ship.PartsInto(interior, _parts, (long)c.ServerTickNow);
            return (pose, _parts);
        }

        private float[] _parts = Array.Empty<float>();
        private readonly List<(int Part, Vector3 Point, float Distance)> _near = new();

        // The ship by its pivot, or a part at its pose (moved by what carries it: the ramp's leaf, a turret), as the server's.
        public bool TryPose(in TargetRef target, out TargetPose pose)
        {
            pose = default;
            if (c.ShipById(target.Id) is not { Def: { } def } ship || Drawn(ship, def) is not { } drawn)
            {
                return false;
            }
            pose = target.Part == ShipParts.Hull ? new TargetPose(ship.Origin, ship.Rotation) : ShipAim.PartPose(def, target.Part, drawn.Pose, drawn.Parts);
            return true;
        }

        // The hull's nearest point, or a part's (its ball's surface, its pieces'), as the server's.
        public bool TryNearest(in TargetRef target, Vector3 from, out Vector3 point)
        {
            point = from;
            if (c.ShipById(target.Id) is not { Def: { } def } ship || Drawn(ship, def) is not { } drawn)
            {
                return false;
            }
            if (!ShipAim.Nearest(def, c.Ships.FrameOf(ship.NetId), drawn.Pose, drawn.Parts, target.Part, from, _near, out point))
            {
                point = ShipShapes.NearestOnHull(def, ship.Position, ship.Rotation, from);
            }
            return true;
        }

        // A ship as drawn: a ship people walk in hides what is behind its pieces (IRON_WAKE.md §4.9: the view goes on through
        // its open ramp, not its hull; a part's pieces are that part), as the server's family does; then every ship's parts'
        // balls, a part reached through a door only while that door lets one in.
        public void Aim(in AimRay ray, ref AimHit best)
        {
            if (c.Client.Prediction?.Frames.Ships is { Count: > 0 } frames &&
                ShipQueries.Ray(frames, _drawn ??= f => c.Ships.DrawnPose((uint)f.Key), ray.Origin, ray.Direction, ray.Upto(best), out ShipQueryHit hit) &&
                hit.Ship is ClientShipFrame { Ship: { Spawned: true, Def: { } walkedDef } walked } && !walked.Has(ShipNetFlags.Destroyed) && !walked.Has(ShipNetFlags.InOrbit))
            {
                int part = walkedDef.PartOfPieces(hit.Hit.Part);
                ray.Consider(ref best, hit.Distance, new TargetRef(TargetFamily.Ship, walked.Id.Value, (byte)part), part == 0 ? AimKind.Backdrop : AimKind.Thing);
            }
            double now = c.ServerTickNow;
            foreach (RemoteShip ship in c.Ships.Known)
            {
                if (ship is { Spawned: true, HasState: true, Def: { } def } && !ship.Has(ShipNetFlags.Destroyed) && !ship.Has(ShipNetFlags.InOrbit) &&
                    Drawn(ship, def) is { } drawn)
                {
                    uint ready = 0;
                    for (int d = 0; d < ship.Doors.Length && d < 32; d++)
                    {
                        if (ship.Doors[d].LetsIn(ship.DoorAt(d, now)))
                        {
                            ready |= 1u << d;
                        }
                    }
                    ShipAim.Balls(ship.Id.Value, def, drawn.Pose, drawn.Parts, ready, ray, ref best);
                }
            }
        }

        private Func<IShipFrame, ShipPose?>? _drawn;

        // What the rules know of a part (IRON_WAKE.md §5.1), as the server's family has it: from the ship's state (at rest and
        // slow as the server judges them, ShipNetFlags.Still and Slow) and its details (doors, seats, health).
        public bool TryFacts(in TargetRef target, in Viewer viewer, out TargetFacts facts)
        {
            facts = default;
            if (c.ShipById(target.Id) is not { Def: { } def } ship || ship.Has(ShipNetFlags.Destroyed) || target.Part >= def.Parts.Count)
            {
                return false;
            }
            CapabilityData data = c.Content.Capabilities;
            ShipTargetPart part = def.Parts[target.Part];
            bool still = ship.Has(ShipNetFlags.Still), slow = ship.Has(ShipNetFlags.Slow);
            double now = c.ServerTickNow;
            bool LetsIn(int d) => d >= 0 && d < ship.Doors.Length && ship.Doors[d].LetsIn(ship.DoorAt(d, now));
            CapabilitySet capabilities = data.Ship.With(part.Capabilities);
            if (part.Through >= 0 && (!LetsIn(part.Through) || !still))
            {
                capabilities = data.Ship;
            }
            ShipDoorDef? door = part.Door >= 0 && part.Door < def.Doors.Count ? def.Doors[part.Door] : null;
            bool hasHealth = part.Health != null || door?.Health != null;
            int seatPart = part.Seat >= 0 ? def.PartOfSeat(part.Seat) : 0;
            facts = new TargetFacts
            {
                Capabilities = capabilities, Flags = ship.Viewer, Name = target.Part == 0 ? def.Name : part.Name,
                Open = door != null && part.Door < ship.Doors.Length && ship.Doors[part.Door].Opening,
                Busy = part.Seat >= 0 && part.Seat < ship.Occupants.Length && !ship.Occupants[part.Seat].IsNone, Still = still,
                Ship = def, ShipPart = target.Part, DoorRule = door != null ? door.Rule(still, slow).Code : OutcomeCode.Done,
                DoorReady = part.Seat >= 0 && LetsIn(def.Seats[part.Seat].Door),
                Jammed = door?.Health != null && part.Door < ship.Doors.Length && ship.Doors[part.Door].Health <= 0,
                Broken = seatPart > 0 && def.Parts[seatPart].Health != null && seatPart < ship.PartHealth.Length && ship.PartHealth[seatPart] <= 0,
                Health = hasHealth && target.Part < ship.PartHealth.Length ? ship.PartHealth[target.Part] : -1,
                Vitality = target.Part == 0 ? new TargetVitality(ship.Health * def.Health, def.Health) :
                    hasHealth && target.Part < ship.PartHealth.Length && (door?.Health ?? part.Health) is { } hp
                        ? new TargetVitality(ship.PartHealth[target.Part] * hp.Max, hp.Max) : null,
                Position = ship.Position,
            };
            return true;
        }
    }

    private sealed class ArrowView(TargetModule c) : ITargetView
    {
        public TargetFamily Family => TargetFamily.Arrow;

        public bool TryPose(in TargetRef target, out TargetPose pose)
        {
            pose = default;
            if (c.Arrows.ArrowOf(new EntityId(target.Id)) is not { Placed: true } arrow)
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
            foreach (RemoteArrow arrow in c.Arrows.All)
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
            if (c.Arrows.ArrowOf(new EntityId(target.Id)) is not { Placed: true } arrow)
            {
                return false;
            }
            facts = new TargetFacts { Capabilities = c.Content.Capabilities.Arrow, Name = arrow.Item.Name, Count = 1, Position = arrow.Pose.Position };
            return true;
        }
    }

    // Fires (IW4) where they burn now (riding a ship's deck): the pool's nearest point; flames hide nothing from the aim and
    // offer nothing, as the server's family says.
    private sealed class FireView(TargetModule c) : ITargetView
    {
        public TargetFamily Family => TargetFamily.Fire;

        public bool TryPose(in TargetRef target, out TargetPose pose)
        {
            pose = default;
            if (c.Fires.FireOf(new EntityId(target.Id)) is not { } fire)
            {
                return false;
            }
            pose = fire.Pose;
            return true;
        }

        public bool TryNearest(in TargetRef target, Vector3 from, out Vector3 point)
        {
            point = from;
            if (c.Fires.FireOf(new EntityId(target.Id)) is not { } fire)
            {
                return false;
            }
            point = Simulation.Raid.Fire.NearestOnPool(fire.Pose, fire.Radius, from);
            return true;
        }

        public void Aim(in AimRay ray, ref AimHit best)
        {
        }

        public bool TryFacts(in TargetRef target, in Viewer viewer, out TargetFacts facts)
        {
            facts = default;
            if (c.Fires.FireOf(new EntityId(target.Id)) is not { } fire)
            {
                return false;
            }
            facts = new TargetFacts { Capabilities = c.Content.Capabilities.Fire, Name = "Fire", Position = fire.Position };
            return true;
        }
    }
}

/// <summary>The accessor on the client (GROUND_SYSTEMS.md §12.5, K11).</summary>
public static class TargetModuleAccess
{
    public static TargetModule Targets(this GameClient client) => client.Require<TargetModule>();
}

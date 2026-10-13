using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Building;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.World;
using Hollow.Simulation.Targets;

namespace Hollow.Simulation;

/// <summary>What a hammer does to a piece.</summary>
public enum HammerAction : byte
{
    Upgrade,
    Rotate,
    Demolish,
    Repair,
}

public enum DoorAction : byte
{
    /// <summary>Fit the door item in a slot into a doorway or wall frame.</summary>
    Place,
    Toggle,

    /// <summary>Take an open, unlocked door back as an item.</summary>
    PickUp,
}

public enum LockAction : byte
{
    /// <summary>Fit the key or code lock in a slot to a door.</summary>
    Attach,
    SetCode,
    EnterCode,
    Lock,
    Unlock,

    /// <summary>Cut a key for a key lock (its owner, for wood).</summary>
    MakeKey,

    /// <summary>Take the lock off as an item.</summary>
    Remove,

    /// <summary>G7: put the player's team on the code lock's list (Rust's "authorise team").</summary>
    AuthoriseTeam,
}

public enum ClaimAction : byte
{
    /// <summary>Place the claim device in a slot on a foundation or floor.</summary>
    Place,
    Authorise,
    Deauthorise,
    ClearList,

    /// <summary>Take the device back as an item, its store with it.</summary>
    PickUp,

    /// <summary>G7: put the player and their whole team on the device's list (Rust's "authorise team").</summary>
    AuthoriseTeam,
}

// Building in a live zone (GROUND.md §6.3, phase 2): the zone's BuildingWorld on its Jolt world, run on the zone's
// tick (collapses) and on a slow schedule (upkeep and decay, closed form); the player's building actions, each checked
// against what they carry and answered with the rules' reason; the claim device as a deployable whose storage is its
// upkeep store; and hits on pieces, doors and devices.
public sealed partial class Zone
{
    private readonly Dictionary<ClaimDevice, Deployable> _claimDeployables = new();
    private readonly List<int> _keys = new();
    private double _buildingAdvancedAt = double.NaN;

    /// <summary>The zone's building (null in a G1 zone, without game data).</summary>
    public BuildingWorld? Building { get; private set; }

    /// <summary>
    /// The building rules' clock in seconds: the real clock (Unix time), so edit windows, repair delays, code waits,
    /// and upkeep and decay run on while the server is down.
    /// </summary>
    public double BuildingNow => UnixMilliseconds / 1000.0;

    /// <summary>When upkeep and decay were last applied (Unix seconds; NaN before the first tick). Saved; on load the server sets it and catches up.</summary>
    public double BuildingAdvancedAt
    {
        get => _buildingAdvancedAt;
        set => _buildingAdvancedAt = value;
    }

    /// <summary>The deployable a claim device is kept as (its storage is the upkeep store), or null.</summary>
    public Deployable? DeployableOf(ClaimDevice device) => _claimDeployables.GetValueOrDefault(device);

    /// <summary>The claim device a deployable stands for, or null.</summary>
    public ClaimDevice? ClaimOf(Deployable deployable) => Building?.ClaimOf(deployable.Id) is { } c && DeployableOf(c) == deployable ? c : null;

    private void InitBuilding(GameContent content)
    {
        Building = new BuildingWorld(content.Building, Physics, NextId) { Characters = CharacterCapsules };
        Building.Changed += OnBuildingChanged;
    }

    // Each tick: collapses due (nothing at all while none is under way). On the slow schedule: upkeep and decay.
    private void StepBuilding()
    {
        BuildingWorld building = Building!;
        double now = BuildingNow;
        building.Tick(now);
        if (double.IsNaN(_buildingAdvancedAt))
        {
            _buildingAdvancedAt = now;
        }
        else if (now - _buildingAdvancedAt >= building.Data.AdvanceSeconds || now < _buildingAdvancedAt)
        {
            AdvanceBuilding();
        }
    }

    /// <summary>Applies upkeep and decay from <see cref="BuildingAdvancedAt"/> to now in one closed-form step (and on load, the time the server was down).</summary>
    public void AdvanceBuilding()
    {
        if (Building == null)
        {
            return;
        }
        double now = BuildingNow;
        if (!double.IsNaN(_buildingAdvancedAt) && now > _buildingAdvancedAt)
        {
            Building.AdvanceTime(now - _buildingAdvancedAt, now);
        }
        _buildingAdvancedAt = now;
    }

    // Who placement and doors must not trap: every living character's standing capsule.
    private IEnumerable<Capsule> CharacterCapsules()
    {
        ComponentPool<Character> characters = Entities.Pool<Character>();
        for (int i = 0; i < characters.Count; i++)
        {
            CharacterState s = characters.Values[i].State;
            if (!s.IsDead)
            {
                yield return new Capsule(s.Position, Frame.UpAt(s.Position), Tuning.Radius, Tuning.StandingHeight);
            }
        }
    }

    // A claim device is also a deployable: replicated and opened like a box, its storage the upkeep store.
    private void OnBuildingChanged(BuildingEvent e)
    {
        switch (e.Change)
        {
            case BuildingChange.ClaimAdded when e.Claim is { } claim && !_claimDeployables.ContainsKey(claim):
            {
                (Vector3 position, Quaternion rotation) = claim.ZoneTransform;
                Vector3 up = Frame.UpAt(position);
                Vector3 along = Vector3.Transform(Vector3.UnitZ, rotation);
                var d = new Deployable(claim.Id, Content!.ClaimDeployable, position, up, MathF.Atan2(along.X, along.Z), new EntityId(claim.Owner))
                {
                    Health = (float)claim.Health,
                };
                _claimDeployables.Add(claim, d);
                AddStatic(d);
                claim.Store = new ItemWallet(Content, new[] { d.Storage! });
                break;
            }
            case BuildingChange.ClaimChanged when e.Claim is { } claim && _claimDeployables.TryGetValue(claim, out Deployable? d):
                if (MathF.Abs(d.Health - (float)claim.Health) > 1e-4f)
                {
                    d.Health = (float)claim.Health;
                    d.Revision++;
                }
                break;
            // Pieces, doors and claim devices broken, brought down, decayed or demolished: destroyed (GROUND.md §6.4.2), with
            // the blow that broke them (or what went with it, as a door in its frame) or the building's own reason.
            case BuildingChange.PieceRemoved when e.Piece is { } piece && e.Removal != Removal.Taken:
            {
                (Vector3 position, Quaternion rotation) = piece.ZoneTransform;
                Destroyed(piece.Ref, piece.Def.Key, piece.Grade, position, rotation, CauseOf(e.Removal));
                break;
            }
            case BuildingChange.DoorRemoved when e.Door is { } door && e.Removal != Removal.Taken:
            {
                (Vector3 position, Quaternion rotation) = door.Frame.ZoneTransform;
                Destroyed(door.Ref, door.Def.Key, 0, position, rotation, CauseOf(e.Removal), door.Open);
                break;
            }
            case BuildingChange.ClaimRemoved when e.Claim is { } claim && _claimDeployables.Remove(claim, out Deployable? d):
                if (e.Removal != Removal.Taken)
                {
                    (Vector3 position, Quaternion rotation) = claim.ZoneTransform;
                    Destroyed(claim.Ref, Content!.ClaimDeployable.Key, 0, position, rotation, CauseOf(e.Removal));
                }
                // Its store spills where it stood, in one bag.
                RemoveStatic(d);
                if (d.Storage != null)
                {
                    Spill(d.Position + d.Up * 0.3f, d.Storage, d.Yaw);
                }
                break;
        }
    }

    // ---------------------------------------------------------------- hits

    /// <summary>The nearest building body along a ray within <paramref name="length"/>: a piece, a door or a claim device, and how far.</summary>
    public bool TraceBuilding(Vector3 origin, Vector3 direction, float length, out float distance, out object? part)
    {
        distance = -1;
        part = null;
        if (Building?.Physics is not { } physics || !physics.Ray(origin, direction, length, out RayHit hit, buildingOnly: true))
        {
            return false;
        }
        part = physics.OwnerOf(hit.Body) switch
        {
            DoorLeaf leaf => leaf.Door,
            { } owner => owner,
            null => null,
        };
        distance = hit.Distance;
        return part != null;
    }

    /// <summary>
    /// Damage to a piece, door or claim device through the building rules (its grade's protection, a wall's soft side):
    /// melee as melee, arrows and bullets as bullets. Returns the health lost. The building receiver's resolver for
    /// blows; called on its own (tests), what it breaks is reported destroyed by a blow of that kind.
    /// </summary>
    public float DamageBuilding(object part, Vector3 direction, float amount, bool projectile)
    {
        BuildingWorld building = Building!;
        var hit = projectile ? new DamageHit(direction, Bullet: amount) : new DamageHit(direction, Melee: amount);
        Blow outer = _blow;
        if (!outer.Active)
        {
            _blow = new Blow { Active = true, Cause = projectile ? DestroyCause.Bullet : DestroyCause.Melee, Direction = direction };
        }
        try
        {
            return part is IBuildingPart p ? (float)building.Damage(p, hit, BuildingNow).Loss : 0;
        }
        finally
        {
            _blow = outer;
        }
    }

    // The building's own reasons for taking a piece away (destroyed: the blow's).
    private static DestroyCause? CauseOf(Removal removal) => removal switch
    {
        Removal.Collapsed => DestroyCause.Collapse,
        Removal.Decayed => DestroyCause.Decay,
        Removal.Demolished => DestroyCause.Demolish,
        _ => null,
    };

    /// <summary>
    /// The building's receiver (GROUND.md §6.4.2): pieces, doors and claim devices, found by their bodies along a ray and
    /// by their nearest points in a blast's reach (a claim device by its deployable's box). Its defences are the building
    /// rules' (a grade's or door's protection by channel, a wall's soft side, whole points); a blast brings the explosive's
    /// own table by grade and door, whole on the piece it hit and a splash share on the rest, its fractions of a point
    /// carried to the next hit. Its consequences are the building rules' (breaking, what it held collapsing: reported
    /// destroyed by <see cref="OnBuildingChanged"/>) and raid hits: any explosive on a claimed base, or a blow, arrow or
    /// bullet that took something off one whose claim device doesn't list the attacker.
    /// </summary>
    private sealed class BuildingReceiver(Zone zone) : IDamageReceiver
    {
        public TargetFamily[] Families { get; } = { TargetFamily.Piece, TargetFamily.Door, TargetFamily.Cupboard };

        public bool Keeps => false;

        public void Raycast(in RayQuery ray, ref TraceHit best)
        {
            // Walls stop swings, arrows and bullets: only up to the nearest thing found so far.
            if (zone.Building != null && zone.TraceBuilding(ray.Origin, ray.Direction, ray.Upto(best), out float wall, out object? part) &&
                part is ITargetable thing)
            {
                ray.Consider(ref best, wall, thing.Ref);
            }
        }

        public void InReach(in BlastQuery blast, List<BlastTarget> into)
        {
            if (zone.Building is not { } building)
            {
                return;
            }
            building.PiecesNear(blast.At, blast.At, blast.Radius + 3.6f, zone._blastPieces);
            foreach (Piece piece in zone._blastPieces)
            {
                if (piece.Gone || piece.Collapsing)
                {
                    continue;
                }
                Vector3 point = ClosestPoint(building.Data, piece, blast.At);
                float d = Vector3.Distance(point, blast.At);
                bool stuck = blast.IsStuckTo(piece.Ref);
                if (d <= blast.Radius || stuck)
                {
                    into.Add(Target(piece, point, CentreOf(piece), stuck ? 0 : d, stuck, table: true));
                }
                if (piece.Door is { Gone: false } door)
                {
                    (Vector3 doorPoint, Vector3 _) = ClosestPoint(door, blast.At);
                    float dd = Vector3.Distance(doorPoint, blast.At);
                    bool onDoor = blast.IsStuckTo(door.Ref);
                    if (dd <= blast.Radius || onDoor)
                    {
                        into.Add(Target(door, doorPoint, CentreOf(door), onDoor ? 0 : dd, onDoor, table: true));
                    }
                }
            }
            // Claim devices by their deployable boxes: their bodies are what hides them, their health the building rules'.
            zone._blastThings.Clear();
            zone.DeployablesNear(blast.At, blast.Radius + 2f, zone._blastThings);
            foreach (Deployable box in zone._blastThings)
            {
                if (box.Health <= 0 || zone.ClaimOf(box) is not { } claim)
                {
                    continue;
                }
                Vector3 point = ClosestPoint(box, blast.At);
                float distance = Vector3.Distance(point, blast.At);
                bool stuck = blast.IsStuckTo(claim.Ref) || blast.IsStuckTo(box.Ref);
                if (distance <= blast.Radius || stuck)
                {
                    into.Add(Target(claim, point, box.Centre, stuck ? 0 : distance, stuck, table: false));
                }
            }
        }

        private static BlastTarget Target(IBuildingPart part, Vector3 point, Vector3 centre, float distance, bool stuck, bool table) => new()
        {
            Hit = new TraceHit { Target = part.Ref, Point = point },
            Thing = part,
            Sight = point,
            Sight2 = centre,
            Self = part,
            Distance = distance,
            Stuck = stuck,
            Table = table,
        };

        public DamageOutcome Receive(in DamageRequest r)
        {
            if (zone.Building is not { } building || zone.Resolve(r.Hit.Target) is not IBuildingPart part || part.Gone || part is Piece { Collapsing: true })
            {
                return default;
            }
            ClaimDevice? claim = part.Base;
            bool explosive = r.Channel == DamageType.Explosive;
            double amount = r.Amounts.Building;
            if (r.Blast is { } table && part is Piece or Door)
            {
                // The explosive's own table for the grade (and side) or the door: whole on the piece it hit, a splash share on the rest.
                double share = r.Falloff * (r.Direct ? 1 : table.Splash);
                if (share <= 0)
                {
                    return default;
                }
                amount = part is Piece p
                    ? table.Piece(building.Data.Grades[p.Grade].Key, p.Def.Family == PieceFamily.Wall && BuildingWorld.SoftSide(p, r.Direction)) * share
                    : table.Door(((Door)part).Def.Protection.Key) * share;
            }
            else if (explosive && amount <= 0)
            {
                return default;
            }
            (double lost, bool destroyed) = explosive
                ? zone.DamageStructure(part, amount, r.Direction, zone.BuildingNow)
                : (zone.DamageBuilding(part, r.Direction, (float)amount, projectile: r.Channel != DamageType.Melee), part.Gone);
            zone.RaidStats.StructureDamage += (long)Math.Round(lost);
            if (destroyed)
            {
                if (part is Piece)
                {
                    zone.RaidStats.PiecesDestroyed++;
                }
                else if (part is Door)
                {
                    zone.RaidStats.DoorsDestroyed++;
                }
            }
            if (claim != null && (explosive || (lost > 0 && (r.By == null || !claim.IsAuthorised(r.By.Id.Value)))))
            {
                // A raid hit: a blast's are summed per base over the blast, a blow's alerts at once.
                RaidTallies raids = r.Raids ?? new RaidTallies();
                raids.Add(claim, zone.RaidName(part), destroyed, r.Direct);
                if (r.Raids == null)
                {
                    raids.Report(zone, r.By?.Id ?? EntityId.None, r.By?.Name ?? "", r.Weapon);
                }
            }
            return new DamageOutcome(true, (float)lost, (float)lost, destroyed, false, part.Id);
        }
    }

    // A demolition by a player with the hammer: what comes down is reported as theirs.
    private Outcome Demolish(Player p, Piece piece, Vector3 eye)
    {
        Blow outer = _blow;
        _blow = new Blow { Active = true, Cause = DestroyCause.Demolish, Attacker = p.Id };
        try
        {
            return Building!.Demolish(p.Id.Value, piece, eye, BuildingNow);
        }
        finally
        {
            _blow = outer;
        }
    }

    // ---------------------------------------------------------------- the player's building actions

    private Outcome Builder(Entity entity, out Player p, out Vector3 eye)
    {
        eye = default;
        p = PlayerOf(entity)!;
        if (Building == null || p == null)
        {
            return OutcomeCode.Invalid;
        }
        if (!p.IsAwakeAndAlive)
        {
            return OutcomeCode.Dead;
        }
        eye = EyeOf(Entities.Get<Character>(entity).State);
        return Outcome.Done;
    }

    private bool Carries(Player p, BuildingRole role)
    {
        foreach (Container c in p.Inventory.Carried)
        {
            for (int i = 0; i < c.Size; i++)
            {
                if (!c[i].IsEmpty && Content!.Item(c[i].Item)?.Building == role)
                {
                    return true;
                }
            }
        }
        return false;
    }

    // The tool of a building role (the building plan, the hammer), for "You need a hammer".
    private int ToolItem(BuildingRole role)
    {
        foreach (ItemDef item in Content!.Items)
        {
            if (item.Building == role)
            {
                return item.Id;
            }
        }
        return 0;
    }

    // The numbers of the keys a player carries.
    private List<int> KeysOf(Player p)
    {
        _keys.Clear();
        foreach (Container c in p.Inventory.Carried)
        {
            for (int i = 0; i < c.Size; i++)
            {
                if (!c[i].IsEmpty && Content!.Item(c[i].Item)?.Building == BuildingRole.Key)
                {
                    _keys.Add(c[i].Condition);
                }
            }
        }
        return _keys;
    }

    // The item in a slot of the player's main or belt slots, when it is what the client saw and plays the role asked for.
    private Outcome Holding(Player p, ContainerRef from, int slot, ushort expected, BuildingRole role, out ItemDef? item)
    {
        item = null;
        if (from is not (ContainerRef.Main or ContainerRef.Belt) || !p.Inventory[from].InRange(slot))
        {
            return OutcomeCode.Invalid;
        }
        ItemStack s = p.Inventory[from][slot];
        if (s.IsEmpty || (expected != 0 && s.Item != expected))
        {
            return OutcomeCode.Stale;
        }
        item = Content!.Item(s.Item);
        if (item?.Building != role && !(role == BuildingRole.KeyLock && item?.Building == BuildingRole.CodeLock))
        {
            return OutcomeCode.Invalid;
        }
        return Outcome.Done;
    }

    private void TakeOne(Player p, ContainerRef from, int slot) => InventoryRules.Remove(p.Inventory[from], slot, 1, 0, out _);

    private void GiveBack(Entity entity, Player p, ItemDef item, int count = 1, ushort condition = 0)
    {
        GiveOrDrop(entity, p, condition != 0 ? new ItemStack(item.Id, count, condition) : ItemStack.Of(item, count));
        GameEvents.Add(new ZoneEvent { Kind = ZoneEventKind.ItemsGained, Player = p.Id, Item = item.Id, Count = count, Reason = GainReason.Refunded });
    }

    private bool WithinReach(Vector3 eye, Vector3 at) => Vector3.Distance(eye, at) <= Content!.Survival.InteractReach + 1.5f;

    private Vector3 DoorMiddle(Door door)
    {
        (Vector3 position, _) = door.Frame.ZoneTransform;
        return position + Frame.UpAt(position) * 1.2f;
    }

    /// <summary>
    /// Places a piece from the building plan (the player must carry one): on a support's socket in an orientation, or a
    /// foundation on open ground at a point and heading (in 15° steps). The rules check everything again; twig is paid
    /// from the player's main and belt slots.
    /// </summary>
    public Outcome PlacePiece(Entity entity, int pieceId, EntityId supportId, int socket, int orientation, Vector3 point, int headingSteps,
        out Piece? piece)
    {
        piece = null;
        Outcome ok = Builder(entity, out Player p, out Vector3 eye);
        if (!ok.Ok)
        {
            return ok;
        }
        BuildingWorld building = Building!;
        if (building.Data.Piece(pieceId) is not { } def)
        {
            return OutcomeCode.Invalid;
        }
        if (!Carries(p, BuildingRole.Plan))
        {
            return OutcomeCode.NoTool.With(ToolItem(BuildingRole.Plan));
        }
        PlacementPlan? plan;
        if (!supportId.IsNone)
        {
            if (building.PieceOf(supportId) is not { } support)
            {
                return OutcomeCode.NotFound;
            }
            if (SocketId.FromCode(socket) is not { } at || orientation is < 0 or > 255)
            {
                return OutcomeCode.Invalid;
            }
            plan = building.FromIntent(def, support, at, orientation);
            if (plan == null)
            {
                return OutcomeCode.NoSocket;
            }
        }
        else
        {
            if (def.Family != PieceFamily.Foundation || !float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z) ||
                headingSteps is < 0 or >= BuildingWorld.HeadingSteps)
            {
                return OutcomeCode.Invalid;
            }
            // The point is on the player's aim: where to look for the ground (across it, near the player) and how high
            // the top goes, which the ground under the foundation bounds (FoundationAt); the rules check the reach to
            // the foundation itself.
            Vector3 across = point - eye, up = Frame.UpAt(eye);
            if ((across - up * Vector3.Dot(across, up)).Length() > building.Data.Reach + 1)
            {
                return OutcomeCode.OutOfReach;
            }
            plan = building.FoundationAt(def, point, headingSteps * BuildingWorld.HeadingStep);
            if (plan == null)
            {
                return OutcomeCode.Floating;
            }
        }
        return building.Place(plan, p.Id.Value, eye, new ItemWallet(Content!, p.Inventory.Carried), BuildingNow, out piece);
    }

    /// <summary>Upgrades (to <paramref name="grade"/>), turns, demolishes or repairs a piece with the hammer (the player must carry one).</summary>
    public Outcome Hammer(Entity entity, HammerAction action, EntityId target, int grade)
    {
        Outcome ok = Builder(entity, out Player p, out Vector3 eye);
        if (!ok.Ok)
        {
            return ok;
        }
        if (!Carries(p, BuildingRole.Hammer))
        {
            return OutcomeCode.NoTool.With(ToolItem(BuildingRole.Hammer));
        }
        BuildingWorld building = Building!;
        if (building.PieceOf(target) is not { } piece)
        {
            return OutcomeCode.NotFound;
        }
        var wallet = new ItemWallet(Content!, p.Inventory.Carried);
        return action switch
        {
            HammerAction.Upgrade => building.Upgrade(p.Id.Value, piece, grade, wallet, eye),
            HammerAction.Rotate => building.Rotate(p.Id.Value, piece, eye, BuildingNow),
            HammerAction.Demolish => Demolish(p, piece, eye),
            HammerAction.Repair => building.Repair(p.Id.Value, piece, wallet, eye, BuildingNow),
            _ => OutcomeCode.Invalid,
        };
    }

    /// <summary>
    /// Doors: fits the door item in a slot into a frame piece (<paramref name="target"/>), turned by
    /// <paramref name="flip"/>; opens or shuts one (keys carried open key locks); or takes an open, unlocked one back.
    /// </summary>
    public Outcome DoorAct(Entity entity, DoorAction action, ContainerRef from, int slot, ushort expected, EntityId target, int flip)
    {
        Outcome ok = Builder(entity, out Player p, out Vector3 eye);
        if (!ok.Ok)
        {
            return ok;
        }
        BuildingWorld building = Building!;
        if (action == DoorAction.Place)
        {
            Outcome holding = Holding(p, from, slot, expected, BuildingRole.Door, out ItemDef? item);
            if (!holding.Ok)
            {
                return holding;
            }
            if (building.PieceOf(target) is not { } frame)
            {
                return OutcomeCode.NotFound;
            }
            Outcome placed = building.PlaceDoor(p.Id.Value, item!.Door!, frame, flip, eye, out _);
            if (placed.Ok)
            {
                TakeOne(p, from, slot);
            }
            return placed;
        }
        if (building.DoorOf(target) is not { } door)
        {
            return OutcomeCode.NotFound;
        }
        if (!WithinReach(eye, DoorMiddle(door)))
        {
            return OutcomeCode.OutOfReach;
        }
        switch (action)
        {
            case DoorAction.Toggle:
                return building.Toggle(p.Id.Value, door, KeysOf(p));
            case DoorAction.PickUp:
            {
                DoorDef def = door.Def;
                Outcome taken = building.PickUpDoor(p.Id.Value, door);
                if (taken.Ok && ItemOfDoor(def) is { } item)
                {
                    GiveBack(entity, p, item);
                }
                return taken;
            }
            default:
                return OutcomeCode.Invalid;
        }
    }

    private ItemDef? ItemOfDoor(DoorDef def)
    {
        foreach (ItemDef item in Content!.Items)
        {
            if (item.Door == def)
            {
                return item;
            }
        }
        return null;
    }

    /// <summary>
    /// Locks on a door: fits the key or code lock in a slot; sets a code, enters one, locks, unlocks; cuts a key (the key
    /// lock's owner, for its price in wood); takes the lock off as an item.
    /// </summary>
    public Outcome LockAct(Entity entity, LockAction action, ContainerRef from, int slot, ushort expected, EntityId target, string? code)
    {
        Outcome ok = Builder(entity, out Player p, out Vector3 eye);
        if (!ok.Ok)
        {
            return ok;
        }
        BuildingWorld building = Building!;
        if (building.DoorOf(target) is not { } door)
        {
            return OutcomeCode.NotFound;
        }
        if (!WithinReach(eye, DoorMiddle(door)))
        {
            return OutcomeCode.OutOfReach;
        }
        ulong player = p.Id.Value;
        switch (action)
        {
            case LockAction.Attach:
            {
                Outcome holding = Holding(p, from, slot, expected, BuildingRole.KeyLock, out ItemDef? item);
                if (!holding.Ok)
                {
                    return holding;
                }
                Outcome attached = item!.Building == BuildingRole.CodeLock
                    ? building.AttachCodeLock(player, door, out _)
                    : building.AttachKeyLock(player, door, out _);
                if (attached.Ok)
                {
                    TakeOne(p, from, slot);
                }
                return attached;
            }
            case LockAction.SetCode:
                return building.SetCode(player, door, code ?? "");
            case LockAction.EnterCode:
                return building.EnterCode(player, door, code ?? "", BuildingNow);
            case LockAction.Lock:
                return building.SetLocked(player, door, true, KeysOf(p));
            case LockAction.Unlock:
                return building.SetLocked(player, door, false, KeysOf(p));
            case LockAction.MakeKey:
            {
                if (door.Lock is not KeyLock keyLock)
                {
                    return OutcomeCode.NoLock;
                }
                if (keyLock.Owner != player)
                {
                    return OutcomeCode.NotOwner;
                }
                BuildingData data = building.Data;
                if (Content!.Item(data.KeyItem) is not { } key)
                {
                    return OutcomeCode.Invalid;
                }
                if (!new ItemWallet(Content, p.Inventory.Carried).Take(data.KeyCostMaterial, data.KeyCost))
                {
                    return OutcomeCode.NotEnough.With(data.MaterialItem(data.KeyCostMaterial));
                }
                GiveBack(entity, p, key, 1, (ushort)keyLock.KeyCode);
                return Outcome.Done;
            }
            case LockAction.AuthoriseTeam:
                return building.AuthoriseOnLock(player, door, TeamOf(p));
            case LockAction.Remove:
            {
                bool coded = door.Lock is CodeLock;
                Outcome removed = building.RemoveLock(player, door);
                if (removed.Ok && Content!.Item(coded ? building.Data.CodeLockItem : building.Data.KeyLockItem) is { } item)
                {
                    GiveBack(entity, p, item);
                }
                return removed;
            }
            default:
                return OutcomeCode.Invalid;
        }
    }

    /// <summary>
    /// The claim device: places the one in a slot on a foundation or floor (<paramref name="target"/>) at a point of
    /// its top (<paramref name="localX"/>, <paramref name="localZ"/> in the structure's frame) turned by
    /// <paramref name="yaw"/> 30° steps; authorises the player, takes them off, clears the list; or picks it up, its
    /// store into the player's slots.
    /// </summary>
    public Outcome ClaimAct(Entity entity, ClaimAction action, ContainerRef from, int slot, ushort expected, EntityId target,
        double localX, double localZ, int yaw)
    {
        Outcome ok = Builder(entity, out Player p, out Vector3 eye);
        if (!ok.Ok)
        {
            return ok;
        }
        BuildingWorld building = Building!;
        ulong player = p.Id.Value;
        if (action == ClaimAction.Place)
        {
            Outcome holding = Holding(p, from, slot, expected, BuildingRole.Claim, out _);
            if (!holding.Ok)
            {
                return holding;
            }
            if (building.PieceOf(target) is not { } support)
            {
                return OutcomeCode.NotFound;
            }
            if (!double.IsFinite(localX) || !double.IsFinite(localZ))
            {
                return OutcomeCode.Invalid;
            }
            Vector3 at = BuildingWorld.F(support.Structure.Frame.ToZone(new Vector3D(localX, support.Pose.Position.Y, localZ)));
            if (Vector3.Distance(eye, at) > building.Data.Reach)
            {
                return OutcomeCode.OutOfReach;
            }
            Outcome placed = building.PlaceClaim(player, support, new Vector3D(localX, 0, localZ), yaw, out _);
            if (placed.Ok)
            {
                TakeOne(p, from, slot);
            }
            return placed;
        }
        if (building.ClaimOf(target) is not { } device)
        {
            return OutcomeCode.NotFound;
        }
        (Vector3 position, _) = device.ZoneTransform;
        if (!WithinReach(eye, position + Frame.UpAt(position) * 0.9f))
        {
            return OutcomeCode.OutOfReach;
        }
        switch (action)
        {
            case ClaimAction.Authorise:
                return building.Authorise(player, device);
            case ClaimAction.Deauthorise:
                return building.Deauthorise(player, device);
            case ClaimAction.ClearList:
                return building.ClearAuthorised(player, device);
            case ClaimAction.AuthoriseTeam:
                return AuthoriseTeam(player, device, TeamOf(p));
            case ClaimAction.PickUp:
            {
                if (!device.IsAuthorised(player))
                {
                    return OutcomeCode.NotAuthorised;
                }
                if (DeployableOf(device)?.Storage is { } store)
                {
                    for (int i = 0; i < store.Size; i++)
                    {
                        if (!store[i].IsEmpty && Content!.Item(store[i].Item) is { } stored)
                        {
                            GiveBack(entity, p, stored, store[i].Count, store[i].Condition);
                        }
                    }
                    store.Clear();
                }
                Outcome taken = building.PickUpClaim(player, device);
                if (taken.Ok && Content!.Item(building.Data.ClaimItem) is { } item)
                {
                    GiveBack(entity, p, item);
                }
                return taken;
            }
            default:
                return OutcomeCode.Invalid;
        }
    }
}

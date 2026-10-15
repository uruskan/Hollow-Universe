using System.Collections.Generic;
using System.Numerics;
using System;
using Hollow.Core;
using Hollow.Simulation.Building;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

/// <summary>What a hammer does to a piece.</summary>
public enum HammerAction : byte
{
    Upgrade,
    Rotate,
    Demolish,
    Repair,
}

// Building in a live zone (GROUND.md §6.3, phase 2): the zone's BuildingWorld on its Jolt world, run on the zone's
// tick (collapses) and on a slow schedule (upkeep and decay, closed form); the player's building actions, each checked
// against what they carry and answered with the rules' reason; the claim device as a deployable whose storage is its
// upkeep store; and hits on pieces, doors and devices.
public sealed class BuildingSystem : ZoneSystem
{
    private DamageSystem? _damageSystem;
    private DamageSystem Damage => _damageSystem ??= Zone.Require<DamageSystem>();
    private PlayerSystem? _playersSystem;
    private PlayerSystem Players => _playersSystem ??= Zone.Require<PlayerSystem>();
    private SpillSystem? _spillSystem;
    private SpillSystem Spill => _spillSystem ??= Zone.Require<SpillSystem>();
    private SupportSystem? _supportsSystem;
    private SupportSystem Supports => _supportsSystem ??= Zone.Require<SupportSystem>();
    private ThingSystem? _thingsSystem;
    private ThingSystem Things => _thingsSystem ??= Zone.Require<ThingSystem>();

    private readonly Dictionary<ClaimDevice, Deployable> _claimDeployables = new();
    private readonly List<int> _keys = new();
    private double _buildingAdvancedAt = double.NaN;

    /// <summary>The zone's building (null in a G1 zone, without game data).</summary>
    public BuildingWorld World { get; private set; } = null!;

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
    public ClaimDevice? ClaimOf(Deployable deployable) => World?.ClaimOf(deployable.Id) is { } c && DeployableOf(c) == deployable ? c : null;

    private void InitBuilding(GameContent content)
    {
        World = new BuildingWorld(content.Building, Physics, NextId) { Characters = CharacterCapsules };
        World.Changed += OnBuildingChanged;
    }

    // Each tick: collapses due (nothing at all while none is under way). On the slow schedule: upkeep and decay.
    private void StepBuilding()
    {
        BuildingWorld building = World!;
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
        if (World == null)
        {
            return;
        }
        double now = BuildingNow;
        if (!double.IsNaN(_buildingAdvancedAt) && now > _buildingAdvancedAt)
        {
            World.AdvanceTime(now - _buildingAdvancedAt, now);
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
                yield return new Capsule(WorldOf(s), UpOf(s), Tuning.Radius, Tuning.StandingHeight);
            }
        }
    }

    // A claim device is also a deployable: replicated and opened like a box, its storage the upkeep store. Whatever the
    // building takes away (or starts to bring down) takes what rests on it or is stuck in it along (GROUND_SYSTEMS.md §3.6).
    private void OnBuildingChanged(BuildingEvent e)
    {
        switch (e.Change)
        {
            case BuildingChange.PieceRemoved or BuildingChange.PieceChanged when e.Piece is { } piece && (piece.Gone || piece.Collapsing):
                Supports.SupportGone(piece.Ref);
                break;
            case BuildingChange.DoorRemoved when e.Door is { } door:
                Supports.SupportGone(door.Ref);
                break;
            case BuildingChange.ClaimRemoved when e.Claim is { } claim:
                Supports.SupportGone(claim.Ref);
                break;
        }
        switch (e.Change)
        {
            case BuildingChange.ClaimAdded when e.Claim is { } claim && !_claimDeployables.ContainsKey(claim):
            {
                (Vector3 position, Quaternion rotation) = claim.ZoneTransform;
                Vector3 up = Frame.UpAt(position);
                Vector3 along = Vector3.Transform(Vector3.UnitZ, rotation);
                var d = new Deployable(claim.Id, Content.ClaimDeployable, position, up, Facing.YawOf(-along, up), new EntityId(claim.Owner))
                {
                    Health = (float)claim.Health,
                };
                _claimDeployables.Add(claim, d);
                Things.AddStatic(d);
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
            case BuildingChange.PieceRemoved or BuildingChange.DoorRemoved when e.Removal != Removal.Taken && BuildingWorld.Removed(e) is { } gone:
                Damage.Destroyed(gone.Target, gone.Key, gone.Grade, gone.Position, gone.Rotation, BuildingWorld.CauseOf(e.Removal), gone.Open);
                break;
            case BuildingChange.ClaimRemoved when e.Claim is { } claim && _claimDeployables.Remove(claim, out Deployable? d):
                if (e.Removal != Removal.Taken)
                {
                    (Vector3 position, Quaternion rotation) = claim.ZoneTransform;
                    Damage.Destroyed(claim.Ref, Content.ClaimDeployable.Key, 0, position, rotation, CauseOf(e.Removal));
                }
                // Its store spills where it stood, in one bag.
                Things.RemoveStatic(d);
                if (d.Storage != null)
                {
                    Spill.Spill(d.Position + d.Up * 0.3f, d.Storage, d.Yaw);
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
        if (World?.Physics is not { } physics || !physics.Ray(origin, direction, length, out RayHit hit, buildingOnly: true))
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
        BuildingWorld building = World!;
        var hit = projectile ? new DamageHit(direction, Bullet: amount) : new DamageHit(direction, Melee: amount);
        DamageSystem.Blow outer = Damage._blow;
        if (!outer.Active)
        {
            Damage._blow = new DamageSystem.Blow { Active = true, Cause = projectile ? DestroyCause.Bullet : DestroyCause.Melee, Direction = direction };
        }
        try
        {
            return part is IBuildingPart p ? (float)building.Damage(p, hit, BuildingNow).Loss : 0;
        }
        finally
        {
            Damage._blow = outer;
        }
    }

    // The building's own reasons for taking a piece away (destroyed: the blow's).
    private static DestroyCause? CauseOf(Removal removal) => BuildingWorld.CauseOf(removal);

    /// <summary>
    /// The building's receiver (GROUND.md §6.4.2): pieces, doors and claim devices, found by their bodies along a ray and
    /// by their nearest points in a blast's reach (a claim device by its deployable's box). Its defences are the building
    /// rules' (a grade's or door's protection by channel, a wall's soft side, whole points); a blast brings the explosive's
    /// own table by grade and door, whole on the piece it hit and a splash share on the rest, its fractions of a point
    /// carried to the next hit. Its consequences are the building rules' (breaking, what it held collapsing: reported
    /// destroyed by <see cref="OnBuildingChanged"/>) and raid hits: any explosive on a claimed base, or a blow, arrow or
    /// bullet that took something off one whose claim device doesn't list the attacker.
    /// </summary>
    internal sealed class BuildingReceiver(Zone zone) : DamageSystem.IDamageReceiver
    {
        public TargetFamily[] Families { get; } = { TargetFamily.Piece, TargetFamily.Door, TargetFamily.Cupboard };

        public void Raycast(in RayQuery ray, ref TraceHit best)
        {
            // Walls stop swings, arrows and bullets: only up to the nearest thing found so far. What owns the body names it
            // (a door's leaf as its part: an arrow stuck in it swings with it).
            if (zone.Building().World?.Physics is { } physics && physics.Ray(ray.Origin, ray.Direction, ray.Upto(best), out RayHit hit, buildingOnly: true) &&
                physics.OwnerOf(hit.Body) is ITargetable owner)
            {
                ray.Consider(ref best, hit.Distance, owner.Ref);
            }
        }

        public void InReach(in BlastQuery blast, List<BlastTarget> into)
        {
            if (zone.Building().World is not { } building)
            {
                return;
            }
            building.PiecesNear(blast.At, blast.At, blast.Radius + 3.6f, zone.Explosives()._blastPieces);
            foreach (Piece piece in zone.Explosives()._blastPieces)
            {
                if (piece.Gone || piece.Collapsing)
                {
                    continue;
                }
                Vector3 point = BlastSystem.ClosestPoint(building.Data, piece, blast.At);
                float d = Vector3.Distance(point, blast.At);
                bool stuck = blast.IsStuckTo(piece.Ref);
                if (d <= blast.Radius || stuck)
                {
                    into.Add(Target(piece, point, BlastSystem.CentreOf(piece), stuck ? 0 : d, stuck, table: true));
                }
                if (piece.Door is { Gone: false } door)
                {
                    (Vector3 doorPoint, Vector3 _) = BlastSystem.ClosestPoint(door, blast.At);
                    float dd = Vector3.Distance(doorPoint, blast.At);
                    bool onDoor = blast.IsStuckTo(door.Ref);
                    if (dd <= blast.Radius || onDoor)
                    {
                        into.Add(Target(door, doorPoint, BlastSystem.CentreOf(door), onDoor ? 0 : dd, onDoor, table: true));
                    }
                }
            }
            // Claim devices by their deployable boxes: their bodies are what hides them, their health the building rules'.
            zone.Explosives()._blastThings.Clear();
            zone.Things().DeployablesNear(blast.At, blast.Radius + 2f, zone.Explosives()._blastThings);
            foreach (Deployable box in zone.Explosives()._blastThings)
            {
                if (box.Health <= 0 || zone.Building().ClaimOf(box) is not { } claim)
                {
                    continue;
                }
                Vector3 point = BlastSystem.ClosestPoint(box, blast.At);
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
            if (zone.Building().World is not { } building || zone.Targets().Resolve(r.Hit.Target) is not IBuildingPart part || part.Gone || part is Piece { Collapsing: true })
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
                ? zone.Blasts().DamageStructure(part, amount, r.Direction, zone.Building().BuildingNow)
                : (zone.Building().DamageBuilding(part, r.Direction, (float)amount, projectile: r.Channel != DamageType.Melee), part.Gone);
            zone.Explosives().RaidStats.StructureDamage += (long)Math.Round(lost);
            if (destroyed)
            {
                if (part is Piece)
                {
                    zone.Explosives().RaidStats.PiecesDestroyed++;
                }
                else if (part is Door)
                {
                    zone.Explosives().RaidStats.DoorsDestroyed++;
                }
            }
            if (claim != null && (explosive || (lost > 0 && (r.By == null || !claim.IsAuthorised(r.By.Id.Value)))))
            {
                // A raid hit: a blast's are summed per base over the blast, a blow's alerts at once.
                BlastSystem.RaidTallies raids = r.Raids ?? new BlastSystem.RaidTallies();
                raids.Add(claim, zone.Blasts().RaidName(part), destroyed, r.Direct);
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
        DamageSystem.Blow outer = Damage._blow;
        Damage._blow = new DamageSystem.Blow { Active = true, Cause = DestroyCause.Demolish, Attacker = p.Id };
        try
        {
            return World!.Demolish(p.Id.Value, piece, eye, BuildingNow);
        }
        finally
        {
            Damage._blow = outer;
        }
    }

    // ---------------------------------------------------------------- the player's building actions

    private Outcome Builder(Entity entity, out Player p, out Vector3 eye)
    {
        eye = default;
        p = Players.PlayerOf(entity)!;
        if (World == null || p == null)
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
                if (!c[i].IsEmpty && Content.Item(c[i].Item)?.Building == role)
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
        foreach (ItemDef item in Content.Items)
        {
            if (item.Building == role)
            {
                return item.Id;
            }
        }
        return 0;
    }

    // The numbers of the keys a player carries (the zone's own list, good until the next call).
    internal List<int> KeysOf(Player p)
    {
        _keys.Clear();
        foreach (Container c in p.Inventory.Carried)
        {
            for (int i = 0; i < c.Size; i++)
            {
                if (!c[i].IsEmpty && Content.Item(c[i].Item)?.Building == BuildingRole.Key)
                {
                    _keys.Add(c[i].Condition);
                }
            }
        }
        return _keys;
    }

    private void GiveBack(Entity entity, Player p, ItemDef item, int count = 1, ushort condition = 0)
    {
        Spill.GiveOrDrop(entity, p, condition != 0 ? new ItemStack(item.Id, count, condition) : ItemStack.Of(item, count));
        Events.Add(new ItemsGained(Player: p.Id, Item: item.Id, Count: count, Reason: GainReason.Refunded));
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
        BuildingWorld building = World!;
        if (building.PieceOf(target) is not { } piece)
        {
            return OutcomeCode.NotFound;
        }
        var wallet = new ItemWallet(Content, p.Inventory.Carried);
        return action switch
        {
            HammerAction.Upgrade => building.Upgrade(p.Id.Value, piece, grade, wallet, eye),
            HammerAction.Rotate => building.Rotate(p.Id.Value, piece, eye, BuildingNow),
            HammerAction.Demolish => Demolish(p, piece, eye),
            HammerAction.Repair => building.Repair(p.Id.Value, piece, wallet, eye, BuildingNow),
            _ => OutcomeCode.Invalid,
        };
    }

    /// <summary>An open door without a lock taken back as its item (the door capability's verb, GROUND_SYSTEMS.md §5).</summary>
    internal Outcome TakeDoor(Entity entity, Player p, Door door)
    {
        DoorDef def = door.Def;
        Outcome taken = World!.PickUpDoor(p.Id.Value, door);
        if (taken.Ok && ItemOfDoor(def) is { } item)
        {
            GiveBack(entity, p, item);
        }
        return taken;
    }

    private ItemDef? ItemOfDoor(DoorDef def)
    {
        foreach (ItemDef item in Content.Items)
        {
            if (item.Door == def)
            {
                return item;
            }
        }
        return null;
    }

    /// <summary>A key cut for a key lock, by its owner for its price in wood (the key lock capability's verb).</summary>
    internal Outcome CutKey(Entity entity, Player p, Door door)
    {
        if (door.Lock is not KeyLock keyLock)
        {
            return OutcomeCode.NoLock;
        }
        if (keyLock.Owner != p.Id.Value)
        {
            return OutcomeCode.NotOwner;
        }
        BuildingData data = World!.Data;
        if (Content.Item(data.KeyItem) is not { } key)
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

    /// <summary>A lock taken off its door as its item, unlocked (the lock capabilities' verb).</summary>
    internal Outcome TakeLock(Entity entity, Player p, Door door)
    {
        BuildingWorld building = World!;
        bool coded = door.Lock is CodeLock;
        Outcome removed = building.RemoveLock(p.Id.Value, door);
        if (removed.Ok && Content.Item(coded ? building.Data.CodeLockItem : building.Data.KeyLockItem) is { } item)
        {
            GiveBack(entity, p, item);
        }
        return removed;
    }

    /// <summary>The tool cupboard picked up by one on its list, its store into their slots (the cupboard capability's verb).</summary>
    internal Outcome TakeClaim(Entity entity, Player p, ClaimDevice device)
    {
        BuildingWorld building = World!;
        ulong player = p.Id.Value;
        if (!device.IsAuthorised(player))
        {
            return OutcomeCode.NotAuthorised;
        }
        if (DeployableOf(device)?.Storage is { } store)
        {
            for (int i = 0; i < store.Size; i++)
            {
                if (!store[i].IsEmpty && Content.Item(store[i].Item) is { } stored)
                {
                    GiveBack(entity, p, stored, store[i].Count, store[i].Condition);
                }
            }
            store.Clear();
        }
        Outcome taken = building.PickUpClaim(player, device);
        if (taken.Ok && Content.Item(building.Data.ClaimItem) is { } item)
        {
            GiveBack(entity, p, item);
        }
        return taken;
    }

    public override int InitOrder => 20;

    protected internal override void Init() => InitBuilding(Content);

    protected internal override void Step()
    {
        StepBuilding();
    }

    public override void Dispose() => World?.Dispose();
}

/// <summary>The accessor on the zone (GROUND_SYSTEMS.md §12.2, K4).</summary>
public static class BuildingSystemAccess
{
    public static BuildingSystem Building(this Zone zone) => zone.Require<BuildingSystem>();
}

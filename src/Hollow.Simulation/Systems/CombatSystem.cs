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
using Hollow.Simulation.Nature;
using Hollow.Simulation.Players;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

/// <summary>An arrow in flight (later bullets): simulated on the server only (ARCHITECTURE.md §11.5).</summary>
public sealed class Projectile
{
    public uint Id;
    public EntityId Shooter;
    public string ShooterName = "";
    public ItemDef Ammo = null!;
    public ushort Weapon;
    public float Damage;
    public Vector3 Position;
    public Vector3 Velocity;
    public int AgeTicks;
    public int LifetimeTicks;

    /// <summary>Targets are tested where they were this many ticks before (what the shooter saw).</summary>
    public float RewindTicks;
}

/// <summary>
/// What a ray met first (GROUND_SYSTEMS.md §3.1): the thing, named by its family and ID (a body's part: the bone hit), the
/// body part's region, how far along and where. The family's receiver finds the thing itself.
/// </summary>
public struct TraceHit
{
    public TargetRef Target;
    /// <summary>Physical identity when gameplay intentionally treats the obstacle as ground (unharvested boulders).</summary>
    public TargetRef ContactTarget;
    public float Distance;
    public Vector3 Point;
    public HitRegion Region;
    public Vector3 Normal;
    public byte Surface;
    public SurfaceContact Contact;

    public readonly bool Hit => Distance >= 0;
}

// Melee and the bow (GROUND.md §6): swings and shots from the held item, tested on the server against where others
// were when the attacker saw them (ARCHITECTURE.md §11.5); arrows fly with drop; damage by body part, armour, bleeding.
public sealed class CombatSystem : ZoneSystem
{
    private ArrowSystem? _arrowsSystem;
    private ArrowSystem Arrows => _arrowsSystem ??= Zone.Require<ArrowSystem>();
    private DamageSystem? _damageSystem;
    private DamageSystem Damage => _damageSystem ??= Zone.Require<DamageSystem>();
    private GunSystem? _gunsSystem;
    private GunSystem Guns => _gunsSystem ??= Zone.Require<GunSystem>();
    private HeldSystem? _heldSystem;
    private HeldSystem Held => _heldSystem ??= Zone.Require<HeldSystem>();
    private PlayerSystem? _playersSystem;
    private PlayerSystem Players => _playersSystem ??= Zone.Require<PlayerSystem>();
    private SpillSystem? _spillSystem;
    private SpillSystem Spill => _spillSystem ??= Zone.Require<SpillSystem>();
    private SupportSystem? _supportsSystem;
    private SupportSystem Supports => _supportsSystem ??= Zone.Require<SupportSystem>();

    /// <summary>A melee swing is a ray thickened by this much (metres).</summary>
    public const float SweepRadius = 0.15f;

    /// <summary>Arrows are this thick.</summary>
    public const float ArrowRadius = 0.02f;

    private readonly List<Projectile> _projectiles = new();
    internal readonly List<NaturalObject> _traceNature = new();
    internal readonly List<Deployable> _traceThings = new();
    internal uint _nextProjectile = 1;

    public IReadOnlyList<Projectile> Projectiles => _projectiles;

    internal float MaxRewindTicks => Content.Survival.MaxRewindSeconds * CharacterMotor.TickRate;

    public const int MaxQueuedMelee = 64;
    public const int MeleeLateTicks = 30;
    public const int MeleeLeadTicks = GunSystem.GunActionLeadTicks;

    /// <summary>A new connection cannot inherit an earlier connection's attempt IDs or delayed work.</summary>
    public void BeginMeleeSession(Entity entity)
    {
        if (Players.PlayerOf(entity) is not { } p) return;
        p.MeleeByIntents = true;
        p.MeleeQueue.Clear();
        p.MeleeJournal.Clear();
        p.MeleeAttack = default;
        p.MeleeActionTick = p.MeleeRequestedTick = 0;
        p.MeleeCancelledThrough = 0;
        p.MeleeContactPending = false;
        p.StrikeTick = -1;
    }

    /// <summary>A reliable attempt has its own identity; repeated/missing movement input cannot create another swing.</summary>
    public OutcomeCode QueueMelee(Entity entity, AttackRequest request)
    {
        if (Players.PlayerOf(entity) is not { } p) return OutcomeCode.Invalid;
        p.MeleeByIntents = true;
        if (request.Id.Kind != AttackDomain.Melee || request.Id.IsNone || request.Id.Session != p.AttackSession) return OutcomeCode.Invalid;
        if (request.Cancel) return CancelMelee(entity, request.Id);
        if (p.MeleeJournal.TryGet(request.Id, out AttackResolution previous))
        {
            Events.Add(new AttackResolved(p.Id, previous));
            return previous.Code;
        }
        if (request.Id.Sequence <= p.MeleeCancelledThrough)
        {
            ResolveMelee(p, new AttackResolution(request.Id, OutcomeCode.Stale, request.Tick,
                ReadyTick: ReadyTick(p)) { Cancelled = true });
            return OutcomeCode.Stale;
        }
        foreach (AttackRequest queued in p.MeleeQueue)
            if (queued.Id == request.Id) return OutcomeCode.Done;
        OutcomeCode refusal = p.MeleeJournal.IsExpired(request.Id) ? OutcomeCode.Stale
            : request.Refused != OutcomeCode.Done ? request.Refused
            : !p.CanAct ? OutcomeCode.Dead
            : request.Tick < Tick - MeleeLateTicks ? OutcomeCode.TooLate
            : request.Tick > Tick + MeleeLeadTicks || request.Slot >= PlayerInventory.BeltSlots ? OutcomeCode.Invalid
            : p.MeleeQueue.Count >= MaxQueuedMelee ? OutcomeCode.RateLimited : OutcomeCode.Done;
        if (refusal != OutcomeCode.Done)
        {
            ResolveMelee(p, new AttackResolution(request.Id, refusal, request.Tick, ReadyTick: ReadyTick(p)));
            return refusal;
        }
        int at = p.MeleeQueue.Count;
        while (at > 0 && (p.MeleeQueue[at - 1].Tick > request.Tick ||
            p.MeleeQueue[at - 1].Tick == request.Tick && p.MeleeQueue[at - 1].Id.Sequence > request.Id.Sequence)) at--;
        p.MeleeQueue.Insert(at, request);
        return OutcomeCode.Done;
    }

    /// <summary>
    /// Cancels unfinished attempts through this identity. The bounded frontier survives reordered requests;
    /// suppressing an unknown/duplicate acknowledgement never suppresses cancellation itself.
    /// </summary>
    public OutcomeCode CancelMelee(Entity entity, AttackId id, bool acknowledge = true)
    {
        if (Players.PlayerOf(entity) is not { } p || id.Kind != AttackDomain.Melee || id.IsNone || id.Session != p.AttackSession)
            return OutcomeCode.Invalid;
        bool answered = false;
        if (id.Sequence > p.MeleeCancelledThrough)
        {
            p.MeleeCancelledThrough = id.Sequence;
            for (int i = p.MeleeQueue.Count - 1; i >= 0; i--)
            {
                AttackRequest queued = p.MeleeQueue[i];
                if (queued.Id.Sequence > id.Sequence) continue;
                p.MeleeQueue.RemoveAt(i);
                ResolveMelee(p, new AttackResolution(queued.Id, OutcomeCode.Stale, queued.Tick,
                    ReadyTick: ReadyTick(p)) { Cancelled = true });
                answered |= queued.Id == id;
            }
            if (p.MeleeContactPending && !p.MeleeAttack.IsNone && p.MeleeAttack.Sequence <= id.Sequence)
            {
                answered |= p.MeleeAttack == id;
                CancelActiveMelee(p, OutcomeCode.Stale);
                p.RecordAction(ActionKind.None, p.HeldItem, Tick);
            }
        }
        if (p.MeleeJournal.TryGet(id, out AttackResolution previous))
        {
            // Completed damage stays accepted. A duplicate cancellation may repeat its answer,
            // but cannot turn an old completed attack into a new cancelled attack.
            if (acknowledge && !answered) Events.Add(new AttackResolved(p.Id, previous));
            return previous.Code;
        }
        if (acknowledge)
            ResolveMelee(p, new AttackResolution(id, OutcomeCode.Stale, 0, ReadyTick: ReadyTick(p)) { Cancelled = true });
        return OutcomeCode.Stale;
    }

    private static uint ReadyTick(Player p) => (uint)Math.Max(0, Math.Max(p.EquipReadyTick, p.NextSwingTick));

    private void ResolveMelee(Player p, AttackResolution result)
    {
        p.MeleeJournal.Remember(result);
        Events.Add(new AttackResolved(p.Id, result));
    }

    private void CancelActiveMelee(Player p, OutcomeCode code)
    {
        p.StrikeTick = -1;
        if (!p.MeleeContactPending) return;
        p.MeleeContactPending = false;
        if (!p.MeleeAttack.IsNone)
            ResolveMelee(p, new AttackResolution(p.MeleeAttack, code, p.MeleeRequestedTick,
                p.MeleeActionTick, ReadyTick: ReadyTick(p)) { Cancelled = true });
    }

    private void CancelAllMelee(Player p, OutcomeCode code)
    {
        CancelActiveMelee(p, code);
        foreach (AttackRequest request in p.MeleeQueue)
            ResolveMelee(p, new AttackResolution(request.Id, code, request.Tick, ReadyTick: ReadyTick(p)) { Cancelled = true });
        p.MeleeQueue.Clear();
    }

    private void StepMeleeQueue(Player p)
    {
        while (p.MeleeQueue.Count > 0 && p.MeleeQueue[0].Tick <= Tick)
        {
            AttackRequest request = p.MeleeQueue[0];
            p.MeleeQueue.RemoveAt(0);
            ItemDef? held = p.Held(Content);
            OutcomeCode code = request.Tick < Tick - MeleeLateTicks ? OutcomeCode.TooLate
                : request.Slot != p.HeldSlot || request.Item != p.HeldItem ? OutcomeCode.Stale
                : held?.Melee == null ? OutcomeCode.NoWeapon
                : Tick < p.EquipReadyTick || Tick < p.NextSwingTick || p.StrikeTick >= 0 ? OutcomeCode.Cooldown : OutcomeCode.Done;
            if (code != OutcomeCode.Done)
            {
                ResolveMelee(p, new AttackResolution(request.Id, code, request.Tick, ReadyTick: ReadyTick(p)));
                continue;
            }
            StartMelee(p, held!, request.Id, request.Tick);
            ResolveMelee(p, new AttackResolution(request.Id, OutcomeCode.Done, request.Tick, p.MeleeActionTick,
                (uint)p.StrikeTick, ReadyTick(p)));
        }
    }

    private void StartMelee(Player p, ItemDef item, AttackId attack = default, uint requestedTick = 0)
    {
        MeleeStats melee = item.Melee!;
        p.MeleeAttack = attack;
        p.MeleeRequestedTick = requestedTick;
        p.MeleeActionTick = (uint)Tick;
        p.MeleeContactPending = true;
        p.StrikeTick = Tick + melee.StrikeTicks;
        p.NextSwingTick = Tick + melee.SwingTicks;
        p.RecordAction(ActionKind.Swing, item.Id, Tick);
    }

    private void StepActions()
    {
        GameContent content = Content;
        ComponentPool<PlayerLink> players = Entities.Pool<PlayerLink>();
        Players._scratchEntities.Clear();
        for (int i = 0; i < players.Count; i++)
        {
            Players._scratchEntities.Add(Entities.AtSlot(players.Slots[i]));
        }
        foreach (Entity entity in Players._scratchEntities)
        {
            if (Players.PlayerOf(entity) is not { } p)
            {
                continue;
            }
            if (!p.IsAwakeAndAlive)
            {
                CancelAllMelee(p, OutcomeCode.Dead);
                continue;
            }
            ref Character c = ref Entities.Get<Character>(entity);
            if (p.Wounded)
            {
                CancelAllMelee(p, OutcomeCode.Dead);
                Guns.RefuseGunActions(p, OutcomeCode.Dead);
                continue;
            }
            if (Entities.Has<Ships.ShipSeat>(entity))
            {
                CancelAllMelee(p, OutcomeCode.Lowered);
                // In a ship's seat (G6): the hands are on the controls.
                Guns.RefuseGunActions(p, OutcomeCode.Lowered);
                continue;
            }
            // Death, sleep and seating systems can clear StrikeTick before this system runs.
            if (p.MeleeContactPending && p.StrikeTick < 0) CancelActiveMelee(p, OutcomeCode.Stale);
            ActionInput input = c.Actions;
            // Taking up another item (or another in the same slot) takes a moment and ends what was going on: the held
            // behaviour of what was in hand says what ends (GROUND_SYSTEMS.md §7); the gun's state follows the hands.
            ushort inSlot = input.HeldSlot < PlayerInventory.BeltSlots ? p.Inventory.Belt[input.HeldSlot].Item : (ushort)0;
            if (input.HeldSlot != p.HeldSlot || inSlot != p.HeldItem)
            {
                CancelActiveMelee(p, OutcomeCode.Stale);
                // Replicate an explicit user slot switch even when both slots hold the same item type. Depletion
                // within the same slot must keep the accepted use/throw event for its remaining presentation.
                if (input.HeldSlot != p.HeldSlot) p.RecordAction(ActionKind.None, inSlot, Tick);
                HeldSystem.IHeldRules? putAway = Held.HeldRulesOf(content.Item(p.HeldItem));
                p.HeldSlot = input.HeldSlot;
                p.HeldItem = inSlot;
                p.EquipReadyTick = Tick + CharacterTicks.From(content.Survival.EquipSeconds);
                putAway?.PutAway(p);
                Guns.EquipGun(p);
            }
            StepMeleeQueue(p);
            // The item in hand's buttons, by its held behaviour.
            if (p.Held(content) is { } held)
            {
                Held.HeldRulesOf(held)?.Step(entity, ref c, p, held);
            }
            Guns.StepGun(entity, ref c, p);
        }
    }

    internal void StepMelee(Entity entity, ref Character c, Player p, ItemDef item, MeleeStats melee)
    {
        if (!p.MeleeByIntents && p.StrikeTick < 0 &&
            (c.Actions.Has(ActionButtons.Primary) || c.Actions.Has(ActionButtons.PrimaryPressed)) && Tick >= p.NextSwingTick && Tick >= p.EquipReadyTick)
        {
            StartMelee(p, item);
        }
        if (p.StrikeTick == Tick)
        {
            p.StrikeTick = -1;
            p.MeleeContactPending = false;
            Strike(entity, ref c, p, item, melee);
        }
    }

    /// <summary>
    /// The swing lands: a thick ray from the eye along the view, out to the tool's reach; whatever it meets first (but the
    /// ground) takes a melee blow through the damage pipeline (GROUND.md §6.4.2): its damage to flesh (by the body part),
    /// its structure damage to deployables and the building, its damage to hulls; trees and outcrops are gathered, a
    /// corpse butchered.
    /// </summary>
    private void Strike(Entity entity, ref Character c, Player p, ItemDef item, MeleeStats melee)
    {
        GameContent content = Content;
        float rewind = Math.Min(c.Actions.ViewDelay / 4f, MaxRewindTicks);
        // From the eye along the view, aboard a ship as the player saw it (IRON_WAKE.md §4.9).
        (Vector3 eye, Vector3 direction, _) = Zone.AttackLine(c, Tick - rewind);
        TraceHit hit = Damage.Trace(eye, direction, melee.Reach, SweepRadius, p.Id, Tick - rewind);
        if (!hit.Hit || hit.Target.Family == TargetFamily.Ground)
        {
            return;
        }
        Damage.Damage(new DamageRequest
        {
            By = p,
            Attacker = p.Id,
            AttackerName = p.Name,
            Weapon = item.Id,
            Attack = p.MeleeAttack,
            ActionTick = p.MeleeActionTick,
            Delivery = DamageDelivery.Melee,
            Channel = DamageType.Melee,
            Amounts = new DamageAmounts
            {
                Flesh = melee.Damage * content.Survival.RegionMultiplier(hit.Region),
                Structure = melee.Structure,
                Building = melee.Structure,
                Hull = melee.Damage,
            },
            Bleed = melee.Bleed,
            Cause = "Struck down",
            Direct = true,
            Hit = hit,
            From = eye,
            Direction = direction,
            Markers = TargetFamilies.All,
        });
        Wear(entity, p, p.HeldSlot);
    }

    internal void Gather(Player p, Entity entity, in NaturalObject o, MeleeStats tool, Vector3 point, Vector3 eye)
    {
        // The swing's own line (the thick ray may have touched the object off it) and the tool (the last hit's bonus).
        ref Character c = ref Entities.Get<Character>(entity);
        Vector3 view = Zone.AttackLine(c, Tick).Direction;
        GatherResult result = Natural!.Hit(o, new GatherSwing(p.Held(Content), tool, point, eye, view, UnixMilliseconds, Tick));
        if (!result.Hit)
        {
            return;
        }
        if (Natural.IsDepleted(o.Id))
        {
            Supports.SupportGone(new TargetRef(TargetFamily.Natural, o.Id));
        }
        Events.Add(new HitConfirmed(By: p.Id, Target: new TargetRef(TargetFamily.Natural, o.Id), Flag: result.WeakSpot, Point: result.Point,
            Weapon: p.Held(Content)?.Id ?? 0) { Attack = p.MeleeAttack, ActionTick = p.MeleeActionTick });
        Give(entity, p, result.Yield, GainReason.Gathered, result.WeakSpot);
    }

    /// <summary>Gives items to a player (onto stacks, then empty slots; the rest at their feet) and says so.</summary>
    internal void Give(Entity entity, Player p, List<ItemAmount> items, GainReason reason, bool flag = false)
    {
        foreach (ItemAmount a in items)
        {
            Spill.GiveOrDrop(entity, p, ItemStack.Of(a.Item, a.Count));
            Events.Add(new ItemsGained(Player: p.Id, Item: a.Item.Id, Count: a.Count, Reason: reason, WeakSpot: flag));
        }
    }

    // A use costs the held tool one point of condition; at none it breaks.
    internal void Wear(Entity entity, Player p, int beltSlot)
    {
        if (beltSlot >= PlayerInventory.BeltSlots)
        {
            return;
        }
        ItemStack s = p.Inventory.Belt[beltSlot];
        ItemDef? item = Content.Item(s.Item);
        if (item == null || !item.HasCondition)
        {
            return;
        }
        if (s.Condition <= 1)
        {
            p.Inventory.Belt.Set(beltSlot, default);
            Guns.ReturnRounds(entity, p, s);
            Events.Add(new ItemsGained(Player: p.Id, Item: s.Item, Count: -1, Reason: GainReason.Broke));
            return;
        }
        s.Condition--;
        p.Inventory.Belt.Set(beltSlot, s);
    }

    internal void StepBow(Entity entity, ref Character c, Player p, ItemDef item, BowStats bow)
    {
        ItemDef? ammo = null;
        foreach (ItemDef a in bow.Ammo)
        {
            if (p.Inventory.Count(a) > 0)
            {
                ammo = a;
                break;
            }
        }
        bool drawing = c.Actions.Has(ActionButtons.Secondary) && Tick >= p.EquipReadyTick && ammo != null &&
            (c.State.Flags & MoveFlags.Swimming) == 0;
        if (!drawing)
        {
            p.DrawStartTick = -1;
            return;
        }
        if (p.DrawStartTick < 0)
        {
            p.DrawStartTick = Tick;
            p.RecordAction(ActionKind.Draw, item.Id, Tick);
        }
        if (!c.Actions.Has(ActionButtons.PrimaryPressed) || Tick - p.DrawStartTick < bow.DrawTicks)
        {
            return;
        }
        // Loose: an arrow from the inventory, from the eye along the view, at the arrow's speed.
        InventoryRules.Take(p.Inventory.Carried, ammo!.Id, 1);
        // Aboard a ship the arrow leaves where the player saw it, with the ship's velocity added (IRON_WAKE.md §4.9).
        float rewindTicks = Math.Min(c.Actions.ViewDelay / 4f, MaxRewindTicks);
        (Vector3 eye, Vector3 direction, Vector3 carried) = Zone.AttackLine(c, Tick - rewindTicks);
        var projectile = new Projectile
        {
            Id = _nextProjectile++,
            Shooter = p.Id,
            ShooterName = p.Name,
            Ammo = ammo,
            Weapon = item.Id,
            Damage = ammo.Ammo!.Damage * bow.DamageScale,
            Position = eye,
            Velocity = direction * (ammo.Ammo.Speed * bow.SpeedScale) + carried,
            LifetimeTicks = CharacterTicks.From(ammo.Ammo.LifetimeSeconds),
            RewindTicks = Math.Min(c.Actions.ViewDelay / 4f, MaxRewindTicks),
        };
        Launch(projectile);
        p.DrawStartTick = Tick; // drawing again while the button is held
        p.RecordAction(ActionKind.Shoot, item.Id, Tick);
        Wear(entity, p, p.HeldSlot);
    }

    /// <summary>Starts a projectile and tells those near.</summary>
    public void Launch(Projectile projectile)
    {
        _projectiles.Add(projectile);
        Events.Add(new ProjectileLaunched(Shooter: projectile.Shooter, Projectile: projectile.Id, Ammo: projectile.Ammo.Id,
            Position: projectile.Position, Velocity: projectile.Velocity)
        {
            Tick = (uint)Tick,
            Weapon = projectile.Weapon,
            Gravity = (float)(Spec.SurfaceGravity * 9.80665 * projectile.Ammo.Ammo!.Gravity),
        });
    }

    // Arrows fly in two steps a tick, under the planet's gravity, tested against the world now and against characters
    // where the shooter saw them.
    private void StepProjectiles()
    {
        const int steps = 2;
        const float h = CharacterMotor.DeltaTime / steps;
        float gravity = (float)(Spec.SurfaceGravity * 9.80665);
        for (int i = _projectiles.Count - 1; i >= 0; i--)
        {
            Projectile pr = _projectiles[i];
            bool ended = false;
            for (int s = 0; s < steps && !ended; s++)
            {
                Vector3 g = -Frame.UpAt(pr.Position) * (gravity * pr.Ammo.Ammo!.Gravity);
                Vector3 next = pr.Position + pr.Velocity * h + g * (0.5f * h * h);
                Vector3 path = next - pr.Position;
                float length = path.Length();
                if (length > 1e-6f)
                {
                    TraceHit hit = Damage.Trace(pr.Position, path / length, length, ArrowRadius, pr.Shooter, Tick - pr.RewindTicks);
                    if (hit.Hit)
                    {
                        ProjectileHit(pr, hit);
                        ended = true;
                        break;
                    }
                }
                pr.Position = next;
                pr.Velocity += g * h;
            }
            if (!ended && ++pr.AgeTicks >= pr.LifetimeTicks)
            {
                ended = true;
                Events.Add(new ProjectileEnded(Shooter: pr.Shooter, Projectile: pr.Id, Position: pr.Position)
                    { Tick = (uint)Tick, Ammo = pr.Ammo.Id });
            }
            if (ended)
            {
                _projectiles.RemoveAt(i);
            }
        }
    }

    // An arrow stops at what it met: an arrow request through the damage pipeline (GROUND.md §6.4.2); one that stays whole
    // sticks where it hit, in whatever it hit, a living body too (GROUND_SYSTEMS.md §5, ArrowSystem).
    private void ProjectileHit(Projectile pr, in TraceHit hit)
    {
        AmmoStats ammo = pr.Ammo.Ammo!;
        Events.Add(new ProjectileEnded(Shooter: pr.Shooter, Projectile: pr.Id, Position: hit.Point, Target: hit.Target)
            { Tick = (uint)Tick, Ammo = pr.Ammo.Id, Contact = hit.Contact });
        Players.TryFindPlayer(pr.Shooter, out _, out Player? shooter);
        Damage.Damage(new DamageRequest
        {
            By = shooter,
            Attacker = shooter?.Id ?? EntityId.None,
            AttackerName = shooter?.Name ?? pr.ShooterName,
            Weapon = pr.Ammo.Id,
            Delivery = DamageDelivery.Arrow,
            Channel = DamageType.Bullet,
            Amounts = new DamageAmounts
            {
                Flesh = pr.Damage * Content.Survival.RegionMultiplier(hit.Region),
                Structure = pr.Damage * ammo.Structure,
                Building = pr.Damage * ammo.Structure,
                Hull = pr.Damage,
            },
            Bleed = ammo.Bleed,
            Cause = "Shot",
            Direct = true,
            Hit = hit,
            From = pr.Position,
            Direction = Vector3.Normalize(pr.Velocity),
            Markers = TargetFamilies.All,
        });
        // It stays whole as often as its ammo says (Rust's arrows break now and then), and then sticks.
        var rng = new Rng(Hash64.Of(pr.Id, (ulong)Tick, 0xA770));
        if (rng.NextDouble() < ammo.Recover)
        {
            Arrows.StickArrow(pr, hit);
        }
    }

    /// <summary>
    /// Damage to a character from a blow or an arrow (tests): a request through the characters' receiver, with the body
    /// part's multiplier (survival.json), then what is worn there takes its share (items.json); bleeding starts in
    /// proportion. Tells the victim and the attacker. Returns the health taken.
    /// </summary>
    public float DamageCharacter(Entity victimEntity, Player victim, float damage, HitRegion region, bool projectile, float bleed,
        Player? attacker, Vector3 from, ushort weapon) => Damage.Damage(new DamageRequest
        {
            By = attacker,
            Attacker = attacker?.Id ?? EntityId.None,
            AttackerName = attacker?.Name ?? "",
            Weapon = weapon,
            Delivery = projectile ? DamageDelivery.Arrow : DamageDelivery.Melee,
            Channel = projectile ? DamageType.Bullet : DamageType.Melee,
            Amounts = new DamageAmounts { Flesh = damage * Content.Survival.RegionMultiplier(region) },
            Bleed = bleed,
            Cause = projectile ? "Shot" : "Struck down",
            Direct = true,
            Hit = new TraceHit { Target = victim.Ref, Region = region, Point = from },
            From = from,
            Markers = TargetFamilies.All,
        }).Taken;

    /// <summary>
    /// The characters' receiver (GROUND.md §6.4.2): players and sleepers, found where the attacker saw them (the hit
    /// history, lag compensation) by their hit shapes, and by chest and head in a blast's reach. Its defences: what is
    /// worn on the part hit (items.json, against blows or against projectiles and blasts). Its consequences: health (a
    /// lethal hit may knock down instead, except a projectile to the head), bleeding and burning, the victim's damage
    /// indicator.
    /// </summary>
    internal sealed class CharacterReceiver(Zone zone) : DamageSystem.IDamageReceiver
    {
        public TargetFamily[] Families { get; } = { TargetFamily.Character };

        public void Raycast(in RayQuery ray, ref TraceHit best)
        {
            // Characters, where the attacker saw them (those nowhere near now are skipped before their history is looked up).
            float length = ray.Length;
            Span<HitShape> shapes = stackalloc HitShape[Hitboxes.MostShapes];
            foreach (EntityId id in zone.History.Characters)
            {
                if (id == ray.Ignore || (zone.History.TryNewest(id, out Vector3 now) && Vector3.DistanceSquared(now, ray.Origin) > (length + 8f) * (length + 8f)) ||
                    !zone.History.Sample(id, ray.ViewTick, out HitPose pose) ||
                    Vector3.DistanceSquared(pose.Feet, ray.Origin) > (length + 2.5f) * (length + 2.5f))
                {
                    continue;
                }
                if (!zone.Players().TryFindPlayer(id, out _, out Player p) || p.Dead)
                {
                    continue;
                }
                float t = Hitboxes.Raycast(pose, ray.Origin, ray.Direction, length, ray.Thickness, out HitRegion region, out int shape);
                if (t >= 0)
                {
                    Hitboxes.Shapes(pose, shapes);
                    Vector3 normal = ContactGeometry.CapsuleNormal(ray.Origin + ray.Direction * t, shapes[shape].A, shapes[shape].B, -ray.Direction);
                    ray.Consider(ref best, t, new TargetRef(TargetFamily.Character, id.Value, (byte)(shape + 1)), region, normal);
                }
            }
        }

        public void InReach(in BlastQuery blast, List<BlastTarget> into)
        {
            // A sleeper and the downed lie; a pilot is the ship's cabin's to protect (G6).
            ComponentPool<PlayerLink> players = zone.Entities.Pool<PlayerLink>();
            for (int i = 0; i < players.Count; i++)
            {
                Player p = players.Values[i].Player;
                if (p.Dead)
                {
                    continue;
                }
                Entity entity = zone.Entities.AtSlot(players.Slots[i]);
                CharacterState s = zone.Entities.Get<Character>(entity).State;
                bool seated = s.Stance == Stance.Seated;
                if (seated && !zone.Ships().SeatExposed(entity))
                {
                    continue; // a sheltered seat: the cabin's to protect
                }
                // Where it is in the world (aboard a ship, where the ship carries it).
                Vector3 up = zone.UpOf(s), feet = zone.WorldOf(s);
                bool lying = p.Sleeping || p.Wounded;
                Vector3 chest = feet + up * (seated ? 0.45f : lying ? 0.25f : s.Stance == Stance.Crouching ? 0.7f : 1.15f);
                Vector3 head = feet + up * (seated ? 0.93f : lying ? 0.3f : s.Stance == Stance.Crouching ? 1.0f : 1.6f);
                // Whoever a round struck takes the blast at its middle (IW4: a direct hit is a direct hit, wherever it landed).
                bool struck = blast.IsStuckTo(p.Ref);
                float distance = struck ? 0 : MathF.Max(0, MathF.Min(Vector3.Distance(chest, blast.At), Vector3.Distance(head, blast.At)) - 0.3f);
                if (distance <= blast.Radius)
                {
                    into.Add(new BlastTarget
                    {
                        Hit = new TraceHit { Target = p.Ref, Region = HitRegion.Body, Point = chest }, Thing = p,
                        Sight = chest, Sight2 = head, Distance = distance, Stuck = struck,
                    });
                }
            }
        }

        public DamageOutcome Receive(in DamageRequest r)
        {
            if (!zone.Players().TryFindPlayer(new EntityId(r.Hit.Target.Id), out Entity victimEntity, out Player victim))
            {
                return default;
            }
            HitRegion region = r.Hit.Region;
            float protection = victim.Inventory.Protection(zone.Content!, region, projectile: r.Channel != DamageType.Melee);
            float amount = r.Amounts.Flesh * (1 - protection);
            if (amount <= 0 || victim.Dead)
            {
                return default;
            }
            bool wasDown = victim.Wounded;
            victim.Vitals.Bleeding += amount * r.Bleed;
            victim.Vitals.Burning += r.Burn * (1 - protection);
            // Rust: a projectile to the head kills outright, and so does fire (as the burning vital does); anything else lethal
            // may knock down instead (G4).
            float taken = zone.Players().Hurt(victimEntity, victim, amount, r.Cause, r.Attacker, r.AttackerName,
                canWound: !r.Outright && !(r.Channel == DamageType.Bullet && region == HitRegion.Head));
            zone.Events.Add(new Hurt(Player: victim.Id, By: r.ByAnimal?.Id ?? r.Attacker, Amount: amount, Region: region, From: r.From, Weapon: r.Weapon));
            return new DamageOutcome(true, amount, taken, victim.Dead, victim.Wounded && !wasDown, victim.Id);
        }
    }

    /// <summary>Where a ray first goes below the ground (within <paramref name="length"/>), or −1.</summary>
    public float GroundCrossing(Vector3 origin, Vector3 direction, float length) => Ground.Crossing(origin, direction, length);

    /// <summary>
    /// Trees, outcrops and boulders (GROUND.md §6.4.2): found by their trunks and rocks along a ray (a boulder stops a
    /// swing or an arrow like the ground and gives nothing); blasts pass them by. A blow of a tool gathers (the gathering
    /// rules, §6.1, which confirm the hit with its weak spot); arrows and bullets do nothing to them.
    /// </summary>
    internal sealed class NatureReceiver(Zone zone) : DamageSystem.IDamageReceiver
    {
        private readonly float _rootReach = NaturalShapes.MaximumRayRootReach(zone.Content!.NaturalKinds);
        public TargetFamily[] Families { get; } = { TargetFamily.Natural };

        public void Raycast(in RayQuery ray, ref TraceHit best)
        {
            Vector3 end = ray.Origin + ray.Direction * ray.Length;
            zone.Combat()._traceNature.Clear();
            zone.Nature!.QueryAlong(ray.Origin, end, _rootReach + ray.Thickness, zone.Combat()._traceNature);
            foreach (NaturalObject o in zone.Combat()._traceNature)
            {
                NaturalKind? kind = zone.Content!.Natural(o.Kind);
                if (kind == null || !kind.IsSolid || zone.Natural!.IsDepleted(o.Id))
                {
                    continue;
                }
                float t;
                Vector3 normal;
                if (kind.Class == NaturalClass.Tree)
                {
                    (Vector3 a, Vector3 b, float r) = NaturalShapes.Trunk(o, kind);
                    t = Geometry.RayCapsule(ray.Origin, ray.Direction, ray.Length, a, b, r + ray.Thickness);
                    normal = ContactGeometry.CapsuleNormal(ray.Origin + ray.Direction * t, a, b, -ray.Direction);
                }
                else
                {
                    (Vector3 centre, float r) = NaturalShapes.Rock(o, kind);
                    t = Geometry.RaySphere(ray.Origin, ray.Direction, ray.Length, centre, r + ray.Thickness);
                    normal = ContactGeometry.CapsuleNormal(ray.Origin + ray.Direction * t, centre, centre, -ray.Direction);
                }
                // A boulder stops the swing or the arrow like the ground, and gives nothing.
                ray.Consider(ref best, t, kind.IsHarvested ? new TargetRef(TargetFamily.Natural, o.Id) : TargetRef.Ground,
                    normal: normal, surface: zone.Content!.Surfaces.FromNatural(kind.Key), contactTarget: new TargetRef(TargetFamily.Natural, o.Id));
            }
        }

        public void InReach(in BlastQuery blast, List<BlastTarget> into)
        {
        }

        public DamageOutcome Receive(in DamageRequest r)
        {
            if (r.Channel == DamageType.Melee && r.By is { } p && zone.Content!.Item(r.Weapon)?.Melee is { } tool && zone.Players().TryFindPlayer(p.Id, out Entity entity, out _) &&
                zone.Nature!.TryGet(r.Hit.Target.Id, out NaturalObject o))
            {
                zone.Combat().Gather(p, entity, o, tool, r.Hit.Point, r.From);
            }
            return default;
        }
    }

    /// <summary>The ground (GROUND.md §6.4.2): it hides all behind it, and takes nothing.</summary>
    internal sealed class GroundReceiver(Zone zone) : DamageSystem.IDamageReceiver
    {
        public TargetFamily[] Families { get; } = { TargetFamily.Ground };

        public void Raycast(in RayQuery ray, ref TraceHit best)
        {
            float length = ray.Upto(best);
            float ground = ray.Bullet ? zone.Guns().BulletGroundCrossing(ray.Origin, ray.Direction, length) : zone.Combat().GroundCrossing(ray.Origin, ray.Direction, length);
            if (ground >= 0)
            {
                Vector3 point = ray.Origin + ray.Direction * ground;
                Vector3 normal = new GroundProbe(zone.Surface, zone.Frame).Normal(point).Normal;
                ray.Consider(ref best, ground, TargetRef.Ground, normal: normal,
                    surface: zone.Content?.Surfaces.AtGround(zone.Surface, zone.Frame, point, normal) ?? 0);
            }
        }

        public void InReach(in BlastQuery blast, List<BlastTarget> into)
        {
        }

        public DamageOutcome Receive(in DamageRequest r) => default;
    }

    protected internal override void Step()
    {
        StepActions();
        StepProjectiles();
    }
}

/// <summary>The accessor on the zone (GROUND_SYSTEMS.md §12.2, K4).</summary>
public static class CombatSystemAccess
{
    public static CombatSystem Combat(this Zone zone) => zone.Require<CombatSystem>();
}

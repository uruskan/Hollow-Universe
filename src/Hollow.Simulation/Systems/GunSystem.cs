using System.Collections.Generic;
using System.Numerics;
using System;
using DamageType = Hollow.Simulation.Building.DamageType;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Targets;
using Hollow.Simulation.World;

namespace Hollow.Simulation;

/// <summary>A bullet in flight (G4): simulated on the server only (ARCHITECTURE.md §11.5); clients draw their own.</summary>
public sealed class Bullet
{
    public uint Id;
    public EntityId Shooter;
    public string ShooterName = "";

    /// <summary>The shooter's gun action that fired it.</summary>
    public uint Seq;
    public AttackId Attack;
    public uint ActionTick;
    public FirearmStats Gun = null!;
    public BulletStats Round = null!;
    public ItemDef Ammo = null!;

    /// <summary>Where it left the gun (the shooter's eye): where the victim's damage indicator points.</summary>
    public Vector3 Origin;
    public Vector3 Position;
    public Vector3 Velocity;

    /// <summary>Speed lost a second (a fraction), the per-step factor that gives, and gravity in m/s² (the gun's times the round's).</summary>
    public double Drag;
    public double Decay;
    public float Gravity;

    /// <summary>Damage before falloff, body part and armour (the gun's, times the round's and the attachments').</summary>
    public float Damage;

    /// <summary>Metres flown so far, and the most it flies.</summary>
    public float Distance;
    public float Range;
    public int AgeTicks;
    public int LifetimeTicks;

    /// <summary>Characters are tested where they were this many ticks before (what the shooter saw).</summary>
    public float RewindTicks;
    public bool Silenced;

    /// <summary>
    /// A battery's rocket (IRON_WAKE.md §6.4, IW3): its motor (null for a gun's round, flown with drag and drop alone), the
    /// burn used so far, the battery's number and the station and barrel it left (a shot's number is <see cref="Seq"/>).
    /// </summary>
    public RocketMotor? Motor;
    public float Burnt;

    /// <summary>The ship a battery's rocket left (its key), passed through for its first <see cref="GunSystem.OwnHullTicks"/> (IW3).</summary>
    public ulong OwnShip;
    public int Battery = -1;
    public int Station = -1;
    public int Barrel = -1;
}

// Firearms (GROUND.md §6.3): shots and reloads the clients asked for, applied at the tick they happened on with the
// rules both sides share (Combat/Firearms.cs); bullets flown with drop and drag and tested against characters where
// the shooter saw them; damage by distance, body part and armour; attachments put on and taken off.
public sealed partial class GunSystem : ZoneSystem
{
    private ActionSystem? _actionsSystem;
    private ActionSystem Actions => _actionsSystem ??= Zone.Require<ActionSystem>();
    private CombatSystem? _combatSystem;
    private CombatSystem Combat => _combatSystem ??= Zone.Require<CombatSystem>();
    private DamageSystem? _damageSystem;
    private DamageSystem Damage => _damageSystem ??= Zone.Require<DamageSystem>();
    private ExplosiveSystem? _explosivesSystem;
    private ExplosiveSystem Explosives => _explosivesSystem ??= Zone.Require<ExplosiveSystem>();
    private PlayerSystem? _playersSystem;
    private PlayerSystem Players => _playersSystem ??= Zone.Require<PlayerSystem>();
    private SpillSystem? _spillSystem;
    private SpillSystem Spill => _spillSystem ??= Zone.Require<SpillSystem>();

    /// <summary>At most this many gun actions wait per player; and they may be at most this many ticks ahead.</summary>
    public const int MaxQueuedGunActions = 64;

    public const int GunActionLeadTicks = 64;

    /// <summary>
    /// A shot or reload may be applied this many ticks after its own (1 s: its intent was lost and resent); its hit test
    /// still looks back at most the rewind limit (0.25 s).
    /// </summary>
    public const int GunActionLateTicks = 30;

    /// <summary>A battery's rocket passes through its own ship's space for this many ticks (it leaves from inside the barrel; IW3).</summary>
    public const int OwnHullTicks = 30;

    /// <summary>Bullets fly in this many steps a tick.</summary>
    public const int BulletSteps = 2;

    public const float BulletStepSeconds = CharacterMotor.DeltaTime / BulletSteps;

    /// <summary>Bullets are lines (RustMVP traces them so); a hair of thickness against rounding.</summary>
    public const float BulletRadius = 0.005f;

    private readonly List<Bullet> _bullets = new();

    /// <summary>Bullets in flight.</summary>
    public IReadOnlyList<Bullet> Bullets => _bullets;

    /// <summary>Time spent flying bullets so far (the load test reports it), and bullet steps taken.</summary>
    public TimeSpan BulletTime { get; private set; }

    public long BulletStepsFlown { get; private set; }

    /// <summary>Gun actions answered so far, by answer code (the load test reports the refusals).</summary>
    public long[] GunResults { get; } = new long[128];

    // ---------------------------------------------------------------- gun actions

    /// <summary>
    /// A shot or reload the player's client asked for (a fire or reload intent), queued for its tick in sequence order
    /// (they travel unordered, so a resent one may come after later ones). Returns what it was refused for already (it is
    /// still answered in order), or <see cref="OutcomeCode.Done"/> when it waits for its tick;
    /// <see cref="OutcomeCode.Stale"/> for one already seen or passed (fire attempts receive their original decision
    /// or an explicit refusal). A gap waits within the bounded late-request window before advancing cumulative state.
    /// </summary>
    public OutcomeCode QueueGunAction(Entity entity, GunAction action)
    {
        if (Players.PlayerOf(entity) is not { } p)
        {
            return OutcomeCode.Invalid;
        }
        AttackId id = new(AttackDomain.Firearm, action.Seq, p.AttackSession);
        if (action.Kind == GunActionKind.Fire && p.GunAttackJournal.TryGet(id, out AttackResolution known))
        {
            Events.Add(new AttackResolved(p.Id, known));
            return OutcomeCode.Stale; // Queue status only; the wire answer is the original authoritative decision.
        }
        int at = p.GunQueue.Count;
        while (at > 0 && p.GunQueue[at - 1].Seq > action.Seq)
        {
            at--;
        }
        if (at > 0 && p.GunQueue[at - 1].Seq == action.Seq)
        {
            // The first request still owns this attempt; its answer will settle both deliveries.
            return OutcomeCode.Stale;
        }
        if (action.Seq <= p.Gun.LastSeq || action.Kind == GunActionKind.Fire && p.GunAttackJournal.IsExpired(id))
        {
            ResolveAttack(p, action, OutcomeCode.Stale);
            return OutcomeCode.Stale;
        }
        if (p.GunQueue.Count >= MaxQueuedGunActions)
        {
            ResolveAttack(p, action, OutcomeCode.RateLimited);
            return OutcomeCode.RateLimited;
        }
        if (action.Refused == OutcomeCode.Done)
        {
            if (!p.CanAct)
            {
                action.Refused = OutcomeCode.Dead;
            }
            else if (action.Tick < Tick - GunActionLateTicks)
            {
                action.Refused = OutcomeCode.TooLate;
            }
            else if (action.Tick > Tick + GunActionLeadTicks)
            {
                action.Refused = OutcomeCode.Invalid;
            }
        }
        if (action.Refused != OutcomeCode.Done && p.GunQueue.Count == 0 &&
            (!p.CanAct || !AttackOrder.AwaitingEarlier(action.Seq, (ulong)p.Gun.LastSeq + 1, action.Tick, Tick, GunActionLateTicks)))
        {
            // An out-of-order refusal must not acknowledge unseen earlier shots/reloads. The dead and
            // sleeping do not step their guns, so their terminal refusals can still be answered immediately.
            Settle(p, action, action.Refused);
            return action.Refused;
        }
        p.GunQueue.Insert(at, action);
        return action.Refused;
    }

    // The gun in hand, after melee and the bow: a reload that ends, then the actions whose tick has come, in order.
    internal void StepGun(Entity entity, ref Character c, Player p)
    {
        FirearmStats? gun = p.Held(Content)?.Firearm;
        if (p.Gun.Reloading && Tick >= p.Gun.ReloadEndTick)
        {
            FinishReload(entity, p, gun);
        }
        while (p.GunQueue.Count > 0)
        {
            GunAction a = p.GunQueue[0];
            if (AttackOrder.AwaitingEarlier(a.Seq, (ulong)p.Gun.LastSeq + 1, a.Tick, Tick, GunActionLateTicks))
            {
                break; // Preserve ammunition/recoil/reload order while a missing reliable request is retransmitted.
            }
            if (a.Refused == OutcomeCode.Done && a.Tick > Tick)
            {
                break;
            }
            p.GunQueue.RemoveAt(0);
            OutcomeCode result = a.Refused != OutcomeCode.Done ? a.Refused : ApplyGunAction(entity, ref c, p, gun, a);
            Settle(p, a, result);
        }
    }

    private void Settle(Player p, in GunAction action, OutcomeCode result)
    {
        uint seq = action.Seq;
        GunResults[(int)result & 127]++;
        p.Gun.LastSeq = Math.Max(p.Gun.LastSeq, seq);
        if (result != OutcomeCode.Done)
        {
            p.GunRefusedSeq = seq;
            p.GunRefused = result;
        }
        p.GunRevision++;
        ResolveAttack(p, action, result);
    }

    private void ResolveAttack(Player p, in GunAction action, OutcomeCode result)
    {
        if (action.Kind != GunActionKind.Fire) return;
        var resolution = new AttackResolution(new AttackId(AttackDomain.Firearm, action.Seq, p.AttackSession), result,
            (uint)Math.Clamp(action.Tick, 0, uint.MaxValue),
            StartTick: result == OutcomeCode.Done ? (uint)Math.Clamp(action.Tick, 0, uint.MaxValue) : 0,
            ReadyTick: (uint)Math.Clamp((p.Gun.NextFire16 + 15) / 16, 0, uint.MaxValue), StateSequence: p.Gun.LastSeq);
        p.GunAttackJournal.Remember(resolution);
        Events.Add(new AttackResolved(p.Id, resolution));
    }

    private OutcomeCode ApplyGunAction(Entity entity, ref Character c, Player p, FirearmStats? gun, in GunAction a)
    {
        if (gun == null)
        {
            return OutcomeCode.NoWeapon;
        }
        if (a.Slot != p.HeldSlot || a.Item != p.HeldItem)
        {
            return OutcomeCode.Stale;
        }
        if (a.Tick < Tick - GunActionLateTicks)
        {
            return OutcomeCode.TooLate;
        }
        ItemStack weapon = p.Inventory.Belt[p.HeldSlot];
        GameContent content = Content;
        if (a.Kind == GunActionKind.Unload)
        {
            // UNLOAD in hand (GROUND_SYSTEMS.md §7): the rounds back into the inventory, in order with the shots before it.
            OutcomeCode can = FirearmRules.CanUnload(p.Gun, weapon, a.Tick);
            return can != OutcomeCode.Done ? can : FirearmRules.Unload(content, p.Inventory, p.Inventory.Belt, p.HeldSlot).Code;
        }
        if (a.Kind == GunActionKind.Reload)
        {
            Container[] carried = { p.Inventory.Main, p.Inventory.Belt };
            ushort ammo = FirearmRules.ReloadAmmo(weapon, gun, a.Ammo, id => InventoryRules.Count(carried, id));
            OutcomeCode reload = FirearmRules.BeginReload(ref p.Gun, weapon, gun, ammo, ammo == 0 ? 0 : InventoryRules.Count(carried, ammo), a.Tick);
            if (reload == OutcomeCode.Done)
            {
                p.RecordAction(ActionKind.Reload, weapon.Item, a.Tick);
            }
            return reload;
        }
        // On its tick the shot leaves from where the shooter is; a late one (its intent was resent) from where they were.
        // Aboard a ship (IRON_WAKE.md §4.9) it is worked out in the ship's frame, as the shooter's client predicted it, and
        // leaves where the shooter saw the ship, with the ship's velocity added.
        ulong frame = c.State.Frame;
        Vector3 up = frame == 0 ? Frame.UpAt(c.State.Position) : Vector3.UnitY;
        Vector3 eye = c.State.Position + up * Tuning.EyeHeight(c.State.Stance);
        Vector3 velocity = c.State.Velocity;
        bool crouched = c.State.Stance == Stance.Crouching;
        MoveFlags flags = c.State.Flags;
        if (a.Tick < Tick)
        {
            if (!History.SampleLocalAtTick(p.Id, a.Tick, out HitPose pose, out ulong then)) return OutcomeCode.TooLate;
            frame = then;
            up = pose.Up;
            eye = pose.Feet + pose.Up * (pose.Pose == Pose.Crouching ? Tuning.CrouchingEyeHeight : Tuning.StandingEyeHeight);
            velocity = pose.Velocity;
            crouched = pose.Pose == Pose.Crouching;
            flags = pose.Flags;
        }
        if ((flags & (MoveFlags.Sprinting | MoveFlags.Swimming)) != 0) return OutcomeCode.Lowered;
        Vector3 view = a.View;
        if (a.Frame != frame)
        {
            // Server ShipFrame.TryPose is current-only: a queued handover needs the recorded
            // source and target frames at the shot tick, before the seen-pose spawn conversion below.
            if (Zone.SeenPose(a.Frame, a.Tick, out ShipPose source)) view = source.DirectionToWorld(view);
            if (Zone.SeenPose(frame, a.Tick, out ShipPose target)) view = target.DirectionToLocal(view);
            view = Vector3.Normalize(view);
        }
        var context = new ShotContext
        {
            Shooter = p.Id,
            Seq = a.Seq,
            Tick = a.Tick,
            Yaw = Facing.YawOf(view, up),
            Pitch = Facing.PitchOf(view, up),
            Aimed = a.Aimed,
            Eye = eye,
            Up = up,
            Velocity = velocity,
            Crouched = crouched,
            Airborne = (flags & (MoveFlags.OnGround | MoveFlags.Swimming)) == 0,
        };
        OutcomeCode result = FirearmRules.Fire(ref p.Gun, ref weapon, content, gun, context, out Shot shot);
        if (result != OutcomeCode.Done)
        {
            return result;
        }
        p.Inventory.Belt.Set(p.HeldSlot, weapon);
        Vector3 shipVelocity = Vector3.Zero;
        float rewind = Math.Min(a.ViewDelay / 4f + Math.Max(0, Tick - a.Tick), Combat.MaxRewindTicks);
        if (frame != 0 && Zone.SeenPose(frame, Tick - rewind, out ShipPose seen))
        {
            Vector3 origin = seen.ToWorld(shot.Origin);
            shipVelocity = seen.VelocityAt(origin);
            shot = shot with { Origin = origin, Direction = seen.DirectionToWorld(shot.Direction) };
        }
        LaunchBullet(p, gun, shot, a, shipVelocity);
        p.RecordAction(ActionKind.Shoot, weapon.Item, a.Tick);
        Combat.Wear(entity, p, p.HeldSlot);
        return OutcomeCode.Done;
    }

    // The end of a reload: other rounds out (back into the inventory), the magazine filled from the reserve.
    private void FinishReload(Entity entity, Player p, FirearmStats? gun)
    {
        p.GunRevision++;
        if (gun == null || p.HeldSlot >= PlayerInventory.BeltSlots || p.Inventory.Belt[p.HeldSlot].Item != gun.Item.Id)
        {
            p.Gun.ReloadEndTick = -1;
            return;
        }
        ItemStack weapon = p.Inventory.Belt[p.HeldSlot];
        ushort ammo = p.Gun.ReloadAmmo;
        Container[] carried = { p.Inventory.Main, p.Inventory.Belt };
        (int take, int returned, ushort returnedAmmo) = FirearmRules.FinishReload(ref p.Gun, ref weapon, gun, InventoryRules.Count(carried, ammo));
        InventoryRules.Take(carried, ammo, take);
        p.Inventory.Belt.Set(p.HeldSlot, weapon);
        if (returned > 0 && Content.Item(returnedAmmo) is { } old)
        {
            Spill.GiveOrDrop(entity, p, ItemStack.Of(old, returned));
        }
    }

    /// <summary>Starts afresh with what is in hand now (after an equip): a gun's equip time, no recoil, no reload.</summary>
    internal void EquipGun(Player p)
    {
        FirearmStats? gun = Content.Item(p.HeldItem)?.Firearm;
        if (FirearmRules.Equip(ref p.Gun, p.HeldSlot, gun != null ? p.HeldItem : (ushort)0, gun, Tick))
        {
            p.GunRevision++;
        }
    }

    /// <summary>A new life or a death: nothing in hand, and every waiting action answered (the client drops them).</summary>
    internal void ResetGun(Player p)
    {
        uint last = p.Gun.LastSeq;
        foreach (GunAction a in p.GunQueue)
        {
            ResolveAttack(p, a, OutcomeCode.Dead);
            last = Math.Max(last, a.Seq);
        }
        p.GunQueue.Clear();
        p.Gun = GunState.None;
        p.Gun.LastSeq = last;
        p.GunRevision++;
    }

    /// <summary>Answers every waiting action with a refusal (the player went down).</summary>
    internal void RefuseGunActions(Player p, OutcomeCode result)
    {
        foreach (GunAction a in p.GunQueue)
        {
            Settle(p, a, result);
        }
        p.GunQueue.Clear();
        if (p.Gun.Reloading)
        {
            p.Gun.ReloadEndTick = -1;
            p.GunRevision++;
        }
    }

    /// <summary>What others see on the gun a player holds: a bit per attachment (weapons.json order).</summary>
    public byte HeldGunLook(Player p)
    {
        if (p.HeldSlot >= PlayerInventory.BeltSlots || Content.Item(p.Inventory.Belt[p.HeldSlot].Item)?.Firearm is not { } gun)
        {
            return 0;
        }
        return GunMods.Of(Content, p.Inventory.Belt[p.HeldSlot], gun).Bits;
    }

    // ---------------------------------------------------------------- bullets

    private void LaunchBullet(Player p, FirearmStats gun, in Shot shot, in GunAction a, Vector3 shipVelocity)
    {
        var bullet = new Bullet
        {
            Id = Combat._nextProjectile++,
            Shooter = p.Id,
            ShooterName = p.Name,
            Seq = a.Seq,
            Attack = new AttackId(AttackDomain.Firearm, a.Seq, p.AttackSession),
            ActionTick = (uint)Math.Clamp(a.Tick, 0, uint.MaxValue),
            Gun = gun,
            Round = shot.Round.Bullet!,
            Ammo = shot.Round,
            Origin = shot.Origin,
            Position = shot.Origin,
            Velocity = shot.Direction * shot.Speed + shipVelocity,
            Drag = gun.Drag,
            Decay = Ballistics.Decay(gun.Drag, BulletStepSeconds),
            Gravity = (float)gun.GravityOf(shot.Round.Bullet, Spec.SurfaceGravity),
            Damage = shot.Damage,
            Range = (float)gun.Range,
            LifetimeTicks = Math.Max(1, CharacterTicks.From(gun.LifetimeSeconds)),
            RewindTicks = Math.Min(a.ViewDelay / 4f + Math.Max(0, Tick - a.Tick), Combat.MaxRewindTicks),
            Silenced = shot.Mods.Silenced,
        };
        _bullets.Add(bullet);
        Events.Add(new ProjectileLaunched(Shooter: p.Id, Projectile: bullet.Id, Ammo: shot.Round.Id, Position: bullet.Position, Velocity: bullet.Velocity, Seq: a.Seq, Silenced: bullet.Silenced)
        {
            Attack = bullet.Attack, Tick = (uint)Tick, Weapon = gun.Item.Id, Gravity = bullet.Gravity, Drag = (float)bullet.Drag,
        });
    }

    /// <summary>
    /// A battery's rocket (IRON_WAKE.md §6.3, §6.4): the projectile system's own, so its blast, raid hits, alerts and fire are
    /// what they are for the shoulder launcher's. It leaves a barrel's muzzle along the barrel at the profile's muzzle speed
    /// with the ship's velocity there, flies under its motor (<see cref="RocketFlight"/>) for the profile's lifetime, and goes
    /// off where it stops. Its shooter is the gunner; its number is the station's shot's.
    /// </summary>
    public Bullet LaunchBattery(EntityId shooter, string shooterName, ItemDef round, BatteryDef battery, LaunchProfile profile, Vector3 origin, Vector3 direction,
        Vector3 carried, uint number, int station, int barrel, float rewindTicks, ulong ownShip = 0, AttackId attack = default)
    {
        var bullet = new Bullet
        {
            Id = Combat._nextProjectile++,
            Shooter = shooter,
            ShooterName = shooterName,
            Seq = number,
            Attack = attack,
            ActionTick = (uint)Tick,
            Round = round.Bullet!,
            Ammo = round,
            Origin = origin,
            Position = origin,
            Velocity = direction * profile.Muzzle + carried,
            Damage = 0,
            Range = float.MaxValue,
            LifetimeTicks = Math.Max(1, CharacterTicks.From(profile.Lifetime)),
            RewindTicks = Math.Min(rewindTicks, Combat.MaxRewindTicks),
            Motor = RocketMotor.Of(battery.Launch, profile),
            Battery = battery.Index,
            Station = station,
            Barrel = barrel,
            OwnShip = ownShip,
        };
        _bullets.Add(bullet);
        Events.Add(new ProjectileLaunched(Shooter: shooter, Projectile: bullet.Id, Ammo: round.Id, Position: bullet.Position, Velocity: bullet.Velocity, Seq: number)
        {
            Battery = battery.Index + 1,
            Attack = attack, Tick = (uint)Tick, Gravity = (float)(Spec.SurfaceGravity * 9.80665),
        });
        return bullet;
    }

    // Bullets fly in two steps a tick, with gravity and drag integrated exactly, tested against the world now and
    // against characters where the shooter saw them, until they hit, fly their range or run out of time.
    private void StepBullets()
    {
        if (_bullets.Count == 0)
        {
            return;
        }
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        BulletStepsFlown += _bullets.Count * (long)BulletSteps;
        for (int i = _bullets.Count - 1; i >= 0; i--)
        {
            Bullet b = _bullets[i];
            bool ended = false;
            for (int s = 0; s < BulletSteps && !ended; s++)
            {
                Vector3 next, velocity;
                if (b.Motor is { } motor)
                {
                    // A battery's rocket under its motor (IRON_WAKE.md §6.4).
                    (next, velocity, b.Burnt) = RocketFlight.Step(b.Position, b.Velocity, -Frame.UpAt(b.Position), (float)(Spec.SurfaceGravity * 9.80665),
                        motor, b.Distance, b.Burnt, BulletStepSeconds);
                }
                else
                {
                    Vector3 gravity = -Frame.UpAt(b.Position) * b.Gravity;
                    (next, velocity) = Ballistics.Step(b.Position, b.Velocity, gravity, b.Drag, b.Decay, BulletStepSeconds);
                }
                Vector3 path = next - b.Position;
                float length = path.Length();
                bool spent = false;
                if (b.Distance + length >= b.Range)
                {
                    float keep = MathF.Max(0, b.Range - b.Distance);
                    next = b.Position + path * (length > 1e-6f ? keep / length : 0);
                    spent = true;
                }
                if (length > 1e-6f)
                {
                    Vector3 direction = path / length;
                    float reach = spent ? MathF.Max(0, b.Range - b.Distance) : length;
                    TraceHit hit = reach > 1e-6f ? Damage.Trace(b.Position, direction, reach, BulletRadius, b.Shooter, Tick - b.RewindTicks, bullet: true,
                            ignoreShip: b.AgeTicks < OwnHullTicks ? b.OwnShip : 0)
                        : new TraceHit { Distance = -1 };
                    if (hit.Hit)
                    {
                        BulletHit(b, hit);
                        ended = true;
                        break;
                    }
                    b.Distance += reach;
                }
                b.Position = next;
                b.Velocity = velocity;
                if (spent)
                {
                    EndFlight(b);
                    ended = true;
                }
            }
            if (!ended && ++b.AgeTicks >= b.LifetimeTicks)
            {
                EndFlight(b);
                ended = true;
            }
            if (ended)
            {
                _bullets.RemoveAt(i);
            }
        }
        BulletTime += System.Diagnostics.Stopwatch.GetElapsedTime(started);
    }

    private void EndBullet(Bullet b, Vector3 at, TargetRef target, SurfaceContact contact = default) =>
        Events.Add(new ProjectileEnded(Shooter: b.Shooter, Projectile: b.Id, Position: at, Target: target, Seq: b.Seq)
        {
            Attack = b.Attack, Tick = (uint)Tick, Battery = b.Battery + 1, Ammo = b.Ammo.Id, Contact = contact,
        });

    // Out of range or time in the air: a bullet just stops; a rocket goes off where it is (G5).
    private void EndFlight(Bullet b)
    {
        EndBullet(b, b.Position, TargetRef.None);
        if (b.Round.Blast is { } blast && b.Damage <= 0)
        {
            Players.TryFindPlayer(b.Shooter, out _, out Player? shooter);
            Explosives.BulletBlast(b, blast, new TraceHit { Point = b.Position, Target = TargetRef.None }, shooter);
        }
    }

    private void BulletHit(Bullet b, in TraceHit hit)
    {
        float distance = b.Distance + hit.Distance;
        EndBullet(b, hit.Point, hit.Target, hit.Contact);
        Players.TryFindPlayer(b.Shooter, out _, out Player? shooter);
        if (b.Round.Blast is { } rocket && b.Damage <= 0)
        {
            // A rocket (G5): its blast is all it does.
            Explosives.BulletBlast(b, rocket, hit, shooter);
            return;
        }
        Damage.Damage(BulletRequest(b, hit, distance, shooter));
        if (b.Round.Blast is { } burst)
        {
            // An explosive round (G5) bursts where it hit, besides what the bullet did.
            Explosives.BulletBlast(b, burst, hit, shooter);
        }
    }

    /// <summary>
    /// A bullet's damage request (GROUND.md §6.4, §6.4.2): the gun's damage (times the round's and the attachments')
    /// falling off with the distance flown; to flesh scaled for the body part by the gun's head, body and limb multipliers
    /// (armour is the receiver's), with the round's bleeding and burning; to deployables the round's share for
    /// structures; to buildings all of it through the bullet channel (none for an explosive round: its burst is all it does
    /// to buildings, so Rust's counts hold); to hulls all of it.
    /// </summary>
    private static DamageRequest BulletRequest(Bullet b, in TraceHit hit, float distance, Player? shooter)
    {
        double falloff = b.Gun.Falloff(distance);
        return new DamageRequest
        {
            By = shooter,
            Attacker = shooter?.Id ?? EntityId.None,
            AttackerName = shooter?.Name ?? b.ShooterName,
            Weapon = b.Gun.Item.Id,
            Attack = b.Attack,
            ActionTick = b.ActionTick,
            Delivery = DamageDelivery.Bullet,
            Channel = DamageType.Bullet,
            Amounts = new DamageAmounts
            {
                Flesh = (float)(b.Damage * falloff * b.Gun.RegionMultiplier(hit.Region)),
                Structure = (float)(b.Damage * falloff * b.Round.Structure),
                Building = b.Round.Blast != null ? 0 : (float)(b.Damage * falloff),
                Hull = (float)(b.Damage * falloff),
            },
            Bleed = (float)b.Round.Bleed,
            Burn = (float)b.Round.Burn,
            Cause = "Shot",
            Direct = true,
            Hit = hit,
            From = b.Origin,
            Direction = b.Velocity.LengthSquared() > 1e-12f ? Vector3.Normalize(b.Velocity) : Vector3.Zero,
            Markers = TargetFamilies.All,
        };
    }

    /// <summary>
    /// A bullet in a character (GROUND.md §6.4; tests): its request through the characters' receiver. The damage falls
    /// off with the distance flown, is scaled for the body part and reduced by what is worn there against projectiles;
    /// the round may start bleeding and set the victim burning. A shot to the head that would kill, kills; any other
    /// knocks down first (the wounded state). Returns the health taken.
    /// </summary>
    public float ShootCharacter(Entity victimEntity, Player victim, Bullet b, HitRegion region, float distance, Vector3 at, Player? shooter) =>
        Damage.Damage(BulletRequest(b, new TraceHit { Target = victim.Ref, Region = region, Point = at }, distance, shooter)).Taken;

    /// <summary>
    /// Where a bullet's path first goes below the ground, or −1: steps as long as the height above the ground allows
    /// (the ground rises at most 1.25 m a metre, 51°), so a bullet high up costs few samples, then bisects the crossing.
    /// </summary>
    public float BulletGroundCrossing(Vector3 origin, Vector3 direction, float length)
    {
        float height = HeightAboveGround(origin);
        if (height < 0)
        {
            return -1;
        }
        const float steepest = 1.25f;
        float descent = MathF.Max(0, -Vector3.Dot(direction, Frame.UpAt(origin)));
        float previous = 0, t = 0;
        while (t < length)
        {
            t = MathF.Min(length, t + MathF.Max(0.75f, height / (steepest + descent)));
            height = HeightAboveGround(origin + direction * t);
            if (height < 0)
            {
                float lo = previous, hi = t;
                for (int k = 0; k < 8; k++)
                {
                    float mid = (lo + hi) / 2;
                    if (HeightAboveGround(origin + direction * mid) < 0)
                    {
                        hi = mid;
                    }
                    else
                    {
                        lo = mid;
                    }
                }
                return hi;
            }
            previous = t;
        }
        return -1;
    }

    // ---------------------------------------------------------------- attachments

    /// <summary>
    /// Puts an attachment (from the player's main slots or belt) on a firearm (also in them): into its slot, swapping out
    /// what was there (it goes where the attachment came from). The gun must take it (<see cref="AttachmentRules"/>).
    /// </summary>
    public Outcome Attach(Entity entity, ContainerRef from, int slot, ContainerRef weaponIn, int weaponSlot, ushort expected)
    {
        if (!Actions.Acting(entity, out Player p))
        {
            return OutcomeCode.Dead;
        }
        if (from is not (ContainerRef.Main or ContainerRef.Belt) || weaponIn is not (ContainerRef.Main or ContainerRef.Belt))
        {
            return OutcomeCode.NotAllowed;
        }
        return AttachmentRules.Attach(Content, p.Inventory[from], slot, p.Inventory[weaponIn], weaponSlot, expected);
    }

    /// <summary>
    /// A gun that broke gives back its rounds and its attachments: into the inventory, the rest at the player's feet (no
    /// notification: these were the player's already).
    /// </summary>
    internal void ReturnRounds(Entity entity, Player p, in ItemStack weapon)
    {
        GameContent content = Content;
        if (weapon.Loaded > 0 && content.Item(weapon.LoadedAmmo) is { } round)
        {
            Spill.GiveOrDrop(entity, p, ItemStack.Of(round, weapon.Loaded));
        }
        foreach (AttachmentSlot slot in GunMods.Slots)
        {
            if (weapon.Mod(slot) != 0 && content.Item(weapon.Mod(slot)) is { } part)
            {
                Spill.GiveOrDrop(entity, p, ItemStack.Of(part, 1));
            }
        }
    }

    protected internal override void Step()
    {
        StepBullets();
    }
}

/// <summary>The accessor on the zone (GROUND_SYSTEMS.md §12.2, K4).</summary>
public static class GunSystemAccess
{
    public static GunSystem Guns(this Zone zone) => zone.Require<GunSystem>();
}

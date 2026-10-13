using System;
using System.Numerics;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;

namespace Hollow.Simulation.Combat;

/// <summary>
/// The recoil of the gun in hand (RustMVP weapon_rules.hpp <c>RecoilState</c>): the target the view is kicked to, where
/// the kick towards it started, the bloom at the last shot, when that shot was (a tick), and the pattern's next step.
/// Degrees, pitch up and yaw to the right.
/// </summary>
public struct RecoilState : IEquatable<RecoilState>
{
    public double Pitch;
    public double Yaw;
    public double StartPitch;
    public double StartYaw;
    public double Bloom;

    /// <summary>The tick of the last shot; −1 before the first.</summary>
    public long ShotTick;

    public ushort Index;

    public static RecoilState None => new() { ShotTick = -1 };

    public readonly bool Equals(RecoilState o) => Pitch.Equals(o.Pitch) && Yaw.Equals(o.Yaw) && StartPitch.Equals(o.StartPitch) &&
        StartYaw.Equals(o.StartYaw) && Bloom.Equals(o.Bloom) && ShotTick == o.ShotTick && Index == o.Index;

    public override readonly bool Equals(object? obj) => obj is RecoilState s && Equals(s);

    public override readonly int GetHashCode() => HashCode.Combine(Pitch, Yaw, ShotTick, Index);
}

/// <summary>The recoil's offset of the view at a moment (degrees: up, right) and the bloom left.</summary>
public readonly record struct RecoilSample(double Pitch, double Yaw, double Bloom);

/// <summary>
/// RustMVP's recoil (weapon_rules.hpp <c>sample_recoil</c>, <c>apply_recoil_shot</c>) on the zone's ticks. A shot
/// samples where the view is, then adds one step (the pattern's next, or the straight kick) towards a new target, which
/// the view reaches over the kick time; after the hold the whole of it returns over the recovery time, along a
/// smoothstep. Fixed and repeatable, as the owner chose for RustMVP: no random part, the same aimed, crouched or moving;
/// only a muzzle attachment scales the step (Rust's brake and silencer). The server and the shooter's client run the
/// same function on the same ticks, so both compute the same, bit for bit.
/// </summary>
public static class Recoil
{
    private const double TicksPerSecond = CharacterTicks.Rate;

    /// <summary>Where the recoil has the view at a (fractional) tick.</summary>
    public static RecoilSample Sample(in RecoilState s, FirearmStats gun, double tick)
    {
        if (s.ShotTick < 0)
        {
            return default;
        }
        RecoilStats r = gun.Recoil;
        double elapsed = Math.Max(0, (tick - s.ShotTick) / TicksPerSecond);
        double kick = r.KickSeconds > 0 ? Math.Clamp(elapsed / r.KickSeconds, 0, 1) : 1;
        double pitch = s.StartPitch + (s.Pitch - s.StartPitch) * kick;
        double yaw = s.StartYaw + (s.Yaw - s.StartYaw) * kick;
        double recoveryStart = Math.Max(r.KickSeconds, r.RecoveryDelaySeconds);
        double retained = 1;
        if (elapsed >= recoveryStart)
        {
            // Smoothstep: no speed at either end, and all of it back in the recovery time however big the burst.
            double p = r.RecoverySeconds > 0 ? Math.Clamp((elapsed - recoveryStart) / r.RecoverySeconds, 0, 1) : 1;
            retained = 1 - p * p * (3 - 2 * p);
        }
        return new RecoilSample(pitch * retained, yaw * retained, Math.Max(0, s.Bloom - elapsed * gun.Spread.BloomRecovery));
    }

    /// <summary>How much of the kick is left to recover at a tick (1 while it holds, 0 once back).</summary>
    public static double Retained(in RecoilState s, FirearmStats gun, double tick)
    {
        if (s.ShotTick < 0)
        {
            return 0;
        }
        RecoilStats r = gun.Recoil;
        double elapsed = Math.Max(0, (tick - s.ShotTick) / TicksPerSecond);
        double recoveryStart = Math.Max(r.KickSeconds, r.RecoveryDelaySeconds);
        if (elapsed < recoveryStart)
        {
            return 1;
        }
        double p = r.RecoverySeconds > 0 ? Math.Clamp((elapsed - recoveryStart) / r.RecoverySeconds, 0, 1) : 1;
        return 1 - p * p * (3 - 2 * p);
    }

    /// <summary>
    /// A shot at <paramref name="tick"/>: the new recoil (RustMVP <c>apply_recoil_shot</c>). <paramref name="scale"/>
    /// multiplies the step: the gun's attachments (a muzzle brake, a silencer), 1 without.
    /// </summary>
    public static RecoilState Kick(in RecoilState s, FirearmStats gun, long tick, double scale = 1)
    {
        RecoilStats r = gun.Recoil;
        RecoilSample now = Sample(s, gun, tick);
        double since = s.ShotTick < 0 ? double.PositiveInfinity : (tick - s.ShotTick) / TicksPerSecond;
        int index = 0;
        RecoilStep step;
        if (r.Mode == RecoilMode.Pattern && r.Pattern.Count > 0)
        {
            // The pattern starts again after a pause; it holds on its last step.
            index = since >= r.ResetSeconds ? 0 : Math.Min(s.Index, r.Pattern.Count - 1);
            step = r.Pattern[index];
        }
        else
        {
            step = new RecoilStep(r.PitchPerShot, 0);
        }
        double stepPitch = step.Pitch * scale;
        double stepYaw = step.Yaw * scale;
        // A kick still under way (legal cadence never allows it, but keep its target rather than where it had got to).
        bool unfinished = since < r.KickSeconds;
        double basePitch = unfinished ? s.Pitch : now.Pitch;
        double baseYaw = unfinished ? s.Yaw : now.Yaw;
        return new RecoilState
        {
            Pitch = Math.Clamp(basePitch + stepPitch, 0, r.PitchLimit),
            Yaw = Math.Clamp(baseYaw + stepYaw, -r.YawLimit, r.YawLimit),
            StartPitch = now.Pitch,
            StartYaw = now.Yaw,
            Bloom = Math.Min(gun.Spread.MaxBloom, now.Bloom + gun.Spread.BloomPerShot),
            ShotTick = tick,
            Index = (ushort)(r.Mode == RecoilMode.Pattern ? Math.Min(index + 1, Math.Max(0, r.Pattern.Count - 1)) : 0),
        };
    }
}

/// <summary>What a gun's attachments add up to (weapons.json <c>attachments</c>), for one weapon stack.</summary>
public readonly record struct GunMods(double Zoom, double HipSpread, double AimedSpread, double Recoil, double Speed, double Damage,
    bool Silenced, bool Light, byte Bits)
{
    public static GunMods Of(GameContent content, in ItemStack weapon, FirearmStats gun)
    {
        double zoom = gun.Zoom, hip = 1, aimed = 1, recoil = 1, speed = 1, damage = 1;
        bool silenced = false, light = false;
        byte bits = 0;
        foreach (AttachmentSlot slot in Slots)
        {
            ushort id = weapon.Mod(slot);
            if (id == 0 || content.Item(id)?.Attachment is not { } a || a.Slot != slot || !gun.Takes(a.Item))
            {
                continue;
            }
            if (a.Zoom > 0)
            {
                zoom = a.Zoom;
            }
            hip *= a.HipSpread;
            aimed *= a.AimedSpread;
            recoil *= a.Recoil;
            speed *= a.Speed;
            damage *= a.Damage;
            silenced |= a.Silenced;
            light |= a.Light;
            bits |= (byte)(1 << a.Index);
        }
        return new GunMods(zoom, hip, aimed, recoil, speed, damage, silenced, light, bits);
    }

    public static readonly AttachmentSlot[] Slots = { AttachmentSlot.Sight, AttachmentSlot.Muzzle, AttachmentSlot.Rail };
}

/// <summary>
/// The gun in a player's hand, besides its magazine (which is part of the weapon item): which it is, when it is ready,
/// the cadence, the recoil, a reload under way, and the last gun action applied. The server keeps one per player and the
/// shooter's client predicts its own with the same rules (<see cref="FirearmRules"/>).
/// </summary>
public struct GunState : IEquatable<GunState>
{
    public const byte NoSlot = 7;

    /// <summary>The belt slot of the gun in hand (<see cref="NoSlot"/>: none) and its item (0: none).</summary>
    public byte Slot;

    public ushort Item;

    /// <summary>No shot or reload before this tick (taking a gun up takes its equip time).</summary>
    public long EquipReadyTick;

    /// <summary>No shot before this, in sixteenths of a tick (the cadence: RustMVP's deadline on the tick grid).</summary>
    public long NextFire16;

    public RecoilState Recoil;

    /// <summary>A reload under way: when it started and ends (−1: none) and the rounds it loads.</summary>
    public long ReloadStartTick;

    public long ReloadEndTick;

    public ushort ReloadAmmo;

    /// <summary>The last gun action applied (its sequence number).</summary>
    public uint LastSeq;

    public static GunState None => new() { Slot = NoSlot, ReloadEndTick = -1, Recoil = RecoilState.None };

    public readonly bool Reloading => ReloadEndTick >= 0;

    public readonly bool Equals(GunState o) => Slot == o.Slot && Item == o.Item && EquipReadyTick == o.EquipReadyTick && NextFire16 == o.NextFire16 &&
        Recoil.Equals(o.Recoil) && ReloadStartTick == o.ReloadStartTick && ReloadEndTick == o.ReloadEndTick && ReloadAmmo == o.ReloadAmmo &&
        LastSeq == o.LastSeq;

    public override readonly bool Equals(object? obj) => obj is GunState s && Equals(s);

    public override readonly int GetHashCode() => HashCode.Combine(Slot, Item, NextFire16, Recoil, ReloadEndTick, LastSeq);
}

/// <summary>What a gun action is: a shot or a reload.</summary>
public enum GunActionKind : byte
{
    Fire,
    Reload,
}

/// <summary>
/// A shot or reload the client asked for (a fire or reload intent): its sequence number, the client's tick it happened
/// on, the gun (belt slot and item) and, for a shot, the view and whether aimed, and how far behind the player saw
/// others; for a reload, which rounds (0: the loaded kind, else the first the player has).
/// </summary>
public struct GunAction
{
    public uint Seq;
    public long Tick;
    public GunActionKind Kind;
    public byte Slot;
    public ushort Item;
    public float Yaw;
    public float Pitch;
    public bool Aimed;
    public byte ViewDelay;
    public ushort Ammo;

    /// <summary>Refused already when it arrived (over budget, too old): answered so when its turn comes (Done: not refused).</summary>
    public OutcomeCode Refused;
}

/// <summary>What the shooter's body was doing at a shot (from the movement state of that tick).</summary>
public struct ShotContext
{
    public EntityId Shooter;
    public uint Seq;
    public long Tick;

    /// <summary>The view the player controls (the input's, without recoil).</summary>
    public float Yaw;
    public float Pitch;
    public bool Aimed;
    public Vector3 Eye;
    public Vector3 Up;
    public Vector3 Velocity;
    public bool Crouched;
    public bool Airborne;
}

/// <summary>A shot as it leaves the gun: from the eye, along a direction, at a speed, with its damage and rounds.</summary>
public readonly record struct Shot(Vector3 Origin, Vector3 Direction, float Speed, float Damage, float Spread, RecoilSample Recoil,
    ItemDef Round, GunMods Mods);

/// <summary>
/// The rules of firearms (GROUND.md §6.3), pure functions of a <see cref="GunState"/>, the weapon stack and the shot's
/// context: the server applies them to its state, the shooter's client to its prediction, so with the same inputs both
/// get the same cadence, magazine, recoil and bullet direction, bit for bit.
/// </summary>
public static class FirearmRules
{
    /// <summary>A shot never leaves steeper than this (RustMVP clamps at 85°).</summary>
    public const double MaxShotPitch = 85 * DMath.DegToRad;

    /// <summary>The steepest view an input can carry (NetConvert clamps there).</summary>
    public const float MaxViewPitch = 1.5534f;

    /// <summary>The seed of a shot's random part (the point in its spread cone): its shooter and sequence number.</summary>
    public static ulong ShotSeed(EntityId shooter, uint seq) => Hash64.Of(shooter.Value, seq, 0x5407_5EED);

    /// <summary>
    /// Takes a gun (or nothing) up: a change of slot or item starts afresh (equip time, no recoil, no reload). False when
    /// nothing changed.
    /// </summary>
    public static bool Equip(ref GunState g, byte slot, ushort item, FirearmStats? gun, long tick)
    {
        if (g.Slot == slot && g.Item == item)
        {
            return false;
        }
        uint last = g.LastSeq;
        g = GunState.None;
        g.LastSeq = last;
        g.Slot = slot;
        g.Item = item;
        g.EquipReadyTick = tick + (gun?.EquipTicks ?? 0);
        return true;
    }

    /// <summary>Whether a shot could leave at a tick (RustMVP: equip, reload, cadence, then the magazine).</summary>
    public static OutcomeCode CanFire(in GunState g, in ItemStack weapon, FirearmStats gun, long tick)
    {
        if (tick < g.EquipReadyTick)
        {
            return OutcomeCode.Cooldown;
        }
        if (g.Reloading)
        {
            return OutcomeCode.Reloading;
        }
        if (tick * 16 < g.NextFire16)
        {
            return OutcomeCode.Cadence;
        }
        return weapon.Loaded == 0 ? OutcomeCode.Empty : OutcomeCode.Done;
    }

    /// <summary>
    /// A shot: checks it, then works out where it goes (the view, the recoil before this shot's kick, a spread cone
    /// drawn from the shot's seed), and moves the gun on (cadence, a round out of the magazine, the kick).
    /// </summary>
    public static OutcomeCode Fire(ref GunState g, ref ItemStack weapon, GameContent content, FirearmStats gun, in ShotContext ctx, out Shot shot)
    {
        shot = default;
        OutcomeCode check = CanFire(g, weapon, gun, ctx.Tick);
        if (check != OutcomeCode.Done)
        {
            return check;
        }
        if (!(Math.Abs(ctx.Pitch) <= MaxViewPitch) || !float.IsFinite(ctx.Yaw) ||
            content.Item(weapon.LoadedAmmo) is not { Bullet: { } bullet } round || bullet.Calibre != gun.Calibre)
        {
            return OutcomeCode.Invalid;
        }
        GunMods mods = GunMods.Of(content, weapon, gun);
        var rng = new Rng(ShotSeed(ctx.Shooter, ctx.Seq));
        RecoilSample recoil = Recoil.Sample(g.Recoil, gun, ctx.Tick);
        double spread = Spread(gun, mods, ctx.Aimed, HorizontalSpeed(ctx.Velocity, ctx.Up), ctx.Airborne, ctx.Crouched, recoil.Bloom);
        Vector3 direction = Direction(ctx.Yaw, ctx.Pitch, ctx.Up, recoil, spread, ref rng);
        double speed = gun.Speed * bullet.Speed * mods.Speed;
        double damage = gun.Damage * bullet.Damage * mods.Damage;

        // The cadence: a deadline crossed since the previous tick carries over (no drift); a longer pause restarts it.
        long now16 = ctx.Tick * 16;
        long from = now16 - 16 < g.NextFire16 ? g.NextFire16 : now16;
        g.NextFire16 = from + gun.IntervalSixteenths;
        weapon.Loaded--;
        g.Recoil = Recoil.Kick(g.Recoil, gun, ctx.Tick, mods.Recoil);
        shot = new Shot(ctx.Eye, direction, (float)speed, (float)damage, (float)spread, recoil, round, mods);
        return OutcomeCode.Done;
    }

    /// <summary>
    /// The cone's half-angle in degrees: hip or aimed (narrowed or widened by attachments), wider moving (in proportion to
    /// the speed, up to sprinting), in the air, narrower crouched, plus the bloom left.
    /// </summary>
    public static double Spread(FirearmStats gun, in GunMods mods, bool aimed, float horizontalSpeed, bool airborne, bool crouched, double bloom)
    {
        SpreadStats s = gun.Spread;
        double cone = aimed ? s.Aimed * mods.AimedSpread : s.Hip * mods.HipSpread;
        double moving = Math.Clamp(horizontalSpeed / MovementTuning.Default.SprintSpeed, 0, 1);
        cone *= 1 + moving * (aimed ? s.MovingAimed : s.Moving);
        if (airborne)
        {
            cone *= s.Airborne;
        }
        if (crouched)
        {
            cone *= s.Crouched;
        }
        return cone + bloom;
    }

    public static float HorizontalSpeed(Vector3 velocity, Vector3 up) => (velocity - up * Vector3.Dot(velocity, up)).Length();

    /// <summary>
    /// Where a shot goes: the view, plus the recoil (pitch up, yaw right), plus a point in the spread cone from two draws
    /// of <paramref name="rng"/>, never steeper than 85°.
    /// </summary>
    public static Vector3 Direction(float yaw, float pitch, Vector3 up, in RecoilSample recoil, double spread, ref Rng rng)
    {
        double angle = rng.NextDouble() * DMath.TwoPi;
        double radius = spread * Math.Sqrt(rng.NextDouble());
        double p = pitch + (recoil.Pitch + radius * DMath.Sin(angle)) * DMath.DegToRad;
        double y = yaw - (recoil.Yaw + radius * DMath.Cos(angle)) * DMath.DegToRad;
        p = Math.Clamp(p, -MaxShotPitch, MaxShotPitch);
        return CharacterMotor.ViewDirection((float)y, (float)p, up);
    }

    /// <summary>
    /// Which rounds a reload loads: those asked for if the gun takes them and there are some, else the kind loaded if there
    /// are more, else the first kind of its calibre the player has; 0 when there are none at all.
    /// </summary>
    public static ushort ReloadAmmo(in ItemStack weapon, FirearmStats gun, ushort wanted, Func<ushort, int> count)
    {
        foreach (ItemDef a in gun.Ammo)
        {
            if (a.Id == wanted && count(a.Id) > 0)
            {
                return a.Id;
            }
        }
        if (weapon.LoadedAmmo != 0 && count(weapon.LoadedAmmo) > 0)
        {
            foreach (ItemDef a in gun.Ammo)
            {
                if (a.Id == weapon.LoadedAmmo)
                {
                    return a.Id;
                }
            }
        }
        foreach (ItemDef a in gun.Ammo)
        {
            if (count(a.Id) > 0)
            {
                return a.Id;
            }
        }
        return 0;
    }

    /// <summary>Starts a reload of <paramref name="ammo"/> (RustMVP <c>begin_reload</c>: equip, reloading, full, reserve).</summary>
    public static OutcomeCode BeginReload(ref GunState g, in ItemStack weapon, FirearmStats gun, ushort ammo, int reserve, long tick)
    {
        if (tick < g.EquipReadyTick)
        {
            return OutcomeCode.Cooldown;
        }
        if (g.Reloading)
        {
            return OutcomeCode.Reloading;
        }
        if (ammo == 0 || reserve <= 0)
        {
            return OutcomeCode.NoAmmo;
        }
        bool takes = false;
        foreach (ItemDef a in gun.Ammo)
        {
            takes |= a.Id == ammo;
        }
        if (!takes)
        {
            return OutcomeCode.Invalid;
        }
        if (weapon.LoadedAmmo == ammo && weapon.Loaded >= gun.Magazine)
        {
            return OutcomeCode.Full;
        }
        g.ReloadStartTick = tick;
        g.ReloadEndTick = tick + gun.ReloadTicks;
        g.ReloadAmmo = ammo;
        return OutcomeCode.Done;
    }

    /// <summary>
    /// The end of a reload: rounds of another kind come out first (given back), then the magazine fills from the reserve,
    /// all at once (RustMVP: nothing is spent until the reload completes). Returns how many to take from the reserve,
    /// and what to give back.
    /// </summary>
    public static (int Take, int Returned, ushort ReturnedAmmo) FinishReload(ref GunState g, ref ItemStack weapon, FirearmStats gun, int reserve)
    {
        ushort ammo = g.ReloadAmmo;
        g.ReloadEndTick = -1;
        int returned = 0;
        ushort returnedAmmo = 0;
        if (weapon.LoadedAmmo != ammo && weapon.Loaded > 0)
        {
            returned = weapon.Loaded;
            returnedAmmo = weapon.LoadedAmmo;
            weapon.Loaded = 0;
        }
        int take = Math.Clamp(Math.Min(gun.Magazine - weapon.Loaded, reserve), 0, gun.Magazine);
        if (take > 0 || returned > 0)
        {
            weapon.LoadedAmmo = ammo;
        }
        weapon.Loaded = (ushort)(weapon.Loaded + take);
        return (take, returned, returnedAmmo);
    }
}

/// <summary>
/// A bullet's flight between samples (GROUND.md §6.3): gravity and linear drag (the speed lost a second is a fraction of
/// it), integrated exactly over a step, so the path doesn't depend on how finely it is sampled.
/// </summary>
public static class Ballistics
{
    /// <summary>
    /// One step of <paramref name="seconds"/>: v' = g − k·v, so v(t) = g/k + (v₀ − g/k)·e^(−kt), and the position its
    /// integral. <paramref name="decay"/> is e^(−k·seconds), worked out once per bullet with <see cref="DMath.Exp"/>.
    /// </summary>
    public static (Vector3 Position, Vector3 Velocity) Step(Vector3 position, Vector3 velocity, Vector3 gravity, double drag, double decay, double seconds)
    {
        if (drag <= 1e-9)
        {
            return (position + velocity * (float)seconds + gravity * (float)(0.5 * seconds * seconds), velocity + gravity * (float)seconds);
        }
        Vector3 terminal = gravity / (float)drag;
        Vector3 v = terminal + (velocity - terminal) * (float)decay;
        Vector3 p = position + terminal * (float)seconds + (velocity - terminal) * (float)((1 - decay) / drag);
        return (p, v);
    }

    public static double Decay(double drag, double seconds) => DMath.Exp(-drag * seconds);
}

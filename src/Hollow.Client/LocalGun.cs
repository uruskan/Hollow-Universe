using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Core;
using Hollow.Net.Bits;
using Hollow.Net.Protocol;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Surface;

namespace Hollow.Client;

/// <summary>A shot this client fired, at once (predicted): for the muzzle flash, the sound, the tracer and the kick.</summary>
/// <param name="Seq">The gun action's sequence number (the server's projectile messages name it too).</param>
/// <param name="Origin">The eye, where the server's bullet leaves; draw the tracer from the muzzle towards the same line.</param>
/// <param name="Direction">Exactly the server's direction when the prediction holds (the same rules and seed).</param>
public readonly record struct ShotEvent(uint Seq, ItemDef Weapon, ItemDef Round, Vector3 Origin, Vector3 Direction, float Speed,
    bool Silenced, bool Aimed);

/// <summary>A reload began (predicted, at once) or ended: the gun, the rounds, how long it takes.</summary>
public readonly record struct ReloadEvent(ItemDef Weapon, ItemDef Round, float Seconds);

/// <summary>A shot or reload the server refused: its effect has been rolled back.</summary>
public readonly record struct GunRefusal(uint Seq, GunResult Result);

/// <summary>
/// The gun in the local player's hand (GROUND.md §6.4), predicted with the server's own rules (<see cref="FirearmRules"/>):
/// every tick it decides whether the trigger fires (cadence, magazine, reload, equip time, not sprinting), and if so
/// fires at once (the round out of the magazine, the kick, the event) and sends a fire intent for that tick; reloads
/// likewise. The server's <see cref="GunUpdate"/> brings its state after the last action it applied; the prediction
/// starts again from there and replays the actions not yet applied, as movement does, so on a clean line nothing is
/// ever corrected.
/// <para>
/// <b>The camera and the mouse.</b> Recoil is an offset on the camera, never on the view the player controls (which goes
/// to the server clean; the server adds the recoil itself). Each frame: add <see cref="ViewKick"/> to the camera; pass
/// the mouse's turn to <see cref="ObserveLook"/> and add what <see cref="TakeCompensation"/> returns to the controlled
/// view, so when the recoil recovers, what the player pulled against it is given back (RustMVP's countersteer credit).
/// </para>
/// Call from the thread that calls <see cref="GameClient.Update"/>.
/// </summary>
public sealed class LocalGun
{
    private readonly LocalPlayer _player;
    private readonly List<Pending> _pending = new();
    private GunState _server = GunState.None;
    private ushort _serverLoaded, _serverLoadedAmmo;
    private bool _serverKnown;
    private GunState _state = GunState.None;
    private ushort _loaded, _loadedAmmo;
    private int _taken;
    private uint _nextSeq = 1;
    private uint _refusedSeen;
    private bool _armed = true;
    private bool _wantReload;
    private ushort _wantAmmo;
    private long _tick;
    private double _renderTick;
    private bool _aimed;
    private float _spread;
    private RecoilCompensation _credit = RecoilCompensation.New;
    private (double Pitch, double Yaw) _owed;

    internal LocalGun(LocalPlayer player)
    {
        _player = player;
    }

    // ---------------------------------------------------------------- what the HUD and the views read

    /// <summary>The firearm in hand, or null.</summary>
    public FirearmStats? Weapon => _player.Content.Item(_state.Item)?.Firearm;

    /// <summary>Rounds in the magazine (predicted) and their kind.</summary>
    public int Loaded => Weapon != null ? _loaded : 0;

    public ItemDef? LoadedAmmo => Weapon != null ? _player.Content.Item(_loadedAmmo) : null;

    /// <summary>Rounds of the kind a reload would load, in the inventory (less what a predicted reload took).</summary>
    public int Reserve
    {
        get
        {
            if (Weapon is not { } gun)
            {
                return 0;
            }
            ushort ammo = FirearmRules.ReloadAmmo(WeaponStack(), gun, _loadedAmmo, Count);
            ammo = ammo != 0 ? ammo : _loadedAmmo;
            return Math.Max(0, Count(ammo) - (ammo == _state.ReloadAmmo || ammo == _loadedAmmo ? _taken : 0));
        }
    }

    public bool Reloading => _state.Reloading;

    /// <summary>How far the reload has got (0 … 1) at the render tick; 0 when not reloading.</summary>
    public float ReloadProgress => !_state.Reloading ? 0 :
        (float)Math.Clamp((_renderTick - _state.ReloadStartTick) / Math.Max(1, _state.ReloadEndTick - _state.ReloadStartTick), 0, 1);

    /// <summary>Taken up and ready (its equip time over).</summary>
    public bool Ready => Weapon != null && _tick >= _state.EquipReadyTick;

    /// <summary>Aiming down the sights this tick (the aim button, a gun ready, not reloading).</summary>
    public bool Aimed => _aimed;

    /// <summary>How much the camera magnifies aimed down the sights (the gun's, or its sight's): 2·atan(tan(fov/2)/zoom).</summary>
    public float Zoom => Weapon is { } gun ? (float)GunMods.Of(_player.Content, WeaponStack(), gun).Zoom : 1;

    /// <summary>The attachments now on the gun in hand (silenced, a light), for the view.</summary>
    public GunMods Mods => Weapon is { } gun ? GunMods.Of(_player.Content, WeaponStack(), gun) : default;

    /// <summary>The cone a shot would leave in now (degrees, half-angle), for the crosshair.</summary>
    public float Spread => _spread;

    /// <summary>The recoil's offset of the camera now, in radians: add to the view's yaw and pitch (yaw + is to the left).</summary>
    public (float Yaw, float Pitch) ViewKick
    {
        get
        {
            if (Weapon is not { } gun)
            {
                return default;
            }
            RecoilSample r = Recoil.Sample(_state.Recoil, gun, _renderTick);
            return ((float)(-r.Yaw * DMath.DegToRad), (float)(r.Pitch * DMath.DegToRad));
        }
    }

    /// <summary>Shots and reloads sent and not yet answered.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>The prediction (for tests): the gun state and the magazine, as the next shot would find them.</summary>
    public GunState State => _state;

    // ---------------------------------------------------------------- events

    public event Action<ShotEvent>? ShotFired;

    public event Action<ReloadEvent>? ReloadStarted;

    public event Action<ReloadEvent>? ReloadFinished;

    public event Action<GunRefusal>? Refused;

    // ---------------------------------------------------------------- what the game does

    /// <summary>Reload the gun in hand (R): with <paramref name="ammo"/>'s rounds, or (null) the kind loaded, else any it takes.</summary>
    public void Reload(ItemDef? ammo = null)
    {
        _wantReload = true;
        _wantAmmo = ammo?.Id ?? 0;
    }

    /// <summary>The mouse turned the view this frame by these (radians, yaw + to the left): it earns credit against the recoil.</summary>
    public void ObserveLook(float deltaYaw, float deltaPitch)
    {
        if (Weapon is not { } gun)
        {
            return;
        }
        RecoilSample r = Recoil.Sample(_state.Recoil, gun, _renderTick);
        _credit.Observe(deltaPitch * DMath.RadToDeg, -deltaYaw * DMath.RadToDeg, r.Pitch, r.Yaw);
    }

    /// <summary>What to add to the controlled view this frame (radians) as the recoil the player pulled against recovers.</summary>
    public (float Yaw, float Pitch) TakeCompensation()
    {
        if (Weapon is { } gun)
        {
            (double pitch, double yaw) = _credit.Recover(Recoil.Retained(_state.Recoil, gun, _renderTick));
            _owed.Pitch += pitch;
            _owed.Yaw += yaw;
        }
        (double p, double y) = _owed;
        _owed = default;
        return ((float)(-y * DMath.DegToRad), (float)(p * DMath.DegToRad));
    }

    // ---------------------------------------------------------------- every tick (GameClient)

    /// <summary>
    /// One tick, after movement was predicted for it: take up what is in hand, end a reload that is due, start one asked
    /// for, and fire if the trigger says so. <paramref name="command"/> is the tick's input as sent (its quantised view and
    /// buttons), <paramref name="state"/> the character after the tick.
    /// </summary>
    internal void Tick(uint tick, in InputCommand command, in CharacterState state, ZoneFrame frame, MovementTuning tuning, EntityId me, bool canAct)
    {
        _tick = tick;
        GameContent content = _player.Content;
        byte slot = command.Held < PlayerInventory.BeltSlots ? command.Held : GunState.NoSlot;
        ItemStack stack = slot < PlayerInventory.BeltSlots ? _player.Inventory.Belt[slot] : default;
        FirearmStats? gun = content.Item(stack.Item)?.Firearm;
        if (FirearmRules.Equip(ref _state, slot, gun != null ? stack.Item : (ushort)0, gun, tick))
        {
            Restart(stack);
        }
        _aimed = false;
        if (gun == null || !canAct || state.IsDead || state.Stance == Stance.Crawling)
        {
            _wantReload = false;
            _armed = true;
            return;
        }
        bool aim = (command.Actions & ActionButtons.Secondary) != 0;
        _aimed = aim && !_state.Reloading && tick >= _state.EquipReadyTick;
        Vector3 up = frame.UpAt(state.Position);
        RecoilSample recoil = Recoil.Sample(_state.Recoil, gun, tick);
        _spread = (float)FirearmRules.Spread(gun, GunMods.Of(content, WeaponStack(), gun), aim, FirearmRules.HorizontalSpeed(state.Velocity, up),
            (state.Flags & (MoveFlags.OnGround | MoveFlags.Swimming)) == 0, state.Stance == Stance.Crouching, recoil.Bloom);

        // A reload that ends now: the magazine fills (the rounds are counted as taken until the server says so).
        if (_state.Reloading && tick >= _state.ReloadEndTick)
        {
            ushort round = _state.ReloadAmmo;
            FinishReload(gun);
            if (content.Item(round) is { } loaded)
            {
                ReloadFinished?.Invoke(new ReloadEvent(gun.Item, loaded, (float)gun.ReloadSeconds));
            }
        }

        // A reload asked for: the same checks as the server's, then the intent for this tick.
        if (_wantReload)
        {
            _wantReload = false;
            ItemStack weapon = WeaponStack();
            ushort ammo = FirearmRules.ReloadAmmo(weapon, gun, _wantAmmo, Count);
            int reserve = ammo == 0 ? 0 : Count(ammo) - (ammo == _loadedAmmo ? _taken : 0);
            GunResult result = FirearmRules.BeginReload(ref _state, weapon, gun, ammo, reserve, tick);
            if (result == GunResult.Accepted && Send(GunActionKind.Reload, tick, slot, stack.Item, command, ammo, default))
            {
                ReloadStarted?.Invoke(new ReloadEvent(gun.Item, content.Item(ammo)!, (float)gun.ReloadSeconds));
                _player.Touch();
            }
            else if (result != GunResult.Accepted)
            {
                Refused?.Invoke(new GunRefusal(0, result));
            }
        }

        // The trigger (RustMVP's TriggerState): a press (even one released before this tick) or a hold fires; automatic
        // repeats while held, semi-automatic once a press. A press held early fires as soon as the gun is ready; a tap
        // that came too early is lost.
        bool held = (command.Actions & ActionButtons.Primary) != 0;
        bool pressed = (command.Actions & ActionButtons.PrimaryPressed) != 0;
        if (pressed || !held)
        {
            _armed = true;
        }
        bool trigger = (pressed || held) && _armed;
        if (!trigger || (state.Flags & (MoveFlags.Sprinting | MoveFlags.Swimming)) != 0 ||
            FirearmRules.CanFire(_state, WeaponStack(), gun, tick) != GunResult.Accepted)
        {
            return;
        }
        var context = new ShotContext
        {
            Shooter = me,
            Seq = _nextSeq,
            Tick = tick,
            Yaw = Quantize.FromAngle16(command.Yaw),
            Pitch = Quantize.FromAngle16(command.Pitch),
            Aimed = aim,
            Eye = state.Position + up * tuning.EyeHeight(state.Stance),
            Up = up,
            Velocity = state.Velocity,
            Crouched = state.Stance == Stance.Crouching,
            Airborne = (state.Flags & (MoveFlags.OnGround | MoveFlags.Swimming)) == 0,
        };
        GunState before = _state;
        ItemStack shotWeapon = WeaponStack();
        if (FirearmRules.Fire(ref _state, ref shotWeapon, content, gun, context, out Shot shot) != GunResult.Accepted)
        {
            return;
        }
        if (!Send(GunActionKind.Fire, tick, slot, stack.Item, command, 0, context))
        {
            _state = before;
            return;
        }
        _loaded = shotWeapon.Loaded;
        if (gun.Mode == FireMode.Semi)
        {
            _armed = false;
        }
        _credit.BeginKick();
        _player.Touch();
        ShotFired?.Invoke(new ShotEvent(context.Seq, gun.Item, shot.Round, shot.Origin, shot.Direction, shot.Speed, shot.Mods.Silenced, aim));
    }

    /// <summary>The render tick (fractional) the camera's kick and the reload's progress are sampled at.</summary>
    internal void Render(double tick) => _renderTick = tick;

    // Sends an intent; the action waits as pending until the server says it applied it.
    private bool Send(GunActionKind kind, uint tick, byte slot, ushort item, InputCommand command, ushort ammo, in ShotContext context)
    {
        uint seq = _nextSeq;
        bool sent = kind == GunActionKind.Fire
            ? _player.SendRaw(w => new FireIntent
            {
                Seq = seq, Tick = tick, Slot = slot, Item = item, Yaw = command.Yaw, Pitch = command.Pitch,
                Aimed = (command.Actions & ActionButtons.Secondary) != 0, ViewDelay = command.ViewDelay,
            }.WriteMessage(w))
            : _player.SendRaw(w => new ReloadIntent { Seq = seq, Tick = tick, Slot = slot, Item = item, Ammo = ammo }.WriteMessage(w));
        if (!sent)
        {
            return false;
        }
        _pending.Add(new Pending(seq, kind, tick, ammo, context));
        _nextSeq++;
        return true;
    }

    // ---------------------------------------------------------------- the server's word

    internal void OnUpdate(in GunUpdate m)
    {
        _server = new GunState
        {
            Slot = m.Slot,
            Item = (ushort)Math.Min(m.Item, ushort.MaxValue),
            EquipReadyTick = (long)Math.Min(m.EquipReadyTick, long.MaxValue),
            NextFire16 = (long)Math.Min(m.NextFire16, long.MaxValue),
            ReloadStartTick = (long)Math.Min(m.ReloadStartTick, long.MaxValue),
            ReloadEndTick = m.ReloadEndTick == 0 ? -1 : (long)Math.Min(m.ReloadEndTick, long.MaxValue),
            ReloadAmmo = (ushort)Math.Min(m.ReloadAmmo, ushort.MaxValue),
            LastSeq = m.LastSeq,
            Recoil = new RecoilState
            {
                Pitch = m.RecoilPitch, Yaw = m.RecoilYaw, StartPitch = m.StartPitch, StartYaw = m.StartYaw, Bloom = m.Bloom,
                ShotTick = m.ShotTick == 0 ? -1 : (long)Math.Min(m.ShotTick, long.MaxValue), Index = m.RecoilIndex,
            },
        };
        _serverLoaded = (ushort)Math.Min(m.Loaded, ushort.MaxValue);
        _serverLoadedAmmo = (ushort)Math.Min(m.LoadedAmmo, ushort.MaxValue);
        _serverKnown = true;
        uint applied = m.LastSeq;
        _pending.RemoveAll(p => p.Seq <= applied);
        if (m.Refused != GunResult.Accepted && m.RefusedSeq > _refusedSeen)
        {
            _refusedSeen = m.RefusedSeq;
            Refused?.Invoke(new GunRefusal(m.RefusedSeq, m.Refused));
        }
        // The server's gun is the one in hand (or it caught up with the equip): start again from its state.
        if (_server.Slot == _state.Slot && _server.Item == _state.Item)
        {
            Replay();
        }
        _player.Touch();
    }

    // The server's state, then every action it hasn't applied yet, as predicted (the same rules: the same results).
    private void Replay()
    {
        FirearmStats? gun = _player.Content.Item(_state.Item)?.Firearm;
        uint next = _state.LastSeq;
        _state = _server;
        _state.LastSeq = Math.Max(next, _server.LastSeq);
        _loaded = _serverLoaded;
        _loadedAmmo = _serverLoadedAmmo;
        _taken = 0;
        if (gun == null)
        {
            return;
        }
        foreach (Pending p in _pending)
        {
            if (_state.Reloading && p.Tick >= _state.ReloadEndTick)
            {
                FinishReload(gun);
            }
            ItemStack weapon = WeaponStack();
            if (p.Kind == GunActionKind.Fire)
            {
                if (FirearmRules.Fire(ref _state, ref weapon, _player.Content, gun, p.Context, out _) == GunResult.Accepted)
                {
                    _loaded = weapon.Loaded;
                }
            }
            else
            {
                int reserve = Count(p.Ammo) - (p.Ammo == _loadedAmmo ? _taken : 0);
                FirearmRules.BeginReload(ref _state, weapon, gun, p.Ammo, reserve, p.Tick);
            }
        }
        if (_state.Reloading && _tick >= _state.ReloadEndTick)
        {
            FinishReload(gun);
        }
        (double pitch, double yaw) = _credit.Rebase(Recoil.Retained(_state.Recoil, gun, _renderTick));
        _owed.Pitch += pitch;
        _owed.Yaw += yaw;
    }

    private void FinishReload(FirearmStats gun)
    {
        ItemStack weapon = WeaponStack();
        ushort ammo = _state.ReloadAmmo;
        int reserve = Math.Max(0, Count(ammo) - _taken);
        (int take, _, _) = FirearmRules.FinishReload(ref _state, ref weapon, gun, reserve);
        _taken += take;
        _loaded = weapon.Loaded;
        _loadedAmmo = weapon.LoadedAmmo;
    }

    // Another gun (or none) in hand: what is known of it is its stack; the recoil credit is given back.
    private void Restart(in ItemStack stack)
    {
        _pending.Clear();
        _loaded = stack.Loaded;
        _loadedAmmo = stack.LoadedAmmo;
        _taken = 0;
        _armed = true;
        (double pitch, double yaw) = _credit.Clear();
        _owed.Pitch += pitch;
        _owed.Yaw += yaw;
        if (_serverKnown && _server.Slot == _state.Slot && _server.Item == _state.Item && _server.Item != 0)
        {
            Replay();
        }
    }

    /// <summary>A new life, or none: nothing in hand, nothing pending.</summary>
    internal void Reset()
    {
        uint last = _state.LastSeq;
        _state = GunState.None;
        _state.LastSeq = last;
        Restart(default);
    }

    /// <summary>The weapon stack as predicted: its attachments from the inventory, its magazine from the prediction.</summary>
    private ItemStack WeaponStack()
    {
        ItemStack stack = _state.Slot < PlayerInventory.BeltSlots ? _player.Inventory.Belt[_state.Slot] : default;
        stack.Loaded = _loaded;
        stack.LoadedAmmo = _loadedAmmo;
        return stack;
    }

    /// <summary>Shows the predicted magazine on the gun's belt slot (after the inventory was rebuilt from the server's).</summary>
    internal void ApplyTo(Container belt)
    {
        if (_state.Item == 0 || _state.Slot >= belt.Size || belt[_state.Slot].Item != _state.Item)
        {
            return;
        }
        ItemStack stack = belt[_state.Slot];
        stack.Loaded = _loaded;
        stack.LoadedAmmo = _loadedAmmo;
        belt.Set(_state.Slot, stack);
    }

    private int Count(ushort item) => item == 0 ? 0 : InventoryRules.Count(_player.Inventory.Carried, item);

    private sealed record Pending(uint Seq, GunActionKind Kind, long Tick, ushort Ammo, ShotContext Context);
}

/// <summary>
/// RustMVP's countersteer credit (recoil_compensation.hpp), owner input accounting only: mouse movement against the
/// recoil still showing earns credit (at most that recoil, per axis); as the recoil recovers, the credit is given back to
/// the view the player controls in proportion, so they don't get a second pull. Degrees, pitch up, yaw right.
/// </summary>
public struct RecoilCompensation
{
    private double _pitch, _yaw, _fraction;

    public static RecoilCompensation New => new() { _fraction = 1 };

    /// <summary>The mouse moved by these while the recoil showed (<paramref name="rawPitch"/>, <paramref name="rawYaw"/>).</summary>
    public void Observe(double deltaPitch, double deltaYaw, double rawPitch, double rawYaw)
    {
        Axis(deltaPitch, rawPitch, ref _pitch);
        Axis(deltaYaw, rawYaw, ref _yaw);
    }

    /// <summary>A shot: a new kick starts from the whole aggregate.</summary>
    public void BeginKick() => _fraction = 1;

    /// <summary>The recoil has <paramref name="retained"/> of itself left (1 … 0): the credit given back since last asked.</summary>
    public (double Pitch, double Yaw) Recover(double retained)
    {
        if (!double.IsFinite(retained))
        {
            return default;
        }
        retained = Math.Clamp(retained, 0, 1);
        if (retained >= _fraction)
        {
            return default;
        }
        double ratio = _fraction > 0 ? retained / _fraction : 0;
        double pitch = _pitch * ratio, yaw = _yaw * ratio;
        (double, double) released = (_pitch - pitch, _yaw - yaw);
        _pitch = pitch;
        _yaw = yaw;
        _fraction = retained;
        return released;
    }

    /// <summary>
    /// The prediction started again: keep only the credit still outstanding (never replaying mouse input or credit given
    /// back already); when nothing of the recoil is left, all of it comes back.
    /// </summary>
    public (double Pitch, double Yaw) Rebase(double retained)
    {
        if (!double.IsFinite(retained))
        {
            return default;
        }
        retained = Math.Clamp(retained, 0, 1);
        if (retained == 0)
        {
            (double, double) released = Clear();
            _fraction = 0;
            return released;
        }
        _fraction = retained;
        return default;
    }

    /// <summary>A new gun or life: all of the credit back.</summary>
    public (double Pitch, double Yaw) Clear()
    {
        (double, double) released = (_pitch, _yaw);
        _pitch = _yaw = 0;
        _fraction = 1;
        return released;
    }

    private static void Axis(double input, double raw, ref double credit)
    {
        if (!double.IsFinite(input) || !double.IsFinite(raw))
        {
            return;
        }
        double remaining = raw - credit;
        if (!((remaining > 0 && input < 0) || (remaining < 0 && input > 0)))
        {
            return;
        }
        credit = Math.Abs(input) >= Math.Abs(remaining) ? raw : credit + Math.CopySign(Math.Abs(input), remaining);
    }
}

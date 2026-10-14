using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;

namespace Hollow.Bots;

/// <summary>
/// A bot that fights with guns (GROUND.md §6.3, the G4 load test) through the same client API as a player: it keeps its
/// rifle and pistol on the belt and the holo sight on the rifle, wears its armour, picks the nearest player or bot in
/// range and shoots at them in bursts (aimed beyond a dozen metres), with an aim that wanders by a degree or two and
/// pulls only part of the recoil back, so it misses as players do; it strafes, reloads when empty, switches to the pistol
/// when the rifle is dry, bandages, now and then helps a downed bot up or finishes one off, crawls when down and respawns
/// when dead. Deterministic per bot (seeded by its number); stays within <see cref="BotBrain.HomeRadius"/> of the origin
/// so the crowd sees itself (the bandwidth worst case).
/// </summary>
public sealed class FightBrain
{
    private const float Range = 60f;

    private Rng _rng;
    private double _nextPlan;
    private double _nextChore;
    private double _burstUntil;
    private double _restUntil;
    private double _strafeUntil;
    private double _nextReload;
    private float _strafe;
    private uint _target;
    private bool _assist;
    private float _yaw;
    private float _pitch;
    private Vector2 _aimError;
    private readonly float _skill;

    public FightBrain(int index)
    {
        _rng = new Rng(Hash64.Of((ulong)index, 0xF16B7));
        _yaw = (float)_rng.Range(-Math.PI, Math.PI);
        _nextPlan = _rng.Range(0, 1);
        _nextChore = _rng.Range(0, 2);
        _skill = (float)_rng.Range(0.5, 0.9);
    }

    /// <summary>What the bot did, for the load test's report: bursts begun, reloads asked for, downed players helped up.</summary>
    public int Bursts { get; private set; }

    public int Reloads { get; private set; }

    public int Assists { get; private set; }

    public InputFrame Think(double time, double delta, GameClient client)
    {
        LocalPlayer me = client.Local;
        var input = new InputFrame { Yaw = _yaw, Pitch = _pitch };
        if (me.IsDead)
        {
            if (time >= _nextChore)
            {
                _nextChore = time + 2;
                me.Respawn();
            }
            return input;
        }
        if (me.IsWounded)
        {
            // Down: crawl away from where it was hit, slowly.
            input.Forward = 1;
            return input;
        }
        if (time >= _nextChore && me.PendingCount == 0)
        {
            _nextChore = time + _rng.Range(1.5, 3);
            Chores(client, me);
        }
        Vector3 feet = client.Character.Position;
        Vector3 up = client.Frame.UpAt(feet);
        Vector3 eye = feet + up * client.EyeHeight;
        if (time >= _nextPlan)
        {
            _nextPlan = time + _rng.Range(0.8, 1.6);
            Plan(client, feet);
        }
        RemoteEntity? target = _target == 0 ? null : client.Entities.FirstOrDefault(e => e.NetId == _target);
        if (target == null || target.Sleeping || (target.Flags & MoveFlags.Dead) != 0)
        {
            _target = 0;
            Wander(ref input, feet, delta);
            return Remember(input);
        }
        float distance = Vector3.Distance(target.Position, feet);
        if (_assist || target.Wounded)
        {
            // A downed one: walk over and help it up (or, now and then, finish it off from where it stands).
            if (_assist)
            {
                if (Steer(ref input, feet, target.Position, 1.6f, delta) && me.HeldSlot >= 0)
                {
                    Interacting.Tap(client, Interacting.Character(target.Id));
                    Assists++;
                    _assist = false;
                    _target = 0;
                }
                return Remember(input);
            }
        }
        LocalGun gun = me.Gun;
        HoldBestGun(client, me);
        if (gun.Weapon != null && gun.Loaded == 0 && !gun.Reloading && gun.Reserve > 0 && time >= _nextReload)
        {
            _nextReload = time + 0.5;
            me.Reload();
            Reloads++;
        }

        // Aim at the chest, off by a wandering error, pulling back only part of the recoil the camera shows.
        Vector3 chest = target.Position + up * (target.Wounded ? 0.3f : target.Stance == Stance.Crouching ? 0.8f : 1.2f);
        _aimError += new Vector2((float)_rng.Range(-1, 1), (float)_rng.Range(-1, 1)) * (float)delta * 0.05f;
        _aimError *= 1 - (float)delta * 0.5f;
        Look(ref input, eye, chest, up);
        (float kickYaw, float kickPitch) = gun.ViewKick;
        input.Yaw += _aimError.X - kickYaw * _skill;
        input.Pitch += _aimError.Y - kickPitch * _skill;

        // Strafe; close in from far, back off when close.
        if (time >= _strafeUntil)
        {
            _strafeUntil = time + _rng.Range(0.8, 2);
            _strafe = (float)_rng.Range(-1, 1);
        }
        input.Right = _strafe;
        input.Forward = distance > 40 ? 1 : distance < 6 ? -1 : 0;
        input.Secondary = distance > 12 && gun.Weapon != null;

        // Bursts: the rifle held for a moment then let go; the pistol tapped.
        bool rifle = gun.Weapon?.Mode == FireMode.Auto;
        if (time >= _restUntil && time >= _burstUntil)
        {
            _burstUntil = time + (rifle ? _rng.Range(0.3, 0.9) : 0.05);
            _restUntil = _burstUntil + (rifle ? _rng.Range(0.3, 0.9) : _rng.Range(0.2, 0.4));
            Bursts++;
            input.PrimaryPressed = true;
        }
        input.Primary = time < _burstUntil && gun.Ready && !gun.Reloading;
        return Remember(input);
    }

    private InputFrame Remember(in InputFrame input)
    {
        _yaw = input.Yaw;
        _pitch = Math.Clamp(input.Pitch, -1.4f, 1.4f);
        return input with { Pitch = _pitch };
    }

    // Who to fight: the nearest awake player in range; now and then a downed one to help up instead.
    private void Plan(GameClient client, Vector3 feet)
    {
        List<RemoteEntity> near = client.Entities
            .Where(e => !e.Sleeping && (e.Flags & MoveFlags.Dead) == 0 && Vector3.Distance(e.Position, feet) < Range)
            .OrderBy(e => Vector3.Distance(e.Position, feet)).ToList();
        RemoteEntity? downed = near.FirstOrDefault(e => e.Wounded);
        if (downed != null && _rng.Chance(0.3))
        {
            _target = downed.NetId;
            _assist = true;
            return;
        }
        _assist = false;
        RemoteEntity? enemy = near.FirstOrDefault(e => !e.Wounded) ?? (_rng.Chance(0.5) ? downed : null);
        _target = enemy?.NetId ?? 0;
    }

    // Guns on the belt (the rifle in slot 0, the pistol in 1), the holo sight on the rifle, armour on, bandages.
    private void Chores(GameClient client, LocalPlayer me)
    {
        GameContent c = client.Content;
        for (int i = 0; i < PlayerInventory.MainSlots; i++)
        {
            ItemStack stack = me.Inventory.Main[i];
            ItemDef? item = c.Item(stack.Item);
            if (item?.Wear != null && Enumerable.Range(0, PlayerInventory.WearSlots)
                    .Any(w => me.Inventory.Wear[w].IsEmpty && me.Inventory.Wear.Accepts(c, w, stack)))
            {
                me.QuickMove(ContainerRef.Main, i);
                return;
            }
            if (item?.Firearm != null)
            {
                int slot = item.Firearm.Mode == FireMode.Auto ? 0 : 1;
                if (me.Inventory.Belt[slot].Item != item.Id)
                {
                    me.MoveItem(ContainerRef.Main, i, ContainerRef.Belt, slot);
                    return;
                }
            }
            if (item?.Attachment is { Slot: AttachmentSlot.Sight } && me.Inventory.Belt[0].Item == c.ItemByKey("rifle").Id && me.Inventory.Belt[0].Sight == 0)
            {
                me.Attach(ContainerRef.Main, i, ContainerRef.Belt, 0);
                return;
            }
        }
        if (me.Vitals.IsBleeding && Find(me, c.ItemByKey("bandage").Id) is { } bandage)
        {
            me.UsePart(bandage.Which, bandage.Slot, ItemPart.Consumable);
            return;
        }
        if (me.Vitals.Health < 40 && Find(me, c.ItemByKey("medical-injector").Id) is { } injector)
        {
            me.UsePart(injector.Which, injector.Slot, ItemPart.Consumable);
            return;
        }
        if (me.Vitals.Calories < 150 && Find(me, c.ItemByKey("roasted-tuber").Id) is { } food)
        {
            me.UsePart(food.Which, food.Slot, ItemPart.Consumable);
        }
    }

    // The rifle while it has rounds (loaded or in reserve), else the pistol.
    private static void HoldBestGun(GameClient client, LocalPlayer me)
    {
        GameContent c = client.Content;
        int slot = -1;
        for (int i = 0; i < 2; i++)
        {
            if (c.Item(me.Inventory.Belt[i].Item)?.Firearm is { } f && (me.Inventory.Belt[i].Loaded > 0 ||
                f.Ammo.Any(a => me.Inventory.Count(a) > 0)))
            {
                slot = i;
                break;
            }
        }
        if (slot >= 0 && me.HeldSlot != slot && !me.Gun.Reloading)
        {
            me.HeldSlot = slot;
        }
    }

    private static (ContainerRef Which, int Slot)? Find(LocalPlayer me, ushort id)
    {
        foreach (ContainerRef which in new[] { ContainerRef.Belt, ContainerRef.Main })
        {
            Container container = me.Resolve(which)!;
            for (int i = 0; i < container.Size; i++)
            {
                if (container[i].Item == id)
                {
                    return (which, i);
                }
            }
        }
        return null;
    }

    private bool Steer(ref InputFrame input, Vector3 feet, Vector3 to, float reach, double delta)
    {
        Vector3 flat = BotGround.Flat(to - feet, feet);
        if (flat.Length() <= reach)
        {
            return true;
        }
        float wanted = BotGround.YawTo(feet, to);
        input.Yaw = _yaw + Math.Clamp(Wrap(wanted - _yaw), -(float)delta * 4, (float)delta * 4);
        input.Pitch = 0;
        input.Forward = 1;
        input.Sprint = flat.Length() > 8;
        return false;
    }

    private static void Look(ref InputFrame input, Vector3 eye, Vector3 at, Vector3 up)
    {
        (input.Yaw, input.Pitch) = BotGround.LookAt(eye, at);
    }

    private void Wander(ref InputFrame input, Vector3 feet, double delta)
    {
        float wanted = Vector3.Distance(feet, BotGround.Home) > BotBrain.HomeRadius * 0.8f ? BotGround.YawTo(feet, BotGround.Home) : _yaw + (float)_rng.Range(-0.3, 0.3);
        input.Yaw = _yaw + Math.Clamp(Wrap(wanted - _yaw), -(float)delta * 2, (float)delta * 2);
        input.Pitch = 0;
        input.Forward = 1;
    }

    private static float Wrap(float a)
    {
        while (a > MathF.PI)
        {
            a -= 2 * MathF.PI;
        }
        while (a < -MathF.PI)
        {
            a += 2 * MathF.PI;
        }
        return a;
    }
}

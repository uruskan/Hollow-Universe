using System;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Core;
using Hollow.Simulation.Content;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Raid;

namespace Hollow.Bots;

/// <summary>
/// A bot that raids (GROUND.md §5.4, G5) through the same client API as a player: its kit (charges, bag charges,
/// grenades, a rocket launcher with rockets, a rifle with explosive rounds) arrives with every life; it runs to the hut of
/// the next one on the server's ring (<c>--raid-bases</c>; its own is hut <c>index mod huts</c>) and works through it: a timed charge placed on the door, then
/// stepping back out of the blast; bag charges and grenades thrown at the doorway and the walls; rockets from fifteen
/// metres; bursts of explosive rounds. Its own hut is raided by the bot before it, and it hears the raid alerts. The huts
/// face the zone's origin, so it raids from the inside of the ring; it never knows what is still standing (no building
/// replication yet), it just goes through its plan. Deterministic per bot.
/// </summary>
public sealed class RaidBrain
{
    // Belt slots for the kit.
    private const int ChargeSlot = 0, BagSlot = 1, GrenadeSlot = 2, LauncherSlot = 3, RifleSlot = 4;

    private readonly int _index;
    private readonly int _huts;
    private readonly float _radius;
    private Rng _rng;
    private int _step;
    private double _nextChore;
    private double _nextAction;
    private double _burstUntil;
    private double _nextReload;
    private bool _arrived;
    private float _yaw;
    private float _pitch;

    public RaidBrain(int index, int huts, float radius)
    {
        _index = index;
        _huts = Math.Max(1, huts);
        _radius = radius;
        _rng = new Rng(Hash64.Of((ulong)index, 0x2A1D));
        _step = _rng.NextInt(5);
        _nextAction = _rng.Range(0, 2);
    }

    /// <summary>What the bot did: charges placed, things thrown, rockets fired, bursts of explosive rounds, alerts heard.</summary>
    public int Placed { get; private set; }

    public int Thrown { get; private set; }

    public int Rockets { get; private set; }

    public int Bursts { get; private set; }

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
            input.Forward = 1;
            return input;
        }
        if (time >= _nextChore && me.PendingCount == 0)
        {
            _nextChore = time + _rng.Range(1, 2);
            Chores(client.Content, me);
        }

        // The target: the next bot's hut on the ring; its doorway faces the origin.
        (float hx, float hz, double _) = TestBases.Ring((_index + 1) % _huts, _huts, _radius);
        Vector3 feet = client.Character.Position;
        Vector3 up = client.Frame.UpAt(feet);
        Vector3 eye = feet + up * client.EyeHeight;
        var centre = new Vector3(hx, Ground(client, hx, hz), hz);
        Vector3 outward = Vector3.Normalize(new Vector3(-hx, 0, -hz));
        Vector3 right = Vector3.Cross(up, outward);
        // The plan: (stand off how far, aim at which face, what).
        (float stand, Vector3 aim, int slot) = (_step % 5) switch
        {
            // The door, or the doorway beside it every other time (a broken door leaves nothing to stick to).
            0 => (1.6f, centre + outward * 1.75f + right * (Placed % 2 == 0 ? 0 : 0.95f) + up * 1.2f, ChargeSlot),
            1 => (5f, centre + outward * 1.75f + up * 0.6f, BagSlot),
            2 => (6f, centre + outward * 1.75f + right * 1.1f + up * 1.0f, GrenadeSlot),
            3 => (15f, centre + outward * 1.7f + right * 1.1f + up * 1.6f, LauncherSlot),
            _ => (10f, centre + outward * 1.7f - right * 1.1f + up * 1.5f, RifleSlot),
        };
        Vector3 spot = centre + outward * (1.7f + stand);
        if (!Steer(ref input, feet, spot, 0.8f, delta))
        {
            _arrived = false;
            return Remember(input);
        }
        Look(ref input, eye, aim, up);
        if (!_arrived)
        {
            // Turned to the target: a moment for the server to see the new view before acting on it (it takes a throw's
            // or a charge's view from the intent only if it is near the view it has for the player).
            _arrived = true;
            _nextAction = Math.Max(_nextAction, time + 0.4);
        }
        if (time < _nextAction)
        {
            return Remember(input);
        }
        if (me.Inventory.Belt[slot].IsEmpty)
        {
            _step++;
            _nextAction = time + 0.2;
            return Remember(input);
        }
        switch (slot)
        {
            case ChargeSlot:
                me.HeldSlot = ChargeSlot;
                if (me.PlaceCharge(ChargeSlot, input.Yaw, input.Pitch) != 0)
                {
                    Placed++;
                }
                // Then out of its blast and wait for it.
                _step++;
                _nextAction = time + 11;
                break;
            case BagSlot:
            case GrenadeSlot:
                me.HeldSlot = slot;
                if (me.Throw(slot, input.Yaw, input.Pitch) != 0)
                {
                    Thrown++;
                }
                _step++;
                _nextAction = time + _rng.Range(1.5, 3);
                break;
            case LauncherSlot:
            {
                me.HeldSlot = LauncherSlot;
                LocalGun gun = me.Gun;
                if (gun.Weapon != null && gun.Loaded == 0 && !gun.Reloading && gun.Reserve > 0 && time >= _nextReload)
                {
                    _nextReload = time + 1;
                    me.Reload();
                }
                else if (gun.Ready && gun.Loaded > 0 && !gun.Reloading)
                {
                    input.Primary = true;
                    input.PrimaryPressed = true;
                    Rockets++;
                    _nextAction = time + 1.5;
                    if (_rng.Chance(0.4))
                    {
                        _step++;
                    }
                }
                else if (gun.Weapon != null && gun.Loaded == 0 && gun.Reserve == 0 && !gun.Reloading)
                {
                    _step++;
                }
                break;
            }
            default:
            {
                me.HeldSlot = RifleSlot;
                LocalGun gun = me.Gun;
                ItemDef explosive = client.Content.ItemByKey("rifle-ammo-explosive");
                if (gun.Weapon != null && (gun.Loaded == 0 || gun.LoadedAmmo != explosive) && !gun.Reloading && me.Inventory.Count(explosive) > 0 &&
                    time >= _nextReload)
                {
                    _nextReload = time + 3;
                    me.Reload(explosive);
                }
                else if (gun.Ready && gun.Loaded > 0 && !gun.Reloading)
                {
                    if (time >= _burstUntil)
                    {
                        _burstUntil = time + _rng.Range(0.4, 0.9);
                        Bursts++;
                        input.PrimaryPressed = true;
                    }
                    input.Primary = time < _burstUntil;
                    input.Secondary = true;
                    if (_rng.Chance(delta * 0.15))
                    {
                        _step++;
                    }
                }
                else if (me.Inventory.Count(explosive) == 0 && gun.Loaded == 0 && !gun.Reloading)
                {
                    _step++;
                }
                break;
            }
        }
        return Remember(input);
    }

    private InputFrame Remember(in InputFrame input)
    {
        _yaw = input.Yaw;
        _pitch = Math.Clamp(input.Pitch, -1.4f, 1.4f);
        return input with { Pitch = _pitch };
    }

    // The kit onto the belt: charges, bag charges, grenades, the launcher, the rifle.
    private static void Chores(GameContent c, LocalPlayer me)
    {
        string[] belt = { "timed-charge", "bag-charge", "tin-grenade", "rocket-launcher", "rifle" };
        for (int slot = 0; slot < belt.Length; slot++)
        {
            ushort id = c.ItemByKey(belt[slot]).Id;
            if (me.Inventory.Belt[slot].Item == id)
            {
                continue;
            }
            int from = Enumerable.Range(0, PlayerInventory.MainSlots).FirstOrDefault(i => me.Inventory.Main[i].Item == id, -1);
            if (from >= 0)
            {
                me.MoveItem(ContainerRef.Main, from, ContainerRef.Belt, slot);
                return;
            }
        }
    }

    // The ground's height under a point (the client's own surface).
    private static float Ground(GameClient client, float x, float z)
    {
        Vector3 up = client.Frame.UpAt(new Vector3(x, 0, z));
        Vector3? hit = client.AimGround(new Vector3(x, 0, z) + up * 60, -up, 120);
        return hit?.Y ?? client.Character.Position.Y;
    }

    private bool Steer(ref InputFrame input, Vector3 feet, Vector3 to, float reach, double delta)
    {
        Vector3 d = to - feet;
        var flat = new Vector2(d.X, d.Z);
        if (flat.Length() <= reach)
        {
            return true;
        }
        float wanted = MathF.Atan2(-flat.X, -flat.Y);
        input.Yaw = _yaw + Math.Clamp(Wrap(wanted - _yaw), -(float)delta * 5, (float)delta * 5);
        input.Pitch = 0;
        input.Forward = 1;
        input.Sprint = flat.Length() > 6;
        return false;
    }

    private static void Look(ref InputFrame input, Vector3 eye, Vector3 at, Vector3 up)
    {
        Vector3 d = at - eye;
        Vector3 flat = d - up * Vector3.Dot(d, up);
        input.Yaw = MathF.Atan2(-flat.X, -flat.Z);
        input.Pitch = MathF.Atan2(Vector3.Dot(d, up), flat.Length());
        input.Forward = 0;
        input.Sprint = false;
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

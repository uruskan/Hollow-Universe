using System;
using System.Linq;
using System.Numerics;
using Hollow.Client;
using Hollow.Core;
using Hollow.Server;
using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Raid;

namespace Hollow.Bots;

/// <summary>
/// A bot that raids (GROUND.md §5.5, G5) through the same client API as a player: its kit (charges, bag charges,
/// grenades, a rocket launcher with rockets, a rifle with explosive rounds) arrives with every life; it runs to the hut of
/// the next one (<c>--raid-bases</c>; its own is hut <c>index mod huts</c>) and works through it: a timed charge placed on
/// the door, then stepping back out of the blast; bag charges and grenades thrown at the doorway and the walls; rockets of
/// every kind it carries in turn from fifteen metres (the incendiary rocket's fire burning at the hut); bursts of explosive
/// rounds. Its own hut is raided by the bot before it, and it hears the raid
/// alerts. With the crowd at the landing site the huts stand on the server's ring round it, facing it, so it raids from
/// the inside of the ring; a bot that arrived on a beach anywhere on the world finds the hut it raids beside that beach,
/// facing it. It doesn't look at what is still standing, it just goes through its plan. Deterministic per bot.
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
    private Piece? _hut;

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

        Vector3 feet = client.Character.Position;
        Vector3 up = client.Frame.UpAt(feet);
        Vector3 eye = feet + up * client.EyeHeight;
        if (Target(client) is not { } target)
        {
            // Not in view yet (streaming in, or far away after a respawn on another beach): back to where it arrived.
            _arrived = false;
            Steer(ref input, feet, BotGround.FirstArrival(client), 2f, delta);
            return Remember(input);
        }
        (Vector3 centre, Vector3 outward) = target;
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
                // On the surface the view meets, as the ghost shows it (GROUND_SYSTEMS.md §6).
                if (me.Inventory.Belt[ChargeSlot] is { IsEmpty: false } charge && client.Content.Item(charge.Item)?.Places is { } placeable &&
                    client.Placement().Plan(placeable, eye, CharacterMotor.ViewDirection(input.Yaw, input.Pitch, up), input.Yaw) is { Valid: true } ghost &&
                    me.Place(ghost, ContainerRef.Belt, ChargeSlot) != 0)
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
                // Every kind of rocket it carries in turn (IW4: the incendiary rocket's fires burn at the huts too).
                ItemDef[] kinds = gun.Weapon?.Ammo.Where(k => me.Inventory.Count(k) > 0).ToArray() ?? Array.Empty<ItemDef>();
                if (gun.Weapon != null && gun.Loaded == 0 && !gun.Reloading && kinds.Length > 0 && time >= _nextReload)
                {
                    _nextReload = time + 1;
                    me.Reload(kinds[Rockets % kinds.Length]);
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
                else if (gun.Weapon != null && gun.Loaded == 0 && kinds.Length == 0 && !gun.Reloading)
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

    // The hut it raids: its middle on the ground and the way its doorway faces. With the crowd at the landing site, the
    // next bot's hut on the server's ring round it (bot n raids hut n mod huts), its doorway facing the landing site.
    // Arrived on a beach of its own, the hut the server put beside that beach (RaidArena), its doorway facing the beach,
    // as the bot's copy of the building shows it; null until it does.
    private (Vector3 Centre, Vector3 Outward)? Target(GameClient client)
    {
        GroundProbe ground = client.Placement().Ground!.Value;
        if (BotGround.ArrivedAtLanding(client))
        {
            (Vector3 at, double _) = TestBases.Ring(ground, BotGround.Landing, (_index + 1) % _huts, _huts, _radius);
            return (at, Vector3.Normalize(BotGround.Flat(BotGround.Landing - at, at)));
        }
        if (_hut is not { Gone: false })
        {
            Vector3 beach = BotGround.FirstArrival(client);
            _hut = client.Building().World?.AllPieces
                .Where(p => !p.Gone && p.Def.Family == PieceFamily.Foundation && Vector3.Distance(p.ZoneTransform.Position, beach) < 2 * RaidArena.BesideBeach)
                .MinBy(p => Vector3.DistanceSquared(p.ZoneTransform.Position, beach));
        }
        if (_hut == null)
        {
            return null;
        }
        // The doorway is on the foundation's +Z edge (TestBases.Hut).
        (Vector3 position, Quaternion rotation) = _hut.ZoneTransform;
        return (ground.Settle(position), Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, rotation)));
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

    private bool Steer(ref InputFrame input, Vector3 feet, Vector3 to, float reach, double delta)
    {
        Vector3 flat = BotGround.Flat(to - feet, feet);
        if (flat.Length() <= reach)
        {
            return true;
        }
        float wanted = BotGround.YawTo(feet, to);
        input.Yaw = _yaw + Math.Clamp(Wrap(wanted - _yaw), -(float)delta * 5, (float)delta * 5);
        input.Pitch = 0;
        input.Forward = 1;
        input.Sprint = flat.Length() > 6;
        return false;
    }

    private static void Look(ref InputFrame input, Vector3 eye, Vector3 at, Vector3 up)
    {
        (input.Yaw, input.Pitch) = BotGround.LookAt(eye, at);
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

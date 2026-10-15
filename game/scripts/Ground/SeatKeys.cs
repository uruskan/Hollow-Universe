using System.Collections.Generic;
using System.Text;
using Godot;
using Hollow.Client;
using Hollow.Simulation.Ships;
using HollowUniverse.UI.Model;

namespace HollowUniverse.Ground;

/// <summary>What a seat's key does (IRON_WAKE.md §5.2): leave it, work a door, the lock, descend from orbit, the camera.</summary>
public enum SeatAction : byte
{
    Leave,
    Door,
    Lock,
    Descend,
    Camera,

    /// <summary>A station's rockets (IW3): tap R to cycle, hold for the ammo radial (the interface's binding).</summary>
    Ammo,

    /// <summary>A station's magazine (Tab, IW3): it opens beside the inventory.</summary>
    Magazine,
}

/// <summary>A seat's key: the key, what it does (a door: which), and what the screen calls it.</summary>
public readonly record struct SeatKey(Key Key, SeatAction Action, int Door, string Label);

/// <summary>
/// The keys in one place (KNOWN_PROBLEMS.md finding 28): the context the player's keys are in (on foot, or seated by role),
/// worked out here alone; each seat's keys from its data (Space leaves any seat; 1–9 each door a seat that works doors
/// works, with L its lock; the helm's J descends from orbit and V the camera); the helm's flight keys, which
/// <c>ShipControls</c> reads (W S the lever, A D slide, Shift Ctrl up and down, Q E roll, B boost, Z flight assist, X the
/// lever to zero); and the lines the screen shows them on.
/// </summary>
public static class SeatKeys
{
    public const Key Rise = Key.Shift, Sink = Key.Ctrl, Boost = Key.B, Assist = Key.Z, ZeroThrottle = Key.X;

    /// <summary>The context the player's keys are in now: the seat's role, as the server says the player sits.</summary>
    public static KeyContext Context(GameClient client)
    {
        if (!client.HasCharacter || client.Ships().Seat is not { } seat)
        {
            return KeyContext.OnFoot;
        }
        return seat.Role switch
        {
            SeatRole.Helm => client.Ships().Piloting ? KeyContext.Helm : KeyContext.Seat,
            SeatRole.Station => KeyContext.Station,
            _ => KeyContext.Seat,
        };
    }

    /// <summary>A seat's keys, from its data: Space leaves; the doors it works (1–9) and the lock; the helm's descent and camera.</summary>
    public static IReadOnlyList<SeatKey> Of(ShipDef def, ShipSeatDef seat)
    {
        var keys = new List<SeatKey> { new(Key.Space, SeatAction.Leave, -1, "LEAVE") };
        if ((seat.Works & SeatWorks.Doors) != 0)
        {
            for (int door = 0; door < def.Doors.Count && door < 9; door++)
            {
                keys.Add(new SeatKey(Key.Key1 + door, SeatAction.Door, door, def.Doors[door].Name.ToUpperInvariant()));
            }
            keys.Add(new SeatKey(Key.L, SeatAction.Lock, -1, "LOCK"));
        }
        if (seat.Flies)
        {
            keys.Add(new SeatKey(Key.V, SeatAction.Camera, -1, "CAMERA"));
            keys.Add(new SeatKey(Key.J, SeatAction.Descend, -1, "RESUME FLIGHT"));
        }
        if (seat.Role == SeatRole.Station)
        {
            // A gunner's (IW3): the mouse aims, the left button fires and the right zooms (read as held); R the rockets, Tab the magazine.
            keys.Add(new SeatKey(Key.R, SeatAction.Ammo, -1, "ROCKETS (HOLD: CHOOSE)"));
            keys.Add(new SeatKey(Key.Tab, SeatAction.Magazine, -1, "MAGAZINE"));
        }
        return keys;
    }

    /// <summary>The key a seat's action has, if it has it.</summary>
    public static SeatKey? Find(IReadOnlyList<SeatKey> keys, Key key)
    {
        foreach (SeatKey k in keys)
        {
            if (k.Key == key)
            {
                return k;
            }
        }
        return null;
    }

    /// <summary>
    /// A line of a seat's keys for the screen (in orbit the helm shows its descent, else not): "SPACE LEAVE   1 RAMP …".
    /// </summary>
    public static string Line(IReadOnlyList<SeatKey> keys, bool inOrbit)
    {
        var line = new StringBuilder();
        foreach (SeatKey k in keys)
        {
            if (k.Action == SeatAction.Descend && !inOrbit || k.Action == SeatAction.Camera)
            {
                continue;
            }
            if (line.Length > 0)
            {
                line.Append("     ");
            }
            line.Append(Name(k.Key)).Append("  ").Append(k.Label);
        }
        return line.ToString();
    }

    /// <summary>A gunner's mouse, for the seat's line (IW3).</summary>
    public const string StationMouse = "LMB  FIRE     RMB  ZOOM     ";

    /// <summary>The helm's flight keys, for the flight HUD's corner.</summary>
    public static string FlightHints(bool cockpit) =>
        $"W S throttle · A D slide · SHIFT CTRL up, down · Q E roll · B boost · Z flight assist · X throttle 0 · V {(cockpit ? "chase" : "cockpit")} camera";

    private static string Name(Key key) => key switch
    {
        Key.Space => "SPACE",
        >= Key.Key1 and <= Key.Key9 => ((int)(key - Key.Key1) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => key.ToString().ToUpperInvariant(),
    };
}

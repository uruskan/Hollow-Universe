namespace HollowUniverse.UI.Model;

/// <summary>
/// What the keys mean now (KNOWN_PROBLEMS.md finding 28; GROUND.md §6.7): on foot, or seated by the seat's role. The ground
/// view works it out in one place (<c>SeatKeys.Context</c>); the interface asks <see cref="KeyContexts"/> which of its keys
/// work in it, and the seat's own keys come from the seat's data (<c>SeatKeys.Of</c>), shown on screen.
/// </summary>
public enum KeyContext : byte
{
    /// <summary>Walking: every key is the hands', the belt's and the screens'.</summary>
    OnFoot,

    /// <summary>At a ship's helm: W S A D, Shift Ctrl, Q E are the ship's; Space leaves the seat.</summary>
    Helm,

    /// <summary>In a passenger's seat (the copilot's): the mouse looks; the seat's keys work the doors; Space leaves.</summary>
    Seat,

    /// <summary>At a station (a battery's seat): the mouse looks from its camera; Space leaves.</summary>
    Station,
}

/// <summary>Which of the interface's keys work in a key context: one rule, asked by every handler.</summary>
public static class KeyContexts
{
    /// <summary>Q crafting and the held E radial: the hands' keys, on foot alone (Q and E roll the ship at the helm).</summary>
    public static bool Hands(KeyContext context) => context == KeyContext.OnFoot;

    /// <summary>Held radials (R's rounds; the right mouse's and E's on foot): on foot, and at a station (R: its rockets, IW3).</summary>
    public static bool Radials(KeyContext context) => context is KeyContext.OnFoot or KeyContext.Station;

    /// <summary>1–6 the belt: on foot alone (seated, the number keys are the seat's doors).</summary>
    public static bool Belt(KeyContext context) => context == KeyContext.OnFoot;

    /// <summary>The belt, the vitals and the crosshair are drawn: everywhere but the helm (the flight HUD has their place).</summary>
    public static bool Hud(KeyContext context) => context != KeyContext.Helm;
}

namespace Hollow.Server;

/// <summary>The server's part of a networked entity: its net ID, view (octahedral, with the head's free look) and owner.</summary>
public struct NetPresence
{
    public uint NetId;

    /// <summary>The view of the last input, octahedral as it came (docs/WHOLE_WORLD.md §5).</summary>
    public ushort ViewU;
    public ushort ViewV;

    /// <summary>Free look (Alt), as the last input had it: drawn by others only (GROUND.md §4.5).</summary>
    public sbyte LookYaw;
    public sbyte LookPitch;
    public PlayerSession? Owner;

    /// <summary>
    /// The frame the character moves in, as the wire names it (IRON_WAKE.md §4.11): its ship's net ID aboard, 0 the zone's;
    /// the view above is that frame's. The ships' module keeps it each tick.
    /// </summary>
    public uint Frame;
}

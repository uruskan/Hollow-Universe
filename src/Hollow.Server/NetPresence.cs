namespace Hollow.Server;

/// <summary>The server's part of a networked entity: its net ID, view angles (with the head's free look) and owner.</summary>
public struct NetPresence
{
    public uint NetId;
    public ushort Yaw;
    public ushort Pitch;

    /// <summary>Free look (Alt), as the last input had it: drawn by others only (GROUND.md §4.5).</summary>
    public sbyte LookYaw;
    public sbyte LookPitch;
    public PlayerSession? Owner;
}

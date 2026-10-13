namespace Hollow.Net.Protocol;

/// <summary>Constants of the ground game's protocol (GROUND.md §4.4).</summary>
public static partial class Protocol
{
    /// <summary>
    /// Raised by hand for changes the schema hash can't see (meaning, not layout). Checked at connect. 3: the zone on the
    /// real planet (stand-in flag, day scale); 4: G4, guns and the wounded; 5: both together.
    /// </summary>
    public const uint Version = 5;

    /// <summary>Default UDP port of a server.</summary>
    public const int DefaultPort = 27015;

    /// <summary>Snapshots a second (the zone ticks at 30).</summary>
    public const int SnapshotRate = 20;

    /// <summary>Remote entities are drawn this far in the past, so there are snapshots on both sides to blend.</summary>
    public const float InterpolationDelaySeconds = 0.1f;

    /// <summary>Each input packet repeats this many earlier inputs besides the newest.</summary>
    public const int RedundantInputs = 3;

    /// <summary>Bytes of entity data a snapshot may carry, before the transport's own limit (≤ 256 kbit/s at 20 Hz).</summary>
    public const int SnapshotByteBudget = 1600;

    /// <summary>The first byte of every packet is its message type; snapshots aren't generated, so their ID lives here.</summary>
    public const byte SnapshotTypeId = 11;

    public const int MaxPlayerNameBytes = 32;
}

public enum GoodbyeReason : byte
{
    Unknown,
    ProtocolMismatch,
    BadName,
    NameInUse,
    ServerFull,
    Kicked,
    ServerShutdown,
    Timeout,
    ContentMismatch,
}

public enum EntityKind : byte
{
    Character,
}

public enum ChatKind : byte
{
    Player,
    Server,
}

/// <summary>What a client sees of a static thing.</summary>
[System.Flags]
public enum ThingFlags : byte
{
    None = 0,

    /// <summary>A campfire or furnace burning.</summary>
    Lit = 1,

    /// <summary>The receiving player's own (their sleeping bag).</summary>
    Mine = 2,
}

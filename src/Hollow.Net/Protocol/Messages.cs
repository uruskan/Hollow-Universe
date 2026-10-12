using Hollow.Simulation.Movement;

namespace Hollow.Net.Protocol;

// Every message of the ground game's protocol (GROUND.md §4.4, ARCHITECTURE.md §11). Definitions only: fields with
// attributes saying how they are packed. After changing anything here run
//     dotnet run --project tools/Hollow.NetGen
// which rewrites Messages.g.cs (a test fails while it is out of date). Any change to a layout changes
// Protocol.SchemaHash, so old clients are refused at connect.

// ---------------------------------------------------------------- connecting

/// <summary>The client's connection request: who it is and which protocol it speaks.</summary>
[NetMessage(1)]
public partial struct Hello
{
    public uint ProtocolVersion;
    public ulong SchemaHash;
    [MaxLength(32)] public string PlayerName;
}

/// <summary>Why a connection was refused or closed (connection reject or disconnect data).</summary>
[NetMessage(2)]
public partial struct Goodbye
{
    [Bits(4)] public GoodbyeReason Reason;
    [MaxLength(200)] public string Text;
}

/// <summary>Where the zone is: everything a client needs to build the same ground as the server.</summary>
[NetStruct]
public partial struct ZoneInfo
{
    public ulong SystemAddress;
    public uint BodyPath;
    public double Radius;
    public double SurfaceGravity;
    public ulong SurfaceSeed;
    public double LatitudeDegrees;
    public double LongitudeDegrees;
}

/// <summary>The server's first message to an accepted client.</summary>
[NetMessage(3)]
public partial struct Welcome
{
    [VarInt] public uint NetId;
    public ulong EntityId;
    public uint ServerTick;
    public byte TickRate;
    public byte SnapshotRate;
    public ZoneInfo Zone;
    [MaxLength(64)] public string ServerName;
}

// ---------------------------------------------------------------- input (client → server, unreliable, every tick)

/// <summary>One tick of a player's input: move axes, view angles and buttons, all quantised.</summary>
[NetStruct]
public partial struct InputCommand
{
    /// <summary>−127 … 127: back to forward.</summary>
    [Signed(8)] public sbyte Forward;

    /// <summary>−127 … 127: left to right.</summary>
    [Signed(8)] public sbyte Right;

    [Angle] public ushort Yaw;

    [Angle] public ushort Pitch;

    [Bits(4)] public MoveButtons Buttons;
}

/// <summary>
/// The client's packet of every tick: the inputs of <c>NewestTick</c> and the three before it (newest first), so a
/// lost packet costs nothing, and which snapshots arrived.
/// </summary>
[NetMessage(10)]
public partial struct InputPacket
{
    /// <summary>The newest snapshot received, and a bit for each of the 32 before it.</summary>
    public uint AckSequence;
    public uint AckBits;
    public uint NewestTick;
    [MaxCount(4)] public InputCommand[] Inputs;
}

// ---------------------------------------------------------------- snapshots (server → client, unreliable, 20 Hz)

/// <summary>The player's own character exactly as the server has it, for reconciliation.</summary>
[NetStruct]
public partial struct OwnState
{
    public float PositionX;
    public float PositionY;
    public float PositionZ;
    public float VelocityX;
    public float VelocityY;
    public float VelocityZ;
    [Bits(1)] public Stance Stance;
    public byte Health;
    [Bits(4)] public MoveFlags Flags;
}

/// <summary>
/// The start of a snapshot. The entities follow it, each delta-coded against the newest state of it the client
/// confirmed (Replication/SnapshotCodec).
/// </summary>
[NetStruct]
public partial struct SnapshotHeader
{
    public uint Sequence;
    public uint ServerTick;

    /// <summary>How many ticks early this client's inputs reached the server lately (the least over a second).</summary>
    [Signed(8)] public sbyte InputMargin;

    /// <summary>The last tick the server stepped this player's character, and its state after it (0: no character).</summary>
    public uint OwnTick;
    public OwnState Own;

    [VarInt] public uint EntityCount;
}

/// <summary>A character as other players see it: position on a 1/256 m grid, view angles, what it's doing.</summary>
[NetState]
public partial struct CharacterNetState
{
    [Signed(32)] public int X;
    [Signed(32)] public int Y;
    [Signed(32)] public int Z;
    [Angle] public ushort Yaw;
    [Angle] public ushort Pitch;
    [Bits(4)] public MoveFlags Flags;
    [Bits(1)] public Stance Stance;
    public byte Health;
}

// ---------------------------------------------------------------- reliable events

/// <summary>An entity became relevant to this client (it then appears in snapshots).</summary>
[NetMessage(20)]
public partial struct Spawn
{
    [VarInt] public uint NetId;
    public ulong EntityId;
    [Bits(4)] public EntityKind Kind;
    [MaxLength(32)] public string Name;
}

/// <summary>An entity stopped being relevant to this client, or is gone.</summary>
[NetMessage(21)]
public partial struct Despawn
{
    [VarInt] public uint NetId;
}

/// <summary>A chat line a player sends.</summary>
[NetMessage(30)]
public partial struct ChatSend
{
    [MaxLength(200)] public string Text;
}

/// <summary>A chat line everyone receives.</summary>
[NetMessage(31)]
public partial struct ChatLine
{
    [Bits(2)] public ChatKind Kind;
    [MaxLength(32)] public string From;
    [MaxLength(200)] public string Text;
}

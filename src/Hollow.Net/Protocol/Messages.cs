using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.World;

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

    /// <summary>The hash of the client's game data (game/data/ground): both sides must run the same rules.</summary>
    public ulong ContentHash;
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

    /// <summary>The view the character was saved with (a returning player faces where they left off).</summary>
    [Angle] public ushort Yaw;
    [Angle] public ushort Pitch;
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

    /// <summary>The belt slot in hand (0 … 5; 7: none).</summary>
    [Bits(3)] public byte Held;

    /// <summary>Attack and aim buttons.</summary>
    [Bits(3)] public ActionButtons Actions;

    /// <summary>Quarter ticks between this input's tick and the tick the player was drawing others at (lag compensation).</summary>
    public byte ViewDelay;
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

    /// <summary>The client's clock in milliseconds (wrapping), echoed in snapshots to measure the round trip.</summary>
    public ushort ClientTime;
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

    /// <summary>The newest input packet's client time, and how many milliseconds the server held it before this.</summary>
    public ushort EchoTime;
    public ushort EchoHold;

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

    /// <summary>The item in hand (0: none), a count of swings and shots (others animate each) and the last kind.</summary>
    public ushort Held;
    [Bits(4)] public byte ActionCount;
    [Bits(2)] public ActionKind Action;

    /// <summary>Logged off and asleep in the world.</summary>
    public bool Sleeping;
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

// ---------------------------------------------------------------- G2: static things, clothing, nature, projectiles

/// <summary>A static thing became relevant: a deployable, a dropped item or a corpse, with all a client draws of it.</summary>
[NetMessage(22)]
public partial struct ThingSpawn
{
    [VarInt] public uint NetId;
    public ulong EntityId;
    [Bits(2)] public StaticKind Kind;

    /// <summary>What it looks like: a deployable's item or a dropped stack's item (0 for a corpse).</summary>
    [VarInt] public uint Look;

    /// <summary>Its base on the ground, in the zone frame, and its turn about up.</summary>
    public float X;
    public float Y;
    public float Z;
    [Angle] public ushort Yaw;

    /// <summary>Health as a fraction of 255 (deployables), the count of a dropped stack.</summary>
    public byte Health;
    [VarInt] public uint Count;
    [Bits(4)] public ThingFlags Flags;

    /// <summary>A corpse's name.</summary>
    [MaxLength(32)] public string Name;
}

/// <summary>A relevant static thing changed: health, flags, count.</summary>
[NetMessage(23)]
public partial struct ThingUpdate
{
    [VarInt] public uint NetId;
    public byte Health;
    [VarInt] public uint Count;
    [Bits(4)] public ThingFlags Flags;
}

/// <summary>An item worn (for <see cref="CharacterLook"/>).</summary>
[NetStruct]
public partial struct WornItem
{
    [VarInt] public uint Item;
}

/// <summary>What a character wears, when it becomes relevant and when it changes.</summary>
[NetMessage(24)]
public partial struct CharacterLook
{
    [VarInt] public uint NetId;
    [MaxCount(7)] public WornItem[] Wear;
}

/// <summary>A natural object as players changed it; health 255, not depleted and no weak spot is as new.</summary>
[NetStruct]
public partial struct NaturalNet
{
    public ulong Id;

    /// <summary>Health as a fraction of 255 of its full health.</summary>
    public byte Health;

    /// <summary>The weak spot, if it has one yet: which way it faces (1/65536 turns) and how high (0 … 255).</summary>
    public bool HasWeakSpot;
    [Angle] public ushort WeakSpotYaw;
    public byte WeakSpotLift;
    public bool Depleted;
}

/// <summary>Changes to natural objects near the player (all of an area when it comes into range, then as they happen).</summary>
[NetMessage(40)]
public partial struct NaturalStates
{
    [MaxCount(64)] public NaturalNet[] States;
}

/// <summary>A projectile was loosed (others draw its flight; the server alone decides what it hits).</summary>
[NetMessage(41)]
public partial struct ProjectileLaunch
{
    [VarInt] public uint Id;
    [VarInt] public uint Shooter;
    [VarInt] public uint Ammo;
    public float X;
    public float Y;
    public float Z;
    public float VelocityX;
    public float VelocityY;
    public float VelocityZ;
}

/// <summary>A projectile stopped, at a point, on something.</summary>
[NetMessage(42)]
public partial struct ProjectileEnd
{
    [VarInt] public uint Id;
    public float X;
    public float Y;
    public float Z;
    [Bits(3)] public HitTarget Target;
}

// ---------------------------------------------------------------- G2: intents (client → server, reliable ordered)
// Each has a sequence number; the server answers every one with an IntentResult (ARCHITECTURE.md §11.4).

/// <summary>Drag items from one slot to another: move, split (part of a stack), top up, swap.</summary>
[NetMessage(50)]
public partial struct MoveItemIntent
{
    [VarInt] public uint Seq;
    [Bits(3)] public ContainerRef From;
    public byte FromSlot;
    [Bits(3)] public ContainerRef To;
    public byte ToSlot;

    /// <summary>How many (0: all); the item the client saw in the source slot.</summary>
    [VarInt] public uint Amount;
    [VarInt] public uint Expected;
}

/// <summary>Right-click: send a stack where it obviously goes.</summary>
[NetMessage(51)]
public partial struct QuickMoveIntent
{
    [VarInt] public uint Seq;
    [Bits(3)] public ContainerRef From;
    public byte Slot;
    [VarInt] public uint Expected;
}

/// <summary>Drop (part of) a stack in front of the player.</summary>
[NetMessage(52)]
public partial struct DropIntent
{
    [VarInt] public uint Seq;
    [Bits(3)] public ContainerRef From;
    public byte Slot;
    [VarInt] public uint Amount;
    [VarInt] public uint Expected;
}

/// <summary>Eat or use a consumable.</summary>
[NetMessage(53)]
public partial struct UseIntent
{
    [VarInt] public uint Seq;
    [Bits(3)] public ContainerRef From;
    public byte Slot;
    [VarInt] public uint Expected;
}

/// <summary>Start crafting a recipe (its output item's ID).</summary>
[NetMessage(54)]
public partial struct CraftIntent
{
    [VarInt] public uint Seq;
    [VarInt] public uint Recipe;
    [VarInt] public uint Count;
}

[NetMessage(55)]
public partial struct CancelCraftIntent
{
    [VarInt] public uint Seq;
    [VarInt] public uint Ticket;
}

/// <summary>Open a box, device, corpse or sleeper.</summary>
[NetMessage(56)]
public partial struct OpenIntent
{
    [VarInt] public uint Seq;
    public ulong Target;
}

[NetMessage(57)]
public partial struct CloseIntent
{
    [VarInt] public uint Seq;
}

/// <summary>Pick up a pickup or berry bush (a natural object's ID) or a dropped item (an entity ID).</summary>
[NetMessage(58)]
public partial struct PickUpIntent
{
    [VarInt] public uint Seq;
    public bool Natural;
    public ulong Target;
}

/// <summary>Place the deployable in a slot at a point (zone frame), turned by a yaw.</summary>
[NetMessage(59)]
public partial struct PlaceIntent
{
    [VarInt] public uint Seq;
    [Bits(3)] public ContainerRef From;
    public byte Slot;
    [VarInt] public uint Expected;
    public float X;
    public float Y;
    public float Z;
    [Angle] public ushort Yaw;
}

/// <summary>Light or put out a campfire or furnace.</summary>
[NetMessage(60)]
public partial struct ToggleIntent
{
    [VarInt] public uint Seq;
    public ulong Target;
}

/// <summary>Respawn at one of the player's sleeping bags (its entity ID), or on the beach (0).</summary>
[NetMessage(61)]
public partial struct RespawnIntent
{
    [VarInt] public uint Seq;
    public ulong Bag;
}

// ---------------------------------------------------------------- G2: the player's own state (server → owner, reliable)

/// <summary>The answer to an intent: accepted, or why not. Sent after the changes it made.</summary>
[NetMessage(70)]
public partial struct IntentResult
{
    [VarInt] public uint Seq;
    [Bits(5)] public ActionResult Result;
}

/// <summary>One slot of a container.</summary>
[NetStruct]
public partial struct SlotNet
{
    public byte Index;
    [VarInt] public uint Item;
    [VarInt] public uint Count;
    [VarInt] public uint Condition;
}

/// <summary>A whole container (the player's own at joining, a loot container when it opens): its size and every filled slot.</summary>
[NetMessage(71)]
public partial struct ContainerContents
{
    /// <summary>The last intent the server had applied when it sent this (the client drops those pending up to it).</summary>
    [VarInt] public uint LastSeq;
    [Bits(3)] public ContainerRef Which;
    public byte Size;
    [MaxCount(255)] public SlotNet[] Slots;
}

/// <summary>Slots of a container that changed (an emptied slot has item 0).</summary>
[NetMessage(72)]
public partial struct ContainerSlots
{
    [VarInt] public uint LastSeq;
    [Bits(3)] public ContainerRef Which;
    [MaxCount(255)] public SlotNet[] Slots;
}

/// <summary>What the player is looting now (target 0: nothing); its containers follow.</summary>
[NetMessage(73)]
public partial struct LootState
{
    [VarInt] public uint LastSeq;
    public ulong Target;
    [Bits(3)] public LootKind Kind;
}

/// <summary>The player's vitals, when a whole unit of anything changes.</summary>
[NetMessage(74)]
public partial struct VitalsUpdate
{
    /// <summary>Health in tenths; calories and water in whole units; felt temperature in °C.</summary>
    [VarInt] public uint Health;
    [VarInt] public uint Calories;
    [VarInt] public uint Hydration;
    [Signed(8)] public sbyte Temperature;

    /// <summary>Health still to bleed away and to heal, in whole points.</summary>
    public byte Bleeding;
    public byte Healing;

    /// <summary>How cold (0 … 255), near a fire, wet.</summary>
    public byte Cold;
    public bool Comfort;
    public bool Wet;
}

/// <summary>A crafting job as the client shows it.</summary>
[NetStruct]
public partial struct CraftJobNet
{
    [VarInt] public uint Ticket;
    [VarInt] public uint Recipe;
    [VarInt] public uint Count;
    [VarInt] public uint TicksLeft;
    [VarInt] public uint UnitTicks;
}

/// <summary>The crafting queue, when it changes: <c>TicksLeft</c> counts from <c>ServerTick</c>.</summary>
[NetMessage(75)]
public partial struct CraftQueueState
{
    [VarInt] public uint LastSeq;
    public uint ServerTick;
    [VarInt] public uint NextTicket;
    [MaxCount(8)] public CraftJobNet[] Jobs;
}

/// <summary>The player's hit landed.</summary>
[NetMessage(76)]
public partial struct HitMarker
{
    [Bits(3)] public HitTarget Target;
    [Bits(2)] public HitRegion Region;
    [VarInt] public uint Damage;
    public bool Killed;
    public bool WeakSpot;
}

/// <summary>Items gained or lost and why (gathered, picked up, crafted; a tool broke), for notifications.</summary>
[NetMessage(77)]
public partial struct ItemsChanged
{
    [VarInt] public uint Item;
    [Signed(32)] public int Count;
    [Bits(3)] public GainReason Reason;
    public bool WeakSpot;
}

/// <summary>The player was hurt by an attack: how much (tenths), where, from where, with what.</summary>
[NetMessage(78)]
public partial struct DamageTaken
{
    [VarInt] public uint Damage;
    [Bits(2)] public HitRegion Region;
    public float FromX;
    public float FromY;
    public float FromZ;
    [VarInt] public uint Weapon;
}

[NetMessage(79)]
public partial struct CraftFinished
{
    [VarInt] public uint Item;
    [VarInt] public uint Count;
}

/// <summary>A sleeping bag the player can respawn at, and in how many seconds.</summary>
[NetStruct]
public partial struct BagNet
{
    public ulong Id;
    public float X;
    public float Z;
    [VarInt] public uint ReadySeconds;
}

/// <summary>Alive or dead; when dead, how, by whom, and where the player can respawn.</summary>
[NetMessage(80)]
public partial struct LifeState
{
    public bool Dead;
    [MaxLength(64)] public string Cause;
    [MaxLength(32)] public string KilledBy;
    [VarInt] public uint BeachReadySeconds;
    [MaxCount(32)] public BagNet[] Bags;
}

// ---------------------------------------------------------------- G3: building intents (client → server, reliable)
// Each has a sequence number from the same series as G2's intents and is answered with a BuildResult (GROUND.md §6.3).

/// <summary>
/// Place a piece from the building plan: on a support's socket in an orientation (the server derives the pose), or,
/// with no support, a foundation on the ground at a point (zone frame) and heading in 15° steps.
/// </summary>
[NetMessage(90)]
public partial struct PlacePieceIntent
{
    [VarInt] public uint Seq;
    public byte Piece;
    public ulong Support;
    public byte Socket;
    public byte Orientation;
    public float X;
    public float Y;
    public float Z;
    [Bits(5)] public byte Heading;
}

/// <summary>A hammer action on a piece: upgrade (to a grade), rotate, demolish, repair.</summary>
[NetMessage(91)]
public partial struct HammerIntent
{
    [VarInt] public uint Seq;
    [Bits(2)] public HammerAction Action;
    public ulong Target;
    [Bits(3)] public byte Grade;
}

/// <summary>Fit the door in a slot into a frame piece (flipped or not), open or shut a door, or pick one up.</summary>
[NetMessage(92)]
public partial struct DoorIntent
{
    [VarInt] public uint Seq;
    [Bits(2)] public DoorAction Action;
    [Bits(3)] public ContainerRef From;
    public byte Slot;
    [VarInt] public uint Expected;
    public ulong Target;
    public bool Flip;
}

/// <summary>A lock on a door: fit one from a slot, set or enter a code, lock, unlock, cut a key, take it off.</summary>
[NetMessage(93)]
public partial struct LockIntent
{
    [VarInt] public uint Seq;
    [Bits(3)] public LockAction Action;
    [Bits(3)] public ContainerRef From;
    public byte Slot;
    [VarInt] public uint Expected;
    public ulong Door;
    [MaxLength(8)] public string Code;
}

/// <summary>
/// The claim device: place the one in a slot on a foundation or floor at a point of its top (frame metres, X and Z)
/// turned by 30° steps; authorise, deauthorise, clear the list; pick it up.
/// </summary>
[NetMessage(94)]
public partial struct ClaimIntent
{
    [VarInt] public uint Seq;
    [Bits(3)] public ClaimAction Action;
    [Bits(3)] public ContainerRef From;
    public byte Slot;
    [VarInt] public uint Expected;
    public ulong Target;
    public double X;
    public double Z;
    [Bits(4)] public byte Yaw;
}

/// <summary>The answer to a building intent: done (None), or the rules' reason with the player's line.</summary>
[NetMessage(95)]
public partial struct BuildResult
{
    [VarInt] public uint Seq;
    [Bits(6)] public Refusal Reason;
    [MaxLength(96)] public string Text;
}

// ---------------------------------------------------------------- G3: building replication (server → client, reliable)
// Pieces, doors and claim devices are static entities (GROUND.md §4.4): sent when their area comes within range,
// streamed a few hundred bytes a snapshot, then only their changes. Positions are exact doubles in an exact frame,
// so a client's copy links, solves and collides exactly as the server's does.

/// <summary>A structure frame (a lattice that pieces snap in): origin and axes in the zone frame.</summary>
[NetStruct]
public partial struct BuildFrameNet
{
    [VarInt] public uint Id;
    public double OriginX;
    public double OriginY;
    public double OriginZ;
    public double XX;
    public double XY;
    public double XZ;
    public double YX;
    public double YY;
    public double YZ;
    public double ZX;
    public double ZY;
    public double ZZ;
}

/// <summary>Frames the pieces that follow use, each sent once.</summary>
[NetMessage(96)]
public partial struct BuildFrames
{
    [MaxCount(16)] public BuildFrameNet[] Frames;
}

/// <summary>
/// A piece as clients have it: its frame, kind, grade, turn and exact position there; health as a fraction of 65,535;
/// the server's body ID (prediction meets the same contacts); seconds left to rotate or demolish it; coming down.
/// </summary>
[NetStruct]
public partial struct PieceNet
{
    public ulong Id;
    [VarInt] public uint Frame;
    public byte Piece;
    [Bits(3)] public byte Grade;
    [Bits(4)] public byte Yaw;
    public double X;
    public double Y;
    public double Z;
    public ushort Health;
    [VarInt] public uint Body;
    [VarInt] public uint EditLeft;
    public bool Collapsing;
}

/// <summary>New pieces in range, and pieces that changed.</summary>
[NetMessage(97)]
public partial struct BuildPieces
{
    [MaxCount(64)] public PieceNet[] Pieces;
}

[NetStruct]
public partial struct BuildIdNet
{
    public ulong Id;
}

/// <summary>Pieces, doors or claim devices gone: destroyed or taken away, or (not destroyed) out of range.</summary>
[NetMessage(98)]
public partial struct BuildRemoved
{
    [Bits(2)] public BuildPartKind Kind;
    public bool Destroyed;
    [MaxCount(255)] public BuildIdNet[] Ids;
}

/// <summary>
/// A door: its frame piece, which door (building.json's order), flipped, open; health as a fraction of 65,535; its
/// lock, whether locked and whether this player may pass it; its leaves' body IDs.
/// </summary>
[NetStruct]
public partial struct DoorNet
{
    public ulong Id;
    public ulong Frame;
    public byte Door;
    public bool Flip;
    public bool Open;
    public ushort Health;
    [Bits(2)] public LockKind Lock;
    public ulong LockId;
    public bool Locked;
    public bool CanUse;
    [VarInt] public uint Body0;
    [VarInt] public uint Body1;
}

[NetMessage(99)]
public partial struct BuildDoors
{
    [MaxCount(32)] public DoorNet[] Doors;
}

/// <summary>
/// A claim device: the piece it stands on, where on it (frame metres) and its turn; health as a fraction of 65,535;
/// whether this player is on its list; its body; how long its store keeps the base from decaying (minutes).
/// </summary>
[NetStruct]
public partial struct ClaimNet
{
    public ulong Id;
    public ulong Support;
    public double X;
    public double Y;
    public double Z;
    [Bits(4)] public byte Yaw;
    public ushort Health;
    public bool Authorised;
    [VarInt] public uint Body;
    [VarInt] public uint ProtectedMinutes;
}

[NetMessage(100)]
public partial struct BuildClaims
{
    [MaxCount(16)] public ClaimNet[] Claims;
}

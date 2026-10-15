using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Diagnostics;
using Hollow.Simulation.Content;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Raid;
using Hollow.Simulation.Teams;
using Hollow.Simulation.World;
using Hollow.Simulation.Targets;

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

    /// <summary>The body the player chose (GROUND.md §4.8): 0 male, 1 female; saved with the character.</summary>
    public byte Body;
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

    /// <summary>The stand-in ground instead of the body's real surface (tests).</summary>
    public bool StandIn;

    /// <summary>The ground the zone stands on (Simulation.GroundKind: 0 the stand-in, 1 the walking-scale ground; S4).</summary>
    public byte Ground;

    /// <summary>How much faster than real the planet turns for play (GROUND.md §1: 24), counted from J2000.</summary>
    public float RotationTimeScale;
    /// <summary>The air's density at the mean radius (kg/m³) and its scale height (m): ships' drag (G6).</summary>
    public float AirDensity;
    public float AirScaleHeight;
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

    /// <summary>The view the character was saved with (a returning player faces where they left off), octahedral.</summary>
    public ushort ViewU;
    public ushort ViewV;
}

// ---------------------------------------------------------------- input (client → server, unreliable, every tick)

/// <summary>One tick of a player's input: move axes, view direction and buttons, all quantised.</summary>
[NetStruct]
public partial struct InputCommand
{
    /// <summary>−127 … 127: back to forward.</summary>
    [Signed(8)] public sbyte Forward;

    /// <summary>−127 … 127: left to right.</summary>
    [Signed(8)] public sbyte Right;

    /// <summary>The view, a direction in the zone's frame, octahedral (docs/WHOLE_WORLD.md §5, <c>Quantize.Octahedral</c>).</summary>
    public ushort ViewU;

    public ushort ViewV;

    /// <summary>Free look (Alt): the head's turn from the view, in <see cref="NetConvert.LookStep"/>s (cosmetic: the
    /// view above is the aim and the movement's).</summary>
    [Signed(8)] public sbyte LookYaw;

    [Signed(8)] public sbyte LookPitch;

    [Bits(4)] public MoveButtons Buttons;

    /// <summary>The belt slot in hand (0 … 5; 7: none).</summary>
    [Bits(3)] public byte Held;

    /// <summary>Attack and aim buttons.</summary>
    [Bits(3)] public ActionButtons Actions;

    /// <summary>Quarter ticks between this input's tick and the tick the player was drawing others at (lag compensation).</summary>
    public byte ViewDelay;

    /// <summary>A pilot's controls (G6): one entry while flying a ship, none on foot (a bit).</summary>
    [MaxCount(1)] public ShipControls[] Ship;

    /// <summary>A gunner's controls (IW3, IRON_WAKE.md §6.8): one entry while seated at a station, none otherwise.</summary>
    [MaxCount(1)] public StationControls[] Station;

    /// <summary>
    /// The frame the view is in (IRON_WAKE.md §4.11): the one the client believed its character was in, a ship's net ID
    /// (0 the zone's). On the tick of a hand-over it is the other: the server turns the view through the ship's pose.
    /// </summary>
    [VarInt] public uint Frame;
}

/// <summary>One tick of a pilot's controls (G6), each −127 … 127: stick, throttle lever, strafe, up and down.</summary>
[NetStruct]
public partial struct ShipControls
{
    [Signed(8)] public sbyte Pitch;
    [Signed(8)] public sbyte Yaw;
    [Signed(8)] public sbyte Roll;
    [Signed(8)] public sbyte Throttle;
    [Signed(8)] public sbyte Strafe;
    [Signed(8)] public sbyte Vertical;

    /// <summary>Boost held, flight assist off.</summary>
    [Bits(2)] public ShipButtons Buttons;
}

/// <summary>
/// One tick of a gunner's controls (IW3, IRON_WAKE.md §6.8): the trigger held, the zoom, the kind asked for (its index in
/// the battery's ammo + 1; 0 no change) and a counter a tap of R raises (a change draws rounds of another kind back). The
/// asked direction is the input's view, given in the zone's frame.
/// </summary>
[NetStruct]
public partial struct StationControls
{
    public bool Fire;
    public bool Zoom;
    [Bits(2)] public byte Choice;
    [Bits(2)] public byte Cycle;
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
    [Bits(2)] public Stance Stance;
    public byte Health;
    [Bits(4)] public MoveFlags Flags;

    /// <summary>An admin's god mode and noclip (GROUND.md §8.2), which the prediction runs with too.</summary>
    [Bits(2)] public MoveCheats Cheats;

    /// <summary>The frame the state is in (IRON_WAKE.md §4.2): aboard, the ship's net ID, and the position and velocity are its frame's; 0 the zone's.</summary>
    [VarInt] public uint Frame;

    /// <summary>
    /// The seat the character sits in (IRON_WAKE.md §5.2): 0 none, n seat n − 1 of the ship its frame names (or, a ship
    /// people only sit in, the one in <c>OwnShip</c>); the prediction carries it there.
    /// </summary>
    [Bits(5)] public byte Seat;
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

    /// <summary>The ship the player flies (G6), exactly, after <c>OwnTick</c>: one entry while in a seat, none on foot.</summary>
    [MaxCount(1)] public OwnShipState[] OwnShip;
}

/// <summary>The ship the player flies, exactly as the server has it, for its prediction's reconciliation (G6).</summary>
[NetStruct]
public partial struct OwnShipState
{
    [VarInt] public uint NetId;

    /// <summary>Its kind (the ship's item).</summary>
    [VarInt] public uint Kind;
    public float PositionX;
    public float PositionY;
    public float PositionZ;
    public float RotationX;
    public float RotationY;
    public float RotationZ;
    public float RotationW;
    public float VelocityX;
    public float VelocityY;
    public float VelocityZ;
    public float SpinX;
    public float SpinY;
    public float SpinZ;
    public float Health;
    public float Fuel;
    public ushort BoostTicks;
    public ushort CooldownTicks;
    public ushort RestTicks;
    [Bits(9)] public ShipFlags Flags;
}

/// <summary>
/// A ship as everyone sees it (G6): position on a 1/256 m grid, its turn as a smallest-three rotation (no gimbal lock
/// on a whole world, docs/WHOLE_WORLD.md §5: <c>Quantize.SmallestThree</c>), what it is doing, its hatch, its engines'
/// output (±127 = ±2 of each axis's thrust, boost above 1), its hull (of 255) and its kind (the item).
/// </summary>
[NetState]
public partial struct ShipNetState
{
    [Signed(32)] public int X;
    [Signed(32)] public int Y;
    [Signed(32)] public int Z;
    [Bits(2)] public byte TurnLargest;
    [Signed(16)] public short TurnA;
    [Signed(16)] public short TurnB;
    [Signed(16)] public short TurnC;
    [Bits(11)] public ShipNetFlags Flags;

    /// <summary>What the receiver is told of it (GROUND_SYSTEMS.md §3.3): theirs, a teammate's, locked, on its crew.</summary>
    [Bits(8)] public ViewerFlags Viewer;
    [Signed(8)] public sbyte ThrustX;
    [Signed(8)] public sbyte ThrustY;
    [Signed(8)] public sbyte ThrustZ;
    public byte Health;
    [VarInt] public uint Kind;

    /// <summary>
    /// Its parked body's ID in the zone's physics (0 while it flies): a client makes its copy with the same ID, so its
    /// prediction meets the same contacts in the same order (as the building's bodies, IRON_WAKE.md §4.6).
    /// </summary>
    [VarInt] public uint Body;
}

/// <summary>A character as other players see it: position on a 1/256 m grid, view direction, what it's doing.</summary>
[NetState]
public partial struct CharacterNetState
{
    [Signed(32)] public int X;
    [Signed(32)] public int Y;
    [Signed(32)] public int Z;

    /// <summary>The view, a direction in the zone's frame, octahedral (docs/WHOLE_WORLD.md §5).</summary>
    public ushort ViewU;
    public ushort ViewV;

    /// <summary>Free look (Alt): where the head looks from the view, in <see cref="NetConvert.LookStep"/>s (drawn only).</summary>
    [Signed(8)] public sbyte LookYaw;
    [Signed(8)] public sbyte LookPitch;
    [Bits(4)] public MoveFlags Flags;
    [Bits(2)] public Stance Stance;
    public byte Health;

    /// <summary>The item in hand (0: none), a count of swings, shots and reloads (others animate each) and the last kind.</summary>
    public ushort Held;
    [Bits(4)] public byte ActionCount;
    [Bits(3)] public ActionKind Action;

    /// <summary>Logged off and asleep in the world.</summary>
    public bool Sleeping;

    /// <summary>G4: aiming a gun down its sights, and the held gun's attachments (bit i: weapons.json's attachment i).</summary>
    public bool Aiming;
    public byte HeldMods;

    /// <summary>
    /// The frame it moves in (IRON_WAKE.md §4.11): aboard, its ship's net ID, and the position and view above are that
    /// ship's frame's (the client places it on the ship as it draws it); 0 the zone's.
    /// </summary>
    [VarInt] public uint Frame;
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

/// <summary>A static thing became relevant: a deployable, a bag of dropped items or a corpse, with all a client draws of it.</summary>
[NetMessage(22)]
public partial struct ThingSpawn
{
    [VarInt] public uint NetId;
    public ulong EntityId;
    [Bits(2)] public StaticKind Kind;

    /// <summary>What it looks like: a deployable's item, a world prop's number; 0 for a corpse or a bag (what a bag holds goes only to whoever opens it).</summary>
    [VarInt] public uint Look;

    /// <summary>Its base on the ground, in the zone frame, and its turn about up.</summary>
    public float X;
    public float Y;
    public float Z;
    [Angle] public ushort Yaw;

    /// <summary>Health as a fraction of 255 (deployables); how many stacks a bag holds.</summary>
    public byte Health;
    [VarInt] public uint Count;

    /// <summary>What the receiver is told of it (GROUND_SYSTEMS.md §3.3): lit, theirs, empty, a teammate's.</summary>
    [Bits(8)] public ViewerFlags Flags;

    /// <summary>Its own name: a body's (the dead player's), a bag's, a world prop's; empty for a deployable (named by its item).</summary>
    [MaxLength(32)] public string Name;

    /// <summary>What it stands on (GROUND_SYSTEMS.md §6): a building piece's family and ID, or none (the ground).</summary>
    [Bits(4)] public TargetFamily SupportFamily;
    [VarInt] public ulong Support;

    /// <summary>
    /// Its capabilities (GROUND_SYSTEMS.md §2.1, capabilities.json's numbers as bits): what the interaction rules offer on
    /// it, the same set the server's rules see.
    /// </summary>
    [VarInt] public ulong Capabilities;
}

/// <summary>A relevant static thing changed: health, count, or what the receiver is told of it.</summary>
[NetMessage(23)]
public partial struct ThingUpdate
{
    [VarInt] public uint NetId;
    public byte Health;
    [VarInt] public uint Count;
    [Bits(8)] public ViewerFlags Flags;
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

    /// <summary>The character's body: 0 male, 1 female.</summary>
    public byte Body;
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

    /// <summary>Hits on the weak spot in a row (each one's sound rises).</summary>
    public byte Streak;

    /// <summary>Ticks since the last hit when this was sent (255: long ago, or none): only a fresh hit plays its effects.</summary>
    public byte HitAge;

    /// <summary>Where the last hit met the drawn surface, from the object's base in 3 cm steps (effects play there).</summary>
    public sbyte HitX;
    public sbyte HitY;
    public sbyte HitZ;
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

    /// <summary>A bullet's shot: the shooter's gun action (their client already drew it), and whether silenced. 0 for arrows.</summary>
    [VarInt] public uint Seq;
    public bool Silenced;

    /// <summary>A battery's rocket (IW3): its battery's number + 1, so it is drawn under its motor (0: a gun's or a bow's); <see cref="Seq"/> is its shot's number.</summary>
    [Bits(4)] public byte Battery;
}

/// <summary>A projectile stopped, at a point, on a thing of a family (none: out of range or time in the air).</summary>
[NetMessage(42)]
public partial struct ProjectileEnd
{
    [VarInt] public uint Id;
    public float X;
    public float Y;
    public float Z;
    [Bits(4)] public TargetFamily Target;
}

/// <summary>A thing named on the wire (GROUND_SYSTEMS.md §3.1): its family, its ID, and a part of it (0: the whole).</summary>
[NetStruct]
public partial struct TargetNet
{
    [Bits(4)] public TargetFamily Family;
    public ulong Id;
    public byte Part;
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

[NetMessage(57)]
public partial struct CloseIntent
{
    [VarInt] public uint Seq;
}

/// <summary>Respawn at one of the player's sleeping bags (its entity ID), or on the beach (0).</summary>
[NetMessage(61)]
public partial struct RespawnIntent
{
    [VarInt] public uint Seq;
    public ulong Bag;
}

// ---------------------------------------------------------------- G2: the player's own state (server → owner, reliable)

/// <summary>
/// The answer to every intent (GROUND_SYSTEMS.md §3.2): done, or the code of why not, and the number its line names (an
/// item, seconds, a percentage; 0 for none). Sent after the changes it made.
/// </summary>
[NetMessage(70)]
public partial struct IntentResult
{
    [VarInt] public uint Seq;
    [Bits(7)] public OutcomeCode Code;
    [VarInt] public uint Detail;
}

/// <summary>
/// A seated occupant's keys (G6, IW2; client → server, reliable ordered, the general intent sequence and budget, answered
/// by <see cref="IntentResult"/>): a door (its index in <see cref="Arg"/>), leaving the seat, the lock, descending from
/// orbit. On foot, a ship's doors, seats, tank, list and cargo are verbs (<see cref="InteractIntent"/>).
/// </summary>
[NetMessage(120)]
public partial struct ShipIntent
{
    [VarInt] public uint Seq;
    public ulong Ship;
    [Bits(3)] public ShipAction Action;
    [Bits(4)] public byte Arg;
}

/// <summary>
/// A ship's details (IRON_WAKE.md §5, §12; server → client, reliable ordered), sent when they change and when a ship whose
/// details differ from a new one's comes into range: its moving parts away from rest that no door drives (their states'
/// bits exactly, so a client's space poses as the server's and its prediction aboard stays exact; every part not listed
/// is at the interior's rest), each door's motion (where it was at a tick and which way it goes: a client works out its
/// progress at any tick as the server does), who sits in each seat taken, and each part's health below its most.
/// </summary>
[NetMessage(121)]
public partial struct ShipDetails
{
    [VarInt] public uint Ship;
    [MaxCount(255)] public ShipPartNet[] Parts;
    [MaxCount(16)] public ShipDoorNet[] Doors;
    [MaxCount(31)] public ShipSeatNet[] Seats;
    [MaxCount(255)] public ShipPartHealthNet[] Health;
}

/// <summary>One part's state: its index in the interior, the float's bits.</summary>
[NetStruct]
public partial struct ShipPartNet
{
    [VarInt] public uint Part;
    public uint Bits;
}

/// <summary>
/// One door's motion (IRON_WAKE.md §5.3): its progress's bits at <see cref="Since"/> (a server tick), which way it goes
/// (+1, −1, 0) or would go once whoever is in its way is clear, and its health as a share of its most (0 broken).
/// </summary>
[NetStruct]
public partial struct ShipDoorNet
{
    public uint From;
    [Signed(2)] public sbyte Moving;
    [Signed(2)] public sbyte Waiting;
    public uint Since;
    public byte Health;
}

/// <summary>A seat taken: its index and who sits there.</summary>
[NetStruct]
public partial struct ShipSeatNet
{
    [Bits(5)] public byte Seat;
    public ulong Occupant;
}

/// <summary>A part's health below its most: its index, its share of its most (0 broken, 255 whole).</summary>
[NetStruct]
public partial struct ShipPartHealthNet
{
    public byte Part;
    public byte Health;
}

/// <summary>
/// A ship's crewed batteries (IW3, IRON_WAKE.md §6.6, §6.9; server → client, reliable ordered), sent when a station's record
/// changes (a shot, a loader's cycle starting, waiting, taking its rounds, reversing or done, the chosen kind, the magazine,
/// a refusal) and when the ship comes into range: like a door's, a loader's time at any tick follows from its record, so
/// nothing is sent while it runs. The mounts' yaw and pitch are the ship's parts (<see cref="ShipDetails"/>).
/// </summary>
[NetMessage(122)]
public partial struct ShipStations
{
    [VarInt] public uint Ship;
    [MaxCount(15)] public StationNet[] Stations;
}

/// <summary>One station's record: its chosen kind, shots so far, each barrel, each loader, the magazine's counts by kind, its last refusal.</summary>
[NetStruct]
public partial struct StationNet
{
    [Bits(4)] public byte Station;
    [Bits(2)] public byte Chosen;
    [VarInt] public uint Shots;
    public bool Limit;
    [MaxCount(8)] public StationBarrelNet[] Barrels;
    [MaxCount(8)] public LoaderNet[] Loaders;
    [MaxCount(3)] public MagazineCountNet[] Counts;
    [Bits(7)] public OutcomeCode Refused;
    [VarInt] public uint RefusedDetail;
    public uint RefusedTick;
}

/// <summary>A barrel: its round (0 empty) and its last shot's tick.</summary>
[NetStruct]
public partial struct StationBarrelNet
{
    [VarInt] public uint Chamber;
    public uint ShotTick;
}

/// <summary>A loader's record (IRON_WAKE.md §6.6): at work, waiting, in reverse, its time's bits at a tick, what it carries.</summary>
[NetStruct]
public partial struct LoaderNet
{
    public bool Active;
    public bool Waiting;
    public bool Reverse;
    public uint Since;
    public uint At;
    [VarInt] public uint Kind;
    [Bits(4)] public byte Count;
}

/// <summary>How many of one of the battery's kinds the magazine holds (the battery's ammo order).</summary>
[NetStruct]
public partial struct MagazineCountNet
{
    [VarInt] public uint Count;
}

/// <summary>
/// A crewed battery fired (IW3, IRON_WAKE.md §6.9; server → client, reliable ordered, to everyone the ship is relevant to):
/// the ship, its station and barrel, the shot's number, the tick and the round, the gunner. The gunner's client matches its
/// predicted shot by the number and plays nothing again; others play the shot. The rocket is the projectile messages'.
/// </summary>
[NetMessage(123)]
public partial struct TurretShot
{
    [VarInt] public uint Ship;
    [Bits(4)] public byte Station;
    [Bits(3)] public byte Barrel;
    [VarInt] public uint Number;
    public uint Tick;
    [VarInt] public uint Kind;
    [VarInt] public uint Gunner;
}

/// <summary>A firearm's magazine and attachments (G4), carried by its slot.</summary>
[NetStruct]
public partial struct GunNet
{
    [VarInt] public uint Loaded;
    [VarInt] public uint LoadedAmmo;
    [VarInt] public uint Sight;
    [VarInt] public uint Muzzle;
    [VarInt] public uint Rail;
}

/// <summary>One slot of a container.</summary>
[NetStruct]
public partial struct SlotNet
{
    public byte Index;
    [VarInt] public uint Item;
    [VarInt] public uint Count;
    [VarInt] public uint Condition;

    /// <summary>A firearm's magazine and attachments: one entry, or none for every other item.</summary>
    [MaxCount(1)] public GunNet[] Gun;
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

    /// <summary>Health still to bleed away, to heal and (G4) to burn, in whole points.</summary>
    public byte Bleeding;
    public byte Healing;
    public byte Burning;

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
    [Bits(4)] public TargetFamily Target;
    [Bits(2)] public HitRegion Region;
    [VarInt] public uint Damage;
    public bool Killed;
    public bool WeakSpot;

    /// <summary>The hit knocked the player down (G4).</summary>
    public bool Downed;
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
    public float Y;
    public float Z;
    [VarInt] public uint ReadySeconds;
}

/// <summary>Alive or dead; when dead, how, by whom, and where the player can respawn.</summary>
[NetMessage(80)]
public partial struct LifeState
{
    public bool Dead;

    /// <summary>Down (wounded, G4): how long until they get up or die, unless helped up first.</summary>
    public bool Wounded;
    [VarInt] public uint WoundedSeconds;
    [MaxLength(64)] public string Cause;
    [MaxLength(32)] public string KilledBy;
    [VarInt] public uint BeachReadySeconds;
    [MaxCount(32)] public BagNet[] Bags;
}

// ---------------------------------------------------------------- G4: guns and the wounded (GROUND.md §6.3, §6.4)

/// <summary>
/// A shot the client fired on one of its ticks (client → server, reliable ordered): its gun action sequence number,
/// the gun (belt slot and item), the view the player controlled (without recoil), whether aimed, and how far behind
/// its tick the player saw others. The server applies it on that tick; the client never says what it hit.
/// </summary>
[NetMessage(62)]
public partial struct FireIntent
{
    [VarInt] public uint Seq;
    public uint Tick;
    [Bits(3)] public byte Slot;
    [VarInt] public uint Item;

    /// <summary>The view the shot left along, octahedral (docs/WHOLE_WORLD.md §5).</summary>
    public ushort ViewU;
    public ushort ViewV;
    public bool Aimed;
    public byte ViewDelay;

    /// <summary>The frame the view is in: the ship the client believed its character aboard (its net ID), 0 the zone's (IRON_WAKE.md §4.11).</summary>
    [VarInt] public uint Frame;
}

/// <summary>A reload begun on a tick (its gun action sequence number): which rounds (0: the loaded kind, else any).</summary>
[NetMessage(63)]
public partial struct ReloadIntent
{
    [VarInt] public uint Seq;
    public uint Tick;
    [Bits(3)] public byte Slot;
    [VarInt] public uint Item;
    [VarInt] public uint Ammo;
}

/// <summary>Put an attachment (from a slot) on a firearm (in another slot) of the player's main slots or belt.</summary>
[NetMessage(64)]
public partial struct AttachIntent
{
    [VarInt] public uint Seq;
    [Bits(3)] public ContainerRef From;
    public byte Slot;
    [Bits(3)] public ContainerRef WeaponIn;
    public byte WeaponSlot;
    [VarInt] public uint Expected;
}

/// <summary>
/// The gun in the player's hand as the server has it (to the owner, reliable, when it changes: each shot or reload
/// applied, a reload done, an equip): the last gun action applied and the last refused (and why), the gun and its
/// magazine, and its cadence, recoil and reload in ticks, so the client's prediction starts again from exactly there.
/// </summary>
[NetMessage(81)]
public partial struct GunUpdate
{
    [VarInt] public uint LastSeq;
    [VarInt] public uint RefusedSeq;
    [Bits(7)] public OutcomeCode Refused;
    [Bits(3)] public byte Slot;
    [VarInt] public uint Item;
    [VarInt] public uint Loaded;
    [VarInt] public uint LoadedAmmo;
    [VarInt] public ulong EquipReadyTick;
    [VarInt] public ulong NextFire16;

    /// <summary>A reload under way (end 0: none).</summary>
    [VarInt] public ulong ReloadStartTick;
    [VarInt] public ulong ReloadEndTick;
    [VarInt] public uint ReloadAmmo;

    /// <summary>The recoil exactly (degrees), at the last shot (tick 0: none yet).</summary>
    [VarInt] public ulong ShotTick;
    public double RecoilPitch;
    public double RecoilYaw;
    public double StartPitch;
    public double StartYaw;
    public double Bloom;
    [Bits(6)] public ushort RecoilIndex;
}

// ---------------------------------------------------------------- G3: building intents (client → server, reliable)
// Each has a sequence number from the same series as G2's intents and is answered with an IntentResult (GROUND.md §6.3).

/// <summary>
/// A hammer action: on a piece, upgrade (to a grade), rotate, demolish; repair anything the hammer mends, named by its
/// family and part (a piece, a ship's door or battery, IRON_WAKE.md §5.5).
/// </summary>
[NetMessage(91)]
public partial struct HammerIntent
{
    [VarInt] public uint Seq;
    [Bits(2)] public HammerAction Action;
    public ulong Target;
    [Bits(3)] public byte Grade;
    [Bits(4)] public byte Family;
    public byte Part;
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
/// lock; what this player is told of it (GROUND_SYSTEMS.md §3.3: may pass, locked, its lock's code set, on its list,
/// theirs); its leaves' body IDs.
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
    [Bits(8)] public ViewerFlags Flags;
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
/// what this player is told of it (GROUND_SYSTEMS.md §3.3: on its list, theirs); its body; how long its store keeps the
/// base from decaying (minutes).
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
    [Bits(8)] public ViewerFlags Flags;
    [VarInt] public uint Body;
    [VarInt] public uint ProtectedMinutes;
}

[NetMessage(100)]
public partial struct BuildClaims
{
    [MaxCount(16)] public ClaimNet[] Claims;
}

// ---------------------------------------------------------------- G5: raiding (GROUND.md §6.6)

/// <summary>Throw the explosive in a belt slot along the view (a lob: gently), with its fuse lit.</summary>
[NetMessage(110)]
public partial struct ThrowIntent
{
    [VarInt] public uint Seq;
    [Bits(3)] public byte Slot;
    [VarInt] public uint Expected;

    /// <summary>The view it was thrown along, octahedral (docs/WHOLE_WORLD.md §5).</summary>
    public ushort ViewU;
    public ushort ViewV;
    public bool Lob;

    /// <summary>The frame the view is in: the ship the client believed its character aboard (its net ID), 0 the zone's (IRON_WAKE.md §4.11).</summary>
    [VarInt] public uint Frame;
}

/// <summary>
/// An explosive as the server has it (to everyone within range, reliable), when it comes into range and whenever it
/// changes: thrown, bounced, stuck, at rest, a dud, lit again, moved with a door. Flying, it is on the arc from
/// (X, Y, Z) at (VelocityX, …) at <c>StateTick</c> under the planet's gravity, which the client draws itself. Its fuse
/// ends at <c>FuseEndTick</c> (0: not burning) and was <c>FuseTicks</c> long.
/// </summary>
[NetMessage(113)]
public partial struct ExplosiveUpdate
{
    public ulong Id;
    [VarInt] public uint Item;
    [VarInt] public uint Owner;
    [Bits(2)] public ExplosiveState State;
    public bool Dud;
    public float X;
    public float Y;
    public float Z;
    public float VelocityX;
    public float VelocityY;
    public float VelocityZ;
    [Quantize(-1, 1, 12)] public float NormalX;
    [Quantize(-1, 1, 12)] public float NormalY;
    [Quantize(-1, 1, 12)] public float NormalZ;
    public uint StateTick;
    public uint FuseEndTick;
    [VarInt] public uint FuseTicks;
}

/// <summary>An explosive left the player's range, or went without a blast (a dud that lay too long).</summary>
[NetMessage(114)]
public partial struct ExplosiveGone
{
    public ulong Id;
}

/// <summary>
/// An explosion (to everyone within hearing, reliable; an explosive round's unreliable): where, facing which way, how
/// far it reaches, of what (the explosive or round), on what, and whose; <c>Id</c> is the explosive that went off (0 for
/// a rocket or round).
/// </summary>
[NetMessage(115)]
public partial struct Detonation
{
    public ulong Id;
    [VarInt] public uint Item;
    [VarInt] public uint Owner;
    public float X;
    public float Y;
    public float Z;
    [Quantize(-1, 1, 12)] public float NormalX;
    [Quantize(-1, 1, 12)] public float NormalY;
    [Quantize(-1, 1, 12)] public float NormalZ;
    [Quantize(0, 20, 10)] public float Radius;
    [Bits(4)] public TargetFamily Target;
    public bool Round;
}

/// <summary>
/// A fire (IW4, IRON_WAKE.md §7), to everyone within range (reliable), when it is lit or comes into range, and again when it
/// falls onto something else (what held it went): its ID; what left it (the round or explosive: its looks and sounds) and
/// whose; its middle (zone frame, now) and the surface's normal there; its radius; what holds it, and its middle's place and
/// turn in that holder's frame (+Y the surface's normal), so a client draws it riding a ship's deck; the server ticks it was
/// lit at and burns out at.
/// </summary>
[NetMessage(118)]
public partial struct FireStarted
{
    public ulong Id;
    [VarInt] public uint Item;
    [VarInt] public uint Owner;
    public float X;
    public float Y;
    public float Z;
    [Quantize(-1, 1, 12)] public float NormalX;
    [Quantize(-1, 1, 12)] public float NormalY;
    [Quantize(-1, 1, 12)] public float NormalZ;
    [Quantize(0, 20, 10)] public float Radius;
    public TargetNet Support;
    public float LocalX;
    public float LocalY;
    public float LocalZ;
    [Quantize(-1, 1, 16)] public float RotationX;
    [Quantize(-1, 1, 16)] public float RotationY;
    [Quantize(-1, 1, 16)] public float RotationZ;
    [Quantize(-1, 1, 16)] public float RotationW;
    public uint StartTick;
    public uint EndTick;
}

/// <summary>A fire went out (Out: burnt out, doused, replaced, fell to nothing; its scorch is left) or left the player's range.</summary>
[NetMessage(119)]
public partial struct FireEnded
{
    public ulong Id;
    public bool Out;
}

/// <summary>A raid alert: which base (its claim device), where it is (zone frame), when it was hit (Unix ms), how often, what broke and with what.</summary>
[NetStruct]
public partial struct RaidAlertNet
{
    public ulong Base;
    public float X;
    public float Y;
    public float Z;
    [VarInt] public ulong FirstUnixMs;
    [VarInt] public ulong LastUnixMs;
    [VarInt] public uint Hits;
    [VarInt] public uint Destroyed;
    [MaxLength(160)] public string What;
    [VarInt] public uint Item;
}

/// <summary>A base the player is authorised on is being raided (to them, online, reliable).</summary>
[NetMessage(116)]
public partial struct RaidAlertMessage
{
    public RaidAlertNet Alert;
}

/// <summary>The raid alerts that waited while the player was offline (at login, oldest first).</summary>
[NetMessage(117)]
public partial struct RaidAlertList
{
    [MaxCount(64)] public RaidAlertNet[] Alerts;
}

// ---------------------------------------------------------------- G7: the world (GROUND.md §6.8)
// Intents (client → server, the general sequence, answered by IntentResult). A blueprint is learnt with UseIntent; a
// recycler or research table switched, a tech tree's node unlocked, a team's invite and "authorise team" are verbs
// (InteractIntent).

/// <summary>A team action: create; accept or decline a team's invite (Team); leave; kick or promote a member (Target).</summary>
[NetMessage(131)]
public partial struct TeamIntent
{
    [VarInt] public uint Seq;
    [Bits(3)] public TeamAction Action;
    public ulong Target;
    public ulong Team;
}

/// <summary>A chat line to the player's team alone.</summary>
[NetMessage(132)]
public partial struct TeamChatSend
{
    [MaxLength(200)] public string Text;
}

/// <summary>Put a marker on the map (a point of the zone frame, a colour, a label), or take one off (Index; −1: all).</summary>
[NetMessage(133)]
public partial struct MarkerIntent
{
    [VarInt] public uint Seq;
    public bool Remove;
    [Signed(8)] public sbyte Index;

    /// <summary>The point on the world (the server settles it on the ground).</summary>
    public float X;
    public float Y;
    public float Z;
    [Bits(3)] public byte Colour;
    [MaxLength(24)] public string Label;
}

// The player's own (server → owner, reliable unless said otherwise).

/// <summary>A recipe (its output item's ID).</summary>
[NetStruct]
public partial struct RecipeNet
{
    [VarInt] public uint Recipe;
}

/// <summary>Every blueprint the player has learnt, when that changes (and at joining).</summary>
[NetMessage(140)]
public partial struct BlueprintList
{
    [VarInt] public uint LastSeq;
    [MaxCount(1024)] public RecipeNet[] Recipes;
}

[NetStruct]
public partial struct TeamMemberNet
{
    public ulong Id;
    [MaxLength(32)] public string Name;
    public bool Online;
}

/// <summary>The player's team (Team 0: none): its leader and its members, oldest first.</summary>
[NetMessage(141)]
public partial struct TeamState
{
    [VarInt] public uint LastSeq;
    public ulong Team;
    public ulong Leader;
    [MaxCount(16)] public TeamMemberNet[] Members;
}

[NetStruct]
public partial struct TeamInviteNet
{
    public ulong Team;
    [MaxLength(32)] public string From;
    [VarInt] public uint Seconds;
}

/// <summary>The team invites waiting for the player (when they change).</summary>
[NetMessage(142)]
public partial struct TeamInvites
{
    [MaxCount(16)] public TeamInviteNet[] Invites;
}

/// <summary>A teammate where they are (zone frame), facing, and how they are.</summary>
[NetStruct]
public partial struct TeammateNet
{
    public ulong Id;
    public float X;
    public float Y;
    public float Z;
    [Angle] public ushort Yaw;
    [Bits(2)] public TeammateState State;
}

/// <summary>Where the player's teammates are (unreliable, twice a second; the newest Sequence wins), for the map and name tags beyond range.</summary>
[NetMessage(143)]
public partial struct TeamPositions
{
    public uint Sequence;
    [MaxCount(16)] public TeammateNet[] Members;
}

[NetStruct]
public partial struct MarkerNet
{
    public float X;
    public float Y;
    public float Z;
    [Bits(3)] public byte Colour;
    [MaxLength(24)] public string Label;
}

[NetStruct]
public partial struct RespawnPointNet
{
    public ulong Id;
    public float X;
    public float Y;
    public float Z;
    public bool Bed;
}

/// <summary>What the player's map shows of their own: their markers, where they died last, their sleeping bags and beds.</summary>
[NetMessage(144)]
public partial struct MapState
{
    [VarInt] public uint LastSeq;
    [MaxCount(8)] public MarkerNet[] Markers;
    public bool HasDeath;
    public float DeathX;
    public float DeathY;
    public float DeathZ;
    [MaxCount(64)] public RespawnPointNet[] Points;
}

/// <summary>The recycler or research table the player is looting: running, how far through (0 … 255), seconds left, what it researches.</summary>
[NetMessage(145)]
public partial struct MachineState
{
    public ulong Target;
    public bool Running;
    public byte Progress;
    [VarInt] public uint SecondsLeft;
    [VarInt] public uint Item;
}

// ---------------------------------------------------------------- the F1 console (GROUND.md §8.2)

/// <summary>A line typed in the F1 console for the server to run as this player (client → server, reliable ordered).</summary>
[NetMessage(150)]
public partial struct ConsoleRun
{
    [VarInt] public uint Seq;
    [MaxLength(200)] public string Text;
}

/// <summary>A line of the server's answer to a console command (its Seq), or a line the server sends by itself (Seq 0).</summary>
[NetMessage(151)]
public partial struct ConsoleReply
{
    [VarInt] public uint Seq;
    [Bits(2)] public ConsoleReplyKind Kind;
    [MaxLength(400)] public string Text;
}

/// <summary>A player on the server, for the console's lists.</summary>
[NetStruct]
public partial struct ServerPlayerNet
{
    [MaxLength(32)] public string Name;
    public bool Admin;
}

/// <summary>What the console knows of the server: the receiver's rights and who is on (at joining, and when either changes).</summary>
[NetMessage(152)]
public partial struct ServerState
{
    public bool Admin;
    [VarInt] public uint MaxPlayers;
    [MaxCount(256)] public ServerPlayerNet[] Players;
}

/// <summary>
/// The zone's day (GROUND.md §8.2): the planet's spin as turns since J2000 at a universe time, and how many times faster
/// than real it turns from there (0 stops it). An admin's <c>time</c> and <c>timescale</c> change it for this zone alone;
/// orbits, seasons and universe time stay as they are. Sent at joining and when it changes.
/// </summary>
[NetMessage(153)]
public partial struct ZoneClock
{
    public double BaseTime;
    public double BaseTurns;
    public double Scale;
    public bool Overridden;
}

// ---------------------------------------------------------------- interaction (GROUND_SYSTEMS.md §5)

/// <summary>
/// The one interaction intent (GROUND_SYSTEMS.md §5): a verb (interactions.json's number) done to a target, with a number
/// (the tech tree's recipe) and a code (a code lock's four digits) where the verb takes them. The server resolves the
/// target, checks the verb's gates, the reach and sight, and that the target offers the verb to this player now, and
/// answers with <see cref="IntentResult"/>.
/// </summary>
[NetMessage(210)]
public partial struct InteractIntent
{
    [VarInt] public uint Seq;
    public TargetNet Target;
    [VarInt] public uint Verb;
    [VarInt] public uint Arg;
    [MaxLength(8)] public string Code;
}

/// <summary>
/// A spent arrow (GROUND_SYSTEMS.md §5): what it is (its item), what holds it (a thing, a door's leaf, a body's bone; the
/// ground) and its tip's place and turn in that holder's frame, the shaft along its +X. A client draws it where its holder
/// is now, so it rides a moving body.
/// </summary>
[NetStruct]
public partial struct ArrowNet
{
    public ulong Id;
    [VarInt] public uint Item;
    public TargetNet Support;
    public float X;
    public float Y;
    public float Z;
    [Quantize(-1, 1, 16)] public float RotationX;
    [Quantize(-1, 1, 16)] public float RotationY;
    [Quantize(-1, 1, 16)] public float RotationZ;
    [Quantize(-1, 1, 16)] public float RotationW;
}

/// <summary>Spent arrows that came within range, or changed (one fell when what held it went), to everyone in range.</summary>
[NetMessage(211)]
public partial struct ArrowStates
{
    [MaxCount(64)] public ArrowNet[] Arrows;
}

/// <summary>Spent arrows gone (taken, their time up) or out of range.</summary>
[NetMessage(212)]
public partial struct ArrowsGone
{
    [MaxCount(255)] public BuildIdNet[] Ids;
}

// ---------------------------------------------------------------- destruction (GROUND.md §6.4.2)

/// <summary>
/// Something was destroyed (to everyone within range, reliable): a deployable or world prop broken, a building piece, door
/// or claim device broken, brought down, decayed or demolished, a ship wrecked. What it was (the thing: its family and ID;
/// its model key and a piece's grade), where it stood as it was drawn (zone frame; a door's frame piece's, open or not),
/// the blow's direction (zero for a collapse, decay or demolition), what did it, who (a character's net ID, 0 if none or
/// gone) and with what (an item).
/// </summary>
[NetMessage(165)]
public partial struct ThingDestroyed
{
    public TargetNet Target;
    [MaxLength(48)] public string Key;
    [Bits(3)] public byte Grade;
    public float X;
    public float Y;
    public float Z;
    [Quantize(-1, 1, 16)] public float RotationX;
    [Quantize(-1, 1, 16)] public float RotationY;
    [Quantize(-1, 1, 16)] public float RotationZ;
    [Quantize(-1, 1, 16)] public float RotationW;
    [Quantize(-1, 1, 12)] public float DirectionX;
    [Quantize(-1, 1, 12)] public float DirectionY;
    [Quantize(-1, 1, 12)] public float DirectionZ;
    [Bits(4)] public DestroyCause Cause;
    public bool Open;
    [VarInt] public uint Attacker;
    [VarInt] public uint Item;
}

// ---------------------------------------------------------------- P2: placing anything (GROUND_SYSTEMS.md §6)

/// <summary>A pose on the wire: a point in the zone frame and a turn about up (in the characters' convention).</summary>
[NetStruct]
public partial struct PoseNet
{
    public float X;
    public float Y;
    public float Z;
    [Angle] public ushort Yaw;
}

/// <summary>
/// Place anything (GROUND_SYSTEMS.md §6; one intent for deployables, building pieces, doors, locks, the claim device,
/// charges on a surface and ships): the slot that pays for it (the item used up, or the building plan carried) and the
/// item the client saw there; the placeable (placement.json's number); what holds it, the socket it snaps to, where it goes
/// and its turn, and its variant (a piece's orientation or a free foundation's heading, a door's side, a turn in its
/// support's steps). The server works the placement out again from these and takes it only within the rule's tolerance of
/// its own; answered by <see cref="IntentResult"/>.
/// </summary>
[NetMessage(220)]
public partial struct PlaceIntent
{
    [VarInt] public uint Seq;
    [Bits(3)] public ContainerRef From;
    public byte Slot;
    [VarInt] public uint Expected;
    [VarInt] public uint Placeable;
    public TargetNet Support;
    public byte Socket;
    public PoseNet Pose;
    public byte Variant;
}

// ---------------------------------------------------------------- P3: held items and inventory actions (GROUND_SYSTEMS.md §7)

/// <summary>
/// A button of the item panel's ACTIONS on a stack in the player's own slots (client → server, the general sequence and
/// budget, answered by <see cref="IntentResult"/>): its action's number in items.json's list (eat, use, wear, take off,
/// learn, unload, take an attachment off) and its argument (which attachment). Replaces <c>UseIntent</c> and
/// <c>DetachIntent</c>.
/// </summary>
[NetMessage(230)]
public partial struct ItemActionIntent
{
    [VarInt] public uint Seq;
    [Bits(3)] public ContainerRef From;
    public byte Slot;
    [VarInt] public uint Expected;
    [VarInt] public uint Action;
    [VarInt] public uint Arg;
}

/// <summary>
/// UNLOAD of the gun in hand (client → server, the gun's channel and sequence, like <see cref="ReloadIntent"/>): its rounds
/// back into the inventory on that tick, in order with the gun's shots and reloads (GROUND_SYSTEMS.md §7). A gun elsewhere
/// is unloaded by an <see cref="ItemActionIntent"/>.
/// </summary>
[NetMessage(231)]
public partial struct UnloadIntent
{
    [VarInt] public uint Seq;
    public uint Tick;
    [Bits(3)] public byte Slot;
    [VarInt] public uint Item;
}

// ---------------------------------------------------------------- the developer panel (GROUND.md §8.5)

/// <summary>
/// A player watches the server through the F2 panel (client → server, reliable ordered): on or off, and which extras they
/// want (the animals' nav grid around them, the spawn areas near them, the server's log). The server answers an admin
/// only (GROUND.md §8.2.2); anyone else is ignored.
/// </summary>
[NetMessage(155)]
public partial struct DevWatch
{
    public bool On;
    public bool Nav;
    public bool Areas;
    public bool Log;
}

/// <summary>One section of the tick's profile (GROUND.md §5.11) over the last stats interval: its average and its longest tick.</summary>
[NetStruct]
public partial struct DevSectionNet
{
    [Bits(6)] public ProfileSection Section;
    public float AverageMs;
    public float WorstMs;
}

/// <summary>A player on the server, for the PLAYERS tab.</summary>
[NetStruct]
public partial struct DevPlayerNet
{
    [MaxLength(32)] public string Name;
    public bool Admin;

    /// <summary>0 logging in, 1 alive, 2 wounded, 3 dead.</summary>
    [Bits(2)] public byte State;
    [VarInt] public uint PingMs;
    public float X;
    public float Y;
    public float Z;
    [Bits(7)] public byte OnTimePercent;
    [VarInt] public uint InView;
    public float KilobitsDown;
    public float KilobitsUp;
}

/// <summary>
/// The server as the F2 panel's SERVER and PLAYERS tabs show it (server → the watching admins, every half second): the
/// tick over the last interval, the profile's sections, the garbage collector, what the zone holds, the game's counters
/// since the server started, the process, and everyone on.
/// </summary>
[NetMessage(156)]
public partial struct DevStats
{
    public uint Tick;
    public byte TickRate;
    public float IntervalSeconds;
    public float AverageMs;
    public float WorstMs;
    [VarInt] public uint Overruns;
    public float GcPauseMs;
    [VarInt] public uint AllocatedKbPerTick;
    [VarInt] public uint UpSeconds;
    [VarInt] public uint WorkingSetMb;
    [VarInt] public uint HeapMb;
    public bool Saves;
    [VarInt] public uint SinceSaveSeconds;
    public float RelevanceRadius;

    // What the zone holds (the console's ents, as numbers).
    [VarInt] public uint Characters;
    [VarInt] public uint Asleep;
    [VarInt] public uint Dead;
    [VarInt] public uint Wounded;
    [VarInt] public uint AnimalsAlive;
    [VarInt] public uint AnimalsAwake;
    [VarInt] public uint AnimalsDead;
    [VarInt] public uint Deployables;
    [VarInt] public uint Props;
    [VarInt] public uint Bags;
    [VarInt] public uint Corpses;
    [VarInt] public uint Pieces;
    [VarInt] public uint Doors;
    [VarInt] public uint Claims;
    [VarInt] public uint Collapsing;
    [VarInt] public uint Ships;
    [VarInt] public uint Explosives;
    [VarInt] public uint Arrows;
    [VarInt] public uint NaturalChanged;
    [VarInt] public uint Tiles;
    [VarInt] public uint TilesOnDemand;
    [VarInt] public uint NetEntities;

    // The game's counters since the server started (GameCounters).
    [VarInt] public uint GatherHits;
    [VarInt] public uint Gathered;
    [VarInt] public uint PickedUp;
    [VarInt] public uint Crafted;
    [VarInt] public uint ArrowsShot;
    [VarInt] public uint Shots;
    [VarInt] public uint BulletHits;
    [VarInt] public uint CharacterHits;
    [VarInt] public uint Kills;
    [VarInt] public uint Downed;
    [VarInt] public uint Recovered;
    [VarInt] public uint Deaths;
    [VarInt] public uint AnimalHits;
    [VarInt] public uint AnimalKills;

    [MaxCount(32)] public DevSectionNet[] Sections;
    [MaxCount(256)] public DevPlayerNet[] Players;
}

/// <summary>The F2 panel's LOOK tab asks what a target is (client → server, reliable ordered; at most four a second).</summary>
[NetMessage(157)]
public partial struct DevInspect
{
    public TargetNet Target;
}

/// <summary>A fact of a described target, or a line of the log: a label and its value.</summary>
[NetStruct]
public partial struct DevLineNet
{
    [MaxLength(40)] public string Label;
    [MaxLength(200)] public string Value;
}

/// <summary>
/// The server's description of a target (server → the asking admin): what its family resolves it to, every public
/// property of that object, and what the rules make of it for this player (GROUND.md §8.5).
/// </summary>
[NetMessage(158)]
public partial struct DevInspected
{
    public TargetNet Target;
    [MaxCount(64)] public DevLineNet[] Lines;
}

/// <summary>One cell of the nav grid window: bit 0 under water, bit 1 blocked, bit 2 too steep for every animal, bit 3 not built yet.</summary>
[NetStruct]
public partial struct DevCellNet
{
    [Bits(4)] public byte Flags;
}

/// <summary>
/// The animals' nav grid around the watcher (server → the watching admin, once a second while wanted; GROUND.md §6.9): a
/// square of <see cref="Cells"/> × <see cref="Cells"/> cells of 2 m laid flat on the ground round a point
/// (<see cref="CentreX"/>, <see cref="CentreY"/>, <see cref="CentreZ"/>), columns running east and rows south from its
/// north-west corner (docs/WHOLE_WORLD.md §7.3), each cell as its flags.
/// </summary>
[NetMessage(159)]
public partial struct DevNav
{
    public float CentreX;
    public float CentreY;
    public float CentreZ;
    public byte Cells;
    [MaxCount(4096)] public DevCellNet[] Flags;
}

/// <summary>A slot of a spawn area: its species, its herd's size, how many live, its home and its range.</summary>
[NetStruct]
public partial struct DevSlotNet
{
    [MaxLength(32)] public string Species;
    [VarInt] public uint Size;
    [VarInt] public uint Living;
    public float HomeX;
    public float HomeY;
    public float HomeZ;
    public float Range;
}

/// <summary>A spawn area of the animals (GROUND.md §6.9): its centre, whether it is active, and its slots.</summary>
[NetStruct]
public partial struct DevAreaNet
{
    public float CentreX;
    public float CentreY;
    public float CentreZ;
    public bool Active;
    [MaxCount(16)] public DevSlotNet[] Slots;
}

/// <summary>The spawn areas near the watcher (server → the watching admin, every two seconds while wanted).</summary>
[NetMessage(160)]
public partial struct DevAreas
{
    [MaxCount(32)] public DevAreaNet[] Areas;
}

/// <summary>Lines of the server's log (server → the watching admins who asked: the backlog at first, then as they come), the time as the label.</summary>
[NetMessage(161)]
public partial struct DevLog
{
    [MaxCount(32)] public DevLineNet[] Lines;
}

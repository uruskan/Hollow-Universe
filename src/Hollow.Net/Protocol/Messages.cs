using Hollow.Simulation;
using Hollow.Simulation.Building;
using Hollow.Simulation.Combat;
using Hollow.Simulation.Content;
using Hollow.Simulation.Items;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Players;
using Hollow.Simulation.Ships;
using Hollow.Simulation.Raid;
using Hollow.Simulation.Teams;
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
/// A ship as everyone sees it (G6): position on a 1/256 m grid, its turn as yaw, pitch and roll (about the zone's +Y,
/// then its own x, then its own z), what it is doing, its hatch, its engines' output (±127 = ±2 of each axis's thrust,
/// boost above 1), its hull (of 255) and its kind (the item).
/// </summary>
[NetState]
public partial struct ShipNetState
{
    [Signed(32)] public int X;
    [Signed(32)] public int Y;
    [Signed(32)] public int Z;
    [Angle] public ushort Yaw;
    [Angle] public ushort Pitch;
    [Angle] public ushort Roll;
    [Bits(12)] public ShipNetFlags Flags;
    [Bits(5)] public byte Hatch;
    [Signed(8)] public sbyte ThrustX;
    [Signed(8)] public sbyte ThrustY;
    [Signed(8)] public sbyte ThrustZ;
    public byte Health;
    [VarInt] public uint Kind;
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
}

/// <summary>A projectile stopped, at a point, on something (nothing: out of range or time in the air).</summary>
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

/// <summary>
/// A ship action (G6, client → server, reliable ordered, the general intent sequence and budget, answered by
/// <see cref="IntentResult"/>): the hatch, boarding, stepping out, the lock, refuelling, descending from orbit.
/// </summary>
[NetMessage(120)]
public partial struct ShipIntent
{
    [VarInt] public uint Seq;
    public ulong Ship;
    [Bits(3)] public ShipAction Action;
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
    [Bits(3)] public HitTarget Target;
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
    [Angle] public ushort Yaw;
    [Angle] public ushort Pitch;
    public bool Aimed;
    public byte ViewDelay;
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

/// <summary>Take the attachment in a slot (sight, muzzle, rail) off a firearm, into the inventory.</summary>
[NetMessage(65)]
public partial struct DetachIntent
{
    [VarInt] public uint Seq;
    [Bits(3)] public ContainerRef WeaponIn;
    public byte WeaponSlot;
    [Bits(2)] public AttachmentSlot Mod;
}

/// <summary>Help a wounded player up (the interact key on them).</summary>
[NetMessage(66)]
public partial struct HelpUpIntent
{
    [VarInt] public uint Seq;
    public ulong Target;
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
    [Bits(4)] public GunResult Refused;
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

// ---------------------------------------------------------------- G5: raiding (GROUND.md §6.6)

/// <summary>Throw the explosive in a belt slot along the view (a lob: gently), with its fuse lit.</summary>
[NetMessage(110)]
public partial struct ThrowIntent
{
    [VarInt] public uint Seq;
    [Bits(3)] public byte Slot;
    [VarInt] public uint Expected;
    [Angle] public ushort Yaw;
    [Angle] public ushort Pitch;
    public bool Lob;
}

/// <summary>Place the charge in a belt slot on the surface the view meets within reach.</summary>
[NetMessage(111)]
public partial struct PlaceChargeIntent
{
    [VarInt] public uint Seq;
    [Bits(3)] public byte Slot;
    [VarInt] public uint Expected;
    [Angle] public ushort Yaw;
    [Angle] public ushort Pitch;
}

/// <summary>Light a dud again (the interact key on it).</summary>
[NetMessage(112)]
public partial struct RearmIntent
{
    [VarInt] public uint Seq;
    public ulong Target;
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
    [Bits(3)] public HitTarget Target;
    public bool Round;
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
// Intents (client → server, the general sequence, answered by IntentResult). A blueprint is learnt with UseIntent, a
// recycler or research table started with ToggleIntent, "authorise team" is ClaimIntent's and LockIntent's.

/// <summary>Unlock a tech tree node (its recipe) at a workbench of its level within reach, for its salvage.</summary>
[NetMessage(130)]
public partial struct UnlockIntent
{
    [VarInt] public uint Seq;
    [VarInt] public uint Recipe;
}

/// <summary>A team action: create; invite a player near by (Target); accept or decline a team's invite (Team); leave; kick or promote a member (Target).</summary>
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
    public float X;
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

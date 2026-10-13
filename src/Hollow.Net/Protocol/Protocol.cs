namespace Hollow.Net.Protocol;

/// <summary>Constants of the ground game's protocol (GROUND.md §4.4).</summary>
public static partial class Protocol
{
    /// <summary>
    /// Raised by hand for changes the schema hash can't see (meaning, not layout). Checked at connect. 3: the zone on the
    /// real planet (stand-in flag, day scale); 4: G4, guns and the wounded; 5: G3, building over the network (and the
    /// integration package's 3 + 4 together); 6: G5, raiding (explosives, detonations, raid alerts); 7: all of them
    /// together on the real planet; 8: G6, ships; 9: G7, the world (world props, blueprints and tech trees, teams, the
    /// map); 10: G7, animals (entities of kind <see cref="EntityKind.Animal"/>, whose snapshot state means what
    /// <see cref="AnimalNet"/> says, and <c>HitTarget.Animal</c>); 11: the F1 console (its commands and answers, the
    /// server's state for it, the zone's clock, and god and noclip in the player's own state); 12: dropped items lie in
    /// bags (a thing of kind <c>StaticKind.DroppedBag</c> shows no item, its count is how many stacks it holds, and it
    /// opens as take-only loot, <c>LootKind.Crate</c>); 13: gathering's feedback (a natural object's change carries its run
    /// of weak-spot hits, how fresh its last hit is and where it met the surface); 14: the damage pipeline's Destroyed
    /// event (<see cref="ThingDestroyed"/>, GROUND.md §6.4.2); 15: free look (Alt), the head's turn from the view in every
    /// input and in characters' state; 16: the shared foundations (GROUND_SYSTEMS.md §3): one answer for every intent
    /// (<see cref="IntentResult"/>'s code and detail; <c>BuildResult</c> is gone), things named by
    /// <see cref="TargetNet"/> (family, ID, part) and hit kinds by target family, and viewer flags; 17: held items, item
    /// information and inventory actions (GROUND_SYSTEMS.md §7): one item action intent for every inventory button
    /// (<see cref="ItemActionIntent"/>; <c>UseIntent</c> and <c>DetachIntent</c> are gone), the gun's unload on its own
    /// timeline (<see cref="UnloadIntent"/>) and its answers in the one answer list (<see cref="GunUpdate"/>'s code); 18:
    /// placing anything is one <see cref="PlaceIntent"/> (GROUND_SYSTEMS.md §6: the deployables', pieces', charges' and
    /// doors', locks' and the claim device's own placing gone), and a thing says what it stands on (<see cref="ThingSpawn"/>).
    /// </summary>
    public const uint Version = 18;

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

    /// <summary>A ship (G6): its state rides in the snapshots' ship section.</summary>
    Ship,

    /// <summary>An animal (G7, GROUND.md §6.9): its <c>Spawn</c> names its species; its state is packed by <see cref="AnimalNet"/>.</summary>
    Animal,
}

/// <summary>
/// What a ship's state says it is doing (G6). Whether it is locked and the receiver's own are its viewer flags
/// (<c>ShipNetState.Viewer</c>, GROUND_SYSTEMS.md §3.3).
/// </summary>
[System.Flags]
public enum ShipNetFlags : ushort
{
    None = 0,
    Piloted = 1,
    HatchOpen = 2,
    Landed = 4,
    Destroyed = 8,
    InOrbit = 16,
    Boosting = 32,
    EnginesOn = 64,
    AssistOff = 128,
    Wet = 256,
    Asleep = 512,
}

/// <summary>A ship action (<see cref="ShipIntent"/>).</summary>
public enum ShipAction : byte
{
    Hatch,
    Board,
    Exit,
    Lock,
    Refuel,
    Descend,
}

/// <summary>What a line of the console's answer is (GROUND.md §8.2): plain output, a refusal or error, or something done.</summary>
public enum ConsoleReplyKind : byte
{
    Output,
    Error,
    Success,
}

public enum ChatKind : byte
{
    Player,
    Server,

    /// <summary>G7: a line to the sender's team alone.</summary>
    Team,
}

/// <summary>How a teammate is (G7): up and about, asleep (logged off), down (wounded) or dead.</summary>
public enum TeammateState : byte
{
    Awake,
    Asleep,
    Down,
    Dead,
}


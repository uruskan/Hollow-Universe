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
    /// doors', locks' and the claim device's own placing gone), and a thing says what it stands on (<see cref="ThingSpawn"/>); 19:
    /// interaction (GROUND_SYSTEMS.md §5): one <see cref="InteractIntent"/> for every verb (Open, PickUp, Toggle, HelpUp,
    /// Rearm and Unlock gone; with 18's placing, nothing is left of Door, Lock and Claim; Team doesn't invite, Ship is the
    /// pilot's keys alone), spent arrows (<see cref="ArrowStates"/>, <see cref="ArrowsGone"/>, the Arrow target family), a
    /// thing's capabilities; 20: the F2 developer panel; 21: S3, the whole world in one zone (views and turns that don't break
    /// at a pole); 22: IW1, ship spaces (a frame with every view and character state, IRON_WAKE.md §12); 23: IW4,
    /// fires (<see cref="FireStarted"/>, <see cref="FireEnded"/>, the Fire target family, a thing destroyed by fire) and
    /// rockets that drop by their own gravity (IRON_WAKE.md §7); 24: S4, the zone's ground
    /// (<see cref="ZoneInfo.Ground"/>: the stand-in or the walking-scale ground, docs/WALKING_GROUND.md); 25: IW2, seats,
    /// parts and doors (IRON_WAKE.md §5.7): a ship at rest and slow as the server's word (<see cref="ShipNetFlags.Still"/>,
    /// <see cref="ShipNetFlags.Slow"/>), the seat in the own state, <see cref="ShipDetails"/> (doors, seats, health), a
    /// seated player's keys, the hammer's family and part; 26: IW5, building aboard (IRON_WAKE.md §8): a thing whose
    /// support is a ship comes in its pose in the ship's frame (<see cref="ThingSpawn"/>), and a placement claimed on a ship
    /// in it (<see cref="PlaceIntent"/>);
    /// 27: IW3, crewed batteries (IRON_WAKE.md §6): a gunner's controls
    /// in the input (<see cref="StationControls"/>), the stations' records (<see cref="ShipStations"/>), shots
    /// (<see cref="TurretShot"/>), a battery's rocket named in its launch, a seated gunner's Tab (<see cref="ShipAction.Magazine"/>).
    /// </summary>
    public const uint Version = 27;

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

    /// <summary>Slow and low enough for a door that opens "slow" (the ramp, IRON_WAKE.md §5.3): the server's word (<c>ShipSystem.ShipSlow</c>).</summary>
    Slow = 2,
    Landed = 4,
    Destroyed = 8,
    InOrbit = 16,
    Boosting = 32,
    EnginesOn = 64,
    AssistOff = 128,
    Wet = 256,
    Asleep = 512,

    /// <summary>
    /// At rest as the server judges it (<c>ShipSystem.ShipStill</c>: landed, asleep, or barely moving near the ground or
    /// afloat): its hatch opens, its seat is reached and its cargo opened only then. One rule on both sides: a client
    /// told only landed and asleep refused what the server allowed (the F1-kill finding, KNOWN_PROBLEMS.md).
    /// </summary>
    Still = 1024,
}

/// <summary>
/// A seated occupant's key (<see cref="ShipIntent"/>, IRON_WAKE.md §5.2): a door from a seat that works them (its index in
/// <c>ShipIntent.Arg</c>), leaving the seat, the lock, descending from orbit; on foot a ship's doors, seats, tank, list and
/// cargo are verbs.
/// </summary>
public enum ShipAction : byte
{
    Door,
    Leave,
    Lock,
    Descend,

    /// <summary>A gunner opens their station's magazine (Tab, IRON_WAKE.md §6.5).</summary>
    Magazine,
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


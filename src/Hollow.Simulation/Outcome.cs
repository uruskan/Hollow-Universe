namespace Hollow.Simulation;

/// <summary>
/// The one answer vocabulary (GROUND_SYSTEMS.md §3.2): every action a player asks for is done, or refused with one of
/// these codes, whatever system it went to (inventory, crafting, building, raiding, ships, the world). texts.json has the
/// player's line for each (the content check makes sure every code has one). The value is the wire number
/// (<c>IntentResult.Code</c>, 7 bits): append only, never reorder.
/// </summary>
public enum OutcomeCode : byte
{
    Done = 0,

    // ---------------------------------------------------------------- any action

    /// <summary>Malformed: a slot, amount, item, recipe, piece, socket, grade or target that can't be.</summary>
    Invalid = 1,

    /// <summary>The slot no longer holds what the client expected (someone else got there first).</summary>
    Stale = 2,

    /// <summary>Over the player's action budget.</summary>
    RateLimited = 3,

    /// <summary>Not possible while dead or down (or, for a respawn, while alive).</summary>
    Dead = 4,

    /// <summary>The target is gone (or never was).</summary>
    NotFound = 5,

    /// <summary>Too far from the target's nearest surface (GROUND_SYSTEMS.md §3.4).</summary>
    OutOfReach = 6,

    /// <summary>Something stands between the eye and the target.</summary>
    OutOfSight = 7,

    /// <summary>That slot or container doesn't take that item (a clothing layer worn already, a take-only box).</summary>
    NotAllowed = 8,

    /// <summary>The item can't be used that way (eating a rock).</summary>
    NotUsable = 9,

    /// <summary>No room where it should go.</summary>
    NoSpace = 10,

    /// <summary>Not enough of something: <see cref="Outcome.Detail"/> names the item when one is known.</summary>
    NotEnough = 11,

    /// <summary>Not ready yet: a respawn point or a throw cooling down.</summary>
    Cooldown = 12,

    /// <summary>Locked: a door's lock, or a ship only its owner opens.</summary>
    Locked = 13,

    /// <summary>Only its owner may do that.</summary>
    NotOwner = 14,

    /// <summary>The player doesn't carry the tool the action needs: <see cref="Outcome.Detail"/> is that item.</summary>
    NoTool = 15,

    // ---------------------------------------------------------------- crafting

    /// <summary>A recipe (or tech tree node) needs a workbench of a level (<see cref="Outcome.Detail"/>) within reach.</summary>
    NeedsWorkbench = 16,

    UnknownRecipe = 17,

    /// <summary>The crafting queue holds eight jobs already.</summary>
    QueueFull = 18,

    /// <summary>The recipe is made at a station (<see cref="Outcome.Detail"/>: its item) that isn't within reach.</summary>
    NeedsStation = 19,

    // ---------------------------------------------------------------- containers and devices

    /// <summary>No container of that kind is open.</summary>
    NotOpen = 20,

    /// <summary>A campfire or furnace with no fuel in it.</summary>
    NoFuel = 21,

    /// <summary>A machine can't start now (a recycler with nothing to take apart, a research table without an item or salvage).</summary>
    CantStart = 22,

    // ---------------------------------------------------------------- placing

    /// <summary>Something is in the way: a placement overlaps a thing, a piece meets an object.</summary>
    Blocked = 23,

    TooSteep = 24,

    /// <summary>Under water, outside the zone, or not on the ground.</summary>
    BadPlacement = 25,

    // ---------------------------------------------------------------- ships (G6)

    /// <summary>Someone is in the ship's seat already.</summary>
    SeatTaken = 26,

    /// <summary>Not while the ship is flying: land first.</summary>
    NotLanded = 27,

    /// <summary>The ship's hatch is shut: open it first.</summary>
    HatchShut = 28,

    // ---------------------------------------------------------------- the world (G7)

    /// <summary>The blueprint is known already.</summary>
    AlreadyKnown = 29,

    /// <summary>A tech tree node whose nodes before it aren't known yet.</summary>
    NeedsEarlierNode = 30,

    /// <summary>The player is in a team already.</summary>
    InTeam = 31,

    NotLeader = 32,

    TeamFull = 33,

    /// <summary>No such team, invite or member (or the invite ran out).</summary>
    NoTeam = 34,

    // ---------------------------------------------------------------- building (G3)

    NoSocket = 35,
    NeedsFoundation = 36,

    /// <summary>Someone else's tool cupboard covers the place.</summary>
    BuildingBlocked = 37,

    /// <summary>Something is already built there.</summary>
    Occupied = 38,
    Floating = 39,
    Buried = 40,
    HitsPiece = 41,
    HitsGround = 42,
    PlayerInTheWay = 43,

    /// <summary>Not stable enough: <see cref="Outcome.Detail"/> is the stability it would have, in percent.</summary>
    Unstable = 44,
    TooManyPieces = 45,
    JoinsClaims = 46,

    /// <summary>No building rights: not on the tool cupboard's list.</summary>
    NotAuthorised = 47,
    EditWindowOver = 48,
    NotHigherGrade = 49,

    /// <summary>Recently damaged: <see cref="Outcome.Detail"/> is the seconds until it can be repaired.</summary>
    RecentlyDamaged = 50,
    FullHealth = 51,
    CannotRotate = 52,
    DoorInTheWay = 53,
    NotAnOpening = 54,

    /// <summary>A single door, aimed at a wall frame.</summary>
    NeedsDoorway = 55,

    /// <summary>A double or garage door, aimed at a doorway.</summary>
    NeedsWallFrame = 56,
    HasDoor = 57,
    HasLock = 58,
    NoLock = 59,
    WrongCode = 60,

    /// <summary>Wait before trying another code: <see cref="Outcome.Detail"/> is the seconds left.</summary>
    WaitForCode = 61,
    BadCode = 62,

    /// <summary>A code lock without a code yet: set one first.</summary>
    NoCode = 63,
    OpenItFirst = 64,
    RemoveLockFirst = 65,
    UnlockItFirst = 66,
    SwingBlocked = 67,
    HasClaim = 68,
    NotOnStructure = 69,
    ListFull = 70,

    /// <summary>Not on the code lock's list.</summary>
    NotOnLock = 71,

    /// <summary>A key lock, without its key.</summary>
    NeedsKey = 72,

    // ---------------------------------------------------------------- placing (P2, GROUND_SYSTEMS.md §6)

    /// <summary>The server worked the placement out again and it doesn't agree with the client's (something moved).</summary>
    Misplaced = 73,

    /// <summary>A corner of its footprint stands over nothing (RustMVP's: every corner supported).</summary>
    Overhangs = 74,

    /// <summary>The structure holds as many of its kind as it may: <see cref="Outcome.Detail"/> is its item.</summary>
    StructureFull = 75,

    /// <summary>The player has placed as many of it as they may: <see cref="Outcome.Detail"/> is its item.</summary>
    TooMany = 76,

    /// <summary>In the sea, or too near its surface.</summary>
    InWater = 77,

    // ---------------------------------------------------------------- interaction (GROUND_SYSTEMS.md §5)

    /// <summary>Not from a ship's seat: the verb's gates keep the seated out.</summary>
    Seated = 78,

    /// <summary>It holds something: empty it before picking it up.</summary>
    NotEmpty = 79,

    // ---------------------------------------------------------------- guns (G4; their own GunResult until P3 folded it in)
    // Numbered from 100, so codes the other packages of GROUND_SYSTEMS.md add beside these never meet them.

    /// <summary>No firearm in that slot (or in hand).</summary>
    NoWeapon = 100,

    /// <summary>The gun is reloading.</summary>
    Reloading = 101,

    /// <summary>The magazine is empty.</summary>
    Empty = 102,

    /// <summary>Faster than the gun fires.</summary>
    Cadence = 103,

    /// <summary>No rounds the gun takes to load.</summary>
    NoAmmo = 104,

    /// <summary>The magazine is full of that kind already.</summary>
    Full = 105,

    /// <summary>Sprinting, swimming or seated: the gun is lowered.</summary>
    Lowered = 106,

    /// <summary>The action is for a tick further back than lag compensation reaches.</summary>
    TooLate = 107,

    // ---------------------------------------------------------------- inventory actions (GROUND_SYSTEMS.md §7)

    /// <summary>Something worn already covers what that would be worn on.</summary>
    Worn = 108,

    // ---------------------------------------------------------------- ship spaces (IRON_WAKE.md §4)

    /// <summary>A ship's ramp or door would move into a character (§4.7).</summary>
    RampBlocked = 109,

    // ---------------------------------------------------------------- seats, parts and doors (IRON_WAKE.md §5, IW2)

    /// <summary>On foot at a ship that isn't at rest: its hatch opens, and its seat is reached, only when it is.</summary>
    ShipMoving = 110,
}

/// <summary>
/// An action's answer (GROUND_SYSTEMS.md §3.2): done, or refused with an <see cref="OutcomeCode"/> and, for codes whose
/// line names something, one number: an item (its ID: "Not enough wood", "You need a hammer"), seconds ("Wait 5 s"), a
/// percentage, a level. Every rule, handler and intent returns one; the server sends it as <c>IntentResult</c>, and the
/// interface shows its line from texts.json (<see cref="Content.Texts"/>). A code converts to an answer without a detail.
/// An answer is its code: two answers are equal when their codes are (the detail only fills in the line), and an answer
/// equals a code when its code is that code.
/// </summary>
public readonly record struct Outcome(OutcomeCode Code, int Detail = 0)
{
    public static readonly Outcome Done = default;

    public bool Ok => Code == OutcomeCode.Done;

    public bool Equals(Outcome other) => Code == other.Code;

    public override int GetHashCode() => (int)Code;

    public static implicit operator Outcome(OutcomeCode code) => new(code);

    public static bool operator ==(Outcome a, OutcomeCode b) => a.Code == b;

    public static bool operator !=(Outcome a, OutcomeCode b) => a.Code != b;

    public static bool operator ==(OutcomeCode a, Outcome b) => a == b.Code;

    public static bool operator !=(OutcomeCode a, Outcome b) => a != b.Code;

    public override string ToString() => Ok ? "done" : Detail != 0 ? $"{Code} ({Detail})" : Code.ToString();
}

/// <summary>Answers with a number for their line.</summary>
public static class OutcomeCodes
{
    /// <summary>This code with a number for its line (an item ID, seconds, a percentage, a level).</summary>
    public static Outcome With(this OutcomeCode code, int detail) => new(code, detail);
}

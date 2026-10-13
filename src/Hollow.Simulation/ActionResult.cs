namespace Hollow.Simulation;

/// <summary>
/// The answer to a player's action (ARCHITECTURE.md §11.4): accepted, or the reason it was refused. The server sends
/// it back for every intent; the client's optimistic layer rolls a refused one back. At most 32 values (5 bits).
/// </summary>
public enum ActionResult : byte
{
    Accepted = 0,

    /// <summary>Malformed: a slot, amount, item, recipe or target that can't be.</summary>
    Invalid,

    /// <summary>The slot no longer holds what the client expected (someone else got there first).</summary>
    Stale,

    /// <summary>That slot doesn't take that item: a clothing layer already worn, a furnace's fuel slot, a corpse.</summary>
    NotAllowed,

    /// <summary>No room where it should go.</summary>
    NoSpace,

    /// <summary>Not enough ingredients, or no arrows.</summary>
    NotEnough,

    /// <summary>The recipe needs a workbench of a higher level within reach.</summary>
    NeedsWorkbench,

    /// <summary>The recipe isn't known.</summary>
    UnknownRecipe,

    /// <summary>The crafting queue holds eight jobs already.</summary>
    QueueFull,

    /// <summary>Too far away (or not in sight).</summary>
    OutOfReach,

    /// <summary>No container of that kind is open.</summary>
    NotOpen,

    /// <summary>Not possible while dead (or, for a respawn, while alive).</summary>
    Dead,

    /// <summary>A placement overlaps something.</summary>
    Blocked,

    /// <summary>The ground is too steep there.</summary>
    TooSteep,

    /// <summary>A respawn point that is still cooling down.</summary>
    Cooldown,

    /// <summary>Over the player's action budget.</summary>
    RateLimited,

    /// <summary>The target is gone.</summary>
    NotFound,

    /// <summary>Under water, outside the zone, or not on the ground.</summary>
    BadPlacement,

    /// <summary>The item can't be used that way (eating a rock).</summary>
    NotUsable,

    /// <summary>The recipe is made at a station (a vehicle workbench, G6) that isn't within reach.</summary>
    NeedsStation,

    /// <summary>The ship is locked (G6): only its owner opens it.</summary>
    Locked,

    /// <summary>Someone is in the ship's seat already (G6).</summary>
    Occupied,

    /// <summary>Not while the ship is flying (G6): land first.</summary>
    NotLanded,

    /// <summary>The ship's hatch is shut (G6): open it first.</summary>
    HatchShut,
}

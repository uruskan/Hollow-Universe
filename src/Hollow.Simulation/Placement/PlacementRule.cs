using System;
using System.Numerics;

namespace Hollow.Simulation.Placement;

/// <summary>
/// What a placement stands on or is fitted to (placement.json's surfaces): the ground, the top of a foundation, floor or
/// stairs' landing, a wall, a roof, a doorway or wall frame, a door's leaf, a deployable or prop, a tree or rock, a ship,
/// the water's surface (a boat's; the aim meets it only for a rule that allows it, docs/MOTORBOAT.md §4). A rule allows a
/// set of them; a candidate is on one.
/// </summary>
[Flags]
public enum PlacementSurfaces : ushort
{
    None = 0,
    Terrain = 1,
    Floor = 2,
    Wall = 4,
    Roof = 8,
    Opening = 16,
    Door = 32,
    Thing = 64,
    Natural = 128,
    Ship = 256,
    Water = 512,
    Any = Terrain | Floor | Wall | Roof | Opening | Door | Thing | Natural | Ship,
}

/// <summary>What must hold a placement up: its middle, or every corner of its footprint (RustMVP's deployables).</summary>
public enum SupportMode : byte
{
    Centre,
    Corners,
}

/// <summary>Which way a placement's up points: the planet's, or along the surface it is on (a charge on a wall).</summary>
public enum AlignMode : byte
{
    Up,
    Normal,
}

/// <summary>
/// Building rights a placement needs where a claim device covers the place: none (charges: raiding), only that no one
/// else's device covers it (Rust's "building blocked"), or to be authorised by a device that covers it.
/// </summary>
public enum PrivilegeMode : byte
{
    Ignored,
    Blocked,
    Needed,
}

/// <summary>
/// One placeable's placement rule (GROUND_SYSTEMS.md §6): placement.json's defaults, its kind's block over them and its own
/// <c>placement</c> block over that. The solver, rotation and checks it names are handlers found by name; every check reads
/// what it needs here, so a check that has nothing to say for a rule passes.
/// </summary>
public sealed record PlacementRule
{
    /// <summary>The surfaces it may stand on or be fitted to.</summary>
    public PlacementSurfaces Surfaces { get; init; } = PlacementSurfaces.Terrain | PlacementSurfaces.Floor;

    /// <summary>The solver that turns an aim into candidates (<see cref="IPlacementSolver"/>, by name).</summary>
    public string Solver { get; init; } = "free";

    /// <summary>The footprint's half extents when the rule gives them, else null (its kind's own, or the hull).</summary>
    public Vector3? HalfExtents { get; init; }

    /// <summary>The footprint is the ship's hull (ships.json, from its model).</summary>
    public bool Hull { get; init; }

    public SupportMode Support { get; init; } = SupportMode.Corners;

    /// <summary>A footprint corner counts as held up by a surface this far above or below it.</summary>
    public float SupportStep { get; init; } = 0.15f;

    public float MaxSlopeDegrees { get; init; } = 30;

    /// <summary>The surface under the aim (or a claim) may be at most this far below or above it.</summary>
    public float MaxStep { get; init; } = 0.6f;

    /// <summary>Whether it may stand in the sea (a charge may; a box may not).</summary>
    public bool Water { get; init; }

    public AlignMode Align { get; init; }

    /// <summary>The rotation handler's name (free, steps, support, flip).</summary>
    public string Rotation { get; init; } = "free";

    /// <summary>For steps: the angle it snaps to, in degrees.</summary>
    public float StepDegrees { get; init; }

    /// <summary>How far one press of R turns it, in degrees.</summary>
    public float TurnDegrees { get; init; } = 90;

    /// <summary>The eye reaches this far to its nearest point (GROUND_SYSTEMS.md §3.4).</summary>
    public float Reach { get; init; } = 3;

    /// <summary>The line of sight to it may end this far short of it; negative: no sight check.</summary>
    public float Sight { get; init; } = 0.1f;

    public PrivilegeMode Privilege { get; init; } = PrivilegeMode.Blocked;

    /// <summary>Claims covering this far around it count (the building's 2.25 m).</summary>
    public float Padding { get; init; }

    /// <summary>At most this many of its kind on one structure (0: no limit).</summary>
    public int PerStructure { get; init; }

    /// <summary>At most this many of it per player (0: no limit).</summary>
    public int PerPlayer { get; init; }

    /// <summary>At most this many deployables aboard one ship (IRON_WAKE.md §8: 80; 0: no limit).</summary>
    public int PerShip { get; init; }

    /// <summary>
    /// Aboard a ship, what it keeps clear under its build zone's ceiling (IRON_WAKE.md §8): 0.2 m, so a deck of 3.8 m takes
    /// things up to 3.6 m tall and the tall bay alone the taller.
    /// </summary>
    public float Headroom { get; init; } = 0.2f;

    /// <summary>The server accepts a claimed pose this close to its own (metres).</summary>
    public float Tolerance { get; init; } = 0.3f;

    /// <summary>The cosine of <see cref="MaxSlopeDegrees"/>: a surface whose normal is at least this far up is flat enough.</summary>
    public float MinUp => MaxSlopeDegrees >= 180 ? -1 : MathF.Cos(MaxSlopeDegrees * MathF.PI / 180);

    /// <summary>Where <see cref="Solver"/> and <see cref="Rotation"/> are in placement.json's lists (their handlers' numbers).</summary>
    internal int SolverIndex { get; init; }

    internal int RotationIndex { get; init; }
}

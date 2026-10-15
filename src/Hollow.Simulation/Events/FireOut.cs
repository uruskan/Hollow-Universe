using System.Numerics;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Raid;

namespace Hollow.Simulation;

/// <summary>A fire went out (IW4, IRON_WAKE.md §7): which, where, and why (burnt out, doused, replaced, fell with nothing below). (GROUND_SYSTEMS.md §12.3)</summary>
public readonly record struct FireOut(
    EntityId Fire = default,
    Vector3 Position = default,
    FireEnd Why = default) : IZoneEvent
{
    /// <summary>The fire, by ID.</summary>
    public EntityId Fire { get; init; } = Fire;

    /// <summary>Why it went out.</summary>
    public FireEnd Why { get; init; } = Why;
}

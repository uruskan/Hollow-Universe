using System.Numerics;

namespace Hollow.Simulation.Players;

/// <summary>
/// A marker a player put on the map (GROUND.md §6.8, Rust's: up to five, theirs alone): a point in the zone frame, a
/// colour (0 … 5, the interface's palette) and a short label.
/// </summary>
public readonly record struct MapMarker(Vector3 Position, byte Colour, string Label)
{
    public const int MaxLabel = 24;
    public const int Colours = 6;
}

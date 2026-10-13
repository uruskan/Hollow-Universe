using System;
using Godot;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Items;
using Hollow.Simulation.Placement;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>
/// Placing in the game (GROUND_SYSTEMS.md §6): the controller the held item's behaviour calls (today the hands in
/// <see cref="GroundView"/> and the building tools in <see cref="BuildView"/>), whatever is held: a deployable, the plan's
/// piece, a door, a lock, the claim device, a charge, a ship. Every frame it asks the client for the ghost
/// (<see cref="GameClient.Plan"/>: the server's own pipeline on the replicated world) and shows it with the one
/// <see cref="PlacementGhost"/>; a click sends what the ghost shows; R turns it by its rule. The lines under the crosshair
/// read its verdict.
/// </summary>
public partial class PlacementView : Node3D
{
    private PlacementGhost _ghost = null!;

    public GameClient Client { get; set; } = null!;

    /// <summary>Notices (a refusal's line when a click is refused here).</summary>
    public Action<string>? Say { get; set; }

    /// <summary>The ghost (its building kit is set once the building's art is loaded).</summary>
    public PlacementGhost Ghost => _ghost;

    /// <summary>What is being placed, or null.</summary>
    public Placeable? Placeable { get; private set; }

    /// <summary>The ghost's candidate (its verdict in its outcome), or null when the aim finds nothing.</summary>
    public PlacementCandidate? Current { get; private set; }

    /// <summary>R's presses since the placeable was taken up.</summary>
    public int Turns { get; private set; }

    /// <summary>The verdict's line under the crosshair: why it doesn't fit, or what the aim lacks; null when it fits.</summary>
    public string? Reason =>
        Placeable == null ? null
        : Current == null ? Line(Client.PlacementNothing(Placeable)) is { Length: > 0 } nothing ? nothing : null
        : Current.Valid ? null
        : Line(Current.Outcome);

    public override void _Ready()
    {
        _ghost = new PlacementGhost { Name = "Ghost" };
        AddChild(_ghost);
    }

    /// <summary>Every frame: the ghost of <paramref name="placeable"/> (null: none) where the player aims, and the placements on their way.</summary>
    public void Refresh(Placeable? placeable, Num.Vector3 eye, Num.Vector3 forward, float viewYaw)
    {
        if (placeable != Placeable)
        {
            Placeable = placeable;
            Turns = 0;
        }
        Current = placeable != null ? Client.Plan(placeable, eye, forward, viewYaw, Turns) : null;
        _ghost.Show(Current);
        _ghost.ShowPending(Client.Local.PendingPlacements);
    }

    /// <summary>R: the next turn by the rule (a step, the other side of a door).</summary>
    public void Turn() => Turns++;

    /// <summary>
    /// A click: sends what the ghost shows, paid from a slot (the held one); when it doesn't fit, says why and sends nothing.
    /// Returns the intent's number, or 0.
    /// </summary>
    public uint Place(ContainerRef from, int slot)
    {
        if (Current is { Valid: true } candidate)
        {
            return Client.Local.Place(candidate, from, slot);
        }
        if (Reason is { } why)
        {
            Say?.Invoke(why);
        }
        return 0;
    }

    private string Line(Outcome outcome) => outcome.Ok ? "" : Client.Content.Texts.Line(outcome);
}

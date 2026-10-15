using System;
using Godot;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Items;
using Hollow.Simulation.Placement;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>
/// Placing in the game (GROUND_SYSTEMS.md §6): the one placement controller, behind <see cref="IPlacement"/> for the held
/// controls (§7: the placer's, the planner's and a charge's), whatever is held: a deployable, the plan's piece, a door, a
/// lock, the claim device, a charge, a ship. Each frame the held control aims it, and it asks the client for the ghost
/// (<see cref="GameClient.Plan"/>: the server's own pipeline on the replicated world) and shows it with the one
/// <see cref="PlacementGhost"/>; a click sends what the ghost shows; R turns it by its rule. The lines under the crosshair
/// (<see cref="BuildView"/>) read its verdict.
/// </summary>
public partial class PlacementView : Node3D, IPlacement
{
    private PlacementGhost _ghost = null!;
    private bool _aimed;
    private PendingPlacement? _lastPlaced;
    private bool _samePlacement;

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
        : Current == null ? Line(Client.Placement().PlacementNothing(Placeable)) is { Length: > 0 } nothing ? nothing : null
        : Current.Valid ? null
        : Line(Current.Outcome);

    public override void _Ready()
    {
        _ghost = new PlacementGhost { Name = "Ghost" };
        AddChild(_ghost);
        Client.Local.IntentRejected += Rejected;
    }

    public override void _ExitTree() => Client.Local.IntentRejected -= Rejected;

    private void Rejected(IntentAnswer answer)
    {
        if (_lastPlaced?.Seq == answer.Seq) _lastPlaced = null;
    }

    /// <summary>A frame starts: the ghost shows only if what is held aims it before <see cref="EndFrame"/>.</summary>
    public void BeginFrame() => _aimed = false;

    /// <summary>A frame ends: nothing aimed, nothing placed (no ghost, R's turns forgotten); the placements on their way are drawn whatever is held.</summary>
    public void EndFrame()
    {
        if (!_aimed)
        {
            Placeable = null;
            Current = null;
            Turns = 0;
            _lastPlaced = null;
            _ghost.Show(null);
        }
        _ghost.ShowPending(Client.Local.PendingPlacements);
    }

    /// <summary>The ghost of <paramref name="placeable"/> where the player aims, turned by R's presses since it was taken up.</summary>
    public void Refresh(Placeable placeable, Num.Vector3 eye, Num.Vector3 forward, float viewYaw)
    {
        if (placeable != Placeable)
        {
            Placeable = placeable;
            Turns = 0;
        }
        _aimed = true;
        Current = Client.Placement().Plan(placeable, eye, forward, viewYaw, Turns);
        _samePlacement = Current != null && _lastPlaced is { } placed && placed.Placeable == placeable &&
            Num.Vector3.DistanceSquared(placed.Position, Current.Position) < 0.0025f &&
            Math.Abs(Num.Quaternion.Dot(placed.Rotation, Current.Rotation)) > 0.999f;
        if (!_samePlacement) _lastPlaced = null;
        // Do not layer the active preview over its pending copy or a newly occupied socket.
        bool pending = Current != null && System.Linq.Enumerable.Any(Client.Local.PendingPlacements,
            p => p.Placeable == placeable && Num.Vector3.DistanceSquared(p.Position, Current.Position) < 0.0025f);
        _ghost.Show(_samePlacement || pending || Current?.Outcome.Code is OutcomeCode.Occupied or OutcomeCode.HasDoor or OutcomeCode.HasLock ? null : Current);
    }

    /// <summary>R: the next turn by the rule (a step, the other side of a door).</summary>
    public void Turn() => Turns++;

    /// <summary>
    /// A click: sends what the ghost shows, paid from a slot (the held one); when it doesn't fit, says why and sends nothing.
    /// Returns the intent's number, or 0.
    /// </summary>
    public uint Place(ContainerRef from, int slot)
    {
        if (Current is { Valid: true } candidate && !_samePlacement)
        {
            uint seq = Client.Local.Place(candidate, from, slot);
            if (seq != 0)
            {
                _lastPlaced = new PendingPlacement(seq, candidate.Placeable, candidate.Position, candidate.Rotation, candidate.Detail);
                _samePlacement = true;
                _ghost.Show(null);
            }
            return seq;
        }
        if (Reason is { } why)
        {
            Say?.Invoke(why);
        }
        return 0;
    }

    // What the held controls ask (the item's placeable, or the plan's piece; paid from the held belt slot).
    void IPlacement.Aim(HeldContext ctx, Num.Vector3 eye, Num.Vector3 forward)
    {
        if ((ctx.Item.Places ?? ctx.Build.PlannedPiece) is { } placeable)
        {
            Refresh(placeable, eye, forward, ctx.Yaw);
        }
    }

    uint IPlacement.Place(HeldContext ctx) => Place(ContainerRef.Belt, ctx.Slot);

    void IPlacement.Turn(HeldContext ctx) => Turn();

    private string Line(Outcome outcome) => outcome.Ok ? "" : Client.Content.Texts.Line(outcome);
}

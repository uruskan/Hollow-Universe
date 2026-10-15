using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>
/// Placing what is in hand (GROUND_SYSTEMS.md §6, package P2's system): the placer's, the planner's and a charge's one
/// way in. The held controls (§7) ask it to aim each frame (where the ghost goes), to place, and to turn; behind it is
/// <see cref="PlacementView"/>, the one placement controller.
/// </summary>
public interface IPlacement
{
    /// <summary>Each frame with the view: where what is held would go.</summary>
    void Aim(HeldContext ctx, Num.Vector3 eye, Num.Vector3 forward);

    /// <summary>The attack button (a charge's: the aim button): place it.</summary>
    uint Place(HeldContext ctx);

    /// <summary>R: turn it.</summary>
    void Turn(HeldContext ctx);
}

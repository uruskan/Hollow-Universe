using System;
using System.Collections.Generic;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Items;
using Num = System.Numerics;

namespace HollowUniverse.Ground;

/// <summary>
/// Placing what is in hand (GROUND_SYSTEMS.md §6, package P2's system): the placer's and the planner's one way in. The held
/// controls (§7) ask it to aim each frame (where the ghost goes), to place, and to turn; what is behind it is P2's.
/// </summary>
public interface IPlacement
{
    /// <summary>Each frame with the view: where what is held would go.</summary>
    void Aim(HeldContext ctx, Num.Vector3 eye, Num.Vector3 forward);

    /// <summary>The attack button: place it.</summary>
    void Place(HeldContext ctx);

    /// <summary>R: turn it.</summary>
    void Turn(HeldContext ctx);
}

/// <summary>
/// Today's placing behind <see cref="IPlacement"/>, until package P2's one placement system replaces it: the old entry
/// points, by the part that makes the item placeable. A deployable goes on the ground aimed at (<see cref="LocalPlayer.Place"/>,
/// its ghost in first person); the building's own placeables (the plan's piece, doors, locks, the tool cupboard) through
/// <see cref="BuildView.Use"/> and <see cref="BuildView.Rotate"/>, which aim for themselves; a ship can't be placed from
/// its item yet (P2).
/// </summary>
public sealed class TodaysPlacement : IPlacement
{
    private readonly Dictionary<ItemPart, Route> _routes;
    private Num.Vector3? _ground;

    public TodaysPlacement(BuildView build)
    {
        var onGround = new Route(AimGround, PlaceOnGround, _ => { });
        var building = new Route((_, _, _) => { }, _ => build.Use(), _ => build.Rotate());
        var notYet = new Route((_, _, _) => { }, ctx => ctx.Refuse(OutcomeCode.BadPlacement), _ => { });
        _routes = new Dictionary<ItemPart, Route>
        {
            [ItemPart.Deployable] = onGround,
            [ItemPart.Plan] = building,
            [ItemPart.Door] = building,
            [ItemPart.Lock] = building,
            [ItemPart.Cupboard] = building,
            [ItemPart.Ship] = notYet,
        };
    }

    public void Aim(HeldContext ctx, Num.Vector3 eye, Num.Vector3 forward) => RouteOf(ctx)?.Aim(ctx, eye, forward);

    public void Place(HeldContext ctx) => RouteOf(ctx)?.Place(ctx);

    public void Turn(HeldContext ctx) => RouteOf(ctx)?.Turn(ctx);

    private Route? RouteOf(HeldContext ctx) => ctx.Item.HeldPart is { } part ? _routes.GetValueOrDefault(part) : null;

    // A deployable: the ground the aim meets within reach, its ghost there.
    private void AimGround(HeldContext ctx, Num.Vector3 eye, Num.Vector3 forward)
    {
        _ground = ctx.Client.AimGround(eye, forward, ctx.Content.PlacementReach);
        ctx.Arms.PlacementPoint = _ground;
        ctx.Arms.PlacementYaw = ctx.Yaw;
    }

    private void PlaceOnGround(HeldContext ctx)
    {
        if (_ground is { } at)
        {
            ctx.Me.Place(ContainerRef.Belt, ctx.Slot, at, ctx.Yaw);
        }
    }

    private sealed record Route(Action<HeldContext, Num.Vector3, Num.Vector3> Aim, Action<HeldContext> Place, Action<HeldContext> Turn);
}

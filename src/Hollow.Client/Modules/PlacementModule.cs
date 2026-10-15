using System.Collections.Generic;
using System.Numerics;
using Hollow.Net.Protocol;
using Hollow.Simulation.Building;
using Hollow.Simulation.Content;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Placement;
using Hollow.Simulation.Targets;

namespace Hollow.Client;

// Placing on the client (GROUND_SYSTEMS.md §6): the placement world over what this client has (its prediction physics
// with the ground, trees, rocks and solid deployables, its mirror of the building, the natural objects as the server
// says, the things, characters and ships in range, what stands on what), so the ghost is the server's own pipeline run on
// the replicated world. Local.Place sends what it shows.
public sealed class PlacementModule : ClientModule
{
    private readonly Dictionary<TargetRef, int> _thingsOn = new();
    private PlacementWorld? _placement;
    private ItemWallet? _placementWallet;
    private SurvivalModule? _survivalModule;
    private SurvivalModule Survival => _survivalModule ??= Client.Require<SurvivalModule>();
    private BuildingModule? _buildingModule;
    private BuildingModule Building => _buildingModule ??= Client.Require<BuildingModule>();
    private ShipModule? _shipModule;
    private ShipModule Ships => _shipModule ??= Client.Require<ShipModule>();

    /// <summary>The placement world on this client (null before joining).</summary>
    public PlacementWorld? World =>
        _placement ??= Prediction is { } predictor && Surface is { } surface && Building.Replica is { } replica
            ? new PlacementWorld(Content, Frame, surface, predictor.World, replica.World, new ClientSurroundings(this), Survival.Nature, Survival.Natural,
                Ships.Collider)
            : null;

    /// <summary>
    /// The ghost (GROUND_SYSTEMS.md §6): where <paramref name="placeable"/> would go from the eye along a direction, the
    /// view's yaw and R pressed <paramref name="turns"/> times, checked by the server's own rules against what this client
    /// knows and this player's materials and building rights; its outcome says whether it fits, and why not. Null when the
    /// aim finds nothing (<see cref="PlacementNothing"/> says what to tell the player).
    /// </summary>
    public PlacementCandidate? Plan(Placeable placeable, Vector3 eye, Vector3 direction, float viewYaw, int turns = 0)
    {
        if (World is not { } world || !HasCharacter)
        {
            return null;
        }
        _placementWallet ??= new ItemWallet(Content, Local.Inventory.Carried);
        var ctx = new PlacementContext(EntityId.Value, eye, placeable, _placementWallet);
        return Placer.Plan(world, ctx, new PlacementAim(direction, viewYaw, turns));
    }

    /// <summary>
    /// The ghost of a placeable aimed from the player's eye at a point (scripted players: the screenshot run, bots,
    /// tests), turned to a yaw.
    /// </summary>
    public PlacementCandidate? PlanAt(Placeable placeable, Vector3 point, float yaw, int turns = 0)
    {
        if (!HasCharacter)
        {
            return null;
        }
        Vector3 eye = Prediction!.WorldPose(EyeHeight).Eye; // aboard a ship, where it carries the player (IRON_WAKE.md §4.8)
        return Plan(placeable, eye, point - eye, yaw, turns);
    }

    /// <summary>
    /// What the server will make of a claim (scripted players: bots and tests): the placement worked out again from it and
    /// checked, exactly as the server does with the intent (<see cref="Placer.Settle"/>); null and why when it doesn't hold.
    /// </summary>
    public PlacementCandidate? Settle(Placeable placeable, in PlacementClaim claim, out Simulation.Outcome refused)
    {
        refused = Simulation.OutcomeCode.Invalid;
        if (World is not { } world || !HasCharacter)
        {
            return null;
        }
        _placementWallet ??= new ItemWallet(Content, Local.Inventory.Carried);
        Vector3 eye = Prediction!.WorldPose(EyeHeight).Eye; // aboard a ship, where it carries the player (IRON_WAKE.md §4.8)
        return Placer.Settle(world, new PlacementContext(EntityId.Value, eye, placeable, _placementWallet), claim, out refused);
    }

    /// <summary>The surface's own ground, asked as the zone asks it (<see cref="Simulation.GroundProbe"/>); null before joining.</summary>
    public Simulation.GroundProbe? Ground => Surface != null && Prediction != null ? new Simulation.GroundProbe(Surface, Frame) : null;

    /// <summary>What to tell the player when the aim finds nothing to place (a door: aim at a doorway; none: nothing).</summary>
    public Simulation.Outcome PlacementNothing(Placeable placeable) =>
        World is { } world ? Placer.Nothing(world, placeable) : Simulation.Outcome.Done;

    public override void Dispose()
    {
        _placement = null;
        _thingsOn.Clear();
    }

    // What stands on what: a deployable's support (a piece, a ship), counted for the structure's and the ship's limits.
    internal void ThingSupportAdded(RemoteThing thing)
    {
        if (thing.Support.Family is TargetFamily.Piece or TargetFamily.Ship && thing.Kind == Simulation.World.StaticKind.Deployable)
        {
            _thingsOn[thing.Support.Whole] = _thingsOn.GetValueOrDefault(thing.Support.Whole) + 1;
        }
    }

    internal void ThingSupportRemoved(RemoteThing thing)
    {
        if (thing.Support.Family is TargetFamily.Piece or TargetFamily.Ship && thing.Kind == Simulation.World.StaticKind.Deployable &&
            _thingsOn.TryGetValue(thing.Support.Whole, out int count))
        {
            if (count > 1)
            {
                _thingsOn[thing.Support.Whole] = count - 1;
            }
            else
            {
                _thingsOn.Remove(thing.Support.Whole);
            }
        }
    }

    /// <summary>A client's surroundings for placement: the things, characters and ships it was sent, and what it knows of them.</summary>
    private sealed class ClientSurroundings(PlacementModule client) : IPlacementSurroundings
    {
        public void ThingsNear(Vector3 point, float radius, List<PlacedThing> into)
        {
            foreach (RemoteThing thing in client.Survival.Things)
            {
                if (Vector3.DistanceSquared(thing.Position, point) <= radius * radius && thing.Deployable(client.Content) is { } def)
                {
                    into.Add(new PlacedThing(new TargetRef(TargetFamily.Thing, thing.Id.Value), def, thing.Position, thing.Up, thing.Yaw));
                }
            }
        }

        public void CharactersNear(Vector3 point, float radius, List<Capsule> into)
        {
            MovementTuning tuning = client.Prediction?.Tuning ?? MovementTuning.Default;
            // Where each is drawn, upright on what carries them (aboard a ship, its deck: IRON_WAKE.md §4.8).
            void Add(Vector3 feet, Vector3 up)
            {
                if (Vector3.DistanceSquared(feet, point) <= radius * radius)
                {
                    into.Add(new Capsule(feet, up, tuning.Radius, tuning.StandingHeight));
                }
            }
            if (client.HasCharacter && !client.Local.IsDead && client.Prediction is { } prediction)
            {
                (Vector3 feet, Vector3 up, _) = prediction.WorldPose(0);
                Add(feet, up);
            }
            foreach (RemoteEntity remote in client.Remotes.Values)
            {
                if (remote.Spawned && remote.HasState && remote.Kind == EntityKind.Character && (remote.Flags & MoveFlags.Dead) == 0)
                {
                    Add(remote.Position, remote.Up);
                }
            }
        }

        public void ShipsNear(Vector3 point, float radius, List<PlacedShip> into)
        {
            foreach (RemoteShip ship in client.Ships.All)
            {
                if (ship.Def != null && Vector3.DistanceSquared(ship.Position, point) <= radius * radius)
                {
                    into.Add(new PlacedShip(ship.Def, ship.Position, ship.Rotation));
                }
            }
        }

        public int ThingsOn(in TargetRef support) => client._thingsOn.GetValueOrDefault(support.Whole);

        // The ships people walk in whose spaces this client keeps, where they are drawn (what the player sees is what the
        // ghost stands on, IRON_WAKE.md §8).
        public void SpacesNear(Vector3 point, float radius, List<PlacedSpace> into)
        {
            foreach (ClientShipFrame frame in client.Ships.Frames)
            {
                if (frame.Ship is { Spawned: true, HasState: true } ship && !ship.Has(ShipNetFlags.Destroyed) && !ship.Has(ShipNetFlags.InOrbit) &&
                    client.Ships.DrawnPose(ship.NetId) is { } pose && Vector3.Distance(pose.Position, point) <= radius + frame.Interior.Reach)
                {
                    into.Add(new PlacedSpace(frame, pose, new TargetRef(TargetFamily.Ship, ship.Id.Value), frame.Def));
                }
            }
        }

        // On the ship's list, as the server tells this player of the ship.
        public bool Crew(in TargetRef ship, ulong player) =>
            client.Ships.ShipOf(new Simulation.Entities.EntityId(ship.Id)) is { } remote && (remote.Viewer & ViewerFlags.Authorised) != 0;

        public int ThingsOwned(ulong player, DeployableDef def)
        {
            int count = 0;
            foreach (RemoteThing thing in client.Survival.Things)
            {
                count += thing.Mine && thing.Deployable(client.Content) == def ? 1 : 0;
            }
            return count;
        }
    }
}

/// <summary>The accessor on the client (GROUND_SYSTEMS.md §12.5, K11).</summary>
public static class PlacementModuleAccess
{
    public static PlacementModule Placement(this GameClient client) => client.Require<PlacementModule>();
}

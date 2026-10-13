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
public sealed partial class GameClient
{
    private readonly Dictionary<ulong, int> _thingsOn = new();
    private PlacementWorld? _placement;
    private ItemWallet? _placementWallet;

    /// <summary>The placement world on this client (null before joining).</summary>
    public PlacementWorld? Placement =>
        _placement ??= _predictor != null && _surface != null && _buildingReplica != null
            ? new PlacementWorld(Content, Frame, _surface, _predictor.World, _buildingReplica.World, new ClientSurroundings(this), Nature, Natural,
                _shipCollider)
            : null;

    /// <summary>
    /// The ghost (GROUND_SYSTEMS.md §6): where <paramref name="placeable"/> would go from the eye along a direction, the
    /// view's yaw and R pressed <paramref name="turns"/> times, checked by the server's own rules against what this client
    /// knows and this player's materials and building rights; its outcome says whether it fits, and why not. Null when the
    /// aim finds nothing (<see cref="PlacementNothing"/> says what to tell the player).
    /// </summary>
    public PlacementCandidate? Plan(Placeable placeable, Vector3 eye, Vector3 direction, float viewYaw, int turns = 0)
    {
        if (Placement is not { } world || !HasCharacter)
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
        Vector3 eye = Character.Position + Frame.UpAt(Character.Position) * EyeHeight;
        return Plan(placeable, eye, point - eye, yaw, turns);
    }

    /// <summary>
    /// What the server will make of a claim (scripted players: bots and tests): the placement worked out again from it and
    /// checked, exactly as the server does with the intent (<see cref="Placer.Settle"/>); null and why when it doesn't hold.
    /// </summary>
    public PlacementCandidate? Settle(Placeable placeable, in PlacementClaim claim, out Simulation.Outcome refused)
    {
        refused = Simulation.OutcomeCode.Invalid;
        if (Placement is not { } world || !HasCharacter)
        {
            return null;
        }
        _placementWallet ??= new ItemWallet(Content, Local.Inventory.Carried);
        Vector3 eye = Character.Position + Frame.UpAt(Character.Position) * EyeHeight;
        return Placer.Settle(world, new PlacementContext(EntityId.Value, eye, placeable, _placementWallet), claim, out refused);
    }

    /// <summary>The surface's own ground, asked as the zone asks it (<see cref="Simulation.GroundProbe"/>); null before joining.</summary>
    public Simulation.GroundProbe? Ground => _surface != null && _predictor != null ? new Simulation.GroundProbe(_surface, Frame) : null;

    /// <summary>What to tell the player when the aim finds nothing to place (a door: aim at a doorway; none: nothing).</summary>
    public Simulation.Outcome PlacementNothing(Placeable placeable) =>
        Placement is { } world ? Placer.Nothing(world, placeable) : Simulation.Outcome.Done;

    private void ResetPlacement()
    {
        _placement = null;
        _thingsOn.Clear();
    }

    // What stands on what: a thing's support, counted for the structure limits.
    private void ThingSupportAdded(RemoteThing thing)
    {
        if (thing.Support.Family == TargetFamily.Piece)
        {
            _thingsOn[thing.Support.Id] = _thingsOn.GetValueOrDefault(thing.Support.Id) + 1;
        }
    }

    private void ThingSupportRemoved(RemoteThing thing)
    {
        if (thing.Support.Family == TargetFamily.Piece && _thingsOn.TryGetValue(thing.Support.Id, out int count))
        {
            if (count > 1)
            {
                _thingsOn[thing.Support.Id] = count - 1;
            }
            else
            {
                _thingsOn.Remove(thing.Support.Id);
            }
        }
    }

    /// <summary>A client's surroundings for placement: the things, characters and ships it was sent, and what it knows of them.</summary>
    private sealed class ClientSurroundings(GameClient client) : IPlacementSurroundings
    {
        public void ThingsNear(Vector3 point, float radius, List<PlacedThing> into)
        {
            foreach (RemoteThing thing in client._things.Values)
            {
                if (Vector3.DistanceSquared(thing.Position, point) <= radius * radius && thing.Deployable(client.Content) is { } def)
                {
                    into.Add(new PlacedThing(new TargetRef(TargetFamily.Thing, thing.Id.Value), def, thing.Position, client.Frame.UpAt(thing.Position), thing.Yaw));
                }
            }
        }

        public void CharactersNear(Vector3 point, float radius, List<Capsule> into)
        {
            MovementTuning tuning = client._predictor?.Tuning ?? MovementTuning.Default;
            void Add(Vector3 feet)
            {
                if (Vector3.DistanceSquared(feet, point) <= radius * radius)
                {
                    into.Add(new Capsule(feet, client.Frame.UpAt(feet), tuning.Radius, tuning.StandingHeight));
                }
            }
            if (client.HasCharacter && !client.Local.IsDead)
            {
                Add(client.Character.Position);
            }
            foreach (RemoteEntity remote in client._remotes.Values)
            {
                if (remote.Spawned && remote.HasState && remote.Kind == EntityKind.Character && (remote.Flags & MoveFlags.Dead) == 0)
                {
                    Add(remote.Position);
                }
            }
        }

        public void ShipsNear(Vector3 point, float radius, List<PlacedShip> into)
        {
            foreach (RemoteShip ship in client.Ships)
            {
                if (ship.Def != null && Vector3.DistanceSquared(ship.Position, point) <= radius * radius)
                {
                    into.Add(new PlacedShip(ship.Def, ship.Position, ship.Rotation));
                }
            }
        }

        public int ThingsOn(in TargetRef support) => support.Family == TargetFamily.Piece ? client._thingsOn.GetValueOrDefault(support.Id) : 0;

        public int ThingsOwned(ulong player, DeployableDef def)
        {
            int count = 0;
            foreach (RemoteThing thing in client._things.Values)
            {
                count += thing.Mine && thing.Deployable(client.Content) == def ? 1 : 0;
            }
            return count;
        }
    }
}

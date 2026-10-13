using System.Numerics;
using Hollow.Net.Protocol;
using Hollow.Simulation.Building;

namespace Hollow.Client;

// Building on the client (GROUND.md §4.3, §6.3, phase 2): the replicated structures to draw, the ghost check with the
// same rules as the server, and (on Local) the building intents with their optimistic effects.
public sealed partial class GameClient
{
    private BuildingReplica? _buildingReplica;
    private bool _ownsBuilding;

    /// <summary>
    /// The structures in range as the server has them (a mirror: draw its <see cref="BuildingWorld.Structures"/>, its
    /// doors and claim devices, and listen to <see cref="BuildingWorld.Changed"/>). Null before joining.
    /// </summary>
    public BuildingWorld? Building => _buildingReplica?.World;

    /// <summary>The replicated building with what the server says of doors and claim devices for this player.</summary>
    public BuildingReplica? BuildingReplica => _buildingReplica;

    /// <summary>
    /// The ghost: the placement the building plan would make where the player aims, checked by the server's own rules
    /// against the replicated building, this player's materials and building rights. <paramref name="turns"/> counts
    /// presses of the rotate key. Null when there is nothing to show.
    /// </summary>
    public PlacementPlan? PlanPiece(PieceDef piece, Vector3 eye, Vector3 direction, int turns = 0)
    {
        if (_buildingReplica == null || !HasCharacter)
        {
            return null;
        }
        return _buildingReplica.World.Plan(EntityId.Value, piece, eye, direction, turns,
            new ItemWallet(Content, Local.Inventory.Carried), ClockSeconds);
    }

    /// <summary>What the player aims at within the building plan's reach: a piece, a door or a claim device (or null).</summary>
    public object? AimBuilding(Vector3 eye, Vector3 direction) =>
        _buildingReplica?.World.Target(eye, direction, (float)Content.Building.Reach, out _);

    private void StartBuilding()
    {
        if (_options.SharedBuilding != null)
        {
            _buildingReplica = _options.SharedBuilding(Zone!, _surface!, _predictor!.World);
        }
        else
        {
            _buildingReplica = new BuildingReplica(Content.Building, _predictor!.World);
            _ownsBuilding = true;
        }
        Local.Building = _buildingReplica;
    }

    private void DisposeBuilding()
    {
        if (_ownsBuilding)
        {
            _buildingReplica?.Dispose();
        }
        _buildingReplica = null;
    }

    private void ReceiveBuilding(uint type)
    {
        switch (type)
        {
            case BuildFrames.TypeId:
            {
                BuildFrames m = BuildFrames.Read(_reader);
                if (!_reader.Failed)
                {
                    _buildingReplica?.Apply(m);
                }
                return;
            }
            case BuildPieces.TypeId:
            {
                BuildPieces m = BuildPieces.Read(_reader);
                if (!_reader.Failed)
                {
                    _buildingReplica?.Apply(m, ClockSeconds);
                }
                return;
            }
            case BuildRemoved.TypeId:
            {
                BuildRemoved m = BuildRemoved.Read(_reader);
                if (!_reader.Failed)
                {
                    _buildingReplica?.Apply(m);
                }
                return;
            }
            case BuildDoors.TypeId:
            {
                BuildDoors m = BuildDoors.Read(_reader);
                if (!_reader.Failed)
                {
                    _buildingReplica?.Apply(m);
                }
                return;
            }
            case BuildClaims.TypeId:
            {
                BuildClaims m = BuildClaims.Read(_reader);
                if (!_reader.Failed)
                {
                    _buildingReplica?.Apply(m, EntityId.Value);
                }
                return;
            }
        }
    }
}

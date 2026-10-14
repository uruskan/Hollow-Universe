using System.Numerics;
using Hollow.Net.Protocol;
using Hollow.Simulation.Building;

namespace Hollow.Client;

// Building on the client (GROUND.md §4.3, §6.3, phase 2): the replicated structures to draw, the ghost check with the
// same rules as the server, and (on Local) the building intents with their optimistic effects.
public sealed class BuildingModule : ClientModule
{
    private BuildingReplica? _buildingReplica;
    private bool _ownsBuilding;

    /// <summary>
    /// The structures in range as the server has them (a mirror: draw its <see cref="BuildingWorld.Structures"/>, its
    /// doors and claim devices, and listen to <see cref="BuildingWorld.Changed"/>). Null before joining.
    /// </summary>
    public BuildingWorld? World => _buildingReplica?.World;

    /// <summary>The replicated building with what the server says of doors and claim devices for this player.</summary>
    public BuildingReplica? Replica => _buildingReplica;

    /// <summary>What the player aims at within the building plan's reach: a piece, a door or a claim device (or null).</summary>
    public object? AimBuilding(Vector3 eye, Vector3 direction) =>
        _buildingReplica?.World.Target(eye, direction, (float)Content.Building.Reach, out _);

    protected internal override void Init(MessageHandlers<GameClient> handlers)
    {
        handlers
            .On<BuildFrames>((_, in m) => _buildingReplica?.Apply(m))
            .On<BuildPieces>((_, in m) => _buildingReplica?.Apply(m, ClockSeconds))
            .On<BuildRemoved>((_, in m) => _buildingReplica?.Apply(m))
            .On<BuildDoors>((_, in m) => _buildingReplica?.Apply(m))
            .On<BuildClaims>((_, in m) => _buildingReplica?.Apply(m, EntityId.Value));
    }

    protected internal override void Welcome()
    {
        if (Client.Options.SharedBuilding != null)
        {
            _buildingReplica = Client.Options.SharedBuilding(Zone!, Surface!, Prediction!.World);
        }
        else
        {
            _buildingReplica = new BuildingReplica(Content.Building, Prediction!.World);
            _ownsBuilding = true;
        }
        Local.Building = _buildingReplica;
    }

    public override void Dispose()
    {
        if (_ownsBuilding)
        {
            _buildingReplica?.Dispose();
        }
        _buildingReplica = null;
    }
}

/// <summary>The accessor on the client (GROUND_SYSTEMS.md §12.5, K11).</summary>
public static class BuildingModuleAccess
{
    public static BuildingModule Building(this GameClient client) => client.Require<BuildingModule>();
}

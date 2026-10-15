using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Hollow.Client.Map;
using Hollow.Net.Protocol;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Physics;
using Hollow.Simulation.World;

namespace Hollow.Client;

// The world in the client (GROUND.md §6.8, G7): the world's props in range (barrels, crates, recyclers: things of kind
// Prop, with the deployables' public state), the player's own world state (LocalPlayer.World.cs), team chat, and the
// zone's map.
public sealed class WorldModule : ClientModule
{
    // Who holds each solid thing's body, per prediction physics world (clients sharing one, the bots, count together).
    private static readonly ConditionalWeakTable<PhysicsWorld, Dictionary<ulong, int>> ThingBodies = new();
    private ZoneMap? _map;
    private SurvivalModule? _survivalModule;
    private SurvivalModule Survival => _survivalModule ??= Client.Require<SurvivalModule>();

    /// <summary>
    /// The zone's map (GROUND.md §6.8): its square and grid, conversions between zone metres and the map, and the
    /// picture, rendered from the surface on demand (<see cref="ZoneMap.Render"/>); null before joining.
    /// </summary>
    public ZoneMap? Map => _map ??= Zone != null && Surface != null ? new ZoneMap(Zone, Surface, Frame, Content.World) : null;

    /// <summary>The world props in range: barrels, crates, recyclers.</summary>
    public IEnumerable<RemoteThing> Props => Survival.Things.Where(t => t.Kind == StaticKind.Prop);

    /// <summary>Whether a player is in the local player's team (for name tags and the map; not the player themself).</summary>
    public bool IsTeammate(EntityId player) => player != EntityId && Local.Team?.Has(player) == true;

    protected internal override void Init(MessageHandlers<GameClient> handlers)
    {
        handlers
            .On<BlueprintList>((_, in m) => Local.OnBlueprints(m))
            .On<TeamState>((_, in m) => Local.OnTeam(m))
            .On<TeamInvites>((_, in m) => OnTeamInvites(m))
            .On<TeamPositions>((_, in m) => Local.OnTeamPositions(m))
            .On<MapState>((_, in m) => Local.OnMap(m))
            .On<MachineState>((_, in m) => Local.OnMachine(m));
    }

    // Solid deployables and the world's props stand in the prediction physics as on the server (the same box from the
    // same place, up and yaw), so walking into a barrel, a crate or a box stops the character here too instead of
    // pulling the prediction back. Clients sharing a physics world count who holds each body: one moving out of range
    // leaves it for the others.
    internal void ThingBodyAdded(RemoteThing thing)
    {
        // A thing aboard a ship is a box of the ship's space instead (ShipModule.ThingAboard, IRON_WAKE.md §8).
        if (Physics is not { } physics || thing.Aboard || thing.Deployable(Content) is not { Solid: true } def)
        {
            return;
        }
        Dictionary<ulong, int> held = ThingBodies.GetOrCreateValue(physics);
        held[thing.Id.Value] = held.GetValueOrDefault(thing.Id.Value) + 1;
        Vector3 up = Frame.UpAt(thing.Position);
        physics.AddProp(PropLayer.Deployable, thing.Id.Value, PropShape.Box(thing.Position + up * def.HalfExtents.Y, def.HalfExtents, up, thing.Yaw));
    }

    internal void ThingBodyRemoved(RemoteThing thing)
    {
        if (Physics is not { } physics || !ThingBodies.TryGetValue(physics, out Dictionary<ulong, int>? held) ||
            !held.TryGetValue(thing.Id.Value, out int count))
        {
            return;
        }
        if (count > 1)
        {
            held[thing.Id.Value] = count - 1;
            return;
        }
        held.Remove(thing.Id.Value);
        physics.RemoveProp(PropLayer.Deployable, thing.Id.Value);
    }

    /// <summary>A chat line to the player's team alone.</summary>
    public void SendTeamChat(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        SendReliable(new TeamChatSend { Text = text.Trim() });
    }

    private void OnTeamInvites(in TeamInvites m)
    {
        Local.ClockSeconds = ClockSeconds;
        Local.OnInvites(m);
    }
}

/// <summary>The accessor on the client (GROUND_SYSTEMS.md §12.5, K11).</summary>
public static class WorldModuleAccess
{
    public static WorldModule World(this GameClient client) => client.Require<WorldModule>();
}

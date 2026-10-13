using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Hollow.Client.Map;
using Hollow.Net.Protocol;
using Hollow.Simulation.Content;
using Hollow.Simulation.Entities;
using Hollow.Simulation.Physics;
using Hollow.Simulation.World;
using LiteNetLib;

namespace Hollow.Client;

// The world in the client (GROUND.md §6.8, G7): the world's props in range (barrels, crates, recyclers: things of kind
// Prop, with the deployables' public state), the player's own world state (LocalPlayer.World.cs), team chat, and the
// zone's map.
public sealed partial class GameClient
{
    // Who holds each solid thing's body, per prediction physics world (clients sharing one, the bots, count together).
    private static readonly ConditionalWeakTable<PhysicsWorld, Dictionary<ulong, int>> ThingBodies = new();
    private ZoneMap? _map;

    /// <summary>
    /// The zone's map (GROUND.md §6.8): its square and grid, conversions between zone metres and the map, and the
    /// picture, rendered from the surface on demand (<see cref="ZoneMap.Render"/>); null before joining.
    /// </summary>
    public ZoneMap? Map => _map ??= Zone != null && _surface != null ? new ZoneMap(Zone, _surface, Frame, Content.World) : null;

    /// <summary>The world props in range: barrels, crates, recyclers.</summary>
    public IEnumerable<RemoteThing> Props => _things.Values.Where(t => t.Kind == StaticKind.Prop);

    /// <summary>Whether a player is in the local player's team (for name tags and the map; not the player themself).</summary>
    public bool IsTeammate(EntityId player) => player != EntityId && Local.Team?.Has(player) == true;

    // Solid deployables and the world's props stand in the prediction physics as on the server (the same box from the
    // same place, up and yaw), so walking into a barrel, a crate or a box stops the character here too instead of
    // pulling the prediction back. Clients sharing a physics world count who holds each body: one moving out of range
    // leaves it for the others.
    private void ThingBodyAdded(RemoteThing thing)
    {
        if (Physics is not { } physics || thing.Deployable(Content) is not { Solid: true } def)
        {
            return;
        }
        Dictionary<ulong, int> held = ThingBodies.GetOrCreateValue(physics);
        held[thing.Id.Value] = held.GetValueOrDefault(thing.Id.Value) + 1;
        Vector3 up = Frame.UpAt(thing.Position);
        physics.AddProp(PropLayer.Deployable, thing.Id.Value, PropShape.Box(thing.Position + up * def.HalfExtents.Y, def.HalfExtents, up, thing.Yaw));
    }

    private void ThingBodyRemoved(RemoteThing thing)
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
        if (_peer == null || State != ClientState.Playing || string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        _writer.Reset();
        new TeamChatSend { Text = text.Trim() }.WriteMessage(_writer);
        _peer.Send(_writer.Data, DeliveryMethod.ReliableOrdered);
    }

    private bool ReceiveWorld(uint type)
    {
        switch (type)
        {
            case BlueprintList.TypeId:
            {
                BlueprintList m = BlueprintList.Read(_reader);
                if (!_reader.Failed)
                {
                    Local.OnBlueprints(m);
                }
                return true;
            }
            case TeamState.TypeId:
            {
                TeamState m = TeamState.Read(_reader);
                if (!_reader.Failed)
                {
                    Local.OnTeam(m);
                }
                return true;
            }
            case TeamInvites.TypeId:
            {
                TeamInvites m = TeamInvites.Read(_reader);
                if (!_reader.Failed)
                {
                    Local.ClockSeconds = ClockSeconds;
                    Local.OnInvites(m);
                }
                return true;
            }
            case TeamPositions.TypeId:
            {
                TeamPositions m = TeamPositions.Read(_reader);
                if (!_reader.Failed)
                {
                    Local.OnTeamPositions(m);
                }
                return true;
            }
            case MapState.TypeId:
            {
                MapState m = MapState.Read(_reader);
                if (!_reader.Failed)
                {
                    Local.OnMap(m);
                }
                return true;
            }
            case MachineState.TypeId:
            {
                MachineState m = MachineState.Read(_reader);
                if (!_reader.Failed)
                {
                    Local.OnMachine(m);
                }
                return true;
            }
            default:
                return false;
        }
    }
}

using System.Collections.Generic;
using Hollow.Simulation.Content;
using Hollow.Simulation.Physics;

namespace Hollow.Simulation.Nature;

/// <summary>
/// Keeps the trees and outcrops of every ground tile in a <see cref="PhysicsWorld"/> as static props, so characters
/// walk around them: added with their tile, removed with it, and removed or put back as they are felled and grow
/// back (<see cref="NaturalState.PresenceChanged"/>). The server's zone and every client's prediction run one, so
/// both collide with the same trees.
/// </summary>
public sealed class NaturalBodies : ITileListener
{
    private readonly PhysicsWorld _world;
    private readonly NaturalState _state;
    private readonly List<NaturalObject> _scratch = new();

    public NaturalBodies(PhysicsWorld world, NaturalState state)
    {
        _world = world;
        _state = state;
        world.Listener = this;
        state.PresenceChanged += Refresh;
    }

    public void TileAdded(int x, int z)
    {
        foreach (NaturalObject o in TileObjects(x, z))
        {
            if (!_state.IsDepleted(o.Id))
            {
                Add(o);
            }
        }
    }

    /// <summary>On a worker thread: the tile's objects are generated (and cached) before the tick adds the tile.</summary>
    public void TilePreparing(int x, int z)
    {
        var scratch = new List<NaturalObject>();
        _state.Objects.Query(x * PhysicsWorld.TileSize, z * PhysicsWorld.TileSize, (x + 1) * PhysicsWorld.TileSize, (z + 1) * PhysicsWorld.TileSize, scratch);
    }

    public void TileRemoved(int x, int z)
    {
        foreach (NaturalObject o in TileObjects(x, z))
        {
            _world.RemoveProp(PropLayer.Nature, o.Id);
        }
    }

    /// <summary>An object was felled or grew back: follow it, if its tile is in the world.</summary>
    public void Refresh(ulong id)
    {
        if (!_state.Objects.TryGet(id, out NaturalObject o))
        {
            return;
        }
        (int x, int z) = PhysicsWorld.TileOf(o.Position);
        if (!_world.HasTile(x, z))
        {
            return;
        }
        if (_state.IsDepleted(id))
        {
            _world.RemoveProp(PropLayer.Nature, id);
        }
        else
        {
            Add(o);
        }
    }

    private void Add(in NaturalObject o)
    {
        NaturalKind? kind = _state.Content.Natural(o.Kind);
        if (kind == null || !kind.IsSolid)
        {
            return;
        }
        if (kind.Class == NaturalClass.Tree)
        {
            (System.Numerics.Vector3 a, System.Numerics.Vector3 b, float radius) = NaturalShapes.Trunk(o, kind);
            _world.AddProp(PropLayer.Nature, o.Id, PropShape.Capsule(a, b, radius, o.Up));
        }
        else
        {
            (System.Numerics.Vector3 centre, float radius) = NaturalShapes.Rock(o, kind);
            _world.AddProp(PropLayer.Nature, o.Id, PropShape.Sphere(centre, radius));
        }
    }

    private List<NaturalObject> TileObjects(int x, int z)
    {
        _scratch.Clear();
        _state.Objects.Query(x * PhysicsWorld.TileSize, z * PhysicsWorld.TileSize, (x + 1) * PhysicsWorld.TileSize, (z + 1) * PhysicsWorld.TileSize, _scratch);
        return _scratch;
    }
}

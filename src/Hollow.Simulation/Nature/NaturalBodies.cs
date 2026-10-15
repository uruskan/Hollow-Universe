using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Content;
using Hollow.Simulation.Diagnostics;
using Hollow.Simulation.Physics;
using Hollow.Surface;

namespace Hollow.Simulation.Nature;

/// <summary>
/// Keeps the trees and outcrops of every ground tile in a <see cref="PhysicsWorld"/> as static props, so characters
/// walk around them: added with their tile, removed with it, and removed or put back as they are felled and grow
/// back (<see cref="NaturalState.PresenceChanged"/>). The server's zone and every client's prediction run one, so
/// both collide with the same trees. An object belongs to the ground tile its base lies in.
/// </summary>
public sealed class NaturalBodies : ITileListener
{
    private readonly PhysicsWorld _world;
    private readonly NaturalState _state;
    private readonly List<NaturalObject> _scratch = new();
    private readonly List<ulong> _ids = new();

    // The solid objects of each tile in the world, felled ones too: a tile leaves without asking the scatter again
    // (its scatter tile may have left the cache, and making it again on the tick costs about 15 ms).
    private readonly Dictionary<TileKey, ulong[]> _solid = new();

    public NaturalBodies(PhysicsWorld world, NaturalState state)
    {
        _world = world;
        _state = state;
        world.Listener = this;
        state.PresenceChanged += Refresh;
    }

    public void TileAdded(TileKey tile)
    {
        List<NaturalObject> objects = TileObjects(tile, _scratch);
        using TickProfile.Scope _ = _world.Profile.Time(ProfileSection.PropAdd);
        _ids.Clear();
        foreach (NaturalObject o in objects)
        {
            if (_state.Content.Natural(o.Kind) is not { IsSolid: true })
            {
                continue;
            }
            _ids.Add(o.Id);
            if (!_state.IsDepleted(o.Id))
            {
                Add(o);
            }
        }
        _solid[tile] = _ids.ToArray();
    }

    /// <summary>On a worker thread: the tile's objects are generated (and cached) before the tick adds the tile.</summary>
    public void TilePreparing(TileKey tile) => TileObjects(tile, new List<NaturalObject>(), timed: false);

    public void TileRemoved(TileKey tile)
    {
        using TickProfile.Scope _ = _world.Profile.Time(ProfileSection.PropRemove);
        if (!_solid.Remove(tile, out ulong[]? ids))
        {
            return;
        }
        foreach (ulong id in ids)
        {
            _world.RemoveProp(PropLayer.Nature, id);
        }
    }

    /// <summary>An object was felled or grew back: follow it, if its tile is in the world.</summary>
    public void Refresh(ulong id)
    {
        if (_state.IsDepleted(id))
        {
            // Felled: its body goes, if it has one (nothing to look up for that).
            _world.RemoveProp(PropLayer.Nature, id);
            return;
        }
        if (!_state.Objects.TryGet(id, out NaturalObject o))
        {
            return;
        }
        if (_world.HasTile(_world.TileOf(o.Position)))
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
            (Vector3 a, Vector3 b, float radius) = NaturalShapes.Trunk(o, kind);
            _world.AddProp(PropLayer.Nature, o.Id, PropShape.Capsule(a, b, radius, o.Up));
        }
        else
        {
            (Vector3 centre, float radius) = NaturalShapes.Rock(o, kind);
            _world.AddProp(PropLayer.Nature, o.Id, PropShape.Sphere(centre, radius));
        }
    }

    // The objects whose base lies in a ground tile: those near its centre, kept when the tile holds them. Timed on the
    // tick's thread only: the profile is that thread's, and a worker preparing a tile would race it and count as the tick.
    private List<NaturalObject> TileObjects(TileKey tile, List<NaturalObject> into, bool timed = true)
    {
        using TickProfile.Scope _ = (timed ? _world.Profile : null).Time(ProfileSection.NatureQuery);
        into.Clear();
        double edge = TileKey.EdgeLength(tile.Level, _world.Surface.Radius);
        Vector3 centre = _world.Frame.ToLocal(tile.Centre * _world.Surface.Radius);
        _state.Objects.Query(centre, (float)(edge * 1.1) + 20f, into);
        into.RemoveAll(o => _world.TileOf(o.Position) != tile);
        return into;
    }
}

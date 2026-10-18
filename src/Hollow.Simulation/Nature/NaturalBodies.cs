using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
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
public sealed class NaturalBodies : ITileListener, IDisposable
{
    private PhysicsWorld? _world;
    private NaturalState? _state;
    private readonly object _lifetime = new();
    private int _callbacks;
    private bool _stopped, _disposed;
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
        if (!Enter(out PhysicsWorld world, out NaturalState state)) return;
        try
        {
            List<NaturalObject> objects = TileObjects(world, state, tile, _scratch);
            using TickProfile.Scope _ = world.Profile.Time(ProfileSection.PropAdd);
            _ids.Clear();
            foreach (NaturalObject o in objects)
            {
                if (state.Content.Natural(o.Kind) is not { IsSolid: true }) continue;
                _ids.Add(o.Id);
                if (!state.IsDepleted(o.Id)) Add(world, state, o);
            }
            _solid[tile] = _ids.ToArray();
        }
        finally { Leave(); }
    }

    /// <summary>On a worker thread: the tile's objects are generated (and cached) before the tick adds the tile.</summary>
    public void TilePreparing(TileKey tile)
    {
        if (!Enter(out PhysicsWorld world, out NaturalState state)) return;
        try { TileObjects(world, state, tile, new List<NaturalObject>(), timed: false); }
        finally { Leave(); }
    }

    public void TileRemoved(TileKey tile)
    {
        if (!Enter(out PhysicsWorld world, out _)) return;
        try
        {
            using TickProfile.Scope _ = world.Profile.Time(ProfileSection.PropRemove);
            if (!_solid.Remove(tile, out ulong[]? ids)) return;
            foreach (ulong id in ids) world.RemoveProp(PropLayer.Nature, id);
        }
        finally { Leave(); }
    }

    /// <summary>An object was felled or grew back: follow it, if its tile is in the world.</summary>
    public void Refresh(ulong id)
    {
        if (!Enter(out PhysicsWorld world, out NaturalState state)) return;
        try
        {
            if (state.IsDepleted(id))
            {
                // Felled: its body goes, if it has one (nothing to look up for that).
                world.RemoveProp(PropLayer.Nature, id);
                return;
            }
            if (state.Objects.TryGet(id, out NaturalObject o) && world.HasTile(world.TileOf(o.Position)))
                Add(world, state, o);
        }
        finally { Leave(); }
    }

    private static void Add(PhysicsWorld world, NaturalState state, in NaturalObject o)
    {
        NaturalKind? kind = state.Content.Natural(o.Kind);
        if (kind == null || !kind.IsSolid)
        {
            return;
        }
        if (kind.Class == NaturalClass.Tree)
        {
            (Vector3 a, Vector3 b, float radius) = NaturalShapes.Trunk(o, kind);
            world.AddProp(PropLayer.Nature, o.Id, PropShape.Capsule(a, b, radius, o.Up));
        }
        else
        {
            (Vector3 centre, float radius) = NaturalShapes.Rock(o, kind);
            world.AddProp(PropLayer.Nature, o.Id, PropShape.Sphere(centre, radius));
        }
    }

    // The objects whose base lies in a ground tile: those near its centre, kept when the tile holds them. Timed on the
    // tick's thread only: the profile is that thread's, and a worker preparing a tile would race it and count as the tick.
    private static List<NaturalObject> TileObjects(PhysicsWorld world, NaturalState state, TileKey tile, List<NaturalObject> into, bool timed = true)
    {
        using TickProfile.Scope _ = (timed ? world.Profile : null).Time(ProfileSection.NatureQuery);
        into.Clear();
        double edge = TileKey.EdgeLength(tile.Level, world.Surface.Radius);
        Vector3 centre = world.Frame.ToLocal(tile.Centre * world.Surface.Radius);
        state.Objects.Query(centre, (float)(edge * 1.1) + 20f, into);
        into.RemoveAll(o => world.TileOf(o.Position) != tile);
        return into;
    }

    private bool Enter(out PhysicsWorld world, out NaturalState state)
    {
        lock (_lifetime)
        {
            world = _world!;
            state = _state!;
            if (_stopped) return false;
            _callbacks++;
            return true;
        }
    }

    private void Leave()
    {
        lock (_lifetime)
        {
            if (--_callbacks == 0) Monitor.PulseAll(_lifetime);
        }
    }

    /// <summary>Detaches the owning-thread subscriptions and refuses even callbacks captured before retirement.</summary>
    public void Stop()
    {
        lock (_lifetime)
        {
            if (_stopped) return;
            _stopped = true;
            NaturalState state = _state!;
            PhysicsWorld world = _world!;
            state.PresenceChanged -= Refresh;
            if (ReferenceEquals(world.Listener, this)) world.Listener = null;
        }
    }

    /// <summary>
    /// Drains admitted callbacks outside the physics gate, then releases their source references. Existing prop
    /// bodies remain owned by PhysicsWorld and are released by its teardown; this listener does not own the world.
    /// </summary>
    public void Dispose()
    {
        lock (_lifetime) { if (_disposed) return; }
        if (JoltRuntime.Gate.IsHeldByCurrentThread)
            throw new InvalidOperationException("Natural body retirement must run outside the Jolt gate.");
        Stop();
        lock (_lifetime)
        {
            while (_callbacks != 0) Monitor.Wait(_lifetime);
            if (_disposed) return;
            _world = null;
            _state = null;
            _scratch.Clear();
            _ids.Clear();
            _solid.Clear();
            _disposed = true;
        }
    }
}

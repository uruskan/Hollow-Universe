using System;
using System.Collections.Generic;
using System.Linq;
using Hollow.Client;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Physics;
using Hollow.Surface;

namespace Hollow.Bots;

/// <summary>One bot session owns its immutable world store and the mutable replicas shared by its clients.</summary>
internal sealed class BotWorlds : IDisposable
{
    private readonly GameContent _content;
    private readonly Dictionary<PlanetEnvironmentDescriptor, BotWorldContext> _contexts = new();
    private bool _disposed;

    /// <summary>Ownership of the supplied store transfers to this session; clients only borrow it.</summary>
    public BotWorlds(GameContent content, WorldStore? ownedStore = null)
    {
        _content = content;
        Store = ownedStore ?? new WorldStore(new WorldStoreOptions());
    }

    public WorldStore Store { get; }

    public BotWorldContext Get(ZoneSpec spec, WorldLease source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        PlanetEnvironmentDescriptor descriptor = source.Descriptor;
        if (_contexts.TryGetValue(descriptor, out BotWorldContext? found))
        {
            if (!ReferenceEquals(found.Surface, source.Surface))
                throw new InvalidOperationException("Bot clients must borrow the same session store.");
            return found;
        }
        var context = new BotWorldContext(spec, source.Fork(), _content);
        _contexts.Add(descriptor, context);
        return context;
    }

    public void ReleaseUnusedGround()
    {
        foreach (BotWorldContext context in _contexts.Values)
            lock (JoltRuntime.Gate) context.Physics.ReleaseUnusedGround(20 * 60);
    }

    // Called after all client updates, when a replaced predictor can no longer access its old shared world.
    public void RetireExcept(IReadOnlySet<PlanetEnvironmentDescriptor> retained)
    {
        if (JoltRuntime.Gate.IsHeldByCurrentThread)
            throw new InvalidOperationException("Bot world retirement must run outside the Jolt gate.");
        var errors = new List<Exception>();
        foreach (PlanetEnvironmentDescriptor key in _contexts.Keys.Where(key => !retained.Contains(key)).ToArray())
        {
            BotWorldContext context = _contexts[key];
            _contexts.Remove(key);
            try { context.Dispose(); } catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Retiring unused bot worlds failed after cleanup.", errors);
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (JoltRuntime.Gate.IsHeldByCurrentThread)
            throw new InvalidOperationException("Bot world retirement must run outside the Jolt gate.");
        _disposed = true;
        var errors = new List<Exception>();
        foreach (BotWorldContext context in _contexts.Values)
            try { context.Dispose(); } catch (Exception error) { errors.Add(error); }
        _contexts.Clear();
        try { Store.Dispose(); } catch (Exception error) { errors.Add(error); }
        if (errors.Count != 0) throw new AggregateException("Bot world retirement failed after cleanup.", errors);
    }
}

internal sealed class BotWorldContext : IDisposable
{
    private WorldLease? _lease;
    private PhysicsWorld? _physics;
    private NaturalState? _nature;
    private NaturalBodies? _bodies;
    private BuildingReplica? _building;
    private bool _disposed;

    public BotWorldContext(ZoneSpec spec, WorldLease ownedLease, GameContent content)
    {
        _lease = ownedLease;
        try
        {
            ISurface surface = ownedLease.Surface;
            ZoneFrame frame = spec.CreateFrame(surface);
            _physics = new PhysicsWorld(surface, frame);
            _nature = new NaturalState(content, spec.CreateNature(surface, frame, content));
            _bodies = new NaturalBodies(_physics, _nature);
            _building = new BuildingReplica(content.Building, _physics, shared: true);
        }
        catch (Exception construction)
        {
            try { Dispose(); }
            catch (Exception cleanup) { throw new AggregateException(construction, cleanup); }
            throw;
        }
    }

    public ISurface Surface => (_lease ?? throw new ObjectDisposedException(nameof(BotWorldContext))).Surface;
    public PhysicsWorld Physics => _physics ?? throw new ObjectDisposedException(nameof(BotWorldContext));
    public NaturalState Nature => _nature ?? throw new ObjectDisposedException(nameof(BotWorldContext));
    public BuildingReplica Building => _building ?? throw new ObjectDisposedException(nameof(BotWorldContext));

    public void Dispose()
    {
        if (_disposed) return;
        if (JoltRuntime.Gate.IsHeldByCurrentThread)
            throw new InvalidOperationException("Bot world retirement must run outside the Jolt gate.");
        _disposed = true;
        var errors = new List<Exception>();
        void Clean(Action action) { try { action(); } catch (Exception error) { errors.Add(error); } }
        // Client predictors are already gone. Stop/drain nature callbacks outside the physics gate, then native work.
        if (_bodies != null) Clean(_bodies.Dispose);
        if (_building != null) Clean(_building.Dispose);
        if (_physics != null) Clean(_physics.Dispose);
        _bodies = null;
        _building = null;
        _physics = null;
        _nature = null;
        if (_lease != null) Clean(_lease.Dispose);
        _lease = null;
        if (errors.Count != 0) throw new AggregateException("Shared bot world cleanup failed.", errors);
    }
}

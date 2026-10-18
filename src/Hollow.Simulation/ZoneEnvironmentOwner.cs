using System;
using System.Collections.Generic;
using System.Numerics;
using Hollow.Simulation.Movement;
using Hollow.Simulation.Nature;
using Hollow.Simulation.Physics;
using Hollow.Surface;

namespace Hollow.Simulation;

/// <summary>
/// Owns one zone's generated source and all readers which must retire before its lease can be released. An injected
/// fixture surface stays borrowed. System initialization is registered before it runs, so a partially initialized
/// system participates in the same rollback as a running zone. Gameplay selection and dispatch remain in Zone.
/// </summary>
internal sealed class ZoneEnvironmentOwner : IDisposable
{
    private WorldLease? _lease;
    private WorldStore? _ownedStore;
    private PhysicsWorld? _physics;
    private CharacterBody? _body;
    private NaturalBodies? _natural;
    private readonly List<ZoneSystem> _initialized = new();

    private ZoneEnvironmentOwner(ISurface surface, WorldLease? lease = null, WorldStore? ownedStore = null)
    {
        Surface = surface;
        _lease = lease;
        _ownedStore = ownedStore;
    }

    public ISurface Surface { get; private set; }
    public bool IsDisposed { get; private set; }

    public static ZoneEnvironmentOwner Borrow(ISurface surface)
    {
        EnsureOutsidePhysicsGate();
        ArgumentNullException.ThrowIfNull(surface);
        return new(surface);
    }

    /// <summary>Consumes the supplied pin, including rejection before native resource construction.</summary>
    public static ZoneEnvironmentOwner Take(WorldLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        try
        {
            EnsureOutsidePhysicsGate();
            return new(lease.Surface, lease);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    public static ZoneEnvironmentOwner Acquire(ZoneSpec spec)
    {
        EnsureOutsidePhysicsGate();
        PlanetEnvironmentDescriptor descriptor = spec.EnvironmentDescriptor();
        var store = new WorldStore(new WorldStoreOptions());
        WorldLease? lease = null;
        try
        {
            lease = store.Acquire(descriptor);
            return new(lease.Surface, lease, store);
        }
        catch (Exception error)
        {
            lease?.Dispose();
            try { store.DisposeAsync().GetAwaiter().GetResult(); }
            catch (Exception cleanup) { throw new AggregateException("World acquisition and rollback failed.", error, cleanup).Flatten(); }
            throw;
        }
    }

    public ISurface SurfaceFor(ZoneSpec spec)
    {
        if (_lease != null && !_lease.Descriptor.Equals(spec.EnvironmentDescriptor(_lease.Descriptor.IsDiagnostic)))
            throw new ArgumentException("The zone and its world lease have different environment inputs.", nameof(spec));
        return Surface;
    }

    public PhysicsWorld CreatePhysics(ZoneFrame frame) => _physics = new PhysicsWorld(Surface, frame);
    public CharacterBody CreateCharacter(MovementTuning tuning) =>
        _body = new CharacterBody(_physics!, tuning, Vector3.Zero, Vector3.UnitY);
    public void ListenTo(NaturalState state) => _natural = new NaturalBodies(_physics!, state);

    public void Initialize(ZoneSystem system)
    {
        // Init may own jobs/resources before it throws. Register first, then retire in reverse initialization order.
        _initialized.Add(system);
        system.Init();
    }

    public static void EnsureOutsidePhysicsGate()
    {
        if (JoltRuntime.Gate.IsHeldByCurrentThread)
            throw new InvalidOperationException("Construct and retire generated environment readers outside the Jolt gate.");
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        EnsureOutsidePhysicsGate();
        IsDisposed = true;
        List<Exception>? failures = null;
        void Release(Action action)
        {
            try { action(); }
            catch (Exception error) { (failures ??= new()).Add(error); }
        }
        // Close callbacks before draining navigation; physics may still hold a callback it captured earlier.
        if (_natural != null) Release(_natural.Stop);
        for (int i = _initialized.Count - 1; i >= 0; i--) Release(_initialized[i].Dispose);
        if (_natural != null) Release(_natural.Dispose);
        if (_body != null) Release(_body.Dispose);
        if (_physics != null) Release(_physics.Dispose);
        // Synchronous source readers borrow the enclosing pin. No worker can still read when this is released.
        if (_lease != null) Release(_lease.Dispose);
        if (_ownedStore != null) Release(() => _ownedStore.DisposeAsync().GetAwaiter().GetResult());
        _initialized.Clear();
        _natural = null;
        _body = null;
        _physics = null;
        _lease = null;
        _ownedStore = null;
        Surface = null!;
        if (failures != null) throw new AggregateException("Zone retirement failed after all owners were drained.", failures).Flatten();
    }
}

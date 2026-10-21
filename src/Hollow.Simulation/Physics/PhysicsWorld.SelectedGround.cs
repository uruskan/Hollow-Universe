using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using Hollow.Surface;
using JoltPhysicsSharp;

namespace Hollow.Simulation.Physics;

/// <summary>A failed open may still own a failed native retirement. Dispose retries it; reservations remain charged.</summary>
public sealed class GroundPhysicsFailure : IDisposable
{
    private PhysicsWorld.SelectedSetupRetirement? _retirement;
    internal GroundPhysicsFailure(GroundAreaStatus status, string message, PhysicsWorld.SelectedSetupRetirement? retirement = null)
    { Status = status; Message = message; _retirement = retirement; }
    public GroundAreaStatus Status { get; }
    public string Message { get; }
    public bool RetirementPending => _retirement != null;
    public void Dispose() { _retirement?.Dispose(); _retirement = null; }
}

internal enum GroundNativeStage
{
    SetupParked, SetupLayers, SetupBroadPhase, SetupFilters, SetupSystem, BeforeSetupDispose,
    BeforeMesh, MeshSettings, ShapeCreated, BeforePublication, BodyCreated, BodyAdded, BeforeTileDispose
}

/// <summary>Internal deterministic failure/barrier seam; never exposed in gameplay options.</summary>
internal sealed class GroundPhysicsHooks
{
    internal Action<GroundNativeStage>? At;
    internal int SetupResources, MeshSettings, Shapes, Bodies;
    internal void Reach(GroundNativeStage stage) => At?.Invoke(stage);
}

public sealed partial class PhysicsWorld
{
    // Fixed physics body/prop-ID arrays and task slots plus coordinator dictionary/ticket-set high-water storage.
    // The ledger separately reserves its Work/area sets. Capacities remain charged after the last tile is released.
    // Per-entry coefficients include bucket/entry arrays, growth overlap and temporary retirement enumeration.
    internal static long SelectedOwnerManagedBytes(GroundStreamingLimits limits) =>
        checked(512L * 1024 + 256L * limits.MaxTiles + 128L * limits.MaxAreaTickets);
    private bool _selectedMode;
    private GroundStreamingOwner? _selectedOwner;
    private WorldLease? _selectedPin;
    private GroundAreaCoordinator? _selectedGround;
    private GroundPhysicsHooks? _selectedHooks;

    /// <summary>
    /// Explicit inactive selected-ground factory. It forks the source pin and reserves setup before native work.
    /// On false, dispose a nonnull failure if RetirementPending: a failed cleanup never releases its reservation.
    /// Legacy listeners/props and gameplay Ensure paths are deliberately unavailable until their transactions migrate.
    /// </summary>
    public static bool TryCreateSelected(WorldLease source, ZoneFrame frame, GroundStreamingBudget budget,
        GroundPhysicsOptions options, out PhysicsWorld? world, out GroundPhysicsFailure? failure) =>
        TryCreateSelected(source, frame, budget, options, null, out world, out failure);

    internal static bool TryCreateSelected(WorldLease source, ZoneFrame frame, GroundStreamingBudget budget,
        GroundPhysicsOptions options, GroundPhysicsHooks? hooks, out PhysicsWorld? world, out GroundPhysicsFailure? failure)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(budget); ArgumentNullException.ThrowIfNull(options);
        if (JoltRuntime.Gate.IsHeldByCurrentThread) throw new InvalidOperationException("Open selected physics outside the Jolt gate.");
        world = null; failure = null; options.Validate(budget);
        WorldLease? pin = null; GroundStreamingOwner? owner = null;
        var setup = new NativeSetup(hooks);
        try
        {
            pin = source.Fork();
            if (pin.Surface is not IPlannedGroundCollision { HasPlannedGroundCollision: true } planned)
                throw new NotSupportedException("The source has no admitted selected collision plan capability.");
            if (planned.CollisionPreparationBounds.BatchBytes(options.MaxRequestTiles) > budget.Limits.MaxPlanningBytes)
            {
                pin.Dispose(); failure = new(GroundAreaStatus.Capacity, "The configured complete request cannot fit its planning allowance."); return false;
            }
            if (!budget.TryOpenOwner(SelectedOwnerManagedBytes(budget.Limits), out owner))
            { pin.Dispose(); failure = new(GroundAreaStatus.Capacity, "Active or retiring physics owners exhaust the shared setup allowance."); return false; }
            setup.Initialize();
            var candidate = new PhysicsWorld(planned, frame, setup)
            { _selectedMode = true, _selectedOwner = owner, _selectedPin = pin, _selectedHooks = hooks };
            candidate._selectedGround = new GroundAreaCoordinator(planned, frame, owner!, options,
                candidate.BuildSelectedTile, static native => ((SelectedNativeTile)native).Publish());
            world = candidate;
            return true;
        }
        catch (Exception error)
        {
            var retirement = new SelectedSetupRetirement(owner, setup, pin);
            try { retirement.Dispose(); }
            catch (Exception cleanup)
            {
                failure = new(GroundAreaStatus.Failed, "Selected physics opening and cleanup failed: " + error.Message + "; " + cleanup.Message, retirement);
                return false;
            }
            failure = new(GroundAreaStatus.Failed, "Selected physics opening failed: " + error.Message);
            return false;
        }
    }

    public GroundAreaRequest RequestGround(Vector3 from, Vector3 to, float reach, CancellationToken cancellation = default) =>
        (_selectedGround ?? throw new InvalidOperationException("An open selected physics owner is required.")).Request(from, to, reach, cancellation);
    public GroundAreaRequest RequestGround(Vector3 point, float reach, CancellationToken cancellation = default) =>
        RequestGround(point, point, reach, cancellation);

    /// <summary>Checks this exact physics owner; a ready ticket from another active or previous world cannot authorize it.</summary>
    public GroundAreaStatus GroundState(GroundAreaTicket ticket)
    { ArgumentNullException.ThrowIfNull(ticket); return _selectedGround?.State(ticket) ?? GroundAreaStatus.Retired; }

    /// <summary>Bounded dispatch/publication. Synchronous mode uses the same shared execution slots and never waits for another worker.</summary>
    public void PumpSelectedGround(bool synchronous = false) =>
        (_selectedGround ?? throw new InvalidOperationException("An open selected physics owner is required.")).Pump(synchronous);

    private void RequireLegacyGround()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_selectedMode) throw new NotSupportedException("Selected ground requires an owned area ticket and explicit readiness transaction.");
    }

    private IDisposable BuildSelectedTile(TileKey key, SelectedGroundCollisionPlan plan, CancellationToken cancellation)
    {
        long started = Stopwatch.GetTimestamp();
        var native = new SelectedNativeTile(this, key, _selectedHooks);
        try
        {
            _selectedHooks?.Reach(GroundNativeStage.BeforeMesh);
            cancellation.ThrowIfCancellationRequested();
            TriangleMesh mesh = ((IPlannedGroundCollision)Surface).BuildGroundCollision(plan, cancellation);
            GroundMeshCheck.Require(key, mesh.Positions, mesh.Indices, mesh.VertexCount, mesh.TriangleCount);
            var vertices = new Vector3[mesh.VertexCount];
            var triangles = new IndexedTriangle[mesh.TriangleCount];
            for (int i = 0; i < vertices.Length; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                vertices[i] = new Vector3(mesh.Positions[3 * i], mesh.Positions[3 * i + 1], mesh.Positions[3 * i + 2]);
            }
            for (int i = 0; i < triangles.Length; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                triangles[i] = new IndexedTriangle(mesh.Indices[3 * i], mesh.Indices[3 * i + 1], mesh.Indices[3 * i + 2], 0, 0);
            }
            native.Create(vertices, triangles);
            cancellation.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _tilesBuilt);
            Interlocked.Add(ref _buildTicks, Stopwatch.GetTimestamp() - started);
            return native;
        }
        catch (Exception error)
        {
            try { native.Dispose(); }
            catch (Exception cleanup)
            { throw new GroundOwnedBuildFailure(native, new AggregateException("Selected shape construction and rollback failed.", error, cleanup)); }
            throw;
        }
    }

    private void DisposeSelected()
    {
        if (JoltRuntime.Gate.IsHeldByCurrentThread) throw new InvalidOperationException("Drain selected physics outside the Jolt gate.");
        _selectedGround?.RequireOwnerThread();
        _closing = true;
        List<Exception>? failures = null;
        try { _selectedGround?.StopAndDrain(); } catch (Exception error) { (failures ??= new()).Add(error); }
        bool released = false;
        try
        {
            _selectedOwner!.BeginRetirement();
            released = _selectedOwner.TryReleaseSetup(_nativeSetup.Dispose);
        }
        catch (Exception error) { (failures ??= new()).Add(error); }
        if (released)
        {
            _disposed = true; _selectedPin!.Dispose(); _selectedPin = null; _selectedOwner = null;
            _selectedGround = null; _selectedHooks = null; Surface = null!;
        }
        else (failures ??= new()).Add(new InvalidOperationException("Selected native retirement is incomplete; the source pin remains held. Retry Dispose."));
        if (failures != null) throw new AggregateException("Selected physics retirement reported failures.", failures);
    }

    internal sealed class SelectedSetupRetirement(GroundStreamingOwner? owner, NativeSetup setup, WorldLease? pin) : IDisposable
    {
        private bool _done;
        public void Dispose()
        {
            if (_done) return;
            if (owner != null)
            {
                owner.BeginRetirement();
                if (!owner.TryReleaseSetup(setup.Dispose)) throw new InvalidOperationException("Selected setup still owns dependencies.");
            }
            else setup.Dispose();
            pin?.Dispose(); _done = true;
        }
    }

    private sealed class SelectedNativeTile(PhysicsWorld world, TileKey key, GroundPhysicsHooks? hooks) : IDisposable
    {
        private PhysicsWorld? _world = world;
        private Shape? _shape;
        private MeshShapeSettings? _settings;
        private bool _created, _added;
        private readonly BodyID _id = new((uint)key.Index);
        internal void Create(Vector3[] vertices, IndexedTriangle[] triangles)
        {
            _settings = new MeshShapeSettings(vertices, triangles).Owned();
            if (hooks != null) Interlocked.Increment(ref hooks.MeshSettings);
            hooks?.Reach(GroundNativeStage.MeshSettings);
            _shape = _settings.Create() ?? throw new MalformedGroundTileException(key, "Jolt refused its mesh");
            if (hooks != null) Interlocked.Increment(ref hooks.Shapes);
            hooks?.Reach(GroundNativeStage.ShapeCreated);
            ReleaseSettings();
        }
        internal void Publish()
        {
            PhysicsWorld owner = _world ?? throw new ObjectDisposedException(nameof(SelectedNativeTile));
            lock (JoltRuntime.Gate)
            {
                hooks?.Reach(GroundNativeStage.BeforePublication);
                if (owner._bodies[_id.ID] != NoBody) throw new InvalidOperationException("Selected ground body ID already has an owner.");
                using var settings = new BodyCreationSettings(_shape!, Vector3.Zero, Quaternion.Identity, MotionType.Static, StaticLayer).Owned();
                _ = owner.Jolt.BodyInterface.CreateBodyWithID(_id, settings) ?? throw new InvalidOperationException("Jolt refused a selected ground body.");
                _created = true; owner._bodies[_id.ID] = ParkedBody; owner.BodiesMade++;
                if (hooks != null) Interlocked.Increment(ref hooks.Bodies);
                hooks?.Reach(GroundNativeStage.BodyCreated);
                owner.Jolt.BodyInterface.AddBody(_id, Activation.DontActivate);
                _added = true; owner._bodies[_id.ID] = BodyInWorld; owner.NoteBodyChanged();
                hooks?.Reach(GroundNativeStage.BodyAdded);
            }
        }
        public void Dispose()
        {
            if (_world == null) return;
            hooks?.Reach(GroundNativeStage.BeforeTileDispose);
            lock (JoltRuntime.Gate)
            {
                if (_added) { _world.Jolt.BodyInterface.RemoveBody(_id); _added = false; _world.NoteBodyChanged(); }
                if (_created)
                {
                    _world.Jolt.BodyInterface.DestroyBody(_id); _created = false; _world._bodies[_id.ID] = NoBody;
                    if (hooks != null) Interlocked.Decrement(ref hooks.Bodies);
                }
                if (_shape != null)
                { _shape.Dispose(); _shape = null; if (hooks != null) Interlocked.Decrement(ref hooks.Shapes); }
                ReleaseSettings(); _world = null;
            }
        }
        private void ReleaseSettings()
        {
            if (_settings == null) return;
            _settings.Dispose(); _settings = null; if (hooks != null) Interlocked.Decrement(ref hooks.MeshSettings);
        }
    }

    internal sealed class NativeSetup(GroundPhysicsHooks? hooks) : IDisposable
    {
        private Shape? _parked;
        private ObjectLayerPairFilterTable? _layers;
        private BroadPhaseLayerInterfaceTable? _broad;
        private ObjectVsBroadPhaseLayerFilterTable? _filters;
        private PhysicsSystem? _system;
        internal Shape Parked => _parked ?? throw new InvalidOperationException("Physics setup is incomplete.");
        internal PhysicsSystem System => _system ?? throw new InvalidOperationException("Physics setup is incomplete.");
        internal void Initialize()
        {
            JoltRuntime.EnsureInitialised();
            lock (JoltRuntime.Gate)
            {
                using (var settings = new SphereShapeSettings(.01f).Owned()) _parked = settings.Create();
                Added(GroundNativeStage.SetupParked);
                _layers = new ObjectLayerPairFilterTable(2); Added(GroundNativeStage.SetupLayers);
                _layers.EnableCollision(StaticLayer, MovingLayer); _layers.EnableCollision(MovingLayer, MovingLayer);
                _broad = new BroadPhaseLayerInterfaceTable(2, 2); Added(GroundNativeStage.SetupBroadPhase);
                _broad.MapObjectToBroadPhaseLayer(StaticLayer, new BroadPhaseLayer(0));
                _broad.MapObjectToBroadPhaseLayer(MovingLayer, new BroadPhaseLayer(1));
                _filters = new ObjectVsBroadPhaseLayerFilterTable(_broad, 2, _layers, 2); Added(GroundNativeStage.SetupFilters);
                _system = new PhysicsSystem(new PhysicsSystemSettings
                {
                    MaxBodies = FirstShipBody + MaxShipBodies, MaxBodyPairs = 4096, MaxContactConstraints = 4096,
                    NumBodyMutexes = 0, ObjectLayerPairFilter = _layers, BroadPhaseLayerInterface = _broad,
                    ObjectVsBroadPhaseLayerFilter = _filters,
                });
                Added(GroundNativeStage.SetupSystem);
            }
        }
        private void Added(GroundNativeStage stage)
        { if (hooks != null) Interlocked.Increment(ref hooks.SetupResources); hooks?.Reach(stage); }
        public void Dispose()
        {
            hooks?.Reach(GroundNativeStage.BeforeSetupDispose);
            lock (JoltRuntime.Gate)
            {
                // Otherwise the native system, its bodies and their shapes are never freed (JoltRuntime.HandOverToSystem).
                if (_system != null && _layers != null && _broad != null && _filters != null)
                    JoltRuntime.HandOverToSystem(_system, _layers, _broad, _filters);
                Release(ref _system); Release(ref _filters); Release(ref _broad); Release(ref _layers); Release(ref _parked);
            }
        }
        private void Release<T>(ref T? value) where T : class, IDisposable
        {
            if (value == null) return;
            value.Dispose(); value = null; if (hooks != null) Interlocked.Decrement(ref hooks.SetupResources);
        }
    }

    private static NativeSetup CreateLegacySetup(ISurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (surface is IPlannedGroundCollision { HasPlannedGroundCollision: true } or IFormationSurface { Formations: not null })
            throw new NotSupportedException("Use the explicit selected physics factory and shared ground allowance.");
        _ = GroundCollisionLayout.LevelOn(surface.Radius);
        var setup = new NativeSetup(null);
        try { setup.Initialize(); return setup; }
        catch (Exception error)
        {
            try { setup.Dispose(); } catch (Exception cleanup) { throw new AggregateException("Physics setup and rollback failed.", error, cleanup); }
            throw;
        }
    }
}

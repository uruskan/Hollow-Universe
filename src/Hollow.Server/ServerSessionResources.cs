using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Hollow.Net.Protocol;
using Hollow.Persistence;
using Hollow.Simulation;
using Hollow.Simulation.Content;
using Hollow.Simulation.Physics;
using Hollow.Surface;
using LiteNetLib;

namespace Hollow.Server;

/// <summary>
/// Constructor and retirement ownership for one server session. The bootstrap lease starts before landing search,
/// transfers to the zone, and outlives every reader through that zone's retirement. A supplied store stays owned by
/// its caller. The server retains tick/message dispatch and decides when to save and disconnect its players.
/// </summary>
internal sealed class ServerSessionResources : IDisposable
{
    private WorldStore? _store;
    private readonly bool _ownsStore;
    private WorldLease? _bootstrap;
    private PlanetEnvironmentDescriptor? _environmentDescriptor;
    private TerrainIdentity? _terrainIdentity;
    private Zone? _zone;
    private NetManager? _network;
    private PersistenceWorker? _persistence;
    private readonly List<ServerModule> _initialized = new();
    private bool _disposed;

    public ServerSessionResources(WorldStore? store)
    {
        EnsureOutsidePhysicsGate();
        _store = store ?? new WorldStore(new WorldStoreOptions());
        _ownsStore = store == null;
    }

    /// <summary>The immutable descriptor actually acquired by this session, retained after transfer to its zone.</summary>
    public PlanetEnvironmentDescriptor EnvironmentDescriptor => _environmentDescriptor ??
        throw new InvalidOperationException("The session has no acquired environment.");

    public ZoneSpec ResolveLanding(ServerOptions options)
    {
        EnsureOutsidePhysicsGate();
        ZoneSpec spec = ZoneSpec.StartingWorld(options.Latitude, options.Longitude) with
        {
            StandIn = options.StandIn,
            Ground = options.Ground,
            RotationTimeScale = options.RotationTimeScale,
        };
        PlanetEnvironmentDescriptor descriptor = spec.EnvironmentDescriptor();
        if (!descriptor.IsLegacyMultiplayerCompatible)
            throw new NotSupportedException("This environment cannot be reconstructed by the current multiplayer protocol.");
        // Landing search is already a generated-world read, even though the zone does not exist yet.
        _bootstrap = _store!.Acquire(descriptor);
        _environmentDescriptor = _bootstrap.Descriptor;
        _terrainIdentity = TerrainIdentity.FromDescriptor(_environmentDescriptor);
        (double latitude, double longitude) = ZoneSpec.LandingFor(_bootstrap.Surface, spec.LatitudeDegrees, spec.LongitudeDegrees);
        return spec with { LatitudeDegrees = latitude, LongitudeDegrees = longitude };
    }

    public Zone CreateZone(ZoneSpec spec, GameContent content)
    {
        WorldLease lease = _bootstrap ?? throw new InvalidOperationException("Acquire the environment before constructing its zone.");
        _bootstrap = null; // Zone consumes this pin even when its constructor rolls back.
        return _zone = new Zone(lease, spec, content: content);
    }

    public PersistenceWorker OpenPersistence(string path, ZoneSpec spec, Action<string> log)
    {
        TerrainIdentity terrain = _terrainIdentity ?? throw new InvalidOperationException("Acquire the environment before opening its persistence.");
        if (spec.SystemAddress != terrain.SystemAddress || spec.BodyPath != terrain.BodyPath)
            throw new InvalidOperationException("Persistence must belong to the acquired environment's body.");
        return _persistence = ServerPersistence.Open(path, terrain, log);
    }

    public NetManager StartNetwork(INetEventListener listener, int port)
    {
        _network = new NetManager(listener)
        {
            AutoRecycle = true,
            EnableStatistics = true,
            DisconnectTimeout = 10_000,
            UpdateTime = 1,
            IPv6Enabled = false,
            UnconnectedMessagesEnabled = false,
        };
        if (!_network.StartInManualMode(IPAddress.Any, IPAddress.IPv6Any, port))
            throw new SocketException((int)SocketError.AddressAlreadyInUse);
        return _network;
    }

    public void Initialize(ServerModule module, MessageHandlers<PlayerSession> handlers)
    {
        _initialized.Add(module);
        module.Init(handlers);
    }

    /// <summary>Give already queued goodbyes their existing bounded flush window before closing the socket.</summary>
    public void FlushGoodbyes(List<Exception> failures)
    {
        EnsureOutsidePhysicsGate();
        for (int i = 0; i < 5; i++)
        {
            Attempt(() => _network!.ManualUpdate(10), failures);
            Attempt(() => Thread.Sleep(10), failures);
        }
    }

    /// <summary>The existing shutdown save window; disposal still drains the persistence worker after a timeout.</summary>
    public static void WaitForSave(Task save)
    {
        EnsureOutsidePhysicsGate();
        if (!save.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("The shutdown save did not complete within ten seconds; persistence will still be drained.");
    }

    /// <summary>Keep the construction error as primary; clean rollback returns so the original catch can rethrow it.</summary>
    public void Rollback(Exception original)
    {
        try { Dispose(); }
        catch (Exception cleanup) { throw new AggregateException("Server construction and rollback failed.", original, cleanup).Flatten(); }
    }

    public static void Attempt(Action action, List<Exception> failures)
    {
        try { action(); }
        catch (Exception error) { failures.Add(error); }
    }

    private static void EnsureOutsidePhysicsGate()
    {
        if (JoltRuntime.Gate.IsHeldByCurrentThread)
            throw new InvalidOperationException("Construct and retire server sessions outside the Jolt gate.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        EnsureOutsidePhysicsGate();
        _disposed = true;
        var failures = new List<Exception>();
        if (_bootstrap != null) Attempt(_bootstrap.Dispose, failures);
        if (_network != null) Attempt(() => _network.Stop(), failures);
        for (int i = _initialized.Count - 1; i >= 0; i--) Attempt(_initialized[i].Dispose, failures);
        if (_persistence != null) Attempt(_persistence.Dispose, failures);
        if (_zone != null) Attempt(_zone.Dispose, failures);
        if (_ownsStore) Attempt(() => _store!.DisposeAsync().GetAwaiter().GetResult(), failures);
        _initialized.Clear();
        _bootstrap = null;
        _environmentDescriptor = null;
        _terrainIdentity = null;
        _network = null;
        _persistence = null;
        _zone = null;
        _store = null;
        if (failures.Count != 0) throw new AggregateException("Server resources failed after retirement.", failures).Flatten();
    }
}

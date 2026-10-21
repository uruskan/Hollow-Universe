using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hollow.Net.Protocol;
using Hollow.Simulation;
using Hollow.Simulation.Physics;
using Hollow.Surface;

namespace Hollow.Client;

/// <summary>
/// Owner-thread lifecycle of the client's generated world and prediction. Stages replacements before retiring old
/// readers, exposes their lease before shared-owner callbacks, and releases that pin only after dependent cleanup.
/// Transport, protocol identity and tick clocks remain the GameClient host's responsibility.
/// </summary>
internal sealed class ClientWorldSession : IDisposable
{
    private readonly ClientOptions _options;
    private readonly WorldStore _store;
    private readonly bool _ownsStore;
    private readonly Action _clearReplication;
    private readonly List<ClientModule> _initializedModules = new();
    private WorldLease? _lease;
    private bool _opening, _retiring, _closed;

    public ClientWorldSession(ClientOptions options, Action clearReplication)
    {
        _options = options;
        _ownsStore = options.WorldStore == null;
        _store = options.WorldStore ?? new WorldStore(new WorldStoreOptions());
        _clearReplication = clearReplication;
    }

    public void Initialize(ClientModule module, MessageHandlers<GameClient> handlers)
    {
        // A module can acquire resources before Init throws; include it in constructor rollback first.
        _initializedModules.Add(module);
        module.Init(handlers);
    }

    public void Rollback(Exception opening, Action stopTransport)
    {
        var failures = new List<Exception> { opening };
        try { Dispose(); } catch (Exception error) { failures.Add(error); }
        try { stopTransport(); } catch (Exception error) { failures.Add(error); }
        try { Retirement.GetAwaiter().GetResult(); } catch (Exception error) { failures.Add(error); }
        if (failures.Count > 1) throw new AggregateException("Client construction and rollback failed.", failures).Flatten();
    }

    public ZoneSpec? Zone { get; private set; }
    public ISurface? Surface { get; private set; }
    public Predictor? Prediction { get; private set; }
    public event Action? WorldRetiring;
    public Task Retirement => _ownsStore ? _store.Retirement : Task.CompletedTask;
    public bool CanOpen => !_closed && !_opening && !_retiring;
    public WorldLease ForkLease() => (_lease ?? throw new InvalidOperationException("No world is open.")).Fork();

    /// <summary>
    /// False with a reason is a rejected server descriptor/acquisition. False without one means replacement admission
    /// was closed (including by a reentrant retirement subscriber). Opening failures retire partial state and propagate.
    /// </summary>
    public bool TryOpen(ZoneInfo info, EnvironmentIdentity identity, Action beforePrediction, Action afterPrediction, out string? rejection)
    {
        rejection = null;
        if (!CanOpen) return false;
        _opening = true;
        WorldLease? staged = null;
        try
        {
            ZoneSpec nextZone;
            try
            {
                if (!NetConvert.TryResolveEnvironment(info, identity, out ZoneSpec? resolvedZone,
                    out PlanetEnvironmentDescriptor? descriptor, out string? reason))
                    throw new InvalidOperationException(reason);
                nextZone = resolvedZone!;
                staged = _store.Acquire(descriptor!);
            }
            catch (Exception error)
            {
                rejection = "Cannot open the server's world: " + error.Message;
                return false;
            }

            Retire();
            if (_closed) return false;
            Zone = nextZone;
            _lease = staged;
            Surface = staged.Surface;
            staged = null; // The published session now owns this pin, including partial-opening cleanup.
            try
            {
                // The published lease is available to shared physics/nature/building owners before prediction starts.
                // A separate construction pin also protects a callback that reentrantly disposes the host.
                using WorldLease construction = _lease.Fork();
                beforePrediction();
                if (_closed) return false;
                PhysicsWorld? shared = _options.SharedPhysics?.Invoke(nextZone, construction.Surface);
                if (_closed) return false;
                var predictor = new Predictor(nextZone, construction.Surface, shared);
                if (_closed) { predictor.Dispose(); return false; }
                Prediction = predictor;
                afterPrediction();
                return !_closed;
            }
            catch (Exception opening)
            {
                try { Retire(); }
                catch (Exception cleanup)
                {
                    throw new AggregateException("Opening the client world and retiring its partial state failed.", opening, cleanup);
                }
                throw;
            }
        }
        finally
        {
            staged?.Dispose();
            _opening = false;
        }
    }

    /// <summary>Notifies readers while the old world is live, then retires modules, physics, replication and the lease.</summary>
    public void Retire()
    {
        if (_retiring) return;
        RequireOutsidePhysicsGate("Retire client world readers");
        _retiring = true;
        try
        {
            List<Exception>? failures = null;
            void Clean(Action action)
            {
                try { action(); }
                catch (Exception error) { (failures ??= new()).Add(error); }
            }
            if (_lease != null && WorldRetiring != null)
                foreach (Action reader in WorldRetiring.GetInvocationList()) Clean(reader);
            for (int i = _initializedModules.Count - 1; i >= 0; i--) Clean(_initializedModules[i].Dispose);
            if (Prediction != null) Clean(Prediction.Dispose);
            Prediction = null;
            Surface = null;
            Zone = null;
            Clean(_clearReplication);
            WorldLease? lease = _lease;
            _lease = null;
            if (lease != null) Clean(lease.Dispose);
            if (failures != null) throw new AggregateException("Client world retirement failed.", failures).Flatten();
        }
        finally { _retiring = false; }
    }

    /// <summary>Close before transport teardown, which may invoke further connection callbacks.</summary>
    public void CloseAdmission()
    {
        RequireOutsidePhysicsGate("Dispose the client");
        _closed = true;
    }

    public void Dispose()
    {
        CloseAdmission();
        try { Retire(); }
        finally
        {
            WorldRetiring = null;
            if (_ownsStore) _store.Dispose();
        }
    }

    private static void RequireOutsidePhysicsGate(string action)
    {
        if (JoltRuntime.Gate.IsHeldByCurrentThread)
            throw new InvalidOperationException(action + " outside the Jolt gate.");
    }
}

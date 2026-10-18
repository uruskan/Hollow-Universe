using System;

namespace Hollow.Surface;

/// <summary>
/// One pin on a session-owned world. Surface is borrowed for this lease's lifetime; passing that raw reference does
/// not transfer ownership. Fork before dispatching work. Dispose clears all strong world/descriptor/store references.
/// </summary>
public sealed class WorldLease : IDisposable
{
    private readonly object _gate = new();
    private WorldStore? _store;
    private WorldStore.Entry? _entry;
    private ISurface? _surface;
    private PlanetEnvironmentDescriptor? _descriptor;

    internal WorldLease(WorldStore store, WorldStore.Entry entry, ISurface surface, PlanetEnvironmentDescriptor descriptor)
    {
        _store = store;
        _entry = entry;
        _surface = surface;
        _descriptor = descriptor;
    }

    public ISurface Surface
    {
        get { lock (_gate) return _surface ?? throw new ObjectDisposedException(nameof(WorldLease)); }
    }

    public PlanetEnvironmentDescriptor Descriptor
    {
        get { lock (_gate) return _descriptor ?? throw new ObjectDisposedException(nameof(WorldLease)); }
    }

    /// <summary>Creates an independent pin. Refused after session retirement begins, even from a live lease.</summary>
    public WorldLease Fork()
    {
        lock (_gate)
        {
            return (_store ?? throw new ObjectDisposedException(nameof(WorldLease))).Fork(_entry!);
        }
    }

    public void Dispose()
    {
        WorldStore? store;
        WorldStore.Entry? entry;
        lock (_gate)
        {
            store = _store;
            entry = _entry;
            _store = null;
            _entry = null;
            _surface = null;
            _descriptor = null;
        }
        // Do not hold the lease lock while releasing. Store cleanup never calls back into a lease.
        if (store != null) store.Release(entry!);
    }
}

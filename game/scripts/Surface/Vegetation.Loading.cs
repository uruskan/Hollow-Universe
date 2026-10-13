using System.Threading;

namespace HollowUniverse.Surface;

// What the loading pipeline reads of the plants (GROUND.md §4.3.1).
public sealed partial class Vegetation
{
    /// <summary>The species' meshes and the painted atlas are built.</summary>
    public bool LibraryReady => _library != null;

    /// <summary>The tree impostors are baked.</summary>
    public bool ImpostorsReady => _impostors != null;

    /// <summary>Scatter tiles asked for and not laid out yet (waiting or on a worker).</summary>
    public int ScatterLeft => Volatile.Read(ref _busy);
}

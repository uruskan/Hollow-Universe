using System;
using Hollow.Persistence;

namespace Hollow.Server;

/// <summary>Opens the world's persistence with its terrain identity before the host starts its network.</summary>
internal static class ServerPersistence
{
    public static PersistenceWorker Open(string path, TerrainIdentity terrain, Action<string> log)
    {
        if (!terrain.IsCanonical) throw new ArgumentException("Server persistence requires the acquired world's canonical identity.", nameof(terrain));
        // The enclosing session owns rollback. Keeping it there preserves this failure alongside any cleanup error.
        return new PersistenceWorker(path, () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), log, terrain);
    }
}

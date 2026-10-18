using System;
using System.IO;
using Hollow.Persistence;
using Hollow.Server;
using Hollow.Simulation;
using Hollow.Surface;
using Microsoft.Data.Sqlite;

namespace Hollow.Tests;

public static class ServerTerrainIdentityTests
{
    [Test]
    public static void ServerPersistenceUsesTheAcquiredLeaseDescriptor()
    {
        using var file = new TempDatabase();
        PlanetEnvironmentDescriptor? dispatched = null;
        int builds = 0;
        using var store = new WorldStore(new WorldStoreOptions(), (descriptor, _) =>
        {
            dispatched = descriptor;
            builds++;
            return new SphereSurface(descriptor.Radius, descriptor.TerrainSeed);
        }, _ => new WorldMemoryEstimate(1, 1));
        var session = new ServerSessionResources(store);
        using (session)
        {
            ZoneSpec initial = ZoneSpec.StartingWorld(0, 0);
            Throws<InvalidOperationException>(() => session.OpenPersistence(file.Path, initial, _ => { }));
            Assert.True(!File.Exists(file.Path), "persistence cannot open before environment acquisition");
            ZoneSpec spec = session.ResolveLanding(new ServerOptions { Ground = GroundKind.Walking });
            Assert.True(ReferenceEquals(dispatched, session.EnvironmentDescriptor), "session captures the actual acquired descriptor");
            Throws<InvalidOperationException>(() => session.OpenPersistence(file.Path,
                spec with { BodyPath = spec.BodyPath + 1 }, _ => { }));
            Assert.True(!File.Exists(file.Path), "wrong-body persistence is rejected before opening a file");
            session.OpenPersistence(file.Path, spec, _ => { });
            using var raw = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = file.Path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            raw.Open();
            using SqliteCommand read = raw.CreateCommand();
            read.CommandText = "SELECT canonical_schema, canonical_identity, identity FROM terrain_identities";
            using SqliteDataReader row = read.ExecuteReader();
            Assert.True(row.Read(), "server stamps canonical identity");
            Assert.Equal((long)session.EnvironmentDescriptor.CanonicalSchema, row.GetInt64(0), "acquired schema");
            Assert.Equal(session.EnvironmentDescriptor.CanonicalIdentity, row.GetString(1), "acquired complete canonical bytes");
            Assert.Equal(session.EnvironmentDescriptor.Identity, row.GetString(2), "acquired digest");
            Assert.Equal(1, builds, "identity capture requires no second world construction");
        }
        Assert.Equal(0, store.Diagnostics.ActiveLeases, "session releases its bootstrap lease after persistence retires");
        Throws<InvalidOperationException>(() => { _ = session.EnvironmentDescriptor; });
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new TestFailure("Expected " + typeof(T).Name);
    }
}

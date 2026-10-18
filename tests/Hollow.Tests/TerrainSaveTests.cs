using System;
using System.IO;
using Hollow.Persistence;
using Hollow.Simulation;
using Hollow.Simulation.Entities;
using Hollow.Surface;
using Microsoft.Data.Sqlite;

namespace Hollow.Tests;

public static class TerrainSaveTests
{
    private static TerrainIdentity Earth => new(StartingWorld.System.Value, StartingWorld.BodyPath,
        WalkingParameters.Earth.TerrainRevision);

    [Test]
    public static void CliffCollisionRevisionFencesEveryWalkingBody()
    {
        Assert.Equal(2, CliffPlacements.GenerationRevision, "review these save identities whenever cliff generation changes");
        Assert.Equal(5, Earth.Revision, "Earth's geography and corrected populations have their own save identity");
        Assert.Equal(5, WalkingParameters.Earth.LayoutVariant, "the reviewed broad-land candidate is the production selection");
        Assert.Equal(2, WalkingParameters.Default.TerrainRevision, "inherited walking bodies also receive corrected biome populations");
        Assert.Equal(5, WalkingParameters.ForBody(Earth.SystemAddress, Earth.BodyPath).TerrainRevision,
            "server lookup selects the new Earth identity");
        Assert.Equal(2, WalkingParameters.ForBody(Earth.SystemAddress, Earth.BodyPath + 1).TerrainRevision,
            "a non-Earth body cannot silently retain revision-zero collision");
    }

    [Test]
    public static void PreviousBiomePopulationSaveIsPreserved()
    {
        foreach (TerrainIdentity current in new[] { Earth, new TerrainIdentity(Earth.SystemAddress, Earth.BodyPath + 1, 2) })
        {
            using var file = new TempDatabase();
            using (GameDatabase.Open(file.Path, current with { Revision = current.Revision - 1 })) { }
            byte[] before = File.ReadAllBytes(file.Path);
            TerrainMismatchException refusal = Refused(file.Path, current);
            Assert.True(refusal.Message.Contains("generated populations", StringComparison.Ordinal), "refusal explains population compatibility");
            Assert.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(file.Path)), "old population save is not rewritten");
        }
    }

    [Test]
    public static void EarlierEarthLandSaveIsRejectedAfterCliffCollisionChanges()
    {
        using var file = new TempDatabase();
        using (GameDatabase.Open(file.Path, Earth with { Revision = 2 })) { }
        byte[] before = File.ReadAllBytes(file.Path);
        TerrainMismatchException refusal = Refused(file.Path, Earth);
        Assert.Equal<int?>(2, refusal.FoundRevision, "revision-2 land does not imply matching cliff solids");
        Assert.True(refusal.Message.Contains("solid cliff placement", StringComparison.Ordinal),
            "the reason explains why unchanged land can still be incompatible");
        Assert.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(file.Path)), "the prior Earth save is preserved");
    }

    [Test]
    public static void PreviousEarthLayoutSaveIsPreservedWhenSelectingNewGeography()
    {
        using var file = new TempDatabase();
        using (GameDatabase.Open(file.Path, Earth with { Revision = 3 })) { }
        byte[] before = File.ReadAllBytes(file.Path);
        TerrainMismatchException refusal = Refused(file.Path, Earth);
        Assert.Equal<int?>(3, refusal.FoundRevision, "previous geography is not loaded into the new layout");
        Assert.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(file.Path)), "previous-layout save remains byte-for-byte intact");
    }

    [Test]
    public static void ChangedCliffCollisionRejectsOldStateOnOtherWalkingBodies()
    {
        uint bodyPath = Earth.BodyPath + 1;
        var current = new TerrainIdentity(Earth.SystemAddress, bodyPath,
            WalkingParameters.ForBody(Earth.SystemAddress, bodyPath).TerrainRevision);
        foreach (int? previous in new int?[] { null, 0 })
        {
            using var file = new TempDatabase();
            TerrainIdentity? old = previous is { } revision ? current with { Revision = revision } : null;
            using (GameDatabase db = GameDatabase.Open(file.Path, old))
            {
                var save = new ZoneSave { SystemAddress = current.SystemAddress, BodyPath = current.BodyPath };
                save.Things.Add(new ThingRecord
                {
                    Id = new EntityId(74), SystemAddress = current.SystemAddress, BodyPath = current.BodyPath,
                    Kind = 1, Data = new byte[] { 1, 2, 3 },
                });
                db.SaveZone(save);
            }
            byte[] before = File.ReadAllBytes(file.Path);
            TerrainMismatchException refusal = Refused(file.Path, current);
            Assert.Equal(previous, refusal.FoundRevision, "both legacy and explicitly revision-zero bodies are refused");
            Assert.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(file.Path)), "other-body saves remain byte-for-byte unchanged");
        }
    }

    [Test]
    public static void FreshTerrainSaveReopensWithItsContents()
    {
        using var file = new TempDatabase();
        var save = new ZoneSave { SystemAddress = Earth.SystemAddress, BodyPath = Earth.BodyPath };
        save.Things.Add(new ThingRecord
        {
            Id = new EntityId(42), SystemAddress = Earth.SystemAddress, BodyPath = Earth.BodyPath,
            Kind = 1, Data = new byte[] { 10, 20, 30 },
        });
        using (var worker = new PersistenceWorker(file.Path, () => 1, terrain: Earth))
            worker.SaveZone(save).GetAwaiter().GetResult();
        using (var worker = new PersistenceWorker(file.Path, () => 2, terrain: Earth))
        {
            ZoneRecord restored = worker.LoadZone(Earth.SystemAddress, Earth.BodyPath).GetAwaiter().GetResult();
            Assert.Equal(1, restored.Things.Count, "the same terrain reopens the world");
            Assert.True(restored.Things[0].Data.AsSpan().SequenceEqual(save.Things[0].Data), "saved contents survive reopening");
        }
    }

    [Test]
    public static void ChangedTerrainRevisionRejectsBeforeWriting()
    {
        using var file = new TempDatabase();
        using (GameDatabase.Open(file.Path, Earth)) { }
        byte[] before = File.ReadAllBytes(file.Path);
        TerrainIdentity changed = Earth with { Revision = Earth.Revision + 1 };
        TerrainMismatchException refusal = Refused(file.Path, changed);
        Assert.Equal<int?>(Earth.Revision, refusal.FoundRevision, "the saved revision is reported");
        Assert.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(file.Path)), "a rejected revision does not rewrite the save");
        using (GameDatabase.Open(file.Path, Earth)) { }
    }

    [Test]
    public static void InheritedTerrainCannotOpenARevisionedEarthSave()
    {
        using var file = new TempDatabase();
        using (GameDatabase.Open(file.Path, Earth)) { }
        byte[] before = File.ReadAllBytes(file.Path);
        TerrainMismatchException refusal = Refused(file.Path, Earth with { Revision = 0 });
        Assert.Equal<int?>(Earth.Revision, refusal.FoundRevision, "stand-in terrain does not bypass the saved revision");
        Assert.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(file.Path)), "stand-in rejection does not rewrite the save");
    }

    [Test]
    public static void InheritedTerrainRetainsUnversionedStateAndStampsRevisionZero()
    {
        using var file = new TempDatabase();
        using (GameDatabase db = GameDatabase.Open(file.Path))
        {
            var save = new ZoneSave { SystemAddress = Earth.SystemAddress, BodyPath = Earth.BodyPath };
            save.Things.Add(new ThingRecord
            {
                Id = new EntityId(63), SystemAddress = Earth.SystemAddress, BodyPath = Earth.BodyPath,
                Kind = 1, Data = new byte[] { 4, 5, 6 },
            });
            db.SaveZone(save);
        }
        using (GameDatabase db = GameDatabase.Open(file.Path, Earth with { Revision = 0 }))
        {
            ZoneRecord restored = db.LoadZone(Earth.SystemAddress, Earth.BodyPath);
            Assert.Equal(1, restored.Things.Count, "inherited terrain retains unversioned world state");
            Assert.True(restored.Things[0].Data.AsSpan().SequenceEqual(new byte[] { 4, 5, 6 }), "the inherited object's data is unchanged");
        }
        TerrainMismatchException refusal = Refused(file.Path, Earth);
        Assert.Equal<int?>(0, refusal.FoundRevision, "the inherited world is now explicitly revision zero");
    }

    [Test]
    public static void PopulatedLegacyTerrainIsRejectedWithoutMigrationOrByteChanges()
    {
        using var file = new TempDatabase();
        // A world containing a placed object but no players still needs protection.
        using (GameDatabase db = GameDatabase.Open(file.Path))
        {
            var save = new ZoneSave { SystemAddress = Earth.SystemAddress, BodyPath = Earth.BodyPath };
            save.Things.Add(new ThingRecord
            {
                Id = new EntityId(91), SystemAddress = Earth.SystemAddress, BodyPath = Earth.BodyPath,
                Kind = 1, Data = new byte[] { 7, 8, 9 },
            });
            db.SaveZone(save);
        }
        using (SqliteConnection raw = OpenRaw(file.Path, SqliteOpenMode.ReadWrite))
        {
            using SqliteCommand command = raw.CreateCommand();
            command.CommandText = "DROP TABLE terrain_revisions; PRAGMA user_version = 8;";
            command.ExecuteNonQuery();
        }
        byte[] before = File.ReadAllBytes(file.Path);
        TerrainMismatchException refusal = Refused(file.Path, Earth);
        Assert.True(refusal.FoundRevision == null, "legacy state is not relabelled as current terrain");
        Assert.Equal(Path.GetFullPath(file.Path), refusal.SavePath, "the error identifies the preserved save");
        Assert.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(file.Path)), "rejected legacy save remains byte-for-byte identical");
        using SqliteConnection read = OpenRaw(file.Path, SqliteOpenMode.ReadOnly);
        using SqliteCommand version = read.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        Assert.Equal(8L, (long)version.ExecuteScalar()!, "rejection precedes schema migration");
    }

    [Test]
    public static void CanonicalTerrainRoundTripsTheCompleteDescriptor()
    {
        using var file = new TempDatabase();
        PlanetEnvironmentDescriptor descriptor = Descriptor();
        TerrainIdentity terrain = TerrainIdentity.FromDescriptor(descriptor);
        using (GameDatabase db = GameDatabase.Open(file.Path, terrain)) db.SaveZone(OneThing(terrain, 201));
        using (GameDatabase db = GameDatabase.Open(file.Path, terrain))
            Assert.Equal(1, db.LoadZone(terrain.SystemAddress, terrain.BodyPath).Things.Count, "matching complete identity restores state");
        using SqliteConnection raw = OpenRaw(file.Path, SqliteOpenMode.ReadOnly);
        using SqliteCommand read = raw.CreateCommand();
        read.CommandText = "SELECT canonical_schema, canonical_identity, identity FROM terrain_identities";
        using SqliteDataReader row = read.ExecuteReader();
        Assert.True(row.Read(), "canonical stamp exists");
        Assert.Equal((long)descriptor.CanonicalSchema, row.GetInt64(0), "descriptor schema is persisted");
        Assert.Equal(descriptor.CanonicalIdentity, row.GetString(1), "complete canonical bytes are persisted");
        Assert.Equal(descriptor.Identity, row.GetString(2), "digest accompanies complete bytes");
    }

    [Test]
    public static void CanonicalTerrainChangesRefuseWithoutWritesOrRevisionFallback()
    {
        using var file = new TempDatabase();
        PlanetEnvironmentDescriptor descriptor = Descriptor();
        TerrainIdentity terrain = TerrainIdentity.FromDescriptor(descriptor);
        using (GameDatabase db = GameDatabase.Open(file.Path, terrain)) db.SaveZone(OneThing(terrain, 202));
        byte[] before = File.ReadAllBytes(file.Path);
        foreach (PlanetEnvironmentDescriptor changed in new[]
        {
            Descriptor(WalkingParameters.Earth with { HillsHeight = WalkingParameters.Earth.HillsHeight + 1 }),
            Descriptor(radius: descriptor.Radius - 1),
        })
        {
            Assert.Equal(terrain.Revision, changed.TerrainRevision, "the coarse revision intentionally stays equal");
            Refused(file.Path, TerrainIdentity.FromDescriptor(changed));
        }
        Refused(file.Path, Earth);
        Throws<TerrainIdentityRequiredException>(() => { using var ignored = GameDatabase.Open(file.Path); });
        Throws<ArgumentException>(() => { using var ignored = GameDatabase.Open(file.Path, terrain with { BodyPath = terrain.BodyPath + 1 }); });
        Assert.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(file.Path)), "all refusals preserve the save byte for byte");
    }

    [Test]
    public static void MalformedOrUnknownCanonicalStampsFailClosedBeforeMigration()
    {
        TerrainIdentity terrain = TerrainIdentity.FromDescriptor(Descriptor());
        foreach (string corruption in new[]
        {
            "canonical_schema = 999", "canonical_identity = 'not-base64'", "canonical_identity = ''",
            "identity = 'invalid'", "identity = '0000000000000000000000000000000000000000000000000000000000000000'",
        })
        {
            using var file = new TempDatabase();
            using (GameDatabase.Open(file.Path, terrain)) { }
            RawExecute(file.Path, "UPDATE terrain_identities SET " + corruption + "; PRAGMA user_version = 9;");
            byte[] before = File.ReadAllBytes(file.Path);
            Refused(file.Path, terrain);
            Assert.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(file.Path)), "bad stamp is neither rewritten nor migrated");
        }
    }

    [Test]
    public static void PopulatedRevisionOnlySaveCannotBeAdoptedAsCanonical()
    {
        TerrainIdentity terrain = TerrainIdentity.FromDescriptor(Descriptor());
        foreach (bool metadataOnly in new[] { false, true })
        {
            using var file = new TempDatabase();
            using (GameDatabase db = GameDatabase.Open(file.Path, Earth))
                db.SaveZone(metadataOnly ? new ZoneSave { SystemAddress = Earth.SystemAddress, BodyPath = Earth.BodyPath, Meta = new byte[] { 1 } }
                    : OneThing(Earth, 203));
            using (GameDatabase.Open(file.Path, Earth)) { } // The unchanged explicit legacy route remains available.
            byte[] before = File.ReadAllBytes(file.Path);
            TerrainMismatchException refusal = Refused(file.Path, terrain);
            Assert.Equal<int?>(Earth.Revision, refusal.FoundRevision, "matching revision is insufficient provenance");
            Assert.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(file.Path)), "legacy contents remain untouched");
        }
    }

    [Test]
    public static void EmptyLegacyBodyCanStampCanonicalButConflictingRevisionCannot()
    {
        TerrainIdentity terrain = TerrainIdentity.FromDescriptor(Descriptor());
        using (var file = new TempDatabase())
        {
            long account;
            using (GameDatabase db = GameDatabase.Open(file.Path, Earth)) account = db.GetOrCreateAccount("Existing", 1);
            RawExecute(file.Path, "DROP TABLE terrain_identities; PRAGMA user_version = 9;");
            using GameDatabase upgraded = GameDatabase.Open(file.Path, terrain);
            Assert.Equal(GameDatabase.SchemaVersion, upgraded.Version, "migration and stamp commit together");
            Assert.Equal(account, upgraded.GetOrCreateAccount("Existing", 2), "nonspatial accounts survive adoption");
        }
        using (var file = new TempDatabase())
        {
            using (GameDatabase.Open(file.Path, Earth with { Revision = Earth.Revision - 1 })) { }
            byte[] before = File.ReadAllBytes(file.Path);
            Refused(file.Path, terrain);
            Assert.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(file.Path)), "an empty body does not override a conflicting revision");
        }
    }

    [Test]
    public static void CanonicalAdmissionCannotTriggerHistoricalDeletionOfAnotherBody()
    {
        using var file = new TempDatabase();
        TerrainIdentity other = Earth with { BodyPath = Earth.BodyPath + 1, Revision = 0 };
        using (GameDatabase db = GameDatabase.Open(file.Path, other)) db.SaveZone(OneThing(other, 208));
        // Recreate the relevant older-schema boundary without running its destructive migration.
        RawExecute(file.Path, "DROP TABLE terrain_identities; DROP TABLE terrain_revisions; PRAGMA user_version = 6;");
        byte[] before = File.ReadAllBytes(file.Path);
        Refused(file.Path, TerrainIdentity.FromDescriptor(Descriptor()));
        Assert.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(file.Path)), "empty requested body cannot erase another body's legacy rows");
    }

    [Test]
    public static void CanonicalIdentityIsRecheckedBeforeEveryWrite()
    {
        using var file = new TempDatabase();
        TerrainIdentity terrain = TerrainIdentity.FromDescriptor(Descriptor());
        using (GameDatabase db = GameDatabase.Open(file.Path, terrain))
        {
            RawExecute(file.Path, "UPDATE terrain_identities SET identity = 'tampered';");
            Throws<TerrainMismatchException>(() => db.SaveZone(OneThing(terrain, 204)));
            Throws<TerrainMismatchException>(() => db.GetOrCreateAccount("MustNotWrite", 1));
            Assert.Equal(0, db.ThingCount, "failed write transaction inserts no thing");
        }
        using SqliteConnection raw = OpenRaw(file.Path, SqliteOpenMode.ReadOnly);
        using SqliteCommand count = raw.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM accounts";
        Assert.Equal(0L, (long)count.ExecuteScalar()!, "failed account mutation writes nothing");
    }

    [Test]
    public static void IdentityBoundDatabaseRejectsOtherBodiesAndForeignEntityIds()
    {
        using var file = new TempDatabase();
        TerrainIdentity terrain = TerrainIdentity.FromDescriptor(Descriptor());
        TerrainIdentity other = Earth with { BodyPath = Earth.BodyPath + 1, Revision = 0 };
        using (GameDatabase db = GameDatabase.Open(file.Path, other)) db.SaveZone(OneThing(other, 205));
        using GameDatabase bound = GameDatabase.Open(file.Path, terrain);
        Throws<InvalidOperationException>(() => bound.LoadZone(other.SystemAddress, other.BodyPath));
        Throws<InvalidOperationException>(() => bound.LoadCharacters(other.SystemAddress, other.BodyPath));
        Throws<InvalidOperationException>(() => bound.SaveZone(OneThing(other, 206)));
        Throws<InvalidOperationException>(() => bound.SaveCharacters(new[]
            { new CharacterRecord { Id = new EntityId(206), SystemAddress = other.SystemAddress, BodyPath = other.BodyPath } }));
        Throws<InvalidOperationException>(() => bound.SaveZone(OneThing(terrain, 205)));
        var removal = new ZoneSave { SystemAddress = terrain.SystemAddress, BodyPath = terrain.BodyPath };
        removal.RemovedThings.Add(new EntityId(205));
        Throws<InvalidOperationException>(() => bound.SaveZone(removal));
        var nested = new ZoneSave { SystemAddress = terrain.SystemAddress, BodyPath = terrain.BodyPath };
        nested.Things.Add(OneThing(other, 207).Things[0]);
        Throws<InvalidOperationException>(() => bound.SaveZone(nested));
        Assert.Equal(1, bound.ThingCount, "foreign object is neither replaced nor deleted");
        Assert.Equal(0, bound.LoadZone(terrain.SystemAddress, terrain.BodyPath).Things.Count, "the bound body stays empty");
    }

    private static PlanetEnvironmentDescriptor Descriptor(WalkingParameters? parameters = null, double? radius = null)
    {
        ZoneSpec spec = ZoneSpec.StartingWorld(0, 0);
        return EnvironmentDescriptors.Resolve(spec.SystemAddress, spec.BodyPath, radius ?? spec.Radius,
            walkingParameters: parameters) ?? throw new TestFailure("Earth metadata must resolve");
    }

    private static ZoneSave OneThing(TerrainIdentity terrain, ulong id)
    {
        var save = new ZoneSave { SystemAddress = terrain.SystemAddress, BodyPath = terrain.BodyPath };
        save.Things.Add(new ThingRecord { Id = new EntityId(id), SystemAddress = terrain.SystemAddress,
            BodyPath = terrain.BodyPath, Kind = 1, Data = new byte[] { 1, 2, 3 } });
        return save;
    }

    private static void RawExecute(string path, string sql)
    {
        using SqliteConnection raw = OpenRaw(path, SqliteOpenMode.ReadWrite);
        using SqliteCommand command = raw.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new TestFailure("Expected " + typeof(T).Name);
    }

    private static TerrainMismatchException Refused(string path, TerrainIdentity terrain)
    {
        try
        {
            using var worker = new PersistenceWorker(path, () => 1, terrain: terrain);
        }
        catch (TerrainMismatchException e)
        {
            return e;
        }
        throw new TestFailure("incompatible terrain must be refused before starting persistence");
    }

    private static SqliteConnection OpenRaw(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }
}

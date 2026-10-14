using System;
using System.IO;
using Hollow.Persistence;
using Hollow.Simulation.Entities;

namespace Hollow.Tests;

/// <summary>A database file in the temp folder, deleted afterwards.</summary>
public sealed class TempDatabase : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"hollow-test-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        foreach (string suffix in new[] { "", "-wal", "-shm" })
        {
            File.Delete(Path + suffix);
        }
    }
}

public static class PersistenceTests
{
    /// <summary>
    /// GROUND.md §4.7: a character is saved and comes back exactly, through the worker, after the database is closed
    /// and opened again (a server restart); names are one account whatever their case; the schema is versioned.
    /// </summary>
    [Test]
    public static void CharacterIsSavedAndRestored()
    {
        using var file = new TempDatabase();
        long now = 1_800_000_000_000;
        CharacterRecord saved;
        using (var worker = new PersistenceWorker(file.Path, () => now))
        {
            (long account, CharacterRecord? none) = worker.Login("Ada").Result;
            Assert.True(none == null, "a new account has no character");
            saved = new CharacterRecord
            {
                Id = new EntityId(0x1234_5678_9ABC_DEF0), AccountId = account, Name = "Ada",
                SystemAddress = 0x7810135EB5240000, BodyPath = 0x20600000,
                BodyX = 1_234_567.125, BodyY = -5_000_000.5, BodyZ = 3_999_999.0625,
                Yaw = 1.25f, Pitch = -0.5f, Stance = 1, Health = 77, SavedAtUnixMilliseconds = now,
            };
            worker.Save(new[] { saved });
            // A login right after a save sees it: one queue, in order.
            (long again, CharacterRecord? same) = worker.Login("ADA").Result;
            Assert.Equal(account, again, "names are case-insensitive");
            Assert.True(same == saved, "read back after the save");
        }
        using (var worker = new PersistenceWorker(file.Path, () => now))
        {
            (long _, CharacterRecord? restored) = worker.Login("ada").Result;
            Assert.True(restored == saved, "restored exactly after reopening");
            (long other, CharacterRecord? fresh) = worker.Login("Bo").Result;
            Assert.True(fresh == null && other != saved.AccountId, "another name is another account");
        }
        using (GameDatabase db = GameDatabase.Open(file.Path))
        {
            Assert.Equal(GameDatabase.SchemaVersion, db.Version, "schema version");
            Assert.Equal(1, db.CharacterCount, "one character");
        }
    }
}

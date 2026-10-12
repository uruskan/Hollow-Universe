using System;
using System.Collections.Generic;
using System.Globalization;
using Hollow.Simulation.Entities;
using Microsoft.Data.Sqlite;

namespace Hollow.Persistence;

/// <summary>
/// A stored character (GROUND.md §4.7): where it is on its body, in the body frame (metres from the body's centre,
/// turning with it), so it stays at the same place on the planet whatever zone frame the server uses next time.
/// </summary>
public sealed record CharacterRecord
{
    public EntityId Id { get; init; }

    public long AccountId { get; init; }

    public string Name { get; init; } = "";

    /// <summary>The body: system address and body path.</summary>
    public ulong SystemAddress { get; init; }

    public uint BodyPath { get; init; }

    /// <summary>Feet, in the body frame (metres).</summary>
    public double BodyX { get; init; }

    public double BodyY { get; init; }

    public double BodyZ { get; init; }

    /// <summary>View angles in radians (yaw about the zone's up).</summary>
    public float Yaw { get; init; }

    public float Pitch { get; init; }

    public byte Stance { get; init; }

    public byte Health { get; init; }

    public long SavedAtUnixMilliseconds { get; init; }

    /// <summary>Died and not yet respawned (G2).</summary>
    public bool Dead { get; init; }

    /// <summary>The survival state (vitals, inventory, crafting queue, timers) as <see cref="WorldCodec"/> writes it; null before G2.</summary>
    public byte[]? State { get; init; }

    // Records compare by value, the state's bytes included.
    public bool Equals(CharacterRecord? other) =>
        other is not null && Id == other.Id && AccountId == other.AccountId && Name == other.Name && SystemAddress == other.SystemAddress &&
        BodyPath == other.BodyPath && BodyX.Equals(other.BodyX) && BodyY.Equals(other.BodyY) && BodyZ.Equals(other.BodyZ) &&
        Yaw.Equals(other.Yaw) && Pitch.Equals(other.Pitch) && Stance == other.Stance && Health == other.Health &&
        SavedAtUnixMilliseconds == other.SavedAtUnixMilliseconds && Dead == other.Dead &&
        (State == null ? other.State == null : other.State != null && State.AsSpan().SequenceEqual(other.State));

    public override int GetHashCode() => HashCode.Combine(Id, AccountId, Name, SystemAddress, BodyPath, BodyX, BodyY, BodyZ);
}

/// <summary>A stored static thing of a body (G2): a deployable, a dropped item or a corpse, encoded by <see cref="WorldCodec"/>.</summary>
public sealed record ThingRecord
{
    public EntityId Id { get; init; }

    public ulong SystemAddress { get; init; }

    public uint BodyPath { get; init; }

    /// <summary>What it is (<c>StaticKind</c>), and the codec's bytes.</summary>
    public byte Kind { get; init; }

    public byte[] Data { get; init; } = Array.Empty<byte>();
}

/// <summary>A stored change to a natural object (ARCHITECTURE.md §7: only overrides are stored).</summary>
public readonly record struct NaturalRecord(ulong ObjectId, float Health, uint WeakSpot, bool Depleted, long RespawnAtUnixMilliseconds);

/// <summary>Everything stored about one body's zone, as loaded at start.</summary>
public sealed class ZoneRecord
{
    public List<CharacterRecord> Characters { get; } = new();

    public List<ThingRecord> Things { get; } = new();

    public List<NaturalRecord> Naturals { get; } = new();

    /// <summary>The zone's own state (world time) from the last save, or null.</summary>
    public byte[]? Meta { get; set; }
}

/// <summary>A save of a zone in one transaction: what changed and what went.</summary>
public sealed class ZoneSave
{
    public ulong SystemAddress { get; init; }

    public uint BodyPath { get; init; }

    public long SavedAtUnixMilliseconds { get; init; }

    public List<CharacterRecord> Characters { get; } = new();

    public List<ThingRecord> Things { get; } = new();

    public List<EntityId> RemovedThings { get; } = new();

    public List<NaturalRecord> Naturals { get; } = new();

    public List<ulong> RemovedNaturals { get; } = new();

    public byte[]? Meta { get; set; }
}

/// <summary>
/// The ground game's SQLite database (ARCHITECTURE.md §13): accounts, characters, and from G2 the static things,
/// natural object overrides and zone metadata of each body. Opening it brings an older schema up to
/// <see cref="SchemaVersion"/> (kept in <c>PRAGMA user_version</c>) and refuses a newer one. Not thread-safe:
/// <see cref="PersistenceWorker"/> owns it on one thread.
/// </summary>
public sealed class GameDatabase : IDisposable
{
    /// <summary>The schema this build writes. Each step up is a migration in <see cref="Migrations"/>.</summary>
    public const int SchemaVersion = 2;

    private static readonly string[] Migrations =
    {
        // 0 → 1: accounts by name (development login), one character per account (G1).
        """
        CREATE TABLE accounts (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL UNIQUE COLLATE NOCASE,
            created_at INTEGER NOT NULL
        );
        CREATE TABLE characters (
            id INTEGER PRIMARY KEY,
            account_id INTEGER NOT NULL UNIQUE REFERENCES accounts(id),
            name TEXT NOT NULL,
            system_address INTEGER NOT NULL,
            body_path INTEGER NOT NULL,
            body_x REAL NOT NULL,
            body_y REAL NOT NULL,
            body_z REAL NOT NULL,
            yaw REAL NOT NULL,
            pitch REAL NOT NULL,
            stance INTEGER NOT NULL,
            health INTEGER NOT NULL,
            saved_at INTEGER NOT NULL
        );
        """,
        // 1 → 2 (G2): characters' survival state; static things, natural overrides and zone metadata per body.
        """
        ALTER TABLE characters ADD COLUMN dead INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE characters ADD COLUMN state BLOB;
        CREATE INDEX characters_body ON characters(system_address, body_path);
        CREATE TABLE things (
            id INTEGER PRIMARY KEY,
            system_address INTEGER NOT NULL,
            body_path INTEGER NOT NULL,
            kind INTEGER NOT NULL,
            data BLOB NOT NULL,
            saved_at INTEGER NOT NULL
        );
        CREATE INDEX things_body ON things(system_address, body_path);
        CREATE TABLE natural_overrides (
            system_address INTEGER NOT NULL,
            body_path INTEGER NOT NULL,
            object_id INTEGER NOT NULL,
            health REAL NOT NULL,
            weak_spot INTEGER NOT NULL,
            depleted INTEGER NOT NULL,
            respawn_at INTEGER NOT NULL,
            PRIMARY KEY (system_address, body_path, object_id)
        );
        CREATE TABLE zones (
            system_address INTEGER NOT NULL,
            body_path INTEGER NOT NULL,
            saved_at INTEGER NOT NULL,
            data BLOB,
            PRIMARY KEY (system_address, body_path)
        );
        """,
    };

    private const string CharacterColumns =
        "id, account_id, name, system_address, body_path, body_x, body_y, body_z, yaw, pitch, stance, health, saved_at, dead, state";

    private readonly SqliteConnection _connection;

    private GameDatabase(SqliteConnection connection)
    {
        _connection = connection;
    }

    public string Path => _connection.DataSource;

    /// <summary>Opens (creating if needed) the database at <paramref name="path"/> and migrates it.</summary>
    public static GameDatabase Open(string path)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        var database = new GameDatabase(connection);
        try
        {
            database.Execute("PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA foreign_keys = ON;");
            database.Migrate();
        }
        catch
        {
            database.Dispose();
            throw;
        }
        return database;
    }

    public int Version => Convert.ToInt32(Scalar("PRAGMA user_version"), CultureInfo.InvariantCulture);

    private void Migrate()
    {
        int version = Version;
        if (version > SchemaVersion)
        {
            throw new InvalidOperationException($"{Path} has schema {version}; this build knows up to {SchemaVersion}.");
        }
        for (; version < SchemaVersion; version++)
        {
            using SqliteTransaction transaction = _connection.BeginTransaction();
            Execute(Migrations[version], transaction);
            Execute($"PRAGMA user_version = {version + 1}", transaction);
            transaction.Commit();
        }
    }

    /// <summary>The account of a player name (case-insensitive), created on first use.</summary>
    public long GetOrCreateAccount(string name, long nowUnixMilliseconds)
    {
        using SqliteCommand insert = _connection.CreateCommand();
        insert.CommandText = "INSERT INTO accounts (name, created_at) VALUES ($name, $now) ON CONFLICT(name) DO NOTHING";
        insert.Parameters.AddWithValue("$name", name);
        insert.Parameters.AddWithValue("$now", nowUnixMilliseconds);
        insert.ExecuteNonQuery();
        using SqliteCommand select = _connection.CreateCommand();
        select.CommandText = "SELECT id FROM accounts WHERE name = $name";
        select.Parameters.AddWithValue("$name", name);
        return Convert.ToInt64(select.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>The account's character, or null if it has none yet.</summary>
    public CharacterRecord? LoadCharacter(long accountId)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = $"SELECT {CharacterColumns} FROM characters WHERE account_id = $account";
        command.Parameters.AddWithValue("$account", accountId);
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? ReadCharacter(reader) : null;
    }

    /// <summary>Every character on a body (at start, they all come back as sleepers or the dead).</summary>
    public List<CharacterRecord> LoadCharacters(ulong systemAddress, uint bodyPath)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = $"SELECT {CharacterColumns} FROM characters WHERE system_address = $system AND body_path = $body ORDER BY id";
        command.Parameters.AddWithValue("$system", unchecked((long)systemAddress));
        command.Parameters.AddWithValue("$body", (long)bodyPath);
        using SqliteDataReader reader = command.ExecuteReader();
        var result = new List<CharacterRecord>();
        while (reader.Read())
        {
            result.Add(ReadCharacter(reader));
        }
        return result;
    }

    private static CharacterRecord ReadCharacter(SqliteDataReader reader) => new()
    {
        Id = new EntityId(unchecked((ulong)reader.GetInt64(0))),
        AccountId = reader.GetInt64(1),
        Name = reader.GetString(2),
        SystemAddress = unchecked((ulong)reader.GetInt64(3)),
        BodyPath = unchecked((uint)reader.GetInt64(4)),
        BodyX = reader.GetDouble(5),
        BodyY = reader.GetDouble(6),
        BodyZ = reader.GetDouble(7),
        Yaw = (float)reader.GetDouble(8),
        Pitch = (float)reader.GetDouble(9),
        Stance = (byte)reader.GetInt64(10),
        Health = (byte)reader.GetInt64(11),
        SavedAtUnixMilliseconds = reader.GetInt64(12),
        Dead = reader.GetInt64(13) != 0,
        State = reader.IsDBNull(14) ? null : (byte[])reader.GetValue(14),
    };

    /// <summary>Writes characters (insert or replace by ID) in one transaction.</summary>
    public void SaveCharacters(IReadOnlyList<CharacterRecord> records)
    {
        if (records.Count == 0)
        {
            return;
        }
        using SqliteTransaction transaction = _connection.BeginTransaction();
        WriteCharacters(records, transaction);
        transaction.Commit();
    }

    private void WriteCharacters(IReadOnlyList<CharacterRecord> records, SqliteTransaction transaction)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO characters (id, account_id, name, system_address, body_path, body_x, body_y, body_z, yaw, pitch, stance, health, saved_at, dead, state)
            VALUES ($id, $account, $name, $system, $body, $x, $y, $z, $yaw, $pitch, $stance, $health, $saved, $dead, $state)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name, system_address = excluded.system_address, body_path = excluded.body_path,
                body_x = excluded.body_x, body_y = excluded.body_y, body_z = excluded.body_z, yaw = excluded.yaw,
                pitch = excluded.pitch, stance = excluded.stance, health = excluded.health, saved_at = excluded.saved_at,
                dead = excluded.dead, state = excluded.state
            """;
        SqliteParameter id = command.Parameters.Add("$id", SqliteType.Integer);
        SqliteParameter account = command.Parameters.Add("$account", SqliteType.Integer);
        SqliteParameter name = command.Parameters.Add("$name", SqliteType.Text);
        SqliteParameter system = command.Parameters.Add("$system", SqliteType.Integer);
        SqliteParameter body = command.Parameters.Add("$body", SqliteType.Integer);
        SqliteParameter x = command.Parameters.Add("$x", SqliteType.Real);
        SqliteParameter y = command.Parameters.Add("$y", SqliteType.Real);
        SqliteParameter z = command.Parameters.Add("$z", SqliteType.Real);
        SqliteParameter yaw = command.Parameters.Add("$yaw", SqliteType.Real);
        SqliteParameter pitch = command.Parameters.Add("$pitch", SqliteType.Real);
        SqliteParameter stance = command.Parameters.Add("$stance", SqliteType.Integer);
        SqliteParameter health = command.Parameters.Add("$health", SqliteType.Integer);
        SqliteParameter saved = command.Parameters.Add("$saved", SqliteType.Integer);
        SqliteParameter dead = command.Parameters.Add("$dead", SqliteType.Integer);
        SqliteParameter state = command.Parameters.Add("$state", SqliteType.Blob);
        foreach (CharacterRecord r in records)
        {
            id.Value = unchecked((long)r.Id.Value);
            account.Value = r.AccountId;
            name.Value = r.Name;
            system.Value = unchecked((long)r.SystemAddress);
            body.Value = (long)r.BodyPath;
            x.Value = r.BodyX;
            y.Value = r.BodyY;
            z.Value = r.BodyZ;
            yaw.Value = (double)r.Yaw;
            pitch.Value = (double)r.Pitch;
            stance.Value = (long)r.Stance;
            health.Value = (long)r.Health;
            saved.Value = r.SavedAtUnixMilliseconds;
            dead.Value = r.Dead ? 1L : 0L;
            state.Value = (object?)r.State ?? DBNull.Value;
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Everything stored about a body's zone: its characters, things, natural overrides and metadata.</summary>
    public ZoneRecord LoadZone(ulong systemAddress, uint bodyPath)
    {
        var zone = new ZoneRecord();
        zone.Characters.AddRange(LoadCharacters(systemAddress, bodyPath));
        long system = unchecked((long)systemAddress), body = bodyPath;
        using (SqliteCommand command = _connection.CreateCommand())
        {
            command.CommandText = "SELECT id, kind, data FROM things WHERE system_address = $system AND body_path = $body ORDER BY id";
            command.Parameters.AddWithValue("$system", system);
            command.Parameters.AddWithValue("$body", body);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                zone.Things.Add(new ThingRecord
                {
                    Id = new EntityId(unchecked((ulong)reader.GetInt64(0))),
                    SystemAddress = systemAddress,
                    BodyPath = bodyPath,
                    Kind = (byte)reader.GetInt64(1),
                    Data = (byte[])reader.GetValue(2),
                });
            }
        }
        using (SqliteCommand command = _connection.CreateCommand())
        {
            command.CommandText = """
                SELECT object_id, health, weak_spot, depleted, respawn_at FROM natural_overrides
                WHERE system_address = $system AND body_path = $body ORDER BY object_id
                """;
            command.Parameters.AddWithValue("$system", system);
            command.Parameters.AddWithValue("$body", body);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                zone.Naturals.Add(new NaturalRecord(unchecked((ulong)reader.GetInt64(0)), (float)reader.GetDouble(1),
                    (uint)reader.GetInt64(2), reader.GetInt64(3) != 0, reader.GetInt64(4)));
            }
        }
        using (SqliteCommand command = _connection.CreateCommand())
        {
            command.CommandText = "SELECT data FROM zones WHERE system_address = $system AND body_path = $body";
            command.Parameters.AddWithValue("$system", system);
            command.Parameters.AddWithValue("$body", body);
            object? data = command.ExecuteScalar();
            zone.Meta = data is byte[] bytes ? bytes : null;
        }
        return zone;
    }

    /// <summary>Writes a zone's save in one transaction (ARCHITECTURE.md §13: one transaction per zone per flush).</summary>
    public void SaveZone(ZoneSave save)
    {
        using SqliteTransaction transaction = _connection.BeginTransaction();
        WriteCharacters(save.Characters, transaction);
        long system = unchecked((long)save.SystemAddress), body = save.BodyPath;
        if (save.Things.Count > 0)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO things (id, system_address, body_path, kind, data, saved_at) VALUES ($id, $system, $body, $kind, $data, $saved)
                ON CONFLICT(id) DO UPDATE SET system_address = excluded.system_address, body_path = excluded.body_path,
                    kind = excluded.kind, data = excluded.data, saved_at = excluded.saved_at
                """;
            SqliteParameter id = command.Parameters.Add("$id", SqliteType.Integer);
            command.Parameters.AddWithValue("$system", system);
            command.Parameters.AddWithValue("$body", body);
            SqliteParameter kind = command.Parameters.Add("$kind", SqliteType.Integer);
            SqliteParameter data = command.Parameters.Add("$data", SqliteType.Blob);
            command.Parameters.AddWithValue("$saved", save.SavedAtUnixMilliseconds);
            foreach (ThingRecord t in save.Things)
            {
                id.Value = unchecked((long)t.Id.Value);
                kind.Value = (long)t.Kind;
                data.Value = t.Data;
                command.ExecuteNonQuery();
            }
        }
        if (save.RemovedThings.Count > 0)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM things WHERE id = $id";
            SqliteParameter id = command.Parameters.Add("$id", SqliteType.Integer);
            foreach (EntityId removed in save.RemovedThings)
            {
                id.Value = unchecked((long)removed.Value);
                command.ExecuteNonQuery();
            }
        }
        if (save.Naturals.Count > 0)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO natural_overrides (system_address, body_path, object_id, health, weak_spot, depleted, respawn_at)
                VALUES ($system, $body, $id, $health, $spot, $depleted, $respawn)
                ON CONFLICT(system_address, body_path, object_id) DO UPDATE SET health = excluded.health,
                    weak_spot = excluded.weak_spot, depleted = excluded.depleted, respawn_at = excluded.respawn_at
                """;
            command.Parameters.AddWithValue("$system", system);
            command.Parameters.AddWithValue("$body", body);
            SqliteParameter id = command.Parameters.Add("$id", SqliteType.Integer);
            SqliteParameter health = command.Parameters.Add("$health", SqliteType.Real);
            SqliteParameter spot = command.Parameters.Add("$spot", SqliteType.Integer);
            SqliteParameter depleted = command.Parameters.Add("$depleted", SqliteType.Integer);
            SqliteParameter respawn = command.Parameters.Add("$respawn", SqliteType.Integer);
            foreach (NaturalRecord n in save.Naturals)
            {
                id.Value = unchecked((long)n.ObjectId);
                health.Value = (double)n.Health;
                spot.Value = (long)n.WeakSpot;
                depleted.Value = n.Depleted ? 1L : 0L;
                respawn.Value = n.RespawnAtUnixMilliseconds;
                command.ExecuteNonQuery();
            }
        }
        if (save.RemovedNaturals.Count > 0)
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM natural_overrides WHERE system_address = $system AND body_path = $body AND object_id = $id";
            command.Parameters.AddWithValue("$system", system);
            command.Parameters.AddWithValue("$body", body);
            SqliteParameter id = command.Parameters.Add("$id", SqliteType.Integer);
            foreach (ulong removed in save.RemovedNaturals)
            {
                id.Value = unchecked((long)removed);
                command.ExecuteNonQuery();
            }
        }
        using (SqliteCommand command = _connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO zones (system_address, body_path, saved_at, data) VALUES ($system, $body, $saved, $data)
                ON CONFLICT(system_address, body_path) DO UPDATE SET saved_at = excluded.saved_at, data = excluded.data
                """;
            command.Parameters.AddWithValue("$system", system);
            command.Parameters.AddWithValue("$body", body);
            command.Parameters.AddWithValue("$saved", save.SavedAtUnixMilliseconds);
            command.Parameters.AddWithValue("$data", (object?)save.Meta ?? DBNull.Value);
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public int CharacterCount => Convert.ToInt32(Scalar("SELECT COUNT(*) FROM characters"), CultureInfo.InvariantCulture);

    public int ThingCount => Convert.ToInt32(Scalar("SELECT COUNT(*) FROM things"), CultureInfo.InvariantCulture);

    private void Execute(string sql, SqliteTransaction? transaction = null)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private object? Scalar(string sql)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    public void Dispose() => _connection.Dispose();
}

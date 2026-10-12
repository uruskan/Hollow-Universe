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
}

/// <summary>
/// The ground game's SQLite database (ARCHITECTURE.md §13): accounts and characters. Opening it brings an older
/// schema up to <see cref="SchemaVersion"/> (kept in <c>PRAGMA user_version</c>) and refuses a newer one. Not
/// thread-safe: <see cref="PersistenceWorker"/> owns it on one thread.
/// </summary>
public sealed class GameDatabase : IDisposable
{
    /// <summary>The schema this build writes. Each step up is a migration in <see cref="Migrations"/>.</summary>
    public const int SchemaVersion = 1;

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
    };

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
        command.CommandText = """
            SELECT id, name, system_address, body_path, body_x, body_y, body_z, yaw, pitch, stance, health, saved_at
            FROM characters WHERE account_id = $account
            """;
        command.Parameters.AddWithValue("$account", accountId);
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }
        return new CharacterRecord
        {
            Id = new EntityId(unchecked((ulong)reader.GetInt64(0))),
            AccountId = accountId,
            Name = reader.GetString(1),
            SystemAddress = unchecked((ulong)reader.GetInt64(2)),
            BodyPath = unchecked((uint)reader.GetInt64(3)),
            BodyX = reader.GetDouble(4),
            BodyY = reader.GetDouble(5),
            BodyZ = reader.GetDouble(6),
            Yaw = (float)reader.GetDouble(7),
            Pitch = (float)reader.GetDouble(8),
            Stance = (byte)reader.GetInt64(9),
            Health = (byte)reader.GetInt64(10),
            SavedAtUnixMilliseconds = reader.GetInt64(11),
        };
    }

    /// <summary>Writes characters (insert or replace by ID) in one transaction.</summary>
    public void SaveCharacters(IReadOnlyList<CharacterRecord> records)
    {
        if (records.Count == 0)
        {
            return;
        }
        using SqliteTransaction transaction = _connection.BeginTransaction();
        using SqliteCommand command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO characters (id, account_id, name, system_address, body_path, body_x, body_y, body_z, yaw, pitch, stance, health, saved_at)
            VALUES ($id, $account, $name, $system, $body, $x, $y, $z, $yaw, $pitch, $stance, $health, $saved)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name, system_address = excluded.system_address, body_path = excluded.body_path,
                body_x = excluded.body_x, body_y = excluded.body_y, body_z = excluded.body_z, yaw = excluded.yaw,
                pitch = excluded.pitch, stance = excluded.stance, health = excluded.health, saved_at = excluded.saved_at
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
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public int CharacterCount => Convert.ToInt32(Scalar("SELECT COUNT(*) FROM characters"), CultureInfo.InvariantCulture);

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

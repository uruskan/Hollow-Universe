using System;
using System.Globalization;
using System.IO;
using Hollow.Surface;
using Microsoft.Data.Sqlite;

namespace Hollow.Persistence;

/// <summary>A body's playable heightfield, generated solids and population revision; zero is legacy or stand-in.</summary>
public readonly record struct TerrainIdentity(ulong SystemAddress, uint BodyPath, int Revision)
{
    private readonly (ulong System, uint Body, int Revision)? _canonicalBinding;
    public int? CanonicalSchema { get; private init; }
    public string? CanonicalIdentity { get; private init; }
    public string? Identity { get; private init; }
    public bool IsCanonical => CanonicalSchema.HasValue;
    internal const int MaximumCanonicalCharacters = ((PlanetEnvironmentDescriptor.MaximumCanonicalBytes + 2) / 3) * 4;

    private TerrainIdentity(PlanetEnvironmentDescriptor descriptor)
        : this(descriptor.Address.SystemAddress, descriptor.Address.BodyPath, descriptor.TerrainRevision)
    {
        _canonicalBinding = (SystemAddress, BodyPath, Revision);
        CanonicalSchema = descriptor.CanonicalSchema;
        CanonicalIdentity = descriptor.CanonicalIdentity;
        Identity = descriptor.Identity;
        Validate();
    }

    /// <summary>Captures the acquired world's physical identity. Qualification/appearance evidence is deliberately separate.</summary>
    public static TerrainIdentity FromDescriptor(PlanetEnvironmentDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return new TerrainIdentity(descriptor);
    }

    internal void Validate()
    {
        if (Revision < 0) throw new ArgumentOutOfRangeException(nameof(Revision));
        if (!IsCanonical) return; // Explicit revision-only compatibility API for old tools and fixtures.
        if (_canonicalBinding != (SystemAddress, BodyPath, Revision) || CanonicalSchema is not (1 or 2) ||
            string.IsNullOrEmpty(CanonicalIdentity) || CanonicalIdentity.Length > MaximumCanonicalCharacters || Identity?.Length != 64)
            throw new ArgumentException("A canonical terrain identity must preserve its captured descriptor and supported encoding.");
    }
}

/// <summary>A save whose positions and terrain-dependent overrides cannot safely be restored on this terrain.</summary>
public sealed class TerrainMismatchException(string path, TerrainIdentity expected, int? found, string? detail = null)
    : InvalidOperationException($"{System.IO.Path.GetFileName(path)} contains {(found.HasValue ? $"terrain revision {found.Value}" : "unversioned legacy terrain")} " +
        $"for body {expected.SystemAddress:X16}/{expected.BodyPath:X8}; this build requires terrain revision {expected.Revision}. " +
        "Terrain revisions include land shape, solid cliff placement and generated populations, so saved positions and natural-object overrides cannot safely be restored. " +
        (detail == null ? "" : detail + " ") +
        "The save has been preserved. Use its matching build or choose a new save with --db <path>.")
{
    public string SavePath { get; } = System.IO.Path.GetFullPath(path);
    public TerrainIdentity Expected { get; } = expected;
    public int? FoundRevision { get; } = found;
}

/// <summary>Unscoped legacy database access must not bypass a body's canonical stamp.</summary>
public sealed class TerrainIdentityRequiredException(string path)
    : InvalidOperationException($"{System.IO.Path.GetFileName(path)} contains canonical world identities; supply the acquired world's identity before opening it.");

/// <summary>Checks a saved body's terrain before migration or restoration, then stamps newly created worlds.</summary>
internal static class TerrainSave
{
    // Body-owned world state, including opaque zone metadata. Teams and accounts are not terrain state.
    private static readonly string[] SpatialTables =
        { "characters", "things", "building", "ships", "natural_overrides", "prop_overrides", "zones" };

    public static void Preflight(string path, TerrainIdentity? terrain)
    {
        terrain?.Validate();
        if (!File.Exists(path) || new FileInfo(path).Length == 0) return;
        var builder = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA user_version";
            int schema = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (schema > GameDatabase.SchemaVersion) throw new NewerSaveException(path, schema, GameDatabase.SchemaVersion);
        }
        Check(connection, null, path, terrain, allowNewIdentity: true);
    }

    public static void Stamp(SqliteConnection connection, SqliteTransaction transaction, TerrainIdentity terrain)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO terrain_revisions (system_address, body_path, revision) VALUES ($system, $body, $revision) " +
            "ON CONFLICT(system_address, body_path) DO NOTHING";
        Address(command, terrain);
        command.Parameters.AddWithValue("$revision", terrain.Revision);
        command.ExecuteNonQuery();
        if (!terrain.IsCanonical) return;
        command.Parameters.Clear();
        command.CommandText = "INSERT INTO terrain_identities (system_address, body_path, canonical_schema, canonical_identity, identity) " +
            "VALUES ($system, $body, $schema, $canonical, $identity) ON CONFLICT(system_address, body_path) DO NOTHING";
        Address(command, terrain);
        command.Parameters.AddWithValue("$schema", terrain.CanonicalSchema!.Value);
        command.Parameters.AddWithValue("$canonical", terrain.CanonicalIdentity!);
        command.Parameters.AddWithValue("$identity", terrain.Identity!);
        command.ExecuteNonQuery();
    }

    internal static void Check(SqliteConnection connection, SqliteTransaction? transaction, string path,
        TerrainIdentity? requested, bool allowNewIdentity = false)
    {
        requested?.Validate();
        if (requested is not { } terrain)
        {
            if (HasTable(connection, transaction, "terrain_identities"))
            {
                using SqliteCommand any = connection.CreateCommand();
                any.Transaction = transaction;
                any.CommandText = "SELECT 1 FROM terrain_identities LIMIT 1";
                if (any.ExecuteScalar() != null) throw new TerrainIdentityRequiredException(path);
            }
            return;
        }
        if (terrain.IsCanonical && allowNewIdentity)
        {
            using SqliteCommand schema = connection.CreateCommand();
            schema.Transaction = transaction;
            schema.CommandText = "PRAGMA user_version";
            int version = Convert.ToInt32(schema.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (version < 7 && HasSpatialState(connection, transaction, null))
                throw new TerrainMismatchException(path, terrain, null,
                    "This historical schema requires a destructive world migration; canonical admission cannot erase existing world state.");
        }
        int? foundRevision = null;
        if (HasTable(connection, transaction, "terrain_revisions"))
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT revision FROM terrain_revisions WHERE system_address = $system AND body_path = $body";
            Address(command, terrain);
            object? stored = command.ExecuteScalar();
            if (stored != null)
            {
                int revision = Convert.ToInt32(stored, CultureInfo.InvariantCulture);
                if (revision != terrain.Revision) throw new TerrainMismatchException(path, terrain, revision);
                foundRevision = revision;
            }
        }
        if (HasTable(connection, transaction, "terrain_identities"))
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            // Bound strings in SQLite before materializing an untrusted value into managed memory.
            command.CommandText = "SELECT CASE WHEN typeof(canonical_schema) = 'integer' THEN canonical_schema END, " +
                "CASE WHEN typeof(canonical_identity) = 'text' AND length(canonical_identity) BETWEEN 1 AND $max THEN canonical_identity END, " +
                "CASE WHEN typeof(identity) = 'text' AND length(identity) = 64 THEN identity END " +
                "FROM terrain_identities WHERE system_address = $system AND body_path = $body";
            Address(command, terrain);
            command.Parameters.AddWithValue("$max", TerrainIdentity.MaximumCanonicalCharacters);
            using SqliteDataReader reader = command.ExecuteReader();
            if (reader.Read())
            {
                if (!terrain.IsCanonical || foundRevision == null || reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2) ||
                    reader.GetInt64(0) != terrain.CanonicalSchema ||
                    !StringComparer.Ordinal.Equals(reader.GetString(1), terrain.CanonicalIdentity) ||
                    !StringComparer.Ordinal.Equals(reader.GetString(2), terrain.Identity))
                    throw new TerrainMismatchException(path, terrain, foundRevision,
                        "The complete canonical physical-world identity is missing, unsupported, malformed or different; revision-only fallback is forbidden.");
                return;
            }
        }
        if (terrain.IsCanonical)
        {
            if (!allowNewIdentity || HasSpatialState(connection, transaction, terrain))
                throw new TerrainMismatchException(path, terrain, foundRevision,
                    "This body has no canonical identity. Existing spatial state cannot be adopted without trusted historical provenance.");
            return;
        }
        if (foundRevision.HasValue) return;
        // Unversioned saves belong to the inherited terrain. A positive revision must never adopt their positions;
        // revision zero may retain them, then stamp that identity so a later terrain change can reject it explicitly.
        if (terrain.Revision == 0) return;
        if (HasSpatialState(connection, transaction, terrain)) throw new TerrainMismatchException(path, terrain, null);
    }

    private static bool HasSpatialState(SqliteConnection connection, SqliteTransaction? transaction, TerrainIdentity? terrain)
    {
        foreach (string table in SpatialTables)
        {
            if (!HasTable(connection, transaction, table)) continue;
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            // Table names are the fixed private list above, never save data or caller input.
            command.CommandText = terrain.HasValue
                ? $"SELECT 1 FROM {table} WHERE system_address = $system AND body_path = $body LIMIT 1"
                : $"SELECT 1 FROM {table} LIMIT 1";
            if (terrain is { } identity) Address(command, identity);
            if (command.ExecuteScalar() != null) return true;
        }
        return false;
    }

    private static bool HasTable(SqliteConnection connection, SqliteTransaction? transaction, string table)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $table";
        command.Parameters.AddWithValue("$table", table);
        return command.ExecuteScalar() != null;
    }

    private static void Address(SqliteCommand command, TerrainIdentity terrain)
    {
        command.Parameters.AddWithValue("$system", unchecked((long)terrain.SystemAddress));
        command.Parameters.AddWithValue("$body", terrain.BodyPath);
    }
}

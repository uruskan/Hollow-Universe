using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Hollow.Core;
using Hollow.Galaxy.Systems;

namespace Hollow.Surface;

/// <summary>
/// Versioned canonical little-endian scalar encoding. Property names are ordinally sorted and encoded with their
/// value types; added unsupported types fail closed. IEEE finite values preserve bits except equivalent signed zero.
/// This representation is an identity contract, not a source of procedural seeds or a network message format.
/// </summary>
internal static class EnvironmentCanonical
{
    private static readonly Lazy<string> PhysicalFingerprint = new(BuildPhysicalFingerprint);
    private static readonly Lazy<string> GradingFingerprint = new(() => BuildPhysicalFingerprint(includeLegacyCliffs: false));
    private static readonly Lazy<string> PhysicalGradingFingerprint = new(BuildPhysicalGradingFingerprint);
    internal static string RequiredPhysicalFingerprint => PhysicalFingerprint.Value;
    // This identifies the legacy shared-build limitation; it is not a verified concealment or LOD certificate.
    internal static readonly string LegacyQualificationIdentity = Digest(Encoding.UTF8.GetBytes("Hollow.Environment.LegacySharedBuild.v1"));

    internal static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    internal static byte[] Descriptor(PlanetEnvironmentDescriptor descriptor)
    {
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory, Encoding.UTF8, leaveOpen: true);
        writer.Write("Hollow.Environment.Descriptor");
        writer.Write(descriptor.CanonicalSchema);
        writer.Write(descriptor.SeedSchema);
        writer.Write(descriptor.UniverseSeed);
        writer.Write(descriptor.Address.SystemAddress);
        writer.Write(descriptor.Address.BodyPath);
        writer.Write((byte)descriptor.Kind);
        Write(writer, descriptor.Radius);
        Write(writer, descriptor.Metadata);
        Write(writer, descriptor.WalkingParameters);
        writer.Write(descriptor.ProfileId);
        writer.Write(descriptor.SurfaceSeed);
        writer.Write(descriptor.TerrainSeed);
        Write(writer, descriptor.Landing);
        writer.Write(descriptor.TerrainRevision);
        writer.Write(descriptor.PopulationRevision);
        writer.Write(descriptor.IsDiagnostic);
        writer.Write(descriptor.RequiredPhysicalContentFingerprint);
        writer.Write(descriptor.GameplayVisibilitySignature);
        if (descriptor.Formations is { } formations)
        {
            writer.Write("Hollow.Environment.FormationSelection.v1");
            writer.Write(formations.FieldContract == null ? GradingFingerprint.Value : PhysicalGradingFingerprint.Value);
            writer.Write(formations.TerrainContract);
            writer.Write(formations.Identity.GenerationCanonical);
        }
        writer.Flush();
        return memory.ToArray();
    }

    internal static byte[] Value(object value)
    {
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory, Encoding.UTF8, leaveOpen: true);
        Write(writer, value);
        writer.Flush();
        return memory.ToArray();
    }

    internal static string GradedWalkingInputIdentity(PlanetEnvironmentDescriptor descriptor, string terrainProfileKey)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        terrainProfileKey = FormationInput.Key(terrainProfileKey, nameof(terrainProfileKey));
        if (descriptor.Kind != EnvironmentSurfaceKind.Walking || descriptor.WalkingParameters == null)
            throw new FormationValidationException("Finalized walking identity requires walking generator inputs.");
        using var hash = new BoundedIdentityHash(64 * 1024);
        using var writer = new BinaryWriter(hash, Encoding.UTF8, leaveOpen: true);
        writer.Write("Hollow.FinalizedWalkingInputs.v1");
        writer.Write(WalkingWorld.Version); writer.Write(WalkingGradedTerrain.Contract);
        writer.Write(MonumentPlacements.GenerationContract);
        Write(writer, MonumentPlacements.Blend); Write(writer, MonumentPlacements.MinimumSpacing);
        Write(writer, MonumentPlacements.LandingClearance); Write(writer, MonumentPlacements.IndexMargin);
        writer.Write(descriptor.SeedSchema); writer.Write(descriptor.UniverseSeed);
        writer.Write(descriptor.Address.SystemAddress); writer.Write(descriptor.Address.BodyPath);
        writer.Write((byte)descriptor.Kind); Write(writer, descriptor.Radius);
        Write(writer, descriptor.Metadata); Write(writer, descriptor.WalkingParameters);
        writer.Write(descriptor.SurfaceSeed); writer.Write(descriptor.TerrainSeed);
        writer.Write(terrainProfileKey); writer.Write(PhysicalGradingFingerprint.Value);
        writer.Flush();
        return hash.Finish();
    }

    private static string BuildPhysicalGradingFingerprint()
    {
        using var hash = new BoundedIdentityHash(32 * 1024 * 1024);
        using var writer = new BinaryWriter(hash, Encoding.UTF8, leaveOpen: true);
        writer.Write("Hollow.PhysicalMonumentGrading.v1");
        // Ordinals select models; radii drive placement, overlap, grading and index coverage. Physical vertices
        // drive protected bounds/collision. Art names, pad appearance, loot and LOD resources do not grade soil.
        writer.Write(MonumentDefinition.All.Count);
        foreach (MonumentDefinition monument in MonumentDefinition.All)
        {
            writer.Write(monument.Key); Write(writer, monument.Radius);
            writer.Write(monument.Positions.Length);
            foreach (Vector3D point in monument.Positions) Write(writer, point);
            writer.Write(monument.Indices.Length);
            foreach (int index in monument.Indices) writer.Write(index);
        }
        writer.Flush();
        return hash.Finish();
    }

    /// <summary>New grading identities hash bounded documents without retaining their canonical byte buffers.</summary>
    private sealed class BoundedIdentityHash(int maximumBytes) : Stream
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private int _written;
        internal string Finish() => Convert.ToHexString(_hash.GetHashAndReset());
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length > maximumBytes - _written)
                throw new FormationCapacityException("Finalized grading identity exceeds its canonical byte ceiling.");
            _written += buffer.Length;
            _hash.AppendData(buffer);
        }
        public override void WriteByte(byte value)
        {
            Span<byte> one = stackalloc byte[1]; one[0] = value; Write(one);
        }
        protected override void Dispose(bool disposing) { if (disposing) _hash.Dispose(); base.Dispose(disposing); }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _written;
        public override long Position { get => _written; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private static void Write(BinaryWriter writer, object? value)
    {
        if (value == null) { writer.Write((byte)0); return; }
        // These are encoding types, not behavior dispatch over game objects.
        switch (value)
        {
            case bool boolean: writer.Write((byte)1); writer.Write(boolean); return;
            case int integer: writer.Write((byte)2); writer.Write(integer); return;
            case uint integer: writer.Write((byte)3); writer.Write(integer); return;
            case ulong integer: writer.Write((byte)4); writer.Write(integer); return;
            case double number:
                if (!double.IsFinite(number)) throw new ArgumentException("Environment inputs must be finite.");
                writer.Write((byte)5); writer.Write(number == 0 ? 0.0 : number); return;
            case float number:
                if (!float.IsFinite(number)) throw new ArgumentException("Environment inputs must be finite.");
                writer.Write((byte)6); writer.Write(number == 0 ? 0.0f : number); return;
            case string text: writer.Write((byte)7); writer.Write(text); return;
            case Enum enumeration:
                Type enumType = enumeration.GetType();
                ulong numeric = Convert.ToUInt64(enumeration);
                if (enumType.IsDefined(typeof(FlagsAttribute), inherit: false))
                {
                    ulong allowed = 0;
                    foreach (object member in Enum.GetValues(enumType)) allowed |= Convert.ToUInt64(member);
                    if ((numeric & ~allowed) != 0) throw new ArgumentException("Unknown environment flag value.");
                }
                else if (!Enum.IsDefined(enumType, enumeration)) throw new ArgumentException("Unknown environment enum value.");
                writer.Write((byte)8); writer.Write(enumType.FullName!); writer.Write(numeric); return;
            case Vector3D vector:
                writer.Write((byte)9); Write(writer, vector.X); Write(writer, vector.Y); Write(writer, vector.Z); return;
        }
        Type type = value.GetType();
        if (type != typeof(PlanetEnvironmentMetadata) && type != typeof(PlanetState) &&
            type != typeof(Composition) && type != typeof(Gases) && type != typeof(WalkingParameters))
            throw new ArgumentException("Unsupported environment identity value type: " + type.FullName);
        writer.Write((byte)10);
        writer.Write(type.FullName!);
        PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
        writer.Write(properties.Length);
        foreach (PropertyInfo property in properties)
        {
            if (property.GetIndexParameters().Length != 0 || property.GetMethod == null)
                throw new ArgumentException("Environment identity records must contain readable scalar properties.");
            writer.Write(property.Name);
            Write(writer, property.GetValue(value));
        }
    }

    private static string BuildPhysicalFingerprint() => BuildPhysicalFingerprint(includeLegacyCliffs: true);

    private static string BuildPhysicalFingerprint(bool includeLegacyCliffs)
    {
        // Hash the required loaded rules/geometry, not file bytes, paths, titles, images or timestamps. These are
        // embedded catalogs. Their current order is consumed as model indices, so it is part of legacy identity.
        // New catalog loaders must remain immutable and extend this encoder when adding consumed physical fields.
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory, Encoding.UTF8, leaveOpen: true);
        writer.Write(includeLegacyCliffs ? "Hollow.RequiredEnvironmentContent.v1" : "Hollow.GradedEnvironmentContent.v1");
        writer.Write(WalkingWorld.Version);
        if (includeLegacyCliffs)
        {
            writer.Write(CliffPlacements.GenerationRevision);
            Write(writer, CliffPlacements.CellMetres);
            Write(writer, CliffPlacements.MaximumExtent);
            Write(writer, CliffPlacements.MaximumOffset);
            writer.Write(CliffPlacements.MaximumTiers);
            writer.Write(CliffPlacements.MaximumColumns);
            writer.Write(CliffGeometry.All.Count);
            foreach (CliffGeometry cliff in CliffGeometry.All)
            {
                writer.Write(cliff.Key);
                Write(writer, cliff.Height);
                Write(writer, cliff.Facing);
                Write(writer, cliff.MinimumGradient);
                Write(writer, cliff.MaximumGradient);
                Write(writer, cliff.SelectionWeight);
                writer.Write(cliff.MaterialFamily);
                writer.Write(cliff.Positions.Length);
                foreach (Vector3D point in cliff.Positions) Write(writer, point);
                writer.Write(cliff.Indices.Length);
                foreach (int index in cliff.Indices) writer.Write(index);
            }
        }
        writer.Write(MonumentDefinition.All.Count);
        foreach (MonumentDefinition monument in MonumentDefinition.All)
        {
            writer.Write(monument.Key);
            Write(writer, monument.Radius);
            writer.Write(monument.Ground);
            writer.Write(monument.Pieces.Count);
            foreach (MonumentPiece piece in monument.Pieces)
            {
                writer.Write(piece.Key); Write(writer, piece.At);
                Write(writer, piece.X); Write(writer, piece.Y); Write(writer, piece.Z); Write(writer, piece.W);
            }
            writer.Write(monument.Loot.Count);
            foreach (MonumentLoot loot in monument.Loot)
            {
                Write(writer, loot.At); Write(writer, loot.Yaw); writer.Write(loot.Table);
            }
            writer.Write(monument.Positions.Length);
            foreach (Vector3D point in monument.Positions) Write(writer, point);
            writer.Write(monument.Indices.Length);
            foreach (int index in monument.Indices) writer.Write(index);
        }
        writer.Flush();
        return Digest(memory.ToArray());
    }
}

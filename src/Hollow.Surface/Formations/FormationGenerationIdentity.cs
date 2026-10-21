using System;
using System.IO;
using System.Text;
using System.Threading;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>
/// Explicit implementation contracts, never procedural seeds. The integration owner must advance the affected
/// revision when successful physical output or support semantics change. These are proposals, not a pilot profile.
/// </summary>
public sealed record FormationAlgorithmRevisions
{
    public int Planner { get; init; } = FormationPlanner.GenerationRevision;
    public int Topology { get; init; } = 1;
    public int RegionalContainment { get; init; } = 2;
    public int ConflictAdmission { get; init; } = 1;
    public int Transitions { get; init; } = 1;
    public int Support { get; init; } = 1;
    public int SurfaceChart { get; init; } = 1;
    public int Substrate { get; init; } = 1;
    public int Qualification { get; init; } = 1;

    internal void Validate()
    {
        if (Planner < 1 || Topology < 1 || RegionalContainment < 1 || ConflictAdmission < 1 || Transitions < 1 ||
            Support < 1 || SurfaceChart < 1 || Substrate < 1 || Qualification < 1)
            throw new FormationValidationException("Formation algorithm revisions must be positive explicit versions.");
    }
}

/// <summary>
/// Immutable identities of explicit decoded formation inputs, without loading, generation, seeds or a global cache.
/// Physical content excludes LOD art and byte receipts. Generation includes physical content plus successful-output
/// policy. Qualification is independent admission evidence; it is not a complete gameplay-visibility signature.
/// This value is not a complete world identity: the owning descriptor must also bind terrain, radius and sites.
/// </summary>
public sealed class FormationGenerationIdentity
{
    public const int EncodingSchema = 1;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    private FormationGenerationIdentity(byte[] physical, byte[] generation, byte[] qualification,
        long estimatedRetainedBytes, long estimatedPeakBytes)
    {
        PhysicalContentByteLength = physical.Length;
        GenerationByteLength = generation.Length;
        QualificationByteLength = qualification.Length;
        EstimatedRetainedBytes = estimatedRetainedBytes;
        EstimatedPeakBytes = estimatedPeakBytes;
        PhysicalContentCanonical = Convert.ToBase64String(physical);
        PhysicalContentDigest = EnvironmentCanonical.Digest(physical);
        GenerationCanonical = Convert.ToBase64String(generation);
        GenerationDigest = EnvironmentCanonical.Digest(generation);
        QualificationCanonical = Convert.ToBase64String(qualification);
        QualificationDigest = EnvironmentCanonical.Digest(qualification);
    }

    public string PhysicalContentCanonical { get; }
    public string PhysicalContentDigest { get; }
    public string GenerationCanonical { get; }
    public string GenerationDigest { get; }
    public string QualificationCanonical { get; }
    public string QualificationDigest { get; }
    public int PhysicalContentByteLength { get; }
    public int GenerationByteLength { get; }
    public int QualificationByteLength { get; }
    /// <summary>Conservative admission estimate for this object and its six retained strings; excludes caller-owned inputs.</summary>
    public long EstimatedRetainedBytes { get; }
    /// <summary>Retention plus all three canonical buffers and encoding workspace; not cumulative allocations or process memory.</summary>
    public long EstimatedPeakBytes { get; }

    /// <summary>
    /// Keyed collections have already been ordinally sorted and validated by their immutable constructors.
    /// Vertex, triangle and patch ordinals, contact chain direction and winding remain meaningful and are preserved.
    /// Operational count/byte/cancellation limits are excluded: they refuse the whole build, never truncate success.
    /// All three document lengths and aggregate estimated peak bytes are admitted before allocating canonical buffers.
    /// The estimate includes UTF-16 Base64 and digest strings, all three buffers, and conservative fixed overhead.
    /// It excludes caller-owned inputs and cumulative allocations pending collection; no measured memory bound is claimed.
    /// </summary>
    public static FormationGenerationIdentity Create(FormationCatalog catalog, FormationPlannerSettings settings,
        FormationAlgorithmRevisions revisions, int maximumCanonicalBytes = 16 * 1024 * 1024,
        CancellationToken cancellation = default, long maximumEstimatedBytes = 256L * 1024 * 1024,
        FormationFieldContract? fieldContract = null)
    {
        cancellation.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(catalog); ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(revisions); revisions.Validate();
        if (maximumCanonicalBytes < 1 || maximumCanonicalBytes > 64 * 1024 * 1024)
            throw new FormationValidationException("Canonical document limit must be between one byte and 64 MiB.");
        if (maximumEstimatedBytes < 1 || maximumEstimatedBytes > 1024L * 1024 * 1024)
            throw new FormationValidationException("Canonical allocation estimate limit must be between one byte and 1 GiB.");
        // Resolve selected required content before creating an identity that could authorize a world.
        _ = catalog.RequireRecipe(settings.RecipeKey, settings.RecipeRevision);
        int physicalLength = Count(w => Physical(w, catalog), maximumCanonicalBytes, cancellation);
        byte[]? physical = null;
        void Generation(BinaryWriter w)
        {
            Header(w, "Hollow.Formation.Generation");
            // Include the complete canonical physical input, not just a collision-prone comparison shortcut.
            PhysicalBlob(w, physicalLength, physical);
            Scalars(w, "SimulationAlgorithms", ("ConflictAdmission", revisions.ConflictAdmission),
                ("Planner", revisions.Planner), ("RegionalContainment", revisions.RegionalContainment),
                ("Substrate", revisions.Substrate), ("Support", revisions.Support), ("SurfaceChart", revisions.SurfaceChart),
                ("Topology", revisions.Topology), ("Transitions", revisions.Transitions));
            // These labels describe the supported implementation, not caller-selectable behavior or seeds.
            // Source admission runs in whole-planet and regional builds; do not hide it in the region branch.
            Scalars(w, "ImplementedPlannerPolicies", ("ConflictPolicy", FormationPlanner.ConflictPolicy),
                ("ExtentPolicy", "outward-coordinate-spans-plus-diagonal-v1"),
                ("GenerationRevision", FormationPlanner.GenerationRevision),
                ("NeighborEvidencePolicy", "outward-physical-aabb-plus-extent-two-edge-ring-shadow-v1"),
                ("OwnershipPolicy", "canonical-contour-core-publication-only-v1"),
                ("RegionalEvidencePolicy", "all-survey-components-and-fits-complete-or-refuse-v1"),
                ("SourceAdmissionPolicy", "paired-last-crest-source-segment-shadow-v1"),
                ("SourceAdmissionScope", "whole-and-regional-v1"), ("SourceShadowEdgeRings", 2));
            Settings(w, settings);
            // Omit the extension entirely for legacy selections: their canonical bytes remain unchanged.
            if (fieldContract != null)
                Scalars(w, "SealedField", ("AdmissionPolicy", (int)fieldContract.AdmissionPolicy),
                    ("CandidatePolicy", FormationFieldContract.CandidatePolicy), ("Contract", FormationFieldContract.Contract),
                    ("FinestTileLevel", fieldContract.FinestTileLevel), ("GuardPolicy", FormationFieldContract.GuardPolicy),
                    ("GuardRings", FormationFieldContract.GuardRings), ("LatticeContract", TerrainLattice.Contract),
                    ("MinimumPlaneBurialMetres", FormationFieldContract.MinimumPlaneBurialMetres),
                    ("Radius", fieldContract.Radius), ("SealContract", FormationTerrainSeal.Contract),
                    ("PinContract", TerrainPinSet.Contract), ("CollisionContract", SurfaceGrid.CanonicalCollisionContract),
                    ("CompositeCollisionContract", SelectedGroundCollision.Contract),
                    ("GroundCollisionLayout", GroundCollisionLayout.Contract),
                    ("PhysicalTraceContract", SelectedGroundTrace.Contract), ("TracePinDomain", SelectedGroundTrace.RadialPinContract),
                    ("TerrainIdentity", fieldContract.TerrainIdentity));
        }
        void Qualification(BinaryWriter w)
        {
            Header(w, "Hollow.Formation.Qualification");
            PhysicalBlob(w, physicalLength, physical);
            Scalars(w, "QualificationAlgorithms", ("Qualification", revisions.Qualification));
            ArrayHeader(w, "QualifiedLods", catalog.Qualified.Length);
            foreach (CliffSupportDefinition definition in catalog.Qualified)
            {
                Scalars(w, "QualifiedOwner", ("Key", definition.Key), ("Revision", definition.Revision));
                ArrayHeader(w, "Lods", definition.Lods.Length);
                foreach (CliffLodBinding lod in definition.Lods)
                {
                    Scalars(w, "Lod", ("Key", lod.Key), ("MaximumPositionErrorMetres", lod.MaximumPositionErrorMetres),
                        ("MinimumNormalDot", lod.MinimumNormalDot));
                    Geometry(w, lod.Mesh);
                    ArrayHeader(w, "TriangleMaps", lod.TriangleMaps.Length);
                    foreach (CliffLodTriangleMap map in lod.TriangleMaps)
                    { w.Write(map.PhysicalTriangle); w.Write(map.VisualTriangle); }
                    if (lod.ContactLoops.Length != 0)
                    {
                        ArrayHeader(w, "ContactLoopBindings", lod.ContactLoops.Length);
                        foreach (FormationContactLoopBinding loop in lod.ContactLoops)
                        {
                            Scalars(w, "ContactLoopBinding", ("LoopKey", loop.LoopKey));
                            Indices(w, "Vertices", loop.VertexIndices);
                            if (!loop.EdgeVertexIndices.IsEmpty) Indices(w, "SeamEdgeVertices", loop.EdgeVertexIndices);
                        }
                    }
                }
            }
        }
        int generationLength = Count(Generation, maximumCanonicalBytes, cancellation);
        int qualificationLength = Count(Qualification, maximumCanonicalBytes, cancellation);
        long canonicalBytes = (long)physicalLength + generationLength + qualificationLength;
        long base64Characters = Base64Characters(physicalLength) + Base64Characters(generationLength) + Base64Characters(qualificationLength);
        // Fixed retention allowance covers the object, six string headers/alignment and three 64-character digests.
        long retained = checked(4096 + 2 * base64Characters);
        long peak = checked(retained + canonicalBytes + 64 * 1024);
        if (peak > maximumEstimatedBytes)
            throw new FormationCapacityException("Canonical identity exceeds its aggregate allocation estimate ceiling.");
        physical = Encode(w => Physical(w, catalog), physicalLength, cancellation);
        byte[] generation = Encode(Generation, generationLength, cancellation);
        byte[] qualification = Encode(Qualification, qualificationLength, cancellation);
        cancellation.ThrowIfCancellationRequested();
        var result = new FormationGenerationIdentity(physical, generation, qualification, retained, peak);
        cancellation.ThrowIfCancellationRequested();
        return result;
    }

    private static void Physical(BinaryWriter w, FormationCatalog catalog)
    {
        Header(w, "Hollow.Formation.PhysicalContent");
        ArrayHeader(w, "Qualified", catalog.Qualified.Length);
        foreach (CliffSupportDefinition definition in catalog.Qualified)
        {
            Geometry(w, definition.Geometry);
            Scalars(w, "QualifiedScales", ("MaximumScale", definition.MaximumScale), ("MinimumScale", definition.MinimumScale));
            ArrayHeader(w, "PatchDomains", definition.Patches.Length);
            foreach (CliffPatchDomain domain in definition.Patches)
            {
                Scalars(w, "PatchDomain", ("PatchKey", domain.PatchKey));
                ArrayHeader(w, "Boundaries", domain.Boundaries.Length);
                foreach (CliffPatchBoundary boundary in domain.Boundaries)
                    Indices(w, "Vertices", boundary.VertexIndices);
            }
            ArrayHeader(w, "Contacts", definition.Contacts.Length);
            foreach (CliffContactDomain contact in definition.Contacts)
            {
                Scalars(w, "Contact", ("Key", contact.Key), ("MaximumBurialMetres", contact.MaximumBurialMetres),
                    ("MaximumGapMetres", contact.MaximumGapMetres), ("MinimumBurialMetres", contact.MinimumBurialMetres),
                    ("PatchKey", contact.PatchKey), ("Role", contact.Role));
                Indices(w, "Vertices", contact.VertexIndices);
            }
            if (definition.ContactLoops.Length != 0)
            {
                ArrayHeader(w, "ClosedContactLoops", definition.ContactLoops.Length);
                foreach (FormationContactLoop loop in definition.ContactLoops)
                {
                    Scalars(w, "ClosedContactLoop", ("Key", loop.Key), ("Closure", "implicit-last-to-first"));
                    Indices(w, "Vertices", loop.VertexIndices);
                }
            }
        }
        ArrayHeader(w, "Profiles", catalog.Profiles.Length);
        foreach (SubstrateProfile p in catalog.Profiles)
            Scalars(w, "SubstrateProfile", ("Bedrock", p.Bedrock), ("GrassCoverage", p.GrassCoverage), ("Key", p.Key),
                ("Litter", p.Litter), ("LooseStone", p.LooseStone), ("MaximumTemperatureC", p.MaximumTemperatureC),
                ("MinimumSoilUpDot", p.MinimumSoilUpDot), ("MinimumTemperatureC", p.MinimumTemperatureC),
                ("PlantSuitability", p.PlantSuitability), ("ShrubsPerSquareMetre", p.ShrubsPerSquareMetre),
                ("Soil", p.Soil), ("SoilDepthMetres", p.SoilDepthMetres), ("TreesPerSquareMetre", p.TreesPerSquareMetre));
        ArrayHeader(w, "Recipes", catalog.Recipes.Length);
        foreach (FormationRecipe recipe in catalog.Recipes)
        {
            Scalars(w, "Recipe", ("FamilyKey", recipe.FamilyKey), ("IdentityDomain", (uint)recipe.IdentityDomain),
                ("Key", recipe.Key), ("MaximumExtentMetres", recipe.MaximumExtentMetres),
                ("MaximumFitProbes", recipe.MaximumFitProbes), ("MaximumGuideStations", recipe.MaximumGuideStations),
                ("MaximumSectionLengthMetres", recipe.MaximumSectionLengthMetres), ("MinimumCoverage", recipe.MinimumCoverage),
                ("Revision", recipe.Revision));
            ArrayHeader(w, "Slots", recipe.Slots.Length);
            foreach (FormationRecipeSlot slot in recipe.Slots)
            {
                Scalars(w, "Slot", ("Required", slot.Required), ("Role", slot.Role), ("Slot", slot.Slot));
                ArrayHeader(w, "Alternatives", slot.Alternatives.Length);
                foreach (FormationGeometryChoice choice in slot.Alternatives)
                {
                    Scalars(w, "Choice", ("Key", choice.Key), ("MaximumScale", choice.MaximumScale),
                        ("MinimumScale", choice.MinimumScale), ("Revision", choice.Revision));
                    ArrayHeader(w, "ContactKeys", choice.ContactKeys.Length);
                    foreach (string key in choice.ContactKeys) Scalar(w, key);
                }
            }
        }
    }

    private static void Geometry(BinaryWriter w, FormationGeometry geometry)
    {
        Scalars(w, "Geometry", ("FamilyKey", geometry.FamilyKey), ("Key", geometry.Key), ("Revision", geometry.Revision));
        ArrayHeader(w, "Vertices", geometry.Vertices.Length);
        foreach (Vector3D point in geometry.Vertices) Scalar(w, point);
        ArrayHeader(w, "Triangles", geometry.Triangles.Length);
        foreach (GeometryTriangle triangle in geometry.Triangles)
        { w.Write(triangle.A); w.Write(triangle.B); w.Write(triangle.C); w.Write(triangle.Patch); }
        ArrayHeader(w, "Patches", geometry.Patches.Length);
        foreach (SupportPatch patch in geometry.Patches)
            Scalars(w, "Patch", ("Capabilities", (uint)patch.Capabilities), ("Key", patch.Key), ("SubstrateProfileKey", patch.SubstrateProfileKey));
    }

    private static void Settings(BinaryWriter w, FormationPlannerSettings s)
    {
        Scalars(w, "PlannerSettings", ("AnchorContactRole", s.AnchorContactRole), ("CoverageAlong", s.CoverageAlong),
            ("CoverageDown", s.CoverageDown), ("CoverageReachMetres", s.CoverageReachMetres),
            ("CrossSectionStepMetres", s.CrossSectionStepMetres), ("CrossSectionSteps", s.CrossSectionSteps),
            ("GeometryUp", s.GeometryUp), ("GradientProbeMetres", s.GradientProbeMetres), ("MinimumRiseRun", s.MinimumRiseRun),
            ("RecipeKey", s.RecipeKey), ("RecipeRevision", s.RecipeRevision), ("SurveyLevel", s.SurveyLevel),
            ("TerrainProfileKey", s.TerrainProfileKey));
        // Null/full-planet and an explicit full-region remain different declarations even if a fixture produces
        // the same output. Region ownership and future bounds must never be inferred from current success.
        w.Write(s.Region != null);
        if (s.Region == null) return;
        FormationSurveyRegion region = s.Region;
        Scalars(w, "SurveyRegion", ("GridLevel", region.GridLevel), ("HaloPolicy", "cube-edge-neighbor-rings-v1"),
            ("HaloRings", region.HaloRings));
        ArrayHeader(w, "CoreCells", region.CoreCells.Length);
        foreach (TileKey cell in region.CoreCells)
        { w.Write((int)cell.Face); w.Write((int)cell.Level); w.Write(cell.X); w.Write(cell.Y); }
    }

    private static void Header(BinaryWriter w, string domain) { w.Write(domain); w.Write(EncodingSchema); }
    private static void ArrayHeader(BinaryWriter w, string name, int length) { w.Write((byte)11); w.Write(name); w.Write(length); }
    private static void Indices(BinaryWriter w, string name, ReadOnlySpan<int> values)
    { ArrayHeader(w, name, values.Length); foreach (int value in values) w.Write(value); }
    private static void Scalars(BinaryWriter w, string type, params (string Name, object Value)[] fields)
    {
        // Match EnvironmentCanonical: explicit record type, ordinal property names, typed finite scalar values.
        Array.Sort(fields, static (a, b) => StringComparer.Ordinal.Compare(a.Name, b.Name));
        w.Write((byte)10); w.Write(type); w.Write(fields.Length);
        foreach ((string name, object value) in fields) { w.Write(name); Scalar(w, value); }
    }
    private static void Scalar(BinaryWriter w, object value)
    {
        switch (value)
        {
            case bool b: w.Write((byte)1); w.Write(b); return;
            case int n: w.Write((byte)2); w.Write(n); return;
            case uint n: w.Write((byte)3); w.Write(n); return;
            case double n:
                if (!double.IsFinite(n)) throw new FormationValidationException("Identity numbers must be finite.");
                w.Write((byte)5); w.Write(n == 0 ? 0.0 : n); return;
            case string s: w.Write((byte)7); w.Write(s); return;
            case Vector3D p:
                w.Write((byte)9); Scalar(w, p.X); Scalar(w, p.Y); Scalar(w, p.Z); return;
            default: throw new FormationValidationException("Unsupported canonical scalar type.");
        }
    }

    private static long Base64Characters(int bytes) => 4 * ((bytes + 2L) / 3);

    private static void PhysicalBlob(BinaryWriter writer, int length, byte[]? physical)
    {
        writer.Write(length);
        if (physical != null) writer.Write(physical);
        else ((CountingStream)writer.BaseStream).Reserve(length);
    }

    private static int Count(Action<BinaryWriter> encode, int maximum, CancellationToken cancellation)
    {
        using var counter = new CountingStream(maximum, cancellation);
        using (var writer = new BinaryWriter(counter, Utf8, leaveOpen: true)) { encode(writer); writer.Flush(); }
        cancellation.ThrowIfCancellationRequested();
        return checked((int)counter.Length);
    }

    private static byte[] Encode(Action<BinaryWriter> encode, int length, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var bytes = new byte[length];
        using var memory = new MemoryStream(bytes, writable: true);
        using var output = new CountingStream(bytes.Length, cancellation, memory);
        // A fixed buffer cannot grow if the immutable-input contract is ever violated between the two passes.
        using (var writer = new BinaryWriter(output, Utf8, leaveOpen: true)) { encode(writer); writer.Flush(); }
        if (memory.Position != bytes.Length) throw new FormationValidationException("Canonical inputs changed during encoding.");
        cancellation.ThrowIfCancellationRequested();
        return bytes;
    }

    private sealed class CountingStream(int maximum, CancellationToken cancellation, Stream? target = null) : Stream
    {
        private long _length;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _length;
        public override long Position { get => _length; set => throw new NotSupportedException(); }
        public override void Flush() { cancellation.ThrowIfCancellationRequested(); target?.Flush(); }
        public override void Write(byte[] buffer, int offset, int count) { Charge(count); target?.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Charge(buffer.Length); target?.Write(buffer); }
        public override void WriteByte(byte value) { Charge(1); target?.WriteByte(value); }
        public void Reserve(int count)
        {
            if (target != null) throw new InvalidOperationException("Only the counting pass can reserve unwritten bytes.");
            Charge(count);
        }
        private void Charge(int count)
        {
            cancellation.ThrowIfCancellationRequested();
            if (count < 0 || count > maximum - _length) throw new FormationCapacityException("Canonical identity exceeds its byte ceiling.");
            _length += count;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}

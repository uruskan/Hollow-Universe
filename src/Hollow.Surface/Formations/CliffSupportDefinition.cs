using System;
using System.Collections.Generic;
using Hollow.Core;

namespace Hollow.Surface;

/// <summary>Implicitly closed physical vertex loop, oriented as the patch's triangle boundary. Holes use that same rule.</summary>
public sealed class CliffPatchBoundary
{
    private readonly int[] _vertices;
    public CliffPatchBoundary(IEnumerable<int> vertices)
    {
        _vertices = FormationInput.Copy(vertices, 4096, nameof(vertices));
        if (_vertices.Length < 3) throw new FormationValidationException("A boundary needs at least three distinct vertices.");
        var seen = new HashSet<int>();
        foreach (int vertex in _vertices)
            if (vertex < 0 || !seen.Add(vertex)) throw new FormationValidationException("Boundary vertices must be nonnegative and unique; omit the repeated closing vertex.");
        int first = 0;
        for (int i = 1; i < _vertices.Length; i++) if (_vertices[i] < _vertices[first]) first = i;
        if (first != 0)
        {
            int[] ordered = new int[_vertices.Length];
            for (int i = 0; i < ordered.Length; i++) ordered[i] = _vertices[(first + i) % _vertices.Length];
            _vertices = ordered;
        }
    }
    public ReadOnlySpan<int> VertexIndices => _vertices;
}

public sealed class CliffPatchDomain
{
    private readonly CliffPatchBoundary[] _boundaries;
    public CliffPatchDomain(string patchKey, IEnumerable<CliffPatchBoundary> boundaries)
    {
        PatchKey = FormationInput.Key(patchKey, nameof(patchKey));
        _boundaries = FormationInput.Copy(boundaries, 64, nameof(boundaries));
        foreach (CliffPatchBoundary boundary in _boundaries)
            if (boundary == null) throw new FormationValidationException("Patch boundary cannot be null.");
        Array.Sort(_boundaries, static (a, b) =>
        {
            int n = Math.Min(a.VertexIndices.Length, b.VertexIndices.Length);
            for (int i = 0; i < n; i++)
            { int order = a.VertexIndices[i].CompareTo(b.VertexIndices[i]); if (order != 0) return order; }
            return a.VertexIndices.Length.CompareTo(b.VertexIndices.Length);
        });
    }
    public string PatchKey { get; }
    public ReadOnlySpan<CliffPatchBoundary> Boundaries => _boundaries;
}

/// <summary>An ordered open contact chain on a named patch boundary; all distances are unscaled local metres.</summary>
public sealed class CliffContactDomain
{
    private readonly int[] _vertices;
    public CliffContactDomain(string key, string role, string patchKey, IEnumerable<int> vertices,
        double minimumBurialMetres, double maximumBurialMetres, double maximumGapMetres)
    {
        Key = FormationInput.Key(key, nameof(key)); Role = FormationInput.Key(role, nameof(role));
        PatchKey = FormationInput.Key(patchKey, nameof(patchKey));
        if (!double.IsFinite(minimumBurialMetres) || !double.IsFinite(maximumBurialMetres) || !double.IsFinite(maximumGapMetres) ||
            minimumBurialMetres < 0 || maximumBurialMetres < minimumBurialMetres || maximumGapMetres < 0)
            throw new FormationValidationException("Contact burial/gap limits must be finite nonnegative intervals.");
        MinimumBurialMetres = minimumBurialMetres; MaximumBurialMetres = maximumBurialMetres; MaximumGapMetres = maximumGapMetres;
        _vertices = FormationInput.Copy(vertices, 64, nameof(vertices));
        if (_vertices.Length < 2) throw new FormationValidationException("A contact chain needs at least two vertices.");
        var seen = new HashSet<int>();
        foreach (int vertex in _vertices)
            if (vertex < 0 || !seen.Add(vertex)) throw new FormationValidationException("Contact chain vertices must be nonnegative and unique.");
    }
    public string Key { get; }
    public string Role { get; }
    public string PatchKey { get; }
    public ReadOnlySpan<int> VertexIndices => _vertices;
    public double MinimumBurialMetres { get; }
    public double MaximumBurialMetres { get; }
    public double MaximumGapMetres { get; }
}

/// <summary>One-to-one physical/visual triangle correspondence; A/B/C map in order, preserving winding.</summary>
public readonly record struct CliffLodTriangleMap(int PhysicalTriangle, int VisualTriangle);

/// <summary>Explicit decoded LOD geometry. The loader verifies file digests before supplying these immutable inputs.</summary>
public sealed class CliffLodBinding
{
    private readonly CliffLodTriangleMap[] _maps;
    private readonly FormationContactLoopBinding[] _contactLoops;
    public CliffLodBinding(string key, string contentSha256, FormationGeometry mesh, IEnumerable<CliffLodTriangleMap> triangleMaps,
        double maximumPositionErrorMetres, double minimumNormalDot, IEnumerable<FormationContactLoopBinding>? contactLoops = null)
    {
        Key = FormationInput.Key(key, nameof(key)); ContentSha256 = CliffSupportValidation.Digest(contentSha256);
        ArgumentNullException.ThrowIfNull(mesh);
        if (!double.IsFinite(maximumPositionErrorMetres) || maximumPositionErrorMetres < 0 ||
            !double.IsFinite(minimumNormalDot) || minimumNormalDot < 0 || minimumNormalDot > 1)
            throw new FormationValidationException("Invalid LOD correspondence tolerances.");
        Mesh = mesh; MaximumPositionErrorMetres = maximumPositionErrorMetres; MinimumNormalDot = minimumNormalDot;
        _maps = FormationInput.Copy(triangleMaps, 131072, nameof(triangleMaps));
        Array.Sort(_maps, static (a, b) => a.PhysicalTriangle.CompareTo(b.PhysicalTriangle));
        var visual = new HashSet<int>();
        for (int i = 0; i < _maps.Length; i++)
            if (_maps[i].PhysicalTriangle < 0 || (uint)_maps[i].VisualTriangle >= (uint)mesh.TriangleCount ||
                (i > 0 && _maps[i - 1].PhysicalTriangle == _maps[i].PhysicalTriangle) || !visual.Add(_maps[i].VisualTriangle))
                throw new FormationValidationException("LOD triangle mappings must be in range and one-to-one.");
        _contactLoops = FormationInput.Copy(contactLoops ?? Array.Empty<FormationContactLoopBinding>(), FormationContactLoop.MaximumLoops, nameof(contactLoops));
        foreach (FormationContactLoopBinding loop in _contactLoops)
            if (loop == null) throw new FormationValidationException("Null visual contact loop binding.");
        Array.Sort(_contactLoops, static (a, b) => StringComparer.Ordinal.Compare(a.LoopKey, b.LoopKey));
        for (int i = 1; i < _contactLoops.Length; i++)
            if (_contactLoops[i - 1].LoopKey == _contactLoops[i].LoopKey)
                throw new FormationValidationException("Duplicate visual contact loop binding.");
    }
    public string Key { get; }
    public string ContentSha256 { get; }
    public FormationGeometry Mesh { get; }
    public ReadOnlySpan<CliffLodTriangleMap> TriangleMaps => _maps;
    public ReadOnlySpan<FormationContactLoopBinding> ContactLoops => _contactLoops;
    public FormationContactLoopBinding RequireContactLoop(string key)
    {
        foreach (FormationContactLoopBinding loop in _contactLoops) if (loop.LoopKey == key) return loop;
        throw new FormationValidationException("Unknown visual contact loop binding: " + key);
    }
    /// <summary>Maximum world-space corner deviation at the definition's maximum scale, not an unscaled centroid statistic.</summary>
    public double MaximumPositionErrorMetres { get; }
    public double MinimumNormalDot { get; }
}

public sealed record CliffSupportLimits
{
    public int MaxDomains { get; init; } = 256;
    public int MaxBoundaryVertices { get; init; } = 65536;
    public int MaxContacts { get; init; } = 128;
    public int MaxLods { get; init; } = 4;
    public int MaxMappedTriangles { get; init; } = 131072;
    public int MaxContactLoops { get; init; } = FormationContactLoop.MaximumLoops;
    /// <summary>Aggregate physical and visual closed-loop vertex references across the definition.</summary>
    public int MaxLoopVertices { get; init; } = 327680;
    public long MaxEstimatedResidentBytes { get; init; } = 128L * 1024 * 1024;
    public long MaxEstimatedScratchBytes { get; init; } = 32L * 1024 * 1024;
}

/// <summary>
/// Required qualified inputs, independent of files, engine, and placement. Exact boundary membership and supplied
/// LOD corner correspondence are verified here. SHA-256 strings bind loader receipts; this class cannot authenticate
/// files it was not given. A loader must hash original bytes and pass the decoded meshes from those same bytes.
/// No normal/height predicate grants physical support permission. Every geometry patch has an explicit domain.
/// </summary>
public sealed class CliffSupportDefinition
{
    private readonly CliffPatchDomain[] _patches;
    private readonly CliffContactDomain[] _contacts;
    private readonly CliffLodBinding[] _lods;
    private readonly FormationContactLoop[] _contactLoops;
    public CliffSupportDefinition(FormationGeometry geometry, string sourceSha256, string physicalSha256,
        double minimumScale, double maximumScale, IEnumerable<CliffPatchDomain> patches,
        IEnumerable<CliffContactDomain> contacts, IEnumerable<CliffLodBinding> lods, CliffSupportLimits? limits = null,
        IEnumerable<FormationContactLoop>? contactLoops = null)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        Geometry = geometry; SourceSha256 = CliffSupportValidation.Digest(sourceSha256); PhysicalSha256 = CliffSupportValidation.Digest(physicalSha256);
        if (!double.IsFinite(minimumScale) || !double.IsFinite(maximumScale) || minimumScale <= 0 || maximumScale < minimumScale)
            throw new FormationValidationException("Invalid qualified scale range.");
        MinimumScale = minimumScale; MaximumScale = maximumScale;
        limits ??= new CliffSupportLimits();
        if (limits.MaxDomains < 1 || limits.MaxBoundaryVertices < 0 || limits.MaxContacts < 0 || limits.MaxLods < 2 ||
            limits.MaxMappedTriangles < 0 || limits.MaxContactLoops < 0 || limits.MaxContactLoops > FormationContactLoop.MaximumLoops ||
            limits.MaxLoopVertices < 0 || limits.MaxEstimatedResidentBytes < 0 || limits.MaxEstimatedScratchBytes < 0)
            throw new FormationValidationException("Invalid cliff qualification limits.");
        long scratch = 8192L + 256L * geometry.TriangleCount + 96L * limits.MaxBoundaryVertices +
            256L * limits.MaxDomains + 1024L * limits.MaxContacts + 32L * limits.MaxMappedTriangles + 1024L * limits.MaxLods;
        if (scratch > limits.MaxEstimatedScratchBytes) throw new FormationCapacityException("Cliff qualification exceeds scratch reservation.");
        _patches = FormationInput.Copy(patches, limits.MaxDomains, nameof(patches));
        _contacts = FormationInput.Copy(contacts, limits.MaxContacts, nameof(contacts));
        _lods = FormationInput.Copy(lods, limits.MaxLods, nameof(lods));
        _contactLoops = FormationInput.Copy(contactLoops ?? Array.Empty<FormationContactLoop>(), limits.MaxContactLoops, nameof(contactLoops));
        if (_patches.Length != geometry.PatchCount || _lods.Length < 2)
            throw new FormationValidationException("Every physical patch and at least two explicit visual LODs are required.");
        foreach (CliffPatchDomain domain in _patches) if (domain == null) throw new FormationValidationException("Null patch domain.");
        foreach (CliffContactDomain contact in _contacts) if (contact == null) throw new FormationValidationException("Null contact domain.");
        foreach (CliffLodBinding lod in _lods) if (lod == null) throw new FormationValidationException("Null LOD binding.");
        foreach (FormationContactLoop loop in _contactLoops) if (loop == null) throw new FormationValidationException("Null physical contact loop.");
        Array.Sort(_patches, static (a, b) => StringComparer.Ordinal.Compare(a.PatchKey, b.PatchKey));
        Array.Sort(_contacts, static (a, b) => StringComparer.Ordinal.Compare(a.Key, b.Key));
        Array.Sort(_lods, static (a, b) => StringComparer.Ordinal.Compare(a.Key, b.Key));
        Array.Sort(_contactLoops, static (a, b) => StringComparer.Ordinal.Compare(a.Key, b.Key));
        int boundaryVertices = 0, mappings = 0;
        long resident = 8192L + geometry.EstimatedResidentBytes + 1024L * (_patches.Length + _contacts.Length + _lods.Length);
        for (int i = 0; i < _patches.Length; i++)
        {
            if (i > 0 && _patches[i - 1].PatchKey == _patches[i].PatchKey) throw new FormationValidationException("Duplicate patch domain.");
            foreach (CliffPatchBoundary boundary in _patches[i].Boundaries)
            {
                if (boundary.VertexIndices.Length > limits.MaxBoundaryVertices - boundaryVertices) throw new FormationCapacityException("Boundary vertex budget exceeded.");
                boundaryVertices += boundary.VertexIndices.Length; resident += 256L + 8L * boundary.VertexIndices.Length;
            }
        }
        for (int i = 0; i < _contacts.Length; i++)
        {
            if (i > 0 && _contacts[i - 1].Key == _contacts[i].Key) throw new FormationValidationException("Duplicate contact domain.");
            if (!double.IsFinite(_contacts[i].MaximumBurialMetres * maximumScale) || !double.IsFinite(_contacts[i].MaximumGapMetres * maximumScale))
                throw new FormationValidationException("Contact tolerances must remain representable at maximum scale.");
            resident += 8L * _contacts[i].VertexIndices.Length;
        }
        for (int i = 0; i < _lods.Length; i++)
        {
            if (i > 0 && _lods[i - 1].Key == _lods[i].Key) throw new FormationValidationException("Duplicate visual LOD key.");
            if (_lods[i].TriangleMaps.Length > limits.MaxMappedTriangles - mappings) throw new FormationCapacityException("LOD mapping budget exceeded.");
            mappings += _lods[i].TriangleMaps.Length;
            resident += _lods[i].Mesh.EstimatedResidentBytes + 16L * _lods[i].TriangleMaps.Length;
        }
        int loopVertices = 0;
        for (int i = 0; i < _contactLoops.Length; i++)
        {
            if (i > 0 && _contactLoops[i - 1].Key == _contactLoops[i].Key) throw new FormationValidationException("Duplicate physical contact loop.");
            AccountLoop(_contactLoops[i].VertexIndices.Length);
        }
        foreach (CliffLodBinding lod in _lods)
        {
            if (lod.ContactLoops.Length > limits.MaxContactLoops) throw new FormationCapacityException("Visual contact loop budget exceeded.");
            foreach (FormationContactLoopBinding loop in lod.ContactLoops)
                AccountLoop(loop.VertexIndices.Length + loop.EdgeVertexIndices.Length);
        }
        // Reserve one mesh's required-edge set and a loop's coordinate set before allocating either.
        // Empty legacy definitions retain their prior accounting exactly.
        if (loopVertices != 0) scratch += 32768L + 64L * loopVertices;
        if (scratch > limits.MaxEstimatedScratchBytes) throw new FormationCapacityException("Closed contact validation exceeds scratch reservation.");
        if (resident > limits.MaxEstimatedResidentBytes) throw new FormationCapacityException("Qualified definition exceeds retained-content budget.");
        EstimatedResidentBytes = resident; ReservedConstructionScratchBytes = scratch;
        // The advertised scale interval must be physically representable, not merely two finite scalar values.
        CheckScale(minimumScale); CheckScale(maximumScale);
        ValidateDomains();
        foreach (CliffLodBinding lod in _lods) ValidateLod(lod);
        ValidateContactLoops();
        void CheckScale(double scale) => _ = new FormationPiece(default, "qualification", geometry,
            new FormationTransform(Vector3D.Zero, Vector3D.UnitX, Vector3D.UnitY, Vector3D.UnitZ, scale));
        void AccountLoop(int count)
        {
            if (count > limits.MaxLoopVertices - loopVertices) throw new FormationCapacityException("Closed contact vertex budget exceeded.");
            loopVertices += count; resident += 256L + 8L * count;
        }
    }
    public FormationGeometry Geometry { get; }
    public string Key => Geometry.Key;
    public int Revision => Geometry.Revision;
    public string FamilyKey => Geometry.FamilyKey;
    public string SourceSha256 { get; }
    public string PhysicalSha256 { get; }
    public double MinimumScale { get; }
    public double MaximumScale { get; }
    public ReadOnlySpan<CliffPatchDomain> Patches => _patches;
    public ReadOnlySpan<CliffContactDomain> Contacts => _contacts;
    public ReadOnlySpan<CliffLodBinding> Lods => _lods;
    public ReadOnlySpan<FormationContactLoop> ContactLoops => _contactLoops;
    public long EstimatedResidentBytes { get; }
    public long ReservedConstructionScratchBytes { get; }
    public CliffPatchDomain RequirePatch(string key)
    {
        foreach (CliffPatchDomain domain in _patches) if (domain.PatchKey == key) return domain;
        throw new FormationValidationException("Unknown qualified patch: " + key);
    }
    public CliffContactDomain RequireContact(string key)
    {
        foreach (CliffContactDomain domain in _contacts) if (domain.Key == key) return domain;
        throw new FormationValidationException("Unknown qualified contact: " + key);
    }

    public FormationContactLoop RequireContactLoop(string key)
    {
        foreach (FormationContactLoop loop in _contactLoops) if (loop.Key == key) return loop;
        throw new FormationValidationException("Unknown physical contact loop: " + key);
    }

    private void ValidateContactLoops()
    {
        foreach (CliffLodBinding lod in _lods)
            if (lod.ContactLoops.Length != _contactLoops.Length)
                throw new FormationValidationException("Every visual LOD must bind every closed physical contact loop exactly once.");
        if (_contactLoops.Length == 0) return;
        var required = new HashSet<(int Low, int High)>();
        foreach (FormationContactLoop loop in _contactLoops) AddEdges(Geometry, loop.VertexIndices, required, ReadOnlySpan<int>.Empty);
        RequireEdges(Geometry, required);
        foreach (CliffLodBinding lod in _lods)
        {
            for (int n = 0; n < _contactLoops.Length; n++)
            {
                FormationContactLoop physical = _contactLoops[n];
                FormationContactLoopBinding visual = lod.ContactLoops[n];
                if (physical.Key != visual.LoopKey || physical.VertexIndices.Length != visual.VertexIndices.Length)
                    throw new FormationValidationException("Visual contact loop keys and vertex counts must match the physical loops.");
                AddEdges(lod.Mesh, visual.VertexIndices, required, visual.EdgeVertexIndices);
                for (int i = 0; i < physical.VertexIndices.Length; i++)
                    if (Geometry.Vertices[physical.VertexIndices[i]] != lod.Mesh.Vertices[visual.VertexIndices[i]])
                        throw new FormationValidationException("Visual contact loop coordinates must equal physical coordinates exactly and in order.");
            }
            RequireEdges(lod.Mesh, required);
        }

        static void AddEdges(FormationGeometry mesh, ReadOnlySpan<int> loop, HashSet<(int Low, int High)> required,
            ReadOnlySpan<int> edgeVertices)
        {
            var points = new HashSet<Vector3D>();
            foreach (int vertex in loop)
                if ((uint)vertex >= (uint)mesh.VertexCount || !points.Add(mesh.Vertices[vertex]))
                    throw new FormationValidationException("Closed contact loop vertices must be in range and have distinct local coordinates.");
            for (int i = 0; i < loop.Length; i++)
            {
                int a = loop[i], b = loop[(i + 1) % loop.Length];
                if (!edgeVertices.IsEmpty)
                {
                    int seamA = edgeVertices[2 * i], seamB = edgeVertices[2 * i + 1];
                    if ((uint)seamA >= (uint)mesh.VertexCount || (uint)seamB >= (uint)mesh.VertexCount ||
                        mesh.Vertices[seamA] != mesh.Vertices[a] || mesh.Vertices[seamB] != mesh.Vertices[b])
                        throw new FormationValidationException("Visual seam endpoints must equal their ordered closed-loop coordinates exactly.");
                    a = seamA; b = seamB;
                }
                required.Add((Math.Min(a, b), Math.Max(a, b)));
            }
        }
        static void RequireEdges(FormationGeometry mesh, HashSet<(int Low, int High)> required)
        {
            if (required.Count == 0) return;
            foreach (GeometryTriangle triangle in mesh.Triangles)
            {
                Remove(triangle.A, triangle.B); Remove(triangle.B, triangle.C); Remove(triangle.C, triangle.A);
            }
            if (required.Count != 0) throw new FormationValidationException("Every closed contact segment, including the closing segment, must be an actual mesh edge.");
            void Remove(int a, int b) => required.Remove((Math.Min(a, b), Math.Max(a, b)));
        }
    }

    private void ValidateDomains()
    {
        var edges = new Dictionary<(int Patch, int Low, int High), (int From, int To, int Count)>();
        var patchIds = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < Geometry.PatchCount; i++) patchIds.Add(Geometry.Patches[i].Key, i);
        foreach (GeometryTriangle triangle in Geometry.Triangles)
        { Add(triangle.Patch, triangle.A, triangle.B); Add(triangle.Patch, triangle.B, triangle.C); Add(triangle.Patch, triangle.C, triangle.A); }
        void Add(int patch, int from, int to)
        {
            var key = (patch, Math.Min(from, to), Math.Max(from, to));
            if (!edges.TryGetValue(key, out var prior)) edges.Add(key, (from, to, 1));
            else if (prior.Count != 1 || prior.From != to || prior.To != from)
                throw new FormationValidationException("Patch triangle edges must be manifold and consistently wound.");
            else edges[key] = (prior.From, prior.To, 2);
        }
        var boundaryEdges = new HashSet<(int Patch, int From, int To)>();
        foreach (CliffPatchDomain domain in _patches)
        {
            if (!patchIds.TryGetValue(domain.PatchKey, out int patch)) throw new FormationValidationException("Domain names an unknown physical patch.");
            var vertices = new HashSet<int>();
            foreach (CliffPatchBoundary loop in domain.Boundaries)
            {
                ReadOnlySpan<int> chain = loop.VertexIndices;
                for (int i = 0; i < chain.Length; i++)
                {
                    int a = chain[i], b = chain[(i + 1) % chain.Length];
                    if ((uint)a >= (uint)Geometry.VertexCount || (uint)b >= (uint)Geometry.VertexCount || !vertices.Add(a) ||
                        !edges.TryGetValue((patch, Math.Min(a, b), Math.Max(a, b)), out var edge) || edge.Count != 1 || edge.From != a || edge.To != b ||
                        !boundaryEdges.Add((patch, a, b))) throw new FormationValidationException("Boundary loops must cover actual open patch edges once, with their winding.");
                }
            }
        }
        foreach (var edge in edges)
            if (edge.Value.Count == 1 && !boundaryEdges.Contains((edge.Key.Patch, edge.Value.From, edge.Value.To)))
                throw new FormationValidationException("A patch boundary edge is omitted.");
        foreach (CliffContactDomain contact in _contacts)
        {
            if (!patchIds.TryGetValue(contact.PatchKey, out int patch)) throw new FormationValidationException("Contact names an unknown patch.");
            for (int i = 1; i < contact.VertexIndices.Length; i++)
            {
                int a = contact.VertexIndices[i - 1], b = contact.VertexIndices[i];
                if (!boundaryEdges.Contains((patch, a, b)) && !boundaryEdges.Contains((patch, b, a)))
                    throw new FormationValidationException("Contact chain must follow its patch's boundary edges.");
            }
        }
    }

    private void ValidateLod(CliffLodBinding lod)
    {
        var mapped = new bool[Geometry.TriangleCount];
        foreach (CliffLodTriangleMap map in lod.TriangleMaps)
        {
            if ((uint)map.PhysicalTriangle >= (uint)Geometry.TriangleCount) throw new FormationValidationException("LOD map references an unknown physical triangle.");
            GeometryTriangle physical = Geometry.Triangles[map.PhysicalTriangle], visual = lod.Mesh.Triangles[map.VisualTriangle];
            Vector3D a = Geometry.Vertices[physical.A], b = Geometry.Vertices[physical.B], c = Geometry.Vertices[physical.C];
            Vector3D va = lod.Mesh.Vertices[visual.A], vb = lod.Mesh.Vertices[visual.B], vc = lod.Mesh.Vertices[visual.C];
            CheckCorner(a, va); CheckCorner(b, vb); CheckCorner(c, vc);
            if (Vector3D.Dot(CliffSupportValidation.Normal(a, b, c), CliffSupportValidation.Normal(va, vb, vc)) < lod.MinimumNormalDot)
                throw new FormationValidationException("Visual support triangle violates its normal correspondence bound.");
            mapped[map.PhysicalTriangle] = true;
        }
        for (int i = 0; i < mapped.Length; i++)
            if (Geometry.Patches[Geometry.Triangles[i].Patch].Capabilities != SupportCapabilities.None && !mapped[i])
                throw new FormationValidationException("Every support-enabled physical triangle needs a mapping in every declared LOD.");
        void CheckCorner(Vector3D physical, Vector3D visual)
        {
            double error = (physical - visual).Length * MaximumScale;
            if (!double.IsFinite(error) || error > lod.MaximumPositionErrorMetres)
                throw new FormationValidationException("Visual support triangle violates its maximum-scale position bound.");
        }
    }
}

internal static class CliffSupportValidation
{
    internal static string Digest(string value)
    {
        if (value == null || value.Length != 64) throw new FormationValidationException("A SHA-256 digest needs exactly 64 hexadecimal characters.");
        foreach (char c in value)
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                throw new FormationValidationException("Invalid SHA-256 digest.");
        return value.ToUpperInvariant();
    }
    internal static Vector3D Normal(Vector3D a, Vector3D b, Vector3D c)
    {
        Vector3D cross = Vector3D.Cross(b - a, c - a);
        double scale = Math.Max(Math.Abs(cross.X), Math.Max(Math.Abs(cross.Y), Math.Abs(cross.Z)));
        Vector3D normal = (cross / scale).Normalized;
        FormationInput.Unit(normal, nameof(normal)); return normal;
    }
}

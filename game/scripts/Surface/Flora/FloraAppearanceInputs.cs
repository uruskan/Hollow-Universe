using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Godot;
using Hollow.Surface;
using GFile = Godot.FileAccess;

namespace HollowUniverse.Surface.Flora;

/// <summary>Per-admission CPU budgets; these are not estimates of native/driver allocations.</summary>
public sealed class FloraAppearanceInputLimits
{
    public FloraAppearanceInputLimits(int maxMetadataBytes, long maxFileBytes, long maxTotalFileBytes,
        long maxGeometryBytes, int maxVerticesPerSurface, int maxIndicesPerSurface)
    {
        if (maxMetadataBytes is < 1 or > 1048576 || maxFileBytes is < 1 or > 268435456 ||
            maxTotalFileBytes < maxFileBytes || maxTotalFileBytes > 1073741824 ||
            maxGeometryBytes is < 1 or > 134217728 || maxVerticesPerSurface is < 3 or > 1048576 ||
            maxIndicesPerSurface is < 3 or > 6291456)
            throw new FloraAppearanceCapacityException("Invalid flora input capture limits.");
        MaxMetadataBytes = maxMetadataBytes; MaxFileBytes = maxFileBytes; MaxTotalFileBytes = maxTotalFileBytes;
        MaxGeometryBytes = maxGeometryBytes; MaxVerticesPerSurface = maxVerticesPerSurface;
        MaxIndicesPerSurface = maxIndicesPerSurface;
    }
    public int MaxMetadataBytes { get; }
    public long MaxFileBytes { get; }
    public long MaxTotalFileBytes { get; }
    public long MaxGeometryBytes { get; }
    public int MaxVerticesPerSurface { get; }
    public int MaxIndicesPerSurface { get; }
}

/// <summary>
/// Main-thread, per-library capture of actual upload/import inputs. This class is deliberately opt-in; it does not
/// activate a package or change a world. Integrate at the existing creation boundary: UploadImage replaces the five
/// ImageTexture uploads; CreateProceduralMesh/AddProceduralSurface replace GeometryBuilder's mesh upload; an
/// ImportedTreeScene supplies its private Root and TakeMesh replaces ArrayMesh.Duplicate in LoadAuthoredTrees.
/// CaptureExport returns a full build record; ValidateRuntime compares only runtime compatibility to an external pin.
///
/// There is no texture/mesh GPU readback here. Imported meshes are identified by the exact imported resource closure
/// and node path, procedural meshes by the arrays passed to the upload. Source files and the export GPU are provenance
/// only. Files must stay immutable during admission; resources must remain exclusively owned and unmodified after
/// capture. Godot exposes GPU-only mutation APIs that emit no Resource.Changed signal: these receipts cannot detect
/// arbitrary subsequent buffer writes. They are creation/lifetime contracts, not a GPU integrity monitor.
/// Dispose clears receipts only; the caller owns returned meshes/textures and must retire them on failed admission.
/// </summary>
public sealed class FloraAppearanceInputs : IDisposable
{
    public const string AdapterRevision = "flora-upload-import-v1";
    public const string FinishRevision = "legacy-full-atlas-mips-v1";
    public const string ProjectionRevision = "ortho-eight-by-eleven-v1";
    private const string RuntimePrefix = "runtime/", ProvenancePrefix = "provenance/";
    private const string CatalogPath = "res://assets/surface/trees/catalog.json";
    private const int MaxMeshes = 128, MaxSurfaces = 16, MaxUniforms = 64, MaxDepth = 12;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private static readonly Regex Includes = new(@"^[\t ]*#[\t ]*include[\t ]+""([^""]+)""[\t ]*\r?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private readonly FloraAppearanceInputLimits _limits;
    private readonly CancellationToken _cancellation;
    private readonly int _thread = System.Environment.CurrentManagedThreadId;
    private readonly Dictionary<ulong, MeshReceipt> _meshes = new();
    private readonly Dictionary<ulong, ImageReceipt> _images = new();
    private readonly Dictionary<ulong, ImportedTextureReceipt> _importedTextures = new();
    private readonly Dictionary<string, FloraAppearanceDependency> _files = new(StringComparer.Ordinal);
    private readonly HashSet<string> _sceneSources = new(StringComparer.Ordinal);
    private readonly HashSet<string> _textureSources = new(StringComparer.Ordinal);
    private long _fileBytes, _geometryBytes;
    private bool _disposed, _sealed;

    private sealed class MeshReceipt
    {
        public required ArrayMesh Mesh;
        public string ImportedIdentity = "";
        public string ImportedMetadata = "";
        public ArrayMesh? ImportedShadow;
        public readonly List<string> Surfaces = new();
        public readonly List<string> SurfaceMetadata = new();
    }
    private sealed class ImageReceipt
    {
        public required ImageTexture Texture;
        public required FloraAppearanceImageLayout Layout;
        public required string Hash;
        public bool Invalid;
        public void Changed() => Invalid = true;
    }
    private sealed class ImportedTextureReceipt
    {
        public required CompressedTexture2D Texture;
        public required string Path;
        public required FloraAppearanceDependency File;
        public int Width, Height;
        public bool Invalid;
        public void Changed() => Invalid = true;
    }

    public FloraAppearanceInputs(FloraAppearanceInputLimits limits, CancellationToken cancellation = default)
    {
        _limits = limits ?? throw new ArgumentNullException(nameof(limits)); _cancellation = cancellation;
        if (OS.GetThreadCallerId() != OS.GetMainThreadId()) Fail("Flora creation/capture requires Godot's main thread.");
        CheckEngine();
    }

    /// <summary>Uploads the exact existing CPU mip chain, without repainting, regenerating mips, or reading a texture.</summary>
    public ImageTexture UploadImage(string role, Image image)
    {
        Creating();
        FloraAppearanceImageLayout layout = NearLayout(role);
        if (_images.Values.Any(i => i.Layout.Role == role)) Fail("Duplicate near-image upload: " + role);
        if (image.GetFormat() != Image.Format.Rgba8 || image.GetWidth() != layout.Width ||
            image.GetHeight() != layout.Height || image.GetMipmapCount() + 1 != layout.MipLevels)
            Fail("Near-image layout differs: " + role);
        byte[] bytes = image.GetData(); // CPU Image, never Texture2D.GetImage.
        if (bytes.Length != layout.ByteLength) Fail("Near-image byte length differs: " + role);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        // Recreate from the hashed bytes so a caller cannot mutate the source Image between hashing and upload.
        using Image owned = Image.CreateFromData(layout.Width, layout.Height, true, Image.Format.Rgba8, bytes);
        ImageTexture texture = ImageTexture.CreateFromImage(owned);
        RecordImage(texture, layout, hash);
        return texture;
    }

    /// <summary>Uploads already admitted package bytes; useful for runtime construction without painting near textures.</summary>
    public ImageTexture UploadImage(FloraAppearanceImage image)
    {
        Creating();
        FloraAppearanceImageLayout layout = NearLayout(image.Layout.Role);
        if (_images.Values.Any(i => i.Layout.Role == layout.Role)) Fail("Duplicate near-image upload: " + layout.Role);
        byte[] bytes = new byte[layout.ByteLength]; image.CopyTo(bytes);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (hash != image.Sha256) Fail("Admitted CPU image no longer matches its digest.");
        using Image owned = Image.CreateFromData(layout.Width, layout.Height, true, Image.Format.Rgba8, bytes);
        ImageTexture texture = ImageTexture.CreateFromImage(owned);
        RecordImage(texture, layout, hash);
        return texture;
    }

    public ArrayMesh CreateProceduralMesh()
    {
        Creating(); Capacity(_meshes.Count < MaxMeshes, "Mesh receipt limit exceeded.");
        var mesh = new ArrayMesh(); _meshes.Add(mesh.GetInstanceId(), new MeshReceipt { Mesh = mesh }); return mesh;
    }

    /// <summary>
    /// Exact GeometryBuilder v1 layout: positions, normals, UV, RGBA vertex colour, RG float wind, triangle indices.
    /// Indices must already have GeometryBuilder's clockwise winding correction. Uploads a private snapshot with the
    /// ordinary flags. Materials are assigned by the existing caller afterwards.
    /// A new attribute/layout must extend this version explicitly; no attributes are silently omitted from the digest.
    /// </summary>
    public void AddProceduralSurface(ArrayMesh mesh, Vector3[] positions, Vector3[] normals,
        Vector2[] uv, Color[] colours, float[] wind, int[] indices)
    {
        Creating();
        if (!_meshes.TryGetValue(mesh.GetInstanceId(), out MeshReceipt? receipt) || receipt.ImportedIdentity.Length != 0)
            throw new FloraAppearanceValidationException("Mesh was not created by this upload recorder.");
        Capacity(receipt.Surfaces.Count < MaxSurfaces, "Surface receipt limit exceeded.");
        if (mesh.GetSurfaceCount() != receipt.Surfaces.Count)
            Fail("Unsupported or externally changed procedural mesh layout.");
        Mesh.ArrayFormat flags = (Mesh.ArrayFormat)((int)Mesh.ArrayCustomFormat.RgFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift);
        int[] slots = { (int)Mesh.ArrayType.Vertex, (int)Mesh.ArrayType.Normal, (int)Mesh.ArrayType.TexUV,
            (int)Mesh.ArrayType.Color, (int)Mesh.ArrayType.Custom0, (int)Mesh.ArrayType.Index };
        ArgumentNullException.ThrowIfNull(positions); ArgumentNullException.ThrowIfNull(normals);
        ArgumentNullException.ThrowIfNull(uv); ArgumentNullException.ThrowIfNull(colours);
        ArgumentNullException.ThrowIfNull(wind); ArgumentNullException.ThrowIfNull(indices);
        int vertices = positions.Length;
        Capacity(vertices >= 3 && vertices <= _limits.MaxVerticesPerSurface && indices.Length >= 3 &&
            indices.Length <= _limits.MaxIndicesPerSurface, "Procedural surface count limit exceeded.");
        if (normals.Length != vertices || uv.Length != vertices || colours.Length != vertices ||
            wind.Length != vertices * 2 || indices.Length % 3 != 0) Fail("Incomplete procedural vertex attributes.");
        _geometryBytes = checked(_geometryBytes + (long)vertices * 56 + (long)indices.Length * 4);
        Capacity(_geometryBytes <= _limits.MaxGeometryBytes, "Procedural geometry byte budget exceeded.");
        // Admit counts before copying, and hash/upload the same private CPU snapshot.
        positions = (Vector3[])positions.Clone(); normals = (Vector3[])normals.Clone(); uv = (Vector2[])uv.Clone();
        colours = (Color[])colours.Clone(); wind = (float[])wind.Clone(); indices = (int[])indices.Clone();
        string digest = Digest(w =>
        {
            w.Write(AdapterRevision); w.Write("procedural-triangles"); w.Write((ulong)flags); w.Write(vertices);
            foreach (Vector3 p in positions) Vector(w, p);
            foreach (Vector3 n in normals) Vector(w, n);
            foreach (Vector2 t in uv) { Float(w, t.X); Float(w, t.Y); }
            foreach (Color c in colours) Colour(w, c);
            foreach (float f in wind) Float(w, f);
            w.Write(indices.Length);
            foreach (int index in indices) { if (index < 0 || index >= vertices) Fail("Procedural index outside vertex array."); w.Write(index); }
        });
        using var snapshot = new Godot.Collections.Array(); snapshot.Resize((int)Mesh.ArrayType.Max);
        snapshot[slots[0]] = positions; snapshot[slots[1]] = normals; snapshot[slots[2]] = uv;
        snapshot[slots[3]] = colours; snapshot[slots[4]] = wind; snapshot[slots[5]] = indices;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, snapshot, null, null, flags);
        if (mesh.GetSurfaceCount() != receipt.Surfaces.Count + 1) Fail("Procedural surface upload failed.");
        receipt.Surfaces.Add(digest); receipt.SurfaceMetadata.Add(SurfaceMetadata(mesh, mesh.GetSurfaceCount() - 1));
    }

    /// <summary>
    /// Loads the exact hashed imported scene with IgnoreDeep, bypassing cached external resources. Do not duplicate
    /// its meshes: ArrayMesh.Duplicate itself causes GPU readback. Use TakeMesh, then apply the existing material adapter.
    /// Import metadata may omit editor-only params/deps in a shipping PCK; only remap information is required.
    /// </summary>
    public ImportedTreeScene LoadImportedTreeScene(string logicalPath, Action<Resource>? own = null)
    {
        Creating(); logicalPath = ResourcePath(logicalPath);
        Capacity(_sceneSources.Count < 8 || _sceneSources.Contains(logicalPath), "Imported scene count exceeded.");
        string path = ResolveImported(logicalPath);
        if (!path.EndsWith(".scn", StringComparison.Ordinal)) Fail("Expected an imported binary tree scene.");
        string identity = CaptureResourceClosure(path, new Dictionary<string, string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal), 0);
        using PackedScene scene = ResourceLoader.Load<PackedScene>(path, "PackedScene", ResourceLoader.CacheMode.IgnoreDeep)
            ?? throw new FloraAppearanceValidationException("Imported tree scene could not be loaded.");
        PreflightScene(scene, own);
        _sceneSources.Add(logicalPath);
        return new ImportedTreeScene(this, scene.Instantiate<Node3D>(), identity);
    }

    public sealed class ImportedTreeScene : IDisposable
    {
        private readonly FloraAppearanceInputs _owner;
        private readonly string _identity;
        private readonly Dictionary<ulong, ImportedNode> _nodes = new();
        private bool _disposed;
        private sealed record ImportedNode(MeshInstance3D Instance, ArrayMesh Mesh, string Path, string Metadata, ArrayMesh? Shadow);
        internal ImportedTreeScene(FloraAppearanceInputs owner, Node3D root, string identity)
        {
            _owner = owner; Root = root; _identity = identity;
            // Snapshot before Root escapes: callers cannot attach a different mesh and obtain a receipt for it.
            try { Visit(root, Transform3D.Identity, 0); }
            catch { root.Free(); throw; }
            void Visit(Node node, Transform3D parent, int depth)
            {
                Capacity(depth <= MaxDepth && _nodes.Count < MaxMeshes, "Imported node tree exceeds limits.");
                Transform3D transform = node is Node3D spatial ? parent * spatial.Transform : parent;
                if (node is MeshInstance3D instance)
                {
                    if (instance.Mesh is not ArrayMesh mesh || !transform.IsEqualApprox(Transform3D.Identity))
                        throw new FloraAppearanceValidationException("Imported mesh is missing or has an unbaked transform.");
                    string path = root.GetPathTo(instance).ToString();
                    if (path.Length > 240) Fail("Imported tree node path is too long.");
                    _nodes.Add(instance.GetInstanceId(), new ImportedNode(instance, mesh, path, owner.MeshMetadata(mesh), mesh.ShadowMesh));
                }
                foreach (Node child in node.GetChildren()) Visit(child, transform, depth + 1);
            }
        }
        public Node3D Root { get; }
        public ArrayMesh TakeMesh(MeshInstance3D instance)
        {
            _owner.Creating(); if (_disposed) throw new ObjectDisposedException(nameof(ImportedTreeScene));
            if (!_nodes.TryGetValue(instance.GetInstanceId(), out ImportedNode? source) || instance != source.Instance ||
                instance.Mesh != source.Mesh || (instance != Root && !Root.IsAncestorOf(instance)))
                throw new FloraAppearanceValidationException("Mesh does not belong to the captured imported scene.");
            ArrayMesh mesh = source.Mesh;
            Transform3D transform = Transform3D.Identity;
            for (Node? node = instance; node != null; node = node.GetParent())
            {
                if (node is Node3D spatial) transform = spatial.Transform * transform;
                if (node == Root) break;
            }
            if (!transform.IsEqualApprox(Transform3D.Identity)) Fail("Imported tree has an unbaked transform.");
            _owner.CapacityForImported(mesh);
            string nodePath = Root.GetPathTo(instance).ToString();
            if (nodePath != source.Path || _owner.MeshMetadata(mesh) != source.Metadata || mesh.ShadowMesh != source.Shadow)
                Fail("Imported mesh binding changed before admission.");
            var receipt = new MeshReceipt { Mesh = mesh,
                ImportedIdentity = Digest(w => { w.Write(_identity); w.Write(nodePath); }),
                ImportedMetadata = source.Metadata, ImportedShadow = source.Shadow };
            if (!_owner._meshes.TryAdd(mesh.GetInstanceId(), receipt)) Fail("Imported mesh is aliased between tree nodes.");
            return mesh;
        }
        public void Dispose() { if (_disposed) return; _disposed = true; _nodes.Clear(); Root.Free(); }
    }

    /// <summary>Full export identity, including curated producer sources and authored source-file closure.</summary>
    public FloraAppearanceInputContract CaptureExport(FloraLibrary library, int recipeSeed, string repositoryRoot)
    {
        Alive(); FloraAppearanceInputContract runtime = Capture(library, recipeSeed);
        var dependencies = runtime.Dependencies.ToDictionary(d => d.Key, StringComparer.Ordinal);
        string root = Path.GetFullPath(repositoryRoot);
        string[] sourceFiles =
        {
            "game/scripts/Surface/Flora/FloraAppearanceInputs.cs", "game/scripts/Surface/Flora/FloraLibrary.cs",
            "game/scripts/Surface/Flora/FloraLibrary.Trees.cs", "game/scripts/Surface/Flora/FloraGeometry.cs",
            "game/scripts/Surface/Flora/FloraLibrary.Rocks.cs", // shared native PBR channel conversion
            "game/scripts/Surface/Flora/FloraTextures.cs", "game/scripts/Surface/Flora/FloraTextures.ImpostorPixels.cs",
            "game/scripts/Surface/Flora/Painter.cs", "game/scripts/Surface/Flora/ImpostorBaker.cs",
            "game/scripts/Surface/Flora/FloraImpostorExport.cs",
            "game/scripts/Surface/NoiseTextures.cs",
            "src/Hollow.Core/Hash64.cs", "src/Hollow.Core/Rng.cs", "src/Hollow.Core/DMath.cs",
        };
        foreach (string file in sourceFiles) Add(dependencies, ReadHostFile(root, file));
        foreach (string resource in _sceneSources.Concat(_textureSources).OrderBy(p => p, StringComparer.Ordinal))
        {
            string relative = "game/" + resource[6..];
            Add(dependencies, ReadHostFile(root, relative));
            Add(dependencies, ReadHostFile(root, relative + ".import"));
            if (!resource.EndsWith(".gltf", StringComparison.Ordinal)) continue;
            byte[] json = ReadSmallResource(resource);
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaxDepth });
            foreach (string array in new[] { "buffers", "images" })
            {
                if (!document.RootElement.TryGetProperty(array, out JsonElement entries)) continue;
                Capacity(entries.GetArrayLength() <= 64, "Authored source dependency limit exceeded.");
                foreach (JsonElement entry in entries.EnumerateArray())
                {
                    if (!entry.TryGetProperty("uri", out JsonElement uri)) continue;
                    string name = uri.GetString() ?? throw new FloraAppearanceValidationException("Invalid glTF source URI.");
                    if (name.Length == 0 || name.Contains(':') || name.Contains('\\') || name.StartsWith('/') ||
                        name.Split('/').Any(s => s is "" or "." or "..")) Fail("Unsupported glTF source URI.");
                    Add(dependencies, ReadHostFile(root, relative[..(relative.LastIndexOf('/') + 1)] + name));
                }
            }
        }
        var profile = new FloraAppearanceBakeProfile(Engine.GetVersionInfo()["string"].AsString(),
            RenderingServer.GetCurrentRenderingMethod(), RenderingServer.GetVideoAdapterName(), AdapterRevision,
            FinishRevision, ProjectionRevision);
        return new FloraAppearanceInputContract(recipeSeed, profile, dependencies.Values.ToArray(), runtime.Rows);
    }

    /// <summary>
    /// Checks a freshly created library against the independently approved package/pin. It never treats an export's
    /// self-declared inputs as approval. The returned fingerprint omits export provenance and hardware, not geometry,
    /// shader code/defaults/material values, imported payloads, near pixels, or ordered row interpretation.
    /// </summary>
    public string ValidateRuntime(FloraLibrary library, int recipeSeed, FloraAppearancePackage approved)
    {
        ArgumentNullException.ThrowIfNull(approved);
        FloraAppearanceInputContract actual = Capture(library, recipeSeed);
        string expected = RuntimeFingerprint(approved.Inputs);
        if (actual.Fingerprint != expected) Fail("Live flora creation inputs differ from the approved appearance.");
        foreach (ImageReceipt receipt in _images.Values)
        {
            FloraAppearanceImage image = approved.Images.Single(i => i.Layout.Role == receipt.Layout.Role);
            if (receipt.Hash != image.Sha256) Fail("Uploaded near image differs from the admitted package.");
        }
        if (library.BladeMean != new Vector3(approved.BladeMean.R, approved.BladeMean.G, approved.BladeMean.B))
            Fail("Live blade mean differs from the admitted package.");
        return actual.Fingerprint;
    }

    public static string RuntimeFingerprint(FloraAppearanceInputContract approved)
    {
        CheckEngine();
        if (approved.Profile.BakeRevision != AdapterRevision || approved.Profile.FinishRevision != FinishRevision ||
            approved.Profile.ProjectionRevision != ProjectionRevision) Fail("Unsupported flora adapter/bake interpretation.");
        if (approved.Dependencies.Any(d => !d.Key.StartsWith(RuntimePrefix, StringComparison.Ordinal) &&
            !d.Key.StartsWith(ProvenancePrefix, StringComparison.Ordinal))) Fail("Unclassified flora input dependency.");
        return new FloraAppearanceInputContract(approved.RecipeSeed, CompatibilityProfile(),
            approved.Dependencies.Where(d => d.Key.StartsWith(RuntimePrefix, StringComparison.Ordinal)).ToArray(), approved.Rows).Fingerprint;
    }

    private FloraAppearanceInputContract Capture(FloraLibrary library, int recipeSeed)
    {
        Alive(); _sealed = true;
        if (_images.Count != 5) Fail("All five near-image upload receipts are required.");
        var dependencies = new Dictionary<string, FloraAppearanceDependency>(StringComparer.Ordinal);
        foreach (FloraAppearanceDependency file in _files.Values) Add(dependencies, file);
        foreach (ImageReceipt receipt in _images.Values)
        {
            CheckTexture(receipt);
            Add(dependencies, new FloraAppearanceDependency(RuntimePrefix + "image/" + receipt.Layout.Role,
                receipt.Layout.ByteLength, receipt.Hash));
        }
        Add(dependencies, ReadResource(CatalogPath));
        var shaderCache = new Dictionary<ulong, string>();
        string bake = ShaderIdentity(GD.Load<Shader>("res://shaders/surface_impostor_bake.gdshader"), dependencies, shaderCache);
        _ = ShaderIdentity(GD.Load<Shader>("res://shaders/surface_impostor.gdshader"), dependencies, shaderCache);
        var rows = new List<FloraAppearanceRow>();
        foreach (FloraLibrary.Species species in library.All)
        {
            if (!species.Tree) continue;
            for (int variantIndex = 0; variantIndex < species.Variants.Count; variantIndex++)
            {
                Capacity(rows.Count < FloraAppearanceSchema.Rows, "Too many tree rows.");
                FloraLibrary.Variant variant = species.Variants[variantIndex];
                if (variant.ImpostorRow != -1 && variant.ImpostorRow != rows.Count) Fail("Live impostor row differs.");
                string material = Digest(w =>
                {
                    w.Write(AdapterRevision); w.Write(bake);
                    foreach (ArrayMesh mesh in new[] { variant.Lod0, variant.Lod1 })
                    {
                        w.Write(mesh.GetSurfaceCount());
                        for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
                        {
                            if (mesh.SurfaceGetMaterial(surface) is not ShaderMaterial sm || sm == library.Rock)
                                throw new FloraAppearanceValidationException("Tree surface lacks an independent shader material.");
                            WriteMaterial(w, sm, rows.Count >= FloraAppearanceSchema.NativeRowStart, dependencies, shaderCache);
                        }
                    }
                });
                rows.Add(new FloraAppearanceRow(rows.Count, species.Kind, variantIndex, species.Name + "-" + variantIndex,
                    species.Scale, variant.Height, variant.Extent, GeometryIdentity(variant.Lod0),
                    GeometryIdentity(variant.Lod1), material));
            }
        }
        // Include resources discovered by material traversal, not only the closure known at capture entry.
        foreach (FloraAppearanceDependency file in _files.Values) Add(dependencies, file);
        return new FloraAppearanceInputContract(recipeSeed, CompatibilityProfile(), dependencies.Values.ToArray(), rows);
    }

    private void WriteMaterial(BinaryWriter w, ShaderMaterial material, bool native,
        Dictionary<string, FloraAppearanceDependency> dependencies, Dictionary<ulong, string> shaders)
    {
        if (material.NextPass != null || material.Shader == null) Fail("Unsupported tree material pass chain.");
        w.Write(ShaderIdentity(material.Shader!, dependencies, shaders)); w.Write(material.RenderPriority);
        bool nativeMeta = material.HasMeta("native_tree");
        if (nativeMeta && (material.GetMeta("native_tree").VariantType != Variant.Type.Bool || !material.GetMeta("native_tree").AsBool()))
            Fail("Native tree metadata must be true when present.");
        if (nativeMeta != native) Fail("Tree native/vertex-colour interpretation differs from its row.");
        w.Write(nativeMeta); w.Write(!nativeMeta); // baker's vertex_colour override
        Variant bakeMode = material.GetShaderParameter("mode"), bakeAlbedo = material.GetShaderParameter("albedo_texture");
        if (bakeMode.VariantType != Variant.Type.Int || bakeMode.AsInt64() is < 0 or > 1 ||
            bakeAlbedo.VariantType != Variant.Type.Object || bakeAlbedo.AsGodotObject() is not Texture2D)
            Fail("Tree baker requires explicit mode and albedo bindings.");
        WriteValue(w, bakeMode, dependencies); WriteValue(w, bakeAlbedo, dependencies);
        if (nativeMeta)
        {
            if (!material.HasMeta("bake_albedo_factor") || !material.HasMeta("bake_alpha_cutoff"))
                Fail("Native bake material metadata is incomplete.");
            WriteValue(w, material.GetMeta("bake_albedo_factor"), dependencies);
            WriteValue(w, material.GetMeta("bake_alpha_cutoff"), dependencies);
        }
        // RenderingServer lists local uniforms, including shader default samplers. Shader.GetShaderUniformList(false)
        // omits those default samplers; using it here would leave effective textures unauthenticated.
        var properties = RenderingServer.GetShaderParameterList(material.Shader!.GetRid());
        Capacity(properties.Count <= MaxUniforms * 2, "Shader uniform metadata limit exceeded.");
        var uniforms = properties.Where(p => (p["usage"].AsInt64() & (long)(PropertyUsageFlags.Group | PropertyUsageFlags.Subgroup)) == 0)
            .OrderBy(p => p["name"].AsString(), StringComparer.Ordinal).ToArray();
        Capacity(uniforms.Length <= MaxUniforms, "Shader uniform count exceeded.");
        w.Write(uniforms.Length);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Godot.Collections.Dictionary property in uniforms)
        {
            string name = property["name"].AsString();
            if (name.Length is < 1 or > 120 || !names.Add(name)) Fail("Invalid or duplicate shader uniform name.");
            string hint = property["hint_string"].AsString();
            Capacity(hint.Length <= 1024, "Shader parameter hint length exceeded.");
            w.Write(name); w.Write(property["type"].AsInt64());
            w.Write(property["hint"].AsInt64()); w.Write(hint);
            Variant value = material.GetShaderParameter(name);
            if (value.VariantType == Variant.Type.Nil)
            {
                Texture? texture = material.Shader.GetDefaultTextureParameter(name, 0);
                value = texture != null ? Variant.From(texture) : RenderingServer.ShaderGetParameterDefault(material.Shader.GetRid(), name);
            }
            WriteValue(w, value, dependencies);
        }
    }

    private void WriteValue(BinaryWriter w, Variant value, Dictionary<string, FloraAppearanceDependency> dependencies)
    {
        w.Write((int)value.VariantType);
        switch (value.VariantType)
        {
            case Variant.Type.Nil: break; // explicit unbound sampler; shader declaration/hints bind its engine default
            case Variant.Type.Bool: w.Write(value.AsBool()); break;
            case Variant.Type.Int: w.Write(value.AsInt64()); break;
            case Variant.Type.Float:
                double d = value.AsDouble(); if (!double.IsFinite(d)) Fail("Nonfinite shader parameter."); w.Write(d); break;
            case Variant.Type.Vector2: Vector2 v2 = value.AsVector2(); Float(w, v2.X); Float(w, v2.Y); break;
            case Variant.Type.Vector3: Vector(w, value.AsVector3()); break;
            case Variant.Type.Vector4:
                Vector4 v4 = value.AsVector4(); Float(w, v4.X); Float(w, v4.Y); Float(w, v4.Z); Float(w, v4.W); break;
            case Variant.Type.Color: Colour(w, value.AsColor()); break;
            case Variant.Type.Object:
                if (value.AsGodotObject() is not Texture2D texture) Fail("Unsupported object-valued tree uniform.");
                else WriteTexture(w, texture, dependencies);
                break;
            default: Fail("Unsupported tree uniform type: " + value.VariantType); break;
        }
    }

    private void WriteTexture(BinaryWriter w, Texture2D texture, Dictionary<string, FloraAppearanceDependency> dependencies)
    {
        w.Write(texture.GetWidth()); w.Write(texture.GetHeight());
        if (_images.TryGetValue(texture.GetInstanceId(), out ImageReceipt? receipt))
        { CheckTexture(receipt); w.Write("cpu-upload"); w.Write(receipt.Layout.Role); w.Write(receipt.Hash); return; }
        if (texture is not CompressedTexture2D imported) Fail("Texture has no captured upload/import receipt.");
        else
        {
            if (!_importedTextures.TryGetValue(texture.GetInstanceId(), out ImportedTextureReceipt? captured) ||
                captured.Invalid || captured.Texture != imported || imported.LoadPath != captured.Path ||
                imported.GetWidth() != captured.Width || imported.GetHeight() != captured.Height)
                Fail("Texture was not admitted with this imported scene, or changed since loading.");
            FloraAppearanceDependency file = captured.File; Add(dependencies, file);
            w.Write("ctex"); w.Write(file.Key); w.Write(file.Bytes); w.Write(file.Sha256);
        }
    }

    private string ShaderIdentity(Shader shader, Dictionary<string, FloraAppearanceDependency> dependencies,
        Dictionary<ulong, string> cache)
    {
        if (cache.TryGetValue(shader.GetInstanceId(), out string? found)) return found;
        var closure = new SortedDictionary<string, string>(StringComparer.Ordinal);
        string path = ResourcePath(shader.ResourcePath);
        CaptureShaderCode(path, shader.Code, closure, dependencies, new HashSet<string>(StringComparer.Ordinal), 0);
        string digest = Digest(w => { foreach (var part in closure) { w.Write(part.Key); w.Write(part.Value); } });
        cache.Add(shader.GetInstanceId(), digest); return digest;
    }

    private void CaptureShaderCode(string path, string code, SortedDictionary<string, string> closure,
        Dictionary<string, FloraAppearanceDependency> dependencies, HashSet<string> active, int depth)
    {
        Alive(); Capacity(depth <= MaxDepth && closure.Count < 32, "Shader include closure limit exceeded.");
        if (!active.Add(path)) Fail("Cyclic shader include.");
        Capacity(code.Length is > 0 && code.Length <= _limits.MaxMetadataBytes && Utf8.GetByteCount(code) <= _limits.MaxMetadataBytes,
            "Shader source exceeds limit.");
        byte[] bytes = Utf8.GetBytes(code);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        if (closure.TryGetValue(path, out string? old) && old != hash) Fail("Shader include changed during capture.");
        closure[path] = hash;
        Add(dependencies, new FloraAppearanceDependency(RuntimePrefix + "shader/" + path[6..], bytes.Length, hash));
        string uncommented = Regex.Replace(code, @"""(?:\\.|[^""\\])*""|/\*[\s\S]*?\*/|//[^\r\n]*",
            m => m.Value[0] == '"' ? m.Value : "", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        int directives = Regex.Matches(uncommented, @"^[\t ]*#[\t ]*include\b", RegexOptions.Multiline,
            TimeSpan.FromSeconds(1)).Count;
        if (directives != Includes.Matches(uncommented).Count) Fail("Unsupported shader include directive.");
        foreach (Match match in Includes.Matches(uncommented))
        {
            string include = match.Groups[1].Value;
            string includePath = ResourcePath(include.StartsWith("res://", StringComparison.Ordinal)
                ? include : path[..(path.LastIndexOf('/') + 1)] + include);
            ShaderInclude resource = GD.Load<ShaderInclude>(includePath)
                ?? throw new FloraAppearanceValidationException("Shader include cannot be loaded.");
            CaptureShaderCode(includePath, resource.Code, closure, dependencies, active, depth + 1);
        }
        active.Remove(path);
    }

    private string GeometryIdentity(ArrayMesh mesh)
    {
        if (!_meshes.TryGetValue(mesh.GetInstanceId(), out MeshReceipt? receipt))
            throw new FloraAppearanceValidationException("Tree mesh has no creation receipt; uploaded arrays cannot be inspected without GPU readback.");
        string metadata = MeshMetadata(mesh);
        if (receipt.ImportedIdentity.Length != 0)
        {
            if (metadata != receipt.ImportedMetadata || mesh.ShadowMesh != receipt.ImportedShadow)
                Fail("Imported mesh metadata changed after admission.");
            return Digest(w => { w.Write(AdapterRevision); w.Write(receipt.ImportedIdentity); w.Write(metadata); });
        }
        if (receipt.Surfaces.Count == 0 || mesh.GetSurfaceCount() != receipt.Surfaces.Count || mesh.ShadowMesh != null)
            Fail("Procedural mesh surfaces/shadow changed after upload.");
        return Digest(w =>
        {
            w.Write(AdapterRevision); w.Write(metadata);
            for (int i = 0; i < receipt.Surfaces.Count; i++)
            {
                if (receipt.SurfaceMetadata[i] != SurfaceMetadata(mesh, i)) Fail("Procedural surface metadata changed after upload.");
                w.Write(receipt.Surfaces[i]);
            }
        });
    }

    private string MeshMetadata(ArrayMesh mesh)
    {
        if (!GodotObject.IsInstanceValid(mesh) || mesh.GetBlendShapeCount() != 0) Fail("Invalid tree mesh or unsupported blend shapes.");
        int count = mesh.GetSurfaceCount(); Capacity(count is > 0 and <= MaxSurfaces, "Tree surface count exceeded.");
        return Digest(w =>
        {
            w.Write(count); Box(w, mesh.GetAabb()); Box(w, mesh.CustomAabb);
            for (int i = 0; i < count; i++) w.Write(SurfaceMetadata(mesh, i));
            // Imported shadow geometry is bound by the same entire scene closure. Procedural trees have none.
            ArrayMesh? shadow = mesh.ShadowMesh; w.Write(shadow != null);
            if (shadow == null) return;
            if (shadow == mesh || shadow.ShadowMesh != null || shadow.GetBlendShapeCount() != 0) Fail("Unsupported recursive shadow mesh.");
            int surfaces = shadow.GetSurfaceCount(); Capacity(surfaces is > 0 and <= MaxSurfaces, "Shadow surface count exceeded.");
            w.Write(surfaces); Box(w, shadow.GetAabb()); Box(w, shadow.CustomAabb);
            for (int i = 0; i < surfaces; i++) w.Write(SurfaceMetadata(shadow, i));
        });
    }

    private string SurfaceMetadata(ArrayMesh mesh, int surface)
    {
        int vertices = mesh.SurfaceGetArrayLen(surface), indices = mesh.SurfaceGetArrayIndexLen(surface);
        Capacity(vertices is >= 3 && vertices <= _limits.MaxVerticesPerSurface && indices >= 0 &&
            indices <= _limits.MaxIndicesPerSurface, "Imported surface count exceeds admission limits.");
        if (mesh.SurfaceGetPrimitiveType(surface) != Mesh.PrimitiveType.Triangles ||
            (indices != 0 ? indices : vertices) % 3 != 0) Fail("Tree surface is not a triangle list.");
        Mesh.ArrayFormat format = mesh.SurfaceGetFormat(surface);
        if ((format & (Mesh.ArrayFormat.FormatBones | Mesh.ArrayFormat.FormatWeights | Mesh.ArrayFormat.FlagUse2DVertices)) != 0)
            Fail("Skinned/2D tree geometry is unsupported.");
        return Digest(w => { w.Write(vertices); w.Write(indices); w.Write((ulong)format); w.Write((int)Mesh.PrimitiveType.Triangles); });
    }

    private void CapacityForImported(ArrayMesh mesh)
    {
        Capacity(_meshes.Count < MaxMeshes, "Mesh receipt limit exceeded.");
        _ = MeshMetadata(mesh);
    }

    private void PreflightScene(PackedScene scene, Action<Resource>? own)
    {
        using SceneState state = scene.GetState();
        int nodes = state.GetNodeCount(); Capacity(nodes is > 0 and <= 128, "Imported scene node count exceeded.");
        if (state.GetBaseSceneState() != null || state.GetConnectionCount() != 0 || scene.ResourceLocalToScene ||
            scene.GetScript().VariantType != Variant.Type.Nil) Fail("Unsupported inherited/scripted tree scene.");
        var resources = new HashSet<ulong>();
        for (int i = 0; i < nodes; i++)
        {
            string type = state.GetNodeType(i).ToString();
            if (type is not ("Node3D" or "MeshInstance3D") || state.GetNodeInstance(i) != null || state.IsNodeInstancePlaceholder(i))
                Fail("Tree scene must contain only plain nodes and meshes, with no nested scenes.");
            int count = state.GetNodePropertyCount(i); Capacity(count <= 64, "Imported node property count exceeded.");
            for (int p = 0; p < count; p++)
            {
                Variant value = state.GetNodePropertyValue(i, p);
                if (state.GetNodePropertyName(i, p).ToString() == "script" && value.VariantType != Variant.Type.Nil)
                    Fail("Scripted tree nodes are unsupported.");
                if (value.VariantType == Variant.Type.Object && value.AsGodotObject() is Resource resource) CheckResource(resource, 0, false);
                else if (value.VariantType is Variant.Type.Array or Variant.Type.Dictionary)
                    Fail("Nested resource-valued scene properties are unsupported.");
            }
        }
        void CheckResource(Resource resource, int depth, bool shadow)
        {
            Capacity(depth <= MaxDepth && resources.Count < 256, "Imported resource preflight exceeds limits.");
            if (!resources.Add(resource.GetInstanceId())) return;
            // Report private resources before inspecting them so a failed creation can release its partial import.
            // The callback must only retain ownership; it must not mutate the resource during qualification.
            own?.Invoke(resource);
            if (resource.ResourceLocalToScene || resource.GetScript().VariantType != Variant.Type.Nil)
                Fail("Local-to-scene/scripted resources could duplicate meshes or alter imported data.");
            if (resource is ArrayMesh mesh)
            {
                if (mesh.ShadowMesh != null) CheckResource(mesh.ShadowMesh, depth + 1, true);
                _ = MeshMetadata(mesh);
                for (int s = 0; s < mesh.GetSurfaceCount(); s++)
                {
                    Material? material = mesh.SurfaceGetMaterial(s);
                    if (shadow) { if (material != null) Fail("Imported shadow surfaces must have no material."); }
                    else if (material == null) Fail("Missing imported tree material.");
                    else CheckResource(material, depth + 1, false);
                }
            }
            else if (resource is StandardMaterial3D material)
            {
                if (material.NextPass != null) Fail("Imported tree material pass chains are unsupported.");
                for (int i = 0; i < (int)BaseMaterial3D.TextureParam.Max; i++)
                {
                    Texture2D? texture = material.GetTexture((BaseMaterial3D.TextureParam)i);
                    if (texture != null) CheckResource(texture, depth + 1, false);
                }
            }
            else if (resource is CompressedTexture2D texture) RecordImportedTexture(texture);
            else Fail("Unsupported imported tree resource type.");
        }
    }

    private string CaptureResourceClosure(string logicalPath, Dictionary<string, string> closure,
        HashSet<string> active, int depth)
    {
        Alive(); Capacity(depth <= MaxDepth && closure.Count < 128, "Imported resource closure exceeds limits.");
        logicalPath = ResourcePath(logicalPath); string path = ResolveImported(logicalPath);
        if (active.Contains(path)) Fail("Cyclic imported resource dependency.");
        if (closure.TryGetValue(path, out string? captured))
            return Digest(w => { w.Write(logicalPath); w.Write(path); w.Write(captured); });
        active.Add(path);
        FloraAppearanceDependency file = ReadResource(path);
        string[] dependencies = ResourceLoader.GetDependencies(path);
        Capacity(dependencies.Length <= 128, "Imported resource dependency count exceeded.");
        var children = new List<(string Declaration, string Identity)>();
        foreach (string dependency in dependencies)
        {
            string[] fields = dependency.Split("::", StringSplitOptions.None);
            string target;
            if (fields.Length == 3 && fields[0].StartsWith("uid://", StringComparison.Ordinal))
            {
                long uid = ResourceUid.TextToId(fields[0]);
                target = ResourceUid.HasId(uid) ? ResourceUid.GetIdPath(uid) : fields[2];
            }
            else if (fields.Length == 1) target = fields[0];
            else throw new FloraAppearanceValidationException("Unsupported decorated resource dependency.");
            if (!target.StartsWith("res://", StringComparison.Ordinal)) Fail("Imported dependency lacks an explicit resource path.");
            children.Add((dependency, CaptureResourceClosure(target, closure, active, depth + 1)));
        }
        active.Remove(path);
        // Bind edges as well as file contents: swapping two UID/remap targets must not produce the same identity
        // merely because the set of resolved files is unchanged.
        string identity = Digest(w =>
        {
            w.Write(AdapterRevision); w.Write("imported-resource"); w.Write(file.Key); w.Write(file.Bytes); w.Write(file.Sha256);
            w.Write(children.Count); foreach (var child in children) { w.Write(child.Declaration); w.Write(child.Identity); }
        });
        Capacity(closure.Count < 128, "Imported resource closure exceeds limits."); closure.Add(path, identity);
        return Digest(w => { w.Write(logicalPath); w.Write(path); w.Write(identity); });
    }

    private string ResolveImported(string path)
    {
        path = ResourcePath(path);
        Variant translations = ProjectSettings.GetSetting("internationalization/locale/translation_remaps");
        if (translations.VariantType == Variant.Type.Dictionary && translations.AsGodotDictionary().Count != 0)
            Fail("Translated resource remaps require a separate qualified appearance profile.");
        for (int depth = 0; depth < 4; depth++)
        {
            string metadata = GFile.FileExists(path + ".remap") ? path + ".remap" : path + ".import";
            if (!GFile.FileExists(metadata)) return path;
            using var config = new ConfigFile();
            string text = Utf8.GetString(ReadSmallResource(metadata));
            // ConfigFile overwrites duplicate keys, unlike the native importer's first matching path. Refuse duplicates.
            var keys = new HashSet<string>(StringComparer.Ordinal); string section = "";
            foreach (string line in text.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith('[') && trimmed.EndsWith(']')) { section = trimmed; continue; }
                if (section != "[remap]" || trimmed.Length == 0 || trimmed.StartsWith(';')) continue;
                int equals = trimmed.IndexOf('=');
                if (equals > 0 && !keys.Add(trimmed[..equals].Trim())) Fail("Duplicate resource remap key.");
            }
            if (config.Parse(text) != Error.Ok || !config.HasSection("remap")) Fail("Invalid resource remap metadata.");
            Variant importMetadata = config.HasSectionKey("remap", "metadata")
                ? config.GetValue("remap", "metadata") : default;
            if (importMetadata.VariantType == Variant.Type.Dictionary &&
                importMetadata.AsGodotDictionary().ContainsKey("has_editor_variant"))
                Fail("Editor-only import variants require a separate qualified appearance profile.");
            string? mapped = null;
            foreach (string key in config.GetSectionKeys("remap"))
            {
                if (key == "path") { mapped = config.GetValue("remap", key).AsString(); break; }
                if (key.StartsWith("path.", StringComparison.Ordinal) && OS.HasFeature(key[5..]))
                { mapped = config.GetValue("remap", key).AsString(); break; }
            }
            if (mapped == null) Fail("No supported resource import remap.");
            string next = ResourcePath(mapped!); if (next == path) Fail("Resource remaps to itself."); path = next;
        }
        throw new FloraAppearanceValidationException("Resource remap depth exceeded.");
    }

    private byte[] ReadSmallResource(string path)
    {
        Alive(); path = ResourcePath(path);
        using GFile file = GFile.Open(path, GFile.ModeFlags.Read)
            ?? throw new FloraAppearanceValidationException("Missing flora metadata: " + path);
        ulong length = file.GetLength(); Capacity(length > 0 && length <= (ulong)_limits.MaxMetadataBytes, "Metadata byte limit exceeded.");
        ChargeFile((long)length); byte[] bytes = file.GetBuffer((long)length);
        if ((ulong)bytes.Length != length) Fail("Short metadata read."); return bytes;
    }

    private FloraAppearanceDependency ReadResource(string path)
    {
        Alive(); path = ResourcePath(path);
        if (_files.TryGetValue(path, out FloraAppearanceDependency? existing)) return existing;
        using GFile file = GFile.Open(path, GFile.ModeFlags.Read)
            ?? throw new FloraAppearanceValidationException("Missing flora runtime input: " + path);
        ulong size = file.GetLength(); Capacity(size > 0 && size <= (ulong)_limits.MaxFileBytes, "Runtime file byte limit exceeded.");
        ChargeFile((long)size); using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long remaining = (long)size;
        while (remaining > 0)
        {
            Alive(); int count = (int)Math.Min(65536, remaining); byte[] bytes = file.GetBuffer(count);
            if (bytes.Length != count) Fail("Short runtime dependency read."); hash.AppendData(bytes); remaining -= count;
        }
        var dependency = new FloraAppearanceDependency(RuntimePrefix + "resource/" + path[6..], (long)size,
            Convert.ToHexString(hash.GetHashAndReset()));
        Capacity(_files.Count < FloraAppearanceSchema.MaxDependencies, "Runtime file count exceeded.");
        _files.Add(path, dependency); return dependency;
    }

    private FloraAppearanceDependency ReadHostFile(string root, string relative)
    {
        Alive(); string full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            Fail("Export source escaped its repository.");
        using var file = new FileStream(full, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read);
        Capacity(file.Length > 0 && file.Length <= _limits.MaxFileBytes, "Export source byte limit exceeded.");
        long length = file.Length; ChargeFile(length);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); byte[] buffer = new byte[65536];
        long remaining = length;
        while (remaining > 0)
        {
            Alive(); int count = file.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (count == 0) Fail("Short export source read."); hash.AppendData(buffer, 0, count); remaining -= count;
        }
        if (file.ReadByte() != -1) Fail("Export source grew during capture.");
        return new FloraAppearanceDependency(ProvenancePrefix + relative, length, Convert.ToHexString(hash.GetHashAndReset()));
    }

    private static FloraAppearanceImageLayout NearLayout(string role)
        => FloraAppearanceSchema.Images.Take(5).SingleOrDefault(i => i.Role == role)
            ?? throw new FloraAppearanceValidationException("Expected one of the five near-image roles.");
    private void RecordImage(ImageTexture texture, FloraAppearanceImageLayout layout, string hash)
    {
        var receipt = new ImageReceipt { Texture = texture, Layout = layout, Hash = hash };
        texture.Changed += receipt.Changed;
        _images.Add(texture.GetInstanceId(), receipt);
    }
    private void RecordImportedTexture(CompressedTexture2D texture)
    {
        if (_importedTextures.ContainsKey(texture.GetInstanceId())) return;
        Capacity(_importedTextures.Count < 128, "Imported texture receipt count exceeded.");
        string path = ResourcePath(texture.LoadPath);
        if (!path.EndsWith(".ctex", StringComparison.Ordinal)) Fail("Expected the actual CTEX load path.");
        var receipt = new ImportedTextureReceipt { Texture = texture, Path = path, File = ReadResource(path),
            Width = texture.GetWidth(), Height = texture.GetHeight() };
        texture.Changed += receipt.Changed; _importedTextures.Add(texture.GetInstanceId(), receipt);
        _textureSources.Add(ResourcePath(texture.ResourcePath));
    }
    private static void CheckTexture(ImageReceipt receipt)
    {
        if (receipt.Invalid || !GodotObject.IsInstanceValid(receipt.Texture) ||
            receipt.Texture.GetWidth() != receipt.Layout.Width || receipt.Texture.GetHeight() != receipt.Layout.Height ||
            receipt.Texture.GetFormat() != Image.Format.Rgba8)
            Fail("Uploaded texture changed after creation.");
    }
    private static FloraAppearanceBakeProfile CompatibilityProfile()
        => new("godot-4.7.2", "runtime-compatible", "prebaked", AdapterRevision, FinishRevision, ProjectionRevision);
    private static void CheckEngine()
    {
        var version = Engine.GetVersionInfo();
        if (version["major"].AsInt32() != 4 || version["minor"].AsInt32() != 7 || version["patch"].AsInt32() != 2)
            Fail("Flora upload/import identity is qualified only for Godot 4.7.2.");
    }
    private static string ResourcePath(string path)
    {
        if (path == null || path.Length is < 7 or > 200 || !path.StartsWith("res://", StringComparison.Ordinal) ||
            path[6..].Contains(':') || path.Contains('\\') || path.Any(char.IsControl) ||
            path[6..].Split('/').Any(s => s is "" or "." or ".."))
            throw new FloraAppearanceValidationException("Expected a canonical bounded res:// input path.");
        return path;
    }
    private static void Add(Dictionary<string, FloraAppearanceDependency> dependencies, FloraAppearanceDependency value)
    {
        if (dependencies.TryGetValue(value.Key, out FloraAppearanceDependency? old))
        { if (old.Bytes != value.Bytes || old.Sha256 != value.Sha256) Fail("Input changed within one capture: " + value.Key); return; }
        Capacity(dependencies.Count < FloraAppearanceSchema.MaxDependencies, "Input dependency count exceeded."); dependencies.Add(value.Key, value);
    }
    private void ChargeFile(long count)
    { _fileBytes = checked(_fileBytes + count); Capacity(_fileBytes <= _limits.MaxTotalFileBytes, "Total input file byte budget exceeded."); }
    private void Creating() { Alive(); if (_sealed) Fail("Flora creation receipts are already sealed."); }
    private void Alive()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FloraAppearanceInputs));
        if (System.Environment.CurrentManagedThreadId != _thread) Fail("Flora capture is main-thread confined.");
        _cancellation.ThrowIfCancellationRequested();
    }
    public void Dispose()
    {
        if (_disposed) return;
        if (System.Environment.CurrentManagedThreadId != _thread) Fail("Flora capture must retire on its owning thread.");
        _disposed = true;
        foreach (ImageReceipt image in _images.Values)
            if (GodotObject.IsInstanceValid(image.Texture)) image.Texture.Changed -= image.Changed;
        foreach (ImportedTextureReceipt texture in _importedTextures.Values)
            if (GodotObject.IsInstanceValid(texture.Texture)) texture.Texture.Changed -= texture.Changed;
        _meshes.Clear(); _images.Clear(); _importedTextures.Clear(); _files.Clear(); _sceneSources.Clear(); _textureSources.Clear();
    }
    private static void Capacity(bool condition, string message) { if (!condition) throw new FloraAppearanceCapacityException(message); }
    [DoesNotReturn]
    private static void Fail(string message) => throw new FloraAppearanceValidationException(message);
    private static void Float(BinaryWriter w, float value) { if (!float.IsFinite(value)) Fail("Nonfinite flora input."); w.Write(value); }
    private static void Vector(BinaryWriter w, Vector3 value) { Float(w, value.X); Float(w, value.Y); Float(w, value.Z); }
    private static void Colour(BinaryWriter w, Color value) { Float(w, value.R); Float(w, value.G); Float(w, value.B); Float(w, value.A); }
    private static void Box(BinaryWriter w, Aabb value) { Vector(w, value.Position); Vector(w, value.Size); }
    private static string Digest(Action<BinaryWriter> write)
    {
        using var stream = new HashStream(); using var writer = new BinaryWriter(stream, Utf8, leaveOpen: true);
        write(writer); writer.Flush(); return stream.Finish();
    }
    private sealed class HashStream : Stream
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        public string Finish() => Convert.ToHexString(_hash.GetHashAndReset());
        public override void Write(byte[] buffer, int offset, int count) => _hash.AppendData(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => _hash.AppendData(buffer);
        public override void Flush() { }
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _hash.Dispose(); base.Dispose(disposing); }
    }
}

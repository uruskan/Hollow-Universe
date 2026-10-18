using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Hollow.Core;
using Hollow.Surface;

namespace HollowUniverse.Surface;

/// <summary>
/// Borrowed, already admitted visual resources. The package/import owner authenticates their actual streams and
/// materials and outlives every view. This binding is not a file loader, an asset qualification or a GPU allocation.
/// </summary>
internal sealed class FormationVisualTemplate
{
    internal readonly record struct Part(ArrayMesh Mesh, Transform3D LocalTransform);
    internal FormationVisualTemplate(CliffSupportDefinition definition, CliffLodBinding near, CliffLodBinding far,
        IReadOnlyList<Part> nearParts, IReadOnlyList<Part> farParts)
    {
        ArgumentNullException.ThrowIfNull(definition); ArgumentNullException.ThrowIfNull(near); ArgumentNullException.ThrowIfNull(far);
        bool hasNear = false, hasFar = false;
        foreach (CliffLodBinding lod in definition.Lods)
        { hasNear |= ReferenceEquals(lod, near); hasFar |= ReferenceEquals(lod, far); }
        if (!hasNear || !hasFar || ReferenceEquals(near, far))
            throw new FormationValidationException("Visual LODs must belong to the same qualified physical definition.");
        Definition = definition; Near = Copy(nearParts, near); Far = Copy(farParts, far);
    }
    internal CliffSupportDefinition Definition { get; }
    internal IReadOnlyList<Part> Near { get; }
    internal IReadOnlyList<Part> Far { get; }
    internal int InstanceCount => Near.Count + Far.Count;
    private static IReadOnlyList<Part> Copy(IReadOnlyList<Part> parts, CliffLodBinding binding)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Count is < 1 or > 32) throw new FormationCapacityException("A visual LOD needs one to 32 mesh parts.");
        var result = new Part[parts.Count]; long triangles = 0;
        for (int i = 0; i < result.Length; i++)
        {
            Part part = parts[i]; ArrayMesh mesh = part.Mesh;
            if (mesh == null || !GodotObject.IsInstanceValid(mesh) || part.LocalTransform != Transform3D.Identity)
                throw new FormationValidationException("Qualified formation visuals require live meshes with baked identity transforms.");
            if (mesh.GetSurfaceCount() is < 1 or > 32) throw new FormationCapacityException("Formation surface count exceeds admission.");
            for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
            {
                if (mesh.SurfaceGetPrimitiveType(surface) != Mesh.PrimitiveType.Triangles || mesh.SurfaceGetMaterial(surface) == null)
                    throw new FormationValidationException("Formation visuals require material-bound triangle surfaces.");
                int count = mesh.SurfaceGetArrayIndexLen(surface);
                if (count == 0) count = mesh.SurfaceGetArrayLen(surface);
                if (count <= 0 || count % 3 != 0) throw new FormationValidationException("Malformed formation triangle surface.");
                triangles += count / 3;
            }
            result[i] = part;
        }
        if (triangles != binding.Mesh.TriangleCount)
            throw new FormationValidationException("Native LOD triangle count differs from the qualified visual geometry.");
        return Array.AsReadOnly(result);
    }
}

internal sealed record FormationViewLimits
{
    internal required int MaxCohorts { get; init; }
    internal required int MaxPieces { get; init; }
    internal required int MaxMeshInstances { get; init; }
    internal required int MaxQueryNodes { get; init; }
    internal required long MaxQueryBytes { get; init; }
    internal required long MaxEstimatedInstanceBytes { get; init; }
    internal required double Reach { get; init; }
    internal required double NearDistance { get; init; }
    internal required double LodHysteresis { get; init; }
}

/// <summary>
/// Per-view formation presentation. PrepareDesired runs BEFORE terrain UpdateLod; PublishReady runs AFTER it.
/// A complete cohort stays hidden until all its finest terrain instances are effectively visible. Hiding always
/// precedes ticket release. This owner never loads/bakes assets, constructs a world, or substitutes legacy cliffs.
/// </summary>
internal sealed partial class FormationFieldView : Node3D
{
    private sealed class PieceView(FormationPiece piece, Node3D root, Node3D near, Node3D far)
    {
        internal readonly FormationPiece Piece = piece;
        internal readonly Node3D Root = root, Near = near, Far = far;
        internal bool NearActive;
    }
    private sealed class Entry(FormationCohort cohort, TerrainCohortTicket ticket)
    {
        internal readonly FormationCohort Cohort = cohort;
        internal readonly TerrainCohortTicket Ticket = ticket;
        internal readonly List<PieceView> Pieces = new();
        internal readonly List<Node3D> Nodes = new();
        internal Node3D? Root;
        internal int ReservedMeshes;
        internal bool Built;
    }
    internal readonly record struct Diagnostics(int Cohorts, int Pieces, int MeshInstances, int VisibleCohorts,
        int WaitingCohorts, long EstimatedInstanceBytes, long Creations, long Refusals, bool Stopped, string? Failure);
    private readonly Dictionary<FormationId, Entry> _entries = new();
    private readonly Dictionary<FormationGeometry, FormationVisualTemplate> _templates = new();
    private readonly List<FormationCohort> _desired = new();
    private readonly HashSet<FormationId> _wanted = new();
    private FormationField? _field;
    private TerrainLod? _terrain;
    private RenderFrame? _frame;
    private FormationViewLimits? _limits;
    private Vector3D _lastQuery = new(double.MaxValue, 0, 0), _origin;
    private bool _configured, _stopped, _halted, _pendingDesired;
    private int _pieces, _meshes;
    private long _creations, _refusals;
    private string? _failure;
    private const long BytesPerMeshInstance = 4096, BytesPerPiece = 1024, BytesPerCohort = 2048;
    internal Diagnostics Statistics => new(_entries.Count, _pieces, _meshes,
        _entries.Values.Count(e => e.Root?.IsVisibleInTree() == true),
        _entries.Values.Count(e => e.Root?.Visible != true), ReservedBytes(_entries.Count, _pieces, _meshes),
        _creations, _refusals, _stopped, _failure);

    internal void Setup(FormationField field, TerrainLod terrain, RenderFrame frame,
        IReadOnlyList<FormationVisualTemplate> templates, FormationViewLimits limits)
    {
        if (_configured || _stopped) throw new InvalidOperationException("Formation view setup is single-use.");
        ArgumentNullException.ThrowIfNull(field); ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(frame); ArgumentNullException.ThrowIfNull(templates); ArgumentNullException.ThrowIfNull(limits);
        if (limits.MaxCohorts is < 1 or > TerrainLod.MaximumCohortTickets || limits.MaxPieces is < 1 or > 256 ||
            limits.MaxMeshInstances is < 2 or > 1024 || limits.MaxQueryNodes is < 1 or > 16384 ||
            limits.MaxQueryBytes is < 8192 or > 4194304 || limits.MaxEstimatedInstanceBytes is < 1 or > 16777216 ||
            !double.IsFinite(limits.Reach) || limits.Reach <= 0 || limits.Reach > 1000 ||
            !double.IsFinite(limits.NearDistance) || limits.NearDistance <= 0 || limits.NearDistance >= limits.Reach ||
            !double.IsFinite(limits.LodHysteresis) || limits.LodHysteresis < 0 || limits.LodHysteresis >= limits.NearDistance ||
            templates.Count is < 1 or > 64)
            throw new FormationCapacityException("Invalid finite formation-view limits.");
        var admitted = new Dictionary<FormationGeometry, FormationVisualTemplate>();
        foreach (FormationVisualTemplate template in templates)
            if (template == null || !admitted.TryAdd(template.Definition.Geometry, template))
                throw new FormationValidationException("Duplicate or null formation visual template.");
        foreach (FormationPiece piece in field.Pieces)
            if (!admitted.ContainsKey(piece.Geometry)) throw new FormationValidationException("A finalized physical piece has no admitted visual template.");
        foreach (var pair in admitted) _templates.Add(pair.Key, pair.Value);
        _field = field; _terrain = terrain; _frame = frame; _limits = limits with { }; _origin = frame.Origin; _configured = true;
    }

    /// <summary>Complete query, then bounded native preparation. At most one new whole cohort per call.</summary>
    internal void PrepareDesired(Vector3D camera)
    {
        if (!_configured || _stopped || _halted) return;
        CheckCamera(camera);
        try
        {
            // Coalesce movement while creating the finite desired set; refreshing does not cancel valid preparation.
            if (!_pendingDesired && (camera - _lastQuery).LengthSquared >= 16)
            {
                double reach = _limits!.Reach;
                var extent = new Vector3D(reach, reach, reach);
                FormationFieldPieceQuery query = _field!.QueryPieces(new FormationBounds(camera - extent, camera + extent),
                    new FormationFieldQueryLimits { MaxPieces = _limits.MaxPieces, MaxVisitedNodes = _limits.MaxQueryNodes,
                        MaxEstimatedBytes = _limits.MaxQueryBytes });
                if (query.Status != FormationQueryStatus.Complete) { _refusals++; return; }
                var next = new List<FormationCohort>(); var ids = new HashSet<FormationId>();
                int pieces = 0, meshes = 0;
                foreach (FormationPiece piece in query.Pieces)
                {
                    if (DistanceSquared(piece.Bounds, camera) > reach * reach || !ids.Add(piece.Id.Formation)) continue;
                    FormationCohort cohort = _field.Cohort(piece.Id.Formation)
                        ?? throw new FormationValidationException("Finalized piece lacks its terrain cohort.");
                    next.Add(cohort);
                    foreach (FormationPiece member in cohort.Pieces) { pieces++; meshes += _templates[member.Geometry].InstanceCount; }
                }
                if (next.Count > _limits.MaxCohorts || pieces > _limits.MaxPieces || meshes > _limits.MaxMeshInstances ||
                    ReservedBytes(next.Count, pieces, meshes) > _limits.MaxEstimatedInstanceBytes)
                { _refusals++; return; } // No partial desired prefix and no old-view mutation on refusal.
                next.Sort(static (a, b) => a.Id.CompareTo(b.Id));
                int unionCohorts = _entries.Count, unionPieces = _pieces, unionMeshes = _meshes;
                foreach (FormationCohort cohort in next)
                {
                    if (_entries.ContainsKey(cohort.Id)) continue;
                    unionCohorts++; unionPieces += cohort.Pieces.Length;
                    foreach (FormationPiece piece in cohort.Pieces) unionMeshes += _templates[piece.Geometry].InstanceCount;
                }
                if (unionCohorts > _limits.MaxCohorts || unionPieces > _limits.MaxPieces || unionMeshes > _limits.MaxMeshInstances ||
                    ReservedBytes(unionCohorts, unionPieces, unionMeshes) > _limits.MaxEstimatedInstanceBytes)
                { _refusals++; return; }
                var acquired = new List<Entry>();
                foreach (FormationCohort cohort in next)
                {
                    if (_entries.ContainsKey(cohort.Id)) continue;
                    if (_terrain!.TryAcquireCohort(cohort, out TerrainCohortTicket? ticket) != TerrainCohortAcquireStatus.Accepted)
                    {
                        foreach (Entry rollback in acquired) Retire(rollback);
                        _refusals++; return;
                    }
                    var entry = new Entry(cohort, ticket!);
                    foreach (FormationPiece piece in cohort.Pieces) entry.ReservedMeshes += _templates[piece.Geometry].InstanceCount;
                    _entries.Add(cohort.Id, entry); acquired.Add(entry);
                    _pieces += cohort.Pieces.Length; _meshes += entry.ReservedMeshes;
                }
                _wanted.Clear(); foreach (FormationCohort cohort in next) _wanted.Add(cohort.Id);
                _desired.Clear(); _desired.AddRange(next); _lastQuery = camera; _pendingDesired = true;
            }
            foreach (FormationCohort cohort in _desired)
            {
                Entry entry = _entries[cohort.Id];
                if (entry.Built) continue;
                try { Build(entry); }
                catch { Retire(entry); throw; }
                break;
            }
        }
        catch (Exception error) { Halt(error); }
    }

    internal void PublishReady(Vector3D camera)
    {
        if (!_configured || _stopped || _halted) return;
        CheckCamera(camera);
        try
        {
            bool rebased = _frame!.Origin != _origin;
            bool replacementReady = _pendingDesired && _desired.All(c => _entries[c.Id].Built &&
                _terrain!.GetCohortState(_entries[c.Id].Ticket) == TerrainCohortState.Ready);
            if (replacementReady)
            {
                foreach (Entry old in _entries.Values.Where(e => !_wanted.Contains(e.Cohort.Id)).ToArray()) Retire(old);
                _pendingDesired = false;
            }
            foreach (Entry entry in _entries.Values)
            {
                if (entry.Root == null) continue;
                TerrainCohortState state = _terrain!.GetCohortState(entry.Ticket);
                entry.Root.Visible = entry.Built && state == TerrainCohortState.Ready;
                if (state is TerrainCohortState.Failed or TerrainCohortState.Retired)
                    throw new InvalidOperationException("Formation terrain cohort failed or retired before its view.");
                foreach (PieceView piece in entry.Pieces)
                {
                    if (rebased) piece.Root.Transform = ToRenderTransform(piece.Piece.Transform);
                    double boundary = _limits!.NearDistance + (piece.NearActive ? _limits.LodHysteresis : -_limits.LodHysteresis);
                    piece.NearActive = DistanceSquared(piece.Piece.Bounds, camera) < boundary * boundary;
                    piece.Near.Visible = piece.NearActive; piece.Far.Visible = !piece.NearActive;
                }
            }
            _origin = _frame.Origin;
        }
        catch (Exception error) { Halt(error); }
    }

    private void Build(Entry entry)
    {
        entry.Root = new Node3D(); entry.Nodes.Add(entry.Root);
        entry.Root.Name = "SealedFormation"; entry.Root.Visible = false;
        AddChild(entry.Root);
        foreach (FormationPiece piece in entry.Cohort.Pieces)
        {
            Node3D root = Child(entry.Root, "Piece"); root.Transform = ToRenderTransform(piece.Transform);
            Node3D near = Child(root, "Near"), far = Child(root, "Far");
            entry.Pieces.Add(new PieceView(piece, root, near, far));
            FormationVisualTemplate template = _templates[piece.Geometry];
            AddParts(near, template.Near); AddParts(far, template.Far);
        }
        entry.Built = true;
        void AddParts(Node3D parent, IReadOnlyList<FormationVisualTemplate.Part> parts)
        {
            foreach (FormationVisualTemplate.Part part in parts)
            {
                var instance = new MeshInstance3D();
                entry.Nodes.Add(instance);
                instance.Mesh = part.Mesh; instance.Transform = part.LocalTransform; parent.AddChild(instance); _creations++;
            }
        }
        Node3D Child(Node3D parent, string name)
        {
            var child = new Node3D(); entry.Nodes.Add(child);
            child.Name = name; parent.AddChild(child); return child;
        }
    }

    private Transform3D ToRenderTransform(FormationTransform transform) => new(new Basis(_frame!.DirectionToRender(transform.Right),
        _frame.DirectionToRender(transform.Up), _frame.DirectionToRender(transform.Forward)).Scaled(Vector3.One * (float)transform.Scale),
        _frame.ToRender(transform.Position));

    private void Retire(Entry entry)
    {
        // A failed hide or native release keeps the same entry and its accounting for a later Stop retry.
        void Hide() { if (entry.Root != null && GodotObject.IsInstanceValid(entry.Root)) entry.Root.Visible = false; }
        Hide();
        List<Exception>? failures = null;
        for (int i = entry.Nodes.Count - 1; i >= 0; i--)
        {
            Node3D node = entry.Nodes[i];
            try { if (GodotObject.IsInstanceValid(node)) node.Free(); entry.Nodes.RemoveAt(i); }
            catch (Exception error) { (failures ??= new()).Add(error); }
        }
        if (failures != null) throw new AggregateException("Formation native retirement remains incomplete.", failures);
        entry.Root = null; entry.Pieces.Clear();
        _terrain!.ReleaseCohort(entry.Ticket, Hide);
        _entries.Remove(entry.Cohort.Id); _pieces -= entry.Cohort.Pieces.Length; _meshes -= entry.ReservedMeshes;
    }

    private void Halt(Exception error)
    {
        _halted = true; _failure = BoundedError(error.Message);
        foreach (Entry entry in _entries.Values)
            try { if (entry.Root != null && GodotObject.IsInstanceValid(entry.Root)) entry.Root.Visible = false; }
            catch (Exception hide) { _failure = BoundedError(_failure + "; hide: " + hide.Message); }
        GD.PushError("Formation view halted: " + _failure);
    }

    internal void Stop()
    {
        _stopped = true; _desired.Clear(); _wanted.Clear();
        foreach (Entry entry in _entries.Values.ToArray())
            try { Retire(entry); } catch (Exception error) { Halt(error); }
        if (_entries.Count != 0) return;
        _templates.Clear(); _field = null; _terrain = null; _frame = null;
    }
    public override void _ExitTree() => Stop();
    private static string BoundedError(string value) => value.Length <= 2048 ? value : value[..2048];
    private static long ReservedBytes(int cohorts, int pieces, int meshes) =>
        16384 + cohorts * BytesPerCohort + pieces * BytesPerPiece + meshes * BytesPerMeshInstance;
    private static void CheckCamera(Vector3D value)
    {
        if (!double.IsFinite(value.X) || !double.IsFinite(value.Y) || !double.IsFinite(value.Z))
            throw new ArgumentException("Formation camera must be finite.");
    }
    private static double DistanceSquared(FormationBounds bounds, Vector3D point)
    {
        double x = point.X - Math.Clamp(point.X, bounds.Min.X, bounds.Max.X);
        double y = point.Y - Math.Clamp(point.Y, bounds.Min.Y, bounds.Max.Y);
        double z = point.Z - Math.Clamp(point.Z, bounds.Min.Z, bounds.Max.Z);
        return x * x + y * y + z * z;
    }
}

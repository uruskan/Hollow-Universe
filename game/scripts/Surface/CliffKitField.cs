using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hollow.Core;
using Hollow.Surface;

namespace HollowUniverse.Surface;

/// <summary>
/// The cliff kit's pieces drawn (docs/WORLD_TERRAIN_PLAN.md §4.3–4.4, iteration 5): the placements within reach, found on
/// a worker, drawn per 59 m cell as one MultiMesh per piece and level of detail, each level visible over its piece's
/// distances (measured to the cell's middle, so widened by half a cell). The near levels cast shadows; the far ones don't.
/// </summary>
public sealed partial class CliffKitField : Node3D
{
    private const double Reach = 420, RefreshDistance = 30;
    private const float CellSlack = 42;      // a cell's levels switch by its middle: half its diagonal more
    private const float Margin = 4;          // hysteresis at every switch
    private const int MaximumCellsPerFrame = 2;

    private sealed class Model(string file, float[] ends, int shadowLod)
    {
        public readonly string File = file;
        public readonly float[] Ends = ends;
        public readonly int ShadowLod = shadowLod;
        public Mesh[]? Lods;
        public bool Requested, Failed;
    }

    private sealed class Cell(TileKey key, Vector3D anchor, Node3D node, KitPlacement[] pieces)
    {
        public readonly TileKey Key = key;
        public readonly Vector3D Anchor = anchor;
        public readonly Node3D Node = node;
        public readonly KitPlacement[] Pieces = pieces;
    }

    private CliffKitPlacements _source = null!;
    private RenderFrame _frame = null!;
    private Model[] _models = Array.Empty<Model>();
    private readonly Dictionary<TileKey, Cell> _cells = new();
    private readonly List<(TileKey Cell, KitPlacement[] Pieces)> _pending = new();
    private Task<List<(TileKey Cell, KitPlacement[] Pieces)>>? _job;
    private Vector3D _centre = new(double.MaxValue, 0, 0), _lastOrigin;
    private bool _queried, _stopped;

    public Task WorkersStopped { get; private set; } = Task.CompletedTask;
    public string? Failure { get; private set; }
    public bool IsIdle => _queried && _job == null && _pending.Count == 0;
    public int CellCount => _cells.Count;
    public int PieceCount { get; private set; }

    public void Setup(CliffKitPlacements source, RenderFrame frame)
    {
        _source = source;
        _frame = frame;
        _lastOrigin = frame.Origin;
        IReadOnlyList<CliffPiece> pieces = source.Kit.Pieces;
        _models = new Model[pieces.Count];
        for (int i = 0; i < pieces.Count; i++)
        {
            CliffPiece p = pieces[i];
            // The kit's distances: where each level ends (the last 0: never, but the field's reach).
            var ends = new float[p.LodDistances.Count];
            for (int l = 0; l < ends.Length; l++)
            {
                ends[l] = p.LodDistances[l] > 0 ? (float)p.LodDistances[l] + CellSlack : 0;
            }
            _models[i] = new Model(p.File, ends, p.ShadowLod);
        }
    }

    public void Follow(Vector3D camera)
    {
        if (_stopped || Failure != null)
        {
            return;
        }
        if (_job is { IsCompleted: true })
        {
            if (_job.IsCompletedSuccessfully)
            {
                Apply(_job.Result, camera);
                _queried = true;
            }
            else
            {
                Fail("Cliff kit placement failed: " + _job.Exception);
            }
            _job = null;
        }
        if (_job == null && Vector3D.Distance(camera, _centre) > RefreshDistance)
        {
            _centre = camera;
            CliffKitPlacements source = _source;
            _job = Task.Run(() => source.Near(camera, Reach));
        }
        if (_frame.Origin != _lastOrigin)
        {
            Vector3 planet = _frame.ToRender(Vector3D.Zero);
            foreach (Cell cell in _cells.Values)
            {
                cell.Node.Position = _frame.ToRender(cell.Anchor);
                foreach (Node child in cell.Node.GetChildren())
                {
                    if (child is GeometryInstance3D geometry)
                    {
                        geometry.SetInstanceShaderParameter("planet_centre", planet);
                    }
                }
            }
            _lastOrigin = _frame.Origin;
        }
        foreach (Model model in _models)
        {
            if (model.Requested && model.Lods == null && !model.Failed)
            {
                Poll(model);
            }
        }
        Build();
    }

    private void Apply(List<(TileKey Cell, KitPlacement[] Pieces)> desired, Vector3D camera)
    {
        var keep = new HashSet<TileKey>(desired.Select(d => d.Cell));
        foreach (TileKey key in _cells.Keys.Where(k => !keep.Contains(k)).ToArray())
        {
            Cell cell = _cells[key];
            PieceCount -= cell.Pieces.Length;
            cell.Node.QueueFree();
            _cells.Remove(key);
        }
        _pending.Clear();
        foreach ((TileKey key, KitPlacement[] pieces) in desired.OrderBy(d => (d.Pieces[0].Position - camera).LengthSquared))
        {
            if (_cells.ContainsKey(key))
            {
                continue;
            }
            foreach (KitPlacement k in pieces)
            {
                Request(_models[k.Piece]);
            }
            _pending.Add((key, pieces));
        }
    }

    // Builds the nearest waiting cells whose models are loaded, a couple a frame.
    private void Build()
    {
        int built = 0;
        for (int n = 0; n < _pending.Count && built < MaximumCellsPerFrame; n++)
        {
            (TileKey key, KitPlacement[] pieces) = _pending[n];
            bool ready = true;
            foreach (KitPlacement k in pieces)
            {
                Model model = _models[k.Piece];
                if (model.Lods == null && !model.Failed)
                {
                    ready = false;
                    break;
                }
            }
            if (!ready)
            {
                continue;
            }
            _pending.RemoveAt(n--);
            _cells[key] = Create(key, pieces);
            PieceCount += pieces.Length;
            built++;
        }
    }

    private Cell Create(TileKey key, KitPlacement[] pieces)
    {
        Vector3D anchor = pieces[0].Position;
        var node = new Node3D { Name = $"Cell{key.Index}", Position = _frame.ToRender(anchor) };
        AddChild(node);
        Vector3 planet = _frame.ToRender(Vector3D.Zero);
        foreach (IGrouping<int, KitPlacement> group in pieces.GroupBy(k => k.Piece))
        {
            Model model = _models[group.Key];
            if (model.Lods is not { } lods)
            {
                continue;
            }
            KitPlacement[] instances = group.ToArray();
            for (int lod = 0; lod < lods.Length; lod++)
            {
                var multimesh = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                    Mesh = lods[lod],
                    InstanceCount = instances.Length,
                };
                for (int i = 0; i < instances.Length; i++)
                {
                    KitPlacement k = instances[i];
                    float s = (float)k.Scale;
                    var basis = new Basis(_frame.DirectionToRender(k.AxisX) * s, _frame.DirectionToRender(k.AxisY) * s,
                        _frame.DirectionToRender(k.AxisZ) * s);
                    multimesh.SetInstanceTransform(i, new Transform3D(basis, _frame.DirectionToRender(k.Position - anchor)));
                }
                float begin = lod == 0 ? 0 : model.Ends[lod - 1];
                float end = lod < model.Ends.Length ? model.Ends[lod] : 0;
                var instance = new MultiMeshInstance3D
                {
                    Multimesh = multimesh,
                    VisibilityRangeBegin = begin,
                    VisibilityRangeBeginMargin = begin > 0 ? Margin : 0,
                    VisibilityRangeEnd = end > 0 ? end : (float)Reach + CellSlack,
                    VisibilityRangeEndMargin = Margin,
                    CastShadow = lod <= model.ShadowLod
                        ? GeometryInstance3D.ShadowCastingSetting.On
                        : GeometryInstance3D.ShadowCastingSetting.Off,
                };
                node.AddChild(instance);
                instance.SetInstanceShaderParameter("planet_centre", planet);
            }
        }
        return new Cell(key, anchor, node, pieces);
    }

    private void Request(Model model)
    {
        if (model.Requested || model.Failed)
        {
            return;
        }
        model.Requested = true;
        if (ResourceLoader.LoadThreadedRequest(model.File, "PackedScene") != Error.Ok)
        {
            model.Failed = true;
            GD.PushWarning($"CliffKitField: cannot load {model.File}");
        }
    }

    // Once a piece's scene has loaded, its levels' meshes by their nodes' names (<key>_lod0, _lod1, ...).
    private void Poll(Model model)
    {
        ResourceLoader.ThreadLoadStatus status = ResourceLoader.LoadThreadedGetStatus(model.File);
        if (status is ResourceLoader.ThreadLoadStatus.Failed or ResourceLoader.ThreadLoadStatus.InvalidResource)
        {
            model.Failed = true;
            GD.PushWarning($"CliffKitField: failed to load {model.File} ({status})");
            return;
        }
        if (status != ResourceLoader.ThreadLoadStatus.Loaded)
        {
            return;
        }
        var scene = (PackedScene)ResourceLoader.LoadThreadedGet(model.File);
        Node root = scene.Instantiate();
        var lods = new SortedDictionary<int, Mesh>();
        Collect(root, lods);
        root.Free();
        if (lods.Count == 0)
        {
            model.Failed = true;
            GD.PushWarning($"CliffKitField: {model.File} has no _lodN meshes");
            return;
        }
        model.Lods = lods.Values.ToArray();
    }

    private static void Collect(Node node, SortedDictionary<int, Mesh> lods)
    {
        if (node is MeshInstance3D { Mesh: { } mesh } instance)
        {
            string name = instance.Name;
            int at = name.LastIndexOf("_lod", StringComparison.Ordinal);
            if (at >= 0 && int.TryParse(name.AsSpan(at + 4), out int level))
            {
                lods[level] = mesh;
            }
        }
        foreach (Node child in node.GetChildren())
        {
            Collect(child, lods);
        }
    }

    private void Fail(string message)
    {
        Failure ??= message;
        GD.PushError(message);
    }

    public void Stop()
    {
        if (_stopped)
        {
            return;
        }
        _stopped = true;
        Visible = false;
        _pending.Clear();
        if (_job is { } job)
        {
            _ = job.ContinueWith(failed => { _ = failed.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            WorkersStopped = job;
            _job = null;
        }
        _source = null!;
        _frame = null!;
    }

    public override void _ExitTree() => Stop();
}

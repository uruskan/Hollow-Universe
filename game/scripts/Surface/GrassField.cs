using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using Hollow.Core;
using Hollow.Surface;

namespace HollowUniverse.Surface;

/// <summary>
/// The ground's grass (GROUND.md §4.6, docs/WALKING_GROUND.md §18): presentation only, not part of the scatter. Clumps of
/// five species and the forest floor's litter (art/grass/build_grass.py) grow on placement cells of half a terrain cell
/// (0.46 m on Earth), each cell's plan coming from the one habitat rule (<see cref="GroundCoverHabitats"/>) and a hash of
/// its place, so nothing shifts as the camera moves and every cell belongs to one cube face. Cells are gathered into
/// tiles of the finest terrain tiles (<see cref="TileCells"/> terrain cells a side): a worker builds a tile's instances
/// from the very grid the terrain draws there (roots on its triangles, the grid's climate and materials), the main thread
/// uploads them into pooled MultiMeshes, one a species, a few tiles a frame, and only tiles in view are submitted, each
/// at the level of detail its distance asks for. Scanned plants stand among them as accents (GrassField.Authored.cs).
/// </summary>
public sealed partial class GrassField : Node3D
{
    /// <summary>Placement cells are half a terrain cell: the cube faces' cells at the level nearest this (metres).</summary>
    public const double CellMetres = 0.46;

    /// <summary>Clumps reach this far at the graphics settings' grass distance 1 (GROUND.md §8.6).</summary>
    public const float FarRadius = 42;
    private const float LitterRadius = 26;

    /// <summary>Terrain cells along a grass tile's edge (the finest terrain tiles have 32): 16 (about 15 m on Earth) or 8.</summary>
    public int TileCells { get; private set; } = 16;

    // Movement before the wanted tiles are worked out again, the margins tiles are wanted and kept within, and the work
    // a frame may spend on uploads.
    private const double Reselect = 1.5;
    private const double WantMargin = 3, KeepMargin = 8;
    private const int UploadTileLimit = 6;
    private const long UploadByteTarget = 768 * 1024;
    private const double UploadMilliseconds = 1.0;

    private float _far = FarRadius, _litterFar = LitterRadius;

    /// <summary>The grass distance and density this field was built with.</summary>
    public (float Distance, float Density) BuiltWith { get; private set; }

    private ISurface _surface = null!;
    private RenderFrame _frame = null!;
    private TerrainLod? _terrain;
    private int _terrainLevel;    // the finest terrain level, whose tiles the grass tiles split
    private int _tileShift;       // grass tiles a terrain tile's edge: 1 << _tileShift
    private int _level;           // the placement cells' level
    private BuildContext? _context;
    private readonly Dictionary<TileKey, GrassTile> _tiles = new();
    private readonly List<GrassTile> _uploadQueue = new();
    private readonly List<TileKey> _around = new();
    private readonly HashSet<TileKey> _wanted = new();
    private readonly List<GrassTile> _scratch = new();
    private Vector3D _selectedAt = new(double.MaxValue, 0, 0);
    private bool _near, _show;
    private int _instances;
    private long _uploadedBytes;
    private double _lastUploadMilliseconds;
    private Vector2 _morphRange = new(1e6f, 2e6f);
    private int _frames;
    private Vector3D _origin;

    // One tile: the cells of a block of one finest terrain tile, built from the grid that terrain tile is drawn with.
    private sealed class GrassTile
    {
        public TileKey Key, Terrain;
        public Vector3D Centre;              // body frame, the tile's middle on the ground (until built: its estimate)
        public Vector3D Minimum, Maximum;    // its instances' bounds less Centre, body axes (until built: an estimate)
        public double Distance;
        public int Version;
        public TileJob? Job;
        public TileBuild? Built, Pending;
        public readonly List<Batch> Batches = new();
        public bool Wanted;
    }

    private sealed class TileJob
    {
        public required TileKey Key, Terrain;
        public required TileGrid? Grid;
        public required int Version;
        public System.Threading.Tasks.Task<TileBuild> Task = null!;
    }

    /// <summary>Clumps submitted last frame (before the renderer's culling; litter and accents included).</summary>
    public int Tufts => _instances + _coverCount;

    /// <summary>True when the grass around the camera is in place (or the camera is too high for grass).</summary>
    public bool IsIdle => _setupComplete && !_stopped && !_requiredJobFailed && (!_near || Settled());

    /// <summary>The cube-sphere level of the placement cells.</summary>
    public int Level => _level;

    /// <summary>Where the terrain's grass layer turns from its scan's colour to the blades' (the terrain shader's
    /// grass_reach): from half the clumps' reach to where their last ones dissolve.</summary>
    public Vector2 TerrainHandoff => new(0.5f * _far, 0.95f * _far);

    /// <summary>
    /// Sets the field up for a surface: its species, materials and pools, and the reach of the graphics settings' grass
    /// distance and density (the surface view builds a new field when they change). <paramref name="terrain"/> is the
    /// drawn terrain the clumps root on; <paramref name="noise"/> the surface's macro noise (the grass colour's patches).
    /// </summary>
    public void Setup(ISurface surface, RenderFrame frame, TerrainLod terrain, Texture2D noise, Vector3 bladeMean)
    {
        BeginSetup();
        // Render diagnosis only: retain loading, generation and readiness while exposing the identical terrain.
        Visible = Array.IndexOf(OS.GetCmdlineUserArgs(), "--hide-ground-cover") < 0;
        _surface = surface;
        _frame = frame;
        _terrain = terrain;
        HollowUniverse.Settings.GraphicsValues graphics = HollowUniverse.Settings.GraphicsSettings.Current;
        BuiltWith = (graphics.GrassDistance, graphics.GrassDensity);
        float reach = Math.Clamp(graphics.GrassDistance, 0.5f, 2f);
        _far = FarRadius * reach;
        _litterFar = LitterRadius * reach;
        string[] args = OS.GetCmdlineUserArgs();
        int cells = Array.IndexOf(args, "--grass-tile-cells");
        if (cells >= 0)
        {
            if (cells + 1 >= args.Length || !int.TryParse(args[cells + 1], out int value) || value is not (8 or 16))
                throw new ArgumentException("--grass-tile-cells needs 8 or 16.");
            TileCells = value;
        }
        Configure(terrain.MaxLevel);
        using Image image = noise.GetImage();
        SpeciesCatalog catalog = LoadCatalog(bladeMean, noise);
        _context = CaptureContext(surface, catalog, new TintNoise(image), Math.Clamp(graphics.GrassDensity, 0.1f, 1f));
        SetupAuthoredCover();
        _setupComplete = true;
    }

    private void Configure(int terrainLevel)
    {
        _terrainLevel = terrainLevel;
        _tileShift = TileCells == 16 ? 1 : 2;
        _level = terrainLevel + TerrainCellsLog2 + 1;
    }

    // ---------------------------------------------------------------- update

    /// <summary>
    /// Follows the camera (body-frame position, <paramref name="altitude"/> metres above the ground): works out the tiles
    /// within the grass's reach, starts their builds, uploads finished ones and picks each tile's level of detail.
    /// </summary>
    public void UpdateGrass(Vector3D camera, double altitude, bool walking)
    {
        if (!_setupComplete || _stopped) return;
        _frames++;
        _near = altitude < _far;
        CollectJobs();
        Vector3D direction = camera.Normalized;
        Vector3D ground = direction * (_surface.Radius + _surface.HeightAt(direction));
        if (_near && (Vector3D.Distance(ground, _selectedAt) > Reselect || _frames % 30 == 0)) SelectTiles(camera, ground);
        if (!_near && _tiles.Count > 0 && _frames % 30 == 0) SelectTiles(camera, ground);
        if (_near && !_requiredJobFailed) StartJobs(camera);
        ContinueUploads();
        _show = _near;
        UpdateVisibility(camera, walking);
        UpdateAuthoredCover(camera, _show);
    }

    // The tiles within the reach of the camera: every block of every finest terrain tile around it that may hold a clump
    // within the reach. Tiles beyond the reach and a margin are released. Both decisions use one estimate, the tile at
    // the camera's ground height (a tile up or down a slope is never nearer than that says, so none in reach is missed);
    // its built bounds are only for drawing, or a tile could be wanted by the one and released by the other in turn.
    private void SelectTiles(Vector3D camera, Vector3D ground)
    {
        _selectedAt = ground;
        _wanted.Clear();
        _around.Clear();
        double half = TileKey.EdgeLength(_terrainLevel + _tileShift, _surface.Radius) * 0.75;
        if (_near)
        {
            TileKey.Around(ground.Normalized, _far + WantMargin + half, _surface.Radius, _terrainLevel, _around);
            int split = 1 << _tileShift;
            foreach (TileKey terrain in _around)
            {
                for (int j = 0; j < split; j++)
                for (int i = 0; i < split; i++)
                {
                    var key = new TileKey(terrain.Face, _terrainLevel + _tileShift, terrain.X * split + i, terrain.Y * split + j);
                    if (Estimate(camera, ground, key, half) > _far + WantMargin) continue;
                    _wanted.Add(key);
                    if (_tiles.ContainsKey(key)) continue;
                    _tiles.Add(key, new GrassTile
                    {
                        Key = key, Terrain = terrain, Centre = key.Centre * ground.Length,
                        Minimum = new Vector3D(-half, -half, -half), Maximum = new Vector3D(half, half, half),
                    });
                }
            }
        }
        _scratch.Clear();
        foreach (GrassTile tile in _tiles.Values)
        {
            tile.Wanted = _wanted.Contains(tile.Key);
            tile.Distance = DistanceToTile(camera, tile);
            if (!tile.Wanted && (!_near || Estimate(camera, ground, tile.Key, half) > _far + KeepMargin)) _scratch.Add(tile);
        }
        foreach (GrassTile tile in _scratch) ReleaseTile(tile);
    }

    // The nearest a tile's clumps can be: its middle at the camera's ground height, less its half-diagonal.
    private static double Estimate(Vector3D camera, Vector3D ground, TileKey key, double half) =>
        Vector3D.Distance(camera, key.Centre * ground.Length) - half * 1.74;

    private static double DistanceToTile(Vector3D camera, GrassTile tile)
    {
        Vector3D p = camera - tile.Centre;
        double x = Math.Max(Math.Max(tile.Minimum.X - p.X, p.X - tile.Maximum.X), 0);
        double y = Math.Max(Math.Max(tile.Minimum.Y - p.Y, p.Y - tile.Maximum.Y), 0);
        double z = Math.Max(Math.Max(tile.Minimum.Z - p.Z, p.Z - tile.Maximum.Z), 0);
        return Math.Sqrt(x * x + y * y + z * z);
    }

    // Wanted tiles that need a build, nearest first, while workers are free: each from its terrain tile's drawn grid
    // (waiting while the terrain still streams it), or, with no terrain (the lifetime probe), from the surface itself.
    private void StartJobs(Vector3D camera)
    {
        int running = 0;
        foreach (GrassTile tile in _tiles.Values) if (tile.Job != null) running++;
        int limit = _instances == 0 ? MaxJobsLoading : MaxJobs;
        if (running >= limit) return;
        _scratch.Clear();
        foreach (GrassTile tile in _tiles.Values)
        {
            if (!tile.Wanted || tile.Job != null || tile.Pending != null) continue;
            if (tile.Built != null && !GridChanged(tile)) continue;
            tile.Distance = DistanceToTile(camera, tile);
            _scratch.Add(tile);
        }
        _scratch.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
        foreach (GrassTile tile in _scratch)
        {
            if (running >= limit) break;
            TileGrid? grid = null;
            if (_terrain != null && !_terrain.TryDrawnGrid(tile.Terrain, out grid, out _)) continue;
            var job = new TileJob { Key = tile.Key, Terrain = tile.Terrain, Grid = grid, Version = tile.Version };
            job.Task = _workers.Start(_context!, job);
            tile.Job = job;
            running++;
        }
    }

    private bool GridChanged(GrassTile tile) =>
        _terrain != null && tile.Built != null && _terrain.TryDrawnGrid(tile.Terrain, out TileGrid grid, out _)
        && !ReferenceEquals(grid, tile.Built.Grid);

    // Finished builds: a current one waits for upload; a stale one (its tile released or rebuilt since) is dropped.
    private void CollectJobs()
    {
        _scratch.Clear();
        foreach (GrassTile tile in _tiles.Values) if (tile.Job is { Task.IsCompleted: true }) _scratch.Add(tile);
        foreach (GrassTile tile in _scratch)
        {
            TileJob job = tile.Job!;
            tile.Job = null;
            if (job.Version != tile.Version || _stopped)
            {
                _workers.Monitor.Discarded();
                continue;
            }
            if (job.Task.IsCompletedSuccessfully)
            {
                tile.Pending = job.Task.Result;
                _uploadQueue.Add(tile);
            }
            else
            {
                _requiredJobFailed = true;
                if (job.Task.IsFaulted) GD.PushError($"Grass tile {tile.Key} failed: {job.Task.Exception}");
            }
        }
    }

    // Uploads finished tiles, nearest first, a few a frame (bounded by count, bytes and time): each species' instances
    // into a pooled MultiMesh, which replaces the tile's previous batches only once all of them are in.
    private void ContinueUploads()
    {
        if (_uploadQueue.Count == 0) return;
        _uploadQueue.RemoveAll(t => t.Pending == null || !_tiles.ContainsKey(t.Key));
        _uploadQueue.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
        long start = Stopwatch.GetTimestamp(), bytes = 0;
        int done = 0;
        while (_uploadQueue.Count > 0 && done < UploadTileLimit)
        {
            GrassTile tile = _uploadQueue[0];
            TileBuild build = tile.Pending!;
            if (done > 0 && (bytes + build.Bytes > UploadByteTarget || Stopwatch.GetElapsedTime(start).TotalMilliseconds > UploadMilliseconds))
                break;
            _uploadQueue.RemoveAt(0);
            Publish(tile, build);
            bytes += build.Bytes;
            done++;
        }
        _uploadedBytes += bytes;
        _lastUploadMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private void Publish(GrassTile tile, TileBuild build)
    {
        var batches = new List<Batch>();
        try
        {
            for (int s = 0; s < build.Species.Length; s++)
            {
                SpeciesBuild data = build.Species[s];
                if (data.Count == 0) continue;
                Batch batch = AcquireBatch(s, data.Capacity);
                batch.MultiMesh.Buffer = data.Buffer;
                batch.Count = data.Count;
                batch.Ranks = data.Ranks;
                batch.MultiMesh.CustomAabb = new Aabb(V(data.Minimum), V(data.Maximum - data.Minimum));
                batch.Instance.Transform = _frame.Anchor(build.Centre);
                batch.Lod = -1;
                batch.Shown = -1;
                batches.Add(batch);
            }
        }
        catch
        {
            foreach (Batch batch in batches) ReleaseBatch(batch);
            throw;
        }
        foreach (Batch old in tile.Batches) ReleaseBatch(old);
        tile.Batches.Clear();
        tile.Batches.AddRange(batches);
        tile.Built = build;
        tile.Pending = null;
        tile.Centre = build.Centre;
        tile.Minimum = build.Minimum;
        tile.Maximum = build.Maximum;
        _coverDirty = true;
        WriteAppearanceDiagnostic(build);
    }

    private void ReleaseTile(GrassTile tile)
    {
        tile.Version++;
        foreach (Batch batch in tile.Batches) ReleaseBatch(batch);
        tile.Batches.Clear();
        tile.Built = null;
        tile.Pending = null;
        // A running job finishes on its own; its stale result is discarded when collected.
        if (tile.Job != null)
        {
            _orphans.Add(tile.Job);
            tile.Job = null;
        }
        _tiles.Remove(tile.Key);
        _coverDirty = true;
    }

    // Each frame: the tiles in reach shown at their level of detail, the clumps they can keep submitted (their buffers are
    // sorted by rank, so the clumps a distance keeps are a prefix), the rest hidden.
    private void UpdateVisibility(Vector3D camera, bool walking)
    {
        DrainOrphans();
        // A frame whose origin follows the camera (the look-dev view's planet) moves every batch with it.
        if (_frame.Origin != _origin)
        {
            _origin = _frame.Origin;
            Reanchor();
        }
        bool grass = (_diagnosticLayers & GroundCoverLayers.Grass) != 0, litter = (_diagnosticLayers & GroundCoverLayers.Litter) != 0;
        if (_frames % 60 == 1) RefreshMorphRange();
        Vector3 cameraRender = _frame.ToRender(camera);
        Vector3 feet = cameraRender - _frame.DirectionToRender(camera.Normalized) * 1.7f;
        (Vector3 up, Vector3 east, Vector3 north) = _frame.LocalAxes(camera);
        _ = up;
        UpdateMaterials(walking ? new Vector4(feet.X, feet.Y, feet.Z, 0.9f) : Vector4.Zero, east, north);
        int instances = 0;
        foreach (GrassTile tile in _tiles.Values)
        {
            if (tile.Batches.Count == 0) continue;
            double d = DistanceToTile(camera, tile);
            tile.Distance = d;
            bool terrainDrawn = _terrain == null || _terrain.TryDrawnGrid(tile.Terrain, out _, out _);
            foreach (Batch batch in tile.Batches)
            {
                Species species = _species[batch.Species];
                bool layer = species.Litter ? litter : grass;
                float reach = species.Litter ? _litterFar : _far;
                bool show = _show && terrainDrawn && layer && d < reach;
                int count = 0;
                if (show)
                {
                    int lod = species.LodFor(d, batch.Lod);
                    if (lod != batch.Lod)
                    {
                        batch.Lod = lod;
                        batch.MultiMesh.Mesh = species.Meshes[lod];
                        batch.Instance.MaterialOverride = species.Materials[lod];
                    }
                    count = batch.Prefix(species.Kept((float)d));
                    if (count == 0) show = false;
                }
                if (show && count != batch.Visible)
                {
                    batch.MultiMesh.VisibleInstanceCount = count;
                    batch.Visible = count;
                }
                int shown = show ? 1 : 0;
                if (shown != batch.Shown)
                {
                    batch.Instance.Visible = show;
                    batch.Shown = shown;
                }
                if (show) instances += count;
            }
        }
        _instances = instances;
    }

    private void RefreshMorphRange()
    {
        if (_terrain == null) return;
        foreach (GrassTile tile in _tiles.Values)
        {
            if (!_terrain.TryDrawnGrid(tile.Terrain, out _, out Vector2 range)) continue;
            if (range != _morphRange)
            {
                _morphRange = range;
                foreach (Species species in _species)
                    foreach (ShaderMaterial material in species.Materials) material.SetShaderParameter("morph_range", range);
            }
            return;
        }
    }

    // Whether every wanted tile within the reach is built and uploaded. A tile whose terrain tile is not drawn at the
    // finest level waits for it while the terrain streams; once the terrain is idle without it (beyond the finest
    // level's reach), nothing will root there.
    private bool Settled()
    {
        if (_uploadQueue.Count > 0) return false;
        foreach (GrassTile tile in _tiles.Values)
        {
            if (!tile.Wanted) continue;
            if (tile.Job != null || tile.Pending != null) return false;
            if (tile.Built != null) continue;
            if (_terrain != null && _terrain.IsIdle && !_terrain.TryDrawnGrid(tile.Terrain, out _, out _)) continue;
            return false;
        }
        return true;
    }

    private static Vector3 V(Vector3D p) => new((float)p.X, (float)p.Y, (float)p.Z);

    /// <summary>Nothing to re-anchor: every tile's batches are placed from the frame each upload, and the world frame
    /// never moves.</summary>
    public void Reanchor()
    {
        foreach (GrassTile tile in _tiles.Values)
            foreach (Batch batch in tile.Batches) batch.Instance.Transform = _frame.Anchor(tile.Centre);
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using Hollow.Core;
using Hollow.Surface;

namespace HollowUniverse.Surface;

/// <summary>
/// Presentation-only copies of the terrain triangles actually drawn. Each slot retains at most one published
/// grid. Missing/stale slots are absent from the lookup, never replaced by independently sampled heightfield data.
/// A frame uploads at most two new grids; existing slots remain usable while replacement tiles are admitted.
/// All calls and native ownership stay on the scene thread. No worker/world lease is retained here.
/// </summary>
internal sealed partial class TerrainContactAtlas : IDisposable
{
    internal enum FixtureFault { None, AfterFineLayer, AfterMetadataBeforeLookup, DuringRetirement,
        AfterAppearanceMaterials, DuringAppearanceRetirement, AppearanceConstructionAndRetirement }
    internal const int Capacity = 128, GridSide = 33, MaximumMapLevel = 9, UploadsPerFrame = 2;
    private readonly int _mapLevel, _mapSide;
    private readonly List<DrawnTerrainTile> _requested = new(Capacity);
    private readonly Dictionary<TileKey, int> _slots = new();
    private readonly DrawnTerrainTile?[] _tiles = new DrawnTerrainTile?[Capacity];
    private readonly int[] _generations = new int[Capacity];
    private readonly HashSet<TileKey> _wanted = new();
    private readonly float[] _lookup, _metadata = new float[5 * Capacity * 4];
    private readonly byte[] _gridBytes = new byte[GridSide * GridSide * 4 * sizeof(float)];
    private readonly float[] _gridFloats = new float[GridSide * GridSide * 4];
    private readonly Texture2DArray _fine = null!, _morph = null!;
    private readonly ImageTexture _map = null!, _meta = null!;
    private bool _disposed, _cleanupComplete;
    private string? _fault;
    private FixtureFault _fixtureFault;
    internal bool Ready { get; private set; }
    internal int ResidentTiles => _slots.Count;
    internal int PendingTiles { get; private set; }
    internal long UploadedBytes { get; private set; }
    internal long Version { get; private set; }
    internal string? Failure { get; private set; }
    internal bool PublicationInvalidationFailed { get; private set; }
    internal bool CleanupComplete => _cleanupComplete;
    internal long EstimatedPayloadBytes => 2L * _gridBytes.Length * Capacity + (long)(_lookup.Length + _metadata.Length) * sizeof(float)
        + AppearancePayloadBytes;

    internal TerrainContactAtlas(int mapLevel, bool appearance = false, FixtureFault constructionFault = FixtureFault.None)
    {
        if (mapLevel < 0 || mapLevel > MaximumMapLevel)
            throw new ArgumentOutOfRangeException(nameof(mapLevel), "Drawn contact map supports terrain levels zero through nine.");
        _mapLevel = mapLevel;
        _mapSide = 1 << mapLevel;
        _lookup = new float[checked(_mapSide * _mapSide * 6)];
        _appearanceEnabled = appearance;
        _fixtureFault = constructionFault;
        try
        {
            _fine = new Texture2DArray(); _morph = new Texture2DArray();
            using Image zero = Image.CreateFromData(GridSide, GridSide, false, Image.Format.Rgbaf, _gridBytes);
            var images = new Godot.Collections.Array<Image>();
            using var imageStorage = (Godot.Collections.Array)images;
            for (int i = 0; i < Capacity; i++) images.Add(zero);
            if (_fine.CreateFromImages(images) != Error.Ok || _morph.CreateFromImages(images) != Error.Ok)
                throw new InvalidOperationException("Cannot allocate bounded drawn-terrain contact arrays.");
            _map = Texture(_lookup, _mapSide, _mapSide * 6, Image.Format.Rf);
            _meta = Texture(_metadata, 5, Capacity, Image.Format.Rgbaf);
            CreateAppearance();
            if (_fixtureFault == FixtureFault.AppearanceConstructionAndRetirement)
                throw new InvalidOperationException("Expected appearance construction fixture failure after native allocation.");
        }
        catch (Exception error)
        {
            try { Dispose(); }
            catch (Exception cleanup) { throw new ConstructionCleanupException(this, error, cleanup); }
            throw;
        }
    }

    internal void Bind(ShaderMaterial material)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        material.SetShaderParameter("terrain_contact_fine", _fine);
        material.SetShaderParameter("terrain_contact_morph", _morph);
        material.SetShaderParameter("terrain_contact_lookup", _map);
        material.SetShaderParameter("terrain_contact_meta", _meta);
        material.SetShaderParameter("terrain_contact_map_side", _mapSide);
        material.SetShaderParameter("terrain_contact_enabled", true);
    }

    // Main-thread snapshot of exactly the completed native publication, not TerrainLod's newer requested grids.
    // A caller must retain this atlas object's identity AND the returned version while using the copies. The
    // caller owns the bounded list/temporary grid references; the atlas retains no additional snapshot history.
    internal bool TryCopyPublishedTiles(List<DrawnTerrainTile> destination, out long version, int maximum = Capacity)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear(); version = Version;
        if (maximum < 1 || maximum > Capacity) throw new ArgumentOutOfRangeException(nameof(maximum));
        if (_disposed || !Ready || PublicationInvalidationFailed) return false;
        foreach (DrawnTerrainTile? value in _tiles)
        {
            if (value is not { } tile) continue;
            if (destination.Count == maximum) { destination.Clear(); return false; }
            destination.Add(tile);
        }
        destination.Sort(static (a, b) => a.Key.Packed.CompareTo(b.Key.Packed));
        return true;
    }

    internal void Update(TerrainLod terrain, Vector3D camera, double reach)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_fault != null) { Failure = _fault; Ready = false; return; }
        Ready = false; Failure = null;
        if (!terrain.CopyContactTiles(camera, reach, _requested, Capacity))
        {
            RefuseSnapshot("Drawn terrain contact selection exceeds its supported grid/count limits."); return;
        }
        UpdateSnapshot(terrain.ContactRevision);
    }

    // Same validated native upload/publication core as the live TerrainLod path; no alternate packing implementation.
    internal void UpdateFixture(IReadOnlyList<DrawnTerrainTile> snapshots, long revision)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(snapshots);
        if (_fault != null) { Failure = _fault; Ready = false; return; }
        Ready = false; Failure = null; _requested.Clear();
        if (snapshots.Count > Capacity) { RefuseSnapshot("Drawn terrain contact selection exceeds 128 tiles."); return; }
        foreach (DrawnTerrainTile tile in snapshots) _requested.Add(tile);
        UpdateSnapshot(revision);
    }

    private void UpdateSnapshot(long revision)
    {
        // Revision is deliberately not a publication trigger: a distant terrain change may leave this entire
        // contact selection unchanged. Grid identity, membership, transform and stored morph range are compared.
        _ = revision;
        _wanted.Clear();
        foreach (DrawnTerrainTile tile in _requested)
        {
            if (!ValidSnapshot(tile) || !_wanted.Add(tile.Key))
            { RefuseSnapshot("Invalid or duplicate drawn terrain contact snapshot."); return; }
            if (_appearanceEnabled && !ValidAppearanceTransform(tile.Transform.Basis))
            { RefuseSnapshot("Drawn terrain appearance requires a finite proper rigid render frame."); return; }
            // Preflight every requested new stream before ANY native write, including the fine position layer.
            // Immutable retained grids were already checked; changed transform metadata is still checked above.
            if (_appearanceEnabled && (!_slots.TryGetValue(tile.Key, out int existing)
                || !ReferenceEquals(_tiles[existing]!.Value.Grid, tile.Grid)) && !ValidAppearanceGrid(tile.Grid))
            { RefuseSnapshot("Invalid drawn terrain appearance streams."); return; }
        }
        foreach (DrawnTerrainTile tile in _requested)
        {
            TileKey ancestor = tile.Key;
            while (ancestor.Level > 0)
            {
                ancestor = ancestor.Parent;
                if (_wanted.Contains(ancestor)) { RefuseSnapshot("Overlapping drawn terrain contact ancestors."); return; }
            }
        }
        try { AdmitSnapshot(); }
        catch (Exception error) { FailUpload(error); }
    }

    private bool ValidSnapshot(DrawnTerrainTile tile)
    {
        const int components = GridSide * GridSide * 3;
        return tile.Key.Level <= _mapLevel && tile.Grid != null && tile.Grid.Key.Equals(tile.Key)
            && tile.Grid.Cells == GridSide - 1 && tile.Grid.Positions.Length == components && tile.Grid.MorphPositions.Length == components
            && Finite(tile.Transform.Origin) && Finite(tile.Transform.Basis.X) && Finite(tile.Transform.Basis.Y) && Finite(tile.Transform.Basis.Z)
            && Finite(tile.Bounds.Position) && Finite(tile.Bounds.End) && tile.Bounds.Size.X >= 0 && tile.Bounds.Size.Y >= 0 && tile.Bounds.Size.Z >= 0
            && float.IsFinite(tile.MorphRange.X) && float.IsFinite(tile.MorphRange.Y);
    }

    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private void AdmitSnapshot()
    {
        bool invalidated = false;
        for (int slot = 0; slot < Capacity; slot++)
        {
            if (_tiles[slot] is not { } tile || _wanted.Contains(tile.Key)) continue;
            _slots.Remove(tile.Key); _tiles[slot] = null; invalidated = true;
        }
        foreach (DrawnTerrainTile tile in _requested)
        {
            if (!_slots.TryGetValue(tile.Key, out int slot) || ReferenceEquals(_tiles[slot]!.Value.Grid, tile.Grid)) continue;
            _slots.Remove(tile.Key); _tiles[slot] = null; invalidated = true;
        }
        // Retire departed/replaced identities before any slot can be overwritten. An upload failure therefore
        // cannot expose a half-new fine/morph pair through a previously published lookup entry.
        if (invalidated) Publish();
        bool changed = false;
        int uploads = 0;
        long began = Stopwatch.GetTimestamp();
        PendingTiles = 0;
        foreach (DrawnTerrainTile tile in _requested)
        {
            bool existing = _slots.TryGetValue(tile.Key, out int slot);
            bool needsUpload = !existing; // replaced grid identities were invalidated before any upload
            if (needsUpload && uploads > 0 && (uploads >= UploadsPerFrame || Stopwatch.GetElapsedTime(began).TotalMilliseconds >= 0.5))
            {
                PendingTiles++; continue;
            }
            if (!existing)
            {
                slot = Array.FindIndex(_tiles, static item => !item.HasValue);
                if (slot < 0) throw new InvalidOperationException("Contact slot admission disagrees with its bounded selection.");
            }
            if (needsUpload)
            {
                Upload(tile.Grid.Positions, _fine, slot);
                ThrowFixtureFault(FixtureFault.AfterFineLayer);
                Upload(tile.Grid.MorphPositions, _morph, slot);
                UploadAppearance(tile.Grid, slot);
                _generations[slot] = _generations[slot] % 131070 + 1;
                _slots[tile.Key] = slot;
                uploads++; changed = true;
            }
            if (_tiles[slot] is not { } previous || previous.Transform != tile.Transform || previous.MorphRange != tile.MorphRange
                || previous.Bounds != tile.Bounds)
                changed = true;
            _tiles[slot] = tile;
        }
        // All native updates precede the frame draw. The lookup is the final publication command; unready slots
        // are zero. Rendered terrain may move ahead of staged contact, but grass never uses a retired triangle.
        if (changed) Publish();
        // An empty drawn selection is complete (for example deep water where the seabed is outside
        // grass reach). Missing support hides grass; it must not keep the world's loading gate pending.
        Ready = PendingTiles == 0;
    }

    private void RefuseSnapshot(string message)
    {
        Ready = false; PendingTiles = 0; Failure = message;
        try { ClearPublished(); }
        catch (Exception error) { FailUpload(error); }
    }

    private void FailUpload(Exception error)
    {
        Ready = false; PendingTiles = 0;
        _fault = Failure = "Drawn terrain contact upload/publication failed: " + error.Message;
        // Fault injection is one-shot. The failure path always attempts an empty publication, even when a
        // native call threw after partially completing; no old lookup is intentionally left usable.
        try { ClearPublished(force: true, suppressFixtureFault: true); }
        catch (Exception cleanup)
        {
            PublicationInvalidationFailed = true;
            _fault = Failure += "; empty-publication cleanup failed: " + cleanup.Message;
            Version++; // invalidate cached CPU culling bounds even if the renderer rejected clearing
        }
        GD.PushError(_fault);
    }

    private void Upload(float[] positions, Texture2DArray texture, int slot)
    {
        for (int i = 0; i < GridSide * GridSide; i++)
        {
            if (!float.IsFinite(positions[3 * i]) || !float.IsFinite(positions[3 * i + 1]) || !float.IsFinite(positions[3 * i + 2]))
                throw new InvalidOperationException("Drawn terrain contact grid contains a non-finite vertex.");
            _gridFloats[4 * i] = positions[3 * i];
            _gridFloats[4 * i + 1] = positions[3 * i + 1];
            _gridFloats[4 * i + 2] = positions[3 * i + 2];
        }
        Buffer.BlockCopy(_gridFloats, 0, _gridBytes, 0, _gridBytes.Length);
        using Image image = Image.CreateFromData(GridSide, GridSide, false, Image.Format.Rgbaf, _gridBytes);
        texture.UpdateLayer(image, slot);
        UploadedBytes += _gridBytes.Length;
    }

    private void Publish(bool suppressFixtureFault = false)
    {
        Array.Clear(_lookup); Array.Clear(_metadata);
        for (int slot = 0; slot < Capacity; slot++)
        {
            if (_tiles[slot] is not { } tile) continue;
            TileKey key = tile.Key;
            int span = 1 << (_mapLevel - key.Level), x0 = key.X * span, y0 = key.Y * span;
            for (int y = y0; y < y0 + span; y++)
                for (int x = x0; x < x0 + span; x++) _lookup[(key.Face * _mapSide + y) * _mapSide + x] = _generations[slot] * Capacity + slot + 1;
            int k = slot * 20;
            Put(k, new Vector3(key.X, key.Y, 1 << key.Level), key.Face);
            Put(k + 4, tile.Transform.Origin, tile.MorphRange.X);
            Put(k + 8, tile.Transform.Basis.X, tile.MorphRange.Y);
            Put(k + 12, tile.Transform.Basis.Y, 1);
            Put(k + 16, tile.Transform.Basis.Z, _generations[slot]);
        }
        UpdateTexture(_meta, _metadata, 5, Capacity, Image.Format.Rgbaf);
        if (!suppressFixtureFault) ThrowFixtureFault(FixtureFault.AfterMetadataBeforeLookup);
        UpdateTexture(_map, _lookup, _mapSide, _mapSide * 6, Image.Format.Rf);
        Version++;
        UploadedBytes += (long)(_lookup.Length + _metadata.Length) * sizeof(float);
        void Put(int index, Vector3 xyz, float w)
        { _metadata[index] = xyz.X; _metadata[index + 1] = xyz.Y; _metadata[index + 2] = xyz.Z; _metadata[index + 3] = w; }
    }

    private void ClearPublished(bool force = false, bool suppressFixtureFault = false)
    {
        bool hadTiles = _slots.Count > 0;
        _slots.Clear(); Array.Clear(_tiles);
        if (force || hadTiles) Publish(suppressFixtureFault);
    }

    // Entire fine/morph vertex envelopes, not a current-camera sample. Roots anywhere in the requested face box
    // remain within this convex bound for every morph value. Used to refresh spatial grass draw/culling bounds.
    internal bool TryBounds(int face, Vector2 low, Vector2 high, out Aabb bounds)
    {
        bool found = false; bounds = default;
        foreach (DrawnTerrainTile? value in _tiles)
        {
            if (value is not { } tile) continue;
            bool crossesSeam = low.X < 0 || low.Y < 0 || high.X > 1 || high.Y > 1;
            if (tile.Key.Face != face && !crossesSeam) continue;
            float scale = 1 << tile.Key.Level;
            Vector2 min = new(tile.Key.X / scale, tile.Key.Y / scale), max = min + Vector2.One / scale;
            if (!crossesSeam && (low.X > max.X || low.Y > max.Y || high.X < min.X || high.Y < min.Y)) continue;
            bounds = found ? bounds.Merge(tile.Bounds) : tile.Bounds; found = true;
        }
        return found;
    }

    private static ImageTexture Texture(float[] values, int width, int height, Image.Format format)
    {
        using Image image = ToImage(values, width, height, format);
        return ImageTexture.CreateFromImage(image);
    }
    private static void UpdateTexture(ImageTexture texture, float[] values, int width, int height, Image.Format format)
    { using Image image = ToImage(values, width, height, format); texture.Update(image); }
    private static Image ToImage(float[] values, int width, int height, Image.Format format)
    {
        byte[] bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return Image.CreateFromData(width, height, false, format, bytes);
    }
    public void Dispose()
    {
        if (_cleanupComplete) return;
        _disposed = true; Ready = false;
        _slots.Clear(); _requested.Clear(); Array.Clear(_tiles); _wanted.Clear();
        PendingTiles = 0;
        List<Exception>? failures = null;
        try { ThrowFixtureFault(FixtureFault.DuringRetirement); }
        catch (Exception error) { (failures ??= new()).Add(error); }
        Retire(_fine, ref failures); Retire(_morph, ref failures); Retire(_map, ref failures); Retire(_meta, ref failures);
        RetireAppearance(ref failures);
        _cleanupComplete = failures == null;
        if (failures != null)
        {
            Failure = "Drawn terrain contact retirement failed; admission is closed and the owner retains failed handles for explicit retry.";
            throw new AggregateException(Failure, failures);
        }
    }

    private static void Retire(GodotObject? resource, ref List<Exception>? failures)
    {
        try { if (resource != null && GodotObject.IsInstanceValid(resource)) resource.Dispose(); }
        catch (Exception error) { (failures ??= new()).Add(error); }
    }

    internal void InjectFixtureFault(FixtureFault fault) => _fixtureFault = fault;
    internal GodotObject[] NativeResourcesForFixture() => new GodotObject[] { _fine, _morph, _map, _meta };

    // A failed constructor cannot return its instance. The exception is the explicit owner transfer when
    // cleanup itself failed; callers must quarantine Owner and retry Dispose before releasing that reference.
    internal sealed class ConstructionCleanupException : AggregateException
    {
        internal TerrainContactAtlas Owner { get; }
        internal ConstructionCleanupException(TerrainContactAtlas owner, Exception creation, Exception cleanup)
            : base("Contact atlas construction and cleanup failed; retain Owner for bounded explicit retirement.", creation, cleanup)
            => Owner = owner;
    }

    private void ThrowFixtureFault(FixtureFault fault)
    {
        if (_fixtureFault != fault) return;
        _fixtureFault = FixtureFault.None;
        throw new InvalidOperationException("Expected contact atlas fixture failure at " + fault);
    }
}

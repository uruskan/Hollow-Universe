using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using Hollow.Core;
using Hollow.Surface;

namespace HollowUniverse.Surface;

public sealed partial class GrassField
{
    // Inactive until explicitly configured and advanced by the owning view. This does not replace the legacy
    // cover path, qualify new roots, or extend a near/middle mesh into the unqualified billboard range.
    // All admission, native upload and retirement run on the scene thread; there is no second worker scheduler.
    internal sealed record AuthoredSpatialSource(int Family, int Variant, int Lod, MeshInstance3D Original,
        GroundCoverRoot Root, ShaderMaterial Material, float DrawRadius);

    internal readonly record struct AuthoredSpatialStatistics(bool Configured, bool Ready, bool AdmissionHalted, int IndexedPlants,
        int IndexedTiles, int ResidentTiles, int ResidentBatches, int NearInstances, int MiddleInstances,
        int FarInstances, int NearBatches, int MiddleBatches, int FarBatches, int PendingBatches, int RetiringBatches, int RetiringInstances,
        int ExaminedLastStep, int MissingSupport, long UploadedBytes, long SubmittedTriangles,
        long NativeBatchesCreated, long Publications, long Cancellations, long CoalescedCameraUpdates,
        bool LatestCameraPending, int StageAgeSteps, int MaximumStageAgeSteps, double LastStepMilliseconds, string? Failure);

    private const int AuthoredSpatialTileCells = 32, AuthoredSpatialTileLimit = 512;
    private const int AuthoredSpatialBatchLimit = 512, AuthoredSpatialInstanceLimit = 18000;
    private const int AuthoredSpatialInputLimit = 65536, AuthoredSpatialWorkLimit = 4096;
    private const int AuthoredSpatialUploadLimit = 4;
    private const long AuthoredSpatialUploadBytes = 512 * 1024;
    private const double AuthoredSpatialStepMilliseconds = 0.5; // Soft target, never a measured hard deadline.
    private const double AuthoredSpatialMovement = 0.75, AuthoredSpatialLodHysteresis = 1;
    private const float AuthoredSpatialRangeHysteresis = 2, AuthoredSpatialMaximumScale = 2;
    private const float AuthoredSpatialMaximumEnvelope = 8;
    private AuthoredSpatialState? _authoredSpatial;
    private string? _authoredSpatialStoppedFailure;
    private AuthoredSpatialFixtureControl? _authoredSpatialFixture;

    // Null in production. Fault counters only exist in explicitly initialized native diagnostics.
    private sealed class AuthoredSpatialFixtureControl
    {
        public bool FailAfterBatchCreation;
        public int NodeRetirementFailures, MeshRetirementFailures;
    }

    private sealed class AuthoredSpatialArt
    {
        public ArrayMesh Mesh = null!;
        public ShaderMaterial Material = null!;
        public GroundCoverRoot Root = null!;
        public float Radius, Envelope;
        public int Lod;
    }

    private sealed class AuthoredSpatialPoint
    {
        public ulong Identity;
        public CoverPlant Plant;
        public TileKey Tile;
        public Vector2 Uv;
    }

    private sealed class AuthoredSpatialTile
    {
        public TileKey Key;
        public readonly List<AuthoredSpatialPoint> Points = new();
        public float Radius, Envelope;
    }

    private sealed class AuthoredSpatialIndex
    {
        public FieldData? Building;
        public readonly WeakReference<FieldData> Source;
        public readonly long Serial;
        public readonly Dictionary<TileKey, AuthoredSpatialTile> ByTile = new();
        public readonly List<AuthoredSpatialTile> Tiles = new();
        public readonly HashSet<ulong> Identities = new();
        public int Face, Plant, Count;
        public bool Complete, Rejected;
        public AuthoredSpatialIndex(FieldData source, long serial)
        { Building = source; Source = new(source); Serial = serial; }
    }

    private readonly record struct AuthoredSpatialChoice(AuthoredSpatialPoint Point, int Art);
    private readonly record struct AuthoredSpatialBatchKey(TileKey Tile, int Art);

    private sealed class AuthoredSpatialPlan
    {
        public AuthoredSpatialBatchKey Key;
        public readonly List<AuthoredSpatialChoice> Choices = new();
    }

    private sealed class AuthoredSpatialBatch
    {
        public MultiMeshInstance3D Instance = null!;
        public MultiMesh MultiMesh = null!;
        public AuthoredSpatialBatchKey Key;
        public Vector3D Anchor;
        public TerrainContactAtlas? BoundsAtlas;
        public long BoundsVersion = -1;
        public Aabb Bounds;
        public bool HasBounds;
        public int Count;
        public AuthoredSpatialFixtureControl? Fixture;
    }

    private sealed class AuthoredSpatialGeneration
    {
        public long IndexSerial;
        public readonly List<AuthoredSpatialBatch> Batches = new();
        public readonly List<AuthoredSpatialChoice> Choices = new();
        public readonly Dictionary<ulong, int> Lods = new();
        public readonly HashSet<TileKey> Tiles = new();
        public readonly int[] InstancesByLod = new int[3], BatchesByLod = new int[3];
        public long Triangles;
    }

    private sealed class AuthoredSpatialStage
    {
        public FieldData Field = null!; // Pinned only until this bounded preparation/upload completes or is cancelled.
        public AuthoredSpatialIndex Index = null!;
        public TerrainContactAtlas Atlas = null!;
        public long Version;
        public int Epoch, Tile, Point, NextPlan;
        public int StartedStep;
        public Vector3D Camera;
        public Vector3 CameraRender;
        public int ContactLevel;
        public bool Selected, SelectionDiffers;
        public readonly List<DrawnTerrainTile> Contacts = new(TerrainContactAtlas.Capacity);
        public readonly Dictionary<TileKey, DrawnTerrainTile> ContactByKey = new();
        public readonly Dictionary<AuthoredSpatialBatchKey, AuthoredSpatialPlan> ByBatch = new();
        public readonly List<AuthoredSpatialPlan> Plans = new();
        public readonly AuthoredSpatialGeneration Generation = new();
    }

    private sealed class AuthoredSpatialState
    {
        public readonly AuthoredSpatialArt?[] Arts = new AuthoredSpatialArt?[CoverFamilies * CoverVariants * 2];
        public readonly int Epoch;
        public TerrainLod? Terrain;
        public TerrainContactAtlas? BoundAtlas, LastAttemptAtlas;
        public AuthoredSpatialIndex? Index;
        public AuthoredSpatialStage? Stage;
        public AuthoredSpatialGeneration? Resident;
        // At most the previous resident and one aborted staging generation. A cleanup fault closes admission,
        // so repeated errors cannot accumulate new native owners behind this bounded retirement quarantine.
        public readonly List<AuthoredSpatialGeneration> Retiring = new(2);
        public long Serial, UploadedBytes, LastAttemptIndex = -1, LastAttemptVersion = -1;
        public Vector3D LastAttemptCamera;
        public Vector3D LatestCamera;
        public int Examined, MissingSupport;
        public int Steps, MaximumStageAgeSteps;
        public long NativeBatchesCreated, Publications, Cancellations, CoalescedCameraUpdates;
        public bool LatestCameraPending;
        public double LastStepMilliseconds;
        public bool Ready, Halted, ExitHooked;
        public string? Failure;
        public AuthoredSpatialFixtureControl? Fixture;
        public AuthoredSpatialState(int epoch) { Epoch = epoch; }
    }

    internal AuthoredSpatialStatistics AuthoredSpatialDiagnostics
    {
        get
        {
            AuthoredSpatialState? s = _authoredSpatial;
            AuthoredSpatialGeneration? r = s?.Resident;
            int retiringBatches = 0, retiringInstances = 0;
            if (s != null) foreach (AuthoredSpatialGeneration generation in s.Retiring)
                foreach (AuthoredSpatialBatch batch in generation.Batches)
                { retiringBatches++; retiringInstances += batch.Count; }
            return new(s != null, s?.Ready == true, s?.Halted == true, s?.Index?.Count ?? 0, s?.Index?.Tiles.Count ?? 0,
                r?.Tiles.Count ?? 0, r?.Batches.Count ?? 0, r?.InstancesByLod[0] ?? 0,
                r?.InstancesByLod[1] ?? 0, r?.InstancesByLod[2] ?? 0, r?.BatchesByLod[0] ?? 0,
                r?.BatchesByLod[1] ?? 0, r?.BatchesByLod[2] ?? 0,
                s?.Stage is { } stage ? stage.Plans.Count - stage.NextPlan : 0,
                retiringBatches, retiringInstances,
                s?.Examined ?? 0, s?.MissingSupport ?? 0, s?.UploadedBytes ?? 0, r?.Triangles ?? 0,
                s?.NativeBatchesCreated ?? 0, s?.Publications ?? 0, s?.Cancellations ?? 0, s?.CoalescedCameraUpdates ?? 0,
                s?.LatestCameraPending == true, s?.Stage is { } pending ? s.Steps - pending.StartedStep + 1 : 0,
                s?.MaximumStageAgeSteps ?? 0,
                s?.LastStepMilliseconds ?? 0, s?.Failure ?? _authoredSpatialStoppedFailure);
        }
    }

    /// <summary>
    /// Explicit admission only: caller pairs catalog family/variant/LOD with already authenticated root metadata.
    /// Original scene/mesh/material are borrowed only during this call; this owner retains its augmented meshes
    /// and cloned materials. Both near and middle roots of each selected variant must agree. Far is unadmitted.
    /// Configure does not hide legacy cover, start workers, or activate itself in UpdateGrass.
    /// </summary>
    internal void ConfigureAuthoredSpatial(IReadOnlyList<AuthoredSpatialSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (!_setupComplete || _stopped || _authoredSpatial != null)
            throw new InvalidOperationException("Authored spatial cover requires one live, configured grass owner.");
        if (_level < 5 || _faceCells > int.MaxValue || sources.Count is < 2 or > CoverFamilies * CoverVariants * 2)
            throw new ArgumentException("Authored spatial admission requires supported 32-cell tiles and bounded near/middle pairs.");
        var state = new AuthoredSpatialState(_epoch) { Fixture = _authoredSpatialFixture };
        try
        {
            foreach (AuthoredSpatialSource source in sources)
            {
                if ((uint)source.Family >= CoverFamilies || (uint)source.Variant >= CoverVariants || source.Lod is < 0 or > 1
                    || source.Root == null || source.Original == null || source.Material == null
                    || !float.IsFinite(source.DrawRadius) || source.DrawRadius <= CoverNear
                    || source.DrawRadius > Math.Min(_far, CoverMiddle))
                    throw new ArgumentException("Only explicit near/middle sources within their actual draw range are admitted.");
                int slot = (source.Family * CoverVariants + source.Variant) * 2 + source.Lod;
                if (state.Arts[slot] != null) throw new ArgumentException("Duplicate authored source admission.");
                // Track each owner before a native constructor/duplicate can throw.
                var art = new AuthoredSpatialArt { Root = source.Root, Radius = source.DrawRadius, Lod = source.Lod };
                state.Arts[slot] = art;
                art.Mesh = AuthoredCoverRootMesh.Create(source.Original, source.Root);
                art.Material = (ShaderMaterial)source.Material.Duplicate();
                if (art.Material.Shader?.ResourcePath != "res://shaders/surface_authored_cover.gdshader")
                    throw new ArgumentException("Authored spatial materials must use the production cover shader.");
                float height = art.Material.GetShaderParameter("plant_height").AsSingle();
                if (!float.IsFinite(height) || height < 0.05f || height > 8)
                    throw new ArgumentException("Authored material height exceeds its declared deformation bounds.");
                art.Envelope = AuthoredSpatialEnvelope(art.Mesh.GetAabb(), V(source.Root.Root), height);
                if (!float.IsFinite(art.Envelope) || art.Envelope > AuthoredSpatialMaximumEnvelope)
                    throw new ArgumentException("Authored footprint exceeds the contact atlas's eight-metre support margin.");
                art.Material.SetShaderParameter("billboard", false);
                art.Material.SetShaderParameter("authored_contact_enabled", true);
                art.Material.SetShaderParameter("authored_growth_axis", V(source.Root.GrowthAxis));
                art.Material.SetShaderParameter("authored_growth_length", (float)source.Root.GrowthLength);
                art.Material.SetShaderParameter("far_radius", source.DrawRadius);
                AuthoredSpatialDetach(art.Material);
            }
            for (int pair = 0; pair < state.Arts.Length; pair += 2)
            {
                AuthoredSpatialArt? near = state.Arts[pair], middle = state.Arts[pair + 1];
                if (near == null && middle == null) continue;
                if (near == null || middle == null || near.Root.Root != middle.Root.Root || near.Radius != middle.Radius)
                    throw new ArgumentException("Every admitted variant requires near/middle meshes sharing one reviewed logical root and radius.");
            }
            TreeExiting += StopAuthoredSpatial;
            state.ExitHooked = true;
            _authoredSpatial = state; _authoredSpatialStoppedFailure = null;
        }
        catch (Exception error)
        {
            try { ReleaseAuthoredSpatialState(state); }
            catch (Exception cleanup)
            {
                // Even a failed configuration must keep ownership if native cleanup was refused. Subsequent
                // Configure calls are blocked until an explicit Stop has confirmed every owner was released.
                _authoredSpatial = state; state.Failure = cleanup.Message;
                if (!state.ExitHooked) { TreeExiting += StopAuthoredSpatial; state.ExitHooked = true; }
                throw new AggregateException(error, cleanup);
            }
            throw;
        }
    }

    /// <summary>
    /// Scene-thread tick, after UpdateDrawnContact. Uses that atlas's exact published grids, never an independent
    /// height sampler. Integration must call StopAuthoredSpatial before StopDrawnContact; TreeExiting is a fallback.
    /// The passed terrain pins this state to one view's owner and supplies its bounded contact addressing level.
    /// </summary>
    internal void AdvanceAuthoredSpatial(TerrainLod terrain, Vector3D camera, bool show)
    {
        AuthoredSpatialState? state = _authoredSpatial;
        if (state == null) return;
        if (_stopped || state.Epoch != _epoch) { StopAuthoredSpatial(); return; }
        if (state.Halted) { HideAuthoredSpatialFault(state); return; }
        long began = Stopwatch.GetTimestamp();
        state.Examined = 0; state.Steps++;
        try
        {
            ArgumentNullException.ThrowIfNull(terrain);
            if (!AuthoredSpatialFinite(camera)) throw new ArgumentOutOfRangeException(nameof(camera));
            if (state.Stage is { } moving)
            {
                state.LatestCameraPending = (camera - moving.Camera).LengthSquared >= AuthoredSpatialMovement * AuthoredSpatialMovement;
                if (state.LatestCameraPending && camera != state.LatestCamera) state.CoalescedCameraUpdates++;
            }
            else state.LatestCameraPending = (camera - state.LastAttemptCamera).LengthSquared >= AuthoredSpatialMovement * AuthoredSpatialMovement;
            state.LatestCamera = camera;
            if (state.Terrain != null && !ReferenceEquals(state.Terrain, terrain))
                throw new InvalidOperationException("Authored spatial state cannot cross terrain/world owners.");
            state.Terrain = terrain;
            if (terrain.MaxLevel > TerrainContactAtlas.MaximumMapLevel)
                throw new InvalidOperationException("Authored support addressing exceeds the atlas's level-nine ceiling.");
            TerrainContactAtlas? atlas = _contact;
            bool supported = show && _contactRequired && atlas?.Ready == true && !atlas.PublicationInvalidationFailed;
            if (!supported || _field == null)
            {
                CancelAuthoredSpatialStage(state);
                state.LastAttemptIndex = -1; // A hidden/unready view must retry when its same publication returns.
                state.Ready = false;
                UpdateAuthoredSpatialVisibility(state, camera, false);
                return;
            }
            if (!ReferenceEquals(state.BoundAtlas, atlas))
            {
                CancelAuthoredSpatialStage(state);
                foreach (AuthoredSpatialArt? art in state.Arts)
                    if (art != null) { AuthoredSpatialDetach(art.Material); atlas!.Bind(art.Material); }
                state.BoundAtlas = atlas;
            }
            if (state.Index == null || !state.Index.Source.TryGetTarget(out FieldData? indexed) || !ReferenceEquals(indexed, _field))
            {
                CancelAuthoredSpatialStage(state);
                state.Index = new AuthoredSpatialIndex(_field, ++state.Serial);
                state.Ready = false;
                int total = 0;
                if (_field.Faces.Count > 3) throw new InvalidOperationException("Authored spatial input exceeds the existing three-face lattice contract.");
                foreach (FaceLattice face in _field.Faces)
                {
                    if (face.Plants.Count > AuthoredSpatialInputLimit - total)
                        throw new InvalidOperationException("Authored spatial input exceeds 65,536 accepted lattice plants; no prefix was admitted.");
                    total += face.Plants.Count;
                }
            }
            AuthoredSpatialIndex index = state.Index!;
            if (index.Rejected) { UpdateAuthoredSpatialVisibility(state, camera, true); return; }
            if (!index.Complete)
            {
                ContinueAuthoredSpatialIndex(state, index, began);
                UpdateAuthoredSpatialVisibility(state, camera, true);
                return;
            }
            if (state.Stage is { } stale && (!ReferenceEquals(stale.Field, _field) || stale.Epoch != _epoch
                || !ReferenceEquals(stale.Atlas, atlas) || stale.Version != atlas!.Version))
                CancelAuthoredSpatialStage(state);
            if (state.Stage == null && (state.LastAttemptIndex != index.Serial || !ReferenceEquals(state.LastAttemptAtlas, atlas)
                || state.LastAttemptVersion != atlas!.Version
                || (camera - state.LastAttemptCamera).LengthSquared >= AuthoredSpatialMovement * AuthoredSpatialMovement))
                BeginAuthoredSpatialStage(state, index, atlas!, terrain, camera);
            if (state.Stage is { } stage) ContinueAuthoredSpatialStage(state, stage, began);
            UpdateAuthoredSpatialVisibility(state, camera, true);
        }
        catch (Exception error)
        {
            state.Ready = false;
            if (state.Index is { Complete: false } index) { index.Rejected = true; index.Building = null; }
            try { CancelAuthoredSpatialStage(state); } catch (Exception cleanup) { error = new AggregateException(error, cleanup); }
            state.Failure = error.Message;
            // Previously committed native generation is retained on admission/upload failure. Its own atlas-bound
            // support gate and visibility refresh still apply; a failed world/atlas binding hides it conservatively.
            UpdateAuthoredSpatialVisibility(state, camera, !state.Halted && ReferenceEquals(state.Terrain, terrain)
                && ReferenceEquals(state.BoundAtlas, _contact) && _contact?.Ready == true);
            GD.PushError($"Authored spatial cover admission failed: {error}");
        }
        finally { state.LastStepMilliseconds = Stopwatch.GetElapsedTime(began).TotalMilliseconds; }
    }

    private void ContinueAuthoredSpatialIndex(AuthoredSpatialState state, AuthoredSpatialIndex index, long began)
    {
        FieldData field = index.Building!;
        while (index.Face < field.Faces.Count && state.Examined < AuthoredSpatialWorkLimit)
        {
            FaceLattice face = field.Faces[index.Face];
            if (index.Plant == face.Plants.Count) { index.Face++; index.Plant = 0; continue; }
            CoverPlant plant = face.Plants[index.Plant++];
            state.Examined++;
            if ((uint)plant.Family >= CoverFamilies || (uint)plant.Variant >= CoverVariants
                || !AuthoredSpatialFinite(plant.Position) || plant.Position.LengthSquared <= 0
                || !float.IsFinite(plant.Scale) || plant.Scale <= 0 || plant.Scale > AuthoredSpatialMaximumScale
                || !float.IsFinite(plant.Yaw) || !float.IsFinite(plant.Tint))
                throw new InvalidOperationException("Invalid accepted authored plant or unsupported deformation scale.");
            int artSlot = (plant.Family * CoverVariants + plant.Variant) * 2;
            if (state.Arts[artSlot] is { } art)
            {
                (double x, double y) = CellOn(face.Face, plant.Position);
                if (!double.IsFinite(x) || !double.IsFinite(y) || x < 0 || y < 0 || x >= _faceCells || y >= _faceCells)
                    throw new InvalidOperationException("Accepted authored plant has no canonical cell on its owning face.");
                var cell = new TileKey(face.Face, _level, (int)x, (int)y);
                if (!index.Identities.Add(cell.Packed))
                    throw new InvalidOperationException("Two accepted authored plants claim one canonical face/cell identity.");
                var tileKey = new TileKey(face.Face, _level - 5, (int)x / AuthoredSpatialTileCells, (int)y / AuthoredSpatialTileCells);
                if (!index.ByTile.TryGetValue(tileKey, out AuthoredSpatialTile? tile))
                {
                    if (index.Tiles.Count == AuthoredSpatialTileLimit)
                        throw new InvalidOperationException("Authored spatial index exceeds 512 tiles; the complete replacement was refused.");
                    tile = new AuthoredSpatialTile { Key = tileKey };
                    index.ByTile.Add(tileKey, tile); index.Tiles.Add(tile);
                }
                if (tile.Points.Count == AuthoredSpatialTileCells * AuthoredSpatialTileCells)
                    throw new InvalidOperationException("A canonical authored tile exceeds one accepted plant per cell.");
                tile.Points.Add(new AuthoredSpatialPoint { Identity = cell.Packed, Plant = plant, Tile = tileKey,
                    Uv = new Vector2((float)(x / _faceCells), (float)(y / _faceCells)) });
                tile.Radius = Math.Max(tile.Radius, art.Radius);
                tile.Envelope = Math.Max(tile.Envelope, Math.Max(art.Envelope, state.Arts[artSlot + 1]!.Envelope));
                index.Count++;
            }
            if (AuthoredSpatialStepSpent(began, state.Examined)) break;
        }
        if (index.Face != field.Faces.Count) return;
        // Only this <=512-item address list is sorted. Plant order retains the source's canonical row traversal;
        // no full-face distance ranking/sorting or extra probabilistic thinning is introduced.
        index.Tiles.Sort(static (a, b) => a.Key.Packed.CompareTo(b.Key.Packed));
        index.Complete = true; index.Building = null; index.Identities.Clear();
        state.Ready = false;
    }

    private void BeginAuthoredSpatialStage(AuthoredSpatialState state, AuthoredSpatialIndex index,
        TerrainContactAtlas atlas, TerrainLod terrain, Vector3D camera)
    {
        var stage = new AuthoredSpatialStage { Field = _field!, Index = index, Atlas = atlas, Epoch = _epoch,
            Camera = camera, CameraRender = _frame.ToRender(camera), ContactLevel = terrain.MaxLevel,
            StartedStep = state.Steps };
        state.Stage = stage;
        if (!atlas.TryCopyPublishedTiles(stage.Contacts, out long version, TerrainContactAtlas.Capacity))
            throw new InvalidOperationException("Authored support publication is incomplete or exceeds the explicit 128-snapshot cap.");
        stage.Version = version; stage.Generation.IndexSerial = index.Serial;
        stage.SelectionDiffers = state.Resident == null || state.Resident.IndexSerial != index.Serial;
        foreach (DrawnTerrainTile contact in stage.Contacts) stage.ContactByKey.Add(contact.Key, contact);
        state.LastAttemptIndex = index.Serial; state.LastAttemptAtlas = atlas;
        state.LastAttemptVersion = version; state.LastAttemptCamera = camera;
        state.LatestCameraPending = false;
        state.MissingSupport = 0; state.Ready = false; state.Failure = null;
    }

    private void ContinueAuthoredSpatialStage(AuthoredSpatialState state, AuthoredSpatialStage stage, long began)
    {
        if (!stage.Selected)
        {
            while (stage.Tile < stage.Index.Tiles.Count && state.Examined < AuthoredSpatialWorkLimit)
            {
                AuthoredSpatialTile tile = stage.Index.Tiles[stage.Tile];
                double tileReach = tile.Radius + tile.Envelope + AuthoredSpatialRangeHysteresis;
                if (stage.Point == 0 && (!AuthoredSpatialTileBounds(stage.Atlas, tile.Key, out Aabb tileBounds)
                    || AuthoredSpatialBoundsDistance(stage.CameraRender, tileBounds) > tileReach * tileReach))
                {
                    stage.Tile++; state.Examined++;
                    if (AuthoredSpatialStepSpent(began, state.Examined)) break;
                    continue;
                }
                if (stage.Point == tile.Points.Count) { stage.Tile++; stage.Point = 0; continue; }
                AuthoredSpatialPoint point = tile.Points[stage.Point++];
                state.Examined++;
                if (!TryAuthoredSpatialContact(stage, point, out TerrainContactSample contact)) state.MissingSupport++;
                else SelectAuthoredSpatialPoint(state, stage, point, (contact.RenderPosition - stage.CameraRender).Length());
                if (AuthoredSpatialStepSpent(began, state.Examined)) break;
            }
            if (stage.Tile != stage.Index.Tiles.Count) return;
            stage.Selected = true;
            if (!stage.SelectionDiffers && state.Resident!.Choices.Count == stage.Generation.Choices.Count)
            {
                ObserveAuthoredSpatialStageAge(state, stage);
                state.Stage = null; state.Ready = !state.LatestCameraPending;
                return; // No native resources, buffers or material duplicates for an unchanged selection.
            }
            return; // Selection and native upload have distinct bounded admission steps.
        }
        int uploads = 0;
        long bytes = 0;
        while (stage.NextPlan < stage.Plans.Count && uploads < AuthoredSpatialUploadLimit)
        {
            AuthoredSpatialPlan plan = stage.Plans[stage.NextPlan];
            long nextBytes = (long)plan.Choices.Count * 16 * sizeof(float);
            if (uploads > 0 && (bytes + nextBytes > AuthoredSpatialUploadBytes
                || Stopwatch.GetElapsedTime(began).TotalMilliseconds >= AuthoredSpatialStepMilliseconds)) break;
            CreateAuthoredSpatialBatch(state, stage, plan);
            stage.NextPlan++; uploads++; bytes += nextBytes; state.UploadedBytes += nextBytes;
        }
        if (stage.NextPlan != stage.Plans.Count) return;
        // Main-thread publication still verifies both native atlas identity and publication epoch. Version alone
        // is insufficient after replacing an atlas/world whose version counter starts again at zero.
        if (_stopped || stage.Epoch != _epoch || !ReferenceEquals(stage.Field, _field)
            || !ReferenceEquals(stage.Atlas, _contact) || !stage.Atlas.Ready || stage.Atlas.Version != stage.Version)
        { CancelAuthoredSpatialStage(state); return; }
        AuthoredSpatialGeneration? old = state.Resident;
        ObserveAuthoredSpatialStageAge(state, stage);
        state.Publications++;
        state.Resident = stage.Generation; state.Stage = null; state.Ready = !state.LatestCameraPending;
        // Commit precedes retirement; a retirement error cannot dispose the newly committed generation.
        try { RetireAuthoredSpatialGeneration(state, old); }
        catch (Exception error) { state.Failure = error.Message; GD.PushError($"Authored spatial retirement failed: {error}"); }
    }

    private static bool TryAuthoredSpatialContact(AuthoredSpatialStage stage, AuthoredSpatialPoint point,
        out TerrainContactSample result)
    {
        result = default;
        for (int level = stage.ContactLevel; level >= 0; level--)
        {
            int count = 1 << level;
            var key = new TileKey(point.Tile.Face, level, Math.Min((int)(point.Uv.X * count), count - 1),
                Math.Min((int)(point.Uv.Y * count), count - 1));
            if (stage.ContactByKey.TryGetValue(key, out DrawnTerrainTile tile))
                return TerrainLod.TrySampleContact(tile, key, point.Uv, stage.CameraRender, out result);
        }
        return false;
    }

    private static void SelectAuthoredSpatialPoint(AuthoredSpatialState state, AuthoredSpatialStage stage,
        AuthoredSpatialPoint point, double distance)
    {
        int first = (point.Plant.Family * CoverVariants + point.Plant.Variant) * 2;
        AuthoredSpatialArt near = state.Arts[first]!;
        int previous = -1;
        bool wasResident = state.Resident != null && state.Resident.Lods.TryGetValue(point.Identity, out previous);
        int lod = distance < CoverNear ? 0 : 1;
        if (wasResident)
        {
            if (previous == 0 && distance < CoverNear + AuthoredSpatialLodHysteresis) lod = 0;
            if (previous == 1 && distance > CoverNear - AuthoredSpatialLodHysteresis) lod = 1;
        }
        AuthoredSpatialArt art = state.Arts[first + lod]!;
        // Whole mesh footprints are retained until beyond their real shader range. Existing entries get a small
        // hysteresis band; the shader still enforces the declared radius, without squeezing individual plants.
        double reach = near.Radius + art.Envelope + (wasResident ? AuthoredSpatialRangeHysteresis : 0);
        if (!double.IsFinite(distance) || distance > reach) return;
        if (stage.Generation.Choices.Count == AuthoredSpatialInstanceLimit)
            throw new InvalidOperationException("Authored spatial selection exceeds 18,000 plants; previous complete generation retained.");
        var key = new AuthoredSpatialBatchKey(point.Tile, first + lod);
        if (!stage.ByBatch.TryGetValue(key, out AuthoredSpatialPlan? plan))
        {
            if (stage.Plans.Count == AuthoredSpatialBatchLimit)
                throw new InvalidOperationException("Authored spatial selection exceeds 512 batches; previous complete generation retained.");
            plan = new AuthoredSpatialPlan { Key = key }; stage.ByBatch.Add(key, plan); stage.Plans.Add(plan);
        }
        var choice = new AuthoredSpatialChoice(point, first + lod);
        int choiceIndex = stage.Generation.Choices.Count;
        if (!stage.SelectionDiffers && (choiceIndex >= state.Resident!.Choices.Count
            || state.Resident.Choices[choiceIndex].Point.Identity != point.Identity
            || state.Resident.Choices[choiceIndex].Art != choice.Art)) stage.SelectionDiffers = true;
        plan.Choices.Add(choice); stage.Generation.Choices.Add(choice);
        stage.Generation.Lods.Add(point.Identity, lod); stage.Generation.Tiles.Add(point.Tile);
        stage.Generation.InstancesByLod[lod]++;
        stage.Generation.Triangles += art.Root.TriangleCount;
    }

    private void CreateAuthoredSpatialBatch(AuthoredSpatialState state, AuthoredSpatialStage stage, AuthoredSpatialPlan plan)
    {
        AuthoredSpatialArt art = state.Arts[plan.Key.Art]!;
        Vector3D anchor = plan.Choices[0].Point.Plant.Position;
        var buffer = new float[checked(plan.Choices.Count * 16)];
        for (int i = 0; i < plan.Choices.Count; i++)
        {
            CoverPlant plant = plan.Choices[i].Point.Plant;
            Vector3D up = plant.Position.Normalized, right = Vector3D.Cross(Vector3D.UnitY, up);
            right = right.LengthSquared > 1e-12 ? right.Normalized : Vector3D.UnitX;
            Vector3D forward = Vector3D.Cross(right, up);
            double cs = Math.Cos(plant.Yaw), sn = Math.Sin(plant.Yaw);
            Vector3D bx = (right * cs + forward * sn) * plant.Scale, by = up * plant.Scale,
                bz = (-right * sn + forward * cs) * plant.Scale;
            Vector3D root = art.Root.Root;
            // The old 2.5 cm radial offset affects only this direction hint. Actual root height is exclusively
            // fetched from the current drawn atlas by the shader; no burial/fallback enters the contact result.
            Vector3D p = plant.Position - anchor - bx * root.X - by * root.Y - bz * root.Z;
            int at = i * 16;
            buffer[at] = (float)bx.X; buffer[at + 1] = (float)by.X; buffer[at + 2] = (float)bz.X; buffer[at + 3] = (float)p.X;
            buffer[at + 4] = (float)bx.Y; buffer[at + 5] = (float)by.Y; buffer[at + 6] = (float)bz.Y; buffer[at + 7] = (float)p.Y;
            buffer[at + 8] = (float)bx.Z; buffer[at + 9] = (float)by.Z; buffer[at + 10] = (float)bz.Z; buffer[at + 11] = (float)p.Z;
            buffer[at + 12] = plant.Tint; buffer[at + 13] = plant.Tint * 0.99f; buffer[at + 14] = plant.Tint * 0.95f;
            buffer[at + 15] = plant.Yaw;
        }
        // Register before the first native allocation. A cleanup exception must leave the exact failed owner in
        // the stage's generation, not in an untracked local that escapes the two-generation reservation.
        var batch = new AuthoredSpatialBatch { Key = plan.Key, Anchor = anchor, Count = plan.Choices.Count,
            BoundsAtlas = stage.Atlas, BoundsVersion = stage.Version, Fixture = state.Fixture };
        stage.Generation.Batches.Add(batch);
        try
        {
            if (!AuthoredSpatialTileBounds(stage.Atlas, plan.Key.Tile, out Aabb bounds))
                throw new InvalidOperationException("A staged authored batch lost its published support bounds.");
            Transform3D transform = _frame.Anchor(anchor);
            batch.MultiMesh = new MultiMesh();
            batch.MultiMesh.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
            batch.MultiMesh.UseCustomData = true; batch.MultiMesh.Mesh = art.Mesh;
            batch.MultiMesh.CustomAabb = (transform.AffineInverse() * bounds).Grow(art.Envelope);
            batch.MultiMesh.InstanceCount = plan.Choices.Count; batch.MultiMesh.Buffer = buffer;
            batch.Instance = new MultiMeshInstance3D();
            state.NativeBatchesCreated++;
            batch.Instance.Name = $"AuthoredTile_{stage.Index.Serial}_{plan.Key.Tile.Packed}_{plan.Key.Art}";
            batch.Instance.Visible = false; batch.Instance.Multimesh = batch.MultiMesh;
            batch.Instance.MaterialOverride = art.Material;
            batch.Instance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            batch.Instance.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
            AddChild(batch.Instance); batch.Instance.GlobalTransform = transform;
            if (state.Fixture?.FailAfterBatchCreation == true)
            {
                state.Fixture.FailAfterBatchCreation = false;
                throw new InvalidOperationException("Expected authored spatial fixture failure after native batch creation.");
            }
            batch.Bounds = bounds; batch.HasBounds = true;
            stage.Generation.BatchesByLod[art.Lod]++;
        }
        catch (Exception error)
        {
            List<Exception>? failures = null;
            ReleaseAuthoredSpatialBatch(batch, ref failures);
            if (batch.Instance == null && batch.MultiMesh == null) stage.Generation.Batches.Remove(batch);
            if (failures != null)
            { state.Halted = true; failures.Insert(0, error); throw new AggregateException(failures); }
            throw;
        }
    }

    private void UpdateAuthoredSpatialVisibility(AuthoredSpatialState state, Vector3D camera, bool show)
    {
        if (state.Resident == null) return;
        TerrainContactAtlas? atlas = _contact;
        bool supported = show && !state.Halted && _contactRequired && atlas?.Ready == true && !atlas.PublicationInvalidationFailed
            && ReferenceEquals(state.BoundAtlas, atlas);
        Vector3 cameraRender = _frame.ToRender(camera);
        foreach (AuthoredSpatialBatch batch in state.Resident.Batches)
        {
            batch.Instance.Visible = false;
            if (!supported) continue;
            AuthoredSpatialArt art = state.Arts[batch.Key.Art]!;
            if (!ReferenceEquals(batch.BoundsAtlas, atlas) || batch.BoundsVersion != atlas!.Version)
            {
                batch.HasBounds = AuthoredSpatialTileBounds(atlas!, batch.Key.Tile, out batch.Bounds);
                batch.BoundsAtlas = atlas; batch.BoundsVersion = atlas!.Version;
            }
            if (!batch.HasBounds) continue;
            Transform3D transform = _frame.Anchor(batch.Anchor);
            batch.Instance.GlobalTransform = transform;
            batch.MultiMesh.CustomAabb = (transform.AffineInverse() * batch.Bounds).Grow(art.Envelope);
            double reach = art.Radius + art.Envelope;
            batch.Instance.Visible = AuthoredSpatialBoundsDistance(cameraRender, batch.Bounds) <= reach * reach;
        }
    }

    private static bool AuthoredSpatialTileBounds(TerrainContactAtlas atlas, TileKey key, out Aabb bounds)
    {
        float scale = 1f / (1 << key.Level);
        return atlas.TryBounds(key.Face, new Vector2(key.X * scale, key.Y * scale),
            new Vector2((key.X + 1) * scale, (key.Y + 1) * scale), out bounds);
    }

    private static double AuthoredSpatialBoundsDistance(Vector3 point, Aabb bounds) =>
        DistanceSquaredToBounds(point, new Vector3D(bounds.Position.X, bounds.Position.Y, bounds.Position.Z),
            new Vector3D(bounds.End.X, bounds.End.Y, bounds.End.Z));

    private static bool AuthoredSpatialFinite(Vector3D p) => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z);
    private static bool AuthoredSpatialStepSpent(long began, int work) => work % 32 == 0
        && Stopwatch.GetElapsedTime(began).TotalMilliseconds >= AuthoredSpatialStepMilliseconds;

    private static float AuthoredSpatialEnvelope(Aabb bounds, Vector3 root, float height)
    {
        float radius = 0;
        for (int corner = 0; corner < 8; corner++) radius = Math.Max(radius, (bounds.GetEndpoint(corner) - root).Length());
        // Full geometry relative to the reviewed root, all current scales <=2, and production hu_wind <=1.15.
        // Gust amplitude <=1. A 1.2 wind envelope plus 2 cm numerical slack is conservative for this shader only.
        return AuthoredSpatialMaximumScale * (radius + height * 1.2f * 0.11f) + 0.02f;
    }

    private static void CancelAuthoredSpatialStage(AuthoredSpatialState state)
    {
        AuthoredSpatialStage? stage = state.Stage;
        state.Stage = null;
        if (stage == null) return;
        state.Cancellations++; ObserveAuthoredSpatialStageAge(state, stage);
        stage.Contacts.Clear(); stage.ContactByKey.Clear();
        RetireAuthoredSpatialGeneration(state, stage.Generation);
    }

    private static void RetireAuthoredSpatialGeneration(AuthoredSpatialState state, AuthoredSpatialGeneration? generation)
    {
        if (generation == null) return;
        if (!state.Retiring.Contains(generation)) state.Retiring.Add(generation);
        try
        {
            ReleaseAuthoredSpatialGeneration(generation);
            state.Retiring.Remove(generation);
        }
        catch
        {
            state.Halted = true; state.Ready = false;
            HideAuthoredSpatialFault(state);
            throw;
        }
    }

    private static void ObserveAuthoredSpatialStageAge(AuthoredSpatialState state, AuthoredSpatialStage stage) =>
        state.MaximumStageAgeSteps = Math.Max(state.MaximumStageAgeSteps, state.Steps - stage.StartedStep + 1);

    private static void ReleaseAuthoredSpatialBatch(AuthoredSpatialBatch batch, ref List<Exception>? failures)
    {
        if (batch.Instance != null)
        {
            try { if (GodotObject.IsInstanceValid(batch.Instance)) batch.Instance.Visible = false; }
            catch (Exception error) { (failures ??= new()).Add(error); }
            try
            {
                if (batch.Fixture?.NodeRetirementFailures > 0)
                { batch.Fixture.NodeRetirementFailures--; throw new InvalidOperationException("Expected authored spatial node retirement refusal."); }
                if (GodotObject.IsInstanceValid(batch.Instance)) batch.Instance.Free();
                batch.Instance = null!;
            }
            catch (Exception error) { (failures ??= new()).Add(error); }
        }
        if (batch.MultiMesh != null)
        {
            try
            {
                if (batch.Fixture?.MeshRetirementFailures > 0)
                { batch.Fixture.MeshRetirementFailures--; throw new InvalidOperationException("Expected authored spatial multimesh retirement refusal."); }
                if (GodotObject.IsInstanceValid(batch.MultiMesh)) batch.MultiMesh.Dispose();
                batch.MultiMesh = null!;
            }
            catch (Exception error) { (failures ??= new()).Add(error); }
        }
    }

    private static void ReleaseAuthoredSpatialGeneration(AuthoredSpatialGeneration generation)
    {
        List<Exception>? failures = null;
        for (int i = generation.Batches.Count - 1; i >= 0; i--)
        {
            AuthoredSpatialBatch batch = generation.Batches[i];
            ReleaseAuthoredSpatialBatch(batch, ref failures);
            if (batch.Instance == null && batch.MultiMesh == null) generation.Batches.RemoveAt(i);
        }
        // Keep failed owners and their instance reservations. Never clear the only reference to a native
        // object whose retirement was refused; Stop retries this same bounded set rather than admitting more.
        if (generation.Batches.Count == 0)
        { generation.Choices.Clear(); generation.Lods.Clear(); generation.Tiles.Clear(); }
        if (failures != null) throw new AggregateException("Authored spatial generation retirement failed.", failures);
    }

    private static void HideAuthoredSpatialFault(AuthoredSpatialState state)
    {
        state.Ready = false;
        foreach (AuthoredSpatialArt? art in state.Arts)
            if (art?.Material != null)
                try { art.Material.SetShaderParameter("terrain_contact_enabled", false); }
                catch (Exception error) { state.Failure ??= error.Message; }
        if (state.Resident != null) Hide(state.Resident);
        if (state.Stage != null) Hide(state.Stage.Generation);
        foreach (AuthoredSpatialGeneration generation in state.Retiring) Hide(generation);
        void Hide(AuthoredSpatialGeneration generation)
        {
            foreach (AuthoredSpatialBatch batch in generation.Batches)
                if (batch.Instance != null)
                    try { if (GodotObject.IsInstanceValid(batch.Instance)) batch.Instance.Visible = false; }
                    catch (Exception error) { state.Failure ??= error.Message; }
        }
    }

    private static void AuthoredSpatialDetach(ShaderMaterial material)
    {
        material.SetShaderParameter("terrain_contact_enabled", false);
        material.SetShaderParameter("terrain_contact_fine", default(Variant));
        material.SetShaderParameter("terrain_contact_morph", default(Variant));
        material.SetShaderParameter("terrain_contact_lookup", default(Variant));
        material.SetShaderParameter("terrain_contact_meta", default(Variant));
    }

    private static void ReleaseAuthoredSpatialState(AuthoredSpatialState state)
    {
        state.Halted = true; state.Ready = false;
        List<Exception>? failures = null;
        // Transfer both currently owned generations before attempting anything. One Stop call attempts each
        // retained owner once; later Stop calls may retry exactly the objects that are still present.
        if (state.Stage is { } stage)
        {
            state.Cancellations++; ObserveAuthoredSpatialStageAge(state, stage);
            state.Stage = null; stage.Contacts.Clear(); stage.ContactByKey.Clear();
            if (!state.Retiring.Contains(stage.Generation)) state.Retiring.Add(stage.Generation);
        }
        if (state.Resident is { } resident)
        {
            state.Resident = null;
            if (!state.Retiring.Contains(resident)) state.Retiring.Add(resident);
        }
        HideAuthoredSpatialFault(state);
        for (int i = state.Retiring.Count - 1; i >= 0; i--)
        {
            try { ReleaseAuthoredSpatialGeneration(state.Retiring[i]); state.Retiring.RemoveAt(i); }
            catch (Exception error) { (failures ??= new()).Add(error); }
        }
        state.Index = null; state.Terrain = null; state.BoundAtlas = null; state.LastAttemptAtlas = null;
        for (int i = 0; i < state.Arts.Length; i++)
        {
            AuthoredSpatialArt? art = state.Arts[i];
            if (art == null) continue;
            try { if (art.Material != null) AuthoredSpatialDetach(art.Material); } catch (Exception error) { (failures ??= new()).Add(error); }
            // Failed nodes may still refer to shared art. Keep its explicit ownership until all batches retire.
            if (state.Retiring.Count != 0) continue;
            try
            {
                if (art.Material != null && GodotObject.IsInstanceValid(art.Material)) art.Material.Dispose();
                art.Material = null!;
            }
            catch (Exception error) { (failures ??= new()).Add(error); }
            try
            {
                if (art.Mesh != null && GodotObject.IsInstanceValid(art.Mesh)) art.Mesh.Dispose();
                art.Mesh = null!;
            }
            catch (Exception error) { (failures ??= new()).Add(error); }
            if (art.Material == null && art.Mesh == null) state.Arts[i] = null;
        }
        if (failures != null) throw new AggregateException("Authored spatial owner retirement failed.", failures);
    }

    internal void StopAuthoredSpatial()
    {
        AuthoredSpatialState? state = _authoredSpatial;
        if (state == null) return;
        try
        {
            ReleaseAuthoredSpatialState(state);
            if (state.ExitHooked) { TreeExiting -= StopAuthoredSpatial; state.ExitHooked = false; }
            _authoredSpatial = null;
        }
        catch (Exception error)
        {
            _authoredSpatialStoppedFailure = error.Message;
            GD.PushError($"Authored spatial resources failed to retire: {error}");
        }
    }

    // Diagnostic inputs enter the real index/selection/native upload path. No replacement scheduler, mesh
    // constructor or sampler is used, and none of these helpers is called by the production view.
    internal void SetupAuthoredSpatialFixture(ISurface surface, RenderFrame frame, int level = 12)
    {
        if (level is < 5 or > 16) throw new ArgumentOutOfRangeException(nameof(level));
        BeginSetup();
        _surface = surface; _frame = frame; _level = level; _faceCells = 1L << level;
        _cellMetres = TileKey.EdgeLength(level, surface.Radius); _far = CoverMiddle;
        _authoredSpatialFixture = new AuthoredSpatialFixtureControl();
        _field = new FieldData { Anchor = new Vector3D(0, surface.Radius, 0) };
        _setupComplete = true;
    }

    internal void ReplaceAuthoredSpatialFixturePlants(IReadOnlyList<Vector3D> positions)
    {
        if (_authoredSpatialFixture == null || _stopped || positions.Count > AuthoredSpatialInputLimit + 1)
            throw new InvalidOperationException("Invalid bounded authored spatial diagnostic input.");
        var field = new FieldData { Anchor = new Vector3D(0, _surface.Radius, 0) };
        foreach (Vector3D point in positions)
        {
            int face = CubeSphere.FromDirection(point).Face;
            FaceLattice? lattice = null;
            foreach (FaceLattice candidate in field.Faces) if (candidate.Face == face) { lattice = candidate; break; }
            if (lattice == null) { lattice = new FaceLattice { Face = face }; field.Faces.Add(lattice); }
            lattice.Plants.Add(new CoverPlant(point, 3, 0, 0.37f, 1, 0.93f, 0.5f));
        }
        _field = field;
    }

    internal TerrainContactAtlas? ReplaceAuthoredSpatialFixtureAtlas(TerrainContactAtlas atlas)
    {
        if (_authoredSpatialFixture == null || _stopped) throw new InvalidOperationException("Authored spatial diagnostic is not live.");
        TerrainContactAtlas? previous = _contact;
        _contact = atlas; _contactRequired = true;
        return previous; // Caller retires the replaced atlas only after Advance has detached its old materials.
    }

    internal void FaultAuthoredSpatialFixture(bool afterCreation = false, int nodeRetirementFailures = 0, int meshRetirementFailures = 0)
    {
        if (_authoredSpatialFixture == null || nodeRetirementFailures is < 0 or > 64 || meshRetirementFailures is < 0 or > 64)
            throw new InvalidOperationException("Invalid authored spatial diagnostic fault request.");
        _authoredSpatialFixture.FailAfterBatchCreation = afterCreation;
        _authoredSpatialFixture.NodeRetirementFailures = nodeRetirementFailures;
        _authoredSpatialFixture.MeshRetirementFailures = meshRetirementFailures;
    }

    internal GodotObject[] AuthoredSpatialNativeOwnersForFixture()
    {
        if (_authoredSpatialFixture == null) throw new InvalidOperationException("Not an authored spatial diagnostic.");
        var owners = new List<GodotObject>();
        AuthoredSpatialState? state = _authoredSpatial;
        if (state == null) return owners.ToArray();
        foreach (AuthoredSpatialArt? art in state.Arts)
        {
            if (art?.Mesh != null) owners.Add(art.Mesh);
            if (art?.Material != null) owners.Add(art.Material);
        }
        if (state.Resident != null) Add(state.Resident);
        if (state.Stage != null) Add(state.Stage.Generation);
        foreach (AuthoredSpatialGeneration generation in state.Retiring) Add(generation);
        return owners.ToArray();
        void Add(AuthoredSpatialGeneration generation)
        {
            foreach (AuthoredSpatialBatch batch in generation.Batches)
            {
                if (batch.Instance != null) owners.Add(batch.Instance);
                if (batch.MultiMesh != null) owners.Add(batch.MultiMesh);
            }
        }
    }

    internal int VisibleAuthoredSpatialInstancesForFixture()
    {
        int count = 0;
        if (_authoredSpatial?.Resident is { } resident)
            foreach (AuthoredSpatialBatch batch in resident.Batches)
                if (batch.Instance != null && GodotObject.IsInstanceValid(batch.Instance) && batch.Instance.Visible) count += batch.Count;
        if (_authoredSpatial != null) foreach (AuthoredSpatialGeneration generation in _authoredSpatial.Retiring)
            foreach (AuthoredSpatialBatch batch in generation.Batches)
                if (batch.Instance != null && GodotObject.IsInstanceValid(batch.Instance) && batch.Instance.Visible) count += batch.Count;
        return count;
    }
}

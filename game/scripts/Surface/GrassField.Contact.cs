using System;
using System.Collections.Generic;
using Godot;
using Hollow.Core;

namespace HollowUniverse.Surface;

public sealed partial class GrassField
{
    private TerrainContactAtlas? _contact;
    private TerrainContactAtlas? _contactRetiring;
    private bool _contactRequired;
    private bool _contactUnaddressable;
    private bool ContactReady => _contactRetiring == null && (!_contactRequired || _contact?.Ready == true);

    /// <summary>
    /// Whether the drawn-contact atlas can address this terrain. It covers levels zero through nine, which holds every
    /// playable world (WORLD-01's radius cap keeps their finest tiles at level six). A deeper surface, such as the
    /// look-dev view's full-size planet, never binds contact and keeps the heightfield placement.
    /// </summary>
    internal static bool ContactAddressable(TerrainLod terrain) => terrain.MaxLevel <= TerrainContactAtlas.MaximumMapLevel;

    /// <summary>Runs after terrain selection and before this frame's grass update. Both hosted/free views use it.</summary>
    internal void UpdateDrawnContact(TerrainLod terrain, Vector3D camera, double altitude)
    {
        if (!_setupComplete || _stopped) return;
        if (_contactRetiring != null) { Visible = false; return; }
        if (!ContactAddressable(terrain))
        {
            // Contact is never partially bound. Report the refusal once rather than failing on every frame.
            if (!_contactUnaddressable)
            {
                _contactUnaddressable = true;
                GD.Print($"[grass] drawn contact off: terrain level {terrain.MaxLevel} is beyond the atlas's " +
                    $"{TerrainContactAtlas.MaximumMapLevel}; heightfield placement is used");
            }
            _contactRequired = false;
            return;
        }
        _contactRequired = altitude < _far;
        if (!_contactRequired) return;
        if (_contact == null)
        {
            try { _contact = new TerrainContactAtlas(terrain.MaxLevel); }
            catch (TerrainContactAtlas.ConstructionCleanupException error)
            {
                // At most one uncertain native owner, never a new allocation on each frame. Stop retries its
                // retirement explicitly; failed construction cannot expose dependent draws or lose the owner.
                _contactRetiring = error.Owner;
                Visible = false;
                GD.PushError(error);
                return;
            }
            if (_spatialTemplate != null) _contact.Bind(_spatialTemplate);
            foreach (FaceGrid? grid in _grids) if (grid != null) _contact.Bind(grid.GrassMaterial);
            foreach (SpatialGeneration? generation in new[] { _spatialResident, _spatialStaging })
                if (generation != null) foreach (SpatialBatch batch in generation.Batches)
                    if (batch.Layer == 0) _contact.Bind(batch.Material);
        }
        // Eight metres admits whole blade footprints and retains a useful band beyond the current visibility range.
        _contact.Update(terrain, camera, _far + 8);
        // If the renderer also refused the empty fallback publication, hide the owning field rather than draw
        // uncertain stale contact. This is a permanent native failure, not a density or quality adjustment.
        if (_contact.PublicationInvalidationFailed) Visible = false;
    }

    private bool TrySpatialContactBounds(SpatialGeneration generation, SpatialBatch batch, out Aabb bounds)
    {
        bounds = default;
        if (_contact == null || batch.Layer != 0) return false;
        if (batch.ContactVersion != _contact.Version)
        {
            FaceLattice face = generation.Field.Faces[batch.Data.FaceIndex];
            SpatialTileData tile = batch.Data;
            // A conservative two-metre angular footprint band; outside a face the atlas includes neighboring
            // face bounds as well. This is a draw bound only, never soil/vegetation permission.
            float margin = (float)(2.0 / _surface.Radius);
            Vector2 low = new((float)((face.CornerX + tile.MinX) / (double)_faceCells) - margin,
                (float)((face.CornerY + tile.MinY) / (double)_faceCells) - margin);
            Vector2 high = new((float)((face.CornerX + tile.MaxX) / (double)_faceCells) + margin,
                (float)((face.CornerY + tile.MaxY) / (double)_faceCells) + margin);
            batch.ContactBoundsValid = _contact.TryBounds(face.Face, low, high, out batch.ContactBounds);
            batch.ContactVersion = _contact.Version;
        }
        bounds = batch.ContactBounds;
        return batch.ContactBoundsValid;
    }

    private void StopDrawnContact()
    {
        TerrainContactAtlas? atlas = _contact ?? _contactRetiring;
        _contact = null; _contactRequired = false; _contactUnaddressable = false;
        if (atlas == null) return;
        List<Exception>? failures = null;
        // Detach native texture references from all still-owned materials before releasing the atlas resources.
        if (_spatialTemplate != null) Detach(_spatialTemplate);
        foreach (FaceGrid? grid in _grids) if (grid != null) Detach(grid.GrassMaterial);
        foreach (SpatialGeneration? generation in new[] { _spatialResident, _spatialStaging })
            if (generation != null) foreach (SpatialBatch batch in generation.Batches) Detach(batch.Material);
        try { atlas.Dispose(); }
        catch (Exception error) { (failures ??= new()).Add(error); }
        _contactRetiring = atlas.CleanupComplete ? null : atlas;
        // Stop() has already closed admission. Never throw here and prevent the rest of its grass/job/native
        // cleanup from running; all failures are reported after every contact reference/resource was attempted.
        if (failures != null) GD.PushError(new AggregateException("Drawn grass contact retirement failures.", failures));
        void Detach(ShaderMaterial material)
        {
            if (!GodotObject.IsInstanceValid(material)) return;
            Set("terrain_contact_enabled", false);
            if (atlas.AppearanceEnabled) Set("terrain_appearance_enabled", false);
            foreach (string key in new[] { "terrain_contact_fine", "terrain_contact_morph", "terrain_contact_lookup", "terrain_contact_meta" })
                Set(key, default(Variant));
            if (atlas.AppearanceEnabled)
                foreach (string key in new[] { "terrain_appearance_materials", "terrain_appearance_normals" }) Set(key, default(Variant));
            void Set(string key, Variant value)
            {
                try { material.SetShaderParameter(key, value); }
                catch (Exception error) { (failures ??= new()).Add(error); }
            }
        }
    }

    internal void AttachContactAtlasForFixture(TerrainContactAtlas atlas)
    {
        if (_contact != null || _contactRetiring != null) throw new InvalidOperationException("The fixture already owns contact.");
        _contact = atlas; _contactRequired = true;
        if (_spatialTemplate != null) atlas.Bind(_spatialTemplate);
        foreach (FaceGrid? grid in _grids) if (grid != null) atlas.Bind(grid.GrassMaterial);
    }

    internal bool ContactRetirementPendingForFixture => _contactRetiring != null;
    internal void RetainFailedContactConstructionForFixture(TerrainContactAtlas.ConstructionCleanupException error)
    {
        if (_contact != null || _contactRetiring != null) throw new InvalidOperationException("The fixture already owns contact.");
        _contactRetiring = error.Owner; _contactRequired = true; Visible = false;
    }
}

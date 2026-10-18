using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hollow.Core;
using Hollow.Surface;

namespace HollowUniverse.Surface;

public partial class EnvironmentLifetimeCheck
{
    private async Task CheckContactAtlas()
    {
        using var source = new ProbeSurface(200);
        using var replacementSource = new ProbeSurface(220);
        var snapshots = Enumerable.Range(0, 6).Select(face => ContactAtlasTile(source, new TileKey(face, 0, 0, 0))).ToArray();
        using var atlas = new TerrainContactAtlas(2);
        atlas.UpdateFixture(snapshots, 1);
        Expect("contact atlas stages bounded native grid pairs", !atlas.Ready && atlas.ResidentTiles is >= 1 and <= 2
            && atlas.PendingTiles == snapshots.Length - atlas.ResidentTiles && ContactAtlasPublishedFaces(atlas) == atlas.ResidentTiles,
            $"resident={atlas.ResidentTiles}, pending={atlas.PendingTiles}; actual lookup exposes only completed fine/morph pairs");
        bool bounded = true;
        await Until(() =>
        {
            int before = atlas.ResidentTiles;
            atlas.UpdateFixture(snapshots, 1);
            bounded &= atlas.ResidentTiles - before is >= 0 and <= TerrainContactAtlas.UploadsPerFrame;
            return atlas.Ready;
        }, "contact atlas initial pairs settle");
        Expect("contact atlas readiness waits for every required pair", bounded && atlas.PendingTiles == 0
            && atlas.ResidentTiles == 6 && ContactAtlasPublishedFaces(atlas) == 6,
            "at most two pairs each update; all six native face entries are published before ready");
        long version = atlas.Version, bytes = atlas.UploadedBytes;
        atlas.UpdateFixture(snapshots, 900);
        Expect("unrelated terrain revision does not republish unchanged contact", atlas.Ready && atlas.Version == version
            && atlas.UploadedBytes == bytes, "same actual grids, transforms and membership: no native upload or culling-version churn");

        using (var empty = new TerrainContactAtlas(0))
        {
            empty.UpdateFixture(Array.Empty<DrawnTerrainTile>(), 1);
            Expect("empty drawn contact selection is complete without support", empty.Ready && empty.Failure == null
                && empty.ResidentTiles == 0 && empty.PendingTiles == 0 && ContactAtlasPublishedFaces(empty) == 0,
                "a camera near sea level may be outside the seabed contact reach; no grass is supported and loading can settle");
        }

        float oldAddress = ContactAtlasAddress(atlas, 0), stableAddress = ContactAtlasAddress(atlas, 1);
        snapshots[0] = ContactAtlasTile(replacementSource, snapshots[0].Key);
        atlas.UpdateFixture(snapshots, 901);
        Expect("contact atlas same-address replacement publishes a complete new generation", atlas.Ready
            && ContactAtlasAddress(atlas, 0) != oldAddress && ContactAtlasAddress(atlas, 1) == stableAddress
            && ContactAtlasGridMatches(atlas, snapshots[0]),
            "actual lookup generation changed; both native layer readbacks match the replacement grid; retained neighbor unchanged");

        version = atlas.Version;
        float addressBeforeMove = ContactAtlasAddress(atlas, 0);
        Vector3 shift = new(17, -4, 9);
        DrawnTerrainTile old = snapshots[0];
        snapshots[0] = old with { Transform = new Transform3D(old.Transform.Basis, old.Transform.Origin + shift),
            Bounds = new Aabb(old.Bounds.Position + shift, old.Bounds.Size) };
        atlas.UpdateFixture(snapshots, 902);
        using (Image metadata = ((ImageTexture)atlas.NativeResourcesForFixture()[3]).GetImage())
        {
            int slot = ((int)ContactAtlasAddress(atlas, 0) - 1) % TerrainContactAtlas.Capacity;
            Color stored = metadata.GetPixel(1, slot);
            Expect("contact atlas rebase refreshes metadata and full morph bounds", atlas.Version == version + 1
                && ContactAtlasAddress(atlas, 0) == addressBeforeMove
                && new Vector3(stored.R, stored.G, stored.B).DistanceTo(snapshots[0].Transform.Origin) < 0.0001f
                && atlas.TryBounds(0, Vector2.Zero, Vector2.One, out Aabb bounds)
                && bounds.Position == snapshots[0].Bounds.Position && bounds.End == snapshots[0].Bounds.End,
                "no grid generation change; actual native metadata and CPU culling bounds use the new transform");
        }

        version = atlas.Version;
        DrawnTerrainTile[] subset = snapshots.Skip(1).ToArray();
        atlas.UpdateFixture(subset, 903);
        Expect("contact atlas removal publishes once before slot reuse", atlas.Ready && atlas.ResidentTiles == 5
            && ContactAtlasAddress(atlas, 0) == 0 && atlas.Version == version + 1,
            "one invalidation upload; retained slots do not cause a duplicate final publication");
        atlas.UpdateFixture(Enumerable.Repeat(snapshots[0], TerrainContactAtlas.Capacity + 1).ToArray(), 904);
        Expect("contact atlas capacity refusal exposes no partial prefix", !atlas.Ready && atlas.Failure != null
            && atlas.ResidentTiles == 0 && atlas.PendingTiles == 0 && ContactAtlasPublishedFaces(atlas) == 0,
            "129 snapshot input rejected before admission; native lookup is empty");
        atlas.UpdateFixture(new[] { snapshots[0] }, 905);
        Expect("contact atlas can recover from a changed oversized selection", atlas.Ready && atlas.Failure == null
            && atlas.ResidentTiles == 1 && ContactAtlasGridMatches(atlas, snapshots[0]),
            "capacity refusal is recoverable after the required selection becomes representable");

        foreach (TerrainContactAtlas.FixtureFault fault in new[] { TerrainContactAtlas.FixtureFault.AfterFineLayer,
            TerrainContactAtlas.FixtureFault.AfterMetadataBeforeLookup })
        {
            using var failed = new TerrainContactAtlas(2);
            failed.UpdateFixture(new[] { snapshots[1] }, 1);
            // Adding a distinct address reaches final publication without an earlier retirement publication.
            failed.InjectFixtureFault(fault);
            failed.UpdateFixture(new[] { snapshots[1], snapshots[0] }, 2);
            Expect("contact atlas upload fault fails closed: " + fault, !failed.Ready && failed.Failure != null
                && !failed.PublicationInvalidationFailed && failed.ResidentTiles == 0 && failed.PendingTiles == 0
                && ContactAtlasPublishedFaces(failed) == 0 && failed.NativeResourcesForFixture().All(GodotObject.IsInstanceValid),
                "fault occurs after a real native write; empty lookup replaces all previously published support");
            long stoppedBytes = failed.UploadedBytes;
            failed.UpdateFixture(new[] { snapshots[1] }, 3);
            Expect("contact atlas native failure stops further admission: " + fault,
                !failed.Ready && failed.UploadedBytes == stoppedBytes && failed.ResidentTiles == 0,
                "failure remains observable without retrying uncertain native writes each frame");
        }

        var field = new GrassField { Name = "ContactAtlasRetirementGrass" };
        AddChild(field);
        using var fieldSource = new ProbeSurface(1200);
        var ownedAtlas = new TerrainContactAtlas(2);
        GodotObject[] ownedHandles = ownedAtlas.NativeResourcesForFixture();
        try
        {
            ownedAtlas.UpdateFixture(new[] { snapshots[0] }, 1);
            field.SetupSpatialLifecycleFixture(new GrassProbeSurface(fieldSource), RenderFrame.World);
            field.AttachContactAtlasForFixture(ownedAtlas);
            field.QueueSpatialLifecycleGeneration();
            await Until(() => { field.AdvanceSpatialLifecycleUpload(); return field.IsIdle; }, "contact-owned grass staging completes");
            ownedAtlas.UpdateFixture(Array.Empty<DrawnTerrainTile>(), 2);
            Expect("settled grass remains idle with no nearby terrain support", field.IsIdle && ownedAtlas.Ready
                && ownedAtlas.ResidentTiles == 0 && ContactAtlasPublishedFaces(ownedAtlas) == 0,
                "the real field readiness gate accepts completed empty contact, including deep-water camera locations");
            GodotObject[] spatialHandles = field.SpatialLifecycleNativeResources();
            ownedAtlas.InjectFixtureFault(TerrainContactAtlas.FixtureFault.DuringRetirement);
            field.Stop(); field.Stop();
            Expect("contact retirement failure cannot strand remaining grass resources", field.IsStopped
                && field.CaptureSpatialDiagnostics().ResidentTiles == 0 && field.SpatialLifecycleDirectBatches == 0
                && ownedHandles.All(handle => !GodotObject.IsInstanceValid(handle))
                && spatialHandles.All(handle => !GodotObject.IsInstanceValid(handle)),
                "all four contact resources and subsequent spatial resources retire despite injected contact failure; repeated Stop safe");
        }
        finally
        {
            field.Stop(); field.QueueFree(); ownedAtlas.Dispose();
        }

        GodotObject[] handles = atlas.NativeResourcesForFixture();
        atlas.Dispose(); atlas.Dispose();
        bool stoppedRejected = false;
        try { atlas.UpdateFixture(snapshots, 1000); }
        catch (ObjectDisposedException) { stoppedRejected = true; }
        Expect("contact atlas disposal closes admission and releases every native resource", stoppedRejected && !atlas.Ready
            && atlas.ResidentTiles == 0 && handles.All(handle => !GodotObject.IsInstanceValid(handle)),
            "real texture handles invalid; double disposal is safe");
    }

    private static DrawnTerrainTile ContactAtlasTile(ISurface source, TileKey key)
    {
        TileGrid grid = TileMesh.Build(source, key, TerrainContactAtlas.GridSide - 1);
        Transform3D transform = RenderFrame.World.Anchor(grid.Origin);
        Vector3 minimum = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 maximum = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        foreach (float[] array in new[] { grid.Positions, grid.MorphPositions })
        for (int p = 0; p < array.Length; p += 3)
        {
            Vector3 v = transform * new Vector3(array[p], array[p + 1], array[p + 2]);
            minimum = new Vector3(Math.Min(minimum.X, v.X), Math.Min(minimum.Y, v.Y), Math.Min(minimum.Z, v.Z));
            maximum = new Vector3(Math.Max(maximum.X, v.X), Math.Max(maximum.Y, v.Y), Math.Max(maximum.Z, v.Z));
        }
        return new DrawnTerrainTile(key, grid, transform, new Vector2(1e6f, 2e6f), new Aabb(minimum, maximum - minimum).Grow(0.01f), 1);
    }

    private static float ContactAtlasAddress(TerrainContactAtlas atlas, int face)
    {
        using Image lookup = ((ImageTexture)atlas.NativeResourcesForFixture()[2]).GetImage();
        int side = lookup.GetWidth();
        return lookup.GetPixel(side / 2, face * side + side / 2).R;
    }

    private static int ContactAtlasPublishedFaces(TerrainContactAtlas atlas)
    {
        using Image lookup = ((ImageTexture)atlas.NativeResourcesForFixture()[2]).GetImage();
        int side = lookup.GetWidth(), count = 0;
        for (int face = 0; face < 6; face++) if (lookup.GetPixel(side / 2, face * side + side / 2).R != 0) count++;
        return count;
    }

    private static bool ContactAtlasGridMatches(TerrainContactAtlas atlas, DrawnTerrainTile tile)
    {
        GodotObject[] handles = atlas.NativeResourcesForFixture();
        int slot = ((int)ContactAtlasAddress(atlas, tile.Key.Face) - 1) % TerrainContactAtlas.Capacity;
        if (slot < 0) return false;
        using Image fine = ((Texture2DArray)handles[0]).GetLayerData(slot);
        using Image morph = ((Texture2DArray)handles[1]).GetLayerData(slot);
        for (int y = 0; y < TerrainContactAtlas.GridSide; y++)
        for (int x = 0; x < TerrainContactAtlas.GridSide; x++)
        {
            int p = (y * TerrainContactAtlas.GridSide + x) * 3;
            Color f = fine.GetPixel(x, y), m = morph.GetPixel(x, y);
            if (f.R != tile.Grid.Positions[p] || f.G != tile.Grid.Positions[p + 1] || f.B != tile.Grid.Positions[p + 2]
                || m.R != tile.Grid.MorphPositions[p] || m.G != tile.Grid.MorphPositions[p + 1] || m.B != tile.Grid.MorphPositions[p + 2]) return false;
        }
        return true;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Hollow.Core;
using Hollow.Surface;
using HollowUniverse.Settings;

namespace HollowUniverse.Surface;

public partial class EnvironmentLifetimeCheck
{
    private async Task CheckTerrainContact()
    {
        CheckSyntheticTerrainContact();
        var terrain = new TerrainLod { Name = "ContactFixtureTerrain", Hurry = true };
        var terrainParent = new Node3D { Name = "ContactTerrainOnlyAncestor" };
        AddChild(terrainParent); terrainParent.AddChild(terrain);
        var sources = new List<ProbeSurface>();
        var retired = new List<Task>();
        using var shader = new Shader { Code = """
            shader_type spatial;
            instance uniform vec2 morph_range = vec2(1e12, 2e12);
            instance uniform int tile_face = 0;
            void vertex() {
                vec3 fine = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
                float k = clamp((length(fine-CAMERA_POSITION_WORLD)-morph_range.x) / max(morph_range.y-morph_range.x,1.0),0.0,1.0);
                VERTEX = mix(VERTEX,CUSTOM0.xyz,k);
            }
            void fragment() { ALBEDO = vec3(0.3,0.4,0.2); }
            """ };
        using var material = new ShaderMaterial { Shader = shader };
        var records = new List<DrawnTerrainTile>();
        var frame = new RenderFrame(new Vector3D(200, 0, 0));
        Vector3D distant = new(100_000, 0, 0), close = new(201.7, 0, 0);
        try
        {
            var source = new ProbeSurface(200);
            sources.Add(source);
            terrain.Setup(source, frame, material, null);
            await Until(() => { terrain.UpdateLod(distant); return terrain.IsIdle; }, "contact coarse terrain settles");
            bool coarse = terrain.CopyContactTiles(distant, 200_000, records);
            Expect("terrain contact snapshots actual coarse drawn roots", coarse && records.Count == CubeSphere.FaceCount
                && records.All(tile => tile.Key.Level == 0 && tile.Grid.Cells == 32 && tile.Grid.VertexCount == 1089
                    && tile.Grid.Positions.Length == 3267 && tile.ContactRevision == terrain.ContactRevision)
                && records.Select(tile => tile.Key.Packed).SequenceEqual(records.Select(tile => tile.Key.Packed).OrderBy(value => value)),
                "six sorted regular grids; no skirt vertices, worker grids or hidden descendants");
            if (!coarse || records.Count == 0) throw new InvalidOperationException("No coarse terrain contact fixture snapshot.");
            DrawnTerrainTile originalRoot = records[0];
            bool overflow = terrain.CopyContactTiles(distant, 200_000, records, maxTiles: 5);
            Expect("terrain contact capacity fails without a partial prefix", !overflow && records.Count == 0,
                "six required roots cannot be represented by a five-tile request");

            await Until(() => { terrain.UpdateLod(close); return terrain.IsIdle; }, "contact nearby terrain settles");
            bool fine = terrain.CopyContactTiles(close, 60, records);
            bool parentOverlap = records.Any(a => records.Any(b => ContactAncestor(a.Key, b.Key)));
            Expect("terrain contact exposes drawn descendants without hidden ancestors", fine && records.Count > 0
                && records.Count <= TerrainLod.ContactTileCapacity && records.Any(tile => tile.Key.Level == terrain.MaxLevel)
                && records.Count <= terrain.DrawnTiles && !parentOverlap,
                $"copied={records.Count}, drawn={terrain.DrawnTiles}, finest={terrain.FinestLevel}; all-or-fail capacity=128");
            if (!fine || records.Count == 0) throw new InvalidOperationException("No fine terrain contact fixture snapshot.");
            Expect("terrain contact bounds contain every fine and morph endpoint", records.All(ContactBoundsEncloseGrid),
                "actual instance transforms and both complete 33x33 regular endpoint arrays, excluding skirts");
            using (var atlas = new TerrainContactAtlas(terrain.MaxLevel))
            {
                foreach (Node3D hidden in new Node3D[] { terrain, terrainParent })
                {
                    await Until(() => { atlas.Update(terrain, close, 60); return atlas.Ready; }, "visible terrain contact publishes");
                    long visibleVersion = atlas.Version;
                    hidden.Visible = false;
                    bool hiddenCopy = terrain.CopyContactTiles(close, 60, records);
                    atlas.Update(terrain, close, 60);
                    bool published = atlas.TryCopyPublishedTiles(records, out _);
                    Expect("hidden " + hidden.Name + " removes all published contact", !hiddenCopy && !published
                        && !atlas.Ready && atlas.ResidentTiles == 0 && records.Count == 0 && atlas.Version != visibleVersion,
                        "effective scene visibility invalidates support even without a terrain-grid revision");
                    hidden.Visible = true;
                    await Until(() => { atlas.Update(terrain, close, 60); return atlas.Ready; }, "restored terrain contact republishes");
                    bool restored = atlas.TryCopyPublishedTiles(records, out _);
                    Expect("restored " + hidden.Name + " recovers complete contact", restored && records.Count > 0
                        && atlas.Failure == null && atlas.PendingTiles == 0,
                        "same grids recover through ordinary bounded upload without a permanent fault");
                }
            }
            terrain.CopyContactTiles(close, 60, records);
            DrawnTerrainTile previous = records.Where(tile => tile.Key.Level > 0)
                .OrderBy(tile => (tile.Bounds.Position + tile.Bounds.Size * 0.5f - frame.ToRender(close)).LengthSquared()).First();
            float oldDetail = GraphicsSettings.Current.TerrainDetail;
            try
            {
                GraphicsSettings.Current.TerrainDetail = oldDetail < 1.2f ? 1.6f : 0.6f;
                bool copied = terrain.CopyContactTiles(close, 60, records);
                DrawnTerrainTile same = records.Single(tile => tile.Key.Equals(previous.Key));
                Expect("terrain contact preserves the range actually assigned to the instance", copied
                    && same.MorphRange == previous.MorphRange && ReferenceEquals(same.Grid, previous.Grid),
                    "changing current terrain detail cannot recompute an existing draw's stored morph range");
            }
            finally { GraphicsSettings.Current.TerrainDetail = oldDetail; }

            Vector2 faceUv = ContactFaceCentre(previous.Key);
            bool oldSample = TerrainLod.TrySampleContact(previous, previous.Key, faceUv, frame.ToRender(close), out TerrainContactSample before);
            Vector3 shift = frame.Move(frame.Origin + new Vector3D(7, 3, -2));
            terrain.Reanchor();
            bool rebased = terrain.CopyContactTiles(close, 60, records);
            DrawnTerrainTile current = records.Single(tile => tile.Key.Equals(previous.Key));
            bool newSample = TerrainLod.TrySampleContact(current, current.Key, faceUv, frame.ToRender(close), out TerrainContactSample after);
            Expect("terrain contact rebase refreshes transforms and revisions without changing grids", rebased && oldSample && newSample
                && ReferenceEquals(previous.Grid, current.Grid) && current.ContactRevision != previous.ContactRevision
                && current.ContactRevision == terrain.ContactRevision
                && ContactNear(current.Transform.Origin, previous.Transform.Origin - shift, 0.0002f)
                && ContactNear(after.RenderPosition + shift, before.RenderPosition, 0.0003f)
                && after.GeometricNormal.Dot(before.GeometricNormal) > 0.9999f,
                "morph distance is invariant when both the drawn transform and camera are rebased; previous snapshots remain historical");

            retired.Add(terrain.WorkersStopped);
            terrain.Stop();
            bool stopped = terrain.CopyContactTiles(close, 60, records);
            Expect("terrain contact stop invalidates old snapshots and clears drawn sources", stopped && records.Count == 0
                && terrain.ContactRevision != current.ContactRevision, "a retained record owns only its frozen grid and value snapshot");
            var replacement = new ProbeSurface(260);
            sources.Add(replacement);
            terrain.Setup(replacement, RenderFrame.World, material, null);
            await Until(() => { terrain.UpdateLod(distant); return terrain.IsIdle; }, "replacement contact terrain settles");
            bool replaced = terrain.CopyContactTiles(distant, 200_000, records);
            DrawnTerrainTile replacementRoot = records.Single(tile => tile.Key.Equals(originalRoot.Key));
            Expect("terrain contact same-address replacement excludes stale world grids", replaced && records.Count == 6
                && records.All(tile => tile.Grid.Radius == 260 && !ReferenceEquals(tile.Grid, previous.Grid)
                    && tile.ContactRevision != previous.ContactRevision)
                && !ReferenceEquals(originalRoot.Grid, replacementRoot.Grid), "replacement setup publishes new immutable grids even at the same tile address");
        }
        finally
        {
            foreach (ProbeSurface source in sources) source.Continue.Set();
            retired.Add(terrain.WorkersStopped);
            terrain.Stop(); terrainParent.QueueFree();
            Task drained = Task.WhenAll(retired);
            await Task.WhenAny(drained, Task.Delay(TimeSpan.FromSeconds(25)));
            Expect("terrain contact fixture workers drain", drained.IsCompletedSuccessfully, "all contact fixture owners retired");
            if (drained.IsCompleted) foreach (ProbeSurface source in sources) source.Dispose();
        }
    }

    private void CheckSyntheticTerrainContact()
    {
        using var source = new ProbeSurface(200);
        var key = new TileKey(0, 0, 0, 0);
        TileGrid grid = TileMesh.Build(source, key, TerrainLod.ContactCells);
        // Fixture-only edits happen before publication. Production contact records never mutate grid arrays.
        for (int j = 0; j <= 32; j++)
        for (int i = 0; i <= 32; i++)
        {
            int offset = (j * 33 + i) * 3;
            float height = 2 * i + 7 * j + 11 * i * j;
            grid.Positions[offset] = grid.MorphPositions[offset] = i;
            grid.Positions[offset + 1] = height; grid.MorphPositions[offset + 1] = height + 10;
            grid.Positions[offset + 2] = grid.MorphPositions[offset + 2] = j;
        }
        var tile = new DrawnTerrainTile(key, grid, Transform3D.Identity, new Vector2(100_000, 100_001),
            new Aabb(Vector3.Zero, new Vector3(32, 11562, 32)), 1);
        Vector2 lowerUv = new(0.75f / 32, 0.25f / 32), upperUv = new(0.25f / 32, 0.75f / 32);
        bool lowerOk = TerrainLod.TrySampleContact(tile, key, lowerUv, Vector3.Zero, out TerrainContactSample lower);
        bool upperOk = TerrainLod.TrySampleContact(tile, key, upperUv, Vector3.Zero, out TerrainContactSample upper);
        Expect("terrain contact selects both actual grid diagonals", lowerOk && upperOk && lower.GridTriangle == 0 && upper.GridTriangle == 1
            && ContactNear(lower.RenderPosition, new Vector3(0.75f, 6, 0.25f), 0.00001f)
            && ContactNear(upper.RenderPosition, new Vector3(0.25f, 8.5f, 0.75f), 0.00001f),
            "a-b-d and a-d-c barycentrics reproduce the regular-grid index topology");
        bool endOk = TerrainLod.TrySampleContact(tile, key, Vector2.One, Vector3.Zero, out TerrainContactSample end);
        bool firstOk = TerrainLod.TrySampleContact(tile, key, Vector2.Zero, Vector3.Zero, out TerrainContactSample first);
        Expect("terrain contact accepts exact face endpoints without index overflow", firstOk && endOk
            && ContactNear(first.RenderPosition, Vector3.Zero, 0.00001f)
            && ContactNear(end.RenderPosition, new Vector3(32, 11552, 32), 0.001f), "UV=0 and UV=1 resolve onto valid edge vertices");
        var full = tile with { MorphRange = new Vector2(-1, 0) };
        bool fullOk = TerrainLod.TrySampleContact(full, key, lowerUv, Vector3.Zero, out TerrainContactSample fullSample);
        Expect("terrain contact reaches both morph extremes", lowerOk && lower.VertexMorph == Vector3.Zero && fullOk
            && fullSample.VertexMorph == Vector3.One && ContactNear(fullSample.RenderPosition, new Vector3(0.75f, 16, 0.25f), 0.00001f),
            "k=0 uses fine vertices and k=1 uses every parent target");
        var mixed = tile with { MorphRange = new Vector2(0, 30) };
        bool mixedOk = TerrainLod.TrySampleContact(mixed, key, lowerUv, Vector3.Zero, out TerrainContactSample mixedSample);
        float kb = MathF.Sqrt(5) / 30, kd = MathF.Sqrt(402) / 30;
        Vector3 expected = new(0.75f, 6 + 10 * (0.5f * kb + 0.25f * kd), 0.25f);
        Vector3 expectedNormal = new Vector3(1, 2 + 10 * kb, 0).Cross(new Vector3(1, 20 + 10 * kd, 1)).Normalized();
        float wrongSingleMorph = new Vector3(0.75f, 6, 0.25f).Length() / 30;
        Expect("terrain contact morphs individual fine vertices before interpolation", mixedOk
            && ContactNear(mixedSample.VertexMorph, new Vector3(0, kb, kd), 0.00001f)
            && ContactNear(mixedSample.RenderPosition, expected, 0.00001f)
            && mixedSample.GeometricNormal.Dot(expectedNormal) > 0.99999f
            && Math.Abs(mixedSample.RenderPosition.Y - (6 + 10 * wrongSingleMorph)) > 0.01f,
            "nonuniform corner k values and the geometric normal differ from morphing one already-interpolated point");
        var transform = new Transform3D(Basis.Identity.Scaled(Vector3.One * 2), new Vector3(10, 3, -4));
        var moved = mixed with { Transform = transform };
        bool movedOk = TerrainLod.TrySampleContact(moved, key, lowerUv, transform.Origin, out TerrainContactSample movedSample);
        Vector3 movedExpected = transform * new Vector3(0.75f, 6 + 10 * (0.5f * MathF.Sqrt(20) / 30 + 0.25f), 0.25f);
        Expect("terrain contact distance uses transformed fine vertices", movedOk
            && ContactNear(movedSample.RenderPosition, movedExpected, 0.00002f), "actual draw transform affects distance before local mixing");
        TileGrid small = TileMesh.Build(source, key, 16);
        Expect("terrain contact rejects wrong addresses, invalid UV and unsupported grids",
            !TerrainLod.TrySampleContact(tile, key.Child(0), lowerUv, Vector3.Zero, out _)
            && !TerrainLod.TrySampleContact(tile, key, new Vector2(-0.01f, 0.5f), Vector3.Zero, out _)
            && !TerrainLod.TrySampleContact(tile, key, new Vector2(float.NaN, 0.5f), Vector3.Zero, out _)
            && !TerrainLod.TrySampleContact(tile with { Grid = small }, key, lowerUv, Vector3.Zero, out _),
            "invalid contact addresses never sample an unrelated grid or a silently rescaled cell count");

        DrawnTerrainTile Built(TileKey address)
        {
            TileGrid built = TileMesh.Build(source, address, 32);
            return new DrawnTerrainTile(address, built, RenderFrame.World.Anchor(built.Origin), tile.MorphRange, default, 2);
        }
        DrawnTerrainTile left = Built(new TileKey(0, 1, 0, 0)), right = Built(new TileKey(0, 1, 1, 0));
        bool leftOk = TerrainLod.TrySampleContact(left, left.Key, new Vector2(0.5f, 0.2f), Vector3.Zero, out TerrainContactSample seamLeft);
        bool rightOk = TerrainLod.TrySampleContact(right, right.Key, new Vector2(0.5f, 0.2f), Vector3.Zero, out TerrainContactSample seamRight);
        DrawnTerrainTile faceX = Built(key), faceZ = Built(new TileKey(5, 0, 0, 0));
        bool xOk = TerrainLod.TrySampleContact(faceX, faceX.Key, new Vector2(1, 0.375f), Vector3.Zero, out TerrainContactSample seamX);
        bool zOk = TerrainLod.TrySampleContact(faceZ, faceZ.Key, new Vector2(0, 0.375f), Vector3.Zero, out TerrainContactSample seamZ);
        Expect("terrain contact preserves tile and cube-face seams", leftOk && rightOk && xOk && zOk
            && ContactNear(seamLeft.RenderPosition, seamRight.RenderPosition, 0.001f)
            && ContactNear(seamX.RenderPosition, seamZ.RenderPosition, 0.001f),
            "explicit adjacent addresses sample the same regular-grid edge; skirts are never used as support");
    }

    private static Vector2 ContactFaceCentre(TileKey key)
    {
        double count = 1L << key.Level;
        return new Vector2((float)((key.X + 0.5) / count), (float)((key.Y + 0.5) / count));
    }

    private static bool ContactAncestor(TileKey possible, TileKey child)
    {
        if (possible.Face != child.Face || possible.Level >= child.Level) return false;
        int shift = child.Level - possible.Level;
        return possible.X == (child.X >> shift) && possible.Y == (child.Y >> shift);
    }

    private static bool ContactNear(Vector3 a, Vector3 b, float tolerance) => a.DistanceTo(b) <= tolerance;

    private static bool ContactBoundsEncloseGrid(DrawnTerrainTile tile)
    {
        Vector3 low = tile.Bounds.Position, high = tile.Bounds.End;
        foreach (float[] values in new[] { tile.Grid.Positions, tile.Grid.MorphPositions })
        for (int offset = 0; offset < values.Length; offset += 3)
        {
            Vector3 point = tile.Transform * new Vector3(values[offset], values[offset + 1], values[offset + 2]);
            if (point.X < low.X || point.Y < low.Y || point.Z < low.Z || point.X > high.X || point.Y > high.Y || point.Z > high.Z)
                return false;
        }
        return true;
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Hollow.Core;
using Hollow.Surface;

namespace HollowUniverse.Surface;

public partial class EnvironmentLifetimeCheck
{
    private const int ContactGpuWidth = 64, ContactGpuHeight = 8;
    private const float ContactGpuPositionTolerance = 0.0003f, ContactGpuNormalTolerance = 0.0025f;
    private readonly record struct ContactGpuQuery(string Name, int Face, Vector2 Uv, Vector3 Hint,
        bool FromHint, bool Supported, Vector3 Position, Vector3 Normal);

    // Opt-in actual-renderer oracle. The shader includes the production helper; it only selects queries and
    // encodes the result. CPU expected positions are encoding pivots, not alternate GPU contact calculations.
    private async Task CheckTerrainContactGpu()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        using var source = new ProbeSurface(200);
        var frame = new RenderFrame(new Vector3D(200, 0, 0));
        long owner = PlanetGlobals.Claim(frame, source.Radius);
        SubViewport? viewport = null;
        ColorRect? rectangle = null;
        Shader? shader = null;
        ShaderMaterial? material = null;
        TerrainContactAtlas? atlas = null;
        ImageTexture? staleLookup = null;
        try
        {
            shader = new Shader { Code = ContactGpuShader };
            material = new ShaderMaterial { Shader = shader };
            atlas = new TerrainContactAtlas(1);
            atlas.Bind(material);
            viewport = new SubViewport
            {
                Name = "TerrainContactGpuOracle", Size = new Vector2I(ContactGpuWidth, ContactGpuHeight),
                Disable3D = true, UseHdr2D = true, TransparentBg = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            };
            AddChild(viewport);
            rectangle = new ColorRect
            {
                Size = new Vector2(ContactGpuWidth, ContactGpuHeight), Material = material,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            viewport.AddChild(rectangle);

            TileGrid synthetic = ContactGpuSyntheticGrid(source, new TileKey(0, 0, 0, 0));
            Basis basis = frame.Basis * Basis.Identity.Scaled(new Vector3(1.2f, 0.9f, 1.1f));
            Transform3D transform = new(basis, frame.ToRender(synthetic.Origin));
            var tile = new DrawnTerrainTile(synthetic.Key, synthetic, transform, new Vector2(0, 4),
                ContactGpuBounds(synthetic, transform), 1);
            int centre = (16 * 33 + 16) * 3;
            Vector3 camera = transform * new Vector3(synthetic.Positions[centre], synthetic.Positions[centre + 1],
                synthetic.Positions[centre + 2]) + new Vector3(0.2f, 0.3f, -0.1f);
            Vector2 lower = new(16.75f / 32, 16.25f / 32), upper = new(16.25f / 32, 16.75f / 32);
            TerrainLod.TrySampleContact(tile, tile.Key, lower, camera, out TerrainContactSample nonuniform);
            Expect("terrain GPU fixture exercises unequal vertex morph factors",
                Math.Abs(nonuniform.VertexMorph.X - nonuniform.VertexMorph.Y) > 0.01f
                && Math.Abs(nonuniform.VertexMorph.Y - nonuniform.VertexMorph.Z) > 0.01f,
                $"k={nonuniform.VertexMorph}; transformed fine vertices own their individual distance");
            await PublishContactGpu(atlas, new[] { tile }, 1, deadline.Token);
            var syntheticQueries = new[]
            {
                ContactGpuSample("lower diagonal", tile, lower, camera),
                ContactGpuSample("upper diagonal", tile, upper, camera),
                ContactGpuSample("diagonal equality", tile, new Vector2(16.5f / 32, 16.5f / 32), camera),
                ContactGpuSample("cell edge", tile, new Vector2(17f / 32, 16.3f / 32), camera),
                ContactGpuSample("face first vertex", tile, Vector2.Zero, camera),
                ContactGpuSample("face final vertex", tile, Vector2.One, camera),
                ContactGpuSample("face final column", tile, new Vector2(1, 0.625f), camera),
                ContactGpuMissing("unpublished face", 4, new Vector2(0.5f, 0.5f)),
                ContactGpuMissing("outside face", 0, new Vector2(-0.01f, 0.5f)),
            };
            await ReadContactGpu("nonuniform morph and boundaries", viewport, material, camera, syntheticQueries, deadline.Token);

            Vector3 shift = frame.Move(frame.Origin + new Vector3D(11, 7, -5));
            PlanetGlobals.Set(owner, frame, source.Radius);
            camera -= shift;
            transform = new Transform3D(basis, frame.ToRender(synthetic.Origin));
            tile = tile with { Transform = transform, Bounds = ContactGpuBounds(synthetic, transform), ContactRevision = 2 };
            await PublishContactGpu(atlas, new[] { tile }, 2, deadline.Token);
            await ReadContactGpu("rebase retains exact drawn contact", viewport, material, camera,
                new[] { ContactGpuSample("rebased lower", tile, lower, camera), ContactGpuSample("rebased upper", tile, upper, camera) },
                deadline.Token);

            // Real cube-sphere grids establish face mapping independently of the synthetic planar fixture.
            using var sphere = new ProbeSurface(8);
            PlanetGlobals.Set(owner, RenderFrame.World, sphere.Radius);
            DrawnTerrainTile faceX = ContactGpuSphereTile(sphere, new TileKey(0, 0, 0, 0), 3);
            DrawnTerrainTile faceZ = ContactGpuSphereTile(sphere, new TileKey(5, 0, 0, 0), 3);
            camera = new Vector3(8.1f, 0.1f, -0.1f);
            await PublishContactGpu(atlas, new[] { faceX, faceZ }, 3, deadline.Token);
            ContactGpuQuery[] seamQueries = ContactGpuSeamQueries(faceX, faceZ, camera);
            await ReadContactGpu("cube face seam and footprint mapping", viewport, material, camera, seamQueries, deadline.Token);

            // Preserve a real published lookup, then reuse the same address/slot with a new immutable grid.
            // Rebinding the old lookup tests the generation guard even though face and tile bounds still match.
            Texture2D publishedLookup = material.GetShaderParameter("terrain_contact_lookup").AsGodotObject() as Texture2D
                ?? throw new InvalidOperationException("Contact lookup binding is missing.");
            using (Image oldMap = publishedLookup.GetImage()) staleLookup = ImageTexture.CreateFromImage(oldMap);
            DrawnTerrainTile replacement = ContactGpuSphereTile(sphere, faceX.Key, 4);
            for (int i = 0; i < replacement.Grid.Positions.Length; i += 3)
            {
                replacement.Grid.Positions[i] += 0.25f;
                replacement.Grid.MorphPositions[i] += 0.25f;
            }
            replacement = replacement with { Bounds = ContactGpuBounds(replacement.Grid, replacement.Transform) };
            await PublishContactGpu(atlas, new[] { replacement, faceZ }, 4, deadline.Token);
            ContactGpuQuery replaced = ContactGpuSample("replacement is current", replacement, new Vector2(0.4f, 0.6f), camera);
            ContactGpuQuery retained = ContactGpuSample("unchanged neighboring slot", faceZ, new Vector2(0.4f, 0.6f), camera);
            await ReadContactGpu("replacement lookup publication", viewport, material, camera, new[] { replaced, retained }, deadline.Token);
            material.SetShaderParameter("terrain_contact_lookup", staleLookup);
            await ReadContactGpu("stale lookup refuses reused slot", viewport, material, camera,
                new[] { replaced with { Name = "stale same-address slot", Supported = false }, retained }, deadline.Token);
            atlas.Bind(material);
            staleLookup.Dispose(); staleLookup = null;

            await CheckContactGpuPackedAddresses(atlas, viewport, material, camera, new[] { replaced, retained }, deadline.Token);

            DrawnTerrainTile left = ContactGpuSphereTile(sphere, new TileKey(0, 1, 0, 0), 5);
            DrawnTerrainTile right = ContactGpuSphereTile(sphere, new TileKey(0, 1, 1, 0), 5);
            await PublishContactGpu(atlas, new[] { left, right, faceZ }, 5, deadline.Token);
            await ReadContactGpu("non-root tile addresses and shared edge", viewport, material, camera, new[]
            {
                ContactGpuSample("left child interior", left, new Vector2(0.2f, 0.3f), camera),
                ContactGpuSample("right child interior", right, new Vector2(0.7f, 0.3f), camera),
                ContactGpuSample("shared tile edge belongs to right", right, new Vector2(0.5f, 0.25f), camera),
                ContactGpuSample("right child final face column", right, new Vector2(1, 0.375f), camera),
                ContactGpuMissing("unpublished upper child", 0, new Vector2(0.75f, 0.75f)),
            }, deadline.Token);

            atlas.UpdateFixture(Array.Empty<DrawnTerrainTile>(), 6);
            await ReadContactGpu("empty publication has no fallback", viewport, material, camera,
                new[] { replaced with { Supported = false }, retained with { Supported = false } }, deadline.Token);
        }
        finally
        {
            // Detach every owner before native atlas disposal; no finalizer or forced collection is part of success.
            if (rectangle != null && GodotObject.IsInstanceValid(rectangle)) rectangle.Material = null;
            if (material != null && GodotObject.IsInstanceValid(material))
                foreach (string name in new[] { "terrain_contact_fine", "terrain_contact_morph", "terrain_contact_lookup", "terrain_contact_meta", "fixture_queries" })
                    material.SetShaderParameter(name, default(Variant));
            staleLookup?.Dispose(); atlas?.Dispose(); material?.Dispose(); shader?.Dispose();
            if (viewport != null && GodotObject.IsInstanceValid(viewport))
            {
                viewport.QueueFree();
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { while (GodotObject.IsInstanceValid(viewport)) await ContactGpuNextDraw(cleanup.Token); }
                catch (OperationCanceledException) { }
                Expect("terrain GPU oracle retires its native viewport", !GodotObject.IsInstanceValid(viewport),
                    "queued destruction checked with a separate two-second cleanup deadline");
            }
            PlanetGlobals.Clear(owner);
        }
    }

    private static async Task PublishContactGpu(TerrainContactAtlas atlas, IReadOnlyList<DrawnTerrainTile> tiles,
        long revision, CancellationToken cancellation)
    {
        for (int attempt = 0; attempt < TerrainContactAtlas.Capacity + 1; attempt++)
        {
            cancellation.ThrowIfCancellationRequested();
            atlas.UpdateFixture(tiles, revision);
            if (atlas.Failure != null) throw new InvalidOperationException(atlas.Failure);
            if (atlas.Ready) return;
            await ContactGpuNextDraw(cancellation);
        }
        throw new InvalidOperationException("Bounded contact fixture never completed admission.");
    }

    private async Task CheckContactGpuPackedAddresses(TerrainContactAtlas atlas, SubViewport viewport,
        ShaderMaterial material, Vector3 camera, IReadOnlyList<ContactGpuQuery> queries, CancellationToken cancellation)
    {
        Texture2D lookup = material.GetShaderParameter("terrain_contact_lookup").AsGodotObject() as Texture2D
            ?? throw new InvalidOperationException("Contact lookup is missing at the packed-address boundary check.");
        Texture2D metadata = material.GetShaderParameter("terrain_contact_meta").AsGodotObject() as Texture2D
            ?? throw new InvalidOperationException("Contact metadata is missing at the packed-address boundary check.");
        // Generation changes only: the production arrays, key, transform and CPU expectations remain identical.
        // The live slot-zero address crosses 2^23+1 here, where adding 0.5 to an exact float integer is incorrect.
        foreach (int generation in new[] { 65535, 65536, 131070 })
        {
            using Image map = lookup.GetImage(), meta = metadata.GetImage();
            for (int y = 0; y < map.GetHeight(); y++)
            for (int x = 0; x < map.GetWidth(); x++)
            {
                int address = (int)map.GetPixel(x, y).R - 1;
                if (address < 0) continue;
                int slot = address % TerrainContactAtlas.Capacity;
                map.SetPixel(x, y, new Color(generation * TerrainContactAtlas.Capacity + slot + 1, 0, 0, 1));
            }
            for (int slot = 0; slot < TerrainContactAtlas.Capacity; slot++)
            {
                if (meta.GetPixel(3, slot).A < 0.5f) continue;
                Color row = meta.GetPixel(4, slot);
                row.A = generation;
                meta.SetPixel(4, slot, row);
            }
            using ImageTexture mapTexture = ImageTexture.CreateFromImage(map), metaTexture = ImageTexture.CreateFromImage(meta);
            try
            {
                material.SetShaderParameter("terrain_contact_lookup", mapTexture);
                material.SetShaderParameter("terrain_contact_meta", metaTexture);
                await ReadContactGpu($"exact packed RF generation {generation}", viewport, material, camera, queries, cancellation);
            }
            finally { atlas.Bind(material); }
        }
    }

    private async Task ReadContactGpu(string stage, SubViewport viewport, ShaderMaterial material, Vector3 camera,
        IReadOnlyList<ContactGpuQuery> queries, CancellationToken cancellation)
    {
        if (queries.Count >= ContactGpuWidth) throw new ArgumentOutOfRangeException(nameof(queries));
        var values = new float[ContactGpuWidth * 3 * 4];
        for (int i = 0; i < queries.Count; i++)
        {
            ContactGpuQuery query = queries[i];
            Put(i, 0, new Vector4(query.Face, query.Uv.X, query.Uv.Y, query.FromHint ? 1 : 0));
            Put(i, 1, new Vector4(query.Hint.X, query.Hint.Y, query.Hint.Z, 0));
            Put(i, 2, new Vector4(query.Position.X, query.Position.Y, query.Position.Z, 0));
        }
        var bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        using Image input = Image.CreateFromData(ContactGpuWidth, 3, false, Image.Format.Rgbaf, bytes);
        using ImageTexture texture = ImageTexture.CreateFromImage(input);
        try
        {
            material.SetShaderParameter("fixture_queries", texture);
            material.SetShaderParameter("fixture_count", queries.Count);
            material.SetShaderParameter("fixture_camera", camera);
            viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
            for (int frame = 0; frame < 3; frame++) await ContactGpuNextDraw(cancellation);
            cancellation.ThrowIfCancellationRequested();
            using Image readback = viewport.GetTexture().GetImage();
            bool hdr = readback.GetFormat() is Image.Format.Rgbah or Image.Format.Rgbaf;
            Color firstMarker = readback.GetPixel(ContactGpuWidth - 1, 1), secondMarker = readback.GetPixel(ContactGpuWidth - 1, 5);
            // Identify rows through shader markers, so the oracle does not assume a backend's readback Y direction.
            int positionRow = Marker(firstMarker, 0.125f, 0.375f, 0.625f) ? 1 : 5;
            int normalRow = positionRow == 1 ? 5 : 1;
            bool calibrated = Marker(positionRow == 1 ? firstMarker : secondMarker, 0.125f, 0.375f, 0.625f)
                && Marker(normalRow == 1 ? firstMarker : secondMarker, 0.25f, 0.5f, 0.75f);
            Expect("terrain GPU readback calibration: " + stage, hdr && calibrated,
                $"format={readback.GetFormat()}, row markers={firstMarker}/{secondMarker}; offscreen HDR uses linear channels");
            float maximumPositionError = 0, maximumNormalError = 0;
            var failures = new List<string>();
            for (int i = 0; i < queries.Count; i++)
            {
                ContactGpuQuery query = queries[i];
                Color position = readback.GetPixel(i, positionRow), normal = readback.GetPixel(i, normalRow);
                bool supported = position.A > 0.5f;
                if (supported != query.Supported || (normal.A > 0.5f) != query.Supported)
                { failures.Add(query.Name + ": support mask differs"); continue; }
                if (!query.Supported) continue;
                Vector3 residual = (new Vector3(position.R, position.G, position.B) - Vector3.One * 0.5f) / 16;
                Vector3 gpuNormal = new Vector3(normal.R, normal.G, normal.B) * 2 - Vector3.One;
                float error = residual.Length(), normalError = gpuNormal.DistanceTo(query.Normal);
                maximumPositionError = Math.Max(maximumPositionError, error);
                maximumNormalError = Math.Max(maximumNormalError, normalError);
                if (!float.IsFinite(error) || !float.IsFinite(normalError)
                    || error > ContactGpuPositionTolerance || normalError > ContactGpuNormalTolerance)
                    failures.Add($"{query.Name}: position residual={residual}, normal error={normalError:R}");
            }
            Expect("terrain GPU agrees with CPU: " + stage, hdr && calibrated && failures.Count == 0,
                $"{queries.Count} samples; max position error={maximumPositionError:R} m (limit {ContactGpuPositionTolerance:R}); "
                + $"max normal-vector error={maximumNormalError:R} (limit {ContactGpuNormalTolerance:R}); "
                + (failures.Count == 0 ? "all support masks match" : string.Join("; ", failures)));
        }
        finally { material.SetShaderParameter("fixture_queries", default(Variant)); }

        void Put(int column, int row, Vector4 value)
        {
            int offset = (row * ContactGpuWidth + column) * 4;
            values[offset] = value.X; values[offset + 1] = value.Y; values[offset + 2] = value.Z; values[offset + 3] = value.W;
        }
        static bool Marker(Color colour, float r, float g, float b) => Math.Abs(colour.R - r) < 0.001f
            && Math.Abs(colour.G - g) < 0.001f && Math.Abs(colour.B - b) < 0.001f && colour.A > 0.99f;
    }

    private static async Task ContactGpuNextDraw(CancellationToken cancellation)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Drawn() => completion.TrySetResult(true);
        RenderingServer.FramePostDraw += Drawn;
        using CancellationTokenRegistration registration = cancellation.Register(() => completion.TrySetCanceled(cancellation));
        try { await completion.Task; }
        finally { RenderingServer.FramePostDraw -= Drawn; }
    }

    private static ContactGpuQuery ContactGpuSample(string name, DrawnTerrainTile tile, Vector2 uv, Vector3 camera)
    {
        if (!TerrainLod.TrySampleContact(tile, tile.Key, uv, camera, out TerrainContactSample sample))
            throw new InvalidOperationException("CPU contact fixture rejected " + name);
        return new(name, tile.Key.Face, uv, Vector3.Zero, false, true, sample.RenderPosition, sample.GeometricNormal);
    }

    private static ContactGpuQuery ContactGpuMissing(string name, int face, Vector2 uv) =>
        new(name, face, uv, Vector3.Zero, false, false, Vector3.Zero, Vector3.Zero);

    private static TileGrid ContactGpuSyntheticGrid(ProbeSurface source, TileKey key)
    {
        TileGrid grid = TileMesh.Build(source, key, 32);
        for (int j = 0; j <= 32; j++)
        for (int i = 0; i <= 32; i++)
        {
            int p = (j * 33 + i) * 3;
            float height = 0.02f * i + 0.03f * j + 0.002f * i * j;
            grid.Positions[p] = height; grid.MorphPositions[p] = height + 0.3f + 0.2f * (i & 1) + 0.15f * (j & 1);
            grid.Positions[p + 1] = grid.MorphPositions[p + 1] = j - 16;
            grid.Positions[p + 2] = grid.MorphPositions[p + 2] = 16 - i;
        }
        return grid;
    }

    private static DrawnTerrainTile ContactGpuSphereTile(ProbeSurface source, TileKey key, long revision)
    {
        TileGrid grid = TileMesh.Build(source, key, 32);
        Transform3D transform = RenderFrame.World.Anchor(grid.Origin);
        return new(key, grid, transform, new Vector2(0, 4), ContactGpuBounds(grid, transform), revision);
    }

    private static ContactGpuQuery[] ContactGpuSeamQueries(DrawnTerrainTile faceX, DrawnTerrainTile faceZ, Vector3 camera)
    {
        var result = new List<ContactGpuQuery>
        {
            ContactGpuSample("+X face endpoint", faceX, new Vector2(1, 0.375f), camera),
            ContactGpuSample("-Z face endpoint", faceZ, new Vector2(0, 0.375f), camera),
            ContactGpuSample("+X first vertex", faceX, Vector2.Zero, camera),
            ContactGpuSample("-Z final vertex", faceZ, Vector2.One, camera),
            ContactGpuMissing("unpublished footprint direction", 4, new Vector2(0.5f, 0.5f))
                with { Hint = new Vector3(0, 0, 8), FromHint = true },
        };
        foreach ((int face, Vector2 uv, string name) in new[]
        {
            (0, new Vector2(1 - 0.0001f, 0.375f), "foot before cube seam"),
            (5, new Vector2(0.0001f, 0.375f), "foot after cube seam"),
            (0, new Vector2(1, 0.375f), "foot on cube seam"),
        })
        {
            Vector3D direction = CubeSphere.Direction(face, 2 * uv.X - 1, 2 * uv.Y - 1);
            Vector3 hint = new((float)(direction.X * 8), (float)(direction.Y * 8), (float)(direction.Z * 8));
            // The query texture stores float hints; derive the CPU address from those exact inputs as well.
            (int actualFace, double u, double v) = CubeSphere.FromDirection(new Vector3D(hint.X, hint.Y, hint.Z));
            Vector2 actualUv = new((float)((u + 1) * 0.5), (float)((v + 1) * 0.5));
            ContactGpuQuery query = ContactGpuSample(name, actualFace == 0 ? faceX : faceZ, actualUv, camera);
            result.Add(query with { Hint = hint, FromHint = true });
        }
        return result.ToArray();
    }

    private static Aabb ContactGpuBounds(TileGrid grid, Transform3D transform)
    {
        Vector3 low = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity), high = -low;
        foreach (float[] points in new[] { grid.Positions, grid.MorphPositions })
        for (int i = 0; i < points.Length; i += 3)
        {
            Vector3 p = transform * new Vector3(points[i], points[i + 1], points[i + 2]);
            low = new Vector3(Math.Min(low.X, p.X), Math.Min(low.Y, p.Y), Math.Min(low.Z, p.Z));
            high = new Vector3(Math.Max(high.X, p.X), Math.Max(high.Y, p.Y), Math.Max(high.Z, p.Z));
        }
        return new Aabb(low, high - low).Grow(0.002f);
    }

    private const string ContactGpuShader = """
        shader_type canvas_item;
        render_mode unshaded, blend_disabled;
        #include "res://shaders/surface_planet.gdshaderinc"
        #include "res://shaders/surface_terrain_contact.gdshaderinc"
        uniform sampler2D fixture_queries : filter_nearest, repeat_disable;
        uniform int fixture_count = 0;
        uniform vec3 fixture_camera;
        void fragment() {
            int column = int(FRAGCOORD.x);
            if (column >= fixture_count) {
                COLOR = FRAGCOORD.y < 4.0 ? vec4(0.125, 0.375, 0.625, 1.0) : vec4(0.25, 0.5, 0.75, 1.0);
            } else {
                vec4 query = texelFetch(fixture_queries, ivec2(column, 0), 0);
                vec3 hint = texelFetch(fixture_queries, ivec2(column, 1), 0).xyz;
                vec3 pivot = texelFetch(fixture_queries, ivec2(column, 2), 0).xyz;
                vec3 point; vec3 normal;
                bool supported = query.w > 0.5
                    ? hu_drawn_contact_at(hint, fixture_camera, point, normal)
                    : hu_drawn_contact(int(query.x), query.yz, fixture_camera, point, normal);
                bool available = query.w > 0.5 ? hu_has_drawn_contact_at(hint) : supported;
                vec3 encoded = FRAGCOORD.y < 4.0 ? (point - pivot) * 16.0 + vec3(0.5) : normal * 0.5 + vec3(0.5);
                COLOR = vec4(encoded, (FRAGCOORD.y < 4.0 ? supported : available) ? 1.0 : 0.0);
            }
        }
        """;
}

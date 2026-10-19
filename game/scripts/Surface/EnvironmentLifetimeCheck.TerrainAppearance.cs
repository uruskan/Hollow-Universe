using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Hollow.Core;
using Hollow.Surface;

namespace HollowUniverse.Surface;

public partial class EnvironmentLifetimeCheck
{
    private sealed record AppearanceExpected(Vector4[] Channels, Vector2 FaceMetres, float Water, Vector3 Point, Vector3 Normal);
    private sealed record AppearanceQuery(string Name, int Face, Vector2 Uv, AppearanceExpected? Expected,
        bool FromHint = false, Vector3 Hint = default);
    private const float AppearanceNormalTolerance = 0.0025f;

    // Isolated actual-renderer check. The first oracle reads the production helper with byte-valued analytic
    // streams; the second rasterizes the production terrain vertex path and compares its native interpolants.
    // Neither checks final PBR appearance, and neither silently treats native normal quantization as bit identity.
    private async Task CheckTerrainAppearance()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        using var source = new ProbeSurface(200);
        var frame = new RenderFrame(new Vector3D(200, 0, 0));
        long owner = PlanetGlobals.Claim(frame, source.Radius);
        SubViewport? viewport = null;
        Shader? shader = null;
        ShaderMaterial? material = null;
        TerrainContactAtlas? atlas = null;
        ImageTexture? stale = null;
        try
        {
            using (var contactOnly = new TerrainContactAtlas(1))
                Expect("contact-only atlas allocates no appearance resources", !contactOnly.AppearanceEnabled
                    && contactOnly.AppearanceReservedBytes == 0 && contactOnly.AppearanceResourcesForFixture().Length == 0,
                    "constructor defaults remain contact-only; no appearance staging arrays or native textures");
            atlas = new TerrainContactAtlas(1, appearance: true);
            shader = new Shader { Code = AppearanceProbeShader };
            material = new ShaderMaterial { Shader = shader };
            atlas.BindAppearance(material);
            viewport = new SubViewport
            {
                Name = "DrawnAppearanceReadback", Size = new Vector2I(64, 8), Disable3D = true,
                UseHdr2D = true, TransparentBg = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            };
            AddChild(viewport);
            viewport.AddChild(new ColorRect { Size = new Vector2(64, 8), Material = material,
                MouseFilter = Control.MouseFilterEnum.Ignore });
            Expect("appearance storage has an explicit fixed reservation", atlas.AppearancePayloadBytes == 8_921_088
                && atlas.AppearanceStagingBytes == 104_544 && atlas.AppearanceReservedBytes < 9L * 1024 * 1024,
                $"payload={atlas.AppearancePayloadBytes} B; reused stages plus one upload Image={atlas.AppearanceStagingBytes} B; excludes driver overhead");

            DrawnTerrainTile tile = AppearanceTile(source, frame, new TileKey(0, 0, 0, 0));
            Vector3 camera = tile.Transform * new Vector3(0.5f, 0.3f, -0.1f);
            await PublishContactGpu(atlas, new[] { tile }, 1, deadline.Token);
            var lower = new Vector2(16.75f / 32, 16.25f / 32);
            var upper = new Vector2(16.25f / 32, 16.75f / 32);
            TerrainLod.TrySampleContact(tile, tile.Key, lower, camera, out TerrainContactSample contact);
            Expect("appearance oracle exercises unequal vertex morph", Math.Abs(contact.VertexMorph.X - contact.VertexMorph.Y) > 0.005f,
                $"per-vertex morph={contact.VertexMorph}; no single interpolated distance approximation");
            AppearanceQuery[] queries =
            {
                AppearanceSample("lower diagonal", tile, lower, camera),
                AppearanceSample("upper diagonal", tile, upper, camera),
                AppearanceSample("diagonal equality", tile, Vector2.One * (16.5f / 32), camera),
                AppearanceSample("first corner", tile, Vector2.Zero, camera),
                AppearanceSample("last corner", tile, Vector2.One, camera),
                new("unpublished face", 3, Vector2.One * .5f, null),
                new("outside face", 0, new Vector2(-.001f, .5f), null),
            };
            await ReadAppearance("both triangles and exact bytes", viewport, material, camera, queries, deadline.Token);

            long beforeMoveBytes = atlas.UploadedBytes;
            Vector3 shift = frame.Move(frame.Origin + new Vector3D(11, 7, -5));
            PlanetGlobals.Set(owner, frame, source.Radius);
            camera -= shift;
            tile = tile with { Transform = frame.Anchor(tile.Grid.Origin),
                Bounds = ContactGpuBounds(tile.Grid, frame.Anchor(tile.Grid.Origin)), ContactRevision = 2 };
            await PublishContactGpu(atlas, new[] { tile }, 2, deadline.Token);
            Expect("appearance rebase reuses immutable stream layers", atlas.UploadedBytes - beforeMoveBytes
                == (long)(6 * 2 * 2 + 5 * TerrainContactAtlas.Capacity * 4) * sizeof(float),
                "only shared lookup/transform metadata republished; appearance UV2 remains the already anchor-subtracted stream");
            await ReadAppearance("rotated frame and rebase", viewport, material, camera,
                new[] { AppearanceSample("rebased lower", tile, lower, camera), AppearanceSample("rebased upper", tile, upper, camera) }, deadline.Token);

            using var sphere = new ProbeSurface(8);
            PlanetGlobals.Set(owner, RenderFrame.World, sphere.Radius);
            DrawnTerrainTile faceX = AppearanceTile(sphere, RenderFrame.World, new TileKey(0, 0, 0, 0), syntheticPosition: false);
            DrawnTerrainTile faceZ = AppearanceTile(sphere, RenderFrame.World, new TileKey(5, 0, 0, 0), syntheticPosition: false);
            camera = new Vector3(8.1f, .1f, -.1f);
            await PublishContactGpu(atlas, new[] { faceX, faceZ }, 3, deadline.Token);
            var seams = new List<AppearanceQuery>();
            foreach ((int face, Vector2 uv) in new[] { (0, new Vector2(.9999f, .375f)), (5, new Vector2(.0001f, .375f)), (0, new Vector2(1, .375f)) })
            {
                Vector3D d = CubeSphere.Direction(face, 2 * uv.X - 1, 2 * uv.Y - 1);
                Vector3 hint = new((float)(d.X * 8), (float)(d.Y * 8), (float)(d.Z * 8));
                (int actualFace, double u, double v) = CubeSphere.FromDirection(new Vector3D(hint.X, hint.Y, hint.Z));
                Vector2 canonical = new((float)((u + 1) * .5), (float)((v + 1) * .5));
                seams.Add(AppearanceSample("seam " + face + "/" + uv, actualFace == 0 ? faceX : faceZ, canonical, camera)
                    with { FromHint = true, Hint = hint });
            }
            await ReadAppearance("canonical seam hints", viewport, material, camera, seams, deadline.Token);

            using (Image previous = ((Texture2D)material.GetShaderParameter("terrain_contact_lookup").AsGodotObject()).GetImage())
                stale = ImageTexture.CreateFromImage(previous);
            DrawnTerrainTile replacement = AppearanceTile(sphere, RenderFrame.World, faceX.Key, syntheticPosition: false);
            replacement.Grid.Climate[0] ^= 127;
            await PublishContactGpu(atlas, new[] { replacement, faceZ }, 4, deadline.Token);
            material.SetShaderParameter("terrain_contact_lookup", stale);
            await ReadAppearance("stale shared generation", viewport, material, camera,
                new[] { new AppearanceQuery("reused slot rejects old generation", 0, Vector2.One * .5f, null),
                    AppearanceSample("unchanged neighbor", faceZ, Vector2.One * .5f, camera) }, deadline.Token);
            atlas.BindAppearance(material);
            atlas.UpdateFixture(Array.Empty<DrawnTerrainTile>(), 5);
            await ReadAppearance("withdrawn drawn selection", viewport, material, camera,
                new[] { new AppearanceQuery("withdrawn slot", 5, Vector2.One * .5f, null) }, deadline.Token);

            DrawnTerrainTile invalid = AppearanceTile(sphere, RenderFrame.World, faceX.Key, syntheticPosition: false);
            invalid.Grid.FaceMetres[7] = float.NaN;
            long beforeBad = atlas.UploadedBytes;
            atlas.UpdateFixture(new[] { invalid }, 6);
            Expect("appearance rejects invalid streams before native upload", !atlas.Ready && atlas.Failure != null
                && atlas.ResidentTiles == 0 && atlas.UploadedBytes == beforeBad,
                "nonfinite UV2 preflight while publication is empty; zero fine/morph/appearance writes");
            foreach ((string name, Basis basis) in new[] { ("scale", Basis.Identity.Scaled(new Vector3(1.1f, 1, 1))),
                ("shear", new Basis(Vector3.Right, new Vector3(.1f, 1, 0), Vector3.Back)),
                ("reflection", new Basis(Vector3.Left, Vector3.Up, Vector3.Back)) })
            {
                await PublishContactGpu(atlas, new[] { faceX }, 7, deadline.Token);
                DrawnTerrainTile unsupported = faceX with { Transform = new Transform3D(basis, faceX.Transform.Origin) };
                atlas.UpdateFixture(new[] { unsupported }, 8);
                Expect("appearance explicitly rejects " + name, !atlas.Ready && atlas.ResidentTiles == 0 && atlas.Failure != null,
                    "proper orthonormal RenderFrame domain; no alternative normal-transform fallback");
                await ReadAppearance("refused " + name + " clears dependencies", viewport, material, camera,
                    new[] { new AppearanceQuery("unsupported slot", 0, Vector2.One * .5f, null) }, deadline.Token);
            }

            await PublishContactGpu(atlas, new[] { faceX }, 9, deadline.Token);
            atlas.InjectFixtureFault(TerrainContactAtlas.FixtureFault.AfterAppearanceMaterials);
            atlas.UpdateFixture(new[] { faceX, faceZ }, 10);
            Expect("appearance partial upload invalidates the whole dependency", !atlas.Ready && atlas.Failure != null
                && !atlas.PublicationInvalidationFailed && atlas.ResidentTiles == 0,
                "fault after the real packed-material native upload, before normals and shared generation publication");
            await ReadAppearance("partial upload failure", viewport, material, camera,
                new[] { new AppearanceQuery("previous slot also cleared", 0, Vector2.One * .5f, null),
                    new AppearanceQuery("partial new slot absent", 5, Vector2.One * .5f, null) }, deadline.Token);

            await CheckAppearanceVisibility(owner, viewport, material, atlas, deadline.Token);
            await CheckAppearanceNativeTerrain(owner, deadline.Token);
            CheckAppearanceRetirement();
        }
        finally
        {
            if (viewport != null && GodotObject.IsInstanceValid(viewport))
            {
                viewport.QueueFree();
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { while (GodotObject.IsInstanceValid(viewport)) await ContactGpuNextDraw(cleanup.Token); }
                catch (OperationCanceledException) { }
                Expect("appearance readback viewport retires before textures", !GodotObject.IsInstanceValid(viewport), "bounded native owner teardown");
            }
            material?.Dispose(); shader?.Dispose(); stale?.Dispose(); atlas?.Dispose();
            PlanetGlobals.Clear(owner);
        }
    }

    private void CheckAppearanceRetirement()
    {
        var atlas = new TerrainContactAtlas(0, appearance: true);
        GodotObject[] handles = atlas.NativeResourcesForFixture().Concat(atlas.AppearanceResourcesForFixture()).ToArray();
        atlas.InjectFixtureFault(TerrainContactAtlas.FixtureFault.DuringAppearanceRetirement);
        bool failed = false;
        try { atlas.Dispose(); } catch (AggregateException) { failed = true; }
        bool closed = false;
        try { atlas.UpdateFixture(Array.Empty<DrawnTerrainTile>(), 0); } catch (ObjectDisposedException) { closed = true; }
        Expect("appearance retirement retains a failed live owner and closes admission", failed && closed && !atlas.CleanupComplete
            && handles.Count(GodotObject.IsInstanceValid) == 1,
            "one injected uncertain appearance handle remains owned; all other native resources retired independently");
        atlas.Dispose(); atlas.Dispose();
        Expect("appearance explicit cleanup retry retires its retained handle", atlas.CleanupComplete && handles.All(h => !GodotObject.IsInstanceValid(h)),
            "bounded repeated Dispose, no new native allocation or reopened admission");

        TerrainContactAtlas.ConstructionCleanupException? construction = null;
        try { _ = new TerrainContactAtlas(0, appearance: true, constructionFault: TerrainContactAtlas.FixtureFault.AppearanceConstructionAndRetirement); }
        catch (TerrainContactAtlas.ConstructionCleanupException error) { construction = error; }
        Expect("failed appearance constructor transfers its retained owner", construction != null && !construction.Owner.CleanupComplete
            && construction.Owner.AppearanceResourcesForFixture().Count(GodotObject.IsInstanceValid) == 1,
            "typed exception explicitly owns the only remaining resource after every independent cleanup attempt");
        // The atlas no longer feeds the grass (it roots on the drawn grid itself): its retained owner is released here.
        construction?.Owner.Dispose();
    }

    private static DrawnTerrainTile AppearanceTile(ProbeSurface surface, RenderFrame frame, TileKey key, bool syntheticPosition = true)
    {
        TileGrid grid = syntheticPosition ? ContactGpuSyntheticGrid(surface, key) : TileMesh.Build(surface, key, 32);
        byte[] bytes = { 1, 0, 128, 255, 255, 255, 0, 1, 255, 255, 0, 128, 1, 255, 128, 255 };
        for (int y = 0; y < 33; y++)
        for (int x = 0; x < 33; x++)
        {
            int i = y * 33 + x;
            // Explicit high packed24 odd integers, maxima and channel boundaries. These are byte inputs,
            // deliberately not normalized biome weights; decoding must be exact even at the full 24-bit range.
            for (int c = 0; c < 16; c++) grid.Materials[16 * i + c] = c < 6 ? bytes[c] : (byte)((bytes[c] + x * 3 + y * 5) & 255);
            grid.Climate[4 * i] = (byte)(x * 7); grid.Climate[4 * i + 1] = (byte)(y * 7);
            grid.Climate[4 * i + 2] = (byte)((x + y) * 3); grid.Climate[4 * i + 3] = 255;
            if (syntheticPosition)
            {
                Vector3 fine = new Vector3(1, .015f * (x - 16), .02f * (y - 16)).Normalized();
                Vector3 coarse = new Vector3(.8f, -.01f * (x - 16), .025f * (y - 16)).Normalized() * .85f;
                for (int c = 0; c < 3; c++) { grid.Normals[3 * i + c] = fine[c]; grid.MorphNormals[3 * i + c] = coarse[c]; }
                grid.FaceMetres[2 * i] = x * .25f - 4;
                grid.FaceMetres[2 * i + 1] = y * .5f - 8;
            }
            grid.Heights[i] = 3 + .03f * x + .07f * y;
            grid.WaterLevels[i] = 2 + .01f * x;
        }
        Transform3D transform = frame.Anchor(grid.Origin);
        return new(key, grid, transform, new Vector2(0, 4), ContactGpuBounds(grid, transform), 1);
    }

    private static AppearanceQuery AppearanceSample(string name, DrawnTerrainTile tile, Vector2 uv, Vector3 camera)
    {
        if (!TerrainLod.TrySampleContact(tile, tile.Key, uv, camera, out TerrainContactSample contact))
            throw new InvalidOperationException("Appearance expected contact is unavailable: " + name);
        int cell = contact.GridTriangle / 2, a = cell / 32 * 33 + cell % 32;
        int[] vertices = contact.GridTriangle % 2 == 0 ? new[] { a, a + 1, a + 34 } : new[] { a, a + 34, a + 33 };
        var channels = new Vector4[5];
        Vector2 metres = Vector2.Zero;
        Vector3 normal = Vector3.Zero;
        float water = 0;
        for (int corner = 0; corner < 3; corner++)
        {
            int i = vertices[corner]; float w = contact.Barycentric[corner], k = contact.VertexMorph[corner];
            for (int c = 0; c < 20; c++) channels[c / 4][c % 4] += w * (c < 16 ? tile.Grid.Materials[16 * i + c] : tile.Grid.Climate[4 * i + c - 16]) / 255f;
            metres += new Vector2(tile.Grid.FaceMetres[2 * i], tile.Grid.FaceMetres[2 * i + 1]) * w;
            water += (tile.Grid.Heights[i] - tile.Grid.WaterLevels[i]) * w;
            Vector3 fine = new(tile.Grid.Normals[3 * i], tile.Grid.Normals[3 * i + 1], tile.Grid.Normals[3 * i + 2]);
            Vector3 coarse = new(tile.Grid.MorphNormals[3 * i], tile.Grid.MorphNormals[3 * i + 1], tile.Grid.MorphNormals[3 * i + 2]);
            normal += (tile.Transform.Basis * (fine * (1 - k) + coarse * k).Normalized()) * w;
        }
        return new(name, tile.Key.Face, uv, new(channels, metres, water, contact.RenderPosition, normal.Normalized()));
    }

    private async Task ReadAppearance(string stage, SubViewport viewport, ShaderMaterial material, Vector3 camera,
        IReadOnlyList<AppearanceQuery> queries, CancellationToken cancellation)
    {
        if (queries.Count >= 64) throw new ArgumentOutOfRangeException(nameof(queries));
        var inputs = new float[64 * 4 * 4];
        for (int i = 0; i < queries.Count; i++)
        {
            AppearanceQuery q = queries[i];
            Put(i, 0, new Vector4(q.Face, q.Uv.X, q.Uv.Y, q.FromHint ? 1 : 0));
            Put(i, 1, new Vector4(q.Hint.X, q.Hint.Y, q.Hint.Z, 0));
            if (q.Expected is not { } e) continue;
            Put(i, 2, new Vector4(e.Point.X, e.Point.Y, e.Point.Z, 0));
            Put(i, 3, new Vector4(e.FaceMetres.X, e.FaceMetres.Y, e.Water, 0));
        }
        using ImageTexture input = GrassRenderFloatTexture(inputs, 64, 4);
        material.SetShaderParameter("fixture_queries", input);
        material.SetShaderParameter("fixture_count", queries.Count);
        material.SetShaderParameter("fixture_camera", camera);
        try
        {
            viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
            for (int i = 0; i < 3; i++) await ContactGpuNextDraw(cancellation);
            using Image readback = viewport.GetTexture().GetImage();
            bool hdr = readback.GetFormat() is Image.Format.Rgbaf or Image.Format.Rgbah;
            int[] rows = Enumerable.Repeat(-1, 8).ToArray();
            for (int y = 0; y < 8; y++)
            {
                Color marker = readback.GetPixel(63, y);
                int row = (int)MathF.Round(marker.G * 8 - .5f);
                if (Math.Abs(marker.R - .125f) < .001f && Math.Abs(marker.B - .625f) < .001f && row is >= 0 and < 8)
                    rows[row] = y;
            }
            bool calibrated = hdr && rows.All(row => row >= 0) && rows.Distinct().Count() == 8;
            var errors = new List<string>();
            float maximumChannel = 0, maximumResidual = 0, maximumNormal = 0;
            if (calibrated)
            for (int i = 0; i < queries.Count; i++)
            {
                AppearanceQuery q = queries[i];
                Color phase = readback.GetPixel(i, rows[5]), n = readback.GetPixel(i, rows[6]), p = readback.GetPixel(i, rows[7]);
                bool expected = q.Expected != null;
                if ((phase.A > .5f) != expected || (n.A > .5f) != expected || (p.A > .5f) != expected)
                { errors.Add(q.Name + ": availability differs"); continue; }
                if (q.Expected is not { } e) continue;
                float channelError = 0;
                for (int group = 0; group < 5; group++)
                {
                    Color actual = readback.GetPixel(i, rows[group]);
                    for (int c = 0; c < 4; c++) channelError = Math.Max(channelError, Math.Abs(actual[c] - e.Channels[group][c]));
                }
                float residual = Math.Max((new Vector3(phase.R, phase.G, phase.B) - Vector3.One * .5f).Length(),
                    (new Vector3(p.R, p.G, p.B) - Vector3.One * .5f).Length()) / 16;
                float normalError = (new Vector3(n.R, n.G, n.B) * 2 - Vector3.One).DistanceTo(e.Normal);
                maximumChannel = Math.Max(maximumChannel, channelError); maximumResidual = Math.Max(maximumResidual, residual);
                maximumNormal = Math.Max(maximumNormal, normalError);
                if (!float.IsFinite(channelError) || !float.IsFinite(residual) || !float.IsFinite(normalError)
                    || channelError > .001f || residual > .0004f || normalError > AppearanceNormalTolerance)
                    errors.Add($"{q.Name}: channels={channelError:R}, point/UV/water={residual:R}, normal={normalError:R}");
            }
            Expect("drawn appearance GPU oracle: " + stage, calibrated && errors.Count == 0,
                $"{queries.Count} samples, calibrated={calibrated}; max channel={maximumChannel:R}, point/UV/water residual={maximumResidual:R}, normal={maximumNormal:R}; "
                + string.Join("; ", errors));
        }
        finally { material.SetShaderParameter("fixture_queries", default(Variant)); }

        void Put(int x, int y, Vector4 value) { for (int c = 0; c < 4; c++) inputs[(y * 64 + x) * 4 + c] = value[c]; }
    }

    private async Task CheckAppearanceNativeTerrain(long owner, CancellationToken cancellation)
    {
        using var source = new ProbeSurface(200);
        var frame = new RenderFrame(new Vector3D(200, 0, 0));
        PlanetGlobals.Set(owner, frame, source.Radius);
        var resources = new List<IDisposable>();
        SubViewport? viewport = null;
        SurfaceDiagnosticGlobals? globals = null;
        try
        {
            ImageTexture zero = Own(GrassRenderSolid(Colors.Black)), one = Own(GrassRenderSolid(Colors.White));
            ImageTexture noise = Own(GrassRenderSolid(new Color(.7f, .7f, .7f, 1)));
            ImageTexture3D dark = Own(GrassRenderVolume(Colors.Black)), clear = Own(GrassRenderVolume(Colors.White));
            globals = new SurfaceDiagnosticGlobals();
            globals.Setup(source.Radius, zero, one, noise, dark, clear);
            viewport = new SubViewport
            {
                Name = "NativeTerrainAppearanceOracle", Size = new Vector2I(128, 128), OwnWorld3D = true,
                TransparentBg = true, Msaa3D = Viewport.Msaa.Disabled, ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled,
                UseTaa = false, UseDebanding = false, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            };
            AddChild(viewport);
            var environment = Own(new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Black,
                TonemapMode = Godot.Environment.ToneMapper.Linear, TonemapExposure = 1,
                GlowEnabled = false, FogEnabled = false, SsaoEnabled = false, SsilEnabled = false,
            });
            viewport.AddChild(new WorldEnvironment { Environment = environment });
            DrawnTerrainTile tile = AppearanceTile(source, frame, new TileKey(0, 0, 0, 0));
            var camera = new Camera3D { Current = true, Projection = Camera3D.ProjectionType.Orthogonal,
                Size = 34, Near = .05f, Far = 100, Position = tile.Transform * new Vector3(40, 1, -2) };
            viewport.AddChild(camera);
            camera.LookAt(tile.Transform * new Vector3(1.3f, 0, 0), tile.Transform.Basis * Vector3.Up);

            // Reuse the actual production vertex source and its exact pre-ground_look normal statement.
            // Only the fragment output is replaced with an error marker. Thus fine NORMAL still goes through
            // Godot's native octahedral stream and raster interpolation; this is not an alternate CPU shader.
            string original = GD.Load<Shader>("res://shaders/surface_terrain.gdshader").Code;
            int fragment = original.IndexOf("void fragment()", StringComparison.Ordinal);
            if (fragment < 0) throw new InvalidOperationException("Production terrain fragment entry changed.");
            int normalStart = original.IndexOf("vec3 n_geo =", fragment, StringComparison.Ordinal);
            if (normalStart < fragment) throw new InvalidOperationException("Production terrain normal statement changed.");
            int normalEnd = original.IndexOf(';', normalStart);
            const string mode = "render_mode cull_back, depth_draw_opaque, diffuse_burley, specular_schlick_ggx;";
            if (fragment < 0 || normalStart < fragment || normalEnd < normalStart || !original.Contains(mode, StringComparison.Ordinal))
                throw new InvalidOperationException("Production terrain shader instrumentation anchors changed; inspect the new pipeline explicitly.");
            string vertexPath = original[..fragment].Replace(mode, "render_mode unshaded, cull_disabled;", StringComparison.Ordinal);
            string normalStatement = original[normalStart..(normalEnd + 1)];
            Shader probe = Own(new Shader { Code = vertexPath
                + "\n#include \"res://shaders/surface_terrain_appearance.gdshaderinc\"\n"
                + NativeAppearanceProbePrefix + "\nvoid fragment() {\n" + normalStatement + NativeAppearanceProbeFragment });
            ShaderMaterial material = Own(new ShaderMaterial { Shader = probe });
            TerrainContactAtlas atlas = Own(new TerrainContactAtlas(0, appearance: true));
            atlas.BindAppearance(material);
            material.SetShaderParameter("fixture_uv_origin", new Vector2(-4, -8));
            material.SetShaderParameter("fixture_uv_extent", new Vector2(8, 16));
            material.SetShaderParameter("fixture_normal_tolerance", AppearanceNormalTolerance);
            TileMeshData data = TileMeshData.From(tile.Grid);
            ArrayMesh mesh = Own(AppearanceNativeMesh(data));
            using (var native = mesh.SurfaceGetArrays(0))
            {
                Vector3[] normals = native[(int)Mesh.ArrayType.Normal].AsVector3Array();
                float maximum = 0;
                for (int i = 0; i < 33 * 33; i++) maximum = Math.Max(maximum, normals[i].DistanceTo(data.Normals[i]));
                Expect("appearance records native fine-normal quantization", float.IsFinite(maximum) && maximum < AppearanceNormalTolerance,
                    $"max native/source normal-vector error={maximum:R}; declared raster tolerance={AppearanceNormalTolerance:R}; no bit-exact normal claim");
            }
            var instance = new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Transform = tile.Transform,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            viewport.AddChild(instance);
            instance.SetInstanceShaderParameter("tile_face", 0);
            foreach ((string name, Vector2 range) in new[] { ("fine", new Vector2(1e5f, 2e5f)),
                ("coarse", new Vector2(-1, 0)), ("unequal vertex morph", new Vector2(30, 52)) })
            {
                tile = tile with { MorphRange = range };
                instance.SetInstanceShaderParameter("morph_range", range);
                await PublishContactGpu(atlas, new[] { tile }, 1, cancellation);
                await Raster(name);
            }
            Vector3 shift = frame.Move(frame.Origin + new Vector3D(17, -3, 5));
            PlanetGlobals.Set(owner, frame, source.Radius);
            camera.Position -= shift;
            tile = tile with { Transform = frame.Anchor(tile.Grid.Origin), Bounds = ContactGpuBounds(tile.Grid, frame.Anchor(tile.Grid.Origin)) };
            instance.Transform = tile.Transform;
            await PublishContactGpu(atlas, new[] { tile }, 2, cancellation);
            await Raster("same native interpolants after rebase");

            async Task Raster(string name)
            {
                viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
                for (int i = 0; i < 3; i++) await ContactGpuNextDraw(cancellation);
                using Image image = viewport.GetTexture().GetImage();
                image.Convert(Image.Format.Rgba8);
                byte[] pixels = image.GetData();
                int drawn = 0, failed = 0;
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    if (pixels[i + 3] < 200 || pixels[i + 1] < 200) continue;
                    drawn++;
                    if (pixels[i] > 127) failed++;
                }
                Expect("appearance matches native terrain interpolants: " + name, drawn > 2000 && failed == 0,
                    $"{drawn} rasterized samples, {failed} failures; GPU checks before framebuffer quantization: material/climate <=.001, UV/water/point <=.002 m, normal-vector <={AppearanceNormalTolerance:R}; includes packed24 0x800001/0xffffff");
            }
        }
        finally
        {
            if (viewport != null && GodotObject.IsInstanceValid(viewport))
            {
                viewport.QueueFree();
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { while (GodotObject.IsInstanceValid(viewport)) await ContactGpuNextDraw(cleanup.Token); }
                catch (OperationCanceledException) { }
                Expect("native terrain appearance viewport retires before resources", !GodotObject.IsInstanceValid(viewport), "private atlas/mesh/material lifetime");
            }
            globals?.Dispose();
            for (int i = resources.Count - 1; i >= 0; i--) resources[i].Dispose();
        }
        T Own<T>(T resource) where T : IDisposable { resources.Add(resource); return resource; }
    }

    private async Task CheckAppearanceVisibility(long owner, SubViewport readback, ShaderMaterial reader,
        TerrainContactAtlas restore, CancellationToken cancellation)
    {
        using var source = new ProbeSurface(200);
        RenderFrame frame = RenderFrame.World;
        PlanetGlobals.Set(owner, frame, source.Radius);
        using var shader = new Shader { Code = """
            shader_type spatial;
            instance uniform vec2 morph_range;
            instance uniform int tile_face;
            void vertex() { VERTEX = mix(VERTEX,CUSTOM0.xyz,0.0); }
            """ };
        using var material = new ShaderMaterial { Shader = shader };
        using var atlas = new TerrainContactAtlas(0, appearance: true);
        var viewport = new SubViewport { Name = "AppearanceVisibilityOwner", OwnWorld3D = true,
            Size = new Vector2I(1, 1), RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled };
        AddChild(viewport);
        var parent = new Node3D { Name = "AppearanceTerrainOnlyAncestor" }; viewport.AddChild(parent);
        var terrain = new TerrainLod { Name = "AppearanceDrawnTerrain", Hurry = true }; parent.AddChild(terrain);
        try
        {
            terrain.Setup(source, frame, material, null);
            Vector3D distant = new(0, 100_000, 0);
            do { terrain.UpdateLod(distant); await ContactGpuNextDraw(cancellation); } while (!terrain.IsIdle);
            Vector3 camera = new(0, 201, 0);
            await PublishVisible();
            atlas.BindAppearance(reader);
            var snapshots = new List<DrawnTerrainTile>();
            if (!atlas.TryCopyPublishedTiles(snapshots, out _) || snapshots.Count != 6)
                throw new InvalidOperationException("Appearance visibility fixture did not publish six actual coarse terrain roots.");
            DrawnTerrainTile top = snapshots.Single(tile => tile.Key.Face == 2);
            AppearanceQuery visible = AppearanceSample("actual top terrain", top, Vector2.One * .5f, camera);
            await ReadAppearance("actual drawn terrain publication", readback, reader, camera, new[] { visible }, cancellation);
            foreach (Node3D node in new Node3D[] { terrain, parent })
            {
                long revision = terrain.ContactRevision;
                node.Visible = false;
                atlas.Update(terrain, distant, 200_000);
                Expect("appearance invalidates hidden producer: " + node.Name, !atlas.Ready && atlas.ResidentTiles == 0
                    && terrain.ContactRevision == revision, "effective tree visibility removes dependencies even without a terrain revision change");
                await ReadAppearance("hidden " + node.Name, readback, reader, camera,
                    new[] { visible with { Expected = null } }, cancellation);
                node.Visible = true;
                await PublishVisible();
                await ReadAppearance("restored " + node.Name, readback, reader, camera, new[] { visible }, cancellation);
            }

            async Task PublishVisible()
            {
                do { atlas.Update(terrain, distant, 200_000); await ContactGpuNextDraw(cancellation); } while (!atlas.Ready);
            }
        }
        finally
        {
            restore.BindAppearance(reader);
            Task workers = terrain.WorkersStopped;
            terrain.Stop();
            // Stop closes this finite six-root producer. Keep its source alive until those admitted jobs drain,
            // including when the render deadline cancelled; a timeout must not dispose a worker's source.
            await workers;
            viewport.QueueFree();
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try { while (GodotObject.IsInstanceValid(viewport)) await ContactGpuNextDraw(cleanup.Token); }
            catch (OperationCanceledException) { }
            Expect("appearance visibility terrain releases workers before source", !GodotObject.IsInstanceValid(viewport),
                "worker completion captured before Stop, then viewport and private resources retire");
        }
    }

    private static ArrayMesh AppearanceNativeMesh(TileMeshData data)
    {
        using var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = data.Vertices;
        arrays[(int)Mesh.ArrayType.Normal] = data.Normals;
        arrays[(int)Mesh.ArrayType.Color] = data.Climate;
        arrays[(int)Mesh.ArrayType.TexUV] = data.Heights;
        arrays[(int)Mesh.ArrayType.TexUV2] = data.FaceMetres;
        arrays[(int)Mesh.ArrayType.Custom0] = data.MorphPositions;
        arrays[(int)Mesh.ArrayType.Custom1] = data.MorphNormals;
        arrays[(int)Mesh.ArrayType.Custom2] = data.MaterialsPacked;
        arrays[(int)Mesh.ArrayType.Custom3] = data.MaterialsLast;
        arrays[(int)Mesh.ArrayType.Index] = data.Indices;
        var format = (Mesh.ArrayFormat)(((int)Mesh.ArrayCustomFormat.RgbFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift)
            | ((int)Mesh.ArrayCustomFormat.RgbFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift)
            | ((int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom2Shift)
            | ((int)Mesh.ArrayCustomFormat.Rgba8Unorm << (int)Mesh.ArrayFormat.FormatCustom3Shift));
        var mesh = new ArrayMesh();
        try { mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null, format); return mesh; }
        catch { mesh.Dispose(); throw; }
    }

    private const string NativeAppearanceProbePrefix = """
        uniform vec2 fixture_uv_origin;
        uniform vec2 fixture_uv_extent;
        uniform float fixture_normal_tolerance;
        float fixture_max4(vec4 x) { return max(max(x.x,x.y),max(x.z,x.w)); }
        """;
    private const string NativeAppearanceProbeFragment = """
            // Skirts have no appearance support by contract. Every regular-fragment sample stays observable.
            if (UV.y > .001) discard;
            vec2 face_uv = (v_face-fixture_uv_origin)/fixture_uv_extent;
            HuTerrainAppearance appearance;
            bool found = hu_drawn_appearance(tile_face, face_uv, CAMERA_POSITION_WORLD, appearance);
            float channels = max(max(fixture_max4(abs(appearance.material0-v_m0)),fixture_max4(abs(appearance.material1-v_m1))),
                max(max(fixture_max4(abs(appearance.material2-v_m2)),fixture_max4(abs(appearance.material3-v_m3))),fixture_max4(abs(appearance.climate-v_climate))));
            float phase_error = max(length(appearance.face_metres-v_face),abs(appearance.water_height-v_height));
            float normal_error = length(appearance.normal-n_geo);
            bool good = found && channels <= .001 && phase_error <= .002
                && length(appearance.point-v_world) <= .002 && normal_error <= fixture_normal_tolerance;
            ALBEDO = vec3(good ? 0.0 : 1.0, 1.0, clamp(normal_error/fixture_normal_tolerance,0.0,1.0));
        }
        """;

    private const string AppearanceProbeShader = """
        shader_type canvas_item;
        render_mode unshaded, blend_disabled;
        #include "res://shaders/surface_planet.gdshaderinc"
        #include "res://shaders/surface_terrain_appearance.gdshaderinc"
        uniform sampler2D fixture_queries : filter_nearest, repeat_disable;
        uniform int fixture_count;
        uniform vec3 fixture_camera;
        void fragment() {
            int column = int(FRAGCOORD.x); int row = int(FRAGCOORD.y);
            if (column >= fixture_count) {
                COLOR = vec4(.125, (float(row)+.5)/8.0, .625, 1.0);
            } else {
                vec4 query = texelFetch(fixture_queries, ivec2(column,0),0);
                HuTerrainAppearance appearance;
                bool found = query.w > .5
                    ? hu_drawn_appearance_at(texelFetch(fixture_queries,ivec2(column,1),0).xyz, fixture_camera, appearance)
                    : hu_drawn_appearance(int(query.x),query.yz,fixture_camera,appearance);
                if (row == 0) COLOR = appearance.material0;
                else if (row == 1) COLOR = appearance.material1;
                else if (row == 2) COLOR = appearance.material2;
                else if (row == 3) COLOR = appearance.material3;
                else if (row == 4) COLOR = appearance.climate;
                else if (row == 5) COLOR = vec4((vec3(appearance.face_metres,appearance.water_height)
                    - texelFetch(fixture_queries,ivec2(column,3),0).xyz)*16.0+.5,found?1.0:0.0);
                else if (row == 6) COLOR = vec4(appearance.normal*.5+.5,found?1.0:0.0);
                else COLOR = vec4((appearance.point-texelFetch(fixture_queries,ivec2(column,2),0).xyz)*16.0+.5,found?1.0:0.0);
            }
        }
        """;
}

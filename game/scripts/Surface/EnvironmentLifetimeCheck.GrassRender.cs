using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Hollow.Core;
using Hollow.Surface;
using HollowUniverse.Surface.Flora;

namespace HollowUniverse.Surface;

public partial class EnvironmentLifetimeCheck
{
    // Code-path and contact/lighting diagnosis only. Synthetic textures deliberately avoid the full flora library;
    // the mesh and shader are the real production resources, and the ground uses the same morphed contact vertices.
    private async Task CheckGrassRender()
    {
        const int width = 640, height = 360, cells = 6, side = cells + 1, faceCells = 512, firstCell = 260;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        using var source = new ProbeSurface(100);
        var frame = new RenderFrame(new Vector3D(0, 100, 0));
        long owner = PlanetGlobals.Claim(frame, source.Radius);
        var resources = new List<IDisposable>();
        SubViewport? viewport = null;
        SurfaceDiagnosticGlobals? globals = null;
        ShaderMaterial? grassMaterial = null;
        TerrainContactAtlas? atlas = null;
        try
        {
            ImageTexture noise = Own(GrassRenderSolid(new Color(0.7f, 0.7f, 0.7f, 1)));
            ImageTexture zero = Own(GrassRenderSolid(new Color(0, 0, 0, 1)));
            ImageTexture one = Own(GrassRenderSolid(Colors.White));
            ImageTexture3D darkAir = Own(GrassRenderVolume(Colors.Black));
            ImageTexture3D clearAir = Own(GrassRenderVolume(Colors.White));
            globals = new SurfaceDiagnosticGlobals();
            globals.Setup(source.Radius, zero, one, noise, darkAir, clearAir);

            viewport = new SubViewport
            {
                Name = "ProductionGrassRenderProbe", Size = new Vector2I(width, height), OwnWorld3D = true,
                Msaa3D = Viewport.Msaa.Disabled, ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled,
                UseTaa = false, UseDebanding = false, PositionalShadowAtlasSize = 0,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            };
            AddChild(viewport);
            var environment = Own(new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.025f, 0.04f, 0.065f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White,
                AmbientLightEnergy = 0.45f, ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
                TonemapMode = Godot.Environment.ToneMapper.Linear, TonemapExposure = 1,
                GlowEnabled = false, FogEnabled = false, SsaoEnabled = false, SsilEnabled = false, SdfgiEnabled = false,
            });
            viewport.AddChild(new WorldEnvironment { Environment = environment });
            viewport.AddChild(new DirectionalLight3D
            {
                RotationDegrees = new Vector3(-48, -28, 0), LightColor = Colors.White,
                LightEnergy = 1.7f, ShadowEnabled = false,
            });
            Vector3D direction = CubeSphere.Direction(2, 2.0 * (firstCell + cells * 0.5) / faceCells - 1,
                2.0 * (firstCell + cells * 0.5) / faceCells - 1);
            Vector3 target = frame.ToRender(direction * source.Radius) + Vector3.Up * 0.55f;
            var camera = new Camera3D
            {
                Projection = Camera3D.ProjectionType.Orthogonal, Size = 3.0f, Near = 0.05f, Far = 100,
                Position = target + new Vector3(3.5f, 1.35f, 4.8f), Current = true,
            };
            viewport.AddChild(camera);
            camera.LookAt(target, Vector3.Up);
            globals.Set("hu_camera_position", RenderingServer.GlobalShaderParameterType.Vec3, camera.GlobalPosition);

            TileGrid grid = TileMesh.Build(source, new TileKey(2, 4, 8, 8), 32);
            foreach ((float[] positions, float tilt) in new[] { (grid.Positions, 0.22f), (grid.MorphPositions, 0.09f) })
            for (int i = 0; i < positions.Length; i += 3)
            {
                Vector3D body = grid.Origin + new Vector3D(positions[i], positions[i + 1], positions[i + 2]);
                double elevation = body.X * tilt + body.Z * 0.07;
                Vector3D local = body + body.Normalized * elevation - grid.Origin;
                positions[i] = (float)local.X; positions[i + 1] = (float)local.Y; positions[i + 2] = (float)local.Z;
            }
            Transform3D transform = frame.Anchor(grid.Origin);
            var tile = new DrawnTerrainTile(grid.Key, grid, transform, new Vector2(3, 8), ContactGpuBounds(grid, transform), 1);
            atlas = Own(new TerrainContactAtlas(4));
            await PublishContactGpu(atlas, new[] { tile }, 1, deadline.Token);

            ArrayMesh ground = Own(GrassRenderGround(tile, camera.GlobalPosition));
            var groundMaterial = Own(new StandardMaterial3D
            {
                AlbedoColor = new Color(0.29f, 0.18f, 0.095f), Roughness = 0.95f,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            });
            var groundInstance = new MeshInstance3D { Mesh = ground, MaterialOverride = groundMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            viewport.AddChild(groundInstance);

            var lattice = new float[side * side * 4];
            var cover = new float[lattice.Length];
            for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            {
                Vector2 uv = new((firstCell + x) / (float)faceCells, (firstCell + y) / (float)faceCells);
                if (!TerrainLod.TrySampleContact(tile, tile.Key, uv, camera.GlobalPosition, out TerrainContactSample sample))
                    throw new InvalidOperationException("Grass fixture lattice has no drawn terrain support.");
                int p = (y * side + x) * 4;
                lattice[p] = sample.RenderPosition.X; lattice[p + 1] = sample.RenderPosition.Y;
                lattice[p + 2] = sample.RenderPosition.Z; lattice[p + 3] = 1;
                cover[p] = 0.8f; // moist, fully grassy, no flowers/plants/litter
            }
            ImageTexture nodes = Own(GrassRenderFloatTexture(lattice, side, side));
            ImageTexture ecology = Own(GrassRenderFloatTexture(cover, side, side));
            ImageTexture blades = Own(GrassRenderBladeTexture());
            Texture2DArray soil = Own(GrassRenderSoilTexture());
            grassMaterial = Own(new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/surface_grass.gdshader") });
            atlas.Bind(grassMaterial);
            grassMaterial.SetShaderParameter("lattice", nodes);
            grassMaterial.SetShaderParameter("lattice_cover", ecology);
            grassMaterial.SetShaderParameter("atlas", blades);
            grassMaterial.SetShaderParameter("ground_albedo", soil);
            grassMaterial.SetShaderParameter("macro_noise", noise);
            grassMaterial.SetShaderParameter("grid_width", cells);
            grassMaterial.SetShaderParameter("lattice_size", side);
            grassMaterial.SetShaderParameter("face_cells", faceCells);
            grassMaterial.SetShaderParameter("lattice_cell", new Vector2I(firstCell, firstCell));
            grassMaterial.SetShaderParameter("grid_offset", Vector2I.Zero);
            grassMaterial.SetShaderParameter("face", 2);
            grassMaterial.SetShaderParameter("anchor", Vector3.Zero);
            grassMaterial.SetShaderParameter("face_origin", new Vector2(1.2f, 1.2f));
            grassMaterial.SetShaderParameter("cell_metres", (float)TileKey.EdgeLength(9, source.Radius));
            grassMaterial.SetShaderParameter("density_scale", 1f);
            grassMaterial.SetShaderParameter("blade_underlayer", true);
            grassMaterial.SetShaderParameter("far_radius", 20f);
            grassMaterial.SetShaderParameter("shrink_start", 18f);
            grassMaterial.SetShaderParameter("blade_mean", new Vector3(0.65f, 0.85f, 0.35f));
            grassMaterial.SetShaderParameter("trample", Vector4.Zero);
            foreach (string cell in new[] { "cell_grass", "cell_dry", "cell_meadow", "cell_flower", "cell_herb", "cell_fern", "cell_bush" })
                grassMaterial.SetShaderParameter(cell, new Vector4(0, 1, 1, 0));
            ArrayMesh tuft = Own(GrassField.CreateGrassRenderFixtureMesh());
            var multimesh = Own(new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = tuft,
                CustomAabb = tile.Bounds.Grow(3), InstanceCount = cells * cells,
            });
            for (int i = 0; i < cells * cells; i++) multimesh.SetInstanceTransform(i, Transform3D.Identity);
            var grass = new MultiMeshInstance3D
            {
                Name = "ActualProductionGrass", Multimesh = multimesh, MaterialOverride = grassMaterial, Visible = false,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            };
            viewport.AddChild(grass);

            using Image baseline = await CaptureGrassRender(viewport, "ground", deadline.Token);
            int groundPixels = GrassRenderCount(baseline, static c => c.R > c.G * 1.15f && c.R > c.B * 1.3f && c.R > 0.05f);
            Expect("production grass fixture has visible tilted ground", groundPixels > 1000,
                $"{groundPixels} brown ground pixels in the ground-only control; synthetic diagnostic material");
            grass.Visible = true;
            using Image calm = await CaptureGrassRender(viewport, "calm", deadline.Token);
            int green = GrassRenderCount(calm, static c => c.G > c.R * 1.05f && c.G > c.B * 1.1f && c.G > 0.06f);
            int added = GrassRenderDifference(baseline, calm);
            Expect("production grass shader renders nonempty blades", green > 100 && added > 100,
                $"{green} green pixels; {added} pixels differ from ground control; 36 actual production tufts, constant paired feet, real contact atlas");

            // Reorder every instance in the compact path while retaining the same canonical cells. The actual
            // production shader must produce the same geometry/lighting: a shape hash based on INSTANCE_ID,
            // an advancing mutable random stream or frame time would fail this comparison. This does not use
            // a CPU copy of the shader's shape equations, nor certify the artistic result of those equations.
            var compact = Own(new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = tuft,
                CustomAabb = multimesh.CustomAabb, InstanceCount = cells * cells,
            });
            for (int i = 0; i < cells * cells; i++)
            {
                int cell = cells * cells - 1 - i;
                compact.SetInstanceTransform(i, Transform3D.Identity);
                compact.SetInstanceCustomData(i, new Color(cell % cells, cell / cells, 0, 0));
            }
            grass.Multimesh = compact;
            grassMaterial.SetShaderParameter("compact_cells", true);
            grassMaterial.SetShaderParameter("tile_cell", Vector2I.Zero);
            using (Image reordered = await CaptureGrassRender(viewport, "calm-compact-reordered", deadline.Token))
            {
                int changed = GrassRenderDifference(calm, reordered);
                Expect("grass card shape keeps canonical identity across compact instance reordering", changed < 8,
                    $"{changed} pixels differ with reversed instance order and explicit canonical cells; same actual mesh, production shader, contacts and calm camera");
            }
            grass.Multimesh = multimesh;
            grassMaterial.SetShaderParameter("compact_cells", false);
            using (Image repeated = await CaptureGrassRender(viewport, "calm-repeat", deadline.Token))
            {
                int changed = GrassRenderDifference(calm, repeated);
                Expect("grass rest shape is stable across frames and path restoration", changed < 8,
                    $"{changed} pixels differ after restoring the legacy instance order; zero wind, unchanged support and camera");
            }

            globals.Set("hu_wind", RenderingServer.GlobalShaderParameterType.Vec4, new Vector4(0.8f, 0, 0.6f, 1.4f));
            using (Image wind = await CaptureGrassRender(viewport, "wind", deadline.Token))
            {
                int windGreen = GrassRenderCount(wind, static c => c.G > c.R * 1.05f && c.G > c.B * 1.1f && c.G > 0.06f);
                int changed = GrassRenderDifference(calm, wind);
                Expect("production grass wind changes the rendered blades", windGreen > 100 && changed > 20,
                    $"{windGreen} green pixels; {changed} pixels change with nonzero wind; PNG comparison is geometry/lighting diagnosis, not art acceptance");
            }
            atlas.UpdateFixture(Array.Empty<DrawnTerrainTile>(), 2);
            using (Image absent = await CaptureGrassRender(viewport, "no-support", deadline.Token))
            {
                int remaining = GrassRenderDifference(baseline, absent);
                Expect("production grass hides roots with missing drawn support", remaining < 8,
                    $"{remaining} pixels differ from the ground-only control after empty contact publication; no independent heightfield fallback");
            }

            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--environment-grass-production-render-check") >= 0)
            {
                // This explicitly requested case owns the real painter output and mips, without building tree
                // geometry or loading the complete ground/flora library. Always join the finite paint job even
                // if the render deadline expires; the returned native Image still has one owner on that path.
                using (Image painted = await Task.Run(() => FloraTextures.Atlas(201)))
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    ImageTexture productionAtlas = Own(ImageTexture.CreateFromImage(painted));
                    grassMaterial.SetShaderParameter("atlas", productionAtlas);
                    grassMaterial.SetShaderParameter("blade_mean", GrassRenderPaintedMean(painted) * 0.75f);
                }
                grassMaterial.SetShaderParameter("cell_grass", FloraTextures.CellUv(FloraTextures.Grass, 6));
                grassMaterial.SetShaderParameter("cell_dry", FloraTextures.CellUv(FloraTextures.DryGrass, 6));
                grassMaterial.SetShaderParameter("cell_meadow", FloraTextures.CellUv(FloraTextures.Meadow, 6));
                grassMaterial.SetShaderParameter("cell_flower", FloraTextures.CellUv(FloraTextures.Flower, 6));
                grassMaterial.SetShaderParameter("cell_herb", FloraTextures.CellUv(FloraTextures.Herb, 6));
                grassMaterial.SetShaderParameter("cell_fern", FloraTextures.CellUv(FloraTextures.Fern, 6));
                grassMaterial.SetShaderParameter("cell_bush", FloraTextures.CellUv(FloraTextures.Bush, 6));
                await PublishContactGpu(atlas, new[] { tile }, 3, deadline.Token);

                // The two directions view the same physical slope and its opposite facing. Each camera changes
                // terrain morph distances, so rebuild the diagnostic ground from those exact morphed vertices.
                await PaintedView("painted-up-slope", new Vector3(3.5f, 1.35f, 4.8f), 3, 0.35f, wind: true);
                await PaintedView("painted-down-slope", new Vector3(-3.5f, 1.35f, -4.8f), 3, 0.35f, wind: true);
                await PaintedView("painted-close-roots", new Vector3(2.0f, 0.45f, 2.8f), 1.35f, 0.16f, wind: false);

                async Task PaintedView(string label, Vector3 offset, float size, float lookHeight, bool wind)
                {
                    Vector3 centre = frame.ToRender(direction * source.Radius);
                    // Position above the entire fine/morph envelope, not above the undeformed sphere at
                    // the patch centre: the uphill ground beneath a low camera can otherwise cover its eye.
                    // This bound is independent of the camera-dependent morph and also works when the eye
                    // lies just outside the diagnostic tile. The rendered mesh checks the clearance below.
                    camera.Position = new Vector3(centre.X + offset.X, tile.Bounds.End.Y + offset.Y, centre.Z + offset.Z);
                    camera.Size = size;
                    Vector2 uv = new((firstCell + cells * 0.5f) / faceCells, (firstCell + cells * 0.5f) / faceCells);
                    if (!TerrainLod.TrySampleContact(tile, tile.Key, uv, camera.GlobalPosition, out TerrainContactSample support))
                        throw new InvalidOperationException("Painted grass camera target has no drawn support.");
                    camera.LookAt(support.RenderPosition + Vector3.Up * lookHeight, Vector3.Up);
                    globals.Set("hu_camera_position", RenderingServer.GlobalShaderParameterType.Vec3, camera.GlobalPosition);
                    ArrayMesh paintedGround = Own(GrassRenderGround(tile, camera.GlobalPosition));
                    groundInstance.Mesh = paintedGround;
                    float minimumEyeClearance = camera.GlobalPosition.Y - paintedGround.GetAabb().End.Y;
                    Expect("painted grass camera stays above drawn slope: " + label, minimumEyeClearance > 0.25f,
                        $"eye is {minimumEyeClearance:0.000} m above the highest actual morphed ground-mesh vertex; no underside root view");
                    globals.Set("hu_wind", RenderingServer.GlobalShaderParameterType.Vec4, new Vector4(0.8f, 0, 0.6f, 0));
                    grass.Visible = false;
                    using Image empty = await CaptureGrassRender(viewport, label + "-ground", deadline.Token);
                    grass.Visible = true;
                    using Image paintedCalm = await CaptureGrassRender(viewport, label + "-calm", deadline.Token);
                    int paintedGreen = GrassRenderCount(paintedCalm,
                        static c => c.G > c.R * 1.05f && c.G > c.B * 1.1f && c.G > 0.04f);
                    int paintedDifference = GrassRenderDifference(empty, paintedCalm);
                    Expect("painted production grass is visible: " + label, paintedGreen > 50 && paintedDifference > 80,
                        $"{paintedGreen} green pixels; {paintedDifference} differ from matching ground; actual 2048 atlas seed201, production cell padding and measured linear blade mean; controlled moisture/noise/soil, not a hosted world");
                    if (!wind) return;
                    globals.Set("hu_wind", RenderingServer.GlobalShaderParameterType.Vec4, new Vector4(0.8f, 0, 0.6f, 1.4f));
                    using Image paintedWind = await CaptureGrassRender(viewport, label + "-wind", deadline.Token);
                    int changed = GrassRenderDifference(paintedCalm, paintedWind);
                    Expect("painted production grass bends in wind: " + label, changed > 20,
                        $"{changed} changed pixels at the same camera/morph; real blade alpha and unchanged production palette/lighting; visual inspection still required");
                }
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
                Expect("production grass render fixture retires its viewport", !GodotObject.IsInstanceValid(viewport),
                    "native node destruction checked before releasing owned materials, meshes and textures");
            }
            globals?.Dispose();
            for (int i = resources.Count - 1; i >= 0; i--) resources[i].Dispose();
            PlanetGlobals.Clear(owner);
        }

        T Own<T>(T resource) where T : IDisposable { resources.Add(resource); return resource; }
    }

    private async Task<Image> CaptureGrassRender(SubViewport viewport, string label, CancellationToken cancellation)
    {
        viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        for (int i = 0; i < 3; i++) await ContactGpuNextDraw(cancellation);
        cancellation.ThrowIfCancellationRequested();
        Image image = viewport.GetTexture().GetImage();
        try
        {
            // The capture is an owned diagnostic readback. Normalize once, then inspect bytes without
            // hundreds of thousands of native GetPixel calls or any dependency on display HDR format.
            image.Convert(Image.Format.Rgba8);
            string report = Path.GetFullPath(OutputPath);
            string folder = Path.GetDirectoryName(report)!;
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, Path.GetFileNameWithoutExtension(report) + "-grass-" + label + ".png");
            Error saved = image.SavePng(path);
            using Image verified = saved == Error.Ok ? Image.LoadFromFile(path) : new Image();
            bool valid = saved == Error.Ok && !verified.IsEmpty() && verified.GetWidth() == 640 && verified.GetHeight() == 360;
            Expect("production grass PNG " + label, valid, path);
            if (!valid) throw new InvalidOperationException("Grass render capture did not produce a valid PNG: " + path);
            return image;
        }
        catch { image.Dispose(); throw; }
    }

    private static ArrayMesh GrassRenderGround(DrawnTerrainTile tile, Vector3 camera)
    {
        var vertices = new Vector3[33 * 33];
        var normals = new Vector3[vertices.Length];
        float count = 1 << tile.Key.Level;
        for (int y = 0; y <= 32; y++)
        for (int x = 0; x <= 32; x++)
        {
            Vector2 uv = new((tile.Key.X + x / 32f) / count, (tile.Key.Y + y / 32f) / count);
            if (!TerrainLod.TrySampleContact(tile, tile.Key, uv, camera, out TerrainContactSample sample))
                throw new InvalidOperationException("Grass ground fixture has an unsupported drawn vertex.");
            vertices[y * 33 + x] = sample.RenderPosition;
            normals[y * 33 + x] = sample.GeometricNormal;
        }
        using var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Index] = TileMesh.Indices(32);
        var mesh = new ArrayMesh();
        try { mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays); return mesh; }
        catch { mesh.Dispose(); throw; }
    }

    private static ImageTexture GrassRenderFloatTexture(float[] values, int width, int height)
    {
        var bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        using Image image = Image.CreateFromData(width, height, false, Image.Format.Rgbaf, bytes);
        return ImageTexture.CreateFromImage(image);
    }

    private static ImageTexture GrassRenderSolid(Color colour)
    {
        using Image image = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8);
        image.Fill(colour); image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    private static ImageTexture GrassRenderBladeTexture()
    {
        using Image image = Image.CreateEmpty(64, 64, false, Image.Format.Rgba8);
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
        {
            float t = 1 - y / 63f, across = Math.Abs((x + 0.5f) / 64 - 0.5f);
            float alpha = across < 0.4f * (1 - 0.8f * t) ? 1 : 0;
            image.SetPixel(x, y, new Color(0.8f, 0.95f, 0.6f, alpha));
        }
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    // Same opaque-texel linear mean used by FloraLibrary.Build, measured directly from this owned production
    // atlas. This avoids creating the entire flora library solely to obtain its BladeMean presentation input.
    private static Vector3 GrassRenderPaintedMean(Image atlas)
    {
        byte[] pixels = atlas.GetData();
        (int column, int row) = FloraTextures.Grass;
        int width = atlas.GetWidth();
        double r = 0, g = 0, b = 0, count = 0;
        for (int y = row * FloraTextures.Cell; y < (row + 1) * FloraTextures.Cell; y++)
        for (int x = column * FloraTextures.Cell; x < (column + 1) * FloraTextures.Cell; x++)
        {
            int p = (y * width + x) * 4;
            if (pixels[p + 3] < 128) continue;
            r += Linear(pixels[p]); g += Linear(pixels[p + 1]); b += Linear(pixels[p + 2]); count++;
        }
        if (count == 0) throw new InvalidOperationException("The production grass atlas contains no opaque blade texels.");
        return new Vector3((float)(r / count), (float)(g / count), (float)(b / count));

        static double Linear(byte value)
        {
            double c = value / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
    }

    private static Texture2DArray GrassRenderSoilTexture()
    {
        using Image image = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8);
        image.Fill(new Color(0.28f, 0.2f, 0.13f)); image.GenerateMipmaps();
        var images = new Godot.Collections.Array<Image> { image };
        using var imageStorage = (Godot.Collections.Array)images;
        var texture = new Texture2DArray();
        try
        {
            if (texture.CreateFromImages(images) != Error.Ok) throw new InvalidOperationException("Cannot create diagnostic soil array.");
            return texture;
        }
        catch { texture.Dispose(); throw; }
    }

    private static ImageTexture3D GrassRenderVolume(Color colour)
    {
        using Image image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        image.Fill(colour);
        var images = new Godot.Collections.Array<Image> { image };
        using var imageStorage = (Godot.Collections.Array)images;
        var texture = new ImageTexture3D();
        try
        {
            if (texture.Create(Image.Format.Rgba8, 1, 1, 1, false, images) != Error.Ok)
                throw new InvalidOperationException("Cannot create neutral diagnostic atmosphere volume.");
            return texture;
        }
        catch { texture.Dispose(); throw; }
    }

    private static int GrassRenderCount(Image image, Func<Color, bool> predicate)
    {
        byte[] pixels = image.GetData();
        int count = 0;
        for (int p = 0; p < pixels.Length; p += 4)
            if (predicate(new Color(pixels[p] / 255f, pixels[p + 1] / 255f, pixels[p + 2] / 255f, pixels[p + 3] / 255f))) count++;
        return count;
    }

    private static int GrassRenderDifference(Image first, Image second)
    {
        byte[] a = first.GetData(), b = second.GetData();
        if (a.Length != b.Length) throw new InvalidOperationException("Grass comparison image sizes differ.");
        int count = 0;
        for (int p = 0; p < a.Length; p += 4)
            if (Math.Max(Math.Abs(a[p] - b[p]), Math.Max(Math.Abs(a[p + 1] - b[p + 1]), Math.Abs(a[p + 2] - b[p + 2]))) > 6) count++;
        return count;
    }

}

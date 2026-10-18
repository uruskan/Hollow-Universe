using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Hollow.Core;
using Hollow.Surface;

namespace HollowUniverse.Surface;

public partial class EnvironmentLifetimeCheck
{
    /// <summary>Eight actual-PBR frames; one root is not a continuous fern-footprint clearance proof.</summary>
    private async Task CheckAuthoredCoverRender()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        using Process process = Process.GetCurrentProcess();
        using var surface = new ProbeSurface(100);
        var frame = new RenderFrame(new Vector3D(0, 100, 0));
        long planetOwner = PlanetGlobals.Claim(frame, surface.Radius);
        var resources = new List<IDisposable>();
        var captures = new List<string>();
        var clearances = new List<object>();
        long peakWorkingSet = 0;
        string phase = "setup";
        bool completed = false;
        Node? imported = null;
        SubViewport? viewport = null;
        SurfaceDiagnosticGlobals? globals = null;
        try
        {
            Budget();
            byte[] catalog = Godot.FileAccess.GetFileAsBytes("res://assets/surface/ground-cover/catalog.json");
            byte[] model = Godot.FileAccess.GetFileAsBytes("res://assets/surface/ground-cover/fern.glb");
            if (GroundCoverRootMetadata.Sha256(catalog) != "11f87351b1bf2486dd645c12d46edcaf2c0457c2f66db987dbf0dfbba6599239"
                || GroundCoverRootMetadata.Sha256(model) != "0fd809d2af27e5acbf6f881c8194fed0be7bd116d06edafc3cbaa4db9d3d8404")
                throw new InvalidOperationException("Authored render requires the exact root-reviewed fern catalog/model receipts.");
            using JsonDocument catalogDocument = JsonDocument.Parse(catalog);
            JsonElement fern = catalogDocument.RootElement.GetProperty("families").EnumerateArray().Single(f => f.GetProperty("key").GetString() == "fern");
            phase = "load real fern PBR";
            imported = GD.Load<PackedScene>("res://assets/surface/ground-cover/fern.glb").Instantiate();
            Budget();
            var sourceMeshes = new Dictionary<string, MeshInstance3D>(StringComparer.Ordinal);
            void Collect(Node node)
            {
                if (node is MeshInstance3D mesh && !sourceMeshes.TryAdd(mesh.Name.ToString(), mesh)) throw new InvalidOperationException("Duplicate fern node.");
                foreach (Node child in node.GetChildren()) Collect(child);
            }
            Collect(imported);
            AuthoredCoverRootMesh.Description[] descriptions = Enumerable.Range(0, 6)
                .Select(i => AuthoredCoverRootMesh.Describe(sourceMeshes[$"fern_v{i / 2}_lod{i % 2}"])).ToArray();
            Vector3D[] roots =
            {
                new(-0.0047980843902062364, -0.0001881867605107998, 0.00106728074549256),
                new(0.0010148137581017513, -0.0011384226127321426, 0.00016790628057927748),
                new(0.0000032782553893717647, -0.0026720985172965003, -0.025081902181761644)
            };
            GroundCoverRootMetadata metadata = DiagnosticRoots(descriptions, roots);
            ImageTexture zero = Own(GrassRenderSolid(new Color(0, 0, 0, 1)));
            ImageTexture one = Own(GrassRenderSolid(Colors.White));
            ImageTexture noise = Own(GrassRenderSolid(new Color(0.7f, 0.7f, 0.7f, 1)));
            ImageTexture3D darkAir = Own(GrassRenderVolume(Colors.Black));
            ImageTexture3D clearAir = Own(GrassRenderVolume(Colors.White));
            globals = new SurfaceDiagnosticGlobals();
            globals.Setup(surface.Radius, zero, one, noise, darkAir, clearAir);
            viewport = new SubViewport
            {
                Name = "ReviewedFernRootRenderProbe", Size = new Vector2I(640, 360), OwnWorld3D = true,
                Msaa3D = Viewport.Msaa.Disabled, ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled,
                UseTaa = false, UseDebanding = false, PositionalShadowAtlasSize = 0,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled
            };
            AddChild(viewport);
            var environment = Own(new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.025f, 0.04f, 0.065f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White,
                AmbientLightEnergy = 0.45f, ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
                TonemapMode = Godot.Environment.ToneMapper.Linear, TonemapExposure = 1,
                GlowEnabled = false, FogEnabled = false, SsaoEnabled = false, SsilEnabled = false, SdfgiEnabled = false
            });
            viewport.AddChild(new WorldEnvironment { Environment = environment });
            var light = new DirectionalLight3D
            { RotationDegrees = new Vector3(-48, -28, 0), LightColor = Colors.White, LightEnergy = 1.7f, ShadowEnabled = false };
            viewport.AddChild(light);
            var camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 3.4f, Near = 0.05f, Far = 100, Current = true };
            viewport.AddChild(camera);
            var groundMaterial = Own(new StandardMaterial3D
            { AlbedoColor = new Color(0.29f, 0.18f, 0.095f), Roughness = 0.95f, CullMode = BaseMaterial3D.CullModeEnum.Disabled });
            var ground = new MeshInstance3D { MaterialOverride = groundMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            viewport.AddChild(ground);
            TerrainContactAtlas atlas = Own(new TerrainContactAtlas(4));
            Shader shader = GD.Load<Shader>("res://shaders/surface_authored_cover.gdshader");
            var plants = new List<MultiMeshInstance3D>();
            var plantMaterials = new List<ShaderMaterial>();
            var transforms = new List<Transform3D>();
            for (int variant = 0; variant < 3; variant++)
            {
                string name = $"fern_v{variant}_lod0";
                MeshInstance3D source = sourceMeshes[name];
                GroundCoverRoot root = metadata.Get(name);
                ArrayMesh mesh = Own(AuthoredCoverRootMesh.Create(source, root));
                if (source.Mesh.SurfaceGetMaterial(0) is not BaseMaterial3D native || native.AlbedoTexture == null || native.NormalTexture == null)
                    throw new InvalidOperationException("Fern diagnostic requires the real base/alpha and normal textures.");
                var material = Own(new ShaderMaterial { Shader = shader });
                material.SetShaderParameter("albedo_texture", native.AlbedoTexture);
                material.SetShaderParameter("normal_texture", native.NormalTexture);
                Texture2D? roughness = native.RoughnessTexture ?? native.OrmTexture;
                if (roughness == null) throw new InvalidOperationException("Fern diagnostic requires the real roughness texture.");
                material.SetShaderParameter("roughness_texture", roughness);
                material.SetShaderParameter("has_normal", true); material.SetShaderParameter("has_roughness", true);
                material.SetShaderParameter("billboard", false); material.SetShaderParameter("far_radius", 42f);
                material.SetShaderParameter("plant_height", fern.GetProperty("variants")[variant].GetProperty("levels")[0].GetProperty("height").GetSingle());
                material.SetShaderParameter("authored_growth_axis", Vector3.Up);
                material.SetShaderParameter("authored_growth_length", (float)root.GrowthLength);
                material.SetShaderParameter("authored_contact_enabled", true);
                atlas.Bind(material);
                var multimesh = Own(new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true,
                    Mesh = mesh, InstanceCount = 1
                });
                // This pole tangent frame rotates body axes as well as translating the origin.
                // Define the review layout in the same body coordinates as AuthoredRenderTile's plane.
                var transform = new Transform3D(frame.Basis * new Basis(Vector3.Up, variant * 0.37f),
                    frame.ToRender(new Vector3D(2.8 + variant * 1.7, surface.Radius, -4)));
                transforms.Add(transform);
                multimesh.SetInstanceTransform(0, transform);
                multimesh.SetInstanceCustomData(0, new Color(1, 0.99f, 0.95f, 0.45f + variant * 0.7f));
                var plant = new MultiMeshInstance3D
                {
                    Name = name, Multimesh = multimesh, MaterialOverride = material, Visible = false,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, GIMode = GeometryInstance3D.GIModeEnum.Disabled
                };
                viewport.AddChild(plant); plants.Add(plant); plantMaterials.Add(material);
            }
            DrawnTerrainTile flat = AuthoredRenderTile(surface, frame, 0, 0, 1);
            await Configure(flat, "flat", new Vector3D(4.5, surface.Radius, -4), new Vector3D(0, 1.2, 6), 3.4f);
            using Image empty = await Capture("flat-ground");
            foreach (MultiMeshInstance3D plant in plants) plant.Visible = true;
            using Image calm = await Capture("flat-calm");
            int difference = GrassRenderDifference(empty, calm);
            Expect("authored real PBR ferns render at reviewed whole-plant roots", difference > 250,
                $"{difference} pixels differ from the same ground control; real source alpha/normal/roughness, three unscaled variants");
            globals.Set("hu_wind", RenderingServer.GlobalShaderParameterType.Vec4, new Vector4(0.8f, 0, 0.6f, 1.8f));
            using Image wind = await Capture("flat-wind");
            Expect("authored fern wind changes real textured silhouette and lighting", GrassRenderDifference(calm, wind) > 20,
                "same root and camera; analytic deformed normal/tangent basis; visual review still required");
            globals.Set("hu_wind", RenderingServer.GlobalShaderParameterType.Vec4, new Vector4(0.8f, 0, 0.6f, 0));
            await PublishContactGpu(atlas, Array.Empty<DrawnTerrainTile>(), 2, deadline.Token);
            using Image missing = await Capture("flat-no-support");
            Expect("authored missing support rejects the entire plant", GrassRenderDifference(empty, missing) == 0,
                "constant root support mask plus fragment discard; no remnant collapsed pixels");

            DrawnTerrainTile slope = AuthoredRenderTile(surface, frame, 0.28f, 0.08f, 3);
            await Configure(slope, "uphill", new Vector3D(4.5, surface.Radius, -4), new Vector3D(-4, 0.7, 5), 3.4f);
            using Image uphill = await Capture("uphill-calm");
            globals.Set("hu_wind", RenderingServer.GlobalShaderParameterType.Vec4, new Vector4(0.8f, 0, 0.6f, 1.8f));
            using Image uphillWind = await Capture("uphill-wind");
            Expect("authored uphill wind remains visible", GrassRenderDifference(uphill, uphillWind) > 20,
                "slope contact and canonical yaw retained; wide-frond intersections are reported separately");
            globals.Set("hu_wind", RenderingServer.GlobalShaderParameterType.Vec4, new Vector4(0.8f, 0, 0.6f, 0));
            await Configure(slope, "downhill", new Vector3D(4.5, surface.Radius, -4), new Vector3D(4, 0.7, -5), 3.4f);
            using Image downhill = await Capture("downhill-calm");
            foreach (MultiMeshInstance3D plant in plants) plant.Visible = plant == plants[2];
            await Configure(slope, "close-roots", new Vector3D(6.2, surface.Radius, -4), new Vector3D(1.4, 0.3, 2), 1.45f);
            using Image close = await Capture("close-roots-calm");

            // Isolate direct dielectric reflection: no environment reflection, wind, geometry or lighting changes.
            // Restoring zero must reproduce the same raster; this is a response check, not an art threshold.
            await Configure(flat, "reflection", new Vector3D(4.5, surface.Radius, -4), new Vector3D(0, 1.8, 5), 3.4f);
            light.RotationDegrees = new Vector3(-35, -28, 0);
            foreach (ShaderMaterial material in plantMaterials) material.SetShaderParameter("authored_reflection_weight", 0f);
            using Image diffuseOnly = await Capture("reflection-zero");
            foreach (ShaderMaterial material in plantMaterials) material.SetShaderParameter("authored_reflection_weight", 0.12f);
            using Image reflected = await Capture("reflection-dielectric");
            byte[] diffusePixels = diffuseOnly.GetData(), reflectedPixels = reflected.GetData();
            bool nonnegative = diffusePixels.Length == reflectedPixels.Length;
            for (int pixel = 0; pixel < diffusePixels.Length && nonnegative; pixel += 4)
                for (int channel = 0; channel < 3; channel++)
                    if (reflectedPixels[pixel + channel] + 1 < diffusePixels[pixel + channel]) nonnegative = false;
            Expect("authored direct reflection contributes without removing diffuse light",
                nonnegative && GrassRenderDifference(diffuseOnly, reflected) > 10,
                "same source roughness, F0 .04 and reflection weight .12; direct contribution is nonnegative");
            foreach (ShaderMaterial material in plantMaterials) material.SetShaderParameter("authored_reflection_weight", 0f);
            using Image diffuseRepeat = await Capture("reflection-zero-repeat");
            Expect("authored direct reflection comparison is repeatable",
                diffusePixels.AsSpan().SequenceEqual(diffuseRepeat.GetData()), "zero-weight repeat is byte-identical");
            foreach (ShaderMaterial material in plantMaterials) material.SetShaderParameter("authored_reflection_weight", 0.12f);
            if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--environment-authored-cover-compression-views") >= 0)
            {
                // Fixed calm views isolate import quality. These are original production PBR shaders and
                // source LOD meshes; the legacy billboard pivot is deliberately not a new root qualification.
                foreach (MultiMeshInstance3D plant in plants) plant.Visible = true;
                await Configure(flat, "compression", new Vector3D(4.5, surface.Radius, -4), new Vector3D(0, 1.2, 6), 3.4f);
                foreach (var angle in new[] { ("side", new Vector3(-18, 70, 0)), ("back", new Vector3(-25, 150, 0)) })
                {
                    light.RotationDegrees = angle.Item2;
                    using Image view = await Capture("near-" + angle.Item1 + "-calm");
                }
                for (int variant = 0; variant < 3; variant++)
                {
                    string name = $"fern_v{variant}_lod1";
                    GroundCoverRoot root = metadata.Get(name);
                    plants[variant].Multimesh.Mesh = Own(AuthoredCoverRootMesh.Create(sourceMeshes[name], root));
                    plantMaterials[variant].SetShaderParameter("authored_growth_length", (float)root.GrowthLength);
                }
                foreach (var angle in new[] { ("front", new Vector3(-48, -28, 0)), ("side", new Vector3(-18, 70, 0)), ("back", new Vector3(-25, 150, 0)) })
                {
                    light.RotationDegrees = angle.Item2;
                    using Image view = await Capture("mid-" + angle.Item1 + "-calm");
                }
                for (int variant = 0; variant < 3; variant++)
                {
                    MeshInstance3D source = sourceMeshes[$"fern_v{variant}_lod2"];
                    if (source.Mesh.SurfaceGetMaterial(0) is not BaseMaterial3D native || native.AlbedoTexture == null)
                        throw new InvalidOperationException("Compression fixture requires the real fern billboard cutout.");
                    plants[variant].Multimesh.Mesh = source.Mesh;
                    Transform3D at = transforms[variant];
                    at.Origin = Sample(flat, at.Origin).RenderPosition;
                    plants[variant].Multimesh.SetInstanceTransform(0, at);
                    ShaderMaterial material = plantMaterials[variant];
                    material.SetShaderParameter("authored_contact_enabled", false);
                    material.SetShaderParameter("billboard", true);
                    material.SetShaderParameter("albedo_texture", native.AlbedoTexture);
                    material.SetShaderParameter("has_normal", false);
                    material.SetShaderParameter("has_roughness", false);
                }
                foreach (var angle in new[] { ("front", new Vector3(-48, -28, 0)), ("back", new Vector3(-25, 150, 0)) })
                {
                    light.RotationDegrees = angle.Item2;
                    using Image view = await Capture("billboard-" + angle.Item1 + "-calm");
                }
            }
            completed = true;

            async Task Configure(DrawnTerrainTile tile, string label, Vector3D bodyTarget, Vector3D bodyOffset, float size)
            {
                phase = label; Budget();
                await PublishContactGpu(atlas, new[] { tile }, tile.ContactRevision, deadline.Token);
                foreach (ShaderMaterial material in plantMaterials) atlas.Bind(material);
                // The fixture's batch bounds enclose its actual drawn fine/morph terrain plus the complete
                // metre-scale fern and wind envelope. They are not a production culling budget or claim.
                foreach (MultiMeshInstance3D plant in plants) plant.Multimesh.CustomAabb = tile.Bounds.Grow(2);
                Vector3 target = frame.ToRender(bodyTarget), offset = frame.DirectionToRender(bodyOffset);
                camera.Position = new Vector3(target.X + offset.X, tile.Bounds.End.Y + offset.Y, target.Z + offset.Z);
                camera.Size = size;
                Vector3 support = Sample(tile, target).RenderPosition;
                camera.LookAt(support + Vector3.Up * 0.35f, Vector3.Up);
                globals.Set("hu_camera_position", RenderingServer.GlobalShaderParameterType.Vec3, camera.GlobalPosition);
                ArrayMesh mesh = Own(GrassRenderGround(tile, camera.GlobalPosition));
                ground.Mesh = mesh;
                Expect("authored camera clears drawn ground: " + label, camera.Position.Y - mesh.GetAabb().End.Y > 0.25f,
                    "camera is above the complete fine/morph envelope, including its own ground footprint");
                ReportClearance(tile, label);
            }

            TerrainContactSample Sample(DrawnTerrainTile tile, Vector3 hint)
            {
                Vector3D body = frame.ToBody(hint);
                var face = CubeSphere.FromDirection(body.Normalized);
                Vector2 uv = new((float)((face.U + 1) * 0.5), (float)((face.V + 1) * 0.5));
                if (face.Face != tile.Key.Face || !TerrainLod.TrySampleContact(tile, tile.Key, uv, camera.GlobalPosition, out TerrainContactSample sample))
                    throw new InvalidOperationException($"Authored render root/clearance sample left drawn tile {tile.Key}: "
                        + $"render={hint}, body={body}, face={face.Face}, UV={uv}. No contact coordinate was clamped.");
                return sample;
            }

            void ReportClearance(DrawnTerrainTile tile, string label)
            {
                for (int variant = 0; variant < 3; variant++)
                {
                    string name = $"fern_v{variant}_lod0";
                    GroundCoverRoot root = metadata.Get(name);
                    Vector3 localRoot = new((float)root.Root.X, (float)root.Root.Y, (float)root.Root.Z);
                    Transform3D transform = transforms[variant];
                    Vector3 at = Sample(tile, transform * localRoot).RenderPosition;
                    using var arrays = sourceMeshes[name].Mesh.SurfaceGetArrays(0);
                    Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    float minimum = float.PositiveInfinity; int below = 0;
                    foreach (Vector3 vertex in vertices)
                    {
                        Vector3 point = at + transform.Basis * (vertex - localRoot);
                        TerrainContactSample support = Sample(tile, point);
                        float separation = (point - support.RenderPosition).Dot(support.GeometricNormal);
                        minimum = Math.Min(minimum, separation); if (separation < -0.005f) below++;
                    }
                    clearances.Add(new { view = label, node = name, root = new[] { at.X, at.Y, at.Z },
                        minimumVertexClearanceMetres = minimum, verticesBelowFiveMillimetres = below, vertices = vertices.Length,
                        meaning = "geometric corners only, includes transparent card margins; not an opaque-frond or continuous footprint proof" });
                }
            }

            async Task<Image> Capture(string label)
            {
                phase = label; Budget();
                viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
                for (int i = 0; i < 3; i++) await ContactGpuNextDraw(deadline.Token);
                Budget();
                Image image = viewport.GetTexture().GetImage();
                try
                {
                    image.Convert(Image.Format.Rgba8);
                    string path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(OutputPath))!,
                        Path.GetFileNameWithoutExtension(OutputPath) + "-authored-" + label + ".png");
                    if (image.SavePng(path) != Error.Ok) throw new IOException("Could not save authored fern PNG.");
                    using Image verified = Image.LoadFromFile(path);
                    bool valid = !verified.IsEmpty() && verified.GetWidth() == 640 && verified.GetHeight() == 360;
                    Expect("authored fern PNG " + label, valid, path);
                    if (!valid) throw new IOException("Authored fern PNG readback failed.");
                    captures.Add(path);
                    return image;
                }
                catch { image.Dispose(); throw; }
            }
        }
        finally
        {
            if (viewport != null && GodotObject.IsInstanceValid(viewport))
            {
                viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
                viewport.QueueFree();
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { while (GodotObject.IsInstanceValid(viewport)) await ContactGpuNextDraw(cleanup.Token); }
                catch (OperationCanceledException) { if (GodotObject.IsInstanceValid(viewport)) viewport.Free(); }
                Expect("authored render retires its viewport", !GodotObject.IsInstanceValid(viewport), "owned art resources are released after native node retirement");
            }
            imported?.Free();
            globals?.Dispose();
            for (int i = resources.Count - 1; i >= 0; i--) resources[i].Dispose();
            PlanetGlobals.Clear(planetOwner);
            if (!string.IsNullOrWhiteSpace(OutputPath)) File.WriteAllText(OutputPath + ".authored-render.json", JsonSerializer.Serialize(new
            {
                completed, lastPhase = phase, captures, peakWorkingSetBytes = peakWorkingSet, workingSetGuardBytes = 1536L * 1024 * 1024,
                runtimeAdmitted = false, clearances,
                limitation = "isolated source-PBR fixture, not hosted-world acceptance; point roots do not certify full frond clearance; optional billboard views use legacy pivots for texture comparison only"
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        T Own<T>(T resource) where T : IDisposable { resources.Add(resource); return resource; }
        void Budget()
        {
            deadline.Token.ThrowIfCancellationRequested();
            process.Refresh(); peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
            if (peakWorkingSet > 1536L * 1024 * 1024) throw new InvalidOperationException("Authored fern render incomplete: process working-set guard exceeded during " + phase);
        }
    }

    private static DrawnTerrainTile AuthoredRenderTile(ProbeSurface source, RenderFrame frame, float slopeX, float slopeZ, long revision)
    {
        TileGrid grid = TileMesh.Build(source, new TileKey(2, 4, 8, 8), 32);
        foreach ((float[] positions, float factor) in new[] { (grid.Positions, 1f), (grid.MorphPositions, 0.45f) })
        for (int i = 0; i < positions.Length; i += 3)
        {
            Vector3D body = grid.Origin + new Vector3D(positions[i], positions[i + 1], positions[i + 2]);
            Vector3D direction = body.Normalized;
            double x = slopeX * factor, z = slopeZ * factor;
            // Radial intersection with a plane through (4.5, radius, -4); preserves each cube-face address.
            double radius = (source.Radius - x * 4.5 + z * 4) / (direction.Y - x * direction.X - z * direction.Z);
            Vector3D local = direction * radius - grid.Origin;
            positions[i] = (float)local.X; positions[i + 1] = (float)local.Y; positions[i + 2] = (float)local.Z;
        }
        Transform3D transform = frame.Anchor(grid.Origin);
        return new DrawnTerrainTile(grid.Key, grid, transform, new Vector2(3, 10), ContactGpuBounds(grid, transform), revision);
    }
}

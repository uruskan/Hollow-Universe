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

namespace HollowUniverse.Surface;

public partial class EnvironmentLifetimeCheck
{
    /// <summary>
    /// One explicit family, original production meshes/pivots/shader, exact saved CTEX versus current
    /// imported materials. This comparison grants no logical-root, billboard-anchor or terrain approval.
    /// </summary>
    private async Task CheckCoverCompressionRender(string receiptPath, string receiptSha256)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        using Process process = Process.GetCurrentProcess();
        var timer = Stopwatch.StartNew();
        var resources = new List<IDisposable>();
        var baselines = new Dictionary<string, CompressedTexture2D>(StringComparer.Ordinal);
        var captures = new List<object>();
        var comparisons = new List<object>();
        var textureFormats = new List<object>();
        var describedCandidates = new HashSet<string>(StringComparer.Ordinal);
        string phase = "authenticate", family = "";
        bool completed = false;
        long peakWorkingSet = 0, planetOwner = 0;
        int maximumBaselineTextures = 0;
        Node? imported = null;
        SubViewport? viewport = null;
        SurfaceDiagnosticGlobals? globals = null;
        try
        {
            Budget();
            CoverCompressionFamily qualification = CoverCompressionAuthenticate(receiptPath, receiptSha256);
            family = qualification.Family;
            var inputBySource = qualification.Inputs.ToDictionary(i => i.Source, StringComparer.Ordinal);
            using JsonDocument catalog = CoverJson(ProjectSettings.GlobalizePath("res://assets/surface/ground-cover/catalog.json"));
            JsonElement group = catalog.RootElement.GetProperty("families").EnumerateArray()
                .Single(f => f.GetProperty("key").GetString() == family);
            CoverCompressionRequire(group.GetProperty("model").GetString() == qualification.Model
                && group.GetProperty("outputSha256").GetString() == qualification.ModelSha256,
                "Render catalog/model qualification differs");
            phase = "load one current family";
            imported = GD.Load<PackedScene>(qualification.Model).Instantiate();
            Budget();
            var sources = new Dictionary<string, MeshInstance3D>(StringComparer.Ordinal);
            void Collect(Node node)
            {
                if (node is Node3D spatial) CoverCompressionRequire(spatial.Transform.IsEqualApprox(Transform3D.Identity),
                    "Cover compression requires the existing identity-transform imported representation");
                if (node is MeshInstance3D mesh) CoverCompressionRequire(sources.TryAdd(mesh.Name.ToString(), mesh), "Duplicate source mesh name");
                foreach (Node child in node.GetChildren()) Collect(child);
            }
            Collect(imported);
            CoverCompressionRequire(sources.Count == 9, "Expected exactly three variants by three source LODs");
            var art = new CoverCompressionArt[3, 3];
            float widest = 0, lowest = 0, highest = 0, maxRadius = 0;
            var variants = group.GetProperty("variants").EnumerateArray().ToArray();
            CoverCompressionRequire(variants.Length == 3, "Expected three source variants");
            foreach (JsonElement variant in variants)
            {
                int v = variant.GetProperty("variant").GetInt32();
                CoverCompressionRequire(v is >= 0 and < 3, "Invalid source variant index");
                JsonElement[] levels = variant.GetProperty("levels").EnumerateArray().ToArray();
                CoverCompressionRequire(levels.Length == 3, "Expected three source LODs");
                for (int lod = 0; lod < 3; lod++)
                {
                    string name = levels[lod].GetProperty("node").GetString()!;
                    CoverCompressionRequire(name == $"{family}_v{v}_lod{lod}" && sources.TryGetValue(name, out _), "Unexpected source LOD identity");
                    Mesh mesh = sources[name].Mesh;
                    CoverCompressionRequire(mesh.GetSurfaceCount() == 1 && mesh.SurfaceGetMaterial(0) is BaseMaterial3D,
                        "Expected the original single-surface PBR mesh");
                    var native = (BaseMaterial3D)mesh.SurfaceGetMaterial(0);
                    bool billboard = levels[lod].GetProperty("billboard").GetBoolean();
                    CoverCompressionRequire(billboard == (lod == 2) && native.AlbedoTexture != null, "Unexpected source billboard/material mapping");
                    ValidateTexture(native.AlbedoTexture!, billboard ? "1" : "0", "albedo_cutout");
                    if (native.NormalTexture != null) ValidateTexture(native.NormalTexture, billboard ? "1" : "0", billboard ? "billboard_normal_alpha" : "normal_xy");
                    Texture2D? roughness = native.RoughnessTexture ?? native.OrmTexture;
                    if (roughness != null) ValidateTexture(roughness, "0", "packed_ort");
                    CoverCompressionRequire(billboard || (native.NormalTexture != null && roughness != null), "Near/mid material lost supplied PBR maps");
                    art[v, lod] = new(mesh, native, Math.Max(0.05f, levels[lod].GetProperty("height").GetSingle()), billboard, name);
                    Aabb bounds = mesh.GetAabb();
                    widest = Math.Max(widest, Math.Max(bounds.Size.X, bounds.Size.Z));
                    lowest = Math.Min(lowest, bounds.Position.Y); highest = Math.Max(highest, bounds.End.Y);
                    maxRadius = Math.Max(maxRadius, Math.Max(Math.Max(Math.Abs(bounds.Position.X), Math.Abs(bounds.End.X)),
                        Math.Max(Math.Abs(bounds.Position.Z), Math.Abs(bounds.End.Z))));
                }
            }
            CoverCompressionRequire(widest > 0 && widest < 4 && highest - lowest is > 0 and < 4
                && art.Cast<CoverCompressionArt>().All(a => a != null), "Unexpected or incomplete bounded physical plant dimensions");

            var frame = new RenderFrame(new Vector3D(0, 100, 0));
            planetOwner = PlanetGlobals.Claim(frame, 100);
            ImageTexture zero = Own(GrassRenderSolid(new Color(0, 0, 0, 1)));
            ImageTexture one = Own(GrassRenderSolid(Colors.White));
            ImageTexture noise = Own(GrassRenderSolid(new Color(0.7f, 0.7f, 0.7f, 1)));
            ImageTexture3D darkAir = Own(GrassRenderVolume(Colors.Black));
            ImageTexture3D clearAir = Own(GrassRenderVolume(Colors.White));
            globals = new SurfaceDiagnosticGlobals();
            globals.Setup(100, zero, one, noise, darkAir, clearAir);
            viewport = new SubViewport
            {
                Name = "CoverCompressionPairedRender", Size = new Vector2I(640, 360), OwnWorld3D = true,
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
            var light = new DirectionalLight3D { LightColor = Colors.White, LightEnergy = 1.7f, ShadowEnabled = false };
            viewport.AddChild(light);
            var camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Near = 0.01f, Far = 100, Current = true };
            viewport.AddChild(camera);
            var groundMaterial = Own(new StandardMaterial3D { AlbedoColor = new Color(0.29f, 0.18f, 0.095f), Roughness = 0.95f });
            var floorMesh = Own(new PlaneMesh { Size = new Vector2(30, 30) });
            viewport.AddChild(new MeshInstance3D
            {
                Mesh = floorMesh, MaterialOverride = groundMaterial, Position = new Vector3(0, lowest - 0.02f, 0),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            });
            Shader shader = GD.Load<Shader>("res://shaders/surface_authored_cover.gdshader");
            var plants = new MultiMeshInstance3D[3];
            var materials = new ShaderMaterial[3];
            float spacing = widest + 0.15f;
            for (int v = 0; v < 3; v++)
            {
                ShaderMaterial material = Own(new ShaderMaterial { Shader = shader });
                materials[v] = material;
                material.SetShaderParameter("far_radius", 42f);
                material.SetShaderParameter("authored_contact_enabled", false);
                material.SetShaderParameter("terrain_contact_enabled", false);
                var multimesh = Own(new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true,
                    Mesh = art[v, 0].Mesh, InstanceCount = 1,
                    CustomAabb = new Aabb(new Vector3((v - 1) * spacing - maxRadius * 1.5f, lowest, -maxRadius * 1.5f),
                        new Vector3(maxRadius * 3, highest - lowest, maxRadius * 3)).Grow(0.05f)
                });
                multimesh.SetInstanceTransform(0, new Transform3D(Basis.Identity, new Vector3((v - 1) * spacing, 0, 0)));
                multimesh.SetInstanceCustomData(0, new Color(1, 0.99f, 0.95f, 0.45f + v * 0.7f));
                plants[v] = new MultiMeshInstance3D
                {
                    Name = family + "_variant_" + v, Multimesh = multimesh, MaterialOverride = material,
                    Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    GIMode = GeometryInstance3D.GIModeEnum.Disabled
                };
                viewport.AddChild(plants[v]);
            }
            float stageHeight = Math.Max((highest - lowest) * 1.35f, (spacing * 2 + widest) / (640f / 360) * 1.15f);
            int activeLod = -1;
            string activeMaterial = "";
            var lights = new[] { ("front", new Vector3(-48, -28, 0)), ("side", new Vector3(-18, 70, 0)), ("back", new Vector3(-25, 150, 0)) };
            for (int lod = 0; lod < 3; lod++)
            {
                SetLod(lod);
                ConfigureCamera(false);
                foreach ((string label, Vector3 rotation) in lights)
                {
                    light.RotationDegrees = rotation;
                    await Pair((lod == 0 ? "near" : lod == 1 ? "mid" : "billboard") + "-" + label, false, lod == 0 && label == "front");
                }
            }
            SetLod(0);
            ConfigureCamera(true);
            light.RotationDegrees = lights[0].Item2;
            await Pair("close-crown-v2-front", true, false);
            completed = true;

            void ValidateTexture(Texture2D texture, string expectedMaterial, string expectedRole)
            {
                CoverCompressionRequire(inputBySource.TryGetValue(texture.ResourcePath, out CoverCompressionInput? input)
                    && input.Material == expectedMaterial && input.Role == expectedRole,
                    "Imported material texture no longer matches the authenticated source/role: " + texture.ResourcePath);
                Image.Format expected = expectedRole == "normal_xy" ? Image.Format.RgtcRg : Image.Format.BptcRgba;
                Image.Format actual = RenderingServer.TextureGetFormat(texture.GetRid());
                CoverCompressionRequire(texture.GetWidth() == 2048 && texture.GetHeight() == 2048
                    && actual == expected, $"Candidate material {texture.ResourcePath} has wrong format/resolution: {actual} {texture.GetWidth()}x{texture.GetHeight()}, expected {expected} 2048x2048");
                if (describedCandidates.Add(texture.ResourcePath)) textureFormats.Add(new
                { source = texture.ResourcePath, role = expectedRole, baseline = false, format = actual.ToString(),
                    sha256 = input!.CandidateSha256 });
            }

            void SetLod(int lod)
            {
                phase = "prepare LOD " + lod; Budget();
                // Clear every material's old baseline reference before retiring that private texture group.
                if (activeLod >= 0) Bind(false);
                string required = lod == 2 ? "1" : "0";
                if (required != activeMaterial)
                {
                    foreach (CompressedTexture2D texture in baselines.Values) texture.Dispose();
                    baselines.Clear();
                    foreach (CoverCompressionInput input in qualification.Inputs.Where(i => i.Material == required))
                    {
                        var texture = new CompressedTexture2D();
                        baselines.Add(input.Source, texture);
                        CoverCompressionRequire(texture.Load(input.Baseline) == Error.Ok, "Cannot load exact original material CTEX");
                        Image.Format expected = input.Role is "normal_xy" or "packed_ort" ? Image.Format.Rgb8 : Image.Format.Rgba8;
                        Image.Format actual = RenderingServer.TextureGetFormat(texture.GetRid());
                        CoverCompressionRequire(texture.GetWidth() == 2048 && texture.GetHeight() == 2048
                            && actual == expected, $"Original CTEX {input.Source} has wrong format/resolution: {actual} {texture.GetWidth()}x{texture.GetHeight()}, expected {expected} 2048x2048");
                        textureFormats.Add(new { source = input.Source, role = input.Role, baseline = true,
                            format = actual.ToString(), sha256 = input.BaselineSha256 });
                    }
                    maximumBaselineTextures = Math.Max(maximumBaselineTextures, baselines.Count);
                    activeMaterial = required;
                }
                activeLod = lod;
                for (int v = 0; v < 3; v++)
                {
                    plants[v].Multimesh.Mesh = art[v, lod].Mesh;
                    materials[v].SetShaderParameter("billboard", art[v, lod].Billboard);
                    materials[v].SetShaderParameter("plant_height", art[v, lod].Height);
                }
                Bind(false);
            }

            void Bind(bool baseline)
            {
                Texture2D Texture(Texture2D native) => baseline ? baselines[native.ResourcePath] : native;
                for (int v = 0; v < 3; v++)
                {
                    BaseMaterial3D native = art[v, activeLod].Native;
                    Texture2D? roughness = native.RoughnessTexture ?? native.OrmTexture;
                    materials[v].SetShaderParameter("albedo_texture", Texture(native.AlbedoTexture!));
                    materials[v].SetShaderParameter("normal_texture", native.NormalTexture == null ? zero : Texture(native.NormalTexture));
                    materials[v].SetShaderParameter("roughness_texture", roughness == null ? Texture(native.AlbedoTexture!) : Texture(roughness));
                    materials[v].SetShaderParameter("has_normal", native.NormalTexture != null);
                    materials[v].SetShaderParameter("has_roughness", roughness != null);
                }
            }

            void ConfigureCamera(bool close)
            {
                Vector3 target = new(0, (highest + lowest) * 0.5f, 0);
                float height = stageHeight;
                if (close)
                {
                    Aabb bounds = art[2, 0].Mesh.GetAabb();
                    target = new Vector3(spacing + bounds.GetCenter().X, bounds.End.Y - bounds.Size.Y * 0.3f, bounds.GetCenter().Z);
                    height = Math.Max(0.18f, bounds.Size.Y * 0.75f);
                }
                camera.Size = height;
                camera.Position = target + new Vector3(0, Math.Max(0.15f, height * 0.20f), Math.Max(3, height * 1.5f));
                camera.LookAt(target, Vector3.Up);
                globals.Set("hu_camera_position", RenderingServer.GlobalShaderParameterType.Vec3, camera.GlobalPosition);
            }

            async Task Pair(string label, bool close, bool repeat)
            {
                phase = label; Budget();
                foreach (MultiMeshInstance3D plant in plants) plant.Visible = false;
                using Image control = await Capture(label + "-control");
                for (int v = 0; v < 3; v++) plants[v].Visible = !close || v == 2;
                Bind(true);
                using Image baseline = await Capture(label + "-baseline");
                if (repeat)
                {
                    using Image again = await Capture(label + "-baseline-repeat");
                    bool deterministic = baseline.GetData().AsSpan().SequenceEqual(again.GetData());
                    Expect("cover paired render is calm and deterministic", deterministic,
                        "identical source textures/camera/light and zero wind; no temporal AA or random view changes");
                    CoverCompressionRequire(deterministic, "Temporal changes prevent a controlled texture comparison");
                }
                Bind(false);
                using Image candidate = await Capture(label + "-candidate");
                int baselineVisible = GrassRenderDifference(control, baseline), candidateVisible = GrassRenderDifference(control, candidate);
                Expect("cover PBR pair contains visible original and candidate " + family + " " + label,
                    baselineVisible > 0 && candidateVisible > 0, $"changed control pixels: original={baselineVisible}, candidate={candidateVisible}; not an appearance acceptance threshold");
                byte[] a = baseline.GetData(), b = candidate.GetData();
                var error = new CoverError(); long changed = 0;
                for (int i = 0; i < a.Length; i += 4)
                {
                    bool different = false;
                    for (int channel = 0; channel < 3; channel++)
                    { error.Add(a[i + channel] / 255.0, b[i + channel] / 255.0); different |= a[i + channel] != b[i + channel]; }
                    if (different) changed++;
                }
                comparisons.Add(new { label, lod = activeLod, nodes = Enumerable.Range(0, 3).Where(v => !close || v == 2).Select(v => art[v, activeLod].Name).ToArray(),
                    baselineVisible, candidateVisible, changedPixels = changed, encodedRgb = error.Report(),
                    cameraPosition = new[] { camera.Position.X, camera.Position.Y, camera.Position.Z }, orthographicHeightMetres = camera.Size,
                    lightRotation = new[] { light.RotationDegrees.X, light.RotationDegrees.Y, light.RotationDegrees.Z } });
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
                        Path.GetFileNameWithoutExtension(OutputPath) + "-cover-" + family + "-" + label + ".png");
                    CoverCompressionRequire(image.SavePng(path) == Error.Ok, "Could not save paired cover PNG");
                    using Image verified = Image.LoadFromFile(path);
                    bool valid = !verified.IsEmpty() && verified.GetWidth() == 640 && verified.GetHeight() == 360;
                    Expect("cover paired PNG " + family + " " + label, valid, path);
                    CoverCompressionRequire(valid, "Paired cover PNG readback failed");
                    captures.Add(new { label, path, sha256 = CoverHash(path, null, 4 * 1024 * 1024) });
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
                Expect("cover paired render retires its viewport", !GodotObject.IsInstanceValid(viewport), "private textures retire after native drawing nodes");
            }
            imported?.Free();
            globals?.Dispose();
            for (int i = resources.Count - 1; i >= 0; i--) resources[i].Dispose();
            foreach (CompressedTexture2D texture in baselines.Values) texture.Dispose();
            baselines.Clear();
            if (planetOwner != 0) PlanetGlobals.Clear(planetOwner);
            File.WriteAllText(OutputPath + ".cover-compression-render.json", JsonSerializer.Serialize(new
            {
                schema = "hollow.cover-compression-paired-render.v1", family, completed, lastPhase = phase,
                receiptPath = Path.GetFullPath(receiptPath), receiptSha256, seconds = timer.Elapsed.TotalSeconds,
                peakWorkingSetBytes = peakWorkingSet, workingSetGuardBytes = 1536L * 1024 * 1024, maximumBaselineTextures,
                renderer = RenderingServer.GetCurrentRenderingMethod(), driver = RenderingServer.GetCurrentRenderingDriverName(),
                runtimeAdmitted = false, appearanceApproved = false, textureFormats, captures, comparisons,
                limitation = "Texture-only comparison: unchanged source geometry/pivots, physical scale 1, production legacy shader and actual PBR maps. Original CTEX snapshots versus current imported materials within each identical calm camera/light pair. Front/side/back refer to light directions. Orthographic framing magnifies source LODs for inspection; this does not prove runtime distance transitions, root anchors, billboard anchors, terrain clearance, hosted appearance or total VRAM residency. Current billboard shader does not use its normal map."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        T Own<T>(T resource) where T : IDisposable { resources.Add(resource); return resource; }
        void Budget()
        {
            deadline.Token.ThrowIfCancellationRequested();
            process.Refresh(); peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
            if (peakWorkingSet > 1536L * 1024 * 1024) throw new InvalidOperationException("Cover paired render exceeded its process working-set guard during " + phase);
        }
    }

    private sealed record CoverCompressionArt(Mesh Mesh, BaseMaterial3D Native, float Height, bool Billboard, string Name);
}

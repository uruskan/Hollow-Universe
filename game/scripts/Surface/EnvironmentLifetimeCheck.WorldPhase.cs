using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Hollow.Core;

namespace HollowUniverse.Surface;

public partial class EnvironmentLifetimeCheck
{
    // Actual production ground_look and wave helper, with small patterned maps. This checks sampler phase and
    // body/render normal frames; it does not claim final art approval or qualify large-radius FaceAnchor UVs.
    private async Task CheckWorldPhases()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var globals = new SurfaceDiagnosticGlobals();
        var v3 = RenderingServer.GlobalShaderParameterType.Vec3;
        globals.Set("hu_phase_wave_main", v3, Vector3.Zero);
        globals.Set("hu_phase_wave_foliage", v3, Vector3.Zero);
        globals.Set("hu_phase_wave_tree", v3, Vector3.Zero);
        globals.Set("hu_phase_wave_angles", v3, Vector3.Zero);
        RenderFrame frame = RenderFrame.World;
        long owner = PlanetGlobals.Claim(frame, 1354);
        var metres = new float[16]; Array.Fill(metres, 4); metres[3] = 2.4f; metres[13] = 2.5f;
        Vector3 wind = new(.8f, 0, .6f);
        SurfaceWorldPhases? phases = null;
        SubViewport? viewport = null;
        Shader? shader = null;
        Texture2DArray? albedo = null, normals = null, roughHeight = null;
        ImageTexture? noise = null;
        var materials = new List<ShaderMaterial>();
        var bindings = new List<IDisposable>();
        Image? baseline = null;
        try
        {
            phases = new SurfaceWorldPhases(owner, metres);
            phases.Update(frame, wind);
            shader = new Shader { Code = WorldPhaseProbeShader };
            albedo = WorldPhaseArray(0); normals = WorldPhaseArray(1); roughHeight = WorldPhaseArray(2);
            using (Image image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8))
            { image.Fill(new Color(.5f, .5f, .5f, .5f)); noise = ImageTexture.CreateFromImage(image); }
            viewport = new SubViewport { Name = "CanonicalSurfacePhaseProbe", Size = new Vector2I(256, 64),
                Disable3D = true, UseHdr2D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled };
            AddChild(viewport);
            var flips = new Vector2[16]; Array.Fill(flips, Vector2.One);
            for (int i = 0; i < 4; i++)
            {
                var material = new ShaderMaterial { Shader = shader }; materials.Add(material);
                material.SetShaderParameter("albedo_array", albedo);
                material.SetShaderParameter("normal_array", normals);
                material.SetShaderParameter("rough_height_array", roughHeight);
                material.SetShaderParameter("macro_noise", noise);
                material.SetShaderParameter("canopy_texture", noise);
                material.SetShaderParameter("layer_metres", metres);
                material.SetShaderParameter("layer_flip", flips);
                material.SetShaderParameter("probe_layer", i < 2 ? 3 : 13);
                bindings.Add(phases.BindGround(material));
                viewport.AddChild(new ColorRect { Position = new Vector2(i * 64, 0), Size = new Vector2(64, 64),
                    Material = material, Color = Colors.White });
            }
            Vector3D point = new(421.125, 592.375, -731.625);
            Configure(point, false);
            baseline = await Capture();
            Expect("phase shared terrain/soil material output", PairError(baseline) < .001f,
                "two independently bound materials per layer share all production look and wave outputs");
            Expect("phase baseline screenshot", baseline.SavePng(OutputPath + ".world-phase-baseline.png") == Error.Ok,
                "rock terrain/soil then sandstone terrain/soil; rows albedo, normal, rough-height, waves");
            frame = new RenderFrame(point);
            await Compare("rotated tangent frame", point);
            frame.Move(point + new Vector3D(-4097, 17, -4099));
            await Compare("negative 4096 boundary", point);
            frame.Move(point + new Vector3D(8193, -31, 4097));
            await Compare("multiple positive 4096 boundaries", point);
            frame.Move(point);
            await Compare("return to original body anchor", point);
            frame = new RenderFrame(point + new Vector3D(23, -19, 51));
            await Compare("independent view starting axes", point);
            baseline.Dispose(); baseline = null;

            // The oracle evaluates the absolute double body point directly. No CPU copy of local+anchor packing
            // is used as its expectation. A large radius/anchor never enters a shader float or RGB position map.
            Vector3D large = new(6_371_123.375, -221_345.625, 75_123.125);
            frame = new RenderFrame(large);
            await Oracle("large body anchor", large);
            frame.Move(large + new Vector3D(-4097, 33, 8193));
            await Oracle("large body anchor after signed rebase", large);
            Vector3D high = new(999_999_123.375, -123_345.625, 75_123.125);
            frame = new RenderFrame(high);
            await Oracle("explicit maximum anchor domain", high);

            long revision = phases.Revision, materialUpdates = phases.MaterialUpdates;
            phases.Update(frame, wind); // warm the exact unchanged path before measuring it
            long before = GC.GetAllocatedBytesForCurrentThread();
            phases.Update(frame, wind);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Expect("unchanged phase snapshot performs no publication or allocation", phases.Revision == revision
                && phases.MaterialUpdates == materialUpdates && allocated == 0,
                $"revision={revision}, materialUpdates={materialUpdates}, managed bytes={allocated}");
            int initial = phases.BindingCount;
            for (int i = initial; i < SurfaceWorldPhases.MaximumBindings; i++)
            { var material = new ShaderMaterial { Shader = shader }; materials.Add(material); bindings.Add(phases.BindGround(material)); }
            using (var excess = new ShaderMaterial { Shader = shader })
            {
                bool refused = false;
                try { phases.BindGround(excess); } catch (InvalidOperationException) { refused = true; }
                Expect("phase material capacity refuses whole extra binding", refused && phases.BindingCount == 64,
                    "fixed 64-entry borrowed binding table; no registration escapes on refusal");
            }
            bool invalid = false;
            try { phases.Update(new RenderFrame(new Vector3D(SurfaceWorldPhases.MaximumAnchorMetres + 1, 0, 0)), wind); }
            catch (ArgumentOutOfRangeException) { invalid = true; }
            Expect("phase anchor bounds reject before publication", invalid && phases.Revision == revision,
                "finite body-coordinate domain is explicit, with no clamping or giant float fallback");
            long successor = PlanetGlobals.Claim(RenderFrame.World, 250);
            try
            {
                bool stale = false;
                try { phases.Update(frame, wind); } catch (InvalidOperationException) { stale = true; }
                phases.Dispose(); phases.Dispose();
                Expect("phase retirement releases borrowed bindings without touching successor", stale
                    && phases.BindingCount == 0 && PlanetGlobals.IsOwner(successor)
                    && materials.TrueForAll(GodotObject.IsInstanceValid),
                    "old owner cannot publish; repeated disposal clears references, never disposes borrowed materials");
            }
            finally { PlanetGlobals.Clear(successor); }

            void Configure(Vector3D body, bool oracle)
            {
                PlanetGlobals.Set(owner, frame, 1354);
                phases.Update(frame, wind);
                for (int i = 0; i < 4; i++)
                {
                    materials[i].SetShaderParameter("probe_point", frame.ToRender(body));
                    materials[i].SetShaderParameter("probe_oracle", oracle);
                }
            }
            async Task<Image> Capture()
            {
                viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
                for (int i = 0; i < 2; i++) await ContactGpuNextDraw(deadline.Token);
                return viewport.GetTexture().GetImage();
            }
            async Task Compare(string name, Vector3D body)
            {
                Configure(body, false);
                using Image image = await Capture();
                float error = PixelError(baseline!, image);
                Expect("native world phase " + name, error < .012f && PairError(image) < .001f,
                    $"maximum RGBA difference={error:0.000000}; full production ground look and normal frame, local-float/readback tolerance .012");
                if (name == "negative 4096 boundary")
                    Expect("phase rebase screenshot", image.SavePng(OutputPath + ".world-phase-rebase.png") == Error.Ok, name);
            }
            async Task Oracle(string name, Vector3D body)
            {
                Configure(body, true);
                using Image image = await Capture();
                Vector3 tree = wind * -.035f, foliage = SurfaceWorldPhases.FoliageDetailWave + tree;
                double[] waves = { Dot(body, SurfaceWorldPhases.MainWave), Dot(body, foliage), Dot(body, tree) };
                float error = 0;
                for (int tile = 0; tile < 4; tile++)
                {
                    double cycles = .5f / metres[tile < 2 ? 3 : 13];
                    double[] uv = { body.X * cycles * Math.PI * 2, body.Y * cycles * Math.PI * 2, body.Z * cycles * Math.PI * 2 };
                    for (int row = 0; row < 4; row++)
                    {
                        Color actual = image.GetPixel(tile * 64 + 32, row * 16 + 8);
                        double[] angles = row < 2 ? uv : waves;
                        for (int c = 0; c < 3; c++)
                        {
                            double expected = .5 + .5 * (row % 2 == 0 ? Math.Sin(angles[c]) : Math.Cos(angles[c]));
                            error = Math.Max(error, (float)Math.Abs(actual[c] - expected));
                        }
                    }
                }
                Expect("native absolute body phase oracle " + name, error < .012f,
                    $"double absolute-point sine/cosine versus actual GPU local+phase; max channel error={error:0.000000}");
            }
        }
        finally
        {
            if (viewport != null && GodotObject.IsInstanceValid(viewport)) viewport.Free();
            baseline?.Dispose();
            foreach (IDisposable binding in bindings) binding.Dispose();
            phases?.Dispose();
            foreach (ShaderMaterial material in materials) material.Dispose();
            shader?.Dispose(); noise?.Dispose(); roughHeight?.Dispose(); normals?.Dispose(); albedo?.Dispose();
            PlanetGlobals.Clear(owner);
        }
    }

    private static double Dot(Vector3D point, Vector3 wave) => point.X * wave.X + point.Y * wave.Y + point.Z * wave.Z;
    private static float PixelError(Image a, Image b)
    {
        float error = 0;
        for (int y = 1; y < 63; y++)
            for (int x = 1; x < 255; x++)
            {
                Color delta = a.GetPixel(x, y) - b.GetPixel(x, y);
                for (int c = 0; c < 4; c++) error = Math.Max(error, Math.Abs(delta[c]));
            }
        return error;
    }
    private static float PairError(Image image)
    {
        float error = 0;
        for (int y = 1; y < 63; y++) for (int x = 1; x < 63; x++) for (int pair = 0; pair < 2; pair++)
        {
            Color delta = image.GetPixel(pair * 128 + x, y) - image.GetPixel(pair * 128 + x + 64, y);
            for (int c = 0; c < 4; c++) error = Math.Max(error, Math.Abs(delta[c]));
        }
        return error;
    }
    private static Texture2DArray WorldPhaseArray(int role)
    {
        var layers = new Godot.Collections.Array<Image>();
        using var storage = (Godot.Collections.Array)layers;
        using Image image = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8);
        for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
        {
            float u = x / 15f, v = y / 15f;
            image.SetPixel(x, y, role == 0 ? new Color(.2f + .4f * u, .15f + .3f * v, .1f + .25f * u * v)
                : role == 1 ? new Color(.4f + .2f * u, .38f + .24f * v, .95f)
                : new Color(.6f + .3f * u, .35f + .2f * v, 0));
        }
        image.GenerateMipmaps();
        for (int i = 0; i < 16; i++) layers.Add(image);
        var texture = new Texture2DArray();
        try
        {
            if (texture.CreateFromImages(layers) != Error.Ok) throw new InvalidOperationException("Phase probe array upload failed.");
            return texture;
        }
        catch { texture.Dispose(); throw; }
    }

    private const string WorldPhaseProbeShader = """
        shader_type canvas_item;
        render_mode unshaded;
        #include "res://shaders/surface_grass.gdshaderinc"
        #include "res://shaders/surface_ground_look.gdshaderinc"
        uniform vec3 probe_point;
        uniform int probe_layer = 3;
        uniform bool probe_oracle = false;
        void fragment() {
            int row = min(int(UV.y * 4.0), 3);
            if (probe_oracle) {
                vec3 p = hu_body_local(probe_point) * ground_body_cycles[probe_layer] + ground_body_phase[probe_layer];
                vec3 a = row < 2 ? p * 6.283185307179586 : hu_body_wave_phases(probe_point);
                COLOR = vec4((row == 0 || row == 2 ? sin(a) : cos(a)) * 0.5 + 0.5, 1.0);
            } else {
                vec2 grid = vec2(UV.x - 0.5, fract(UV.y * 4.0) - 0.5);
                vec3 p = probe_point + transpose(hu_render_to_body) * vec3(grid.x * .13, grid.y * .17, grid.x * .07);
                vec3 n = transpose(hu_render_to_body) * normalize(vec3(.34, .72, .6));
                vec4 m0 = probe_layer == 3 ? vec4(0,0,0,1) : vec4(0);
                vec4 m3 = probe_layer == 13 ? vec4(0,1,0,0) : vec4(0);
                Look look = ground_look(vec2(19,37) + grid, transpose(hu_render_to_body) * vec3(1,0,0),
                    transpose(hu_render_to_body) * vec3(0,1,0), n, .7, 30.0, 0.0, p,
                    m0, vec4(0), vec4(0), m3, vec4(.5,.5,0,0), 5.0);
                vec3 result;
                if (row == 0) result = look.albedo;
                else if (row == 1) result = normalize(hu_render_to_body * (n + look.bump)) * .5 + .5;
                else if (row == 2) result = vec3(look.rough, look.height, look.reflection_weight);
                else result = sin(hu_body_wave_phases(p) + vec3(.73, .37, .37)) * .5 + .5;
                COLOR = vec4(result, 1);
            }
        }
        """;
}

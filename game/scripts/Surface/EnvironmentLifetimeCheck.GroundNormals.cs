using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace HollowUniverse.Surface;

public partial class EnvironmentLifetimeCheck
{
    // Analytic normals on signed axis planes, a tilted surface and rotated texture axes.
    // Executes the production include on the GPU; expectations below are independent
    // geometric examples, not a CPU transcription of its implementation.
    private async Task CheckGroundNormalFrames()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var shader = new Shader { Code = """
            shader_type canvas_item;
            render_mode unshaded;
            #include "res://shaders/surface_ground_normals.gdshaderinc"
            uniform vec3 base_normal;
            uniform vec3 sample_x;
            uniform vec3 sample_y;
            uniform vec3 sample_z;
            uniform vec3 weights;
            uniform vec2 axes;
            uniform bool planar;
            uniform vec2 rotation_cs;
            void fragment() {
                vec3 bump;
                if (planar) {
                    mat2 r = mat2(rotation_cs, vec2(-rotation_cs.y, rotation_cs.x));
                    vec2 slope = hu_ground_rotated_slope(sample_x, axes, r);
                    bump = hu_ground_plane_bump(slope, vec3(1,0,0), vec3(0,1,0), base_normal);
                } else {
                    bump = hu_ground_triplanar_bump(sample_x, sample_y, sample_z, axes, weights, base_normal);
                }
                COLOR = vec4(normalize(base_normal + bump) * 0.5 + 0.5, 1.0);
            }
            """ };
        var materials = new List<ShaderMaterial>();
        SubViewport? viewport = null;
        var cases = new List<GroundNormalCase>();
        var flat = new Vector3(0.5f, 0.5f, 1);
        foreach (Vector3 n in new[] { Vector3.Right, Vector3.Left, Vector3.Up, Vector3.Down, Vector3.Back, Vector3.Forward })
            cases.Add(new("flat " + n, n, flat, flat, flat, n.Abs(), Vector2.One, false, Vector2.Right, n));
        foreach (float sign in new[] { -1f, 1f })
        {
            cases.Add(new("X zy " + sign, new(sign, 0, 0), new(.6f, .8f, 1), flat, flat,
                Vector3.Right, Vector2.One, false, Vector2.Right, new(sign, .6f, .2f)));
            cases.Add(new("Y xz " + sign, new(0, sign, 0), flat, new(.75f, .5f, 1), flat,
                Vector3.Up, Vector2.One, false, Vector2.Right, new(.5f, sign, 0)));
            cases.Add(new("Z xy " + sign, new(0, 0, sign), flat, flat, new(.5f, .75f, 1),
                Vector3.Back, Vector2.One, false, Vector2.Right, new(0, .5f, sign)));
        }
        float q = Mathf.Sqrt(.5f);
        cases.Add(new("two projections on diagonal", new(q, q, 0), new(.75f, .5f, 1), new(.5f, .75f, 1), flat,
            new(.5f, .5f, 0), Vector2.One, false, Vector2.Right, new(q, q, .5f)));
        cases.Add(new("remove normal component", new(q, q, 0), flat, new(.75f, .5f, 1), flat,
            Vector3.Up, Vector2.One, false, Vector2.Right, new(q + .25f, q - .25f, 0)));
        cases.Add(new("quarter-turn texture", Vector3.Back, new(.75f, .5f, 1), flat, flat,
            Vector3.Zero, Vector2.One, true, Vector2.Down, new(0, -.5f, 1)));
        cases.Add(new("half-turn texture", Vector3.Back, new(.75f, .5f, 1), flat, flat,
            Vector3.Zero, Vector2.One, true, Vector2.Left, new(-.5f, 0, 1)));
        cases.Add(new("normal channel flip before rotation", Vector3.Back, new(.75f, .5f, 1), flat, flat,
            Vector3.Zero, new(-1, 1), true, Vector2.Down, new(0, .5f, 1)));
        cases.Add(new("normal slope preserves tangent normal", Vector3.Back, new(.65f, .7f, .9f), flat, flat,
            Vector3.Zero, Vector2.One, true, Vector2.Right, new(.3f, .4f, .8f)));
        cases.Add(new("near horizontal normal remains finite", Vector3.Back, new(1, .5f, .5f), flat, flat,
            Vector3.Zero, Vector2.One, true, Vector2.Right, new(10, 0, 1)));
        cases.Add(new("zero slope stays flat when rotated", Vector3.Back, flat, flat, flat,
            Vector3.Zero, new(-1, -1), true, new(q, q), Vector3.Back));
        try
        {
            viewport = new SubViewport { Name = "GroundNormalFrameProbe", Size = new Vector2I(320, 256),
                Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled, UseHdr2D = false };
            AddChild(viewport);
            for (int i = 0; i < cases.Count; i++)
            {
                GroundNormalCase c = cases[i];
                var material = new ShaderMaterial { Shader = shader };
                materials.Add(material);
                material.SetShaderParameter("base_normal", c.Normal);
                material.SetShaderParameter("sample_x", c.X);
                material.SetShaderParameter("sample_y", c.Y);
                material.SetShaderParameter("sample_z", c.Z);
                material.SetShaderParameter("weights", c.Weights);
                material.SetShaderParameter("axes", c.Axes);
                material.SetShaderParameter("planar", c.Planar);
                material.SetShaderParameter("rotation_cs", c.Rotation);
                viewport.AddChild(new ColorRect { Position = new Vector2(i % 5 * 64, i / 5 * 64),
                    Size = new Vector2(64, 64), Color = Colors.White, Material = material });
            }
            viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
            for (int i = 0; i < 3; i++) await ContactGpuNextDraw(deadline.Token);
            using Image image = viewport.GetTexture().GetImage();
            for (int i = 0; i < cases.Count; i++)
            {
                Color pixel = image.GetPixel(i % 5 * 64 + 32, i / 5 * 64 + 32);
                Vector3 actual = new(pixel.R * 2 - 1, pixel.G * 2 - 1, pixel.B * 2 - 1);
                Vector3 expected = cases[i].Expected.Normalized();
                float error = actual.DistanceTo(expected);
                Expect("ground normal GPU " + cases[i].Name, error < .012f && pixel.A > .99f,
                    $"expected={expected}, readback={actual}, error={error:0.000000}; RGBA8 tolerance includes three quantized channels");
            }
            Expect("ground normal fixture screenshot", image.SavePng(OutputPath + ".ground-normals.png") == Error.Ok,
                "20 analytic cases, production shader include, 320x256 native readback");
        }
        finally
        {
            if (viewport != null && GodotObject.IsInstanceValid(viewport)) viewport.Free();
            for (int i = materials.Count - 1; i >= 0; i--) materials[i].Dispose();
        }
    }

    private sealed record GroundNormalCase(string Name, Vector3 Normal, Vector3 X, Vector3 Y, Vector3 Z,
        Vector3 Weights, Vector2 Axes, bool Planar, Vector2 Rotation, Vector3 Expected);
}

using Godot;

namespace HollowUniverse.Settings;

/// <summary>
/// Applies the graphics settings to the window and everything global whenever they change (GROUND.md §8.6), and draws
/// the sharpening pass: AMD's contrast-adaptive sharpening (CAS, the sharpener FSR uses) over the 3D image, on a canvas
/// layer under every interface, so text and icons are never sharpened. When FSR draws, its own sharpening does it.
/// </summary>
public partial class GraphicsNode : Node
{
    private CanvasLayer _layer = null!;
    private ShaderMaterial _sharpen = null!;

    public override void _Ready()
    {
        _sharpen = new ShaderMaterial { Shader = new Shader { Code = SharpenShader } };
        _layer = new CanvasLayer { Name = "Sharpen", Layer = -100 };
        var rect = new ColorRect { Name = "Pass", Material = _sharpen, MouseFilter = Control.MouseFilterEnum.Ignore };
        rect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(rect);
        AddChild(_layer);
        GraphicsSettings.Changed += Apply;
        Apply(GraphicsSettings.Current);
    }

    public override void _ExitTree() => GraphicsSettings.Changed -= Apply;

    private void Apply(GraphicsValues values)
    {
        bool sharpen = GraphicsSettings.ApplyGlobal(GetTree().Root, values);
        _layer.Visible = sharpen;
        _sharpen.SetShaderParameter("sharpness", values.SharpenStrength);
    }

    // CAS (FidelityFX, MIT): the cross's minimum and maximum set how much the centre may be sharpened, so edges that
    // are already sharp do not ring and flat areas do not grain.
    private const string SharpenShader = @"
shader_type canvas_item;
render_mode unshaded;

uniform sampler2D screen : hint_screen_texture, filter_nearest;
uniform float sharpness : hint_range(0.0, 1.0) = 0.3;

void fragment() {
	vec2 p = SCREEN_PIXEL_SIZE;
	vec3 b = textureLod(screen, SCREEN_UV + vec2(0.0, -p.y), 0.0).rgb;
	vec3 d = textureLod(screen, SCREEN_UV + vec2(-p.x, 0.0), 0.0).rgb;
	vec3 e = textureLod(screen, SCREEN_UV, 0.0).rgb;
	vec3 f = textureLod(screen, SCREEN_UV + vec2(p.x, 0.0), 0.0).rgb;
	vec3 h = textureLod(screen, SCREEN_UV + vec2(0.0, p.y), 0.0).rgb;
	vec3 lo = min(min(min(b, d), min(e, f)), h);
	vec3 hi = max(max(max(b, d), max(e, f)), h);
	vec3 amp = sqrt(clamp(min(lo, 1.0 - hi) / max(hi, vec3(1e-4)), 0.0, 1.0));
	vec3 w = amp * (-1.0 / mix(8.0, 5.0, sharpness));
	vec3 c = (b * w + d * w + f * w + h * w + e) / (1.0 + 4.0 * w);
	COLOR = vec4(clamp(c, 0.0, 1.0), 1.0);
}
";
}

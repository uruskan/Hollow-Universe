// Camera immersion, below the HUD. The world's depth fog handles visibility; this adds a subtle waterline transition.
shader_type canvas_item;
render_mode unshaded;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear, repeat_disable;
uniform float immersion = 0.0;
uniform float water_depth = 0.0;

void fragment() {
	vec2 ripple = vec2(sin(SCREEN_UV.y * 28.0 + TIME * 1.8), cos(SCREEN_UV.x * 24.0 - TIME * 1.4));
	vec2 uv = clamp(SCREEN_UV + ripple * 0.0008 * immersion, vec2(0.001), vec2(0.999));
	vec3 scene = texture(screen_texture, uv).rgb;
	vec3 filtered = scene * vec3(0.66, 0.88, 0.91);
	float edge = smoothstep(0.24, 0.72, length(SCREEN_UV - vec2(0.5)));
	filtered *= 1.0 - edge * 0.16;
	filtered = mix(filtered, vec3(0.018, 0.075, 0.07), clamp(water_depth / 90.0, 0.0, 0.25));
	COLOR = vec4(mix(scene, filtered, immersion), 1.0);
}

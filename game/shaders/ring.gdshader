// Planetary rings: a flat annulus with procedural bands. UV.x runs from the inner to the outer edge.
shader_type spatial;
render_mode blend_mix, cull_disabled, unshaded, depth_draw_never, shadows_disabled;

uniform vec3 colour = vec3(0.85, 0.78, 0.65);
uniform float opacity = 0.75;
uniform float seed = 0.0;

float hash(float x) {
	return fract(sin(x * 91.3458 + seed) * 47453.5453);
}

float noise(float x) {
	float i = floor(x);
	float f = fract(x);
	return mix(hash(i), hash(i + 1.0), f * f * (3.0 - 2.0 * f));
}

void fragment() {
	float r = UV.x;
	float bands = noise(r * 40.0) * 0.6 + noise(r * 130.0) * 0.4;
	float edge = smoothstep(0.0, 0.05, r) * (1.0 - smoothstep(0.92, 1.0, r));
	float gap = 1.0 - smoothstep(0.02, 0.0, abs(r - 0.68)) * 0.85;
	ALBEDO = colour * (0.7 + 0.5 * bands);
	ALPHA = opacity * edge * gap * (0.35 + 0.65 * bands);
}

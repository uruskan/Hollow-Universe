// Sector grid on the horizontal plane through the focus: lines every sector (1024 ly), finer lines when
// zoomed in, fading away from the focus.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, depth_test_disabled, cull_disabled, fog_disabled, shadows_disabled;

uniform vec3 grid_origin;
uniform vec3 focus;
uniform float cell = 1024.0;
uniform float fine_cell = 128.0;
uniform float fine_alpha = 0.0;
uniform float fade_radius = 8000.0;
uniform float alpha = 0.5;
uniform vec3 colour = vec3(0.30, 0.62, 0.90);

varying vec3 v_world;

void vertex() {
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

float grid_line(vec2 p) {
	vec2 g = abs(fract(p - 0.5) - 0.5) / max(fwidth(p), vec2(1e-6));
	return 1.0 - min(min(g.x, g.y), 1.0);
}

void fragment() {
	vec2 rel = v_world.xz - grid_origin.xz;
	float major = grid_line(rel / cell);
	float minor = grid_line(rel / fine_cell) * fine_alpha;
	float fade = 1.0 - smoothstep(0.25, 1.0, length(v_world.xz - focus.xz) / fade_radius);
	ALBEDO = colour * max(major, minor * 0.5) * fade * alpha;
}

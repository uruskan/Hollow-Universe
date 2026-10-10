// Galaxy-map stars: one point per star. Size and brightness come from the star's absolute magnitude
// (UV.x) and its distance to the camera, relative to a reference magnitude set from the zoom level.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, depth_test_disabled, cull_disabled, fog_disabled, shadows_disabled;

uniform float reference_magnitude = 10.0;
uniform float exposure = 1.0;
uniform float size_scale = 1.0;
uniform float min_size = 1.7;
uniform float max_size = 20.0;
// Each level (UV.y) is shown out to its own radius around the focus and fades out before it.
uniform vec3 focus_position;
uniform float level_radius[8];
// Where a level's stars crowd together on screen (towards the core) their total light is cut by this factor, so
// they add up to a glow instead of a white blob.
uniform float level_exposure[8];
// Where each level's fade-out starts, as a fraction of its radius (earlier for levels the star budget cut short).
uniform float level_fade_start[8];
uniform float view_radius = 100.0;

varying float v_brightness;

void vertex() {
	vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float d = max(length(world - CAMERA_POSITION_WORLD), 0.002);
	// Apparent magnitude: M + 5·log10(d / 10 pc), with d in light-years (10 pc = 32.616 ly).
	float m = UV.x + 5.0 * log(d / 32.616) / log(10.0);
	float flux = pow(10.0, -0.4 * (m - reference_magnitude));
	float b = clamp(flux, 0.0, 900.0);
	float from_focus = length(world - focus_position);
	int level = clamp(int(UV.y + 0.5), 0, 7);
	float radius = level_radius[level];
	float level_fade = 1.0 - smoothstep(level_fade_start[level] * radius, radius, from_focus);
	// Stars close to the focus stay visible however faint they are: this is a map, not a sky.
	float near_floor = 0.2 * (1.0 - smoothstep(0.35 * view_radius, 0.75 * view_radius, from_focus));
	float brightness = max(clamp(0.95 * sqrt(b), 0.0, 1.8), near_floor) * level_fade * exposure;
	float size = clamp(min_size + 2.0 * pow(b, 0.35), min_size, max_size) * size_scale;
	// Crowding cuts a level's light by level_exposure: mostly by shrinking the points (crowded stars look like
	// fine sand, not fog), the rest by dimming them.
	float crowd = level_exposure[level];
	float crowded_size = max(size * pow(crowd, 0.4), min(size, 1.5));
	brightness *= crowd * (size * size) / (crowded_size * crowded_size);
	v_brightness = brightness;
	POINT_SIZE = brightness < 0.01 ? 0.0 : crowded_size;
}

void fragment() {
	vec2 c = POINT_COORD * 2.0 - 1.0;
	float r2 = dot(c, c);
	if (r2 > 1.0) {
		discard;
	}
	float core = exp(-r2 * 10.0);
	float halo = exp(-r2 * 3.2) * 0.35;
	ALBEDO = COLOR.rgb * (core * 1.5 + halo) * v_brightness;
}

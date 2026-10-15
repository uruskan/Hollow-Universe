// Far trees of the surface view (GROUND.md §4.6): one quad per tree turned toward the camera about the tree's own up,
// showing the nearest two of eight views baked when the view opens (colour with alpha, camera-space normals) for its
// variant, so forests reach the horizon for two triangles a tree, lit like the leaves up close. A tree changes to and
// from its impostor in a short cross-fade over time (never a standing dither); the near set's impostors and the far
// blocks' meet at an exact distance, where both show the same picture.
shader_type spatial;
render_mode cull_disabled, depth_draw_opaque, skip_vertex_transform, diffuse_burley, specular_disabled;

#include "res://shaders/surface_atmosphere.gdshaderinc"
#include "res://shaders/surface_clouds.gdshaderinc"

uniform sampler2D colour_atlas : source_color, filter_linear_mipmap, repeat_disable;
uniform sampler2D normal_atlas : filter_linear_mipmap, repeat_disable;
uniform float rows = 1.0;
uniform float views = 8.0;
uniform float fade_out = 4500.0;
global uniform float hu_lod_clock;
instance uniform vec2 cut = vec2(0.0, 1e9); // drawn from x to y metres (the near set, or the far blocks)
const float LOD_FADE = 0.45;
uniform float translucency = 0.55;

varying vec3 v_world;
varying float v_frame;
varying float v_row;
varying float v_fade_in;
varying float v_fade_out;
varying float v_cloud;
varying vec3 v_right;
varying vec3 v_up;
varying vec3 v_toward;
varying vec3 v_tint;
varying float v_height;

void vertex() {
	vec3 origin = MODEL_MATRIX[3].xyz;
	vec3 up = normalize(MODEL_MATRIX[1].xyz);
	float scale = length(MODEL_MATRIX[1].xyz);
	vec3 to_camera = CAMERA_POSITION_WORLD - origin;
	float dist = length(to_camera);
	// Custom data: x a random number for the colour, y the variant's row, z the view's extent and the height packed
	// (floor(extent * 100) + height / 100, metres), w the start of a fade (> 0 in, < 0 out, 0 none; hu_lod_clock).
	float fade = INSTANCE_CUSTOM.w;
	v_fade_in = fade > 0.0 ? clamp((hu_lod_clock - fade) / LOD_FADE, 0.0, 1.0) : 1.0;
	// The far end by distance along the ground, so from the air the forest below keeps its trees.
	float along_ground = length(to_camera - up * dot(to_camera, up));
	// Seen steeply from above, a billboard is a sliver: the ground's canopy takes over there (as in the terrain).
	float steep = smoothstep(0.45, 0.75, dot(to_camera, up) / max(dist, 1.0)) * smoothstep(250.0, 500.0, dist);
	v_fade_out = (fade < 0.0 ? 1.0 - clamp((hu_lod_clock + fade) / LOD_FADE, 0.0, 1.0) : 1.0)
		* (1.0 - smoothstep(fade_out * 0.85, fade_out, along_ground)) * step(cut.x, dist) * step(dist, cut.y) * (1.0 - steep);
	float v_fade = v_fade_in * v_fade_out;
	float extent = floor(INSTANCE_CUSTOM.z) / 100.0 * scale;
	float height = fract(INSTANCE_CUSTOM.z) * 100.0 * scale;
	vec3 flat_to = to_camera - up * dot(to_camera, up);
	vec3 toward = length(flat_to) > 1e-3 ? normalize(flat_to) : normalize(cross(up, vec3(1.0, 0.0, 0.0)));
	vec3 right = normalize(cross(up, toward));
	// Which baked view: the camera's direction in the tree's own frame.
	vec3 local = transpose(mat3(MODEL_MATRIX)) * to_camera;
	float azimuth = atan(local.x, local.z);
	v_frame = fract(azimuth / 6.2831853) * views;
	v_row = INSTANCE_CUSTOM.y;
	vec3 corner = origin + right * (VERTEX.x * extent) + up * (height * 0.5 + (VERTEX.y - 0.5) * extent);
	if (v_fade <= 0.0) {
		corner = origin;
	}
	v_world = corner;
	v_right = right;
	v_up = up;
	v_toward = toward;
	float t = INSTANCE_CUSTOM.x, h = fract(t * 7.31);
	float k = 0.86 + 0.28 * t;
	v_tint = vec3(k * (0.92 + 0.16 * h), k, k * (1.04 - 0.1 * h));
	v_height = VERTEX.y;
	VERTEX = (VIEW_MATRIX * vec4(corner, 1.0)).xyz;
	NORMAL = normalize((VIEW_MATRIX * vec4(toward, 0.0)).xyz);
}

void fragment() {
	float f0 = floor(v_frame);
	float t = v_frame - f0;
	float f1 = mod(f0 + 1.0, views);
	vec2 cell = vec2(UV.x, UV.y);
	vec2 uv0 = vec2((f0 + cell.x) / views, (v_row + cell.y) / rows);
	vec2 uv1 = vec2((f1 + cell.x) / views, (v_row + cell.y) / rows);
	vec4 c0 = texture(colour_atlas, uv0);
	vec4 c1 = texture(colour_atlas, uv1);
	vec4 c = mix(c0, c1, t);
	float dither = fract(52.9829189 * fract(dot(FRAGCOORD.xy, vec2(0.06711056, 0.00583715))));
	if (dither > v_fade_out || 1.0 - dither > v_fade_in) {
		discard;
	}
	ALPHA = c.a;
	ALPHA_SCISSOR_THRESHOLD = 0.5;
	ALPHA_ANTIALIASING_EDGE = 0.25;
	ALPHA_TEXTURE_COORDINATE = vec2(UV.x, UV.y) * vec2(256.0);
	vec3 n0 = texture(normal_atlas, uv0).rgb * 2.0 - 1.0;
	vec3 n1 = texture(normal_atlas, uv1).rgb * 2.0 - 1.0;
	vec3 nc = normalize(mix(n0, n1, t) + vec3(0.0, 0.0, 0.25));
	vec3 n = normalize(v_right * nc.x + v_up * nc.y + v_toward * nc.z);
	ALBEDO = c.rgb * v_tint;
	NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);
	SPECULAR = 0.12;
	ROUGHNESS = 0.82;
	vec3 a = ALBEDO;
	vec4 fog;
	hu_aerial(v_world, CAMERA_POSITION_WORLD, 6, a, fog);
	ALBEDO = a;
	FOG = fog;
	v_cloud = hu_cloud_shadow(v_world);
}

void light() {
	float ndl = dot(NORMAL, LIGHT);
	float cloud = LIGHT_IS_DIRECTIONAL ? v_cloud : 1.0;
	vec3 light = LIGHT_COLOR * ATTENUATION * cloud / HU_PI;
	float back = clamp(dot(VIEW, LIGHT), 0.0, 1.0);
	float wrap = clamp(-ndl * 0.6 + 0.4, 0.0, 1.0);
	DIFFUSE_LIGHT += light * (clamp(ndl, 0.0, 1.0) + translucency * (pow(back, 4.0) * 1.6 + wrap * 0.35) * vec3(0.9, 1.05, 0.55));
}

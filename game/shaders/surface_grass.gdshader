// Grass of the surface view (GROUND.md §4.6): no plant models, a field of blade clusters drawn by the GPU on a grid
// that follows the camera. Every cell of the grid is a cell of the ground in stable coordinates, so its tuft (place,
// size, lean, colour, dryness, kind) comes from a hash of the cell and never shifts as the grid moves. The ground's
// height, the grass density, dryness and flowers come from a field texture made on the CPU from the surface
// generator (GrassField). Blades are hollow-orbit's painted cards, coloured like the ground's grass layer under them,
// with normals that mostly point up so a field lights as one; the base takes the ground's own colour. Wind bends them
// with gusts that roll across the field; they bend away from the walker's feet; far away they shrink into the ground.
shader_type spatial;
render_mode cull_disabled, depth_draw_opaque, diffuse_burley, specular_schlick_ggx, skip_vertex_transform;

#include "res://shaders/surface_atmosphere.gdshaderinc"
#include "res://shaders/surface_clouds.gdshaderinc"
#include "res://shaders/surface_grass.gdshaderinc"

global uniform vec3 hu_texture_offset;
global uniform vec4 hu_wind;

uniform int grid_width = 180;
uniform float cell = 0.45;
uniform ivec2 corner_cell;          // stable cell index of grid cell (0, 0)
uniform vec2 corner_render;         // render-space x, z of that cell's corner
uniform float far_radius = 42.0;    // blades are gone here
uniform float shrink_start = 24.0;  // and start shrinking here

uniform sampler2D field : filter_linear, repeat_disable; // r: ground height, g: density, b: moisture, a: flowers
uniform vec2 field_origin;          // render x, z of texel (0, 0)
uniform float field_spacing = 0.5;
uniform float field_texels = 256.0;
uniform float field_base = 0.0;     // render y added to the stored heights

uniform sampler2D atlas : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform vec4 cell_grass;            // atlas cells (u0, v bottom, u1, v top)
uniform vec4 cell_dry;
uniform vec4 cell_meadow;
uniform vec4 cell_flower;
uniform vec4 cell_herb;

uniform sampler2DArray ground_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D macro_noise : filter_linear_mipmap, repeat_enable;
uniform float grass_layer_size = 4.0;
uniform vec3 blade_mean = vec3(0.5, 0.63, 0.34);
uniform vec4 trample = vec4(0.0);   // xyz: the walker's feet (render space), w: radius (0: none)
uniform float translucency = 0.35;

varying vec3 v_world;
varying vec4 v_cell;
varying vec3 v_tint;
varying vec3 v_ground;
varying float v_t;
varying float v_cloud;

uint hash_u(uint x) {
	x ^= x >> 16u;
	x *= 0x7feb352du;
	x ^= x >> 15u;
	x *= 0x846ca68bu;
	x ^= x >> 16u;
	return x;
}

float rnd(inout uint s) {
	s = hash_u(s);
	return float(s >> 8u) / 16777216.0;
}

void vertex() {
	int i = INSTANCE_ID % grid_width;
	int j = INSTANCE_ID / grid_width;
	ivec2 id = corner_cell + ivec2(i, j);
	uint s = hash_u(uint(id.x) * 0x9e3779b9u ^ hash_u(uint(id.y) + 0x632be5abu));
	vec2 xz = corner_render + (vec2(float(i), float(j)) + vec2(rnd(s), rnd(s))) * cell;
	float dist = length(xz - CAMERA_POSITION_WORLD.xz);

	vec2 ft = (xz - field_origin) / field_spacing;
	vec4 f = texture(field, (ft + 0.5) / field_texels);
	float inside = step(0.0, ft.x) * step(0.0, ft.y) * step(ft.x, field_texels - 1.0) * step(ft.y, field_texels - 1.0);
	float keep = inside * step(rnd(s), f.g);
	float size = keep * (1.0 - smoothstep(shrink_start, far_radius, dist));

	// The tuft: kind, height, width, turn, lean, colour.
	vec2 wp = xz + hu_texture_offset.xz;
	float r_kind = rnd(s);
	float dry = clamp(hu_grass_dryness(wp, f.b, macro_noise) + (rnd(s) - 0.5) * 0.3, 0.0, 1.0);
	float flower = step(r_kind, f.a * 0.06);
	v_cell = flower > 0.5 ? (rnd(s) < 0.75 ? cell_flower : cell_herb) : (dry > 0.62 ? cell_dry : (r_kind > 0.82 ? cell_meadow : cell_grass));
	float lush = 1.0 - dry;
	float height = (0.28 + 0.26 * rnd(s)) * (0.7 + 0.45 * f.g) * (0.75 + 0.35 * lush);
	if (flower > 0.5) {
		height = 0.38 + 0.2 * rnd(s);
	}
	float width = 0.8 + 0.4 * rnd(s);
	float yaw = rnd(s) * 6.2831853;
	float lean_a = rnd(s) * 6.2831853;
	float lean = 0.05 + 0.12 * rnd(s);
	float v = rnd(s);
	v_tint = flower > 0.5 ? vec3(1.0) : hu_grass_tint(wp, dry, macro_noise) * (0.8 + 0.4 * v);
	if (flower > 0.5) {
		float p = rnd(s);
		v_tint = p < 0.3 ? vec3(1.0, 0.82, 0.12) : p < 0.55 ? vec3(0.95, 0.95, 0.9) : p < 0.75 ? vec3(0.62, 0.38, 0.92) : p < 0.9 ? vec3(0.95, 0.35, 0.22) : vec3(1.0, 0.62, 0.18);
	}

	float t = COLOR.a;
	v_t = t;
	float c = cos(yaw), sn = sin(yaw);
	vec3 p = VERTEX;
	p.xz = vec2(c * p.x - sn * p.z, sn * p.x + c * p.z) * width * (0.5 + 0.5 * size);
	p.y *= height * size;
	vec2 bend = vec2(cos(lean_a), sin(lean_a)) * lean * t * t * height;

	// Wind: gusts as patches of the macro noise drifting downwind, and a ripple running along it.
	vec2 wd = normalize(hu_wind.xz + vec2(1e-5));
	float gust = texture(macro_noise, wp / 70.0 - wd * TIME * 0.085).a;
	float ripple = sin(dot(wp, wd) * 0.45 - TIME * 2.4 + v * 1.2) * 0.5 + 0.5;
	float w = hu_wind.w * (0.2 + 1.2 * smoothstep(0.3, 0.75, gust)) * (0.55 + 0.45 * ripple);
	bend += wd * w * 0.28 * t * t * height;
	bend += vec2(-wd.y, wd.x) * sin(TIME * 5.1 + v * 30.0 + wp.x * 0.7) * 0.012 * t * hu_wind.w;
	// Bend away from the walker's feet.
	if (trample.w > 0.0) {
		vec2 away = xz + p.xz - trample.xz;
		float d = length(away);
		float k = (1.0 - smoothstep(trample.w * 0.3, trample.w, d)) * step(abs(trample.y - f.r - field_base), 1.5);
		bend += away / max(d, 1e-3) * k * 0.6 * t * height;
	}
	float b = length(bend);
	p.xz += bend;
	p.y -= min(b * b / max(height, 0.05), height * 0.6) * t;

	float ground_y = f.r + field_base;
	vec3 world = vec3(xz.x, ground_y - 0.03, xz.y) + p;
	v_world = world;
	VERTEX = (VIEW_MATRIX * vec4(world, 1.0)).xyz;
	vec3 n = normalize(vec3(c * NORMAL.x - sn * NORMAL.z, NORMAL.y * 2.5, sn * NORMAL.x + c * NORMAL.z));
	NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);

	// The ground's own colour at the tuft's foot, as the terrain shader draws its grass layer.
	vec3 detail = texture(ground_albedo, vec3(wp / grass_layer_size, 0.0)).rgb;
	float lum = dot(detail, vec3(0.2126, 0.7152, 0.0722));
	v_ground = clamp(lum / 0.1, 0.4, 1.8) * blade_mean * hu_grass_tint(wp, dry, macro_noise);
}

void fragment() {
	vec2 uv = vec2(mix(v_cell.x, v_cell.z, UV.x), mix(v_cell.y, v_cell.w, UV.y));
	vec4 tex = texture(atlas, uv);
	ALPHA = tex.a;
	ALPHA_SCISSOR_THRESHOLD = 0.5;
	ALPHA_ANTIALIASING_EDGE = 0.25;
	ALPHA_TEXTURE_COORDINATE = uv * vec2(textureSize(atlas, 0));
	vec3 blade = tex.rgb * v_tint * (0.62 + 0.45 * v_t);
	ALBEDO = mix(v_ground, blade, smoothstep(0.0, 0.3, v_t));
	ROUGHNESS = 0.85;
	SPECULAR = 0.2;
	vec3 a = ALBEDO;
	vec4 fog;
	hu_aerial(v_world, CAMERA_POSITION_WORLD, 6, a, fog);
	ALBEDO = a;
	FOG = fog;
	float altitude = hu_camera_altitude + dot(v_world - hu_camera_position, hu_camera_up);
	v_cloud = hu_cloud_shadow(v_world, altitude);
}

void light() {
	float ndl = dot(NORMAL, LIGHT);
	float cloud = LIGHT_IS_DIRECTIONAL ? v_cloud : 1.0;
	vec3 light = LIGHT_COLOR * ATTENUATION * cloud / HU_PI;
	DIFFUSE_LIGHT += light * clamp(ndl, 0.0, 1.0);
	// Light through the blades: the tips glow looking toward a low sun.
	float toward = clamp(dot(-VIEW, LIGHT), 0.0, 1.0);
	float wrap = clamp(-ndl * 0.6 + 0.4, 0.0, 1.0);
	DIFFUSE_LIGHT += light * translucency * (pow(toward, 4.0) * 1.4 * v_t + wrap * 0.35) * vec3(0.9, 1.05, 0.55);
}

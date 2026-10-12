// The ground of a planet (GROUND.md §4.6): tiles from TerrainLod, morphing toward their parent's grid with distance;
// eight ground materials blended by the weights the surface generator gives each vertex, refined per pixel by slope
// and height; triplanar on steep ground; detail close up and broken-up repetition far away; cloud shadows; the
// planet's air in front of it.
shader_type spatial;
render_mode cull_back, depth_draw_opaque, diffuse_burley, specular_schlick_ggx;

#include "res://shaders/surface_atmosphere.gdshaderinc"
#include "res://shaders/surface_clouds.gdshaderinc"

instance uniform vec2 morph_range = vec2(1e12, 2e12);
global uniform vec3 hu_texture_offset;

// Per material: albedo (texture arrays), its world size in metres, and a flat colour for far away.
uniform sampler2DArray albedo_array : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray normal_array : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray rough_array : filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool textured = false;
const float tile_size[8] = { 3.0, 3.0, 3.0, 6.0, 3.0, 3.0, 4.0, 3.0 };
const vec3 far_colour[8] = { vec3(0.16, 0.24, 0.07), vec3(0.12, 0.10, 0.06), vec3(0.24, 0.18, 0.12), vec3(0.30, 0.29, 0.27),
	vec3(0.33, 0.31, 0.28), vec3(0.60, 0.53, 0.40), vec3(0.85, 0.87, 0.90), vec3(0.16, 0.20, 0.10) };
uniform sampler2D macro_noise : filter_linear_mipmap, repeat_enable;
uniform int debug_view = 0;

varying vec3 v_world;
varying vec4 v_m0;
varying vec4 v_m1;
varying vec4 v_climate;
varying float v_height;
varying float v_cloud;

void vertex() {
	vec3 fine = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float dist = length(fine - CAMERA_POSITION_WORLD);
	float k = clamp((dist - morph_range.x) / max(morph_range.y - morph_range.x, 1.0), 0.0, 1.0);
	VERTEX = mix(VERTEX, CUSTOM0.xyz, k);
	NORMAL = normalize(mix(NORMAL, CUSTOM1.xyz, k));
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_m0 = CUSTOM2;
	v_m1 = CUSTOM3;
	v_climate = COLOR;
	v_height = UV.x;
}

vec3 world_normal(vec3 view_normal, mat4 inv_view) {
	return normalize((inv_view * vec4(view_normal, 0.0)).xyz);
}

void fragment() {
	vec3 n = world_normal(NORMAL, INV_VIEW_MATRIX);
	float w[8] = { v_m0.r, v_m0.g, v_m0.b, v_m0.a, v_m1.r, v_m1.g, v_m1.b, v_m1.a };
	float total = 0.0;
	for (int i = 0; i < 8; i++) {
		total += w[i];
	}
	vec3 albedo = vec3(0.0);
	for (int i = 0; i < 8; i++) {
		albedo += far_colour[i] * w[i];
	}
	albedo /= max(total, 1e-3);
	ALBEDO = albedo;
	ROUGHNESS = 0.92;
	vec3 a = ALBEDO;
	vec4 fog;
	hu_aerial(v_world, CAMERA_POSITION_WORLD, 8, a, fog);
	ALBEDO = a;
	FOG = fog;
	v_cloud = hu_cloud_shadow(v_world, v_height);
	if (debug_view == 1) {
		ALBEDO = v_m0.rgb;
		FOG = vec4(0.0);
	} else if (debug_view == 2) {
		ALBEDO = v_m1.rgb;
		FOG = vec4(0.0);
	} else if (debug_view == 3) {
		ALBEDO = n * 0.5 + 0.5;
		FOG = vec4(0.0);
	} else if (debug_view == 4) {
		ALBEDO = v_climate.rgb;
		FOG = vec4(0.0);
	}
}

void light() {
	float ndl = clamp(dot(NORMAL, LIGHT), 0.0, 1.0);
	float cloud = LIGHT_IS_DIRECTIONAL ? v_cloud : 1.0;
	DIFFUSE_LIGHT += LIGHT_COLOR * ATTENUATION * ndl * cloud / HU_PI;
	vec3 h = normalize(VIEW + LIGHT);
	float ndh = clamp(dot(NORMAL, h), 0.0, 1.0);
	float r = ROUGHNESS * ROUGHNESS;
	float d = r * r / (HU_PI * pow(ndh * ndh * (r * r - 1.0) + 1.0, 2.0));
	float ndv = clamp(dot(NORMAL, VIEW), 1e-3, 1.0);
	float k = r * 0.5;
	float g = ndl / (ndl * (1.0 - k) + k) * ndv / (ndv * (1.0 - k) + k);
	float f = 0.02 + 0.98 * pow(1.0 - clamp(dot(h, VIEW), 0.0, 1.0), 5.0);
	SPECULAR_LIGHT += LIGHT_COLOR * ATTENUATION * cloud * d * g * f / max(4.0 * ndl * ndv, 1e-3) * ndl * SPECULAR_AMOUNT;
}

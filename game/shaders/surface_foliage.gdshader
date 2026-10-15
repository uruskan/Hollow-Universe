// Plants of the surface view (GROUND.md §4.6), drawn with MultiMesh, after hollow-orbit's flora materials:
// - leaves: the painted atlas, alpha-tested with alpha to coverage, vertex-colour occlusion, crown-shaped normals,
//   light through the leaves and a warm sheen where the sun is behind the viewer, little specular;
// - bark: tiling bark with a normal map, tinted by vertex colour; berries: vertex colour, glossy;
// - wind (per vertex weights in CUSTOM0: bend, flutter): the whole plant bends along the wind with travelling gusts,
//   leaves flutter along their normals;
// - levels of detail: each MultiMesh draws a distance band, cross-fading by dither, or shrinking into the ground at
//   the far end of its last band; cloud shadows and the planet's air in front.
shader_type spatial;
render_mode cull_disabled, depth_draw_opaque, diffuse_burley, specular_schlick_ggx;

#include "res://shaders/surface_atmosphere.gdshaderinc"
#include "res://shaders/surface_clouds.gdshaderinc"

global uniform vec3 hu_texture_offset;
global uniform vec4 hu_wind; // xyz: direction the wind blows toward (render space, unit, along the ground at the camera); w: strength (1 = a breeze)
// A tool's blow (GROUND.md §9.3): the plant standing at xyz (render space) sways w metres at its top, away along
// hu_shake_dir.xyz, settling over the seconds since the blow (hu_shake_dir.w). w = 0: none.
global uniform vec4 hu_shake_at;
global uniform vec4 hu_shake_dir;

uniform int mode = 0; // 0 leaves, 1 bark, 2 berries
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D normal_texture : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform vec3 wind_params = vec3(0.22, 1.15, 0.035); // metres of bend, its frequency, metres of flutter
uniform float translucency = 0.55;
uniform float roughness = 0.82;
uniform float specular_scale = 0.35;

instance uniform vec4 lod_band = vec4(0.0, 0.0, 1e6, 2e6); // fade in from x to y, out from z to w (metres)
instance uniform float shrink = 0.0;
global uniform float hu_lod_clock;
const float LOD_FADE = 0.45; // seconds a change of level of detail takes (custom data z: > 0 in, < 0 out, 0 none) // 1: shrink into the ground across the fade-out instead of dithering

varying vec3 v_world;
varying float v_fade_in;
varying float v_fade_out;
varying float v_cloud;
varying vec3 v_tint;
varying float v_local_y;

void vertex() {
	v_local_y = VERTEX.y * length(MODEL_MATRIX[1].xyz);
	vec3 origin = MODEL_MATRIX[3].xyz;
	// The main camera's distance, also in shadow passes (so each level casts the shadows it draws).
	float dist = length(origin - MAIN_CAM_INV_VIEW_MATRIX[3].xyz);
	float fade_in = lod_band.y > lod_band.x ? smoothstep(lod_band.x, lod_band.y, dist) : 1.0;
	float fade_out = smoothstep(lod_band.z, lod_band.w, dist);
	float size = shrink > 0.5 ? 1.0 - fade_out : 1.0;
	float lod_fade = INSTANCE_CUSTOM.z;
	v_fade_in = fade_in * (lod_fade > 0.0 ? clamp((hu_lod_clock - lod_fade) / LOD_FADE, 0.0, 1.0) : 1.0);
	v_fade_out = (shrink > 0.5 ? 1.0 : 1.0 - fade_out) * (lod_fade < 0.0 ? 1.0 - clamp((hu_lod_clock + lod_fade) / LOD_FADE, 0.0, 1.0) : 1.0);
	float v_fade = v_fade_in * v_fade_out;
	if (v_fade <= 0.0 || size <= 0.0) {
		VERTEX = vec3(0.0);
	} else {
		mat3 m = mat3(MODEL_MATRIX);
		float s2 = max(dot(m[0], m[0]), 1e-6);
		vec3 base = origin + hu_texture_offset;
		// The wind blows along the ground where the plant stands (its own up is the model's Y).
		vec3 plant_up = m[1] / sqrt(max(dot(m[1], m[1]), 1e-6));
		vec3 wind = (hu_wind.xyz - plant_up * dot(hu_wind.xyz, plant_up)) * hu_wind.w;
		vec3 wl = vec3(dot(m[0], wind), dot(m[1], wind), dot(m[2], wind)) / s2;
		float ph = dot(base, vec3(0.061, 0.047, 0.053));
		// Gusts roll across the forest: a slow wave in space and time.
		float gust = 0.55 + 0.45 * sin(TIME * 0.37 - dot(base, hu_wind.xyz) * 0.035 + dot(base, vec3(0.011, 0.007, 0.009)) * 6.0);
		float bend = CUSTOM0.x * (0.65 + 0.35 * sin(TIME * wind_params.y + ph)) * gust;
		vec3 v = VERTEX + wl * bend * wind_params.x;
		float flutter = CUSTOM0.y * sin(TIME * 7.3 + ph * 4.0 + dot(VERTEX, vec3(2.1, 1.7, 2.3))) * gust * hu_wind.w;
		v += NORMAL * flutter * wind_params.z / sqrt(s2);
		if (hu_shake_at.w > 0.0) {
			vec3 off = origin - hu_shake_at.xyz;
			if (dot(off, off) < 0.16) {
				float age = hu_shake_dir.w;
				float sway = hu_shake_at.w * exp(-age * 5.5) * sin(age * 24.0);
				vec3 push = vec3(dot(m[0], hu_shake_dir.xyz), dot(m[1], hu_shake_dir.xyz), dot(m[2], hu_shake_dir.xyz)) / s2;
				v += push * CUSTOM0.x * sway;
				// The leaves shiver with it.
				v += NORMAL * CUSTOM0.y * sway * 0.25 * sin(age * 41.0 + dot(VERTEX, vec3(3.1, 2.3, 2.7))) / sqrt(s2);
			}
		}
		VERTEX = v * size;
	}
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	// Per-instance colour (custom data x, y: two random numbers): brightness, and a warmer or cooler hue.
	float t = INSTANCE_CUSTOM.x, h = INSTANCE_CUSTOM.y;
	float k = 0.86 + 0.28 * t;
	v_tint = vec3(k * (0.92 + 0.16 * h), k, k * (1.04 - 0.1 * h));
	if (mode == 1) {
		v_tint = vec3(0.9 + 0.2 * t);
	}
}

void fragment() {
	vec3 albedo;
	float alpha = 1.0;
	if (mode == 2) {
		albedo = COLOR.rgb;
		ROUGHNESS = roughness;
		SPECULAR = 0.5;
	} else {
		vec4 tex = texture(albedo_texture, UV);
		albedo = tex.rgb * COLOR.rgb * v_tint;
		ROUGHNESS = roughness;
		if (mode == 0) {
			alpha = tex.a;
			// Cards have made-up normals: keep sky reflections off them, or leaves turn white at grazing angles.
			SPECULAR = 0.12;
		} else {
			NORMAL_MAP = texture(normal_texture, UV).rgb;
			NORMAL_MAP_DEPTH = 1.0;
			SPECULAR = 0.3;
			// Moss low on the trunk, on its north side (render -Z is north), broken up by the bark's own relief.
			vec3 wn = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
			float north = smoothstep(-0.35, 0.55, -wn.z);
			float low = 1.0 - smoothstep(0.15, 0.9 + 1.1 * tex.r, v_local_y);
			float moss = clamp(low * north * (0.6 + 0.8 * tex.g) - 0.15, 0.0, 1.0);
			albedo = mix(albedo, vec3(0.055, 0.085, 0.022) * (0.75 + 0.5 * tex.r), moss);
			ROUGHNESS = mix(ROUGHNESS, 0.95, moss);
		}
	}
	// Alpha-tested leaves with alpha to coverage; everything else opaque (the outputs must be set on every path).
	ALPHA = alpha;
	ALPHA_SCISSOR_THRESHOLD = 0.5;
	ALPHA_ANTIALIASING_EDGE = 0.25;
	ALPHA_TEXTURE_COORDINATE = UV * vec2(textureSize(albedo_texture, 0));
	// Interleaved gradient noise: a stable per-pixel threshold for the cross-fade between levels of detail. A level
	// fading out keeps the pixels below its fade, one fading in those above: together they cover each pixel once.
	float dither = fract(52.9829189 * fract(dot(FRAGCOORD.xy, vec2(0.06711056, 0.00583715))));
	if (dither > v_fade_out || 1.0 - dither > v_fade_in) {
		discard;
	}
	// (With culling off, Godot has already turned NORMAL toward the viewer on back faces, as three.js does.)
	ALBEDO = albedo;
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
	DIFFUSE_LIGHT += light * clamp(ndl, 0.0, 1.0);
	if (mode == 0) {
		// hollow-orbit's foliage light: a warm sheen with the sun behind the viewer, and light through leaves that face
		// away from it.
		float back = clamp(dot(VIEW, LIGHT), 0.0, 1.0);
		float wrap = clamp(-ndl * 0.6 + 0.4, 0.0, 1.0);
		DIFFUSE_LIGHT += light * translucency * (pow(back, 4.0) * 1.6 + wrap * 0.35) * vec3(0.9, 1.05, 0.55);
	}
	SPECULAR_LIGHT += LIGHT_COLOR * ATTENUATION * cloud * hu_specular(NORMAL, VIEW, LIGHT, ROUGHNESS) * specular_scale;
}

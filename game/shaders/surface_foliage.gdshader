// Plants and rocks of the surface view (GROUND.md §4.6), drawn with MultiMesh: alpha-tested leaves lit through from
// behind, wind that sways whole trees and flutters their foliage, a distance band per level of detail with a
// dithered cross-fade (instances outside it collapse to nothing), cloud shadows and the planet's air in front.
shader_type spatial;
render_mode cull_disabled, depth_draw_opaque, diffuse_burley, specular_schlick_ggx;

#include "res://shaders/surface_atmosphere.gdshaderinc"
#include "res://shaders/surface_clouds.gdshaderinc"

uniform sampler2D albedo_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D normal_texture : hint_normal, filter_linear_mipmap, repeat_enable;
uniform bool foliage = true;
uniform bool has_normal = false;
uniform bool two_sided = true;
uniform vec4 lod_band = vec4(0.0, 0.0, 50.0, 60.0); // fade in from x to y, out from z to w (metres)
uniform vec3 tint = vec3(1.0);
uniform float wind = 0.25;        // metres of sway at the top
uniform float plant_height = 10.0;
uniform float translucency = 0.6;
uniform float alpha_cut = 0.5;
uniform float roughness = 0.75;

varying vec3 v_world;
varying float v_fade;
varying float v_cloud;
varying vec3 v_tint;
varying float v_height;

void vertex() {
	vec3 origin = MODEL_MATRIX[3].xyz;
	float dist = length(origin - CAMERA_POSITION_WORLD);
	float fade_in = lod_band.y > lod_band.x ? smoothstep(lod_band.x, lod_band.y, dist) : 1.0;
	float fade_out = 1.0 - smoothstep(lod_band.z, lod_band.w, dist);
	v_fade = fade_in * fade_out;
	v_height = clamp(VERTEX.y / max(plant_height, 0.1), 0.0, 1.0);
	if (v_fade <= 0.0) {
		VERTEX = vec3(0.0);
	} else if (wind > 0.0) {
		// Sway grows with height squared; two frequencies so it never looks like a metronome.
		float s = length(MODEL_MATRIX[0].xyz);
		float phase = INSTANCE_CUSTOM.x * 6.2831 + dot(origin.xz, vec2(0.031, 0.017));
		float h = v_height;
		float sway = (sin(TIME * 0.83 + phase) * 0.65 + sin(TIME * 1.91 + phase * 1.7) * 0.35) * wind * h * h;
		vec3 world_offset = vec3(0.86, 0.0, 0.5) * sway;
		if (foliage) {
			float flutter = sin(TIME * 5.3 + dot(VERTEX, vec3(2.1, 1.3, 1.7)) + phase) * 0.04 * wind * (0.3 + h);
			world_offset += vec3(flutter, flutter * 0.6, -flutter * 0.4);
		}
		VERTEX += (vec4(world_offset, 0.0) * MODEL_MATRIX).xyz / (s * s);
	}
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_tint = tint * mix(vec3(0.82, 0.86, 0.8), vec3(1.12, 1.1, 1.0), INSTANCE_CUSTOM.x);
}

void fragment() {
	vec4 tex = texture(albedo_texture, UV);
	if (foliage && tex.a < alpha_cut) {
		discard;
	}
	// Interleaved gradient noise: a stable per-pixel threshold for the cross-fade between levels of detail.
	float dither = fract(52.9829189 * fract(dot(FRAGCOORD.xy, vec2(0.06711056, 0.00583715))));
	if (v_fade < 0.999 && dither > v_fade) {
		discard;
	}
	ALBEDO = tex.rgb * v_tint;
	if (has_normal) {
		NORMAL_MAP = texture(normal_texture, UV).rgb;
	}
	if (two_sided && !FRONT_FACING) {
		NORMAL = -NORMAL;
	}
	ROUGHNESS = roughness;
	AO = mix(0.55, 1.0, v_height);
	AO_LIGHT_AFFECT = 0.3;
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
	float diffuse = foliage ? clamp(ndl * 0.75 + 0.25, 0.0, 1.0) : clamp(ndl, 0.0, 1.0);
	// Light through the leaves, strongest looking toward the light.
	float through = foliage ? translucency * clamp(-ndl, 0.0, 1.0) * (0.35 + 0.65 * pow(clamp(dot(VIEW, -LIGHT), 0.0, 1.0), 3.0)) : 0.0;
	DIFFUSE_LIGHT += LIGHT_COLOR * ATTENUATION * cloud * (diffuse + through) / HU_PI;
	vec3 hv = normalize(VIEW + LIGHT);
	float ndh = clamp(dot(NORMAL, hv), 0.0, 1.0);
	float r = ROUGHNESS * ROUGHNESS;
	float d = r * r / (HU_PI * pow(ndh * ndh * (r * r - 1.0) + 1.0, 2.0));
	float nl = clamp(ndl, 0.0, 1.0);
	float ndv = clamp(dot(NORMAL, VIEW), 1e-3, 1.0);
	float f = 0.03 + 0.97 * pow(1.0 - clamp(dot(hv, VIEW), 0.0, 1.0), 5.0);
	SPECULAR_LIGHT += LIGHT_COLOR * ATTENUATION * cloud * d * f * nl / max(4.0 * nl * ndv, 1e-3) * nl * 0.5;
}

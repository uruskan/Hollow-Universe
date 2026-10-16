// Boulders and stones of the surface view (GROUND.md §4.6): the ground's rock layer projected three ways in the
// rock's own frame (so every rock is textured at true scale without UVs), moss on the faces that look up, occlusion
// from vertex colour, a colour and texture offset per instance; distance bands like the plants (a dither between the
// two meshes, shrinking into the ground at the far end); cloud shadows and the planet's air in front.
shader_type spatial;
render_mode cull_back, depth_draw_opaque, diffuse_burley, specular_schlick_ggx;

#include "res://shaders/surface_atmosphere.gdshaderinc"
#include "res://shaders/surface_clouds.gdshaderinc"

uniform sampler2DArray albedo_array : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray normal_array : filter_linear_mipmap_anisotropic, repeat_enable;
uniform float rock_layer = 3.0;
uniform float moss_layer = 7.0;
uniform float texture_size = 2.0; // metres per repeat
uniform vec3 moss_colour = vec3(0.16, 0.23, 0.07);

instance uniform vec4 lod_band = vec4(0.0, 0.0, 1e6, 2e6);
instance uniform float shrink = 0.0;
global uniform float hu_lod_clock;
const float LOD_FADE = 0.45; // seconds a change of level of detail takes (custom data z: > 0 in, < 0 out, 0 none)

varying vec3 v_world;
varying vec3 v_local;
varying vec3 v_local_normal;
varying float v_fade_in;
varying float v_fade_out;
varying float v_tint;

void vertex() {
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
	float s = length(MODEL_MATRIX[0].xyz);
	v_local = VERTEX * s + vec3(INSTANCE_CUSTOM.x, INSTANCE_CUSTOM.y, INSTANCE_CUSTOM.x * 0.7) * 37.0;
	v_local_normal = NORMAL;
	VERTEX *= (v_fade <= 0.0 ? 0.0 : size);
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_tint = 0.82 + 0.3 * INSTANCE_CUSTOM.y;
}

void fragment() {
	float dither = fract(52.9829189 * fract(dot(FRAGCOORD.xy, vec2(0.06711056, 0.00583715))));
	if (dither > v_fade_out || 1.0 - dither > v_fade_in) {
		discard;
	}
	vec3 n = normalize(v_local_normal);
	vec3 w = pow(abs(n), vec3(4.0));
	w /= dot(w, vec3(1.0));
	vec3 p = v_local / texture_size;
	vec2 ux = p.zy, uy = p.xz, uz = p.xy;
	vec3 rock = texture(albedo_array, vec3(ux, rock_layer)).rgb * w.x + texture(albedo_array, vec3(uy, rock_layer)).rgb * w.y
		+ texture(albedo_array, vec3(uz, rock_layer)).rgb * w.z;
	vec3 tx = texture(normal_array, vec3(ux, rock_layer)).rgb * 2.0 - 1.0;
	vec3 ty = texture(normal_array, vec3(uy, rock_layer)).rgb * 2.0 - 1.0;
	vec3 tz = texture(normal_array, vec3(uz, rock_layer)).rgb * 2.0 - 1.0;
	// Each projection's tangent-space bump, turned into the rock's frame.
	vec3 bump = vec3(0.0, tx.y, tx.x) * w.x + vec3(ty.x, 0.0, ty.y) * w.y + vec3(tz.x, tz.y, 0.0) * w.z;
	vec3 nl = normalize(n + bump * 0.9);
	// Moss where the face looks up, broken up by the moss layer's own grain.
	vec3 moss_tex = texture(albedo_array, vec3(uy * 0.7, moss_layer)).rgb;
	float grain = dot(moss_tex, vec3(0.333));
	float moss = smoothstep(0.45, 0.8, nl.y + (grain - 0.35) * 0.8 + (tz.z * 0.5 + 0.5) * 0.15);
	vec3 moss_col = moss_colour * (0.6 + 1.6 * grain);
	vec3 albedo = mix(rock * v_tint, moss_col, moss * 0.85) * COLOR.rgb;
	ALBEDO = albedo;
	ROUGHNESS = mix(0.86, 0.95, moss);
	SPECULAR = 0.35;
	NORMAL = normalize((VIEW_MATRIX * (MODEL_MATRIX * vec4(nl, 0.0))).xyz);
	vec3 a = ALBEDO;
	vec4 fog;
	hu_aerial(v_world, CAMERA_POSITION_WORLD, 6, a, fog);
	ALBEDO = a;
	FOG = fog;

}

void light() {
	float ndl = clamp(dot(NORMAL, LIGHT), 0.0, 1.0);
	float cloud = LIGHT_IS_DIRECTIONAL ? hu_cloud_shadow(v_world, normalize((INV_VIEW_MATRIX * vec4(LIGHT, 0.0)).xyz)) : 1.0;
	DIFFUSE_LIGHT += LIGHT_COLOR * ATTENUATION * cloud * ndl / HU_PI;
	SPECULAR_LIGHT += LIGHT_COLOR * ATTENUATION * cloud * hu_specular(NORMAL, VIEW, LIGHT, ROUGHNESS) * 0.5;
}

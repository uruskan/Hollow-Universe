// The ground of a planet (GROUND.md §4.6): tiles from TerrainLod, morphing toward their parent's grid with distance;
// eight ground materials (texture arrays, in Hollow.Surface.GroundMaterial order) blended by the weights the surface
// generator gives each vertex, refined per pixel by slope and by the materials' own heights; triplanar rock on steep
// ground; two scales and large-scale variation so repetition doesn't show; cloud shadows; the planet's air in front.
shader_type spatial;
render_mode cull_back, depth_draw_opaque, diffuse_burley, specular_schlick_ggx;

#include "res://shaders/surface_atmosphere.gdshaderinc"
#include "res://shaders/surface_clouds.gdshaderinc"
#include "res://shaders/surface_grass.gdshaderinc"

instance uniform vec2 morph_range = vec2(1e12, 2e12);
global uniform vec3 hu_texture_offset;

uniform sampler2DArray albedo_array : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray normal_array : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D macro_noise : filter_linear_mipmap, repeat_enable;
uniform float normal_strength = 1.0;
uniform vec2 normal_sign = vec2(1.0, -1.0);
uniform int debug_view = 0;
uniform vec3 blade_mean = vec3(0.5, 0.63, 0.34);      // mean linear colour of the painted grass blades
uniform float grass_layer_luminance = 0.1;            // mean luminance of the grass layer's texture
uniform vec2 canopy_fade = vec2(1600.0, 2200.0);      // where far forests become the ground's colour

// Metres covered by one repeat of each layer (the scans' own sizes), and their roughness.
const float layer_size[8] = { 4.0, 3.0, 2.0, 2.4, 4.0, 4.0, 2.0, 4.0 };
const float layer_rough[8] = { 0.94, 0.86, 0.80, 0.80, 0.85, 0.80, 0.55, 0.90 };

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

vec3 planet_up(vec3 world) {
	vec3 centre = hu_camera_position - hu_camera_up * (hu_planet_radius + hu_camera_altitude);
	return normalize(world - centre);
}

mat2 rotation(float a) {
	float c = cos(a);
	float s = sin(a);
	return mat2(vec2(c, s), vec2(-s, c));
}

void fragment() {
	vec3 n_geo = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
	vec3 up = planet_up(v_world);
	float slope = 1.0 - clamp(dot(n_geo, up), 0.0, 1.0);
	float dist = length(v_world - CAMERA_POSITION_WORLD);
	vec3 wp3 = v_world + hu_texture_offset;
	vec2 wp = wp3.xz;

	// Large-scale variation: brightness, hue and patchiness that never repeat within view.
	vec4 macro = texture(macro_noise, wp / 1730.0);
	vec4 macro2 = texture(macro_noise, wp / 211.0 + vec2(0.31, 0.77));
	vec4 macro3 = texture(macro_noise, wp / 37.0 + vec2(0.13, 0.41));

	float w[8] = { v_m0.r, v_m0.g, v_m0.b, v_m0.a, v_m1.r, v_m1.g, v_m1.b, v_m1.a };
	// Patchy borders between materials, and bare rock where the pixel's own slope is steep.
	w[0] *= 0.55 + 0.9 * macro2.r;
	w[1] *= 0.55 + 0.9 * macro2.g;
	w[2] *= 0.4 + 1.2 * macro2.b * macro3.a;
	w[4] *= 0.5 + 1.0 * macro3.g;
	w[7] *= 0.5 + 1.0 * macro2.a;
	float cliff = smoothstep(0.30, 0.48, slope + (macro3.r - 0.5) * 0.12);
	w[3] = mix(w[3], 1.6, cliff);
	w[6] *= 1.0 - cliff * 0.8;
	float total = 0.0;
	for (int i = 0; i < 8; i++) {
		total += w[i];
	}
	for (int i = 0; i < 8; i++) {
		w[i] /= max(total, 1e-4);
	}

	float dryness = hu_grass_dryness(wp, v_climate.g, macro_noise);
	vec3 grass_colour = blade_mean * hu_grass_tint(wp, dryness, macro_noise);

	// Near: two scales per layer; middle: the large one; far: each layer's mean colour.
	float near = 1.0 - smoothstep(120.0, 260.0, dist);
	float far = smoothstep(900.0, 2500.0, dist);
	mat2 rot = rotation(1.7);
	mat2 rot_huge = rotation(0.6);
	float huge = smoothstep(90.0, 380.0, dist) * (1.0 - far);
	vec3 colours[8];
	vec2 normals[8];
	float heights[8];
	float best = -1.0;
	for (int i = 0; i < 8; i++) {
		colours[i] = vec3(0.0);
		normals[i] = vec2(0.0);
		heights[i] = 0.0;
		if (w[i] < 0.02) {
			continue;
		}
		float layer = float(i);
		vec2 uv = wp / layer_size[i];
		vec2 uv_large = rot * uv * 0.29 + vec2(0.37, 0.11);
		vec3 c;
		vec3 t;
		if (far >= 1.0) {
			c = textureLod(albedo_array, vec3(uv_large, layer), 10.0).rgb;
			t = vec3(0.5, 0.5, 0.5);
		} else if (i == 3 && cliff > 0.05) {
			// Rock on cliffs: three projections, so steep faces aren't smeared.
			vec3 bw = pow(abs(n_geo), vec3(4.0));
			bw /= dot(bw, vec3(1.0));
			vec2 ux = wp3.zy / layer_size[i] * 0.5;
			vec2 uy = wp3.xz / layer_size[i] * 0.5;
			vec2 uz = wp3.xy / layer_size[i] * 0.5;
			c = texture(albedo_array, vec3(ux, layer)).rgb * bw.x + texture(albedo_array, vec3(uy, layer)).rgb * bw.y
				+ texture(albedo_array, vec3(uz, layer)).rgb * bw.z;
			t = texture(normal_array, vec3(ux, layer)).rgb * bw.x + texture(normal_array, vec3(uy, layer)).rgb * bw.y
				+ texture(normal_array, vec3(uz, layer)).rgb * bw.z;
			if (far > 0.0) {
				c = mix(c, textureLod(albedo_array, vec3(uv_large, layer), 10.0).rgb, far);
				t = mix(t, vec3(0.5, 0.5, 0.5), far);
			}
		} else {
			vec3 c_large = texture(albedo_array, vec3(uv_large, layer)).rgb;
			vec3 t_large = texture(normal_array, vec3(uv_large, layer)).rgb;
			c = c_large;
			t = t_large;
			if (near > 0.0) {
				vec3 c_small = texture(albedo_array, vec3(uv, layer)).rgb;
				vec3 t_small = texture(normal_array, vec3(uv, layer)).rgb;
				float m = 0.62 * (0.45 + 0.55 * macro3.b) * near;
				c = mix(c_large, c_small, m);
				t = mix(t_large, t_small, m);
			}
			if (huge > 0.0) {
				// From the air the large scale would repeat in rows: blend in a much larger, turned copy.
				vec2 uv_huge = rot_huge * uv * 0.071 + vec2(0.71, 0.23);
				c = mix(c, texture(albedo_array, vec3(uv_huge, layer)).rgb, huge * 0.6);
				t = mix(t, texture(normal_array, vec3(uv_huge, layer)).rgb, huge * 0.6);
			}
			if (far > 0.0) {
				c = mix(c, textureLod(albedo_array, vec3(uv_large, layer), 10.0).rgb, far);
				t = mix(t, vec3(0.5, 0.5, 0.5), far);
			}
		}
		if (i == 1) {
			// Forest floor: brown leaf litter and needles over dark humus, with cushions of moss in patches.
			vec4 m4 = texture(macro_noise, wp / 9.0 + vec2(0.57, 0.19));
			float lum1 = dot(c, vec3(0.2126, 0.7152, 0.0722));
			vec3 litter = c * vec3(1.25, 0.92, 0.55) * (0.75 + 0.45 * m4.r);
			float moss = smoothstep(0.6, 0.8, m4.g + (macro2.b - 0.5) * 0.35);
			vec3 moss_colour = vec3(0.05, 0.085, 0.02) * clamp(lum1 / 0.09, 0.5, 1.7);
			c = mix(litter, moss_colour, moss * 0.85);
		}
		if (i == 0) {
			// The grass layer takes the blades' colour: its texture only gives the detail.
			float lum = dot(c, vec3(0.2126, 0.7152, 0.0722)) / grass_layer_luminance;
			lum = mix(lum, 1.0, smoothstep(150.0, 900.0, dist) * 0.55);
			c = clamp(lum, 0.4, 1.8) * grass_colour;
		}
		colours[i] = c;
		normals[i] = vec2((t.r * 2.0 - 1.0) * normal_sign.x, (t.g * 2.0 - 1.0) * normal_sign.y);
		heights[i] = t.b;
		best = max(best, w[i] * 1.6 + t.b);
	}
	// Height blending: where layers meet, the higher grains of each poke through.
	vec3 albedo = vec3(0.0);
	vec2 nrm = vec2(0.0);
	float height = 0.0;
	float rough = 0.0;
	float sum = 0.0;
	for (int i = 0; i < 8; i++) {
		if (w[i] < 0.02) {
			continue;
		}
		float b = max(w[i] * 1.6 + heights[i] - best + 0.45, 0.0) * w[i];
		albedo += colours[i] * b;
		nrm += normals[i] * b;
		height += heights[i] * b;
		rough += layer_rough[i] * b;
		sum += b;
	}
	albedo /= max(sum, 1e-4);
	nrm /= max(sum, 1e-4);
	height /= max(sum, 1e-4);
	rough /= max(sum, 1e-4);

	// Moss follows the climate too; the other layers' hues wander a little.
	vec3 lush = vec3(0.70, 0.95, 0.42);
	vec3 dry = vec3(1.20, 1.02, 0.62);
	vec3 tint = mix(dry, lush, smoothstep(0.25, 0.7, v_climate.g + (macro.g - 0.5) * 0.3));
	albedo *= mix(vec3(1.0), tint, clamp(w[7], 0.0, 1.0));
	float others = 1.0 - clamp(w[0], 0.0, 1.0);
	albedo *= mix(1.0, 0.82 + 0.36 * macro.r, others);
	albedo = mix(albedo, albedo * vec3(1.06, 1.0, 0.9), macro.b * others);
	// Far forests: the trees have gone, their canopy colours the ground (as in hollow-orbit).
	float canopy = smoothstep(canopy_fade.x, canopy_fade.y, dist) * smoothstep(0.15, 0.6, v_climate.a) * (1.0 - cliff) * (1.0 - w[6]);
	vec3 canopy_colour = vec3(0.030, 0.050, 0.022) * (0.75 + 0.5 * macro2.r) * (0.85 + 0.3 * macro.b);
	albedo = mix(albedo, canopy_colour, canopy * 0.92);
	rough = mix(rough, 0.95, canopy);
	// Wet sand and stones at the waterline.
	float wet = 1.0 - smoothstep(0.1, 0.9, v_height);
	albedo *= 1.0 - 0.38 * wet;
	rough = mix(rough, 0.35, wet);

	// Normal: the materials' bumps on the geometric normal (fading with distance).
	vec3 tangent = normalize(cross(up, vec3(0.0, 0.0, 1.0)));
	vec3 bitangent = normalize(cross(tangent, up));
	float strength = normal_strength * (1.0 - smoothstep(60.0, 400.0, dist) * 0.8);
	vec3 n = normalize(n_geo + (tangent * nrm.x + bitangent * nrm.y) * strength);

	ALBEDO = albedo;
	ROUGHNESS = rough;
	// Under the trees near by, the canopy keeps much of the sky's light off the ground (the sun's flecks stay).
	float canopy_ao = 1.0 - 0.38 * smoothstep(0.2, 0.8, v_climate.a) * (1.0 - smoothstep(250.0, 600.0, dist));
	AO = mix(0.55, 1.0, smoothstep(0.05, 0.6, height)) * canopy_ao;
	AO_LIGHT_AFFECT = 0.25;
	NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);

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
	} else if (debug_view == 5) {
		ALBEDO = vec3(1.0);
		AO = 0.0;
		AO_LIGHT_AFFECT = 0.0;
		FOG = vec4(0.0);
	}
}

void light() {
	if (debug_view == 5) {
		if (LIGHT_IS_DIRECTIONAL) {
			DIFFUSE_LIGHT += LIGHT_COLOR * 0.1;
		}
	} else {
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
}

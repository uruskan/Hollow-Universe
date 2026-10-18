// Shared substrate appearance for canonical terrain and sealed formation soil.
// Callers provide the same planet/grass helpers, world phase, layer arrays and climate.
// Rough-height is R roughness / G height; normal axes remain layer_flip * normal_sign.
#include "res://shaders/surface_ground_normals.gdshaderinc"
#include "res://shaders/surface_world_phase.gdshaderinc"

uniform sampler2DArray albedo_array : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray normal_array : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray rough_height_array : filter_linear_mipmap_anisotropic, repeat_enable; // r roughness, g height
uniform float layer_metres[16];                       // metres covered by one repeat of each layer (layers.json)
uniform vec2 layer_flip[16];                          // each layer's normal axes against the first layer's
// View-owned phases use the same float cycles/metre on CPU and GPU. Fractional anchor phase only;
// never apply fract to the interpolated coordinates, which would break implicit texture derivatives.
uniform float ground_body_cycles[16];
uniform vec3 ground_body_phase[16];
uniform sampler2D macro_noise : filter_linear_mipmap, repeat_enable;
uniform float normal_strength = 1.0;
// Reflection energy multiplier for dry porous ground, applied to direct light and the sky reflection.
// Saturated shoreline material and ice retain full dielectric reflection; roughness remains the scan's own.
uniform float dry_reflection_weight : hint_range(0.0, 1.0) = 0.25;
uniform vec2 normal_sign = vec2(1.0, -1.0);
uniform int debug_view = 0;
uniform vec3 blade_mean = vec3(0.5, 0.63, 0.34);      // mean linear colour of the painted grass blades
uniform float grass_layer_luminance = 0.1;            // mean luminance of the grass layer's texture
uniform vec2 canopy_fade = vec2(1600.0, 2200.0);      // where far forests become the ground's colour
uniform sampler2D canopy_texture : filter_linear_mipmap_anisotropic, repeat_enable; // crowns: height, kind, slope
uniform float canopy_tile = 150.0;                    // metres covered by the crown texture
uniform float seam_band = 2.0;                        // metres either side of a face seam the textures blend over

mat2 rotation(float a) {
	float c = cos(a);
	float s = sin(a);
	return mat2(vec2(c, s), vec2(-s, c));
}

// The ground as one face's texture coordinates (wp, metres) and axes (tangent t and bitangent b along them) paint it.
struct Look {
	vec3 albedo;
	vec3 bump;    // the materials' and crowns' tilt of the normal, world axes
	float rough;
	float height; // the materials' own height, 0–1
	float reflection_weight;
};

// The vertex data comes in as arguments (world point, materials, climate, height above local water).
Look ground_look(vec2 wp, vec3 t, vec3 b, vec3 n_geo, float slope, float dist, float far_fade, vec3 world, vec4 m0, vec4 m1,
		vec4 m2, vec4 m3, vec4 climate, float water_height) {
	vec3 body_local = hu_body_local(world);
	// Large-scale variation: brightness, hue and patchiness that never repeat within view.
	vec4 macro = texture(macro_noise, wp / 1730.0);
	vec4 macro2 = texture(macro_noise, wp / 211.0 + vec2(0.31, 0.77));
	vec4 macro3 = texture(macro_noise, wp / 37.0 + vec2(0.13, 0.41));

	float w[16] = { m0.r, m0.g, m0.b, m0.a, m1.r, m1.g, m1.b, m1.a, m2.r, m2.g, m2.b, m2.a, m3.r, m3.g, m3.b, m3.a };
	// Patchy borders between materials (each kind like its family), and bare rock where the pixel's own slope is steep:
	// sandstone where the vertex has more of it than of granite.
	w[0] *= 0.55 + 0.9 * macro2.r;
	w[10] *= 0.55 + 0.9 * macro2.r;
	w[1] *= 0.55 + 0.9 * macro2.g;
	w[11] *= 0.55 + 0.9 * macro2.g;
	w[2] *= 0.4 + 1.2 * macro2.b * macro3.a;
	w[14] *= 0.4 + 1.2 * macro2.b * macro3.a;
	w[4] *= 0.5 + 1.0 * macro3.g;
	w[9] *= 0.5 + 1.0 * macro3.g;
	w[7] *= 0.5 + 1.0 * macro2.a;
	w[12] *= 0.5 + 1.0 * macro2.a;
	float cliff = smoothstep(0.30, 0.48, slope + (macro3.r - 0.5) * 0.12);
	float sandstone = w[13] / max(w[3] + w[13], 1e-4);
	w[3] = mix(w[3], 1.6 * (1.0 - sandstone), cliff);
	w[13] = mix(w[13], 1.6 * sandstone, cliff);
	w[6] *= 1.0 - cliff * 0.8;
	w[15] *= 1.0 - cliff * 0.8;
	float total = 0.0;
	for (int i = 0; i < 16; i++) {
		total += w[i];
	}
	for (int i = 0; i < 16; i++) {
		w[i] /= max(total, 1e-4);
	}

	float dryness = hu_grass_dryness(wp, climate.g, macro_noise);
	vec3 grass_colour = blade_mean * hu_grass_tint(wp, dryness, macro_noise);

	// Near: two scales per layer; middle: the large one; far: each layer's mean colour.
	float near = 1.0 - smoothstep(120.0, 260.0, dist);
	float far = smoothstep(900.0, 2500.0, dist);
	mat2 rot = rotation(1.7);
	mat2 rot_huge = rotation(0.6);
	float huge = smoothstep(90.0, 380.0, dist) * (1.0 - far);
	vec3 colours[16];
	vec3 normals[16];
	float heights[16];
	float roughs[16];
	float best = -1.0;
	for (int i = 0; i < 16; i++) {
		colours[i] = vec3(0.0);
		normals[i] = vec3(0.0);
		heights[i] = 0.0;
		roughs[i] = 0.0;
		if (w[i] < 0.02) {
			continue;
		}
		float layer = float(i);
		vec2 uv = wp / layer_metres[i];
		vec2 uv_large = rot * uv * 0.29 + vec2(0.37, 0.11);
		vec3 c;
		vec3 bump = vec3(0.0);
		vec2 axes = normal_sign * layer_flip[i];
		vec2 rh;
		if (far >= 1.0) {
			c = textureLod(albedo_array, vec3(uv_large, layer), 10.0).rgb;
			rh = textureLod(rough_height_array, vec3(uv_large, layer), 9.0).rg;
		} else if ((i == 3 || i == 13) && cliff > 0.05) {
			// Rock and sandstone on cliffs: three projections, so steep faces aren't smeared.
			vec3 n_body = hu_render_to_body * n_geo;
			vec3 bw = pow(abs(n_body), vec3(4.0));
			bw /= dot(bw, vec3(1.0));
			vec3 phase = body_local * ground_body_cycles[i] + ground_body_phase[i];
			vec2 ux = phase.zy;
			vec2 uy = phase.xz;
			vec2 uz = phase.xy;
			c = texture(albedo_array, vec3(ux, layer)).rgb * bw.x + texture(albedo_array, vec3(uy, layer)).rgb * bw.y
				+ texture(albedo_array, vec3(uz, layer)).rgb * bw.z;
			vec3 body_bump = hu_ground_triplanar_bump(texture(normal_array, vec3(ux, layer)).rgb,
				texture(normal_array, vec3(uy, layer)).rgb, texture(normal_array, vec3(uz, layer)).rgb,
				axes, bw, n_body);
			bump = transpose(hu_render_to_body) * body_bump;
			rh = texture(rough_height_array, vec3(ux, layer)).rg * bw.x + texture(rough_height_array, vec3(uy, layer)).rg * bw.y
				+ texture(rough_height_array, vec3(uz, layer)).rg * bw.z;
			if (far > 0.0) {
				c = mix(c, textureLod(albedo_array, vec3(uv_large, layer), 10.0).rgb, far);
				bump *= 1.0 - far;
			}
		} else {
			vec3 c_large = texture(albedo_array, vec3(uv_large, layer)).rgb;
			vec3 t_large = texture(normal_array, vec3(uv_large, layer)).rgb;
			vec2 r_large = texture(rough_height_array, vec3(uv_large, layer)).rg;
			c = c_large;
			vec2 normal_slope = hu_ground_rotated_slope(t_large, axes, rot);
			rh = r_large;
			if (near > 0.0) {
				// Near: the scan at its own size over the large, turned copy (the detail blend).
				vec3 c_small = texture(albedo_array, vec3(uv, layer)).rgb;
				vec3 t_small = texture(normal_array, vec3(uv, layer)).rgb;
				vec2 r_small = texture(rough_height_array, vec3(uv, layer)).rg;
				float m = (0.72 + 0.18 * macro3.b) * near;
				c = mix(c_large, c_small, m);
				normal_slope = mix(normal_slope, hu_ground_normal_slope(t_small, axes), m);
				rh = mix(r_large, r_small, m);
			}
			if (huge > 0.0) {
				// From the air the large scale would repeat in rows: blend in a much larger, turned copy.
				vec2 uv_huge = rot_huge * uv * 0.071 + vec2(0.71, 0.23);
				c = mix(c, texture(albedo_array, vec3(uv_huge, layer)).rgb, huge * 0.6);
				normal_slope = mix(normal_slope,
					hu_ground_rotated_slope(texture(normal_array, vec3(uv_huge, layer)).rgb, axes, rot_huge), huge * 0.6);
			}
			if (far > 0.0) {
				c = mix(c, textureLod(albedo_array, vec3(uv_large, layer), 10.0).rgb, far);
				normal_slope *= 1.0 - far;
			}
			bump = hu_ground_plane_bump(normal_slope, t, b, n_geo);
		}
		if (i == 1) {
			// Forest floor: brown leaf litter and needles over dark humus, with cushions of moss in patches.
			vec4 m4 = texture(macro_noise, wp / 9.0 + vec2(0.57, 0.19));
			float lum1 = dot(c, vec3(0.2126, 0.7152, 0.0722));
			vec3 litter = c * vec3(1.12, 0.94, 0.7) * (0.8 + 0.4 * m4.r);
			float moss = smoothstep(0.6, 0.8, m4.g + (macro2.b - 0.5) * 0.35);
			vec3 moss_colour = vec3(0.05, 0.085, 0.02) * clamp(lum1 / 0.09, 0.5, 1.7);
			c = mix(litter, moss_colour, moss * 0.85);
		}
		if (i == 0 || i == 10) {
			// Retain nearby soil/straw colour variation with a partial blade tint, avoiding both a
			// uniform green floor and bare brown meadow. Reach the old match as base grass thins out.
			float lum = dot(c, vec3(0.2126, 0.7152, 0.0722)) / grass_layer_luminance;
			lum = mix(lum, 1.0, smoothstep(150.0, 900.0, dist) * 0.55);
			vec3 blades = clamp(lum, 0.4, 1.8) * grass_colour;
			float blade_match = mix(0.45, 1.0, smoothstep(22.0, 42.0, dist));
			c = mix(c, blades, blade_match * (i == 0 ? 1.0 : 0.45));
		}
		colours[i] = c;
		normals[i] = bump;
		if (i == 5) {
			// Beach sand: lighter and warmer than the scan, with wind ripples across it near by.
			colours[i] *= vec3(1.5, 1.42, 1.25);
			vec2 rd = normalize(vec2(0.86, -0.5));
			float rp = dot(wp, rd) / 0.045 + texture(macro_noise, wp / 3.0).r * 14.0 + texture(macro_noise, wp / 11.0).g * 20.0;
			float ripple_mask = smoothstep(0.35, 0.65, texture(macro_noise, wp / 17.0 + vec2(0.3, 0.6)).b);
			normals[i] += hu_ground_plane_bump(rd, t, b, n_geo) * cos(rp) * 0.12 * ripple_mask
				* (1.0 - smoothstep(4.0, 14.0, dist));
		}
		heights[i] = rh.g;
		roughs[i] = rh.r;
		best = max(best, w[i] * 1.6 + rh.g);
	}
	// Height blending: where layers meet, the higher grains of each poke through.
	vec3 albedo = vec3(0.0);
	vec3 nrm = vec3(0.0);
	float height = 0.0;
	float rough = 0.0;
	float sum = 0.0;
	for (int i = 0; i < 16; i++) {
		if (w[i] < 0.02) {
			continue;
		}
		float bl = max(w[i] * 1.6 + heights[i] - best + 0.45, 0.0) * w[i];
		albedo += colours[i] * bl;
		nrm += normals[i] * bl;
		height += heights[i] * bl;
		rough += roughs[i] * bl;
		sum += bl;
	}
	albedo /= max(sum, 1e-4);
	nrm /= max(sum, 1e-4);
	height /= max(sum, 1e-4);
	rough /= max(sum, 1e-4);

	// Moss follows the climate too; the other layers' hues wander a little.
	vec3 lush = vec3(0.70, 0.95, 0.42);
	vec3 dry = vec3(1.20, 1.02, 0.62);
	vec3 tint = mix(dry, lush, smoothstep(0.25, 0.7, climate.g + (macro.g - 0.5) * 0.3));
	albedo *= mix(vec3(1.0), tint, clamp(w[7], 0.0, 1.0));
	float others = 1.0 - clamp(w[0] + w[10], 0.0, 1.0);
	albedo *= mix(1.0, 0.82 + 0.36 * macro.r, others);
	albedo = mix(albedo, albedo * vec3(1.06, 1.0, 0.9), macro.b * others);
	// Far forests: where the trees' impostors end, the canopy is drawn on the ground: crowns (a tiling crown map with
	// its own slopes, lit by the sun below), dark gaps between them, and crisp, irregular forest edges with a shadow
	// line along the open side.
	float edge_noise = texture(macro_noise, wp / 97.0 + vec2(0.17, 0.53)).g * 0.6 + texture(macro_noise, wp / 23.0 + vec2(0.61, 0.07)).b * 0.4;
	float cover = climate.a + (edge_noise - 0.5) * 0.4;
	float forest = smoothstep(0.4, 0.5, cover);
	float canopy = far_fade * forest * (1.0 - cliff) * (1.0 - w[6] - w[15]);
	vec4 crowns = texture(canopy_texture, wp / canopy_tile);
	// Groves at a larger scale, so the canopy keeps its texture where single crowns blur together.
	vec4 groves = texture(canopy_texture, wp / (canopy_tile * 6.0) + vec2(0.37, 0.11));
	// Stands of one kind (as the scatter grows them): dark conifers, lighter broadleaf and birch.
	float stand = smoothstep(0.42, 0.62, texture(macro_noise, wp / 700.0 + vec2(0.21, 0.87)).r + (crowns.g - 0.5) * 0.15);
	vec3 conifer = vec3(0.02, 0.036, 0.018);
	vec3 broad = vec3(0.05, 0.08, 0.024);
	vec3 crown_colour = mix(conifer, broad, stand) * (0.8 + 0.4 * crowns.g);
	float crown_cover = smoothstep(0.02, 0.25, crowns.r) * (0.55 + 0.45 * smoothstep(0.05, 0.5, groves.r));
	vec3 canopy_colour = mix(vec3(0.007, 0.011, 0.006), crown_colour * (0.8 + 0.4 * groves.r), crown_cover) * (0.85 + 0.3 * macro.b);
	albedo = mix(albedo, canopy_colour, canopy);
	rough = mix(rough, 0.95, canopy);
	float edge_shadow = far_fade * (1.0 - forest) * smoothstep(0.25, 0.4, cover) * (1.0 - cliff);
	albedo *= 1.0 - 0.45 * edge_shadow;
	// Wet sand: dark and glossy below the wave line, and a band the swash keeps wet, wider and narrower in time; thin
	// lines of foam left where the waves ran out.
	float shore_material = clamp(w[5] + w[8] + 0.45 * w[9], 0.0, 1.0);
	float wet = (1.0 - smoothstep(0.1, 0.9, water_height)) * shore_material;
	float swash = 1.0 - smoothstep(0.0, 0.4 + 0.15 * sin(TIME * 0.7 + wp.x * 0.05 + wp.y * 0.03), water_height);
	wet = max(wet * 0.7, swash * shore_material);
	albedo *= 1.0 - 0.28 * wet;
	rough = mix(rough, 0.48, wet);

	// The materials' bumps (fading with distance) and the crowns' slopes, along this face's axes.
	float strength = normal_strength * (1.0 - smoothstep(60.0, 400.0, dist) * 0.8);
	vec2 crown_slope = (crowns.ba - 0.5) * 2.0 + (groves.ba - 0.5) * 1.2;
	Look look;
	look.albedo = albedo;
	look.bump = nrm * strength + hu_ground_plane_bump(crown_slope, t, b, n_geo) * 1.6 * canopy;
	look.rough = rough;
	look.height = height;
	look.reflection_weight = mix(dry_reflection_weight, 1.0, clamp(max(wet, w[8] + w[15]), 0.0, 1.0));
	return look;
}


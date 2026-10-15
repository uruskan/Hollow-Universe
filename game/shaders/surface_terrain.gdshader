// The ground of a planet (GROUND.md §4.6): tiles from TerrainLod, morphing toward their parent's grid with distance;
// eight ground materials (texture arrays, in Hollow.Surface.GroundMaterial order) blended by the weights the surface
// gives each vertex, refined per pixel by slope and by the materials' own heights; triplanar rock on steep ground; two
// scales and large-scale variation so repetition doesn't show; cloud shadows; the planet's air in front. Textures are
// measured in metres on the tile's cube face (UV2, docs/WHOLE_WORLD.md §10.1), with the tangent along them, and blended
// with the neighbouring face's within a couple of metres of a seam, so they neither stretch nor break anywhere on a ball.
shader_type spatial;
render_mode cull_back, depth_draw_opaque, diffuse_burley, specular_schlick_ggx;

#include "res://shaders/surface_atmosphere.gdshaderinc"
#include "res://shaders/surface_planet.gdshaderinc"
#include "res://shaders/surface_clouds.gdshaderinc"
#include "res://shaders/surface_grass.gdshaderinc"

instance uniform vec2 morph_range = vec2(1e12, 2e12);
instance uniform int tile_face = 0;
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
uniform sampler2D canopy_texture : filter_linear_mipmap_anisotropic, repeat_enable; // crowns: height, kind, slope
uniform float canopy_tile = 150.0;                    // metres covered by the crown texture
uniform float seam_band = 2.0;                        // metres either side of a face seam the textures blend over

// Metres covered by one repeat of each layer (the scans' own sizes), and their roughness.
const float layer_size[8] = { 4.0, 3.0, 2.0, 2.4, 4.0, 4.0, 2.0, 4.0 };
const float layer_rough[8] = { 0.94, 0.86, 0.80, 0.80, 0.85, 0.80, 0.55, 0.90 };

varying vec3 v_world;
varying vec4 v_m0;
varying vec4 v_m1;
varying vec4 v_climate;
varying float v_height;
varying float v_cloud;
varying vec2 v_face;

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
	v_face = UV2;
}

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
};

// The vertex data comes in as arguments (world point, materials, climate, height above the sea).
Look ground_look(vec2 wp, vec3 t, vec3 b, vec3 n_geo, float slope, float dist, float far_fade, vec3 world, vec4 m0, vec4 m1, vec4 climate,
		float sea_height) {
	vec3 wp3 = world + hu_texture_offset;
	// Large-scale variation: brightness, hue and patchiness that never repeat within view.
	vec4 macro = texture(macro_noise, wp / 1730.0);
	vec4 macro2 = texture(macro_noise, wp / 211.0 + vec2(0.31, 0.77));
	vec4 macro3 = texture(macro_noise, wp / 37.0 + vec2(0.13, 0.41));

	float w[8] = { m0.r, m0.g, m0.b, m0.a, m1.r, m1.g, m1.b, m1.a };
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

	float dryness = hu_grass_dryness(wp, climate.g, macro_noise);
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
		vec3 tn;
		if (far >= 1.0) {
			c = textureLod(albedo_array, vec3(uv_large, layer), 10.0).rgb;
			tn = vec3(0.5, 0.5, 0.5);
		} else if (i == 3 && cliff > 0.05) {
			// Rock on cliffs: three projections, so steep faces aren't smeared.
			vec3 bw = pow(abs(n_geo), vec3(4.0));
			bw /= dot(bw, vec3(1.0));
			vec2 ux = wp3.zy / layer_size[i] * 0.5;
			vec2 uy = wp3.xz / layer_size[i] * 0.5;
			vec2 uz = wp3.xy / layer_size[i] * 0.5;
			c = texture(albedo_array, vec3(ux, layer)).rgb * bw.x + texture(albedo_array, vec3(uy, layer)).rgb * bw.y
				+ texture(albedo_array, vec3(uz, layer)).rgb * bw.z;
			tn = texture(normal_array, vec3(ux, layer)).rgb * bw.x + texture(normal_array, vec3(uy, layer)).rgb * bw.y
				+ texture(normal_array, vec3(uz, layer)).rgb * bw.z;
			if (far > 0.0) {
				c = mix(c, textureLod(albedo_array, vec3(uv_large, layer), 10.0).rgb, far);
				tn = mix(tn, vec3(0.5, 0.5, 0.5), far);
			}
		} else {
			vec3 c_large = texture(albedo_array, vec3(uv_large, layer)).rgb;
			vec3 t_large = texture(normal_array, vec3(uv_large, layer)).rgb;
			c = c_large;
			tn = t_large;
			if (near > 0.0) {
				vec3 c_small = texture(albedo_array, vec3(uv, layer)).rgb;
				vec3 t_small = texture(normal_array, vec3(uv, layer)).rgb;
				float m = 0.62 * (0.45 + 0.55 * macro3.b) * near;
				c = mix(c_large, c_small, m);
				tn = mix(t_large, t_small, m);
			}
			if (huge > 0.0) {
				// From the air the large scale would repeat in rows: blend in a much larger, turned copy.
				vec2 uv_huge = rot_huge * uv * 0.071 + vec2(0.71, 0.23);
				c = mix(c, texture(albedo_array, vec3(uv_huge, layer)).rgb, huge * 0.6);
				tn = mix(tn, texture(normal_array, vec3(uv_huge, layer)).rgb, huge * 0.6);
			}
			if (far > 0.0) {
				c = mix(c, textureLod(albedo_array, vec3(uv_large, layer), 10.0).rgb, far);
				tn = mix(tn, vec3(0.5, 0.5, 0.5), far);
			}
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
		if (i == 0) {
			// The grass layer takes the blades' colour: its texture only gives the detail.
			float lum = dot(c, vec3(0.2126, 0.7152, 0.0722)) / grass_layer_luminance;
			lum = mix(lum, 1.0, smoothstep(150.0, 900.0, dist) * 0.55);
			c = clamp(lum, 0.4, 1.8) * grass_colour;
		}
		colours[i] = c;
		normals[i] = vec2((tn.r * 2.0 - 1.0) * normal_sign.x, (tn.g * 2.0 - 1.0) * normal_sign.y);
		if (i == 5) {
			// Beach sand: lighter and warmer than the scan, with wind ripples across it near by.
			colours[i] *= vec3(1.5, 1.42, 1.25);
			vec2 rd = normalize(vec2(0.86, -0.5));
			float rp = dot(wp, rd) / 0.045 + texture(macro_noise, wp / 3.0).r * 14.0 + texture(macro_noise, wp / 11.0).g * 20.0;
			float ripple_mask = smoothstep(0.35, 0.65, texture(macro_noise, wp / 17.0 + vec2(0.3, 0.6)).b);
			normals[i] += rd * cos(rp) * 0.12 * ripple_mask * (1.0 - smoothstep(4.0, 14.0, dist));
		}
		heights[i] = tn.b;
		best = max(best, w[i] * 1.6 + tn.b);
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
		float bl = max(w[i] * 1.6 + heights[i] - best + 0.45, 0.0) * w[i];
		albedo += colours[i] * bl;
		nrm += normals[i] * bl;
		height += heights[i] * bl;
		rough += layer_rough[i] * bl;
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
	float others = 1.0 - clamp(w[0], 0.0, 1.0);
	albedo *= mix(1.0, 0.82 + 0.36 * macro.r, others);
	albedo = mix(albedo, albedo * vec3(1.06, 1.0, 0.9), macro.b * others);
	// Far forests: where the trees' impostors end, the canopy is drawn on the ground: crowns (a tiling crown map with
	// its own slopes, lit by the sun below), dark gaps between them, and crisp, irregular forest edges with a shadow
	// line along the open side.
	float edge_noise = texture(macro_noise, wp / 97.0 + vec2(0.17, 0.53)).g * 0.6 + texture(macro_noise, wp / 23.0 + vec2(0.61, 0.07)).b * 0.4;
	float cover = climate.a + (edge_noise - 0.5) * 0.4;
	float forest = smoothstep(0.4, 0.5, cover);
	float canopy = far_fade * forest * (1.0 - cliff) * (1.0 - w[6]);
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
	float wet = 1.0 - smoothstep(0.1, 0.9, sea_height);
	float swash = 1.0 - smoothstep(0.0, 0.4 + 0.15 * sin(TIME * 0.7 + wp.x * 0.05 + wp.y * 0.03), sea_height);
	wet = max(wet * 0.7, swash);
	albedo *= 1.0 - 0.45 * wet;
	rough = mix(rough, 0.16, wet);
	float residue = smoothstep(0.92, 1.0, sin(sea_height * 38.0 + texture(macro_noise, wp / 3.0).g * 7.0)) * smoothstep(0.15, 0.3, sea_height)
		* (1.0 - smoothstep(0.45, 0.7, sea_height)) * w[5] * (1.0 - smoothstep(20.0, 60.0, dist));
	albedo = mix(albedo, vec3(0.6, 0.6, 0.58), residue * 0.6);

	// The materials' bumps (fading with distance) and the crowns' slopes, along this face's axes.
	float strength = normal_strength * (1.0 - smoothstep(60.0, 400.0, dist) * 0.8);
	vec2 crown_slope = (crowns.ba - 0.5) * 2.0 + (groves.ba - 0.5) * 1.2;
	Look look;
	look.albedo = albedo;
	look.bump = (t * nrm.x + b * nrm.y) * strength + (t * crown_slope.x + b * crown_slope.y) * 1.6 * canopy;
	look.rough = rough;
	look.height = height;
	return look;
}

void fragment() {
	vec3 n_geo = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
	vec3 up = hu_up(v_world);
	float slope = 1.0 - clamp(dot(n_geo, up), 0.0, 1.0);
	float dist = length(v_world - CAMERA_POSITION_WORLD);
	vec3 to_point = v_world - CAMERA_POSITION_WORLD;
	float along_ground = length(to_point - up * dot(to_point, up));
	float far_fade = smoothstep(canopy_fade.x, canopy_fade.y, along_ground);
	// Seen steeply from the air, impostors are thin slivers: the forest below is drawn as canopy there too.
	float steep = smoothstep(0.45, 0.75, -dot(to_point, up) / max(length(to_point), 1.0)) * smoothstep(250.0, 500.0, dist);
	far_fade = max(far_fade, steep);

	// The faces this point is measured on: its tile's (exact coordinates from the vertices), and within a seam's band
	// the neighbour's (from the direction).
	vec3 d = hu_body_direction(v_world);
	ivec3 faces;
	vec3 fw;
	hu_face_blend(d, seam_band, faces, fw);
	vec3 albedo = vec3(0.0);
	vec3 bump = vec3(0.0);
	float rough = 0.0;
	float height = 0.0;
	for (int k = 0; k < 3; k++) {
		if (fw[k] <= 0.0) {
			continue;
		}
		int f = faces[k];
		vec2 wp = f == tile_face ? v_face : hu_face_metres(d, f);
		vec3 t;
		vec3 b;
		hu_face_axes(d, f, t, b);
		Look look = ground_look(wp, t, b, n_geo, slope, dist, far_fade, v_world, v_m0, v_m1, v_climate, v_height);
		albedo += look.albedo * fw[k];
		bump += look.bump * fw[k];
		rough += look.rough * fw[k];
		height += look.height * fw[k];
	}
	vec3 n = normalize(n_geo + bump);

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
	v_cloud = hu_cloud_shadow(v_world);
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
	} else if (debug_view == 6) {
		// The faces and their seams' bands: red, green and blue by axis.
		ALBEDO = fw;
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

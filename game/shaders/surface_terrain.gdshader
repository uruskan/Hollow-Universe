// The ground of a planet (GROUND.md §4.6): tiles from TerrainLod, morphing toward their parent's grid with distance;
// sixteen ground materials (the scans' 4K layers in texture arrays, in Hollow.Surface.GroundMaterial order: colour,
// normals, roughness and height; GroundLayers) blended by the weights the surface gives each vertex, refined per pixel
// by slope and by the materials' own heights; triplanar rock and sandstone on steep ground; two scales, a far turned copy
// and large-scale variation so repetition doesn't show; cloud shadows; the planet's air in front. Textures are
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
#include "res://shaders/surface_ground_look.gdshaderinc"

varying vec3 v_world;
varying vec4 v_m0;
varying vec4 v_m1;
varying vec4 v_m2;
varying vec4 v_m3;
varying vec4 v_climate;
varying float v_height;
varying float v_reflection_weight;
varying vec2 v_face;

void vertex() {
	vec3 fine = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float dist = length(fine - CAMERA_POSITION_WORLD);
	float k = clamp((dist - morph_range.x) / max(morph_range.y - morph_range.x, 1.0), 0.0, 1.0);
	VERTEX = mix(VERTEX, CUSTOM0.xyz, k);
	NORMAL = normalize(mix(NORMAL, CUSTOM1.xyz, k));
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	// Sixteen weights: twelve three to a float (w0 + 256 w1 + 65536 w2, exact integers), four as bytes.
	// Packed values are exact float24 integers; +0.5 rounds odd values above 2^23 upward.
	uvec4 p = uvec4(CUSTOM2);
	v_m0 = vec4(float(p.x & 255u), float((p.x >> 8u) & 255u), float(p.x >> 16u), float(p.y & 255u)) / 255.0;
	v_m1 = vec4(float((p.y >> 8u) & 255u), float(p.y >> 16u), float(p.z & 255u), float((p.z >> 8u) & 255u)) / 255.0;
	v_m2 = vec4(float(p.z >> 16u), float(p.w & 255u), float((p.w >> 8u) & 255u), float(p.w >> 16u)) / 255.0;
	v_m3 = CUSTOM3;
	v_climate = COLOR;
	v_height = UV.x;
	v_face = UV2;
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
	float reflection_weight = 0.0;
	for (int k = 0; k < 3; k++) {
		if (fw[k] <= 0.0) {
			continue;
		}
		int f = faces[k];
		vec2 wp = f == tile_face ? v_face : hu_face_metres(d, f);
		vec3 t;
		vec3 b;
		hu_face_axes(d, f, t, b);
		Look look = ground_look(wp, t, b, n_geo, slope, dist, far_fade, v_world, v_m0, v_m1, v_m2, v_m3, v_climate, v_height);
		albedo += look.albedo * fw[k];
		bump += look.bump * fw[k];
		rough += look.rough * fw[k];
		height += look.height * fw[k];
		reflection_weight += look.reflection_weight * fw[k];
	}
	vec3 n = normalize(n_geo + bump);

	ALBEDO = albedo;
	ROUGHNESS = rough;
	// Godot's environment F0 is 0.16 * SPECULAR^2; this matches the direct lobe's 0.02 below.
	SPECULAR = 0.35355339;
	// Keep the same energy weight on the sky reflection as on the custom direct-light lobe below.
	RADIANCE = vec4(vec3(0.0), 1.0 - reflection_weight);
	v_reflection_weight = reflection_weight;
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
	float cloud = LIGHT_IS_DIRECTIONAL ? hu_cloud_shadow(v_world, normalize((INV_VIEW_MATRIX * vec4(LIGHT, 0.0)).xyz)) : 1.0;
	DIFFUSE_LIGHT += LIGHT_COLOR * ATTENUATION * ndl * cloud / HU_PI;
	vec3 h = normalize(VIEW + LIGHT);
	float ndh = clamp(dot(NORMAL, h), 0.0, 1.0);
	float r = ROUGHNESS * ROUGHNESS;
	float d = r * r / (HU_PI * pow(ndh * ndh * (r * r - 1.0) + 1.0, 2.0));
	float ndv = clamp(dot(NORMAL, VIEW), 1e-3, 1.0);
	float k = r * 0.5;
	float g = ndl / (ndl * (1.0 - k) + k) * ndv / (ndv * (1.0 - k) + k);
	float f = 0.02 + 0.98 * pow(1.0 - clamp(dot(h, VIEW), 0.0, 1.0), 5.0);
	// SPECULAR_AMOUNT belongs to the light (and is 1 for the sun). Match the sky reflection weight;
	// changing fragment SPECULAR alone had left dry soil's direct white sheen intact.
	SPECULAR_LIGHT += LIGHT_COLOR * ATTENUATION * cloud * d * g * f / max(4.0 * ndl * ndv, 1e-3)
		* ndl * SPECULAR_AMOUNT * v_reflection_weight;
}
}

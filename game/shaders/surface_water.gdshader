// The sea (GROUND.md §4.6): a surface at sea level on the terrain's own tile grid. Wind waves (Gerstner swell near
// the camera, tiling normal maps everywhere), the sky and sun reflected with Fresnel, the sea floor seen through
// water that absorbs red first, turquoise shallows, surf lines rolling in over the shallows, a swash of foam on the
// sand, cloud shadows, and the air in front of it. The swell, the ripples and the foam are measured on the tile's cube
// face along its axes, with up from the planet's centre (docs/WHOLE_WORLD.md §10.1), blended across face seams.
shader_type spatial;
render_mode cull_disabled, depth_draw_always, diffuse_burley, specular_schlick_ggx;

#include "res://shaders/surface_atmosphere.gdshaderinc"
#include "res://shaders/surface_planet.gdshaderinc"
#include "res://shaders/surface_clouds.gdshaderinc"

instance uniform vec2 morph_range = vec2(1e12, 2e12);
instance uniform int tile_face = 0;
uniform sampler2D wave_normals : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap, repeat_disable;
uniform sampler2D depth_texture : hint_depth_texture, filter_nearest, repeat_disable;

uniform vec3 absorption = vec3(0.45, 0.10, 0.075);   // per metre
uniform vec3 scattering = vec3(0.004, 0.022, 0.028); // light the water itself sends back, per unit of light
uniform float swell = 0.16;                          // metres; small-world shores must remain readable
uniform float seam_band = 4.0;                       // metres either side of a face seam the waves blend over

varying vec3 v_world;
varying float v_floor;
varying float v_inland; // 0 the sea, 1 a lake's or a stream's water (TileMeshData.WaterDepth.y)
varying vec2 v_face;

// Swell: a few Gerstner waves, travelling with the wind, only near the camera: x along the face's u, y up, z along v.
vec3 gerstner(vec2 p, float t, float amount) {
	vec3 d = vec3(0.0);
	const vec4 waves[4] = { vec4(0.94, 0.34, 23.0, 1.0), vec4(0.70, -0.71, 14.0, 0.55), vec4(0.99, 0.12, 8.5, 0.35), vec4(0.45, 0.89, 5.3, 0.22) };
	for (int i = 0; i < 4; i++) {
		vec2 dir = normalize(waves[i].xy);
		float k = 6.2831853 / waves[i].z;
		float c = sqrt(9.0 / k);
		float a = swell * waves[i].w * amount;
		float f = k * (dot(dir, p) - c * t);
		d.xz += dir * (0.6 * a * cos(f));
		d.y += a * sin(f);
	}
	return d;
}

// Metres on face f of a point: the tile's own from its vertices (own), a neighbour's from the direction.
vec2 face_metres(vec3 d, int f, int own_face, vec2 own) {
	return f == own_face ? own : hu_face_metres(d, f);
}

void vertex() {
	vec3 fine = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float dist = length(fine - CAMERA_POSITION_WORLD);
	float k = clamp((dist - morph_range.x) / max(morph_range.y - morph_range.x, 1.0), 0.0, 1.0);
	VERTEX = mix(VERTEX, CUSTOM0.xyz, k);
	vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_floor = UV.x;
	v_inland = UV.y;
	v_face = UV2;
	float near = (1.0 - smoothstep(60.0, 320.0, dist)) * (1.0 - v_inland);
	float shallow = smoothstep(0.0, 3.0, v_floor);
	if (near * shallow > 0.0) {
		vec3 d = hu_body_direction(world);
		vec3 up = hu_up(world);
		ivec3 faces;
		vec3 fw;
		hu_face_blend(d, seam_band, faces, fw);
		vec3 swell_offset = vec3(0.0);
		for (int i = 0; i < 3; i++) {
			if (fw[i] > 0.0) {
				vec3 t;
				vec3 b;
				hu_face_axes(d, faces[i], t, b);
				vec3 g = gerstner(face_metres(d, faces[i], tile_face, UV2), TIME, near * shallow);
				swell_offset += (t * g.x + up * g.y + b * g.z) * fw[i];
			}
		}
		world += swell_offset;
		// The model's basis is a rotation: its transpose takes the offset back to model space.
		VERTEX += (vec4(swell_offset, 0.0) * MODEL_MATRIX).xyz;
	}
	v_world = world;
}

void fragment() {
	float dist = length(v_world - CAMERA_POSITION_WORLD);
	vec3 up = hu_up(v_world);
	// Ripples: three scales of the wave map drifting different ways, calmer with distance; foam and surf noise. All
	// measured on the faces this point lies on, blended across a seam.
	vec3 d = hu_body_direction(v_world);
	ivec3 faces;
	vec3 fw;
	hu_face_blend(d, seam_band, faces, fw);
	float detail = 1.0 - smoothstep(30.0, 400.0, dist);
	float calm = mix(0.3, 0.1, smoothstep(150.0, 6000.0, dist)) * mix(1.0, 0.45, v_inland);
	vec3 tilt = vec3(0.0);
	vec2 slope = vec2(0.0);
	float foam_noise = 0.0;
	float broken = 0.0;
	float lace_noise = 0.0;
	float swash_phase = 0.0;
	for (int i = 0; i < 3; i++) {
		if (fw[i] <= 0.0) {
			continue;
		}
		vec2 p = face_metres(d, faces[i], tile_face, v_face);
		vec3 t;
		vec3 b;
		hu_face_axes(d, faces[i], t, b);
		vec2 s1 = texture(wave_normals, p / 37.0 + vec2(0.011, 0.004) * TIME).xy * 2.0 - 1.0;
		vec2 s2 = texture(wave_normals, p / 15.0 - vec2(0.005, 0.013) * TIME).xy * 2.0 - 1.0;
		vec2 s3 = texture(wave_normals, p / 5.3 + vec2(0.018, -0.006) * TIME).xy * 2.0 - 1.0;
		vec2 sl = (s1 * 0.55 + s2 * 0.45 + s3 * 0.35 * detail) * calm;
		tilt += (t * sl.x + b * sl.y) * fw[i];
		slope += sl * fw[i];
		foam_noise += texture(wave_normals, p / 3.1 + vec2(0.02, 0.0) * TIME).a * fw[i];
		broken += texture(wave_normals, p / 23.0 + vec2(0.003, 0.001) * TIME).a * fw[i];
		lace_noise += texture(wave_normals, p / 4.7 - vec2(0.0, 0.05) * TIME).a * fw[i];
		swash_phase += (p.x * 0.05 + p.y * 0.03) * fw[i];
	}
	vec3 n = normalize(up + tilt);
	// Both sides are continuous. Back-face culling left only occasional wave facets visible from below,
	// producing bright broken horizontal lines instead of the underside of the water surface.
	bool submerged = dot(CAMERA_POSITION_WORLD - v_world, up) < 0.0;
	if (submerged) n = -n;
	NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);

	// What lies behind the surface: how much water the view ray crosses.
	float raw = texture(depth_texture, SCREEN_UV).r;
	vec4 behind = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, raw, 1.0);
	behind.xyz /= behind.w;
	float ray_path = max(length(behind.xyz) - length(VERTEX), 0.0);
	float ndv = clamp(dot(n, normalize(CAMERA_POSITION_WORLD - v_world)), 0.0, 1.0);
	float depth = max(v_floor, 0.0);
	// The depth buffer can contain sky, or an object in front of the water. The sampled bed depth is also a
	// conservative bound at grazing angles; never let either case turn a shoreline into an opaque black sheet.
	float bed_path = depth / max(ndv, 0.12);
	float path = raw <= 0.000001 ? bed_path : min(ray_path, max(bed_path * 1.5, 0.03));
	if (submerged) path = dist; // from the submerged eye to the surface, not from the surface to its bed
	path = clamp(path, 0.0, 80.0);
	// Bend the view of the floor with the ripples, more through deeper water.
	vec2 bent = clamp(SCREEN_UV + slope * 0.018 * clamp(path / 4.0, 0.0, 1.0) / max(dist * 0.02, 1.0), vec2(0.001), vec2(0.999));
	float bent_raw = texture(depth_texture, bent).r;
	vec4 bent_point = INV_PROJECTION_MATRIX * vec4(bent * 2.0 - 1.0, bent_raw, 1.0);
	if (bent_raw > 0.000001 && -bent_point.z / bent_point.w < -VERTEX.z) {
		bent = SCREEN_UV;
	}
	vec3 floor_colour = textureLod(screen_texture, bent, 0.0).rgb;

	// The air in front of the surface (also already on the floor's colour, so take it off there once).
	vec3 dummy = vec3(1.0);
	vec4 fog;
	hu_aerial(v_world, CAMERA_POSITION_WORLD, 8, dummy, fog);
	vec3 clear_floor = max(floor_colour - fog.rgb * fog.a, vec3(0.0)) / max(1.0 - fog.a, 0.05);

	vec3 through = exp(-absorption * path);
	float depth_mix = 1.0 - dot(through, vec3(1.0 / 3.0));
	// The water body is lit by the same sun, moon and ambient light as the ground. The old emission-only body
	// omitted the game's moonlight boost, leaving black water beside brightly lit foam at night.
	float shallows = 1.0 - smoothstep(0.5, 6.0, path);
	vec3 body_colour = mix(vec3(0.012, 0.045, 0.055), vec3(0.035, 0.11, 0.095), shallows);
	float fresnel = 0.02 + 0.98 * pow(1.0 - ndv, 5.0);

	// Foam follows physical bed depth, not the distance along the view ray. It breaks into small patches rather
	// than the old compulsory white contour around every shore.
	float foam = (1.0 - smoothstep(0.025, 0.18, depth))
		* smoothstep(0.58, 0.86, foam_noise + 0.16 * sin(TIME * 1.3 + swash_phase * 3.0)) * 0.5;
	// Surf: lines of breaking waves along the depth contours, rolling in toward the beach and fading as they
	// spread; broken up by the ripples, never closer than a few metres from each other.
	float surf_zone = smoothstep(0.08, 0.35, depth) * (1.0 - smoothstep(0.9, 2.8, depth)) * (1.0 - smoothstep(150.0, 600.0, dist)) * (1.0 - v_inland);
	float phase = depth * 0.95 + TIME * 0.13 + broken * 0.7;
	float f = fract(phase);
	float crest = smoothstep(0.0, 0.03, f) * (1.0 - smoothstep(0.03, 0.2, f));
	float lace = smoothstep(0.42, 0.75, lace_noise + crest * 0.25);
	float runs = smoothstep(0.35, 0.6, broken);
	foam = max(foam, crest * lace * runs * surf_zone * 0.3);
	// The swash: a thin sheet of foam running up the sand and back.
	float swash = 1.0 - smoothstep(0.0, 0.055 + 0.025 * sin(TIME * 0.7 + swash_phase), depth);
	foam = max(foam, swash * smoothstep(0.58, 0.82, foam_noise) * 0.3 * (1.0 - smoothstep(80.0, 300.0, dist)) * (1.0 - v_inland));
	// Inland water only thins out at its edge, without the sea's foam.
	foam *= mix(1.0, 0.35, v_inland);

	ALBEDO = mix(body_colour * depth_mix, vec3(0.65, 0.68, 0.66), foam);
	EMISSION = clear_floor * through * (1.0 - fresnel) * (1.0 - foam);
	ROUGHNESS = mix(mix(0.12, 0.22, smoothstep(300.0, 20000.0, dist)), 0.8, foam);
	SPECULAR = 0.35;
	METALLIC = 0.0;
	FOG = fog;
}

void light() {
	float ndl = clamp(dot(NORMAL, LIGHT), 0.0, 1.0);
	float cloud = LIGHT_IS_DIRECTIONAL ? hu_cloud_shadow(v_world, normalize((INV_VIEW_MATRIX * vec4(LIGHT, 0.0)).xyz)) : 1.0;
	DIFFUSE_LIGHT += LIGHT_COLOR * ATTENUATION * ndl * cloud / HU_PI;
	vec3 hv = normalize(VIEW + LIGHT);
	float ndh = clamp(dot(NORMAL, hv), 0.0, 1.0);
	float r = max(ROUGHNESS * ROUGHNESS, 0.002);
	float d = r * r / (HU_PI * pow(ndh * ndh * (r * r - 1.0) + 1.0, 2.0));
	float ndv = clamp(dot(NORMAL, VIEW), 1e-3, 1.0);
	float k = r * 0.5;
	float g = ndl / (ndl * (1.0 - k) + k) * ndv / (ndv * (1.0 - k) + k);
	float f = 0.02 + 0.98 * pow(1.0 - clamp(dot(hv, VIEW), 0.0, 1.0), 5.0);
	SPECULAR_LIGHT += LIGHT_COLOR * ATTENUATION * cloud * d * g * f / max(4.0 * ndl * ndv, 1e-3) * ndl;
}

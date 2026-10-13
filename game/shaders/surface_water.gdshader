// The sea (GROUND.md §4.6): a surface at sea level on the terrain's own tile grid. Wind waves (Gerstner swell near
// the camera, tiling normal maps everywhere), the sky and sun reflected with Fresnel, the sea floor seen through
// water that absorbs red first, turquoise shallows, surf lines rolling in over the shallows, a swash of foam on the
// sand, cloud shadows, and the air in front of it.
shader_type spatial;
render_mode cull_back, depth_draw_always, diffuse_burley, specular_schlick_ggx;

#include "res://shaders/surface_atmosphere.gdshaderinc"
#include "res://shaders/surface_clouds.gdshaderinc"

instance uniform vec2 morph_range = vec2(1e12, 2e12);
global uniform vec3 hu_texture_offset;
uniform sampler2D wave_normals : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap, repeat_disable;
uniform sampler2D depth_texture : hint_depth_texture, filter_nearest, repeat_disable;

uniform vec3 absorption = vec3(0.45, 0.10, 0.075);   // per metre
uniform vec3 scattering = vec3(0.004, 0.022, 0.028); // light the water itself sends back, per unit of light
uniform float swell = 0.35;                          // metres

varying vec3 v_world;
varying float v_floor;
varying float v_cloud;

vec3 planet_up(vec3 world) {
	vec3 centre = hu_camera_position - hu_camera_up * (hu_planet_radius + hu_camera_altitude);
	return normalize(world - centre);
}

// Swell: a few Gerstner waves, travelling with the wind, only near the camera.
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

void vertex() {
	vec3 fine = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float dist = length(fine - CAMERA_POSITION_WORLD);
	float k = clamp((dist - morph_range.x) / max(morph_range.y - morph_range.x, 1.0), 0.0, 1.0);
	VERTEX = mix(VERTEX, CUSTOM0.xyz, k);
	vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_floor = UV.x;
	float near = 1.0 - smoothstep(60.0, 320.0, dist);
	float shallow = smoothstep(0.0, 3.0, v_floor);
	if (near * shallow > 0.0) {
		vec3 swell_offset = gerstner(world.xz + hu_texture_offset.xz, TIME, near * shallow);
		world += swell_offset;
		// The model's basis is a rotation: its transpose takes the offset back to model space.
		VERTEX += (vec4(swell_offset, 0.0) * MODEL_MATRIX).xyz;
	}
	v_world = world;
}

void fragment() {
	float dist = length(v_world - CAMERA_POSITION_WORLD);
	vec3 up = planet_up(v_world);
	// Ripples: three scales of the wave map drifting different ways, calmer with distance.
	vec2 p = v_world.xz + hu_texture_offset.xz;
	vec2 s1 = texture(wave_normals, p / 37.0 + vec2(0.011, 0.004) * TIME).xy * 2.0 - 1.0;
	vec2 s2 = texture(wave_normals, p / 15.0 - vec2(0.005, 0.013) * TIME).xy * 2.0 - 1.0;
	vec2 s3 = texture(wave_normals, p / 5.3 + vec2(0.018, -0.006) * TIME).xy * 2.0 - 1.0;
	float detail = 1.0 - smoothstep(30.0, 400.0, dist);
	vec2 slope = (s1 * 0.55 + s2 * 0.45 + s3 * 0.35 * detail) * mix(0.3, 0.1, smoothstep(150.0, 6000.0, dist));
	vec3 n = normalize(up + vec3(slope.x, 0.0, slope.y));
	NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);

	// What lies behind the surface: how much water the view ray crosses.
	float raw = texture(depth_texture, SCREEN_UV).r;
	vec4 behind = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, raw, 1.0);
	behind.xyz /= behind.w;
	float path = max(length(behind.xyz) - length(VERTEX), 0.0);
	// Bend the view of the floor with the ripples, more through deeper water.
	vec2 bent = SCREEN_UV + slope * 0.04 * clamp(path / 4.0, 0.0, 1.0) / max(dist * 0.02, 1.0);
	vec3 floor_colour = textureLod(screen_texture, bent, 0.0).rgb;

	// The air in front of the surface (also already on the floor's colour, so take it off there once).
	vec3 dummy = vec3(1.0);
	vec4 fog;
	hu_aerial(v_world, CAMERA_POSITION_WORLD, 8, dummy, fog);
	vec3 clear_floor = max(floor_colour - fog.rgb * fog.a, vec3(0.0)) / max(1.0 - fog.a, 0.05);

	// Light reaching the water: the sun through the air and clouds, and the sky.
	float mus = dot(hu_sun_direction, up);
	v_cloud = hu_cloud_shadow(v_world, 0.0);
	vec3 light = hu_sun_illuminance * hu_light_transmittance(0.0, mus) * max(mus, 0.0) * v_cloud / HU_PI
		+ hu_sun_illuminance * hu_multiscatter(0.0, mus) * 2.5
		+ hu_moon_illuminance * hu_light_transmittance(0.0, dot(hu_moon_direction, up)) * max(dot(hu_moon_direction, up), 0.0) / HU_PI;

	vec3 through = exp(-absorption * path);
	float depth_mix = 1.0 - dot(through, vec3(1.0 / 3.0));
	// Shallows over sand glow turquoise; deep water is a darker blue.
	float shallows = 1.0 - smoothstep(0.5, 6.0, path);
	vec3 body = clear_floor * through + scattering * light * depth_mix * (4.0 + 5.0 * shallows) * vec3(0.8, 1.0, 1.0);
	float ndv = clamp(dot(n, normalize(CAMERA_POSITION_WORLD - v_world)), 0.0, 1.0);
	float fresnel = 0.02 + 0.98 * pow(1.0 - ndv, 5.0);

	// Foam where the water thins out on the shore, broken by the ripples.
	float foam_noise = texture(wave_normals, p / 3.1 + vec2(0.02, 0.0) * TIME).b;
	float foam = (1.0 - smoothstep(0.05, 0.9, path)) * smoothstep(0.35, 0.75, foam_noise + 0.25 * sin(TIME * 1.3 + p.x * 0.15));
	foam = max(foam, (1.0 - smoothstep(0.0, 0.25, path)) * 0.6);
	// Surf: lines of breaking waves along the depth contours, rolling in toward the beach and fading as they
	// spread; broken up by the ripples, never closer than a few metres from each other.
	float depth = max(v_floor, 0.0);
	float surf_zone = smoothstep(0.08, 0.35, depth) * (1.0 - smoothstep(0.9, 2.8, depth)) * (1.0 - smoothstep(150.0, 600.0, dist));
	float broken = texture(wave_normals, p / 23.0 + vec2(0.003, 0.001) * TIME).b;
	float phase = depth * 0.95 + TIME * 0.13 + broken * 0.7;
	float f = fract(phase);
	float crest = smoothstep(0.0, 0.03, f) * (1.0 - smoothstep(0.03, 0.2, f));
	float lace = smoothstep(0.42, 0.75, texture(wave_normals, p / 4.7 - vec2(0.0, 0.05) * TIME).b + crest * 0.25);
	float runs = smoothstep(0.35, 0.6, broken);
	foam = max(foam, crest * lace * runs * surf_zone * 0.85);
	// The swash: a thin sheet of foam running up the sand and back.
	float swash = 1.0 - smoothstep(0.0, 0.1 + 0.08 * sin(TIME * 0.7 + p.x * 0.05 + p.y * 0.03), depth);
	foam = max(foam, swash * smoothstep(0.3, 0.6, foam_noise) * 0.8 * (1.0 - smoothstep(80.0, 300.0, dist)));

	ALBEDO = vec3(0.85) * foam;
	EMISSION = body * (1.0 - fresnel) * (1.0 - foam);
	ROUGHNESS = mix(mix(0.04, 0.14, smoothstep(300.0, 20000.0, dist)), 0.7, foam);
	SPECULAR = 0.35;
	METALLIC = 0.0;
	FOG = fog;
}

void light() {
	float ndl = clamp(dot(NORMAL, LIGHT), 0.0, 1.0);
	float cloud = LIGHT_IS_DIRECTIONAL ? v_cloud : 1.0;
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

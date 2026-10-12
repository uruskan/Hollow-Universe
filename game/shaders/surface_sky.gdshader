// The sky of a planet's surface (GROUND.md §4.6): its air scattering the sun and the moon (computed at quarter
// resolution), a cloud layer, then at full resolution the sun's disc, the moon with its phase, the system's other
// stars and planets, a star field and the galaxy's band. Presentation only.
shader_type sky;
render_mode use_quarter_res_pass;

#include "res://shaders/surface_atmosphere.gdshaderinc"
#include "res://shaders/surface_clouds.gdshaderinc"

// Render space to the system frame: the star field and the galaxy are fixed there.
uniform mat3 celestial = mat3(1.0);
uniform vec3 galaxy_pole = vec3(0.0, 1.0, 0.0);      // the galactic plane's normal, system frame
uniform vec3 galaxy_centre = vec3(0.0, 0.0, 1.0);    // toward the galactic centre, system frame
uniform float star_brightness = 1.0;                 // illuminance of a magnitude-0 star
uniform float galaxy_brightness = 1.0;
uniform float pixel_angle = 0.0015;                  // radians per pixel, for point sources

uniform float sun_angular_radius = 0.0045;
uniform vec3 moon_direction_view = vec3(0.0, 1.0, 0.0); // from the camera (with parallax)
uniform float moon_angular_radius = 0.008;
uniform vec3 moon_radiance = vec3(0.1);             // a fully lit patch of the moon
uniform float moon_max = 1e9;                        // the brightest the disc is shown
uniform vec3 moon_sun_direction = vec3(0.0, 1.0, 0.0); // from the moon toward the sun
uniform vec3 moon_earthshine = vec3(0.0);

// Point lights of the sky: the other suns and the planets (direction xyz, w = illuminance), colour per point.
uniform vec4 points[8];
uniform vec3 point_colours[8];
uniform int point_count = 0;
uniform int debug_view = 0;

float hash12(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

vec2 hash22(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * vec3(0.1031, 0.1030, 0.0973));
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.xx + p3.yz) * p3.zy);
}

// A point source of illuminance e at angular distance a: a Gaussian about a pixel wide holding all its light.
float point_source(float a, float e) {
	float s = pixel_angle * 0.75;
	return e / (2.0 * HU_PI * s * s) * exp(-0.5 * a * a / (s * s));
}

vec3 star_colour(float t) {
	// From red dwarfs to blue giants; most stars near white.
	vec3 red = vec3(1.0, 0.62, 0.42);
	vec3 white = vec3(1.0, 0.96, 0.92);
	vec3 blue = vec3(0.72, 0.82, 1.0);
	return t < 0.5 ? mix(red, white, t * 2.0) : mix(white, blue, (t - 0.5) * 2.0);
}

// One layer of the star field on a cube grid of n cells per face edge.
vec3 star_layer(vec3 d, float n, float density, float mag_min, float mag_max, float seed) {
	vec3 a = abs(d);
	vec2 uv;
	float face;
	if (a.x >= a.y && a.x >= a.z) {
		uv = d.yz / a.x;
		face = d.x > 0.0 ? 0.0 : 1.0;
	} else if (a.y >= a.z) {
		uv = d.xz / a.y;
		face = d.y > 0.0 ? 2.0 : 3.0;
	} else {
		uv = d.xy / a.z;
		face = d.z > 0.0 ? 4.0 : 5.0;
	}
	vec2 g = (uv * 0.5 + 0.5) * n;
	vec2 cell = floor(g);
	vec3 result = vec3(0.0);
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 c = cell + vec2(float(i), float(j));
			vec2 key = c + vec2(face * 1013.0 + seed, face * 7.0 + seed * 3.0);
			if (hash12(key) > density) {
				continue;
			}
			vec2 p = (c + hash22(key + 17.0)) / n * 2.0 - 1.0;
			vec3 sd;
			if (face < 1.5) {
				sd = normalize(vec3(face < 0.5 ? 1.0 : -1.0, p.x, p.y));
			} else if (face < 3.5) {
				sd = normalize(vec3(p.x, face < 2.5 ? 1.0 : -1.0, p.y));
			} else {
				sd = normalize(vec3(p.x, p.y, face < 4.5 ? 1.0 : -1.0));
			}
			float angle = acos(clamp(dot(sd, d), -1.0, 1.0));
			float m = mix(mag_min, mag_max, pow(hash12(key + 5.0), 0.6));
			float e = star_brightness * pow(10.0, -0.4 * m);
			result += star_colour(hash12(key + 9.0)) * point_source(angle, e);
		}
	}
	return result;
}

float noise3(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float n000 = hash12(i.xy + i.z * 37.0), n100 = hash12(i.xy + vec2(1.0, 0.0) + i.z * 37.0);
	float n010 = hash12(i.xy + vec2(0.0, 1.0) + i.z * 37.0), n110 = hash12(i.xy + vec2(1.0, 1.0) + i.z * 37.0);
	float n001 = hash12(i.xy + (i.z + 1.0) * 37.0), n101 = hash12(i.xy + vec2(1.0, 0.0) + (i.z + 1.0) * 37.0);
	float n011 = hash12(i.xy + vec2(0.0, 1.0) + (i.z + 1.0) * 37.0), n111 = hash12(i.xy + vec2(1.0, 1.0) + (i.z + 1.0) * 37.0);
	return mix(mix(mix(n000, n100, f.x), mix(n010, n110, f.x), f.y), mix(mix(n001, n101, f.x), mix(n011, n111, f.x), f.y), f.z);
}

// The galaxy's band: brightest toward its centre, split by dark dust lanes.
vec3 galaxy_band(vec3 c) {
	float b = asin(clamp(dot(c, galaxy_pole), -1.0, 1.0));
	float toward = dot(c, galaxy_centre) * 0.5 + 0.5;
	float n = noise3(c * 6.0) * 0.6 + noise3(c * 15.0) * 0.3 + noise3(c * 40.0) * 0.1;
	float width = 0.065 + 0.035 * toward * toward;
	float band = exp(-b * b / (width * width)) * (0.3 + 0.6 * pow(toward, 3.0)) * (0.35 + 1.3 * n * n);
	float dust = 1.0 - 0.75 * exp(-b * b / (0.018 * 0.018)) * smoothstep(0.35, 0.75, noise3(c * 9.0 + 3.0));
	vec3 colour = mix(vec3(0.75, 0.82, 1.0), vec3(1.0, 0.86, 0.68), toward);
	return colour * band * dust * galaxy_brightness;
}

void sky() {
	vec3 rd = normalize(EYEDIR);
	if (AT_CUBEMAP_PASS) {
		bool ground;
		float t = hu_ray_length(rd, ground);
		vec3 L;
		vec3 T;
		hu_atmosphere(rd, t, 10, L, T);
		if (ground) {
			// The ground below the horizon, for reflections and ambient light: lit by the sun through the air.
			float mus = dot(hu_sun_direction, hu_camera_up);
			L += T * hu_sun_illuminance * hu_light_transmittance(0.0, mus) * max(mus, 0.0) * vec3(0.12, 0.14, 0.11) / HU_PI;
		}
		vec4 cloud = hu_clouds(rd, 6, 2, 0.5);
		COLOR = L * (1.0 - cloud.a) + cloud.rgb;
	} else if (AT_QUARTER_RES_PASS) {
		bool ground;
		float t = hu_ray_length(rd, ground);
		vec3 L;
		vec3 T;
		hu_atmosphere(rd, t, 32, L, T);
		if (ground) {
			float mus = dot(hu_sun_direction, hu_camera_up);
			L += T * hu_sun_illuminance * hu_light_transmittance(0.0, mus) * max(mus, 0.0) * vec3(0.10, 0.12, 0.09) / HU_PI;
		}
		// Interleaved gradient noise spreads the march's samples, so its steps don't show as slices.
		float jitter = fract(52.9829189 * fract(dot(FRAGCOORD.xy, vec2(0.06711056, 0.00583715))));
		vec4 cloud = hu_clouds(rd, 28, 4, jitter);
		COLOR = L * (1.0 - cloud.a) + cloud.rgb;
		ALPHA = cloud.a;
	} else if (debug_view == 1) {
		COLOR = vec3(0.2, 0.4, 0.8);
	} else if (debug_view == 2) {
		COLOR = hu_transmittance(hu_camera_altitude, dot(rd, hu_camera_up));
	} else if (debug_view == 3) {
		bool g;
		float t = hu_ray_length(rd, g);
		vec3 L;
		vec3 T;
		hu_atmosphere(rd, t, 16, L, T);
		COLOR = L * 4.0;
	} else if (debug_view == 4) {
		COLOR = vec3(hu_multiscatter(hu_camera_altitude, dot(hu_sun_direction, hu_camera_up)) * 50.0);
	} else if (debug_view == 5) {
		COLOR = abs(hu_sun_direction);
	} else {
		vec4 base = QUARTER_RES_COLOR;
		vec3 colour = base.rgb;
		float clear = 1.0 - base.a;
		float mu = dot(rd, hu_camera_up);
		vec3 T = hu_transmittance(hu_camera_altitude, mu);
		bool ground;
		hu_ray_length(rd, ground);
		if (!ground) {
			// The sun's disc, darker at its limb.
			float cs = dot(rd, hu_sun_direction);
			float a = acos(clamp(cs, -1.0, 1.0));
			if (a < sun_angular_radius * 1.5) {
				float x = clamp(a / sun_angular_radius, 0.0, 1.0);
				float limb = 1.0 - 0.6 * (1.0 - sqrt(max(1.0 - x * x, 0.0)));
				float edge = 1.0 - smoothstep(0.9, 1.05, a / sun_angular_radius);
				vec3 radiance = hu_sun_illuminance / (HU_PI * sun_angular_radius * sun_angular_radius) * limb * edge;
				colour += radiance * T * clear;
			}
			// The moon: a sphere lit from the sun's side, with maria and craters; earthshine on its dark side.
			float am = acos(clamp(dot(rd, moon_direction_view), -1.0, 1.0));
			float moon_alpha = 0.0;
			if (am < moon_angular_radius * 1.2) {
				vec3 axis_x = normalize(cross(moon_direction_view, abs(moon_direction_view.y) < 0.9 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0)));
				vec3 axis_y = cross(axis_x, moon_direction_view);
				vec2 p = vec2(dot(rd, axis_x), dot(rd, axis_y)) / sin(moon_angular_radius);
				float rr = dot(p, p);
				if (rr < 1.0) {
					vec3 n = normalize(p.x * axis_x + p.y * axis_y - sqrt(1.0 - rr) * moon_direction_view);
					vec3 local = vec3(dot(n, axis_x), dot(n, axis_y), dot(n, moon_direction_view));
					float maria = smoothstep(0.45, 0.6, noise3(local * 2.2 + 4.0) * 0.7 + noise3(local * 5.0) * 0.3);
					float craters = noise3(local * 18.0) * 0.5 + noise3(local * 40.0) * 0.5;
					float albedo = mix(1.0, 0.62, maria) * (0.85 + 0.3 * craters);
					float lit = max(dot(n, moon_sun_direction), 0.0);
					vec3 radiance = moon_radiance * albedo * lit + moon_earthshine * albedo;
					radiance *= min(1.0, moon_max / max(max(moon_radiance.r, moon_radiance.g), 1e-12));
					colour = colour * (1.0 - clear) + clear * (colour + radiance * T);
					moon_alpha = 1.0;
				}
			}
			// Night sky: stars, the system's other suns and planets, the galaxy's band; all behind the moon.
			vec3 night = vec3(0.0);
			vec3 c = celestial * rd;
			night += star_layer(c, 9.0, 0.32, -0.6, 2.6, 1.0);
			night += star_layer(c, 34.0, 0.5, 2.6, 4.8, 2.0);
			night += star_layer(c, 110.0, 0.45, 4.8, 6.4, 3.0);
			night += galaxy_band(c);
			for (int i = 0; i < 8; i++) {
				if (i >= point_count) {
					break;
				}
				float ap = acos(clamp(dot(rd, points[i].xyz), -1.0, 1.0));
				night += point_colours[i] * point_source(ap, points[i].w);
			}
			colour += night * T * clear * (1.0 - moon_alpha);
		}
		COLOR = colour;
	}
}

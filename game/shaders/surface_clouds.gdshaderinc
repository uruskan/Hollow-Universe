// A layer of fair-weather clouds (GROUND.md §4.6): a shell of cumulus between two altitudes, shaped from a tiling
// noise texture that drifts with the wind, lit by the sun and the moon through their own thickness and by the sky.
// The terrain samples the same field for cloud shadows. Needs surface_atmosphere.gdshaderinc first.

global uniform sampler2D hu_cloud_noise;    // r: cover, g: billows, b: detail, a: wisps
global uniform vec2 hu_cloud_wind;          // metres the field has drifted
global uniform float hu_cloud_coverage;     // 0 clear, 1 overcast
global uniform vec2 hu_cloud_layer;         // bottom and top altitude, metres
global uniform vec3 hu_camera_position;     // render space

const float HU_CLOUD_EXTINCTION = 0.06;     // per metre at full density

float hu_cloud_density(vec2 xz, float h) {
	float bottom = hu_cloud_layer.x;
	float top = hu_cloud_layer.y;
	float hf = clamp((h - bottom) / (top - bottom), 0.0, 1.0);
	vec2 p = xz + hu_cloud_wind;
	float cover = texture(hu_cloud_noise, p / 60000.0).r;
	float billow = texture(hu_cloud_noise, p / 15000.0 + vec2(0.37, 0.71)).g;
	float base = cover * 0.62 + billow * 0.38;
	// Flat bases, rounded tops: the cloud narrows with height.
	float shape = smoothstep(0.0, 0.12, hf) * (1.0 - smoothstep(0.35, 1.0, hf) * 0.75);
	float threshold = 1.0 - hu_cloud_coverage * shape;
	float d = clamp((base - threshold) / max(1.0 - threshold, 0.05), 0.0, 1.0);
	if (d <= 0.0) {
		return 0.0;
	}
	float detail = texture(hu_cloud_noise, p / 2600.0 + vec2(hf * 0.13, 0.0)).b;
	d = clamp(d - (1.0 - detail) * 0.35 * (1.0 - d), 0.0, 1.0);
	return d * smoothstep(0.0, 0.08, hf) * smoothstep(1.0, 0.85, hf);
}

float hu_henyey_greenstein(float g, float nu) {
	float g2 = g * g;
	return (1.0 - g2) / (4.0 * HU_PI * pow(max(1.0 + g2 - 2.0 * g * nu, 1e-4), 1.5));
}

// Clouds seen along rd from the camera: rgb is the light they send (already dimmed by the air in front of them),
// a their opacity.
vec4 hu_clouds(vec3 rd, int steps, int light_steps, float jitter) {
	if (hu_cloud_coverage <= 0.0) {
		return vec4(0.0);
	}
	float R = hu_planet_radius;
	float h0 = hu_camera_altitude;
	float r0 = R + h0;
	float mu = dot(rd, hu_camera_up);
	float rho0 = h0 * (2.0 * R + h0);
	// Distances to the bottom and top shells (camera below the layer, the usual case on the ground).
	float rb = R + hu_cloud_layer.x;
	float rt = R + hu_cloud_layer.y;
	float discB = r0 * r0 * mu * mu - rho0 + (rb - R) * (rb + R);
	float discT = r0 * r0 * mu * mu - rho0 + (rt - R) * (rt + R);
	float t0;
	float t1;
	if (h0 < hu_cloud_layer.x) {
		if (discB < 0.0) {
			return vec4(0.0);
		}
		t0 = -r0 * mu + sqrt(discB);
		t1 = -r0 * mu + sqrt(max(discT, 0.0));
		if (t0 < 0.0) {
			return vec4(0.0);
		}
	} else if (h0 < hu_cloud_layer.y) {
		t0 = 0.0;
		t1 = discB >= 0.0 && mu < 0.0 ? -r0 * mu - sqrt(discB) : -r0 * mu + sqrt(max(discT, 0.0));
	} else {
		if (mu >= 0.0 || discT < 0.0) {
			return vec4(0.0);
		}
		t0 = -r0 * mu - sqrt(discT);
		t1 = discB >= 0.0 ? -r0 * mu - sqrt(discB) : t0 + 4000.0;
	}
	float far_fade = 1.0 - smoothstep(60000.0, 140000.0, t0);
	if (far_fade <= 0.0) {
		return vec4(0.0);
	}
	t1 = min(t1, t0 + 9000.0);
	float dt = (t1 - t0) / float(steps);
	float mus0 = dot(hu_sun_direction, hu_camera_up);
	float mum0 = dot(hu_moon_direction, hu_camera_up);
	float nus = dot(rd, hu_sun_direction);
	float num = dot(rd, hu_moon_direction);
	float phase_s = mix(hu_henyey_greenstein(-0.15, nus), hu_henyey_greenstein(0.75, nus), 0.6);
	float phase_m = mix(hu_henyey_greenstein(-0.15, num), hu_henyey_greenstein(0.75, num), 0.6);
	vec3 light = vec3(0.0);
	float T = 1.0;
	float first = -1.0;
	for (int i = 0; i < steps; i++) {
		float t = t0 + (float(i) + jitter) * dt;
		vec3 p = hu_camera_position + rd * t;
		float rho2 = max(rho0 + 2.0 * r0 * mu * t + t * t, 0.0);
		float r = sqrt(R * R + rho2);
		float h = rho2 / (r + R);
		float d = hu_cloud_density(p.xz, h);
		if (d <= 0.001) {
			continue;
		}
		if (first < 0.0) {
			first = t;
		}
		float sigma = d * HU_CLOUD_EXTINCTION;
		// Optical depth toward the sun inside the layer.
		float mus = (r0 * mus0 + t * nus) / r;
		float od = 0.0;
		float ls = (hu_cloud_layer.y - h) / max(mus, 0.12) / float(light_steps);
		for (int j = 1; j <= light_steps; j++) {
			vec3 q = p + hu_sun_direction * ls * (float(j) - 0.5);
			od += hu_cloud_density(q.xz, h + mus * ls * (float(j) - 0.5)) * HU_CLOUD_EXTINCTION * ls;
		}
		float beer = exp(-od) * 0.7 + exp(-od * 0.25) * 0.3;   // light leaking deeper than Beer's law alone
		float powder = 1.0 - exp(-sigma * 120.0);              // dark edges where the cloud is thin toward the light
		vec3 sun = hu_sun_illuminance * hu_light_transmittance(h, mus) * beer * phase_s * mix(1.0, powder, 0.5);
		vec3 ambient = hu_sun_illuminance * hu_multiscatter(h, mus) * 6.0 * (0.6 + 0.4 * (h - hu_cloud_layer.x) / (hu_cloud_layer.y - hu_cloud_layer.x));
		float mum = (r0 * mum0 + t * num) / r;
		vec3 moon = hu_moon_illuminance * (hu_light_transmittance(h, mum) * exp(-od) * phase_m + hu_multiscatter(h, mum) * 6.0);
		vec3 S = (sun + ambient + moon) * sigma;
		float step_t = exp(-sigma * dt);
		light += T * (S - S * step_t) / sigma;
		T *= step_t;
		if (T < 0.02) {
			break;
		}
	}
	if (first < 0.0) {
		return vec4(0.0);
	}
	// The air between the camera and the cloud dims it and adds its own light.
	vec3 L;
	vec3 Ta;
	hu_atmosphere(rd, first, 6, L, Ta);
	float alpha = (1.0 - T) * far_fade;
	return vec4((light * Ta + L * (1.0 - T)) * far_fade, alpha);
}

// How much of the sun reaches a point on the ground through the clouds (0–1).
float hu_cloud_shadow(vec3 world_point, float altitude) {
	if (hu_cloud_coverage <= 0.0) {
		return 1.0;
	}
	float mid = mix(hu_cloud_layer.x, hu_cloud_layer.y, 0.35);
	float mus = max(dot(hu_sun_direction, hu_camera_up), 0.08);
	vec3 q = world_point + hu_sun_direction * (mid - altitude) / mus;
	float d = hu_cloud_density(q.xz, mid);
	return exp(-d * HU_CLOUD_EXTINCTION * (hu_cloud_layer.y - hu_cloud_layer.x) * 0.25 / mus);
}

// The planet's air, shared by the sky and everything drawn under it (GROUND.md §4.6): single scattering ray-marched
// along the view ray, with transmittance and multiple scattering from tables precomputed for this planet
// (AtmosphereModel.cs, after Bruneton 2017 and Hillaire 2020). Everything is relative to the camera: its altitude
// and "up" come from the CPU in double precision, so heights stay exact although the planet's centre is thousands
// of kilometres away. Units: metres; light in the same units as the sun's illuminance.

global uniform float hu_planet_radius;      // sea level
global uniform float hu_atmosphere_top;     // radius of the top of the air
global uniform float hu_camera_altitude;    // above sea level
global uniform vec3 hu_camera_up;           // unit, render space
global uniform vec3 hu_sun_direction;       // unit, render space, toward the sun
global uniform vec3 hu_sun_illuminance;     // above the air
global uniform vec3 hu_moon_direction;
global uniform vec3 hu_moon_illuminance;    // sunlight reflected by the moon, above the air
global uniform vec4 hu_rayleigh;            // rgb scattering per metre at sea level, a = scale height
global uniform vec4 hu_mie;                 // x scattering, y extinction, z scale height, w anisotropy g
global uniform vec4 hu_ozone;               // rgb absorption per metre at its peak, a = altitude of the peak
global uniform float hu_ozone_half_width;
global uniform sampler2D hu_transmittance_lut;
global uniform sampler2D hu_multiscatter_lut;

const float HU_PI = 3.14159265358979;

// Transmittance from altitude h toward the top of the air at cosine of zenith mu (Bruneton's table mapping).
vec3 hu_transmittance(float h, float mu) {
	float R = hu_planet_radius;
	float top = hu_atmosphere_top;
	h = max(h, 0.0);
	float H2 = (top - R) * (top + R);
	float rho2 = h * (2.0 * R + h);
	float r = R + h;
	float d = max(-r * mu + sqrt(max(H2 - rho2 + r * r * mu * mu, 0.0)), 0.0);
	float rho = sqrt(rho2);
	float dmin = (top - R) - h;
	float dmax = rho + sqrt(H2);
	float u = clamp((d - dmin) / max(dmax - dmin, 1.0), 0.0, 1.0);
	float v = clamp(rho / sqrt(H2), 0.0, 1.0);
	return texture(hu_transmittance_lut, vec2(0.5 + u * 255.0, 0.5 + v * 63.0) / vec2(256.0, 64.0)).rgb;
}

// Transmittance toward a light, with the planet's shadow softened over the light's disc.
vec3 hu_light_transmittance(float h, float mus) {
	h = max(h, 0.0);
	float r = hu_planet_radius + h;
	float horizon = -sqrt(h * (2.0 * hu_planet_radius + h)) / r;
	return hu_transmittance(h, mus) * smoothstep(horizon - 0.008, horizon + 0.004, mus);
}

vec3 hu_multiscatter(float h, float mus) {
	float x = mus * 0.5 + 0.5;
	float y = clamp(h / (hu_atmosphere_top - hu_planet_radius), 0.0, 1.0);
	return texture(hu_multiscatter_lut, vec2(0.5 + x * 31.0, 0.5 + y * 31.0) / 32.0).rgb;
}

float hu_rayleigh_phase(float nu) {
	return 3.0 / (16.0 * HU_PI) * (1.0 + nu * nu);
}

float hu_mie_phase(float nu) {
	float g = hu_mie.w;
	float g2 = g * g;
	return 3.0 / (8.0 * HU_PI) * ((1.0 - g2) * (1.0 + nu * nu)) / ((2.0 + g2) * pow(max(1.0 + g2 - 2.0 * g * nu, 1e-4), 1.5));
}

// Distance from the camera along rd to the top of the air (or to the sea-level sphere when ground is true).
float hu_ray_length(vec3 rd, out bool ground) {
	float R = hu_planet_radius;
	float h0 = hu_camera_altitude;
	float r0 = R + h0;
	float mu = dot(rd, hu_camera_up);
	float rho0 = h0 * (2.0 * R + h0);
	float H2 = (hu_atmosphere_top - R) * (hu_atmosphere_top + R);
	float discGround = r0 * r0 * mu * mu - rho0;
	ground = mu < 0.0 && discGround >= 0.0;
	if (ground) {
		return max(-r0 * mu - sqrt(discGround), 0.0);
	}
	return max(-r0 * mu + sqrt(max(H2 - rho0 + r0 * r0 * mu * mu, 0.0)), 0.0);
}

// Light scattered toward the camera along the view ray rd over t_max metres, and the transmittance of that path,
// from the sun and (when it shines) the moon.
void hu_atmosphere(vec3 rd, float t_max, int steps, out vec3 inscatter, out vec3 transmittance) {
	float R = hu_planet_radius;
	float h0 = hu_camera_altitude;
	float r0 = R + h0;
	float rho0 = h0 * (2.0 * R + h0);
	float mu = dot(rd, hu_camera_up);
	float mus0 = dot(hu_sun_direction, hu_camera_up);
	float mum0 = dot(hu_moon_direction, hu_camera_up);
	float nus = dot(rd, hu_sun_direction);
	float num = dot(rd, hu_moon_direction);
	float prs = hu_rayleigh_phase(nus);
	float pms = hu_mie_phase(nus);
	float prm = hu_rayleigh_phase(num);
	float pmm = hu_mie_phase(num);
	bool moon = dot(hu_moon_illuminance, vec3(1.0)) > 0.0;
	vec3 L = vec3(0.0);
	vec3 T = vec3(1.0);
	float dt = t_max / float(steps);
	for (int i = 0; i < steps; i++) {
		float t = (float(i) + 0.5) * dt;
		float rho2 = max(rho0 + 2.0 * r0 * mu * t + t * t, 0.0);
		float r = sqrt(R * R + rho2);
		float h = rho2 / (r + R);
		float dr = exp(-h / hu_rayleigh.w);
		float dm = exp(-h / hu_mie.z);
		float dz = max(0.0, 1.0 - abs(h - hu_ozone.w) / hu_ozone_half_width);
		vec3 sr = hu_rayleigh.rgb * dr;
		float sm = hu_mie.x * dm;
		vec3 extinction = sr + vec3(hu_mie.y * dm) + hu_ozone.rgb * dz;
		float mus = (r0 * mus0 + t * nus) / r;
		vec3 S = hu_sun_illuminance * (hu_light_transmittance(h, mus) * (sr * prs + sm * pms) + hu_multiscatter(h, mus) * (sr + sm));
		if (moon) {
			float mum = (r0 * mum0 + t * num) / r;
			S += hu_moon_illuminance * (hu_light_transmittance(h, mum) * (sr * prm + sm * pmm) + hu_multiscatter(h, mum) * (sr + sm));
		}
		vec3 step_t = exp(-extinction * dt);
		L += T * (S - S * step_t) / max(extinction, vec3(1e-12));
		T *= step_t;
	}
	inscatter = L;
	transmittance = T;
}

// Aerial perspective for a surface point: applies the air between the camera and the point through Godot's fog
// output (colour × mean transmittance + in-scattered light), tinting the albedo by the transmittance's colour.
void hu_aerial(vec3 world_point, vec3 camera, int steps, inout vec3 albedo, out vec4 fog) {
	vec3 d = world_point - camera;
	float t = max(length(d), 0.01);
	vec3 L;
	vec3 T;
	hu_atmosphere(d / t, t, steps, L, T);
	float ta = clamp(dot(T, vec3(1.0 / 3.0)), 1e-4, 1.0);
	albedo *= clamp(T / ta, vec3(0.0), vec3(2.0));
	fog = vec4(L / max(1.0 - ta, 1e-4), 1.0 - ta);
}

// System-map planets and moons: procedural surface colours by planet type, lit by the star.
shader_type spatial;
render_mode diffuse_lambert, specular_disabled;

uniform vec3 colour_a = vec3(0.5);
uniform vec3 colour_b = vec3(0.4);
uniform vec3 colour_c = vec3(0.2, 0.3, 0.6);
uniform float banding = 0.0;
uniform float noise_scale = 3.0;
uniform float seed = 0.0;
uniform float ocean = 0.0;
uniform float ice_caps = 0.0;
uniform float atmosphere = 0.0;
uniform vec3 atmosphere_colour = vec3(0.5, 0.7, 1.0);
uniform float ambient = 0.06;

varying vec3 v_object;

void vertex() {
	v_object = VERTEX;
}

float hash(vec3 p) {
	p = fract(p * 0.3183099 + vec3(0.11, 0.17, 0.13));
	p *= 17.0;
	return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float noise(vec3 x) {
	vec3 i = floor(x);
	vec3 f = fract(x);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash(i + vec3(0, 0, 0)), hash(i + vec3(1, 0, 0)), f.x),
				   mix(hash(i + vec3(0, 1, 0)), hash(i + vec3(1, 1, 0)), f.x), f.y),
			   mix(mix(hash(i + vec3(0, 0, 1)), hash(i + vec3(1, 0, 1)), f.x),
				   mix(hash(i + vec3(0, 1, 1)), hash(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

float fbm(vec3 p) {
	float v = 0.0;
	float a = 0.5;
	for (int i = 0; i < 5; i++) {
		v += a * noise(p);
		p = p * 2.03 + vec3(1.7, 9.2, 3.1);
		a *= 0.5;
	}
	return v;
}

void fragment() {
	vec3 p = normalize(v_object);
	float n = fbm(p * noise_scale + vec3(seed));
	vec3 colour;
	if (banding > 0.0) {
		float warp = fbm(p * 2.0 + vec3(seed * 1.3)) * 2.5;
		float bands = sin(p.y * banding + warp);
		colour = mix(colour_a, colour_b, bands * 0.5 + 0.5);
		colour = mix(colour, colour_c, smoothstep(0.62, 0.8, n) * 0.5);
	} else {
		colour = mix(colour_a, colour_b, smoothstep(0.3, 0.7, n));
		if (ocean > 0.0) {
			float land = smoothstep(ocean - 0.015, ocean + 0.015, n);
			colour = mix(colour_c, colour, land);
		}
		if (ice_caps > 0.0) {
			float cap = smoothstep(1.0 - ice_caps, 1.0 - ice_caps + 0.06, abs(p.y) + (n - 0.5) * 0.15);
			colour = mix(colour, vec3(0.92, 0.95, 1.0), cap);
		}
	}
	ALBEDO = colour;
	float rim = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
	EMISSION = atmosphere_colour * rim * atmosphere + colour * ambient;
}

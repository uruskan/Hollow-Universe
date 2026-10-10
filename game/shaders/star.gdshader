// System-map star surface: bright, limb-darkened, slowly churning.
shader_type spatial;
render_mode unshaded;

uniform vec3 colour = vec3(1.0, 0.9, 0.7);
uniform float intensity = 3.0;
uniform float seed = 0.0;

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
	return mix(mix(mix(hash(i), hash(i + vec3(1, 0, 0)), f.x), mix(hash(i + vec3(0, 1, 0)), hash(i + vec3(1, 1, 0)), f.x), f.y),
			   mix(mix(hash(i + vec3(0, 0, 1)), hash(i + vec3(1, 0, 1)), f.x), mix(hash(i + vec3(0, 1, 1)), hash(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

void fragment() {
	vec3 p = normalize(v_object);
	float t = TIME * 0.05;
	float g = noise(p * 9.0 + vec3(seed, t, -t)) * 0.6 + noise(p * 23.0 + vec3(t, seed, t)) * 0.4;
	float mu = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	float limb = 0.45 + 0.55 * pow(mu, 0.6);
	ALBEDO = colour * intensity * limb * (0.85 + 0.3 * g);
}

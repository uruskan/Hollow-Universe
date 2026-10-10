// The whole galaxy as glowing gas and unresolved stars, drawn as the sky: for each pixel, a ray from the
// camera is marched through a volume texture built from the same density model the stars come from.
// As a sky it is always behind everything and independent of the camera's far plane.
// Texture x/z map to the model frame (centred on the Galactic Centre); y is stretched near the plane.
shader_type sky;

uniform sampler3D volume : filter_linear, repeat_disable;
uniform vec3 galaxy_centre;
uniform vec3 half_extent = vec3(60000.0, 12000.0, 60000.0);
uniform float intensity = 1.0;
uniform float dust_strength = 1.0;
uniform float gamma = 1.0;
uniform int steps = 96;
uniform bool ready = false;

vec3 to_uvw(vec3 p) {
	float yn = clamp(p.y / half_extent.y, -1.0, 1.0);
	float v = 0.5 + 0.5 * sign(yn) * sqrt(abs(yn));
	return vec3(p.x / (2.0 * half_extent.x) + 0.5, v, p.z / (2.0 * half_extent.z) + 0.5);
}

vec3 galaxy(vec3 position, vec3 direction, vec2 fragcoord) {
	vec3 cam = position - galaxy_centre;
	vec3 dir = normalize(direction);
	vec3 inv = 1.0 / dir;
	vec3 t0 = (-half_extent - cam) * inv;
	vec3 t1 = (half_extent - cam) * inv;
	vec3 tmin = min(t0, t1);
	vec3 tmax = max(t0, t1);
	float t_near = max(max(tmin.x, tmin.y), max(tmin.z, 0.0));
	float t_far = min(min(tmax.x, tmax.y), tmax.z);
	if (t_far <= t_near) {
		return vec3(0.0);
	}
	float dt = (t_far - t_near) / float(steps);
	float jitter = fract(sin(dot(fragcoord, vec2(12.9898, 78.233))) * 43758.5453);
	vec3 light = vec3(0.0);
	float transmittance = 1.0;
	for (int i = 0; i < steps; i++) {
		vec3 p = cam + dir * (t_near + (float(i) + jitter) * dt);
		vec4 s = texture(volume, to_uvw(p));
		light += transmittance * s.rgb * dt;
		transmittance *= exp(-s.a * dust_strength * dt);
		if (transmittance < 0.01) {
			break;
		}
	}
	// Tone-map brightness, keep the hue: young arms stay blue, the bulge stays gold.
	float luminance = dot(light, vec3(0.2126, 0.7152, 0.0722));
	float mapped = 1.0 - exp(-pow(luminance * intensity, gamma));
	vec3 hue = light / max(luminance, 1e-6);
	return clamp(hue * mapped, 0.0, 1.0);
}

void sky() {
	vec3 background = vec3(0.004, 0.006, 0.012);
	if (AT_CUBEMAP_PASS || !ready) {
		COLOR = background;
	} else {
		COLOR = max(background, galaxy(POSITION, EYEDIR, FRAGCOORD.xy));
	}
}

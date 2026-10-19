// What the ground's plants share (docs/WALKING_GROUND.md §18): the grass's clumps and the authored ground cover. The air
// between the camera and a plant is worked out once a vertex, at the plant's root, rather than once a pixel; and a thin
// leaf's light is diffuse on its lit side, a glow through it toward the light that is strongest looking into a low sun
// (bounded: never more than the light itself), and a rough, faint sheen whose roughness is held high enough not to glitter.
// Needs the atmosphere include (hu_ap_lookup, hu_specular).
#ifndef HU_FOLIAGE_INCLUDED
#define HU_FOLIAGE_INCLUDED

// hu_aerial's two terms for a point: the albedo's tint by the air's transmittance and Godot's fog (colour, amount).
void hu_aerial_terms(vec3 world_point, vec3 camera, out vec3 tint, out vec4 fog) {
	vec3 d = world_point - camera;
	float t = max(length(d), 0.01);
	vec3 L;
	vec3 T;
	hu_ap_lookup(d / t, t, L, T);
	float ta = clamp(dot(T, vec3(1.0 / 3.0)), 1e-4, 1.0);
	tint = clamp(T / ta, vec3(0.0), vec3(2.0));
	fog = vec4(L / max(1.0 - ta, 1e-4), 1.0 - ta);
}

// One light on a thin leaf. n: the leaf's normal turned toward the viewer, v toward the viewer, l toward the light (one
// space); `strength` the light's colour times its attenuation and shadows; `transmission` how much passes through the
// leaf (0–1); `tip` how far along the blade (tips are thinner and glow more); `roughness` and `sheen` the specular lobe.
void hu_foliage_light(vec3 n, vec3 v, vec3 l, vec3 strength, float transmission, float tip, float roughness, float sheen,
		vec3 glow, inout vec3 diffuse, inout vec3 specular) {
	vec3 light = strength / HU_PI;
	float ndl = dot(n, l);
	diffuse += light * clamp(ndl, 0.0, 1.0);
	float behind = clamp(-ndl, 0.0, 1.0);
	float toward = pow(clamp(dot(-v, l), 0.0, 1.0), 4.0);
	diffuse += light * glow * transmission * min(0.35 * behind + 1.25 * toward * (0.3 + 0.7 * tip), 1.0);
	if (ndl > 0.0) {
		specular += strength * hu_specular(n, v, l, max(roughness, 0.45)) * sheen;
	}
}
#endif

// The colour of grass at a place (GROUND.md §4.6): one function for the blades and for the ground's grass layer under
// them, so the two match where the blades thin out into the distance. The blades are painted pale (hollow-orbit's
// atlas) and take their colour from this multiplier; the ground uses it times the blades' mean colour.

// How dry the grass looks: the climate's moisture, broken into patches (0 lush .. 1 straw).
float hu_grass_dryness(vec2 wp, float moisture, sampler2D macro_noise) {
	vec4 macro = texture(macro_noise, wp / 1730.0);
	vec4 macro2 = texture(macro_noise, wp / 211.0 + vec2(0.31, 0.77));
	float patch = smoothstep(0.3, 0.7, macro2.g);
	float local_patch = texture(macro_noise, wp / 13.0 + vec2(0.19, 0.67)).r;
	return clamp((1.0 - moisture) * 0.8 - 0.12 + (macro.g - 0.5) * 0.45 + (1.0 - patch) * 0.22
		+ (local_patch - 0.5) * 0.5, 0.0, 1.0);
}

// The tint of grass of a dryness at a brightness: leaf greens to dry straw (after hollow-orbit's meadow tints). The grass's
// clumps take it with the brightness GrassField works out on the CPU for each clump's root (GrassField.Tint.cs, the same
// noise and numbers as hu_grass_tint below), so blades and the ground under them share one colour.
vec3 hu_grass_tint_of(float dryness, float brightness) {
	return mix(vec3(0.08, 0.18, 0.065), vec3(0.28, 0.24, 0.13), dryness) * brightness;
}

// Linear colour multiplier of grass of that dryness, with gentle large patches.
vec3 hu_grass_tint(vec2 wp, float dryness, sampler2D macro_noise) {
	vec4 macro = texture(macro_noise, wp / 1730.0);
	vec4 macro3 = texture(macro_noise, wp / 37.0 + vec2(0.13, 0.41));
	float local_patch = texture(macro_noise, wp / 7.7 + vec2(0.41, 0.13)).b;
	return hu_grass_tint_of(dryness, (0.84 + 0.32 * macro.r) * (0.82 + 0.30 * macro3.b) * (0.84 + 0.28 * local_patch));
}

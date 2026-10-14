// The colour of grass at a place (GROUND.md §4.6): one function for the blades and for the ground's grass layer under
// them, so the two match where the blades thin out into the distance. The blades are painted pale (hollow-orbit's
// atlas) and take their colour from this multiplier; the ground uses it times the blades' mean colour.

// How dry the grass looks: the climate's moisture, broken into patches (0 lush .. 1 straw).
float hu_grass_dryness(vec2 wp, float moisture, sampler2D macro_noise) {
	vec4 macro = texture(macro_noise, wp / 1730.0);
	vec4 macro2 = texture(macro_noise, wp / 211.0 + vec2(0.31, 0.77));
	float patch = smoothstep(0.3, 0.7, macro2.g);
	return clamp((1.0 - moisture) * 0.8 - 0.12 + (macro.g - 0.5) * 0.45 + (1.0 - patch) * 0.22, 0.0, 1.0);
}

// Linear colour multiplier of grass of that dryness (after hollow-orbit's meadow tints), with gentle large patches.
vec3 hu_grass_tint(vec2 wp, float dryness, sampler2D macro_noise) {
	vec4 macro = texture(macro_noise, wp / 1730.0);
	vec4 macro3 = texture(macro_noise, wp / 37.0 + vec2(0.13, 0.41));
	vec3 tint = vec3(mix(0.13, 0.40, dryness), mix(0.27, 0.34, dryness), mix(0.06, 0.09, dryness));
	return tint * (0.84 + 0.32 * macro.r) * (0.9 + 0.2 * macro3.b);
}

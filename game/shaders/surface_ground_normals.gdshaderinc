// Normal maps describe slopes in their own texture axes. Move those slopes to
// world axes before blending, then remove the component along the surface normal.
// Surface-gradient formulation: https://jcgt.org/published/0009/03/04/
// These functions return the NEGATIVE gradient, added to the geometric normal.
vec2 hu_ground_normal_slope(vec3 encoded, vec2 axes) {
	vec3 n = encoded * 2.0 - 1.0;
	// Keep quantized near-horizontal normal texels finite (maximum slope 10).
	return n.xy * axes / max(n.z, 0.1);
}

vec2 hu_ground_rotated_slope(vec3 encoded, vec2 axes, mat2 uv_rotation) {
	vec2 slope = hu_ground_normal_slope(encoded, axes);
	// Texture rotation transforms coordinates; its transpose transforms slopes.
	return vec2(dot(uv_rotation[0], slope), dot(uv_rotation[1], slope));
}

vec3 hu_ground_tangent_bump(vec3 bump, vec3 n_geo) {
	return bump - n_geo * dot(n_geo, bump);
}

vec3 hu_ground_plane_bump(vec2 slope, vec3 u, vec3 v, vec3 n_geo) {
	return hu_ground_tangent_bump(u * slope.x + v * slope.y, n_geo);
}

vec3 hu_ground_triplanar_bump(vec3 sample_x, vec3 sample_y, vec3 sample_z,
		vec2 axes, vec3 weights, vec3 n_geo) {
	vec2 sx = hu_ground_normal_slope(sample_x, axes);
	vec2 sy = hu_ground_normal_slope(sample_y, axes);
	vec2 sz = hu_ground_normal_slope(sample_z, axes);
	// Match the sampled coordinate order exactly: X uses zy, Y xz, Z xy.
	vec3 bump = vec3(0.0, sx.y, sx.x) * weights.x
		+ vec3(sy.x, 0.0, sy.y) * weights.y
		+ vec3(sz.x, sz.y, 0.0) * weights.z;
	return hu_ground_tangent_bump(bump, n_geo);
}

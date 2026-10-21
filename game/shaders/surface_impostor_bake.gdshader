// Bakes the views of a tree for its impostor (GROUND.md §4.6): unlit, no wind; pass 0 writes the surface colour with
// alpha, pass 1 the camera-space normal (encoded so the stored sRGB value is n * 0.5 + 0.5).
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_opaque;

uniform int pass = 0;
uniform int mode = 0; // 0 leaves, 1 bark, 2 berries
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap, repeat_enable;
uniform vec4 albedo_factor : source_color = vec4(1.0);
uniform float alpha_cutoff = 0.5;
uniform bool vertex_colour = true;

vec3 srgb_to_linear(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

void fragment() {
	vec4 tex = mode == 2 ? vec4(1.0) : texture(albedo_texture, UV);
	tex *= albedo_factor;
	if (mode == 0 && tex.a < alpha_cutoff) {
		discard;
	}
	vec3 albedo = mode == 2 ? COLOR.rgb : tex.rgb * (vertex_colour ? COLOR.rgb : vec3(1.0));
	if (pass == 0) {
		ALBEDO = albedo;
	} else {
		vec3 n = normalize(NORMAL);
		ALBEDO = srgb_to_linear(clamp(n * 0.5 + 0.5, 0.0, 1.0));
	}
}

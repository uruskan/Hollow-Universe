// Camera-facing glow around a star.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, fog_disabled, shadows_disabled;

uniform vec3 colour = vec3(1.0, 0.9, 0.7);
uniform float strength = 1.0;

void vertex() {
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
	MODELVIEW_MATRIX = MODELVIEW_MATRIX * mat4(vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0), vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0), vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0), vec4(0.0, 0.0, 0.0, 1.0));
}

void fragment() {
	float r = length(UV * 2.0 - 1.0);
	float glow = exp(-r * r * 9.0) * 0.9 + exp(-r * 4.5) * 0.35;
	glow *= 1.0 - smoothstep(0.85, 1.0, r);
	ALBEDO = colour * glow * strength;
}

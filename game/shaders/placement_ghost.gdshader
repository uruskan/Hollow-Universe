// The placement ghost (GROUND_SYSTEMS.md §6): what a player is about to place, drawn as a hologram over its own model:
// one colour when it fits, red when it doesn't, a slow breathing while the server's answer is on its way. Unshaded, so
// it reads in any light; divided by the camera's exposure (the sky meters it a few thousand times brighter by night), so
// it looks as bright by day as by night instead of burning white in the dark.
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_back, shadows_disabled;

uniform vec4 colour : source_color = vec4(0.35, 0.85, 1.0, 0.4);
// The camera's exposure (Glow.Exposure): 1 by day.
uniform float exposure = 1.0;
// 1 while the answer is on its way: the hologram breathes.
uniform float pending = 0.0;
// How strongly the edges glow (a fresnel rim).
uniform float rim = 1.4;

void fragment() {
	float facing = clamp(abs(dot(NORMAL, VIEW)), 0.0, 1.0);
	float edge = pow(1.0 - facing, 2.5);
	// Faint lines across the model, in its own height, so a solid colour still reads as a projection.
	vec3 world = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float lines = 0.9 + 0.1 * step(0.5, fract(world.y * 12.0));
	float breathe = 1.0 - pending * (0.35 + 0.35 * sin(TIME * 5.0));
	ALBEDO = colour.rgb * (0.75 + rim * edge) / max(exposure, 0.001);
	ALPHA = clamp(colour.a * (0.6 + 1.4 * edge) * lines * breathe, 0.0, 0.95);
}

// Canonical body-space wave phases. CPU doubles reduce only each wave's anchor angle; local render coordinates
// never receive a huge world origin or a common metre wrap. The same float wavevectors are used on both sides.
#ifndef HU_WORLD_PHASE_INCLUDED
#define HU_WORLD_PHASE_INCLUDED
#include "res://shaders/surface_planet.gdshaderinc"
global uniform vec3 hu_phase_wave_main;
global uniform vec3 hu_phase_wave_foliage;
global uniform vec3 hu_phase_wave_tree;
global uniform vec3 hu_phase_wave_angles;

vec3 hu_body_local(vec3 render_point) {
	return hu_render_to_body * render_point;
}

vec3 hu_body_wave_phases(vec3 render_point) {
	vec3 local = hu_body_local(render_point);
	return vec3(dot(local, hu_phase_wave_main), dot(local, hu_phase_wave_foliage),
		dot(local, hu_phase_wave_tree)) + hu_phase_wave_angles;
}
#endif

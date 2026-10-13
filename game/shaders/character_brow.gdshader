// Eyebrows (GROUND.md §4.8): cards cut out at a threshold, both sides drawn, in the player's hair colour.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled, diffuse_burley, specular_schlick_ggx;

#define SCISSOR
#include "res://shaders/character_hair.gdshaderinc"

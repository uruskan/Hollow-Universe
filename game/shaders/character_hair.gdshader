// Hair (GROUND.md §4.8): the thin ends of the cards blend over a depth pre-pass of their solid middle, so the hairline
// is soft, not cut.
shader_type spatial;
render_mode blend_mix, depth_prepass_alpha, cull_back, diffuse_burley, specular_schlick_ggx;

#include "res://shaders/character_hair.gdshaderinc"

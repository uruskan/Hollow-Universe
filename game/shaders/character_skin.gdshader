// Skin of the player body (and the underwear, which is part of the body mesh). GROUND.md §4.8.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

#include "res://shaders/character_skin.gdshaderinc"

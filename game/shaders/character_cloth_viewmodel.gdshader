// First-person variant (GROUND.md §4.8).
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled, diffuse_burley, specular_schlick_ggx;

#define VIEWMODEL
#include "res://shaders/character_cloth.gdshaderinc"

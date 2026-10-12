// Clothing, armour and the underwear (GROUND.md §4.8). Garments are single shells drawn from both sides; the inside is
// darker, as it would be in shade.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled, diffuse_burley, specular_schlick_ggx;

#include "res://shaders/character_cloth.gdshaderinc"

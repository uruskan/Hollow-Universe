// Lit, alpha-blended particles (GROUND.md §9.3): dust, mist. See fx_billboard.gdshaderinc.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, specular_disabled, diffuse_lambert_wrap, skip_vertex_transform;

#define FX_LIT
#include "res://assets/fx/shaders/fx_billboard.gdshaderinc"

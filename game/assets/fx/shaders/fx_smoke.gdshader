// Lit smoke from the C4 flipbook with its normals (GROUND.md §9.3). See fx_billboard.gdshaderinc.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, specular_disabled, diffuse_lambert_wrap, skip_vertex_transform;

#define FX_LIT
#define FX_NORMAL_MAP
#include "res://assets/fx/shaders/fx_billboard.gdshaderinc"

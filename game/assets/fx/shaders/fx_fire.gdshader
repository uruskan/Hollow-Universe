// Fire from the C4 kit's premultiplied flipbook (GROUND.md §9.3): its light adds, its soot darkens what is behind.
// See fx_billboard.gdshaderinc.
shader_type spatial;
render_mode blend_premul_alpha, unshaded, depth_draw_never, cull_disabled, shadows_disabled, skip_vertex_transform;

#define FX_FIRE
#include "res://assets/fx/shaders/fx_billboard.gdshaderinc"

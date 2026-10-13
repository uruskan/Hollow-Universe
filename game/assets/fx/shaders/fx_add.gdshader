// Additive, unshaded particles (GROUND.md §9.3): flashes, fire, embers. See fx_billboard.gdshaderinc.
shader_type spatial;
render_mode blend_add, unshaded, depth_draw_never, cull_disabled, shadows_disabled, skip_vertex_transform;

#define FX_ADD
#include "res://assets/fx/shaders/fx_billboard.gdshaderinc"

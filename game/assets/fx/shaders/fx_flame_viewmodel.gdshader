// Flames in first person (the torch in hand): the view model's field of view (GROUND.md §9.3).
shader_type spatial;
render_mode blend_add, unshaded, depth_draw_never, cull_disabled, shadows_disabled, skip_vertex_transform;

#define VIEWMODEL
#include "res://assets/fx/shaders/fx_flame.gdshaderinc"

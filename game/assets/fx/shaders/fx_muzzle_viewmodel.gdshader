// Muzzle flash, first person: the view model's field of view, on its render layer (GROUND.md §9.3).
shader_type spatial;
render_mode blend_add, unshaded, depth_draw_never, cull_disabled, shadows_disabled;

#define VIEWMODEL
#include "res://assets/fx/shaders/fx_muzzle.gdshaderinc"

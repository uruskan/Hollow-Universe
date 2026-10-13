// Sparks (GROUND.md §9.3): additive streaks that cool from COLOR to COLOR * cool_color. See fx_streak.gdshaderinc.
shader_type spatial;
render_mode blend_add, unshaded, depth_draw_never, cull_disabled, shadows_disabled, skip_vertex_transform;

#define FX_ADD
#include "res://assets/fx/shaders/fx_streak.gdshaderinc"

// Heat haze (GROUND.md §9.3): the screen behind, bent by the C4 kit's distortion ring or by rising noise.
shader_type spatial;
render_mode blend_mix, unshaded, depth_draw_never, cull_disabled, shadows_disabled, skip_vertex_transform;

#define FX_HAZE
#include "res://assets/fx/shaders/fx_billboard.gdshaderinc"

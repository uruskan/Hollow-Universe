// Schematic galaxy-map symbols. Physical sky stars continue to use their own luminosity shaders.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, depth_test_disabled, cull_disabled, fog_disabled, shadows_disabled;

uniform float point_size = 7.0;
uniform float brightness = 1.15;

void vertex() {
    // UV.x is a map-symbol weight (Sol is larger); size is in screen pixels at every camera distance.
    POINT_SIZE = point_size * UV.x;
}

void fragment() {
    vec2 point = POINT_COORD * 2.0 - 1.0;
    float radius_squared = dot(point, point);
    if (radius_squared > 1.0) discard;
    float core = exp(-radius_squared * 14.0);
    float halo = exp(-radius_squared * 3.8) * 0.32;
    vec3 spectral_colour = max(COLOR.rgb, vec3(0.28));
    ALBEDO = spectral_colour * (core * 3.0 + halo) * brightness;
}

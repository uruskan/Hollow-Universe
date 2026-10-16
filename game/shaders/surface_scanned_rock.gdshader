// Scanned UV/PBR stone, with the same instance fade and planetary atmosphere as vegetation.
shader_type spatial;
render_mode cull_back, depth_draw_opaque, diffuse_burley, specular_schlick_ggx;
#include "res://shaders/surface_atmosphere.gdshaderinc"
#include "res://shaders/surface_clouds.gdshaderinc"
uniform sampler2D albedo_map : source_color, filter_linear_mipmap_anisotropic;
uniform sampler2D normal_map : hint_normal, filter_linear_mipmap_anisotropic;
uniform sampler2D roughness_map : filter_linear_mipmap_anisotropic;
uniform sampler2D ao_map : filter_linear_mipmap_anisotropic;
uniform vec4 albedo_factor : source_color = vec4(1.0);
uniform vec4 roughness_channel = vec4(0.0, 1.0, 0.0, 0.0);
uniform vec4 ao_channel = vec4(1.0, 0.0, 0.0, 0.0);
uniform float roughness_factor = 1.0;
uniform float normal_strength = 1.0;
uniform bool has_ao = false;
instance uniform vec4 lod_band = vec4(0.0, 0.0, 1e6, 2e6);
instance uniform float shrink = 0.0;
global uniform float hu_lod_clock;
varying vec3 v_world;
varying float v_fade_in;
varying float v_fade_out;
varying float v_tint;
void vertex() {
    float dist = length(MODEL_MATRIX[3].xyz - MAIN_CAM_INV_VIEW_MATRIX[3].xyz);
    float incoming = lod_band.y > lod_band.x ? smoothstep(lod_band.x, lod_band.y, dist) : 1.0;
    float outgoing = smoothstep(lod_band.z, lod_band.w, dist);
    float transition = INSTANCE_CUSTOM.z;
    v_fade_in = incoming * (transition > 0.0 ? clamp((hu_lod_clock-transition)/0.45, 0.0, 1.0) : 1.0);
    v_fade_out = (1.0-outgoing) * (transition < 0.0 ? 1.0-clamp((hu_lod_clock+transition)/0.45, 0.0, 1.0) : 1.0);
    // Stone fades at range; it never visibly contracts into a shrinking lump.
    v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    v_tint = 0.96 + 0.08 * INSTANCE_CUSTOM.y;
}
void fragment() {
    float dither = fract(52.9829189 * fract(dot(FRAGCOORD.xy, vec2(0.06711056, 0.00583715))));
    if (dither > v_fade_out || 1.0-dither > v_fade_in) discard;
    ALBEDO = texture(albedo_map, UV).rgb * albedo_factor.rgb * v_tint;
    ROUGHNESS = clamp(dot(texture(roughness_map, UV), roughness_channel) * roughness_factor, 0.32, 1.0);
    NORMAL_MAP = texture(normal_map, UV).rgb;
    NORMAL_MAP_DEPTH = normal_strength;
    AO = has_ao ? dot(texture(ao_map, UV), ao_channel) : 1.0;
    SPECULAR = 0.35;
    vec3 a = ALBEDO;
    vec4 fog;
    hu_aerial(v_world, CAMERA_POSITION_WORLD, 6, a, fog);
    ALBEDO = a;
    FOG = fog;

}
void light() {
    float cloud = LIGHT_IS_DIRECTIONAL ? hu_cloud_shadow(v_world, normalize((INV_VIEW_MATRIX * vec4(LIGHT, 0.0)).xyz)) : 1.0;
    DIFFUSE_LIGHT += LIGHT_COLOR * ATTENUATION * cloud * max(dot(NORMAL, LIGHT), 0.0) / HU_PI;
    SPECULAR_LIGHT += LIGHT_COLOR * ATTENUATION * cloud * hu_specular(NORMAL, VIEW, LIGHT, ROUGHNESS) * 0.5;
}

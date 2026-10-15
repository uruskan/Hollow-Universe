// Supplied plant UVs, opacity and colours. Cutouts stay in the opaque pass for coherent water refraction/depth.
shader_type spatial;
render_mode cull_disabled, depth_draw_opaque, skip_vertex_transform;
#include "res://shaders/surface_atmosphere.gdshaderinc"
#include "res://shaders/surface_clouds.gdshaderinc"
global uniform vec4 hu_wind;
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform sampler2D normal_texture : hint_normal, filter_linear_mipmap_anisotropic, repeat_disable;
uniform sampler2D roughness_texture : filter_linear_mipmap_anisotropic, repeat_disable;
uniform bool has_normal = false;
uniform bool has_roughness = false;
uniform bool billboard = false;
uniform float plant_height = 0.5;
uniform float far_radius = 42.0;
varying vec3 v_world;
varying vec3 v_normal;
varying vec3 v_tint;
varying float v_tip;
varying float v_cloud;

void vertex() {
    vec3 base = MODEL_MATRIX[3].xyz;
    vec3 up = normalize(MODEL_MATRIX[1].xyz);
    vec3 right = normalize(MODEL_MATRIX[0].xyz);
    vec3 forward = normalize(MODEL_MATRIX[2].xyz);
    if (billboard) {
        vec3 eye = CAMERA_POSITION_WORLD - base;
        right = normalize(cross(up, eye));
        forward = normalize(cross(right, up));
    }
    float scale = length(MODEL_MATRIX[1].xyz);
    vec3 world = base + (right * VERTEX.x + up * VERTEX.y + forward * VERTEX.z) * scale;
    v_tip = clamp(VERTEX.y / plant_height, 0.0, 1.0);
    float phase = INSTANCE_CUSTOM.a + dot(base, vec3(0.31, 0.17, 0.23));
    vec3 wind = hu_wind.xyz - up * dot(hu_wind.xyz, up);
    float gust = sin(TIME * 1.6 + phase) * 0.6 + sin(TIME * 0.7 + phase * 0.3) * 0.4;
    world += wind * hu_wind.w * gust * v_tip * v_tip * plant_height * 0.11;
    v_world = world;
    vec3 normal = right * NORMAL.x + up * NORMAL.y + forward * NORMAL.z;
    v_normal = normalize(normal + up * 0.7);
    NORMAL = normalize((VIEW_MATRIX * vec4(v_normal, 0.0)).xyz);
    vec3 tangent = right * TANGENT.x + up * TANGENT.y + forward * TANGENT.z;
    vec3 binormal = right * BINORMAL.x + up * BINORMAL.y + forward * BINORMAL.z;
    tangent = normalize(tangent - v_normal * dot(tangent, v_normal));
    TANGENT = normalize((VIEW_MATRIX * vec4(tangent, 0.0)).xyz);
    BINORMAL = normalize((VIEW_MATRIX * vec4(binormal, 0.0)).xyz);
    VERTEX = (VIEW_MATRIX * vec4(world, 1.0)).xyz;
    v_tint = INSTANCE_CUSTOM.rgb;
}

void fragment() {
    vec4 scan = texture(albedo_texture, UV);
    float fade = 1.0 - smoothstep(far_radius - 8.0, far_radius, length(v_world - CAMERA_POSITION_WORLD));
    if (scan.a < mix(1.0, 0.42, fade)) discard;
    ALBEDO = scan.rgb * v_tint;
    NORMAL = normalize((VIEW_MATRIX * vec4(v_normal, 0.0)).xyz);
    if (has_normal && !billboard) { NORMAL_MAP = texture(normal_texture, UV).rgb; NORMAL_MAP_DEPTH = 0.4; }
    ROUGHNESS = has_roughness ? clamp(texture(roughness_texture, UV).g, 0.65, 1.0) : 0.85;
    SPECULAR = 0.15;
    AO = mix(0.74, 1.0, v_tip);
    AO_LIGHT_AFFECT = 0.0;
    vec3 a = ALBEDO;
    vec4 fog;
    hu_aerial(v_world, CAMERA_POSITION_WORLD, 6, a, fog);
    ALBEDO = a;
    FOG = fog;
    v_cloud = hu_cloud_shadow(v_world);
}

void light() {
    float ndl = dot(NORMAL, LIGHT);
    float cloud = LIGHT_IS_DIRECTIONAL ? v_cloud : 1.0;
    vec3 light = LIGHT_COLOR * ATTENUATION * cloud / HU_PI;
    DIFFUSE_LIGHT += light * max(ndl, 0.0);
    // Modest transmitted light; the scan supplies the leaf colour instead of an invented yellow-green tint.
    DIFFUSE_LIGHT += light * 0.18 * clamp(-ndl * 0.6 + 0.35, 0.0, 1.0) * mix(0.5, 1.0, v_tip);
}

// Supplied plant UVs, opacity and colours. Cutouts stay in the opaque pass for coherent water refraction/depth.
shader_type spatial;
render_mode cull_disabled, depth_draw_opaque, skip_vertex_transform;
#include "res://shaders/surface_atmosphere.gdshaderinc"
#include "res://shaders/surface_clouds.gdshaderinc"
#include "res://shaders/surface_planet.gdshaderinc"
#include "res://shaders/surface_terrain_contact.gdshaderinc"
global uniform vec4 hu_wind;
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform sampler2D normal_texture : hint_normal, filter_linear_mipmap_anisotropic, repeat_disable;
uniform sampler2D roughness_texture : filter_linear_mipmap_anisotropic, repeat_disable;
uniform bool has_normal = false;
uniform bool has_roughness = false;
uniform bool billboard = false;
uniform float plant_height = 0.5;
uniform float far_radius = 42.0;
// Enabled only for a receipt-qualified whole-plant CUSTOM0 stream. Billboards need separate anchor review.
uniform bool authored_contact_enabled = false;
uniform vec3 authored_growth_axis = vec3(0.0, 1.0, 0.0);
uniform float authored_growth_length = 1.0;
// The qualified path uses the same dielectric F0 and reflection weight for direct light and the environment.
uniform float authored_reflection_weight : hint_range(0.0, 1.0) = 0.12;
varying vec3 v_world;
varying vec3 v_normal;
varying vec3 v_tint;
varying float v_tip;
varying vec3 v_tangent;
varying vec3 v_binormal;
varying float v_supported;

void vertex() {
    v_supported = 1.0;
    vec3 base = MODEL_MATRIX[3].xyz;
    vec3 up = normalize(MODEL_MATRIX[1].xyz);
    vec3 right = normalize(MODEL_MATRIX[0].xyz);
    vec3 forward = normalize(MODEL_MATRIX[2].xyz);
    if (billboard && !authored_contact_enabled) {
        vec3 eye = CAMERA_POSITION_WORLD - base;
        right = normalize(cross(up, eye));
        forward = normalize(cross(right, up));
    }
    float scale = length(MODEL_MATRIX[1].xyz);
    if (authored_contact_enabled) {
        vec3 root = CUSTOM0.xyz;
        vec3 root_hint = base + (right * root.x + up * root.y + forward * root.z) * scale;
        vec3 contact = vec3(0.0), support_normal = up;
        bool supported = terrain_contact_enabled && !billboard && scale > 0.0 && authored_growth_length > 0.0;
        if (supported) supported = hu_drawn_contact_at(root_hint, CAMERA_POSITION_WORLD, contact, support_normal);
        v_supported = supported ? 1.0 : 0.0;
        // Preserve canonical up and yaw. One reviewed logical root moves the complete plant together;
        // this does not certify that wide low fronds clear every point of uneven terrain.
        vec3 relative = VERTEX - root;
        vec3 world = contact + (right * relative.x + up * relative.y + forward * relative.z) * scale;
        v_tip = clamp(CUSTOM0.w, 0.0, 1.0);
        // INSTANCE_CUSTOM.a must be a stable plant phase, independent of the render origin.
        float phase = INSTANCE_CUSTOM.a;
        float gust = sin(TIME * 1.6 + phase) * 0.6 + sin(TIME * 0.7 + phase * 0.3) * 0.4;
        vec3 wind = hu_wind.xyz - up * dot(hu_wind.xyz, up);
        vec3 displacement = wind * hu_wind.w * gust * plant_height * scale * 0.11;
        world += displacement * v_tip * v_tip;
        vec3 normal = normalize(right * NORMAL.x + up * NORMAL.y + forward * NORMAL.z);
        vec3 tangent = right * TANGENT.x + up * TANGENT.y + forward * TANGENT.z;
        vec3 binormal = right * BINORMAL.x + up * BINORMAL.y + forward * BINORMAL.z;
        vec3 axis = normalize(right * authored_growth_axis.x + up * authored_growth_axis.y + forward * authored_growth_axis.z);
        // For p' = p + displacement*w(p)^2, J = I + displacement outer grad(w^2).
        // Apply its exact inverse transpose to artist normals and J to both UV basis vectors.
        vec3 gradient = vec3(0.0);
        if (v_tip > 0.0 && v_tip < 1.0) gradient = axis * (2.0 * v_tip / max(authored_growth_length * scale, 0.0001));
        float determinant = 1.0 + dot(gradient, displacement);
        // This safety decision is constant for the whole plant, including clamped tips and roots.
        float minimum_determinant = 1.0 + min(0.0, dot(axis * (2.0 / max(authored_growth_length * scale, 0.0001)), displacement));
        if (minimum_determinant < 0.05) {
            v_supported = 0.0;
        } else {
            normal = normalize(normal - gradient * (dot(displacement, normal) / determinant));
            tangent += displacement * dot(gradient, tangent);
            binormal += displacement * dot(gradient, binormal);
        }
        tangent = normalize(tangent - normal * dot(tangent, normal));
        binormal = normalize(binormal - normal * dot(binormal, normal) - tangent * dot(binormal, tangent));
        v_world = world;
        v_normal = normal;
        v_tangent = tangent;
        v_binormal = binormal;
        NORMAL = normalize((VIEW_MATRIX * vec4(normal, 0.0)).xyz);
        TANGENT = normalize((VIEW_MATRIX * vec4(tangent, 0.0)).xyz);
        BINORMAL = normalize((VIEW_MATRIX * vec4(binormal, 0.0)).xyz);
        VERTEX = v_supported > 0.5 ? (VIEW_MATRIX * vec4(world, 1.0)).xyz : vec3(0.0);
        v_tint = INSTANCE_CUSTOM.rgb;
    } else {
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
    v_tangent = tangent;
    v_binormal = binormal;
    VERTEX = (VIEW_MATRIX * vec4(world, 1.0)).xyz;
    v_tint = INSTANCE_CUSTOM.rgb;
    }
}

void fragment() {
    if (authored_contact_enabled && v_supported < 0.5) discard;
    vec4 scan = texture(albedo_texture, UV);
    float fade = 1.0 - smoothstep(far_radius - 8.0, far_radius, length(v_world - CAMERA_POSITION_WORLD));
    if (scan.a < mix(1.0, 0.42, fade)) discard;
    ALBEDO = scan.rgb * v_tint;
    NORMAL = normalize((VIEW_MATRIX * vec4(v_normal, 0.0)).xyz);
    if (authored_contact_enabled) {
        float facing = FRONT_FACING ? 1.0 : -1.0;
        NORMAL *= facing;
        TANGENT = normalize((VIEW_MATRIX * vec4(v_tangent, 0.0)).xyz);
        BINORMAL = normalize((VIEW_MATRIX * vec4(v_binormal * facing, 0.0)).xyz);
    }
    if (has_normal && !billboard) { NORMAL_MAP = texture(normal_texture, UV).rgb; NORMAL_MAP_DEPTH = 0.4; }
    ROUGHNESS = has_roughness ? clamp(texture(roughness_texture, UV).g, 0.65, 1.0) : 0.85;
    SPECULAR = 0.15;
    if (authored_contact_enabled) {
        SPECULAR = 0.5;
        RADIANCE = vec4(vec3(0.0), 1.0 - clamp(authored_reflection_weight, 0.0, 1.0));
    }
    AO = mix(0.74, 1.0, v_tip);
    AO_LIGHT_AFFECT = 0.0;
    vec3 a = ALBEDO;
    vec4 fog;
    hu_aerial(v_world, CAMERA_POSITION_WORLD, 6, a, fog);
    ALBEDO = a;
    FOG = fog;

}

void light() {
    float ndl = dot(NORMAL, LIGHT);
    float cloud = LIGHT_IS_DIRECTIONAL ? hu_cloud_shadow(v_world, normalize((INV_VIEW_MATRIX * vec4(LIGHT, 0.0)).xyz)) : 1.0;
    vec3 light = LIGHT_COLOR * ATTENUATION * cloud / HU_PI;
    DIFFUSE_LIGHT += light * max(ndl, 0.0);
    if (authored_contact_enabled && !billboard) {
        SPECULAR_LIGHT += LIGHT_COLOR * ATTENUATION * cloud * hu_specular(NORMAL, VIEW, LIGHT, ROUGHNESS)
            * clamp(authored_reflection_weight, 0.0, 1.0);
    }
    // Modest transmitted light; the scan supplies the leaf colour instead of an invented yellow-green tint.
    DIFFUSE_LIGHT += light * 0.18 * clamp(-ndl * 0.6 + 0.35, 0.0, 1.0) * mix(0.5, 1.0, v_tip);
}

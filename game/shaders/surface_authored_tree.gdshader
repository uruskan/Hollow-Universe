// Owned, authored tree UVs and native PBR. Same spherical-world instance/fall
// transforms, complementary LOD fades and tool shake as the shared vegetation.
shader_type spatial;
render_mode cull_disabled, depth_draw_opaque, diffuse_burley, specular_schlick_ggx;
#include "res://shaders/surface_atmosphere.gdshaderinc"
#include "res://shaders/surface_clouds.gdshaderinc"

global uniform vec3 hu_texture_offset;
global uniform vec4 hu_wind;
global uniform vec4 hu_shake_at;
global uniform vec4 hu_shake_dir;
global uniform float hu_lod_clock;
uniform int mode = 0; // 0 native alpha-cutout leaves, 1 native opaque bark
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D normal_texture : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D roughness_texture : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D ao_texture : filter_linear_mipmap_anisotropic, repeat_enable;
uniform vec4 albedo_factor : source_color = vec4(1.0);
uniform vec4 roughness_channel = vec4(0.0,1.0,0.0,0.0);
uniform vec4 ao_channel = vec4(1.0,0.0,0.0,0.0);
uniform bool has_normal = true;
uniform bool has_roughness = false;
uniform bool has_ao = false;
uniform float normal_strength = 1.0;
uniform float roughness_factor = 0.88;
uniform float alpha_cutoff = 0.42;
uniform float tree_height = 12.0;
instance uniform vec4 lod_band = vec4(0.0,0.0,1e6,2e6);
instance uniform float shrink = 0.0;
varying vec3 v_world;
varying vec3 v_tint;
varying float v_fade_in;
varying float v_fade_out;

void vertex() {
    vec3 origin = MODEL_MATRIX[3].xyz;
    float dist = length(origin - MAIN_CAM_INV_VIEW_MATRIX[3].xyz);
    float incoming = lod_band.y > lod_band.x ? smoothstep(lod_band.x,lod_band.y,dist) : 1.0;
    float outgoing = smoothstep(lod_band.z,lod_band.w,dist);
    float transition = INSTANCE_CUSTOM.z;
    v_fade_in = incoming * (transition > 0.0 ? clamp((hu_lod_clock-transition)/0.45,0.0,1.0) : 1.0);
    v_fade_out = (1.0-outgoing) * (transition < 0.0 ? 1.0-clamp((hu_lod_clock+transition)/0.45,0.0,1.0) : 1.0);
    if (v_fade_in*v_fade_out <= 0.0) {
        VERTEX = vec3(0.0);
    } else {
        mat3 m = mat3(MODEL_MATRIX);
        float scale2 = max(dot(m[0],m[0]),1e-6);
        vec3 up = normalize(m[1]);
        vec3 wind = (hu_wind.xyz-up*dot(hu_wind.xyz,up))*hu_wind.w;
        vec3 local_wind = transpose(m)*wind/scale2;
        vec3 base = origin+hu_texture_offset;
        float phase = dot(base,vec3(0.061,0.047,0.053));
        float gust = 0.55+0.45*sin(TIME*0.37-dot(base,hu_wind.xyz)*0.035);
        float bend = pow(smoothstep(0.3,tree_height,VERTEX.y),1.6);
        VERTEX += local_wind*bend*0.22*(0.65+0.35*sin(TIME*1.15+phase))*gust;
        if (mode == 0) {
            VERTEX += NORMAL*(bend*0.025*sin(TIME*7.3+phase*4.0+dot(VERTEX,vec3(2.1,1.7,2.3)))*gust*hu_wind.w/sqrt(scale2));
        }
        if (hu_shake_at.w > 0.0 && length(origin-hu_shake_at.xyz) < 0.4) {
            float age = hu_shake_dir.w;
            float sway = hu_shake_at.w*exp(-age*5.5)*sin(age*24.0);
            VERTEX += transpose(m)*hu_shake_dir.xyz/scale2*bend*sway;
        }
    }
    v_world = (MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;
    // A restrained variation around source reflectance, no painted green tint.
    float k = 0.94+0.12*INSTANCE_CUSTOM.x;
    v_tint = vec3(k);
}

void fragment() {
    float dither = fract(52.9829189*fract(dot(FRAGCOORD.xy,vec2(0.06711056,0.00583715))));
    if (dither > v_fade_out || 1.0-dither > v_fade_in) discard;
    vec4 tex = texture(albedo_texture,UV)*albedo_factor;
    ALBEDO = tex.rgb*v_tint;
    // Stay in the opaque colour/depth pass used by refracting water. Writing
    // ALPHA/antialiasing built-ins schedules transparent colour separately from
    // its depth even with scissor; discard preserves textured leaf silhouettes.
    if (mode == 0 && tex.a < alpha_cutoff) discard;
    ROUGHNESS = clamp((has_roughness ? dot(texture(roughness_texture,UV),roughness_channel) : 1.0)*roughness_factor,0.45,1.0);
    if (has_normal) {
        NORMAL_MAP = texture(normal_texture,UV).rgb;
        NORMAL_MAP_DEPTH = normal_strength;
    }
    AO = has_ao ? dot(texture(ao_texture,UV),ao_channel) : 1.0;
    // F0 .04 matches hu_specular. Weight direct and environment reflection
    // consistently; roughness remains the supplier's native texture/channel.
    SPECULAR = 0.5;
    float reflection_weight = mode == 0 ? 0.12 : 0.3;
    RADIANCE = vec4(vec3(0.0), 1.0-reflection_weight);
    vec3 a = ALBEDO;
    vec4 fog;
    hu_aerial(v_world,CAMERA_POSITION_WORLD,6,a,fog);
    ALBEDO = a;
    FOG = fog;
}

void light() {
    float cloud = LIGHT_IS_DIRECTIONAL ? hu_cloud_shadow(v_world,normalize((INV_VIEW_MATRIX*vec4(LIGHT,0.0)).xyz)) : 1.0;
    float ndl = dot(NORMAL,LIGHT);
    vec3 light = LIGHT_COLOR*ATTENUATION*cloud/HU_PI;
    DIFFUSE_LIGHT += light*max(ndl,0.0);
    if (mode == 0) {
        // Small uncoloured transmission keeps native leaf hues and leaf normals.
        float back = pow(max(dot(VIEW,LIGHT),0.0),4.0);
        DIFFUSE_LIGHT += light*(0.22*back+0.10*clamp(-ndl,0.0,1.0));
    }
    float reflection_weight = mode == 0 ? 0.12 : 0.3;
    SPECULAR_LIGHT += LIGHT_COLOR*ATTENUATION*cloud*hu_specular(NORMAL,VIEW,LIGHT,ROUGHNESS)*reflection_weight;
}

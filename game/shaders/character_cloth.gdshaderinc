// Body of the cloth shaders; see character_cloth.gdshader and character_cloth_viewmodel.gdshader.
#include "res://shaders/character_common.gdshaderinc"
uniform sampler2D albedo_texture : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform vec3 tint : source_color = vec3(1.0);
uniform sampler2D normal_texture : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float normal_strength = 1.0;
// R ambient occlusion, G roughness, B metallic (only read when use_orm is set).
uniform sampler2D orm_texture : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_orm = false;
uniform float roughness : hint_range(0.0, 1.0) = 0.9;
uniform float metallic : hint_range(0.0, 1.0) = 0.0;
uniform float specular_level : hint_range(0.0, 1.0) = 0.35;
uniform sampler2D detail_normal : hint_normal, filter_linear_mipmap, repeat_enable;
uniform float detail_tiling = 40.0;
uniform float detail_strength = 0.6;
uniform float inside_shade : hint_range(0.0, 1.0) = 0.4;


void fragment() {
	vec3 albedo = texture(albedo_texture, UV).rgb * tint;
	vec3 n = texture(normal_texture, UV).xyz * 2.0 - 1.0;
	vec3 d = texture(detail_normal, UV * detail_tiling).xyz * 2.0 - 1.0;
	n.xy = n.xy * normal_strength + d.xy * detail_strength;
	NORMAL_MAP = normalize(n) * 0.5 + 0.5;
	float ao = 1.0;
	float r = roughness;
	float m = metallic;
	if (use_orm) {
		vec3 orm = texture(orm_texture, UV).rgb;
		ao = orm.r;
		r = orm.g;
		m = orm.b;
	}
	ALBEDO = FRONT_FACING ? albedo : albedo * inside_shade;
	AO = ao;
	AO_LIGHT_AFFECT = 0.35;
	ROUGHNESS = r;
	METALLIC = m;
	SPECULAR = specular_level;
}

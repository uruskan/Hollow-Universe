// Body of the skin shaders; see character_skin.gdshader and character_skin_viewmodel.gdshader.
#include "res://shaders/character_common.gdshaderinc"
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform vec3 tint : source_color = vec3(1.0);
uniform sampler2D detail_normal : hint_normal, filter_linear_mipmap, repeat_enable;
uniform float detail_tiling = 22.0;
uniform float detail_strength = 0.45;
uniform float roughness : hint_range(0.0, 1.0) = 0.56;
uniform float specular_level : hint_range(0.0, 1.0) = 0.38;
uniform float subsurface : hint_range(0.0, 1.0) = 0.32;
uniform vec3 transmittance : source_color = vec3(0.85, 0.32, 0.22);


void fragment() {
	vec3 albedo = texture(albedo_texture, UV).rgb * tint;
	ALBEDO = albedo;
	NORMAL_MAP = texture(detail_normal, UV * detail_tiling).xyz;
	NORMAL_MAP_DEPTH = detail_strength;
	// Slightly glossier where the skin is darker red (lips, eyelids) in the texture.
	float redness = clamp((albedo.r - albedo.g) * 2.2 - 0.25, 0.0, 1.0);
	ROUGHNESS = roughness - redness * 0.12;
	SPECULAR = specular_level;
	SSS_STRENGTH = subsurface;
	SSS_TRANSMITTANCE_COLOR = vec4(transmittance, 1.0);
	SSS_TRANSMITTANCE_DEPTH = 0.08;
	SSS_TRANSMITTANCE_BOOST = 0.12;
}

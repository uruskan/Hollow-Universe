// Body of the hair and brow shaders (GROUND.md §4.8, looks): MakeHuman's hair cards, their texture turned into
// brightness about its mean (art/characters/build_body.py), coloured per player by an instance uniform, so every
// hair style takes any hair colour without a material per player.
uniform sampler2D albedo_texture : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D normal_texture : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_normal = false;
uniform float normal_scale = 0.6;
uniform float roughness : hint_range(0.0, 1.0) = 0.55;
uniform float specular_level : hint_range(0.0, 1.0) = 0.3;
instance uniform vec3 hair_colour : source_color = vec3(0.075, 0.052, 0.036);

void fragment() {
	vec4 t = texture(albedo_texture, UV);
	// 0.5 in the texture is the hair's mean brightness.
	ALBEDO = hair_colour * t.r * 2.0;
	ALPHA = t.a;
#ifdef SCISSOR
	ALPHA_SCISSOR_THRESHOLD = 0.3;
	ALPHA_ANTIALIASING_EDGE = 0.3;
	ALPHA_TEXTURE_COORDINATE = UV * vec2(textureSize(albedo_texture, 0));
#endif
	if (use_normal) {
		NORMAL_MAP = texture(normal_texture, UV).xyz;
		NORMAL_MAP_DEPTH = normal_scale;
	}
	ROUGHNESS = roughness;
	SPECULAR = specular_level;
}

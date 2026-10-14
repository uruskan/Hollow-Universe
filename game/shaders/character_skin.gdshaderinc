// Body of the skin shaders; see character_skin.gdshader and character_skin_viewmodel.gdshader.
#include "res://shaders/character_common.gdshaderinc"
// Albedo of the light skin; its alpha scales the roughness (lower on the nails, so they shine).
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
// The dark skin, blended in by the player's skin tone (GROUND.md §4.8, looks).
uniform sampler2D albedo_dark : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
// R the hair base painted on the scalp, G stubble, B the base's strand brightness.
uniform sampler2D look_mask : hint_default_black, filter_linear_mipmap_anisotropic, repeat_disable;
uniform bool use_look = false;
uniform vec3 tint : source_color = vec3(1.0);
// The skin's own relief (creases, knuckles, nails), made from its texture (art/characters/skin_maps.py).
uniform sampler2D normal_texture : hint_normal, filter_linear_mipmap_anisotropic, repeat_disable;
uniform float normal_strength = 1.0;
uniform sampler2D detail_normal : hint_normal, filter_linear_mipmap, repeat_enable;
uniform float detail_tiling = 22.0;
uniform float detail_strength = 0.45;
uniform float roughness : hint_range(0.0, 1.0) = 0.56;
uniform float specular_level : hint_range(0.0, 1.0) = 0.38;
uniform float subsurface : hint_range(0.0, 1.0) = 0.32;
uniform vec3 transmittance : source_color = vec3(0.85, 0.32, 0.22);

// The player's look (Looks.cs): x skin tone (0 the light texture … 1 the dark one), y stubble, z the scalp's hair
// base; a tint on the skin (linear) and the hair colour, which the stubble and the hair base take.
instance uniform vec4 skin_look = vec4(0.0, 0.0, 1.0, 0.0);
instance uniform vec3 skin_tint = vec3(1.0);
instance uniform vec3 hair_colour : source_color = vec3(0.075, 0.052, 0.036);


void fragment() {
	vec4 tex = texture(albedo_texture, UV);
	vec3 albedo = tex.rgb;
	float tone = 0.0;
	if (use_look) {
		tone = skin_look.x;
		// The textures blend in linear light, where the light skin outweighs the dark: the tone is spread so that
		// equal steps of it look like equal steps from light to dark.
		float blend = 1.0 - pow(1.0 - clamp(tone, 0.0, 1.0), 1.6);
		albedo = mix(albedo, texture(albedo_dark, UV).rgb, blend) * skin_tint;
		vec3 m = texture(look_mask, UV).rgb;
		// A few days' stubble: short hairs over the skin, in the hair's colour.
		albedo = mix(albedo, hair_colour * 0.8, clamp(m.g * skin_look.y, 0.0, 1.0) * 0.45);
		// The hair base on the scalp, its strands a little lighter and darker.
		albedo = mix(albedo, hair_colour * (0.68 + 0.3 * m.b), clamp(m.r * skin_look.z, 0.0, 1.0) * 0.92);
	}
	albedo *= tint;
	ALBEDO = albedo;
	vec3 n = texture(normal_texture, UV).xyz * 2.0 - 1.0;
	vec3 d = texture(detail_normal, UV * detail_tiling).xyz * 2.0 - 1.0;
	n.xy = n.xy * normal_strength + d.xy * detail_strength;
	NORMAL_MAP = normalize(n) * 0.5 + 0.5;
	// Slightly glossier where the skin is darker red (lips, eyelids) in the texture, and darker skin a little more.
	float redness = clamp((tex.r - tex.g) * 2.2 - 0.25, 0.0, 1.0);
	ROUGHNESS = (roughness - redness * 0.12 - tone * 0.05) * tex.a;
	SPECULAR = specular_level;
#ifndef VIEWMODEL
	// Screen-space scattering blurs whatever is in front of it; the first-person arms are pulled towards the near
	// plane, so there it would smear them pink. They go without (light through thin skin is lost, detail is kept).
	// Melanin keeps light from travelling as far, so darker skin scatters less.
	SSS_STRENGTH = subsurface * (1.0 - 0.45 * tone);
	SSS_TRANSMITTANCE_COLOR = vec4(transmittance * (1.0 - 0.6 * tone), 1.0);
	SSS_TRANSMITTANCE_DEPTH = 0.08;
	SSS_TRANSMITTANCE_BOOST = 0.12;
#endif
}

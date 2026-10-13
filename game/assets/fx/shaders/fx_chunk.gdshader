// Debris (GROUND.md §9.3): lit meshes of chips, splinters and shards. One mesh holds several shapes, each with its index
// in UV2.x; a particle draws the one its random number picks and collapses the rest. UV2.y marks the second material of
// a mesh (bark on wood, the rounder dirt clods). The particle's COLOR tints it; it shrinks away at the end of its life.
shader_type spatial;
render_mode blend_mix, cull_back, diffuse_burley, specular_schlick_ggx;

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_enable, hint_default_white;
uniform float shapes = 8.0;
uniform vec3 first_color = vec3(1.0);
uniform vec3 second_color = vec3(1.0);
uniform float metallic = 0.0;
uniform float roughness = 0.85;
uniform float texture_strength = 1.0;

varying float v_second;

void vertex() {
	float shape = floor(INSTANCE_CUSTOM.z * shapes);
	if (abs(UV2.x - 0.5 - shape) > 0.25) {
		VERTEX = vec3(0.0);
	}
	float life = max(INSTANCE_CUSTOM.x, 0.001);
	VERTEX *= 1.0 - smoothstep(life * 0.82, life, INSTANCE_CUSTOM.y);
	v_second = UV2.y;
}

void fragment() {
	vec3 base = mix(first_color, second_color, step(0.5, v_second));
	vec3 t = mix(vec3(1.0), texture(albedo_tex, UV).rgb * 2.2, texture_strength);
	ALBEDO = base * t * COLOR.rgb;
	METALLIC = metallic;
	ROUGHNESS = roughness;
}

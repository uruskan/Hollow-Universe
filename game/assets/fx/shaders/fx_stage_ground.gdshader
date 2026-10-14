// The effects viewer's ground patches (GROUND.md §9.3): one layer of the planet's ground textures (the surface's
// texture arrays) in world space; the grass layer takes its colour from `tint` (its texture only gives the detail).
shader_type spatial;

uniform sampler2DArray albedo_array : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray normal_array : filter_linear_mipmap_anisotropic, repeat_enable;
uniform float layer = 2.0;
uniform float tiling = 0.45;
uniform vec3 tint = vec3(1.0);
uniform bool luminance_only = false;
uniform float roughness = 0.95;

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec3 uvw = vec3(world_pos.xz * tiling, layer);
	vec3 c = texture(albedo_array, uvw).rgb;
	if (luminance_only) {
		c = vec3(dot(c, vec3(0.2126, 0.7152, 0.0722)) * 2.2);
	}
	ALBEDO = c * tint;
	vec2 t = texture(normal_array, uvw).rg * 2.0 - 1.0;
	NORMAL_MAP = vec3(t * 0.5 + 0.5, 1.0);
	ROUGHNESS = roughness;
}

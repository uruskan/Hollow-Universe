// Rings lying in a plane (GROUND.md §9.3): the blast's pressure ring on the ground, ripples on water. The particle's
// basis Y is the plane's normal; the ring grows from its size to size * INSTANCE_CUSTOM.w over its life.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, specular_disabled, skip_vertex_transform;

uniform sampler2D tex : source_color, filter_linear_mipmap, repeat_disable;
uniform float growth_power = 0.4;
uniform float fade_out = 0.8;
uniform float lift = 0.02;   // metres off the surface

varying float v_alpha;

void vertex() {
	float life = max(INSTANCE_CUSTOM.x, 0.001);
	float age = clamp(INSTANCE_CUSTOM.y / life, 0.0, 1.0);
	float growth = INSTANCE_CUSTOM.w > 0.0 ? INSTANCE_CUSTOM.w : 1.0;
	vec3 n = normalize(MODEL_MATRIX[1].xyz);
	vec3 t = normalize(MODEL_MATRIX[0].xyz);
	vec3 b = cross(n, t);
	float size = length(MODEL_MATRIX[0].xyz) * mix(1.0, growth, pow(age, growth_power));
	vec3 p = MODEL_MATRIX[3].xyz + n * lift + (t * VERTEX.x + b * VERTEX.y) * size;
	VERTEX = (VIEW_MATRIX * vec4(p, 1.0)).xyz;
	NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);
	v_alpha = (1.0 - smoothstep(1.0 - fade_out, 1.0, age)) * smoothstep(0.0, 0.05, age) * COLOR.a;
}

void fragment() {
	vec4 t = texture(tex, UV);
	ALBEDO = COLOR.rgb;
	ALPHA = clamp(t.a * v_alpha, 0.0, 1.0);
	ROUGHNESS = 0.8;
}

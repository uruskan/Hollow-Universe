// Streaks along the velocity (GROUND.md §9.3): sparks (FX_ADD) and droplets of blood, water or sand (FX_LIT).
// fx_process.gdshader (align 1) keeps the streak in MODEL_MATRIX[1] (its direction times its length, the head at the
// particle's position); INSTANCE_CUSTOM.w is its width. Never thinner than min_pixels on screen: thinner streaks are
// drawn that wide and dimmer instead, so far sparks don't shimmer.

uniform sampler2D tex : source_color, filter_linear_mipmap, repeat_disable;
uniform float min_pixels = 1.5;
uniform float shrink = 0.6;            // share of the width lost over the life
uniform float fade_out = 0.5;          // share of the life
uniform float intensity = 1.0;         // additive: HDR multiplier
uniform vec3 cool_color = vec3(1.0);   // additive: the colour it cools to (times COLOR)
uniform float roughness = 0.4;         // lit

varying float v_alpha;
varying float v_age;

void vertex() {
	float life = max(INSTANCE_CUSTOM.x, 0.001);
	float age = clamp(INSTANCE_CUSTOM.y / life, 0.0, 1.0);
	v_age = age;
	vec3 head = MODEL_MATRIX[3].xyz;
	vec3 axis = MODEL_MATRIX[1].xyz;
	float width = max(INSTANCE_CUSTOM.w, 0.0005) * (1.0 - shrink * age);
	vec3 p = head + axis * (VERTEX.y - 0.5);
	vec3 to_camera = normalize(CAMERA_POSITION_WORLD - p);
	vec3 side = cross(axis, to_camera);
	float l = length(side);
	side = l > 1e-6 ? side / l : normalize(cross(vec3(0.0, 1.0, 0.0), to_camera) + vec3(1e-4, 0.0, 0.0));
	vec4 clip = PROJECTION_MATRIX * (VIEW_MATRIX * vec4(p, 1.0));
	float pixel = abs(clip.w) * 2.0 / (VIEWPORT_SIZE.y * abs(PROJECTION_MATRIX[1][1]));
	float w = max(width, pixel * min_pixels);
	p += side * VERTEX.x * w;
	VERTEX = (VIEW_MATRIX * vec4(p, 1.0)).xyz;
	NORMAL = normalize((VIEW_MATRIX * vec4(to_camera, 0.0)).xyz);
	v_alpha = (1.0 - smoothstep(1.0 - fade_out, 1.0, age)) * COLOR.a * (width / w);
}

void fragment() {
	vec4 t = texture(tex, UV);
#ifdef FX_ADD
	vec3 colour = mix(COLOR.rgb, COLOR.rgb * cool_color, smoothstep(0.0, 1.0, v_age));
	ALBEDO = colour * intensity;
	ALPHA = clamp(t.a * v_alpha, 0.0, 1.0);
#else
	ALBEDO = COLOR.rgb;
	ALPHA = clamp(t.a * v_alpha, 0.0, 1.0);
	ROUGHNESS = roughness;
	SPECULAR = 0.4;
#endif
}

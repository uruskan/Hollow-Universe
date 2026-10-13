// Tracers and trails (GROUND.md §9.3), one MultiMesh for all of them. Each instance's transform carries its data:
// origin the head, basis X the vector from the head to the tail, basis Y (width m, brightness, opacity); its colour
// is the tint. The card faces the camera across the line, never thinner than min_pixels (thinner: dimmer instead).
shader_type spatial;
render_mode blend_add, unshaded, depth_draw_never, cull_disabled, shadows_disabled, skip_vertex_transform;

uniform float min_pixels = 1.4;

varying float v_along;
varying float v_strength;

void vertex() {
	vec3 head = MODEL_MATRIX[3].xyz;
	vec3 tail = head + MODEL_MATRIX[0].xyz;
	float width = MODEL_MATRIX[1].x;
	v_along = VERTEX.x;
	vec3 p = mix(tail, head, VERTEX.x);
	vec3 axis = head - tail;
	vec3 to_camera = normalize(CAMERA_POSITION_WORLD - p);
	vec3 side = cross(axis, to_camera);
	float l = length(side);
	side = l > 1e-6 ? side / l : vec3(0.0, 1.0, 0.0);
	vec4 clip = PROJECTION_MATRIX * (VIEW_MATRIX * vec4(p, 1.0));
	float pixel = abs(clip.w) * 2.0 / (VIEWPORT_SIZE.y * abs(PROJECTION_MATRIX[1][1]));
	float w = max(width, pixel * min_pixels);
	p += side * VERTEX.y * w * 0.5;
	VERTEX = (VIEW_MATRIX * vec4(p, 1.0)).xyz;
	v_strength = MODEL_MATRIX[1].y * (width / w) * MODEL_MATRIX[1].z;
}

void fragment() {
	float across = 1.0 - abs(UV.y * 2.0 - 1.0);
	float core = smoothstep(0.0, 0.9, across);
	float along = pow(clamp(v_along, 0.0, 1.0), 1.6);
	ALBEDO = COLOR.rgb * v_strength * (0.6 + 0.4 * core);
	ALPHA = clamp(core * along, 0.0, 1.0);
}

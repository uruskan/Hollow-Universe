shader_type spatial;
// Ships' thruster plumes and lamp glows (G6, GROUND.md §6.7). A plume is one quad turned about its axis (the node's +Y,
// scaled to the plume's length; +X scaled to its width at the nozzle) to face the camera; a glow (mode 1) is a quad
// facing the camera, a nozzle seen end-on or a lamp. Light only adds: nothing here is lit, shadowed or fogged.
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, fog_disabled, skip_vertex_transform, shadows_disabled;

// What the thruster gives (0 idle, 1 full, above 1 boosting), or a lamp's brightness.
instance uniform float intensity = 0.0;
// The gas's colour away from the hot core (linear).
instance uniform vec4 tint = vec4(1.0, 0.55, 0.22, 1.0);
// 0 a plume along the node's +Y, 1 a glow facing the camera.
uniform int mode = 0;
// Brightness per unit of intensity: the host divides it by the camera's exposure, so plumes read alike by day and night.
uniform float energy = 1.0;

varying float v_along;

void vertex() {
	vec3 origin = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	vec3 camera = INV_VIEW_MATRIX[3].xyz;
	float width = length((MODEL_MATRIX * vec4(1.0, 0.0, 0.0, 0.0)).xyz);
	vec3 world;
	if (mode == 0) {
		vec3 axis_scaled = (MODEL_MATRIX * vec4(0.0, 1.0, 0.0, 0.0)).xyz;
		float len = length(axis_scaled);
		vec3 axis = axis_scaled / max(len, 1e-5);
		vec3 side = cross(axis, camera - origin);
		if (length(side) < 1e-5) {
			side = cross(axis, abs(axis.y) < 0.9 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0));
		}
		side = normalize(side);
		// The quad's y runs -0.5 (nozzle) to 0.5 (tail); the gas spreads as it goes.
		float along = VERTEX.y + 0.5;
		v_along = along;
		world = origin + axis * along * len + side * VERTEX.x * width * (1.0 + along * 1.6);
	} else {
		v_along = 0.0;
		vec3 right = INV_VIEW_MATRIX[0].xyz;
		vec3 up = INV_VIEW_MATRIX[1].xyz;
		world = origin + (right * VERTEX.x + up * VERTEX.y) * width;
	}
	VERTEX = (VIEW_MATRIX * vec4(world, 1.0)).xyz;
	NORMAL = vec3(0.0, 0.0, 1.0);
}

void fragment() {
	vec3 hot = vec3(1.0, 0.9, 0.74);
	if (mode == 0) {
		float across = abs(UV.x - 0.5) * 2.0;
		float a = clamp(v_along, 0.0, 1.0);
		float body = exp(-across * across * 3.0) * pow(1.0 - a, 1.3);
		float core = exp(-across * across * 14.0) * pow(1.0 - a, 3.0);
		float edge = smoothstep(1.0, 0.75, across) * smoothstep(0.0, 0.04, a);
		float flicker = 0.9 + 0.1 * sin(TIME * 61.0 + a * 23.0) * sin(TIME * 37.0 + a * 7.0);
		vec3 colour = mix(tint.rgb, hot, clamp(core * 1.5, 0.0, 1.0));
		ALBEDO = colour * (body * 0.55 + core * 1.6) * edge * intensity * energy * flicker;
	} else {
		float r = length(UV - vec2(0.5)) * 2.0;
		float glow = exp(-r * r * 5.0) * smoothstep(1.0, 0.55, r);
		float centre = exp(-r * r * 40.0);
		ALBEDO = mix(tint.rgb, hot, centre) * (glow * 0.6 + centre * 1.8) * intensity * energy;
	}
}

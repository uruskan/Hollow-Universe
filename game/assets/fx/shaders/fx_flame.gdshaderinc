// Flames (GROUND.md §9.3): campfire, furnace, torch, things burning. One mesh holds a few flame cards; all four corners
// of a card sit at its foot (VERTEX), UV is the corner, UV2 the card's width and height, COLOR.r its random number and
// COLOR.g its strength. The vertex shader stands each card up along world up and turns it to the camera; the fragment
// shader draws a tongue of fire from scrolling noise, hottest at the foot, breaking into licks as it rises.

uniform sampler2D noise_tex : repeat_enable, filter_linear_mipmap;
uniform float sway = 1.0;
instance uniform float fx_brightness = 3.0;
instance uniform float fx_speed = 1.0;
instance uniform float fx_strength = 1.0;   // the whole fire (fades it in and out)
#ifdef VIEWMODEL
uniform float viewmodel_fov : hint_range(30.0, 110.0) = 60.0;
uniform float viewmodel_depth : hint_range(0.01, 1.0) = 0.12;
#endif

varying float v_seed;
varying float v_power;

void vertex() {
	v_seed = COLOR.r;
	v_power = COLOR.g;
	vec3 foot = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec3 up = vec3(0.0, 1.0, 0.0);
	vec3 right = INV_VIEW_MATRIX[0].xyz;
	right = normalize(right - up * dot(right, up) + vec3(1e-5, 0.0, 0.0));
	vec3 p = foot + right * (UV.x - 0.5) * UV2.x + up * (1.0 - UV.y) * UV2.y;
	VERTEX = (VIEW_MATRIX * vec4(p, 1.0)).xyz;
	NORMAL = normalize((VIEW_MATRIX * vec4(cross(right, up), 0.0)).xyz);
#ifdef VIEWMODEL
	mat4 pm = PROJECTION_MATRIX;
	float f = 1.0 / tan(radians(viewmodel_fov) * 0.5);
	pm[0][0] = f * (pm[0][0] / abs(pm[1][1]));
	pm[1][1] = f * sign(pm[1][1]);
	POSITION = pm * vec4(VERTEX, 1.0);
	POSITION.z = mix(POSITION.w, POSITION.z, viewmodel_depth);
#endif
}

vec3 fire_ramp(float h) {
	vec3 c = mix(vec3(0.3, 0.02, 0.0), vec3(0.95, 0.24, 0.02), smoothstep(0.0, 0.3, h));
	c = mix(c, vec3(1.0, 0.56, 0.12), smoothstep(0.25, 0.55, h));
	c = mix(c, vec3(1.0, 0.84, 0.45), smoothstep(0.5, 0.8, h));
	return mix(c, vec3(1.0, 0.96, 0.86), smoothstep(0.8, 1.0, h));
}

void fragment() {
	vec2 uv = vec2(UV.x, 1.0 - UV.y);
	float t = TIME * fx_speed + v_seed * 31.0;
	float n1 = texture(noise_tex, vec2(uv.x * 0.55 + v_seed * 3.7, uv.y * 0.42 - t * 0.55)).r;
	float n2 = texture(noise_tex, vec2(uv.x * 1.1 - v_seed * 1.3, uv.y * 0.9 - t * 1.15)).g;
	float n3 = texture(noise_tex, vec2(uv.x * 2.3 + v_seed, uv.y * 1.9 - t * 2.1)).b;
	float wobble = ((n1 - 0.5) * 0.9 + (n2 - 0.5) * 0.45) * sway;
	float x = (uv.x - 0.5) * 2.0 + wobble * uv.y * 1.1;
	float y = uv.y;
	float width = 0.78 * pow(max(1.0 - y, 0.0), 0.55) * smoothstep(-0.08, 0.1, y);
	float body = smoothstep(width, width * 0.25, abs(x));
	float licks = smoothstep(0.35, 0.75, n2 * 0.6 + n3 * 0.4 + (1.0 - y) * 0.75);
	float cover = body * mix(1.0, licks, smoothstep(0.15, 0.6, y));
	float heat = cover * (1.05 - y * 0.8) * (0.7 + 0.5 * n1) * (1.0 - 0.45 * smoothstep(0.3, 1.0, abs(x) / max(width, 1e-3)));
	ALBEDO = fire_ramp(clamp(heat, 0.0, 1.0)) * fx_brightness * v_power * fx_strength;
	ALPHA = clamp(cover * smoothstep(0.0, 0.08, y), 0.0, 1.0);
}

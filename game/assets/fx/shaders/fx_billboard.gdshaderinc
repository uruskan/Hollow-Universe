// Camera-facing particles of the effects library (GROUND.md §9.3): dust, smoke, fire, flashes, embers and heat haze.
// Included by fx_add (additive, unshaded), fx_lit (alpha, lit) and fx_haze (screen distortion); FX_NORMAL_MAP adds the
// smoke flipbook's normals. The particle data comes from fx_process.gdshader:
//   MODEL_MATRIX   the particle's transform (world); its basis length is the size
//   INSTANCE_CUSTOM  x lifetime, y age, z random, w growth (the size at the end of life over the size at the start)
//   COLOR          tint and opacity

#ifdef FX_HAZE
uniform sampler2D tex : filter_linear_mipmap, repeat_disable;
#else
uniform sampler2D tex : source_color, filter_linear_mipmap, repeat_disable;
#endif
#ifdef FX_NORMAL_MAP
uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap, repeat_disable;
#endif
#ifdef FX_HAZE
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap, repeat_disable;
uniform sampler2D noise_tex : repeat_enable, filter_linear;
uniform float strength = 0.02;        // screen-space offset at full strength
uniform bool scroll = false;          // noise rising (fire) instead of the texture's ring (blasts)
#endif
uniform sampler2D depth_tex : hint_depth_texture, filter_linear, repeat_disable;

uniform vec2 frames = vec2(1.0, 1.0);        // atlas columns and rows
uniform bool animate = false;                // frames from frame_range.x to .y over the life
uniform vec2 frame_range = vec2(0.0, 0.0);   // otherwise one frame, picked by the particle's random number
uniform float frame_jitter = 0.0;            // animated: each particle starts up to this many frames on
uniform float anchor = 0.0;                  // 0 centred; 0.5 the particle sits at the bottom edge
uniform bool random_roll = true;
uniform bool random_flip = false;
uniform float spin = 0.0;                    // radians a second, either way
uniform float growth_power = 0.5;            // how the size eases to its end (lower: faster at first)
uniform float fade_in = 0.05;                // share of the life
uniform float fade_out = 0.6;                // share of the life
uniform float softness = 0.25;               // metres of depth over which it fades into what is behind it
uniform float near_fade = 0.5;               // metres from the camera
uniform float intensity = 1.0;               // additive: HDR multiplier
uniform bool alpha_is_mask = true;           // additive: false for a premultiplied flipbook (fire, black is clear)
uniform float premultiplied_scale = 1.25;    // the fire flipbook was stored at 1/1.25 of its value
uniform float soot = 0.55;                   // fire: how much its dark smoky parts hide what is behind
uniform float vignette = 0.0;                // fades the texture's edges (flat-bottomed clouds turned any way)
uniform float edge_fade = 0.0;               // fades a margin along the quad's four sides (UV)
uniform float bulge = 0.65;                  // lit, without normals: a rounded normal
uniform float albedo_boost = 1.0;            // lit: brightens a texture with its own shading baked in
uniform vec3 glow_color = vec3(0.0);         // lit: emission while young (smoke lit by its fire)
uniform float glow_time = 0.0;               // seconds

varying float v_alpha;
varying vec2 v_uv;
varying vec2 v_uv2;
varying float v_blend;
varying vec2 v_corner;
varying float v_glow;

vec2 fx_cell(float f, vec2 uv) {
	float cols = frames.x;
	vec2 cell = vec2(mod(f, cols), floor(f / cols));
	return (cell + uv) / frames;
}

void vertex() {
	float life = max(INSTANCE_CUSTOM.x, 0.001);
	float age = clamp(INSTANCE_CUSTOM.y / life, 0.0, 1.0);
	float seed = INSTANCE_CUSTOM.z;
	float growth = INSTANCE_CUSTOM.w > 0.0 ? INSTANCE_CUSTOM.w : 1.0;
	float size = length(MODEL_MATRIX[0].xyz) * mix(1.0, growth, pow(age, growth_power));
	float roll = (random_roll ? seed * 6.2831853 : 0.0) + spin * (fract(seed * 7.13) > 0.5 ? 1.0 : -1.0) * INSTANCE_CUSTOM.y;
	vec2 q = VERTEX.xy;
	q.y += anchor;
	float c = cos(roll), s = sin(roll);
	q = vec2(c * q.x - s * q.y, s * q.x + c * q.y);
	v_corner = q * 2.0;
	vec3 centre = (VIEW_MATRIX * vec4(MODEL_MATRIX[3].xyz, 1.0)).xyz;
	VERTEX = centre + vec3(q * size, 0.0);
	NORMAL = vec3(0.0, 0.0, 1.0);
	TANGENT = vec3(c, s, 0.0);
	BINORMAL = vec3(-s, c, 0.0);
	vec2 uv = UV;
	if (random_flip && fract(seed * 13.7) > 0.5) {
		uv.x = 1.0 - uv.x;
	}
	float f;
	if (animate) {
		f = min(mix(frame_range.x, frame_range.y, age) + fract(seed * 5.31) * frame_jitter, frame_range.y);
	} else {
		f = floor(mix(frame_range.x, frame_range.y + 0.999, fract(seed * 3.71)));
	}
	float f0 = floor(f);
	v_blend = animate ? f - f0 : 0.0;
	v_uv = fx_cell(f0, uv);
	v_uv2 = fx_cell(min(f0 + 1.0, frame_range.y), uv);
	v_alpha = smoothstep(0.0, max(fade_in, 1e-4), age) * (1.0 - smoothstep(1.0 - fade_out, 1.0, age)) * COLOR.a;
	v_alpha *= smoothstep(near_fade * 0.5, near_fade, -centre.z);
	v_glow = glow_time > 0.0 ? 1.0 - clamp(INSTANCE_CUSTOM.y / glow_time, 0.0, 1.0) : 0.0;
}

float fx_soft(vec2 screen_uv, float depth, mat4 inv_projection) {
	float d = textureLod(depth_tex, screen_uv, 0.0).r;
	vec4 v = inv_projection * vec4(screen_uv * 2.0 - 1.0, d, 1.0);
	float scene = -v.z / v.w;
	return clamp((scene - depth) / max(softness, 1e-3), 0.0, 1.0);
}

void fragment() {
	vec4 t = mix(texture(tex, v_uv), texture(tex, v_uv2), v_blend);
	float soft = fx_soft(SCREEN_UV, -VERTEX.z, INV_PROJECTION_MATRIX);
	if (vignette > 0.0) {
		t.a *= 1.0 - vignette * smoothstep(0.45, 1.0, length(UV * 2.0 - 1.0));
	}
	if (edge_fade > 0.0) {
		vec2 e = smoothstep(vec2(0.0), vec2(edge_fade), UV) * smoothstep(vec2(0.0), vec2(edge_fade), 1.0 - UV);
		t.a *= e.x * e.y;
	}
#ifdef FX_ADD
	float mask = alpha_is_mask ? t.a : 1.0;
	ALBEDO = t.rgb * COLOR.rgb * intensity * (alpha_is_mask ? 1.0 : premultiplied_scale);
	ALPHA = clamp(mask * v_alpha * soft, 0.0, 1.0);
#endif
#ifdef FX_FIRE
	// The C4 fire flipbook is premultiplied on black: its colour adds, its darker sooty parts cover what is behind.
	vec3 c = t.rgb * premultiplied_scale;
	float cover = smoothstep(0.015, 0.22, max(c.r, max(c.g, c.b))) * v_alpha * soft;
	ALBEDO = c * COLOR.rgb * intensity * v_alpha * soft;
	ALPHA = clamp(cover * soot, 0.0, 1.0);
#endif
#ifdef FX_LIT
	ALBEDO = min(t.rgb * albedo_boost, vec3(1.0)) * COLOR.rgb;
	ALPHA = clamp(t.a * v_alpha * soft, 0.0, 1.0);
#ifdef FX_NORMAL_MAP
	NORMAL_MAP = mix(texture(normal_tex, v_uv).rgb, texture(normal_tex, v_uv2).rgb, v_blend);
#else
	vec2 cn = clamp(v_corner, vec2(-1.0), vec2(1.0));
	NORMAL = normalize(vec3(cn * bulge, 1.0));
#endif
	ROUGHNESS = 1.0;
	SPECULAR = 0.0;
	EMISSION = glow_color * v_glow * t.a;
#endif
#ifdef FX_HAZE
	vec2 dir;
	float mask;
	if (scroll) {
		vec2 n = texture(noise_tex, UV * 0.8 + vec2(0.0, -TIME * 0.6) + COLOR.b).rg - 0.5;
		dir = n * 2.0;
		vec2 c = UV * 2.0 - 1.0;
		mask = (1.0 - smoothstep(0.3, 1.0, length(c))) * t.a;
	} else {
		dir = (t.rg - 0.5) * 2.0;
		mask = t.a;
	}
	vec2 offset = dir * strength * mask * v_alpha * soft;
	ALBEDO = textureLod(screen_tex, SCREEN_UV + offset, 0.0).rgb;
	ALPHA = clamp(mask * v_alpha * soft * 4.0, 0.0, 1.0);
#endif
}

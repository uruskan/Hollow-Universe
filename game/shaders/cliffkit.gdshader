// The cliff kit's rock (docs/WORLD_TERRAIN_PLAN.md §4.3, art/cliffkit): each piece's own albedo (the scan's occlusion
// multiplied in) and normal map, baked from its scan; the family's shared tiling detail (2K, one repeat every
// `detail_metres`, about 820 px/m) projected in world space on the two planes the surface faces most (biplanar), blended
// by reoriented normal mapping and fading out by about 30 m; moss, snow or grass where the surface faces the planet's
// up, its edge broken by the detail's height; a biome tint per instance; the lowest metre or so fading into the
// ground's colour. Where a piece was cut (blue mask) or the scan didn't reach (the albedo's alpha), the detail material
// itself shows, in the scan's mean colour.
//
// Vertex colour (linear): r the base's dirt or wetness, g where cover may grow, b the fractures.
// Up comes from the planet's centre, an instance uniform the placement sets (far below by default: up is +Y), so that
// the material needs no global and loads anywhere. The planet's air (surface_atmosphere.gdshaderinc) is left to the
// step that places the pieces on the planet.
shader_type spatial;
render_mode cull_back, depth_draw_opaque, diffuse_burley, specular_schlick_ggx;

uniform sampler2D albedo_map : source_color, filter_linear_mipmap_anisotropic;
uniform sampler2D normal_map : hint_normal, filter_linear_mipmap_anisotropic;
uniform sampler2D detail_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable; // a: height
uniform sampler2D detail_normal : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable; // rg normal, b roughness, a occlusion
uniform vec4 scan_colour : source_color = vec4(0.45, 0.33, 0.26, 1.0); // the scan's mean colour (fractures)
uniform vec4 detail_mean : source_color = vec4(0.5, 0.42, 0.36, 1.0); // the detail map's mean colour
uniform float detail_metres = 2.5;
uniform float detail_strength = 0.55;   // how much the detail's colour varies the scan's
uniform float detail_bump = 1.0;        // the detail normal's strength
uniform vec2 detail_fade = vec2(16.0, 32.0); // metres from the camera: all the detail, then none
uniform float roughness_base = 0.86;
uniform float cover_threshold = 0.7;    // the surface's dot with up above which cover grows
uniform float cover_breakup = 0.6;      // how far the detail's height moves the cover's edge
uniform float wetness = 0.3;            // how much the base's dirt darkens and smooths the rock
uniform vec2 ground_fade = vec2(-0.2, 1.1); // metres above the ground line: all ground colour, then none
uniform vec4 cover_default : source_color = vec4(0.30, 0.34, 0.16, 1.0); // the family's cover (a: how much), unless the instance sets one
uniform vec4 ground_default : source_color = vec4(0.40, 0.34, 0.27, 1.0); // the family's ground, unless the instance sets one

instance uniform vec4 biome_tint : source_color = vec4(1.0, 1.0, 1.0, 1.0);
instance uniform vec4 cover_colour : source_color = vec4(0.0); // rgb moss, snow or grass; a how much (0: the material's)
instance uniform vec4 ground_colour : source_color = vec4(0.0); // rgb the ground's; a how much (0: the material's)
instance uniform vec3 planet_centre = vec3(0.0, -6371000.0, 0.0); // render space

varying vec3 v_world;
varying float v_height;

void vertex() {
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_height = VERTEX.y * length(MODEL_MATRIX[1].xyz);
}

float side(float v) {
	return v >= 0.0 ? 1.0 : -1.0;
}

float component(vec3 v, int axis) {
	return axis == 0 ? v.x : (axis == 1 ? v.y : v.z);
}

// A projection plane's coordinates (u right, v up) for the plane across `axis`, seen from the side the normal faces,
// so that the detail is never mirrored; linear, so it maps the position's derivatives too.
vec2 plane_coords(int axis, vec3 v, vec3 n) {
	if (axis == 0) {
		return vec2(-side(n.x) * v.z, v.y);
	}
	if (axis == 1) {
		return vec2(v.x, -side(n.y) * v.z);
	}
	return vec2(side(n.z) * v.x, v.y);
}

// That plane's tangent frame (columns: tangent, bitangent, normal), right-handed like the maps' own.
mat3 plane_frame(int axis, vec3 n) {
	if (axis == 0) {
		return mat3(vec3(0.0, 0.0, -side(n.x)), vec3(0.0, 1.0, 0.0), vec3(side(n.x), 0.0, 0.0));
	}
	if (axis == 1) {
		return mat3(vec3(1.0, 0.0, 0.0), vec3(0.0, 0.0, -side(n.y)), vec3(0.0, side(n.y), 0.0));
	}
	return mat3(vec3(side(n.z), 0.0, 0.0), vec3(0.0, 1.0, 0.0), vec3(0.0, 0.0, side(n.z)));
}

vec4 plane_sample(sampler2D tex, int axis, vec3 p, vec3 dx, vec3 dy, vec3 n) {
	// Godot's v runs down the image: flipped, so that the image's up is the world's.
	vec2 uv = plane_coords(axis, p, n);
	vec2 gx = plane_coords(axis, dx, n);
	vec2 gy = plane_coords(axis, dy, n);
	return textureGrad(tex, vec2(uv.x, -uv.y), vec2(gx.x, -gx.y), vec2(gy.x, -gy.y));
}

// The detail's normal on a plane, reoriented (RNM) onto the base normal, in world axes.
vec3 plane_normal(int axis, vec4 packed, vec3 base, vec3 n) {
	vec2 xy = (packed.xy * 2.0 - 1.0) * detail_bump;
	vec3 d = normalize(vec3(xy, 1.0));
	mat3 f = plane_frame(axis, n);
	vec3 t = vec3(dot(base, f[0]), dot(base, f[1]), dot(base, f[2])) + vec3(0.0, 0.0, 1.0);
	vec3 u = d * vec3(-1.0, -1.0, 1.0);
	vec3 r = t * dot(t, u) / t.z - u;
	return normalize(f * r);
}

void fragment() {
	vec4 scan = texture(albedo_map, UV);
	vec2 sxy = texture(normal_map, UV).xy * 2.0 - 1.0;
	vec3 scan_ts = vec3(sxy, sqrt(max(0.0, 1.0 - dot(sxy, sxy))));
	mat3 to_world = mat3(INV_VIEW_MATRIX);
	vec3 ng = normalize(to_world * NORMAL);
	vec3 tg = normalize(to_world * TANGENT);
	vec3 bg = normalize(to_world * BINORMAL);
	vec3 n_scan = normalize(tg * scan_ts.x + bg * scan_ts.y + ng * scan_ts.z);

	// Fractures and wherever the scan didn't reach show the detail material alone, at every distance.
	float raw = clamp(max(COLOR.b, 1.0 - scan.a), 0.0, 1.0);
	vec3 base = normalize(mix(n_scan, ng, raw));
	float dist = length(v_world - CAMERA_POSITION_WORLD);
	float near = 1.0 - smoothstep(detail_fade.x, detail_fade.y, dist);
	float amount = max(near, raw);

	// Biplanar: the planes across the normal's largest and middle components, weighted as Quilez does.
	vec3 p = v_world / detail_metres;
	vec3 dx = dFdx(p);
	vec3 dy = dFdy(p);
	vec3 a = abs(ng);
	int major = a.x > a.y ? (a.x > a.z ? 0 : 2) : (a.y > a.z ? 1 : 2);
	int minor = a.x < a.y ? (a.x < a.z ? 0 : 2) : (a.y < a.z ? 1 : 2);
	int middle = 3 - major - minor;
	vec2 w = clamp((vec2(component(a, major), component(a, middle)) - 0.5773) / (1.0 - 0.5773), 0.0, 1.0);
	w.x = max(w.x, 0.0001);
	w /= w.x + w.y;
	vec4 c0 = plane_sample(detail_albedo, major, p, dx, dy, ng);
	vec4 c1 = plane_sample(detail_albedo, middle, p, dx, dy, ng);
	vec4 m0 = plane_sample(detail_normal, major, p, dx, dy, ng);
	vec4 m1 = plane_sample(detail_normal, middle, p, dx, dy, ng);
	vec4 colour = c0 * w.x + c1 * w.y;
	vec4 maps = m0 * w.x + m1 * w.y;
	vec3 n_detail = normalize(plane_normal(major, m0, base, ng) * w.x + plane_normal(middle, m1, base, ng) * w.y);
	vec3 n = normalize(mix(base, n_detail, amount));
	float height = colour.a;

	vec3 ratio = colour.rgb / max(detail_mean.rgb, vec3(0.02));
	vec3 rock = scan.rgb * mix(vec3(1.0), ratio, near * detail_strength);
	vec3 albedo = mix(rock, scan_colour.rgb * ratio, raw) * biome_tint.rgb;
	float rough = mix(roughness_base, clamp(maps.b, 0.5, 1.0), amount * 0.6);
	albedo *= 1.0 - wetness * COLOR.r;
	rough = mix(rough, rough * 0.75, wetness * COLOR.r);

	// Cover where the surface faces the planet's up.
	vec3 up = normalize(v_world - planet_centre);
	float cover = smoothstep(cover_threshold - 0.06, cover_threshold + 0.06, dot(n, up) + (height - 0.5) * cover_breakup);
	vec4 cover_rgba = cover_colour.a > 0.0 ? cover_colour : cover_default;
	cover *= COLOR.g * cover_rgba.a;
	albedo = mix(albedo, cover_rgba.rgb * (0.7 + 0.6 * height), cover);
	rough = mix(rough, 0.95, cover);
	n = normalize(mix(n, ng, cover * 0.4));

	// The base into the ground's colour.
	float ground = 1.0 - smoothstep(ground_fade.x, ground_fade.y, v_height + (height - 0.5) * 0.8);
	vec4 ground_rgba = ground_colour.a > 0.0 ? ground_colour : ground_default;
	albedo = mix(albedo, ground_rgba.rgb, ground * ground_rgba.a);
	rough = mix(rough, 0.95, ground * ground_rgba.a);

	ALBEDO = albedo;
	ROUGHNESS = rough;
	SPECULAR = 0.35;
	AO = mix(1.0, maps.a, amount * 0.6);
	AO_LIGHT_AFFECT = 0.0;
	NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);
}

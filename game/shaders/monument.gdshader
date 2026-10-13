// Monuments (MONUMENTS.md §8.2): one shader for every surface of the monuments and their kit pieces.
// - Tiling texture sets in metres (UV1 / tile_metres): albedo with the paint mask in A (where the vertex tint
//   applies, so chips, rust and bare metal keep their own colour), ORM, normal map. Or a plain colour (glass, lamps,
//   glazed and matte parts).
// - Vertex colour: RGB the part's tint (stored sRGB by the importer, decoded here), A its baked occlusion.
// - The height above the monument's ground is the vertex's own height in the model (grime and splashed dirt near
//   it); wear is the material's.
// - Per instance: piece_tint (kit pieces placed with a colour). Lamps: lit (their material's, switched by power).
// - A world-space variation of a few per cent so long walls don't repeat.
// Self-contained, so monuments draw anywhere (the monument viewer, the model viewer). On a planet the world view
// will swap in a variant with the planet's air and cloud shadows (MONUMENTS.md §10, M3).
shader_type spatial;
render_mode cull_back, depth_draw_opaque, diffuse_burley, specular_schlick_ggx;

const float HU_PI = 3.14159265358979;

// Cook-Torrance specular (GGX, Smith-Schlick, Schlick Fresnel with F0 0.04) times the cosine, as the surface's own.
float hu_specular(vec3 n, vec3 v, vec3 l, float roughness) {
	float ndl = clamp(dot(n, l), 0.0, 1.0);
	float ndv = clamp(dot(n, v), 1e-3, 1.0);
	vec3 h = normalize(v + l);
	float ndh = clamp(dot(n, h), 0.0, 1.0);
	float r = roughness * roughness;
	float d = r * r / (HU_PI * pow(ndh * ndh * (r * r - 1.0) + 1.0, 2.0));
	float k = r * 0.5;
	float g = ndl / (ndl * (1.0 - k) + k) * ndv / (ndv * (1.0 - k) + k);
	float f = 0.04 + 0.96 * pow(1.0 - clamp(dot(h, v), 0.0, 1.0), 5.0);
	return d * g * f / max(4.0 * ndl * ndv, 1e-3) * ndl;
}

uniform bool textured = true;
uniform sampler2D albedo_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D orm_tex : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float tile_metres = 2.0;
uniform bool alpha_cut = false;
uniform vec4 plain_albedo : source_color = vec4(0.8, 0.8, 0.8, 1.0);
uniform float plain_roughness = 0.6;
uniform float plain_metallic = 0.0;
uniform float plain_specular = 0.5;
uniform float emission_strength = 0.0;
uniform vec3 grime_colour : source_color = vec3(0.24, 0.205, 0.16);
uniform float grime_height = 1.8;
uniform float wear = 0.4;

instance uniform vec4 piece_tint = vec4(1.0);
uniform float lit = 1.0; // lamps on or off for the whole material (a powered monument draws its own lamp material)

varying vec3 v_world;
varying float v_height;

vec3 srgb_to_linear(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

float hash3(vec3 p) {
	p = fract(p * 0.3183099 + vec3(0.1, 0.17, 0.13));
	p *= 17.0;
	return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

// Smooth value noise in world space, for the slow variation across long surfaces.
float vnoise(vec3 x) {
	vec3 i = floor(x);
	vec3 f = fract(x);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash3(i), hash3(i + vec3(1, 0, 0)), f.x), mix(hash3(i + vec3(0, 1, 0)), hash3(i + vec3(1, 1, 0)), f.x), f.y),
		mix(mix(hash3(i + vec3(0, 0, 1)), hash3(i + vec3(1, 0, 1)), f.x), mix(hash3(i + vec3(0, 1, 1)), hash3(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

void vertex() {
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_height = VERTEX.y;
}

void fragment() {
	vec3 tint = srgb_to_linear(COLOR.rgb) * piece_tint.rgb;
	float ao = COLOR.a;
	float height = v_height;
	vec3 albedo;
	float rough;
	float metal;
	float occl = 1.0;
	if (textured) {
		vec2 uv = UV / tile_metres;
		vec4 a = texture(albedo_tex, uv);
		if (alpha_cut && a.a < 0.5) {
			discard;
		}
		float mask = alpha_cut ? 0.0 : a.a;
		albedo = mix(a.rgb, a.rgb * tint, mask);
		vec3 orm = texture(orm_tex, uv).rgb;
		occl = orm.r;
		rough = orm.g;
		metal = orm.b;
		NORMAL_MAP = texture(normal_tex, uv).rgb;
		SPECULAR = 0.5;
	} else {
		albedo = plain_albedo.rgb * tint;
		rough = plain_roughness;
		metal = plain_metallic;
		SPECULAR = plain_specular;
	}
	// The world's object-space position isn't stable under the floating origin, so the variation follows the
	// camera-independent part of it: the texture's own metres plus the height.
	// Stains tens of metres across and a finer mottle, so big aprons and walls aren't one flat colour from afar.
	float n = vnoise(vec3(UV * 0.11, height * 0.07));
	float big = vnoise(vec3(UV * 0.03, height * 0.02 + 5.0));
	albedo *= (0.8 + 0.28 * smoothstep(0.3, 0.7, big)) * (0.95 + 0.1 * n);
	float g = (1.0 - smoothstep(0.0, grime_height, height)) * (0.2 + 0.55 * wear);
	albedo = mix(albedo, grime_colour * (0.85 + 0.3 * n), g);
	rough = clamp(rough + g * 0.25, 0.04, 1.0);
	ALBEDO = albedo * occl;
	AO = ao;
	AO_LIGHT_AFFECT = 0.65;
	ROUGHNESS = rough;
	METALLIC = metal;
	if (emission_strength > 0.0) {
		EMISSION = tint * emission_strength * lit;
	}

}

void light() {
	float ndl = clamp(dot(NORMAL, LIGHT), 0.0, 1.0);
	float shade = ATTENUATION;
	DIFFUSE_LIGHT += LIGHT_COLOR * shade * ndl / HU_PI;
	SPECULAR_LIGHT += LIGHT_COLOR * shade * hu_specular(NORMAL, VIEW, LIGHT, ROUGHNESS) * (0.4 + METALLIC * 0.6);
}

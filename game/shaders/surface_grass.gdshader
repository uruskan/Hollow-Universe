// The ground's grass clumps (GROUND.md §4.6, docs/WALKING_GROUND.md §18): five species of our own (art/grass/build_grass.py)
// and the forest floor's litter, each a bundle of cards carrying painted blades, drawn as MultiMesh instances that
// GrassField lays out per tile on a worker, rooted on the drawn terrain's own triangles. Per instance:
// - transform: the root (on the finest drawn triangle), the clump's turn and size, and the ground's slope as a shear, so
//   blades stay upright while their feet follow the ground;
// - colour: the place's grass brightness (r, GrassField.Tint.cs: the terrain's own tint noise at the root), dryness (g)
//   and canopy over it (b) (Godot multiplies the instance colour into COLOR; the meshes carry none of their own, their
//   cards' height fraction and rank being UV2);
// - custom: the terrain's morph at the root (x, metres along up: the drawn ground sinks toward its parent's grid far off),
//   a wind phase (y), the clump's stable thinning rank (z) and its variant and palette draw (w).
// With distance, cards drop one by one in a stable order (each level of detail is the first cards of the nearest, with
// fewer segments; GrassField switches a tile's level once the cards it lacks are gone), whole clumps thin by their rank
// while the rest widen, and the far edge dissolves clump by clump instead of shrinking into a flat ring.
// Wind bends the cards with gusts rolling across the field, and their normals with them; the walker's feet push them
// aside. Light: the cards' own normals (bent with the card, rounded across it, tilted by the painted blades' folds), a
// bounded glow through the blades toward a low sun and a rough sheen (surface_foliage.gdshaderinc).
shader_type spatial;
render_mode cull_disabled, depth_draw_opaque, diffuse_burley, specular_schlick_ggx, skip_vertex_transform;

#include "res://shaders/surface_atmosphere.gdshaderinc"
#include "res://shaders/surface_planet.gdshaderinc"
#include "res://shaders/surface_clouds.gdshaderinc"
#include "res://shaders/surface_grass.gdshaderinc"
#include "res://shaders/surface_foliage.gdshaderinc"

global uniform vec4 hu_wind;

uniform sampler2D albedo_atlas : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform sampler2D detail_atlas : filter_linear_mipmap_anisotropic, repeat_disable; // fold across, along; accent; translucency
uniform sampler2D macro_noise : filter_linear_mipmap, repeat_enable;
uniform int kind = 0;                    // 0 clump cards, 1 billboard (two crossed quads), 2 litter lying flat
uniform vec2 variant_offset = vec2(0.125, 0.0); // the atlas offset of variant bit 1 (u) and bit 2 (v)
uniform vec3 species_mean = vec3(0.24, 0.33, 0.14); // the species' painted mean colour (grass.json)
uniform vec3 blade_mean = vec3(0.5, 0.63, 0.34);    // the terrain's grass reference: its grass is blade_mean × tint
uniform float mesh_height = 0.5;         // the clump's height at scale one
uniform float mesh_reach = 0.35;         // its cards' farthest reach from the root
uniform float dry_bias = 0.0;            // added to the place's dryness (seed heads paler)
uniform float dry_floor = 0.0;           // dryness at least this (dry grass: straw anywhere)
uniform float green = 0.0;               // keeps the colour this much lush (herbs)
uniform vec4 accent_a : source_color = vec4(1.0);  // accents (petals, seed heads, bells) take one of these
uniform vec4 accent_b : source_color = vec4(1.0);
uniform vec4 accent_c : source_color = vec4(1.0);
uniform vec4 accent_d : source_color = vec4(1.0);
uniform float accent_tint = 0.0;
uniform float translucency = 0.6;
uniform vec3 glow = vec3(1.0, 0.95, 0.72); // the colour light picks up through the blades
uniform float roughness = 0.55;
uniform float sheen = 0.25;
uniform float fold = 0.6;                // how far the painted folds tilt the normal
uniform float stiffness = 1.0;           // the species' resistance to wind
uniform vec4 card_curve = vec4(5.0, 23.0, 0.3, 0.0); // cards all kept to x m, down to z kept at y m
uniform vec4 thin = vec4(12.6, 31.5, 0.45, 0.0);    // clumps all kept to x m, down to z kept at y m
uniform vec2 fade = vec2(35.7, 42.0);    // the last clumps dissolve between these distances
uniform vec2 morph_range = vec2(1e6, 2e6); // the finest terrain's morph toward its parent (TerrainLod)
uniform vec2 dome = vec2(0.5, 0.82);     // normals smoothed toward the clump's dome: near, far
uniform vec4 trample = vec4(0.0);        // xyz: the walker's feet (render space), w: radius (0: none)
uniform vec3 wind_east = vec3(1.0, 0.0, 0.0); // the ground's axes at the camera (render), for the gusts' pattern
uniform vec3 wind_north = vec3(0.0, 0.0, -1.0);

varying vec3 v_world;
varying vec3 v_normal;                   // the card's own normal (flipped to the viewer in fragment)
varying vec3 v_dome;                     // the clump's dome normal
varying vec3 v_across;                   // across the card (+u), for the painted folds
varying float v_t;                       // height fraction along the card
varying float v_dome_blend;
varying flat vec3 v_colour;              // the clump's colour: blade_mean × the place's tint (litter: its brightness)
varying flat vec4 v_accent;              // rgb: the accent colour, a: the canopy over the clump
varying flat vec2 v_variant;
varying vec3 v_air;
varying vec4 v_fog;
varying float v_cloud;

// The share of a clump's cards kept at a distance (GrassField.CardsKept is the same).
float cards_kept(float d) {
	return mix(1.0, card_curve.z, smoothstep(card_curve.x, card_curve.y, d));
}

// The share of clumps kept at a distance (GrassField.ClumpsKept is the same).
float clumps_kept(float d) {
	return mix(1.0, thin.z, smoothstep(thin.x, thin.y, d)) * (1.0 - smoothstep(fade.x, fade.y, d));
}

void vertex() {
	vec3 X = MODEL_MATRIX[0].xyz;
	vec3 Y = MODEL_MATRIX[1].xyz;
	vec3 Z = MODEL_MATRIX[2].xyz;
	vec3 root = MODEL_MATRIX[3].xyz;
	float sy = length(Y);
	vec3 up = Y / max(sy, 1e-6);
	float dist = length(root - CAMERA_POSITION_WORLD);
	// The drawn terrain morphs toward its parent's grid far off: the root follows it (the morph's own factor).
	float morph = clamp((dist - morph_range.x) / max(morph_range.y - morph_range.x, 1.0), 0.0, 1.0);
	root += up * INSTANCE_CUSTOM.x * morph;

	float rank = INSTANCE_CUSTOM.z;
	float draw = INSTANCE_CUSTOM.w * 4.0;
	float variant = floor(draw);
	float palette = fract(draw);
	float keep = clumps_kept(dist);
	float alive = 1.0 - smoothstep(keep - 0.06, keep, rank);
	float widen = clamp(inversesqrt(max(keep, 0.2)), 1.0, 1.45);
	float card_rank = UV2.y;
	float shade = fract(card_rank * 19.19 + 0.31);
	float card_stiffness = 0.6 + 0.4 * fract(card_rank * 7.77 + 0.53);
	float card = 1.0;
	if (kind == 0) {
		float kept = cards_kept(dist);
		float jitter = (fract(palette * 13.7) - 0.5) * 0.06;
		card = 1.0 - smoothstep(kept - 0.08, kept, card_rank + jitter + 0.04);
		widen *= clamp(inversesqrt(max(kept, 0.25)), 1.0, 1.5);
	}
	float size = alive * card;
	v_t = UV2.x;
	v_world = vec3(0.0);
	v_normal = up;
	v_dome = up;
	v_across = vec3(1.0, 0.0, 0.0);
	v_dome_blend = 0.0;
	v_colour = vec3(1.0);
	v_accent = vec4(1.0, 1.0, 1.0, COLOR.b);
	v_variant = vec2(mod(variant, 2.0) >= 1.0 ? variant_offset.x : 0.0, variant >= 2.0 ? variant_offset.y : 0.0);
	v_air = vec3(1.0);
	v_fog = vec4(0.0);
	v_cloud = 1.0;
	if (size <= 0.002) {
		VERTEX = vec3(0.0);
		NORMAL = vec3(0.0, 0.0, 1.0);
	} else {
		// The clump's axes without the slope's shear: its turn and width.
		vec3 Xh = X - up * dot(X, up);
		vec3 Zh = Z - up * dot(Z, up);
		vec3 xn = normalize(Xh);
		vec3 zn = normalize(Zh);
		float height = sy * mesh_height;
		float t = UV2.x;
		float w = widen * size;
		vec3 offset = (X * VERTEX.x + Z * VERTEX.z) * w;     // across the ground, its slope's lift included
		vec3 rise = up * (sy * VERTEX.y * mix(0.35, 1.0, size));
		vec3 normal = normalize(xn * NORMAL.x + up * NORMAL.y + zn * NORMAL.z);
		vec3 bend = vec3(0.0);
		vec3 bend_rate = vec3(0.0);
		if (kind != 2) {
			// Wind: gusts as patches of the noise drifting downwind, a ripple along them, each card a little flutter.
			vec2 wp = vec2(dot(root, wind_east), dot(root, wind_north));
			vec2 wd = normalize(vec2(dot(hu_wind.xyz, wind_east), dot(hu_wind.xyz, wind_north)) + vec2(1e-5));
			vec3 wdir = normalize(wind_east * wd.x + wind_north * wd.y);
			vec3 wside = normalize(cross(up, wdir));
			float gust = texture(macro_noise, wp / 70.0 - wd * TIME * 0.085).a;
			float ripple = sin(dot(wp, wd) * 0.45 - TIME * 2.4 + INSTANCE_CUSTOM.y * 6.283) * 0.5 + 0.5;
			float strength = hu_wind.w * (0.2 + 1.2 * smoothstep(0.3, 0.75, gust)) * (0.55 + 0.45 * ripple);
			float give = strength * 0.24 / (stiffness * card_stiffness);
			float flutter = sin(TIME * (4.1 + 2.0 * shade) + INSTANCE_CUSTOM.y * 30.0 + shade * 12.0) * 0.035 * hu_wind.w;
			bend = wdir * (give * t * t * height) + wside * (flutter * t * height);
			bend_rate = wdir * (2.0 * give * t) + wside * flutter;
			if (trample.w > 0.0) {
				vec3 off = root + offset - trample.xyz;
				vec3 away = off - up * dot(off, up);
				float dd = length(away);
				float k = (1.0 - smoothstep(trample.w * 0.3, trample.w, dd)) * step(abs(dot(root - trample.xyz, up)), 1.5);
				vec3 push = away / max(dd, 1e-3) * (k * 0.6);
				bend += push * (t * height);
				bend_rate += push;
			}
		}
		// Bending keeps a blade's length: what leans over comes down.
		float b = length(bend);
		vec3 world = root + offset + rise + bend - up * (min(b * b / max(height, 0.05), height * 0.6) * t);
		// The bent card's normal: p' = p + f(h)·d tilts the normal by the bend's rate along the height.
		normal = normalize(normal - up * dot(bend_rate, normal));
		vec3 across = normalize(xn * TANGENT.x + up * TANGENT.y + zn * TANGENT.z);
		vec3 radial = offset - up * dot(offset, up);
		vec3 dome_normal = normalize(up + radial / max(mesh_reach * sy, 0.05) * 0.35);
		if (kind == 2) {
			normal = up;
			dome_normal = up;
		}
		v_world = world;
		v_normal = normal;
		v_dome = dome_normal;
		v_across = across;
		v_dome_blend = kind == 1 ? 0.8 : mix(dome.x, dome.y, smoothstep(4.0, 30.0, dist));
		VERTEX = (VIEW_MATRIX * vec4(world, 1.0)).xyz;
		NORMAL = normalize((VIEW_MATRIX * vec4(normal, 0.0)).xyz);

		// The clump's colour: the place's grass tint at its root (the terrain's, for its dryness), or litter's brown.
		if (kind == 2) {
			v_colour = vec3(0.7 + 0.6 * palette) * mix(1.0, 0.75, COLOR.b);
		} else {
			float dry = clamp(max(COLOR.g + dry_bias, dry_floor), 0.0, 1.0) * (1.0 - green);
			v_colour = blade_mean * hu_grass_tint_of(dry, COLOR.r) * mix(0.92, 1.08, shade);
		}
		float pick = fract(palette * 7.31);
		vec3 accent = pick < 0.25 ? accent_a.rgb : pick < 0.5 ? accent_b.rgb : pick < 0.75 ? accent_c.rgb : accent_d.rgb;
		v_accent = vec4(accent, COLOR.b);
		hu_aerial_terms(root, CAMERA_POSITION_WORLD, v_air, v_fog);
		v_cloud = hu_cloud_shadow(root, hu_sun_direction);
	}
}

void fragment() {
	vec2 uv = UV + v_variant;
	vec4 tex = texture(albedo_atlas, uv);
	// Mip levels average a blade's edge into its gaps: sharpen alpha by the level so blades keep their coverage far off.
	vec2 texel = uv * vec2(textureSize(albedo_atlas, 0));
	vec2 dx = dFdx(texel);
	vec2 dy = dFdy(texel);
	float level = max(0.0, 0.5 * log2(max(dot(dx, dx), dot(dy, dy))));
	// Cut-outs stay in the opaque pass: water needs their colour and depth in its screen snapshot.
	if (tex.a * (1.0 + 0.25 * level) < 0.5) discard;
	vec4 detail = texture(detail_atlas, uv);
	vec3 n_card = normalize(v_normal) * (FRONT_FACING ? 1.0 : -1.0);
	vec3 across = normalize(v_across - n_card * dot(v_across, n_card));
	vec3 along = cross(n_card, across);
	vec2 tilt = (detail.rg * 2.0 - 1.0) * fold;
	vec3 n = normalize(n_card + across * tilt.x + along * tilt.y);
	n = normalize(mix(n, normalize(v_dome), v_dome_blend));
	vec3 albedo;
	if (kind == 2) {
		albedo = tex.rgb * v_colour;
	} else {
		albedo = tex.rgb / species_mean * v_colour;
		float lum = dot(tex.rgb, vec3(0.2126, 0.7152, 0.0722));
		albedo = mix(albedo, v_accent.rgb * clamp(lum / 0.7, 0.3, 1.3), detail.b * accent_tint);
	}
	// The clump's dense, shaded foot.
	float foot = smoothstep(0.0, 0.4, v_t);
	ALBEDO = albedo * mix(0.86, 1.0, kind == 2 ? 1.0 : foot) * v_air;
	NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);
	ROUGHNESS = roughness;
	SPECULAR = 0.16;
	AO = (kind == 2 ? 0.8 : mix(0.78, 1.0, foot)) * (1.0 - 0.38 * smoothstep(0.15, 0.85, v_accent.a));
	AO_LIGHT_AFFECT = 0.0;
	BACKLIGHT = vec3(translucency * detail.a);
	FOG = v_fog;
}

void light() {
	float shadow = LIGHT_IS_DIRECTIONAL ? v_cloud : 1.0;
	vec3 diffuse = vec3(0.0);
	vec3 specular = vec3(0.0);
	hu_foliage_light(NORMAL, VIEW, LIGHT, LIGHT_COLOR * ATTENUATION * shadow, kind == 2 ? 0.0 : BACKLIGHT.r, v_t,
		ROUGHNESS, kind == 2 ? 0.05 : sheen, glow, diffuse, specular);
	DIFFUSE_LIGHT += diffuse;
	SPECULAR_LIGHT += specular;
}

// Grass and ground cover of the surface view (GROUND.md §4.6, docs/WHOLE_WORLD.md §10.3): no plant models, a field of
// small cards drawn by the GPU on a grid that follows the camera. Every instance is a cell of a cube face (one grid a
// face near a seam: GrassField.cs), so what grows in it (place, size, lean, colour, dryness, kind) comes from a hash of
// the face and the cell and never shifts as the grid moves; a cell whose middle lies beyond its face's edge is left to
// the neighbouring face's grid. The cells' corners lie on the drawn ground (a lattice texture made on the CPU, with the
// grass density); the moisture, flowers, forest cover and small plants come from a second texture. Each tuft stands along
// up from the planet's centre, turned in the ground's own plane; its colour is measured in metres on the face, as the
// terrain's grass layer under it.
// - Layer 0, upright: grass tufts, meadow grass, dry grass, flowers;
//   under trees ferns, herbs and seedlings. Blades are hollow-orbit's painted cards, coloured like the ground's grass
//   layer under them; grass normals follow the deformed cards and the base takes the ground's colour.
//   Wind bends them with gusts that roll across the field; they bend away from the walker's feet.
// - Layer 1, flat: leaf litter, needles and twigs lying on the forest floor (the atlas's leaf and twig cells, faded
//   to browns).
// Grass fades by stable whole-card rejection at full height. Plants and litter retain their existing shrink fade.
shader_type spatial;
render_mode cull_disabled, depth_draw_opaque, diffuse_burley, specular_schlick_ggx, skip_vertex_transform;

#include "res://shaders/surface_atmosphere.gdshaderinc"
#include "res://shaders/surface_planet.gdshaderinc"
#include "res://shaders/surface_terrain_contact.gdshaderinc"
#include "res://shaders/surface_clouds.gdshaderinc"
#include "res://shaders/surface_grass.gdshaderinc"

global uniform vec4 hu_wind;

uniform int layer = 0;              // 0 upright tufts and plants, 1 flat litter
uniform bool blade_underlayer = false; // authored crowns supply the extra species; retain the vertex grass's own height
uniform int grid_width = 180;
uniform ivec2 grid_offset;          // the grid's first cell in the lattice
uniform bool compact_cells = false; // explicit stable cells in the opt-in spatial path
uniform ivec2 tile_cell;            // tile's first cell relative to this immutable lattice
uniform float far_radius = 42.0;    // blades are gone here
uniform float shrink_start = 24.0;  // grass card fade starts here; plants/litter start shrinking
uniform float density_scale = 1.0;  // the share of tufts kept (the graphics settings' grass density)

uniform sampler2D lattice : filter_nearest, repeat_disable;       // xyz: the cell corner from the anchor; w: grass density
uniform sampler2D lattice_cover : filter_nearest, repeat_disable; // r: moisture, g: flowers, b: forest cover (litter), a: small plants
uniform int lattice_size = 256;     // corners along a side
uniform ivec2 lattice_cell;         // the face's cell index of the lattice's first cell
uniform int face_cells = 8192;      // cells along a face's edge
uniform int face = 0;               // the cube face
uniform vec3 anchor;                // render position the corners are measured from
uniform vec2 face_origin;           // metres on the face of the first corner (less the texture anchor)
uniform float cell_metres = 0.46;

uniform sampler2D atlas : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform vec4 cell_grass;            // atlas cells (u0, v bottom, u1, v top)
uniform vec4 cell_dry;
uniform vec4 cell_meadow;
uniform vec4 cell_flower;
uniform vec4 cell_herb;
uniform vec4 cell_fern;
uniform vec4 cell_bush;
uniform vec4 cell_leaves_a;         // broadleaf, beech, dark oak and birch leaves: litter
uniform vec4 cell_leaves_b;
uniform vec4 cell_leaves_c;
uniform vec4 cell_leaves_d;
uniform vec4 cell_twigs;            // twigs and fallen conifer sprigs
uniform vec4 cell_needles;

uniform sampler2DArray ground_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D macro_noise : filter_linear_mipmap, repeat_enable;
uniform float grass_layer_size = 4.0;
uniform vec3 blade_mean = vec3(0.5, 0.63, 0.34);
uniform vec4 trample = vec4(0.0);   // xyz: the walker's feet (render space), w: radius (0: none)
uniform float translucency = 0.35;
uniform float seam_band = 2.0;      // metres either side of a face seam the colours blend over (as the terrain's)
// Keep the original readable blade height. Authored crowns add species; they do not replace this grass layer.
uniform vec2 grass_height_metres = vec2(0.28, 0.54);
uniform vec2 grass_patch_height = vec2(0.86, 1.14);

varying vec3 v_world;
varying vec4 v_cell;
varying vec3 v_tint;
varying vec3 v_ground;
varying float v_t;
varying float v_kind;               // 0 grass, 1 plant, 2 litter (faded to browns)
varying float v_cover;              // forest cover: the canopy keeps sky light off the floor
varying flat float v_dry;           // biome-derived moisture, with stable local patches
varying vec3 v_normal;              // retained plant/litter normal; grass uses its deformed triangle

uint hash_u(uint x) {
	x ^= x >> 16u;
	x *= 0x7feb352du;
	x ^= x >> 15u;
	x *= 0x846ca68bu;
	x ^= x >> 16u;
	return x;
}

float rnd(inout uint s) {
	s = hash_u(s);
	return float(s >> 8u) / 16777216.0;
}

void vertex() {
	int i = INSTANCE_ID % grid_width;
	int j = INSTANCE_ID / grid_width;
	ivec2 c = grid_offset + ivec2(i, j);
	if (compact_cells) c = tile_cell + ivec2(INSTANCE_CUSTOM.xy);
	ivec2 g = lattice_cell + c;
	uint s = hash_u(uint(g.x) * 0x9e3779b9u ^ hash_u(uint(g.y) + 0x632be5abu) ^ uint(layer) * 0x51ed27u ^ uint(face) * 0x27d4eb2fu);
	vec2 jit = vec2(rnd(s), rnd(s));
	// The tuft's foot on the ground: between its cell's four corners.
	vec4 n00 = texelFetch(lattice, c, 0);
	vec4 n10 = texelFetch(lattice, c + ivec2(1, 0), 0);
	vec4 n01 = texelFetch(lattice, c + ivec2(0, 1), 0);
	vec4 n11 = texelFetch(lattice, c + ivec2(1, 1), 0);
	vec4 f = mix(mix(n00, n10, jit.x), mix(n01, n11, jit.x), jit.y);
	// Upright roots follow the sampled lattice's triangle diagonal; density stays bilinear.
	// The separate litter layer retains its existing positions.
	vec3 ground_x = jit.x >= jit.y ? n10.xyz - n00.xyz : n11.xyz - n01.xyz;
	vec3 ground_y = jit.x >= jit.y ? n11.xyz - n10.xyz : n01.xyz - n00.xyz;
	if (layer == 0) f.xyz = n00.xyz + ground_x * jit.x + ground_y * jit.y;
	vec4 f2 = texelFetch(lattice_cover, c, 0);
	vec3 base = anchor + f.xyz;
	float dist = length(base - CAMERA_POSITION_WORLD);
	// Only the cells whose middle lies on this face: the neighbour's grid draws the rest.
	float inside = (g.x >= 0 && g.y >= 0 && g.x < face_cells && g.y < face_cells) ? 1.0 : 0.0;
	// Compact tiles include the legacy window's movement halo. Keep its exact half-open active square.
	if (compact_cells && (c.x < grid_offset.x || c.y < grid_offset.y || c.x >= grid_offset.x + grid_width || c.y >= grid_offset.y + grid_width)) inside = 0.0;
	vec2 wp = face_origin + (vec2(c) + jit) * cell_metres;
	// The ground's plane at the tuft: up from the planet's centre, across along the face's cells.
	vec3 up = hu_up(base);
	vec3 across = n10.xyz - n00.xyz;
	vec3 tx = normalize(across - up * dot(across, up) + vec3(1e-6));
	vec3 tz = cross(tx, up);
	vec3 ground_normal = normalize(cross(ground_x, ground_y));
	ground_normal *= dot(ground_normal, up) < 0.0 ? -1.0 : 1.0;

	// What grows in this cell. Thin grass grows in patches, not evenly spread.
	float density = f.w;
	float patchy = texture(macro_noise, wp / 5.3 + vec2(0.71, 0.29)).r;
	float patches = mix(0.25 + 1.5 * smoothstep(0.3, 0.7, patchy), 1.0, smoothstep(0.55, 0.9, density)); // dense meadows stay closed
	density = clamp(density * patches, 0.0, 1.0) * step(0.02, density);
	float roll = rnd(s);
	float kind = -1.0; // -1 nothing, 0 grass, 1 plant, 2 litter
	if (layer == 0) {
		// Habitat and the quality setting select a tuft once. Surviving tufts retain all their blades and size.
		if (roll < density * density_scale) {
			kind = 0.0;
		} else if (!blade_underlayer && roll < density + (1.0 - density) * f2.a * 0.3) {
			kind = 1.0;
		}
	} else if (roll < f2.b * 0.85) {
		kind = 2.0;
	}
	// Preserve the old quality decision for optional plants and the independent litter layer.
	float keep = float(hash_u(s ^ 0xA511E9B3u) & 0xFFFFu) / 65535.0;
	if (kind != 0.0) kind = keep <= density_scale ? kind : -1.0;
	// Reject habitat/window misses before reading the drawn-terrain atlas. Contact is authoritative when bound:
	// a missing drawn tile cannot fall back to a different heightfield. The exact base also owns the fade distance.
	bool contact_grass = terrain_contact_enabled && kind == 0.0;
	bool base_supported = true;
	if (contact_grass && inside > 0.0) {
		vec3 contact_base;
		vec3 contact_normal;
		base_supported = hu_drawn_contact(face, (vec2(g) + jit) / float(face_cells), CAMERA_POSITION_WORLD, contact_base, contact_normal);
		if (base_supported) {
			base = contact_base;
			ground_normal = contact_normal;
			up = hu_up(base);
			tx = normalize(across - up * dot(across, up) + vec3(1e-6));
			tz = cross(tx, up);
			dist = length(base - CAMERA_POSITION_WORLD);
		}
	}
	float distance_cover = 1.0 - smoothstep(shrink_start, far_radius, dist);
	float size = inside * step(0.0, kind) * (kind == 0.0 ? 1.0 : distance_cover);
	if (!base_supported) size = 0.0;
	v_kind = kind;
	v_cover = f2.b;
	v_dry = 0.0;
	v_t = COLOR.a;
	v_world = vec3(0.0);
	v_cell = cell_grass;
	v_tint = vec3(1.0);
	v_ground = vec3(0.0);
	v_normal = up;
	// Five full-size cards per grass tuft. Distance thresholds belong to the stable cell/card, not screen pixels
	// or moving UVs, so cutout holes cannot crawl as a blade sways. Threshold crossings still need motion review.
	int card = VERTEX_ID / 8;
	float cards = 5.0;
	if (kind == 0.0) {
		float distance_rank = float(hash_u(s ^ (uint(card) + 1u) * 0x68bc21ebu) >> 8u) / 16777216.0;
		if (distance_cover <= distance_rank) size = 0.0;
		s = hash_u(s); // consume the original card-count draw; size, colour and wind identities stay stable
	} else if (kind == 1.0) {
		cards = 2.0 + floor(rnd(s) * 2.0);
	}
	if (size <= 0.0 || float(card) >= cards) {
		VERTEX = vec3(0.0);
		NORMAL = vec3(0.0, 0.0, 1.0);
	} else {
		// Independent shape choices for each stable cell/card. Never advance s here: occupancy, palette,
		// atlas selection, yaw and the coherent wind field keep their existing identities. INSTANCE_ID is
		// deliberately absent so compact tiles and whole-face grids produce the same card at the same cell.
		vec4 card_shape = vec4(0.0);
		if (kind == 0.0) {
			uint shape_seed = hash_u(s ^ (uint(card) + 1u) * 0xb5297a4du ^ 0x1b56c4e9u);
			card_shape.x = rnd(shape_seed);
			card_shape.y = rnd(shape_seed);
			card_shape.z = rnd(shape_seed);
			card_shape.w = rnd(shape_seed);
		}
		// The grass's colour, as the terrain's grass layer has it: measured on this face, and within a seam's band
		// blended with the neighbour's, as the ground under it is.
		vec3 d = hu_body_direction(base);
		ivec3 faces;
		vec3 fw;
		hu_face_blend(d, seam_band, faces, fw);
		float r_dry = (rnd(s) - 0.5) * 0.3;
		float dry_face = 0.0;
		vec3 tint_face = vec3(0.0);
		vec3 detail = vec3(0.0);
		for (int k = 0; k < 3; k++) {
			if (fw[k] > 0.0) {
				vec2 q = faces[k] == face ? wp : hu_face_metres(d, faces[k]);
				float dk = clamp(hu_grass_dryness(q, f2.r, macro_noise) + r_dry, 0.0, 1.0);
				dry_face += dk * fw[k];
				tint_face += hu_grass_tint(q, dk, macro_noise) * fw[k];
				detail += textureLod(ground_albedo, vec3(q / grass_layer_size, 0.0), 5.0).rgb * fw[k];
			}
		}
		float r_kind = rnd(s);
		float dry = dry_face;
		float lush = 1.0 - dry;
		// The original vertex grass remains the continuous field. The previous short-base range plus a
		// second 0.68..0.88 multiplier made it look clipped/buried beneath the authored plant accents.
		float height_roll = rnd(s);
		// Keep the same height range and cell-scale patch multiplier, but avoid five synchronized tops.
		// Optional flowers replace height below; the independent plant and litter paths are unchanged.
		if (kind == 0.0) height_roll = fract(height_roll + card_shape.x);
		float height = mix(grass_height_metres.x, grass_height_metres.y, height_roll)
			* 1.15 * (0.75 + 0.35 * lush);
		float width = 0.8 + 0.4 * rnd(s);
		float yaw = rnd(s) * 6.2831853;
		float lean_a = rnd(s) * 6.2831853;
		float lean = 0.05 + 0.12 * rnd(s);
		float v = rnd(s);
		if (kind == 0.0) {
			v_dry = dry;
			float flower = blade_underlayer ? 0.0 : step(r_kind, f2.g * 0.12);
			v_cell = flower > 0.5 ? (rnd(s) < 0.75 ? cell_flower : cell_herb) : (dry > 0.62 ? cell_dry : (r_kind > 0.65 ? cell_meadow : cell_grass));
			v_tint = tint_face * (0.68 + 0.32 * v);
			if (flower > 0.5) {
				v_kind = 1.0; // petals use their atlas silhouette, not the grass blade mask
				height = 0.38 + 0.2 * rnd(s);
				float p = rnd(s);
				v_tint = p < 0.3 ? vec3(1.0, 0.82, 0.12) : p < 0.55 ? vec3(0.95, 0.95, 0.9) : p < 0.75 ? vec3(0.62, 0.38, 0.92) : p < 0.9 ? vec3(0.95, 0.35, 0.22) : vec3(1.0, 0.62, 0.18);
			}
			if (blade_underlayer) {
				// Smooth patches and the existing biome/canopy cover create low, tall, damp and dry
				// groups without altering the deterministic scatter, density or draw budget.
				height *= mix(grass_patch_height.x, grass_patch_height.y, patchy);
				vec3 open_tint = mix(vec3(0.90, 0.98, 1.04), vec3(1.10, 1.00, 0.86), dry);
				vec3 shade_tint = vec3(0.82, 0.94, 0.96);
				v_tint *= mix(open_tint, shade_tint, clamp(f2.b, 0.0, 1.0));
			}
		} else if (kind == 1.0) {
			// Forest floor plants: ferns, herbs and seedlings, in the shade's greens.
			float p = rnd(s);
			v_cell = p < 0.6 ? cell_fern : p < 0.85 ? cell_herb : cell_leaves_a;
			height = p < 0.55 ? 0.3 + 0.3 * rnd(s) : p < 0.8 ? 0.16 + 0.18 * rnd(s) : 0.3 + 0.3 * rnd(s);
			width = p < 0.55 ? 0.8 + 0.35 * rnd(s) : 0.7 + 0.3 * rnd(s);
			lean = p < 0.55 ? 0.45 + 0.35 * rnd(s) : 0.15 + 0.25 * rnd(s);
			v_tint = vec3(0.85 + 0.3 * v, 0.95 + 0.2 * v, 0.75 + 0.2 * v) * (p < 0.55 ? 1.0 : 0.9);
		} else {
			// Litter: dead leaves of the trees around, sprigs and twigs; colour comes from the leaf's own shading.
			float p = rnd(s);
			v_cell = p < 0.2 ? cell_leaves_a : p < 0.4 ? cell_leaves_b : p < 0.55 ? cell_leaves_c : p < 0.65 ? cell_leaves_d : p < 0.85 ? cell_needles : cell_twigs;
			float q = rnd(s);
			v_tint = q < 0.3 ? vec3(0.45, 0.3, 0.16) : q < 0.55 ? vec3(0.5, 0.38, 0.22) : q < 0.75 ? vec3(0.32, 0.22, 0.13) : q < 0.9 ? vec3(0.55, 0.45, 0.25) : vec3(0.42, 0.25, 0.12);
			v_tint *= 0.8 + 0.4 * v;
			if (p >= 0.65) {
				v_tint *= vec3(0.8, 0.75, 0.7);
			}
			height = 1.0;
			width = 0.5 + 0.45 * rnd(s);
		}
		if (v_kind == 0.0) {
			// Resting lean varies independently of wind, including in calm weather. At the tip it is at most
			// .32 * live_height; the root is still exactly fixed. No density or distance factor changes shape.
			lean_a += card_shape.w * 6.2831853;
			lean = mix(0.08, 0.32, card_shape.z);
		}

		float t = COLOR.a;
		float co = cos(yaw), sn = sin(yaw);
		float live_height = height * size;
		// Unlike VERTEX.xz - UV2, CUSTOM0 is constant along each side: taper never moves the sampled foot.
		vec2 foot = vec2(co * CUSTOM0.x - sn * CUSTOM0.y, sn * CUSTOM0.x + co * CUSTOM0.y) * width * (0.5 + 0.5 * size);
		vec2 opposite_foot = vec2(co * CUSTOM0.z - sn * CUSTOM0.w, sn * CUSTOM0.z + co * CUSTOM0.w) * width * (0.5 + 0.5 * size);
		vec3 foot_point = base;
		bool footprint_supported = true;
		if (contact_grass) {
			vec3 foot_normal;
			footprint_supported = hu_drawn_contact_at(base + tx * foot.x + tz * foot.y, CAMERA_POSITION_WORLD, foot_point, foot_normal);
			// Both sides make the same accept/reject decision, so one missing endpoint cannot stretch a card to
			// the collapsed position. The opposite endpoint needs address availability, not another full grid read.
			if (footprint_supported) footprint_supported = hu_has_drawn_contact_at(base + tx * opposite_foot.x + tz * opposite_foot.y);
		}
		vec3 p3 = VERTEX;
		vec2 root = vec2(0.0);
		vec2 bend = vec2(0.0);
		if (kind == 2.0) {
			// Flat pieces: the mesh is already lying down; scale and turn it.
			p3.xz = vec2(co * p3.x - sn * p3.z, sn * p3.x + co * p3.z) * width * (0.4 + 0.6 * size);
			p3.y *= size;
		} else {
			// UV2 stores only the authored growth bend. Root spacing and blade width remain unchanged.
			vec2 stem = p3.xz - UV2;
			root = vec2(co * stem.x - sn * stem.y, sn * stem.x + co * stem.y) * width * (0.5 + 0.5 * size);
			vec2 growth = UV2;
			float rest_curve = t * t;
			if (v_kind == 0.0) {
				// Retain the authored endpoint and vary where the card curves. Both factors stay in [0,1],
				// so the growth displacement cannot exceed the original mesh's tip envelope. Three segments
				// remain three segments; derivative lighting follows these actual deformed triangles.
				growth *= mix(1.0, t, card_shape.y);
				rest_curve = mix(t * t, t, card_shape.y * 0.65);
			}
			p3.xz = stem + growth * height * size;
			p3.xz = vec2(co * p3.x - sn * p3.z, sn * p3.x + co * p3.z) * width * (0.5 + 0.5 * size);
			p3.y *= height * size;
			bend = vec2(cos(lean_a), sin(lean_a)) * lean * t * t * live_height;
			if (v_kind == 0.0) bend = vec2(cos(lean_a), sin(lean_a)) * lean * rest_curve * live_height;
			// Wind, along the ground here: gusts as patches of the macro noise drifting downwind, and a ripple along it.
			vec2 wd = normalize(vec2(dot(hu_wind.xyz, tx), dot(hu_wind.xyz, tz)) + vec2(1e-5));
			float gust = texture(macro_noise, wp / 70.0 - wd * TIME * 0.085).a;
			float ripple = sin(dot(wp, wd) * 0.45 - TIME * 2.4 + v * 1.2) * 0.5 + 0.5;
			float w = hu_wind.w * (0.2 + 1.2 * smoothstep(0.3, 0.75, gust)) * (0.55 + 0.45 * ripple) * (kind == 1.0 ? 0.4 : 1.0);
			bend += wd * w * 0.28 * t * t * live_height;
			bend += vec2(-wd.y, wd.x) * sin(TIME * 5.1 + v * 30.0 + wp.x * 0.7) * 0.012 * t * hu_wind.w * size;
			// Bend away from the walker's feet.
			if (trample.w > 0.0) {
				vec3 off = base + tx * p3.x + tz * p3.z - trample.xyz;
				vec2 away = vec2(dot(off, tx), dot(off, tz));
				float dd = length(away);
				float k = (1.0 - smoothstep(trample.w * 0.3, trample.w, dd)) * step(abs(dot(base - trample.xyz, up)), 1.5);
				bend += away / max(dd, 1e-3) * k * 0.6 * t * live_height;
			}
		}
		float b = length(bend);
		p3.xz += bend;
		p3.y -= min(b * b / max(live_height, 0.05), live_height * 0.6) * t;

		// Bound grass grows from each exact Cartesian triangle endpoint, with no burial or double translation.
		// This is endpoint support, not a guarantee of clearance across a wide footprint or a terrain crease.
		// Unbound plants and litter retain their existing heightfield offsets.
		float root_height = kind == 2.0 ? 0.0 : -dot(tx * root.x + tz * root.y, ground_normal) / max(dot(up, ground_normal), 0.15);
		vec3 world = base + up * (root_height + (kind == 2.0 ? 0.004 : -0.005) + p3.y) + tx * p3.x + tz * p3.z;
		if (contact_grass) world = foot_point + up * p3.y + tx * (p3.x - foot.x) + tz * (p3.z - foot.y);
		if (footprint_supported) {
			v_world = world;
			VERTEX = (VIEW_MATRIX * vec4(world, 1.0)).xyz;
			vec3 nl = kind == 2.0 ? vec3(0.0, 1.0, 0.0) : normalize(vec3(co * NORMAL.x - sn * NORMAL.z, NORMAL.y * 2.5, sn * NORMAL.x + co * NORMAL.z));
			vec3 n = normalize(tx * nl.x + up * nl.y + tz * nl.z);
			NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);
			v_normal = n;
		} else {
			VERTEX = vec3(0.0);
			NORMAL = vec3(0.0, 0.0, 1.0);
		}

		// The ground's own colour at the tuft's foot, as the terrain shader draws its grass layer.
		float lum = dot(detail, vec3(0.2126, 0.7152, 0.0722));
		v_ground = clamp(lum / 0.1, 0.4, 1.8) * blade_mean * tint_face;
		if (blade_underlayer && kind == 0.0) v_ground *= vec3(0.82, 0.90, 1.10);
	}
}

void fragment() {
	// Take derivatives before any cutout discard. These are the actual wind/trample-deformed triangles.
	vec3 geometry_normal = cross(dFdx(v_world), dFdy(v_world));
	float normal_length = dot(geometry_normal, geometry_normal);
	geometry_normal = normal_length > 1e-12 ? geometry_normal * inversesqrt(normal_length) : v_normal;
	// Thin sheets have two lit sides: orient the visible face toward the eye, then soften it slightly toward up.
	// Back lighting is handled separately by transmission rather than replacing both sides with a sky normal.
	geometry_normal *= dot(geometry_normal, CAMERA_POSITION_WORLD - v_world) < 0.0 ? -1.0 : 1.0;
	vec3 blade_normal = normalize(geometry_normal + hu_up(v_world) * 0.2);
	vec2 uv = vec2(mix(v_cell.x, v_cell.z, UV.x), mix(v_cell.y, v_cell.w, UV.y));
	vec4 tex = texture(atlas, uv);
	float alpha = tex.a;
	// The atlas owns the connected blade silhouette, including gaps at the roots. No second independent mask.
	// Keep cutouts in the opaque pass: water needs their colour and depth in the same screen snapshot.
	// Writing ALPHA moved the cards after screen-texture water and left dark card-shaped silhouettes underwater.
	if (alpha < 0.5) discard;
	vec3 albedo;
	if (v_kind > 1.5) {
		// Dead leaves and twigs: the painted shading, faded to the litter's browns.
		float lum = dot(tex.rgb, vec3(0.2126, 0.7152, 0.0722));
		albedo = v_tint * clamp(lum * 2.2, 0.3, 1.25);
	} else {
		vec3 blade = tex.rgb * v_tint * (0.78 + 0.3 * v_t);
		albedo = v_kind > 0.5 ? blade : mix(v_ground, blade, smoothstep(0.0, 0.45, v_t));
	}
	ALBEDO = albedo;
	NORMAL = normalize((VIEW_MATRIX * vec4(v_kind < 0.5 ? blade_normal : v_normal, 0.0)).xyz);
	ROUGHNESS = v_kind < 0.5 ? 0.72 : 0.85;
	SPECULAR = 0.2;
	AO = 1.0 - 0.38 * smoothstep(0.15, 0.85, v_cover);
	if (v_kind > 1.5) {
		SPECULAR = 0.08;
	}
	AO_LIGHT_AFFECT = 0.0;
	vec3 a = ALBEDO;
	vec4 fog;
	hu_aerial(v_world, CAMERA_POSITION_WORLD, 6, a, fog);
	ALBEDO = a;
	FOG = fog;

}

void light() {
	float ndl = dot(NORMAL, LIGHT);
	float cloud = LIGHT_IS_DIRECTIONAL ? hu_cloud_shadow(v_world, normalize((INV_VIEW_MATRIX * vec4(LIGHT, 0.0)).xyz)) : 1.0;
	vec3 light = LIGHT_COLOR * ATTENUATION * cloud / HU_PI;
	DIFFUSE_LIGHT += light * clamp(ndl, 0.0, 1.0);
	if (v_kind < 0.5 && ndl > 0.0) {
		SPECULAR_LIGHT += LIGHT_COLOR * ATTENUATION * cloud * hu_specular(NORMAL, VIEW, LIGHT, ROUGHNESS) * 0.3;
	}
	if (v_kind < 1.5) {
		// Light through the blades: the tips glow looking toward a low sun.
		float toward = clamp(dot(-VIEW, LIGHT), 0.0, 1.0);
		float wrap = clamp(-ndl * 0.6 + 0.4, 0.0, 1.0);
		float transmission = translucency * (blade_underlayer ? 0.5 : 1.0);
		vec3 transmission_colour = blade_underlayer ? mix(vec3(0.88, 1.0, 0.82), vec3(1.0, 0.94, 0.72), v_dry) : vec3(0.9, 1.05, 0.55);
		DIFFUSE_LIGHT += light * transmission * (pow(toward, 4.0) * 1.4 * v_t + wrap * 0.35) * transmission_colour;
	}
}

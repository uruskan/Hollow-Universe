// Grass and ground cover of the surface view (GROUND.md §4.6, docs/WHOLE_WORLD.md §10.3): no plant models, a field of
// small cards drawn by the GPU on a grid that follows the camera. Every instance is a cell of a cube face (one grid a
// face near a seam: GrassField.cs), so what grows in it (place, size, lean, colour, dryness, kind) comes from a hash of
// the face and the cell and never shifts as the grid moves; a cell whose middle lies beyond its face's edge is left to
// the neighbouring face's grid. The cells' corners lie on the drawn ground (a lattice texture made on the CPU, with the
// grass density); the moisture, flowers, forest cover and small plants come from a second texture. Each tuft stands along
// up from the planet's centre, turned in the ground's own plane; its colour is measured in metres on the face, as the
// terrain's grass layer under it.
// - Layer 0, upright: grass tufts (fewer, looser blades where grass is sparse), meadow grass, dry grass, flowers;
//   under trees ferns, herbs and seedlings. Blades are hollow-orbit's painted cards, coloured like the ground's grass
//   layer under them, with normals that mostly point up so a field lights as one; the base takes the ground's colour.
//   Wind bends them with gusts that roll across the field; they bend away from the walker's feet.
// - Layer 1, flat: leaf litter, needles and twigs lying on the forest floor (the atlas's leaf and twig cells, faded
//   to browns).
// Far away everything shrinks into the ground.
shader_type spatial;
render_mode cull_disabled, depth_draw_opaque, diffuse_burley, specular_schlick_ggx, skip_vertex_transform;

#include "res://shaders/surface_atmosphere.gdshaderinc"
#include "res://shaders/surface_planet.gdshaderinc"
#include "res://shaders/surface_clouds.gdshaderinc"
#include "res://shaders/surface_grass.gdshaderinc"

global uniform vec4 hu_wind;

uniform int layer = 0;              // 0 upright tufts and plants, 1 flat litter
uniform bool blade_underlayer = false; // authored crowns supply the larger species above this closed short sward
uniform int grid_width = 180;
uniform ivec2 grid_offset;          // the grid's first cell in the lattice
uniform float far_radius = 42.0;    // blades are gone here
uniform float shrink_start = 24.0;  // and start shrinking here
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

varying vec3 v_world;
varying vec4 v_cell;
varying vec3 v_tint;
varying vec3 v_ground;
varying float v_t;
varying float v_cloud;
varying float v_kind;               // 0 grass, 1 plant, 2 litter (faded to browns)
varying float v_cover;              // forest cover: the canopy keeps sky light off the floor
varying float v_seed;               // per tuft, for the ragged outline of its blades
varying vec3 v_normal;              // world normal, the same on both faces of a blade

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
	ivec2 g = lattice_cell + c;
	uint s = hash_u(uint(g.x) * 0x9e3779b9u ^ hash_u(uint(g.y) + 0x632be5abu) ^ uint(layer) * 0x51ed27u ^ uint(face) * 0x27d4eb2fu);
	vec2 jit = vec2(rnd(s), rnd(s));
	// The tuft's foot on the ground: between its cell's four corners.
	vec4 n00 = texelFetch(lattice, c, 0);
	vec4 n10 = texelFetch(lattice, c + ivec2(1, 0), 0);
	vec4 n01 = texelFetch(lattice, c + ivec2(0, 1), 0);
	vec4 n11 = texelFetch(lattice, c + ivec2(1, 1), 0);
	vec4 f = mix(mix(n00, n10, jit.x), mix(n01, n11, jit.x), jit.y);
	vec4 f2 = texelFetch(lattice_cover, c, 0);
	vec3 base = anchor + f.xyz;
	float dist = length(base - CAMERA_POSITION_WORLD);
	// Only the cells whose middle lies on this face: the neighbour's grid draws the rest.
	float inside = (g.x >= 0 && g.y >= 0 && g.x < face_cells && g.y < face_cells) ? 1.0 : 0.0;
	vec2 wp = face_origin + (vec2(c) + jit) * cell_metres;
	// The ground's plane at the tuft: up from the planet's centre, across along the face's cells.
	vec3 up = hu_up(base);
	vec3 across = n10.xyz - n00.xyz;
	vec3 tx = normalize(across - up * dot(across, up) + vec3(1e-6));
	vec3 tz = cross(tx, up);

	// What grows in this cell. Thin grass grows in patches, not evenly spread.
	float density = f.w;
	float patchy = texture(macro_noise, wp / 5.3 + vec2(0.71, 0.29)).r;
	float patches = mix(0.25 + 1.5 * smoothstep(0.3, 0.7, patchy), 1.0, smoothstep(0.55, 0.9, density)); // dense meadows stay closed
	density = clamp(density * patches, 0.0, 1.0) * step(0.02, density);
	float roll = rnd(s);
	float kind = -1.0; // -1 nothing, 0 grass, 1 plant, 2 litter
	if (layer == 0) {
		if (roll < density) {
			kind = 0.0;
		} else if (!blade_underlayer && roll < density + (1.0 - density) * f2.a * 0.3) {
			kind = 1.0;
		}
	} else if (roll < f2.b * 0.85) {
		kind = 2.0;
	}
	// The graphics settings thin the tufts out by a hash of their own (the cell's other draws stay as they were).
	float keep = float(hash_u(s ^ 0xA511E9B3u) & 0xFFFFu) / 65535.0;
	kind = keep <= density_scale ? kind : -1.0;
	float size = inside * step(0.0, kind) * (1.0 - smoothstep(shrink_start, far_radius, dist));
	v_kind = kind;
	v_cover = f2.b;
	v_seed = float(s & 1023u) * 0.6180339;
	v_t = COLOR.a;
	v_world = vec3(0.0);
	v_cell = cell_grass;
	v_tint = vec3(1.0);
	v_ground = vec3(0.0);
	v_normal = up;
	// Each card has eight vertices: drop the cards a sparse tuft doesn't have, so thin grass is loose blades.
	int card = VERTEX_ID / 8;
	float cards = 5.0;
	if (kind == 0.0) {
		cards = 1.0 + floor(clamp(density * 4.6 + rnd(s) * 0.9, 0.0, 4.0));
	} else if (kind == 1.0) {
		cards = 2.0 + floor(rnd(s) * 2.0);
	}
	if (size <= 0.0 || float(card) >= cards) {
		VERTEX = vec3(0.0);
		NORMAL = vec3(0.0, 0.0, 1.0);
	} else {
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
		float height = (0.28 + 0.26 * rnd(s)) * (0.7 + 0.45 * density) * (0.75 + 0.35 * lush);
		float width = 0.8 + 0.4 * rnd(s);
		float yaw = rnd(s) * 6.2831853;
		float lean_a = rnd(s) * 6.2831853;
		float lean = 0.05 + 0.12 * rnd(s);
		float v = rnd(s);
		if (kind == 0.0) {
			float flower = blade_underlayer ? 0.0 : step(r_kind, f2.g * 0.12);
			v_cell = flower > 0.5 ? (rnd(s) < 0.75 ? cell_flower : cell_herb) : (dry > 0.62 ? cell_dry : (r_kind > 0.65 ? cell_meadow : cell_grass));
			v_tint = tint_face * (0.68 + 0.32 * v);
			if (flower > 0.5) {
				v_kind = 1.0; // petals use their atlas silhouette, not the grass blade mask
				height = 0.38 + 0.2 * rnd(s);
				float p = rnd(s);
				v_tint = p < 0.3 ? vec3(1.0, 0.82, 0.12) : p < 0.55 ? vec3(0.95, 0.95, 0.9) : p < 0.75 ? vec3(0.62, 0.38, 0.92) : p < 0.9 ? vec3(0.95, 0.35, 0.22) : vec3(1.0, 0.62, 0.18);
			}
			// Where grass is thin, a tuft is a few narrow cards: a handful of loose blades, not a block.
			width *= mix(0.32, 1.0, smoothstep(0.08, 0.65, density));
			height *= mix(0.8, 1.0, smoothstep(0.08, 0.65, density));
			if (blade_underlayer) {
				height *= 0.68 + 0.2 * patchy;
				v_tint *= vec3(0.82, 0.90, 1.10);
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

		float t = COLOR.a;
		float co = cos(yaw), sn = sin(yaw);
		vec3 p3 = VERTEX;
		vec2 bend = vec2(0.0);
		if (kind == 2.0) {
			// Flat pieces: the mesh is already lying down; scale and turn it.
			p3.xz = vec2(co * p3.x - sn * p3.z, sn * p3.x + co * p3.z) * width * (0.4 + 0.6 * size);
			p3.y *= size;
		} else {
			p3.xz = vec2(co * p3.x - sn * p3.z, sn * p3.x + co * p3.z) * width * (0.5 + 0.5 * size);
			p3.y *= height * size;
			bend = vec2(cos(lean_a), sin(lean_a)) * lean * t * t * height;
			// Wind, along the ground here: gusts as patches of the macro noise drifting downwind, and a ripple along it.
			vec2 wd = normalize(vec2(dot(hu_wind.xyz, tx), dot(hu_wind.xyz, tz)) + vec2(1e-5));
			float gust = texture(macro_noise, wp / 70.0 - wd * TIME * 0.085).a;
			float ripple = sin(dot(wp, wd) * 0.45 - TIME * 2.4 + v * 1.2) * 0.5 + 0.5;
			float w = hu_wind.w * (0.2 + 1.2 * smoothstep(0.3, 0.75, gust)) * (0.55 + 0.45 * ripple) * (kind == 1.0 ? 0.4 : 1.0);
			bend += wd * w * 0.28 * t * t * height;
			bend += vec2(-wd.y, wd.x) * sin(TIME * 5.1 + v * 30.0 + wp.x * 0.7) * 0.012 * t * hu_wind.w;
			// Bend away from the walker's feet.
			if (trample.w > 0.0) {
				vec3 off = base + tx * p3.x + tz * p3.z - trample.xyz;
				vec2 away = vec2(dot(off, tx), dot(off, tz));
				float dd = length(away);
				float k = (1.0 - smoothstep(trample.w * 0.3, trample.w, dd)) * step(abs(dot(base - trample.xyz, up)), 1.5);
				bend += away / max(dd, 1e-3) * k * 0.6 * t * height;
			}
		}
		float b = length(bend);
		p3.xz += bend;
		p3.y -= min(b * b / max(height, 0.05), height * 0.6) * t;

		vec3 world = base + up * ((kind == 2.0 ? 0.004 : -0.03) + p3.y) + tx * p3.x + tz * p3.z;
		v_world = world;
		VERTEX = (VIEW_MATRIX * vec4(world, 1.0)).xyz;
		vec3 nl = kind == 2.0 ? vec3(0.0, 1.0, 0.0) : normalize(vec3(co * NORMAL.x - sn * NORMAL.z, NORMAL.y * 2.5, sn * NORMAL.x + co * NORMAL.z));
		vec3 n = normalize(tx * nl.x + up * nl.y + tz * nl.z);
		NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);
		v_normal = n;

		// The ground's own colour at the tuft's foot, as the terrain shader draws its grass layer.
		float lum = dot(detail, vec3(0.2126, 0.7152, 0.0722));
		v_ground = clamp(lum / 0.1, 0.4, 1.8) * blade_mean * tint_face;
		if (blade_underlayer && kind == 0.0) v_ground *= vec3(0.82, 0.90, 1.10);
	}
}

void fragment() {
	vec2 uv = vec2(mix(v_cell.x, v_cell.z, UV.x), mix(v_cell.y, v_cell.w, UV.y));
	vec4 tex = texture(atlas, uv);
	float alpha = tex.a;
	if (v_kind < 0.5) {
		// Grass: blades of different heights (a ragged top) and a tuft that narrows upward, so no card reads as a block.
		float x = UV.x * 9.0 + v_seed;
		float cell_x = floor(x);
		float h0 = fract(sin(cell_x * 12.9898 + v_seed * 3.1) * 43758.5453);
		float h1 = fract(sin((cell_x + 1.0) * 12.9898 + v_seed * 3.1) * 43758.5453);
		float top = 0.42 + 0.58 * mix(h0, h1, smoothstep(0.0, 1.0, fract(x)));
		alpha *= step(UV.y, top) * step(abs(UV.x - 0.5), 0.5 - 0.22 * UV.y);
		// The painted atlas is solid at the roots. Separate individual blades all the way down, otherwise the
		// base of every upright card reads as a flat rectangular fence at shorelines and against bare soil.
		float blade_centre = 0.5 + sin(cell_x * 1.7 + v_seed + UV.y * 3.0) * 0.16 * UV.y;
		float blade_width = mix(0.32, 0.07, UV.y);
		alpha *= 1.0 - smoothstep(blade_width, blade_width + 0.035, abs(fract(x) - blade_centre));
	}
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
	// Blades are thin: both faces take the sky's light from above. (Godot would turn the normal down on back faces,
	// leaving every card seen from behind black in the shade.)
	NORMAL = normalize((VIEW_MATRIX * vec4(v_normal, 0.0)).xyz);
	ROUGHNESS = 0.85;
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
	v_cloud = hu_cloud_shadow(v_world);
}

void light() {
	float ndl = dot(NORMAL, LIGHT);
	float cloud = LIGHT_IS_DIRECTIONAL ? v_cloud : 1.0;
	vec3 light = LIGHT_COLOR * ATTENUATION * cloud / HU_PI;
	DIFFUSE_LIGHT += light * clamp(ndl, 0.0, 1.0);
	if (v_kind < 1.5) {
		// Light through the blades: the tips glow looking toward a low sun.
		float toward = clamp(dot(-VIEW, LIGHT), 0.0, 1.0);
		float wrap = clamp(-ndl * 0.6 + 0.4, 0.0, 1.0);
		float transmission = translucency * (blade_underlayer ? 0.5 : 1.0);
		vec3 transmission_colour = blade_underlayer ? vec3(0.94, 1.0, 0.85) : vec3(0.9, 1.05, 0.55);
		DIFFUSE_LIGHT += light * transmission * (pow(toward, 4.0) * 1.4 * v_t + wrap * 0.35) * transmission_colour;
	}
}

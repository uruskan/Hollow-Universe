// The walls' rock skins (docs/TERRAIN_SCENERY_PLAN.md T2, CliffField.Walls.cs): one of the ground's own tileable
// rock scans from its texture arrays, projected from the world's three axes (the skin has no texture coordinates), with
// its normals blended by the "whiteout" rule so the rock's relief lights from every side, and its height darkening
// the hollows. Over it the rock's beds, painted here so their lines stay sharp at any distance: bands a few metres
// thick by height above the planet's centre, drifting slowly, each its own shade, with a dark groove and a lit ledge
// where one bed meets the next.
shader_type spatial;

uniform sampler2DArray albedo_array : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray normal_array : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray rough_height_array : filter_linear_mipmap_anisotropic, repeat_enable; // r roughness, g height
uniform int layer = 0;            // the texture's place in the arrays
uniform float metres = 5.0;       // metres one repeat covers
uniform vec2 flip = vec2(1.0);    // the texture's normal-map convention (GroundLayers)
uniform vec3 gain = vec3(1.0);    // colour gain over the scan's own
uniform float sharpness = 4.0;    // how quickly the projections hand over across an edge
uniform float hollows = 0.35;     // how much the texture's low ground darkens
uniform vec3 planet_centre = vec3(0.0);   // render space
uniform float planet_radius = 1200.0;
uniform float bed_metres = 3.2;   // a bed's thickness
uniform float strata = 1.0;       // how strongly the beds show (sandstone 1, crystalline rock less)

varying vec3 world_position;
varying vec3 world_normal;

void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

vec3 unpack(vec4 sample) {
	vec3 n = sample.xyz * 2.0 - 1.0;
	n.xy *= flip;
	return n;
}

void fragment() {
	vec3 n = normalize(world_normal);
	vec3 w = pow(abs(n), vec3(sharpness));
	w /= w.x + w.y + w.z;
	vec3 p = world_position / metres;
	float l = float(layer);
	vec3 ux = vec3(p.zy, l), uy = vec3(p.xz, l), uz = vec3(p.xy, l);

	vec3 albedo = texture(albedo_array, ux).rgb * w.x + texture(albedo_array, uy).rgb * w.y + texture(albedo_array, uz).rgb * w.z;
	vec2 rh = texture(rough_height_array, ux).rg * w.x + texture(rough_height_array, uy).rg * w.y + texture(rough_height_array, uz).rg * w.z;

	// Whiteout blend: each projection's tangent normal swizzled into the world and added to the surface's.
	vec3 tx = unpack(texture(normal_array, ux));
	vec3 ty = unpack(texture(normal_array, uy));
	vec3 tz = unpack(texture(normal_array, uz));
	tx = vec3(tx.xy + n.zy, abs(tx.z) * n.x);
	ty = vec3(ty.xy + n.xz, abs(ty.z) * n.y);
	tz = vec3(tz.xy + n.xy, abs(tz.z) * n.z);
	vec3 blended = normalize(tx.zyx * w.x + ty.xzy * w.y + tz.xyz * w.z);

	// The beds: a band's place from its height, drifting by a metre or two over tens along the wall (the texture's
	// height from a coarse mip, so the drift is smooth, not the scan's own detail).
	vec3 radial = world_position - planet_centre;
	float height = length(radial) - planet_radius;
	vec3 upward = radial / max(length(radial), 1.0);
	float drift = textureLod(rough_height_array, vec3(world_position.xz / 173.0 + world_position.y / 211.0, l), 7.0).g;
	float t = height / bed_metres + drift * 0.7;
	float s = fract(t);
	float bed = floor(t);
	float shade = 0.72 + 0.5 * fract(sin(bed * 12.9898 + 4.1) * 43758.5453);
	float groove = smoothstep(0.0, 0.16, s) * smoothstep(1.0, 0.86, s);
	float bands = mix(1.0, shade * mix(0.62, 1.0, groove), strata);
	// A ledge catches the light at a bed's top, an undercut hides under it.
	blended = normalize(blended + upward * strata * (smoothstep(0.82, 0.97, s) * 0.6 - smoothstep(0.08, 0.0, s) * 0.5));

	ALBEDO = albedo * gain * mix(1.0 - hollows, 1.0, rh.g) * bands;
	ROUGHNESS = rh.r;
	METALLIC = 0.0;
	NORMAL = normalize((VIEW_MATRIX * vec4(blended, 0.0)).xyz);
}

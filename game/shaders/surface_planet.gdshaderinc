// The planet under everything drawn (docs/WHOLE_WORLD.md §10.1–10.2): where its centre is, which way is up at a point,
// how high a point stands, and the cube faces the ground's texture coordinates are measured on. Set by PlanetGlobals.cs
// for the view that draws a planet (the ground game's whole world, origin at its centre; the look-dev view's tangent
// frame, its centre thousands of kilometres below); with no planet the centre lies far below, so up is +Y.

#ifndef HU_PLANET_INCLUDED
#define HU_PLANET_INCLUDED

global uniform vec3 hu_planet_centre;   // render space
global uniform float hu_ground_radius;  // the mean radius heights are measured from, metres
global uniform float hu_planet_k;       // |centre|² − radius², from the CPU in double precision (heights below)
global uniform mat3 hu_render_to_body;  // render axes to the body's axes (the cube faces are the body's)

// Up at a render-space point: away from the planet's centre.
vec3 hu_up(vec3 p) {
	return normalize(p - hu_planet_centre);
}

// Height of a render-space point above the mean radius, exact to a fraction of a millimetre on a planet of any size:
// (|p − c|² − R²) / (|p − c| + R), with |p − c|² − R² expanded so that no large number is subtracted from another.
float hu_height(vec3 p) {
	return (dot(p, p) - 2.0 * dot(p, hu_planet_centre) + hu_planet_k) / (length(p - hu_planet_centre) + hu_ground_radius);
}

// The cube-sphere's faces (Hollow.Surface.CubeSphere): 0 +X, 1 −X, 2 +Y, 3 −Y, 4 +Z, 5 −Z, each with its outward normal
// and axes U, V (U × V = N), in the body frame.
const vec3 HU_FACE_N[6] = { vec3(1.0, 0.0, 0.0), vec3(-1.0, 0.0, 0.0), vec3(0.0, 1.0, 0.0), vec3(0.0, -1.0, 0.0), vec3(0.0, 0.0, 1.0), vec3(0.0, 0.0, -1.0) };
const vec3 HU_FACE_U[6] = { vec3(0.0, 0.0, -1.0), vec3(0.0, 0.0, 1.0), vec3(1.0, 0.0, 0.0), vec3(1.0, 0.0, 0.0), vec3(1.0, 0.0, 0.0), vec3(-1.0, 0.0, 0.0) };
const vec3 HU_FACE_V[6] = { vec3(0.0, 1.0, 0.0), vec3(0.0, 1.0, 0.0), vec3(0.0, 0.0, -1.0), vec3(0.0, 0.0, 1.0), vec3(0.0, 1.0, 0.0), vec3(0.0, 1.0, 0.0) };

// The body-frame direction of a render-space point from the planet's centre.
vec3 hu_body_direction(vec3 p) {
	return normalize(hu_render_to_body * (p - hu_planet_centre));
}

// Metres along face f of a body direction (TileMesh.FaceMetresOf): radius × atan(d·U / d·N), likewise along V. Exact on
// a world a few kilometres across; on a planet of thousands of kilometres a direction from the centre is good to half a
// metre, so the ground's own face takes its coordinates from its vertices instead (UV2).
vec2 hu_face_metres(vec3 d, int f) {
	float n = dot(d, HU_FACE_N[f]);
	return hu_ground_radius * vec2(atan(dot(d, HU_FACE_U[f]), n), atan(dot(d, HU_FACE_V[f]), n));
}

// The faces a body direction is measured on and their weights, in axis order (x, y, z): the face the direction lies
// in weighs 1 before normalising, a neighbour within `band` metres of the seam between them up to 1 at the seam, so
// textures blend across the seam instead of breaking. Faces whose weight is 0 are not sampled.
void hu_face_blend(vec3 d, float band, out ivec3 faces, out vec3 weights) {
	vec3 a = abs(d);
	float m = max(a.x, max(a.y, a.z));
	// Metres from the seam with the largest axis: (m − a) × R / √2, exact in the middle of an edge.
	vec3 w = clamp(1.0 - (m - a) * hu_ground_radius / (1.41421356 * band), 0.0, 1.0);
	faces = ivec3(d.x >= 0.0 ? 0 : 1, d.y >= 0.0 ? 2 : 3, d.z >= 0.0 ? 4 : 5);
	weights = w / (w.x + w.y + w.z);
}

// The directions in which face f's metres grow at body direction d, in render axes: tangent (u) and bitangent (v).
void hu_face_axes(vec3 d, int f, out vec3 t, out vec3 b) {
	vec3 n = HU_FACE_N[f];
	float dn = dot(d, n);
	mat3 to_render = transpose(hu_render_to_body);
	t = to_render * normalize(HU_FACE_U[f] * dn - n * dot(d, HU_FACE_U[f]));
	b = to_render * normalize(HU_FACE_V[f] * dn - n * dot(d, HU_FACE_V[f]));
}

#endif

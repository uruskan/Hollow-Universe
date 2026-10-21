// A presentation root follows the exact regular triangle currently drawn by TerrainLod, including each
// vertex's own morph factor. Skirts, missing tiles and retired contact slots never provide root support.
#ifndef HU_TERRAIN_CONTACT_INCLUDED
#define HU_TERRAIN_CONTACT_INCLUDED
uniform bool terrain_contact_enabled = false;
uniform sampler2DArray terrain_contact_fine : filter_nearest, repeat_disable;
uniform sampler2DArray terrain_contact_morph : filter_nearest, repeat_disable;
uniform sampler2D terrain_contact_lookup : filter_nearest, repeat_disable;
uniform sampler2D terrain_contact_meta : filter_nearest, repeat_disable;
uniform int terrain_contact_map_side = 1;

int hu_contact_slot(int face_index, vec2 face_uv) {
    if (face_index < 0 || face_index >= 6 || any(lessThan(face_uv, vec2(0.0))) || any(greaterThan(face_uv, vec2(1.0)))) return -1;
    ivec2 cell = min(ivec2(floor(face_uv * float(terrain_contact_map_side))), ivec2(terrain_contact_map_side - 1));
    cell.y += face_index * terrain_contact_map_side;
    // RF stores exact integers below 2^24; adding 0.5 corrupts odd addresses above 2^23.
    int address = int(texelFetch(terrain_contact_lookup, cell, 0).r) - 1;
    if (address < 0) return -1;
    int slot = address % 128;
    int generation = address / 128;
    if (int(texelFetch(terrain_contact_meta, ivec2(4, slot), 0).w + 0.5) != generation) return -1;
    vec4 key = texelFetch(terrain_contact_meta, ivec2(0, slot), 0);
    float active = texelFetch(terrain_contact_meta, ivec2(3, slot), 0).w;
    vec2 local_uv = face_uv * key.z - key.xy;
    if (active < 0.5 || int(key.w + 0.5) != face_index || key.z < 1.0
        || any(lessThan(local_uv, vec2(0.0))) || any(greaterThan(local_uv, vec2(1.0)))) return -1;
    return slot;
}

void hu_contact_face(vec3 hint, out int face_index, out vec2 uv) {
    vec3 direction = hu_body_direction(hint);
    vec3 a = abs(direction);
    face_index = a.x >= a.y && a.x >= a.z ? (direction.x >= 0.0 ? 0 : 1)
                 : a.y >= a.z ? (direction.y >= 0.0 ? 2 : 3) : (direction.z >= 0.0 ? 4 : 5);
    float dn = dot(direction, HU_FACE_N[face_index]);
    vec2 ratio = vec2(dot(direction, HU_FACE_U[face_index]), dot(direction, HU_FACE_V[face_index])) / dn;
    uv = atan(ratio) * (2.0 / 3.14159265358979323846) + 0.5;
}

bool hu_has_drawn_contact_at(vec3 hint) {
    int face_index; vec2 uv;
    hu_contact_face(hint, face_index, uv);
    return hu_contact_slot(face_index, uv) >= 0;
}

vec3 hu_contact_vertex(ivec2 cell, int slot, vec3 origin, mat3 basis, vec2 range, vec3 camera) {
    vec3 fine = texelFetch(terrain_contact_fine, ivec3(cell, slot), 0).xyz;
    vec3 coarse = texelFetch(terrain_contact_morph, ivec3(cell, slot), 0).xyz;
    vec3 fine_world = origin + basis * fine;
    float k = clamp((length(fine_world - camera) - range.x) / max(range.y - range.x, 1.0), 0.0, 1.0);
    return origin + basis * mix(fine, coarse, k);
}

bool hu_drawn_contact(int face_index, vec2 face_uv, vec3 camera, out vec3 point, out vec3 normal) {
    point = vec3(0.0); normal = vec3(0.0, 1.0, 0.0);
    int slot = hu_contact_slot(face_index, face_uv);
    if (slot < 0) return false;
    vec4 key = texelFetch(terrain_contact_meta, ivec2(0, slot), 0);
    vec4 origin = texelFetch(terrain_contact_meta, ivec2(1, slot), 0);
    vec4 bx = texelFetch(terrain_contact_meta, ivec2(2, slot), 0);
    vec4 by = texelFetch(terrain_contact_meta, ivec2(3, slot), 0);
    vec4 bz = texelFetch(terrain_contact_meta, ivec2(4, slot), 0);
    if (by.w < 0.5 || int(key.w + 0.5) != face_index || key.z < 1.0) return false;
    vec2 local_uv = face_uv * key.z - key.xy;
    // Validate the actual address as well as the lookup. A recycled slot cannot impersonate its predecessor.
    if (any(lessThan(local_uv, vec2(0.0))) || any(greaterThan(local_uv, vec2(1.0)))) return false;
    vec2 grid = local_uv * 32.0;
    ivec2 cell = min(ivec2(floor(grid)), ivec2(31));
    vec2 fraction = grid - vec2(cell);
    mat3 basis = mat3(bx.xyz, by.xyz, bz.xyz);
    vec2 range = vec2(origin.w, bx.w);
    vec3 a = hu_contact_vertex(cell, slot, origin.xyz, basis, range, camera);
    vec3 c = hu_contact_vertex(cell + ivec2(1, 1), slot, origin.xyz, basis, range, camera);
    bool lower = fraction.x >= fraction.y;
    vec3 b = hu_contact_vertex(cell + (lower ? ivec2(1, 0) : ivec2(0, 1)), slot, origin.xyz, basis, range, camera);
    point = lower ? a * (1.0 - fraction.x) + b * (fraction.x - fraction.y) + c * fraction.y
                  : a * (1.0 - fraction.y) + b * (fraction.y - fraction.x) + c * fraction.x;
    normal = normalize(cross(b - a, c - a));
    if (dot(normal, hu_up(point)) < 0.0) normal = -normal;
    return true;
}

// Map a footprint point to the dominant face using the same equiangular cube parameterization as CubeSphere.
// The resulting contact is a Cartesian point on a triangle; it is never projected radially after interpolation.
bool hu_drawn_contact_at(vec3 hint, vec3 camera, out vec3 point, out vec3 normal) {
    int face_index; vec2 uv;
    hu_contact_face(hint, face_index, uv);
    return hu_drawn_contact(face_index, uv, camera, point, normal);
}
#endif

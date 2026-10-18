// Optional appearance inputs of the SAME complete slot/generation as drawn-terrain contact.
// The caller includes surface_planet before this file. No surface query or missing-slot fallback exists.
#ifndef HU_TERRAIN_APPEARANCE_INCLUDED
#define HU_TERRAIN_APPEARANCE_INCLUDED
#include "res://shaders/surface_terrain_contact.gdshaderinc"
uniform bool terrain_appearance_enabled = false;
uniform sampler2DArray terrain_appearance_materials : filter_nearest, repeat_disable;
uniform sampler2DArray terrain_appearance_normals : filter_nearest, repeat_disable;

struct HuTerrainAppearance {
    vec3 point;
    vec3 normal;
    vec2 face_metres;
    vec4 material0;
    vec4 material1;
    vec4 material2;
    vec4 material3;
    vec4 climate;
    float water_height;
    int face_index;
};

HuTerrainAppearance hu_empty_appearance() {
    HuTerrainAppearance value;
    value.point = vec3(0.0); value.normal = vec3(0.0, 1.0, 0.0); value.face_metres = vec2(0.0);
    value.material0 = vec4(0.0); value.material1 = vec4(0.0);
    value.material2 = vec4(0.0); value.material3 = vec4(0.0); value.climate = vec4(0.0);
    value.water_height = 0.0; value.face_index = -1;
    return value;
}

HuTerrainAppearance hu_appearance_vertex(ivec2 cell, int slot, vec3 origin, mat3 basis, vec2 range, vec3 camera) {
    HuTerrainAppearance value;
    vec3 fine = texelFetch(terrain_contact_fine, ivec3(cell, slot), 0).xyz;
    vec3 coarse = texelFetch(terrain_contact_morph, ivec3(cell, slot), 0).xyz;
    vec4 normal_fine = texelFetch(terrain_appearance_normals, ivec3(cell, slot), 0);
    vec4 normal_coarse = texelFetch(terrain_appearance_normals, ivec3(cell + ivec2(0, 33), slot), 0);
    float k = clamp((length(origin + basis * fine - camera) - range.x) / max(range.y - range.x, 1.0), 0.0, 1.0);
    value.point = origin + basis * mix(fine, coarse, k);
    // Admission permits proper rigid render frames. The terrain normalizes the mixed LOCAL normal per
    // vertex, then interpolates its transformed result. Fine native NORMAL octahedral quantization is
    // intentionally not claimed bit-exact to this source copy; the native fixture measures that difference.
    value.normal = basis * normalize(mix(normal_fine.xyz, normal_coarse.xyz, k));
    value.face_metres = vec2(normal_fine.w, normal_coarse.w);
    uvec4 a = uvec4(texelFetch(terrain_appearance_materials, ivec3(cell, slot), 0));
    vec4 last = texelFetch(terrain_appearance_materials, ivec3(cell + ivec2(0, 33), slot), 0);
    // Float32 exactly represents every packed integer here. Adding .5 corrupts odd values above 2^23.
    uvec3 b = uvec3(last.xyz);
    value.material0 = vec4(float(a.x & 255u), float((a.x >> 8u) & 255u), float(a.x >> 16u), float(a.y & 255u)) / 255.0;
    value.material1 = vec4(float((a.y >> 8u) & 255u), float(a.y >> 16u), float(a.z & 255u), float((a.z >> 8u) & 255u)) / 255.0;
    value.material2 = vec4(float(a.z >> 16u), float(a.w & 255u), float((a.w >> 8u) & 255u), float(a.w >> 16u)) / 255.0;
    value.material3 = vec4(float(b.x & 255u), float((b.x >> 8u) & 255u), float(b.x >> 16u), float(b.y & 255u)) / 255.0;
    value.climate = vec4(float((b.y >> 8u) & 255u), float(b.y >> 16u), float(b.z & 255u), float((b.z >> 8u) & 255u)) / 255.0;
    value.water_height = last.w; value.face_index = -1;
    return value;
}

bool hu_drawn_appearance(int face_index, vec2 face_uv, vec3 camera, out HuTerrainAppearance value) {
    value = hu_empty_appearance();
    if (!terrain_appearance_enabled || !terrain_contact_enabled) return false;
    int slot = hu_contact_slot(face_index, face_uv);
    if (slot < 0) return false;
    vec4 key = texelFetch(terrain_contact_meta, ivec2(0, slot), 0);
    vec4 origin = texelFetch(terrain_contact_meta, ivec2(1, slot), 0);
    vec4 bx = texelFetch(terrain_contact_meta, ivec2(2, slot), 0);
    vec4 by = texelFetch(terrain_contact_meta, ivec2(3, slot), 0);
    vec4 bz = texelFetch(terrain_contact_meta, ivec2(4, slot), 0);
    vec2 grid = (face_uv * key.z - key.xy) * 32.0;
    ivec2 cell = min(ivec2(floor(grid)), ivec2(31));
    vec2 f = grid - vec2(cell);
    bool lower = f.x >= f.y;
    vec3 weights = lower ? vec3(1.0 - f.x, f.x - f.y, f.y) : vec3(1.0 - f.y, f.y - f.x, f.x);
    mat3 basis = mat3(bx.xyz, by.xyz, bz.xyz);
    vec2 range = vec2(origin.w, bx.w);
    HuTerrainAppearance a = hu_appearance_vertex(cell, slot, origin.xyz, basis, range, camera);
    HuTerrainAppearance b = hu_appearance_vertex(cell + (lower ? ivec2(1, 0) : ivec2(0, 1)), slot, origin.xyz, basis, range, camera);
    HuTerrainAppearance c = hu_appearance_vertex(cell + ivec2(1, 1), slot, origin.xyz, basis, range, camera);
    vec3 shading_normal = a.normal * weights.x + b.normal * weights.y + c.normal * weights.z;
    if (dot(shading_normal, shading_normal) <= 1e-12) return false;
    value.point = a.point * weights.x + b.point * weights.y + c.point * weights.z;
    value.normal = normalize(shading_normal);
    value.face_metres = a.face_metres * weights.x + b.face_metres * weights.y + c.face_metres * weights.z;
    value.material0 = a.material0 * weights.x + b.material0 * weights.y + c.material0 * weights.z;
    value.material1 = a.material1 * weights.x + b.material1 * weights.y + c.material1 * weights.z;
    value.material2 = a.material2 * weights.x + b.material2 * weights.y + c.material2 * weights.z;
    value.material3 = a.material3 * weights.x + b.material3 * weights.y + c.material3 * weights.z;
    value.climate = a.climate * weights.x + b.climate * weights.y + c.climate * weights.z;
    value.water_height = a.water_height * weights.x + b.water_height * weights.y + c.water_height * weights.z;
    value.face_index = face_index;
    return true;
}

bool hu_drawn_appearance_at(vec3 hint, vec3 camera, out HuTerrainAppearance value) {
    int face_index; vec2 face_uv;
    hu_contact_face(hint, face_index, face_uv);
    return hu_drawn_appearance(face_index, face_uv, camera, value);
}
#endif

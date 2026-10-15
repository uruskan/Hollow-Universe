"""Stage repaired GLBs from the owned, already fitted building meshes.

Preserves UVs, materials, LOD names, collision nodes and node transforms. Extends only
foundation skirts below the top 20 cm to the configured depth, transforms their
normals, and drops duplicate triangles with the SAME winding (back faces remain).
Coplanar partial overlaps are polygon-clipped, with interpolated UVs/normals.
No game assets are overwritten by the CLI. Uses only the Python standard library.
"""
import json
import math
import pathlib
import struct

ROOT = pathlib.Path(__file__).resolve().parents[1]
OUT = ROOT / '.artifacts/building-repair'
OUT.mkdir(parents=True, exist_ok=True)
DEPTH = json.loads((ROOT / 'game/data/ground/building.json').read_text())['module']['foundationDepth']


def area(poly):
    return abs(sum(a[0]*b[1] - a[1]*b[0] for a, b in zip(poly, poly[1:] + poly[:1]))) * 0.5 if poly else 0


def half_plane(poly, a, b):
    def side(p):
        return (b[0]-a[0])*(p[1]-a[1]) - (b[1]-a[1])*(p[0]-a[0])
    result = []
    for start, end in zip(poly, poly[1:] + poly[:1]):
        ds, de = side(start), side(end)
        if (ds >= 0) != (de >= 0):
            t = ds / (ds - de)
            result.append((start[0] + t*(end[0]-start[0]), start[1] + t*(end[1]-start[1])))
        if de >= 0:
            result.append(end)
    return result


def subtract(poly, triangle):
    remaining, pieces = poly, []
    for a, b in zip(triangle, triangle[1:] + triangle[:1]):
        if not remaining:
            break
        outside = half_plane(remaining, b, a)
        if area(outside) > 1e-9:
            pieces.append(outside)
        remaining = half_plane(remaining, a, b)
    # Ignore numerical crumbs on adjacent edges, retaining the original triangle exactly.
    return pieces if area(remaining) > 1e-8 else [poly]


def repair(source, destination=None):
    data = source.read_bytes()
    length = struct.unpack_from('<I', data, 12)[0]
    model = json.loads(data[20:20 + length])
    binary = bytearray(data[28 + length:])

    def accessor(index):
        a = model['accessors'][index]
        v = model['bufferViews'][a['bufferView']]
        fmt = {5123: 'H', 5125: 'I', 5126: 'f'}[a['componentType']]
        count = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}[a['type']]
        fmt = '<' + fmt * count
        return a, fmt, v.get('byteOffset', 0) + a.get('byteOffset', 0), v.get('byteStride', struct.calcsize(fmt))

    def read(index):
        a, fmt, offset, stride = accessor(index)
        return [struct.unpack_from(fmt, binary, offset + i * stride) for i in range(a['count'])]

    def write(index, values):
        a, fmt, offset, stride = accessor(index)
        assert len(values) <= a['count']
        for i, value in enumerate(values):
            struct.pack_into(fmt, binary, offset + i * stride, *value)
        a['count'] = len(values)
        if 'min' in a:
            a['min'] = [min(v[k] for v in values) for k in range(len(values[0]))]
            a['max'] = [max(v[k] for v in values) for k in range(len(values[0]))]

    def append(values, component, kind, target):
        binary.extend(b'\0' * (-len(binary) % 4))
        offset = len(binary)
        fmt = '<' + ({5125: 'I', 5126: 'f'}[component] * len(values[0]))
        for value in values:
            binary.extend(struct.pack(fmt, *value))
        view = len(model['bufferViews'])
        model['bufferViews'].append({'buffer': 0, 'byteOffset': offset, 'byteLength': len(binary)-offset, 'target': target})
        index = len(model['accessors'])
        model['accessors'].append({'bufferView': view, 'componentType': component, 'count': len(values), 'type': kind,
            'min': [min(v[k] for v in values) for k in range(len(values[0]))], 'max': [max(v[k] for v in values) for k in range(len(values[0]))]})
        return index

    removed = {}
    clipped = {}
    footings = []
    for node in model['nodes']:
        if 'mesh' not in node:
            continue
        name = node['name']
        foundation = name.split('_')[0] in ('foundation', 'foundation-triangle')
        hull = 'convcolonly' in name
        seen = set()
        for primitive in model['meshes'][node['mesh']]['primitives']:
            positions = primitive['attributes']['POSITION']
            points = read(positions)
            if foundation:
                old = -min(p[1] for p in points)
                cap = min(0.2, old * 0.5)
                factor = (DEPTH - cap) / (old - cap)
                fitted = [(x, -cap + (y + cap) * factor if y < -cap else y, z) for x, y, z in points]
                write(positions, fitted)
                if 'NORMAL' in primitive['attributes']:
                    ni = primitive['attributes']['NORMAL']
                    normals = read(ni)
                    transformed = []
                    for p, n in zip(points, normals):
                        x, y, z = n
                        if p[1] < -cap:
                            y /= factor
                        size = math.sqrt(x*x + y*y + z*z)
                        transformed.append((x/size, y/size, z/size) if size else n)
                    write(ni, transformed)
                points = fitted
                footings.append(name)
            if hull:
                continue
            original = read(primitive['indices'])
            keep = []
            for i in range(0, len(original), 3):
                tri = original[i:i+3]
                corners = tuple(tuple(round(v, 5) for v in points[index[0]]) for index in tri)
                # Cyclic order retains the normal direction; a deliberately reversed back face is not a duplicate.
                key = min(corners[k:] + corners[:k] for k in range(3))
                if key in seen:
                    removed[name] = removed.get(name, 0) + 1
                else:
                    seen.add(key)
                    keep.extend(tri)
            write(primitive['indices'], keep)
            # Intersecting faces can have different triangulations. Subtract earlier coplanar polygons,
            # preserving their union rather than merely deleting matching vertex triples.
            attributes = {name: read(ai) for name, ai in primitive['attributes'].items()}
            expanded = {name: list(values) for name, values in attributes.items()}
            indices = [value[0] for value in keep]
            groups = {}
            result = []
            changed = 0
            for i in range(0, len(indices), 3):
                original_indices = indices[i:i+3]
                tri = [points[index] for index in original_indices]
                a, b, c = tri
                u, v = [b[k]-a[k] for k in range(3)], [c[k]-a[k] for k in range(3)]
                normal = [u[1]*v[2]-u[2]*v[1], u[2]*v[0]-u[0]*v[2], u[0]*v[1]-u[1]*v[0]]
                size = math.sqrt(sum(n*n for n in normal))
                if size < 1e-10:
                    continue
                normal = [n/size for n in normal]
                key = tuple(round(n, 5) for n in normal + [sum(normal[k]*a[k] for k in range(3))])
                axis = max(range(3), key=lambda k: abs(normal[k]))
                original_poly = [tuple(p[k] for k in range(3) if k != axis) for p in tri]
                poly = list(original_poly)
                reverse = sum(a[0]*b[1]-a[1]*b[0] for a, b in zip(poly, poly[1:]+poly[:1])) < 0
                if reverse:
                    poly.reverse()
                bounds = tuple((min(p[k] for p in poly), max(p[k] for p in poly)) for k in range(2))
                fragments = [poly]
                previous = groups.setdefault(key, [])
                for other, other_bounds in previous:
                    if any(bounds[k][0] >= other_bounds[k][1]-1e-7 or bounds[k][1] <= other_bounds[k][0]+1e-7 for k in range(2)):
                        continue
                    fragments = [piece for fragment in fragments for piece in subtract(fragment, other)]
                    if not fragments:
                        break
                previous.append((poly, bounds))
                if abs(sum(area(p) for p in fragments)-area(poly)) < 1e-8:
                    result.extend((index,) for index in original_indices)
                    continue
                changed += 1
                p0, p1, p2 = original_poly
                denominator = (p1[1]-p2[1])*(p0[0]-p2[0]) + (p2[0]-p1[0])*(p0[1]-p2[1])
                for fragment in fragments:
                    if reverse:
                        fragment.reverse()
                    generated = []
                    for x, y in fragment:
                        w0 = ((p1[1]-p2[1])*(x-p2[0]) + (p2[0]-p1[0])*(y-p2[1])) / denominator
                        w1 = ((p2[1]-p0[1])*(x-p2[0]) + (p0[0]-p2[0])*(y-p2[1])) / denominator
                        weights = (w0, w1, 1-w0-w1)
                        generated.append(len(expanded['POSITION']))
                        for name, values in attributes.items():
                            value = tuple(sum(weights[t]*values[original_indices[t]][k] for t in range(3)) for k in range(len(values[0])))
                            if name == 'NORMAL':
                                length = math.sqrt(sum(v*v for v in value))
                                value = tuple(v/length for v in value)
                            expanded[name].append(value)
                    for k in range(1, len(generated)-1):
                        if area([fragment[0], fragment[k], fragment[k+1]]) > 1e-10:
                            result.extend((index,) for index in (generated[0], generated[k], generated[k+1]))
            if changed:
                clipped[node['name']] = changed
                assert result, 'a complete primitive unexpectedly removed'
                for attribute, values in expanded.items():
                    primitive['attributes'][attribute] = append(values, 5126, 'VEC' + str(len(values[0])), 34962)
                primitive['indices'] = append(result, 5125, 'SCALAR', 34963)
    # Drop superseded buffer ranges after clipping; the runtime package does not carry a second copy of each mesh.
    used = {}
    for mesh in model['meshes']:
        for primitive in mesh['primitives']:
            used[primitive['indices']] = 34963
            for index in primitive['attributes'].values():
                used[index] = 34962
    packed = bytearray()
    views, accessors, mapping = [], [], {}
    for index, target in sorted(used.items()):
        a, fmt, offset, stride = accessor(index)
        size = struct.calcsize(fmt)
        packed.extend(b'\0' * (-len(packed) % 4))
        begin = len(packed)
        for i in range(a['count']):
            packed.extend(binary[offset+i*stride:offset+i*stride+size])
        mapping[index] = len(accessors)
        views.append({'buffer': 0, 'byteOffset': begin, 'byteLength': len(packed)-begin, 'target': target})
        accessors.append({**a, 'bufferView': len(views)-1, 'byteOffset': 0})
    for mesh in model['meshes']:
        for primitive in mesh['primitives']:
            primitive['indices'] = mapping[primitive['indices']]
            primitive['attributes'] = {name: mapping[index] for name, index in primitive['attributes'].items()}
    model['bufferViews'], model['accessors'] = views, accessors
    binary = packed
    model['buffers'][0]['byteLength'] = len(binary)
    payload = json.dumps(model, separators=(',', ':')).encode()
    payload += b' ' * (-len(payload) % 4)
    binary += b'\0' * (-len(binary) % 4)
    result = struct.pack('<III', 0x46546c67, 2, 28 + len(payload) + len(binary))
    result += struct.pack('<II', len(payload), 0x4e4f534a) + payload
    result += struct.pack('<II', len(binary), 0x004e4942) + binary
    (destination or OUT / source.name).write_bytes(result)
    return {'file': source.name, 'removedTriangles': sum(removed.values()), 'meshes': removed, 'clippedTriangles': sum(clipped.values()), 'clippedMeshes': clipped, 'foundationNodes': footings}


if __name__ == '__main__':
    report = [repair(path) for path in sorted((ROOT / 'game/assets/building').glob('building_*.glb'))]
    (OUT / 'repair-report.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps([{'file': r['file'], 'removedTriangles': r['removedTriangles'], 'clippedTriangles': r['clippedTriangles'], 'foundationNodes': len(r['foundationNodes'])} for r in report]))

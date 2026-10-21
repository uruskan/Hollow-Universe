"""Focused stdlib regression for repair_building_glbs.repair on tiny GLBs only.

Requires the retained old tool, the existing raw roof export, and a fresh scratch
directory. Never calls repair on an asset pack or writes production assets.
"""
import argparse
from collections import Counter
import hashlib
import json
import math
from pathlib import Path
import struct
import sys
import traceback
import types

ROOT = Path(__file__).resolve().parents[1]
SCRATCH = ROOT / '.artifacts/p7-roofs'
OLD_SHA = 'bbefa37ee862285067d2c897ffd26e431e6b9f56e9c515aa2fca7f846a2d9ae8'
RAW_SHA = '604b26a8558959a70ed4bc0810912a898f8ee91067715a52a9a2e24df95729f7'
POINT = (0.5934874, 1.2601899, 0.3953736)


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_glb(path):
    data = path.read_bytes()
    magic, version, total, length, tag = struct.unpack_from('<5I', data)
    require((magic, version, total, tag) == (0x46546c67, 2, len(data), 0x4e4f534a), 'GLB header')
    doc = json.loads(data[20:20 + length])
    size, tag = struct.unpack_from('<II', data, 20 + length)
    require(tag == 0x004e4942 and 28 + length + size == len(data), 'BIN header')
    binary = data[28 + length:]

    def values(index):
        a = doc['accessors'][index]
        v = doc['bufferViews'][a['bufferView']]
        fmt = '<' + {5123: 'H', 5125: 'I', 5126: 'f'}[a['componentType']] * {
            'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}[a['type']]
        offset = v.get('byteOffset', 0) + a.get('byteOffset', 0)
        stride = v.get('byteStride', struct.calcsize(fmt))
        return [struct.unpack_from(fmt, binary, offset + i * stride) for i in range(a['count'])]

    node = next(n for n in doc['nodes'] if n.get('name') == 'roof')
    prim = doc['meshes'][node['mesh']]['primitives'][0]
    attrs = {key: values(index) for key, index in prim['attributes'].items()}
    indices = [v[0] for v in values(prim['indices'])]
    triangles = [[{key: rows[i] for key, rows in attrs.items()} for i in indices[j:j + 3]]
                 for j in range(0, len(indices), 3)]
    return doc, triangles


def write_tiny(path, triangles):
    require(not path.exists() and 1 <= len(triangles) <= 3, 'only new GLBs of at most three triangles')
    binary, views, accessors = bytearray(), [], []

    def append(rows, component, kind, target):
        binary.extend(b'\0' * (-len(binary) % 4))
        start = len(binary)
        fmt = '<' + {5125: 'I', 5126: 'f'}[component] * len(rows[0])
        for row in rows:
            binary.extend(struct.pack(fmt, *row))
        views.append({'buffer': 0, 'byteOffset': start, 'byteLength': len(binary) - start, 'target': target})
        accessors.append({'bufferView': len(views) - 1, 'componentType': component,
                          'count': len(rows), 'type': kind,
                          'min': [min(row[k] for row in rows) for k in range(len(rows[0]))],
                          'max': [max(row[k] for row in rows) for k in range(len(rows[0]))]})
        return len(accessors) - 1

    vertices = [v for triangle in triangles for v in triangle]
    attributes = {}
    for key in vertices[0]:
        rows = [v[key] for v in vertices]
        attributes[key] = append(rows, 5126, 'VEC' + str(len(rows[0])), 34962)
    indices = append([(i,) for i in range(len(vertices))], 5125, 'SCALAR', 34963)
    doc = {'asset': {'version': '2.0', 'generator': 'P7 tiny coplanar regression', 'extras': {'retain': 17}},
           'scene': 0, 'scenes': [{'name': 'retained scene', 'nodes': [0]}],
           'nodes': [{'name': 'roof', 'mesh': 0, 'translation': [0.25, 0.5, 0.75],
                      'rotation': [0, 0, 0, 1], 'scale': [1, 1, 1], 'extras': {'retain': 'node'}}],
           'materials': [{'name': 'retained material', 'doubleSided': False,
                          'pbrMetallicRoughness': {'baseColorFactor': [0.2, 0.3, 0.4, 1], 'roughnessFactor': 0.7}}],
           'meshes': [{'name': 'retained mesh', 'primitives': [
               {'attributes': attributes, 'indices': indices, 'material': 0, 'mode': 4}]}],
           'bufferViews': views, 'accessors': accessors, 'buffers': [{'byteLength': len(binary)}]}
    payload = json.dumps(doc, separators=(',', ':')).encode()
    payload += b' ' * (-len(payload) % 4)
    binary += b'\0' * (-len(binary) % 4)
    path.write_bytes(struct.pack('<5I', 0x46546c67, 2, 28 + len(payload) + len(binary), len(payload), 0x4e4f534a)
                     + payload + struct.pack('<II', len(binary), 0x004e4942) + binary)
    require(path.stat().st_size < 32768, 'tiny input size bound')


def normal(triangle):
    a, b, c = [v['POSITION'] for v in triangle]
    u = [b[k] - a[k] for k in range(3)]
    v = [c[k] - a[k] for k in range(3)]
    return (u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0])


def area(triangles):
    return sum(math.sqrt(sum(x*x for x in normal(t))) / 2 for t in triangles)


def covers(triangle, point):
    n = normal(triangle)
    length = math.sqrt(sum(x*x for x in n))
    if length < 1e-10:
        return False
    a, b, c = [v['POSITION'] for v in triangle]
    if abs(sum(n[k] * (point[k] - a[k]) for k in range(3))) > length * 2e-6:
        return False
    axis = max(range(3), key=lambda k: abs(n[k]))
    project = lambda p: tuple(p[k] for k in range(3) if k != axis)
    a, b, c, p = [project(v) for v in (a, b, c, point)]
    denominator = (b[1]-c[1])*(a[0]-c[0]) + (c[0]-b[0])*(a[1]-c[1])
    u = ((b[1]-c[1])*(p[0]-c[0]) + (c[0]-b[0])*(p[1]-c[1])) / denominator
    v = ((c[1]-a[1])*(p[0]-c[0]) + (a[0]-c[0])*(p[1]-c[1])) / denominator
    return min(u, v, 1-u-v) >= -1e-7


def count_at(triangles, point):
    return sum(covers(t, point) for t in triangles)


def geometry(triangles):
    result = []
    for triangle in triangles:
        corners = tuple(tuple(round(x, 6) for x in v['POSITION']) for v in triangle)
        result.append(min(corners[k:] + corners[:k] for k in range(3)))
    return Counter(result)


def load_tool(path, name):
    # The retained old bytes need the original __file__ context for ROOT/data.
    # The existing output directory is required so module initialization creates nothing.
    require((ROOT / '.artifacts/building-repair').is_dir(), 'repair workspace already exists')
    module = types.ModuleType(name)
    module.__file__ = str(ROOT / 'tools/repair_building_glbs.py')
    exec(compile(path.read_bytes(), str(path), 'exec'), module.__dict__)
    return module


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline', type=Path, required=True)
    parser.add_argument('--raw', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    args.out = args.out.resolve()
    require(args.out.is_relative_to(SCRATCH.resolve()) and args.out != SCRATCH.resolve()
            and not args.out.exists(), 'fresh output below roof scratch only')
    require(digest(args.baseline) == OLD_SHA and digest(args.raw) == RAW_SHA, 'retained source hashes')
    candidate_path = ROOT / 'tools/repair_building_glbs.py'
    before = {str(p): digest(p) for p in (args.baseline, args.raw, candidate_path)}
    args.out.mkdir()
    result = {'passed': False, 'inputs': before, 'cases': [], 'runs': [], 'scope': 'tiny meshes only; no asset repair'}
    candidate, old = load_tool(candidate_path, 'p7_candidate'), load_tool(args.baseline, 'p7_retained_old')

    def run(name, triangles, module=candidate):
        source, dest = args.out / (name + '.in.glb'), args.out / (name + '.out.glb')
        write_tiny(source, triangles)
        source_hash = digest(source)
        report = module.repair(source, dest)
        require(digest(source) == source_hash, name + ': source preserved')
        original, _ = read_glb(source)
        document, output = read_glb(dest)
        for key in ('asset', 'scene', 'scenes', 'nodes', 'materials'):
            require(document[key] == original[key], name + ': metadata ' + key)
        require(document['meshes'][0]['name'] == original['meshes'][0]['name'], name + ': mesh name')
        p, q = document['meshes'][0]['primitives'][0], original['meshes'][0]['primitives'][0]
        require(p['material'] == q['material'] and p['mode'] == q['mode']
                and p['attributes'].keys() == q['attributes'].keys(), name + ': primitive metadata/attributes')
        result['runs'].append({'name': name, 'inputTriangles': len(triangles), 'outputTriangles': len(output),
                               'inputBytes': source.stat().st_size, 'outputSHA256': digest(dest), 'repairReport': report})
        return output, dest, report

    def passed(name, **details):
        result['cases'].append({'name': name, 'passed': True, **details})
        print('PASS ' + name, flush=True)

    try:
        _, raw = read_glb(args.raw)
        pair = [raw[4177], raw[11378]]
        require(count_at(pair, POINT) == 1, 'raw backing point is covered')
        old_output, _, _ = run('retained-old-negative-control', pair, old)
        new_output, _, _ = run('extracted-pair', pair)
        require(count_at(old_output, POINT) == 0, 'old behavior must reproduce missing backing')
        require(count_at(new_output, POINT) == 1, 'corrected repair must retain backing')
        require(abs(area(new_output)-area(pair)) < 2e-6, 'disjoint pair area retained')
        require(new_output == pair, 'unclipped extracted attributes, winding and positions retained exactly')
        passed('actual-raw-pair-and-old-negative-control', oldPointCoverage=0, newPointCoverage=1,
               rawTriangleOrdinals=[4177, 11378], point=POINT)

        for order in (pair, list(reversed(pair))):
            for shift in range(3):
                for flip in (False, True):
                    variant = [t[shift:]+t[:shift] for t in order]
                    if flip:
                        variant = [list(reversed(t)) for t in variant]
                    output, _, _ = run(f'pair-order-{order is pair}-cycle-{shift}-flip-{flip}', variant)
                    require(geometry(output) == geometry(variant), 'pair geometry/winding order invariance')
                    require(count_at(output, POINT) == 1, 'pair coverage order invariance')
        passed('near-tie-order-cyclic-and-reversed-winding', variants=12)

        mappings = {
            'drop-z': (lambda u, v: (u, v, 0), lambda p: (p[0], p[1])),
            'drop-y': (lambda u, v: (u, 0, -v), lambda p: (p[0], -p[2])),
            'drop-x': (lambda u, v: (0, u, v), lambda p: (p[1], p[2])),
            'tie-yz': (lambda u, v: (u, v, -v), lambda p: (p[0], p[1])),
            'tie-xyz': (lambda u, v: (u, v, -u-v), lambda p: (p[0], p[1])),
        }
        planar = [[(0, 0), (2, 0), (0, 2)], [(1, 0), (3, 0), (1, 2)]]
        deterministic = None
        for name, (forward, inverse) in mappings.items():
            for reverse_order in (False, True):
                for flip in (False, True):
                    n = normal([[{'POSITION': forward(u, v)} for u, v in planar[0]]][0])
                    length = math.sqrt(sum(x*x for x in n))
                    unit = tuple(x / length * (-1 if flip else 1) for x in n)
                    triangles = [[{'POSITION': forward(u, v), 'NORMAL': unit,
                                   'TEXCOORD_0': (0.1 + u/4, 0.2 + v/4),
                                   'COLOR_0': (u/4, v/4, 0.25, 1), 'TANGENT': (1, 0, 0, 1)}
                                  for u, v in t] for t in planar]
                    if reverse_order:
                        triangles.reverse()
                    if flip:
                        triangles = [list(reversed(t)) for t in triangles]
                    output, dest, report = run(f'overlap-{name}-order-{reverse_order}-flip-{flip}', triangles)
                    # Each planar triangle area is 2; the intersection area is 0.5.
                    require(abs(area(output) - 3.5 * length/4) < 2e-5, 'true overlap union area')
                    require(report['clippedTriangles'] > 0, 'actual overlap subtraction exercised')
                    for u, v, expected in ((.271, .193, 1), (1.271, .193, 1),
                                           (2.271, .193, 1), (2.271, 1.193, 0)):
                        require(count_at(output, forward(u, v)) == expected, 'union point coverage/no duplicate interior')
                    for t in output:
                        require(sum(a*b for a, b in zip(normal(t), unit)) > 0, 'winding retained')
                        for vertex in t:
                            u, v = inverse(vertex['POSITION'])
                            expected = {'NORMAL': unit, 'TEXCOORD_0': (0.1+u/4, 0.2+v/4),
                                        'COLOR_0': (u/4, v/4, .25, 1), 'TANGENT': (1, 0, 0, 1)}
                            for key, values in expected.items():
                                require(max(abs(a-b) for a, b in zip(vertex[key], values)) < 2e-6,
                                        'interpolated attribute ' + key)
                    if deterministic is None:
                        deterministic = (triangles, dest)
        passed('real-overlap-union-winding-axes-ties-and-attributes', variants=20)

        triangle = [{'POSITION': (u, v, 0), 'NORMAL': (0, 0, 1), 'TEXCOORD_0': (u, v)} for u, v in planar[0]]
        back = [{**vertex, 'NORMAL': (0, 0, -1)} for vertex in reversed(triangle)]
        output, _, report = run('opposite-face-and-same-facing-duplicate', [triangle, back, triangle])
        require(len(output) == 2 and geometry(output) == geometry([triangle, back]), 'opposite face retained')
        require(report['removedTriangles'] == 1, 'same-facing duplicate still removed')
        require(count_at(output, (.271, .193, 0)) == 2, 'both intended face windings retained')
        passed('opposite-winding-and-same-facing-duplicate')

        triangles, first_dest = deterministic
        _, repeat_dest, _ = run('deterministic-repeat', triangles)
        require(first_dest.read_bytes() == repeat_dest.read_bytes(), 'byte deterministic repair output')
        passed('byte-determinism', outputSHA256=digest(first_dest))
        require(before == {path: digest(Path(path)) for path in before}, 'tool/raw inputs preserved')
        result['passed'] = True
    except Exception as exc:
        result['error'] = {'type': type(exc).__name__, 'message': str(exc)}
        print('FAIL ' + str(exc), flush=True)
        traceback.print_exc()
        raise
    finally:
        (args.out / 'results.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(f"PASS {len(result['cases'])} groups; {len(result['runs'])} tiny repair invocations; no asset packs repaired", flush=True)


if __name__ == '__main__':
    main()

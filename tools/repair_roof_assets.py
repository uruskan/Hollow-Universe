"""Bounded N10 asset staging. No Blender import or filesystem writes at module import.

All outputs are NEW files below this checkout's .artifacts/p7-roofs. The merge keeps
the original BIN and all original mesh/accessor/material records, replacing only
selected live nodes. Obsolete roof buffers remain unreferenced deliberately; this
is not a general glTF optimizer. Unsupported layouts fail closed before writing.

python -I tools/repair_roof_assets.py merge --kind building --grade twig \
  --patch .artifacts/p7-roofs/r1/roof-source-twig.glb \
  --out .artifacts/p7-roofs/r1/building_twig.glb
"""
import argparse
import copy
import hashlib
import json
import math
from pathlib import Path
import re
import struct

ROOT = Path(__file__).resolve().parents[1]
SCRATCH = ROOT / '.artifacts' / 'p7-roofs'
GRADES = ('twig', 'wood', 'stone', 'metal', 'armoured')
ROOFS = ('roof', 'roof-triangle')
COMPONENTS = {5121: 'B', 5123: 'H', 5125: 'I', 5126: 'f'}
WIDTHS = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}


def require(ok, why):
    if not ok:
        raise ValueError(why)


def digest(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def staged(path, *, exists=False):
    path = Path(path).resolve()
    require(path.is_relative_to(SCRATCH.resolve()) and path != SCRATCH.resolve(),
            f'path must stay under {SCRATCH}: {path}')
    require(path.is_file() if exists else not path.exists(), f'expected {"existing input" if exists else "new output"}: {path}')
    return path


def output_pair(path, expected):
    path = staged(path)
    require(path.name == expected, f'output must be named {expected}')
    receipt = staged(str(path) + '.receipt.json')
    require(not Path(str(path) + '.import').exists(), 'staged outputs must not have import settings')
    return path, receipt


def baseline(kind, grade):
    require(kind in ('building', 'gibs') and grade in GRADES, 'unknown kind or grade')
    name = f'building_{grade}.glb' if kind == 'building' else f'building-{grade}.glb'
    return ROOT / 'game' / 'assets' / kind / name


def protected():
    """Files the bounded exporters must never rewrite; receipt includes content and import settings."""
    paths = [ROOT / 'game/data/ground' / f for f in ('building.json', 'models.json')]
    paths += [ROOT / 'art/import/building_fit.report.txt']
    for kind in ('building', 'gibs'):
        for grade in GRADES:
            p = baseline(kind, grade)
            paths += [p, Path(str(p) + '.import')]
    paths += list((ROOT / 'game/assets/building/textures').glob('*'))
    paths += list((ROOT / 'game/assets/materials').glob('building_*'))
    paths += list((ROOT / 'game/assets/materials').glob('door_garage*'))
    return {p.relative_to(ROOT).as_posix(): digest(p) for p in paths if p.is_file()}


def unchanged(snapshot):
    require(snapshot == protected(), 'protected production assets, catalogue, materials or import settings changed')


def write_receipt(path, details):
    path = staged(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open('x', encoding='utf-8', newline='\n') as stream:
        json.dump(details, stream, indent=2, allow_nan=False)
        stream.write('\n')


def values(doc, binary, index):
    a = doc['accessors'][index]
    v = doc['bufferViews'][a['bufferView']]
    fmt = '<' + COMPONENTS[a['componentType']] * WIDTHS[a['type']]
    size = struct.calcsize(fmt)
    start = v.get('byteOffset', 0) + a.get('byteOffset', 0)
    step = v.get('byteStride', size)
    for i in range(a['count']):
        yield struct.unpack_from(fmt, binary, start + i * step)


def read_glb(path):
    with Path(path).open('rb') as stream:
        head = stream.read(20)
        require(len(head) == 20, 'short GLB')
        magic, version, total, size, tag = struct.unpack('<5I', head)
        require(magic == 0x46546c67 and version == 2 and tag == 0x4e4f534a and size % 4 == 0,
                'requires GLB v2 JSON first')
        require(total == Path(path).stat().st_size and total < 512 * 1024 * 1024, 'invalid/unbounded GLB length')
        doc = json.loads(stream.read(size))
        chunk = stream.read(8)
        require(len(chunk) == 8, 'missing BIN header')
        length, tag = struct.unpack('<2I', chunk)
        require(tag == 0x004e4942 and length % 4 == 0 and total == size + length + 28, 'requires one BIN chunk')
        binary = stream.read(length)
        require(len(binary) == length and not stream.read(1), 'truncated/extra GLB data')
    validate(doc, binary)
    return doc, binary[:doc['buffers'][0]['byteLength']]


def validate(doc, binary):
    require(set(doc) <= {'asset', 'scene', 'scenes', 'nodes', 'materials', 'meshes', 'accessors', 'bufferViews', 'buffers'},
            'unsupported GLB fields/extensions/animation/skin/images')
    require({'asset', 'scenes', 'nodes', 'meshes', 'accessors', 'bufferViews', 'buffers'} <= set(doc), 'missing GLB arrays')
    require(doc['asset']['version'] == '2.0' and doc.get('scene', 0) == 0 and len(doc['scenes']) == 1, 'requires one scene')
    require(set(doc['scenes'][0]) <= {'name', 'nodes'}, 'unsupported scene fields')
    require(len(doc['buffers']) == 1 and set(doc['buffers'][0]) == {'byteLength'}, 'requires embedded buffer')
    length = doc['buffers'][0]['byteLength']
    require(isinstance(length, int) and 0 <= len(binary) - length <= 3, 'bad BIN length')
    require(not any(binary[length:]), 'nonzero BIN padding')
    for v in doc['bufferViews']:
        require(set(v) <= {'buffer', 'byteOffset', 'byteLength', 'byteStride', 'target'}, 'unsupported buffer view')
        require(v.get('buffer') == 0 and isinstance(v.get('byteOffset', 0), int) and v.get('byteOffset', 0) >= 0 and
                isinstance(v['byteLength'], int) and v['byteLength'] >= 0 and v.get('byteOffset', 0) + v['byteLength'] <= length,
                'buffer view outside BIN')
    for a in doc['accessors']:
        require(set(a) <= {'bufferView', 'byteOffset', 'componentType', 'count', 'type', 'min', 'max', 'normalized'},
                'unsupported accessor (including sparse)')
        require(a.get('componentType') in COMPONENTS and a.get('type') in WIDTHS and
                isinstance(a.get('bufferView'), int) and 0 <= a['bufferView'] < len(doc['bufferViews']), 'bad accessor type/view')
        require(isinstance(a['count'], int) and a['count'] > 0, 'empty/invalid accessor')
        v = doc['bufferViews'][a['bufferView']]
        size = struct.calcsize('<' + COMPONENTS[a['componentType']] * WIDTHS[a['type']])
        offset, stride = a.get('byteOffset', 0), v.get('byteStride', size)
        component_size = struct.calcsize('<' + COMPONENTS[a['componentType']])
        require(isinstance(offset, int) and offset >= 0 and isinstance(stride, int) and stride >= size and
                offset + stride * (a['count'] - 1) + size <= v['byteLength'], 'accessor outside view')
        require((v.get('byteOffset', 0) + offset) % component_size == 0 and stride % component_size == 0,
                'misaligned accessor')
        require(a['componentType'] != 5126 or not a.get('normalized', False), 'normalized float accessor')
        for bound in ('min', 'max'):
            if bound in a:
                require(len(a[bound]) == WIDTHS[a['type']] and all(math.isfinite(c) for c in a[bound]), 'invalid accessor bounds')
    for i, a in enumerate(doc['accessors']):
        if a['componentType'] == 5126:
            require(all(math.isfinite(c) for row in values(doc, binary, i) for c in row), 'nonfinite attribute')
    names = [n.get('name') for n in doc['nodes']]
    require(all(isinstance(n, str) and n for n in names) and len(set(names)) == len(names), 'node names must be unique')
    for n in doc['nodes']:
        require(set(n) <= {'name', 'mesh', 'children', 'translation', 'rotation', 'scale', 'matrix'}, 'unsupported node')
        require(not ('matrix' in n and any(k in n for k in ('translation', 'rotation', 'scale'))), 'mixed node transforms')
        for key, count in (('translation', 3), ('rotation', 4), ('scale', 3), ('matrix', 16)):
            if key in n:
                require(len(n[key]) == count and all(isinstance(c, (int, float)) and math.isfinite(c) for c in n[key]), 'bad transform')
        if 'mesh' in n:
            require(isinstance(n['mesh'], int) and 0 <= n['mesh'] < len(doc['meshes']), 'invalid node mesh')
    seen = set()
    def walk(i):
        require(isinstance(i, int) and 0 <= i < len(names) and i not in seen, 'invalid/cyclic/shared scene node')
        seen.add(i)
        for child in doc['nodes'][i].get('children', []):
            walk(child)
    for i in doc['scenes'][0]['nodes']:
        walk(i)
    require(len(seen) == len(names), 'unreachable nodes are unsupported')
    materials = doc.get('materials', [])
    require(len({m.get('name') for m in materials}) == len(materials), 'ambiguous materials')
    for m in materials:
        require(set(m) <= {'name', 'doubleSided', 'pbrMetallicRoughness', 'alphaMode', 'alphaCutoff', 'emissiveFactor'}, 'unsupported material')
        require(set(m.get('pbrMetallicRoughness', {})) <= {'baseColorFactor', 'metallicFactor', 'roughnessFactor'}, 'textured material unsupported')
    for m in doc['meshes']:
        require(set(m) <= {'name', 'primitives'} and len(m['primitives']) > 0, 'unsupported mesh')
        for p in m['primitives']:
            require(set(p) <= {'attributes', 'indices', 'material', 'mode'} and p.get('mode', 4) == 4, 'requires triangles')
            attrs = p['attributes']
            require('POSITION' in attrs and set(attrs) <= {'POSITION', 'NORMAL', 'TEXCOORD_0', 'COLOR_0', 'TANGENT'}, 'unsupported attributes')
            refs = list(attrs.values()) + [p['indices']]
            require(all(isinstance(i, int) and 0 <= i < len(doc['accessors']) for i in refs), 'bad accessor index')
            position = doc['accessors'][attrs['POSITION']]
            require(position['type'] == 'VEC3' and position['componentType'] == 5126, 'position format')
            require(all(doc['accessors'][i]['count'] == position['count'] for i in attrs.values()), 'attribute count mismatch')
            indices = doc['accessors'][p['indices']]
            require(indices['type'] == 'SCALAR' and indices['componentType'] in (5121, 5123, 5125) and indices['count'] % 3 == 0,
                    'index format/count')
            require(not indices.get('normalized', False), 'normalized indices unsupported')
            require(all(0 <= row[0] < position['count'] for row in values(doc, binary, p['indices'])), 'index outside vertices')
            if 'material' in p:
                require(isinstance(p['material'], int) and 0 <= p['material'] < len(materials), 'invalid material')


def identity(node):
    return (node.get('translation', [0, 0, 0]) == [0, 0, 0] and node.get('rotation', [0, 0, 0, 1]) == [0, 0, 0, 1] and
            node.get('scale', [1, 1, 1]) == [1, 1, 1] and
            node.get('matrix', [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]) == [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1])


def selection(doc, kind, *, candidate=False, partial=False):
    nodes = doc['nodes']
    by_name = {n['name']: i for i, n in enumerate(nodes)}
    roots = set(doc['scenes'][0]['nodes'])
    selected = set()
    if kind == 'building':
        expected = {r + s for r in ROOFS for s in ('', '_LOD1', '_LOD2', '_col00-convcolonly')}
        removed = {r + s for r in ROOFS for s in ('_col01-convcolonly', '_col02-convcolonly')}
        require(expected <= by_name.keys(), 'missing roof render/slab nodes')
        require(not candidate or not removed.intersection(by_name), 'candidate still contains gable hulls')
        for name in expected | removed.intersection(by_name):
            i = by_name[name]
            require(i in roots and 'mesh' in nodes[i] and not nodes[i].get('children') and identity(nodes[i]), 'roof node must be a direct identity mesh')
            selected.add(i)
    else:
        for roof in ROOFS:
            require(roof in by_name, 'missing direct roof gib group')
            i = by_name[roof]
            group = nodes[i]
            require(i in roots and 'mesh' not in group and identity(group) and 2 <= len(group.get('children', [])) <= 9, 'invalid roof gib group')
            selected.add(i)
            for child in group['children']:
                n = nodes[child]
                require('mesh' in n and not n.get('children') and re.fullmatch(re.escape(roof) + r'_c\d{2}', n['name']), 'invalid direct gib fragment')
                selected.add(child)
    require(all(i in selected for i, n in enumerate(nodes) if n['name'].startswith('roof')), 'unexpected roof node')
    require(not partial or len(selected) == len(nodes), 'partial output contains unrelated nodes')
    if partial:
        require({n['mesh'] for n in nodes if 'mesh' in n} == set(range(len(doc['meshes']))), 'unused partial meshes unsupported')
    if candidate:
        for i in selected:
            node = nodes[i]
            if 'mesh' not in node or 'convcolonly' in node['name']:
                continue
            for primitive in doc['meshes'][node['mesh']]['primitives']:
                require({'POSITION', 'NORMAL', 'TEXCOORD_0'} <= set(primitive['attributes']), 'roof render attributes missing')
                require('material' in primitive, 'roof render material binding missing')
    return selected


def mesh_statistics(doc):
    """Actual exported counts, separate from pre-repair generator/fracture counts."""
    result = []
    for node in doc['nodes']:
        if 'mesh' not in node:
            continue
        primitives = doc['meshes'][node['mesh']]['primitives']
        result.append({'node': node['name'],
            'triangles': sum(doc['accessors'][p['indices']]['count'] // 3 for p in primitives),
            'vertices': sum(doc['accessors'][p['attributes']['POSITION']]['count'] for p in primitives)})
    return result


def candidate_receipt(path, mode, grade):
    path = staged(path, exists=True)
    receipt = staged(str(path) + '.receipt.json', exists=True)
    data = json.loads(receipt.read_text(encoding='utf-8'))
    require(data.get('mode') == mode and data.get('grade') == grade and data.get('outputSHA256') == digest(path), 'candidate receipt/hash mismatch')
    unchanged(data['protected'])
    for source, sha in data.get('inputs', {}).items():
        p = (ROOT / source).resolve()
        require(p.is_relative_to(ROOT) and digest(p) == sha, 'candidate input changed')
    return data


def building_candidate(path, grade):
    path = staged(path, exists=True)
    require(path.name == f'building_{grade}.glb', 'fractures require an explicit merged staged building pack')
    candidate_receipt(path, 'merge-building', grade)
    doc, binary = read_glb(path)
    selection(doc, 'building', candidate=True)
    return path, doc, binary


def encode(doc, binary):
    data = json.dumps(doc, separators=(',', ':'), allow_nan=False).encode('utf-8')
    data += b' ' * (-len(data) % 4)
    binary += b'\0' * (-len(binary) % 4)
    return (struct.pack('<5I', 0x46546c67, 2, len(data) + len(binary) + 28, len(data), 0x4e4f534a) +
            data + struct.pack('<2I', len(binary), 0x004e4942) + binary)


def merge_documents(old, old_bin, patch, patch_bin, kind, grade):
    """Pure merge core: no paths, reads or writes. Used by the CLI and isolated synthetic controls."""
    require(kind in ('building', 'gibs') and grade in GRADES, 'unknown kind or grade')
    validate(old, old_bin)
    validate(patch, patch_bin)
    removed = selection(old, kind)
    selection(patch, kind, candidate=True, partial=True)
    names = [m.get('name') for m in old.get('materials', [])]
    require(names == [f'Building_{grade}'] and [m.get('name') for m in patch.get('materials', [])] == names,
            'grade/material binding mismatch')
    result = copy.deepcopy(old)
    vb, ab, mb = len(old['bufferViews']), len(old['accessors']), len(old['meshes'])
    binary = old_bin + b'\0' * (-len(old_bin) % 4)
    offset = len(binary)
    binary += patch_bin
    for view in patch['bufferViews']:
        view = copy.deepcopy(view)
        view['byteOffset'] = view.get('byteOffset', 0) + offset
        result['bufferViews'].append(view)
    for accessor in patch['accessors']:
        accessor = copy.deepcopy(accessor)
        accessor['bufferView'] += vb
        result['accessors'].append(accessor)
    for mesh in patch['meshes']:
        mesh = copy.deepcopy(mesh)
        for p in mesh['primitives']:
            p['indices'] += ab
            p['attributes'] = {k: i + ab for k, i in p['attributes'].items()}
            # The original material records are retained verbatim, including runtime remap names.
        result['meshes'].append(mesh)
    kept = [i for i in range(len(old['nodes'])) if i not in removed]
    remap = {old_i: new_i for new_i, old_i in enumerate(kept)}
    nodes = []
    for i in kept:
        n = copy.deepcopy(old['nodes'][i])
        require(not any(c in removed for c in n.get('children', [])), 'partial replacement of unrelated parent')
        if 'children' in n:
            n['children'] = [remap[c] for c in n['children']]
        nodes.append(n)
    nb = len(nodes)
    for n in patch['nodes']:
        n = copy.deepcopy(n)
        if 'mesh' in n:
            n['mesh'] += mb
        if 'children' in n:
            n['children'] = [c + nb for c in n['children']]
        nodes.append(n)
    patch_roots = {patch['nodes'][i]['name']: i + nb for i in patch['scenes'][0]['nodes']}
    roots = []
    for i in old['scenes'][0]['nodes']:
        if i not in removed:
            roots.append(remap[i])
        elif old['nodes'][i]['name'] in patch_roots:
            roots.append(patch_roots.pop(old['nodes'][i]['name']))
    require(not patch_roots, 'replacement has roots absent from baseline')
    result['nodes'] = nodes
    result['scenes'][0]['nodes'] = roots
    result['buffers'][0]['byteLength'] = len(binary)
    validate(result, binary)
    selection(result, kind, candidate=True)
    # Stronger than visual equivalence: every original non-roof reference/payload remains unchanged.
    require(binary[:len(old_bin)] == old_bin, 'baseline BIN changed')
    for key, count in (('bufferViews', vb), ('accessors', ab), ('meshes', mb)):
        require(result[key][:count] == old[key], f'baseline {key} changed')
    for key in set(old) - {'buffers', 'bufferViews', 'accessors', 'meshes', 'nodes', 'scenes'}:
        require(result[key] == old[key], f'baseline {key} changed')
    for i in kept:
        before, after = copy.deepcopy(old['nodes'][i]), copy.deepcopy(result['nodes'][remap[i]])
        for n, doc in ((before, old), (after, result)):
            if 'children' in n:
                n['children'] = [doc['nodes'][c]['name'] for c in n['children']]
        require(before == after, f'unrelated node changed: {before["name"]}')
    before_scene, after_scene = copy.deepcopy(old['scenes'][0]), copy.deepcopy(result['scenes'][0])
    before_scene['nodes'] = [old['nodes'][i]['name'] for i in old['scenes'][0]['nodes'] if i not in removed]
    after_scene['nodes'] = [result['nodes'][i]['name'] for i in roots if i < nb]
    require(before_scene == after_scene, 'unrelated scene order/content changed')
    return result, binary


def merge(kind, grade, patch_path, out):
    source = baseline(kind, grade)
    out, receipt_path = output_pair(out, source.name)
    patch_path = staged(patch_path, exists=True)
    receipt = candidate_receipt(patch_path, 'roof-source' if kind == 'building' else 'roof-gibs', grade)
    old, old_bin = read_glb(source)
    patch, patch_bin = read_glb(patch_path)
    result, binary = merge_documents(old, old_bin, patch, patch_bin, kind, grade)
    unchanged(receipt['protected'])
    payload = encode(result, binary)
    out.parent.mkdir(parents=True, exist_ok=True)
    with out.open('xb') as stream:
        stream.write(payload)
    write_receipt(receipt_path, {'mode': 'merge-' + kind, 'grade': grade,
        'inputs': {source.relative_to(ROOT).as_posix(): digest(source), patch_path.relative_to(ROOT).as_posix(): digest(patch_path)},
        'protected': receipt['protected'], 'outputSHA256': hashlib.sha256(payload).hexdigest(),
        'bytes': len(payload), 'unrelatedContentUnchanged': True, 'obsoleteBuffersRetained': True,
        'replacementMeshes': mesh_statistics(patch)})
    print(f'{out}: preserved unrelated nodes, original BIN, materials and import settings; {len(payload)} bytes')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('operation', choices=['merge'])
    parser.add_argument('--kind', required=True, choices=['building', 'gibs'])
    parser.add_argument('--grade', required=True, choices=GRADES)
    parser.add_argument('--patch', required=True)
    parser.add_argument('--out', required=True)
    args = parser.parse_args()
    merge(args.kind, args.grade, args.patch, args.out)


if __name__ == '__main__':
    main()

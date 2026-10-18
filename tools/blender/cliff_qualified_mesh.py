"""Constrained, review-only cliff authoring from receipt-matched near glTF bytes.

Despite its location this tool uses Python's standard library, not Blender's
weighted Decimate modifier. Endpoint-only area-weighted QEM preserves retained
source attributes exactly. Protected corners, open boundaries and attribute
seams are constraints, never penalty weights. Failure to reach a budget is an
error, not permission to export an oversized asset.

Example (DO NOT run concurrently with game capture/build work):
  python tools/blender/cliff_qualified_mesh.py --asset nordic_forest_ledge \
    --destination .artifacts/cliff-prototype-NEW --seconds 55

Writes only into a new explicit directory. No overwrite/resume mode exists.
The sidecar has no support/habitat capabilities, contacts or active recipes:
selection approval preserves geometry; it does not approve soil or planting.
This is a prototype authoring tool, not an asset-admission or render validator.
Local link/inversion tests do not establish global self-intersection freedom,
Hausdorff error, good shading, or runtime performance; those remain review gates.
"""
from __future__ import annotations

import argparse
from collections import defaultdict
from dataclasses import dataclass
import hashlib
import heapq
import json
import math
from pathlib import Path
import re
import struct
import time


class QualificationError(ValueError):
    pass


class BudgetError(QualificationError):
    pass


def require(condition, message):
    if not condition:
        raise QualificationError(message)


def canonical(value):
    return (json.dumps(value, sort_keys=True, separators=(",", ":"),
                       ensure_ascii=False, allow_nan=False) + "\n").encode("utf-8")


def sha256(path):
    h = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def strict_json(path, maximum=4 * 1024 * 1024, expected_sha256=None):
    require(Path(path).stat().st_size <= maximum, "JSON byte budget exceeded")
    raw = Path(path).read_bytes()
    require(len(raw) <= maximum, "JSON changed beyond byte budget")
    if expected_sha256 is not None:
        require(hashlib.sha256(raw).hexdigest() == expected_sha256, "Authenticated JSON digest mismatch")
    def pairs(items):
        result = {}
        for key, value in items:
            require(key not in result, "Duplicate JSON property: " + key)
            result[key] = value
        return result
    def bad(value):
        raise QualificationError("Non-finite JSON number: " + value)
    return json.loads(raw.decode("utf-8-sig"),
                      object_pairs_hook=pairs, parse_constant=bad)


def contained(root, relative):
    require(isinstance(relative, str) and relative and not Path(relative).is_absolute()
            and "\\" not in relative and ":" not in relative, "Expected relative portable path")
    root = Path(root).resolve()
    path = (root / relative).resolve()
    require(path.is_relative_to(root), "Path escapes root: " + relative)
    return path


def uri_file(root, uri):
    require(isinstance(uri, str) and re.fullmatch(r"[A-Za-z0-9_.-]+", uri)
            and uri not in (".", ".."), "Only local filename glTF URIs are admitted")
    return contained(root, uri)


def sub(a, b):
    return tuple(x - y for x, y in zip(a, b))


def dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def cross(a, b):
    return (a[1]*b[2] - a[2]*b[1], a[2]*b[0] - a[0]*b[2], a[0]*b[1] - a[1]*b[0])


def face_normal(positions, triangle):
    a, b, c = (positions[v] for v in triangle)
    return cross(sub(b, a), sub(c, a))


def edge(a, b):
    return (a, b) if a < b else (b, a)


def face_edges(triangle):
    a, b, c = triangle
    return ((a, b), (b, c), (c, a))


def uv_area(attributes, corners):
    a, b, c = (attributes[v][2] for v in corners)
    return (b[0]-a[0])*(c[1]-a[1]) - (b[1]-a[1])*(c[0]-a[0])


@dataclass
class Mesh:
    positions: list
    triangles: list
    corners: list
    attributes: list
    canonical_corner: list
    seams: set
    source_triangles: list

    @classmethod
    def from_arrays(cls, positions, normals, uvs, triangles):
        require(0 < len(positions) <= 60000 and 0 < len(triangles) <= 50000,
                "Input geometry exceeds authoring count limits")
        require(len(normals) == len(uvs) == len(positions), "Attribute count mismatch")
        attributes = [(tuple(p), tuple(n), tuple(uv)) for p, n, uv in zip(positions, normals, uvs)]
        require(all(len(p) == 3 and len(n) == 3 and len(uv) == 2 and
                    all(math.isfinite(v) for row in (p, n, uv) for v in row)
                    for p, n, uv in attributes), "Invalid vertex attributes")
        unique, mapping, coordinates, corners, seams = {}, [], [], [], set()
        for original, (p, n, uv) in enumerate(attributes):
            if p not in unique:
                unique[p] = len(coordinates)
                coordinates.append(p)
                corners.append(original)
            v = unique[p]
            mapping.append(v)
            if attributes[corners[v]][1:] != (n, uv):
                seams.add(v)
        for triangle in triangles:
            require(len(triangle) == 3 and all(type(v) is int and 0 <= v < len(mapping) for v in triangle),
                    "Invalid source index")
        welded = [tuple(mapping[v] for v in t) for t in triangles]
        validate_topology(coordinates, welded)
        return cls(coordinates, welded, [tuple(t) for t in triangles], attributes,
                   corners, seams, list(range(len(triangles))))


def validate_topology(positions, triangles):
    edges, faces = defaultdict(list), set()
    for number, triangle in enumerate(triangles):
        require(len(set(triangle)) == 3, "Degenerate triangle indices")
        require(dot(face_normal(positions, triangle), face_normal(positions, triangle)) > 1e-24,
                "Zero or numerically unusable source area")
        key = tuple(sorted(triangle))
        require(key not in faces, "Duplicate source triangle")
        faces.add(key)
        for a, b in face_edges(triangle):
            edges[edge(a, b)].append((a, b, number))
    for rows in edges.values():
        require(len(rows) <= 2, "Nonmanifold edge")
        if len(rows) == 2:
            require(rows[0][:2] == tuple(reversed(rows[1][:2])), "Inconsistently wound edge")
    return edges


class ConstrainedQem:
    """Stable face ordinals; coordinates never move, only unlocked endpoints retire.

    Source boundary vertices may receive a collapse, but are never removed.
    Both endpoints of an attribute seam edge are immutable; no chart transfer is
    inferred. Protected triangles retain their original vertex and corner IDs.
    Vertex-link connectivity is checked before any collapse. Pinched source
    vertices are frozen; final physical patch fans are separated explicitly.
    """
    def __init__(self, mesh, protected, maximum_normal_change_degrees=75):
        self.mesh = mesh
        self.faces = list(mesh.triangles)
        self.corners = list(mesh.corners)
        self.protected = frozenset(protected)
        require(all(type(v) is int and 0 <= v < len(self.faces) for v in self.protected),
                "Protected source triangle is outside source")
        require(0 < maximum_normal_change_degrees < 90, "Invalid inversion guard")
        self.minimum_dot = math.cos(math.radians(maximum_normal_change_degrees))
        self.edges, self.incident = defaultdict(set), [set() for _ in mesh.positions]
        self.face_keys = {}
        self.original_normals = [face_normal(mesh.positions, t) for t in self.faces]
        self.quadrics = [[0.0] * 16 for _ in mesh.positions]
        for f, triangle in enumerate(self.faces):
            self._insert(f, triangle)
            normal = self.original_normals[f]
            length = math.sqrt(dot(normal, normal))
            unit = tuple(v / length for v in normal)
            plane = unit + (-dot(unit, mesh.positions[triangle[0]]),)
            q = [plane[i] * plane[j] * length * .5 for i in range(4) for j in range(4)]
            for vertex in triangle:
                self.quadrics[vertex] = [a+b for a, b in zip(self.quadrics[vertex], q)]
        self.source_boundary = {v for e, fs in self.edges.items() if len(fs) == 1 for v in e}
        self.locked = self.source_boundary | set(mesh.seams) | {
            v for f in self.protected for v in self.faces[f]}
        self.pinches = {v for v in range(len(mesh.positions)) if not self._single_fan(v)}
        self.locked |= self.pinches
        self.frozen = set(mesh.seams) | self.pinches
        self.versions = [0] * len(mesh.positions)
        self.alive = len(self.faces)
        self.heap = []
        self.collapses = 0
        self.maximum_accepted_cost = 0.0
        self.maximum_endpoint_distance = 0.0
        self.rejections = defaultdict(int)

    def _insert(self, f, triangle):
        self.face_keys[tuple(sorted(triangle))] = f
        for v in triangle:
            self.incident[v].add(f)
        for a, b in face_edges(triangle):
            self.edges[edge(a, b)].add(f)

    def _remove(self, f):
        triangle = self.faces[f]
        self.face_keys.pop(tuple(sorted(triangle)))
        for v in triangle:
            self.incident[v].remove(f)
        for a, b in face_edges(triangle):
            key = edge(a, b)
            self.edges[key].remove(f)
            if not self.edges[key]:
                del self.edges[key]

    def _single_fan(self, vertex):
        remaining = set(self.incident[vertex])
        if not remaining:
            return True
        pending = [min(remaining)]
        remaining.remove(pending[0])
        while pending:
            f = pending.pop()
            for other in self.faces[f]:
                if other != vertex:
                    for neighbour in sorted(self.edges[edge(vertex, other)]):
                        if neighbour in remaining:
                            remaining.remove(neighbour)
                            pending.append(neighbour)
        return not remaining

    def neighbours(self, vertex):
        return {v for f in self.incident[vertex] for v in self.faces[f] if v != vertex}

    def _queue(self, a, b):
        if a in self.frozen or b in self.frozen or len(self.edges.get(edge(a, b), ())) != 2:
            return
        q = [x+y for x, y in zip(self.quadrics[a], self.quadrics[b])]
        for keep, remove in ((a, b), (b, a)):
            if remove in self.locked:
                continue
            p = self.mesh.positions[keep] + (1.0,)
            cost = max(0.0, sum(p[i] * q[i*4+j] * p[j] for i in range(4) for j in range(4)))
            heapq.heappush(self.heap, (cost, keep, remove, self.versions[keep], self.versions[remove]))

    def _rebuild_heap(self):
        self.heap = []
        for a, b in sorted(self.edges):
            self._queue(a, b)

    def proposal(self, keep, remove):
        if remove in self.locked or keep in self.frozen or remove in self.frozen:
            return None, "locked"
        shared = self.edges.get(edge(keep, remove), set())
        if len(shared) != 2:
            return None, "boundary"
        opposites = {v for f in shared for v in self.faces[f] if v not in (keep, remove)}
        if len(opposites) != 2 or self.neighbours(keep) & self.neighbours(remove) != opposites:
            return None, "link"
        changed = {}
        keys = set()
        for f in sorted(self.incident[remove]):
            if f in self.protected:
                return None, "protected"
            if f in shared:
                changed[f] = None
                continue
            before = self.faces[f]
            after = tuple(keep if v == remove else v for v in before)
            key = tuple(sorted(after))
            if key in keys or (key in self.face_keys and self.face_keys[key] != f):
                return None, "duplicate"
            keys.add(key)
            new_normal = face_normal(self.mesh.positions, after)
            length = math.sqrt(dot(new_normal, new_normal))
            if length <= 1e-12:
                return None, "degenerate"
            for normal in (face_normal(self.mesh.positions, before), self.original_normals[f]):
                if dot(normal, new_normal) <= self.minimum_dot * math.sqrt(dot(normal, normal)) * length:
                    return None, "inversion"
            corners = tuple(self.mesh.canonical_corner[keep] if v == remove else c
                            for v, c in zip(before, self.corners[f]))
            original_uv = uv_area(self.mesh.attributes, self.mesh.corners[f])
            new_uv = uv_area(self.mesh.attributes, corners)
            if abs(original_uv) > 1e-14 and (abs(new_uv) <= 1e-14 or original_uv * new_uv <= 0):
                return None, "uv-inversion"
            changed[f] = (after, corners)
        return changed, None

    def simplify(self, maximum_triangles, deadline=None):
        require(type(maximum_triangles) is int and maximum_triangles > 0, "Invalid triangle budget")
        if maximum_triangles < len(self.protected):
            raise BudgetError("Budget is smaller than the immutable protected faces")
        self._rebuild_heap()
        attempts = 0
        while self.alive > maximum_triangles and self.heap:
            attempts += 1
            if attempts % 128 == 0 and deadline is not None and time.monotonic() >= deadline:
                raise BudgetError("Authoring deadline expired before budget was reached")
            cost, keep, remove, kv, rv = heapq.heappop(self.heap)
            if kv != self.versions[keep] or rv != self.versions[remove]:
                continue
            changes, reason = self.proposal(keep, remove)
            if changes is None:
                self.rejections[reason] += 1
                continue
            affected = self.neighbours(keep) | self.neighbours(remove) | {keep, remove}
            for f in sorted(changes):
                self._remove(f)
            for f, result in sorted(changes.items()):
                if result is None:
                    self.faces[f] = None
                    self.alive -= 1
                else:
                    self.faces[f], self.corners[f] = result
                    self._insert(f, self.faces[f])
            self.quadrics[keep] = [a+b for a, b in zip(self.quadrics[keep], self.quadrics[remove])]
            for v in sorted(affected):
                self.versions[v] += 1
            for a, b in sorted({edge(v, n) for v in affected for n in self.neighbours(v)}):
                self._queue(a, b)
            self.collapses += 1
            self.maximum_accepted_cost = max(self.maximum_accepted_cost, cost)
            self.maximum_endpoint_distance = max(self.maximum_endpoint_distance,
                math.sqrt(dot(sub(self.mesh.positions[keep], self.mesh.positions[remove]),
                              sub(self.mesh.positions[keep], self.mesh.positions[remove]))))
            if len(self.heap) > max(1000, len(self.edges) * 12):
                self._rebuild_heap()
        if self.alive > maximum_triangles:
            raise BudgetError(f"Constraints stop at {self.alive} triangles; budget {maximum_triangles}. "
                              f"Rejections: {dict(sorted(self.rejections.items()))}")
        surviving = [i for i, f in enumerate(self.faces) if f is not None]
        validate_topology(self.mesh.positions, [self.faces[i] for i in surviving])
        for f in sorted(self.protected):
            require(self.faces[f] == self.mesh.triangles[f] and self.corners[f] == self.mesh.corners[f],
                    "Protected face or attributes changed")
        for a, b in self.source_edges():
            require(edge(a, b) in self.edges and len(self.edges[edge(a, b)]) == 1,
                    "Original open boundary changed")
        return surviving

    def source_edges(self):
        rows = validate_topology(self.mesh.positions, self.mesh.triangles)
        return [e for e, faces in rows.items() if len(faces) == 1]

    def receipt(self):
        return dict(method="area-weighted endpoint-only constrained QEM", collapses=self.collapses,
                    triangles=self.alive, protectedTriangles=len(self.protected),
                    lockedVertices=len(self.locked), attributeSeamVertices=len(self.mesh.seams),
                    frozenPinchedVertices=len(self.pinches), maximumAcceptedQemCost=self.maximum_accepted_cost,
                    maximumEndpointCollapseDistanceMetres=self.maximum_endpoint_distance,
                    rejected=dict(sorted(self.rejections.items())),
                    globalSelfIntersectionAndHausdorffError="not-qualified")


def split_patch_fans(positions, triangles, memberships):
    """Duplicate only disconnected per-patch fans; never connect loops spatially.

    This can introduce coincident index seams against another patch. Coordinates
    and triangle geometry stay stitched; indexed watertightness is not claimed.
    """
    output = list(positions)
    result = [list(t) for t in triangles]
    incident = defaultdict(set)
    edges = defaultdict(list)
    for f, triangle in enumerate(triangles):
        for v in triangle:
            incident[v].add(f)
        for a, b in face_edges(triangle):
            edges[edge(a, b)].append(f)
    duplicates = []
    for vertex in sorted(incident):
        for patch in sorted({memberships[f] for f in incident[vertex]}):
            remaining = {f for f in incident[vertex] if memberships[f] == patch}
            first = True
            while remaining:
                seed = min(remaining)
                remaining.remove(seed)
                pending, fan = [seed], [seed]
                while pending:
                    f = pending.pop()
                    for other in triangles[f]:
                        if other == vertex:
                            continue
                        for neighbour in sorted(edges[edge(vertex, other)]):
                            if neighbour in remaining:
                                remaining.remove(neighbour)
                                pending.append(neighbour)
                                fan.append(neighbour)
                target = vertex
                if not first:
                    target = len(output)
                    output.append(positions[vertex])
                    duplicates.append(dict(original=vertex, duplicate=target, patch=patch, triangles=sorted(fan)))
                for f in fan:
                    result[f][triangles[f].index(vertex)] = target
                first = False
    for before, after in zip(triangles, result):
        require([positions[v] for v in before] == [output[v] for v in after], "Fan splitting moved geometry")
    return output, result, duplicates


def patch_boundaries(triangles, memberships, patch, patch_key=None, source_anchor=None):
    label = "Physical patch " + (patch_key if patch_key is not None else str(patch))
    if source_anchor is not None:
        label += f" (source triangle anchor {source_anchor})"
    edges = defaultdict(list)
    for f, triangle in enumerate(triangles):
        if memberships[f] == patch:
            for a, b in face_edges(triangle):
                edges[edge(a, b)].append((a, b))
    boundary = []
    for rows in edges.values():
        require(len(rows) <= 2, label + ": nonmanifold edge")
        if len(rows) == 1:
            boundary.append(rows[0])
        else:
            require(rows[0] == tuple(reversed(rows[1])), label + ": winding mismatch")
    outgoing, incoming = {}, {}
    for a, b in boundary:
        require(a not in outgoing and b not in incoming, label + ": unsplit boundary branch")
        outgoing[a], incoming[b] = b, a
    require(set(outgoing) == set(incoming), label + ": open domain chain")
    remaining, loops = set(outgoing), []
    while remaining:
        start = min(remaining)
        loop, v = [], start
        while v in remaining:
            remaining.remove(v)
            loop.append(v)
            v = outgoing[v]
        require(v == start and len(loop) >= 3, label + ": invalid boundary cycle")
        require(len(loop) <= 4096, label + f": boundary has {len(loop)} vertices; F3 limit is 4096")
        loops.append(loop)
    require(len(loops) <= 64, label + f": {len(loops)} boundary loops; F3 limit is 64. "
            "No automatic connected-domain subdivision or hole filling is allowed.")
    return sorted(loops)


def decode_source(root, selection, manifest):
    """Strict supported subset: one identity node, one static triangle primitive."""
    path = contained(root, selection["near"]["file"])
    require(sha256(path) == selection["near"]["sha256"], "Near glTF digest mismatch")
    assets = [a for a in manifest["assets"] if a["key"] == selection["asset"]]
    require(len(assets) == 1, "Missing/duplicate provenance asset")
    rows = [r for r in assets[0]["outputs"] if r["file"] == selection["near"]["file"]]
    require(len(rows) == 1 and rows[0]["sha256"] == selection["near"]["sha256"],
            "Near source does not match pinned provenance")
    require(rows[0]["dependencies"] == selection["near"]["dependencies"], "Dependency receipt changed")
    document = strict_json(path, expected_sha256=selection["near"]["sha256"])
    require(document.get("asset", {}).get("version") == "2.0" and
            not any(document.get(k) for k in ("animations", "skins", "extensionsUsed", "extensionsRequired")),
            "Unsupported glTF version/extension/animation")
    require(document.get("scene", 0) == 0 and len(document["scenes"]) == 1 and
            document["scenes"][0]["nodes"] == [0] and len(document["nodes"]) == 1 and
            set(document["nodes"][0]) <= {"name", "mesh"} and document["nodes"][0]["mesh"] == 0,
            "Expected one untransformed source node")
    require(len(document["meshes"]) == 1 and len(document["meshes"][0]["primitives"]) == 1,
            "Expected one source primitive")
    primitive = document["meshes"][0]["primitives"][0]
    require(set(primitive) <= {"attributes", "indices", "material", "mode"} and primitive.get("mode", 4) == 4,
            "Unsupported primitive")
    require(set(primitive["attributes"]) == {"POSITION", "NORMAL", "TEXCOORD_0"}, "Unsupported attribute layout")
    require(len(document.get("materials", [])) == 1 and primitive.get("material") == 0,
            "Expected one source material")
    require(len(document["buffers"]) == 1, "Expected one source buffer")
    dependencies = {row["file"]: row["sha256"] for row in rows[0]["dependencies"]}
    require(len(dependencies) == len(rows[0]["dependencies"]), "Duplicate dependency receipt")
    actual, buffers = set(), []
    for field in ("buffers", "images"):
        for item in document.get(field, []):
            file = uri_file(path.parent, item["uri"])
            relative = file.relative_to(Path(root).resolve()).as_posix()
            require(relative in dependencies and sha256(file) == dependencies[relative], "Dependency digest mismatch")
            actual.add(relative)
            if field == "buffers":
                require(file.stat().st_size == item["byteLength"] <= 16*1024*1024, "Buffer byte budget/length mismatch")
                binary = file.read_bytes()
                require(hashlib.sha256(binary).hexdigest() == dependencies[relative], "Loaded buffer digest mismatch")
                buffers.append(binary)
    require(actual == set(dependencies), "Receipt has missing or extra dependencies")
    def accessor(index, kind, components):
        require(type(index) is int and 0 <= index < len(document["accessors"]), "Accessor out of range")
        row = document["accessors"][index]
        require(row.get("type") == kind and "sparse" not in row and not row.get("normalized")
                and row["componentType"] in components, "Unsupported accessor")
        require(type(row["count"]) is int and 0 < row["count"] <= 150000, "Accessor count bound")
        number = row["bufferView"]
        require(type(number) is int and 0 <= number < len(document["bufferViews"]), "Buffer view out of range")
        view = document["bufferViews"][number]
        require(view["buffer"] == 0, "Unexpected buffer")
        dtype, size = {5126: ("f", 4), 5125: ("I", 4), 5123: ("H", 2)}[row["componentType"]]
        width = {"VEC3": 3, "VEC2": 2, "SCALAR": 1}[kind]
        offset, view_offset = row.get("byteOffset", 0), view.get("byteOffset", 0)
        stride = view.get("byteStride", size * width)
        require(all(type(v) is int and v >= 0 for v in (offset, view_offset, stride, view["byteLength"]))
                and stride >= size * width and stride % size == 0 and offset % size == 0 and view_offset % size == 0,
                "Invalid accessor alignment")
        require(offset + (row["count"]-1)*stride + size*width <= view["byteLength"] and
                view_offset + view["byteLength"] <= len(buffers[0]), "Accessor exceeds buffer view")
        return [struct.unpack_from("<"+dtype*width, buffers[0], view_offset+offset+i*stride)
                for i in range(row["count"])]
    positions = accessor(primitive["attributes"]["POSITION"], "VEC3", {5126})
    normals = accessor(primitive["attributes"]["NORMAL"], "VEC3", {5126})
    uvs = accessor(primitive["attributes"]["TEXCOORD_0"], "VEC2", {5126})
    indices = [r[0] for r in accessor(primitive["indices"], "SCALAR", {5123, 5125})]
    require(len(indices) % 3 == 0 and len(indices)//3 == rows[0]["triangles"], "Source triangle count mismatch")
    return Mesh.from_arrays(positions, normals, uvs, [indices[i:i+3] for i in range(0, len(indices), 3)]), document, path


def write_new(path, data):
    with Path(path).open("xb") as stream:
        stream.write(data)


def export_visual(destination, key, source_document, mesh, simplifier, surviving):
    """Serialize retained source attributes; preserve material/image/sampler definitions."""
    attributes, corner_map, indices = [], {}, []
    for f in surviving:
        for corner in simplifier.corners[f]:
            if corner not in corner_map:
                corner_map[corner] = len(attributes)
                attributes.append(mesh.attributes[corner])
            indices.append(corner_map[corner])
    blob, views, accessors = bytearray(), [], []
    for column, width, kind in ((0, 3, "VEC3"), (1, 3, "VEC3"), (2, 2, "VEC2")):
        values = [a[column] for a in attributes]
        offset = len(blob)
        for value in values:
            blob.extend(struct.pack("<"+"f"*width, *value))
        views.append(dict(buffer=0, byteOffset=offset, byteLength=len(blob)-offset, target=34962))
        item = dict(bufferView=len(views)-1, componentType=5126, count=len(values), type=kind)
        if column == 0:
            item.update(min=[min(v[i] for v in values) for i in range(3)],
                        max=[max(v[i] for v in values) for i in range(3)])
        accessors.append(item)
    offset = len(blob)
    for index in indices:
        blob.extend(struct.pack("<I", index))
    views.append(dict(buffer=0, byteOffset=offset, byteLength=len(blob)-offset, target=34963))
    accessors.append(dict(bufferView=3, componentType=5125, count=len(indices), type="SCALAR"))
    document = {k: source_document[k] for k in ("materials", "textures", "images", "samplers") if k in source_document}
    document.update(asset=dict(version="2.0", generator="Hollow constrained cliff authoring v1"),
                    scene=0, scenes=[dict(nodes=[0])], nodes=[dict(mesh=0, name=key)],
                    meshes=[dict(primitives=[dict(attributes=dict(POSITION=0, NORMAL=1, TEXCOORD_0=2), indices=3, material=0)])],
                    buffers=[dict(uri=key+".bin", byteLength=len(blob))], bufferViews=views, accessors=accessors)
    write_new(destination / (key+".bin"), bytes(blob))
    write_new(destination / (key+".gltf"), canonical(document))
    return key+".gltf"


def partition_physical_patches(triangles, source_triangle_ids, protected):
    """Separate edge-disconnected bare components, without cutting any surface.

    Keys use the minimum original source triangle ordinal, never discovery order
    or compacted output ordinals. A changed source/reduction still requires a new
    geometry revision. Vertex-only contact is not an edge connection. The reviewed
    crown remains a single authored region. A connected component exceeding an F3
    domain limit fails later; this function performs no recursive subdivision.
    """
    require(len(triangles) == len(source_triangle_ids) and len(set(source_triangle_ids)) == len(triangles)
            and all(type(f) is int and f >= 0 for f in source_triangle_ids),
            "Physical partition requires unique original source triangle IDs")
    require(set(protected) <= set(source_triangle_ids), "A protected source triangle disappeared before partitioning")
    bare = {f for f, source in enumerate(source_triangle_ids) if source not in protected}
    edges = defaultdict(list)
    for f in sorted(bare, key=lambda i: source_triangle_ids[i]):
        for a, b in face_edges(triangles[f]):
            edges[edge(a, b)].append(f)
    memberships = [-1] * len(triangles)
    descriptions = []
    remaining = set(bare)
    for seed in sorted(bare, key=lambda i: source_triangle_ids[i]):
        if seed not in remaining:
            continue
        remaining.remove(seed)
        pending = [(source_triangle_ids[seed], seed)]
        faces = []
        while pending:
            _, f = heapq.heappop(pending)
            faces.append(f)
            for a, b in face_edges(triangles[f]):
                for neighbour in edges[edge(a, b)]:
                    if neighbour in remaining:
                        remaining.remove(neighbour)
                        heapq.heappush(pending, (source_triangle_ids[neighbour], neighbour))
        sources = sorted(source_triangle_ids[f] for f in faces)
        patch = len(descriptions)
        for f in faces:
            memberships[f] = patch
        descriptions.append(dict(key=f"bare-rock-f{sources[0]:06d}", semanticRole="bare-rock",
            minimumSourceTriangle=sources[0], sourceTriangleIds=sources,
            partitionMethod="edge-connected surviving source triangles; no added cuts"))
    crown = [f for f, source in enumerate(source_triangle_ids) if source in protected]
    if crown:
        sources = sorted(source_triangle_ids[f] for f in crown)
        patch = len(descriptions)
        for f in crown:
            memberships[f] = patch
        descriptions.append(dict(key="preserved-crown", semanticRole="preserved-crown",
            minimumSourceTriangle=sources[0], sourceTriangleIds=sources,
            partitionMethod="explicit reviewed source triangle selection"))
    require(all(p >= 0 for p in memberships), "Physical partition omitted a triangle")
    require(len(descriptions) <= 256,
            f"Physical partition has {len(descriptions)} patches; F3 limit is 256")
    return memberships, descriptions


def physical_geometry(mesh, simplifier, surviving, protected):
    used = sorted({v for f in surviving for v in simplifier.faces[f]})
    remap = {old: new for new, old in enumerate(used)}
    positions = [mesh.positions[v] for v in used]
    triangles = [[remap[v] for v in simplifier.faces[f]] for f in surviving]
    memberships, partition = partition_physical_patches(triangles, surviving, protected)
    positions, triangles, splits = split_patch_fans(positions, triangles, memberships)
    domains = [dict(patchKey=row["key"], boundaries=patch_boundaries(triangles, memberships, p,
                    row["key"], row["minimumSourceTriangle"])) for p, row in enumerate(partition)]
    for row, domain in zip(partition, domains):
        row["boundaryLoopCount"] = len(domain["boundaries"])
        row["boundaryVertexCount"] = sum(len(loop) for loop in domain["boundaries"])
    boundary_vertices = sum(row["boundaryVertexCount"] for row in partition)
    require(boundary_vertices <= 65536,
            f"Physical domains have {boundary_vertices} boundary vertices; F3 limit is 65536")
    require(len(positions) <= 8192 and len(triangles) <= 16384,
            f"Physical geometry has {len(positions)} vertices/{len(triangles)} triangles; F3 limits are 8192/16384")
    geometry = dict(vertices=positions, triangles=[t+[p] for t, p in zip(triangles, memberships)],
                    patches=[dict(key=row["key"], substrateProfileKey="qualification-bare", capabilities=[])
                             for row in partition])
    return geometry, domains, splits, partition


def verify_visual_roundtrip(destination, name, mesh, corners, source_document):
    """Validate serialized geometry and every retained PBR/attribute value."""
    document = strict_json(destination/name)
    dependencies = [dict(file=row["uri"], sha256=sha256(destination/row["uri"]))
                    for field in ("buffers", "images") for row in document.get(field, [])]
    near = dict(file=name, sha256=sha256(destination/name), dependencies=dependencies,
                triangles=len(corners))
    decoded, reread, _ = decode_source(destination, dict(asset="roundtrip", near=near),
        dict(assets=[dict(key="roundtrip", outputs=[near])]))
    require(len(decoded.corners) == len(corners), "Roundtrip triangle count mismatch")
    for original, restored in zip(corners, decoded.corners):
        require([mesh.attributes[v] for v in original] == [decoded.attributes[v] for v in restored],
                "Serialized visual corner POSITION/NORMAL/UV changed")
    for key in ("materials", "textures", "images", "samplers"):
        require(reread.get(key) == source_document.get(key), "PBR material provenance changed")


def contact_proposals(geometry, domains):
    # Actual oriented boundary edges only. No burial/gap tuning is approved here.
    proposals = []
    for domain in domains:
        candidates = [(sum(geometry["vertices"][v][1] for v in (loop[i], loop[(i+1) % len(loop)]))*.5,
                       loop[i], loop[(i+1) % len(loop)])
                      for loop in domain["boundaries"] for i in range(len(loop))]
        if candidates:
            height, a, b = min(candidates)
            proposals.append(dict(patchKey=domain["patchKey"], vertices=[a, b], meanLocalY=height,
                selection="Lowest mean-Y actual boundary edge; proposal only, not contact approval",
                role=None, minimumBurialMetres=None, maximumBurialMetres=None, maximumGapMetres=None))
    return proposals


def author(root, configuration, asset, destination, seconds=55):
    root, destination = Path(root).resolve(), Path(destination).resolve()
    require(not destination.exists(), "Destination must be fresh; overwrite and resume are forbidden")
    require(destination.parent.is_dir(), "Destination parent must already exist")
    require(destination.is_relative_to(root), "Destination must remain within the selected project root")
    require(not destination.is_relative_to(root / "game") and not destination.is_relative_to(root / "src"),
            "Live game/source destinations are forbidden")
    require(math.isfinite(seconds) and 0 < seconds <= 600, "Invalid authoring wall-clock limit")
    deadline = time.monotonic() + seconds
    configuration_digest = sha256(configuration)
    config = strict_json(configuration, expected_sha256=configuration_digest)
    require(config["schemaVersion"] == 1, "Unsupported selection schema")
    manifest_path = contained(root, config["manifest"]["file"])
    require(sha256(manifest_path) == config["manifest"]["sha256"], "Provenance manifest digest mismatch")
    entries = [row for row in config["assets"] if row["asset"] == asset]
    require(len(entries) == 1, "Unknown/duplicate selected asset")
    selection = entries[0]
    require(selection["approval"] == "geometry-preservation-only" and selection["capabilities"] == [],
            "This authoring slice cannot grant support or habitat capabilities")
    protected = selection["protectedSourceTriangles"]
    require(protected == sorted(set(protected)) and len(protected) == selection["protectedTriangleCount"]
            and hashlib.sha256(canonical(protected)).hexdigest() == selection["protectedTriangleIdsSha256"],
            "Authored triangle selection changed")
    mesh, document, source_path = decode_source(root, selection,
        strict_json(manifest_path, expected_sha256=config["manifest"]["sha256"]))
    protected = frozenset(protected)
    simplifier = ConstrainedQem(mesh, protected, selection["maximumNormalChangeDegrees"])
    surviving = simplifier.simplify(selection["physicalTriangleBudget"], deadline)
    require(time.monotonic() < deadline, "Authoring wall-clock limit exceeded")
    physical, domains, splits, partition = physical_geometry(mesh, simplifier, surviving, protected)
    # All geometry checks precede output creation. A later I/O failure leaves an
    # explicitly incomplete fresh directory without qualification-receipt.json.
    destination.mkdir()
    near_name = asset + "_qualified_lod0.gltf"
    near_document = json.loads(json.dumps(document))
    buffer_name = asset + "_qualified_lod0.bin"
    near_document["buffers"][0]["uri"] = buffer_name
    source_buffer = uri_file(source_path.parent, document["buffers"][0]["uri"])
    write_new(destination / buffer_name, source_buffer.read_bytes())
    dependency_hashes = {Path(row["file"]).name: row["sha256"] for row in selection["near"]["dependencies"]}
    require(sha256(destination/buffer_name) == dependency_hashes[source_buffer.name], "Near buffer copy digest mismatch")
    write_new(destination / near_name, canonical(near_document))
    for item in document.get("images", []):
        source_image = uri_file(source_path.parent, item["uri"])
        # Copy streams rather than retaining 4K PBR images in process memory.
        with source_image.open("rb") as src, (destination / item["uri"]).open("xb") as dst:
            for block in iter(lambda: src.read(1024*1024), b""):
                dst.write(block)
        require(dependency_hashes[source_image.name] == sha256(destination / item["uri"]), "Texture copy mismatch")
    far_name = export_visual(destination, asset+"_qualified_lod1", document, mesh, simplifier, surviving)
    verify_visual_roundtrip(destination, near_name, mesh, mesh.corners, document)
    verify_visual_roundtrip(destination, far_name, mesh, [simplifier.corners[f] for f in surviving], document)
    write_new(destination / "physical.json", canonical(physical))
    near_maps = [[p, f] for p, f in enumerate(surviving) if f in protected]
    far_maps = [[p, p] for p, f in enumerate(surviving) if f in protected]
    for p, f in near_maps:
        corners = physical["triangles"][p][:3]
        require([physical["vertices"][v] for v in corners] == [mesh.positions[v] for v in mesh.triangles[f]],
                "Protected physical-to-near correspondence changed")
        require(simplifier.corners[f] == mesh.corners[f], "Protected visual UV/normal correspondence changed")
    definition = dict(key=asset+"-qualified", revision=1, familyKey=selection["familyKey"],
        sourceSha256=selection["near"]["sha256"], physicalSha256=sha256(destination/"physical.json"),
        units="metres", basis="right-handed-y-up", minimumScale=1, maximumScale=1,
        physical=physical, domains=domains, contacts=[],
        # Position error is exactly zero. The normal-dot margin accounts only
        # for unit-vector arithmetic in the engine-free validator.
        lods=[dict(key=key, assetPath=name, contentSha256=sha256(destination/name),
                   maximumPositionErrorMetres=0, minimumNormalDot=0.999999999999, triangleMaps=maps)
              for key, name, maps in (("near", near_name, near_maps), ("far", far_name, far_maps))])
    profile = dict(key="qualification-bare", bedrock=1, looseStone=0, soil=0, soilDepthMetres=0,
                   plantSuitability=0, grassCoverage=0, shrubsPerSquareMetre=0, treesPerSquareMetre=0,
                   litter=0, minimumTemperatureC=0, maximumTemperatureC=40, minimumSoilUpDot=.6)
    write_new(destination/"sidecar.json", canonical(dict(schemaVersion=1, definitions=[definition], profiles=[profile], recipes=[])))
    write_new(destination/"contact-proposals.json", canonical(dict(status="pending-root-review", proposals=contact_proposals(physical, domains))))
    write_new(destination/"triangle-provenance.json", canonical(dict(sourceTriangleIds=surviving,
        protectedSourceTriangleIds=sorted(protected), physicalFanSplits=splits, physicalPatchPartition=partition,
        fanSplitNote="Coincident index seams possible; exact triangle positions preserved; no caps/holes repaired")))
    files = [dict(file=p.name, sha256=sha256(p), bytes=p.stat().st_size) for p in sorted(destination.iterdir())]
    receipt = dict(schemaVersion=1, status="prototype-awaiting-root-validation-no-habitat-approval",
        selectionFileSha256=configuration_digest, toolSha256=sha256(Path(__file__)),
        source=selection["near"], manifest=config["manifest"], outputFiles=files,
        reduction=simplifier.receipt(), physicalVertexCount=len(physical["vertices"]),
        fanSplitCount=len(splits), physicalPatchCount=len(partition),
        physicalPatchLoops=[dict(key=row["key"], loops=row["boundaryLoopCount"],
                                triangles=len(row["sourceTriangleIds"])) for row in partition],
        physicalBudget=selection["physicalTriangleBudget"],
        exactProtectedPositionUvNormalAndWinding=True, outputsShareOriginalPbrBytes=True,
        pending=["synthetic tests", "F3 decoder/constructor validation", "curved crown containment",
                 "global intersection and unprotected approximation review", "contact authoring",
                 "scale qualification", "render inspection", "performance measurement", "habitat approval"])
    write_new(destination/"qualification-receipt.json", canonical(receipt))
    return receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--selection", type=Path, default=Path(__file__).with_name("data")/"cliff_support_regions.json")
    parser.add_argument("--asset", required=True)
    parser.add_argument("--destination", type=Path, required=True)
    parser.add_argument("--seconds", type=float, default=55)
    args = parser.parse_args()
    try:
        result = author(args.root, args.selection, args.asset, args.destination, args.seconds)
    except (QualificationError, OSError, KeyError, TypeError, IndexError, struct.error) as exc:
        parser.exit(2, f"QUALIFICATION_FAILED: {exc}\n")
    print(json.dumps(dict(status=result["status"], triangles=result["reduction"]["triangles"],
                          destination=str(args.destination.resolve())), sort_keys=True))


if __name__ == "__main__":
    main()

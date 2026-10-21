"""Receipt-bound, review-only root proposals for the four existing authored cover families.

Standard-library Python; no bpy, network, scene export, source edits or asset admission.
Run only when the coordinating agent releases the build/capture resource window:
  python tools/blender/ground_cover_roots.py --destination .artifacts/cover-roots-NEW
  python -m unittest discover -s tools/blender/tests -p test_ground_cover_roots.py

Every triangle corner gets the stable mesh-local integer 3*triangle+corner+1. A future
owned exporter may carry it losslessly in unused UV2.U (integers <2^24), splitting
vertices as needed while copying *all* source corner attributes. This tool does not
claim Blender/Godot preserves source ordering or custom glTF attributes. Exported GLB
hash, all imported corner IDs, positions and original attributes must be checked in
that later step before CUSTOM0/runtime admission. No fallback by nearest position.

Topology and lowest-boundary points are hypotheses, never artistic root truth.
Proposals include three orthographic SVG views with candidate roots, no automatic
approval. Ambiguous components get no candidate root. A reviewer may explicitly
merge/split components in a recipe bound to each proposal's SHA-256. A reviewed
sidecar still has runtimeAdmitted=false: it has no export/import/terrain-contact proof.

Review file schema: {schema:'hollow.cover-root-review.v1', meshes:[{
  meshKey, proposalSha256, geometrySha256, reviewer, note, groups:[{
    key, intent:'card'|'stem'|'billboard', triangles:[source triangle ordinals],
    rootLocal:[x,y,z], rootZoneCornerIds:[...], supportCornerIds:[...],
    maximumRootDistanceMetres, maximumHeightSpreadMetres, maximumSlopeDegrees,
    growthAxis:[unit x,y,z], growthLengthMetres, windWeight, windPhase
  }]}]}. Coordinates are glTF Y-up metres after the declared source node TRS.
Each triangle must belong to exactly one reviewed root group. Growth is explicitly
authored axial distance from root, clamped to [0,1]; _wind raw ushort values remain
opaque provenance and are never guessed to be roots, growth weights or phases.
"""
from __future__ import annotations

import argparse
from collections import defaultdict
from dataclasses import dataclass
import hashlib
import html
import json
import math
from pathlib import Path
import re
import struct
import time


SCHEMA = "hollow.cover-root-proposal.v1"
FAMILIES = ("wild", "cotton", "tundra", "fern")
MAX_VERTICES, MAX_TRIANGLES, MAX_MESHES = 60000, 60000, 36
MAX_SOURCE_BYTES, MAX_OUTPUT_BYTES = 64 * 1024 * 1024, 128 * 1024 * 1024
MAX_CORNERS = 1000000


class RootError(ValueError):
    pass


def require(value, message):
    if not value:
        raise RootError(message)


def canonical(value):
    return (json.dumps(value, sort_keys=True, separators=(",", ":"),
                       ensure_ascii=False, allow_nan=False) + "\n").encode("utf-8")


def digest(data):
    return hashlib.sha256(data).hexdigest()


def bounded_read(path, maximum):
    require(path.is_file() and path.stat().st_size <= maximum, "Missing or oversized input: " + str(path))
    with path.open("rb") as stream:
        data = stream.read(maximum + 1)
    require(len(data) <= maximum, "Input grew beyond its byte budget: " + str(path))
    return data


def strict_json(raw):
    require(len(raw) <= 16 * 1024 * 1024, "JSON exceeds the finite input budget")
    def pairs(items):
        result = {}
        for key, value in items:
            require(key not in result, "Duplicate JSON property: " + key)
            result[key] = value
        return result
    def bad(value):
        raise RootError("Non-finite JSON number: " + value)
    return json.loads(raw.decode("utf-8-sig"), object_pairs_hook=pairs, parse_constant=bad)


def integer(value, minimum, maximum, name):
    require(type(value) is int and minimum <= value <= maximum, "Invalid " + name)
    return value


def finite(value):
    return type(value) in (float, int) and math.isfinite(value)


def vector(value, count=3):
    require(isinstance(value, (list, tuple)) and len(value) == count and all(finite(x) for x in value),
            "Expected finite numeric vector")
    return tuple(float(x) for x in value)


class Budget:
    def __init__(self, seconds=60):
        require(finite(seconds) and 0 < seconds <= 120, "Inspection deadline must be in (0,120] seconds")
        self.deadline = time.monotonic() + seconds
        self.corners = self.output = 0

    def check(self):
        require(time.monotonic() < self.deadline, "Root inspection exceeded its deadline; no review manifest published")

    def charge_corners(self, count):
        self.check()
        self.corners += count
        require(self.corners <= MAX_CORNERS, "Aggregate corner budget exceeded")

    def write(self, path, data):
        self.check()
        self.output += len(data)
        require(self.output <= MAX_OUTPUT_BYTES, "Aggregate diagnostic output byte budget exceeded")
        with path.open("xb") as stream:
            stream.write(data)


def authenticated(path, expected, maximum, budget):
    budget.check()
    require(re.fullmatch(r"[0-9a-f]{64}", expected or "") is not None, "Missing SHA-256 receipt")
    data = bounded_read(path, maximum)
    require(len(data) <= maximum and digest(data) == expected, "Input does not match its catalog receipt: " + str(path))
    return data


def accessor(doc, buffers, index, maximum, decode=True):
    """Decode a bounded source stream and hash its packed element bytes, preserving raw integer wind data."""
    index = integer(index, 0, len(doc["accessors"]) - 1, "accessor index")
    a = doc["accessors"][index]
    require("sparse" not in a and "extensions" not in a, "Sparse/extended accessors need an explicit reader")
    count = integer(a["count"], 1, maximum, "accessor count")
    widths = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}
    types = {5121: ("B", 1), 5123: ("H", 2), 5125: ("I", 4), 5126: ("f", 4)}
    require(a["type"] in widths and a["componentType"] in types, "Unsupported accessor representation")
    components = widths[a["type"]]
    code, size = types[a["componentType"]]
    normalized = a.get("normalized", False)
    require(type(normalized) is bool and not (normalized and code in ("I", "f")), "Invalid normalized accessor")
    view = doc["bufferViews"][integer(a["bufferView"], 0, len(doc["bufferViews"]) - 1, "buffer view")]
    require("extensions" not in view, "Compressed buffer views need an explicit reader")
    data = buffers[integer(view["buffer"], 0, len(buffers) - 1, "buffer index")]
    start = integer(view.get("byteOffset", 0), 0, len(data), "view offset")
    length = integer(view["byteLength"], 0, len(data) - start, "view length")
    offset = integer(a.get("byteOffset", 0), 0, length, "accessor offset")
    element = components * size
    stride = integer(view.get("byteStride", element), element, 252, "accessor stride")
    require(stride % size == 0 and (start + offset) % size == 0 and offset + (count - 1) * stride + element <= length,
            "Accessor range/alignment exceeds its view")
    rows, packed = [], bytearray()
    unpack = struct.Struct("<" + code * components)
    for i in range(count):
        at = start + offset + i * stride
        values = unpack.unpack_from(data, at)
        require(all(math.isfinite(x) for x in values), "Non-finite source attribute")
        if decode:
            rows.append(values)
        packed.extend(data[at:at + element])
    return rows, {"accessor": index, "componentType": a["componentType"], "type": a["type"],
                  "normalized": normalized, "count": count, "packedBytesSha256": digest(packed)}


def transformed(positions, node):
    require(not any(key in node for key in ("matrix", "children", "skin", "weights", "extensions")),
            "Only explicit single-root TRS source nodes are supported")
    translation = vector(node.get("translation", [0, 0, 0]))
    scale = vector(node.get("scale", [1, 1, 1]))
    require(all(x > 0 for x in scale), "Negative/zero scale requires explicit handedness review")
    q = vector(node.get("rotation", [0, 0, 0, 1]), 4)
    norm = math.sqrt(sum(x*x for x in q))
    require(abs(norm - 1) <= 0.00001, "Source quaternion is not unit length")
    x, y, z, w = (v / norm for v in q)
    rotation = ((1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)),
                (2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)),
                (2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)))
    result = []
    for p in positions:
        v = tuple(p[i]*scale[i] for i in range(3))
        result.append(tuple(sum(rotation[i][j]*v[j] for j in range(3)) + translation[i] for i in range(3)))
    require(all(all(math.isfinite(x) for x in p) for p in result), "Non-finite transformed source position")
    return result


@dataclass
class SourceMesh:
    positions: list
    triangles: list
    wind: list
    attribute_receipts: dict
    source: dict

    def __post_init__(self):
        require(0 < len(self.positions) <= MAX_VERTICES and 0 < len(self.triangles) <= MAX_TRIANGLES,
                "Mesh exceeds explicit inspection limits")
        require(len(self.wind) == len(self.positions), "Wind/position count mismatch")
        self.positions = [vector(p) for p in self.positions]
        for values in self.wind:
            require(len(values) == 4 and all(type(x) is int and 0 <= x <= 65535 for x in values), "Expected raw ushort4 wind values")
        for triangle in self.triangles:
            require(len(triangle) == 3 and len(set(triangle)) == 3 and
                    all(type(i) is int and 0 <= i < len(self.positions) for i in triangle), "Invalid/degenerate indexed triangle")
        require(3*len(self.triangles) < (1 << 24), "Corner IDs cannot be represented exactly in float32")

    @property
    def geometry_sha256(self):
        return digest(canonical({"source": self.source, "attributes": self.attribute_receipts,
                                 "positions": self.positions, "triangles": self.triangles}))


def load_mesh(doc, buffers, node_index, source, budget):
    node = doc["nodes"][node_index]
    mesh_index = integer(node["mesh"], 0, len(doc["meshes"]) - 1, "mesh index")
    mesh = doc["meshes"][mesh_index]
    require(len(mesh["primitives"]) == 1 and "weights" not in mesh, "Multiple primitives or morphs require explicit authoring")
    primitive = mesh["primitives"][0]
    require(primitive.get("mode", 4) == 4 and "targets" not in primitive and "extensions" not in primitive,
            "Only plain indexed triangles are supported")
    attributes = primitive["attributes"]
    require(3 <= len(attributes) <= 16, "Source attribute count exceeds the explicit inspection ceiling")
    require("POSITION" in attributes and "TEXCOORD_0" in attributes and "_wind" in attributes,
            "Expected known source POSITION/TEXCOORD_0/_wind contract")
    require("TEXCOORD_1" not in attributes, "UV2 already carries authored data; refuse proposed corner-ID channel")
    decoded, receipts = {}, {}
    for name, index in sorted(attributes.items()):
        budget.check()
        decoded[name], receipts[name] = accessor(doc, buffers, index, MAX_VERTICES, decode=name in ("POSITION", "_wind"))
    count = len(decoded["POSITION"])
    require(all(receipt["count"] == count for receipt in receipts.values()), "Vertex attribute counts disagree")
    require(receipts["POSITION"]["componentType"] == 5126 and receipts["POSITION"]["type"] == "VEC3" and
            receipts["_wind"]["componentType"] == 5123 and receipts["_wind"]["type"] == "VEC4" and receipts["_wind"]["normalized"],
            "Position/wind representation changed; refuse guessed semantics")
    indices, receipts["indices"] = accessor(doc, buffers, primitive["indices"], 3*MAX_TRIANGLES)
    require(receipts["indices"]["type"] == "SCALAR" and receipts["indices"]["componentType"] in (5123, 5125)
            and not receipts["indices"]["normalized"] and len(indices) % 3 == 0, "Expected ushort/uint triangle indices")
    triangles = [tuple(indices[i+j][0] for j in range(3)) for i in range(0, len(indices), 3)]
    source = dict(source, nodeIndex=node_index, nodeName=node["name"], meshIndex=mesh_index, primitive=0,
                  nodeTransform={k: node[k] for k in ("translation", "rotation", "scale") if k in node})
    return SourceMesh(transformed(decoded["POSITION"], node), triangles, decoded["_wind"], receipts, source)


def components(mesh):
    """Index-edge connectivity only: no proximity welding, height-based grouping or attribute changes."""
    edges = defaultdict(list)
    for t, triangle in enumerate(mesh.triangles):
        for a, b in ((triangle[0], triangle[1]), (triangle[1], triangle[2]), (triangle[2], triangle[0])):
            edges[tuple(sorted((a, b)))].append(t)
    adjacency = [set() for _ in mesh.triangles]
    for owners in edges.values():
        # A fan is enough to discover a component; do not allocate O(n^2) on nonmanifold edges.
        for t in owners[1:]:
            adjacency[owners[0]].add(t); adjacency[t].add(owners[0])
    visited, result = set(), []
    for first in range(len(mesh.triangles)):
        if first in visited:
            continue
        pending, found = [first], []
        visited.add(first)
        while pending:
            t = pending.pop(); found.append(t)
            for other in adjacency[t]:
                if other not in visited:
                    visited.add(other); pending.append(other)
        result.append(sorted(found))
    return result, edges


def propose(mesh, mesh_key, billboard, budget):
    budget.charge_corners(3*len(mesh.triangles))
    groups, edges = components(mesh)
    by_position = defaultdict(set)
    for i, p in enumerate(mesh.positions):
        by_position[p].add(i)
    proposals = []
    for group in groups:
        budget.check()
        vertices = sorted({v for t in group for v in mesh.triangles[t]})
        # Visit only this component's edges. Scanning all edges per disconnected card is quadratic.
        group_edges = {}
        for t in group:
            a, b, c = mesh.triangles[t]
            for edge in (tuple(sorted((a, b))), tuple(sorted((b, c))), tuple(sorted((c, a)))):
                group_edges[edge] = edges[edge]
        boundary = [e for e, owners in group_edges.items() if len(owners) == 1]
        reasons = []
        if any(len(owners) > 2 for owners in group_edges.values()):
            reasons.append("nonmanifold-index-edge")
        degree = defaultdict(int)
        for a, b in boundary:
            degree[a] += 1; degree[b] += 1
        if not boundary or any(n != 2 for n in degree.values()):
            reasons.append("closed-or-branching-boundary")
        boundary_neighbors = defaultdict(set)
        for a, b in boundary:
            boundary_neighbors[a].add(b); boundary_neighbors[b].add(a)
        boundary_seen, loops = set(), 0
        for start in boundary_neighbors:
            if start in boundary_seen:
                continue
            loops += 1; pending = [start]; boundary_seen.add(start)
            while pending:
                for other in boundary_neighbors[pending.pop()]:
                    if other not in boundary_seen:
                        boundary_seen.add(other); pending.append(other)
        if loops != 1:
            reasons.append("boundary-is-not-one-loop")
        for t in group:
            a, b, c = (mesh.positions[i] for i in mesh.triangles[t])
            u, v = [b[i]-a[i] for i in range(3)], [c[i]-a[i] for i in range(3)]
            cross = (u[1]*v[2]-u[2]*v[1], u[2]*v[0]-u[0]*v[2], u[0]*v[1]-u[1]*v[0])
            if not all(math.isfinite(x) for x in cross) or all(x == 0 for x in cross):
                reasons.append("degenerate-or-nonfinite-triangle"); break
        # Equal positions with distinct source indices can be UV/material seams or overlapping cards.
        # Never weld them or turn index connectivity into a claim about botanical stem identity.
        if any(len(by_position[mesh.positions[v]]) > 1 for v in vertices):
            reasons.append("coincident-distinct-source-indices")
        lowest = min(mesh.positions[v][1] for v in vertices)
        lower = sorted(v for v in degree if mesh.positions[v][1] <= lowest + 0.001)
        lower_set = set(lower)
        if len(lower) not in (1, 2) or (len(lower) == 2 and tuple(lower) not in group_edges):
            reasons.append("lowest-boundary-is-not-one-point-or-one-edge")
        candidate = None if reasons else [sum(mesh.positions[v][a] for v in lower)/len(lower) for a in range(3)]
        proposals.append({"key": "component-" + str(group[0]), "triangles": group,
                          "status": "refused-ambiguous" if reasons else "unreviewed-hypothesis",
                          "ambiguities": reasons, "candidateRootLocal": candidate,
                          "candidateRootZoneCornerIds": [3*t+c+1 for t in group for c, v in enumerate(mesh.triangles[t]) if v in lower_set],
                          "rootLocal": None, "hypothesis": "index-edge component; lowest boundary within 1 mm is a display suggestion only"})
    return {"schema": SCHEMA, "meshKey": mesh_key, "status": "proposal-only", "runtimeAdmitted": False,
            "source": mesh.source, "geometrySha256": mesh.geometry_sha256, "attributes": mesh.attribute_receipts,
            "coordinateSpace": "source-node TRS applied; glTF Y-up metres; export/import identity unverified",
            "catalogBillboard": billboard, "cornerIdRule": "1 + 3*sourceTriangleOrdinal + cornerOrdinal; primitive 0; no reordering",
            "positions": mesh.positions, "triangles": mesh.triangles, "sourceWindRawUshort4": mesh.wind,
            "sourceWindSemantics": "unknown; preserved raw, never interpreted as roots or deformation weights",
            "groups": proposals}


def reviewed_sidecar(proposal, proposal_bytes, review):
    require(review.get("proposalSha256") == digest(proposal_bytes) and review.get("geometrySha256") == proposal["geometrySha256"]
            and review.get("meshKey") == proposal["meshKey"], "Review does not bind this exact proposal/source geometry")
    require(all(isinstance(review.get(k), str) and 0 < len(review[k]) <= 1024 for k in ("reviewer", "note")),
            "Explicit reviewer and artistic-root rationale are required")
    triangles, positions = proposal["triangles"], proposal["positions"]
    groups = review.get("groups")
    require(isinstance(groups, list) and 0 < len(groups) <= min(4096, len(triangles)), "Invalid reviewed root-group count")
    ownership, keys, records, corner_data = {}, set(), [], []
    require(all(isinstance(g, dict) and isinstance(g.get("key"), str) for g in groups), "Malformed reviewed root group")
    for group in sorted(groups, key=lambda g: g["key"]):
        key = group.get("key")
        require(isinstance(key, str) and re.fullmatch(r"[a-z0-9][a-z0-9-]{0,63}", key) and key not in keys, "Invalid/duplicate root-group key")
        keys.add(key)
        intent = group.get("intent")
        require(intent in ("card", "stem", "billboard") and (not proposal["catalogBillboard"] or intent == "billboard"),
                "Billboard root zones require explicit billboard intent")
        selected = group.get("triangles")
        require(isinstance(selected, list) and 0 < len(selected) <= len(triangles), "Missing root-group triangles")
        for t in selected:
            integer(t, 0, len(triangles)-1, "root-group triangle")
            require(t not in ownership, "Triangle assigned to multiple root groups")
            ownership[t] = key
        selected = sorted(selected)
        corners = {3*t+c+1 for t in selected for c in range(3)}
        zone, support = group.get("rootZoneCornerIds"), group.get("supportCornerIds")
        require(isinstance(zone, list) and 0 < len(zone) <= 64 and all(type(c) is int and c in corners for c in zone)
                and len(set(zone)) == len(zone), "Root zone must name unique corners in its own group")
        require(isinstance(support, list) and 0 < len(support) <= 16 and all(type(c) is int and c in zone for c in support)
                and len(set(support)) == len(support), "Support samples must be explicit bounded root-zone corners")
        root = vector(group.get("rootLocal")); axis = vector(group.get("growthAxis"))
        require(abs(sum(x*x for x in axis)-1) <= 0.000001, "Growth axis must be explicitly unit length")
        for name in ("maximumRootDistanceMetres", "maximumHeightSpreadMetres", "growthLengthMetres"):
            require(finite(group.get(name)) and 0 < group[name] <= 10, "Invalid root/deformation extent: " + name)
        require(finite(group.get("maximumSlopeDegrees")) and 0 <= group["maximumSlopeDegrees"] < 90, "Missing finite slope limit")
        for name in ("windWeight", "windPhase"):
            require(finite(group.get(name)) and 0 <= group[name] <= 1, "Wind weight and phase must be explicitly authored")
        def position(corner):
            t, c = divmod(corner-1, 3)
            return positions[triangles[t][c]]
        points = [position(c) for c in zone]
        require(all(math.dist(p, root) <= group["maximumRootDistanceMetres"] for p in points), "Reviewed root zone exceeds its declared radius")
        require(max(p[1] for p in points)-min(p[1] for p in points) <= group["maximumHeightSpreadMetres"], "Reviewed root zone exceeds its declared height spread")
        group_index = len(records)
        records.append({k: group[k] for k in ("key", "intent", "rootLocal", "maximumRootDistanceMetres", "maximumHeightSpreadMetres",
                                             "maximumSlopeDegrees", "growthAxis", "growthLengthMetres", "windWeight", "windPhase")}
                       | {"groupId": digest(canonical(["hollow.cover-root-group.v1", proposal["meshKey"], proposal["geometrySha256"], key])),
                          "triangles": selected, "rootZoneCornerIds": sorted(zone), "supportCornerIds": sorted(support)})
        for corner in sorted(corners):
            p = position(corner)
            growth = max(0.0, min(1.0, sum((p[i]-root[i])*axis[i] for i in range(3))/group["growthLengthMetres"]))
            corner_data.append([corner, group_index, growth])
    require(len(ownership) == len(triangles), "Every source triangle needs exactly one explicit reviewed root group")
    corner_data.sort()
    require([c[0] for c in corner_data] == list(range(1, 3*len(triangles)+1)), "Corner IDs are not a total unique partition")
    return {"schema": "hollow.cover-root-sidecar.v1", "runtimeAdmitted": False,
            "status": "reviewed-authoring-input; export/import/contact-unverified", "meshKey": proposal["meshKey"],
            "proposalSha256": digest(proposal_bytes), "geometrySha256": proposal["geometrySha256"],
            "reviewSha256": digest(canonical(review)), "reviewer": review["reviewer"], "reviewNote": review["note"],
            "coordinateSpace": proposal["coordinateSpace"], "source": proposal["source"], "attributeReceipts": proposal["attributes"],
            "cornerIdRule": proposal["cornerIdRule"], "groups": records,
            "cornerDataColumns": ["cornerId", "rootGroupIndex", "reviewedAxialGrowthWeight"], "cornerData": corner_data,
            "sourceWindSemantics": proposal["sourceWindSemantics"], "sourceWindRawUshort4": proposal["sourceWindRawUshort4"]}


def svg(proposal, budget):
    positions, triangles = proposal["positions"], proposal["triangles"]
    panels = [(0, 1, "front X/Y"), (2, 1, "side Z/Y"), (0, 2, "top X/Z")]
    out = ['<svg xmlns="http://www.w3.org/2000/svg" width="1800" height="700" viewBox="0 0 1800 700">',
           '<rect width="1800" height="700" fill="#10151d"/>',
           '<text x="24" y="30" fill="white" font-family="sans-serif">' + html.escape(proposal["meshKey"]) +
           ' — UNREVIEWED geometry/root hypotheses; no texture or root certification</text>']
    for panel, (u, v, label) in enumerate(panels):
        low = [min(p[i] for p in positions) for i in range(3)]
        high = [max(p[i] for p in positions) for i in range(3)]
        scale = min(540/max(high[u]-low[u], 0.001), 580/max(high[v]-low[v], 0.001))
        def xy(p):
            return (panel*600+30+(p[u]-low[u])*scale, 650-(p[v]-low[v])*scale)
        out.append(f'<text x="{panel*600+24}" y="60" fill="white" font-family="sans-serif">{label} (metres)</text>')
        for index, group in enumerate(proposal["groups"]):
            budget.check()
            hue = (index*137) % 360
            out.append(f'<g fill="hsl({hue},45%,55%)" fill-opacity=".15" stroke="hsl({hue},65%,70%)" stroke-width=".5">')
            out.append('<title>' + html.escape(group["key"] + ': ' + group["status"] + '; ' + ', '.join(group["ambiguities"])) + '</title>')
            for t in group["triangles"]:
                points = ' '.join(f'{x:.3f},{y:.3f}' for x, y in (xy(positions[i]) for i in triangles[t]))
                out.append(f'<polygon points="{points}"/>')
            out.append('</g>')
            if group["candidateRootLocal"] is not None:
                x, y = xy(group["candidateRootLocal"])
                out.append(f'<circle cx="{x:.3f}" cy="{y:.3f}" r="4" fill="#ffcf54"><title>{group["key"]}: unreviewed root suggestion</title></circle>')
        out.append(f'<text x="{panel*600+24}" y="684" fill="#ffcf54" font-family="sans-serif">Yellow = lowest-boundary suggestion; hover components for ID/status.</text>')
    out.append('</svg>')
    return '\n'.join(out).encode('utf-8')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--catalog", type=Path, default=Path(__file__).resolve().parents[2]/"game/assets/surface/ground-cover/catalog.json")
    parser.add_argument("--destination", type=Path, required=True)
    parser.add_argument("--review", type=Path)
    parser.add_argument("--seconds", type=float, default=60)
    args = parser.parse_args()
    budget = Budget(args.seconds)
    raw_catalog = bounded_read(args.catalog, 16*1024*1024)
    catalog = strict_json(raw_catalog)
    require([f["key"] for f in catalog["families"]] == list(FAMILIES), "Catalog must identify the four existing licensed families in canonical order")
    reviews = {}
    if args.review:
        review_doc = strict_json(bounded_read(args.review, 16*1024*1024))
        require(review_doc.get("schema") == "hollow.cover-root-review.v1" and isinstance(review_doc.get("meshes"), list), "Unknown review schema")
        for review in review_doc["meshes"]:
            require(review["meshKey"] not in reviews, "Duplicate mesh review")
            reviews[review["meshKey"]] = review
    destination = args.destination.resolve()
    require(not destination.exists(), "Destination must be new; no overwrite/resume mode")
    require(not destination.is_relative_to(Path(__file__).resolve().parents[2]/"game"), "Diagnostics cannot write into game assets")
    for family in catalog["families"]:
        source_paths = [Path(i["path"]).resolve() for i in family["inputs"] if i["path"].endswith("_nonUE.gltf")]
        require(len(source_paths) == 1 and source_paths[0].parent.name == "standard", "Unexpected source layout")
        require(not destination.is_relative_to(source_paths[0].parent.parent), "Diagnostics cannot write into source assets")
    destination.mkdir(parents=True)
    manifest = {"schema": "hollow.cover-root-inspection.v1", "catalogSha256": digest(raw_catalog),
                "runtimeAdmitted": False, "sourceLicenseBasis": "Existing owner-supplied Fab Standard License receipts; no new acquisition or relicensing",
                "meshes": []}
    for family in catalog["families"]:
        budget.check()
        receipts = {Path(i["path"]).resolve(): i["sha256"] for i in family["inputs"]}
        sources = [p for p in receipts if p.name.endswith("_nonUE.gltf")]
        require(len(sources) == 1, "Expected exactly one receipt-bound nonUE source")
        source = sources[0]
        require(source.parent.name == "standard", "Unexpected source layout")
        source_root = source.parent.parent
        require(not destination.is_relative_to(source_root), "Diagnostics cannot write into source assets")
        raw = authenticated(source, receipts[source], 16*1024*1024, budget)
        doc = strict_json(raw)
        require(doc.get("asset", {}).get("version") == "2.0" and not doc.get("extensionsRequired"), "Unsupported glTF version/required extension")
        buffers, buffer_receipts = [], []
        total_bytes = 0
        for buffer in doc["buffers"]:
            uri = buffer.get("uri", "")
            require(isinstance(uri, str) and re.fullmatch(r"[A-Za-z0-9_./-]+", uri) and not Path(uri).is_absolute(), "Only relative receipt-bound buffer files are supported")
            path = (source.parent/uri).resolve()
            require(path.is_relative_to(source_root) and path in receipts, "Buffer escapes source package or lacks receipt")
            data = authenticated(path, receipts[path], MAX_SOURCE_BYTES, budget)
            require(len(data) == buffer["byteLength"], "Buffer byte length differs from glTF")
            total_bytes += len(data)
            require(total_bytes <= MAX_SOURCE_BYTES, "Aggregate source buffer budget exceeded")
            buffers.append(data); buffer_receipts.append({"uri": uri, "sha256": receipts[path]})
        roots = doc["scenes"][doc.get("scene", 0)]["nodes"]
        require(len(set(roots)) == len(roots) and not any(n.get("children") for n in doc["nodes"]), "Source scene hierarchy requires explicit transform support")
        for variant in family["variants"]:
            for level in variant["levels"]:
                require(len(manifest["meshes"]) < MAX_MESHES, "Selected model count exceeds 36")
                name = level["sourceNode"]
                matches = [i for i in roots if doc["nodes"][i].get("name") == name]
                require(len(matches) == 1, "Source node name is missing or ambiguous: " + name)
                key = family["key"] + "/" + level["node"]
                mesh = load_mesh(doc, buffers, matches[0], {"gltfSha256": receipts[source], "buffers": buffer_receipts,
                                                         "archiveSha256": family["archiveSha256"]}, budget)
                proposal = propose(mesh, key, bool(level["billboard"]), budget)
                encoded = canonical(proposal)
                filename = level["node"]
                require(re.fullmatch(r"[a-z0-9_]+", filename) is not None, "Unsafe catalog output name")
                budget.write(destination/(filename + ".proposal.json"), encoded)
                budget.write(destination/(filename + ".svg"), svg(proposal, budget))
                row = {"meshKey": key, "proposal": filename + ".proposal.json", "sha256": digest(encoded),
                       "preview": filename + ".svg", "groups": len(proposal["groups"]),
                       "ambiguousGroups": sum(g["status"] == "refused-ambiguous" for g in proposal["groups"])}
                if key in reviews:
                    sidecar = reviewed_sidecar(proposal, encoded, reviews.pop(key))
                    sidecar_data = canonical(sidecar)
                    budget.write(destination/(filename + ".roots.reviewed.json"), sidecar_data)
                    row["reviewedSidecar"] = filename + ".roots.reviewed.json"
                    row["reviewedSidecarSha256"] = digest(sidecar_data)
                manifest["meshes"].append(row)
                print(f'{key}: {len(mesh.triangles)} triangles, {row["groups"]} topology groups, {row["ambiguousGroups"]} ambiguous; NOT admitted', flush=True)
    require(not reviews, "Review names a model outside the existing selected catalog")
    require(len(manifest["meshes"]) == MAX_MESHES, "Expected all 36 selected family/variant/LOD meshes")
    budget.write(destination/"inspection.json", canonical(manifest))


if __name__ == "__main__":
    main()

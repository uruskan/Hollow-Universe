"""Bounded offline geometry assessment; does not export or admit game assets.

Exact dyadic predicates classify all BVH candidate triangle pairs. Positive-area
coplanar overlap is rejected even when triangles share a vertex/edge. Work or
arithmetic ceilings yield INCOMPLETE, never a clean result. Existing source open
boundaries are permitted only when preserved exactly. Sampled distances use
float64 queries and are descriptive, NOT a Hausdorff bound or acceptance test.
Component checks use directed boundary ownership and original face provenance
where available, plus V-E+F; those invariants do not prove homeomorphism. The
CLI requires testedAllLodInvariantsClear, which includes unchanged source/near
intersections as well as physical/far checks. assetAccepted remains false.

Root schedules execution, with an external memory/time guard:
  python tools/blender/cliff_mesh_quality.py --package <fresh prototype> \
    --receipt-sha256 <independently pinned digest> --report <new report.json>
No Blender, engine, image decoding, packages or external processes are used.
"""
from __future__ import annotations

import argparse
from collections import Counter, defaultdict
from dataclasses import dataclass
from fractions import Fraction
import heapq
import importlib.util
import itertools
import math
from pathlib import Path
import sys
import time

_spec = importlib.util.spec_from_file_location("_cliff_quality_author", Path(__file__).with_name("cliff_qualified_mesh.py"))
author = importlib.util.module_from_spec(_spec)
sys.modules[_spec.name] = author
_spec.loader.exec_module(author)


class Incomplete(RuntimeError):
    pass


@dataclass(frozen=True)
class Limits:
    maximum_vertices: int = 60000
    maximum_triangles: int = 50000
    maximum_dyadic_exponent: int = 256
    maximum_coordinate_bits: int = 384
    maximum_fraction_bits: int = 8192
    maximum_pair_tests: int = 2000000
    maximum_exact_operations: int = 100000
    maximum_node_visits: int = 4000000
    maximum_distance_tests: int = 2000000
    maximum_samples: int = 4096
    maximum_witnesses: int = 100
    maximum_topology_steps: int = 2000000
    maximum_admitted_files: int = 128
    maximum_admitted_bytes: int = 256*1024*1024
    maximum_file_bytes: int = 64*1024*1024
    seconds: float = 55.0


class Work:
    def __init__(self, limits=None):
        self.limits = limits or Limits()
        author.require(all(type(v) is int and v >= 0 for k, v in vars(self.limits).items() if k != "seconds")
                       and math.isfinite(self.limits.seconds) and 0 < self.limits.seconds <= 600,
                       "Invalid quality work limits")
        self.deadline = time.monotonic() + self.limits.seconds
        self.counts = Counter()

    def take(self, kind, amount=1):
        self.counts[kind] += amount
        ceiling = getattr(self.limits, "maximum_" + kind)
        if self.counts[kind] > ceiling:
            raise Incomplete(f"{kind} budget exceeded ({ceiling})")
        self.check()

    def check(self):
        if time.monotonic() >= self.deadline:
            raise Incomplete("Quality assessment deadline exceeded")

    def fractions(self, points):
        for point in points:
            for value in point:
                value = Fraction(value)
                if max(value.numerator.bit_length(), value.denominator.bit_length()) > self.limits.maximum_fraction_bits:
                    raise Incomplete("Exact rational bit budget exceeded")


def sub(a, b):
    return tuple(x-y for x, y in zip(a, b))


def dot(a, b):
    return sum(x*y for x, y in zip(a, b))


def cross(a, b):
    return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])


def normal(triangle):
    return cross(sub(triangle[1], triangle[0]), sub(triangle[2], triangle[0]))


def exact_coordinates(positions, work):
    ratios = []
    exponent = 0
    for point in positions:
        author.require(len(point) == 3 and all(type(v) in (int, float) and math.isfinite(v) and abs(v) <= 1e6 for v in point),
                       "Quality coordinates must be finite local metres within ±1e6")
        row = [float(v).as_integer_ratio() for v in point]
        ratios.append(row)
        exponent = max(exponent, *(d.bit_length()-1 for _, d in row))
    if exponent > work.limits.maximum_dyadic_exponent:
        raise Incomplete("Dyadic coordinate exponent budget exceeded")
    scale = 1 << exponent
    result = [tuple(n*(scale//d) for n, d in row) for row in ratios]
    if any(abs(v).bit_length() > work.limits.maximum_coordinate_bits for p in result for v in p):
        raise Incomplete("Exact coordinate integer bit budget exceeded")
    return result, scale


def projected_axes(n):
    drop = max(range(3), key=lambda i: (abs(n[i]), -i))
    return tuple(i for i in range(3) if i != drop)


def orient(a, b, p, axes):
    x, y = axes
    return (b[x]-a[x])*(p[y]-a[y]) - (b[y]-a[y])*(p[x]-a[x])


def interpolate(a, b, fraction):
    return tuple(Fraction(x) + fraction*(y-x) for x, y in zip(a, b))


def unique(points):
    return list(dict.fromkeys(tuple(p) for p in points))


def slice_plane(triangle, distances, work):
    work.take("exact_operations")
    points = [tuple(Fraction(v) for v in p) for p, d in zip(triangle, distances) if d == 0]
    for i in range(3):
        j = (i+1) % 3
        if distances[i] * distances[j] < 0:
            points.append(interpolate(triangle[i], triangle[j], Fraction(distances[i], distances[i]-distances[j])))
    points = unique(points)
    work.fractions(points)
    return points


def coplanar_clip(a, b, n, work):
    axes = projected_axes(n)
    sign = 1 if orient(b[0], b[1], b[2], axes) > 0 else -1
    polygon = [tuple(Fraction(v) for v in p) for p in a]
    for i in range(3):
        if not polygon:
            break
        output = []
        previous = polygon[-1]
        previous_d = sign*orient(b[i], b[(i+1) % 3], previous, axes)
        for point in polygon:
            work.take("exact_operations")
            distance = sign*orient(b[i], b[(i+1) % 3], point, axes)
            if (previous_d >= 0) != (distance >= 0):
                output.append(interpolate(previous, point, previous_d/(previous_d-distance)))
            if distance >= 0:
                output.append(point)
            previous, previous_d = point, distance
        polygon = unique(output)
        work.fractions(polygon)
    area = sum(polygon[i][axes[0]]*polygon[(i+1) % len(polygon)][axes[1]] -
               polygon[(i+1) % len(polygon)][axes[0]]*polygon[i][axes[1]] for i in range(len(polygon)))
    return polygon, area


def inside_strict(point, triangle, n):
    axes = projected_axes(n)
    values = [orient(triangle[i], triangle[(i+1) % 3], point, axes) for i in range(3)]
    return all(v > 0 for v in values) or all(v < 0 for v in values)


def on_segment(point, a, b):
    direction, relative = sub(b, a), sub(point, a)
    return cross(direction, relative) == (0, 0, 0) and 0 <= dot(relative, direction) <= dot(direction, direction)


@dataclass(frozen=True)
class Intersection:
    kind: str
    points: tuple = ()
    interior_on_a: bool = False
    interior_on_b: bool = False


ALLOWED = frozenset(("disjoint", "shared-vertex", "shared-edge"))


def classify_exact(a, b, work):
    """Coordinates must share one integer scale. No tolerance or edge exemption."""
    work.take("pair_tests")
    na, nb = normal(a), normal(b)
    author.require(na != (0, 0, 0) and nb != (0, 0, 0), "Degenerate triangle is not qualifiable")
    da = [dot(nb, sub(p, b[0])) for p in a]
    db = [dot(na, sub(p, a[0])) for p in b]
    if any(all(d > 0 for d in ds) or all(d < 0 for d in ds) for ds in (da, db)):
        return Intersection("disjoint")
    line = cross(na, nb)
    common = set(a) & set(b)
    # These are exact proofs of simplex-only contact, not blanket exemptions.
    # Two nonparallel planes sharing an edge intersect only along that edge.
    if line != (0, 0, 0) and len(common) == 2:
        return Intersection("shared-edge", tuple(sorted(common)))
    if line != (0, 0, 0) and len(common) == 1:
        for triangle, distances in ((a, da), (b, db)):
            other = [d for p, d in zip(triangle, distances) if p not in common]
            if all(d > 0 for d in other) or all(d < 0 for d in other):
                return Intersection("shared-vertex", tuple(common))
    if line == (0, 0, 0):
        if any(da):
            return Intersection("disjoint")
        # A separating supporting edge proves there is no coplanar area
        # overlap. Only then can its exact shared zero-set be admitted.
        for first, second in ((a, b), (b, a)):
            axes = projected_axes(na)
            sign = 1 if orient(first[0], first[1], first[2], axes) > 0 else -1
            for i in range(3):
                distances = [sign*orient(first[i], first[(i+1) % 3], p, axes) for p in second]
                if max(distances) < 0:
                    return Intersection("disjoint")
                if max(distances) == 0:
                    zeros = {p for p, d in zip(second, distances) if d == 0}
                    if zeros <= common:
                        return Intersection("shared-vertex" if len(zeros) == 1 else "shared-edge", tuple(sorted(zeros)))
        points, area = coplanar_clip(a, b, na, work)
        if area != 0:
            return Intersection("duplicate-triangle" if len(common) == 3 else "coplanar-overlap", tuple(points), True, True)
        if not points:
            return Intersection("disjoint")
        axis = max(range(3), key=lambda i: (max(p[i] for p in points)-min(p[i] for p in points), -i))
        points = unique((min(points, key=lambda p: p[axis]), max(points, key=lambda p: p[axis])))
    else:
        sa, sb = slice_plane(a, da, work), slice_plane(b, db, work)
        if not sa or not sb:
            return Intersection("disjoint")
        axis = max(range(3), key=lambda i: (abs(line[i]), -i))
        sa.sort(key=lambda p: p[axis]); sb.sort(key=lambda p: p[axis])
        low, high = max(sa[0][axis], sb[0][axis]), min(sa[-1][axis], sb[-1][axis])
        if low > high:
            return Intersection("disjoint")
        def at(value):
            if len(sa) == 1:
                return sa[0]
            return interpolate(sa[0], sa[-1], (value-sa[0][axis])/(sa[-1][axis]-sa[0][axis]))
        points = unique((at(low), at(high)))
        work.fractions(points)
    if len(points) == 1 and points[0] in common:
        return Intersection("shared-vertex", tuple(points))
    if len(common) == 2:
        c, d = sorted(common)
        if all(on_segment(p, c, d) for p in points):
            return Intersection("shared-edge", tuple(points))
    middle = tuple(sum(p[i] for p in points)/len(points) for i in range(3))
    ia, ib = inside_strict(middle, a, na), inside_strict(middle, b, nb)
    return Intersection("interior-crossing" if ia and ib else "nonshared-contact", tuple(points), ia, ib)


def classify_triangles(a, b, limits=None):
    work = Work(limits)
    positions, _ = exact_coordinates(list(a)+list(b), work)
    return classify_exact(positions[:3], positions[3:], work)


@dataclass
class Node:
    low: tuple
    high: tuple
    count: int
    faces: tuple = ()
    children: tuple = ()


class Mesh:
    def __init__(self, positions, triangles, work):
        author.require(0 < len(positions) <= work.limits.maximum_vertices and
                       0 < len(triangles) <= work.limits.maximum_triangles, "Quality geometry count ceiling exceeded")
        author.require(all(isinstance(p, (list, tuple)) and len(p) == 3 and
                           all(type(v) in (int, float) and math.isfinite(v) and abs(v) <= 1e6 for v in p)
                           for p in positions), "Quality vertices require three finite numeric coordinates")
        self.positions = [tuple(float(v) for v in p) for p in positions]
        self.triangles = [tuple(t) for t in triangles]
        self.source_triangle_ids = None
        self.source_face_provenance_authenticated = False
        author.require(all(len(t) == 3 and all(type(v) is int and 0 <= v < len(positions) for v in t) for t in triangles),
                       "Invalid quality triangle index")
        self.exact, self.scale = exact_coordinates(self.positions, work)
        author.require(all(normal(tuple(self.exact[v] for v in t)) != (0, 0, 0) for t in self.triangles),
                       "Degenerate quality triangle")
        self.boxes = [(tuple(min(self.positions[v][i] for v in t) for i in range(3)),
                       tuple(max(self.positions[v][i] for v in t) for i in range(3))) for t in self.triangles]
        self.nodes = []
        self._build(list(range(len(self.triangles))), work)

    def _build(self, faces, work):
        work.take("node_visits")
        low = tuple(min(self.boxes[f][0][i] for f in faces) for i in range(3))
        high = tuple(max(self.boxes[f][1][i] for f in faces) for i in range(3))
        index = len(self.nodes)
        self.nodes.append(Node(low, high, len(faces)))
        if len(faces) <= 8:
            self.nodes[index].faces = tuple(sorted(faces))
        else:
            axis = max(range(3), key=lambda i: (high[i]-low[i], -i))
            faces.sort(key=lambda f: (self.boxes[f][0][axis]+self.boxes[f][1][axis], f))
            middle = len(faces)//2
            self.nodes[index].children = (self._build(faces[:middle], work), self._build(faces[middle:], work))
        return index

    def points(self, face, exact=False):
        positions = self.exact if exact else self.positions
        return tuple(positions[v] for v in self.triangles[face])


def overlaps(a, b):
    return all(a.low[i] <= b.high[i] and b.low[i] <= a.high[i] for i in range(3))


def candidate_pairs(mesh, work):
    pending = [(0, 0)]
    while pending:
        a, b = pending.pop()
        work.take("node_visits")
        na, nb = mesh.nodes[a], mesh.nodes[b]
        if not overlaps(na, nb):
            continue
        if not na.children and not nb.children:
            pairs = itertools.combinations(na.faces, 2) if a == b else itertools.product(na.faces, nb.faces)
            for f, g in pairs:
                if all(mesh.boxes[f][0][i] <= mesh.boxes[g][1][i] and mesh.boxes[g][0][i] <= mesh.boxes[f][1][i] for i in range(3)):
                    yield min(f, g), max(f, g)
        elif a == b:
            left, right = na.children
            pending.extend(((right, right), (left, right), (left, left)))
        elif na.children and (not nb.children or na.count >= nb.count):
            pending.extend((child, b) for child in reversed(na.children))
        else:
            pending.extend((a, child) for child in reversed(nb.children))


def intersections(mesh, work, classifier=None):
    """Enumerate every candidate pair; an optional exact classifier receives mesh/face ordinals/work.

The caller may reuse a previously proved classification for identical immutable
triangle geometry. The default remains the uncached exact dyadic classifier.
"""
    counts, witnesses = Counter(), []
    for a, b in candidate_pairs(mesh, work):
        hit = (classify_exact(mesh.points(a, True), mesh.points(b, True), work) if classifier is None
               else classifier(mesh, a, b, work))
        counts[hit.kind] += 1
        if hit.kind not in ALLOWED and len(witnesses) < work.limits.maximum_witnesses:
            witnesses.append(dict(triangles=[a, b], classification=hit.kind,
                interiorOnFirst=hit.interior_on_a, interiorOnSecond=hit.interior_on_b,
                positionsMetres=[[float(v/mesh.scale) for v in p] for p in hit.points],
                exactScaledRationalPoints=[[str(v) for v in p] for p in hit.points]))
    rejected = sum(v for k, v in counts.items() if k not in ALLOWED)
    return dict(complete=True, clear=rejected == 0, classifications=dict(sorted(counts.items())),
                rejectedPairs=rejected, witnesses=witnesses, omittedWitnesses=max(0, rejected-len(witnesses)),
                integerCoordinateScale=str(mesh.scale),
                scope="All self triangle pairs, with inclusive exact-coordinate AABB rejection; shared simplex contact is allowed")


def topology(mesh, work=None):
    edges, indexed_edges = defaultdict(list), defaultdict(list)
    used = set()
    parents = list(range(len(mesh.triangles)))
    def root(f):
        while parents[f] != f:
            parents[f] = parents[parents[f]]
            f = parents[f]
        return f
    for f, t in enumerate(mesh.triangles):
        if work: work.take("topology_steps")
        used.update(t)
        for i in range(3):
            a, b = t[i], t[(i+1) % 3]
            pa, pb = mesh.positions[a], mesh.positions[b]
            edges[tuple(sorted((pa, pb)))].append((pa, pb, f))
            indexed_edges[tuple(sorted((a, b)))].append(f)
    for rows in edges.values():
        if work: work.take("topology_steps")
        for row in rows[1:]:
            a, b = root(rows[0][2]), root(row[2])
            parents[max(a, b)] = min(a, b)
    face_components = [root(f) for f in range(len(mesh.triangles))]
    components = {}
    for f, key in enumerate(face_components):
        component = components.setdefault(key, dict(faces=[], positions=set(), boundaries=set(), edges=0))
        component["faces"].append(f)
        component["positions"].update(mesh.points(f))
    boundary_owners = {}
    for rows in edges.values():
        key = face_components[rows[0][2]]
        components[key]["edges"] += 1
        if len(rows) == 1:
            boundary = rows[0][:2]
            components[key]["boundaries"].add(boundary)
            boundary_owners[boundary] = key
    descriptions = []
    for key, row in sorted(components.items()):
        row["euler"] = len(row["positions"])-row["edges"]+len(row["faces"])
        descriptions.append(dict(id=key, faces=len(row["faces"]), geometricVertices=len(row["positions"]),
            geometricEdges=row["edges"], eulerCharacteristic=row["euler"],
            boundaryEdges=len(row["boundaries"]), closed=not row["boundaries"]))
    boundaries = {rows[0][:2] for rows in edges.values() if len(rows) == 1}
    nonmanifold = [rows for rows in edges.values() if len(rows) > 2]
    wrong_winding = [rows for rows in edges.values() if len(rows) == 2 and rows[0][:2] != tuple(reversed(rows[1][:2]))]
    indexed_open = sum(len(rows) == 1 for rows in indexed_edges.values())
    return dict(boundaries=boundaries, geometricOpenEdges=len(boundaries), indexedOpenEdges=indexed_open,
                indexOnlyOpenEdges=indexed_open-len(boundaries), nonmanifoldEdges=len(nonmanifold),
                inconsistentWindingEdges=len(wrong_winding), verticesWithoutEdgeFaces=sorted(set(range(len(mesh.positions)))-used),
                edgeConnectedComponents=len(components), components=descriptions,
                componentEulerSum=sum(row["euler"] for row in components.values()),
                _components=components, _faceComponents=face_components, _boundaryOwners=boundary_owners)


def bind_source_ids(mesh, ids, source_count, authenticated=False):
    author.require(isinstance(ids, list) and len(ids) == len(mesh.triangles) and
                   all(type(f) is int and 0 <= f < source_count for f in ids) and len(set(ids)) == len(ids),
                   "Provenance requires one unique valid original face ID per triangle")
    mesh.source_triangle_ids = tuple(ids)
    mesh.source_face_provenance_authenticated = authenticated


def component_correspondence(source, candidate, before, after, work=None):
    ids = candidate.source_triangle_ids
    mode = ("authenticated" if candidate.source_face_provenance_authenticated else "provided") + "-source-face-labels-and-source-endpoints"
    if ids is None and same_triangle_positions(source, candidate):
        ids = tuple(range(len(source.triangles)))
        mode = "exact-ordered-source-triangle-positions"
    elif ids is None:
        mode = "exact-directed-boundary-ownership; closed components unresolved"
    mapping, records = [], []
    for key, row in sorted(after["_components"].items()):
        if work: work.take("topology_steps")
        issues = []
        if ids is not None:
            owners = {before["_faceComponents"][ids[f]] for f in row["faces"]}
        else:
            owners = {before["_boundaryOwners"][edge] for edge in row["boundaries"] if edge in before["_boundaryOwners"]}
            if not row["boundaries"]:
                issues.append("Closed component has no source-face correspondence")
            if any(edge not in before["_boundaryOwners"] for edge in row["boundaries"]):
                issues.append("Component contains a boundary absent from the source")
        owner = next(iter(owners)) if len(owners) == 1 else None
        if owner is None:
            issues.append("Component does not map to exactly one source component")
        else:
            original = before["_components"][owner]
            mapping.append(owner)
            if row["euler"] != original["euler"]:
                issues.append("Mapped component Euler characteristic changed")
            if row["boundaries"] != original["boundaries"]:
                issues.append("Mapped component directed boundaries changed ownership or membership")
            if ids is not None and not row["positions"] <= original["positions"]:
                issues.append("Candidate uses positions outside the labeled source component endpoints")
        records.append(dict(candidateComponent=key, sourceComponent=owner, clear=not issues, issues=issues))
    mapped = Counter(mapping)
    missing = sorted(set(before["_components"])-set(mapped))
    repeated = sorted(key for key, count in mapped.items() if count != 1)
    equal_count = len(before["_components"]) == len(after["_components"])
    return dict(clear=equal_count and not missing and not repeated and all(row["clear"] for row in records),
                mode=mode, equalComponentCounts=equal_count, missingSourceComponents=missing,
                multiplyMappedSourceComponents=repeated, mapping=records,
                scope="Edge-connected geometric components; one-to-one source correspondence, directed boundary ownership, and per-component V-E+F",
                caveat="Counts and Euler values alone do not prove correspondence or homeomorphism; even authenticated source-face labels are provenance, not replay of the collapse sequence")


def continuity(source, candidate, work=None, source_topology=None):
    before, after = source_topology or topology(source, work), topology(candidate, work)
    added = sorted(after["boundaries"]-before["boundaries"])
    missing = sorted(before["boundaries"]-after["boundaries"])
    components = component_correspondence(source, candidate, before, after, work)
    def summary(data):
        return {k: v for k, v in data.items() if k != "boundaries" and not k.startswith("_")}
    return dict(clear=not added and not missing and not after["nonmanifoldEdges"] and
                     not after["inconsistentWindingEdges"] and components["clear"],
                source=summary(before), candidate=summary(after), componentCorrespondence=components,
                newBoundaryEdges=added[:100], missingSourceBoundaryEdges=missing[:100],
                newBoundaryCount=len(added), missingSourceBoundaryCount=len(missing),
                criterion="Exact source boundary directions/ownership, manifold edges, consistent winding, component correspondence and Euler values preserved; coincident fan indices welded only for this assessment")


def closest_triangle(point, a, b, c):
    """Float64 closest point; measurements only, never an acceptance predicate."""
    ab, ac, ap = sub(b, a), sub(c, a), sub(point, a)
    d1, d2 = dot(ab, ap), dot(ac, ap)
    if d1 <= 0 and d2 <= 0: return a
    bp = sub(point, b); d3, d4 = dot(ab, bp), dot(ac, bp)
    if d3 >= 0 and d4 <= d3: return b
    vc = d1*d4-d3*d2
    if vc <= 0 and d1 >= 0 and d3 <= 0: return tuple(x+(d1/(d1-d3))*y for x, y in zip(a, ab))
    cp = sub(point, c); d5, d6 = dot(ab, cp), dot(ac, cp)
    if d6 >= 0 and d5 <= d6: return c
    vb = d5*d2-d1*d6
    if vb <= 0 and d2 >= 0 and d6 <= 0: return tuple(x+(d2/(d2-d6))*y for x, y in zip(a, ac))
    va = d3*d6-d5*d4
    if va <= 0 and d4-d3 >= 0 and d5-d6 >= 0:
        return tuple(x+((d4-d3)/((d4-d3)+(d5-d6)))*y for x, y in zip(b, sub(c, b)))
    denominator = va+vb+vc
    if denominator <= 0 or not math.isfinite(denominator):
        raise Incomplete("Float64 distance query is numerically indeterminate")
    v, w = vb/denominator, vc/denominator
    return tuple(x+v*y+w*z for x, y, z in zip(a, ab, ac))


def box_distance(point, node):
    return sum(max(node.low[i]-point[i], 0, point[i]-node.high[i])**2 for i in range(3))


def nearest(point, target, work):
    queue = [(box_distance(point, target.nodes[0]), 0)]
    best, face, closest = math.inf, -1, None
    while queue:
        bound, index = heapq.heappop(queue)
        work.take("node_visits")
        # Numeric slack is ONLY for this descriptive float64 sampling query.
        if bound > best + max(1e-20, best*1e-12):
            continue
        node = target.nodes[index]
        if node.children:
            for child in node.children:
                heapq.heappush(queue, (box_distance(point, target.nodes[child]), child))
        else:
            for candidate in node.faces:
                work.take("distance_tests")
                q = closest_triangle(point, *target.points(candidate))
                distance = dot(sub(point, q), sub(point, q))
                if not math.isfinite(distance):
                    raise Incomplete("Nonfinite sampled distance")
                if (distance, candidate) < (best, face):
                    best, face, closest = distance, candidate, q
    return math.sqrt(best), face, closest


def sampled_distances(source, target, work, faces=512):
    author.require(type(faces) is int and faces > 0, "Sample face count must be positive")
    count = min(faces, len(source.triangles))
    selected = [0] if count == 1 else [i*(len(source.triangles)-1)//(count-1) for i in range(count)]
    values, worst = [], []
    for f in selected:
        triangle = source.points(f)
        points = list(triangle)+[tuple(sum(p[i] for p in triangle)/3 for i in range(3))]
        for slot, p in enumerate(points):
            work.take("samples")
            distance, target_face, q = nearest(p, target, work)
            values.append(distance)
            worst.append(dict(sourceTriangle=f, sampleSlot=slot, position=p, closestPosition=q,
                              targetTriangle=target_face, distanceMetres=distance))
    values.sort()
    worst.sort(key=lambda row: (-row["distanceMetres"], row["sourceTriangle"], row["sampleSlot"]))
    return dict(complete=True, sampleCount=len(values), maximumSampledDistanceMetres=values[-1],
                medianSampledDistanceMetres=values[(len(values)-1)//2], p95SampledDistanceMetres=values[math.ceil(.95*len(values))-1],
                witnesses=worst[:16], method="Deterministic face-ordinal selection; corners and centroid; float64 closest-triangle BVH",
                acceptanceEvidence=False, hausdorffBound=False,
                caveat="Unsampled locations may differ more; numeric nearest-point estimates are descriptive, not certified error bounds")


def same_triangle_positions(a, b):
    return len(a.triangles) == len(b.triangles) and all(a.points(i) == b.points(i) for i in range(len(a.triangles)))


def assess(source, physical, far, work):
    report = dict(complete=False, assetAccepted=False, reason="Assessment in progress", workLimits=vars(work.limits),
                  distanceMeasurementsAreAcceptanceEvidence=False, allLodIntersectionsClear=False,
                  testedCandidateInvariantsClear=False, testedAllLodInvariantsClear=False,
                  testedCandidateInvariantsScope="Physical/far exact self-intersections and source-relative boundary/component/Euler invariants",
                  testedAllLodInvariantsScope="Candidate invariants plus source/near exact self-intersections; no approximation, shading or habitat approval")
    try:
        source_topology = topology(source, work)
        report["continuity"] = continuity(source, physical, work, source_topology)
        report["farContinuity"] = continuity(source, far, work, source_topology)
        report["physicalFarExactTriangleCorrespondence"] = same_triangle_positions(physical, far)
        report["physicalIntersections"] = intersections(physical, work)
        if report["physicalFarExactTriangleCorrespondence"]:
            report["farIntersections"] = dict(reuses="physicalIntersections", reason="Every ordered triangle corner position is exactly equal",
                                              complete=True, clear=report["physicalIntersections"]["clear"])
        else:
            report["farIntersections"] = intersections(far, work)
        report["sourceIntersections"] = intersections(source, work)
        report["allLodIntersectionsClear"] = all(report[key]["clear"] for key in
            ("physicalIntersections", "farIntersections", "sourceIntersections"))
        face_samples = min(512, work.limits.maximum_samples//8)
        if face_samples < 1:
            raise Incomplete("Insufficient bidirectional sampling budget")
        report["nearToFarSamples"] = sampled_distances(source, far, work, face_samples)
        report["farToNearSamples"] = sampled_distances(far, source, work, face_samples)
        flags = [report["continuity"]["clear"], report["farContinuity"]["clear"], report["physicalIntersections"]["clear"],
                 report["farIntersections"]["clear"]]
        work.check()
        report.update(complete=True, testedCandidateInvariantsClear=all(flags),
                      testedAllLodInvariantsClear=all(flags) and report["allLodIntersectionsClear"],
                      reason="Candidate invariants checked; sampled distances do not establish approximation quality or habitat approval")
    except (Incomplete, ArithmeticError) as error:
        report.update(complete=False, testedCandidateInvariantsClear=False, testedAllLodInvariantsClear=False, reason=str(error))
    report["workPerformed"] = dict(sorted(work.counts.items()))
    return report


def read_physical(document, work):
    """Strict geometry record shape; full F3 domain/capability validation remains separate."""
    author.require(type(document) is dict and set(document) == {"vertices", "triangles", "patches"},
                   "Physical geometry requires vertices, triangles and patches only")
    vertices, triangles, patches = (document[key] for key in ("vertices", "triangles", "patches"))
    author.require(type(vertices) is list and 0 < len(vertices) <= min(8192, work.limits.maximum_vertices) and
                   all(type(row) is list and len(row) == 3 and
                       all(type(v) in (int, float) and math.isfinite(v) and abs(v) <= 1e6 for v in row)
                       for row in vertices), "Physical vertices require three finite numeric coordinates")
    author.require(type(patches) is list and 0 < len(patches) <= 256 and
                   all(type(row) is dict and set(row) == {"key", "substrateProfileKey", "capabilities"} and
                       all(type(row[key]) is str and 0 < len(row[key]) <= 128 for key in ("key", "substrateProfileKey")) and
                       type(row["capabilities"]) is list and len(row["capabilities"]) <= 32 and
                       all(type(v) is str and 0 < len(v) <= 128 for v in row["capabilities"])
                       for row in patches), "Invalid physical patch shape")
    author.require(len({row["key"] for row in patches}) == len(patches), "Duplicate physical patch key")
    author.require(type(triangles) is list and 0 < len(triangles) <= min(16384, work.limits.maximum_triangles) and
                   all(type(row) is list and len(row) == 4 and all(type(v) is int for v in row) and
                       all(0 <= v < len(vertices) for v in row[:3]) and 0 <= row[3] < len(patches)
                       for row in triangles), "Physical triangle requires three integer corners and one valid integer patch")
    author.require({row[3] for row in triangles} == set(range(len(patches))), "Physical geometry contains an unused patch")
    return Mesh(vertices, [row[:3] for row in triangles], work)


def typed_json_equal(a, b):
    """Python bool/int equality must not authenticate a different embedded record."""
    if type(a) is not type(b):
        return False
    if isinstance(a, dict):
        return a.keys() == b.keys() and all(typed_json_equal(value, b[key]) for key, value in a.items())
    if isinstance(a, list):
        return len(a) == len(b) and all(typed_json_equal(x, y) for x, y in zip(a, b))
    return a == b


def read_package(project, package, expected_receipt, work):
    project, package = Path(project).resolve(), Path(package).resolve()
    author.require(package.is_relative_to(project), "Quality package must remain within project root")
    admitted = {}
    def admit(path):
        path = Path(path).resolve()
        author.require(path.is_relative_to(project), "Quality input escapes project root")
        size = path.stat().st_size
        author.require(0 < size <= work.limits.maximum_file_bytes, "Quality input file byte ceiling exceeded")
        if path not in admitted:
            work.take("admitted_files")
            work.take("admitted_bytes", size)
            admitted[path] = size
        author.require(admitted[path] == size, "Quality input changed size during admission")
        work.check()
        return path
    receipt_path = admit(package/"qualification-receipt.json")
    receipt = author.strict_json(receipt_path, 1024*1024, expected_receipt)
    author.require(receipt["schemaVersion"] == 1 and len(receipt["outputFiles"]) <= 64, "Unsupported quality receipt")
    pins = {}
    for row in receipt["outputFiles"]:
        path = admit(author.contained(package, row["file"]))
        author.require(row["file"] not in pins and type(row["bytes"]) is int and path.stat().st_size == row["bytes"],
                       "Invalid quality output admission")
        author.require(author.sha256(path) == row["sha256"], "Quality output bytes/digest mismatch")
        work.check()
        pins[row["file"]] = row
    manifest_pin = receipt["manifest"]
    manifest = author.strict_json(admit(author.contained(project, manifest_pin["file"])), 4*1024*1024, manifest_pin["sha256"])
    matches = [asset for asset in manifest["assets"] for output in asset["outputs"] if output["file"] == receipt["source"]["file"]]
    author.require(len(matches) == 1, "Quality source must match exactly one manifest entry")
    author.require(len(receipt["source"]["dependencies"]) <= 64, "Quality source dependency count ceiling exceeded")
    admit(author.contained(project, receipt["source"]["file"]))
    for dependency in receipt["source"]["dependencies"]:
        admit(author.contained(project, dependency["file"]))
    source, _, _ = author.decode_source(project, dict(asset=matches[0]["key"], near=receipt["source"]), manifest)
    work.check()
    def document(name):
        author.require(name in pins, "Unlisted quality JSON")
        return author.strict_json(author.contained(package, name), 16*1024*1024, pins[name]["sha256"])
    sidecar, physical = document("sidecar.json"), document("physical.json")
    author.require(sidecar["schemaVersion"] == 1 and len(sidecar["definitions"]) == 1, "Expected one physical definition")
    definition = sidecar["definitions"][0]
    author.require(typed_json_equal(definition["physical"], physical) and definition["physicalSha256"] == pins["physical.json"]["sha256"]
                   and definition["sourceSha256"] == receipt["source"]["sha256"], "Quality physical/source binding mismatch")
    lods = {lod["key"]: lod for lod in definition["lods"]}
    author.require(set(lods) == {"near", "far"} and len(definition["lods"]) == 2, "Quality prototype requires exactly near and far LODs")
    def visual(lod):
        name = lod["assetPath"]
        author.require(author.uri_file(package, name).name == name, "Quality prototype LOD must use a flat filename")
        gltf = document(name)
        author.require(lod["contentSha256"] == pins[name]["sha256"], "Quality LOD hash mismatch")
        dependencies = []
        for field in ("buffers", "images"):
            for row in gltf.get(field, []):
                path = author.uri_file(package, row["uri"])
                relative = path.relative_to(package).as_posix()
                author.require(relative in pins, "Unlisted quality LOD dependency")
                dependencies.append(dict(file=relative, sha256=pins[relative]["sha256"]))
        primitive = gltf["meshes"][0]["primitives"][0]
        triangle_count = gltf["accessors"][primitive["indices"]]["count"]//3
        near = dict(file=name, sha256=pins[name]["sha256"], dependencies=dependencies, triangles=triangle_count)
        result, _, _ = author.decode_source(package, dict(asset="quality-output", near=near),
                                           dict(assets=[dict(key="quality-output", outputs=[near])]))
        work.check()
        return result
    near, far = visual(lods["near"]), visual(lods["far"])
    author.require(len(near.corners) == len(source.corners) and all(
        [near.attributes[v] for v in a] == [source.attributes[v] for v in b] for a, b in zip(near.corners, source.corners)),
        "Prototype near LOD differs from authenticated original near source")
    source_mesh = Mesh(source.positions, source.triangles, work)
    physical_mesh = read_physical(physical, work)
    provenance = document("triangle-provenance.json")
    bind_source_ids(physical_mesh, provenance["sourceTriangleIds"], len(source.triangles), authenticated=True)
    far_mesh = Mesh(far.positions, far.triangles, work)
    if same_triangle_positions(physical_mesh, far_mesh):
        bind_source_ids(far_mesh, list(physical_mesh.source_triangle_ids), len(source.triangles), authenticated=True)
    return source_mesh, physical_mesh, far_mesh


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--package", type=Path, required=True)
    parser.add_argument("--receipt-sha256", required=True)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--seconds", type=float, default=55)
    args = parser.parse_args()
    report_path = args.report.resolve()
    author.require(report_path.is_relative_to(args.root.resolve()) and report_path.parent.is_dir() and not report_path.exists(),
                   "Quality report must be a fresh path within project root")
    work = Work(Limits(seconds=args.seconds))
    try:
        meshes = read_package(args.root, args.package, args.receipt_sha256, work)
        report = assess(*meshes, work)
    except (ValueError, Incomplete, ArithmeticError, OSError, KeyError, TypeError, IndexError, AttributeError, RecursionError) as error:
        report = dict(complete=False, assetAccepted=False, testedCandidateInvariantsClear=False, reason=str(error),
                      testedAllLodInvariantsClear=False, allLodIntersectionsClear=False,
                      workPerformed=dict(sorted(work.counts.items())))
    report.update(schemaVersion=1, receiptSha256=args.receipt_sha256, toolSha256=author.sha256(Path(__file__)),
                  authoringReaderSha256=author.sha256(Path(author.__file__)))
    author.write_new(report_path, author.canonical(report))
    print(author.canonical(dict(complete=report["complete"], candidateClear=report["testedCandidateInvariantsClear"],
                               allLodClear=report["testedAllLodInvariantsClear"], report=str(report_path))).decode().strip())
    return 0 if report["complete"] and report["testedAllLodInvariantsClear"] else 2


if __name__ == "__main__":
    raise SystemExit(main())

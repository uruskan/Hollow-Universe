"""Build a bounded, UNADMITTED closed forest-cliff geometry candidate.

The authenticated scan and qualified physical/far triangles remain exact prefixes.
Only the scan's single actual outer rim is extended; interior crown-domain loops
are never filled. Four explicitly pinned anchors segment that rim. Two retained
rings follow the geometric boundary collar and reach the full-source envelope.
The region between the rings is triangulated as one exact projected annulus;
source arclength does not force potentially crossing spokes between them.
A bounded global zipper-path search retains every vertex in both rings until a
complete correspondence is found. Failure is explicitly scoped to this fixed-
bridge triangulation class; it is not a proof that every annulus is impossible.
The side guides follow an explicit rear vertical / low return profile. The rear
legs belong to the back wall, so they cannot overlap a second side-wall patch.
Only beyond that envelope are wall faces coarsened to four lower base corners.
All generated positions are rounded to float32 BEFORE any geometry checks so the
visual and physical bytes describe the same surface. No automatic topology repair.

This is an offline standard-library tool, not Blender execution or asset admission.
Original rock PBR is retained. New surfaces have review colors, not qualified soil
materials. No contacts, roots, habitat, or gameplay capabilities are published.
Global exact intersections, geometric vertex links, edges, winding and volume are
checked for all three resulting LOD/physical surfaces before any output is written.
Original near-to-physical approximation is a separate prerequisite; closing a scan
does not improve or certify it. Runtime terrain fit and artist review remain gates.

Root schedules this under an external <=512 MiB / <=60 s process guard. The internal
deadline is 55 s by default, shared with input admission and exact geometry work.
"""
from __future__ import annotations

import argparse
from collections import defaultdict
from dataclasses import dataclass, replace
from fractions import Fraction
import importlib.util
import json
import math
from pathlib import Path
import re
import shutil
import struct
import sys

_spec = importlib.util.spec_from_file_location("_forest_module_quality", Path(__file__).with_name("cliff_mesh_quality.py"))
quality = importlib.util.module_from_spec(_spec)
sys.modules[_spec.name] = quality
_spec.loader.exec_module(quality)
author = quality.author
require = author.require
_uv_spec = importlib.util.spec_from_file_location("_forest_contact_metric_uv",Path(__file__).with_name("forest_contact_uv.py"))
metric_uv = importlib.util.module_from_spec(_uv_spec)
sys.modules[_uv_spec.name] = metric_uv
_uv_spec.loader.exec_module(metric_uv)
_normal_spec = importlib.util.spec_from_file_location("_forest_contact_soil_normals",Path(__file__).with_name("forest_contact_normals.py"))
soil_normals = importlib.util.module_from_spec(_normal_spec)
sys.modules[_normal_spec.name] = soil_normals
_normal_spec.loader.exec_module(soil_normals)

GROUP_COLORS = {
    "soil-cap-candidate": (0.40, 0.27, 0.14),
    "toe-apron-candidate": (0.57, 0.44, 0.28),
    "left-return": (0.50, 0.37, 0.56), "right-return": (0.43, 0.38, 0.62),
    "transition-interior-candidate": (0.60, 0.42, 0.22),
    "back-wall": (0.35, 0.43, 0.60), "toe-wall": (0.40, 0.58, 0.63),
    "left-wall": (0.50, 0.55, 0.65), "right-wall": (0.42, 0.50, 0.59),
    "base": (0.23, 0.28, 0.33),
}
ROLES = {
    frozenset(("crest-left", "crest-right")): "crest",
    frozenset(("crest-right", "toe-right")): "right",
    frozenset(("toe-right", "toe-left")): "toe",
    frozenset(("toe-left", "crest-left")): "left",
}
TOP_GROUP = dict(crest="soil-cap-candidate", toe="toe-apron-candidate", left="left-return", right="right-return")
WALL_GROUP = dict(crest="back-wall", toe="toe-wall", left="left-wall", right="right-wall")


class AuthoringWork(quality.Work):
    """Same limits/predicates as quality.Work, with bounded refusal diagnostics."""
    def __init__(self, limits=None):
        super().__init__(limits)
        self.phase, self.phase_counts, self.phases, self.details = "initialization", {}, [], None
        self.pair_cache = ExactPairCache()

    def mark(self, name, details=None):
        require(type(name) is str and len(name) <= 80 and len(self.phases) < 64,
                "Authoring phase diagnostic budget exceeded")
        self.phases.append(dict(phase=self.phase,workDelta={key:value-self.phase_counts.get(key,0)
            for key,value in self.counts.items() if value != self.phase_counts.get(key,0)}))
        self.phase, self.phase_counts, self.details = name, dict(self.counts), details

    def annotate(self, error):
        if getattr(error,"authoring_phase_attached",False): return error
        report = dict(phase=self.phase,workCounts=dict(self.counts),
            phaseWorkDelta={key:value-self.phase_counts.get(key,0) for key,value in self.counts.items()
                            if value != self.phase_counts.get(key,0)},
            completedPhases=self.phases,details=self.details,exactPairCache=self.pair_cache.summary())
        # Details are bounded search counters, never meshes, textures or files.
        error.args = (str(error)+"; authoringWork="+json.dumps(report,separators=(",",":")),)
        error.authoring_phase_attached = True
        return error

    def take(self, kind, amount=1):
        try: return super().take(kind,amount)
        except quality.Incomplete as error: raise self.annotate(error)

    def check(self):
        try: return super().check()
        except quality.Incomplete as error: raise self.annotate(error)


def authoring_phase(work, name, details=None):
    # Direct synthetic helpers also accept the unchanged shared quality.Work.
    if isinstance(work,AuthoringWork): work.mark(name,details)


class ExactPairCache:
    """Bounded, run-local reuse of exact ALLOWED triangle-pair proofs only.

Identity is three exact represented float positions, sorted without winding or
mesh indices. The shared classifier's allowed set is invariant under triangle
permutation, winding and exact coincident fan indices. Every pair is still
enumerated/charged; only already completed arithmetic can be reused. Rejections
are recomputed so exact witnesses/first-vs-second interior flags remain intact.
No old package proof, tolerance, hash digest or approximate spatial key is used.

Callers derive identities from the same immutable position arrays that generated
their exact coordinates. No mesh references or mutable position arrays survive
inside this cache. Capacity exhaustion computes normally without adding entries.
The byte charge is conservative admission accounting, not measured Python RSS.
"""
    MAXIMUM_POINTS = 65536
    MAXIMUM_TRIANGLES = 80000
    MAXIMUM_PAIRS = 131072
    MAXIMUM_RESERVED_BYTES = 64*1024*1024
    POINT_BYTES, TRIANGLE_BYTES, PAIR_BYTES = 512, 256, 128

    def __init__(self, maximum_bytes=MAXIMUM_RESERVED_BYTES, maximum_pairs=MAXIMUM_PAIRS):
        require(type(maximum_bytes) is int and 0 <= maximum_bytes <= self.MAXIMUM_RESERVED_BYTES and
                type(maximum_pairs) is int and 0 <= maximum_pairs <= self.MAXIMUM_PAIRS,
                "Invalid exact pair cache reservation")
        self.maximum_bytes, self.maximum_pairs = maximum_bytes, maximum_pairs
        self.points, self.triangles, self.pairs = {}, {}, {}
        self.reserved_bytes = self.hits = self.misses = self.capacity_fallbacks = 0

    def _reserve(self, amount):
        if self.reserved_bytes+amount > self.maximum_bytes:
            self.capacity_fallbacks += 1
            return False
        self.reserved_bytes += amount
        return True

    def triangle_id(self, points):
        require(len(points) == 3,"Exact cache requires three triangle corners")
        ids = []
        for point in points:
            key = tuple(point)
            value = self.points.get(key)
            if value is None:
                require(len(key) == 3 and all(type(v) in (int,float) and math.isfinite(v) for v in key),
                        "Exact cache positions must be finite represented coordinates")
                if len(self.points) >= self.MAXIMUM_POINTS:
                    self.capacity_fallbacks += 1
                    return None
                if not self._reserve(self.POINT_BYTES): return None
                value = self.points[key] = len(self.points)
            ids.append(value)
        key = tuple(sorted(ids))
        value = self.triangles.get(key)
        if value is None:
            if len(self.triangles) >= self.MAXIMUM_TRIANGLES:
                self.capacity_fallbacks += 1
                return None
            if not self._reserve(self.TRIANGLE_BYTES): return None
            value = self.triangles[key] = len(self.triangles)
        return value

    def mesh_ids(self, mesh, work):
        ids = []
        for triangle in mesh.triangles:
            work.take("topology_steps")
            ids.append(self.triangle_id([mesh.positions[v] for v in triangle]))
        return ids

    def classify(self, a, b, id_a, id_b, work):
        key = None if id_a is None or id_b is None else (min(id_a,id_b) << 32) | max(id_a,id_b)
        kind = self.pairs.get(key) if key is not None else None
        if kind is not None:
            work.take("pair_tests")
            self.hits += 1
            # Reconstruct shared simplex points in this invocation's exact
            # scale; the cached proof itself stores no scaled rational values.
            common = tuple(sorted(set(a) & set(b))) if kind != "disjoint" else ()
            return quality.Intersection(kind,common)
        self.misses += 1
        hit = quality.classify_exact(a,b,work)
        if key is not None and hit.kind in quality.ALLOWED:
            if len(self.pairs) >= self.maximum_pairs:
                self.capacity_fallbacks += 1
            elif self._reserve(self.PAIR_BYTES):
                self.pairs[key] = hit.kind
        return hit

    def summary(self):
        return dict(points=len(self.points),triangles=len(self.triangles),pairs=len(self.pairs),
            hits=self.hits,uncachedClassifications=self.misses,capacityFallbacks=self.capacity_fallbacks,
            reservedBytes=self.reserved_bytes,maximumReservedBytes=self.maximum_bytes,
            maximumPoints=self.MAXIMUM_POINTS,maximumTriangles=self.MAXIMUM_TRIANGLES,maximumPairs=self.maximum_pairs,
            scope="Exact allowed geometric pair classifications within this run; every BVH candidate still enumerated and charged; uncached fallback at capacity")


def module_intersections(mesh, work):
    cache = getattr(work,"pair_cache",None)
    if cache is None: return quality.intersections(mesh,work)
    ids = cache.mesh_ids(mesh,work)
    def classifier(current,a,b,current_work):
        return cache.classify(current.points(a,True),current.points(b,True),ids[a],ids[b],current_work)
    return quality.intersections(mesh,work,classifier)


def f32(value):
    require(type(value) in (int, float) and math.isfinite(value) and abs(value) <= 1e4,
            "Generated coordinate is outside finite local metre bounds")
    return struct.unpack("<f", struct.pack("<f", value))[0]


def validate_recipe(recipe):
    require(type(recipe) is dict and set(recipe) == {
        "schemaVersion", "status", "assetKey", "receiptSha256", "sourceSha256", "physicalSha256",
        "expectedOuterEdges", "scale", "anchors", "outerGuideRule", "returnRailProfile", "dimensionsMetres", "budgets", "uvAuthoring", "normalAuthoring"},
        "Unexpected module recipe shape")
    require(type(recipe["schemaVersion"]) is int and recipe["schemaVersion"] == 1 and
            recipe["status"] == "proposed-unadmitted" and recipe["assetKey"] == "nordic_forest_ledge",
            "Unsupported module recipe/status")
    require(recipe["outerGuideRule"] == "global-collar-y-envelope-v1",
            "Unsupported explicit outer guide rule")
    profile = recipe["returnRailProfile"]
    require(type(profile) is dict and set(profile) == {"rule", "boundsSemantics", "baseClearanceMetres"} and
            profile["rule"] == "rear-vertical-low-return-v1" and
            profile["boundsSemantics"] == "complete-authenticated-near-position-bounds" and
            type(profile["baseClearanceMetres"]) in (int, float) and
            profile["baseClearanceMetres"] == .10,
            "Return rail requires the authored rear/lower profile, full-source bounds and 0.10m base clearance")
    require(type(recipe["scale"]) in (int, float) and recipe["scale"] == 1,
            "This candidate is qualified only at exact unit scale")
    for key in ("receiptSha256", "sourceSha256", "physicalSha256"):
        require(type(recipe[key]) is str and re.fullmatch("[0-9a-f]{64}", recipe[key]), "Invalid recipe digest")
    require(type(recipe["expectedOuterEdges"]) is int and 4 <= recipe["expectedOuterEdges"] <= 1024,
            "Outer boundary edge budget is 4..1024")
    anchors = recipe["anchors"]
    require(type(anchors) is dict and set(anchors) == {"crest-left", "crest-right", "toe-left", "toe-right"},
            "Four named, explicit anchors are required")
    for row in anchors.values():
        require(type(row) is dict and set(row) == {"physicalVertex", "positionMetres"} and
                type(row["physicalVertex"]) is int and 0 <= row["physicalVertex"] < 8192 and
                type(row["positionMetres"]) is list and len(row["positionMetres"]) == 3 and
                all(type(v) in (int, float) and math.isfinite(v) and abs(v) < 1e4 for v in row["positionMetres"]),
                "Anchor requires a pinned physical index and exact coordinate")
    require(len({tuple(row["positionMetres"]) for row in anchors.values()}) == 4, "Anchor coordinates must be distinct")
    dimensions = recipe["dimensionsMetres"]
    require(type(dimensions) is dict and set(dimensions) == {
        "rearExtension", "toeExtension", "leftExtension", "rightExtension", "baseBelowLowestSource",
        "crestOuterRise", "toeOuterRise", "collarWidth"}, "All metre dimensions must be explicit")
    for key, value in dimensions.items():
        require(type(value) in (int, float) and math.isfinite(value) and
                (0 <= value <= .5 if key.endswith("Rise") else .005 <= value <= .25 if key == "collarWidth" else .05 <= value <= 3),
                "Invalid explicit extension/depth/rise")
    budgets = recipe["budgets"]
    require(type(budgets) is dict and set(budgets) == {"maximumAddedTriangles", "maximumPhysicalTriangles", "maximumAddedVertices"},
            "Explicit candidate geometry budgets required")
    for key, ceiling in (("maximumAddedTriangles", 5200), ("maximumPhysicalTriangles", 16384), ("maximumAddedVertices", 2053)):
        require(type(budgets[key]) is int and 1 <= budgets[key] <= ceiling, "Invalid module geometry budget")
    try: metric_uv.validate_policy(recipe["uvAuthoring"])
    except metric_uv.UvError as error: raise author.QualificationError(str(error)) from error
    require(recipe["uvAuthoring"]["maximumFaces"] == budgets["maximumAddedTriangles"],
            "UV authoring and added-geometry face capacities must agree")
    try: soil_normals.validate_policy(recipe["normalAuthoring"])
    except soil_normals.NormalError as error: raise author.QualificationError(str(error)) from error
    require(recipe["normalAuthoring"]["maximumFaces"] == budgets["maximumAddedTriangles"],
            "Normal authoring and added-geometry face capacities must agree")
    return recipe


def outer_cycle(mesh, expected_edges, work):
    """Exact coordinate welding is only for topology, never source mutation."""
    topology = quality.topology(mesh, work)
    require(topology["edgeConnectedComponents"] == 1 and topology["componentEulerSum"] == 1 and
            not topology["nonmanifoldEdges"] and not topology["inconsistentWindingEdges"] and
            not topology["verticesWithoutEdgeFaces"], "Input must be one consistently wound edge-manifold disk candidate")
    edges = topology["boundaries"]
    require(len(edges) == expected_edges, "Actual outer edge count differs from pinned recipe")
    next_points, previous_points = {}, {}
    for a, b in sorted(edges):
        require(a not in next_points and b not in previous_points, "Branch/pinch on actual outer rim; artist segmentation required")
        next_points[a], previous_points[b] = b, a
    require(set(next_points) == set(previous_points), "Unclosed boundary chain")
    start = min(next_points)
    cycle, visited, current = [], set(), start
    while current not in visited:
        work.take("topology_steps")
        visited.add(current)
        cycle.append(current)
        current = next_points[current]
    require(current == start and len(cycle) == len(edges), "Expected exactly one simple outer rim, not internal holes")
    return cycle


def pinned_arcs(cycle, physical, anchors, work):
    names = {}
    for name, row in anchors.items():
        index, point = row["physicalVertex"], tuple(row["positionMetres"])
        require(index < len(physical.positions) and physical.positions[index] == point,
                "Authenticated physical anchor moved: " + name)
        require(point in cycle, "Pinned anchor is not on the actual whole-scan outer rim: " + name)
        names[point] = name
    # Rotate to an explicit anchor. Respect source winding even if it is reversed.
    offset = cycle.index(tuple(anchors["crest-left"]["positionMetres"]))
    ordered = cycle[offset:] + cycle[:offset]
    starts = [i for i, p in enumerate(ordered) if p in names]
    require(len(starts) == 4, "Ambiguous outer-rim anchors")
    arcs = []
    for i, start in enumerate(starts):
        finish = starts[i+1] if i+1 < len(starts) else len(ordered)
        points = ordered[start:finish] + [ordered[finish % len(ordered)]]
        role = ROLES.get(frozenset((names[points[0]], names[points[-1]])))
        require(role is not None, "Anchor order does not define crest, right, toe and left arcs")
        work.take("topology_steps", len(points))
        arcs.append(dict(role=role, start=names[points[0]], end=names[points[-1]], points=points))
    require(len({arc["role"] for arc in arcs}) == 4, "Duplicated arc role")
    return arcs


def parameters(points):
    lengths = [math.dist(a, b) for a, b in zip(points, points[1:])]
    require(all(math.isfinite(v) and v > 0 for v in lengths), "Zero/invalid arc segment")
    total = math.fsum(lengths)
    require(total > 0, "Zero-length arc")
    result, amount = [0.], 0.
    for value in lengths:
        amount += value
        result.append(amount / total)
    result[-1] = 1.
    require(all(a < b for a, b in zip(result, result[1:])), "Arc length parameter lost precision")
    return result


def collar_positions(cycle, source, width, work):
    """A local exterior collar uses original geometric faces, never averaged UV normals.

This only establishes the outward side of each incident boundary face. Other
source folds can still obstruct the collar, so all-pairs geometry checks follow.
"""
    edges = set(zip(cycle, cycle[1:]+cycle[:1]))
    incident, directions = {}, defaultdict(list)
    for triangle in source.triangles:
        work.take("topology_steps")
        points = [source.positions[v] for v in triangle]
        for i in range(3):
            a, b, c = points[i], points[(i+1) % 3], points[(i+2) % 3]
            if (a, b) not in edges: continue
            require((a, b) not in incident, "Ambiguous original boundary incidence")
            incident[(a, b)] = c
            normal = quality.normal((a, b, c))
            outward = quality.cross(quality.sub(b, a), normal)
            length = math.sqrt(quality.dot(outward, outward))
            require(length > 0 and math.isfinite(length), "Unusable boundary co-normal")
            direction = tuple(v/length for v in outward)
            directions[a].append(direction)
            directions[b].append(direction)
    require(set(incident) == edges, "Missing original boundary face")
    result = []
    for point in cycle:
        require(len(directions[point]) == 2, "Boundary collar needs two unambiguous incident edges")
        direction = tuple(sum(row[i] for row in directions[point]) for i in range(3))
        length = math.sqrt(quality.dot(direction, direction))
        require(length > 1e-8 and math.isfinite(length), "Opposing boundary co-normals need artist-authored collar")
        result.append(tuple(f32(p+width*d/length) for p, d in zip(point, direction)))
    for i, a in enumerate(cycle):
        b, c = cycle[(i+1) % len(cycle)], incident[(a, cycle[(i+1) % len(cycle)])]
        ea, eb, ec = (tuple(Fraction(v) for v in point) for point in (a, b, c))
        normal = quality.normal((ea, eb, ec))
        for point in (result[i], result[(i+1) % len(cycle)]):
            work.take("exact_operations")
            q = tuple(Fraction(v) for v in point)
            sign = quality.dot(quality.cross(quality.sub(eb, ea), quality.sub(q, ea)), normal)
            require(sign < 0, "Rounded collar does not leave the original edge on its exterior side")
    return result


def outside_source_envelope(points, low, high):
    """Convex triangle lies strictly outside the full-source AABB, by one plane."""
    return any(all(p[i] < low[i] for p in points) or all(p[i] > high[i] for p in points) for i in range(3))


def return_profile_points(start, bend, end, source_times):
    """Retain the source arc's parameter events and insert one explicit bend.

Parameter distance is along this two-segment authored profile, never a straight
endpoint chord. The bend is retained even when no source event lands there. All
positions are rounded before topology/clearance; rounding coincidences refuse.
This is a fixed constructive rule, not a clearance-dependent length search.
"""
    require(2 <= len(source_times) <= 1025 and source_times[0] == 0 and source_times[-1] == 1 and
            all(type(t) in (int, float) and math.isfinite(t) for t in source_times) and
            all(a < b for a, b in zip(source_times, source_times[1:])), "Invalid bounded return profile events")
    first, second = math.dist(start, bend), math.dist(bend, end)
    require(first > 0 and second > 0 and math.isfinite(first+second), "Invalid authored return profile legs")
    bend_time = first/(first+second)
    require(0 < bend_time < 1, "Return profile bend lost parameter precision")
    times = sorted(set(source_times) | {bend_time})
    points = []
    for t in times:
        if t == 0: point = start
        elif t == 1: point = end
        elif t == bend_time: point = bend
        else:
            a, b, u = (start, bend, t/bend_time) if t < bend_time else (bend, end, (t-bend_time)/(1-bend_time))
            point = tuple(x+u*(y-x) for x, y in zip(a, b))
        points.append(tuple(f32(v) for v in point))
    require(len(set(points)) == len(points), "Rounded return profile events coincide; no hidden welding")
    return times, points, times.index(bend_time)


def triangulate_planar_polygon(ids, positions, work):
    """Deterministic exact ears preserve every boundary segment, including collinear ones.

The wall top is monotone in its envelope coordinate but need not be convex in
height. A fan from one base corner can overlap across a low notch, so it is not
used. Work is explicitly charged; impossible/ambiguous polygons are refused.
"""
    require(3 <= len(ids) <= 1027 and len(set(ids)) == len(ids), "Invalid bounded wall polygon")
    points, _ = quality.exact_coordinates([positions[v] for v in ids], work)
    fixed = [i for i in range(3) if len({p[i] for p in points}) == 1]
    require(len(fixed) == 1, "Wall polygon must lie in exactly one envelope plane")
    axes = tuple(i for i in range(3) if i != fixed[0])
    area = sum(points[i][axes[0]]*points[(i+1)%len(points)][axes[1]] -
               points[(i+1)%len(points)][axes[0]]*points[i][axes[1]] for i in range(len(points)))
    require(area != 0, "Zero-area envelope wall")
    sign, pending, result = 1 if area > 0 else -1, list(range(len(ids))), []
    while len(pending) > 3:
        found = False
        for j, b in enumerate(pending):
            a, c = pending[j-1], pending[(j+1)%len(pending)]
            work.take("topology_steps")
            if sign*quality.orient(points[a], points[b], points[c], axes) <= 0: continue
            occupied = False
            for other in pending:
                if other in (a, b, c): continue
                work.take("topology_steps")
                if all(sign*quality.orient(points[x], points[y], points[other], axes) >= 0
                       for x,y in ((a,b),(b,c),(c,a))):
                    occupied = True
                    break
            if occupied: continue
            result.append((ids[a], ids[b], ids[c]))
            del pending[j]
            found = True
            break
        require(found, "No exact nondegenerate wall ear; artist-authored envelope required")
    a, b, c = pending
    require(sign*quality.orient(points[a], points[b], points[c], axes) > 0, "Degenerate final wall ear")
    result.append((ids[a], ids[b], ids[c]))
    return result


@dataclass
class Closure:
    boundary: list
    positions: list  # boundary first, then newly generated points
    triangles: list
    groups: list
    arcs: list
    outer: list
    base_y: float
    annulus: dict


def construct_closure(cycle, arcs, source, recipe, work):
    dims, budgets = recipe["dimensionsMetres"], recipe["budgets"]
    # Two retained rings, at most one extra bend per side, four base corners and
    # a base center. Exact per-face/per-vertex caps below still govern output.
    require(2*len(cycle)+7 <= budgets["maximumAddedVertices"], "Added vertex budget exceeded by retained rings and return bends")
    require(5*len(cycle)+12 <= budgets["maximumAddedTriangles"], "Added triangle budget exceeded by retained rings and return bends")
    source_positions = source.positions
    low = [min(p[i] for p in source_positions) for i in range(3)]
    high = [max(p[i] for p in source_positions) for i in range(3)]
    xmin = f32(min(p[0] for p in source_positions)-dims["leftExtension"])
    xmax = f32(max(p[0] for p in source_positions)+dims["rightExtension"])
    rear = f32(min(p[2] for p in source_positions)-dims["rearExtension"])
    front = f32(max(p[2] for p in source_positions)+dims["toeExtension"])
    base_y = f32(min(p[1] for p in source_positions)-dims["baseBelowLowestSource"])
    require(xmin < min(p[0] for p in source_positions) and xmax > max(p[0] for p in source_positions) and
            rear < min(p[2] for p in source_positions) and front > max(p[2] for p in source_positions) and
            base_y < min(p[1] for p in source_positions), "Float32 rounding erased an explicit exterior extension")
    corners = {"crest-left": (xmin, rear), "crest-right": (xmax, rear),
               "toe-left": (xmin, front), "toe-right": (xmax, front)}
    positions, triangles, groups = list(cycle), [], []
    by_point = {point: i for i, point in enumerate(positions)}
    outer_corners, outer_cycle_ids, arc_evidence = {}, [], []

    def add_position(point):
        point = tuple(f32(v) for v in point)
        require(point not in by_point, "Generated point coincides with existing geometry; no hidden welding")
        require(len(positions)-len(cycle) < budgets["maximumAddedVertices"], "Added vertex budget exceeded")
        index = len(positions)
        positions.append(point)
        by_point[point] = index
        return index

    def face(a, b, c, group):
        require(len(triangles) < budgets["maximumAddedTriangles"], "Added triangle budget exceeded")
        triangles.append((a, b, c))
        groups.append(group)
        work.take("topology_steps")

    authoring_phase(work,"source-collar")
    collar = {point: add_position(other) for point, other in zip(cycle, collar_positions(cycle, source, dims["collarWidth"], work))}
    collar_low_y = min(positions[i][1] for i in collar.values())
    collar_high_y = max(positions[i][1] for i in collar.values())
    crest_y = f32(collar_high_y+dims["crestOuterRise"])
    toe_y = f32(collar_low_y+dims["toeOuterRise"])
    require(collar_low_y > base_y and toe_y > base_y,"Collar reaches/passes the explicit base depth")
    require(crest_y > toe_y,"Global envelope crest must be above its toe")
    bend_y = f32(base_y+recipe["returnRailProfile"]["baseClearanceMetres"])
    require(base_y < bend_y < min(collar_low_y, toe_y, low[1]) and toe_y < low[1],
            "Authored return rail base clearance must leave both lower endpoints below the full source and bend below the collar")
    require(Fraction(toe_y)-Fraction(bend_y) < Fraction(front)-Fraction(rear),
            "Authored lower return must be strictly monotone in Y-Z")
    projected_low = Fraction(toe_y)-Fraction(front)
    projected_high = Fraction(crest_y)-Fraction(rear)
    require(all(xmin < positions[i][0] < xmax and
                projected_low < Fraction(positions[i][1])-Fraction(positions[i][2]) < projected_high
                for i in collar.values()),
            "Rounded global guide rectangle does not strictly enclose the complete collar")
    authoring_phase(work,"return-guides-and-collar-faces")
    lower_corners = {name: add_position((point[0], base_y, point[1])) for name, point in corners.items()}
    for arc in arcs:
        points, role = arc["points"], arc["role"]
        times, inner = parameters(points), [by_point[p] for p in points]
        guide, collar_ids = [], [collar[p] for p in points]
        ax, az = corners[arc["start"]]
        bx, bz = corners[arc["end"]]
        ay = crest_y if arc["start"].startswith("crest") else toe_y
        by = crest_y if arc["end"].startswith("crest") else toe_y
        bend_index = None
        if role in ("left", "right"):
            guide_times, guide_points, bend_index = return_profile_points(
                (ax, ay, az), (ax, bend_y, rear), (bx, by, bz), times)
        else:
            guide_times = times
            guide_points = [(ax+t*(bx-ax), ay, az) for t in times]
        for t, point in zip(guide_times, guide_points):
            anchor = arc["start"] if t == 0 else arc["end"] if t == 1 else None
            if anchor in outer_corners:
                guide.append(outer_corners[anchor])
                continue
            index = add_position(point)
            guide.append(index)
            if anchor: outer_corners[anchor] = index
        outer_cycle_ids.extend(guide[:-1])
        first_face = len(triangles)
        for i in range(len(inner)-1):
            face(inner[i+1], inner[i], collar_ids[i], TOP_GROUP[role])
            face(inner[i+1], collar_ids[i], collar_ids[i+1], TOP_GROUP[role])
        evidence = dict(role=role, startAnchor=arc["start"], endAnchor=arc["end"],
            boundaryPointIds=inner, collarPointIds=collar_ids, outerGuidePointIds=guide,
            sourceArcParameters=times, outerGuideProfileParameters=guide_times,
            addedFaceOrdinals=list(range(first_face, len(triangles))),
            contactApproved=False, rootApproved=False)
        if bend_index is not None:
            starts_at_crest = arc["start"].startswith("crest")
            rear_guide = guide[:bend_index+1] if starts_at_crest else guide[bend_index:]
            lower_guide = guide[bend_index:] if starts_at_crest else guide[:bend_index+1]
            evidence.update(returnBendPointId=guide[bend_index], rearGuidePointIds=rear_guide,
                lowerGuidePointIds=lower_guide,
                parameterSemantics="Source arclength events along authored two-segment profile, plus exact bend event")
        arc_evidence.append(evidence)
        require(all(positions[v][1] > base_y for v in guide), "Collar reaches/passes the explicit base depth")
    # Rear vertical return legs and crest are ONE back-wall boundary. Keeping an
    # old side wall behind these legs would produce positive-area overlaps.
    rear_set = {v for v in outer_cycle_ids if positions[v][2] == rear}
    starts = [i for i, v in enumerate(outer_cycle_ids)
              if v in rear_set and outer_cycle_ids[i-1] not in rear_set]
    require(len(starts) == 1, "Rear guide must be one contiguous boundary chain")
    rear_chain, at = [], starts[0]
    while outer_cycle_ids[at % len(outer_cycle_ids)] in rear_set:
        require(len(rear_chain) < len(outer_cycle_ids), "Rear guide consumed the complete boundary")
        rear_chain.append(outer_cycle_ids[at % len(outer_cycle_ids)])
        at += 1
        work.take("topology_steps")
    require(set(rear_chain) == rear_set and
            all(positions[v][1] == bend_y for v in (rear_chain[0], rear_chain[-1])),
            "Rear guide must end at both explicit return bends")
    rear_base = {xmin: lower_corners["crest-left"], xmax: lower_corners["crest-right"]}
    wall_polygons = [("back-wall", list(reversed(rear_chain))+
                      [rear_base[positions[rear_chain[0]][0]], rear_base[positions[rear_chain[-1]][0]]])]
    for arc in arc_evidence:
        if arc["role"] == "crest": continue
        guide = arc.get("lowerGuidePointIds", arc["outerGuidePointIds"])
        wall_polygons.append((WALL_GROUP[arc["role"]], list(reversed(guide))+
                              [lower_corners[arc["startAnchor"]], lower_corners[arc["endAnchor"]]]))
    authoring_phase(work,"outer-envelope-wall-triangulation")
    wall_evidence = []
    for group, polygon in wall_polygons:
        first_face = len(triangles)
        # Coarsen ONLY on a separating full-source envelope plane. Every guide
        # vertex remains a wall boundary vertex; no split-edge T junctions.
        for tri in triangulate_planar_polygon(polygon, positions, work):
            require(outside_source_envelope([positions[v] for v in tri], low, high), "Wall coarsening enters the source envelope")
            face(*tri, group)
        wall_evidence.append(dict(group=group, boundaryPointIds=polygon,
                                  addedFaceOrdinals=list(range(first_face, len(triangles)))))
    require(len(set(outer_cycle_ids)) == len(outer_cycle_ids), "Generated outer guide is not a simple indexed loop")
    projected_guide = {(Fraction(positions[i][0]),Fraction(positions[i][1])-Fraction(positions[i][2]))
                       for i in outer_cycle_ids}
    rectangle_corners = {(Fraction(x),v) for x in (xmin,xmax) for v in (projected_low,projected_high)}
    require(rectangle_corners <= projected_guide and
            all(xmin <= x <= xmax and projected_low <= v <= projected_high and
                (x in (xmin,xmax) or v in (projected_low,projected_high)) for x,v in projected_guide),
            "Rounded outer guide left the authored projected rectangle")
    inner_cycle_ids = [collar[p] for p in cycle]
    # Source geometry and all already retained closure faces are fixed. A
    # proposed zipper triangle must clear both before advancing either ring.
    authoring_phase(work,"retained-closure-bvh")
    retained = quality.Mesh(positions,triangles,work)
    clearance = AnnulusClearance(positions,[("source-near",source),("retained-closure",retained)],work)
    annulus_triangles = triangulate_projected_annulus(inner_cycle_ids, outer_cycle_ids, positions, work,clearance)
    authoring_phase(work,"append-annulus-and-base")
    roles_by_edge = {}
    for arc in arc_evidence:
        for key in ("collarPointIds", "outerGuidePointIds"):
            for a,b in zip(arc[key],arc[key][1:]):
                roles_by_edge[tuple(sorted((a,b)))] = arc["role"]
    first_annulus_face = len(triangles)
    for triangle in annulus_triangles:
        roles = {roles_by_edge[tuple(sorted((a,b)))] for a,b in zip(triangle,triangle[1:]+triangle[:1])
                 if tuple(sorted((a,b))) in roles_by_edge}
        # A face may span authored role boundaries. Do not infer cap/root
        # semantics by its normal, centroid, nearest rim, or arbitrary split.
        group = TOP_GROUP[next(iter(roles))] if len(roles) == 1 else "transition-interior-candidate"
        face(*triangle,group)
    annulus = dict(projection="X,Y-Z",method="exact-global-two-ring-zipper-v1",
        outerGuideRule=recipe["outerGuideRule"],
        returnRailProfile=dict(recipe["returnRailProfile"], bendYMetres=bend_y,
            sourceMinimumMetres=low, sourceMaximumMetres=high,
            bendPointIds=[arc["returnBendPointId"] for arc in arc_evidence if "returnBendPointId" in arc],
            rearWallGuidePointIds=rear_chain, wallPatches=wall_evidence,
            semantics="Fixed rear vertical and below-source lower return; full original bounds, no fit burial or clearance search"),
        guideEnvelope=dict(collarMinimumYMetres=collar_low_y,collarMaximumYMetres=collar_high_y,
            crestYMetres=crest_y,toeYMetres=toe_y,rearZMetres=rear,frontZMetres=front,
            minimumXMetres=xmin,maximumXMetres=xmax,
            projectedMinimumYminusZExact=str(projected_low),projectedMaximumYminusZExact=str(projected_high),
            roundedCollarStrictlyContained=True,
            semantics="Proposed global cap/toe envelope; slope, material, soil depth and terrain contacts require review"),
        collarPointIds=inner_cycle_ids,outerGuidePointIds=outer_cycle_ids,
        addedFaceOrdinals=list(range(first_annulus_face,len(triangles))),
        projectedGraphVerified=True,sourceAwareTriangleChecks=clearance.summary(),sourceOverhangsRequireGlobal3dCheck=True,
        contactApproved=False,rootApproved=False)
    center = add_position(((xmin+xmax)/2, base_y, (rear+front)/2))
    for arc in arcs:
        tri = (lower_corners[arc["end"]], lower_corners[arc["start"]], center)
        require(outside_source_envelope([positions[v] for v in tri], low, high), "Base enters the source envelope")
        face(*tri, "base")
    return Closure(list(cycle), positions, triangles, groups, arc_evidence, outer_cycle_ids, base_y, annulus)


def compose(mesh, closure, work):
    positions = list(mesh.positions)
    lookup = {}
    for i, point in enumerate(positions):
        lookup.setdefault(point, i)
    require(all(point in lookup for point in closure.boundary), "LOD does not preserve an exact original rim position")
    mapping = [lookup[point] for point in closure.boundary]
    mapping.extend(range(len(positions), len(positions)+len(closure.positions)-len(closure.boundary)))
    positions.extend(closure.positions[len(closure.boundary):])
    triangles = list(mesh.triangles) + [tuple(mapping[v] for v in row) for row in closure.triangles]
    return quality.Mesh(positions, triangles, work), mapping


def closed_vertex_links(mesh, work):
    """A geometric manifold vertex has one connected cycle as its triangle link."""
    links = defaultdict(list)
    for triangle in mesh.triangles:
        points = [mesh.positions[i] for i in triangle]
        for i, point in enumerate(points):
            links[point].append((points[(i+1) % 3], points[(i+2) % 3]))
    for point, rows in sorted(links.items()):
        graph = defaultdict(list)
        for a, b in rows:
            work.take("topology_steps")
            graph[a].append(b)
            graph[b].append(a)
        require(all(len(neighbors) == 2 for neighbors in graph.values()), "Closed candidate has a nonmanifold vertex link")
        visited, pending = set(), [min(graph)]
        while pending:
            vertex = pending.pop()
            if vertex in visited: continue
            visited.add(vertex)
            pending.extend(other for other in graph[vertex] if other not in visited)
        require(len(visited) == len(graph), "Closed candidate has a pinched/disconnected vertex link")
    return len(links)


def qualify_closed(mesh, work, label="candidate"):
    authoring_phase(work,label+"-closed-topology")
    topology = quality.topology(mesh, work)
    require(topology["geometricOpenEdges"] == 0 and topology["nonmanifoldEdges"] == 0 and
            topology["inconsistentWindingEdges"] == 0 and topology["edgeConnectedComponents"] == 1 and
            topology["componentEulerSum"] == 2 and not topology["verticesWithoutEdgeFaces"],
            "Candidate must be one closed consistently wound geometric sphere manifold")
    authoring_phase(work,label+"-closed-vertex-links-volume")
    vertices = closed_vertex_links(mesh, work)
    signed_six_volume = 0
    for triangle in mesh.triangles:
        work.take("topology_steps")
        a, b, c = (mesh.exact[v] for v in triangle)
        signed_six_volume += quality.dot(a, quality.cross(b, c))
    require(signed_six_volume > 0, "Closed mesh winding must enclose positive outward volume; no silent reversal")
    authoring_phase(work,label+"-global-self-intersections")
    intersections = module_intersections(mesh, work)
    require(intersections["clear"], "Candidate self-intersects: " + json.dumps(intersections["witnesses"][:3], separators=(",", ":")))
    return dict(complete=True, geometricClosedManifold=True, geometricVertices=vertices,
        triangles=len(mesh.triangles), eulerCharacteristic=2, edgeConnectedComponents=1,
        signedVolumeCubicMetres=signed_six_volume/(6*mesh.scale**3), intersections=intersections,
        scope="Exact represented positions, geometric edge and vertex manifold, outward winding, all self triangle pairs; no terrain/root/material approval")


def build_geometry(source, physical, far, recipe, work):
    authoring_phase(work,"source-lod-rim-topology")
    validate_recipe(recipe)
    cycle = outer_cycle(source, recipe["expectedOuterEdges"], work)
    source_edges = set(zip(cycle, cycle[1:]+cycle[:1]))
    for mesh in (physical, far):
        other = outer_cycle(mesh, recipe["expectedOuterEdges"], work)
        require(set(zip(other, other[1:]+other[:1])) == source_edges, "LOD outer rim differs in position or direction")
    arcs = pinned_arcs(cycle, physical, recipe["anchors"], work)
    closure = construct_closure(cycle, arcs, source, recipe, work)
    require(len(physical.triangles)+len(closure.triangles) <= recipe["budgets"]["maximumPhysicalTriangles"],
            "Combined physical triangle budget exceeded; source triangles are never removed")
    require(len(physical.positions)+len(closure.positions)-len(closure.boundary) <= 8192,
            "Combined physical vertex count exceeds the engine geometry contract")
    candidates, mappings, reports = {}, {}, {}
    for name, mesh in (("near", source), ("physical", physical), ("far", far)):
        authoring_phase(work,name+"-compose-bvh")
        candidates[name], mappings[name] = compose(mesh, closure, work)
        if name == "far" and quality.same_triangle_positions(candidates[name], candidates["physical"]):
            reports[name] = dict(reuses="physical", complete=True, reason="Every ordered triangle position is exactly equal")
        else:
            reports[name] = qualify_closed(candidates[name], work,name)
    return closure, candidates, mappings, reports


def write_visual(destination, name, original, closure, work, uv_layout=None):
    """Leave original bytes unchanged; append review-color primitives with qualified UV/TBN."""
    if uv_layout is None:
        uv_layout = metric_uv.build(closure.positions,closure.triangles,closure.groups,check=work.check)
    require(len(uv_layout.faces) == len(closure.triangles),"Metric UV mapping does not cover the complete closure")
    document = json.loads(json.dumps(original))
    primitive = document["meshes"][0]["primitives"][0]
    require(len(document["meshes"][0]["primitives"]) == 1 and primitive["material"] == 0,
            "Expected authenticated single original rock primitive")
    blob = bytearray()
    buffer_index = len(document["buffers"])
    added_face_to_primitive = [None]*len(closure.triangles)
    for group in GROUP_COLORS:
        ids = [i for i, value in enumerate(closure.groups) if value == group]
        if not ids: continue
        positions, normals, uvs, tangents = [], [], [], []
        for f in ids:
            points = [closure.positions[v] for v in closure.triangles[f]]
            positions.extend(points)
            frame = uv_layout.faces[f]
            normals.extend(frame.normals); uvs.extend(frame.uvs); tangents.extend(frame.tangents)
        attributes = {}
        for attribute, values, width, kind in (("POSITION", positions, 3, "VEC3"),
                ("NORMAL", normals, 3, "VEC3"), ("TEXCOORD_0", uvs, 2, "VEC2"), ("TANGENT", tangents, 4, "VEC4")):
            offset = len(blob)
            for row in values: blob.extend(struct.pack("<"+"f"*width, *row))
            view_id = len(document["bufferViews"])
            document["bufferViews"].append(dict(buffer=buffer_index, byteOffset=offset, byteLength=len(blob)-offset, target=34962))
            accessor = dict(bufferView=view_id, componentType=5126, count=len(values), type=kind)
            if attribute == "POSITION": accessor.update(min=[min(p[i] for p in positions) for i in range(3)], max=[max(p[i] for p in positions) for i in range(3)])
            attributes[attribute] = len(document["accessors"])
            document["accessors"].append(accessor)
        offset = len(blob)
        for i in range(len(positions)): blob.extend(struct.pack("<I", i))
        view_id = len(document["bufferViews"])
        document["bufferViews"].append(dict(buffer=buffer_index, byteOffset=offset, byteLength=len(blob)-offset, target=34963))
        index_accessor = len(document["accessors"])
        document["accessors"].append(dict(bufferView=view_id, componentType=5125, count=len(positions), type="SCALAR"))
        material_id = len(document["materials"])
        document["materials"].append(dict(name=group+"-REVIEW-ONLY", pbrMetallicRoughness=dict(
            baseColorFactor=[*GROUP_COLORS[group], 1], metallicFactor=0, roughnessFactor=1), doubleSided=False))
        primitive_id = len(document["meshes"][0]["primitives"])
        document["meshes"][0]["primitives"].append(dict(attributes=attributes, indices=index_accessor, material=material_id, mode=4))
        for local, f in enumerate(ids): added_face_to_primitive[f] = [primitive_id, local]
        work.check()
    document["buffers"].append(dict(uri=name+"-transition.bin", byteLength=len(blob)))
    require(document["meshes"][0]["primitives"][0] == original["meshes"][0]["primitives"][0] and
            document["materials"][0] == original["materials"][0], "Original visual primitive/PBR changed")
    author.write_new(destination/(name+"-transition.bin"), bytes(blob))
    author.write_new(destination/(name+".gltf"), author.canonical(document))
    return added_face_to_primitive


def write_previews(destination, mesh, original_count, closure, work):
    """Bounded orthographic SVGs and exact face-group data; no raster/render claims."""
    views = (("top", 0, 2, 1), ("front", 0, 1, 2), ("side", 2, 1, 0))
    for name, horizontal, vertical, depth in views:
        low = [min(p[i] for p in mesh.positions) for i in range(3)]
        high = [max(p[i] for p in mesh.positions) for i in range(3)]
        scale = min(1160/(high[horizontal]-low[horizontal]), 700/(high[vertical]-low[vertical]))
        faces = sorted(range(len(mesh.triangles)), key=lambda f: (sum(mesh.positions[v][depth] for v in mesh.triangles[f]), f))
        lines = ['<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="800" viewBox="0 0 1200 800">',
                 '<rect width="1200" height="800" fill="#172029"/>',
                 '<text x="20" y="25" fill="white" font-family="sans-serif" font-size="16">'+name+' — unadmitted geometry / review colors / no root permissions</text>']
        for f in faces:
            color = (.46, .48, .49) if f < original_count else GROUP_COLORS[closure.groups[f-original_count]]
            color_text = "#"+"".join(f"{round(v*255):02x}" for v in color)
            coordinates = [((mesh.positions[v][horizontal]-low[horizontal])*scale+20,
                            760-(mesh.positions[v][vertical]-low[vertical])*scale) for v in mesh.triangles[f]]
            path = "M"+" L".join(f"{x:.4f},{y:.4f}" for x, y in coordinates)+" Z"
            lines.append(f'<path d="{path}" fill="{color_text}" stroke="#111820" stroke-width="0.12"/>')
        lines.append('</svg>')
        author.write_new(destination/("review-"+name+".svg"), ("\n".join(lines)+"\n").encode("utf-8"))
        work.check()


def _orient2(a, b, c):
    return (b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])


def _segment_parameters(a, b, c, d):
    """Exact closed-segment contacts as parameters on AB, including overlap ends."""
    r, s, q = (b[0]-a[0], b[1]-a[1]), (d[0]-c[0], d[1]-c[1]), (c[0]-a[0], c[1]-a[1])
    cross = lambda u,v: u[0]*v[1]-u[1]*v[0]
    denominator = cross(r, s)
    if denominator:
        t, u = Fraction(cross(q, s), denominator), Fraction(cross(q, r), denominator)
        return [t] if 0 <= t <= 1 and 0 <= u <= 1 else []
    if cross(q, r): return []
    if r == (0, 0):
        return [Fraction(0)] if _orient2(c,d,a) == 0 and all(min(c[i],d[i]) <= a[i] <= max(c[i],d[i]) for i in range(2)) else []
    axis = 0 if abs(r[0]) >= abs(r[1]) else 1
    ts = sorted((Fraction(c[axis]-a[axis], r[axis]), Fraction(d[axis]-a[axis], r[axis])))
    low, high = max(Fraction(0),ts[0]), min(Fraction(1),ts[1])
    return sorted({low,high}) if low <= high else []


class _BoundaryIndex:
    def __init__(self, loop, work):
        self.loop, self.work, self.nodes = loop, work, []
        self.edges = [(loop[i],loop[(i+1)%len(loop)]) for i in range(len(loop))]
        self.boxes = [(tuple(min(a[i],b[i]) for i in range(2)), tuple(max(a[i],b[i]) for i in range(2))) for a,b in self.edges]
        self._build(list(range(len(loop))))

    def _build(self, ids):
        self.work.take("node_visits")
        low = tuple(min(self.boxes[i][0][a] for i in ids) for a in range(2))
        high = tuple(max(self.boxes[i][1][a] for i in ids) for a in range(2))
        node = len(self.nodes)
        self.nodes.append(None)
        if len(ids) <= 8: self.nodes[node] = (low,high,tuple(sorted(ids)),())
        else:
            axis = max(range(2),key=lambda a:(high[a]-low[a],-a))
            ids.sort(key=lambda i:(self.boxes[i][0][axis]+self.boxes[i][1][axis],i))
            half = len(ids)//2
            self.nodes[node] = (low,high,(),(self._build(ids[:half]),self._build(ids[half:])))
        return node

    def query(self, low, high):
        pending = [0]
        while pending:
            self.work.take("node_visits")
            a,b,ids,children = self.nodes[pending.pop()]
            if any(b[i] < low[i] or a[i] > high[i] for i in range(2)): continue
            if children: pending.extend(reversed(children))
            else:
                for index in ids:
                    a,b = self.boxes[index]
                    if all(b[i] >= low[i] and a[i] <= high[i] for i in range(2)): yield index

    def contains(self, point):
        low, high = self.nodes[0][:2]
        if any(point[i] < low[i] or point[i] > high[i] for i in range(2)): return "outside"
        inside = False
        for index in self.query(point, (high[0],point[1])):
            self.work.take("topology_steps")
            a,b = self.edges[index]
            orientation = _orient2(a,b,point)
            if orientation == 0 and all(min(a[i],b[i]) <= point[i] <= max(a[i],b[i]) for i in range(2)): return "boundary"
            if (a[1] > point[1]) != (b[1] > point[1]):
                # Exact sign of horizontal-ray intersection x minus point.x.
                if (orientation > 0) == (b[1] > a[1]): inside = not inside
        return "inside" if inside else "outside"


def _simple_projected_loop(ids, points, work, name):
    loop = [points[i] for i in ids]
    require(len(set(loop)) == len(loop), name+" has coincident projected vertices")
    index = _BoundaryIndex(loop,work)
    for i,(a,b) in enumerate(index.edges):
        for j in index.query(*index.boxes[i]):
            if j <= i: continue
            work.take("topology_steps")
            contacts = _segment_parameters(a,b,*index.edges[j])
            adjacent = j == i+1 or (i == 0 and j == len(loop)-1)
            allowed = [Fraction(1 if j == i+1 else 0)]
            require(not contacts or (adjacent and contacts == allowed),
                    f"{name} is not simple at edges {i},{j}; authored guide required")
    area = sum(a[0]*b[1]-b[0]*a[1] for a,b in index.edges)
    require(area != 0,name+" has zero projected area")
    return index,area


def _only_endpoint_contacts(a,b,c,d):
    """True only for no contact or exact common endpoints; overlaps refuse."""
    contacts = _segment_parameters(a,b,c,d)
    return not contacts or (len(contacts) == 1 and
        ((contacts[0] == 0 and a in (c,d)) or (contacts[0] == 1 and b in (c,d))))


def _verify_projected_annulus(inner,outer,points,triangles,work):
    """Verify a conforming connected annulus on exact represented coordinates."""
    incidence = defaultdict(list)
    total_area = 0
    for f,triangle in enumerate(triangles):
        work.take("topology_steps")
        total_area += _orient2(*(points[i] for i in triangle))
        for a,b in zip(triangle,triangle[1:]+triangle[:1]):
            incidence[tuple(sorted((a,b)))].append((f,a,b))
    expected = set(zip(outer,outer[1:]+outer[:1])) | {(b,a) for a,b in zip(inner,inner[1:]+inner[:1])}
    actual,neighbors = set(),defaultdict(list)
    for rows in incidence.values():
        work.take("topology_steps")
        if len(rows) == 1:
            actual.add(rows[0][1:])
        else:
            require(len(rows) == 2 and rows[0][1:] == tuple(reversed(rows[1][1:])),
                    "Projected annulus has inconsistent/nonmanifold edge incidence")
            a,b = rows[0][0],rows[1][0]
            neighbors[a].append(b)
            neighbors[b].append(a)
    require(actual == expected,"Projected annulus did not preserve every directed inner/outer boundary segment")
    expected_area = sum(points[a][0]*points[b][1]-points[b][0]*points[a][1] for a,b in expected)
    used = {v for t in triangles for v in t}
    require(used == set(inner+outer) and total_area == expected_area and total_area > 0 and
            len(used)-len(incidence)+len(triangles) == 0,"Projected annulus area/Euler/vertex mismatch")
    visited,pending = set(),[0]
    while pending:
        f = pending.pop()
        if f in visited: continue
        work.take("topology_steps")
        visited.add(f)
        pending.extend(neighbors[f])
    require(len(visited) == len(triangles),"Projected annulus is disconnected")
    # Exact AABB sweep avoids a dense distance/pair matrix. The existing robust
    # classifier permits only shared original corners/edges, never a T junction
    # or positive-area overlap. All tuples share one exact integer scale.
    boxes = [(tuple(min(points[v][a] for v in t) for a in range(2)),
              tuple(max(points[v][a] for v in t) for a in range(2))) for t in triangles]
    active = []
    for f in sorted(range(len(triangles)),key=lambda i:(boxes[i][0][0],i)):
        next_active = []
        for g in active:
            work.take("topology_steps")
            if boxes[g][1][0] < boxes[f][0][0]: continue
            next_active.append(g)
            if boxes[g][1][1] < boxes[f][0][1] or boxes[f][1][1] < boxes[g][0][1]: continue
            a,b = (tuple((points[v][0],points[v][1],0) for v in triangles[i]) for i in (f,g))
            hit = quality.classify_exact(a,b,work)
            require(hit.kind in quality.ALLOWED,
                    f"Projected annulus triangles {f},{g} have {hit.kind}")
        active = next_active+[f]


class AnnulusClearance:
    """Bounded exact3D triangle admission against existing immutable mesh BVHs.

Only the fixed three vertex ids determine a triangle's surface; cached results
are therefore reusable across path choices. No source/retained face is
exempt because of shared vertices: the robust classifier must establish contact
is confined to a shared simplex. The independent final mesh checks still run.
"""
    MAXIMUM_CANDIDATES = 4096
    MAXIMUM_PAIR_TESTS = 250000
    MAXIMUM_NODE_VISITS = 750000

    def __init__(self,positions,obstacles,work):
        require(0 < len(positions) <= 4096 and 0 < len(obstacles) <= 4,
                "Annulus clearance input count cap exceeded")
        self.positions = [tuple(p) for p in positions]
        exact,scale = quality.exact_coordinates(self.positions,work)
        self.scale = max([scale]+[m.scale for _,m in obstacles])
        factor = self.scale//scale
        self.exact = [tuple(v*factor for v in p) for p in exact]
        self.obstacles = []
        for name,mesh in obstacles:
            require(type(name) is str and len(name) <= 64,"Invalid bounded clearance obstacle name")
            factor = self.scale//mesh.scale
            scaled = mesh.exact if factor == 1 else [tuple(v*factor for v in p) for p in mesh.exact]
            self.obstacles.append((name,mesh,scaled))
        self.work,self.cache,self.last_witness = work,{},None
        self.cache_hits,self.rejected,self.pair_tests,self.node_visits = 0,0,0,0
        self.zipper_selection = None
        self.pair_cache = getattr(work,"pair_cache",None)
        self.obstacle_triangle_ids = ([self.pair_cache.mesh_ids(mesh,work) for _,mesh,_ in self.obstacles]
                                     if self.pair_cache is not None else None)

    def _charge(self,kind):
        current = getattr(self,kind)+1
        setattr(self,kind,current)
        maximum = self.MAXIMUM_PAIR_TESTS if kind == "pair_tests" else self.MAXIMUM_NODE_VISITS
        if current > maximum: raise quality.Incomplete("Annulus clearance "+kind+" budget exceeded")
        # classify_exact charges pair_tests itself; BVH visits are charged here.
        if kind == "node_visits": self.work.take(kind)

    def summary(self):
        return dict(uniqueCandidates=len(self.cache),cachedQueries=self.cache_hits,rejectedCandidates=self.rejected,
            pairTests=self.pair_tests,nodeVisits=self.node_visits,
            maximumCandidates=self.MAXIMUM_CANDIDATES,maximumPairTests=self.MAXIMUM_PAIR_TESTS,
            maximumNodeVisits=self.MAXIMUM_NODE_VISITS,obstacles=[name for name,_,_ in self.obstacles],
            zipperSelection=self.zipper_selection,
            scope="Complete exact triangle checks against fixed source/retained geometry; final full mesh checks still required")

    def allows(self,triangle):
        self.work.take("topology_steps")
        require(len(triangle) == 3 and len(set(triangle)) == 3 and
                all(type(v) is int and 0 <= v < len(self.positions) for v in triangle),
                "Invalid clearance triangle")
        key = tuple(sorted(triangle))
        if key in self.cache:
            self.cache_hits += 1
            self.last_witness = self.cache[key]
            return self.last_witness is None
        if len(self.cache) >= self.MAXIMUM_CANDIDATES:
            raise quality.Incomplete("Annulus clearance unique candidate budget exceeded")
        floats = tuple(self.positions[v] for v in key)
        candidate = tuple(self.exact[v] for v in key)
        candidate_id = self.pair_cache.triangle_id(floats) if self.pair_cache is not None else None
        require(quality.normal(candidate) != (0,0,0),"Degenerate clearance triangle")
        low,high = (tuple(function(p[i] for p in floats) for i in range(3)) for function in (min,max))
        for obstacle_id,(name,mesh,exact) in enumerate(self.obstacles):
            pending = [0]
            while pending:
                self._charge("node_visits")
                node = mesh.nodes[pending.pop()]
                if any(node.high[i] < low[i] or high[i] < node.low[i] for i in range(3)): continue
                if node.children:
                    pending.extend(reversed(node.children))
                    continue
                for face in node.faces:
                    a,b = mesh.boxes[face]
                    if any(b[i] < low[i] or high[i] < a[i] for i in range(3)): continue
                    self._charge("pair_tests")
                    other = tuple(exact[v] for v in mesh.triangles[face])
                    hit = (quality.classify_exact(candidate,other,self.work) if self.pair_cache is None else
                           self.pair_cache.classify(candidate,other,candidate_id,
                               self.obstacle_triangle_ids[obstacle_id][face],self.work))
                    if hit.kind in quality.ALLOWED: continue
                    # Store a bounded witness, not arbitrary rational expressions
                    # for every rejected candidate. Pinned vertex coordinates and
                    # both ordinals allow reproduction of the exact classification.
                    witness = dict(candidateVertices=list(key),candidatePositions=floats,obstacle=name,
                        obstacleTriangle=face,obstaclePositions=mesh.points(face),classification=hit.kind,
                        intersectionPositionsMetres=[[float(v/self.scale) for v in p] for p in hit.points[:6]])
                    if mesh.source_triangle_ids is not None:
                        witness["originalSourceFace"] = mesh.source_triangle_ids[face]
                    self.cache[key] = self.last_witness = witness
                    self.rejected += 1
                    return False
        self.cache[key],self.last_witness = None,None
        return True


ZIPPER_MAXIMUM_STATES = 4096


def triangulate_projected_annulus(inner_ids,outer_ids,positions,work,clearance=None):
    """Find a global two-ring zipper path, with no vertex movement or ear prefix.

A state (i,j) is the current collar-to-guide connector. Its two possible triangles
advance exactly one original edge of either ring. Iterative backtracking and
failed-state memoization retain every original guide port until a complete path
is known. All local triangles have positive projected area, exact internal
connectors and immutable source/retained clearance; the full annulus and 3D mesh
verifiers remain independent final gates.

Completeness is ONLY for this fixed bridge and the zipper triangulation class:
every triangle touches one original boundary edge, and there are no same-ring
diagonal triangles or Steiner vertices. A capped search is incomplete. Exhausting
this class is not proof that all triangulations of the annulus are impossible.
"""
    authoring_phase(work,"annulus-projected-boundaries")
    inner,outer = list(inner_ids),list(outer_ids)
    require(3 <= len(inner) <= 1024 and 3 <= len(outer) <= 1024 and
            len(set(inner+outer)) == len(inner)+len(outer) and
            all(type(v) is int and 0 <= v < len(positions) for v in inner+outer),
            "Invalid bounded annulus loop indices")
    require(all(tuple(f32(v) for v in positions[i]) == tuple(positions[i]) for i in inner+outer),
            "Annulus must validate actual float32 output positions, not prerounded coordinates")
    ids = sorted(inner+outer)
    exact,_ = quality.exact_coordinates([positions[i] for i in ids],work)
    points = {i:(p[0],p[1]-p[2]) for i,p in zip(ids,exact)}
    inner_index,inner_area = _simple_projected_loop(inner,points,work,"Collar loop")
    outer_index,outer_area = _simple_projected_loop(outer,points,work,"Outer guide loop")
    require(inner_area*outer_area > 0,"Outer guide winding differs from collar")
    for a,b in inner_index.edges:
        work.take("topology_steps")
        require(outer_index.contains(a) == "inside","Outer guide does not strictly enclose collar")
        low = tuple(min(a[i],b[i]) for i in range(2))
        high = tuple(max(a[i],b[i]) for i in range(2))
        for edge in outer_index.query(low,high):
            work.take("topology_steps")
            require(not _segment_parameters(a,b,*outer_index.edges[edge]),
                    "Outer guide touches/crosses collar; authored guide required")
    sign = 1 if outer_area > 0 else -1
    if sign < 0: inner.reverse(); outer.reverse()
    h = min(inner,key=lambda i:(points[i],i))
    connector_cache = {}
    maximum_connectors = 2*ZIPPER_MAXIMUM_STATES+len(outer)+1
    def visible(a_id,b_id):
        key = (a_id,b_id)
        if key in connector_cache: return connector_cache[key]
        if len(connector_cache) >= maximum_connectors:
            raise quality.Incomplete("Annulus zipper connector budget exceeded")
        a,b = points[a_id],points[b_id]
        low = tuple(min(a[i],b[i]) for i in range(2))
        high = tuple(max(a[i],b[i]) for i in range(2))
        result = True
        for index in (inner_index,outer_index):
            for edge in index.query(low,high):
                work.take("topology_steps")
                if not _only_endpoint_contacts(a,b,*index.edges[edge]):
                    result = False
                    break
            if not result: break
        middle = tuple(Fraction(a[i]+b[i],2) for i in range(2))
        result = result and outer_index.contains(middle) == "inside" and inner_index.contains(middle) == "outside"
        connector_cache[key] = result
        return result
    ordered_outer = sorted(outer,key=lambda i:(sum((points[i][a]-points[h][a])**2 for a in range(2)),points[i],i))
    bridge = next((o for o in ordered_outer if visible(h,o)),None)
    require(bridge is not None,"No exact visible annulus bridge; authored guide required")
    hi,oi = inner.index(h),outer.index(bridge)
    inner,outer = inner[hi:]+inner[:hi],outer[oi:]+outer[:oi]
    n,m = len(inner),len(outer)
    report = dict(method="global-two-ring-zipper-v1",innerVertices=n,outerVertices=m,
        bridgeVertexIds=[h,bridge],states=0,maximumStates=ZIPPER_MAXIMUM_STATES,
        transitionChecks=0,maximumTransitionChecks=2*ZIPPER_MAXIMUM_STATES,
        geometricRejections=0,physicalRejections=0,backtracks=0,connectors=0,
        maximumConnectors=maximum_connectors,furthestState=[0,0],complete=False,solution=False,
        scope="Fixed-bridge two-ring zipper class only; no same-ring diagonal triangles, Steiner points or irreversible ear prefix")
    if clearance is not None: clearance.zipper_selection = report
    failed,admitted = set(),set()
    physical_witnesses,geometric_witnesses = [],[]
    path,stack = [],[]

    def push(state):
        if state not in admitted:
            if len(admitted) >= ZIPPER_MAXIMUM_STATES:
                raise quality.Incomplete("Annulus zipper state budget exceeded: "+json.dumps(report,separators=(",",":")))
            admitted.add(state)
            report["states"] = len(admitted)
        i,j = state
        if (i+j,i,j) > (sum(report["furthestState"]),*report["furthestState"]):
            report["furthestState"] = [i,j]
        options = []
        if i < n: options.append(((i+1,j),(inner[(i+1)%n],inner[i%n],outer[j%m]),"collar"))
        if j < m: options.append(((i,j+1),(inner[i%n],outer[j%m],outer[(j+1)%m]),"guide"))
        def rank(option):
            (a,b),triangle,kind = option
            first,last = inner[a%n],outer[b%m]
            return (sum((points[first][axis]-points[last][axis])**2 for axis in range(2)),first,last,kind)
        options.sort(key=rank)
        stack.append([state,options,0])

    def geometrically_admissible(next_state,triangle):
        if _orient2(*(points[v] for v in triangle)) <= 0: return "nonpositive-area"
        i,j = next_state
        if not visible(inner[i%n],outer[j%m]): return "blocked-next-connector"
        center = tuple(Fraction(sum(points[v][axis] for v in triangle),3) for axis in range(2))
        if outer_index.contains(center) != "inside" or inner_index.contains(center) != "outside":
            return "triangle-centroid-outside-annulus"
        return None

    authoring_phase(work,"annulus-source-aware-zipper-search",report)
    push((0,0))
    while stack:
        work.take("topology_steps")
        state,options,next_option = stack[-1]
        if state == (n,m):
            report["complete"],report["solution"] = True,True
            break
        if next_option == len(options):
            failed.add(state)
            stack.pop()
            if path: path.pop()
            report["backtracks"] += 1
            continue
        next_state,triangle,kind = options[next_option]
        stack[-1][2] += 1
        report["transitionChecks"] += 1
        require(report["transitionChecks"] <= report["maximumTransitionChecks"],"Annulus zipper transition accounting exceeded state bound")
        if next_state in failed: continue
        rejection = geometrically_admissible(next_state,triangle)
        report["connectors"] = len(connector_cache)
        if rejection:
            report["geometricRejections"] += 1
            if len(geometric_witnesses) < 8:
                geometric_witnesses.append(dict(state=list(state),nextState=list(next_state),advance=kind,
                    candidateVertices=list(triangle),reason=rejection))
            continue
        if clearance is not None and not clearance.allows(triangle):
            report["physicalRejections"] += 1
            if len(physical_witnesses) < 8 and clearance.last_witness not in physical_witnesses:
                physical_witnesses.append(clearance.last_witness)
            continue
        push(next_state)
        path.append(triangle)
    if not report["solution"]:
        report["complete"] = True
        raise author.QualificationError("No admissible global annulus zipper path: "+json.dumps(dict(
            search=report,blockingTriangleWitnesses=physical_witnesses,geometricWitnesses=geometric_witnesses,
            clearance=clearance.summary() if clearance is not None else None,
            scope="Fixed-bridge zipper class exhausted; this does not prove every annulus triangulation impossible. Same-ring diagonals or authored Steiner geometry may be required."),
            separators=(",",":")))
    require(len(path) == n+m,"Annulus zipper triangle count changed")
    authoring_phase(work,"annulus-projected-whole-graph-verification",report)
    _verify_projected_annulus(inner,outer,points,path,work)
    return path if sign > 0 else [tuple(reversed(t)) for t in path]


def projection_diagnostic(source, cycle, columns, work):
    """Exact simple-loop and whole projected source containment, not asset approval.

For a simple polygon without holes, source vertices and every complete source
edge lying in its closed interior imply containment of each closed triangle.
Edges are split at EVERY boundary contact, including endpoint tangencies and
collinear overlap endpoints; one midpoint per resulting open interval is exact
classification, not an unsplit sparse sample. Source projection overlaps are OK.
"""
    project = lambda p: tuple(sum(p[i]*column[i] for i in range(3)) for column in columns)
    points = [project(p) for p in source.exact]
    # A diagnostic loop may be an added collar rather than original vertices.
    # Represent it in the source's exact scale without silently rounding it.
    loop = [project(tuple(Fraction(v)*source.scale for v in p)) for p in cycle]
    work.fractions(loop)
    require(3 <= len(loop) <= 1024, "Diagnostic projected rim size is outside bounds")
    witnesses, crossings = [], 0
    if len(set(loop)) != len(loop):
        return dict(complete=True, simpleRim=False, containedSource=False, reason="Distinct rim positions coincide in projection")
    boundary = _BoundaryIndex(loop, work)
    for i,(a,b) in enumerate(boundary.edges):
        for j in boundary.query(*boundary.boxes[i]):
            if j <= i: continue
            work.take("topology_steps")
            ts = _segment_parameters(a,b,*boundary.edges[j])
            work.fractions([ts])
            adjacent = j == i+1 or (i == 0 and j == len(loop)-1)
            shared_parameter = Fraction(1) if j == i+1 else Fraction(0)
            if ts and not (adjacent and ts == [shared_parameter]):
                crossings += 1
                if len(witnesses) < 20:
                    witnesses.append(dict(rimEdges=[i,j], firstEdgeParameters=[str(t) for t in ts],
                        rimEdgePositions=[[cycle[i],cycle[(i+1)%len(cycle)]],[cycle[j],cycle[(j+1)%len(cycle)]]],
                        contactProjected=[[float((a[k]+t*(b[k]-a[k]))/source.scale) for k in range(2)] for t in ts]))
    area = sum(loop[i][0]*loop[(i+1)%len(loop)][1]-loop[(i+1)%len(loop)][0]*loop[i][1] for i in range(len(loop)))
    if crossings or not area:
        return dict(complete=True, simpleRim=False, containedSource=False, boundaryCrossings=crossings,
                    twiceSignedAreaScaled=str(area), boundaryWitnesses=witnesses)
    outside_vertices, outside_edges, contacts, vertex_witnesses, edge_witnesses = 0,0,0,[],[]
    for i,p in enumerate(points):
        work.take("topology_steps")
        if boundary.contains(p) == "outside":
            outside_vertices += 1
            if len(vertex_witnesses) < 20: vertex_witnesses.append(dict(sourceVertex=i, position=source.positions[i], projected=[float(v/source.scale) for v in p]))
    edges = sorted({tuple(sorted((t[i],t[(i+1)%3]))) for t in source.triangles for i in range(3)})
    for ia,ib in edges:
        work.take("topology_steps")
        a,b = points[ia],points[ib]
        if a == b: continue  # Complete collapsed edge is the already classified vertex.
        low,high = tuple(min(a[i],b[i]) for i in range(2)),tuple(max(a[i],b[i]) for i in range(2))
        cuts, hit_edges = {Fraction(0),Fraction(1)}, []
        for index in boundary.query(low,high):
            work.take("topology_steps")
            ts = _segment_parameters(a,b,*boundary.edges[index])
            if ts:
                work.fractions([ts])
                cuts.update(ts)
                hit_edges.append(index)
        if not hit_edges: continue  # No crossing and both endpoints classified above.
        contacts += len(hit_edges)
        ordered = sorted(cuts)
        for start,end in zip(ordered,ordered[1:]):
            mid = (start+end)/2
            p = tuple(Fraction(a[i])+mid*(b[i]-a[i]) for i in range(2))
            work.fractions([p])
            if boundary.contains(p) == "outside":
                outside_edges += 1
                if len(edge_witnesses) < 20:
                    edge_witnesses.append(dict(sourceVertices=[ia,ib], boundaryEdges=hit_edges,
                        outsideInterval=[str(start),str(end)], midpointProjected=[float(v/source.scale) for v in p]))
                break
    return dict(complete=True, simpleRim=True, containedSource=outside_vertices == 0 and outside_edges == 0,
        outsideSourceVertices=outside_vertices, outsideSourceEdges=outside_edges, sourceBoundaryContacts=contacts,
        testedSourceEdges=len(edges), sourceVertexWitnesses=vertex_witnesses, sourceEdgeWitnesses=edge_witnesses,
        sourceProjectedSelfOverlapAllowed=True,
        proofScope="Represented exact linear projection; all source vertices and complete edge intervals inside one simple closed rim imply closed-triangle containment")


def diagnose(project, package, recipe_path, report_path, expected_receipt, near_faces, seconds=55):
    """No geometry export/qualification; fresh diagnostic JSON only."""
    project, package, recipe_path, report_path = (Path(p).resolve() for p in (project,package,recipe_path,report_path))
    require(report_path.is_relative_to(project/".artifacts") and not report_path.exists() and report_path.parent.is_dir(),
            "Diagnostic report must be fresh under an existing project .artifacts parent")
    require(recipe_path.is_relative_to(project) and type(seconds) in (int,float) and math.isfinite(seconds) and 0 < seconds <= 55,
            "Invalid diagnostic root/deadline")
    require(len(near_faces) <= 32 and all(type(v) is int and v >= 0 for v in near_faces), "Diagnostic witness face cap exceeded")
    recipe_sha = author.sha256(recipe_path)
    recipe = validate_recipe(author.strict_json(recipe_path,64*1024,recipe_sha))
    require(expected_receipt == recipe["receiptSha256"], "Caller receipt pin differs from recipe")
    work = quality.Work(replace(quality.Limits(), maximum_topology_steps=12000000, seconds=seconds))
    source,physical,far = quality.read_package(project,package,expected_receipt,work)
    receipt = author.strict_json(package/"qualification-receipt.json",1024*1024,expected_receipt)
    pins = {row["file"]:row for row in receipt["outputFiles"]}
    require(receipt["source"]["sha256"] == recipe["sourceSha256"] and pins["physical.json"]["sha256"] == recipe["physicalSha256"],
            "Diagnostic recipe source/physical mismatch")
    cycle = outer_cycle(source,recipe["expectedOuterEdges"],work)
    arcs = pinned_arcs(cycle,physical,recipe["anchors"],work)
    closure = construct_closure(cycle,arcs,source,recipe,work)
    rim_edges = set(zip(cycle,cycle[1:]+cycle[:1]))
    rim_sources = {}
    for ordinal,triangle in enumerate(source.triangles):
        work.take("topology_steps")
        points = [source.positions[v] for v in triangle]
        for a,b in zip(points,points[1:]+points[:1]):
            if (a,b) in rim_edges:
                rim_sources[(a,b)] = dict(nearTriangle=ordinal,
                    originalSourceFace=source.source_triangle_ids[ordinal] if source.source_triangle_ids is not None else None,
                    sourceVertices=triangle,positions=points)
    rows = []
    for face in near_faces:
        require(face < len(source.triangles)+len(closure.triangles), "Diagnostic face is outside candidate near geometry")
        if face < len(source.triangles):
            rows.append(dict(nearTriangle=face,stage="source",sourceVertices=source.triangles[face],positions=source.points(face)))
            continue
        added = face-len(source.triangles)
        row = dict(nearTriangle=face,closureTriangle=added,semanticGroup=closure.groups[added],
                   closureVertices=closure.triangles[added],positions=[closure.positions[v] for v in closure.triangles[added]],stage="wall/base")
        if added in closure.annulus["addedFaceOrdinals"]:
            row.update(stage="projected-annulus",projection=closure.annulus["projection"])
        for arc in closure.arcs:
            if added not in arc["addedFaceOrdinals"]: continue
            count = len(arc["boundaryPointIds"])-1
            offset = added-arc["addedFaceOrdinals"][0]
            edge = (offset % (2*count))//2
            row.update(stage="source-to-collar",role=arc["role"],arcEdge=edge,
                sourceRimFace=rim_sources[(closure.positions[arc["boundaryPointIds"][edge]],closure.positions[arc["boundaryPointIds"][edge+1]])],
                boundaryNeighbors=[dict(arcVertex=i,boundary=closure.positions[arc["boundaryPointIds"][i]],
                    collar=closure.positions[arc["collarPointIds"][i]],
                    envelope=closure.positions[arc["outerGuidePointIds"][arc["outerGuideProfileParameters"].index(arc["sourceArcParameters"][i])]])
                    for i in range(max(0,edge-2),min(count+1,edge+4))])
        rows.append(row)
    projections = {}
    # The scanned face rises towards -Z: Y-Z separates crest and toe, while
    # Y+Z can collapse them. Keep both in the diagnostic rather than searching
    # arbitrary projection angles until a troublesome boundary happens to pass.
    for name,columns in (("XZ",((1,0,0),(0,0,1))),("XY",((1,0,0),(0,1,0))),
                         ("X_YplusZ",((1,0,0),(0,1,1))),("X_YminusZ",((1,0,0),(0,1,-1)))):
        projections[name] = projection_diagnostic(source,cycle,columns,work)
    collar_projection = projection_diagnostic(source, closure.positions[len(cycle):2*len(cycle)],
                                            ((1,0,0),(0,1,-1)), work)
    result = dict(schemaVersion=1,status="diagnostic-only-no-geometry-export",assetAccepted=False,recipeSha256=recipe_sha,
        sourceReceiptSha256=expected_receipt,toolSha256=author.sha256(Path(__file__)),witnessFaces=rows,projections=projections,
        collarProjection=collar_projection,
        arcs=[dict(role=a["role"],start=a["start"],end=a["end"],edges=len(a["points"])-1) for a in arcs],
        workCounts=dict(work.counts),workLimits=vars(work.limits),retainedBoundaryPositions=cycle)
    work.check()
    author.write_new(report_path,author.canonical(result))
    return result


def generate(project, package, recipe_path, destination, expected_receipt, seconds=55):
    require(type(seconds) in (int, float) and math.isfinite(seconds) and 0 < seconds <= 55,
            "Candidate authoring deadline must be positive and at most 55 seconds")
    project, package, destination = Path(project).resolve(), Path(package).resolve(), Path(destination).resolve()
    require(destination.is_relative_to(project/".artifacts") and not destination.exists() and destination.parent.is_dir(),
            "Destination must be fresh, explicit and under existing project .artifacts parent")
    recipe_path = Path(recipe_path).resolve()
    require(recipe_path.is_relative_to(project), "Recipe escapes project root")
    recipe_sha = author.sha256(recipe_path)
    recipe = validate_recipe(author.strict_json(recipe_path, 64*1024, recipe_sha))
    require(expected_receipt == recipe["receiptSha256"], "Caller receipt pin differs from authored recipe")
    work = AuthoringWork(replace(quality.Limits(), maximum_topology_steps=3000000, seconds=seconds))
    authoring_phase(work,"package-authentication")
    source, physical, far = quality.read_package(project, package, expected_receipt, work)
    authoring_phase(work,"recipe-provenance")
    receipt = author.strict_json(package/"qualification-receipt.json", 1024*1024, expected_receipt)
    pins = {row["file"]: row for row in receipt["outputFiles"]}
    require(receipt["source"]["sha256"] == recipe["sourceSha256"] and
            pins["physical.json"]["sha256"] == recipe["physicalSha256"], "Recipe source/physical provenance mismatch")
    try:
        closure, candidates, mappings, reports = build_geometry(source, physical, far, recipe, work)
    except quality.Incomplete as error:
        raise work.annotate(error)
    authoring_phase(work,"authenticated-visual-dependencies")
    sidecar = author.strict_json(package/"sidecar.json", 16*1024*1024, pins["sidecar.json"]["sha256"])
    lods = {row["key"]: row for row in sidecar["definitions"][0]["lods"]}
    documents, dependencies = {}, set()
    for name in ("near", "far"):
        filename = lods[name]["assetPath"]
        documents[name] = author.strict_json(package/filename, 4*1024*1024, pins[filename]["sha256"])
        for field in ("buffers", "images"):
            for row in documents[name].get(field, []):
                path = author.uri_file(package, row["uri"])
                require(path.name in pins, "Unpinned visual dependency")
                dependencies.add(path.name)
    authoring_phase(work,"closure-soil-normals")
    normal_layout = soil_normals.build(closure.positions,closure.triangles,closure.groups,recipe["normalAuthoring"],work.check)
    authoring_phase(work,"closure-metric-uv")
    uv_layout = metric_uv.build(closure.positions,closure.triangles,closure.groups,recipe["uvAuthoring"],work.check,
                               corner_normals=normal_layout.corner_normals)
    require(normal_layout.evidence["closureGeometrySha256"] == uv_layout.evidence["closureGeometrySha256"],
            "Normal and UV authoring geometry identity differs")
    # No output directory is created until every geometry, normal and UV check has completed.
    authoring_phase(work,"candidate-output")
    destination.mkdir()
    for name in sorted(dependencies):
        source_path, target = author.uri_file(package, name), destination/name
        require(not target.exists(), "Output dependency collision")
        with source_path.open("rb") as incoming, target.open("xb") as outgoing:
            shutil.copyfileobj(incoming, outgoing, 1024*1024)
        require(target.stat().st_size == pins[name]["bytes"] and author.sha256(target) == pins[name]["sha256"],
                "Copied source dependency digest changed")
        work.check()
    visual_mappings = {name: write_visual(destination, name, documents[name], closure, work,uv_layout) for name in ("near", "far")}
    require(author.sha256(destination/"near-transition.bin") == author.sha256(destination/"far-transition.bin"),
            "Near/far added positions, UVs, normals and tangents differ")
    geometry = {}
    for name, original in (("near", source), ("physical", physical), ("far", far)):
        candidate = candidates[name]
        visual_map = visual_mappings.get(name, [None]*len(closure.triangles))
        geometry[name] = dict(vertices=candidate.positions, triangles=candidate.triangles,
            indexSpace="Original exact-position-welded quality mesh; original glTF primitive attributes/indices stay unchanged",
            originalTriangleCount=len(original.triangles), originalVertexCount=len(original.positions),
            originalToCandidateTriangleOrdinal=list(range(len(original.triangles))),
            originalToCandidateVertexOrdinal=list(range(len(original.positions))),
            originalSourceFaceOrdinals=original.source_triangle_ids,
            closurePointToCandidateVertex=mappings[name],
            newFaces=[dict(ordinal=len(original.triangles)+f, closureOrdinal=f, semanticGroup=group,
                          visualPrimitiveTriangle=visual_map[f],uvChart=uv_layout.faces[f].chart)
                      for f, group in enumerate(closure.groups)])
    data = dict(schemaVersion=1, status="unadmitted-geometry-candidate", assetAccepted=False,
        scale=1, capabilities=[], contacts=[], rootVolumes=[], meshes=geometry,
        localFrame=dict(up=[0, 1, 0], outwardDownhill=[0, 0, 1], canonicalCrestDirection=[1, 0, 0]),
        closure=dict(points=closure.positions, triangles=closure.triangles, groups=closure.groups,
                     originalOuterRim=closure.boundary, arcs=closure.arcs, outerGuide=closure.outer,
                     baseYMetres=closure.base_y,annulus=closure.annulus),
        originalRockPbrPreserved=True, newMaterials="semantic review colors only",uvAuthoring=uv_layout.evidence,
        normalAuthoring=normal_layout.evidence,
        pending=["source approximation certificate", "artist segmentation and three-view review", "soil PBR and seam treatment",
                 "soil normal-fan visual review and scan-boundary continuity; straight base wall still needs real terrain seal review",
                 "continuous finalized-terrain contact", "soil volume/depth/footprint and overhead clearance",
                 "bounded package-loader contract", "in-engine collision/traversal and near/far review", "performance qualification"])
    author.write_new(destination/"module-candidate.json", author.canonical(data))
    author.write_new(destination/"geometry-checks.json", author.canonical(reports))
    author.write_new(destination/"uv-checks.json",author.canonical(uv_layout.evidence))
    author.write_new(destination/"normal-checks.json",author.canonical(normal_layout.evidence))
    author.write_new(destination/"recipe.json", author.canonical(recipe))
    write_previews(destination, candidates["physical"], len(physical.triangles), closure, work)
    work.check()
    outputs = [dict(file=path.name, bytes=path.stat().st_size, sha256=author.sha256(path)) for path in sorted(destination.iterdir())]
    result = dict(schemaVersion=1, status="unadmitted-geometry-candidate", assetAccepted=False,
        sourcePackage=package.relative_to(project).as_posix(), recipeFile=recipe_path.relative_to(project).as_posix(),
        sourceReceiptSha256=expected_receipt, sourceSha256=recipe["sourceSha256"], sourcePhysicalSha256=recipe["physicalSha256"],
        recipeSha256=recipe_sha, toolSha256=author.sha256(Path(__file__)),
        uvSchema=metric_uv.SCHEMA,uvToolSha256=author.sha256(Path(metric_uv.__file__)),
        normalSchema=soil_normals.SCHEMA,normalToolSha256=author.sha256(Path(soil_normals.__file__)),
        normalStreamSha256=normal_layout.evidence["normalStreamSha256"],
        closureGeometrySha256=uv_layout.evidence["closureGeometrySha256"],
        addedAttributeStreamSha256=uv_layout.evidence["addedAttributeStreamSha256"],
        originalBoundaryEdges=len(closure.boundary), addedVertices=len(closure.positions)-len(closure.boundary),
        addedTriangles=len(closure.triangles), physicalTriangles=len(candidates["physical"].triangles),
        outputFiles=outputs, workCounts=dict(work.counts), workPhases=work.phases,
        exactPairCache=work.pair_cache.summary(), capabilities=[], rootPermissions=False,
        qualificationScope="Closed geometry candidate only; original primitive bytes and exact rim correspondences retained")
    work.check()
    author.write_new(destination/"module-candidate-receipt.json", author.canonical(result))
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--package", type=Path, required=True)
    parser.add_argument("--recipe", type=Path, default=Path("tools/blender/data/forest_contact_module.json"))
    parser.add_argument("--destination", type=Path)
    parser.add_argument("--diagnostic-report", type=Path)
    parser.add_argument("--witness-near-triangles", type=int, nargs="*", default=[20673,20738,20739,20740])
    parser.add_argument("--receipt-sha256", required=True)
    parser.add_argument("--seconds", type=float, default=55)
    args = parser.parse_args()
    try:
        if args.diagnostic_report is not None:
            require(args.destination is None,"Diagnostic mode does not export a destination")
            result = diagnose(args.root,args.package,args.recipe,args.diagnostic_report,args.receipt_sha256,args.witness_near_triangles,args.seconds)
            print(json.dumps(dict(status=result["status"],projections={k:{f:v.get(f) for f in ("simpleRim","containedSource")} for k,v in result["projections"].items()}),sort_keys=True))
            return 0
        require(args.destination is not None,"Candidate export requires an explicit fresh destination")
        result = generate(args.root, args.package, args.recipe, args.destination, args.receipt_sha256, args.seconds)
        print(json.dumps({key: result[key] for key in ("status", "addedTriangles", "physicalTriangles", "assetAccepted")}, sort_keys=True))
    except (author.QualificationError, quality.Incomplete, OSError, KeyError, TypeError, ValueError) as error:
        print("INCOMPLETE/REFUSED: " + str(error), file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

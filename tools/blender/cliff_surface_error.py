"""Bounded whole-triangle-surface error certificate; never asset acceptance.

The metric is unsigned Euclidean distance between unions of CLOSED triangles,
including their edges. It is not volume, winding, collision/contact correctness,
or a normal-error test. Both directions are required for a symmetric bound.
Decoded IEEE coordinates are treated as exact dyadic values. Midpoints, BVH
pruning, point/triangle distances and threshold comparisons use rational
arithmetic. Only report square roots are floating point, rounded OUTWARD and
verified by exact squaring. No undocumented floating tolerance can create a pass.

For a source triangle cell C and a target union T, two upper bounds are used:
  d(center(C), T) + max_corner |corner-center(C)| (distance is 1-Lipschitz);
  min_target_triangle max_corner d(corner, target_triangle).
The second bound uses convexity of distance to ONE closed convex triangle.
Maxima of nearest-UNION corner distances are lower bounds only. Longest-edge
midpoint subdivision partitions the exact source triangle without moving it.
Every source triangle must be covered by certified cells before an upper bound
is published. Exhausted work/storage/depth/time returns incomplete, never pass.

Root schedules execution with an additional OS memory/wall-clock guard:
 python tools/blender/cliff_surface_error.py --package <prototype> \
   --receipt-sha256 <pinned digest> --report <fresh.json> --error-metres 0.15 --scale 1
No Blender, game, export, image decoding, package install or subprocess is used.
"""
from __future__ import annotations

import argparse
from dataclasses import dataclass
from fractions import Fraction
import heapq
import importlib.util
import math
from pathlib import Path
import re
import sys

_spec = importlib.util.spec_from_file_location("_cliff_error_quality", Path(__file__).with_name("cliff_mesh_quality.py"))
quality = importlib.util.module_from_spec(_spec)
sys.modules[_spec.name] = quality
_spec.loader.exec_module(quality)
author = quality.author
Incomplete = quality.Incomplete
sub, dot, cross = quality.sub, quality.dot, quality.cross


@dataclass(frozen=True)
class Limits(quality.Limits):
    maximum_cells: int = 250000
    maximum_subdivision_depth: int = 32
    maximum_pending_cells: int = 64
    maximum_heap_entries: int = 131072
    maximum_estimated_bytes: int = 256 * 1024 * 1024
    maximum_geometry_identity_tests: int = 150000
    maximum_distance_tests: int = 2000000
    maximum_node_visits: int = 4000000
    seconds: float = 55.0


class Work(quality.Work):
    def __init__(self, limits=None):
        super().__init__(limits or Limits())
        author.require(self.limits.maximum_subdivision_depth <= 40 and
                       self.limits.maximum_fraction_bits <= 8192 and
                       self.limits.maximum_vertices <= 60000 and
                       self.limits.maximum_triangles <= 50000,
                       "Surface certificate implementation ceilings exceeded")
        self.reserved = 0
        self.peak_reserved = 0

    def reserve(self, amount):
        """Conservative declared Python-storage estimate, NOT measured RSS."""
        self.check()
        if amount > self.limits.maximum_estimated_bytes - self.reserved:
            raise Incomplete("Estimated working-storage budget exceeded")
        self.reserved += amount
        self.peak_reserved = max(self.peak_reserved, self.reserved)

    def release(self, amount):
        self.reserved -= amount
        assert self.reserved >= 0

    def rational(self, value):
        value = Fraction(value)
        if max(abs(value.numerator).bit_length(), value.denominator.bit_length()) > self.limits.maximum_fraction_bits:
            raise Incomplete("Exact rational bit budget exceeded")
        return value


def parameter(value, name, positive=False):
    if type(value) is str:
        author.require(len(value) <= 64 and re.fullmatch(r"[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?", value),
                       "Invalid exact decimal " + name)
        # Bound decimal exponents BEFORE Fraction can allocate enormous powers.
        exponent = re.search(r"[eE]([+-]?\d+)$", value)
        author.require(exponent is None or abs(int(exponent[1])) <= 64, "Parameter exponent ceiling exceeded")
    else:
        author.require(type(value) in (int, float, Fraction) and
                       (type(value) is not float or math.isfinite(value)), "Invalid " + name)
    result = Fraction(value)
    author.require((result > 0 if positive else result >= 0) and result <= 1000000 and
                   max(abs(result.numerator).bit_length(), result.denominator.bit_length()) <= 256,
                   "Parameter range exceeded: " + name)
    return result


def sqrt_interval(value):
    """An exactly checked binary64 enclosure of sqrt(nonnegative Fraction)."""
    value = Fraction(value)
    author.require(value >= 0, "Negative squared distance")
    if not value:
        return 0.0, 0.0
    exponent = (value.numerator.bit_length() - value.denominator.bit_length()) // 2
    scaled = value / (Fraction(2) ** (2 * exponent))
    try:
        estimate = math.ldexp(math.sqrt(float(scaled)), exponent)
    except OverflowError as error:
        raise Incomplete("Distance report is outside binary64 range") from error
    if not math.isfinite(estimate):
        raise Incomplete("Distance report is outside binary64 range")
    low = high = estimate
    # Corrections are verified, not assumed from sqrt/libm accuracy. Even total
    # underflow has the valid enclosure [0, smallest-positive-binary64].
    for _ in range(8):
        if Fraction(low) * Fraction(low) <= value:
            break
        low = math.nextafter(low, 0.0)
    else:
        raise Incomplete("Could not round distance lower bound outward")
    for _ in range(8):
        if Fraction(high) * Fraction(high) >= value:
            break
        high = math.nextafter(high, math.inf)
    else:
        raise Incomplete("Could not round distance upper bound outward")
    if not math.isfinite(high):
        raise Incomplete("Distance upper bound overflow")
    return low, high


def squared_distance(point, triangle, work):
    """Exact distance to a closed nondegenerate triangle, including edges."""
    work.take("distance_tests")
    point = tuple(Fraction(v) for v in point)
    a, b, c = (tuple(Fraction(v) for v in row) for row in triangle)
    ab, ac, ap = sub(b, a), sub(c, a), sub(point, a)
    n = cross(ab, ac)
    n2 = dot(n, n)
    author.require(n2 > 0, "Degenerate target triangle")
    u, v = dot(cross(ap, ac), n), dot(cross(ab, ap), n)
    if u >= 0 and v >= 0 and u + v <= n2:
        height = dot(ap, n)
        return work.rational(height * height / n2)
    best = None
    for start, end in ((a, b), (b, c), (c, a)):
        direction, relative = sub(end, start), sub(point, start)
        length, along = dot(direction, direction), dot(relative, direction)
        if along <= 0:
            value = dot(relative, relative)
        elif along >= length:
            delta = sub(point, end)
            value = dot(delta, delta)
        else:
            value = dot(relative, relative) - along * along / length
        author.require(value >= 0, "Negative exact triangle distance")
        best = value if best is None else min(best, value)
    return work.rational(best)


class Surface:
    def __init__(self, mesh, work):
        # Counts also bound hash tables, cached rational corners and BVH boxes.
        # Includes the reused reader's Mesh storage, not just this wrapper.
        reserve = 4096 + 2048 * (len(mesh.positions) + len(mesh.triangles)) + 1024 * len(mesh.nodes)
        work.reserve(reserve)
        self.mesh = mesh
        self.vertices = [tuple(Fraction(v, mesh.scale) for v in row) for row in mesh.exact]
        self.boxes = [(tuple(Fraction(v) for v in node.low), tuple(Fraction(v) for v in node.high)) for node in mesh.nodes]
        self.identical = {}
        for face in range(len(mesh.triangles)):
            work.take("geometry_identity_tests")
            # Surface distance ignores winding; a reversed identical triangle
            # is still zero distance. Winding qualification is a separate gate.
            self.identical.setdefault(tuple(sorted(self.points(face))), face)

    def points(self, face):
        return tuple(self.vertices[v] for v in self.mesh.triangles[face])


def box_distance_squared(point, bounds):
    low, high = bounds
    return sum((lo - p if p < lo else p - hi if p > hi else 0) ** 2 for p, lo, hi in zip(point, low, high))


def nearest(point, target, work):
    """Exact nearest-union distance; exact box lower bounds permit pruning."""
    point = tuple(Fraction(v) for v in point)
    if work.limits.maximum_heap_entries < 1:
        raise Incomplete("Nearest-query heap entry budget exceeded")
    heap = [(box_distance_squared(point, target.boxes[0]), 0)]
    best, best_face = None, None
    while heap:
        work.take("node_visits")
        lower, index = heapq.heappop(heap)
        if best is not None and lower > best:
            continue
        node = target.mesh.nodes[index]
        if node.children:
            for child in node.children:
                bound = work.rational(box_distance_squared(point, target.boxes[child]))
                if best is None or bound <= best:
                    if len(heap) >= work.limits.maximum_heap_entries:
                        raise Incomplete("Nearest-query heap entry budget exceeded")
                    heapq.heappush(heap, (bound, child))
        else:
            for face in node.faces:
                value = squared_distance(point, target.points(face), work)
                if best is None or value < best or value == best and face < best_face:
                    best, best_face = value, face
    author.require(best is not None, "Empty target triangle surface")
    return best, best_face


def midpoint(a, b):
    return tuple((x + y) / 2 for x, y in zip(a, b))


def split_cell(triangle):
    lengths = [dot(sub(triangle[(i + 1) % 3], triangle[i]), sub(triangle[(i + 1) % 3], triangle[i])) for i in range(3)]
    edge = max(range(3), key=lambda i: (lengths[i], -i))
    a, b, c = (triangle[(edge + i) % 3] for i in range(3))
    middle = midpoint(a, b)
    return (a, middle, c), (middle, b, c)


def cell_bounds(triangle, target, tolerance_squared, work):
    same = target.identical.get(tuple(sorted(triangle)))
    if same is not None:
        return Fraction(0), Fraction(0), triangle[0], same
    center = tuple(sum(p[i] for p in triangle) / 3 for i in range(3))
    center_distance, face = nearest(center, target, work)
    lower, witness, witness_face = center_distance, center, face
    candidate_faces = {face}
    upper = max(squared_distance(p, target.points(face), work) for p in triangle)
    if upper > tolerance_squared:
        # Corner nearest-union values are LOWER evidence only. The returned
        # target triangles may independently improve a convex-triangle UPPER.
        for point in triangle:
            distance, corner_face = nearest(point, target, work)
            candidate_faces.add(corner_face)
            if distance > lower:
                lower, witness, witness_face = distance, point, corner_face
        for candidate in sorted(candidate_faces - {face}):
            upper = min(upper, max(squared_distance(p, target.points(candidate), work) for p in triangle))
        radius_squared = max(dot(sub(p, center), sub(p, center)) for p in triangle)
        # Adding Fraction versions of outward square roots avoids an inward
        # rounded floating addition. The resulting squared bound stays exact.
        lipschitz = Fraction(sqrt_interval(center_distance)[1]) + Fraction(sqrt_interval(radius_squared)[1])
        upper = min(upper, work.rational(lipschitz * lipschitz))
    return lower, upper, witness, witness_face


def directed(source, target, threshold, scale, work, name="source-to-target"):
    """Threshold-directed refinement with a complete-surface upper bound."""
    threshold, scale = parameter(threshold, "threshold"), parameter(scale, "scale", True)
    tolerance_squared = work.rational((threshold / scale) ** 2)
    lower, upper = Fraction(0), Fraction(0)
    witness = None
    refuted = False
    cells, leaves, deepest, completed_faces = 0, 0, 0, 0
    # Query heap and one depth-first cell stack have explicit count/byte caps.
    temporary = 4096 + work.limits.maximum_pending_cells * 16384 + min(len(target.mesh.nodes), work.limits.maximum_heap_entries) * 2048
    reserved = False
    result = dict(name=name, complete=False, withinThreshold=False, thresholdRefuted=False,
                  certifiedUpperBoundMetres=None, measuredLowerBoundMetres=0.0,
                  measuredLowerBoundIsNotHausdorffEstimate=True)
    try:
        work.reserve(temporary)
        reserved = True
        for source_face in range(len(source.mesh.triangles)):
            if work.limits.maximum_pending_cells < 1:
                raise Incomplete("Pending source-cell budget exceeded")
            pending = [(source.points(source_face), 0)]
            while pending:
                work.take("cells")
                triangle, depth = pending.pop()
                cells += 1
                deepest = max(deepest, depth)
                low, high, point, target_face = cell_bounds(triangle, target, tolerance_squared, work)
                if low > lower:
                    lower = low
                    witness = dict(sourceTriangle=source_face, targetTriangle=target_face,
                                   pointLocalExact=[str(v) for v in point], squaredDistanceLocalExact=str(low))
                refuted |= low > tolerance_squared
                if not refuted and high > tolerance_squared:
                    if depth >= work.limits.maximum_subdivision_depth:
                        raise Incomplete("Source-cell subdivision depth budget exceeded")
                    if len(pending) + 2 > work.limits.maximum_pending_cells:
                        raise Incomplete("Pending source-cell budget exceeded")
                    left, right = split_cell(triangle)
                    work.fractions(left + right)
                    pending.extend(((right, depth + 1), (left, depth + 1)))
                else:
                    upper = max(upper, high)
                    leaves += 1
            completed_faces += 1
        work.check()
        world_upper = work.rational(upper * scale * scale)
        result.update(complete=True, withinThreshold=upper <= tolerance_squared,
                      certifiedUpperBoundMetres=sqrt_interval(world_upper)[1],
                      certifiedUpperSquaredWorldExact=str(world_upper), reason="Every source triangle covered")
    except Incomplete as error:
        result["reason"] = str(error)
    finally:
        if reserved:
            work.release(temporary)
    world_lower = work.rational(lower * scale * scale)
    result.update(thresholdRefuted=refuted, measuredLowerBoundMetres=sqrt_interval(world_lower)[0],
                  measuredLowerSquaredWorldExact=str(world_lower), witness=witness,
                  processedCells=cells, certifiedLeaves=leaves, deepestSubdivision=deepest,
                  sourceTrianglesCompleted=completed_faces, sourceTriangles=len(source.mesh.triangles))
    return result


def assess(source, physical, far, work=None, threshold="0.15", scale="1"):
    work = work or Work()
    report = dict(schemaVersion=1, complete=False, withinThreshold=False, assetAccepted=False,
                  metric="Symmetric unsigned Euclidean distance between unions of closed triangles",
                  scope="All source/near, physical and far triangle surfaces; no volume, normal, topology, habitat or contact acceptance",
                  arithmetic="Exact decoded dyadic coordinates and rational distances/pruning; outward-verified binary64 report bounds",
                  upperBoundMethods=["One-convex-target-triangle maximum corner distance", "Nearest-union center distance plus whole-cell covering radius"],
                  reportRounding="Binary64 upper fields may round one ULP above the exact decimal threshold; pass uses exact squared bounds",
                  budgetScope="One aggregate work/deadline/estimated-storage ledger for authentication, all meshes and all directions",
                  storageAccounting="Declared conservative reservation; not measured RSS; external OS memory guard required",
                  directions=[], workLimits=vars(work.limits))
    try:
        threshold, scale = parameter(threshold, "threshold"), parameter(scale, "scale", True)
        report.update(thresholdMetresExact=str(threshold), uniformScaleExact=str(scale))
        # Build all wrappers under the same reservation. No stale/global cache.
        near_surface = Surface(source, work)
        physical_surface = Surface(physical, work)
        work.take("geometry_identity_tests", len(physical.triangles) + len(far.triangles))
        same_far = quality.same_triangle_positions(physical, far)
        work.check()
        if same_far:
            # The decoder still owns a distinct far Mesh even if proof queries
            # reuse the identical physical Surface wrapper.
            work.reserve(1024 * (len(far.positions) + len(far.triangles) + len(far.nodes)))
        far_surface = physical_surface if same_far else Surface(far, work)
        report["farPhysicalExactOrderedTriangleReuse"] = same_far
        pairs = (("near-to-physical", near_surface, physical_surface),
                 ("physical-to-near", physical_surface, near_surface),
                 ("near-to-far", near_surface, far_surface),
                 ("far-to-near", far_surface, near_surface))
        for index, (name, first, second) in enumerate(pairs):
            if same_far and index >= 2:
                result = dict(report["directions"][index - 2], name=name, reusedExactGeometry=True)
            else:
                result = directed(first, second, threshold, scale, work, name)
            report["directions"].append(result)
            if not result["complete"]:
                report["reason"] = result["reason"]
                break
        if len(report["directions"]) == 4 and all(r["complete"] for r in report["directions"]):
            report.update(complete=True, withinThreshold=all(r["withinThreshold"] for r in report["directions"]),
                          certifiedSymmetricUpperBoundMetres=max(r["certifiedUpperBoundMetres"] for r in report["directions"]),
                          reason="Every requested triangle surface covered in both directions")
    except (Incomplete, ValueError, ArithmeticError, KeyError, TypeError, IndexError, AttributeError, RecursionError) as error:
        report["reason"] = str(error)
    report.update(thresholdRefuted=any(r["thresholdRefuted"] for r in report["directions"]),
                  measuredSymmetricLowerBoundMetres=max((r["measuredLowerBoundMetres"] for r in report["directions"]), default=0.0),
                  estimatedPeakWorkingBytes=work.peak_reserved, workPerformed=dict(sorted(work.counts.items())))
    return report


def read_package(root, package, receipt_sha, work):
    """Reuse pinned source/LOD/dependency reader, reserving decoding before it runs."""
    root, package = Path(root).resolve(), Path(package).resolve()
    author.require(package.is_relative_to(root), "Surface-error package escapes root")
    # Covers initial bounded receipt parsing, hashing buffers and reader overhead.
    work.reserve(32 * 1024 * 1024)
    receipt_path = author.contained(package, "qualification-receipt.json")
    receipt = author.strict_json(receipt_path, 1024 * 1024, receipt_sha)
    author.require(type(receipt["outputFiles"]) is list and len(receipt["outputFiles"]) <= 64 and
                   type(receipt["source"]["dependencies"]) is list and len(receipt["source"]["dependencies"]) <= 64,
                   "Surface-error dependency count ceiling exceeded")
    author.require(Path(receipt["source"]["file"]).suffix.lower() == ".gltf", "Source must be a glTF JSON file")
    author.require(Path(receipt["manifest"]["file"]).suffix.lower() == ".json", "Manifest must be explicit JSON")
    paths = {receipt_path, author.contained(root, receipt["manifest"]["file"]),
             author.contained(root, receipt["source"]["file"])}
    paths.update(author.contained(package, row["file"]) for row in receipt["outputFiles"])
    paths.update(author.contained(root, row["file"]) for row in receipt["source"]["dependencies"])
    decoded = total = 0
    for path in sorted(paths):
        work.check()
        size = path.stat().st_size
        author.require(0 < size <= work.limits.maximum_file_bytes, "Surface-error input file byte ceiling exceeded")
        total += size
        # Existing reader hashes images in 1MiB blocks and never decodes pixels.
        if path.suffix.lower() not in (".png", ".jpg", ".jpeg"):
            decoded += size
    author.require(len(paths) <= work.limits.maximum_admitted_files and total <= work.limits.maximum_admitted_bytes,
                   "Surface-error aggregate input ceiling exceeded")
    work.reserve(decoded * 16)
    # Extension-based streaming reservations must not allow a malicious sidecar
    # to relabel JSON or a binary vertex buffer as a supposedly hash-only image.
    pins = {row["file"]: row for row in receipt["outputFiles"]}
    author.require(len(pins) == len(receipt["outputFiles"]) and "sidecar.json" in pins,
                   "Invalid surface-error output pins")
    sidecar = author.strict_json(author.contained(package, "sidecar.json"), 16 * 1024 * 1024, pins["sidecar.json"]["sha256"])
    author.require(len(sidecar["definitions"]) == 1 and len(sidecar["definitions"][0]["lods"]) == 2,
                   "Expected one near/far authoring definition")
    gltfs = [(author.contained(root, receipt["source"]["file"]), receipt["source"]["sha256"])]
    for lod in sidecar["definitions"][0]["lods"]:
        name = lod["assetPath"]
        author.require(Path(name).suffix.lower() == ".gltf" and name in pins, "LOD must be pinned glTF JSON")
        gltfs.append((author.contained(package, name), pins[name]["sha256"]))
    for path, digest in gltfs:
        gltf = author.strict_json(path, 16 * 1024 * 1024, digest)
        author.require(len(gltf.get("buffers", [])) <= 64 and len(gltf.get("images", [])) <= 64,
                       "Surface-error glTF dependency count exceeded")
        for row in gltf.get("buffers", []):
            author.require(author.uri_file(path.parent, row["uri"]).suffix.lower() == ".bin",
                           "Decoded buffers must use explicit .bin files")
        for row in gltf.get("images", []):
            author.require(author.uri_file(path.parent, row["uri"]).suffix.lower() in (".png", ".jpg", ".jpeg"),
                           "Hash-only images require explicit image files")
        work.check()
    return quality.read_package(root, package, receipt_sha, work)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--package", type=Path, required=True)
    parser.add_argument("--receipt-sha256", required=True)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--error-metres", default="0.15")
    parser.add_argument("--scale", default="1")
    parser.add_argument("--seconds", type=float, default=55.0)
    args = parser.parse_args()
    destination = args.report.resolve()
    author.require(destination.is_relative_to(args.root.resolve()) and destination.parent.is_dir() and not destination.exists(),
                   "Surface-error report must be a fresh path within project root")
    work = Work(Limits(seconds=args.seconds))
    try:
        threshold, scale = parameter(args.error_metres, "threshold"), parameter(args.scale, "scale", True)
        meshes = read_package(args.root, args.package, args.receipt_sha256, work)
        report = assess(*meshes, work, threshold, scale)
    except (Incomplete, ValueError, ArithmeticError, OSError, KeyError, TypeError, IndexError, AttributeError, RecursionError) as error:
        report = dict(schemaVersion=1, complete=False, withinThreshold=False, assetAccepted=False, reason=str(error),
                      workPerformed=dict(sorted(work.counts.items())), estimatedPeakWorkingBytes=work.peak_reserved)
    report.update(receiptSha256=args.receipt_sha256, toolSha256=author.sha256(Path(__file__)),
                  qualityReaderSha256=author.sha256(Path(quality.__file__)), authoringReaderSha256=author.sha256(Path(author.__file__)))
    author.write_new(destination, author.canonical(report))
    print(author.canonical(dict(complete=report["complete"], withinThreshold=report["withinThreshold"],
                               report=str(destination))).decode().strip())
    return 0 if report["complete"] and report["withinThreshold"] else 2


if __name__ == "__main__":
    raise SystemExit(main())

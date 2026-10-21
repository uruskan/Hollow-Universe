"""Exact/adversarial synthetic surface-bound fixtures; root schedules runs."""
from dataclasses import replace
from fractions import Fraction
import importlib.util
import math
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock

TOOL = Path(__file__).resolve().parents[1] / "cliff_surface_error.py"
spec = importlib.util.spec_from_file_location("cliff_surface_error", TOOL)
error = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = error
spec.loader.exec_module(error)

A = ((0., 0., 0.), (2., 0., 0.), (0., 2., 0.))


def mesh(triangles):
    positions = [p for triangle in triangles for p in triangle]
    return error.quality.Mesh(positions, [(3*i, 3*i+1, 3*i+2) for i in range(len(triangles))], error.Work())


def translated(triangle, z):
    return tuple((x, y, height + z) for x, y, height in triangle)


def directed(source, target, threshold="0.15", scale="1", limits=None):
    work = error.Work(limits)
    return error.directed(error.Surface(mesh(source), work), error.Surface(mesh(target), work), threshold, scale, work)


class ExactDistanceTests(unittest.TestCase):
    def test_closed_triangle_face_edge_and_corner_distance(self):
        work = error.Work()
        self.assertEqual(Fraction(1, 64), error.squared_distance((.5, .5, .125), A, work))
        self.assertEqual(Fraction(1), error.squared_distance((1., -1., 0.), A, work))
        self.assertEqual(Fraction(2), error.squared_distance((-1., -1., 0.), A, work))
        self.assertEqual(Fraction(1, 2), error.squared_distance((1.5, 1.5, 0.), A, work))

    def test_nearest_bvh_matches_exhaustive_exact_distances(self):
        triangles = [tuple((x+3*i, y, z) for x, y, z in A) for i in range(20)]
        work = error.Work()
        surface = error.Surface(mesh(triangles), work)
        for point in ((.25, .75, .125), (20., 2., 0.), (60., -1., 3.)):
            p = tuple(Fraction(v) for v in point)
            expected = min(error.squared_distance(p, surface.points(i), work) for i in range(len(triangles)))
            actual, _ = error.nearest(p, surface, work)
            self.assertEqual(expected, actual)

    def test_sqrt_report_bounds_are_outward_even_at_extreme_scales(self):
        for q in (Fraction(0), Fraction(1, 64), Fraction(2), Fraction(1, 3),
                  Fraction(1, 2**600), Fraction(1, 2**2200), Fraction(10**20)):
            low, high = error.sqrt_interval(q)
            self.assertLessEqual(Fraction(low)**2, q)
            self.assertGreaterEqual(Fraction(high)**2, q)
            self.assertLessEqual(low, high)

    def test_bad_math_library_estimate_never_becomes_a_certificate(self):
        with mock.patch.object(error.math, "sqrt", return_value=0.0):
            with self.assertRaises(error.Incomplete):
                error.sqrt_interval(Fraction(1))

    def test_degenerate_nonfinite_and_boolean_meshes_refuse(self):
        for triangle in (((0., 0., 0.), (1., 0., 0.), (2., 0., 0.)),
                         ((math.nan, 0., 0.), A[1], A[2]),
                         ((math.inf, 0., 0.), A[1], A[2]),
                         ((True, 0., 0.), A[1], A[2])):
            with self.assertRaises(error.author.QualificationError):
                mesh([triangle])


class WholeSurfaceTests(unittest.TestCase):
    def test_identical_reversed_triangle_surface_certifies_exact_zero(self):
        result = directed([A], [tuple(reversed(A))], threshold=0)
        self.assertTrue(result["complete"])
        self.assertTrue(result["withinThreshold"])
        self.assertEqual(0., result["certifiedUpperBoundMetres"])
        self.assertEqual(1, result["processedCells"])

    def test_contained_coplanar_triangle_uses_convex_bound_without_subdivision(self):
        inner = ((.25, .25, 0.), (.75, .25, 0.), (.25, .75, 0.))
        result = directed([inner], [A], threshold=0)
        self.assertTrue(result["withinThreshold"])
        self.assertEqual(0., result["certifiedUpperBoundMetres"])
        self.assertEqual(0, result["deepestSubdivision"])

    def test_parallel_translation_has_exact_surface_distance(self):
        result = directed([A], [translated(A, .125)], threshold="0.125")
        self.assertTrue(result["complete"])
        self.assertTrue(result["withinThreshold"])
        self.assertEqual(.125, result["measuredLowerBoundMetres"])
        self.assertEqual(.125, result["certifiedUpperBoundMetres"])

    def test_one_binary64_step_above_threshold_cannot_pass(self):
        offset = math.nextafter(.125, math.inf)
        result = directed([A], [translated(A, offset)], threshold="0.125")
        self.assertTrue(result["complete"])
        self.assertFalse(result["withinThreshold"])
        self.assertTrue(result["thresholdRefuted"])
        self.assertEqual(offset, result["measuredLowerBoundMetres"])

    def test_one_binary64_step_below_threshold_can_pass(self):
        offset = math.nextafter(.125, 0.)
        result = directed([A], [translated(A, offset)], threshold="0.125")
        self.assertTrue(result["withinThreshold"])
        self.assertLessEqual(result["certifiedUpperBoundMetres"], .125)

    def test_decimal_threshold_is_not_silently_changed_to_float(self):
        # Binary64 .1 is greater than exact decimal 1/10.
        result = directed([A], [translated(A, .1)], threshold="0.1")
        self.assertTrue(result["thresholdRefuted"])
        self.assertFalse(result["withinThreshold"])

    def test_uniform_scale_applies_to_bound_and_threshold(self):
        result = directed([A], [translated(A, .125)], threshold="0.2", scale="2")
        self.assertTrue(result["complete"])
        self.assertFalse(result["withinThreshold"])
        self.assertEqual(.25, result["certifiedUpperBoundMetres"])
        self.assertEqual(.25, result["measuredLowerBoundMetres"])

    def test_retriangulated_coplanar_square_certifies_zero_with_exact_partition(self):
        a, b, c, d = ((0., 0., 0.), (2., 0., 0.), (2., 2., 0.), (0., 2., 0.))
        result = directed([(a, b, c), (a, c, d)], [(a, b, d), (b, c, d)], threshold=0)
        self.assertTrue(result["complete"])
        self.assertTrue(result["withinThreshold"])
        self.assertGreater(result["deepestSubdivision"], 0)
        self.assertEqual(0., result["certifiedUpperBoundMetres"])

    def test_corner_and_center_union_samples_do_not_prove_interior_coverage(self):
        # Every old corner/centroid probe is covered by one tiny target island;
        # the source triangle interior between those islands is not covered.
        islands = [((0., 0., 0.), (.02, 0., 0.), (0., .02, 0.)),
                   ((2., 0., 0.), (1.98, 0., 0.), (1.98, .02, 0.)),
                   ((0., 2., 0.), (0., 1.98, 0.), (.02, 1.98, 0.)),
                   ((.6, .6, 0.), (.8, .6, 0.), (.6, .8, 0.))]
        result = directed([A], islands, threshold="0.1")
        self.assertTrue(result["complete"])
        self.assertFalse(result["withinThreshold"])
        self.assertTrue(result["thresholdRefuted"])
        self.assertGreater(result["deepestSubdivision"], 0)
        self.assertGreater(result["measuredLowerBoundMetres"], .1)

    def test_tiny_source_feature_cannot_be_skipped_by_face_sampling(self):
        feature = ((.9, .9, 0.), (.90001, .9, 0.), (.9, .90001, 1.))
        result = directed([A, feature], [A], threshold="0.15")
        self.assertTrue(result["complete"])
        self.assertTrue(result["thresholdRefuted"])
        self.assertEqual(1, result["witness"]["sourceTriangle"])
        self.assertEqual(1., result["measuredLowerBoundMetres"])

    def test_symmetric_check_detects_target_only_extra_surface(self):
        near = mesh([A])
        physical = mesh([A, translated(A, 1.)])
        report = error.assess(near, physical, physical)
        self.assertTrue(report["complete"])
        self.assertFalse(report["withinThreshold"])
        self.assertTrue(report["directions"][0]["withinThreshold"])
        self.assertFalse(report["directions"][1]["withinThreshold"])
        self.assertFalse(report["assetAccepted"])

    def test_far_only_difference_is_not_reused_as_physical(self):
        near = mesh([A])
        report = error.assess(near, near, mesh([translated(A, .5)]))
        self.assertTrue(report["complete"])
        self.assertFalse(report["farPhysicalExactOrderedTriangleReuse"])
        self.assertFalse(report["withinThreshold"])
        self.assertTrue(report["directions"][0]["withinThreshold"])
        self.assertFalse(report["directions"][2]["withinThreshold"])

    def test_report_is_deterministic_without_wall_time_fields(self):
        near, far = mesh([A]), mesh([translated(A, .125)])
        a = error.assess(near, far, far)
        b = error.assess(near, far, far)
        self.assertEqual(error.author.canonical(a), error.author.canonical(b))


class ExhaustionAndAdmissionTests(unittest.TestCase):
    def test_cell_budget_exhaustion_has_no_partial_upper_bound(self):
        result = directed([A], [A], limits=replace(error.Limits(), maximum_cells=0))
        self.assertFalse(result["complete"])
        self.assertFalse(result["withinThreshold"])
        self.assertIsNone(result["certifiedUpperBoundMetres"])

    def test_exact_distance_and_query_heap_exhaustion_refuse(self):
        for limits in (replace(error.Limits(), maximum_distance_tests=0), replace(error.Limits(), maximum_heap_entries=0)):
            result = directed([A], [translated(A, .125)], limits=limits)
            self.assertFalse(result["complete"])
            self.assertFalse(result["withinThreshold"])
            self.assertIsNone(result["certifiedUpperBoundMetres"])

    def test_depth_exhaustion_does_not_forgive_unresolved_surface(self):
        a, b, c, d = ((0., 0., 0.), (2., 0., 0.), (2., 2., 0.), (0., 2., 0.))
        result = directed([(a, b, c)], [(a, b, d), (b, c, d)], threshold=0,
                          limits=replace(error.Limits(), maximum_subdivision_depth=0))
        self.assertFalse(result["complete"])
        self.assertFalse(result["withinThreshold"])

    def test_pending_cell_cap_refuses_before_subdivision_allocation(self):
        a, b, c, d = ((0., 0., 0.), (2., 0., 0.), (2., 2., 0.), (0., 2., 0.))
        result = directed([(a, b, c)], [(a, b, d), (b, c, d)], threshold=0,
                          limits=replace(error.Limits(), maximum_pending_cells=1))
        self.assertFalse(result["complete"])
        self.assertIn("Pending", result["reason"])

    def test_storage_and_deadline_exhaustion_fail_closed(self):
        near = mesh([A])
        low_memory = error.assess(near, near, near, error.Work(replace(error.Limits(), maximum_estimated_bytes=1)))
        self.assertFalse(low_memory["complete"])
        self.assertFalse(low_memory["withinThreshold"])
        work = error.Work()
        work.deadline = 0
        timeout = error.assess(near, near, near, work)
        self.assertFalse(timeout["complete"])
        self.assertFalse(timeout["withinThreshold"])

    def test_node_and_rational_budgets_fail_closed(self):
        near, far = mesh([A]), mesh([translated(A, .125)])
        for limits in (replace(error.Limits(), maximum_node_visits=0),
                       replace(error.Limits(), maximum_fraction_bits=1)):
            report = error.assess(near, far, far, error.Work(limits))
            self.assertFalse(report["complete"])
            self.assertFalse(report["withinThreshold"])

    def test_all_directions_share_the_cell_budget(self):
        near, far = mesh([A]), mesh([translated(A, .125)])
        report = error.assess(near, far, far, error.Work(replace(error.Limits(), maximum_cells=1)))
        self.assertFalse(report["complete"])
        self.assertFalse(report["withinThreshold"])
        self.assertTrue(report["directions"][0]["complete"])
        self.assertFalse(report["directions"][1]["complete"])

    def test_parameters_are_bounded_before_large_exact_allocations(self):
        for value in (True, math.inf, math.nan, "1e9999999999999999999", "1/2", -1):
            with self.assertRaises((ValueError, error.author.QualificationError)):
                error.parameter(value, "test")
        with self.assertRaises(error.author.QualificationError):
            error.parameter(0, "scale", True)

    def test_receipt_hash_failure_precedes_geometry_reader(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / "qualification-receipt.json").write_text("{}", encoding="utf-8")
            with mock.patch.object(error.quality, "read_package") as decoder:
                with self.assertRaises(error.author.QualificationError):
                    error.read_package(root, root, "0" * 64, error.Work())
                decoder.assert_not_called()

    def test_package_escape_refuses_before_read(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder) / "root"
            root.mkdir()
            with mock.patch.object(error.author, "strict_json") as reader:
                with self.assertRaises(error.author.QualificationError):
                    error.read_package(root, Path(folder), "0" * 64, error.Work())
                reader.assert_not_called()


if __name__ == "__main__":
    unittest.main()

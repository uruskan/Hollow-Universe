"""Synthetic quality-gate tests. Root runs these; no live assets are required."""
import importlib.util
import copy
import itertools
import math
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock

TOOL = Path(__file__).resolve().parents[1]/"cliff_mesh_quality.py"
spec = importlib.util.spec_from_file_location("cliff_mesh_quality", TOOL)
quality = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = quality
spec.loader.exec_module(quality)

A = ((0., 0., 0.), (2., 0., 0.), (0., 2., 0.))


def mesh_of(triangles, work):
    positions = [p for triangle in triangles for p in triangle]
    return quality.Mesh(positions, [(3*i, 3*i+1, 3*i+2) for i in range(len(triangles))], work)


def tetrahedron(x=10):
    p = ((x, 0., 0.), (x+1., 0., 0.), (x, 1., 0.), (x, 0., 1.))
    return [tuple(p[v] for v in face) for face in ((0, 2, 1), (0, 1, 3), (0, 3, 2), (1, 2, 3))]


def physical_record():
    return dict(vertices=[list(p) for p in A], triangles=[[0, 1, 2, 0]],
                patches=[dict(key="rock", substrateProfileKey="bare", capabilities=[])])


class ExactTriangleTests(unittest.TestCase):
    def test_nonparallel_interior_crossing(self):
        b = ((.5, .5, -1.), (.5, .5, 1.), (1.5, .5, 0.))
        hit = quality.classify_triangles(A, b)
        self.assertEqual("interior-crossing", hit.kind)
        self.assertTrue(hit.interior_on_a and hit.interior_on_b)

    def test_exact_tiny_parallel_gap_is_not_rounded_to_contact(self):
        for gap in (2.**-80, 2.**-200):
            b = tuple((x, y, gap) for x, y, _ in A)
            self.assertEqual("disjoint", quality.classify_triangles(A, b).kind)

    def test_shared_nonparallel_edge_is_allowed(self):
        b = ((2., 0., 0.), (0., 0., 0.), (1., 0., 1.))
        self.assertEqual("shared-edge", quality.classify_triangles(A, b).kind)

    def test_shared_coplanar_edge_opposite_sides_is_allowed(self):
        b = ((2., 0., 0.), (0., 0., 0.), (1., -1., 0.))
        self.assertEqual("shared-edge", quality.classify_triangles(A, b).kind)

    def test_shared_edge_never_exempts_positive_coplanar_overlap(self):
        b = ((0., 0., 0.), (2., 0., 0.), (1., 1., 0.))
        for other in (b, tuple(reversed(b))):
            self.assertEqual("coplanar-overlap", quality.classify_triangles(A, other).kind)

    def test_shared_vertex_never_exempts_area_overlap(self):
        b = ((0., 0., 0.), (1.5, .25, 0.), (.25, 1.5, 0.))
        self.assertEqual("coplanar-overlap", quality.classify_triangles(A, b).kind)

    def test_vertex_only_contact_is_allowed(self):
        for b in (((0., 0., 0.), (-1., 0., 0.), (0., -1., 1.)),
                  ((0., 0., 0.), (-1., 0., 0.), (0., -1., 0.))):
            self.assertEqual("shared-vertex", quality.classify_triangles(A, b).kind)

    def test_t_junction_contact_is_not_a_shared_corner(self):
        b = ((1., 0., 0.), (2., -1., 0.), (0., -1., 0.))
        self.assertEqual("nonshared-contact", quality.classify_triangles(A, b).kind)

    def test_coplanar_edge_crossing_without_contained_vertices(self):
        a = ((0., 0., 0.), (2., 0., 0.), (1., 2., 0.))
        b = ((0., 1., 0.), (2., 1., 0.), (1., -1., 0.))
        self.assertEqual("coplanar-overlap", quality.classify_triangles(a, b).kind)

    def test_duplicate_geometry_is_rejected_in_either_winding(self):
        for b in (A, tuple(reversed(A))):
            self.assertEqual("duplicate-triangle", quality.classify_triangles(A, b).kind)

    def test_one_ulp_coplanar_overlap_is_not_rounded_away(self):
        x = math.nextafter(2., 0.)
        b = ((x, 0., 0.), (3., 0., 0.), (x, 1., 0.))
        self.assertEqual("coplanar-overlap", quality.classify_triangles(A, b).kind)

    def test_degenerate_input_is_not_clear(self):
        with self.assertRaises(quality.author.QualificationError):
            quality.classify_triangles(A, ((0., 0., 0.), (1., 0., 0.), (2., 0., 0.)))

    def test_exact_arithmetic_budget_failure_is_explicit(self):
        with self.assertRaises(quality.Incomplete):
            quality.classify_triangles(A, tuple((x, y, 2.**-300) for x, y, _ in A))
        b = ((.5, .5, -1.), (.5, .5, 1.), (1.5, .5, 0.))
        with self.assertRaises(quality.Incomplete):
            quality.classify_triangles(A, b, quality.Limits(maximum_exact_operations=0))


class BvhAndContinuityTests(unittest.TestCase):
    def test_optional_exact_classifier_preserves_every_candidate_and_report(self):
        triangles = [A,((.5,.5,-1.),(.5,.5,1.),(1.5,.5,0.)),
                     ((2.,0.,0.),(0.,0.,0.),(1.,-1.,0.))]+tetrahedron()
        first_work,second_work = quality.Work(),quality.Work()
        mesh = mesh_of(triangles,first_work)
        expected = quality.intersections(mesh,first_work)
        seen = []
        def classifier(current,a,b,work):
            self.assertIs(mesh,current)
            seen.append((a,b))
            return quality.classify_exact(current.points(a,True),current.points(b,True),work)
        actual = quality.intersections(mesh,second_work,classifier)
        self.assertEqual(expected,actual)
        self.assertEqual(list(quality.candidate_pairs(mesh,quality.Work())),seen)
        self.assertEqual(first_work.counts["pair_tests"],second_work.counts["pair_tests"])
        self.assertEqual(first_work.counts["exact_operations"],second_work.counts["exact_operations"])

    def test_bvh_matches_exhaustive_exact_classification_without_duplicate_pairs(self):
        triangles = [tuple((x+4*i, y, z) for x, y, z in A) for i in range(10)]
        triangles += [((.5, .5, -1.), (.5, .5, 1.), (1.5, .5, 0.)), A]
        work = quality.Work()
        mesh = mesh_of(triangles, work)
        pairs = list(quality.candidate_pairs(mesh, work))
        self.assertEqual(len(pairs), len(set(pairs)))
        actual = {}
        for a, b in pairs:
            hit = quality.classify_exact(mesh.points(a, True), mesh.points(b, True), work)
            if hit.kind != "disjoint": actual[(a, b)] = hit.kind
        expected = {}
        for a, b in itertools.combinations(range(len(triangles)), 2):
            hit = quality.classify_exact(mesh.points(a, True), mesh.points(b, True), work)
            if hit.kind != "disjoint": expected[(a, b)] = hit.kind
        self.assertEqual(expected, actual)

    def test_coincident_fan_indices_do_not_create_geometric_cracks(self):
        work = quality.Work()
        source = quality.Mesh([(0., 0., 0.), (1., 0., 0.), (1., 1., 0.), (0., 1., 0.)],
                              [(0, 1, 2), (0, 2, 3)], work)
        candidate = mesh_of([source.points(0), source.points(1)], work)
        result = quality.continuity(source, candidate)
        self.assertTrue(result["clear"])
        self.assertEqual(2, result["candidate"]["indexOnlyOpenEdges"])
        self.assertTrue(quality.intersections(candidate, work)["clear"])

    def test_missing_face_and_wrong_winding_are_reported(self):
        work = quality.Work()
        source = quality.Mesh([(0., 0., 0.), (1., 0., 0.), (1., 1., 0.), (0., 1., 0.)],
                              [(0, 1, 2), (0, 2, 3)], work)
        missing = mesh_of([source.points(0)], work)
        reversed_face = mesh_of([source.points(0), tuple(reversed(source.points(1)))], work)
        self.assertFalse(quality.continuity(source, missing)["clear"])
        self.assertGreater(quality.continuity(source, missing)["newBoundaryCount"], 0)
        self.assertFalse(quality.continuity(source, reversed_face)["clear"])
        self.assertEqual(1, quality.continuity(source, reversed_face)["candidate"]["inconsistentWindingEdges"])

    def test_vertices_without_edge_faces_are_reported(self):
        work = quality.Work()
        mesh = quality.Mesh(list(A)+[(99., 99., 99.)], [(0, 1, 2)], work)
        self.assertEqual([3], quality.topology(mesh)["verticesWithoutEdgeFaces"])

    def test_work_exhaustion_never_returns_a_clear_assessment(self):
        work = quality.Work(quality.Limits(maximum_pair_tests=0))
        mesh = mesh_of([A, A], work)
        report = quality.assess(mesh, mesh, mesh, work)
        self.assertFalse(report["complete"])
        self.assertFalse(report["testedCandidateInvariantsClear"])
        self.assertFalse(report["assetAccepted"])

    def test_witness_order_and_exact_positions_are_deterministic(self):
        results = []
        for _ in range(2):
            work = quality.Work()
            mesh = mesh_of([A, A, ((.5, .5, -1.), (.5, .5, 1.), (1.5, .5, 0.))], work)
            results.append(quality.author.canonical(quality.intersections(mesh, work)))
        self.assertEqual(results[0], results[1])

    def test_far_reuse_requires_all_ordered_corners_not_only_protected_faces(self):
        work = quality.Work()
        a = mesh_of([A], work)
        b = mesh_of([tuple(reversed(A))], work)
        self.assertFalse(quality.same_triangle_positions(a, b))

    def test_closed_component_addition_and_deletion_fail_with_unchanged_open_boundaries(self):
        work = quality.Work()
        bare = mesh_of([A], work)
        extra = mesh_of([A]+tetrahedron(), work)
        for source, candidate in ((bare, extra), (extra, bare)):
            report = quality.continuity(source, candidate)
            self.assertFalse(report["clear"])
            self.assertEqual(0, report["newBoundaryCount"])
            self.assertEqual(0, report["missingSourceBoundaryCount"])
            self.assertFalse(report["componentCorrespondence"]["equalComponentCounts"])

    def test_component_counts_and_euler_values_do_not_prove_closed_correspondence(self):
        work = quality.Work()
        source = mesh_of([A]+tetrahedron(10), work)
        candidate = mesh_of([A]+tetrahedron(20), work)
        report = quality.continuity(source, candidate)
        self.assertFalse(report["clear"])
        self.assertTrue(report["componentCorrespondence"]["equalComponentCounts"])
        self.assertEqual(report["source"]["componentEulerSum"], report["candidate"]["componentEulerSum"])
        self.assertTrue(any("Closed component" in issue for row in report["componentCorrespondence"]["mapping"] for issue in row["issues"]))

    def test_source_face_labels_map_reordered_closed_and_open_components(self):
        work = quality.Work()
        source = mesh_of([A]+tetrahedron(), work)
        candidate = mesh_of(tetrahedron()+[A], work)
        quality.bind_source_ids(candidate, [1, 2, 3, 4, 0], 5)
        report = quality.continuity(source, candidate)
        self.assertTrue(report["clear"])
        self.assertEqual([1, 0], [row["sourceComponent"] for row in report["componentCorrespondence"]["mapping"]])

    def test_swapped_closed_component_labels_fail_source_endpoint_membership(self):
        work = quality.Work()
        source = mesh_of(tetrahedron(10)+tetrahedron(20), work)
        candidate = mesh_of(tetrahedron(10)+tetrahedron(20), work)
        quality.bind_source_ids(candidate, [4, 5, 6, 7, 0, 1, 2, 3], 8)
        report = quality.continuity(source, candidate)
        self.assertFalse(report["clear"])
        self.assertTrue(any("endpoints" in issue for row in report["componentCorrespondence"]["mapping"] for issue in row["issues"]))

    def test_directed_boundaries_must_belong_to_the_mapped_source_component(self):
        work = quality.Work()
        triangles = [A, tuple((x+10, y, z) for x, y, z in A)]
        source, candidate = mesh_of(triangles, work), mesh_of(triangles, work)
        quality.bind_source_ids(candidate, [1, 0], 2)
        report = quality.continuity(source, candidate)
        self.assertFalse(report["clear"])
        self.assertEqual(0, report["newBoundaryCount"])
        self.assertTrue(any("ownership" in issue for row in report["componentCorrespondence"]["mapping"] for issue in row["issues"]))

    def test_per_component_euler_change_is_reported_even_with_same_component_count(self):
        work = quality.Work()
        p = ((1., 0., 0.), (-1., 0., 0.), (0., 1., 0.), (0., -1., 0.), (0., 0., 1.), (0., 0., -1.))
        source_faces = ((0, 2, 4), (2, 1, 4), (1, 3, 4), (3, 0, 4), (2, 0, 5), (1, 2, 5), (3, 1, 5), (0, 3, 5))
        joined_tetrahedra = ((0, 2, 1), (0, 1, 4), (0, 4, 2), (1, 2, 4),
                            (0, 3, 1), (0, 1, 5), (0, 5, 3), (1, 3, 5))
        source = quality.Mesh(p, source_faces, work)
        candidate = quality.Mesh(p, joined_tetrahedra, work)
        quality.bind_source_ids(candidate, list(range(8)), 8)
        report = quality.continuity(source, candidate)
        self.assertFalse(report["clear"])
        self.assertTrue(report["componentCorrespondence"]["equalComponentCounts"])
        self.assertEqual(2, report["source"]["componentEulerSum"])
        self.assertEqual(3, report["candidate"]["componentEulerSum"])
        self.assertIn("Mapped component Euler characteristic changed", report["componentCorrespondence"]["mapping"][0]["issues"])

    def test_vertex_contact_does_not_merge_edge_connected_components(self):
        work = quality.Work()
        b = ((0., 0., 0.), (-1., 0., 0.), (0., -1., 0.))
        source = mesh_of([A, b], work)
        report = quality.topology(source)
        self.assertEqual(2, report["edgeConnectedComponents"])
        self.assertEqual([1, 1], [row["eulerCharacteristic"] for row in report["components"]])

    def test_provenance_ids_must_be_unique_typed_in_range_and_complete(self):
        mesh = mesh_of([A, A], quality.Work())
        for ids in ([0], [0, 0], [0, True], [0, "1"], [0, 2], [0, -1]):
            with self.subTest(ids=ids), self.assertRaises(quality.author.QualificationError):
                quality.bind_source_ids(mesh, ids, 2)

    def test_topology_work_exhaustion_is_incomplete(self):
        work = quality.Work(quality.Limits(maximum_topology_steps=0))
        mesh = mesh_of([A], work)
        report = quality.assess(mesh, mesh, mesh, work)
        self.assertFalse(report["complete"])
        self.assertFalse(report["testedAllLodInvariantsClear"])

    def test_source_near_intersections_are_included_in_the_all_lod_flag(self):
        work = quality.Work()
        mesh = mesh_of([A], work)
        # Isolate aggregation: the source/near result can fail while physical/far succeeds.
        with mock.patch.object(quality, "intersections", side_effect=[dict(complete=True, clear=True), dict(complete=True, clear=False)]):
            report = quality.assess(mesh, mesh, mesh, work)
        self.assertTrue(report["complete"])
        self.assertTrue(report["testedCandidateInvariantsClear"])
        self.assertFalse(report["allLodIntersectionsClear"])
        self.assertFalse(report["testedAllLodInvariantsClear"])


class PhysicalShapeTests(unittest.TestCase):
    def test_valid_physical_shape_preserves_geometry(self):
        mesh = quality.read_physical(physical_record(), quality.Work())
        self.assertEqual(A, mesh.points(0))

    def test_embedded_physical_record_does_not_use_python_bool_number_equality(self):
        original = physical_record()
        same = copy.deepcopy(original)
        self.assertTrue(quality.typed_json_equal(original, same))
        same["triangles"][0][0] = False
        self.assertEqual(original, same)  # Demonstrates the previous false binding.
        self.assertFalse(quality.typed_json_equal(original, same))

    def test_physical_coordinates_do_not_coerce_strings_or_booleans(self):
        for value in ("0", False, True, None, float("inf"), float("nan")):
            record = physical_record()
            record["vertices"][0][0] = value
            with self.subTest(value=value), self.assertRaises(quality.author.QualificationError):
                quality.read_physical(record, quality.Work())

    def test_physical_triangle_requires_exactly_four_integer_fields(self):
        for triangle in ([0, 1, 2], [0, 1, 2, 0, 7], [0., 1, 2, 0], [False, 1, 2, 0], [0, 1, 2, False], [0, 1, 2, "0"]):
            record = physical_record()
            record["triangles"] = [triangle]
            with self.subTest(triangle=triangle), self.assertRaises(quality.author.QualificationError):
                quality.read_physical(record, quality.Work())

    def test_physical_indices_and_patch_ordinals_are_bounded(self):
        for triangle in ([0, 1, 3, 0], [-1, 1, 2, 0], [0, 1, 2, -1], [0, 1, 2, 1]):
            record = physical_record()
            record["triangles"] = [triangle]
            with self.subTest(triangle=triangle), self.assertRaises(quality.author.QualificationError):
                quality.read_physical(record, quality.Work())

    def test_physical_patches_require_typed_unique_used_records(self):
        records = []
        duplicate = physical_record()
        duplicate["patches"].append(copy.deepcopy(duplicate["patches"][0]))
        records.append(duplicate)
        unused = physical_record()
        unused["patches"].append(dict(key="unused", substrateProfileKey="bare", capabilities=[]))
        records.append(unused)
        malformed = physical_record()
        malformed["patches"][0]["capabilities"] = "support"
        records.append(malformed)
        extra = physical_record()
        extra["unexpected"] = 1
        records.append(extra)
        for record in records:
            with self.subTest(record=record), self.assertRaises(quality.author.QualificationError):
                quality.read_physical(record, quality.Work())


class MeasurementAndAdmissionTests(unittest.TestCase):
    def test_sampled_parallel_offset_has_explicit_nonacceptance_labels(self):
        work = quality.Work()
        source = mesh_of([A], work)
        target = mesh_of([tuple((x, y, z+1) for x, y, z in A)], work)
        report = quality.sampled_distances(source, target, work, faces=1)
        self.assertEqual(4, report["sampleCount"])
        self.assertAlmostEqual(1, report["maximumSampledDistanceMetres"])
        self.assertFalse(report["acceptanceEvidence"])
        self.assertFalse(report["hausdorffBound"])

    def test_sample_query_budget_is_not_silently_truncated(self):
        work = quality.Work(quality.Limits(maximum_distance_tests=0))
        source = mesh_of([A], work)
        with self.assertRaises(quality.Incomplete):
            quality.sampled_distances(source, source, work, faces=1)

    def test_empty_sample_request_is_rejected(self):
        work = quality.Work()
        source = mesh_of([A], work)
        with self.assertRaisesRegex(quality.author.QualificationError, "must be positive"):
            quality.sampled_distances(source, source, work, faces=0)

    def test_receipt_must_match_the_independent_caller_pin(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            package = root/"package"
            package.mkdir()
            (package/"qualification-receipt.json").write_bytes(b"{}")
            with self.assertRaises(quality.author.QualificationError):
                quality.read_package(root, package, "0"*64, quality.Work())

    def test_even_nondecoded_output_images_are_authenticated(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            package = root/"package"
            package.mkdir()
            (package/"rock.png").write_bytes(b"bad!")
            receipt = package/"qualification-receipt.json"
            receipt.write_bytes(quality.author.canonical(dict(schemaVersion=1,
                outputFiles=[dict(file="rock.png", bytes=4, sha256="0"*64)])))
            with self.assertRaisesRegex(quality.author.QualificationError, "digest mismatch"):
                quality.read_package(root, package, quality.author.sha256(receipt), quality.Work())

    def test_source_dependencies_share_the_aggregate_input_file_budget(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            package = root/"package"
            package.mkdir()
            (root/"source.gltf").write_bytes(b"{}")
            (root/"image.png").write_bytes(b"source-image")
            source = dict(file="source.gltf", sha256=quality.author.sha256(root/"source.gltf"),
                          dependencies=[dict(file="image.png", sha256=quality.author.sha256(root/"image.png"))])
            manifest = root/"manifest.json"
            manifest.write_bytes(quality.author.canonical(dict(assets=[dict(key="test", outputs=[source])])))
            receipt = package/"qualification-receipt.json"
            receipt.write_bytes(quality.author.canonical(dict(schemaVersion=1, outputFiles=[], source=source,
                manifest=dict(file="manifest.json", sha256=quality.author.sha256(manifest)))))
            work = quality.Work(quality.Limits(maximum_admitted_files=3))
            with self.assertRaisesRegex(quality.Incomplete, "admitted_files budget"):
                quality.read_package(root, package, quality.author.sha256(receipt), work)

    def test_output_and_receipt_share_the_aggregate_byte_budget(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            package = root/"package"
            package.mkdir()
            image = package/"image.png"
            image.write_bytes(b"image")
            receipt = package/"qualification-receipt.json"
            receipt.write_bytes(quality.author.canonical(dict(schemaVersion=1,
                outputFiles=[dict(file="image.png", bytes=5, sha256=quality.author.sha256(image))])))
            work = quality.Work(quality.Limits(maximum_admitted_bytes=receipt.stat().st_size+4))
            with self.assertRaisesRegex(quality.Incomplete, "admitted_bytes budget"):
                quality.read_package(root, package, quality.author.sha256(receipt), work)

    def test_invalid_authenticated_json_produces_an_incomplete_cli_report(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            package = root/"package"
            package.mkdir()
            receipt = package/"qualification-receipt.json"
            receipt.write_bytes(b"{invalid-json}")
            report = root/"report.json"
            args = [str(TOOL), "--root", str(root), "--package", str(package),
                    "--receipt-sha256", quality.author.sha256(receipt), "--report", str(report)]
            with mock.patch.object(sys, "argv", args), mock.patch("builtins.print"):
                self.assertEqual(2, quality.main())
            result = quality.author.strict_json(report)
            self.assertFalse(result["complete"])
            self.assertFalse(result["testedCandidateInvariantsClear"])


if __name__ == "__main__":
    unittest.main()

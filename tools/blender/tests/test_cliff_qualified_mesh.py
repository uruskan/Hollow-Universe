"""Synthetic authoring invariants. No Blender, live assets, or game imports.

Root runs: python -m unittest discover -s tools/blender/tests -p test_cliff_qualified_mesh.py
These tests have not been run by the authoring subagent; root serializes checks.
"""
import importlib.util
import json
from pathlib import Path
import struct
import sys
import tempfile
import unittest

TOOL = Path(__file__).resolve().parents[1] / "cliff_qualified_mesh.py"
spec = importlib.util.spec_from_file_location("cliff_qualified_mesh", TOOL)
cliff = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = cliff
spec.loader.exec_module(cliff)


def grid(size=5):
    positions = [(float(x), float(y), 0.0) for y in range(size) for x in range(size)]
    uvs = [(x/(size-1), y/(size-1)) for y in range(size) for x in range(size)]
    triangles = []
    for y in range(size-1):
        for x in range(size-1):
            a = y*size+x
            triangles.extend(((a, a+1, a+size+1), (a, a+size+1, a+size)))
    return cliff.Mesh.from_arrays(positions, [(0.0, 0.0, 1.0)]*len(positions), uvs, triangles)


def selected_attributes(mesh, corners):
    return tuple(mesh.attributes[c] for c in corners)


class ConstrainedMeshTests(unittest.TestCase):
    def test_locked_face_positions_uvs_normals_and_winding_survive(self):
        mesh = grid()
        protected = {12, 13}
        engine = cliff.ConstrainedQem(mesh, protected)
        remaining = engine.simplify(26)
        self.assertLessEqual(len(remaining), 26)
        for f in protected:
            self.assertIn(f, remaining)
            self.assertEqual(mesh.triangles[f], engine.faces[f])
            self.assertEqual(selected_attributes(mesh, mesh.corners[f]),
                             selected_attributes(mesh, engine.corners[f]))
        geometry, _, _, _ = cliff.physical_geometry(mesh, engine, remaining, protected)
        for f in protected:
            p = remaining.index(f)
            self.assertEqual([mesh.positions[v] for v in mesh.triangles[f]],
                             [geometry["vertices"][v] for v in geometry["triangles"][p][:3]])
            self.assertEqual("preserved-crown", geometry["patches"][geometry["triangles"][p][3]]["key"])
        self.assertTrue(all(not p["capabilities"] for p in geometry["patches"]))

    def test_source_boundary_edges_are_not_removed_or_moved(self):
        mesh = grid()
        engine = cliff.ConstrainedQem(mesh, set())
        original_edges = set(engine.source_edges())
        engine.simplify(24)
        actual = {e for e, fs in engine.edges.items() if len(fs) == 1}
        self.assertEqual(original_edges, actual)
        self.assertEqual(mesh.positions, engine.mesh.positions)

    def test_uv_seam_and_normal_seam_vertices_are_frozen(self):
        mesh = grid()
        positions = [a[0] for a in mesh.attributes]
        normals = [a[1] for a in mesh.attributes]
        uvs = [a[2] for a in mesh.attributes]
        faces = [list(t) for t in mesh.corners]
        for source, uv_delta, normal in ((12, .25, (0, 0, 1)), (13, 0, (0, 1, 0))):
            duplicate = len(positions)
            positions.append(positions[source]); normals.append(normal)
            uvs.append((uvs[source][0]+uv_delta, uvs[source][1]))
            f = next(t for t in faces if source in t)
            f[f.index(source)] = duplicate
        seam_mesh = cliff.Mesh.from_arrays(positions, normals, uvs, faces)
        engine = cliff.ConstrainedQem(seam_mesh, set())
        self.assertEqual({12, 13}, seam_mesh.seams)
        for seam in seam_mesh.seams:
            for neighbour in engine.neighbours(seam):
                self.assertEqual("locked", engine.proposal(seam, neighbour)[1])
                self.assertEqual("locked", engine.proposal(neighbour, seam)[1])

    def test_link_duplicate_guard_rejects_tetrahedron_collapse(self):
        p = [(0., 0., 0.), (1., 0., 0.), (0., 1., 0.), (0., 0., 1.)]
        t = [(0, 2, 1), (0, 1, 3), (1, 2, 3), (2, 0, 3)]
        mesh = cliff.Mesh.from_arrays(p, [(0., 0., 1.)]*4, [(0., 0.)]*4, t)
        engine = cliff.ConstrainedQem(mesh, set())
        for a, b in sorted(engine.edges):
            self.assertIsNone(engine.proposal(a, b)[0])
        with self.assertRaises(cliff.BudgetError):
            engine.simplify(2)

    def test_nonmanifold_and_inconsistent_winding_sources_fail(self):
        p = [(0., 0., 0.), (1., 0., 0.), (0., 1., 0.), (0., -1., 0.), (0., 0., 1.)]
        for t in ([(0, 1, 2), (1, 0, 3), (0, 1, 4)], [(0, 1, 2), (0, 1, 3)]):
            with self.assertRaises(cliff.QualificationError):
                cliff.Mesh.from_arrays(p, [(0., 0., 1.)]*5, [(0., 0.)]*5, t)

    def test_winding_and_uv_orientation_survive_all_collapses(self):
        mesh = grid(6)
        engine = cliff.ConstrainedQem(mesh, set())
        remaining = engine.simplify(32)
        cliff.validate_topology(mesh.positions, [engine.faces[f] for f in remaining])
        for f in remaining:
            normal = cliff.face_normal(mesh.positions, engine.faces[f])
            self.assertGreater(cliff.dot(normal, engine.original_normals[f]), 0)
            self.assertGreater(cliff.uv_area(mesh.attributes, engine.corners[f]) *
                               cliff.uv_area(mesh.attributes, mesh.corners[f]), 0)

    def test_inverting_endpoint_is_rejected(self):
        original = grid(3)
        positions = [a[0] for a in original.attributes]
        # A folded source fan is legal indexed topology. Collapsing its centre
        # onto vertex 0 would reverse the surviving (1,5,4) face.
        positions[4] = (1.9, .1, 0.)
        mesh = cliff.Mesh.from_arrays(positions, [a[1] for a in original.attributes],
                                      [a[2] for a in original.attributes], original.corners)
        proposal, reason = cliff.ConstrainedQem(mesh, set()).proposal(0, 4)
        self.assertIsNone(proposal)
        self.assertEqual("inversion", reason)

    def test_deterministic_order_and_bytes(self):
        results = []
        for protected in ({12, 13}, set(reversed([12, 13]))):
            mesh = grid()
            engine = cliff.ConstrainedQem(mesh, protected)
            remaining = engine.simplify(26)
            geometry, domains, splits, partition = cliff.physical_geometry(mesh, engine, remaining, protected)
            results.append(cliff.canonical(dict(geometry=geometry, domains=domains,
                splits=splits, partition=partition, remaining=remaining, corners=[engine.corners[f] for f in remaining])))
        self.assertEqual(results[0], results[1])

    def test_impossible_budget_never_returns_oversized_result(self):
        mesh = grid(3)
        with self.assertRaises(cliff.BudgetError):
            cliff.ConstrainedQem(mesh, set(range(8))).simplify(7)
        with self.assertRaises(cliff.BudgetError):
            cliff.ConstrainedQem(mesh, set()).simplify(1)

    def test_fan_split_preserves_geometry_and_actual_boundary_cycles(self):
        positions = [(0., 0., 0.), (1., 0., 0.), (0., 1., 0.), (-1., 0., 0.), (0., -1., 0.)]
        triangles = [(0, 1, 2), (0, 3, 4)]
        output, remapped, splits = cliff.split_patch_fans(positions, triangles, [0, 0])
        self.assertEqual(1, len(splits))
        self.assertEqual(positions[0], output[-1])
        self.assertEqual([[positions[v] for v in t] for t in triangles],
                         [[output[v] for v in t] for t in remapped])
        loops = cliff.patch_boundaries(remapped, [0, 0], 0)
        self.assertEqual(2, len(loops))
        self.assertTrue(all(len(loop) == len(set(loop)) == 3 for loop in loops))
        self.assertEqual({e for t in remapped for e in cliff.face_edges(t)},
                         {(loop[i], loop[(i+1) % len(loop)]) for loop in loops for i in range(len(loop))})

    def test_unapproved_contact_proposals_follow_real_edges(self):
        mesh = grid()
        engine = cliff.ConstrainedQem(mesh, {12, 13})
        remaining = engine.simplify(26)
        geometry, domains, _, _ = cliff.physical_geometry(mesh, engine, remaining, {12, 13})
        proposals = cliff.contact_proposals(geometry, domains)
        for proposal in proposals:
            domain = next(d for d in domains if d["patchKey"] == proposal["patchKey"])
            actual = {(loop[i], loop[(i+1) % len(loop)]) for loop in domain["boundaries"] for i in range(len(loop))}
            self.assertIn(tuple(proposal["vertices"]), actual)
            self.assertIsNone(proposal["role"])
            self.assertIsNone(proposal["maximumGapMetres"])

    def test_disconnected_bare_components_preserve_geometry_and_source_membership(self):
        positions, uvs, triangles = [], [], []
        for i in range(71):
            offset = len(positions)
            positions.extend(((float(3*i), 0., 0.), (float(3*i+1), 0., 0.), (float(3*i), 1., 0.)))
            uvs.extend(((0., 0.), (1., 0.), (0., 1.)))
            triangles.append((offset, offset+1, offset+2))
        mesh = cliff.Mesh.from_arrays(positions, [(0., 0., 1.)]*len(positions), uvs, triangles)
        engine = cliff.ConstrainedQem(mesh, {70})
        remaining = engine.simplify(71)
        geometry, domains, splits, partition = cliff.physical_geometry(mesh, engine, remaining, {70})
        self.assertEqual(71, len(geometry["patches"]))
        self.assertEqual(71, len(geometry["triangles"]))
        self.assertEqual([], splits)
        self.assertTrue(all(len(domain["boundaries"]) == 1 for domain in domains))
        self.assertEqual([[i] for i in range(71)], [row["sourceTriangleIds"] for row in partition])
        self.assertEqual("preserved-crown", partition[-1]["key"])
        self.assertTrue(all(not patch["capabilities"] for patch in geometry["patches"]))
        for original, physical in zip(mesh.triangles, geometry["triangles"]):
            self.assertEqual([mesh.positions[v] for v in original],
                             [geometry["vertices"][v] for v in physical[:3]])

    def test_component_keys_follow_source_ids_and_ignore_vertex_only_contact(self):
        triangles = [(0, 1, 2), (0, 3, 4), (5, 6, 7)]
        sources = [42, 7, 99]
        memberships, rows = cliff.partition_physical_patches(triangles, sources, {99})
        self.assertEqual([1, 0, 2], memberships)
        self.assertEqual(["bare-rock-f000007", "bare-rock-f000042", "preserved-crown"],
                         [row["key"] for row in rows])
        _, shuffled = cliff.partition_physical_patches(list(reversed(triangles)), list(reversed(sources)), {99})
        self.assertEqual(rows, shuffled)
        # Edge adjacency joins source faces even when they are far apart in ordinal order.
        membership, connected = cliff.partition_physical_patches([(0, 1, 2), (2, 1, 3)], [500, 2], set())
        self.assertEqual([0, 0], membership)
        self.assertEqual([2, 500], connected[0]["sourceTriangleIds"])
        self.assertEqual("bare-rock-f000002", connected[0]["key"])

    def test_connected_component_with_65_loops_fails_without_automatic_subdivision(self):
        original = grid(18)
        triangles = []
        for cell in range(17*17):
            x, y = cell % 17, cell // 17
            if x % 2 == 1 and y % 2 == 1:
                continue  # 64 separate one-cell holes, plus the exterior boundary.
            triangles.extend(original.corners[2*cell:2*cell+2])
        mesh = cliff.Mesh.from_arrays([a[0] for a in original.attributes], [a[1] for a in original.attributes],
                                      [a[2] for a in original.attributes], triangles)
        engine = cliff.ConstrainedQem(mesh, set())
        remaining = engine.simplify(len(triangles))
        with self.assertRaisesRegex(cliff.QualificationError,
                                    r"bare-rock-f000000.*65 boundary loops; F3 limit is 64"):
            cliff.physical_geometry(mesh, engine, remaining, set())

    def test_component_count_keeps_the_f3_patch_limit(self):
        triangles = [(3*i, 3*i+1, 3*i+2) for i in range(257)]
        with self.assertRaisesRegex(cliff.QualificationError, "257 patches; F3 limit is 256"):
            cliff.partition_physical_patches(triangles, list(range(257)), set())


class SerializationTests(unittest.TestCase):
    def test_export_roundtrip_preserves_corner_attributes_and_pbr(self):
        mesh = grid()
        engine = cliff.ConstrainedQem(mesh, {12, 13})
        remaining = engine.simplify(26)
        document = dict(materials=[dict(name="synthetic-rock", pbrMetallicRoughness=dict(metallicFactor=0))],
                        textures=[], images=[], samplers=[])
        with tempfile.TemporaryDirectory() as folder:
            destination = Path(folder)
            name = cliff.export_visual(destination, "synthetic", document, mesh, engine, remaining)
            cliff.verify_visual_roundtrip(destination, name, mesh, [engine.corners[f] for f in remaining], document)
            data = json.loads((destination/name).read_text())
            self.assertEqual(document["materials"], data["materials"])
            # Corruption must fail even when the temporary roundtrip wrapper hashes
            # the corrupted bytes: it compares actual decoded corner values.
            buffer = destination/"synthetic.bin"
            raw = bytearray(buffer.read_bytes())
            struct.pack_into("<f", raw, 0, 100.0)
            buffer.write_bytes(raw)
            with self.assertRaises(cliff.QualificationError):
                cliff.verify_visual_roundtrip(destination, name, mesh, [engine.corners[f] for f in remaining], document)

    def test_fresh_destination_and_path_boundaries(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            with self.assertRaises(cliff.QualificationError):
                cliff.author(root, root/"absent.json", "synthetic", root)
            for invalid in ("../escape", "C:/outside", "a\\b"):
                with self.assertRaises(cliff.QualificationError):
                    cliff.contained(root, invalid)
            with self.assertRaises(cliff.QualificationError):
                cliff.uri_file(root, "https://example.invalid/asset.bin")

    def test_duplicate_json_fields_and_nonfinite_numbers_fail(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder)/"input.json"
            for text in ('{"x":1,"x":2}', '{"x":NaN}'):
                path.write_text(text)
                with self.assertRaises(cliff.QualificationError):
                    cliff.strict_json(path)

    def test_pinned_manifest_hash_fails_before_output_creation(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            manifest = root/"manifest.json"
            manifest.write_bytes(b"{}")
            config = root/"selection.json"
            config.write_bytes(cliff.canonical(dict(schemaVersion=1,
                manifest=dict(file="manifest.json", sha256="0"*64), assets=[])))
            output = root/"fresh"
            with self.assertRaisesRegex(cliff.QualificationError, "manifest digest"):
                cliff.author(root, config, "synthetic", output)
            self.assertFalse(output.exists())

    def test_dependency_digest_is_checked_before_geometry_decode(self):
        mesh = grid()
        engine = cliff.ConstrainedQem(mesh, {12, 13})
        remaining = engine.simplify(26)
        document = dict(materials=[dict(name="synthetic-rock")], images=[], textures=[], samplers=[])
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            name = cliff.export_visual(root, "synthetic", document, mesh, engine, remaining)
            near = dict(file=name, sha256=cliff.sha256(root/name), triangles=len(remaining),
                        dependencies=[dict(file="synthetic.bin", sha256=cliff.sha256(root/"synthetic.bin"))])
            manifest = dict(assets=[dict(key="synthetic", outputs=[near])])
            (root/"synthetic.bin").write_bytes(b"tampered")
            with self.assertRaisesRegex(cliff.QualificationError, "Dependency digest"):
                cliff.decode_source(root, dict(asset="synthetic", near=near), manifest)


if __name__ == "__main__":
    unittest.main()

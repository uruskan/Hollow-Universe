"""Root authoring invariants only; no Blender, production model reads, exports or game runtime.

Root runs: python -m unittest discover -s tools/blender/tests -p test_ground_cover_roots.py
The authoring subagent has not executed these tests.
"""
import copy
import importlib.util
import math
from pathlib import Path
import struct
import sys
import unittest

TOOL = Path(__file__).resolve().parents[1] / "ground_cover_roots.py"
spec = importlib.util.spec_from_file_location("ground_cover_roots", TOOL)
roots = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = roots
spec.loader.exec_module(roots)


def card():
    return roots.SourceMesh([(0, 0, 0), (1, 0, 0), (1, 1, 0), (0, 1, 0)],
                            [(0, 1, 2), (0, 2, 3)],
                            [(0, 1234, 65535, 17), (1, 2, 3, 4), (500, 600, 700, 800), (9, 8, 7, 6)],
                            {"POSITION": {"packedBytesSha256": "a"*64}}, {"gltfSha256": "b"*64})


def proposal(mesh=None, billboard=False):
    return roots.propose(mesh or card(), "wild/wild_v0_lod0", billboard, roots.Budget())


def review(p):
    return {"meshKey": p["meshKey"], "proposalSha256": roots.digest(roots.canonical(p)),
            "geometrySha256": p["geometrySha256"], "reviewer": "synthetic-fixture", "note": "Explicit synthetic bottom edge",
            "groups": [{"key": "stem-a", "intent": "card", "triangles": [0, 1], "rootLocal": [0.5, 0, 0],
                        "rootZoneCornerIds": [1, 2, 4], "supportCornerIds": [1, 2],
                        "maximumRootDistanceMetres": 0.51, "maximumHeightSpreadMetres": 0.01, "maximumSlopeDegrees": 30,
                        "growthAxis": [0, 1, 0], "growthLengthMetres": 1, "windWeight": 0.7, "windPhase": 0.25}]}


class GroundCoverRootsTests(unittest.TestCase):
    def test_proposal_is_reproducible_read_only_and_never_approved(self):
        mesh = card()
        before = copy.deepcopy(mesh)
        first, second = proposal(mesh), proposal(mesh)
        self.assertEqual(roots.canonical(first), roots.canonical(second))
        self.assertEqual(before, mesh)
        self.assertFalse(first["runtimeAdmitted"])
        self.assertEqual("unreviewed-hypothesis", first["groups"][0]["status"])
        self.assertIsNone(first["groups"][0]["rootLocal"])
        self.assertEqual([0.5, 0.0, 0.0], first["groups"][0]["candidateRootLocal"])
        self.assertEqual(mesh.wind, first["sourceWindRawUshort4"])

    def test_coincident_cards_are_not_welded_or_certified(self):
        mesh = card()
        doubled = roots.SourceMesh(mesh.positions*2, mesh.triangles + [tuple(v+4 for v in t) for t in mesh.triangles],
                                   mesh.wind*2, mesh.attribute_receipts, mesh.source)
        p = proposal(doubled)
        self.assertEqual(2, len(p["groups"]))
        for group in p["groups"]:
            self.assertEqual("refused-ambiguous", group["status"])
            self.assertIsNone(group["candidateRootLocal"])
            self.assertIn("coincident-distinct-source-indices", group["ambiguities"])

    def test_nonmanifold_component_has_no_automatic_root(self):
        mesh = roots.SourceMesh([(0,0,0), (1,0,0), (0,1,0), (0,0,1), (0,1,1)],
                                [(0,1,2), (1,0,3), (0,1,4)], [(0,0,0,0)]*5, {}, {})
        p = proposal(mesh)
        self.assertIn("nonmanifold-index-edge", p["groups"][0]["ambiguities"])
        self.assertIsNone(p["groups"][0]["candidateRootLocal"])

    def test_large_flat_boundary_remains_ambiguous_with_total_corner_evidence(self):
        # All 1024 boundary vertices are equally low. This is the refused case that used to do
        # a linear lower-list membership scan per corner; the production author now uses a set.
        count = 1024
        positions = [(0,0,0)] + [(math.cos(i*math.tau/count), 0, math.sin(i*math.tau/count)) for i in range(count)]
        triangles = [(0, i+1, (i+1) % count+1) for i in range(count)]
        mesh = roots.SourceMesh(positions, triangles, [(0,0,0,0)]*len(positions), {}, {})
        group = proposal(mesh)["groups"][0]
        self.assertEqual("refused-ambiguous", group["status"])
        self.assertIsNone(group["candidateRootLocal"])
        self.assertEqual(count*2, len(group["candidateRootZoneCornerIds"]))

    def test_corner_partition_preserves_source_order_and_opaque_wind(self):
        p = proposal()
        out = roots.reviewed_sidecar(p, roots.canonical(p), review(p))
        self.assertEqual(list(range(1, 7)), [row[0] for row in out["cornerData"]])
        self.assertEqual([0,0,1,0,1,1], [row[2] for row in out["cornerData"]])
        self.assertEqual(p["sourceWindRawUshort4"], out["sourceWindRawUshort4"])
        self.assertFalse(out["runtimeAdmitted"])

    def test_review_refuses_stale_receipt_missing_duplicate_and_foreign_corners(self):
        p = proposal()
        changes = [lambda r: r.update(proposalSha256="0"*64),
                   lambda r: r["groups"][0].update(triangles=[0]),
                   lambda r: r["groups"][0].update(triangles=[0,0,1]),
                   lambda r: r["groups"][0].update(rootZoneCornerIds=[7]),
                   lambda r: r["groups"][0].update(supportCornerIds=[3]),
                   lambda r: r["groups"][0].update(maximumRootDistanceMetres=0.1)]
        for change in changes:
            with self.subTest(change=change):
                r = review(p); change(r)
                with self.assertRaises(roots.RootError):
                    roots.reviewed_sidecar(p, roots.canonical(p), r)

    def test_explicit_billboard_zone_and_wind_semantics_are_required(self):
        p = proposal(billboard=True); r = review(p)
        with self.assertRaises(roots.RootError):
            roots.reviewed_sidecar(p, roots.canonical(p), r)
        r["groups"][0]["intent"] = "billboard"
        self.assertFalse(roots.reviewed_sidecar(p, roots.canonical(p), r)["runtimeAdmitted"])
        del r["groups"][0]["windPhase"]
        with self.assertRaises(roots.RootError):
            roots.reviewed_sidecar(p, roots.canonical(p), r)

    def test_group_identity_and_corner_indices_ignore_review_enumeration_order(self):
        p = proposal(); r = review(p)
        first = r["groups"][0]
        first.update(key="stem-b", triangles=[0], rootZoneCornerIds=[1,2], supportCornerIds=[1])
        second = copy.deepcopy(first)
        second.update(key="stem-a", triangles=[1], rootLocal=[0,0,0], rootZoneCornerIds=[4], supportCornerIds=[4])
        r["groups"].append(second)
        a = roots.reviewed_sidecar(p, roots.canonical(p), r)
        r["groups"].reverse()
        b = roots.reviewed_sidecar(p, roots.canonical(p), r)
        self.assertEqual(a["groups"], b["groups"])
        self.assertEqual(a["cornerData"], b["cornerData"])

    def test_source_transform_is_y_up_metres_and_preserves_input(self):
        source = [(0, 0, -100), (100, 0, 0)]
        result = roots.transformed(source, {"scale": [.01,.01,.01], "rotation": [2**-.5,0,0,2**-.5]})
        self.assertAlmostEqual(1, result[0][1])
        self.assertAlmostEqual(0, result[0][2])
        self.assertEqual([(0, 0, -100), (100, 0, 0)], source)

    def test_accessor_retains_raw_normalized_wind_and_skips_stride_padding(self):
        data = struct.pack("<4H4x4H4x", 0,1234,65535,17, 1,2,3,4)
        doc = {"accessors": [{"bufferView": 0, "count": 2, "componentType": 5123, "type": "VEC4", "normalized": True}],
               "bufferViews": [{"buffer": 0, "byteOffset": 0, "byteLength": len(data), "byteStride": 12}]}
        values, receipt = roots.accessor(doc, [data], 0, 5)
        self.assertEqual([(0,1234,65535,17), (1,2,3,4)], values)
        self.assertEqual(roots.digest(struct.pack("<8H", 0,1234,65535,17,1,2,3,4)), receipt["packedBytesSha256"])
        doc["bufferViews"][0]["byteLength"] -= 5
        with self.assertRaises(roots.RootError):
            roots.accessor(doc, [data], 0, 5)

    def test_geometry_identity_changes_with_attributes_transform_or_corner_order(self):
        a = card(); b = copy.deepcopy(a)
        b.attribute_receipts["POSITION"]["packedBytesSha256"] = "c"*64
        self.assertNotEqual(a.geometry_sha256, b.geometry_sha256)
        b = copy.deepcopy(a); b.source["nodeTransform"] = {"scale": [2,2,2]}
        self.assertNotEqual(a.geometry_sha256, b.geometry_sha256)
        b = copy.deepcopy(a); b.triangles[0] = (1,2,0)
        self.assertNotEqual(a.geometry_sha256, b.geometry_sha256)

    def test_strict_json_and_budget_refuse_before_authoring(self):
        for raw in (b'{"x":1,"x":2}', b'{"x":NaN}'):
            with self.assertRaises(roots.RootError): roots.strict_json(raw)
        budget = roots.Budget()
        with self.assertRaises(roots.RootError): budget.charge_corners(roots.MAX_CORNERS + 1)


if __name__ == "__main__":
    unittest.main()

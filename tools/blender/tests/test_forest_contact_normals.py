from copy import deepcopy
import importlib.util
import json
from pathlib import Path
import sys
import unittest

PATH = Path(__file__).resolve().parents[1]/"forest_contact_normals.py"
spec = importlib.util.spec_from_file_location("forest_contact_normals",PATH)
normals = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = normals
spec.loader.exec_module(normals)

SOIL = "toe-apron-candidate"
POINTS = [(0.,0.,0.),(1.,0.,0.),(0.,0.,1.),(1.,.5,1.)]
FACES = [(0,2,1),(1,2,3)]


class SoilNormalTests(unittest.TestCase):
    def test_area_weighted_shared_fan_is_exactly_shared_across_the_diagonal(self):
        result = normals.build(POINTS,FACES,[SOIL,SOIL])
        expected = normals.encoded_unit((-.5,2.,-.5))
        self.assertEqual(expected,result.corner_normals[0][2])
        self.assertEqual(expected,result.corner_normals[1][0])
        self.assertEqual(expected,result.corner_normals[0][1])
        self.assertEqual(expected,result.corner_normals[1][1])
        self.assertEqual(1,result.evidence["smoothEdges"])
        self.assertEqual(4,result.evidence["smoothFanCount"])
        self.assertTrue(result.evidence["encodedOutwardHemisphereExact"])

    def test_hard_semantic_boundary_prevents_soil_to_rock_smoothing(self):
        result = normals.build(POINTS,FACES,[SOIL,"left-return"])
        self.assertEqual((0.,1.,0.),result.corner_normals[0][2])
        rock = normals.encoded_unit((-.5,1.,-.5))
        self.assertEqual((rock,)*3,result.corner_normals[1])
        self.assertEqual(0,result.evidence["smoothEdges"])
        self.assertEqual(1,sum(edge["reason"] == "semantic" for edge in result.evidence["hardEdges"]))

    def test_two_soil_semantics_remain_an_explicit_hard_boundary(self):
        result = normals.build(POINTS,FACES,["soil-cap-candidate",SOIL])
        self.assertNotEqual(result.corner_normals[0][2],result.corner_normals[1][0])
        self.assertEqual(0,result.evidence["smoothEdges"])

    def test_nonsoil_faces_retain_every_flat_corner_normal(self):
        for group in set(normals.GROUPS)-set(normals.SOIL_GROUPS):
            result = normals.build(POINTS,FACES,[group,group])
            self.assertEqual((0.,1.,0.),result.corner_normals[0][0])
            expected = normals.encoded_unit(normals.area_vector([POINTS[v] for v in FACES[1]]))
            self.assertEqual((expected,)*3,result.corner_normals[1])
            self.assertEqual(0,result.evidence["smoothFanCount"])

    def test_disconnected_vertex_fans_do_not_merge_at_a_shared_index(self):
        points = POINTS+[(-1.,0.,0.),(0.,1.,-1.)]
        result = normals.build(points,[(0,2,1),(0,4,5)],[SOIL,SOIL])
        self.assertNotEqual(result.corner_normals[0][0],result.corner_normals[1][0])
        self.assertEqual(2,sum(fan["vertex"] == 0 for fan in result.evidence["fans"]))

    def test_uv_mapping_is_not_an_input_to_physical_normal_fans(self):
        # Both faces share one authored normal despite a hypothetical signed-projection chart cut.
        result = normals.build(POINTS,FACES,[SOIL,SOIL])
        self.assertEqual(result.corner_normals[0][1],result.corner_normals[1][1])
        self.assertFalse(result.evidence["uvChanged"])

    def test_area_sum_does_not_depend_on_triangle_subdivision_at_the_same_vertex(self):
        # Splitting a neighboring planar face through an exact point preserves its total oriented area contribution.
        points = POINTS+[(.5,.25,1.)]
        original = normals.build(POINTS,FACES,[SOIL,SOIL])
        split = normals.build(points,[(0,2,1),(1,2,4),(1,4,3)],[SOIL]*3)
        self.assertEqual(original.corner_normals[0][2],split.corner_normals[0][2])

    def test_folded_and_cancelling_neighbors_get_explicit_hard_creases_without_inward_normals(self):
        for points in ([(0.,0.,0.),(1.,0.,0.),(0.,1.,0.),(0.,1.,0.)],
                       [(0.,0.,0.),(1.,0.,0.),(0.,1.,0.),(0.,2.,.01)]):
            encoded = [tuple(normals.f32(v) for v in point) for point in points]
            result = normals.build(encoded,[(0,1,2),(1,0,3)],[SOIL,SOIL])
            self.assertEqual(0,result.evidence["smoothEdges"])
            self.assertTrue(result.evidence["encodedOutwardHemisphereExact"])
            self.assertEqual(1,sum(row["reason"] == "geometric-dihedral-over-60" for row in result.evidence["hardEdges"]))

    def test_actual_collar_apron_fold_retains_every_position_but_splits_its_inward_fan(self):
        # Authenticated r11 closure faces101,102,103,1027,1028,1029 at vertex355.
        # The earlier unsplit area average pointed inward at original corner310.
        points = [(-7.397897720336914,.012011494487524033,2.178274154663086),
                  (-7.620689392089844,-.10292641073465347,2.2194344997406006),
                  (-7.401796340942383,-.10738109797239304,2.166863203048706),
                  (-7.200155735015869,.0027694874443113804,2.1737520694732666),
                  (-7.202217102050781,-.11576151847839355,2.155146837234497),
                  (-7.431154251098633,-.09776153415441513,3.003990888595581),
                  (-7.181011199951172,-.09776153415441513,3.003990888595581)]
        faces = [(0,1,2),(3,0,2),(3,2,4),(2,1,5),(2,5,6),(4,2,6)]
        before = deepcopy((points,faces))
        areas = [normals.area_vector([tuple(normals.Fraction(v) for v in points[i]) for i in face]) for face in faces]
        old_sum = tuple(sum((a[k] for a in areas),normals.Fraction(0)) for k in range(3))
        old_normal = tuple(normals.Fraction(v) for v in normals.encoded_unit(old_sum))
        self.assertLessEqual(normals.dot(old_normal,areas[2]),0)
        result = normals.build(points,faces,[SOIL]*6)
        self.assertEqual(before,(points,faces))
        self.assertGreater(result.evidence["smoothEdges"],0)
        self.assertTrue(any(row["reason"] == "geometric-dihedral-over-60" for row in result.evidence["hardEdges"]))
        self.assertGreater(sum(fan["vertex"] == 2 for fan in result.evidence["fans"]),1)
        for area,ns in zip(areas,result.corner_normals):
            for n in ns: self.assertGreater(normals.dot(area,tuple(normals.Fraction(v) for v in n)),0)

    def test_locally_shallow_edges_cannot_wrap_a_complete_fan_outside_the_authored_cone(self):
        points = [(0.,0.,0.),(1.,0.,0.),(0.,1.,0.),(-1.,1.,1.),(0.,0.,1.)]
        faces = [(0,1,2),(0,2,3),(0,3,4)]
        # Adjacent normal angles are45 and60 degrees; end normals are90 degrees apart.
        result = normals.build(points,faces,[SOIL]*3)
        self.assertEqual(1,result.evidence["smoothEdges"])
        self.assertTrue(any(row["reason"] == "endpoint-fan-spread-over-60" for row in result.evidence["hardEdges"]))
        self.assertNotEqual(result.corner_normals[0][0],result.corner_normals[2][0])

    def test_exact_sixty_degree_edge_is_admitted_but_wider_edge_and_pair_work_overflow_refuse_smoothing(self):
        points = [(0.,0.,0.),(0.,1.,0.),(-1.,1.,1.),(0.,0.,1.)]
        faces = [(0,1,2),(0,2,3)]
        self.assertEqual(1,normals.build(points,faces,[SOIL]*2).evidence["smoothEdges"])
        points[3] = (0.,.125,1.)
        self.assertEqual(0,normals.build(points,faces,[SOIL]*2).evidence["smoothEdges"])
        policy = dict(normals.DEFAULT_POLICY,maximumPairTests=1)
        with self.assertRaisesRegex(normals.NormalError,"pair-test capacity"):
            normals.build(POINTS,FACES,[SOIL]*2,policy)

    def test_nonmanifold_and_wrong_winding_refuse(self):
        for faces in ([(0,2,1),(1,2,3),(1,2,0)],[(0,2,1),(2,1,3)]):
            with self.assertRaises(normals.NormalError): normals.build(POINTS,faces,[SOIL]*len(faces))

    def test_geometry_input_is_immutable_and_output_is_deterministic(self):
        points,faces,groups = deepcopy(POINTS),deepcopy(FACES),[SOIL,SOIL]
        before = deepcopy((points,faces,groups))
        first,second = normals.build(points,faces,groups),normals.build(points,faces,groups)
        self.assertEqual(before,(points,faces,groups))
        self.assertEqual(first,second)
        self.assertEqual(first.evidence,json.loads(json.dumps(first.evidence)))
        self.assertFalse(first.evidence["geometryChanged"])
        self.assertFalse(first.evidence["sourceScanAttributesChanged"])
        self.assertFalse(first.evidence["rootsApproved"])

    def test_identity_binds_geometry_semantics_policy_and_actual_normals(self):
        first = normals.build(POINTS,FACES,[SOIL,SOIL])
        changed = normals.build(POINTS,FACES,[SOIL,"left-return"])
        self.assertNotEqual(first.evidence["closureGeometrySha256"],changed.evidence["closureGeometrySha256"])
        self.assertNotEqual(first.evidence["normalStreamSha256"],changed.evidence["normalStreamSha256"])
        policy = deepcopy(normals.DEFAULT_POLICY); policy.update(maximumFaces=1000,maximumCorners=3000,maximumEdges=3000)
        limited = normals.build(POINTS,FACES,[SOIL,SOIL],policy)
        self.assertNotEqual(first.evidence["policySha256"],limited.evidence["policySha256"])
        self.assertEqual(first.corner_normals,limited.corner_normals)

    def test_face_edge_and_policy_caps_refuse_instead_of_partial_fans(self):
        for changes in (dict(maximumFaces=1,maximumCorners=3,maximumEdges=3),dict(maximumEdges=1),
                        dict(maximumCorners=1),dict(smoothGroups=["left-return"]),dict(weighting="guessed"),
                        dict(maximumNormalSpreadDegrees=61),dict(minimumNormalCosine=.49),dict(maximumPairTests=262145)):
            policy = deepcopy(normals.DEFAULT_POLICY); policy.update(changes)
            with self.assertRaises(normals.NormalError): normals.build(POINTS,FACES,[SOIL,SOIL],policy)

    def test_nonfinite_unencoded_and_degenerate_inputs_refuse(self):
        for value in (True,float("nan"),float("inf"),.1,10001.):
            points = deepcopy(POINTS); points[0] = (value,0.,0.)
            with self.assertRaises(normals.NormalError): normals.build(points,FACES,[SOIL,SOIL])
        with self.assertRaises(normals.NormalError): normals.build(POINTS,[(0,0,1)],[SOIL])
        with self.assertRaises(normals.NormalError): normals.build(POINTS,FACES,[SOIL,"unreviewed-soil"])

    def test_cancellation_propagates_before_output_publication(self):
        class Cancelled(Exception): pass
        calls = 0
        def check():
            nonlocal calls
            calls += 1
            if calls == 4: raise Cancelled()
        with self.assertRaises(Cancelled): normals.build(POINTS,FACES,[SOIL,SOIL],check=check)


if __name__ == "__main__":
    unittest.main()

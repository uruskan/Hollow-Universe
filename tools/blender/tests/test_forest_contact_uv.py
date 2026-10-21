"""Synthetic metric-chart tests; only the root schedules execution."""
from copy import deepcopy
import importlib.util
import math
from pathlib import Path
import sys
import unittest

spec = importlib.util.spec_from_file_location("forest_contact_uv",Path(__file__).resolve().parents[1]/"forest_contact_uv.py")
uv = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = uv
spec.loader.exec_module(uv)

UP = [(0.,0.,0.),(1.,0.,0.),(0.,0.,-1.)]


class MetricUvTests(unittest.TestCase):
    def test_six_axis_planes_have_positive_area_and_exact_unit_metric(self):
        for projection,(u,v,n) in uv.PROJECTIONS.items():
            points = [(0.,0.,0.),tuple(float(x) for x in u),tuple(float(x) for x in v)]
            group = next(g for g,p in uv.PREFERRED.items() if p == projection)
            layout = uv.build(points,[(0,1,2)],[group])
            face = layout.faces[0]
            self.assertEqual(projection,face.projection)
            self.assertEqual(((0.,0.),(1.,0.),(0.,1.)),face.uvs)
            self.assertEqual(1,layout.evidence["maximumStretchMeasured"])
            self.assertEqual(1,layout.evidence["minimumStretchMeasured"])
            self.assertEqual(3,len(layout.evidence["originalBoundaryEdges"]))
            for normal,tangent in zip(face.normals,face.tangents):
                self.assertEqual(tuple(float(x) for x in n),normal)
                self.assertEqual((*u,1.),tangent)

    def test_wall_uvs_do_not_collapse_like_old_xz_projection(self):
        points = [(2.,0.,0.),(2.,0.,2.),(2.,3.,0.)]
        layout = uv.build(points,[(0,1,2)],["left-wall"])
        self.assertEqual("-x",layout.faces[0].projection)
        self.assertEqual(0,layout.evidence["collapsedTriangles"])
        self.assertEqual(((0.,0.),(2.,0.),(0.,3.)),layout.faces[0].uvs)

    def test_steep_cap_uses_signed_dominant_projection_without_moving_geometry(self):
        points = [(0.,0.,0.),(0.,1.,0.),(0.,0.,1.)]
        before = deepcopy(points)
        layout = uv.build(points,[(0,1,2)],["soil-cap-candidate"])
        self.assertEqual("+x",layout.faces[0].projection)
        self.assertEqual(before,points)
        self.assertFalse(layout.evidence["geometryChanged"])
        self.assertFalse(layout.evidence["contactsApproved"])

    def test_preferred_plane_and_dominant_ties_are_deterministic(self):
        # Normal (1,1,1): +Y preferred is within the bound, while a -X
        # preference is on the wrong side and falls back to the X-first tie.
        points = [(0.,0.,0.),(1.,-1.,0.),(1.,0.,-1.)]
        self.assertEqual("+y",uv.build(points,[(0,1,2)],["soil-cap-candidate"]).faces[0].projection)
        self.assertEqual("+x",uv.build(points,[(0,1,2)],["left-return"]).faces[0].projection)
        a = uv.build(points,[(0,1,2)],["left-return"])
        b = uv.build(deepcopy(points),[(0,1,2)],["left-return"],dict(reversed(list(uv.DEFAULT_POLICY.items()))))
        self.assertEqual(a,b)
        self.assertLessEqual(a.evidence["maximumStretchMeasured"],math.sqrt(3)+1e-12)

    def test_shared_chart_preserves_exact_vertex_uv_and_only_records_real_cuts(self):
        points = UP+[(1.,0.,-1.),(1.,1.,0.)]
        triangles = [(0,1,2),(1,3,2),(1,4,3)]
        layout = uv.build(points,triangles,["soil-cap-candidate"]*3)
        self.assertEqual(layout.faces[0].chart,layout.faces[1].chart)
        self.assertNotEqual(layout.faces[1].chart,layout.faces[2].chart)
        self.assertEqual(layout.faces[0].uvs[1],layout.faces[1].uvs[0])
        self.assertEqual(layout.faces[0].uvs[2],layout.faces[1].uvs[2])
        self.assertEqual([dict(vertices=[1,3],faces=[1,2],reason="projection")],layout.evidence["seams"])
        self.assertEqual([0,1],layout.evidence["charts"][0]["faceOrdinals"])

    def test_disconnected_and_semantically_distinct_regions_are_separate_charts(self):
        points = UP+[(1.,0.,-1.),(5.,0.,0.),(6.,0.,0.),(5.,0.,-1.)]
        layout = uv.build(points,[(0,1,2),(1,3,2),(4,5,6)],
                          ["soil-cap-candidate","toe-apron-candidate","soil-cap-candidate"])
        self.assertEqual(3,len(layout.evidence["charts"]))
        self.assertEqual("semantic",layout.evidence["seams"][0]["reason"])
        self.assertEqual("soil-cap-candidate:+y:2",layout.evidence["charts"][2]["key"])

    def test_all_direction_certificate_rejects_collapse_mirror_and_excess_distortion(self):
        for points,coordinates in ((UP,[(0,0)]*3),(UP,[(0,0),(0,1),(1,0)]),
                                  ([(0,0,0),(3,0,0),(0,0,-1)],[(0,0),(1,0),(0,1)]),
                                  (UP,[(0,0),(3,0),(0,1)])):
            with self.assertRaises(uv.UvError): uv.metric_certificate(points,coordinates)
        # Every edge ratio is <2 here, but a tangent direction has stretch >2.
        # The positive-semidefinite test sees the interior directional failure.
        with self.assertRaisesRegex(uv.UvError,"distortion"):
            uv.metric_certificate([(0,0,0),(1.9,0,0),(1.2,0,-1)],[(0,0),(1,0),(0,1)])

    def test_distortion_boundary_is_exact_and_does_not_use_measurement_rounding(self):
        coordinates = [(0,0),(1,0),(0,1)]
        points = [(0,0,0),(2,0,0),(0,0,-1)]
        self.assertTrue(uv.metric_certificate(points,coordinates)[2]["allDirectionsWithinTwoExact"])
        points[1] = (math.nextafter(2.,math.inf),0,0)
        with self.assertRaises(uv.UvError): uv.metric_certificate(points,coordinates)

    def test_tangent_derivatives_support_future_smooth_corner_normals(self):
        points = [(0.,0.,0.),(2.,0.,0.),(0.,3.,0.)]
        coordinates = [(0.,0.),(2.,0.),(0.,3.)]
        normals = [uv.encoded_unit(v) for v in ((0,0,1),(.3,0,1),(0,.4,1))]
        actual,tangents,evidence = uv.tangent_frames(points,coordinates,normals)
        self.assertEqual(tuple(normals),actual)
        for n,t in zip(actual,tangents):
            self.assertAlmostEqual(1,uv.dot(t[:3],t[:3]),places=6)
            self.assertAlmostEqual(0,uv.dot(n,t[:3]),places=6)
            self.assertEqual(1,t[3])
            self.assertGreater(uv.dot(uv.cross(n,t[:3]),(0,1,0))*t[3],0)
        self.assertLessEqual(evidence["maximumEncodedNormalTangentDot"],2e-6)
        with self.assertRaises(uv.UvError): uv.tangent_frames(points,coordinates,[(1.,0.,0.)]*3)

    def test_encoded_geometry_and_capacity_failures_never_return_partial_charts(self):
        for points,triangles,groups in (([(True,0,0),*UP[1:]],[(0,1,2)],["base"]),
            ([(float("nan"),0,0),*UP[1:]],[(0,1,2)],["base"]),
            ([(.1,0,0),*UP[1:]],[(0,1,2)],["base"]),
            (UP,[(0,0,1)],["base"]),(UP,[(0,1,2)],["invented-soil"])):
            with self.assertRaises(uv.UvError): uv.build(points,triangles,groups)
        points = UP+[(1.,0.,-1.)]
        for policy in (dict(uv.DEFAULT_POLICY,maximumFaces=1,maximumCharts=1,maximumEdges=3),
                       dict(uv.DEFAULT_POLICY,maximumCharts=1),dict(uv.DEFAULT_POLICY,maximumEdges=2)):
            with self.assertRaises(uv.UvError):
                uv.build(points,[(0,1,2),(1,3,2)],["soil-cap-candidate","toe-apron-candidate"],policy)

    def test_supplied_shading_normals_change_only_normal_tangent_identity_not_charts_or_geometry(self):
        flat = uv.build(UP,[(0,1,2)],["soil-cap-candidate"])
        ns = tuple(uv.encoded_unit(v) for v in ((0,1,0),(.3,1,0),(0,1,.4)))
        smooth = uv.build(UP,[(0,1,2)],["soil-cap-candidate"],corner_normals=(ns,))
        self.assertEqual(ns,smooth.faces[0].normals)
        self.assertEqual(flat.faces[0].uvs,smooth.faces[0].uvs)
        for key in ("closureGeometrySha256","charts","seams","originalBoundaryEdges"):
            self.assertEqual(flat.evidence[key],smooth.evidence[key])
        self.assertNotEqual(flat.evidence["addedAttributeStreamSha256"],smooth.evidence["addedAttributeStreamSha256"])
        self.assertNotEqual(flat.evidence["normalPolicy"],smooth.evidence["normalPolicy"])
        for invalid in ((),(ns,ns),((ns[0],ns[1]),),((ns[0],(0,2,0),ns[2]),)):
            with self.assertRaises(uv.UvError): uv.build(UP,[(0,1,2)],["soil-cap-candidate"],corner_normals=invalid)

    def test_nonmanifold_and_inconsistent_input_edges_refuse(self):
        points = UP+[(0.,1.,0.),(0.,-1.,0.)]
        for triangles in ([(0,1,2),(0,1,3)],[(0,1,2),(1,0,3),(0,1,4)]):
            with self.assertRaises(uv.UvError): uv.build(points,triangles,["soil-cap-candidate"]*len(triangles))

    def test_policy_and_geometry_identity_are_explicit_without_geometric_acceptance(self):
        a = uv.build(UP,[(0,1,2)],["soil-cap-candidate"])
        changed = deepcopy(UP); changed[1] = (2.,0.,0.)
        b = uv.build(changed,[(0,1,2)],["soil-cap-candidate"])
        self.assertNotEqual(a.evidence["closureGeometrySha256"],b.evidence["closureGeometrySha256"])
        self.assertNotEqual(a.evidence["addedAttributeStreamSha256"],b.evidence["addedAttributeStreamSha256"])
        self.assertEqual(a.evidence["policySha256"],b.evidence["policySha256"])
        for key,value in (("maximumLinearDistortion",2.01),("metresPerCoordinate",True),("originMetres",[1,0,0]),
                          ("maximumFaces",5201),("schema","fallback")):
            with self.assertRaises(uv.UvError): uv.build(UP,[(0,1,2)],["base"],dict(uv.DEFAULT_POLICY,**{key:value}))

    def test_cancellation_or_shared_deadline_check_propagates_without_output(self):
        calls = []
        def check():
            calls.append(1)
            if len(calls) == 2: raise TimeoutError("root deadline")
        with self.assertRaises(TimeoutError): uv.build(UP,[(0,1,2)],["base"],check=check)


if __name__ == "__main__": unittest.main()

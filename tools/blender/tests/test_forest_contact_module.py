"""Synthetic candidate-authoring tests; execution is scheduled by the root agent."""
from copy import deepcopy
from dataclasses import replace
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

TOOL = Path(__file__).resolve().parents[1]/"forest_contact_module.py"
spec = importlib.util.spec_from_file_location("forest_contact_module", TOOL)
module = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = module
spec.loader.exec_module(module)

# Slanted open rock face, outward toward +Y/+Z. This is deliberately not a
# horizontal plane; closure must respect the source's actual boundary direction.
POSITIONS = [(-1., 1., -.5), (1., 1., -.5), (-1., 0., .5), (1., 0., .5)]
TRIANGLES = [(0, 2, 1), (1, 2, 3)]


def recipe():
    value = json.loads((TOOL.parent/"data"/"forest_contact_module.json").read_text(encoding="utf-8"))
    value["expectedOuterEdges"] = 4
    for name, index in (("crest-left", 0), ("crest-right", 1), ("toe-left", 2), ("toe-right", 3)):
        value["anchors"][name] = dict(physicalVertex=index, positionMetres=list(POSITIONS[index]))
    return value


def mesh(positions=None, triangles=None, work=None):
    return module.quality.Mesh(positions or POSITIONS, triangles or TRIANGLES, work or module.quality.Work())


def build(value=None):
    work = module.quality.Work()
    original = mesh(work=work)
    return module.build_geometry(original, original, original, value or recipe(), work)


class RecipeTests(unittest.TestCase):
    def test_proposed_forest_recipe_has_explicit_dimensions_and_pins(self):
        value = module.validate_recipe(json.loads((TOOL.parent/"data"/"forest_contact_module.json").read_text(encoding="utf-8")))
        self.assertEqual(314, value["expectedOuterEdges"])
        self.assertEqual(1, value["scale"])
        self.assertEqual(.4, value["dimensionsMetres"]["baseBelowLowestSource"])
        self.assertEqual(8500, value["budgets"]["maximumPhysicalTriangles"])
        self.assertEqual(.12, value["dimensionsMetres"]["collarWidth"])
        self.assertEqual("global-collar-y-envelope-v1",value["outerGuideRule"])
        self.assertEqual(800,value["budgets"]["maximumAddedVertices"])
        self.assertEqual(.10,value["returnRailProfile"]["baseClearanceMetres"])
        self.assertEqual("complete-authenticated-near-position-bounds",value["returnRailProfile"]["boundsSemantics"])
        self.assertEqual(module.metric_uv.SCHEMA,value["uvAuthoring"]["schema"])
        self.assertEqual(2,value["uvAuthoring"]["maximumLinearDistortion"])
        self.assertEqual(module.soil_normals.SCHEMA,value["normalAuthoring"]["schema"])
        self.assertEqual(["soil-cap-candidate","toe-apron-candidate"],value["normalAuthoring"]["smoothGroups"])

    def test_nonunit_scale_boolean_values_and_hidden_fields_refuse(self):
        for mutate in (
            lambda r: r.update(scale=1.01), lambda r: r.update(scale=True),
            lambda r: r.update(extraBurial=.2), lambda r: r.update(expectedOuterEdges=True),
            lambda r: r["dimensionsMetres"].update(toeExtension=True),
            lambda r: r["dimensionsMetres"].update(toeExtension=float("nan")),
            lambda r: r["dimensionsMetres"].update(baseBelowLowestSource=-.1),
            lambda r: r.update(receiptSha256="bad"),
            lambda r: r.update(outerGuideRule="fit-until-clear"),
            lambda r: r["returnRailProfile"].update(rule="try-another-length"),
            lambda r: r["returnRailProfile"].update(boundsSemantics="rim-anchors-only"),
            lambda r: r["returnRailProfile"].update(baseClearanceMetres=True),
            lambda r: r["returnRailProfile"].update(baseClearanceMetres=.101),
            lambda r: r["returnRailProfile"].update(extraBurial=.1),
            lambda r: r["budgets"].update(maximumAddedVertices=2054),
            lambda r: r["uvAuthoring"].update(maximumLinearDistortion=2.01),
            lambda r: r["uvAuthoring"].update(maximumFaces=1700),
            lambda r: r["uvAuthoring"].update(hiddenJitter=.01),
            lambda r: r["normalAuthoring"].update(smoothGroups=["back-wall"]),
            lambda r: r["normalAuthoring"].update(maximumFaces=1700,maximumCorners=5100,maximumEdges=5100),
            lambda r: r["normalAuthoring"].update(hiddenAutoCrease=45),
        ):
            value = recipe()
            mutate(value)
            with self.assertRaises(module.author.QualificationError): module.validate_recipe(value)

    def test_ambiguous_anchor_positions_and_invalid_collar_width_refuse(self):
        value = recipe()
        value["anchors"]["toe-right"] = deepcopy(value["anchors"]["toe-left"])
        with self.assertRaises(module.author.QualificationError): module.validate_recipe(value)
        for width in (0, -.1, .251, True, float("inf")):
            value = recipe()
            value["dimensionsMetres"]["collarWidth"] = width
            with self.assertRaises(module.author.QualificationError): module.validate_recipe(value)


class WorkDiagnosticTests(unittest.TestCase):
    def test_refusal_records_current_phase_existing_counters_and_completed_deltas(self):
        limits = replace(module.quality.Limits(),maximum_exact_operations=1)
        work = module.AuthoringWork(limits)
        module.authoring_phase(work,"first")
        work.take("exact_operations")
        module.authoring_phase(work,"second",dict(states=3))
        with self.assertRaises(module.quality.Incomplete) as caught:
            work.take("exact_operations")
        message = str(caught.exception)
        self.assertEqual(1,message.count("authoringWork="))
        report = json.loads(message.split("; authoringWork=",1)[1])
        self.assertEqual("second",report["phase"])
        self.assertEqual(2,report["workCounts"]["exact_operations"])
        self.assertEqual(1,report["phaseWorkDelta"]["exact_operations"])
        self.assertEqual(dict(states=3),report["details"])
        self.assertEqual(dict(phase="first",workDelta=dict(exact_operations=1)),report["completedPhases"][-1])
        self.assertEqual(limits,work.limits)

    def test_phase_history_has_a_fixed_capacity(self):
        work = module.AuthoringWork()
        for i in range(64): module.authoring_phase(work,str(i))
        with self.assertRaisesRegex(module.author.QualificationError,"phase diagnostic budget"):
            module.authoring_phase(work,"overflow")


class ExactPairCacheTests(unittest.TestCase):
    A = ((0.,0.,0.),(2.,0.,0.),(0.,2.,0.))
    # Inclusive boxes overlap and neither triangle lies strictly on one side of
    # the other's plane. Proving separation needs exact plane slices.
    B = ((1.5,1.5,-1.),(1.5,1.5,1.),(3.,3.,0.))

    @staticmethod
    def classify(cache,a,b,work):
        exact,_ = module.quality.exact_coordinates(list(a)+list(b),work)
        return cache.classify(exact[:3],exact[3:],cache.triangle_id(a),cache.triangle_id(b),work)

    def test_exact_disjoint_proof_reused_without_relaxing_pair_or_arithmetic_cap(self):
        cache,first = module.ExactPairCache(),module.quality.Work()
        self.assertEqual("disjoint",self.classify(cache,self.A,self.B,first).kind)
        self.assertGreater(first.counts["exact_operations"],0)
        second = module.quality.Work(replace(module.quality.Limits(),maximum_exact_operations=0))
        self.assertEqual("disjoint",self.classify(cache,tuple(reversed(self.B)),self.A[1:]+self.A[:1],second).kind)
        self.assertEqual(0,second.counts["exact_operations"])
        self.assertEqual(1,second.counts["pair_tests"])
        self.assertEqual(1,cache.hits)
        with self.assertRaises(module.quality.Incomplete):
            self.classify(cache,self.A,self.B,module.quality.Work(replace(module.quality.Limits(),maximum_pair_tests=0)))

    def test_changed_coordinates_do_not_reuse_a_safe_pair_even_with_same_vertex_ordinals(self):
        cache,work = module.ExactPairCache(),module.quality.Work()
        self.assertEqual("disjoint",self.classify(cache,self.A,self.B,work).kind)
        crossing = ((.5,.5,-1.),(.5,.5,1.),(1.5,.5,0.))
        hit = self.classify(cache,self.A,crossing,work)
        self.assertEqual("interior-crossing",hit.kind)
        self.assertEqual(0,cache.hits)
        self.assertEqual(1,len(cache.pairs))
        changed = list(self.B)
        changed[0] = (module.math.nextafter(1.5,2.),1.5,-1.)
        self.assertNotEqual(cache.triangle_id(self.B),cache.triangle_id(changed))

    def test_rejected_pair_is_recomputed_with_ordered_exact_witnesses(self):
        cache,work = module.ExactPairCache(),module.quality.Work()
        other = ((0.,0.,0.),(2.,0.,0.),(1.,1.,0.))
        for a,b in ((self.A,other),(tuple(reversed(other)),self.A)):
            hit = self.classify(cache,a,b,work)
            expected = module.quality.classify_triangles(a,b)
            self.assertEqual(expected,hit)
        self.assertEqual(0,len(cache.pairs))
        self.assertEqual(0,cache.hits)

    def test_shared_geometry_uses_exact_positions_across_fan_indices_and_integer_scales(self):
        cache,work = module.ExactPairCache(),module.quality.Work()
        a = ((1.,1.,1.),(3.,1.,1.),(1.,3.,1.))
        b = ((1.,1.,1.),(0.,1.,1.),(1.,0.,2.))
        self.assertEqual("shared-vertex",self.classify(cache,a,b,work).kind)
        positions = list(reversed(b))+list(reversed(a))+[(2.**-80,0.,0.)]
        current = mesh(positions,[(0,1,2),(3,4,5)])
        ids = cache.mesh_ids(current,work)
        hit = cache.classify(current.points(0,True),current.points(1,True),ids[0],ids[1],work)
        self.assertEqual("shared-vertex",hit.kind)
        self.assertEqual(((current.scale,current.scale,current.scale),),hit.points)
        self.assertEqual(1,cache.hits)

    def test_zero_cache_or_pair_capacity_falls_back_to_exact_work(self):
        for cache in (module.ExactPairCache(maximum_bytes=0),module.ExactPairCache(maximum_pairs=0)):
            work = module.quality.Work()
            for _ in range(2): self.assertEqual("disjoint",self.classify(cache,self.A,self.B,work).kind)
            self.assertEqual(0,cache.hits)
            self.assertEqual(0,len(cache.pairs))
            self.assertGreaterEqual(work.counts["exact_operations"],4)
            self.assertGreater(cache.capacity_fallbacks,0)
            self.assertLessEqual(cache.reserved_bytes,cache.maximum_bytes)

    def test_annulus_and_final_check_share_only_actual_geometric_pairs(self):
        work = module.AuthoringWork()
        obstacle = mesh(list(self.B),[(0,1,2)],work)
        clearance = module.AnnulusClearance(list(self.A),[("source-near",obstacle)],work)
        self.assertTrue(clearance.allows((0,1,2)))
        before = work.counts["exact_operations"]
        combined = mesh(list(self.A)+list(self.B),[(0,1,2),(3,4,5)],work)
        report = module.module_intersections(combined,work)
        self.assertTrue(report["clear"])
        self.assertEqual(before,work.counts["exact_operations"])
        self.assertEqual(1,work.pair_cache.hits)
        expected = module.quality.intersections(combined,module.quality.Work())
        self.assertEqual(expected,report)

    def test_complete_cached_build_matches_uncached_geometry_and_reports(self):
        baseline = build()
        work = module.AuthoringWork()
        original = mesh(work=work)
        actual = module.build_geometry(original,original,original,recipe(),work)
        self.assertEqual(baseline[0],actual[0])
        self.assertEqual(baseline[2:],actual[2:])
        for name,candidate in baseline[1].items():
            self.assertEqual(candidate.positions,actual[1][name].positions)
            self.assertEqual(candidate.triangles,actual[1][name].triangles)
        self.assertGreater(work.pair_cache.hits,0)
        self.assertLessEqual(work.pair_cache.reserved_bytes,64*1024*1024)


class BoundaryTests(unittest.TestCase):
    def test_actual_outer_rim_has_all_source_edges_in_original_direction(self):
        work, original = module.quality.Work(), mesh()
        cycle = module.outer_cycle(original, 4, work)
        actual = set(zip(cycle, cycle[1:]+cycle[:1]))
        self.assertEqual(module.quality.topology(original)["boundaries"], actual)
        arcs = module.pinned_arcs(cycle, original, recipe()["anchors"], work)
        self.assertEqual({"crest", "toe", "left", "right"}, {arc["role"] for arc in arcs})
        self.assertEqual(4, sum(len(arc["points"])-1 for arc in arcs))

    def test_moved_or_interior_anchor_refuses_instead_of_nearest_snapping(self):
        original, work = mesh(), module.quality.Work()
        cycle = module.outer_cycle(original, 4, work)
        value = recipe()
        value["anchors"]["crest-left"]["positionMetres"][0] += 1e-6
        with self.assertRaisesRegex(module.author.QualificationError, "anchor moved"):
            module.pinned_arcs(cycle, original, value["anchors"], work)
        interior = (0., .5, 0.)
        fan = mesh(POSITIONS+[interior], [(0, 2, 4), (2, 3, 4), (3, 1, 4), (1, 0, 4)])
        value = recipe()
        value["anchors"]["crest-left"] = dict(physicalVertex=4, positionMetres=list(interior))
        with self.assertRaisesRegex(module.author.QualificationError, "not on the actual"):
            module.pinned_arcs(cycle, fan, value["anchors"], work)

    def test_crossed_anchor_semantics_refuse(self):
        value = recipe()
        value["anchors"]["crest-right"], value["anchors"]["toe-right"] = value["anchors"]["toe-right"], value["anchors"]["crest-right"]
        with self.assertRaisesRegex(module.author.QualificationError, "Anchor order"):
            build(value)

    def test_boundary_count_and_second_component_refuse(self):
        with self.assertRaisesRegex(module.author.QualificationError, "edge count"):
            module.outer_cycle(mesh(), 5, module.quality.Work())
        other = [(p[0]+5, p[1], p[2]) for p in POSITIONS]
        disconnected = mesh(POSITIONS+other, TRIANGLES+[(a+4, b+4, c+4) for a, b, c in TRIANGLES])
        with self.assertRaises(module.author.QualificationError):
            module.outer_cycle(disconnected, 8, module.quality.Work())

    def test_interior_hole_is_never_treated_as_cap_boundary(self):
        positions = [(0.,0.,0.), (3.,0.,0.), (3.,0.,3.), (0.,0.,3.),
                     (1.,0.,1.), (2.,0.,1.), (2.,0.,2.), (1.,0.,2.)]
        triangles = [(0,4,1),(1,4,5),(1,5,2),(2,5,6),(2,6,3),(3,6,7),(3,7,0),(0,7,4)]
        with self.assertRaises(module.author.QualificationError):
            module.outer_cycle(mesh(positions, triangles), 8, module.quality.Work())

    def test_physical_boundary_shift_refuses_exact_correspondence(self):
        work = module.quality.Work()
        changed = list(POSITIONS)
        changed[0] = (-1.01, 1., -.5)
        with self.assertRaisesRegex(module.author.QualificationError, "LOD outer rim differs"):
            module.build_geometry(mesh(), mesh(changed), mesh(), recipe(), work)


class ConstructiveTests(unittest.TestCase):
    def test_closed_candidate_preserves_every_original_position_face_and_winding(self):
        closure, candidates, mappings, reports = build()
        self.assertEqual(32, len(closure.triangles))  # 8 collar + 10 annulus + 10 walls + 4 base.
        self.assertEqual(15, len(closure.positions)-len(closure.boundary))
        for name, candidate in candidates.items():
            self.assertEqual(POSITIONS, candidate.positions[:4])
            self.assertEqual(TRIANGLES, candidate.triangles[:2])
            self.assertEqual(34, len(candidate.triangles))
            self.assertEqual(0, module.quality.topology(candidate)["geometricOpenEdges"])
            self.assertEqual(2, module.quality.topology(candidate)["componentEulerSum"])
            for i, point in enumerate(closure.positions): self.assertEqual(point, candidate.positions[mappings[name][i]])
        self.assertTrue(reports["near"]["intersections"]["clear"])
        self.assertGreater(reports["near"]["signedVolumeCubicMetres"], 0)
        self.assertEqual("physical", reports["far"]["reuses"])

    def test_every_original_rim_edge_meets_one_new_triangle_oppositely(self):
        closure, candidates, _, _ = build()
        candidate = candidates["physical"]
        original_edges = module.quality.topology(mesh())["boundaries"]
        directed = []
        for triangle in candidate.triangles[2:]:
            points = [candidate.positions[v] for v in triangle]
            directed.extend(zip(points, points[1:]+points[:1]))
        for a, b in original_edges:
            self.assertEqual(1, directed.count((b, a)))
            self.assertEqual(0, directed.count((a, b)))

    def test_float32_geometry_is_identical_in_all_representations(self):
        closure, candidates, mappings, _ = build()
        for point in closure.positions:
            self.assertEqual(point, tuple(module.f32(v) for v in point))
        for f, triangle in enumerate(closure.triangles):
            expected = tuple(closure.positions[v] for v in triangle)
            for name, candidate in candidates.items(): self.assertEqual(expected, candidate.points(2+f))

    def test_semantics_separate_cap_apron_returns_walls_and_base_without_habitat(self):
        closure, _, _, _ = build()
        self.assertTrue(set(closure.groups) <= set(module.GROUP_COLORS))
        self.assertTrue((set(module.GROUP_COLORS)-{"transition-interior-candidate"}) <= set(closure.groups))
        self.assertTrue(all(not arc["contactApproved"] and not arc["rootApproved"] for arc in closure.arcs))
        self.assertFalse(closure.annulus["contactApproved"])
        self.assertFalse(closure.annulus["rootApproved"])
        self.assertTrue(closure.annulus["projectedGraphVerified"])
        self.assertEqual(module.f32(-.4), closure.base_y)

    def test_annulus_replacement_preserves_all_original_collar_vertices_and_faces(self):
        closure, _, _, _ = build()
        expected = module.collar_positions(closure.boundary,mesh(),recipe()["dimensionsMetres"]["collarWidth"],module.quality.Work())
        self.assertEqual(expected,closure.positions[4:8])
        for arc in closure.arcs:
            inner,collar = arc["boundaryPointIds"],arc["collarPointIds"]
            triangles = []
            for i in range(len(inner)-1):
                triangles.extend(((inner[i+1],inner[i],collar[i]),(inner[i+1],collar[i],collar[i+1])))
            self.assertEqual(triangles,[closure.triangles[f] for f in arc["addedFaceOrdinals"]])
        self.assertEqual(10,len(closure.annulus["addedFaceOrdinals"]))

    def test_global_guide_records_rounded_envelope_and_contains_exact_collar(self):
        closure, _, _, _ = build()
        envelope = closure.annulus["guideEnvelope"]
        collar = closure.positions[4:8]
        self.assertEqual(module.f32(max(p[1] for p in collar)+.08),envelope["crestYMetres"])
        self.assertEqual(module.f32(min(p[1] for p in collar)+.02),envelope["toeYMetres"])
        self.assertEqual("global-collar-y-envelope-v1",closure.annulus["outerGuideRule"])
        low,high = (module.Fraction(envelope[key]) for key in ("projectedMinimumYminusZExact","projectedMaximumYminusZExact"))
        for p in collar:
            self.assertLess(low,module.Fraction(p[1])-module.Fraction(p[2]))
            self.assertLess(module.Fraction(p[1])-module.Fraction(p[2]),high)
        for arc in closure.arcs:
            points = [closure.positions[i] for i in arc["outerGuidePointIds"]]
            if arc["role"] in ("crest","toe"):
                self.assertEqual({envelope[arc["role"]+"YMetres"]},{p[1] for p in points})
            else:
                self.assertEqual(1,len({p[0] for p in points}))

    def test_uneven_crest_uses_one_plane_without_erasing_source_or_collar(self):
        positions = [(-1.,1.,-.5),(0.,1.25,-.5),(1.,1.,-.5),(1.,0.,.5),
                     (0.,.125,.5),(-1.,0.,.5),(0.,.5,0.)]
        cycle_ids = [0,5,4,3,2,1]
        triangles = [(a,cycle_ids[(i+1)%6],6) for i,a in enumerate(cycle_ids)]
        original,work = mesh(positions,triangles),module.quality.Work()
        value = recipe()
        value["expectedOuterEdges"] = 6
        for name,index in (("crest-left",0),("crest-right",2),("toe-left",5),("toe-right",3)):
            value["anchors"][name] = dict(physicalVertex=index,positionMetres=list(positions[index]))
        closure,candidates,_,reports = module.build_geometry(original,original,original,value,work)
        crest = next(arc for arc in closure.arcs if arc["role"] == "crest")
        self.assertEqual(3,len(crest["outerGuidePointIds"]))
        guide = [closure.positions[i] for i in crest["outerGuidePointIds"]]
        self.assertEqual(1,len({p[1] for p in guide}))
        self.assertEqual(1,len({p[2] for p in guide}))
        self.assertEqual(positions,candidates["near"].positions[:len(positions)])
        expected_collar = module.collar_positions(closure.boundary,original,.12,module.quality.Work())
        self.assertEqual(expected_collar,closure.positions[6:12])
        self.assertTrue(reports["near"]["intersections"]["clear"])

    def test_return_legs_and_back_wall_share_every_boundary_segment_once(self):
        closure, _, _, _ = build()
        profile = closure.annulus["returnRailProfile"]
        self.assertEqual(module.f32(closure.base_y+.10),profile["bendYMetres"])
        self.assertEqual([-1.,0.,-.5],profile["sourceMinimumMetres"])
        self.assertEqual([1.,1.,.5],profile["sourceMaximumMetres"])
        self.assertEqual(2,len(profile["bendPointIds"]))
        wall_edges = {}
        for patch_row in profile["wallPatches"]:
            triangles = [closure.triangles[f] for f in patch_row["addedFaceOrdinals"]]
            wall = mesh(closure.positions,triangles)
            expected = [closure.positions[v] for v in patch_row["boundaryPointIds"]]
            self.assertEqual(set(zip(expected,expected[1:]+expected[:1])),module.quality.topology(wall)["boundaries"])
            wall_edges[patch_row["group"]] = module.quality.topology(wall)["boundaries"]
        for arc in closure.arcs:
            if arc["role"] not in ("left","right"): continue
            guide_times = arc["outerGuideProfileParameters"]
            self.assertEqual(set(arc["sourceArcParameters"]),set(guide_times)-{guide_times[1]})
            rear_points = [closure.positions[v] for v in arc["rearGuidePointIds"]]
            lower_points = [closure.positions[v] for v in arc["lowerGuidePointIds"]]
            self.assertEqual(1,len({p[2] for p in rear_points}))
            self.assertTrue(all(p[1] < 0 for p in lower_points))
            for a,b in zip(rear_points,rear_points[1:]):
                self.assertIn((b,a),wall_edges["back-wall"])
                self.assertNotIn((b,a),wall_edges[arc["role"]+"-wall"])
            for a,b in zip(lower_points,lower_points[1:]):
                self.assertIn((b,a),wall_edges[arc["role"]+"-wall"])
                self.assertNotIn((b,a),wall_edges["back-wall"])
        # Whole-candidate proof also checks the annulus side of each shared edge.
        self.assertTrue(all(len(ids) >= 3 for ids in (p["boundaryPointIds"] for p in profile["wallPatches"])))

    def test_return_profile_refuses_when_explicit_base_cannot_clear_collar(self):
        value = recipe()
        value["dimensionsMetres"]["baseBelowLowestSource"] = .15
        with self.assertRaisesRegex(module.author.QualificationError,"return rail base clearance"):
            build(value)

    def test_every_wall_and_base_face_has_full_source_separating_plane(self):
        closure, _, _, _ = build()
        low = [min(p[i] for p in POSITIONS) for i in range(3)]
        high = [max(p[i] for p in POSITIONS) for i in range(3)]
        for triangle, group in zip(closure.triangles, closure.groups):
            if group.endswith("wall") or group == "base":
                self.assertTrue(module.outside_source_envelope([closure.positions[v] for v in triangle], low, high))
        # An AABB overlap cannot pass merely because every vertex is outside on
        # a different side. One separating plane must contain the entire face.
        self.assertFalse(module.outside_source_envelope([(-2.,.5,0.),(2.,.5,0.),(0.,.5,2.)], low, high))

    def test_shape_following_collar_clears_protruding_low_side_before_coarsening(self):
        # The left arc descends below its endpoint chord and protrudes beyond
        # the crest/toe X coordinates: sparse Y/Z remapping is unsafe here.
        positions = [(-1.,1.,-.5), (1.,1.,-.5), (1.,0.,.5), (-.8,0.,.5),
                     (-1.3,0.,-.2), (-1.1,.1,-.5), (0.,.5,0.)]
        cycle_ids = [0,5,4,3,2,1]
        triangles = [(a, cycle_ids[(i+1)%6], 6) for i,a in enumerate(cycle_ids)]
        original, work = mesh(positions, triangles), module.quality.Work()
        value = recipe()
        value["expectedOuterEdges"] = 6
        for name,index in (("crest-left",0),("crest-right",1),("toe-left",3),("toe-right",2)):
            value["anchors"][name] = dict(physicalVertex=index, positionMetres=list(positions[index]))
        closure, candidates, mappings, reports = module.build_geometry(original, original, original, value, work)
        left = next(arc for arc in closure.arcs if arc["role"] == "left")
        i = left["boundaryPointIds"].index(closure.boundary.index(positions[4]))
        low_collar = closure.positions[left["collarPointIds"][i]]
        self.assertLess(low_collar[1], 0)
        self.assertLess(low_collar[0], positions[4][0])
        self.assertTrue(reports["near"]["intersections"]["clear"])
        self.assertEqual(42, len(closure.triangles))
        self.assertEqual(19, len(closure.positions)-len(closure.boundary))
        self.assertEqual(positions, candidates["physical"].positions[:len(positions)])

    def test_explicit_base_depth_must_clear_the_whole_collar(self):
        value = recipe()
        value["dimensionsMetres"]["baseBelowLowestSource"] = .05
        value["dimensionsMetres"]["collarWidth"] = .25
        with self.assertRaisesRegex(module.author.QualificationError, "base depth"):
            build(value)

    def test_notched_envelope_wall_is_triangulated_without_fan_overlap(self):
        # All points lie on X=-2. The low central notch makes a one-corner fan
        # invalid; every notch edge and the collinear bottom split must survive.
        positions = [(-2.,2.,0.),(-2.,.2,1.),(-2.,2.,2.),(-2.,2.,3.),
                     (-2.,0.,3.),(-2.,0.,1.),(-2.,0.,0.)]
        work = module.quality.Work()
        triangles = module.triangulate_planar_polygon(list(range(7)), positions, work)
        candidate = mesh(positions, triangles, work)
        self.assertTrue(module.quality.intersections(candidate, work)["clear"])
        expected = {(positions[i],positions[(i+1)%7]) for i in range(7)}
        self.assertEqual(expected, module.quality.topology(candidate)["boundaries"])
        self.assertEqual(5, len(triangles))

    def test_physical_fan_split_indices_can_stay_distinct_at_exact_position(self):
        work = module.quality.Work()
        physical = mesh(POSITIONS+[POSITIONS[1]], [(0,2,1), (4,2,3)])
        closure, candidates, _, _ = module.build_geometry(mesh(), physical, mesh(), recipe(), work)
        candidate = candidates["physical"]
        self.assertEqual(physical.positions, candidate.positions[:5])
        self.assertEqual(physical.triangles, candidate.triangles[:2])
        self.assertEqual(0, module.quality.topology(candidate)["geometricOpenEdges"])
        self.assertGreater(module.quality.topology(candidate)["indexedOpenEdges"], 0)

    def test_deterministic_faces_positions_semantic_order_and_mappings(self):
        a, ma, maps_a, reports_a = build()
        b, mb, maps_b, reports_b = build()
        self.assertEqual(a, b)
        self.assertEqual(maps_a, maps_b)
        self.assertEqual(reports_a, reports_b)
        for name in ma:
            self.assertEqual(ma[name].positions, mb[name].positions)
            self.assertEqual(ma[name].triangles, mb[name].triangles)

    def test_constraints_never_silently_reduce_source_or_exceed_budgets(self):
        for key, amount in (("maximumAddedTriangles", 10), ("maximumAddedVertices", 4), ("maximumPhysicalTriangles", 3)):
            value = recipe()
            value["budgets"][key] = amount
            if key == "maximumAddedTriangles":
                # Keep the UV/geometry capacity contract coherent so this case
                # reaches the intended geometry budget refusal.
                value["uvAuthoring"].update(maximumFaces=amount, maximumCharts=amount,
                                            maximumEdges=3 * amount)
                value["normalAuthoring"].update(maximumFaces=amount, maximumCorners=3 * amount,
                                                maximumEdges=3 * amount)
            with self.assertRaisesRegex(module.author.QualificationError, "budget exceeded"):
                build(value)

    def test_inverted_source_is_not_silently_reversed(self):
        original = mesh(triangles=[tuple(reversed(t)) for t in TRIANGLES])
        with self.assertRaisesRegex(module.author.QualificationError, "positive outward volume"):
            module.build_geometry(original, original, original, recipe(), module.quality.Work())

    def test_exact_geometry_work_cap_refuses_instead_of_clear(self):
        work = module.quality.Work(replace(module.quality.Limits(), maximum_pair_tests=0))
        original = mesh(work=work)
        with self.assertRaises(module.quality.Incomplete):
            module.build_geometry(original, original, original, recipe(), work)

    def test_degenerate_generated_geometry_is_refused(self):
        closure, _, _, _ = build()
        closure.triangles[0] = (0, 0, 1)
        with self.assertRaisesRegex(module.author.QualificationError, "Degenerate"):
            module.compose(mesh(), closure, module.quality.Work())

    def test_pinched_closed_vertex_link_refuses_even_with_manifold_edges(self):
        # Two tetrahedra share only one geometric point: every edge has two
        # incident faces, but the shared point has two disconnected link cycles.
        positions = [(0.,0.,0.), (1.,0.,0.), (0.,1.,0.), (0.,0.,1.),
                     (-1.,0.,0.), (0.,-1.,0.), (0.,0.,-1.)]
        first = [(0,2,1),(0,1,3),(0,3,2),(1,2,3)]
        second = [(0,4,5),(0,6,4),(0,5,6),(4,6,5)]
        with self.assertRaisesRegex(module.author.QualificationError, "pinched/disconnected"):
            module.closed_vertex_links(mesh(positions, first+second), module.quality.Work())

    def test_positive_volume_closed_edge_topology_still_rejects_crossing(self):
        # An octahedron apex passes through the opposite pyramid's side. Its
        # topology and positive signed volume survive; global geometry does not.
        positions = [(1.,0.,0.), (0.,0.,1.), (-1.,0.,0.), (0.,0.,-1.), (2.,-.2,0.), (0.,-1.,0.)]
        triangles = []
        for i in range(4): triangles.extend(((i,4,(i+1)%4), (i,(i+1)%4,5)))
        candidate = mesh(positions, triangles)
        self.assertEqual(0, module.quality.topology(candidate)["geometricOpenEdges"])
        with self.assertRaisesRegex(module.author.QualificationError, "self-intersects"):
            module.qualify_closed(candidate, module.quality.Work())


class ReturnProfileTests(unittest.TestCase):
    def test_bend_is_explicit_and_events_follow_both_legs_without_a_shortcut(self):
        start,bend,end = (-2.,3.,-2.),(-2.,-.3,-2.),(-2.,-.1,2.)
        source_times = [0.,.15,.6,1.]
        times,points,bend_index = module.return_profile_points(start,bend,end,source_times)
        self.assertEqual(5,len(times))
        self.assertEqual(tuple(module.f32(v) for v in bend),points[bend_index])
        self.assertTrue(set(source_times) <= set(times))
        self.assertTrue(all(p[2] == -2 for p in points[:bend_index+1]))
        self.assertTrue(all(p[1] < 0 for p in points[bend_index:]))
        values = [module.Fraction(p[1])-module.Fraction(p[2]) for p in points]
        self.assertTrue(all(a > b for a,b in zip(values,values[1:])))
        backward = module.return_profile_points(end,bend,start,[0.,1.])
        self.assertEqual([tuple(module.f32(v) for v in p) for p in (end,bend,start)],backward[1])
        # An existing event at the exact bend is retained, never duplicated.
        t = module.math.dist(start,bend)/(module.math.dist(start,bend)+module.math.dist(bend,end))
        same_times,same_points,index = module.return_profile_points(start,bend,end,[0.,t,1.])
        self.assertEqual([0.,t,1.],same_times)
        self.assertEqual(1,index)
        self.assertEqual(3,len(same_points))

    def test_bad_or_rounded_duplicate_events_refuse_without_welding(self):
        points = ((-2.,3.,-2.),(-2.,-.3,-2.),(-2.,-.1,2.))
        for times in ([0.,0.,1.],[0.,float("nan"),1.],[0.,1e-20,1.]):
            with self.assertRaises(module.author.QualificationError):
                module.return_profile_points(*points,times)

    def test_measured_wrong_way_witness_has_an_exterior_rear_profile_route(self):
        # Pinned from contact-view-left-r1 witness0. This is only its local
        # obstruction regression; it is not acceptance of the entire module.
        a = (-9.867741584777832,2.145522117614746,-2.28536319732666)
        b = (-9.808635711669922,2.054175615310669,-2.288388252258301)
        old_guide = (-10.30000114440918,3.4392788410186768,-.8958241939544678)
        obstacle = mesh([(-9.811481475830078,2.0584025382995605,-2.1684963703155518),
                         (-9.818360328674316,2.1706578731536865,-2.178922414779663),a],[(0,1,2)])
        start = (-10.30000114440918,6.,module.f32(-2.203990936279297-1.2))
        bend,end = (start[0],module.f32(-.4+.10),start[2]),(start[0],-.1,module.f32(2.203990936279297+.8))
        _,profile,_ = module.return_profile_points(start,bend,end,[0.,.2,1.])
        new_guide = profile[1]
        self.assertEqual(start[2],new_guide[2])
        clearance = module.AnnulusClearance([a,b,old_guide,new_guide],[("retained-closure",obstacle)],module.quality.Work())
        self.assertFalse(clearance.allows((0,1,2)))
        self.assertEqual("interior-crossing",clearance.last_witness["classification"])
        self.assertTrue(clearance.allows((0,1,3)))


class AnnulusTests(unittest.TestCase):
    INNER = [(-1.,-1.),(1.,-1.),(1.,1.),(-1.,1.)]
    OUTER = [(-3.,-3.),(3.,-3.),(3.,3.),(-3.,3.)]

    def triangulate(self,inner=None,outer=None,curved=False):
        inner = self.INNER if inner is None else inner
        outer = self.OUTER if outer is None else outer
        points = inner+outer
        # (X,Y-Z) exactly reproduces the supplied2D vertices. Nonuniform W
        # demonstrates that the annulus is a graph rather than a planar fill.
        heights = [float(i%3-1) if curved else v for i,(u,v) in enumerate(points)]
        positions = [(u,(v+w)/2,(w-v)/2) for (u,v),w in zip(points,heights)]
        ids = list(range(len(inner))),list(range(len(inner),len(points)))
        work = module.quality.Work()
        triangles = module.triangulate_projected_annulus(*ids,positions,work)
        return positions,triangles,ids

    def assert_annulus(self,positions,triangles,ids):
        original = mesh(positions,triangles)
        report = module.quality.topology(original)
        self.assertEqual(0,report["componentEulerSum"])
        self.assertEqual(1,report["edgeConnectedComponents"])
        self.assertEqual(len(positions),report["geometricOpenEdges"])
        expected = {(positions[a],positions[b]) for a,b in zip(ids[1],ids[1][1:]+ids[1][:1])}
        expected |= {(positions[b],positions[a]) for a,b in zip(ids[0],ids[0][1:]+ids[0][:1])}
        self.assertEqual(expected,report["boundaries"])
        self.assertTrue(module.quality.intersections(original,module.quality.Work())["clear"])

    def test_hole_is_preserved_and_never_filled(self):
        positions,triangles,ids = self.triangulate()
        self.assertEqual(8,len(triangles))
        self.assert_annulus(positions,triangles,ids)
        for triangle in triangles:
            center = tuple(sum(positions[i][a] for i in triangle)/3 for a in range(3))
            self.assertFalse(-1 < center[0] < 1 and -1 < center[1]-center[2] < 1)

    def test_concave_hole_and_nonuniform_depth_remain_one_embedded_graph(self):
        inner = [(0.,0.),(4.,0.),(4.,4.),(3.,4.),(3.,1.),(1.,1.),(1.,4.),(0.,4.)]
        # An explicit outer port sees the notch. Without it this boundary-edge
        # zipper class requires a same-ring diagonal or authored Steiner point.
        outer = [(-2.,-2.),(6.,-2.),(6.,6.),(2.,6.),(-2.,6.)]
        self.assert_annulus(*self.triangulate(inner,outer,True))

    def test_collinear_segments_survive_without_simplification(self):
        inner = [(-1.,-1.),(0.,-1.),(1.,-1.),(1.,1.),(-1.,1.)]
        outer = [(-3.,-3.),(0.,-3.),(3.,-3.),(3.,3.),(0.,3.),(-3.,3.)]
        positions,triangles,ids = self.triangulate(inner,outer)
        self.assertEqual(11,len(triangles))
        self.assert_annulus(positions,triangles,ids)

    def test_winding_is_preserved_and_loop_rotations_are_deterministic(self):
        positions,triangles,ids = self.triangulate()
        rotated = [row[1:]+row[:1] for row in ids]
        again = module.triangulate_projected_annulus(*rotated,positions,module.quality.Work())
        self.assertEqual(triangles,again)
        reversed_ids = [list(reversed(row)) for row in ids]
        reverse = module.triangulate_projected_annulus(*reversed_ids,positions,module.quality.Work())
        self.assertEqual([tuple(reversed(t)) for t in triangles],reverse)
        self.assert_annulus(positions,reverse,reversed_ids)

    def test_touching_crossed_noncontaining_or_opposite_winding_loops_refuse(self):
        for outer in (
            [(-1.,-3.),(3.,-3.),(3.,3.),(-1.,3.)],  # Touches collar.
            [(-3.,-3.),(3.,3.),(-3.,3.),(3.,-3.)],  # Crosses itself.
            [(0.,-3.),(3.,-3.),(3.,3.),(0.,3.)],    # Clips collar.
            list(reversed(self.OUTER)),
        ):
            with self.assertRaises(module.author.QualificationError): self.triangulate(outer=outer)

    def test_float32_rounding_is_checked_before_projected_topology(self):
        positions,_,ids = self.triangulate()
        positions[0] = (positions[0][0]+1e-10,*positions[0][1:])
        with self.assertRaisesRegex(module.author.QualificationError,"float32"):
            module.triangulate_projected_annulus(*ids,positions,module.quality.Work())

    def test_bounded_annulus_work_refuses_instead_of_partial_output(self):
        positions,_,ids = self.triangulate()
        work = module.quality.Work(replace(module.quality.Limits(),maximum_topology_steps=0))
        with self.assertRaises(module.quality.Incomplete):
            module.triangulate_projected_annulus(*ids,positions,work)


class AnnulusClearanceTests(unittest.TestCase):
    @staticmethod
    def lifted(u,v,w):
        return (u,(v+w)/2,(w-v)/2)

    def test_obstructed_greedy_zipper_is_replaced_without_moving_vertices(self):
        positions,obstruction,obstacle,ids = GlobalZipperTests.trap()
        initial = module.triangulate_projected_annulus(*ids,positions,module.quality.Work())
        work = module.quality.Work()
        clearance = module.AnnulusClearance(positions,[("source-near",obstacle)],work)
        self.assertEqual((0,4,5),initial[0])
        self.assertTrue(clearance.allows(initial[0]))  # Valid but traps the next step.
        self.assertFalse(clearance.allows((1,0,5)))    # Flat remaining strip crosses.
        self.assertTrue(clearance.allows((1,0,4)))     # Preserve raised guide port4.
        self.assertTrue(clearance.allows((1,4,5)))
        before = list(positions)
        triangles = module.triangulate_projected_annulus(*ids,positions,work,clearance)
        self.assertEqual(before,positions)
        self.assertNotEqual(initial,triangles)
        self.assertEqual((1,0,4),triangles[0])
        self.assertGreater(clearance.rejected,0)
        self.assertTrue(clearance.zipper_selection["complete"])
        self.assertTrue(clearance.zipper_selection["solution"])
        self.assertGreater(clearance.zipper_selection["backtracks"],0)
        self.assertTrue(all(clearance.allows(t) for t in triangles))
        combined = mesh(positions+obstruction,triangles+[(8,9,10)])
        self.assertTrue(module.quality.intersections(combined,module.quality.Work())["clear"])

    def test_unavoidable_obstacle_reports_bounded_original_face_witness(self):
        positions,_,ids = AnnulusTests().triangulate()
        obstacle = mesh([(-10.,-10.,0.),(10.,-10.,0.),(0.,10.,0.)],[(0,1,2)])
        obstacle.source_triangle_ids = [177]
        work = module.quality.Work()
        clearance = module.AnnulusClearance(positions,[("source-near",obstacle)],work)
        with self.assertRaisesRegex(module.author.QualificationError,"No admissible global annulus zipper") as caught:
            module.triangulate_projected_annulus(*ids,positions,work,clearance)
        report = json.loads(str(caught.exception).split(": ",1)[1])
        self.assertTrue(0 < len(report["blockingTriangleWitnesses"]) <= 8)
        witness = report["blockingTriangleWitnesses"][0]
        self.assertEqual("source-near",witness["obstacle"])
        self.assertEqual(0,witness["obstacleTriangle"])
        self.assertEqual(177,witness["originalSourceFace"])
        self.assertIn("does not prove",report["scope"])

    def test_repeated_candidate_uses_bounded_cache_independent_of_winding(self):
        positions = [(0.,0.,0.),(1.,0.,0.),(0.,1.,0.)]
        obstacle = mesh([(.25,.25,-1.),(.25,.25,1.),(.75,.25,0.)],[(0,1,2)])
        work = module.quality.Work()
        clearance = module.AnnulusClearance(positions,[("source-near",obstacle)],work)
        self.assertFalse(clearance.allows((0,1,2)))
        before = clearance.pair_tests,clearance.node_visits
        witness = clearance.last_witness
        self.assertFalse(clearance.allows((2,1,0)))
        self.assertEqual(before,(clearance.pair_tests,clearance.node_visits))
        self.assertEqual(witness,clearance.last_witness)
        self.assertEqual(1,clearance.cache_hits)

    def test_shared_retained_edge_does_not_exempt_positive_area_overlap(self):
        positions = [(0.,0.,0.),(2.,0.,0.),(0.,1.,0.),(0.,-1.,0.)]
        retained = mesh([(0.,0.,0.),(2.,0.,0.),(0.,2.,0.)],[(0,1,2)])
        clearance = module.AnnulusClearance(positions,[("retained-closure",retained)],module.quality.Work())
        self.assertFalse(clearance.allows((0,1,2)))
        self.assertEqual("coplanar-overlap",clearance.last_witness["classification"])
        self.assertTrue(clearance.allows((0,1,3)))

    def test_distinct_exact_coordinate_scales_do_not_hide_a_thin_crossing(self):
        positions = [(0.,0.,0.),(1.,0.,0.),(0.,1.,0.)]
        tiny = 2**-20
        obstacle = mesh([(.25,.25,-tiny),(.25,.25,tiny),(.5,.25,0.)],[(0,1,2)])
        clearance = module.AnnulusClearance(positions,[("source-near",obstacle)],module.quality.Work())
        self.assertGreater(clearance.scale,1)
        self.assertFalse(clearance.allows((0,1,2)))

    def test_candidate_node_and_pair_caps_refuse_without_false_clear(self):
        positions = [(0.,0.,0.),(1.,0.,0.),(0.,1.,0.),(0.,-1.,0.)]
        far = mesh([(10.,10.,0.),(11.,10.,0.),(10.,11.,0.)],[(0,1,2)])
        clearance = module.AnnulusClearance(positions,[("far",far)],module.quality.Work())
        clearance.MAXIMUM_CANDIDATES = 1
        self.assertTrue(clearance.allows((0,1,2)))
        with self.assertRaisesRegex(module.quality.Incomplete,"candidate budget"):
            clearance.allows((0,1,3))
        clearance = module.AnnulusClearance(positions,[("far",far)],module.quality.Work())
        clearance.MAXIMUM_NODE_VISITS = 0
        with self.assertRaisesRegex(module.quality.Incomplete,"node_visits budget"):
            clearance.allows((0,1,2))
        crossing = mesh([(.25,.25,-1.),(.25,.25,1.),(.75,.25,0.)],[(0,1,2)])
        clearance = module.AnnulusClearance(positions,[("source-near",crossing)],module.quality.Work())
        clearance.MAXIMUM_PAIR_TESTS = 0
        with self.assertRaisesRegex(module.quality.Incomplete,"pair_tests budget"):
            clearance.allows((0,1,2))


class GlobalZipperTests(unittest.TestCase):
    @staticmethod
    def trap():
        # Guide corner4 is the only raised vertex. Advancing past it first is
        # locally clear but leaves the next collar-edge triangle flat across the
        # obstruction. The opposite advance preserves corner4 to roof this strip.
        uv = AnnulusTests.INNER+AnnulusTests.OUTER
        positions = [AnnulusClearanceTests.lifted(u,v,1. if i == 4 else 0.) for i,(u,v) in enumerate(uv)]
        obstacle_positions = [AnnulusClearanceTests.lifted(1.-.015625,-1.5,-.015625),
            AnnulusClearanceTests.lifted(1.+.015625,-1.5,-.015625),
            AnnulusClearanceTests.lifted(1.,-1.5,.015625)]
        return positions,obstacle_positions,mesh(obstacle_positions,[(0,1,2)]),(list(range(4)),list(range(4,8)))

    def test_state_and_aggregate_work_caps_refuse_without_solution(self):
        positions,_,obstacle,ids = self.trap()
        work = module.quality.Work()
        clearance = module.AnnulusClearance(positions,[("source-near",obstacle)],work)
        with patch.object(module,"ZIPPER_MAXIMUM_STATES",1):
            with self.assertRaisesRegex(module.quality.Incomplete,"state budget"):
                module.triangulate_projected_annulus(*ids,positions,work,clearance)
        self.assertFalse(clearance.zipper_selection["complete"])
        self.assertFalse(clearance.zipper_selection["solution"])
        self.assertEqual(1,clearance.zipper_selection["states"])
        work = module.quality.Work(replace(module.quality.Limits(),maximum_topology_steps=0))
        clearance = module.AnnulusClearance(positions,[("source-near",obstacle)],work)
        with self.assertRaises(module.quality.Incomplete):
            module.triangulate_projected_annulus(*ids,positions,work,clearance)

    def test_fixed_zipper_class_refuses_hidden_notch_without_claiming_global_impossibility(self):
        inner = [(0.,0.),(4.,0.),(4.,4.),(3.,4.),(3.,1.),(1.,1.),(1.,4.),(0.,4.)]
        outer = [(-2.,-2.),(6.,-2.),(6.,6.),(-2.,6.)]
        with self.assertRaisesRegex(module.author.QualificationError,"No admissible global annulus zipper") as caught:
            AnnulusTests().triangulate(inner,outer)
        report = json.loads(str(caught.exception).split(": ",1)[1])
        self.assertTrue(report["search"]["complete"])
        self.assertFalse(report["search"]["solution"])
        self.assertLessEqual(report["search"]["transitionChecks"],2*report["search"]["states"])
        self.assertLessEqual(len(report["geometricWitnesses"]),8)
        self.assertIn("Fixed-bridge zipper class exhausted",report["scope"])
        self.assertIn("does not prove",report["scope"])
        # An explicit visible boundary port changes the authored input, never an
        # automatic fallback or hidden inserted vertex in the production search.
        outer.insert(3,(2.,6.))
        AnnulusTests().assert_annulus(*AnnulusTests().triangulate(inner,outer))

    def test_every_triangle_preserves_an_original_ring_edge_without_same_ring_shortcuts(self):
        positions,triangles,ids = AnnulusTests().triangulate()
        edges = {frozenset((a,b)) for ring in ids for a,b in zip(ring,ring[1:]+ring[:1])}
        inner,outer = map(set,ids)
        self.assertEqual(len(positions),len(triangles))
        for triangle in triangles:
            self.assertTrue(set(triangle)&inner and set(triangle)&outer)
            self.assertTrue(any(frozenset((a,b)) in edges for a,b in zip(triangle,triangle[1:]+triangle[:1])))

    def test_global_backtracking_is_deterministic_under_loop_rotation_and_winding(self):
        positions,_,obstacle,ids = self.trap()
        def solve(rings):
            work = module.quality.Work()
            clearance = module.AnnulusClearance(positions,[("source-near",obstacle)],work)
            triangles = module.triangulate_projected_annulus(*rings,positions,work,clearance)
            return triangles,clearance.zipper_selection
        first,report = solve(ids)
        rotated,again = solve([ring[2:]+ring[:2] for ring in ids])
        reverse,_ = solve([list(reversed(ring)) for ring in ids])
        self.assertEqual(first,rotated)
        self.assertEqual(report,again)
        self.assertEqual([tuple(reversed(t)) for t in first],reverse)
        self.assertGreater(report["backtracks"],0)
        self.assertLessEqual(report["transitionChecks"],2*report["states"])
        self.assertLessEqual(report["connectors"],report["maximumConnectors"])

    def test_complete_no_solution_retains_original_source_face_witness(self):
        positions,_,ids = AnnulusTests().triangulate()
        obstacle = mesh([(-10.,-10.,0.),(10.,-10.,0.),(0.,10.,0.)],[(0,1,2)])
        obstacle.source_triangle_ids = [177]
        work = module.quality.Work()
        clearance = module.AnnulusClearance(positions,[("source-near",obstacle)],work)
        with self.assertRaises(module.author.QualificationError) as caught:
            module.triangulate_projected_annulus(*ids,positions,work,clearance)
        report = json.loads(str(caught.exception).split(": ",1)[1])
        self.assertTrue(report["search"]["complete"])
        self.assertFalse(report["search"]["solution"])
        self.assertEqual(177,report["blockingTriangleWitnesses"][0]["originalSourceFace"])
        self.assertLessEqual(len(report["blockingTriangleWitnesses"]),8)


class ProjectionDiagnosticTests(unittest.TestCase):
    XZ = ((1,0,0),(0,0,1))

    def test_face_aligned_projection_separates_crest_and_toe(self):
        positions = [(-1.,1.,-1.),(1.,1.,-1.),(1.,-1.,1.),(-1.,-1.,1.)]
        original = mesh(positions,[(0,2,1),(0,3,2)])
        collapsed = module.projection_diagnostic(original,positions,((1,0,0),(0,1,1)),module.quality.Work())
        separated = module.projection_diagnostic(original,positions,((1,0,0),(0,1,-1)),module.quality.Work())
        self.assertFalse(collapsed["simpleRim"])
        self.assertTrue(separated["simpleRim"])
        self.assertTrue(separated["containedSource"])

    def test_added_loop_coordinates_do_not_require_source_vertex_identity(self):
        original = mesh()
        loop = [(-1.25,0.,-.75),(1.25,0.,-.75),(1.25,0.,.75),(-1.25,0.,.75)]
        result = module.projection_diagnostic(original,loop,self.XZ,module.quality.Work())
        self.assertTrue(result["simpleRim"])
        self.assertTrue(result["containedSource"])

    def test_exact_simple_rim_contains_source_and_collinear_boundary_edges(self):
        positions = [(0.,0.,0.),(1.,0.,0.),(2.,0.,0.),(2.,0.,2.),(0.,0.,2.),(1.,1.,1.)]
        triangles = [(i,(i+1)%5,5) for i in range(5)]
        result = module.projection_diagnostic(mesh(positions,triangles),positions[:5],self.XZ,module.quality.Work())
        self.assertTrue(result["simpleRim"])
        self.assertTrue(result["containedSource"])
        self.assertEqual(0,result["outsideSourceEdges"])
        self.assertGreater(result["sourceBoundaryContacts"],0)

    def test_source_projected_interior_overlap_is_not_a_false_refusal(self):
        # The projection helper judges containment only. Full 3D manifold and
        # intersection checks are separate gates; duplicate interior projection
        # must not invalidate the exterior-annulus disjointness criterion.
        positions = [(0.,0.,0.),(2.,0.,0.),(2.,0.,2.),(0.,0.,2.),(1.,1.,1.),
                     (.5,2.,.5),(1.5,2.,.5),(1.,2.,1.5)]
        triangles = [(i,(i+1)%4,4) for i in range(4)]+[(5,6,7)]
        result = module.projection_diagnostic(mesh(positions,triangles),positions[:4],self.XZ,module.quality.Work())
        self.assertTrue(result["containedSource"])
        self.assertTrue(result["sourceProjectedSelfOverlapAllowed"])

    def test_concave_rim_checks_complete_edges_not_only_contained_vertices(self):
        # U shape: every triangle vertex is inside, but its top edge crosses the
        # open notch. Exact contact splitting must find the outside interval.
        rim = [(0.,0.,0.),(4.,0.,0.),(4.,0.,4.),(3.,0.,4.),
               (3.,0.,1.),(1.,0.,1.),(1.,0.,4.),(0.,0.,4.)]
        positions = rim+[(.5,0.,3.5),(3.5,0.,3.5),(.5,1.,3.)]
        result = module.projection_diagnostic(mesh(positions,[(8,9,10)]),rim,self.XZ,module.quality.Work())
        self.assertTrue(result["simpleRim"])
        self.assertEqual(0,result["outsideSourceVertices"])
        self.assertFalse(result["containedSource"])
        self.assertGreater(result["outsideSourceEdges"],0)
        self.assertTrue(any(w["sourceVertices"] == [8,9] for w in result["sourceEdgeWitnesses"]))

    def test_crossed_adjacent_overlap_and_coincident_projection_rims_refuse(self):
        for positions in (
            [(0.,0.,0.),(2.,0.,2.),(0.,0.,2.),(2.,0.,0.)],
            [(0.,0.,0.),(2.,0.,0.),(1.,0.,0.),(1.,0.,1.),(0.,0.,1.)],
            [(0.,0.,0.),(2.,0.,0.),(2.,0.,2.),(0.,0.,2.),(0.,1.,0.)],
        ):
            # Pick a nondegenerate source face independently of rim quality.
            triangle = (0,1,3) if len(positions) == 5 and positions[2][2] == 0 else (0,1,2)
            result = module.projection_diagnostic(mesh(positions,[triangle]),positions,self.XZ,module.quality.Work())
            self.assertFalse(result["simpleRim"])
            self.assertFalse(result["containedSource"])

    def test_boundary_index_exact_rational_tangencies_and_inside_parity(self):
        fraction = module.Fraction
        loop = [(0,0),(4,0),(4,4),(3,4),(3,1),(1,1),(1,4),(0,4)]
        index = module._BoundaryIndex(loop,module.quality.Work())
        for point,expected in (((1,2),"boundary"),((2,2),"outside"),((fraction(1,2),2),"inside"),
                               ((0,0),"boundary"),((4,2),"boundary"),((2,fraction(1,2)),"inside")):
            self.assertEqual(expected,index.contains(point))
        self.assertEqual([fraction(1,4),fraction(3,4)],module._segment_parameters((0,0),(4,0),(1,0),(3,0)))
        self.assertEqual([fraction(1,2)],module._segment_parameters((0,0),(4,0),(2,0),(2,2)))

    def test_projection_work_exhaustion_is_incomplete(self):
        original = mesh()
        work = module.quality.Work(replace(module.quality.Limits(),maximum_topology_steps=0))
        with self.assertRaises(module.quality.Incomplete):
            module.projection_diagnostic(original,[POSITIONS[i] for i in (0,2,3,1)],self.XZ,work)


class OutputTests(unittest.TestCase):
    def test_runtime_deadline_cannot_silently_exceed_external_guard(self):
        for seconds in (0, 56, True, float("nan")):
            with self.assertRaisesRegex(module.author.QualificationError, "at most 55"):
                module.generate(Path.cwd(), Path("absent"), Path("absent"), Path("absent"), "0"*64, seconds)

    def test_visual_append_keeps_original_primitive_pbr_accessors_and_buffers(self):
        original = dict(asset=dict(version="2.0"), buffers=[dict(uri="original.bin", byteLength=12)],
            bufferViews=[dict(buffer=0, byteOffset=0, byteLength=12)],
            accessors=[dict(bufferView=0, componentType=5126, count=1, type="VEC3")],
            materials=[dict(name="original-rock", normalTexture=dict(index=1), pbrMetallicRoughness=dict(baseColorTexture=dict(index=0)))],
            textures=[dict(source=0), dict(source=1)], images=[dict(uri="rock.jpg"),dict(uri="normal.png")],
            meshes=[dict(primitives=[dict(attributes=dict(POSITION=0), indices=0, material=0)])])
        before = deepcopy(original)
        closure, _, _, _ = build()
        normal_layout = module.soil_normals.build(closure.positions,closure.triangles,closure.groups)
        expected_uv = module.metric_uv.build(closure.positions,closure.triangles,closure.groups,
                                            corner_normals=normal_layout.corner_normals)
        self.assertEqual(normal_layout.evidence["closureGeometrySha256"],expected_uv.evidence["closureGeometrySha256"])
        geometry_before = deepcopy((closure.positions,closure.triangles,closure.groups,closure.outer))
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary)
            mapping = module.write_visual(path, "near", original, closure, module.quality.Work(),expected_uv)
            output = json.loads((path/"near.gltf").read_text(encoding="utf-8"))
            self.assertEqual(before, original)
            self.assertEqual(before["meshes"][0]["primitives"][0], output["meshes"][0]["primitives"][0])
            self.assertEqual(before["materials"][0], output["materials"][0])
            self.assertEqual(before["buffers"][0], output["buffers"][0])
            self.assertEqual(before["accessors"][0], output["accessors"][0])
            self.assertEqual(before["images"], output["images"])
            self.assertEqual(len(closure.triangles), len(mapping))
            self.assertEqual(len(mapping), len(set(tuple(v) for v in mapping)))
            # Decode every emitted new POSITION and index, not just the JSON claim.
            blob = (path/"near-transition.bin").read_bytes()
            for f, (primitive_id, triangle_id) in enumerate(mapping):
                primitive = output["meshes"][0]["primitives"][primitive_id]
                pa = output["accessors"][primitive["attributes"]["POSITION"]]
                pv = output["bufferViews"][pa["bufferView"]]
                ia = output["accessors"][primitive["indices"]]
                iv = output["bufferViews"][ia["bufferView"]]
                indices = module.struct.unpack_from("<III", blob, iv["byteOffset"]+triangle_id*12)
                actual = tuple(module.struct.unpack_from("<fff", blob, pv["byteOffset"]+v*12) for v in indices)
                self.assertEqual(tuple(closure.positions[v] for v in closure.triangles[f]), actual)
                for attribute,width,expected in (("TEXCOORD_0",2,expected_uv.faces[f].uvs),
                                                 ("NORMAL",3,expected_uv.faces[f].normals),
                                                 ("TANGENT",4,expected_uv.faces[f].tangents)):
                    accessor = output["accessors"][primitive["attributes"][attribute]]
                    view = output["bufferViews"][accessor["bufferView"]]
                    values = tuple(module.struct.unpack_from("<"+"f"*width,blob,view["byteOffset"]+v*width*4) for v in indices)
                    self.assertEqual(expected,values)
            self.assertEqual(geometry_before,(closure.positions,closure.triangles,closure.groups,closure.outer))
            far_mapping = module.write_visual(path,"far",original,closure,module.quality.Work(),expected_uv)
            self.assertEqual(mapping,far_mapping)
            self.assertEqual(blob,(path/"far-transition.bin").read_bytes())
            with self.assertRaises(FileExistsError): module.write_visual(path, "near", original, closure, module.quality.Work())

    def test_three_view_evidence_is_explicitly_unadmitted(self):
        closure, candidates, _, _ = build()
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary)
            module.write_previews(path, candidates["physical"], 2, closure, module.quality.Work())
            for name in ("top", "front", "side"):
                text = (path/("review-"+name+".svg")).read_text(encoding="utf-8")
                self.assertIn("unadmitted geometry", text)
                self.assertIn("no root permissions", text)
                self.assertEqual(34, text.count("<path "))

    def test_live_or_existing_destination_refuses_before_input_admission(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for destination in (root/"game"/"assets", root/".artifacts"):
                if destination.name == ".artifacts": destination.mkdir()
                with self.assertRaisesRegex(module.author.QualificationError, "Destination must be fresh"):
                    module.generate(root, root/"absent", root/"absent.json", destination, "a"*64)

    def test_caller_receipt_pin_must_match_recipe(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root/".artifacts").mkdir()
            path = root/"recipe.json"
            path.write_bytes(module.author.canonical(recipe()))
            with self.assertRaisesRegex(module.author.QualificationError, "Caller receipt pin"):
                module.generate(root, root/"absent", path, root/".artifacts"/"new", "0"*64)
            self.assertFalse((root/".artifacts"/"new").exists())


if __name__ == "__main__":
    unittest.main()

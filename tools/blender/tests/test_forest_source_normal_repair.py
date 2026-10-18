"""Pure normal projection and authenticated additive mapping tests; root-run only."""
from copy import deepcopy
from fractions import Fraction
import importlib.util
import json
import math
from pathlib import Path
import struct
import sys
import tempfile
import unittest

HERE=Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location("forest_source_normal_repair",HERE.parent/"forest_source_normal_repair.py")
repair=importlib.util.module_from_spec(spec); sys.modules[spec.name]=repair; spec.loader.exec_module(repair)
spec=importlib.util.spec_from_file_location("_normal_repair_test_fixtures",HERE/"test_forest_contact_tangents.py")
fixtures=importlib.util.module_from_spec(spec); sys.modules[spec.name]=fixtures; spec.loader.exec_module(fixtures)


def work(**changes):
    policy=deepcopy(repair.DEFAULT_POLICY); policy.update(changes)
    return repair.Work(policy)


def fixture(**kwargs):
    if "normals" not in kwargs: kwargs["normals"]=[(0.,0.,-1.),(0.,0.,1.),(0.,0.,1.),(0.,0.,1.)]
    document,buffers,source=fixtures.fixture(**kwargs)
    diagnostic,_=fixtures.tangent.build_patch(document,buffers,source,
        [(1.,0.,0.,1.)]*(3*len(source.triangles)),buffer_name="diagnostic.bin",allow_splits=True)
    return document,buffers,source,diagnostic


def authenticated_fixture(root):
    package,module_sha,source_path=fixtures.package_fixture(root)
    document,buffers,source=fixtures.fixture()
    diagnostic=root/".artifacts"/"diagnostic"; diagnostic.mkdir()
    outputs=[]
    for name in ("near","far"):
        patch,blob=fixtures.tangent.build_patch(document,buffers,source,[(1.,0.,0.,1.)]*6,
            buffer_name=name+"-original-tangents.bin")
        # Synthetic authentication fixture only; this does not execute or claim
        # native Mikk qualification for an actual asset.
        patch.update(nativeMikkVerified=True,baseDocumentSha256=repair.sha(repair.author.canonical(document)))
        for filename,raw in ((name+"-tangent-patch.json",repair.author.canonical(patch)),
                (name+"-original-tangents.bin",blob)):
            (diagnostic/filename).write_bytes(raw)
            outputs.append(dict(file=filename,bytes=len(raw),sha256=repair.sha(raw)))
    receipt=dict(schema=repair.tangent.SCHEMA,assetAccepted=False,baseModuleReceiptSha256=module_sha,outputs=outputs)
    raw=repair.author.canonical(receipt); (diagnostic/"tangent-patch-receipt.json").write_bytes(raw)
    diagnostic_sha=repair.sha(raw)
    recipe=dict(schema=repair.SCHEMA,status="proposed-unadmitted",baseModuleReceiptSha256=module_sha,
        diagnosticReceiptSha256=diagnostic_sha,policy=deepcopy(repair.DEFAULT_POLICY))
    recipe_path=root/"recipe.json"; recipe_path.write_bytes(repair.author.canonical(recipe))
    return package,module_sha,diagnostic,diagnostic_sha,recipe_path,source_path


class NormalRepairTests(unittest.TestCase):
    def test_feasible_inherited_vector_is_unchanged(self):
        normal,evidence=repair.project_normal((0.,0.,1.),[(7,(Fraction(0),Fraction(0),Fraction(1)))],work(),3)
        self.assertEqual((0.,0.,1.),normal); self.assertEqual("0",evidence["exactRawSquaredChange"])
        self.assertEqual([],evidence["activeTriangles"])

    def test_exact_three_active_constraint_projection(self):
        normal,evidence=repair.project_normal((-1.,-2.,-3.),[(10,(1,0,0)),(11,(0,1,0)),(12,(0,0,1))],work(),4)
        expected=repair.f32(1/math.sqrt(3))
        self.assertEqual((expected,expected,expected),normal)
        self.assertEqual([10,11,12],evidence["activeTriangles"])
        self.assertTrue(evidence["encodedOutwardHemisphereExact"])

    def test_two_active_constraints_preserve_unconstrained_component_before_normalizing(self):
        normal,evidence=repair.project_normal((-1.,2.,-1.),[(0,(1,0,0)),(1,(0,0,1))],work(),4)
        bound=1/256; length=math.sqrt(4+2*bound*bound)
        self.assertEqual(tuple(repair.f32(v/length) for v in (bound,2.,bound)),normal)
        self.assertEqual([0,1],evidence["activeTriangles"])

    def test_dependent_active_constraints_are_skipped_without_losing_solution(self):
        normal,evidence=repair.project_normal((0.,0.,-1.),[(0,(0,0,1)),(1,(0,0,2))],work(),3)
        self.assertEqual((0.,0.,1.),normal); self.assertEqual([0],evidence["activeTriangles"])

    def test_infeasible_shared_normal_reports_incident_witness_without_fallback(self):
        with self.assertRaisesRegex(repair.RepairError,'NO FEASIBLE SHARED NORMAL.*"vertex": 42.*3, 9'):
            repair.project_normal((1.,0.,0.),[(3,(0,0,1)),(9,(0,0,-1))],work(),42)

    def test_zero_area_constraint_is_explicitly_refused(self):
        with self.assertRaisesRegex(repair.RepairError,"Zero incident area"):
            repair.project_normal((0.,0.,1.),[(0,(0,0,0))],work(),0)

    def test_candidate_constraint_arithmetic_and_bit_caps_refuse(self):
        for overrides,pattern in ((dict(maximumCandidates=1),"candidates"),
                (dict(maximumConstraintTests=1),"constraint_tests"),
                (dict(maximumArithmeticOperations=1),"arithmetic"),
                (dict(maximumRationalBits=2),"rational bit")):
            with self.subTest(overrides=overrides),self.assertRaisesRegex(repair.RepairError,pattern):
                repair.project_normal((0.,0.,-1.),[(0,(0,0,1)),(1,(0,0,2))],work(**overrides),0)

    def test_incident_capacity_cannot_silently_drop_a_face(self):
        with self.assertRaisesRegex(repair.RepairError,"incident-face capacity"):
            repair.project_normal((0.,0.,1.),[(0,(0,0,1)),(1,(0,1,0))],work(maximumIncidentFacesPerVertex=1),0)

    def test_only_bad_indexed_normal_changes_and_all_incident_area_is_reported(self):
        document,buffers,source,diagnostic=fixture(); original=deepcopy(source.normals)
        normals,evidence=repair.repair(source,diagnostic,work())
        self.assertEqual(original,source.normals); self.assertEqual((0.,0.,1.),normals[0])
        self.assertEqual(original[1:],normals[1:]); self.assertEqual(1,evidence["changedVertices"])
        self.assertEqual([0,1],evidence["touchedTriangleOrdinals"])
        self.assertEqual(1.,evidence["touchedTriangleAreaMeasuredSquareMetres"])
        self.assertTrue(evidence["allOutputCornersOutwardExact"])
        self.assertEqual(180.,evidence["maximumAngularChangeDegreesMeasured"])

    def test_equal_positions_across_distinct_uv_seams_do_not_share_repair(self):
        positions=[(0.,0.,0.),(1.,0.,0.),(0.,1.,0.),(0.,0.,0.),(1.,0.,0.),(0.,1.,0.)]
        normals=[(0.,0.,-1.)]+[(0.,0.,1.)]*5
        uvs=[(0.,0.),(1.,0.),(0.,1.),(.25,.25),(.75,.25),(.25,.75)]
        _,_,source,diagnostic=fixture(positions=positions,normals=normals,uvs=uvs,triangles=[(0,1,2),(3,4,5)])
        values,evidence=repair.repair(source,diagnostic,work())
        self.assertEqual([0],[row["vertex"] for row in evidence["changes"]])
        self.assertEqual(source.normals[3],values[3]); self.assertEqual([0],evidence["touchedTriangleOrdinals"])

    def test_diagnostic_selection_and_full_stream_hashes_are_recomputed(self):
        _,_,source,diagnostic=fixture()
        for change in (lambda r:r.update(affectedSourceVertexIndices=[]),
                       lambda r:r.update(affectedTriangleOrdinals=[1]),
                       lambda r:r.update(sourceNormalSha256="0"*64)):
            altered=deepcopy(diagnostic); change(altered["sourceNormalHemisphere"])
            with self.assertRaisesRegex(repair.RepairError,"selection|identity"):
                repair.repair(source,altered,work())

    def test_all_corner_work_and_selected_vertex_capacity_are_bounded(self):
        _,_,source,diagnostic=fixture()
        with self.assertRaisesRegex(repair.RepairError,"corner_tests"):
            repair.repair(source,diagnostic,work(maximumCornerTests=1))
        with self.assertRaisesRegex(repair.RepairError,"selection/capacity"):
            repair.repair(source,diagnostic,work(maximumAffectedVerticesPerLod=0))

    def test_cancellation_precedes_any_source_processing(self):
        _,_,source,diagnostic=fixture()
        def stop(): raise RuntimeError("cancelled")
        with self.assertRaisesRegex(RuntimeError,"cancelled"):
            repair.repair(source,diagnostic,repair.Work(deepcopy(repair.DEFAULT_POLICY),stop))

    def test_patch_appends_normal_only_and_preserves_all_other_byte_references(self):
        document,buffers,source,diagnostic=fixture(); before=deepcopy(document)
        patch,blob=repair.build_normal_patch(document,buffers,source,diagnostic)
        result,payloads=repair.apply_repaired_document(document,buffers,patch,blob)
        self.assertEqual(before,document); self.assertIs(buffers[0],payloads[0])
        for key in ("materials","images","textures","samplers","nodes"):
            self.assertEqual(document[key],result[key])
        old=document["meshes"][0]["primitives"][0]; new=result["meshes"][0]["primitives"][0]
        self.assertEqual(old["indices"],new["indices"])
        self.assertEqual(old["attributes"]["POSITION"],new["attributes"]["POSITION"])
        self.assertEqual(old["attributes"]["TEXCOORD_0"],new["attributes"]["TEXCOORD_0"])
        self.assertNotEqual(old["attributes"]["NORMAL"],new["attributes"]["NORMAL"])
        self.assertEqual(len(source.normals)*12,len(blob)); self.assertTrue(patch["nativeMikkRegenerationRequired"])
        self.assertFalse(patch["nativeFramesValid"]); self.assertFalse(patch["physicalGeometryChanged"])
        repaired_source=repair.tangent.preflight(result,payloads)
        self.assertEqual(source.positions,repaired_source.positions); self.assertEqual(source.uvs,repaired_source.uvs)
        self.assertEqual(source.triangles,repaired_source.triangles)

    def test_patch_is_deterministic_and_inherited_signed_zero_bytes_remain_exact(self):
        document,buffers,source,diagnostic=fixture(positions=[(-0.,0.,0.),(1.,0.,0.),(1.,1.,0.),(0.,1.,0.)])
        a=repair.build_normal_patch(document,buffers,source,diagnostic)
        b=repair.build_normal_patch(document,buffers,source,diagnostic)
        self.assertEqual(a,b)
        result,payloads=repair.apply_repaired_document(document,buffers,*a)
        unchanged=repair.tangent.preflight(result,payloads)
        self.assertEqual(repair.packed(source.positions,3),repair.packed(unchanged.positions,3))

    def test_apply_refuses_source_or_normal_payload_tampering(self):
        document,buffers,source,diagnostic=fixture(); patch,blob=repair.build_normal_patch(document,buffers,source,diagnostic)
        damaged=bytearray(blob); damaged[0]^=1
        with self.assertRaisesRegex(repair.RepairError,"authenticate"):
            repair.apply_repaired_document(document,buffers,patch,bytes(damaged))
        damaged=bytearray(buffers[0]); struct.pack_into("<f",damaged,0,.125)
        with self.assertRaisesRegex(repair.RepairError,"source/output stream"):
            repair.apply_repaired_document(document,[bytes(damaged)],patch,blob)

    def test_apply_cannot_patch_uv_indices_or_buffer_path(self):
        document,buffers,source,diagnostic=fixture(); patch,blob=repair.build_normal_patch(document,buffers,source,diagnostic)
        for change in (lambda p:p["replacementPrimitive"].update(indices=0),
                       lambda p:p["replacementPrimitive"]["attributes"].update(TEXCOORD_0=0),
                       lambda p:p["appendBuffers"][0].update(uri="../escape.bin")):
            altered=deepcopy(patch); change(altered)
            with self.assertRaises(repair.RepairError): repair.apply_repaired_document(document,buffers,altered,blob)

    def test_unchanged_healthy_source_is_an_explicit_noop_normal_patch(self):
        document,buffers,source,diagnostic=fixture(normals=[(0.,0.,1.)]*4)
        patch,blob=repair.build_normal_patch(document,buffers,source,diagnostic)
        self.assertEqual(0,patch["evidence"]["changedVertices"])
        self.assertEqual(repair.packed(source.normals,3),blob)
        self.assertFalse(patch["sourceNormalsModified"])

    def test_policy_cannot_invent_other_margin_or_reduce_declared_reservation(self):
        for update in (dict(marginDivisor=512),dict(maximumCandidates=65537),dict(maximumExtraReservedBytes=1)):
            policy=deepcopy(repair.DEFAULT_POLICY); policy.update(update)
            with self.assertRaises(repair.RepairError): repair.validate_policy(policy)

    def test_repaired_source_can_regenerate_mikk_patch_with_new_normal_identity(self):
        document,buffers,source,diagnostic=fixture(); patch,blob=repair.build_normal_patch(document,buffers,source,diagnostic)
        result,payloads=repair.apply_repaired_document(document,buffers,patch,blob)
        repaired=repair.tangent.preflight(result,payloads)
        new_tangent,_=repair.tangent.build_patch(result,payloads,repaired,[(1.,0.,0.,1.)]*6,buffer_name="new-mikk.bin")
        self.assertTrue(new_tangent["sourceNormalHemisphere"]["clear"])
        self.assertEqual(patch["outputNormalSha256"],new_tangent["sourceNormalSha256"])
        self.assertEqual(diagnostic["sourcePositionSha256"],new_tangent["sourcePositionSha256"])
        self.assertNotEqual(diagnostic["sourceNormalSha256"],new_tangent["sourceNormalSha256"])

    def test_added_closure_primitive_descriptors_and_attributes_are_untouched(self):
        document,buffers,source,diagnostic=fixture()
        document["meshes"][0]["primitives"].append(deepcopy(document["meshes"][0]["primitives"][0]))
        closure=deepcopy(document["meshes"][0]["primitives"][1])
        patch,blob=repair.build_normal_patch(document,buffers,source,diagnostic)
        result,_=repair.apply_repaired_document(document,buffers,patch,blob)
        self.assertEqual(closure,result["meshes"][0]["primitives"][1])
        self.assertEqual(document["accessors"],result["accessors"][:-1])

    def test_authentication_binds_both_lods_original_dependencies_and_extra_decode_reservation(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory); args=authenticated_fixture(root)
            receipt,inputs,diagnostics,recipe,admission,reserved=repair.authenticate_inputs(*args[:5],root=root)
            self.assertEqual({"near","far"},set(inputs)); self.assertEqual(set(inputs),set(diagnostics))
            self.assertGreater(reserved,sum(row[2].reserved_bytes for row in inputs.values())+64*1024*1024)
            self.assertLessEqual(reserved,384*1024*1024); admission.recheck()
            self.assertEqual(args[1],recipe["baseModuleReceiptSha256"])

    def test_authentication_refuses_original_dependency_or_diagnostic_bytes_changed(self):
        for target in ("source","diagnostic"):
            with self.subTest(target=target),tempfile.TemporaryDirectory() as directory:
                root=Path(directory); args=authenticated_fixture(root)
                path=args[5]/"normal.png" if target=="source" else args[2]/"near-original-tangents.bin"
                raw=bytearray(path.read_bytes()); raw[0]^=1; path.write_bytes(raw)
                with self.assertRaises(ValueError): repair.authenticate_inputs(*args[:5],root=root)

    def test_authentication_refuses_wrong_recipe_parent_and_duplicate_diagnostic_membership(self):
        for target in ("parent","members"):
            with self.subTest(target=target),tempfile.TemporaryDirectory() as directory:
                root=Path(directory); args=list(authenticated_fixture(root))
                if target=="parent":
                    proposal=json.loads(args[4].read_text()); proposal["baseModuleReceiptSha256"]="f"*64
                    args[4].write_bytes(repair.author.canonical(proposal))
                else:
                    path=args[2]/"tangent-patch-receipt.json"; receipt=json.loads(path.read_text())
                    receipt["outputs"].append(deepcopy(receipt["outputs"][0]))
                    raw=repair.author.canonical(receipt); path.write_bytes(raw); args[3]=repair.sha(raw)
                    proposal=json.loads(args[4].read_text()); proposal["diagnosticReceiptSha256"]=args[3]
                    args[4].write_bytes(repair.author.canonical(proposal))
                with self.assertRaises(ValueError): repair.authenticate_inputs(*args[:5],root=root)


if __name__=="__main__": unittest.main()

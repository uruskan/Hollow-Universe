"""Pure authoring/mapping tests. No Blender, export, game or native invocation."""
from copy import deepcopy
import importlib.util
import json
import math
from pathlib import Path
import struct
import sys
import tempfile
import unittest

TOOL=Path(__file__).resolve().parents[1]/"forest_contact_tangents.py"
spec=importlib.util.spec_from_file_location("forest_contact_tangents",TOOL)
tangent=importlib.util.module_from_spec(spec); sys.modules[spec.name]=tangent; spec.loader.exec_module(tangent)


def fixture(positions=None,normals=None,uvs=None,triangles=None):
    positions=positions or [(0.,0.,0.),(1.,0.,0.),(1.,1.,0.),(0.,1.,0.)]
    normals=normals or [(0.,0.,1.)]*len(positions)
    uvs=uvs or [(0.,0.),(1.,0.),(1.,1.),(0.,1.)]
    triangles=triangles or [(0,1,2),(0,2,3)]
    blob=bytearray(); views=[]; accessors=[]
    for values,width,kind,component,target in ((positions,3,"VEC3",5126,34962),(normals,3,"VEC3",5126,34962),
            (uvs,2,"VEC2",5126,34962),([(v,) for f in triangles for v in f],1,"SCALAR",5123,34963)):
        offset=len(blob)
        for value in values: blob.extend(struct.pack("<"+("f" if component==5126 else "H")*width,*value))
        views.append(dict(buffer=0,byteOffset=offset,byteLength=len(blob)-offset,target=target))
        accessors.append(dict(bufferView=len(views)-1,componentType=component,count=len(values),type=kind))
    document=dict(asset=dict(version="2.0"),nodes=[dict(mesh=0)],meshes=[dict(primitives=[dict(
        attributes=dict(POSITION=0,NORMAL=1,TEXCOORD_0=2),indices=3,material=0)])],
        buffers=[dict(uri="source.bin",byteLength=len(blob))],bufferViews=views,accessors=accessors,
        materials=[dict(name="original",normalTexture=dict(index=0),pbrMetallicRoughness=dict(baseColorFactor=[1,1,1,1]))],
        images=[dict(uri="normal.png")],textures=[dict(source=0)],samplers=[])
    buffers=[bytes(blob)]
    return document,buffers,tangent.preflight(document,buffers)


def package_fixture(root):
    document,buffers,_=fixture()
    raw=tangent.author.canonical(document)
    source=root/"game"/"source"; source.mkdir(parents=True)
    qualified=root/".artifacts"/"qualified"; qualified.mkdir(parents=True)
    module=root/".artifacts"/"module"; module.mkdir(parents=True)
    def write(owner,name,data):
        (owner/name).write_bytes(data)
        return dict(file=name,bytes=len(data),sha256=tangent.sha(data))
    source_record=write(source,"original.gltf",raw)
    dependencies=[]
    for name,data in (("source.bin",buffers[0]),("normal.png",b"authenticated source image; not decoded by this tool")):
        record=write(source,name,data)
        dependencies.append(dict(file=(source/name).relative_to(root).as_posix(),sha256=record["sha256"]))
    qualified_files=[write(qualified,"qualified-near.gltf",raw),write(qualified,"qualified-far.gltf",raw)]
    module_files=[write(module,"near.gltf",raw),write(module,"far.gltf",raw)]
    for name in ("source.bin","normal.png"):
        qualified_files.append(write(qualified,name,(source/name).read_bytes()))
        module_files.append(write(module,name,(source/name).read_bytes()))
    sidecar=dict(definitions=[dict(lods=[dict(key="near",assetPath="qualified-near.gltf"),dict(key="far",assetPath="qualified-far.gltf")])])
    qualified_files.append(write(qualified,"sidecar.json",tangent.author.canonical(sidecar)))
    qualification=dict(source=dict(file="game/source/original.gltf",sha256=source_record["sha256"],dependencies=dependencies),outputFiles=qualified_files)
    qualified_receipt=write(qualified,"qualification-receipt.json",tangent.author.canonical(qualification))
    receipt=dict(status="unadmitted-geometry-candidate",assetAccepted=False,sourcePackage=".artifacts/qualified",
        sourceReceiptSha256=qualified_receipt["sha256"],sourceSha256=source_record["sha256"],outputFiles=module_files)
    record=write(module,"module-candidate-receipt.json",tangent.author.canonical(receipt))
    return module,record["sha256"],source


class TangentPatchTests(unittest.TestCase):
    def test_additive_no_split_preserves_original_indices_attributes_tables_and_materials(self):
        doc,buffers,source=fixture(); original=deepcopy(doc)
        patch,blob=tangent.build_patch(doc,buffers,source,[(1.,0.,0.,1.)]*6,buffer_name="near-tangents.bin")
        result=tangent.apply_patch_document(doc,patch,blob)
        self.assertEqual(original,doc)
        self.assertEqual(0,patch["splitVertices"])
        self.assertTrue(patch["originalIndexAccessorPreservedAndUsed"])
        self.assertEqual(list(range(4)),patch["sourceVertexForOutput"])
        self.assertEqual([0,1,2,0,2,3],patch["outputCornerIndices"])
        for field in ("materials","images","textures","samplers"): self.assertEqual(doc[field],result[field])
        for field in ("buffers","bufferViews","accessors"): self.assertEqual(doc[field],result[field][:len(doc[field])])
        primitive=result["meshes"][0]["primitives"][0]
        self.assertEqual(3,primitive["indices"])
        self.assertEqual({"POSITION":0,"NORMAL":1,"TEXCOORD_0":2},
            {k:v for k,v in primitive["attributes"].items() if k != "TANGENT"})
        frames=tangent.preview.accessor(result,buffers+[blob],primitive["attributes"]["TANGENT"],"VEC4",(5126,),60000)
        self.assertEqual([(1.,0.,0.,1.)]*4,frames)

    def test_mikk_averaged_frames_need_not_equal_each_face_derivative(self):
        doc,buffers,source=fixture()
        frame=(tangent.f32(.8),tangent.f32(.6),0.,1.)
        patch,_=tangent.build_patch(doc,buffers,source,[frame]*6,buffer_name="a.bin")
        self.assertEqual(0,patch["splitVertices"])
        self.assertIn("Not per-face derivative equality",patch["validation"])

    def test_conflicting_corner_frames_require_explicit_split_with_witness(self):
        doc,buffers,source=fixture()
        frames=[(1.,0.,0.,1.)]*3+[(tangent.f32(.8),tangent.f32(.6),0.,1.)]*3
        with self.assertRaisesRegex(tangent.TangentError,'explicit vertex split.*"triangle": 1'):
            tangent.build_patch(doc,buffers,source,frames,buffer_name="a.bin")

    def test_splits_append_only_and_preserve_every_corner_attribute_byte(self):
        doc,buffers,source=fixture(positions=[(-0.,0.,0.),(1.,0.,0.),(1.,1.,0.),(0.,1.,0.)])
        frames=[(1.,0.,0.,1.)]*3+[(tangent.f32(.8),tangent.f32(.6),0.,1.)]*3
        patch,blob=tangent.build_patch(doc,buffers,source,frames,buffer_name="a.bin",allow_splits=True)
        self.assertEqual([0,1,2,3,0,2],patch["sourceVertexForOutput"])
        self.assertEqual([0,1,2,4,5,3],patch["outputCornerIndices"])
        self.assertEqual(2,patch["splitVertices"])
        result=tangent.apply_patch_document(doc,patch,blob); primitive=result["meshes"][0]["primitives"][0]
        decoded=[]
        for attribute,kind,width in (("POSITION","VEC3",3),("NORMAL","VEC3",3),("TEXCOORD_0","VEC2",2)):
            values=tangent.preview.accessor(result,buffers+[blob],primitive["attributes"][attribute],kind,(5126,),60000)
            before={"POSITION":source.positions,"NORMAL":source.normals,"TEXCOORD_0":source.uvs}[attribute]
            self.assertEqual(tangent.packed(before,width),tangent.packed(values[:len(before)],width))
            decoded.append(values)
        indices=[tuple(patch["outputCornerIndices"][i:i+3]) for i in range(0,6,3)]
        self.assertEqual(tangent.corner_hash(source,source.triangles),tangent.corner_hash(source,indices,*decoded))
        self.assertEqual(buffers[0],(buffers+[blob])[0])

    def test_repeated_frame_variants_reuse_one_appended_vertex(self):
        doc,buffers,source=fixture(triangles=[(0,1,2),(0,2,3),(0,2,3)])
        frames=[(1.,0.,0.,1.)]*3+[(tangent.f32(.8),tangent.f32(.6),0.,1.)]*6
        patch,_=tangent.build_patch(doc,buffers,source,frames,buffer_name="a.bin",allow_splits=True)
        self.assertEqual(2,patch["splitVertices"])
        self.assertEqual(patch["outputCornerIndices"][3:6],patch["outputCornerIndices"][6:9])

    def test_signed_zero_does_not_manufacture_a_split(self):
        doc,buffers,source=fixture()
        frames=[(1.,0.,0.,1.)]*3+[(1.,-0.,0.,1.)]*3
        patch,_=tangent.build_patch(doc,buffers,source,frames,buffer_name="a.bin")
        self.assertEqual(0,patch["splitVertices"])
        self.assertEqual(3,patch["negativeZeroComponentsCanonicalized"])

    def test_same_inputs_have_exact_stable_patch_and_buffer_order(self):
        doc,buffers,source=fixture(); frames=[(1.,0.,0.,1.)]*6
        a=tangent.build_patch(doc,buffers,source,frames,buffer_name="a.bin")
        b=tangent.build_patch(doc,buffers,source,list(frames),buffer_name="a.bin")
        self.assertEqual(tangent.author.canonical(a[0]),tangent.author.canonical(b[0])); self.assertEqual(a[1],b[1])

    def test_required_split_capacity_refuses_without_prefix_result(self):
        doc,buffers,source=fixture(); frames=[(1.,0.,0.,1.)]*3+[(.8,.6,0.,1.)]*3
        with self.assertRaisesRegex(tangent.TangentError,"capacity"):
            tangent.build_patch(doc,buffers,source,frames,buffer_name="a.bin",allow_splits=True,maximum_output_vertices=5)

    def test_undefined_zero_area_uv_refuses_before_native_backend(self):
        with self.assertRaisesRegex(tangent.TangentError,"degenerate original UV triangle"):
            fixture(uvs=[(0.,0.),(1.,0.),(2.,0.),(0.,1.)])

    def test_degenerate_geometry_refuses_even_with_valid_uv(self):
        with self.assertRaisesRegex(tangent.TangentError,"Degenerate original geometry"):
            fixture(positions=[(0.,0.,0.),(1.,0.,0.),(2.,0.,0.),(0.,1.,0.)])

    def test_normal_parallel_to_uv_tangent_refuses(self):
        with self.assertRaisesRegex(tangent.TangentError,"normal-projected"):
            fixture(normals=[(1.,0.,0.)]*4)

    def test_invalid_frame_shape_nonfinite_unit_orthogonality_sign_and_handedness_refuse(self):
        doc,buffers,source=fixture()
        for frame in ((1.,0.,0.),(math.nan,0.,0.,1.),(1e300,0.,0.,1.),(0.,0.,0.,1.),
                      (0.,0.,1.,1.),(1.,0.,0.,0.),(1.,0.,0.,-1.)):
            with self.subTest(frame=frame),self.assertRaises(tangent.TangentError):
                tangent.build_patch(doc,buffers,source,[frame]*6,buffer_name="a.bin")

    def test_per_face_derivative_sign_is_diagnostic_not_a_mikk_contract(self):
        doc,buffers,source=fixture()
        patch,_=tangent.build_patch(doc,buffers,source,[(-1.,0.,0.,1.)]*6,buffer_name="a.bin")
        self.assertEqual(6,patch["incidentDerivativeDiagnostics"]["nonPositiveTangentDuCorners"])
        self.assertEqual(6,patch["incidentDerivativeDiagnostics"]["nonPositiveBitangentDvCorners"])
        self.assertTrue(patch["sourceNormalHemisphere"]["clear"])
        # Pure input validation does NOT certify fabricated frames as native Mikk output.
        self.assertFalse(patch["nativeMikkVerified"])

    def test_behind_face_source_normals_are_reported_without_repair_or_tangent_rejection(self):
        doc,buffers,source=fixture(normals=[(0.,0.,-1.)]*4)
        patch,blob=tangent.build_patch(doc,buffers,source,[(1.,0.,0.,1.)]*6,buffer_name="a.bin")
        report=patch["sourceNormalHemisphere"]
        self.assertFalse(report["clear"]); self.assertTrue(report["appearanceAdmissionBlocked"])
        self.assertFalse(report["sourceNormalsModified"]); self.assertFalse(patch["appearanceAccepted"])
        self.assertEqual(6,report["negativeCornerCount"]); self.assertEqual(0,report["zeroCornerCount"])
        self.assertEqual([0,1],report["affectedTriangleOrdinals"])
        self.assertEqual([0,1,2,3],report["affectedSourceVertexIndices"])
        self.assertEqual(1.,report["totalTriangleAreaMeasuredSquareMetres"])
        self.assertEqual(1.,report["affectedTriangleAreaMeasuredSquareMetres"])
        self.assertEqual(-1.,report["minimumCornerCosineMeasured"])
        self.assertEqual(patch["sourceNormalSha256"],report["sourceNormalSha256"])
        self.assertEqual(0,patch["splitVertices"])
        result=tangent.apply_patch_document(doc,patch,blob)
        self.assertEqual(1,result["meshes"][0]["primitives"][0]["attributes"]["NORMAL"])

    def test_tangent_source_normal_is_exactly_classified_as_zero(self):
        doc,buffers,source=fixture(normals=[(0.,1.,0.)]*4)
        patch,_=tangent.build_patch(doc,buffers,source,[(1.,0.,0.,1.)]*6,buffer_name="a.bin")
        report=patch["sourceNormalHemisphere"]
        self.assertEqual(0,report["negativeCornerCount"]); self.assertEqual(6,report["zeroCornerCount"])
        self.assertEqual(0.,report["minimumCornerCosineMeasured"])
        self.assertTrue(report["appearanceAdmissionBlocked"])

    def test_measured_triangle_15203_corner_keeps_native_frame_and_reports_inherited_normal(self):
        # The measured corner0 P/N/UV/frame is retained verbatim. Other corner
        # normals use that same N synthetically; this does not claim their native values.
        normal=(.35929742455482483,-.06729951500892639,.9307933449745178)
        frame=(.7608129978179932,.5987206697463989,-.2503935396671295,1.)
        doc,buffers,source=fixture(
            positions=[(1.7301876544952393,1.1834479570388794,.7720316052436829),
                       (1.6937862634658813,1.1730828285217285,.8162661194801331),
                       (1.7547255754470825,1.2010763883590698,.7947630286216736)],
            uvs=[(.1547866016626358,.08523315191268921),(.15293319523334503,.08404195308685303),
                 (.15619288384914398,.08510559797286987)],normals=[normal]*3,triangles=[(0,1,2)])
        patch,blob=tangent.build_patch(doc,buffers,source,[frame]*3,buffer_name="a.bin")
        self.assertFalse(patch["sourceNormalHemisphere"]["clear"])
        self.assertEqual("-1008365609972205593/1180591620717411303424",
            patch["sourceNormalHemisphere"]["witnesses"][0]["normalDotGeometricAreaExact"])
        self.assertEqual(frame,struct.unpack_from("<4f",blob))
        self.assertEqual(0,patch["splitVertices"])

    def test_normal_report_enumerates_all_affected_faces_but_bounds_rich_witnesses(self):
        doc,buffers,source=fixture(normals=[(0.,0.,-1.)]*4,triangles=[(0,1,2),(0,2,3)]*20)
        patch,_=tangent.build_patch(doc,buffers,source,[(1.,0.,0.,1.)]*120,buffer_name="a.bin")
        report=patch["sourceNormalHemisphere"]
        self.assertEqual(40,report["affectedTriangleCount"])
        self.assertEqual(list(range(40)),report["affectedTriangleOrdinals"])
        self.assertEqual(32,len(report["witnesses"])); self.assertTrue(report["witnessesTruncated"])
        self.assertEqual(20.,report["affectedTriangleAreaMeasuredSquareMetres"])

    def test_mirrored_uv_keeps_valid_negative_handedness(self):
        doc,buffers,source=fixture(uvs=[(0.,0.),(1.,0.),(1.,-1.),(0.,-1.)])
        patch,blob=tangent.build_patch(doc,buffers,source,[(1.,0.,0.,-1.)]*6,buffer_name="a.bin")
        self.assertEqual(0,patch["splitVertices"])
        self.assertEqual(-1.,struct.unpack_from("<4f",blob)[3])

    def test_orientation_refusal_reports_native_frame_and_exact_input_witness(self):
        doc,buffers,source=fixture()
        with self.assertRaises(tangent.TangentError) as error:
            tangent.build_patch(doc,buffers,source,[(1.,0.,0.,-1.)]*6,buffer_name="a.bin")
        witness=json.loads(str(error.exception).split("; witness=",1)[1])
        self.assertEqual(0,witness["failure"]["triangle"])
        self.assertEqual(0,witness["failure"]["corner"])
        self.assertEqual("1",witness["failure"]["uvDeterminantExact"])
        self.assertEqual("1",witness["failure"]["normalDotGeometricAreaExact"])
        self.assertEqual(1.,witness["failure"]["tangentDotDu"])
        self.assertEqual(-1.,witness["failure"]["bitangentDotDv"])
        self.assertEqual(2,witness["samePositionIncidentCorners"])
        self.assertFalse(witness["incidentsTruncated"])

    def test_frame_quality_aggregates_outliers_without_modifying_or_approving_them(self):
        doc,buffers,source=fixture()
        frame=(1.,0.,.001,1.)
        patch,blob=tangent.build_patch(doc,buffers,source,[frame]*6,buffer_name="a.bin")
        quality=patch["nativeFrameQuality"]
        self.assertEqual(6,quality["testedCorners"])
        self.assertEqual(6,quality["orthogonalityOutOfPolicyCorners"])
        self.assertEqual(0,quality["unitOutOfPolicyCorners"])
        self.assertEqual(0,quality["runtimeDecoderUnitRejectedCorners"])
        self.assertFalse(quality["clear"]); self.assertTrue(patch["appearanceAdmissionBlocked"])
        self.assertFalse(quality["nativeFramesModified"]); self.assertFalse(patch["appearanceAccepted"])
        self.assertEqual(frame,quality["worstOrthogonality"]["rawFrame"])
        self.assertEqual(tangent.f32(.001),quality["maximumAbsoluteNormalDot"])
        self.assertEqual(1e-4,quality["policy"]["maximumNormalizedAbsoluteNormalDot"])
        self.assertEqual(tuple(tangent.f32(v) for v in frame),struct.unpack_from("<4f",blob))

    def test_runtime_incompatible_length_is_fully_reported_but_not_approved(self):
        doc,buffers,source=fixture()
        patch,_=tangent.build_patch(doc,buffers,source,[(2.,0.,0.,1.)]*6,buffer_name="a.bin")
        quality=patch["nativeFrameQuality"]
        self.assertEqual(6,quality["runtimeDecoderUnitRejectedCorners"])
        self.assertEqual(6,quality["unitOutOfPolicyCorners"])
        self.assertEqual(3.,quality["maximumUnitSquaredError"])
        self.assertFalse(quality["clear"])

    def test_native_float32_residual_policy_is_separate_from_legacy_threshold(self):
        doc,buffers,source=fixture()
        patch,_=tangent.build_patch(doc,buffers,source,[(1.,0.,3e-5,1.)]*6,buffer_name="a.bin")
        quality=patch["nativeFrameQuality"]
        self.assertTrue(quality["clear"])
        self.assertEqual(6,quality["legacy2e6RawNormalDotOutlierCorners"])
        self.assertEqual(0,quality["outOfPolicyCornerCount"])
        self.assertFalse(patch["appearanceAccepted"])

    def test_orientation_witness_distinguishes_coincident_seams_and_caps_output(self):
        doc,buffers,source=fixture(
            positions=[(0.,0.,0.),(1.,0.,0.),(0.,1.,0.),(0.,0.,0.),(-1.,0.,0.),(0.,-1.,0.)],
            uvs=[(0.,0.),(1.,0.),(0.,1.),(.5,.5),(1.5,.5),(.5,1.5)],
            triangles=[(0,1,2),(3,4,5)])
        witness=tangent.orientation_witness(source,[(1.,0.,0.,1.)]*6,0,0)
        self.assertEqual(2,witness["samePositionIncidentCorners"])
        self.assertTrue(witness["incidents"][0]["sameMikkAttributes"])
        self.assertFalse(witness["incidents"][1]["sameMikkAttributes"])
        witness=tangent.orientation_witness(source,[(1.,0.,0.,1.)]*6,0,0,maximum_incidents=1)
        self.assertEqual(1,witness["reportedIncidentCorners"])
        self.assertTrue(witness["incidentsTruncated"])

    def test_unreferenced_original_vertex_refuses_undefined_tangent(self):
        with self.assertRaisesRegex(tangent.TangentError,"Unused source vertices"):
            fixture(positions=[(0.,0.,0.),(1.,0.,0.),(1.,1.,0.),(0.,1.,0.),(2.,2.,0.)],
                    uvs=[(0.,0.),(1.,0.),(1.,1.),(0.,1.),(2.,2.)])

    def test_frame_count_and_existing_tangent_refuse(self):
        doc,buffers,source=fixture()
        with self.assertRaisesRegex(tangent.TangentError,"corner count"):
            tangent.build_patch(doc,buffers,source,[(1.,0.,0.,1.)]*5,buffer_name="a.bin")
        doc["meshes"][0]["primitives"][0]["attributes"]["TANGENT"]=0
        with self.assertRaisesRegex(tangent.TangentError,"no existing tangent"): tangent.preflight(doc,buffers)

    def test_decoder_table_and_aggregate_caps_admitted_before_attribute_decode(self):
        doc,buffers,_=fixture()
        doc["accessors"] += [deepcopy(doc["accessors"][0])]*124
        with self.assertRaisesRegex(tangent.TangentError,"bounded room"): tangent.preflight(doc,buffers)
        doc,buffers,_=fixture(); doc["accessors"][0]["count"]=60001
        with self.assertRaisesRegex(tangent.TangentError,"primitive vertices"): tangent.preflight(doc,buffers)
        doc,buffers,_=fixture(); doc["meshes"][0]["primitives"][0]["attributes"]["POSITION"]=-1
        with self.assertRaisesRegex(tangent.TangentError,"position accessor"): tangent.preflight(doc,buffers)

    def test_patch_rejects_changed_base_document_or_payload(self):
        doc,buffers,source=fixture(); patch,blob=tangent.build_patch(doc,buffers,source,[(1.,0.,0.,1.)]*6,buffer_name="a.bin")
        changed=deepcopy(doc); changed["materials"][0]["name"]="other"
        for document,payload in ((changed,blob),(doc,blob[:-1]+bytes([blob[-1]^1]))):
            with self.assertRaisesRegex(tangent.TangentError,"does not authenticate"):
                tangent.apply_patch_document(document,patch,payload)

    def test_poll_callback_interrupts_bounded_work(self):
        doc,buffers,source=fixture()
        def stop(): raise RuntimeError("cancelled")
        with self.assertRaisesRegex(RuntimeError,"cancelled"): tangent.preflight(doc,buffers,stop)
        with self.assertRaisesRegex(RuntimeError,"cancelled"):
            tangent.build_patch(doc,buffers,source,[(1.,0.,0.,1.)]*6,buffer_name="a.bin",check=stop)

    def test_source_hash_recheck_refuses_mutation_and_output_escape(self):
        with tempfile.TemporaryDirectory() as temporary:
            root=Path(temporary); path=root/"input.bin"; path.write_bytes(b"source")
            admission=tangent.Admission(root); admission.pin(path,tangent.sha(b"source"))
            self.assertEqual(b"source",admission.read(path,16))
            path.write_bytes(b"change")
            with self.assertRaisesRegex(tangent.TangentError,"source changed"): admission.recheck()
            with self.assertRaises(ValueError): admission.pin(root.parent/"escape.bin","a"*64)

    def test_full_module_original_receipt_and_dependency_chain_is_authenticated(self):
        with tempfile.TemporaryDirectory() as temporary:
            root=Path(temporary); package,pin,_=package_fixture(root)
            receipt,inputs,admission=tangent.prepare(package,pin,root)
            self.assertEqual({"near","far"},set(inputs))
            self.assertEqual(4,len(inputs["near"][2].positions))
            self.assertEqual(2,len(inputs["far"][2].triangles))
            self.assertGreater(admission.bytes,0); admission.recheck()

    def test_mutated_original_source_dependency_refuses_even_when_module_copy_is_unchanged(self):
        with tempfile.TemporaryDirectory() as temporary:
            root=Path(temporary); package,pin,source=package_fixture(root)
            (source/"normal.png").write_bytes(b"changed source")
            with self.assertRaisesRegex(tangent.TangentError,"source bytes/hash changed"):
                tangent.prepare(package,pin,root)

    def test_independent_receipt_pin_and_qualified_original_prefix_are_required(self):
        with tempfile.TemporaryDirectory() as temporary:
            root=Path(temporary); package,pin,_=package_fixture(root)
            with self.assertRaisesRegex(tangent.TangentError,"source bytes/hash changed"):
                tangent.prepare(package,"f"*64,root)
            receipt=json.loads((package/"module-candidate-receipt.json").read_text())
            doc=json.loads((package/"near.gltf").read_text()); doc["materials"][0]["name"]="different rock"
            raw=tangent.author.canonical(doc); (package/"near.gltf").write_bytes(raw)
            for row in receipt["outputFiles"]:
                if row["file"] == "near.gltf": row.update(bytes=len(raw),sha256=tangent.sha(raw))
            raw=tangent.author.canonical(receipt); (package/"module-candidate-receipt.json").write_bytes(raw)
            with self.assertRaisesRegex(tangent.TangentError,"PBR differ"):
                tangent.prepare(package,tangent.sha(raw),root)

    def test_additive_buffer_name_cannot_escape_or_replace_source_dependency(self):
        doc,buffers,source=fixture()
        for name in ("../outside.bin","source.bin","normal.png"):
            with self.subTest(name=name),self.assertRaises(ValueError):
                tangent.build_patch(doc,buffers,source,[(1.,0.,0.,1.)]*6,buffer_name=name)


if __name__ == "__main__": unittest.main()

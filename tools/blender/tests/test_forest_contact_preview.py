"""Pure, bounded preview admission/proof regressions; root schedules execution."""
from copy import deepcopy
import importlib.util
import json
import math
from pathlib import Path
import struct
import sys
import tempfile
import unittest

TOOL = Path(__file__).resolve().parents[1]/"forest_contact_preview.py"
spec = importlib.util.spec_from_file_location("forest_contact_preview",TOOL)
preview = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = preview
spec.loader.exec_module(preview)

PIN = "a"*64
A = [(0.,0.,0.),(1.,0.,0.),(0.,0.,1.)]
C = [(2.,0.,0.),(3.,0.,0.),(2.,0.,1.)]


def fixture():
    originals = {}
    data = dict(meshes={},closure=dict(points=[list(p) for p in C],triangles=[[0,1,2]]))
    for name in ("near","physical","far"):
        positions = A if name == "near" else [(x,y+.125,z) for x,y,z in A]
        originals[name] = preview.quality.Mesh(positions,[(0,1,2)],preview.quality.Work())
        data["meshes"][name] = dict(vertices=[list(p) for p in positions+C],triangles=[[0,1,2],[3,4,5]],
            originalVertexCount=3,originalTriangleCount=1,newFaces=[dict(ordinal=1,visualPrimitiveTriangle=[1,0])])
    certificate = dict(complete=True,withinThreshold=True,thresholdRefuted=False,receiptSha256=PIN,
        uniformScaleExact="1",metric="Symmetric unsigned Euclidean distance between unions of closed triangles",
        certifiedSymmetricUpperBoundMetres=.125,measuredSymmetricLowerBoundMetres=.125,
        directions=[dict(name=name,complete=True,withinThreshold=True,thresholdRefuted=False,
                         sourceTriangles=1,sourceTrianglesCompleted=1,certifiedUpperSquaredWorldExact="1/64")
                    for name in ("near-to-physical","physical-to-near","near-to-far","far-to-near")])
    return certificate,originals,data


class TransferTests(unittest.TestCase):
    def test_shared_union_transfers_upper_bound_and_never_old_lower_bound(self):
        certificate,originals,data = fixture()
        result = preview.transfer_certificate(certificate,PIN,originals,data)
        self.assertTrue(result["complete"])
        self.assertEqual(.125,result["certifiedSymmetricUpperBoundMetres"])
        self.assertFalse(result["oldMeasuredLowerBoundTransferred"])
        self.assertNotIn("measuredSymmetricLowerBoundMetres",result)
        self.assertEqual(1,result["commonClosureTriangles"])

    def test_changed_original_prefix_or_one_lod_closure_refuses(self):
        for key in ("prefix","closure","record"):
            certificate,originals,data = fixture()
            if key == "prefix": data["meshes"]["near"]["vertices"][0][1] = .01
            elif key == "closure": data["meshes"]["far"]["vertices"][3][1] = .01
            else: data["closure"]["points"][0][1] = .01
            with self.assertRaises(preview.author.QualificationError):
                preview.transfer_certificate(certificate,PIN,originals,data)

    def test_wrong_receipt_scale_incomplete_direction_or_underreported_bound_refuses(self):
        for mutate in (lambda r:r.update(receiptSha256="b"*64),lambda r:r.update(uniformScaleExact="2"),
                       lambda r:r["directions"][0].update(sourceTrianglesCompleted=0),
                       lambda r:r.update(certifiedSymmetricUpperBoundMetres=.12),
                       lambda r:r["directions"][0].update(certifiedUpperSquaredWorldExact="1/4")):
            certificate,originals,data = fixture(); mutate(certificate)
            with self.assertRaises(preview.author.QualificationError):
                preview.transfer_certificate(certificate,PIN,originals,data)

    def test_boolean_nonfinite_and_out_of_range_geometry_cannot_match_numerically(self):
        for value in (True,float("inf"),1e6):
            certificate,originals,data = fixture()
            data["meshes"]["near"]["vertices"][1][0] = value
            with self.assertRaises(preview.author.QualificationError):
                preview.transfer_certificate(certificate,PIN,originals,data)


def visual_fixture():
    document = dict(asset=dict(version="2.0"),nodes=[dict(mesh=0)],meshes=[dict(primitives=[])],
                    materials=[{},{}],accessors=[],bufferViews=[])
    blob = bytearray()
    def accessor(values,kind,code,component):
        offset = len(blob)
        for row in values: blob.extend(struct.pack("<"+code*len(row),*row))
        view = len(document["bufferViews"])
        document["bufferViews"].append(dict(buffer=0,byteOffset=offset,byteLength=len(blob)-offset))
        index = len(document["accessors"])
        document["accessors"].append(dict(bufferView=view,componentType=component,type=kind,count=len(values)))
        return index
    for material,positions in enumerate((A,C)):
        attributes = dict(POSITION=accessor(positions,"VEC3","f",5126),
            NORMAL=accessor([(0.,1.,0.)]*3,"VEC3","f",5126),
            TEXCOORD_0=accessor([(0.,0.),(1.,0.),(0.,1.)],"VEC2","f",5126))
        index = accessor([(0,),(1,),(2,)],"SCALAR","I",5125)
        document["meshes"][0]["primitives"].append(dict(attributes=attributes,indices=index,material=material))
    _,_,data = fixture()
    return document,[bytes(blob)],data["meshes"]["near"]


class VisualDecodeTests(unittest.TestCase):
    def test_measured_mikk_difference_is_diagnostic_but_native_normal_mismatch_still_refuses(self):
        result = preview.compare_native_tangent_frame((-1,0,0),(0,1,0),-1,
                    (-1,0,-2.5932075004675426e-5),(0,1,0),-1)
        self.assertFalse(result["clear"])
        self.assertTrue(preview.native_normal_import_clear(result))
        self.assertEqual(2e-5,result["tangentTolerance"])
        result["actualNormal"] = [0,1,3e-5]
        self.assertFalse(preview.native_normal_import_clear(result))
        result["actualNormal"] = [0,float("nan"),0]
        self.assertFalse(preview.native_normal_import_clear(result))

    def test_explicit_encoded_basis_retains_gltf_handedness_after_image_v_flip(self):
        # glTF +Y normal, +X tangent, w=-1 yields +Z for a green normal-map direction.
        # The proper XYZ -> X,-Z,Y rotation maps that to Blender -Y, not +Y from a second sign flip.
        cv = lambda p:(p[0],-p[2],p[1])
        n,t = (0,1,0),(1,0,0,-1)
        for sample,expected in (((.5,.5,1),(0,1,0)),((1,.5,.5),(1,0,0)),((.5,1,.5),(0,0,1))):
            result = preview.authored_normal_map_vector(n,t,sample)
            self.assertEqual(expected,result)
            converted = preview.authored_normal_map_vector(cv(n),(*cv(t[:3]),t[3]),sample)
            self.assertEqual(cv(result),converted)
        self.assertEqual((0.,0.,-1.),preview.authored_normal_map_vector(n,(1,0,0,1),(.5,1,.5)))
        self.assertEqual((0.,1.,0.),preview.authored_normal_map_vector(n,t,(1,1,1),0))
        with self.assertRaises(preview.author.QualificationError): preview.authored_normal_map_vector(n,(0,1,0,1),(.5,.5,1))

    def test_native_float_normal_version_is_explicit_not_a_silent_fallback(self):
        preview.require_native_float_normal_version((5,2,1))
        for version in ((5,2,0),(5,2,2),(6,0,0)):
            with self.assertRaises(preview.author.QualificationError): preview.require_native_float_normal_version(version)

    def test_only_exact_authored_geometric_normals_select_native_flat_path(self):
        row = dict(positions=A,triangles=[(0,1,2)],normals=[(0.,-1.,0.)]*3)
        self.assertTrue(preview.has_exact_authored_flat_normals(row))
        row["normals"] = [(0.,1.,0.)]*3
        self.assertFalse(preview.has_exact_authored_flat_normals(row))
        row["normals"] = [(0.,-1.,0.),(0.,-1.,1e-7),(0.,-1.,0.)]
        self.assertFalse(preview.has_exact_authored_flat_normals(row))

    def test_native_tangent_diagnostics_keep_thresholds_and_handedness_strict(self):
        clear = preview.compare_native_tangent_frame((1,0,0),(0,0,1),-1,(1,0,0),(0,0,1),-1)
        self.assertTrue(clear["clear"])
        for tangent,normal,sign in (((1,3e-5,0),(0,0,1),-1),
                                     ((1,0,0),(0,3e-5,1),-1),
                                     ((1,0,0),(0,0,1),1),
                                     ((float("nan"),0,0),(0,0,1),-1)):
            result = preview.compare_native_tangent_frame((1,0,0),(0,0,1),-1,tangent,normal,sign)
            self.assertFalse(result["clear"])
            self.assertEqual(list(tangent),result["actualTangent"])
            self.assertEqual(2e-5,result["tangentTolerance"])
            self.assertEqual(2e-5,result["normalTolerance"])

    def test_actual_position_normal_uv_and_added_face_mapping_are_retained(self):
        document,buffers,record = visual_fixture()
        result = preview.decode_visual(document,buffers,record)
        self.assertEqual(A,result[0]["positions"])
        self.assertEqual([(0.,1.,0.)]*3,result[0]["normals"])
        self.assertEqual([(0.,0.),(1.,0.),(0.,1.)],result[0]["uv"])
        self.assertEqual(C,result[1]["positions"])

    def test_unmapped_or_moved_visual_triangle_refuses(self):
        for mode in ("missing","moved"):
            document,buffers,record = visual_fixture()
            if mode == "missing": record["newFaces"] = []
            else: record["vertices"][3][1] = .01
            with self.assertRaises(preview.author.QualificationError): preview.decode_visual(document,buffers,record)

    def test_sparse_accessor_escape_or_nonidentity_node_refuses(self):
        for mode in ("sparse","escape","transform"):
            document,buffers,record = visual_fixture()
            if mode == "sparse": document["accessors"][0]["sparse"] = {}
            elif mode == "escape": document["accessors"][0]["byteOffset"] = len(buffers[0])
            else: document["nodes"][0]["translation"] = [0,0,1]
            with self.assertRaises(preview.author.QualificationError): preview.decode_visual(document,buffers,record)

    def test_optional_tangent_stream_matches_derivatives_and_keeps_original_absence(self):
        document,buffers,record = visual_fixture()
        # This fixture's +Y normals lie opposite the triangle geometric normal.
        # The explicit w=-1 still reconstructs the actual UV +Z bitangent.
        raw = bytearray(buffers[0]); offset = len(raw)
        for _ in range(3): raw.extend(struct.pack("<ffff",1,0,0,-1))
        view = len(document["bufferViews"])
        document["bufferViews"].append(dict(buffer=0,byteOffset=offset,byteLength=48))
        index = len(document["accessors"])
        document["accessors"].append(dict(bufferView=view,componentType=5126,type="VEC4",count=3))
        document["meshes"][0]["primitives"][1]["attributes"]["TANGENT"] = index
        result = preview.decode_visual(document,[bytes(raw)],record)
        self.assertIsNone(result[0]["tangents"])
        self.assertEqual([(1.,0.,0.,-1.)]*3,result[1]["tangents"])
        self.assertTrue(result[1]["tangentEvidence"]["encodedUnitOrthogonalHandednessVerified"])
        for mode in ("wrong-w","wrong-direction","non-unit","nonfinite","wrong-width","unexpected-attribute"):
            changed = deepcopy(document); blob = bytearray(raw)
            if mode == "wrong-width": changed["accessors"][index]["type"] = "VEC3"
            elif mode == "unexpected-attribute": changed["meshes"][0]["primitives"][1]["attributes"]["COLOR_0"] = index
            else:
                value = {"wrong-w":(1,0,0,1),"wrong-direction":(-1,0,0,-1),
                         "non-unit":(2,0,0,-1),"nonfinite":(float("nan"),0,0,-1)}[mode]
                struct.pack_into("<ffff",blob,offset,*value)
            with self.assertRaises(preview.author.QualificationError): preview.decode_visual(changed,[bytes(blob)],record)

    def test_tangent_validation_refuses_nonorthogonal_normal_and_fractional_handedness(self):
        for tangent in ((1,0,0,.5),(0,1,0,1)):
            with self.assertRaises(preview.author.QualificationError):
                preview.validate_tangents(A,[(0,1,0)]*3,[(0,0),(1,0),(0,1)],[tangent]*3,[(0,1,2)])


class ShadingProofTests(unittest.TestCase):
    def fixture(self,smooth=True):
        closure = dict(points=[(0.,0.,0.),(1.,0.,0.),(0.,0.,1.),(1.,.5,1.)],
                       triangles=[(0,2,1),(1,2,3)],groups=["toe-apron-candidate"]*2)
        normal = preview.module.soil_normals.build(closure["points"],closure["triangles"],closure["groups"]) if smooth else None
        uv = preview.module.metric_uv.build(closure["points"],closure["triangles"],closure["groups"],
                corner_normals=normal.corner_normals if smooth else None)
        receipt = dict(uvSchema=preview.module.metric_uv.SCHEMA,closureGeometrySha256=uv.evidence["closureGeometrySha256"],
                       addedAttributeStreamSha256=uv.evidence["addedAttributeStreamSha256"])
        data = dict(closure=closure,uvAuthoring=uv.evidence)
        if smooth:
            receipt.update(normalSchema=preview.module.soil_normals.SCHEMA,normalStreamSha256=normal.evidence["normalStreamSha256"])
            data["normalAuthoring"] = normal.evidence
        # Actual receipts/proofs have undergone JSON encoding and decoding.
        return json.loads(json.dumps([receipt,data,uv.evidence,None if normal is None else normal.evidence]))

    def test_normal_proof_roundtrip_reproduces_shading_and_exact_geometry_identity(self):
        args = self.fixture()
        result = preview.verify_authored_shading(*args)
        self.assertEqual(args[2],result.evidence)
        self.assertEqual(args[0]["closureGeometrySha256"],args[3]["closureGeometrySha256"])
        self.assertNotEqual(result.faces[0].normals[0],result.faces[0].normals[1])

    def test_modified_normal_proof_or_receipt_cannot_be_used_as_authority(self):
        for mode in ("stream","geometry","fan","policy","schema"):
            receipt,data,uv,normal = self.fixture()
            if mode == "stream": receipt["normalStreamSha256"] = "b"*64
            elif mode == "geometry": receipt["closureGeometrySha256"] = "b"*64
            elif mode == "fan": normal["fans"][0]["normal"] = [0,0,1]; data["normalAuthoring"] = normal
            elif mode == "policy": normal["policy"]["smoothGroups"] = ["back-wall"]; data["normalAuthoring"] = normal
            else: receipt["normalSchema"] = "unreviewed"
            with self.assertRaises(ValueError): preview.verify_authored_shading(receipt,data,uv,normal)

    def test_prior_flat_package_reproduces_but_unbound_smoothing_proof_refuses(self):
        args = self.fixture(False)
        self.assertEqual(args[2],preview.verify_authored_shading(*args).evidence)
        args[3] = self.fixture()[3]
        with self.assertRaises(preview.author.QualificationError): preview.verify_authored_shading(*args)


class ShaderGraphTests(unittest.TestCase):
    def test_actual_node_graph_reconstructs_the_authored_basis_without_mikk_or_green_inversion(self):
        # A minimal evaluator for the actual graph function, independent of the CPU oracle.
        class Socket:
            def __init__(self,node,key): self.node,self.key,self.default_value,self.source = node,key,0,None
        class Sockets(dict):
            def __init__(self,node): super().__init__(); self.node = node
            def __missing__(self,key): self[key] = Socket(self.node,key); return self[key]
        class Node:
            def __init__(self,kind): self.kind = kind; self.inputs,self.outputs = Sockets(self),Sockets(self)
        class Nodes(list):
            def new(self,kind): node = Node(kind); self.append(node); return node
        class Links:
            def new(self,source,target): target.source = source
        nodes,links = Nodes(),Links()
        colour = nodes.new("sample").outputs[0]
        result = preview.link_authored_normal_map(nodes,links,colour,.7)
        self.assertLessEqual(len(nodes),32)
        self.assertFalse(any(node.kind == "ShaderNodeNormalMap" for node in nodes))
        self.assertEqual({"hollow_authored_normal","hollow_authored_tangent","hollow_authored_sign"},
                         {node.attribute_name for node in nodes if node.kind == "ShaderNodeAttribute"})
        attributes = dict(hollow_authored_normal=(0.,.6,.8),hollow_authored_tangent=(1.,.2,.1),hollow_authored_sign=-1.)
        sample = (1.,.8,.9)
        def evaluate(socket):
            if socket.source is not None: return evaluate(socket.source)
            node = socket.node
            if socket in node.inputs.values(): return socket.default_value
            if node.kind == "sample": return sample
            if node.kind == "ShaderNodeAttribute": return attributes[node.attribute_name]
            if node.kind == "ShaderNodeSeparateColor": return evaluate(node.inputs[0])[dict(Red=0,Green=1,Blue=2)[socket.key]]
            if node.kind == "ShaderNodeMath":
                self.assertEqual("MULTIPLY_ADD",node.operation)
                return evaluate(node.inputs[0])*evaluate(node.inputs[1])+evaluate(node.inputs[2])
            if node.kind == "ShaderNodeVectorTransform":
                self.assertEqual(("NORMAL","OBJECT","WORLD"),(node.vector_type,node.convert_from,node.convert_to))
                return evaluate(node.inputs[0])  # Authenticated identity mesh/object transform.
            self.assertEqual("ShaderNodeVectorMath",node.kind)
            a = evaluate(node.inputs[0]); operation = node.operation
            if operation == "NORMALIZE": return tuple(v/math.sqrt(sum(x*x for x in a)) for v in a)
            if operation == "SCALE": return tuple(v*evaluate(node.inputs[3]) for v in a)
            b = evaluate(node.inputs[1])
            if operation == "DOT_PRODUCT": return sum(x*y for x,y in zip(a,b))
            if operation == "CROSS_PRODUCT": return (a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0])
            if operation == "SUBTRACT": return tuple(x-y for x,y in zip(a,b))
            self.assertEqual("ADD",operation); return tuple(x+y for x,y in zip(a,b))
        for sign in (-1.,1.):
            attributes["hollow_authored_sign"] = sign
            expected = preview.authored_normal_map_vector(attributes["hollow_authored_normal"],
                        (*attributes["hollow_authored_tangent"],sign),sample,.7)
            for actual,wanted in zip(evaluate(result),expected): self.assertAlmostEqual(actual,wanted,places=12)


class ImageHeaderTests(unittest.TestCase):
    def test_png_and_jpeg_dimensions_are_preflighted_without_decoding(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)/"image"
            path.write_bytes(b"\x89PNG\r\n\x1a\n"+struct.pack(">I",13)+b"IHDR"+struct.pack(">II",8192,4096)+b"\x08\x02\x00\x00\x00"+b"\0"*4)
            self.assertEqual((8192,4096),preview.image_dimensions(path))
            path.write_bytes(b"\xff\xd8\xff\xc0"+struct.pack(">HBHHB",8,8,64,128,3))
            self.assertEqual((128,64),preview.image_dimensions(path))

    def test_oversized_or_absent_frame_header_refuses(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)/"image"
            for raw in (b"bad",b"\xff\xd8\xff\xda",b"\x89PNG\r\n\x1a\n"+struct.pack(">I",13)+b"IHDR"+struct.pack(">II",8193,8192)+b"\0"*9):
                path.write_bytes(raw)
                with self.assertRaises(preview.author.QualificationError): preview.image_dimensions(path)


if __name__ == "__main__":
    unittest.main()

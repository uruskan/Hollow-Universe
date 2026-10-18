"""Pure additive appearance tests; no Blender, export, render or game invocation."""
from copy import deepcopy
import importlib.util
import json
import math
from pathlib import Path
import struct
import sys
import unittest
from unittest.mock import patch as mocked

TOOL=Path(__file__).resolve().parents[1]/"forest_contact_appearance.py"
spec=importlib.util.spec_from_file_location("forest_contact_appearance",TOOL)
appearance=importlib.util.module_from_spec(spec); sys.modules[spec.name]=appearance; spec.loader.exec_module(appearance)


def recipe():
    return json.loads((TOOL.parent/"data"/"forest_contact_appearance.json").read_text())


def fixture():
    rim=[(-1.,0.,-1.),(1.,0.,-1.),(1.,0.,1.),(-1.,0.,1.)]
    ring=lambda scale:[tuple(appearance.f32(v*scale) for v in p) for p in rim]
    points=rim+ring(1.2)+ring(2)
    closure=[]
    for inner,outer in ((0,4),(4,8)):
        for i in range(4):
            j=(i+1)%4
            closure.extend([(inner+i,outer+i,outer+j),(inner+i,outer+j,inner+j)])
    arcs=[dict(role=role,boundaryPointIds=[i,(i+1)%4],collarPointIds=[i+4,(i+1)%4+4],
               outerGuidePointIds=[i+8,(i+1)%4+8]) for i,role in enumerate(appearance.ROLES)]
    source=rim+[(0.,0.,0.)]; original=[(i,(i+1)%4,4) for i in range(4)]
    def record(vertices,faces):
        mapping=list(range(4))+list(range(len(vertices),len(vertices)+8))
        return dict(vertices=vertices+points[4:],triangles=faces+[tuple(mapping[v] for v in t) for t in closure],
            originalVertexCount=len(vertices),originalTriangleCount=len(faces),
            newFaces=[dict(ordinal=len(faces)+i,closureOrdinal=i,visualPrimitiveTriangle=[1,i]) for i in range(len(closure))])
    data=dict(scale=1,assetAccepted=False,closure=dict(points=points,triangles=closure,
        originalOuterRim=rim,outerGuide=[8,9,10,11],arcs=arcs),
        meshes=dict(near=record(source,original),far=record(rim,[(0,1,2),(0,2,3)])))
    def visual(vertices,faces):
        def primitive(p,t,material): return dict(positions=p,normals=[(0.,1.,0.)]*len(p),
            uv=[(v[0],v[2]) for v in p],tangents=[(1.,0.,0.,1.)]*len(p),triangles=t,material=material)
        return [primitive(vertices,faces,0),primitive(points,closure,1)]
    return data,dict(near=visual(source,original),far=visual(rim,[(0,1,2),(0,2,3)]))


class AppearanceTests(unittest.TestCase):
    def test_real_rim_endpoints_match_and_outer_contact_is_exact(self):
        data,_=fixture(); original=deepcopy(data)
        source,closure,evidence=appearance.build_fields(data,recipe())
        self.assertEqual(original,data)
        for i,p in enumerate(data["closure"]["originalOuterRim"]):
            self.assertEqual(source[p],closure[i]); self.assertEqual((1.,0.),closure[i][:2])
        self.assertTrue(all(closure[i][1]==1 for i in data["closure"]["outerGuide"]))
        self.assertEqual((0.,0.,0.,0.),source[(0.,0.,0.)])
        self.assertIn("simplified far interiors",evidence["interpolationRisk"])

    def test_all_encoded_domain_values_are_finite_and_partition_valid(self):
        data,_=fixture(); source,closure,_=appearance.build_fields(data,recipe())
        for row in [*source.values(),*closure]:
            self.assertTrue(all(math.isfinite(v) and 0<=v<=1 and appearance.f32(v)==v for v in row))
            self.assertLessEqual(row[2]+row[3],1)

    def test_source_uv_and_attributes_never_enter_mask_generation(self):
        data,visual=fixture(); before=deepcopy(visual)
        source,closure,_=appearance.build_fields(data,recipe())
        blob,description=appearance.encode_lod("near",data["meshes"]["near"],visual["near"],source,closure,data["closure"])
        self.assertEqual(before,visual); self.assertFalse(description["physicalGeometryChanged"])
        self.assertFalse(description["sourceAttributesChanged"])
        self.assertEqual(16*sum(len(v["positions"]) for v in visual["near"]),len(blob))
        self.assertTrue(description["primitives"][0]["sourceAtlasSamplingAllowed"])
        self.assertFalse(description["primitives"][1]["sourceAtlasSamplingAllowed"])

    def test_near_far_source_vertices_and_added_corner_masks_match(self):
        data,visual=fixture(); source,closure,_=appearance.build_fields(data,recipe())
        a,ad=appearance.encode_lod("near",data["meshes"]["near"],visual["near"],source,closure,data["closure"])
        b,bd=appearance.encode_lod("far",data["meshes"]["far"],visual["far"],source,closure,data["closure"])
        self.assertEqual(a[:4*16],b[:4*16])
        self.assertEqual(ad["primitives"][1]["cornerMaskSha256"],bd["primitives"][1]["cornerMaskSha256"])

    def test_domains_follow_authored_arc_roles_not_height(self):
        data,_=fixture(); _,before,_=appearance.build_fields(data,recipe())
        for arc in data["closure"]["arcs"]:
            if arc["role"]=="crest": arc["role"]="toe"
            elif arc["role"]=="toe": arc["role"]="crest"
        _,after,_=appearance.build_fields(data,recipe())
        self.assertNotEqual(before,after)
        self.assertEqual(before[0][2:],after[0][2:][::-1])

    def test_consistent_arc_order_does_not_depend_on_request_order(self):
        data,_=fixture(); a=appearance.build_fields(data,recipe())
        data["closure"]["arcs"].reverse(); b=appearance.build_fields(data,recipe())
        self.assertEqual(a,b)

    def test_graph_distances_report_existing_edges_not_straight_distance(self):
        work=appearance.Work()
        adjacency=(((1,2.),),((0,2.),(2,3.)),((1,3.),))
        result=appearance.distances(adjacency,{0:7},work)
        self.assertEqual((5.,7),result[2])
        limited=appearance.distances(adjacency,{0:7},appearance.Work(),maximum=2)
        self.assertEqual((math.inf,-1),limited[2])

    def test_equal_distance_seed_ties_use_stable_label_not_insertion(self):
        adjacency=(((1,1.),),((0,1.),(2,1.)),((1,1.),))
        a=appearance.distances(adjacency,{2:4,0:3},appearance.Work())
        b=appearance.distances(adjacency,{0:3,2:4},appearance.Work())
        self.assertEqual(a,b); self.assertEqual((1.,3),a[1])

    def test_edge_heap_push_and_visit_caps_refuse_without_output(self):
        for field,value in (("maximum_edges",1),("maximum_pushes",1),("maximum_visits",1),("maximum_heap",1)):
            data,_=fixture(); work=appearance.Work(**{field:value})
            with self.subTest(field=field),self.assertRaisesRegex(ValueError,"capacity"):
                appearance.build_fields(data,recipe(),work)

    def test_cancellation_is_polled_before_authoring(self):
        data,_=fixture()
        def stop(): raise RuntimeError("cancelled")
        with self.assertRaisesRegex(RuntimeError,"cancelled"):
            appearance.build_fields(data,recipe(),appearance.Work(stop))

    def test_invalid_recipe_bands_permissions_and_unknown_materials_refuse(self):
        for update in ({"scale":2},{"geometryChanges":True},{"rootPermissions":True},
                {"anchorTransitionMetres":0},{"outerContactBandMetres":float("nan")},
                {"materials":{"cap":{"0":1},"toe":{"2":1},"return":{"3":1}}}):
            proposal=recipe(); proposal.update(update)
            with self.subTest(update=update),self.assertRaises(ValueError): appearance.validate_recipe(proposal)

    def test_unproven_interior_hole_or_missing_boundary_cannot_seed_soil(self):
        data,_=fixture(); data["meshes"]["near"]["triangles"][0]=(0,1,3)
        with self.assertRaisesRegex(ValueError,"boundary|Nonmanifold"):
            appearance.build_fields(data,recipe())

    def test_arc_missing_reversed_or_duplicated_edges_refuse(self):
        for change in (lambda a:a["boundaryPointIds"].reverse(),lambda a:a.update(role="toe"),
                       lambda a:a.update(boundaryPointIds=[0,2])):
            data,_=fixture(); change(data["closure"]["arcs"][0])
            with self.assertRaises(ValueError): appearance.build_fields(data,recipe())

    def test_overlapping_anchor_bands_refuse(self):
        data,_=fixture(); proposal=recipe(); proposal["anchorTransitionMetres"]=1.1
        with self.assertRaisesRegex(ValueError,"overlap"):
            appearance.build_fields(data,proposal)

    def test_ambiguous_or_displaced_common_rim_refuses(self):
        data,_=fixture(); data["closure"]["points"][0]=(0.,0.,-1.)
        with self.assertRaisesRegex(ValueError,"rim"):
            appearance.build_fields(data,recipe())

    def test_far_new_position_cannot_be_filled_by_nearest_guess(self):
        data,visual=fixture(); source,closure,_=appearance.build_fields(data,recipe())
        visual["far"][0]["positions"]=list(visual["far"][0]["positions"])
        visual["far"][0]["positions"][0]=(-.5,0.,-1.)
        with self.assertRaisesRegex(ValueError,"exact retained"):
            appearance.encode_lod("far",data["meshes"]["far"],visual["far"],source,closure,data["closure"])

    def test_added_visual_mapping_must_be_total_and_exact(self):
        data,visual=fixture(); source,closure,_=appearance.build_fields(data,recipe())
        for change in (lambda r:r["newFaces"].pop(),lambda r:r["newFaces"][1].update(visualPrimitiveTriangle=[1,0])):
            record=deepcopy(data["meshes"]["near"]); change(record)
            with self.assertRaises(ValueError):
                appearance.encode_lod("near",record,visual["near"],source,closure,data["closure"])
        visual["near"][1]["triangles"]=[*visual["near"][1]["triangles"],visual["near"][1]["triangles"][0]]
        with self.assertRaisesRegex(ValueError,"Unmapped added visual triangle"):
            appearance.encode_lod("near",data["meshes"]["near"],visual["near"],source,closure,data["closure"])

    def test_same_inputs_emit_identical_float_bytes_and_descriptors(self):
        data,visual=fixture(); source,closure,_=appearance.build_fields(data,recipe())
        a=appearance.encode_lod("near",data["meshes"]["near"],visual["near"],source,closure,data["closure"])
        b=appearance.encode_lod("near",data["meshes"]["near"],visual["near"],source,closure,data["closure"])
        self.assertEqual(a,b)

    def test_tangent_append_mapping_inherits_masks_and_preserves_corner_identity(self):
        data,visual=fixture(); source,closure,_=appearance.build_fields(data,recipe())
        _,base=appearance.encode_lod("near",data["meshes"]["near"],visual["near"],source,closure,data["closure"])
        original=visual["near"][0]
        indices=[v for t in original["triangles"] for v in t]; indices[0]=5
        patch=dict(sourceVertexForOutput=[0,1,2,3,4,0],outputCornerIndices=indices,
            perCornerPositionNormalUvSha256=base["primitives"][0]["baseCornerPositionNormalUvSha256"],
            encodedTangentSha256=appearance.stream_sha([(1.,0.,0.,1.)]*6,"<4f"),appearanceAdmissionBlocked=True)
        frames=[(1.,0.,0.,1.)]*6
        blob,result=appearance.encode_lod("near",data["meshes"]["near"],visual["near"],source,closure,data["closure"],patch,frames)
        self.assertEqual(blob[:16],blob[80:96])
        self.assertEqual(base["primitives"][0]["cornerMaskSha256"],result["primitives"][0]["cornerMaskSha256"])
        self.assertNotEqual(base["primitives"][0]["orderedVertexPositionNormalUvSha256"],
            result["primitives"][0]["orderedVertexPositionNormalUvSha256"])
        self.assertNotEqual(base["primitives"][0]["orderedLocalIndexSha256"],
            result["primitives"][0]["orderedLocalIndexSha256"])
        self.assertEqual(6,result["primitives"][1]["firstVertex"])
        self.assertTrue(result["tangentAppearanceAdmissionBlocked"])
        patch["sourceVertexForOutput"][-1]=1
        with self.assertRaisesRegex(ValueError,"corner mapping"):
            appearance.encode_lod("near",data["meshes"]["near"],visual["near"],source,closure,data["closure"],patch,frames)

    def test_final_ordered_stream_hashes_and_canonical_offsets_are_explicit(self):
        data,visual=fixture(); source,closure,_=appearance.build_fields(data,recipe())
        blob,result=appearance.encode_lod("near",data["meshes"]["near"],visual["near"],source,closure,data["closure"])
        self.assertEqual("forest-contact-additive-appearance-lod-v1",result["schema"])
        self.assertEqual("near-appearance.bin",result["dataFile"])
        self.assertEqual((17,20),(result["vertexCount"],result["triangleCount"]))
        self.assertEqual(appearance.sha(blob),result["sha256"])
        first_vertex=first_triangle=0
        for row,descriptor in zip(visual["near"],result["primitives"]):
            self.assertEqual((first_vertex,first_triangle),(descriptor["firstVertex"],descriptor["firstTriangle"]))
            self.assertEqual(first_vertex*16,descriptor["byteOffset"])
            vertex_bytes=b"".join(struct.pack("<8f",*p,*n,*uv) for p,n,uv in zip(row["positions"],row["normals"],row["uv"]))
            index_bytes=b"".join(struct.pack("<I",v) for tri in row["triangles"] for v in tri)
            tangent_bytes=b"".join(struct.pack("<4f",*t) for t in row["tangents"])
            self.assertEqual(appearance.sha(vertex_bytes),descriptor["orderedVertexPositionNormalUvSha256"])
            self.assertEqual(appearance.sha(index_bytes),descriptor["orderedLocalIndexSha256"])
            self.assertEqual(appearance.sha(tangent_bytes),descriptor["orderedTangentSha256"])
            first_vertex+=len(row["positions"]); first_triangle+=len(row["triangles"])

    def test_missing_tangents_and_invalid_mask_values_refuse(self):
        data,visual=fixture(); source,closure,_=appearance.build_fields(data,recipe())
        visual["near"][1]["tangents"]=None
        with self.assertRaisesRegex(ValueError,"tangent stream"):
            appearance.encode_lod("near",data["meshes"]["near"],visual["near"],source,closure,data["closure"])
        data,visual=fixture(); source[(-1.,0.,-1.)]=(1.1,0.,0.,0.)
        with self.assertRaisesRegex(ValueError,"four-channel"):
            appearance.encode_lod("near",data["meshes"]["near"],visual["near"],source,closure,data["closure"])

    def test_lod_aggregate_cap_is_checked_before_output_allocation(self):
        data,visual=fixture(); source,closure,_=appearance.build_fields(data,recipe())
        for constant,limit in (("MAX_VERTICES",16),("MAX_TRIANGLES",19)):
            with mocked.object(appearance,constant,limit),self.assertRaisesRegex(ValueError,"aggregate geometry"):
                appearance.encode_lod("near",data["meshes"]["near"],visual["near"],source,closure,data["closure"])

    def test_bound_actual_tangent_accessors_must_match_source_mapping_bytes(self):
        _,visual=fixture(); original=visual["near"][0]; count=len(original["positions"])
        blob=bytearray(); views=[]; accessors=[]
        def append(rows,kind,format,component=5126):
            start=len(blob)
            for row in rows: blob.extend(struct.pack(format,*row))
            views.append(dict(buffer=1,byteOffset=start,byteLength=len(blob)-start))
            accessors.append(dict(bufferView=len(views)-1,count=len(rows),type=kind,componentType=component))
            return len(accessors)-1
        attrs={key:append(original[source],kind,format) for key,source,kind,format in
            (("POSITION","positions","VEC3","<3f"),("NORMAL","normals","VEC3","<3f"),
             ("TEXCOORD_0","uv","VEC2","<2f"),("TANGENT","tangents","VEC4","<4f"))}
        indices=[v for tri in original["triangles"] for v in tri]
        index=append([(v,) for v in indices],"SCALAR","<I",5125)
        target=dict(buffers=[dict(byteLength=1),dict(byteLength=len(blob))],bufferViews=views,accessors=accessors,
            meshes=[dict(primitives=[dict(attributes=attrs,indices=index)])])
        metadata=dict(sourceVertexForOutput=list(range(count)),outputCornerIndices=indices,
            encodedTangentSha256=appearance.stream_sha(original["tangents"],"<4f"))
        with mocked.object(appearance.tangent,"apply_patch_document",return_value=target):
            frames=appearance.bind_patch_streams({},[b"x"],metadata,bytes(blob),original)
            self.assertEqual(original["tangents"],frames)
            changed=bytearray(blob); struct.pack_into("<f",changed,0,-.5)
            with self.assertRaisesRegex(ValueError,"source attribute bytes: POSITION"):
                appearance.bind_patch_streams({},[b"x"],metadata,bytes(changed),original)
            # Signed zero is geometrically equivalent but must preserve the byte chain.
            changed=bytearray(blob); struct.pack_into("<f",changed,4,-0.)
            with self.assertRaisesRegex(ValueError,"source attribute bytes: POSITION"):
                appearance.bind_patch_streams({},[b"x"],metadata,bytes(changed),original)
            changed=bytearray(blob); tangent_start=views[attrs["TANGENT"]]["byteOffset"]
            struct.pack_into("<f",changed,tangent_start,appearance.f32(.99))
            with self.assertRaisesRegex(ValueError,"tangent stream hash"):
                appearance.bind_patch_streams({},[b"x"],metadata,bytes(changed),original)

    def test_nonfinite_non_float32_and_zero_graph_edges_refuse(self):
        for points,triangles in (([(0.,0.,0.),(0.,0.,0.),(1.,0.,0.)],[(0,1,2)]),
                ([(0.,0.,0.),(.1,0.,0.),(0.,1.,0.)],[(0,1,2)]),
                ([(math.inf,0.,0.),(1.,0.,0.),(0.,1.,0.)],[(0,1,2)])):
            with self.assertRaises(ValueError): appearance.graph(points,triangles,appearance.Work())


if __name__=="__main__": unittest.main()

"""Bounded additive MikkTSpace patches for the original forest rock primitive.

Run only in a root-serialized Blender 5.2.1 background process (512 MiB/60 s
external guard; 45 s internal deadline). No image decoding or rendering. Source
packages, geometry, materials and existing attribute buffers are never written.
The fresh output is a reusable PATCH, not an admitted/renderable asset package:
apply_patch_document verifies the base and appends its one buffer/views/accessors.
The caller must copy/re-pin every existing dependency and update its qualification
receipt before admitting the resulting glTF. Near/far triangle and corner ordinals
stay fixed. Splits require explicit --allow-vertex-splits and append duplicates to
the original vertex prefix; per-corner POSITION/NORMAL/UV bytes remain identical.

Mikk's averaged frames are NOT the per-face derivative frames used by the new
soil closure. We validate finite nonzero frames and exact UV handedness, report
all encoded unit/orthogonality residuals, and preserve native Mikk output without averaging,
renormalization, UV flips, axis changes or an undefined-tangent fallback.
Per-face derivative agreement is diagnostic, not Mikk's 180-degree averaging
contract. Source-normal hemispheres are checked separately using exact signs;
any negative/tangent corner blocks appearance admission, not tangent encoding.
"""
from __future__ import annotations

import argparse
from copy import deepcopy
from dataclasses import dataclass
from fractions import Fraction
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import struct
import sys
import time

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
spec = importlib.util.spec_from_file_location("_forest_tangent_preview", HERE/"forest_contact_preview.py")
preview = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = preview
spec.loader.exec_module(preview)
author = preview.author
SCHEMA = "forest-original-mikk-corner-patch-v3"
MAX_VERTICES, MAX_TRIANGLES, MAX_BUFFERS, MAX_TABLE = 60000, 50000, 8, 128
MAX_ENCODED, MAX_WORK_BYTES, MAX_ADMITTED = 32*1024*1024, 256*1024*1024, 256*1024*1024
# Explicit visual-authoring policy, NOT a universal bound on Mikk float32 error.
# Cancellation can amplify a small native averaging/projection residual. Preserve
# native values and report every outlier; the runtime decoder's independent unit
# vector admission is squared length 0.98..1.02 and has no orthogonality gate.
FRAME_UNIT_SQUARED_TOLERANCE = FRAME_NORMALIZED_DOT_TOLERANCE = 1e-4


class TangentError(ValueError):
    pass


def require(condition, message):
    if not condition: raise TangentError(message)


def f32(value): return struct.unpack("<f", struct.pack("<f", value))[0]
def dot(a,b): return sum(x*y for x,y in zip(a,b))
def sub(a,b): return tuple(x-y for x,y in zip(a,b))
def cross(a,b): return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])
def sha(raw): return hashlib.sha256(raw).hexdigest()
def packed(rows,width): return b"".join(struct.pack("<"+"f"*width,*row) for row in rows)
def integer(value,low,high,label):
    require(type(value) is int and low <= value <= high,"Invalid "+label)
    return value


@dataclass(frozen=True)
class Primitive:
    positions: list
    normals: list
    uvs: list
    triangles: list
    aggregate_vertices: int
    aggregate_triangles: int
    encoded_bytes: int
    reserved_bytes: int


def descriptor_budget(document,buffers,check=lambda:None):
    """Admit totals BEFORE decoding source attributes or allocating native meshes."""
    require(document.get("asset",{}).get("version") == "2.0" and not document.get("extensionsRequired")
        and len(document.get("meshes",[])) == 1 and len(document.get("nodes",[])) == 1
        and set(document["nodes"][0]) <= {"name","mesh"} and document["nodes"][0]["mesh"] == 0,
        "Expected one identity-transform glTF mesh")
    require(type(buffers) is list and 1 <= len(buffers) < MAX_BUFFERS and len(document["buffers"]) == len(buffers)
        and len(document["accessors"])+5 <= MAX_TABLE and len(document["bufferViews"])+5 <= MAX_TABLE,
        "No bounded room for the additive tangent patch")
    primitives = document["meshes"][0]["primitives"]
    require(1 <= len(primitives) <= 32,"Primitive count exceeds decoder contract")
    vertices = triangles = 0
    for row in primitives:
        check()
        require(row.get("mode",4) == 4 and not row.get("targets") and not row.get("extensions"),"Unsupported primitive topology")
        position_accessor=integer(row["attributes"]["POSITION"],0,len(document["accessors"])-1,"position accessor")
        index_accessor=integer(row["indices"],0,len(document["accessors"])-1,"index accessor")
        count = integer(document["accessors"][position_accessor]["count"],1,MAX_VERTICES,"primitive vertices")
        indices = integer(document["accessors"][index_accessor]["count"],3,MAX_TRIANGLES*3,"primitive indices")
        require(indices%3 == 0,"Triangle index count is not divisible by three")
        vertices += count; triangles += indices//3
    require(vertices <= MAX_VERTICES and triangles <= MAX_TRIANGLES,"Aggregate decoder geometry capacity exceeded")
    encoded = sum(len(raw) for raw in buffers)
    require(all(type(raw) is bytes and len(raw) == row["byteLength"] for raw,row in zip(buffers,document["buffers"]))
        and encoded <= MAX_ENCODED,"Encoded glTF buffers exceed their declared total")
    # Includes bounded Python vectors/maps/Fractions, native mesh/loop arrays, flattened native transfer,
    # output streams, mapping JSON and retained base bytes. Process guard separately includes Blender.
    reserved = 8*1024*1024 + encoded*3 + vertices*1024 + triangles*3072 + MAX_VERTICES*64
    require(reserved <= MAX_WORK_BYTES,"Decoded/native/work/output reservation exceeds 256 MiB")
    return vertices,triangles,encoded,reserved


def preflight(document,buffers,check=lambda:None):
    vertices,triangles,encoded,reserved=descriptor_budget(document,buffers,check)
    original = document["meshes"][0]["primitives"][0]
    require(set(original["attributes"]) == {"POSITION","NORMAL","TEXCOORD_0"},
        "Original primitive must have precisely POSITION/NORMAL/TEXCOORD_0 and no existing tangent")
    require(type(original.get("material")) is int and 0 <= original["material"] < len(document["materials"])
        and "normalTexture" in document["materials"][original["material"]],"Original primitive lacks its normal-mapped material")
    positions = preview.accessor(document,buffers,original["attributes"]["POSITION"],"VEC3",(5126,),MAX_VERTICES)
    normals = preview.accessor(document,buffers,original["attributes"]["NORMAL"],"VEC3",(5126,),MAX_VERTICES)
    uvs = preview.accessor(document,buffers,original["attributes"]["TEXCOORD_0"],"VEC2",(5126,),MAX_VERTICES)
    indices = [row[0] for row in preview.accessor(document,buffers,original["indices"],"SCALAR",(5123,5125),3*MAX_TRIANGLES)]
    require(len(positions) == len(normals) == len(uvs) and len(indices)%3 == 0
        and all(0 <= v < len(positions) for v in indices),"Invalid original attribute/index correspondence")
    require(all(all(math.isfinite(v) and abs(v) <= 1e4 for v in row) for row in positions+uvs)
        and all(abs(dot(n,n)-1) <= 2e-6 for n in normals),"Source position/UV bounds or encoded unit normals are invalid")
    faces = [tuple(indices[i:i+3]) for i in range(0,len(indices),3)]
    require(len(set(indices)) == len(positions),"Unused source vertices have no defined corner tangent; refuse instead of inventing one")
    result = Primitive(positions,normals,uvs,faces,vertices,triangles,encoded,reserved)
    # Do this BEFORE Mikk, which can manufacture a default frame for collapsed UVs.
    for ordinal,face in enumerate(faces):
        if ordinal%256 == 0: check()
        derivatives(result,face,ordinal)
    return result


def triangle_basis(source,face,ordinal):
    p = [tuple(Fraction(v) for v in source.positions[i]) for i in face]
    uv = [tuple(Fraction(v) for v in source.uvs[i]) for i in face]
    e,f = sub(p[1],p[0]),sub(p[2],p[0]); a,b = sub(uv[1],uv[0]),sub(uv[2],uv[0])
    determinant = a[0]*b[1]-a[1]*b[0]
    area=cross(e,f)
    require(any(area),"Degenerate original geometry triangle "+str(ordinal))
    require(determinant != 0,"Undefined tangent: degenerate original UV triangle "+str(ordinal))
    du = tuple(float((e[i]*b[1]-f[i]*a[1])/determinant) for i in range(3))
    dv = tuple(float((f[i]*a[0]-e[i]*b[0])/determinant) for i in range(3))
    require(all(math.isfinite(v) for v in (*du,*dv)),"Unrepresentable source UV derivatives")
    for vertex in face:
        n = source.normals[vertex]
        projected = sub(du,tuple(v*dot(n,du)/dot(n,n) for v in n))
        require(dot(projected,projected) > dot(du,du)*1e-12,
            "Undefined normal-projected source tangent at triangle "+str(ordinal))
    return du,dv,determinant,area


def derivatives(source,face,ordinal):
    du,dv,_,_=triangle_basis(source,face,ordinal)
    return du,dv


def native_mikk(source,bpy,check=lambda:None):
    require(bpy.app.background,"Mikk authoring requires a dedicated background Blender process")
    probe = preview.native_float_normal_probe(bpy)
    mesh = bpy.data.meshes.new("Authenticated original forest tangent input")
    try:
        mesh.from_pydata(source.positions,[],source.triangles)
        require(len(mesh.vertices) == len(source.positions) and len(mesh.polygons) == len(source.triangles)
            and len(mesh.loops) == 3*len(source.triangles),"Blender changed source counts")
        require(all(tuple(vertex.co) == source.positions[i] for i,vertex in enumerate(mesh.vertices)),"Blender changed source positions")
        uv = mesh.uv_layers.new(name="UnchangedGlTFUV0")
        normals = mesh.attributes.new(name="custom_normal",type="FLOAT_VECTOR",domain="CORNER")
        require(normals.name == "custom_normal" and normals.domain == "CORNER" and normals.data_type == "FLOAT_VECTOR",
            "Verified float corner normals unavailable")
        for polygon in mesh.polygons:
            if polygon.index%256 == 0: check()
            require(tuple(polygon.vertices) == source.triangles[polygon.index],"Blender changed source corner/triangle order")
            polygon.use_smooth = True
            for corner in polygon.loop_indices:
                vertex = mesh.loops[corner].vertex_index
                uv.data[corner].uv = source.uvs[vertex]
                normals.data[corner].vector = source.normals[vertex]
        mesh.update(); check(); mesh.calc_tangents(uvmap=uv.name); check()
        frames = []
        for loop in mesh.loops:
            if loop.index%768 == 0: check()
            vertex = loop.vertex_index
            require(tuple(mesh.corner_normals[loop.index].vector) == source.normals[vertex]
                and tuple(uv.data[loop.index].uv) == source.uvs[vertex],
                "Native source normal/UV readback changed at loop "+str(loop.index))
            frames.append((*tuple(loop.tangent),float(loop.bitangent_sign)))
        return frames,dict(provider="Blender Mesh.calc_tangents/MikkTSpace",blenderVersion=list(bpy.app.version),
            nativeFloatCornerNormalProbe=probe,normalAndUvReadbackExact=True,axisConversion="none",uvConversion="none",
            loops=len(frames),sourceNormalsChanged=False)
    finally:
        bpy.data.meshes.remove(mesh)


def corner_hash(source,indices,positions=None,normals=None,uvs=None):
    positions = source.positions if positions is None else positions
    normals = source.normals if normals is None else normals
    uvs = source.uvs if uvs is None else uvs
    digest = hashlib.sha256()
    for triangle in indices:
        for v in triangle: digest.update(struct.pack("<8f",*positions[v],*normals[v],*uvs[v]))
    return digest.hexdigest()


def orientation_witness(source,corner_frames,ordinal,corner,check=lambda:None,maximum_incidents=32):
    """Bounded refusal evidence, not a replacement acceptance rule or native re-run.

    Include same-position corners across index/normal/UV seams. Mikk welds by
    all three attributes, so the exact-match flags distinguish its eligible
    neighbours from coincident but deliberately separate source corners.
    """
    integer(maximum_incidents,1,32,"diagnostic incident cap")
    face=source.triangles[ordinal]; vertex=face[corner]
    position,normal,uv=source.positions[vertex],source.normals[vertex],source.uvs[vertex]
    def evidence(triangle,local_corner):
        indices=source.triangles[triangle]; v=indices[local_corner]
        du,dv=derivatives(source,indices,triangle)
        n=source.normals[v]; frame=tuple(f32(x) for x in corner_frames[triangle*3+local_corner])
        p=[tuple(Fraction(x) for x in source.positions[i]) for i in indices]
        q=[tuple(Fraction(x) for x in source.uvs[i]) for i in indices]
        e,f=sub(p[1],p[0]),sub(p[2],p[0]); a,b=sub(q[1],q[0]),sub(q[2],q[0])
        determinant=a[0]*b[1]-a[1]*b[0]
        normal_area=dot(tuple(Fraction(x) for x in n),cross(e,f))
        bitangent=tuple(frame[3]*x for x in cross(n,frame[:3]))
        def cosine(left,right):
            denominator=math.sqrt(dot(left,left)*dot(right,right))
            return dot(left,right)/denominator if denominator else None
        return dict(triangle=triangle,corner=local_corner,vertex=v,indices=list(indices),
            positions=[source.positions[i] for i in indices],normal=n,uvs=[source.uvs[i] for i in indices],
            frame=frame,du=du,dv=dv,uvDeterminantExact=str(determinant),
            normalDotGeometricAreaExact=str(normal_area),tangentDotDu=dot(frame[:3],du),
            bitangentDotDv=dot(bitangent,dv),tangentDuCosine=cosine(frame[:3],du),
            bitangentDvCosine=cosine(bitangent,dv),sameSourceVertex=v==vertex,
            sameNormal=n==normal,sameUv=source.uvs[v]==uv,
            sameMikkAttributes=n==normal and source.uvs[v]==uv)
    incidents=[]; total=0
    for triangle,indices in enumerate(source.triangles):
        if triangle%256 == 0: check()
        for local_corner,v in enumerate(indices):
            if source.positions[v] != position: continue
            total+=1
            if len(incidents)<maximum_incidents: incidents.append(evidence(triangle,local_corner))
    result=dict(scope="Diagnostic only; exact UV-handedness rejection witness. Native frames are not recalculated.",
        failure=evidence(ordinal,corner),samePositionIncidentCorners=total,
        reportedIncidentCorners=len(incidents),incidentsTruncated=total>len(incidents),incidents=incidents)
    require(len(json.dumps(result,sort_keys=True))<=64*1024,"Orientation diagnostic exceeds 64 KiB cap")
    return result


def build_patch(document,buffers,source,corner_frames,*,buffer_name,allow_splits=False,maximum_output_vertices=MAX_VERTICES,check=lambda:None):
    """Pure deterministic encoder. Production supplies only verified native_mikk output.

    Different float32 Mikk frames at one original index cannot share one glTF
    TANGENT value. Retain its first authored corner's frame; append only the other
    exact frame variants, reusing that pair on subsequent corners. No tolerance
    merging, cross-source welding, averaging or original vertex reordering.
    """
    require(type(allow_splits) is bool,"Explicit split policy must be boolean")
    integer(maximum_output_vertices,1,MAX_VERTICES,"output vertex cap")
    author.uri_file(Path.cwd(),buffer_name)
    require(buffer_name not in {row["uri"] for row in document["buffers"]+document.get("images",[])},
        "Additive tangent filename collides with an existing dependency")
    require(type(corner_frames) is list and len(corner_frames) == 3*len(source.triangles),"Native Mikk corner count differs")
    count = len(source.positions)
    require(count <= maximum_output_vertices,"Original exceeds output vertex cap")
    tangents,source_vertices = [None]*count,list(range(count))
    variants = {}; indices = []; split_witnesses = []; maximum_orthogonal = 0.0; canonicalized_zero_components=0
    negative_corners=zero_corners=0
    affected_faces=[]; affected_vertices=set(); hemisphere_witnesses=[]
    all_areas=[]; affected_areas=[]; minimum_normal_cosine=1.0
    minimum_tangent_cosine=minimum_bitangent_cosine=1.0
    nonpositive_tangent=nonpositive_bitangent=0; derivative_witnesses=[]
    frame_bad=unit_bad=orthogonal_bad=runtime_unit_bad=legacy_unit_bad=legacy_orthogonal_bad=0
    maximum_unit_error=maximum_normalized_dot=maximum_source_unit_error=0.0
    worst_unit=worst_orthogonal=worst_source_normal=None; frame_witnesses=[]
    for ordinal,face in enumerate(source.triangles):
        if ordinal%256 == 0: check()
        du,dv,uv_determinant,area = triangle_basis(source,face,ordinal)
        area_length=math.sqrt(float(dot(area,area))); all_areas.append(area_length*.5)
        face_affected=False
        output = []
        for corner,vertex in enumerate(face):
            raw = corner_frames[ordinal*3+corner]
            require(type(raw) in (tuple,list) and len(raw) == 4 and all(type(v) in (int,float) and math.isfinite(v) and abs(v) <= 2 for v in raw),
                "Nonfinite/invalid native Mikk frame")
            encoded = tuple(f32(v) for v in raw)
            canonicalized_zero_components += sum(v == 0 and math.copysign(1,v) < 0 for v in encoded)
            t = tuple(0.0 if v == 0 else v for v in encoded); n = source.normals[vertex]
            tangent_squared=dot(t[:3],t[:3]); normal_squared=dot(n,n); normal_dot=dot(n,t[:3])
            normal_cross=cross(n,t[:3])
            if not (t[3] in (-1.,1.) and tangent_squared > 0 and normal_squared > 0 and dot(normal_cross,normal_cross)>0):
                witness=orientation_witness(source,corner_frames,ordinal,corner,check,maximum_incidents=8)
                witness["frameInvariant"]=dict(rawFrame=raw,encodedFrame=t,sourceNormal=n,
                    sourceNormalLengthSquared=normal_squared,rawTangentLengthSquared=dot(raw[:3],raw[:3]),
                    encodedTangentLengthSquared=tangent_squared,rawNormalDotTangent=dot(n,raw[:3]),
                    encodedNormalDotTangent=normal_dot,
                    scope="Hard nonzero/handedness gate only. Quality residuals are aggregated separately. Production native_mikk requires exact native/source N and UV readback.")
                raise TangentError("Encoded Mikk frame has zero/collinear directions or invalid handedness; witness="
                    +json.dumps(witness,sort_keys=True))
            unit_error=abs(tangent_squared-1); normalized_dot=abs(normal_dot)/math.sqrt(normal_squared*tangent_squared)
            source_unit_error=abs(normal_squared-1)
            unit_failed=unit_error>FRAME_UNIT_SQUARED_TOLERANCE
            orthogonal_failed=normalized_dot>FRAME_NORMALIZED_DOT_TOLERANCE
            frame_bad+=int(unit_failed or orthogonal_failed); unit_bad+=int(unit_failed); orthogonal_bad+=int(orthogonal_failed)
            runtime_unit_bad+=int(not .98<=tangent_squared<=1.02)
            legacy_unit_bad+=int(unit_error>2e-6); legacy_orthogonal_bad+=int(abs(normal_dot)>2e-6)
            if (worst_unit is None or unit_error>maximum_unit_error or normalized_dot>maximum_normalized_dot
                    or source_unit_error>maximum_source_unit_error or ((unit_failed or orthogonal_failed) and len(frame_witnesses)<32)):
                measured=dict(triangle=ordinal,corner=corner,vertex=vertex,rawFrame=raw,encodedFrame=t,sourceNormal=n,
                    tangentLengthSquared=tangent_squared,sourceNormalLengthSquared=normal_squared,
                    rawNormalDotTangent=dot(n,raw[:3]),encodedNormalDotTangent=normal_dot,
                    unitSquaredError=unit_error,normalizedAbsoluteNormalDot=normalized_dot)
                if worst_unit is None or unit_error>maximum_unit_error: worst_unit=measured
                if worst_orthogonal is None or normalized_dot>maximum_normalized_dot: worst_orthogonal=measured
                if worst_source_normal is None or source_unit_error>maximum_source_unit_error: worst_source_normal=measured
                if (unit_failed or orthogonal_failed) and len(frame_witnesses)<32: frame_witnesses.append(measured)
            maximum_unit_error=max(maximum_unit_error,unit_error)
            maximum_normalized_dot=max(maximum_normalized_dot,normalized_dot)
            maximum_source_unit_error=max(maximum_source_unit_error,source_unit_error)
            # Mikk's default 180-degree averaging does not promise a positive dot
            # against EACH incident derivative. Its basic w is the UV orientation,
            # independently of whether an inherited smooth normal is behind a face.
            if t[3] != (1.0 if uv_determinant > 0 else -1.0):
                witness=orientation_witness(source,corner_frames,ordinal,corner,check)
                raise TangentError("Mikk frame handedness differs from exact UV orientation at triangle "+str(ordinal)
                    +"; witness="+json.dumps(witness,sort_keys=True))
            normal_area=dot(tuple(Fraction(x) for x in n),area)
            normal_cosine=float(normal_area)/(math.sqrt(dot(n,n))*area_length)
            minimum_normal_cosine=min(minimum_normal_cosine,normal_cosine)
            if normal_area <= 0:
                negative_corners+=int(normal_area < 0); zero_corners+=int(normal_area == 0)
                face_affected=True; affected_vertices.add(vertex)
                if len(hemisphere_witnesses)<32:
                    hemisphere_witnesses.append(dict(triangle=ordinal,corner=corner,vertex=vertex,
                        indices=list(face),positions=[source.positions[i] for i in face],normal=n,
                        normalDotGeometricAreaExact=str(normal_area),normalCosineMeasured=normal_cosine))
            bitangent=tuple(t[3]*x for x in cross(n,t[:3]))
            tangent_dot=dot(t[:3],du); bitangent_dot=dot(bitangent,dv)
            tangent_cosine=tangent_dot/math.sqrt(dot(t[:3],t[:3])*dot(du,du))
            bitangent_cosine=bitangent_dot/math.sqrt(dot(bitangent,bitangent)*dot(dv,dv))
            minimum_tangent_cosine=min(minimum_tangent_cosine,tangent_cosine)
            minimum_bitangent_cosine=min(minimum_bitangent_cosine,bitangent_cosine)
            nonpositive_tangent+=int(tangent_dot<=0); nonpositive_bitangent+=int(bitangent_dot<=0)
            if (tangent_dot<=0 or bitangent_dot<=0) and len(derivative_witnesses)<8:
                derivative_witnesses.append(dict(triangle=ordinal,corner=corner,vertex=vertex,
                    tangentDuCosineMeasured=tangent_cosine,bitangentDvCosineMeasured=bitangent_cosine,
                    normalDotGeometricAreaExact=str(normal_area),frame=t))
            maximum_orthogonal = max(maximum_orthogonal,abs(dot(n,t[:3])))
            identity = (vertex,struct.pack("<4f",*t))
            if identity not in variants:
                if tangents[vertex] is None:
                    target = vertex; tangents[vertex] = t
                else:
                    if not allow_splits:
                        raise TangentError("Mikk requires an explicit vertex split: "+json.dumps(dict(vertex=vertex,triangle=ordinal,
                            corner=corner,firstFrame=tangents[vertex],otherFrame=t),sort_keys=True))
                    target = len(tangents)
                    require(target < maximum_output_vertices and source.aggregate_vertices+target-count < MAX_VERTICES,
                        "Necessary Mikk splits exceed decoder/output vertex capacity")
                    tangents.append(t); source_vertices.append(vertex)
                    if len(split_witnesses) < 8: split_witnesses.append(dict(sourceVertex=vertex,outputVertex=target,triangle=ordinal,corner=corner))
                variants[identity] = target
            output.append(variants[identity])
        indices.append(tuple(output))
        if face_affected:
            affected_faces.append(ordinal); affected_areas.append(area_length*.5)
    require(all(t is not None for t in tangents),"Undefined unreferenced tangent")
    split = len(tangents)-count
    output_positions = [source.positions[v] for v in source_vertices]
    output_normals = [source.normals[v] for v in source_vertices]
    output_uvs = [source.uvs[v] for v in source_vertices]
    original_corner_sha = corner_hash(source,source.triangles)
    require(original_corner_sha == corner_hash(source,indices,output_positions,output_normals,output_uvs),
        "Tangent patch changed per-corner source attribute bytes")
    require(packed(output_positions[:count],3) == packed(source.positions,3)
        and packed(output_normals[:count],3) == packed(source.normals,3)
        and packed(output_uvs[:count],2) == packed(source.uvs,2),"Source attribute prefix changed")
    blob = bytearray(); views=[]; accessors=[]
    replacement = deepcopy(document["meshes"][0]["primitives"][0])
    def append(values,width,kind,component=5126,target=34962):
        offset = len(blob)
        for row in values: blob.extend(struct.pack("<"+("f" if component == 5126 else "I")*width,*row))
        views.append(dict(buffer=len(document["buffers"]),byteOffset=offset,byteLength=len(blob)-offset,target=target))
        accessor = dict(bufferView=len(document["bufferViews"])+len(views)-1,componentType=component,count=len(values),type=kind)
        if kind == "VEC3" and values is output_positions:
            accessor.update(min=[min(p[i] for p in values) for i in range(3)],max=[max(p[i] for p in values) for i in range(3)])
        accessors.append(accessor)
        return len(document["accessors"])+len(accessors)-1
    if split:
        replacement["attributes"]["POSITION"] = append(output_positions,3,"VEC3")
        replacement["attributes"]["NORMAL"] = append(output_normals,3,"VEC3")
        replacement["attributes"]["TEXCOORD_0"] = append(output_uvs,2,"VEC2")
        replacement["indices"] = append([(v,) for face in indices for v in face],1,"SCALAR",5125,34963)
    replacement["attributes"]["TANGENT"] = append(tangents,4,"VEC4")
    require(source.encoded_bytes+len(blob) <= MAX_ENCODED,"Patched encoded buffers exceed decoder aggregate")
    check()
    patch = dict(schema=SCHEMA,baseDocumentCanonicalSha256=sha(author.canonical(document)),primitive=0,
        basePrimitive=deepcopy(document["meshes"][0]["primitives"][0]),replacementPrimitive=replacement,
        appendBuffers=[dict(uri=buffer_name,byteLength=len(blob))],appendBufferViews=views,appendAccessors=accessors,
        bufferSha256=sha(blob),sourceVertexForOutput=source_vertices,outputCornerIndices=[v for face in indices for v in face],
        triangleOrdinalMapping="identity; same triangle and three corner ordinals",originalVertices=count,outputVertices=len(tangents),
        splitVertices=split,originalVertexPrefixPreserved=True,originalIndexAccessorPreservedAndUsed=not bool(split),
        originalAccessorAndBufferBytesArchivedUnchanged=True,perCornerPositionNormalUvSha256=original_corner_sha,
        sourcePositionSha256=sha(packed(source.positions,3)),sourceNormalSha256=sha(packed(source.normals,3)),sourceUvSha256=sha(packed(source.uvs,2)),
        sourceIndexValuesSha256=sha(b"".join(struct.pack("<I",v) for face in source.triangles for v in face)),
        encodedTangentSha256=sha(packed(tangents,4)),splitWitnesses=split_witnesses,maximumEncodedNormalDotTangent=maximum_orthogonal,
        negativeZeroComponentsCanonicalized=canonicalized_zero_components,
        reservedDecodedWorkOutputBytes=source.reserved_bytes,allowVertexSplits=allow_splits,
        assetAccepted=False,appearanceAccepted=False,physicalGeometryChanged=False,capabilitiesChanged=False,nativeMikkVerified=False,
        validation="Exact source geometry/UV nondegeneracy and UV-orientation handedness; finite nonzero native frame. All-corner unit/orthogonality quality residuals are reported, not repaired. Native provider evidence must be attached by the root-run production wrapper. Not per-face derivative equality or per-face positive derivative dot. Source-normal quality has a separate exact hemisphere gate.")
    patch["nativeFrameQuality"]=dict(schema="native-mikk-float32-quality-v1",clear=frame_bad==0,
        appearanceAdmissionBlocked=frame_bad!=0,testedCorners=3*len(source.triangles),nativeFramesModified=False,
        maximumUnitSquaredError=maximum_unit_error,maximumNormalizedAbsoluteNormalDot=maximum_normalized_dot,
        maximumAbsoluteNormalDot=maximum_orthogonal,maximumSourceNormalUnitSquaredError=maximum_source_unit_error,
        outOfPolicyCornerCount=frame_bad,unitOutOfPolicyCorners=unit_bad,orthogonalityOutOfPolicyCorners=orthogonal_bad,
        legacy2e6UnitOutlierCorners=legacy_unit_bad,legacy2e6RawNormalDotOutlierCorners=legacy_orthogonal_bad,
        runtimeDecoderUnitRejectedCorners=runtime_unit_bad,worstUnit=worst_unit,worstOrthogonality=worst_orthogonal,
        worstSourceNormal=worst_source_normal,witnesses=frame_witnesses,witnessesTruncated=frame_bad>len(frame_witnesses),
        policy=dict(maximumUnitSquaredError=FRAME_UNIT_SQUARED_TOLERANCE,
            maximumNormalizedAbsoluteNormalDot=FRAME_NORMALIZED_DOT_TOLERANCE,
            maximumDepartureFromOrthogonalityDegreesMeasured=math.degrees(math.asin(FRAME_NORMALIZED_DOT_TOLERANCE)),
            runtimeDecoderUnitSquaredRange=[.98,1.02],runtimeDecoderHasOrthogonalityGate=False,
            meaning="Authoring quality policy, not a universal native float32 rounding bound. It is stricter than runtime unit admission. Finite nonzero vectors and exact w/UV handedness remain hard requirements; all quality failures block appearance admission without modifying the native frame."))
    patch["appearanceAdmissionBlocked"]=bool(affected_faces) or frame_bad!=0
    patch["sourceNormalHemisphere"]=dict(schema="original-source-normal-hemisphere-v1",
        clear=not affected_faces,appearanceAdmissionBlocked=bool(affected_faces),sourceNormalsModified=False,
        testedTriangles=len(source.triangles),testedCorners=3*len(source.triangles),
        negativeCornerCount=negative_corners,zeroCornerCount=zero_corners,
        affectedTriangleCount=len(affected_faces),affectedTriangleOrdinals=affected_faces,
        affectedSourceVertexCount=len(affected_vertices),affectedSourceVertexIndices=sorted(affected_vertices),
        minimumCornerCosineMeasured=minimum_normal_cosine,totalTriangleAreaMeasuredSquareMetres=math.fsum(all_areas),
        affectedTriangleAreaMeasuredSquareMetres=math.fsum(affected_areas),witnesses=hemisphere_witnesses,
        witnessCap=32,witnessesTruncated=negative_corners+zero_corners>len(hemisphere_witnesses),
        sourcePositionSha256=patch["sourcePositionSha256"],sourceNormalSha256=patch["sourceNormalSha256"],
        sourceUvSha256=patch["sourceUvSha256"],sourceIndexValuesSha256=patch["sourceIndexValuesSha256"],
        scope="All original-primitive face corners in this LOD, in unchanged triangle/corner order. Exact sign of N dot cross(B-A,C-A); strictly positive required. Cosines and areas are measured double values, not certified bounds. Each affected triangle counted once in area. Far faces may be simplified although retained source attributes are unchanged. No normal repair or appearance approval.")
    patch["incidentDerivativeDiagnostics"]=dict(nonPositiveTangentDuCorners=nonpositive_tangent,
        nonPositiveBitangentDvCorners=nonpositive_bitangent,minimumTangentDuCosineMeasured=minimum_tangent_cosine,
        minimumBitangentDvCosineMeasured=minimum_bitangent_cosine,witnesses=derivative_witnesses,
        scope="Descriptive only. Mikk's default averaging does not promise positive per-face derivative dots.")
    require(len(author.canonical(patch)) <= 4*1024*1024,"Tangent mapping JSON exceeds bounded output")
    return patch,bytes(blob)


def apply_patch_document(document,patch,blob):
    """Reusable integration API. Caller authenticates the patch receipt and ALL base dependency bytes.

    This returns JSON only; it does not copy dependencies, modify sources or make
    an asset package complete. Existing arrays are prefix-preserved byte references.
    """
    require(patch.get("schema") == SCHEMA and patch.get("primitive") == 0
        and patch["baseDocumentCanonicalSha256"] == sha(author.canonical(document))
        and document["meshes"][0]["primitives"][0] == patch["basePrimitive"]
        and sha(blob) == patch["bufferSha256"] and patch["appendBuffers"][0]["byteLength"] == len(blob),
        "Patch does not authenticate this base document/buffer")
    result = deepcopy(document)
    for field,addition in (("buffers","appendBuffers"),("bufferViews","appendBufferViews"),("accessors","appendAccessors")):
        result[field].extend(deepcopy(patch[addition]))
    result["meshes"][0]["primitives"][0] = deepcopy(patch["replacementPrimitive"])
    require(len(result["buffers"]) <= MAX_BUFFERS and len(result["bufferViews"]) <= MAX_TABLE and len(result["accessors"]) <= MAX_TABLE,
        "Patched document exceeds decoder table contract")
    return result


class Admission:
    def __init__(self,root):
        self.root=Path(root).resolve(); self.pins={}; self.bytes=0; self.started=time.monotonic()
    def check(self):
        require(time.monotonic()-self.started <= 45,"Tangent authoring exceeded its 45 second deadline")
    def pin(self,path,expected,maximum=64*1024*1024):
        self.check(); path=preview.safe_path(path,self.root); preview.digest(expected)
        if path in self.pins:
            require(self.pins[path][0] == expected,"Conflicting tangent source pin"); return path
        size=path.stat().st_size
        require(0 < size <= maximum and len(self.pins) < 64 and self.bytes+size <= MAX_ADMITTED,"Tangent authentication byte/file cap")
        require(author.sha256(path) == expected and path.stat().st_size == size,"Tangent source bytes/hash changed")
        self.bytes+=size; self.pins[path]=(expected,size); return path
    def read(self,path,maximum):
        self.check(); expected,size=self.pins[path]
        require(size <= maximum,"Tangent decoded input byte cap")
        raw=path.read_bytes(); require(len(raw) == size and sha(raw) == expected,"Tangent input changed while reading")
        return raw
    def recheck(self):
        for path,(expected,size) in self.pins.items():
            self.check(); preview.safe_path(path,self.root)
            require(path.stat().st_size == size and author.sha256(path) == expected,"Original source changed during tangent authoring")


def prepare(package,receipt_sha,root=ROOT):
    admission=Admission(root); package=preview.safe_path(package,admission.root,exists=False)
    receipt_path=admission.pin(package/"module-candidate-receipt.json",receipt_sha,4*1024*1024)
    receipt=author.strict_json(receipt_path,4*1024*1024,receipt_sha)
    require(receipt.get("status") == "unadmitted-geometry-candidate" and receipt.get("assetAccepted") is False,
        "Expected unadmitted module candidate")
    def pin_outputs(owner,rows):
        require(type(rows) is list and 1 <= len(rows) <= 48,"Bounded receipt output list required")
        result={}
        for row in rows:
            name=row["file"]; require(name not in result,"Duplicate pinned output")
            path=admission.pin(author.uri_file(owner,name),row["sha256"])
            require(path.stat().st_size == row["bytes"],"Output size differs from receipt")
            result[name]=path
        return result
    pins=pin_outputs(package,receipt["outputFiles"])
    source_package=preview.safe_path(author.contained(admission.root,receipt["sourcePackage"]),admission.root,exists=False)
    source_receipt_path=admission.pin(source_package/"qualification-receipt.json",receipt["sourceReceiptSha256"],4*1024*1024)
    original=author.strict_json(source_receipt_path,4*1024*1024,receipt["sourceReceiptSha256"])
    original_pins=pin_outputs(source_package,original["outputFiles"])
    source=original["source"]
    require(source["sha256"] == receipt["sourceSha256"],"Original source receipt identity differs")
    source_path=admission.pin(author.contained(admission.root,source["file"]),source["sha256"],4*1024*1024)
    source_document=author.strict_json(source_path,4*1024*1024,source["sha256"])
    dependencies=source["dependencies"]
    require(type(dependencies) is list and 1 <= len(dependencies) <= 16,"Original dependency count cap")
    dependency_paths={}
    for row in dependencies:
        path=admission.pin(author.contained(admission.root,row["file"]),row["sha256"])
        require(path not in dependency_paths,"Duplicate original source dependency")
        dependency_paths[path]=row["sha256"]
    declared={preview.safe_path(author.uri_file(source_path.parent,row["uri"]),admission.root)
        for row in source_document.get("buffers",[])+source_document.get("images",[])}
    require(declared == set(dependency_paths),"Original glTF dependency closure differs from receipt")
    sidecar=author.strict_json(original_pins["sidecar.json"],16*1024*1024,admission.pins[original_pins["sidecar.json"]][0])
    lods={row["key"]:row["assetPath"] for row in sidecar["definitions"][0]["lods"]}
    encoded_inputs={}; total_reserved=0; total_encoded=0
    for name in ("near","far"):
        path=pins[name+".gltf"]
        document=author.strict_json(path,4*1024*1024,admission.pins[path][0])
        original_path=original_pins[lods[name]]
        original_document=author.strict_json(original_path,4*1024*1024,admission.pins[original_path][0])
        require(document["meshes"][0]["primitives"][0] == original_document["meshes"][0]["primitives"][0]
            and all(document[field][:len(original_document[field])] == original_document[field]
                for field in ("accessors","bufferViews","buffers"))
            and document["materials"][0] == original_document["materials"][0]
            and all(document.get(field) == original_document.get(field) for field in ("images","textures","samplers")),
            "Module original primitive/attributes/PBR differ from qualified source")
        for row in original_document["buffers"]+original_document["images"]:
            filename=author.uri_file(source_package,row["uri"]).name
            require(filename in pins and admission.pins[pins[filename]][0] == admission.pins[original_pins[filename]][0],
                "Original copied dependency bytes differ")
        require(len(document["buffers"]) < MAX_BUFFERS and sum(row["byteLength"] for row in document["buffers"]) <= MAX_ENCODED,
            "Encoded dependency admission exceeds decoder contract")
        buffers=[]
        for row in document["buffers"]:
            filename=author.uri_file(package,row["uri"]).name
            require(filename in pins and pins[filename].stat().st_size == row["byteLength"],"Unpinned/mismatched original buffer dependency")
            buffers.append(admission.read(pins[filename],MAX_ENCODED))
        for row in document["images"]:
            require(author.uri_file(package,row["uri"]).name in pins,"Unpinned original PBR dependency")
        descriptor=descriptor_budget(document,buffers,admission.check)
        total_reserved+=descriptor[3]; total_encoded+=descriptor[2]
        encoded_inputs[name]=(document,buffers)
    require(total_reserved <= MAX_WORK_BYTES and total_encoded <= MAX_ENCODED,
        "Retained near+far decoded/work/output reservations exceed aggregate bound")
    inputs={name:(document,buffers,preflight(document,buffers,admission.check))
        for name,(document,buffers) in encoded_inputs.items()}
    return receipt,inputs,admission


def main(argv=None):
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package",type=Path,required=True); parser.add_argument("--receipt-sha256",required=True)
    parser.add_argument("--destination",type=Path,required=True); parser.add_argument("--allow-vertex-splits",action="store_true")
    options=parser.parse_args(argv)
    receipt,inputs,admission=prepare(options.package,preview.digest(options.receipt_sha256))
    destination=preview.safe_path(options.destination,ROOT/".artifacts",exists=False)
    require(not destination.exists() and destination.parent.is_dir(),"Tangent destination must be fresh with an existing parent")
    import bpy
    results=[]
    for name,(document,buffers,source) in inputs.items():
        frames,native=native_mikk(source,bpy,admission.check)
        patch,blob=build_patch(document,buffers,source,frames,buffer_name=name+"-original-tangents.bin",
            allow_splits=options.allow_vertex_splits,check=admission.check)
        patch.update(baseDocumentSha256=admission.pins[Path(options.package).resolve()/(name+".gltf")][0],
            native=native,nativeMikkVerified=True)
        apply_patch_document(document,patch,blob)
        results.append((name,patch,blob))
        del frames
    admission.recheck()  # No destination or output until BOTH LODs and every immutable source recheck pass.
    destination.mkdir()
    outputs=[]
    for name,patch,blob in results:
        for filename,raw in ((name+"-original-tangents.bin",blob),(name+"-tangent-patch.json",author.canonical(patch))):
            require(len(raw) <= 4*1024*1024,"Tangent output file exceeds bound")
            author.write_new(destination/filename,raw); outputs.append(dict(file=filename,bytes=len(raw),sha256=sha(raw)))
    admission.recheck()
    report=dict(schema=SCHEMA,status="unadmitted-additive-tangent-patches",assetAccepted=False,
        baseModuleReceiptSha256=options.receipt_sha256,sourceReceiptSha256=receipt["sourceReceiptSha256"],sourceSha256=receipt["sourceSha256"],
        toolSha256=author.sha256(Path(__file__)),allowVertexSplits=options.allow_vertex_splits,outputs=outputs,
        authenticatedInputs=[dict(file=path.relative_to(ROOT).as_posix(),bytes=size,sha256=pin) for path,(pin,size) in admission.pins.items()],
        encodedInputBytes=admission.bytes,reservedDecodedWorkOutputBytes=sum(row[2].reserved_bytes for row in inputs.values()),
        seconds=time.monotonic()-admission.started,appearanceAccepted=False,
        sourceNormalHemisphereClear=all(patch["sourceNormalHemisphere"]["clear"] for _,patch,_ in results),
        nativeFrameQualityClear=all(patch["nativeFrameQuality"]["clear"] for _,patch,_ in results),
        appearanceAdmissionBlocked=any(patch["appearanceAdmissionBlocked"] for _,patch,_ in results),
        nativeFrameSummary={name:{key:patch["nativeFrameQuality"][key] for key in
            ("clear","testedCorners","maximumUnitSquaredError","maximumNormalizedAbsoluteNormalDot",
             "maximumAbsoluteNormalDot","outOfPolicyCornerCount","runtimeDecoderUnitRejectedCorners")}
            for name,patch,_ in results},
        originalNormalSummary={name:{key:patch["sourceNormalHemisphere"][key] for key in
            ("clear","negativeCornerCount","zeroCornerCount","affectedTriangleCount","affectedSourceVertexCount",
             "minimumCornerCosineMeasured","totalTriangleAreaMeasuredSquareMetres","affectedTriangleAreaMeasuredSquareMetres")}
            for name,patch,_ in results},
        scope="Reusable patches only. Integrator must copy/re-pin complete original glTF dependency closure and update qualification mappings/receipt. Triangle/corner order and per-corner POSITION/NORMAL/UV bytes preserved; no physical geometry, contact, habitat, PBR-map or source asset changes.")
    author.write_new(destination/"tangent-patch-receipt.json",author.canonical(report))
    print(json.dumps(report,sort_keys=True))


if __name__ == "__main__":
    arguments=sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else sys.argv[1:]
    try: main(arguments)
    except (ValueError,KeyError,OSError) as error:
        print("INCOMPLETE/REFUSED: "+str(error),file=sys.stderr); raise SystemExit(2)

"""Bounded root-run review of the actual UNADMITTED forest contact module.

One LOD/view/light per process, fresh artifact output only. Actual exported
POSITION/NORMAL/UV/material streams are decoded and matched to the pinned module
geometry before rendering. Original PBR images are authenticated, decoded ONE AT
A TIME and reduced only in memory to 1024px. Source/exported files never change.
Closure materials remain their exported review colors; --closure-uv-checker is
an explicitly labeled diagnostic override on new faces only, never soil authoring.

--check-only needs no Blender and emits bounded authentication/correspondence
evidence. The pinned original whole-surface certificate transfers by the exact
common-union theorem, after original prefixes and every added triangle agree.
Its old lower bound DOES NOT transfer. No contact/root/normal/volume acceptance.

Root must serialize this under a <=2 GiB process / <=150s wall guard. Internal
deadline 120s, Cycles CPU 2 threads, 8 samples, 800x600. At most one 8192x8192 source
image may be decoded at once; retained review images are <=1024x1024. This is
downsampled offline review, not full-resolution or Godot appearance acceptance.
"""
from __future__ import annotations

import argparse
from dataclasses import replace
from fractions import Fraction
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import re
import struct
import sys

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
spec = importlib.util.spec_from_file_location("_forest_contact_preview_module", HERE/"forest_contact_module.py")
module = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = module
spec.loader.exec_module(module)
quality,author,require = module.quality,module.author,module.require


def integer(value, minimum, maximum, label):
    require(type(value) is int and minimum <= value <= maximum,"Invalid "+label)
    return value


def digest(value):
    require(type(value) is str and re.fullmatch("[0-9a-f]{64}",value),"Expected independently pinned SHA-256")
    return value


def safe_path(path, owner=ROOT, exists=True):
    path,owner = Path(path).absolute(),Path(owner).absolute()
    require(path.is_relative_to(owner),"Preview input/output escapes its owner")
    for current in (path,*path.parents):
        if current.exists():
            attributes = getattr(current.stat(follow_symlinks=False),"st_file_attributes",0)
            require(not current.is_symlink() and not (attributes & 0x400),"Preview paths cannot use reparse links")
        if current == owner: break
    resolved = path.resolve()
    require(resolved.is_relative_to(owner.resolve()) and (not exists or resolved.is_file()),"Missing/escaped preview input")
    return resolved


def image_dimensions(path):
    """Read <=256 KiB of standard PNG/JPEG headers before allocating an image."""
    with path.open("rb") as stream: raw = stream.read(256*1024)
    if raw[:8] == b"\x89PNG\r\n\x1a\n":
        require(len(raw) >= 33 and raw[12:16] == b"IHDR","Invalid PNG header")
        width,height = struct.unpack_from(">II",raw,16)
    else:
        require(raw[:2] == b"\xff\xd8","Preview supports only the authenticated PNG/JPEG maps")
        at,width,height = 2,None,None
        while at < len(raw):
            require(raw[at] == 255,"Invalid JPEG marker")
            while at < len(raw) and raw[at] == 255: at += 1
            require(at < len(raw),"Truncated JPEG marker")
            marker = raw[at]; at += 1
            require(marker not in (0xda,0xd9),"JPEG frame header absent from bounded prefix")
            if marker == 0x01 or 0xd0 <= marker <= 0xd8: continue
            require(at+2 <= len(raw),"Truncated JPEG segment")
            size = struct.unpack_from(">H",raw,at)[0]
            require(size >= 2 and at+size <= len(raw),"JPEG header exceeds bounded prefix")
            if marker in (0xc0,0xc1,0xc2):
                require(size >= 8 and raw[at+2] == 8,"Unsupported JPEG precision")
                height,width = struct.unpack_from(">HH",raw,at+3)
                break
            at += size
        require(width is not None,"No supported JPEG frame header")
    require(0 < width <= 8192 and 0 < height <= 8192 and width*height <= 8192**2,
            "Source image exceeds the explicit one-at-a-time 8K decode ceiling")
    return width,height


def accessor(document,buffers,index,kind,components,maximum):
    integer(index,0,len(document["accessors"])-1,"accessor index")
    item = document["accessors"][index]
    require(item.get("type") == kind and item.get("componentType") in components and
            not item.get("normalized",False) and "sparse" not in item and not item.get("extensions"),
            "Unsupported preview accessor representation")
    count = integer(item["count"],1,maximum,"accessor count")
    view = document["bufferViews"][integer(item["bufferView"],0,len(document["bufferViews"])-1,"buffer view")]
    require(not view.get("extensions"),"Extended buffer views are not supported")
    blob = buffers[integer(view["buffer"],0,len(buffers)-1,"buffer index")]
    code,width = {5126:("f",4),5125:("I",4),5123:("H",2)}[item["componentType"]]
    lanes = {"SCALAR":1,"VEC2":2,"VEC3":3,"VEC4":4}[kind]
    stride = integer(view.get("byteStride",lanes*width),lanes*width,256,"accessor stride")
    start = integer(view.get("byteOffset",0),0,len(blob),"buffer-view offset")
    size = integer(view["byteLength"],1,len(blob)-start,"buffer-view byte length")
    offset = integer(item.get("byteOffset",0),0,size,"accessor offset")
    require(offset+(count-1)*stride+lanes*width <= size,"Accessor escapes authenticated buffer view")
    values = [struct.unpack_from("<"+code*lanes,blob,start+offset+i*stride) for i in range(count)]
    require(all(math.isfinite(v) for row in values for v in row),"Nonfinite preview attribute")
    return values


def compare_native_tangent_frame(expected_t,expected_n,expected_sign,actual_t,actual_n,actual_sign):
    """Report the native conversion separately from the authenticated exported frame; never hide a mismatch."""
    values = (*expected_t,*expected_n,expected_sign,*actual_t,*actual_n,actual_sign)
    finite = all(math.isfinite(v) for v in values)
    tangent_error = math.dist(actual_t,expected_t) if finite else None
    normal_error = math.dist(actual_n,expected_n) if finite else None
    return dict(clear=finite and tangent_error <= 2e-5 and normal_error <= 2e-5 and actual_sign == expected_sign,
                finite=finite,tangentVectorError=tangent_error,normalVectorError=normal_error,
                expectedTangent=list(expected_t),expectedNormal=list(expected_n),expectedSign=expected_sign,
                actualTangent=list(actual_t),actualNormal=list(actual_n),actualSign=actual_sign,
                tangentTolerance=2e-5,normalTolerance=2e-5)


def native_normal_import_clear(compared):
    """Mikk is a comparison, not the authority for an explicitly supplied glTF tangent."""
    expected,actual = compared["expectedNormal"],compared["actualNormal"]
    return (all(math.isfinite(v) for v in (*expected,*actual)) and
            math.dist(expected,actual) <= compared["normalTolerance"])


def authored_normal_map_vector(normal,tangent,colour,scale=1):
    """CPU oracle for the explicit closure shader, in whichever proper-rotation frame N/T use.

    Sampling Blender UV (u,1-v) corrects image coordinates; it does NOT change the
    encoded glTF tangent.w or the sampled normal's green channel. Mikk computes a
    different basis from that flipped UV and is deliberately not used here.
    """
    dot,cross = module.metric_uv.dot,module.metric_uv.cross
    require(len(normal) == 3 and len(tangent) == 4 and len(colour) == 3 and
            all(math.isfinite(v) for v in (*normal,*tangent,*colour,scale)) and
            tangent[3] in (-1,1) and 0 <= scale <= 16,"Invalid authored normal-map frame/sample")
    def unit(vector):
        length = math.sqrt(dot(vector,vector)); require(length > 1e-12,"Degenerate interpolated authored frame")
        return tuple(v/length for v in vector)
    n = unit(normal)
    t = unit(tuple(tangent[i]-n[i]*dot(n,tangent[:3]) for i in range(3)))
    b = tuple(tangent[3]*v for v in cross(n,t))
    sample = ((2*colour[0]-1)*scale,(2*colour[1]-1)*scale,2*colour[2]-1)
    return unit(tuple(t[i]*sample[0]+b[i]*sample[1]+n[i]*sample[2] for i in range(3)))


def link_authored_normal_map(nodes,links,colour,scale):
    """Explicit interpolated glTF TBN; the mesh object has the validated identity transform."""
    require(type(scale) in (int,float) and math.isfinite(scale) and 0 <= scale <= 16,
            "Closure normal-map scale exceeds explicit preview bound")
    def attribute(name,output):
        node = nodes.new("ShaderNodeAttribute"); node.attribute_name = name
        return node.outputs[output]
    def vector(operation,a,b=None,scalar=None):
        node = nodes.new("ShaderNodeVectorMath"); node.operation = operation
        links.new(a,node.inputs[0])
        if b is not None: links.new(b,node.inputs[1])
        if scalar is not None: links.new(scalar,node.inputs[3])
        return node.outputs["Value" if operation == "DOT_PRODUCT" else "Vector"]
    n = vector("NORMALIZE",attribute("hollow_authored_normal","Vector"))
    raw_t = attribute("hollow_authored_tangent","Vector")
    t = vector("NORMALIZE",vector("SUBTRACT",raw_t,vector("SCALE",n,scalar=vector("DOT_PRODUCT",n,raw_t))))
    b = vector("SCALE",vector("CROSS_PRODUCT",n,t),scalar=attribute("hollow_authored_sign","Fac"))
    separate = nodes.new("ShaderNodeSeparateColor"); separate.mode = "RGB"; links.new(colour,separate.inputs[0])
    axes = []
    for channel,axis,strength in (("Red",t,scale),("Green",b,scale),("Blue",n,1)):
        decoded = nodes.new("ShaderNodeMath"); decoded.operation = "MULTIPLY_ADD"
        links.new(separate.outputs[channel],decoded.inputs[0])
        decoded.inputs[1].default_value = 2*strength; decoded.inputs[2].default_value = -strength
        axes.append(vector("SCALE",axis,scalar=decoded.outputs[0]))
    object_normal = vector("NORMALIZE",vector("ADD",vector("ADD",axes[0],axes[1]),axes[2]))
    world = nodes.new("ShaderNodeVectorTransform"); world.vector_type = "NORMAL"
    world.convert_from,world.convert_to = "OBJECT","WORLD"
    links.new(object_normal,world.inputs[0])
    return vector("NORMALIZE",world.outputs[0])


def verify_authored_shading(receipt,data,uv_evidence,normal_evidence,check=lambda:None):
    """Reproduce pinned shading from immutable closure geometry, including pre-smoothing UV packages."""
    require(receipt.get("uvSchema") == module.metric_uv.SCHEMA and data.get("uvAuthoring") == uv_evidence,
            "UV schema/embedded proof differs from its pinned sidecar")
    closure = data["closure"]
    normals = None
    if "normalSchema" in receipt:
        require(receipt["normalSchema"] == module.soil_normals.SCHEMA and normal_evidence is not None and
                data.get("normalAuthoring") == normal_evidence,"Normal schema/embedded proof differs from its pinned sidecar")
        normals = module.soil_normals.build(closure["points"],closure["triangles"],closure["groups"],normal_evidence["policy"],check)
        require(normals.evidence == normal_evidence and receipt["normalStreamSha256"] == normal_evidence["normalStreamSha256"] and
                receipt["closureGeometrySha256"] == normal_evidence["closureGeometrySha256"],"Soil normal evidence does not reproduce")
    else:
        require(normal_evidence is None and "normalAuthoring" not in data,"Unbound soil normal authoring proof")
    derived = module.metric_uv.build(closure["points"],closure["triangles"],closure["groups"],uv_evidence["policy"],check,
                                    corner_normals=None if normals is None else normals.corner_normals)
    require(derived.evidence == uv_evidence and receipt["closureGeometrySha256"] == uv_evidence["closureGeometrySha256"] and
            receipt["addedAttributeStreamSha256"] == uv_evidence["addedAttributeStreamSha256"],"Metric UV evidence does not reproduce")
    return derived


def authored_flat_normal_diagnostics(row):
    """Diagnose exact selection without treating approximate flatness as permission to replace authored normals."""
    mismatches,witnesses,maximum = 0,[],0.0
    for ordinal,triangle in enumerate(row["triangles"]):
        expected = module.metric_uv.encoded_unit(module.metric_uv.normal([row["positions"][v] for v in triangle]))
        for vertex in triangle:
            actual = tuple(row["normals"][vertex])
            if actual == expected: continue
            mismatches += 1
            error = math.dist(actual,expected); maximum = max(maximum,error)
            if len(witnesses) < 4: witnesses.append(dict(triangle=ordinal,vertex=vertex,
                authoredNormal=actual,recomputedEncodedGeometricNormal=expected,vectorError=error))
    return dict(allExactlyFlat=mismatches == 0,mismatchedCorners=mismatches,maximumVectorError=maximum,
                witnesses=witnesses,omittedWitnesses=max(0,mismatches-len(witnesses)))


def has_exact_authored_flat_normals(row):
    return authored_flat_normal_diagnostics(row)["allExactlyFlat"]


def require_native_float_normal_version(version):
    require(tuple(version) == (5,2,1),"Float corner-normal preview is qualified only for the explicit Blender 5.2.1 feature probe")


def native_float_normal_probe(bpy):
    """A three-corner capability check, before any image decode; failure cannot fall back to quantized normals."""
    require_native_float_normal_version(bpy.app.version)
    mesh = bpy.data.meshes.new("Bounded float corner-normal capability probe")
    try:
        mesh.from_pydata([(0,0,0),(1,0,0),(0,1,0)],[],[(0,1,2)])
        mesh.polygons[0].use_smooth = True
        uv = mesh.uv_layers.new(name="UV0")
        for corner,value in enumerate(((0,0),(1,0),(0,1))): uv.data[corner].uv = value
        normal = (0.,module.metric_uv.f32(.6),module.metric_uv.f32(.8))
        attribute = mesh.attributes.new(name="custom_normal",type="FLOAT_VECTOR",domain="CORNER")
        require(attribute.name == "custom_normal" and attribute.data_type == "FLOAT_VECTOR" and
                attribute.domain == "CORNER" and len(attribute.data) == 3,"Native float corner-normal attribute is unavailable")
        attribute.data.foreach_set("vector",list(normal)*3)
        mesh.update(); mesh.calc_tangents(uvmap=uv.name)
        frames = [compare_native_tangent_frame((1,0,0),normal,1,loop.tangent,
                    mesh.corner_normals[loop.index].vector,loop.bitangent_sign) for loop in mesh.loops]
        result = dict(blenderVersion=list(bpy.app.version),attributeType=attribute.data_type,domain=attribute.domain,
            measuredCorners=3,frames=frames,verified=all(frame["clear"] for frame in frames))
        require(result["verified"],"Native float corner-normal capability probe refused: "+json.dumps(result,sort_keys=True))
        return result
    finally:
        bpy.data.meshes.remove(mesh)


def validate_tangents(positions,normals,uvs,tangents,triangles):
    require(len(tangents) == len(positions),"Tangent count differs from positions")
    maximum_error = 0.0
    for triangle in triangles:
        points = [tuple(Fraction(v) for v in positions[i]) for i in triangle]
        uv = [tuple(Fraction(v) for v in uvs[i]) for i in triangle]
        e,f = module.metric_uv.sub(points[1],points[0]),module.metric_uv.sub(points[2],points[0])
        a,b = module.metric_uv.sub(uv[1],uv[0]),module.metric_uv.sub(uv[2],uv[0])
        determinant = a[0]*b[1]-a[1]*b[0]
        require(determinant != 0,"Encoded tangent has a collapsed UV triangle")
        du = tuple(float((e[i]*b[1]-f[i]*a[1])/determinant) for i in range(3))
        dv = tuple(float((f[i]*a[0]-e[i]*b[0])/determinant) for i in range(3))
        dot,cross = module.metric_uv.dot,module.metric_uv.cross
        for vertex in triangle:
            n,t = normals[vertex],tangents[vertex]
            require(len(t) == 4 and all(math.isfinite(v) for v in t) and t[3] in (-1,1) and
                    abs(dot(n,n)-1) <= 2e-6 and abs(dot(t[:3],t[:3])-1) <= 2e-6 and abs(dot(n,t[:3])) <= 2e-6,
                    "Tangent must be finite, unit, normal-orthogonal with explicit handedness")
            projected = tuple(du[i]-n[i]*(dot(n,du)/dot(n,n)) for i in range(3))
            expected = module.metric_uv.encoded_unit(projected)
            error = math.sqrt(sum((t[i]-expected[i])**2 for i in range(3)))
            require(error <= 2e-6 and dot(cross(n,t[:3]),dv)*t[3] > 0,
                    "Tangent differs from actual UV derivatives/bitangent orientation")
            maximum_error = max(maximum_error,error)
    return dict(corners=3*len(triangles),maximumDerivativeTangentError=maximum_error,
                encodedUnitOrthogonalHandednessVerified=True)


def decode_visual(document,buffers,record):
    require(document.get("asset",{}).get("version") == "2.0" and not document.get("extensionsRequired") and
            len(document["meshes"]) == 1 and len(document["nodes"]) == 1 and
            set(document["nodes"][0]) <= {"name","mesh"} and document["nodes"][0]["mesh"] == 0,
            "Preview requires one identity-transform exported mesh")
    primitives = document["meshes"][0]["primitives"]
    require(1 <= len(primitives) <= 16 and 1 <= len(document["materials"]) <= 16,"Preview primitive/material cap")
    result = []
    for row in primitives:
        require(row.get("mode",4) == 4 and not row.get("extensions") and not row.get("targets") and
                set(row["attributes"]) in ({"POSITION","NORMAL","TEXCOORD_0"},{"POSITION","NORMAL","TEXCOORD_0","TANGENT"}),
                "Unsupported visual primitive")
        positions = accessor(document,buffers,row["attributes"]["POSITION"],"VEC3",(5126,),60000)
        normals = accessor(document,buffers,row["attributes"]["NORMAL"],"VEC3",(5126,),60000)
        uv = accessor(document,buffers,row["attributes"]["TEXCOORD_0"],"VEC2",(5126,),60000)
        indices = [v[0] for v in accessor(document,buffers,row["indices"],"SCALAR",(5123,5125),150000)]
        require(len(positions) == len(normals) == len(uv) and len(indices)%3 == 0 and
                all(0 <= v < len(positions) for v in indices) and
                all(sum(v*v for v in normal) > 0 for normal in normals),"Invalid visual vertex/index streams")
        triangles = [tuple(indices[i:i+3]) for i in range(0,len(indices),3)]
        tangents = accessor(document,buffers,row["attributes"]["TANGENT"],"VEC4",(5126,),60000) if "TANGENT" in row["attributes"] else None
        tangent_evidence = validate_tangents(positions,normals,uv,tangents,triangles) if tangents is not None else None
        result.append(dict(positions=positions,normals=normals,uv=uv,triangles=triangles,
                           tangents=tangents,tangentEvidence=tangent_evidence,
                           material=integer(row["material"],0,len(document["materials"])-1,"material index")))
    expected = record["triangles"]
    def points(face): return tuple(tuple(record["vertices"][v]) for v in expected[face])
    require(len(result[0]["triangles"]) == record["originalTriangleCount"],"Original visual primitive face count changed")
    for i,triangle in enumerate(result[0]["triangles"]):
        require(tuple(result[0]["positions"][v] for v in triangle) == points(i),"Original visual triangle differs from physical correspondence record")
    mapped = set()
    for face in record["newFaces"]:
        mapping = face["visualPrimitiveTriangle"]
        require(type(mapping) is list and len(mapping) == 2,"Missing exact new-face visual mapping")
        primitive = integer(mapping[0],1,len(result)-1,"new-face primitive")
        index = integer(mapping[1],0,len(result[primitive]["triangles"])-1,"new-face visual ordinal")
        require((primitive,index) not in mapped,"Duplicate new-face visual mapping")
        mapped.add((primitive,index))
        triangle = result[primitive]["triangles"][index]
        require(tuple(result[primitive]["positions"][v] for v in triangle) == points(face["ordinal"]),
                "Added exported visual triangle differs from its exact module geometry")
    require(len(mapped) == sum(len(row["triangles"]) for row in result[1:]),"Visual primitive has an unmapped triangle")
    return result


def transfer_certificate(certificate,source_receipt,originals,data):
    require(certificate.get("complete") is True and certificate.get("withinThreshold") is True and
            certificate.get("thresholdRefuted") is False and certificate.get("receiptSha256") == source_receipt and
            certificate.get("uniformScaleExact") == "1" and certificate.get("metric") ==
            "Symmetric unsigned Euclidean distance between unions of closed triangles", "Certificate scope/identity does not match")
    expected = {"near-to-physical":len(originals["near"].triangles),"physical-to-near":len(originals["physical"].triangles),
                "near-to-far":len(originals["near"].triangles),"far-to-near":len(originals["far"].triangles)}
    directions = certificate["directions"]
    require(type(directions) is list and len(directions) == 4 and {row["name"] for row in directions} == set(expected),
            "Certificate must cover all four directions")
    bound = certificate["certifiedSymmetricUpperBoundMetres"]
    require(type(bound) in (int,float) and math.isfinite(bound) and 0 <= bound <= .15,"Unexpected surface certificate upper bound")
    for row in directions:
        require(row["complete"] is True and row["withinThreshold"] is True and row["thresholdRefuted"] is False and
                row["sourceTriangles"] == row["sourceTrianglesCompleted"] == expected[row["name"]] and
                type(row["certifiedUpperSquaredWorldExact"]) is str and len(row["certifiedUpperSquaredWorldExact"]) <= 4096,
                "Incomplete or mismatched original triangle coverage")
        squared = Fraction(row["certifiedUpperSquaredWorldExact"])
        require(0 <= squared <= Fraction(3,20)**2 and Fraction(bound)**2 >= squared,"Upper report does not enclose exact source bound")
    common = None
    for name,original in originals.items():
        record = data["meshes"][name]
        require(type(record["vertices"]) is list and 0 < len(record["vertices"]) <= 60000 and
                all(type(p) is list and len(p) == 3 and all(type(v) in (int,float) and math.isfinite(v) and abs(v) <= 1e4 for v in p)
                    for p in record["vertices"]) and type(record["triangles"]) is list and 0 < len(record["triangles"]) <= 50000 and
                all(type(t) is list and len(t) == 3 and all(type(v) is int and 0 <= v < len(record["vertices"]) for v in t)
                    for t in record["triangles"]),"Invalid bounded module geometry")
        require(record["originalVertexCount"] == len(original.positions) and record["originalTriangleCount"] == len(original.triangles)
                and [tuple(p) for p in record["vertices"][:len(original.positions)]] == original.positions and
                [tuple(t) for t in record["triangles"][:len(original.triangles)]] == original.triangles,
                "Module changed the authenticated original prefix: "+name)
        faces = [tuple(tuple(record["vertices"][v]) for v in triangle) for triangle in record["triangles"][len(original.triangles):]]
        require(len(faces) == len(data["closure"]["triangles"]) and faces,"Missing added closure triangles")
        if common is None: common = faces
        else: require(faces == common,"Near/physical/far do not add the identical ordered closure")
    require(common == [tuple(tuple(data["closure"]["points"][v]) for v in t) for t in data["closure"]["triangles"]],
            "Shared closure differs from its correspondence record")
    return dict(complete=True,scale=1,certifiedSymmetricUpperBoundMetres=bound,commonClosureTriangles=len(common),
        theorem="For the same closed triangle union C, d_H(A union C,B union C) <= d_H(A,B)",
        metric=certificate["metric"],oldMeasuredLowerBoundTransferred=False,
        scope="Unsigned surface distance upper bound only; no normal, signed-volume, collision margin, contact or habitat acceptance")


def authenticate(options):
    work = quality.Work(replace(quality.Limits(),seconds=120))
    package = safe_path(options.package,exists=False)
    require(package.is_dir() and package.is_relative_to(ROOT/".artifacts"),"Preview module must be an artifact package")
    receipt_sha = digest(options.receipt_sha256)
    receipt = author.strict_json(safe_path(package/"module-candidate-receipt.json"),1024*1024,receipt_sha)
    require(receipt.get("status") == "unadmitted-geometry-candidate" and receipt.get("assetAccepted") is False and
            receipt.get("capabilities") == [] and receipt.get("rootPermissions") is False,"Not an unadmitted module receipt")
    pins = {}
    require(type(receipt["outputFiles"]) is list and 1 <= len(receipt["outputFiles"]) <= 32,"Module receipt file cap")
    for row in receipt["outputFiles"]:
        path = safe_path(author.uri_file(package,row["file"]),package)
        require(row["file"] not in pins and type(row["bytes"]) is int and 0 < row["bytes"] <= 64*1024*1024 and
                path.stat().st_size == row["bytes"] and author.sha256(path) == digest(row["sha256"]),"Module receipt bytes/hash mismatch")
        work.take("admitted_files"); work.take("admitted_bytes",row["bytes"])
        pins[row["file"]] = row
    def document(name):
        require(name in pins,"Unpinned module JSON")
        return author.strict_json(package/name,16*1024*1024,pins[name]["sha256"])
    data,checks = document("module-candidate.json"),document("geometry-checks.json")
    require(data.get("status") == receipt["status"] and data.get("assetAccepted") is False and data.get("scale") == 1 and
            data.get("capabilities") == [] and data.get("contacts") == [] and data.get("rootVolumes") == [],"Unexpected candidate admission/capability state")
    for name in ("near","physical"):
        require(checks[name]["complete"] is True and checks[name]["geometricClosedManifold"] is True and
                checks[name]["intersections"]["clear"] is True and checks[name]["intersections"]["rejectedPairs"] == 0,
                "Candidate lacks complete closed-mesh proof")
    author.contained(ROOT,receipt["sourcePackage"])
    source_package = safe_path(ROOT/receipt["sourcePackage"],exists=False)
    near,physical,far = quality.read_package(ROOT,source_package,receipt["sourceReceiptSha256"],work)
    originals = dict(near=near,physical=physical,far=far)
    original_receipt = author.strict_json(source_package/"qualification-receipt.json",1024*1024,receipt["sourceReceiptSha256"])
    original_pins = {row["file"]:row for row in original_receipt["outputFiles"]}
    original_sidecar = author.strict_json(source_package/"sidecar.json",16*1024*1024,original_pins["sidecar.json"]["sha256"])
    original_lods = {row["key"]:row for row in original_sidecar["definitions"][0]["lods"]}
    certificate_path = safe_path(options.surface_certificate)
    certificate_sha = digest(options.certificate_sha256)
    certificate = author.strict_json(certificate_path,1024*1024,certificate_sha)
    transfer = transfer_certificate(certificate,receipt["sourceReceiptSha256"],originals,data)
    documents,decoded = {},{}
    for name in ("near","far"):
        document_gltf = document(name+".gltf")
        original_name = original_lods[name]["assetPath"]
        original_gltf = author.strict_json(source_package/original_name,4*1024*1024,original_pins[original_name]["sha256"])
        require(document_gltf["meshes"][0]["primitives"][0] == original_gltf["meshes"][0]["primitives"][0] and
                document_gltf["accessors"][:len(original_gltf["accessors"])] == original_gltf["accessors"] and
                document_gltf["bufferViews"][:len(original_gltf["bufferViews"])] == original_gltf["bufferViews"] and
                document_gltf["buffers"][:len(original_gltf["buffers"])] == original_gltf["buffers"] and
                document_gltf["materials"][0] == original_gltf["materials"][0] and
                all(document_gltf.get(field) == original_gltf.get(field) for field in ("images","textures","samplers")),
                "Original visual attributes/PBR provenance changed")
        for row in original_gltf["buffers"]+original_gltf["images"]:
            filename = author.uri_file(source_package,row["uri"]).name
            require(filename in pins and pins[filename]["sha256"] == original_pins[filename]["sha256"],
                    "Original visual/PBR dependency bytes changed")
        require(1 <= len(document_gltf["buffers"]) <= 4 and len(document_gltf.get("images",[])) == 3,"Visual dependency count cap")
        buffers = []
        for row in document_gltf["buffers"]:
            path = safe_path(author.uri_file(package,row["uri"]),package)
            require(path.name in pins and row["byteLength"] == pins[path.name]["bytes"] <= 4*1024*1024,"Unpinned/oversized visual buffer")
            raw = path.read_bytes()
            require(len(raw) == row["byteLength"] and hashlib.sha256(raw).hexdigest() == pins[path.name]["sha256"],"Visual buffer changed")
            buffers.append(raw)
        documents[name] = document_gltf
        decoded[name] = decode_visual(document_gltf,buffers,data["meshes"][name])
        work.check()
    require(documents["near"]["materials"] == documents["far"]["materials"] and
            documents["near"]["images"] == documents["far"]["images"],"Near/far material provenance differs")
    uv_evidence = normal_evidence = None
    require("normalSchema" not in receipt or "uvSchema" in receipt,"Normal proof requires the authenticated UV/frame proof")
    if "uvSchema" in receipt:
        uv_evidence = document("uv-checks.json")
        normal_evidence = document("normal-checks.json") if "normalSchema" in receipt else None
        derived = verify_authored_shading(receipt,data,uv_evidence,normal_evidence,work.check)
        for name in ("near","far"):
            for face in data["meshes"][name]["newFaces"]:
                expected = derived.faces[integer(face["closureOrdinal"],0,len(derived.faces)-1,"UV closure face")]
                primitive,ordinal = face["visualPrimitiveTriangle"]
                row = decoded[name][primitive]; triangle = row["triangles"][ordinal]
                require(face["uvChart"] == expected.chart and row["tangents"] is not None and
                        tuple(row["uv"][v] for v in triangle) == expected.uvs and
                        tuple(row["normals"][v] for v in triangle) == expected.normals and
                        tuple(row["tangents"][v] for v in triangle) == expected.tangents,
                        "Actual near/far UV/TBN stream differs from the closure proof")
    image_records = []
    for row in documents["near"]["images"]:
        path = safe_path(author.uri_file(package,row["uri"]),package)
        require(path.name in pins,"Unpinned visual image")
        width,height = image_dimensions(path)
        image_records.append(dict(file=path.name,sha256=pins[path.name]["sha256"],sourceSize=[width,height]))
    uv_rows = []
    for index,row in enumerate(decoded[options.lod][1:],1):
        attributes = [(None,None,tuple(Fraction(v) for v in uv)) for uv in row["uv"]]
        degenerate = sum(module.author.uv_area(attributes,t) == 0 for t in row["triangles"])
        uv_rows.append(dict(primitive=index,material=documents[options.lod]["materials"][row["material"]]["name"],
                            triangles=len(row["triangles"]),zeroAreaUvTriangles=degenerate,tangents=row["tangentEvidence"]))
    evidence = dict(schemaVersion=1,status="offline-preview-unadmitted",assetAccepted=False,sourceReceiptSha256=receipt["sourceReceiptSha256"],
        moduleReceiptSha256=receipt_sha,surfaceCertificateSha256=certificate_sha,moduleToolSha256=receipt["toolSha256"],
        previewToolSha256=author.sha256(Path(__file__)),lod=options.lod,view=options.view,light=options.light,
        closureUvChecker=options.closure_uv_checker,sourcePbrUnchanged=True,exportedGeometryAndAttributesUnchanged=True,
        surfaceErrorTransfer=transfer,newPrimitiveUvDiagnostics=uv_rows,sourceImages=image_records,
        geometryProof=checks,metricUvProof=uv_evidence,soilNormalProof=normal_evidence,workCounts=dict(work.counts),
        limitations=["1024px in-memory map proxies; native/full-resolution material and LOD review remains required",
                    "New surfaces still use exported semantic review colors, not soil PBR",
                    "A reference plane is not finalized terrain, burial, sealing, root clearance or a contact approval",
                    "Unsigned surface error does not establish matching normals, volume or safe collision clearance"])
    return package,data,documents[options.lod],decoded[options.lod],pins,work,evidence


def render(options,prepared):
    import bpy
    from mathutils import Vector
    package,data,document,primitives,pins,work,evidence = prepared
    require(bpy.app.background,"Use a dedicated background Blender process")
    destination = safe_path(options.destination,ROOT/".artifacts",exists=False)
    require(not destination.exists() and destination.parent.is_dir(),"Preview destination must be fresh with an existing parent")
    cv = lambda p: Vector((p[0],-p[2],p[1]))
    bpy.ops.wm.read_factory_settings(use_empty=True)
    evidence["nativeFloatCornerNormalProbe"] = native_float_normal_probe(bpy)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 8
    scene.cycles.use_denoising = False
    scene.cycles.max_bounces = 3
    scene.render.threads_mode,scene.render.threads = "FIXED",2
    scene.render.resolution_x,scene.render.resolution_y,scene.render.resolution_percentage = 800,600,100
    scene.render.image_settings.file_format = "PNG"
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    scene.world = bpy.data.worlds.new("Neutral offline review")
    scene.world.use_nodes = True
    background = scene.world.node_tree.nodes["Background"]
    background.inputs["Color"].default_value = (.18,.20,.23,1)
    background.inputs["Strength"].default_value = .6 if options.light == "neutral" else .25
    image_roles = {}
    original_material = document["materials"][0]
    for role,info in (("sRGB",original_material["pbrMetallicRoughness"]["baseColorTexture"]),
                      ("Non-Color",original_material["pbrMetallicRoughness"]["metallicRoughnessTexture"]),
                      ("Non-Color",original_material["normalTexture"])):
        index = document["textures"][info["index"]]["source"]
        require(index not in image_roles or image_roles[index] == role,"PBR image has conflicting color/data roles")
        image_roles[index] = role
    require(set(image_roles) == set(range(len(evidence["sourceImages"]))),"Unexpected unassigned PBR map")
    images = []
    for index,row in enumerate(evidence["sourceImages"]):
        work.check()
        path = package/row["file"]
        require(author.sha256(path) == row["sha256"],"PBR image changed before decode")
        image = bpy.data.images.load(str(path),check_existing=False)
        require(list(image.size) == row["sourceSize"],"Native image dimensions differ from preflight")
        image.colorspace_settings.name = image_roles[index]
        ratio = min(1,1024/max(image.size))
        image.scale(max(1,round(image.size[0]*ratio)),max(1,round(image.size[1]*ratio)))
        require(max(image.size) <= 1024,"Image proxy exceeds retained decode bound")
        images.append(image)
        row["reviewSize"] = list(image.size)
        row["colourSpace"] = image_roles[index]
        require(author.sha256(path) == row["sha256"],"PBR image changed during decode")
        work.check()
    def material(index):
        source = document["materials"][index]
        frame_modes = {row["tangents"] is not None for row in primitives if row["material"] == index}
        require(len(frame_modes) == 1,"Material has no primitive or mixes absent and explicit tangent frames")
        uses_authored_frame = index > 0 and True in frame_modes
        require(not source.get("extensions") and source.get("alphaMode","OPAQUE") == "OPAQUE","Unsupported preview material")
        result = bpy.data.materials.new(source.get("name","Material"))
        result.use_nodes = True
        result.use_backface_culling = not source.get("doubleSided",False)
        nodes,links = result.node_tree.nodes,result.node_tree.links
        shader = nodes.get("Principled BSDF")
        pbr = source["pbrMetallicRoughness"]
        shader.inputs["Base Color"].default_value = pbr.get("baseColorFactor",[1,1,1,1])
        shader.inputs["Metallic"].default_value = pbr.get("metallicFactor",1)
        shader.inputs["Roughness"].default_value = pbr.get("roughnessFactor",1)
        def texture(info,role):
            require(info.get("texCoord",0) == 0 and not info.get("extensions"),"Unsupported PBR UV transform")
            row = document["textures"][integer(info["index"],0,len(document["textures"])-1,"texture index")]
            require(not row.get("extensions"),"Extended textures are unsupported")
            sampler = document.get("samplers",[{}])[row.get("sampler",0)]
            require(sampler.get("wrapS",10497) == sampler.get("wrapT",10497) == 10497,"Expected repeat PBR sampler")
            image = images[integer(row["source"],0,len(images)-1,"image index")]
            image.colorspace_settings.name = "sRGB" if role == "base" else "Non-Color"
            node = nodes.new("ShaderNodeTexImage"); node.image = image; node.interpolation = "Linear"
            return node
        if "baseColorTexture" in pbr:
            colour = texture(pbr["baseColorTexture"],"base")
            multiply = nodes.new("ShaderNodeMixRGB"); multiply.blend_type = "MULTIPLY"; multiply.inputs[0].default_value = 1
            multiply.inputs[2].default_value = pbr.get("baseColorFactor",[1,1,1,1])
            links.new(colour.outputs["Color"],multiply.inputs[1]); links.new(multiply.outputs[0],shader.inputs["Base Color"])
        if "normalTexture" in source:
            colour = texture(source["normalTexture"],"normal").outputs["Color"]
            strength = source["normalTexture"].get("scale",1)
            if index > 0:
                require(uses_authored_frame,"Closure normal map requires an authenticated encoded tangent stream")
                links.new(link_authored_normal_map(nodes,links,colour,strength),shader.inputs["Normal"])
            else:
                normal = nodes.new("ShaderNodeNormalMap"); normal.inputs["Strength"].default_value = strength
                links.new(colour,normal.inputs["Color"]); links.new(normal.outputs["Normal"],shader.inputs["Normal"])
        elif uses_authored_frame:
            # Neutral tangent-space input exercises the same explicit frame path for review colours/checkers;
            # it produces the authored normal and invents no normal-map detail or material acceptance.
            neutral = nodes.new("ShaderNodeRGB"); neutral.outputs[0].default_value = (.5,.5,1,1)
            links.new(link_authored_normal_map(nodes,links,neutral.outputs[0],1),shader.inputs["Normal"])
        if "metallicRoughnessTexture" in pbr:
            separate = nodes.new("ShaderNodeSeparateColor"); separate.mode = "RGB"
            links.new(texture(pbr["metallicRoughnessTexture"],"packed").outputs["Color"],separate.inputs[0])
            for channel,name,factor in (("Green","Roughness","roughnessFactor"),("Blue","Metallic","metallicFactor")):
                multiply = nodes.new("ShaderNodeMath"); multiply.operation = "MULTIPLY"; multiply.inputs[1].default_value = pbr.get(factor,1)
                links.new(separate.outputs[channel],multiply.inputs[0]); links.new(multiply.outputs[0],shader.inputs[name])
            if "occlusionTexture" in source:
                require(source["occlusionTexture"]["index"] == pbr["metallicRoughnessTexture"]["index"],"Review separately mapped occlusion explicitly")
                evidence["occlusionPreview"] = "Original packed R retained; Cycles geometric shadows used. No Godot/glTF AO response parity claim."
        if index > 0 and options.closure_uv_checker:
            uv = nodes.new("ShaderNodeTexCoord"); checker = nodes.new("ShaderNodeTexChecker")
            checker.inputs["Scale"].default_value = 4
            checker.inputs["Color1"].default_value = (.9,.9,.9,1); checker.inputs["Color2"].default_value = (.03,.03,.03,1)
            links.new(uv.outputs["UV"],checker.inputs["Vector"]); links.new(checker.outputs["Color"],shader.inputs["Base Color"])
        return result
    materials = [material(i) for i in range(len(document["materials"]))]
    blender_tangent_evidence = []
    for index,row in enumerate(primitives):
        mesh = bpy.data.meshes.new("Exported primitive "+str(index))
        mesh.from_pydata([cv(p) for p in row["positions"]],[],row["triangles"])
        mesh.update()
        require(len(mesh.polygons) == len(row["triangles"]),"Blender changed primitive topology")
        uv = mesh.uv_layers.new(name="UV0")
        # Blender's legacy normals_split_custom_set_from_vertices path encoded custom normals as short2 and
        # introduced measured ~2.99e-5 errors. New closure corners use the verified FLOAT_VECTOR attribute.
        # Native normals and explicitly stored frame attributes are checked below. Recalculated Mikk tangents
        # are diagnostic only; the shader consumes authored TBN. The original scan retains its import path.
        flat_diagnostics = authored_flat_normal_diagnostics(row) if row["tangents"] is not None else None
        for polygon in mesh.polygons:
            require(tuple(polygon.vertices) == row["triangles"][polygon.index],"Blender changed triangle corner order")
            polygon.use_smooth = True
            for loop in polygon.loop_indices:
                u,v = row["uv"][mesh.loops[loop].vertex_index]; uv.data[loop].uv = (u,1-v)
        if row["tangents"] is None:
            mesh.normals_split_custom_set_from_vertices([cv(normal) for normal in row["normals"]])
        else:
            attribute = mesh.attributes.new(name="custom_normal",type="FLOAT_VECTOR",domain="CORNER")
            require(attribute.name == "custom_normal" and attribute.data_type == "FLOAT_VECTOR" and
                    attribute.domain == "CORNER" and len(attribute.data) == len(mesh.loops),
                    "Closure float corner-normal attribute differs from the verified probe")
            attribute.data.foreach_set("vector",[value for loop in mesh.loops for value in cv(row["normals"][loop.vertex_index])])
            for name,kind,field,values in (
                ("hollow_authored_normal","FLOAT_VECTOR","vector",[tuple(cv(row["normals"][loop.vertex_index])) for loop in mesh.loops]),
                ("hollow_authored_tangent","FLOAT_VECTOR","vector",[tuple(cv(row["tangents"][loop.vertex_index][:3])) for loop in mesh.loops]),
                ("hollow_authored_sign","FLOAT","value",[row["tangents"][loop.vertex_index][3] for loop in mesh.loops])):
                frame_attribute = mesh.attributes.new(name=name,type=kind,domain="CORNER")
                require(frame_attribute.name == name and frame_attribute.data_type == kind and
                        frame_attribute.domain == "CORNER" and len(frame_attribute.data) == len(mesh.loops),
                        "Authored frame attribute shape changed")
                frame_attribute.data.foreach_set(field,[v for value in values for v in value] if kind == "FLOAT_VECTOR" else values)
                for item,expected in zip(frame_attribute.data,values):
                    actual = tuple(item.vector) if kind == "FLOAT_VECTOR" else item.value
                    require(actual == expected,"Native authored TBN attribute differs from encoded glTF")
            mesh.update()
        if row["tangents"] is not None:
            mesh.calc_tangents(uvmap=uv.name)
            largest_tangent_error = largest_normal_error = 0.0
            mismatch_count,mismatches = 0,[]
            for loop in mesh.loops:
                vertex = loop.vertex_index
                expected_t = cv(row["tangents"][vertex][:3]); expected_n = cv(row["normals"][vertex])
                compared = compare_native_tangent_frame(expected_t,expected_n,-row["tangents"][vertex][3],
                    loop.tangent,mesh.corner_normals[loop.index].vector,loop.bitangent_sign)
                require(native_normal_import_clear(compared),"Actual native float3 corner normal differs: "+json.dumps(dict(
                    primitive=index,loop=loop.index,frame=compared),sort_keys=True))
                if not compared["clear"]:
                    mismatch_count += 1
                    if len(mismatches) < 8:
                        polygon = mesh.polygons[loop.index//3]
                        triangle = row["triangles"][polygon.index]
                        mismatches.append(dict(primitive=index,triangle=polygon.index,loop=loop.index,vertex=vertex,
                            frame=compared,sourcePositions=[row["positions"][v] for v in triangle],
                            sourceUvs=[row["uv"][v] for v in triangle],
                            blenderUvs=[list(uv.data[k].uv) for k in polygon.loop_indices],
                            geometricNormalBlender=list(polygon.normal)))
                if compared["finite"]:
                    largest_tangent_error = max(largest_tangent_error,compared["tangentVectorError"])
                    largest_normal_error = max(largest_normal_error,compared["normalVectorError"])
            blender_tangent_evidence.append(dict(primitive=index,loops=len(mesh.loops),nativeNormalVerified=True,
                encodedFrameAttributesVerified=True,maximumShadingNormalError=largest_normal_error,
                normalImportMode="verified-float3-corner-attribute",authoredFlatNormalDiagnostics=flat_diagnostics,
                normalMapPresent="normalTexture" in document["materials"][row["material"]],
                closureFrame="Explicit encoded glTF N/T/w; interpolated N/T normalized and orthogonalized in shader; w unchanged",
                mikkDiagnostic=dict(equalWithinOriginalTolerance=mismatch_count == 0,mismatchedLoops=mismatch_count,
                    maximumTangentVectorError=largest_tangent_error,witnesses=mismatches,
                    omittedWitnesses=max(0,mismatch_count-len(mismatches)),usedForClosureShading=False,
                    comparison="Mikk recomputed from Blender UV V -> 1-V, so comparison w is negated; no imported-tangent equality claim"),
                conversion="Proper rotation Godot XYZ to Blender X,-Z,Y; UV V -> 1-V changes image coordinates only, encoded normal-map TBN retains glTF w"))
        obj = bpy.data.objects.new(mesh.name,mesh); scene.collection.objects.link(obj)
        mesh.materials.append(materials[row["material"]])
    evidence["blenderTangentFrames"] = blender_tangent_evidence
    points = [cv(p) for p in data["meshes"][options.lod]["vertices"]]
    low,high = (Vector(tuple(fn(p[a] for p in points) for a in range(3))) for fn in (min,max))
    target = (low+high)/2
    directions = dict(front=(0,-1,.18),left=(-1,-.15,.2),right=(1,-.15,.2),top=(0,-.02,1),back=(0,1,.12),toe=(-.2,-1,.18))
    camera_data = bpy.data.cameras.new("Review camera"); camera = bpy.data.objects.new("Review camera",camera_data)
    scene.collection.objects.link(camera); scene.camera = camera
    camera_data.type = "ORTHO"; camera_data.clip_start = .01; camera_data.clip_end = 100
    if options.view == "toe":
        envelope = data["closure"]["annulus"]["guideEnvelope"]
        target = cv((0,envelope["toeYMetres"],envelope["frontZMetres"]-.4))
    camera.location = target+Vector(directions[options.view]).normalized()*35
    camera.rotation_euler = (target-camera.location).to_track_quat("-Z","Y").to_euler()
    inverse = camera.rotation_euler.to_quaternion().inverted()
    projected = [inverse@(point-target) for point in points]
    width,height = (max(p[a] for p in projected)-min(p[a] for p in projected) for a in (0,1))
    camera_data.ortho_scale = 5 if options.view == "toe" else 1.18*max(width,height*4/3)
    light_data = bpy.data.lights.new("Neutral sun" if options.light == "neutral" else "Grazing sun","SUN")
    light_data.energy = 2.5 if options.light == "neutral" else 3
    light_data.angle = math.radians(8 if options.light == "neutral" else 3)
    light = bpy.data.objects.new(light_data.name,light_data); scene.collection.objects.link(light)
    light.location = target+Vector((-8,-10,14) if options.light == "neutral" else (12,-8,1.2))
    light.rotation_euler = (target-light.location).to_track_quat("-Z","Y").to_euler()
    evidence["camera"] = dict(targetBlender=list(target),locationBlender=list(camera.location),orthographicWidthMetres=camera_data.ortho_scale)
    evidence["datumPlane"] = None
    if options.datum_plane:
        datum_y = data["closure"]["baseYMetres"]-.05
        bpy.ops.mesh.primitive_plane_add(size=50,location=(0,0,datum_y))
        bpy.context.object.name = "Reference plane 0.05m BELOW base; NOT terrain contact"
        reference = bpy.data.materials.new("Reference plane only"); reference.diffuse_color = (.12,.13,.14,1)
        bpy.context.object.data.materials.append(reference)
        evidence["datumPlane"] = dict(yMetres=datum_y,terrainFit=False,description="0.05m below authored base; no burial or seal")
    caption_data = bpy.data.curves.new("Unadmitted review caption","FONT")
    caption_data.body = f"UNADMITTED {options.lod} / {options.view} / {options.light}\n1024px PBR proxy; closure {'UV CHECKER' if options.closure_uv_checker else 'REVIEW COLORS'}; no terrain/root approval"
    caption_data.size = camera_data.ortho_scale*.016
    caption = bpy.data.objects.new(caption_data.name,caption_data); scene.collection.objects.link(caption)
    caption.parent = camera; caption.location = (-camera_data.ortho_scale*.48,-camera_data.ortho_scale*.35,-1)
    label = bpy.data.materials.new("Caption"); label.use_nodes = True
    shader = label.node_tree.nodes.get("Principled BSDF"); shader.inputs["Base Color"].default_value = (.92,.94,.96,1)
    shader.inputs["Emission Color"].default_value = (.92,.94,.96,1); shader.inputs["Emission Strength"].default_value = 1
    caption_data.materials.append(label)
    work.check()
    destination.mkdir()
    output = destination/"preview.png"
    scene.render.filepath = str(output)
    bpy.ops.render.render(write_still=True)
    work.check()
    require(output.is_file(),"Blender did not write its review PNG")
    evidence.update(renderer="Cycles CPU 2 threads / 8 samples / 800x600",pngSha256=author.sha256(output))
    author.write_new(destination/"preview-receipt.json",author.canonical(evidence))
    return evidence


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package",type=Path,required=True)
    parser.add_argument("--receipt-sha256",required=True)
    parser.add_argument("--surface-certificate",type=Path,required=True)
    parser.add_argument("--certificate-sha256",required=True)
    parser.add_argument("--lod",choices=("near","far"),default="near")
    parser.add_argument("--view",choices=("front","left","right","top","back","toe"),default="front")
    parser.add_argument("--light",choices=("neutral","grazing"),default="neutral")
    parser.add_argument("--closure-uv-checker",action="store_true")
    parser.add_argument("--datum-plane",action="store_true")
    parser.add_argument("--check-only",action="store_true")
    parser.add_argument("--destination",type=Path)
    args = parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else None)
    prepared = authenticate(args)
    if args.check_only:
        print(json.dumps(prepared[-1],sort_keys=True))
        return
    require(args.destination is not None,"Rendering needs a fresh explicit destination")
    evidence = render(args,prepared)
    print(json.dumps(dict(status=evidence["status"],lod=args.lod,view=args.view,assetAccepted=False),sort_keys=True))


if __name__ == "__main__":
    main()

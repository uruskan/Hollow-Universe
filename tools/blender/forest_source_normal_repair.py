"""Bounded, normal-only repair proposals for the authenticated forest source LODs.

Root runs this engine-free authoring command under 512 MiB/60 seconds. Original
files, positions, UVs, indices, triangle/corner order, closure attributes and
physical/contact topology are immutable. This writes fresh additive NORMAL
patches, never an admitted package. Native Mikk and appearance review follow.

For each vertex identified by the authenticated complete hemisphere report,
project its inherited normal onto ALL incident oriented-face halfspaces. The
strict convex quadratic is solved exactly by enumerating independent active
sets of at most three constraints. This is not a normal-angle search. Empty
feasibility or exhausted work refuses with witnesses; no automatic fan split,
geometry change, cross-seam welding or per-corner fallback exists.
"""
from __future__ import annotations

import argparse
from copy import deepcopy
from dataclasses import dataclass
from fractions import Fraction
import hashlib
import importlib.util
from itertools import combinations
import json
import math
from pathlib import Path
import struct
import sys

HERE=Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location("_forest_normal_repair_tangents",HERE/"forest_contact_tangents.py")
tangent=importlib.util.module_from_spec(spec); sys.modules[spec.name]=tangent; spec.loader.exec_module(tangent)
preview,author=tangent.preview,tangent.author
ROOT=HERE.parents[1]
SCHEMA="forest-source-normal-repair-v1"
DEFAULT_POLICY=dict(algorithm="exact-minimum-change-incident-halfspaces-v1",marginDivisor=256,
    maximumAffectedVerticesPerLod=64,maximumIncidentFacesPerVertex=32,maximumCandidates=65536,
    maximumConstraintTests=1000000,maximumCornerTests=600000,maximumArithmeticOperations=16000000,
    maximumRationalBits=8192,maximumExtraReservedBytes=64*1024*1024)


class RepairError(ValueError): pass
def require(value,message):
    if not value: raise RepairError(message)
def sha(raw): return hashlib.sha256(raw).hexdigest()
def packed(rows,width): return b"".join(struct.pack("<"+str(width)+"f",*row) for row in rows)
def indices_sha(triangles): return sha(b"".join(struct.pack("<I",v) for row in triangles for v in row))
def f32(value): return struct.unpack("<f",struct.pack("<f",value))[0]


def validate_policy(policy):
    require(type(policy) is dict and set(policy)==set(DEFAULT_POLICY),"Unexpected normal repair policy fields")
    require(policy["algorithm"]==DEFAULT_POLICY["algorithm"] and policy["marginDivisor"]==256,
        "Normal repair algorithm/margin is explicit and versioned")
    require(policy["maximumExtraReservedBytes"]==DEFAULT_POLICY["maximumExtraReservedBytes"],
        "Normal repair's declared extra work reservation cannot be reduced")
    for key,maximum in DEFAULT_POLICY.items():
        if key in ("algorithm","marginDivisor"): continue
        require(type(policy[key]) is int and 1<=policy[key]<=maximum,"Normal repair policy capacity: "+key)


@dataclass
class Work:
    policy: dict
    check: object=lambda:None
    candidates: int=0
    constraint_tests: int=0
    corner_tests: int=0
    arithmetic: int=0
    maximum_bits_seen: int=0
    location: str="preflight"
    def take(self,name):
        field={"candidates":"maximumCandidates","constraint_tests":"maximumConstraintTests",
            "corner_tests":"maximumCornerTests","arithmetic":"maximumArithmeticOperations"}[name]
        count=getattr(self,name)+1
        require(count<=self.policy[field],"INCOMPLETE normal repair "+name+" budget exceeded at "+self.location)
        setattr(self,name,count)
        if count%256==0: self.check()
    def value(self,value):
        value=Fraction(value)
        size=max(value.numerator.bit_length(),value.denominator.bit_length())
        self.maximum_bits_seen=max(self.maximum_bits_seen,size)
        require(size<=self.policy["maximumRationalBits"],"INCOMPLETE normal repair rational bit budget exceeded at "+self.location)
        return value
    def add(self,a,b): self.take("arithmetic"); return self.value(a+b)
    def sub(self,a,b): self.take("arithmetic"); return self.value(a-b)
    def mul(self,a,b): self.take("arithmetic"); return self.value(a*b)
    def div(self,a,b): self.take("arithmetic"); return self.value(self.value(a)/self.value(b))
    def dot(self,a,b):
        result=Fraction(0)
        for x,y in zip(a,b): result=self.add(result,self.mul(x,y))
        return result
    def vector_sub(self,a,b): return tuple(self.sub(x,y) for x,y in zip(a,b))
    def area(self,points):
        a,b,c=[tuple(self.value(v) for v in p) for p in points]
        e,f=self.vector_sub(b,a),self.vector_sub(c,a)
        result=tuple(self.sub(self.mul(e[j],f[k]),self.mul(e[k],f[j])) for j,k in ((1,2),(2,0),(0,1)))
        require(any(result),"Degenerate source normal geometry")
        return result
    def evidence(self):
        return dict(candidates=self.candidates,constraintTests=self.constraint_tests,cornerTests=self.corner_tests,
            arithmeticOperations=self.arithmetic,maximumRationalBitsSeen=self.maximum_bits_seen)


def solve(matrix,right,work):
    """Exact Gaussian elimination; dependent active rows are not candidate bases."""
    count=len(right); rows=[list(row)+[value] for row,value in zip(matrix,right)]
    for column in range(count):
        pivot=next((i for i in range(column,count) if rows[i][column]),None)
        if pivot is None: return None
        rows[column],rows[pivot]=rows[pivot],rows[column]
        divisor=rows[column][column]
        rows[column]=[work.div(v,divisor) for v in rows[column]]
        for row in range(count):
            if row==column or not rows[row][column]: continue
            factor=rows[row][column]
            rows[row]=[work.sub(v,work.mul(factor,u)) for v,u in zip(rows[row],rows[column])]
    return tuple(row[-1] for row in rows)


def project_normal(original,incident,work,vertex):
    work.location="projection vertex "+str(vertex)
    require(0<len(incident)<=work.policy["maximumIncidentFacesPerVertex"],
        "INCOMPLETE incident-face capacity at vertex "+str(vertex))
    original=tuple(work.value(v) for v in original)
    constraints=[]
    for face,area in incident:
        scale=max(abs(v) for v in area); require(scale>0,"Zero incident area")
        constraints.append(tuple(work.div(v,scale) for v in area))
    bound=Fraction(1,work.policy["marginDivisor"])
    winner=None; winner_cost=None; winner_active=None
    for size in range(min(3,len(constraints))+1):
        for active in combinations(range(len(constraints)),size):
            work.location="projection vertex "+str(vertex)+", active triangles "+str([incident[i][0] for i in active])
            work.take("candidates")
            if size:
                rows=[constraints[i] for i in active]
                gram=[[work.dot(a,b) for b in rows] for a in rows]
                right=[work.sub(bound,work.dot(row,original)) for row in rows]
                multipliers=solve(gram,right,work)
                if multipliers is None or any(v<0 for v in multipliers): continue
                candidate=list(original)
                for row,multiplier in zip(rows,multipliers):
                    candidate=[work.add(value,work.mul(multiplier,component)) for value,component in zip(candidate,row)]
                candidate=tuple(candidate)
            else: candidate=original
            feasible=True
            for row in constraints:
                work.take("constraint_tests")
                if work.dot(row,candidate)<bound: feasible=False; break
            if not feasible: continue
            delta=work.vector_sub(candidate,original); cost=work.dot(delta,delta)
            if winner_cost is None or cost<winner_cost:
                winner,winner_cost,winner_active=candidate,cost,active
    require(winner is not None,"NO FEASIBLE SHARED NORMAL: "+json.dumps(dict(vertex=vertex,
        incidentTriangles=[row[0] for row in incident],constraintScope="all incident faces; no fan/topology fallback")))
    values=tuple(float(v) for v in winner)
    length=math.sqrt(sum(v*v for v in values))
    require(math.isfinite(length) and length>0,"Exact repaired normal is not float-encodable")
    encoded=tuple(f32(v/length) for v in values)
    require(abs(sum(v*v for v in encoded)-1)<=2e-6,"Encoded repaired normal violates unit policy")
    exact=tuple(work.value(v) for v in encoded)
    minimum_cosine=1.
    for face,area in incident:
        facing=work.dot(exact,area)
        require(facing>0,"Encoded repair is tangent/inward at vertex "+str(vertex)+", face "+str(face))
        minimum_cosine=min(minimum_cosine,float(facing)/math.sqrt(float(work.mul(work.dot(exact,exact),work.dot(area,area)))))
    old_length=math.sqrt(sum(float(v)*float(v) for v in original))
    cosine=sum(float(a)*b for a,b in zip(original,encoded))/(old_length*math.sqrt(sum(v*v for v in encoded)))
    return encoded,dict(vertex=vertex,oldNormal=[float(v) for v in original],newNormal=list(encoded),
        incidentTriangles=[row[0] for row in incident],activeTriangles=[incident[i][0] for i in winner_active],
        exactRawSquaredChange=str(winner_cost),angularChangeDegreesMeasured=math.degrees(math.acos(max(-1.,min(1.,cosine)))),
        minimumIncidentCosineMeasured=minimum_cosine,encodedOutwardHemisphereExact=True)


def source_hashes(source):
    return dict(sourcePositionSha256=sha(packed(source.positions,3)),sourceNormalSha256=sha(packed(source.normals,3)),
        sourceUvSha256=sha(packed(source.uvs,2)),sourceIndexValuesSha256=indices_sha(source.triangles))


def repair(source,diagnostic,work):
    """No global mesh rebuild: only identified indexed vertex normals may change."""
    work.check(); report=diagnostic["sourceNormalHemisphere"]
    require(report.get("schema")=="original-source-normal-hemisphere-v1" and
        report.get("sourceNormalsModified") is False and report["testedTriangles"]==len(source.triangles) and
        report["testedCorners"]==3*len(source.triangles),"Diagnostic does not cover the complete source primitive")
    hashes=source_hashes(source)
    require(all(report.get(key)==value for key,value in hashes.items()),"Diagnostic source stream identity differs")
    affected=report["affectedSourceVertexIndices"]
    require(type(affected) is list and len(affected)<=work.policy["maximumAffectedVerticesPerLod"] and
        all(type(v) is int and 0<=v<len(source.positions) for v in affected) and affected==sorted(set(affected)),
        "Affected vertex selection/capacity invalid")
    incident={v:[] for v in affected}; touched_areas={}; bad_faces=set(); bad_vertices=set(); negative=zero=0
    exact_normals=[tuple(work.value(v) for v in n) for n in source.normals]
    for face,triangle in enumerate(source.triangles):
        work.location="complete source hemisphere triangle "+str(face)
        if face%128==0: work.check()
        area=work.area([source.positions[v] for v in triangle])
        for vertex in triangle:
            work.take("corner_tests"); facing=work.dot(exact_normals[vertex],area)
            if facing<=0:
                bad_faces.add(face); bad_vertices.add(vertex)
                if facing<0: negative+=1
                else: zero+=1
            if vertex in incident:
                require(len(incident[vertex])<work.policy["maximumIncidentFacesPerVertex"],
                    "INCOMPLETE incident-face capacity at vertex "+str(vertex)+", triangle "+str(face))
                incident[vertex].append((face,area)); touched_areas[face]=area
    require(sorted(bad_vertices)==affected and sorted(bad_faces)==report["affectedTriangleOrdinals"] and
        negative==report["negativeCornerCount"] and zero==report["zeroCornerCount"] and
        len(affected)==report["affectedSourceVertexCount"] and len(bad_faces)==report["affectedTriangleCount"],
        "Complete recomputed hemisphere defects differ from authenticated selection")
    normals=list(source.normals); changes=[]
    for vertex in affected:
        work.check(); normals[vertex],change=project_normal(source.normals[vertex],incident[vertex],work,vertex); changes.append(change)
    # All untouched corners were certified above; certify all changed incident
    # corners below. Their union is the entire output, not sparse sampling.
    for vertex in affected:
        normal=tuple(work.value(v) for v in normals[vertex])
        for face,area in incident[vertex]:
            work.take("corner_tests")
            require(work.dot(normal,area)>0,"Repaired incident hemisphere remains invalid")
    unchanged=[v for v in range(len(normals)) if v not in incident]
    require(packed((normals[v] for v in unchanged),3)==packed((source.normals[v] for v in unchanged),3),
        "Unselected source normal changed")
    areas=[.5*math.sqrt(float(work.dot(area,area))) for area in touched_areas.values()]
    return normals,dict(complete=True,allOutputCornersOutwardExact=True,testedOutputCorners=3*len(source.triangles),
        changedVertices=len(changes),changes=changes,touchedTriangleOrdinals=sorted(touched_areas),
        touchedTriangleCount=len(touched_areas),touchedTriangleAreaMeasuredSquareMetres=math.fsum(areas),
        originalDefectiveTriangles=len(bad_faces),originalNegativeCorners=negative,originalZeroCorners=zero,
        minimumAngularChangeDegreesMeasured=min((row["angularChangeDegreesMeasured"] for row in changes),default=0.),
        maximumAngularChangeDegreesMeasured=max((row["angularChangeDegreesMeasured"] for row in changes),default=0.),
        minimumRepairedIncidentCosineMeasured=min((row["minimumIncidentCosineMeasured"] for row in changes),default=1.),
        unchangedNormalsByteExact=True,sourceHashes=hashes,outputNormalSha256=sha(packed(normals,3)),
        method="Exact strictly convex projection onto all incident halfspaces; independent active sets size0..3; final encoded outward signs exact",
        marginMeaning="area/maxAbs(area) dot raw vector >=1/256 before unit encoding; explicit authoring margin, not a minimum final angle or visual acceptance")


def build_normal_patch(document,buffers,source,diagnostic,policy=None,*,buffer_name="normal-repair.bin",work=None):
    policy=deepcopy(DEFAULT_POLICY) if policy is None else policy; validate_policy(policy)
    work=work or Work(policy); require(work.policy==policy,"One repair work ledger must use the same policy")
    require(len(document["buffers"])+1<=8 and len(document["bufferViews"])+1<=128 and len(document["accessors"])+1<=128,
        "Normal patch descriptor capacity")
    require(sum(len(raw) for raw in buffers)+12*len(source.positions)<=32*1024*1024,"Normal patch encoded capacity")
    require(buffer_name==Path(buffer_name).name and buffer_name.endswith(".bin") and not any(c in buffer_name for c in ":/\\"),
        "Normal patch buffer must have a leaf name")
    normals,evidence=repair(source,diagnostic,work)
    blob=packed(normals,3); original=document["meshes"][0]["primitives"][0]; replacement=deepcopy(original)
    replacement["attributes"]["NORMAL"]=len(document["accessors"])
    patch=dict(schema=SCHEMA,baseDocumentCanonicalSha256=sha(author.canonical(document)),primitive=0,
        basePrimitive=deepcopy(original),replacementPrimitive=replacement,
        appendBuffers=[dict(uri=buffer_name,byteLength=len(blob))],
        appendBufferViews=[dict(buffer=len(document["buffers"]),byteOffset=0,byteLength=len(blob),target=34962)],
        appendAccessors=[dict(bufferView=len(document["bufferViews"]),componentType=5126,count=len(normals),type="VEC3")],
        bufferSha256=sha(blob),sourceHashes=source_hashes(source),outputNormalSha256=sha(blob),
        vertexCount=len(normals),triangleCount=len(source.triangles),triangleOrdinalMapping="identity",
        originalVertexPrefixPreserved=True,positionUvIndexBytesPreserved=True,closureAttributesPreserved=True,
        policy=policy,evidence=evidence,assetAccepted=False,appearanceAccepted=False,physicalGeometryChanged=False,
        contactsChanged=False,capabilitiesChanged=False,nativeMikkRegenerationRequired=True,
        nativeFramesValid=False,sourceNormalsModified=bool(evidence["changedVertices"]))
    require(len(author.canonical(patch))<=1024*1024,"Normal patch metadata cap")
    apply_repaired_document(document,buffers,patch,blob)
    return patch,blob


def apply_repaired_document(document,buffers,patch,blob):
    """Reusable authenticated stream patch. Caller authenticates receipt and complete base dependencies.

    Returns copied document/buffer-list containers; payload bytes are immutable
    and borrowed. Apply descriptor compaction AFTER this, then generate Mikk on
    that exact resulting document. This API grants no asset/material permission.
    """
    require(patch.get("schema")==SCHEMA and patch.get("primitive")==0 and
        patch.get("baseDocumentCanonicalSha256")==sha(author.canonical(document)) and
        patch["basePrimitive"]==document["meshes"][0]["primitives"][0] and sha(blob)==patch["bufferSha256"],
        "Normal patch does not authenticate base JSON/payload")
    require(type(blob) is bytes and type(buffers) is list and len(document["buffers"])==len(buffers) and
        all(type(raw) is bytes and len(raw)==row["byteLength"] for row,raw in zip(document["buffers"],buffers)) and
        sum(len(raw) for raw in buffers)+len(blob)<=32*1024*1024,"Normal patch buffer capacity/length mismatch")
    original=patch["basePrimitive"]; replacement=deepcopy(original)
    require(set(original["attributes"])=={"POSITION","NORMAL","TEXCOORD_0"},
        "Normal repair must precede Mikk; stale tangent streams are not preserved")
    replacement["attributes"]["NORMAL"]=len(document["accessors"])
    require(patch["replacementPrimitive"]==replacement and len(patch["appendBuffers"])==1 and
        patch["appendBuffers"][0]["byteLength"]==len(blob) and patch["appendBufferViews"]==[
            dict(buffer=len(buffers),byteOffset=0,byteLength=len(blob),target=34962)] and
        patch["appendAccessors"]==[dict(bufferView=len(document["bufferViews"]),componentType=5126,
            count=patch["vertexCount"],type="VEC3")],"Normal patch may only append the exact NORMAL accessor")
    count=patch["vertexCount"]
    buffer_name=patch["appendBuffers"][0]["uri"]
    require(type(buffer_name) is str and buffer_name==Path(buffer_name).name and buffer_name.endswith(".bin") and
        not any(c in buffer_name for c in ":/\\"),"Normal patch buffer must have a leaf name")
    require(type(count) is int and 0<count<=60000 and len(blob)==count*12,"Normal patch vertex/output capacity")
    require(len(document["buffers"])+1<=8 and len(document["bufferViews"])+1<=128 and
        len(document["accessors"])+1<=128,"Normal patch descriptor capacity")
    attrs=original["attributes"]
    rows={key:preview.accessor(document,buffers,attrs[key],kind,(5126,),60000) for key,kind in
        (("POSITION","VEC3"),("NORMAL","VEC3"),("TEXCOORD_0","VEC2"))}
    indices=[v[0] for v in preview.accessor(document,buffers,original["indices"],"SCALAR",(5123,5125),150000)]
    require(all(len(values)==count for values in rows.values()) and len(indices)==patch["triangleCount"]*3,
        "Normal patch source counts differ")
    actual=dict(sourcePositionSha256=sha(packed(rows["POSITION"],3)),sourceNormalSha256=sha(packed(rows["NORMAL"],3)),
        sourceUvSha256=sha(packed(rows["TEXCOORD_0"],2)),sourceIndexValuesSha256=sha(b"".join(struct.pack("<I",v) for v in indices)))
    require(actual==patch["sourceHashes"] and sha(blob)==patch["outputNormalSha256"],"Normal patch source/output stream hashes differ")
    values=list(struct.iter_unpack("<3f",blob))
    require(all(all(math.isfinite(v) for v in n) and abs(sum(v*v for v in n)-1)<=2e-6 for n in values),
        "Normal patch output is nonfinite/nonunit")
    result=deepcopy(document)
    for field,addition in (("buffers","appendBuffers"),("bufferViews","appendBufferViews"),("accessors","appendAccessors")):
        result[field].extend(deepcopy(patch[addition]))
    result["meshes"][0]["primitives"][0]=replacement
    return result,[*buffers,blob]


def authenticate_inputs(package,receipt_sha,diagnostic_package,diagnostic_sha,recipe_path,root=ROOT):
    receipt,inputs,admission=tangent.prepare(package,receipt_sha,root)
    recipe_path=preview.safe_path(recipe_path,admission.root)
    admission.pin(recipe_path,author.sha256(recipe_path),65536)
    recipe=author.strict_json(recipe_path,65536,admission.pins[recipe_path][0])
    require(recipe.get("schema")==SCHEMA and recipe.get("status")=="proposed-unadmitted" and
        recipe.get("baseModuleReceiptSha256")==receipt_sha and recipe.get("diagnosticReceiptSha256")==diagnostic_sha,
        "Normal recipe targets another authenticated source/diagnostic chain")
    validate_policy(recipe["policy"])
    reserved=sum(row[2].reserved_bytes for row in inputs.values())+recipe["policy"]["maximumExtraReservedBytes"]
    require(reserved<=384*1024*1024,"Normal authoring aggregate reservation exceeded")
    diagnostic_package=preview.safe_path(diagnostic_package,admission.root,exists=False)
    path=admission.pin(diagnostic_package/"tangent-patch-receipt.json",diagnostic_sha,1024*1024)
    diagnostic_receipt=author.strict_json(path,1024*1024,diagnostic_sha)
    require(diagnostic_receipt.get("schema")==tangent.SCHEMA and diagnostic_receipt.get("assetAccepted") is False and
        diagnostic_receipt.get("baseModuleReceiptSha256")==receipt_sha,"Normal diagnostic receipt/source mismatch")
    expected={name+suffix for name in ("near","far") for suffix in ("-tangent-patch.json","-original-tangents.bin")}
    outputs=diagnostic_receipt["outputs"]
    require(type(outputs) is list and len(outputs)==4 and {row["file"] for row in outputs}==expected,"Normal diagnostic dependency closure differs")
    for row in outputs:
        path=admission.pin(author.uri_file(diagnostic_package,row["file"]),row["sha256"],4*1024*1024)
        require(path.stat().st_size==row["bytes"],"Normal diagnostic output size differs")
        reserved+=row["bytes"]*(12 if row["file"].endswith(".json") else 2)
    require(reserved<=384*1024*1024,"Normal diagnostic decode reservation exceeded")
    diagnostics={}
    for name in ("near","far"):
        path=diagnostic_package/(name+"-tangent-patch.json")
        diagnostic=author.strict_json(path,4*1024*1024,admission.pins[path][0])
        blob=admission.read(diagnostic_package/(name+"-original-tangents.bin"),4*1024*1024)
        require(diagnostic.get("nativeMikkVerified") is True and diagnostic.get("baseDocumentSha256")==
            admission.pins[Path(package).resolve()/(name+".gltf")][0],"Normal diagnostic base/native evidence differs")
        tangent.apply_patch_document(inputs[name][0],diagnostic,blob)
        diagnostics[name]=diagnostic
    admission.recheck()
    return receipt,inputs,diagnostics,recipe,admission,reserved


def main(argv=None):
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package",type=Path,required=True); parser.add_argument("--receipt-sha256",required=True)
    parser.add_argument("--diagnostic-package",type=Path,required=True); parser.add_argument("--diagnostic-receipt-sha256",required=True)
    parser.add_argument("--recipe",type=Path,default=HERE/"data"/"forest_source_normal_repair.json")
    parser.add_argument("--destination",type=Path,required=True)
    options=parser.parse_args(argv)
    receipt,inputs,diagnostics,recipe,admission,reserved=authenticate_inputs(options.package,preview.digest(options.receipt_sha256),
        options.diagnostic_package,preview.digest(options.diagnostic_receipt_sha256),options.recipe)
    destination=preview.safe_path(options.destination,ROOT/".artifacts",exists=False)
    require(not destination.exists() and destination.parent.is_dir(),"Normal repair destination must be fresh")
    work=Work(recipe["policy"],admission.check); results=[]
    for name,(document,buffers,source) in inputs.items():
        try:
            patch,blob=build_normal_patch(document,buffers,source,diagnostics[name],recipe["policy"],
                buffer_name=name+"-repaired-normals.bin",work=work)
        except RepairError as error: raise RepairError(name+": "+str(error)) from error
        patch["baseDocumentSha256"]=admission.pins[Path(options.package).resolve()/(name+".gltf")][0]
        results.append((name,patch,blob)); admission.check()
    admission.recheck(); destination.mkdir(); outputs=[]
    for name,patch,blob in results:
        for filename,raw in ((name+"-repaired-normals.bin",blob),(name+"-normal-repair.json",author.canonical(patch))):
            require(len(raw)<=1024*1024,"Normal repair output file cap")
            author.write_new(destination/filename,raw); outputs.append(dict(file=filename,bytes=len(raw),sha256=sha(raw)))
    report=dict(schema=SCHEMA,status="unadmitted-additive-normal-repair",assetAccepted=False,appearanceAccepted=False,
        physicalGeometryChanged=False,contactsChanged=False,capabilitiesChanged=False,nativeMikkRegenerationRequired=True,
        baseModuleReceiptSha256=options.receipt_sha256,diagnosticReceiptSha256=options.diagnostic_receipt_sha256,
        recipeSha256=admission.pins[Path(options.recipe).resolve()][0],recipe=recipe,outputs=outputs,
        toolSha256=author.sha256(Path(__file__)),work=work.evidence(),
        reservedDecodedWorkOutputBytes=reserved,
        lods={name:patch["evidence"] for name,patch,_ in results},
        inputPins=[dict(file=p.relative_to(ROOT).as_posix(),bytes=size,sha256=pin) for p,(pin,size) in admission.pins.items()],
        integration="Authenticate base, apply NORMAL patch, compact descriptors if desired, regenerate native Mikk on that exact repaired document, then rebuild visual qualification and appearance stream identities",
        pending=["Native Mikk regeneration and complete frame/hemisphere report","Before/after source-normal and grazing PBR visual review",
            "Near/far material continuity and actual sealed-terrain contact views"])
    admission.recheck(); raw=author.canonical(report); require(len(raw)<=1024*1024,"Normal repair receipt cap")
    author.write_new(destination/"normal-repair-receipt.json",raw)
    print(json.dumps(dict(status=report["status"],assetAccepted=False,outputs=outputs,work=report["work"],
        lods={name:{key:patch["evidence"][key] for key in ("changedVertices","touchedTriangleCount",
            "touchedTriangleAreaMeasuredSquareMetres","maximumAngularChangeDegreesMeasured")} for name,patch,_ in results}),sort_keys=True))


if __name__=="__main__":
    try: main()
    except (ValueError,KeyError,OSError) as error:
        print("INCOMPLETE/REFUSED: "+str(error),file=sys.stderr); raise SystemExit(2)

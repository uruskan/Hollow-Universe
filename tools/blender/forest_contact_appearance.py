"""Authenticated additive soil-appearance masks; no geometry/material admission.

Root-run only under a 512 MiB/60 second guard (45 seconds internal, no Blender,
images or GPU). Original scan/closure P/N/UV/T and indices are never changed.
Four little-endian float32 values per visual vertex are R scan->substrate,
G outer-contact appearance blend, B cap domain, A toe domain. Return rock is
1-B-A. Original UV0 may only sample the original primitive's existing atlas.
Soil/gravel phase must come from the shared drawn-terrain appearance contract.
Runtime transport is CUSTOM0 RGBA_FLOAT, never a quantized vertex colour or
vertex-ID lookup. Ordered per-primitive streams are hashed before publication.

Distances are bounded shortest EDGE-PATH distances on the existing geometry,
not exact surface geodesics. The masks are piecewise linear on each LOD; shared
vertices/rims/closure match exactly, but simplified interior interpolation can
differ. This tool measures no texture/lighting error and approves no habitat.
"""
from __future__ import annotations

import argparse
from dataclasses import dataclass
import hashlib
import heapq
import importlib.util
import json
import math
from pathlib import Path
import struct
import sys

HERE=Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location("_forest_appearance_tangents",HERE/"forest_contact_tangents.py")
tangent=importlib.util.module_from_spec(spec); sys.modules[spec.name]=tangent; spec.loader.exec_module(tangent)
preview,author=tangent.preview,tangent.author
ROOT=HERE.parents[1]
SCHEMA="forest-contact-additive-appearance-v1"
LOD_SCHEMA="forest-contact-additive-appearance-lod-v1"
MAX_VERTICES,MAX_TRIANGLES,MAX_EDGES,MAX_PUSHES,MAX_VISITS=60000,50000,150000,600000,1200000
MAX_HEAP=131072
ROLES=("left","toe","right","crest")
ROLE_DOMAIN={"left":(0.,0.),"right":(0.,0.),"crest":(1.,0.),"toe":(0.,1.)}


def require(condition,message):
    if not condition: raise ValueError(message)


def sha(raw): return hashlib.sha256(raw).hexdigest()
def stream_sha(rows,format):
    digest=hashlib.sha256()
    for row in rows: digest.update(struct.pack(format,*row))
    return digest.hexdigest()
def f32(value): return struct.unpack("<f",struct.pack("<f",value))[0]
def smooth(value):
    value=max(0.,min(1.,value)); return value*value*(3-2*value)
def edge(a,b): return (a,b) if a<b else (b,a)


@dataclass
class Work:
    check: object=lambda:None
    maximum_edges: int=MAX_EDGES
    maximum_pushes: int=MAX_PUSHES
    maximum_visits: int=MAX_VISITS
    maximum_heap: int=MAX_HEAP
    edges: int=0
    pushes: int=0
    visits: int=0
    def take(self,name):
        value=getattr(self,name)+1
        require(value<=getattr(self,"maximum_"+name),"Appearance "+name+" capacity exceeded")
        setattr(self,name,value)
        if value%256==0: self.check()


def validate_recipe(recipe):
    require(recipe.get("schema")==SCHEMA and recipe.get("status")=="proposed-unadmitted" and recipe.get("scale")==1,
        "Expected inactive scale1 appearance recipe")
    require(recipe.get("distanceRule")=="shortest-existing-edge-path-v1","Unknown distance rule")
    widths=recipe["sourceBandMetres"]
    require(set(widths)==set(ROLES),"All four authored arc widths are required")
    for value in [*widths.values(),recipe["anchorTransitionMetres"],recipe["outerContactBandMetres"]]:
        require(type(value) in (int,float) and math.isfinite(value) and 0<value<=2,"Invalid explicit metre band")
    require(recipe.get("channels")==["scanToSubstrate","outerContact","capDomain","toeDomain"],"Unexpected mask ABI")
    require(set(recipe["materials"])=={"cap","toe","return"},"Material domains differ")
    for mix in recipe["materials"].values():
        require(type(mix) is dict and mix and set(mix)<={"1","2","3","4"} and
            all(type(v) in (int,float) and math.isfinite(v) and 0<=v<=1 for v in mix.values()) and
            abs(sum(mix.values())-1)<=1e-12,"Only explicit normalized existing substrate layers are allowed")
    require(recipe.get("rootPermissions") is False and recipe.get("geometryChanges") is False,
        "Appearance recipe cannot grant roots or alter geometry")


def geometry(positions,triangles,label):
    require(type(positions) is list and 0<len(positions)<=MAX_VERTICES and type(triangles) is list
        and 0<len(triangles)<=MAX_TRIANGLES,"Bounded "+label+" geometry required")
    for p in positions:
        require(type(p) in (list,tuple) and len(p)==3 and all(type(v) in (float,int) and math.isfinite(v)
            and abs(v)<=1e4 and f32(v)==v for v in p),"Invalid represented float32 position")
    for t in triangles:
        require(type(t) in (list,tuple) and len(t)==3 and len(set(t))==3 and
            all(type(v) is int and 0<=v<len(positions) for v in t),"Invalid bounded triangle")


def graph(positions,triangles,work):
    geometry(positions,triangles,"graph")
    adjacency=[{} for _ in positions]; incidence={}
    for ordinal,face in enumerate(triangles):
        if ordinal%256==0: work.check()
        for a,b in zip(face,(*face[1:],face[0])):
            key=edge(a,b); incidence[key]=incidence.get(key,0)+1
            if b in adjacency[a]: continue
            work.take("edges")
            length=math.dist(positions[a],positions[b]); require(length>0 and math.isfinite(length),"Zero graph edge")
            adjacency[a][b]=length; adjacency[b][a]=length
    return [tuple(sorted(row.items())) for row in adjacency],incidence


def distances(adjacency,seeds,work,maximum=math.inf):
    require(seeds and len(seeds)<=len(adjacency),"Missing/oversized distance seeds")
    best=[(math.inf,-1)]*len(adjacency); pending=[]
    for vertex,label in sorted(seeds.items()):
        require(type(vertex) is int and 0<=vertex<len(adjacency) and type(label) is int and label>=0,"Invalid distance seed")
        require(len(pending)<work.maximum_heap,"Appearance pending heap capacity exceeded")
        work.take("pushes"); best[vertex]=(0.,label); heapq.heappush(pending,(0.,label,vertex))
    while pending:
        distance,label,vertex=heapq.heappop(pending)
        if best[vertex]!=(distance,label): continue
        for other,length in adjacency[vertex]:
            work.take("visits"); candidate=(distance+length,label)
            if candidate[0]>maximum or candidate>=best[other]: continue
            require(len(pending)<work.maximum_heap,"Appearance pending heap capacity exceeded")
            work.take("pushes"); best[other]=candidate; heapq.heappush(pending,(*candidate,other))
    return best


def arc_values(points,arcs,key,recipe,expected_loop):
    """Continuous endpoint values, authored in arc length without role-by-height inference."""
    require(len(arcs)==4 and {a.get("role") for a in arcs}==set(ROLES),"Four unique authored arcs required")
    memberships={}; covered=[]
    for arc in arcs:
        ids=arc[key]
        require(type(ids) is list and 2<=len(ids)<=2048 and len(set(ids))==len(ids) and
            all(type(v) is int and 0<=v<len(points) for v in ids),"Invalid authored arc indices")
        covered.extend(zip(ids,ids[1:]))
        for vertex in ids: memberships.setdefault(vertex,[]).append(arc["role"])
    expected=list(zip(expected_loop,(*expected_loop[1:],expected_loop[0])))
    require(len(covered)==len(expected) and set(covered)==set(expected),"Arc edges do not partition the actual ordered loop")
    require(all(1<=len(roles)<=2 for roles in memberships.values()),"Ambiguous arc junction")
    values={}
    for arc in arcs:
        ids=arc[key]; role=arc["role"]; base=(*ROLE_DOMAIN[role],recipe["sourceBandMetres"][role])
        lengths=[0.]
        for a,b in zip(ids,ids[1:]): lengths.append(lengths[-1]+math.dist(points[a],points[b]))
        band=recipe["anchorTransitionMetres"]
        require(lengths[-1]>=2*band,"Authored anchor transition bands overlap")
        def endpoint(v):
            roles=memberships[v]; require(len(roles)==2,"Open authored arc endpoint")
            rows=[(*ROLE_DOMAIN[r],recipe["sourceBandMetres"][r]) for r in sorted(roles)]
            return tuple((a+b)*.5 for a,b in zip(*rows))
        first,last=endpoint(ids[0]),endpoint(ids[-1])
        for vertex,distance in zip(ids,lengths):
            if distance<band:
                factor=smooth(distance/band); value=tuple(a+(b-a)*factor for a,b in zip(first,base))
            elif lengths[-1]-distance<band:
                factor=smooth((lengths[-1]-distance)/band); value=tuple(a+(b-a)*factor for a,b in zip(last,base))
            else: value=base
            if vertex in values: require(values[vertex]==value,"Anchor values differ across arcs")
            values[vertex]=value
    return values


def build_fields(data,recipe,work=None):
    validate_recipe(recipe); work=work or Work(); work.check()
    require(data.get("scale")==1 and data.get("assetAccepted") is False,"Expected unadmitted scale1 module")
    closure=data["closure"]; points=closure["points"]; triangles=closure["triangles"]
    geometry(points,triangles,"closure")
    rim=[tuple(p) for p in closure["originalOuterRim"]]; n=len(rim)
    require(3<=n<=2048 and [tuple(p) for p in points[:n]]==rim and len(set(rim))==n,"Actual source rim prefix differs")
    outer=closure["outerGuide"]
    require(type(outer) is list and 3<=len(outer)<=2048 and len(set(outer))==len(outer) and
        all(type(v) is int and n<=v<len(points) for v in outer),"Invalid distinct outer guide")
    inner_values=arc_values(points,closure["arcs"],"boundaryPointIds",recipe,list(range(n)))
    outer_values=arc_values(points,closure["arcs"],"outerGuidePointIds",recipe,outer)
    near=data["meshes"]["near"]
    require(type(near["originalVertexCount"]) is int and 0<near["originalVertexCount"]<=len(near["vertices"]) and
        type(near["originalTriangleCount"]) is int and 0<near["originalTriangleCount"]<=len(near["triangles"]),"Invalid original prefix counts")
    positions=near["vertices"][:near["originalVertexCount"]]
    source_triangles=near["triangles"][:near["originalTriangleCount"]]
    geometry(positions,source_triangles,"original near")
    lookup={tuple(p):i for i,p in enumerate(positions)}
    require(len(lookup)==len(positions) and all(p in lookup for p in rim),"Original exact-position correspondence is ambiguous/missing")
    source_graph,incidence=graph(positions,source_triangles,work)
    boundary={edge(lookup[a],lookup[b]) for a,b in zip(rim,(*rim[1:],rim[0]))}
    require({e for e,count in incidence.items() if count==1}==boundary,"Source loop is not the complete actual outer boundary")
    require(all(count<=2 for count in incidence.values()),"Nonmanifold original graph")
    source_distance=distances(source_graph,{lookup[p]:i for i,p in enumerate(rim)},work,max(recipe["sourceBandMetres"].values()))
    source_masks={}
    for i,p in enumerate(positions):
        distance,label=source_distance[i]
        if label<0: row=(0.,0.,0.,0.)
        else:
            cap,toe,width=inner_values[label]; row=(smooth(1-distance/width),0.,cap,toe)
        source_masks[tuple(p)]=tuple(f32(v) for v in row)
    closure_graph,_=graph(points,triangles,work)
    to_inner=distances(closure_graph,{i:i for i in range(n)},work)
    to_outer=distances(closure_graph,{i:i for i in outer},work)
    domains=dict(inner_values)
    for arc in closure["arcs"]:
        require(len(arc["boundaryPointIds"])==len(arc["collarPointIds"]),"Collar/source correspondence count differs")
        for a,b in zip(arc["boundaryPointIds"],arc["collarPointIds"]):
            require(type(b) is int and n<=b<len(points),"Invalid collar point")
            if b in domains: require(domains[b]==inner_values[a],"Conflicting collar domain")
            domains[b]=inner_values[a]
    for vertex,value in outer_values.items():
        require(vertex not in domains,"Outer guide overlaps retained collar domain")
        domains[vertex]=value
    closure_masks=[]
    for i in range(len(points)):
        if i%256==0: work.check()
        di,do=to_inner[i][0],to_outer[i][0]
        require(math.isfinite(di) and math.isfinite(do) and di+do>0,"Disconnected/coincident closure interfaces")
        # Both interfaces are exact despite a narrow local band: source rim G=0;
        # outer guide G=1. Distance-to-inner prevents bleed into original scan.
        outer_mix=smooth(1-do/recipe["outerContactBandMetres"])*di/(di+do)
        cap,toe,_=domains.get(i,(0.,0.,0.))
        row=tuple(f32(v) for v in (1.,outer_mix,cap,toe)); closure_masks.append(row)
        require(all(0<=v<=1 for v in row) and row[2]+row[3]<=1,"Encoded mask domain is invalid")
    require(all(closure_masks[i]==source_masks[rim[i]] for i in range(n)),"Common source rim masks differ")
    require(all(closure_masks[i][1]==1 for i in outer),"Outer contact endpoint is not exact")
    return source_masks,closure_masks,dict(sourceRimVertices=n,outerGuideVertices=len(outer),
        closureVertices=len(points),edges=work.edges,heapPushes=work.pushes,edgeVisits=work.visits,
        orderedCommonRimPositionMaskSha256=stream_sha(((*rim[i],*closure_masks[i]) for i in range(n)),"<7f"),
        orderedClosurePointMaskSha256=stream_sha(closure_masks,"<4f"),
        distanceRule=recipe["distanceRule"],nativeOrVisualAcceptance=False,
        interpolationRisk="Graph-distance masks are sampled at existing vertices. Triangle interpolation can exceed the nominal metre band, and simplified far interiors can differ. Original rim and all added closure values are exact shared correspondences; actual near/far material views remain required.")


def bind_patch_streams(document,buffers,patch,blob,original):
    """Authenticate actual replacement accessor bytes against the claimed corner map.

    Do not apply the closure's per-face derivative tangent test to native Mikk
    frames. Their separate source-normal/frame policy remains explicitly blocked.
    """
    target=tangent.apply_patch_document(document,patch,blob); raw_buffers=[*buffers,blob]
    require(len(target["buffers"])==len(raw_buffers) and len(raw_buffers)<=8 and
        sum(len(raw) for raw in raw_buffers)<=32*1024*1024,"Patched visual encoded capacity")
    primitive=target["meshes"][0]["primitives"][0]; attributes=primitive["attributes"]
    require(set(attributes)=={"POSITION","NORMAL","TEXCOORD_0","TANGENT"},"Unexpected patched attributes")
    rows={key:preview.accessor(target,raw_buffers,attributes[key],kind,(5126,),MAX_VERTICES)
        for key,kind in (("POSITION","VEC3"),("NORMAL","VEC3"),("TEXCOORD_0","VEC2"),("TANGENT","VEC4"))}
    indices=[v[0] for v in preview.accessor(target,raw_buffers,primitive["indices"],"SCALAR",(5123,5125),MAX_TRIANGLES*3)]
    mapping=patch["sourceVertexForOutput"]
    require(type(mapping) is list and 0<len(mapping)<=MAX_VERTICES and
        all(type(v) is int and 0<=v<len(original["positions"]) for v in mapping),"Invalid patched source map")
    require(all(len(values)==len(mapping) for values in rows.values()) and indices==patch["outputCornerIndices"],
        "Replacement accessor counts/indices differ from tangent mapping")
    for key,source,width in (("POSITION","positions",3),("NORMAL","normals",3),("TEXCOORD_0","uv",2)):
        require(stream_sha(rows[key],"<"+str(width)+"f")==
            stream_sha((original[source][v] for v in mapping),"<"+str(width)+"f"),
            "Replacement changed source attribute bytes: "+key)
    require(stream_sha(rows["TANGENT"],"<4f")==patch["encodedTangentSha256"],"Encoded tangent stream hash differs")
    return rows["TANGENT"]


def encode_lod(name,record,visual,source_masks,closure_masks,closure,patch=None,patch_tangents=None):
    """Serialize only appearance; original and added primitives retain explicit identity."""
    require(name in ("near","far") and 1<=len(visual)<=32,"Appearance LOD/primitive cap")
    require(sum(len(row["positions"]) for row in visual)<=MAX_VERTICES and
        sum(len(row["triangles"]) for row in visual)<=MAX_TRIANGLES,"Appearance aggregate geometry capacity")
    per_primitive=[[None]*len(row["positions"]) for row in visual]
    original=visual[0]
    require(len(original["triangles"])==record["originalTriangleCount"],"Original visual triangle count differs")
    for i,p in enumerate(original["positions"]):
        require(tuple(p) in source_masks,"Far/source vertex lacks an exact retained source position")
        per_primitive[0][i]=source_masks[tuple(p)]
    require(len(record["newFaces"])==len(closure["triangles"]),"Closure face count differs")
    mapped=set(); mapped_visual=set()
    for face in record["newFaces"]:
        ordinal=face["closureOrdinal"]
        require(type(ordinal) is int and 0<=ordinal<len(closure["triangles"]) and ordinal not in mapped
            and face["ordinal"]==record["originalTriangleCount"]+ordinal,"Duplicate/invalid closure face")
        mapped.add(ordinal); primitive,triangle=face["visualPrimitiveTriangle"]
        require(type(primitive) is int and 1<=primitive<len(visual) and type(triangle) is int and
            0<=triangle<len(visual[primitive]["triangles"]),"Invalid visual closure mapping")
        require((primitive,triangle) not in mapped_visual,"Duplicate visual closure mapping")
        mapped_visual.add((primitive,triangle))
        for vertex,closure_vertex in zip(visual[primitive]["triangles"][triangle],closure["triangles"][ordinal]):
            require(tuple(visual[primitive]["positions"][vertex])==tuple(closure["points"][closure_vertex]),"Closure visual position changed")
            value=closure_masks[closure_vertex]; old=per_primitive[primitive][vertex]
            require(old is None or old==value,"One visual vertex needs conflicting corner masks")
            per_primitive[primitive][vertex]=value
    require(len(mapped_visual)==sum(len(row["triangles"]) for row in visual[1:]),"Unmapped added visual triangle")
    if patch is not None:
        mapping=patch["sourceVertexForOutput"]
        require(type(mapping) is list and len(mapping)<=MAX_VERTICES and
            mapping[:len(original["positions"]) ]==list(range(len(original["positions"]))) and
            all(type(i) is int and 0<=i<len(original["positions"]) for i in mapping),"Tangent output prefix/source mapping differs")
        old_indices=[v for tri in original["triangles"] for v in tri]
        new_indices=patch["outputCornerIndices"]
        require(len(new_indices)==len(old_indices) and all(type(v) is int and 0<=v<len(mapping) for v in new_indices)
            and [mapping[v] for v in new_indices]==old_indices,"Tangent patch changed source corner mapping")
        per_primitive[0]=[per_primitive[0][v] for v in mapping]
    require(sum(len(values) for values in per_primitive)<=MAX_VERTICES,"Appearance split vertex capacity")
    blob=bytearray(); descriptors=[]; first_vertex=first_triangle=0
    for primitive,(row,values) in enumerate(zip(visual,per_primitive)):
        require(all(v is not None for v in values),"Unmapped visual appearance vertex")
        require(all(len(v)==4 and all(type(x) in (int,float) and math.isfinite(x) and 0<=x<=1 for x in v)
            and v[2]+v[3]<=1 for v in values),"Invalid encoded four-channel appearance mask")
        triangles=row["triangles"] if primitive or patch is None else [patch["outputCornerIndices"][i:i+3]
            for i in range(0,len(patch["outputCornerIndices"]),3)]
        vertex_map=patch["sourceVertexForOutput"] if primitive==0 and patch is not None else range(len(values))
        tangents=patch_tangents if primitive==0 and patch is not None else row.get("tangents")
        require(tangents is not None and len(tangents)==len(values) and
            all(len(t)==4 and all(math.isfinite(x) and f32(x)==x for x in t) for t in tangents),
            "Missing/mismatched authenticated tangent stream")
        tangent_sha=stream_sha(tangents,"<4f")
        if primitive==0 and patch is not None:
            require(tangent_sha==patch["encodedTangentSha256"],"Tangent mask target stream hash differs")
        offset=len(blob); raw=b"".join(struct.pack("<4f",*v) for v in values); blob.extend(raw)
        corner_mask=stream_sha((values[v] for t in triangles for v in t),"<4f")
        source_corner=stream_sha(((*row["positions"][v],*row["normals"][v],*row["uv"][v])
            for t in row["triangles"] for v in t),"<8f")
        if primitive==0 and patch is not None:
            require(patch["perCornerPositionNormalUvSha256"]==source_corner,"Tangent/source corner byte identity differs")
        descriptors.append(dict(primitive=primitive,sourceKind="original-scan" if primitive==0 else "added-closure",
            material=row["material"],firstVertex=first_vertex,firstTriangle=first_triangle,
            vertexCount=len(values),triangleCount=len(triangles),byteOffset=offset,byteLength=len(raw),
            componentType="float32-le",components=4,strideBytes=16,vertexMaskSha256=sha(raw),cornerMaskSha256=corner_mask,
            orderedVertexPositionNormalUvSha256=stream_sha(((*row["positions"][v],*row["normals"][v],*row["uv"][v])
                for v in vertex_map),"<8f"),
            orderedLocalIndexSha256=stream_sha(((v,) for t in triangles for v in t),"<I"),
            orderedTangentSha256=tangent_sha,
            baseCornerPositionNormalUvSha256=source_corner,sourceAtlasSamplingAllowed=primitive==0))
        first_vertex+=len(values); first_triangle+=len(triangles)
    require(sum(row["vertexCount"] for row in descriptors)<=MAX_VERTICES and len(blob)<=MAX_VERTICES*16,"Appearance stream cap")
    return bytes(blob),dict(schema=LOD_SCHEMA,lod=name,dataFile=name+"-appearance.bin",primitives=descriptors,
        vertexCount=first_vertex,triangleCount=first_triangle,bytes=len(blob),sha256=sha(blob),
        tangentAppearanceAdmissionBlocked=patch.get("appearanceAdmissionBlocked",True) if patch is not None else True,
        triangleCornerOrderPreserved=True,physicalGeometryChanged=False,sourceAttributesChanged=False)


def prepare(package,receipt_sha,recipe_path,tangent_package,tangent_receipt_sha,root=ROOT):
    receipt,inputs,admission=tangent.prepare(package,receipt_sha,root)
    recipe_path=preview.safe_path(recipe_path,admission.root)
    # The recipe itself is explicit local authoring input; its exact bytes become the output policy identity.
    admission.pin(recipe_path,author.sha256(recipe_path),65536)
    recipe=author.strict_json(recipe_path,65536,admission.pins[recipe_path][0]); validate_recipe(recipe)
    require(recipe["moduleReceiptSha256"]==receipt_sha,"Recipe targets another module receipt")
    layer=recipe["groundLayers"]
    layer_path=admission.pin(author.contained(admission.root,layer["file"]),layer["sha256"],65536)
    layers=author.strict_json(layer_path,65536,layer["sha256"])
    for index,key,metres in ((1,"forest_floor",3),(2,"soil",2),(3,"rock",2.4),(4,"gravel",4)):
        row=layers["layers"][index]
        require(row["index"]==index and row["key"]==key and row["metres"]==metres,"Shared ground layer role/metres changed")
    package=Path(package).resolve(); metadata=package/"module-candidate.json"
    require(metadata in admission.pins,"Unpinned module metadata")
    # Covers bounded JSON/list expansion, all render streams, adjacency duplicates,
    # source masks, capped pending heap and output buffers in addition to the
    # authenticated tangent reader's original geometry reservation.
    reserved=sum(row[2].reserved_bytes+row[2].aggregate_vertices*256 for row in inputs.values())
    reserved+=admission.pins[metadata][1]*12+64*1024*1024
    require(reserved<=384*1024*1024,"Appearance decoded/work reservation exceeds384MiB")
    data=author.strict_json(metadata,16*1024*1024,admission.pins[metadata][0])
    require(data.get("capabilities")==[] and data.get("rootVolumes")==[] and data.get("contacts")==[],"Unexpected source admission")
    visual={}
    for name,(document,buffers,_) in inputs.items():
        record=data["meshes"][name]; geometry(record["vertices"],record["triangles"],name)
        visual[name]=preview.decode_visual(document,buffers,record); admission.check()
    tangent_package=preview.safe_path(tangent_package,admission.root,exists=False)
    target=admission.pin(tangent_package/"tangent-patch-receipt.json",tangent_receipt_sha,4*1024*1024)
    tangent_receipt=author.strict_json(target,4*1024*1024,tangent_receipt_sha)
    require(tangent_receipt.get("schema")==tangent.SCHEMA and tangent_receipt.get("assetAccepted") is False
        and tangent_receipt["baseModuleReceiptSha256"]==receipt_sha,"Tangent receipt/base identity differs")
    expected={name+suffix for name in ("near","far") for suffix in ("-tangent-patch.json","-original-tangents.bin")}
    outputs=tangent_receipt["outputs"]
    require(type(outputs) is list and len(outputs)==4 and {row["file"] for row in outputs}==expected,"Unexpected tangent output closure")
    for row in outputs:
        path=admission.pin(author.uri_file(tangent_package,row["file"]),row["sha256"],4*1024*1024)
        require(path.stat().st_size==row["bytes"],"Tangent file size differs")
        reserved+=row["bytes"]*(12 if row["file"].endswith(".json") else 2)
    require(reserved<=384*1024*1024,"Appearance/tangent decoded/work reservation exceeds384MiB")
    patches={}; patch_tangents={}
    for name in ("near","far"):
        path=tangent_package/(name+"-tangent-patch.json")
        patch=author.strict_json(path,4*1024*1024,admission.pins[path][0])
        blob=admission.read(tangent_package/(name+"-original-tangents.bin"),4*1024*1024)
        require(patch.get("nativeMikkVerified") is True and patch["baseDocumentSha256"]==admission.pins[package/(name+".gltf")][0],
            "Missing native tangent/base evidence")
        patch_tangents[name]=bind_patch_streams(inputs[name][0],inputs[name][1],patch,blob,visual[name][0])
        patches[name]=patch
    admission.check()
    return receipt,data,recipe,visual,patches,patch_tangents,admission,reserved


def main(argv=None):
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package",type=Path,required=True); parser.add_argument("--receipt-sha256",required=True)
    parser.add_argument("--recipe",type=Path,default=HERE/"data"/"forest_contact_appearance.json")
    parser.add_argument("--tangent-package",type=Path,required=True); parser.add_argument("--tangent-receipt-sha256",required=True)
    parser.add_argument("--destination",type=Path,required=True)
    options=parser.parse_args(argv)
    receipt,data,recipe,visual,patches,patch_tangents,admission,reserved=prepare(options.package,preview.digest(options.receipt_sha256),
        options.recipe,options.tangent_package,preview.digest(options.tangent_receipt_sha256))
    destination=preview.safe_path(options.destination,ROOT/".artifacts",exists=False)
    require(not destination.exists() and destination.parent.is_dir(),"Appearance destination must be fresh")
    work=Work(admission.check); source_masks,closure_masks,evidence=build_fields(data,recipe,work)
    results=[]
    for name in ("near","far"):
        blob,descriptor=encode_lod(name,data["meshes"][name],visual[name],source_masks,closure_masks,data["closure"],
            patches[name],patch_tangents[name])
        descriptor["baseGltfSha256"]=admission.pins[Path(options.package).resolve()/(name+".gltf")][0]
        metadata_raw=author.canonical(descriptor); require(len(metadata_raw)<=1024*1024,"Appearance LOD metadata cap")
        results.append((name,blob,metadata_raw)); admission.check()
    admission.recheck(); destination.mkdir(); outputs=[]
    for name,blob,metadata_raw in results:
        for filename,raw in ((name+"-appearance.bin",blob),(name+"-appearance.json",metadata_raw)):
            author.write_new(destination/filename,raw); outputs.append(dict(file=filename,bytes=len(raw),sha256=sha(raw)))
    report=dict(schema=SCHEMA,status="unadmitted-additive-appearance",assetAccepted=False,appearanceAccepted=False,
        capabilities=[],rootPermissions=False,baseModuleReceiptSha256=options.receipt_sha256,
        lods=[dict(key=name,metadataFile=name+"-appearance.json",dataFile=name+"-appearance.bin") for name in ("near","far")],
        tangentAppearanceAdmissionBlocked=any(patch["appearanceAdmissionBlocked"] for patch in patches.values()),
        tangentReceiptSha256=options.tangent_receipt_sha256,recipe=recipe,recipeSha256=admission.pins[Path(options.recipe).resolve()][0],
        toolSha256=author.sha256(Path(__file__)),evidence=evidence,outputs=outputs,reservedDecodedWorkOutputBytes=reserved,
        inputPins=[dict(file=p.relative_to(ROOT).as_posix(),bytes=size,sha256=pin) for p,(pin,size) in admission.pins.items()],
        runtimeRequirements=["Borrow existing shared substrate texture arrays and metre scales",
            "Original atlas only on original primitive; no UV extrapolation onto added faces",
            "Outer blend requires matching actual published terrain UV2/material/climate/water/normal sample and ready cohort",
            "Missing/stale/out-of-range terrain appearance is unavailable, never extrapolated",
            "Decode source and substrate normal maps in their verified frames before world-space blending",
            "No geometry, contact, soil depth, overhead clearance or root permission follows from these masks"],
        pending=["Original normal hemisphere/frame-quality review", "Near/far interpolation and native PBR views",
            "Actual sealed terrain contact/rebase/cube-seam/visibility views", "Charged native residency and performance"])
    admission.recheck(); raw=author.canonical(report); require(len(raw)<=1024*1024,"Appearance receipt cap")
    author.write_new(destination/"appearance-receipt.json",raw)
    print(json.dumps(dict(status=report["status"],assetAccepted=False,outputs=outputs,evidence=evidence),sort_keys=True))


if __name__=="__main__":
    try: main()
    except (ValueError,KeyError,OSError) as error:
        print("INCOMPLETE/REFUSED: "+str(error),file=sys.stderr); raise SystemExit(2)

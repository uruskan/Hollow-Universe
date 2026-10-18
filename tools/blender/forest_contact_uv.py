"""Bounded metric UV charts for added forest-module faces; no geometry authoring.

The six signed orthonormal projections use module-local metres with origin zero.
Planar walls therefore have unit metric. Curved faces prefer the authored semantic
plane when its normal cosine is >= 1/2, otherwise use their signed dominant axis.
Charts are edge-connected equal-semantic/equal-projection components. Every chart
cut is reported; there is no hidden per-face jitter, packing, topology change or
claim of a globally isometric unwrap. Repeating textures do not need atlas packing.

All area/orientation and <=2 bidirectional linear-distortion decisions use exact
decoded float32 coordinates. Tangents are derived from the actual UV derivatives,
orthogonalized against each supplied corner shading normal, and validated after
float32 encoding. Standalone calls default to flat normals; the module supplies
its separately authenticated intentional soil-fan normals without changing UV charts.
"""
from __future__ import annotations

from collections import defaultdict
from dataclasses import dataclass
from fractions import Fraction
import hashlib
import json
import math
import struct

SCHEMA = "forest-contact-metric-uv-v1"
MAXIMUM_FACES, MAXIMUM_VERTICES, MAXIMUM_EDGES = 5200, 4096, 15600
MAXIMUM_RESERVED_BYTES = 64*1024*1024
PROJECTIONS = {
    "+x": ((0,0,-1),(0,1,0),(1,0,0)), "-x": ((0,0,1),(0,1,0),(-1,0,0)),
    "+y": ((1,0,0),(0,0,-1),(0,1,0)), "-y": ((1,0,0),(0,0,1),(0,-1,0)),
    "+z": ((1,0,0),(0,1,0),(0,0,1)), "-z": ((-1,0,0),(0,1,0),(0,0,-1)),
}
PREFERRED = {
    "soil-cap-candidate":"+y", "toe-apron-candidate":"+y", "transition-interior-candidate":"+y",
    "left-return":"-x", "right-return":"+x", "back-wall":"-z", "toe-wall":"+z",
    "left-wall":"-x", "right-wall":"+x", "base":"-y",
}
DEFAULT_POLICY = dict(schema=SCHEMA,metresPerCoordinate=1,originMetres=[0,0,0],
    maximumLinearDistortion=2,preferredNormalCosineMinimum=.5,
    projectionRule="semantic-preferred-then-signed-dominant-axis-v1",
    seams="edge-connected-semantic-signed-projection-v1",maximumFaces=1800,maximumCharts=1800,maximumEdges=5400)


class UvError(ValueError):
    pass


def require(condition,message):
    if not condition: raise UvError(message)


def validate_policy(policy):
    require(type(policy) is dict and set(policy) == set(DEFAULT_POLICY),"Unexpected metric UV policy shape")
    for key in ("schema","projectionRule","seams"):
        require(policy[key] == DEFAULT_POLICY[key],"Unsupported metric UV policy: "+key)
    for key,expected in (("metresPerCoordinate",1),("maximumLinearDistortion",2),("preferredNormalCosineMinimum",.5)):
        require(type(policy[key]) in (int,float) and policy[key] == expected,"Metric UV scalar policy changed: "+key)
    require(type(policy["originMetres"]) is list and len(policy["originMetres"]) == 3 and
        all(type(v) in (int,float) and v == 0 for v in policy["originMetres"]),"Metric UV origin must remain exact module zero")
    for key,maximum in (("maximumFaces",MAXIMUM_FACES),("maximumCharts",MAXIMUM_FACES),("maximumEdges",MAXIMUM_EDGES)):
        require(type(policy[key]) is int and 1 <= policy[key] <= maximum,"Metric UV capacity exceeds hard limit: "+key)
    require(policy["maximumCharts"] <= policy["maximumFaces"] and policy["maximumEdges"] <= policy["maximumFaces"]*3,
            "Inconsistent metric UV capacities")


def f32(value):
    return struct.unpack("<f",struct.pack("<f",value))[0]


def sub(a,b): return tuple(x-y for x,y in zip(a,b))
def dot(a,b): return sum(x*y for x,y in zip(a,b))
def cross(a,b): return (a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0])
def normal(points): return cross(sub(points[1],points[0]),sub(points[2],points[0]))


def encoded_unit(vector):
    length = math.sqrt(dot(vector,vector))
    require(math.isfinite(length) and length > 0,"Cannot encode a degenerate tangent frame")
    return tuple(f32(v/length) for v in vector)


def psd(a,b,c):
    """Positive semidefinite symmetric 2x2 matrix, with exact entries."""
    return a >= 0 and c >= 0 and a*c-b*b >= 0


def metric_certificate(points,uvs):
    """Certify lengths in ALL tangent directions, not only the three edges."""
    p = [tuple(Fraction(v) for v in row) for row in points]
    uv = [tuple(Fraction(v) for v in row) for row in uvs]
    e,f = sub(p[1],p[0]),sub(p[2],p[0])
    a,b = sub(uv[1],uv[0]),sub(uv[2],uv[0])
    area = a[0]*b[1]-a[1]*b[0]
    require(area > 0,"Metric UV triangle collapsed or mirrored after float32 encoding")
    g = (dot(e,e),dot(e,f),dot(f,f))
    h = (dot(a,a),dot(a,b),dot(b,b))
    require(g[0]*g[2]-g[1]*g[1] > 0,"Metric UV source triangle is degenerate")
    require(psd(*(4*h[i]-g[i] for i in range(3))) and psd(*(4*g[i]-h[i] for i in range(3))),
            "Metric UV bidirectional linear distortion exceeds 2")
    # A diagnostic of this particular chart, not the authority for the pass.
    determinant = float(area)
    du = tuple(float((e[i]*b[1]-f[i]*a[1])/area) for i in range(3))
    dv = tuple(float((f[i]*a[0]-e[i]*b[0])/area) for i in range(3))
    q,r,s = dot(du,du),dot(du,dv),dot(dv,dv)
    largest = .5*(q+s+math.hypot(q-s,2*r))
    smallest = (q*s-r*r)/largest
    require(determinant > 0 and smallest > 0 and math.isfinite(largest),"Unrepresentable UV derivative diagnostic")
    return du,dv,dict(area2Exact=str(area),maximumStretchMeasured=math.sqrt(largest),
                     minimumStretchMeasured=math.sqrt(smallest),allDirectionsWithinTwoExact=True)


def tangent_frames(points,uvs,corner_normals=None):
    du,dv,metric = metric_certificate(points,uvs)
    if corner_normals is None:
        corner_normals = (encoded_unit(normal(points)),)*3
    require(len(corner_normals) == 3,"Expected three shading normals")
    normals,tangents = [],[]
    residual = 0.0
    for supplied in corner_normals:
        require(len(supplied) == 3 and all(type(v) in (int,float) and math.isfinite(v) for v in supplied),"Invalid corner shading normal")
        n = tuple(float(v) for v in supplied)
        require(all(f32(v) == v for v in n) and abs(dot(n,n)-1) <= 2e-6,"Shading normal must be encoded float32 and unit length")
        projected = tuple(du[i]-n[i]*(dot(n,du)/dot(n,n)) for i in range(3))
        require(dot(projected,projected) > dot(du,du)*1e-12,"Shading normal leaves a degenerate UV tangent")
        t = encoded_unit(projected)
        handed = dot(cross(n,t),dv)
        require(math.isfinite(handed) and abs(handed) > math.sqrt(dot(dv,dv))*1e-7,
                "Shading normal leaves an ambiguous UV bitangent")
        w = 1.0 if handed > 0 else -1.0
        orthogonality = abs(dot(n,t))
        require(orthogonality <= 2e-6 and abs(dot(t,t)-1) <= 2e-6 and dot(t,projected) > 0,
                "Float32 tangent frame did not retain its derivative orientation")
        residual = max(residual,orthogonality)
        normals.append(n); tangents.append((*t,w))
    metric["maximumEncodedNormalTangentDot"] = residual
    return tuple(normals),tuple(tangents),metric


@dataclass(frozen=True)
class FaceUv:
    projection: str
    chart: int
    uvs: tuple
    normals: tuple
    tangents: tuple


@dataclass(frozen=True)
class UvLayout:
    faces: tuple
    evidence: dict


def build(positions,triangles,groups,policy=None,check=lambda:None,*,corner_normals=None):
    policy = DEFAULT_POLICY if policy is None else policy
    validate_policy(policy)
    require(type(positions) in (list,tuple) and 0 < len(positions) <= MAXIMUM_VERTICES and
            type(triangles) in (list,tuple) and 0 < len(triangles) <= policy["maximumFaces"] and
            len(groups) == len(triangles),"Metric UV geometry capacity/shape exceeded")
    reserved = 16384+len(positions)*512+len(triangles)*8192+min(len(triangles)*3,policy["maximumEdges"])*256
    require(reserved <= MAXIMUM_RESERVED_BYTES,"Metric UV declared storage reservation exceeded")
    require(all(type(p) in (list,tuple) and len(p) == 3 and
                all(type(v) in (int,float) and math.isfinite(v) and abs(v) <= 1e4 and f32(v) == v for v in p)
                for p in positions),"Metric UV positions must be exact finite float32 metres")
    require(all(type(t) in (list,tuple) and len(t) == 3 and len(set(t)) == 3 and
                all(type(v) is int and 0 <= v < len(positions) for v in t) for t in triangles),"Invalid metric UV triangle indices")
    require(all(type(g) is str and g in PREFERRED for g in groups),"Unknown semantic UV group")
    require(corner_normals is None or (type(corner_normals) in (list,tuple) and
            len(corner_normals) == len(triangles) and all(type(face) in (list,tuple) and len(face) == 3
            for face in corner_normals)),"Shading normal layout must cover every face and corner")
    geometry_hash = hashlib.sha256(b"forest-contact-closure-geometry-v1\0")
    geometry_hash.update(struct.pack("<II",len(positions),len(triangles)))
    for p in positions: geometry_hash.update(struct.pack("<fff",*p))
    edges = defaultdict(list)
    projections,encoded,metrics = [],[],[]
    for ordinal,(triangle,group) in enumerate(zip(triangles,groups)):
        check()
        geometry_hash.update(struct.pack("<III",*triangle))
        name = group.encode("ascii"); geometry_hash.update(struct.pack("<I",len(name))+name)
        points = tuple(tuple(positions[v]) for v in triangle)
        exact = tuple(tuple(Fraction(v) for v in p) for p in points)
        n = normal(exact); length2 = dot(n,n)
        require(length2 > 0,"Degenerate metric UV source triangle")
        preferred = PREFERRED[group]
        component = dot(n,PROJECTIONS[preferred][2])
        if component > 0 and 4*component*component >= length2: projection = preferred
        else:
            axis = max(range(3),key=lambda i:(abs(n[i]),-i))
            projection = ("+" if n[axis] > 0 else "-")+"xyz"[axis]
        u,v,_ = PROJECTIONS[projection]
        uv = tuple((f32(dot(p,u)),f32(dot(p,v))) for p in points)
        ns,ts,metric = tangent_frames(points,uv,None if corner_normals is None else corner_normals[ordinal])
        projections.append(projection); encoded.append((uv,ns,ts)); metrics.append(metric)
        for a,b in zip(triangle,triangle[1:]+triangle[:1]):
            key = tuple(sorted((a,b)))
            if key not in edges: require(len(edges) < policy["maximumEdges"],"Metric UV edge capacity exceeded")
            edges[key].append((ordinal,a,b))
            require(len(edges[key]) <= 2,"Nonmanifold added-face UV edge")
    neighbors = [[] for _ in triangles]
    seams,boundary = [],[]
    for edge,incident in sorted(edges.items()):
        if len(incident) == 1:
            boundary.append(dict(vertices=list(edge),face=incident[0][0],reason="original-scan-boundary")); continue
        a,b = incident
        require(a[1] == b[2] and a[2] == b[1],"Inconsistent added-face winding at UV edge")
        fa,fb = a[0],b[0]
        if (groups[fa],projections[fa]) == (groups[fb],projections[fb]):
            neighbors[fa].append(fb); neighbors[fb].append(fa)
        else:
            seams.append(dict(vertices=list(edge),faces=[fa,fb],reason="semantic" if groups[fa] != groups[fb] else "projection"))
    charts,assignment = [],[-1]*len(triangles)
    for first in range(len(triangles)):
        if assignment[first] >= 0: continue
        require(len(charts) < policy["maximumCharts"],"Metric UV chart capacity exceeded")
        chart = len(charts); assignment[first] = chart; pending,faces = [first],[]
        while pending:
            check(); face = pending.pop(); faces.append(face)
            for other in sorted(neighbors[face],reverse=True):
                if assignment[other] < 0: assignment[other] = chart; pending.append(other)
        faces.sort(); projection = projections[first]
        charts.append(dict(id=chart,key=f"{groups[first]}:{projection}:{first}",semanticGroup=groups[first],
            projection=projection,originMetres=[0,0,0],uAxis=list(PROJECTIONS[projection][0]),
            vAxis=list(PROJECTIONS[projection][1]),faceOrdinals=faces))
    faces = tuple(FaceUv(projections[i],assignment[i],*encoded[i]) for i in range(len(triangles)))
    stream_hash = hashlib.sha256(SCHEMA.encode("ascii")+b"\0")
    for face in faces:
        stream_hash.update(struct.pack("<I",face.chart))
        for uv,n,t in zip(face.uvs,face.normals,face.tangents): stream_hash.update(struct.pack("<fffffffff",*uv,*n,*t))
    evidence = dict(schema=SCHEMA,policy=policy,policySha256=hashlib.sha256(json.dumps(policy,sort_keys=True,separators=(",",":" )).encode()).hexdigest(),
        closureGeometrySha256=geometry_hash.hexdigest(),addedAttributeStreamSha256=stream_hash.hexdigest(),
        faceCount=len(faces),chartCount=len(charts),edgeCount=len(edges),charts=charts,seams=seams,
        originalBoundaryEdges=boundary,collapsedTriangles=0,mirroredTriangles=0,
        allDirectionsWithinTwoExact=True,maximumLinearDistortion=2,
        maximumStretchMeasured=max(m["maximumStretchMeasured"] for m in metrics),
        minimumStretchMeasured=min(m["minimumStretchMeasured"] for m in metrics),
        maximumEncodedNormalTangentDot=max(m["maximumEncodedNormalTangentDot"] for m in metrics),
        declaredReservedBytes=reserved,maximumReservedBytes=MAXIMUM_RESERVED_BYTES,
        geometryChanged=False,contactsApproved=False,materialsApproved=False,
        tangentContract="Actual UV derivatives Gram-Schmidt against supplied per-corner shading normals; glTF tangent.w carries handedness",
        normalPolicy=("Current flat closure normals retained; future intentional soil-patch smoothing is a separate visual authoring gate"
            if corner_normals is None else "Authenticated supplied per-corner normals; UV derivatives reprojected into each shading tangent plane"),
        scope="Exact all-direction metric bound on decoded float32 UVs; seams explicit, no global isometry or final material/appearance approval")
    check()
    return UvLayout(faces,evidence)

"""Bounded authored shading normals for the two proposed soil closure domains.

This changes no position, index, UV, physical normal, original scan attribute or
contact. Normal fans connect triangle corners through shared indexed edges of
the same explicitly named soil semantic. UV chart cuts do not make hard edges.
Other semantics remain flat; disconnected vertex fans never merge by position.

Indexed edges are considered in sorted order. A proposed smooth edge must keep
both endpoint fans pairwise within an explicit 60-degree geometric normal spread;
otherwise it is an authored hard crease. This prevents a chain of shallow joins
from wrapping around a folded collar. Each fan uses an exact sum of oriented
triangle area vectors, then one float32 unit encoding. A zero sum or final encoded
normal outside any incident triangle's open outward hemisphere still refuses.
There is no angle search, geometry change or unbounded corrective retry.
This is shading intent, not a soil/root permission or a terrain seal certificate.
"""
from __future__ import annotations

from dataclasses import dataclass
from fractions import Fraction
import hashlib
import json
import math
import struct

SCHEMA = "forest-contact-soil-normal-fans-v2"
MAXIMUM_FACES,MAXIMUM_VERTICES,MAXIMUM_EDGES = 5200,4096,15600
MAXIMUM_RESERVED_BYTES = 64*1024*1024
MAXIMUM_PAIR_TESTS = 262144
SOIL_GROUPS = ("soil-cap-candidate","toe-apron-candidate")
GROUPS = (*SOIL_GROUPS,"transition-interior-candidate","left-return","right-return",
          "back-wall","toe-wall","left-wall","right-wall","base")
DEFAULT_POLICY = dict(schema=SCHEMA,smoothGroups=list(SOIL_GROUPS),
    fanRule="sorted-indexed-edge-pairwise-normal-cone-v2",weighting="exact-oriented-area-vector-sum-v1",
    hardBoundaryRule="different-semantics-and-original-scan-boundary-v1",
    maximumNormalSpreadDegrees=60,minimumNormalCosine=.5,maximumPairTests=131072,
    maximumFaces=1800,maximumCorners=5400,maximumEdges=5400)


class NormalError(ValueError):
    pass


def require(condition,message):
    if not condition: raise NormalError(message)


def f32(value): return struct.unpack("<f",struct.pack("<f",value))[0]
def sub(a,b): return tuple(x-y for x,y in zip(a,b))
def dot(a,b): return sum(x*y for x,y in zip(a,b))
def cross(a,b): return (a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0])
def area_vector(points): return cross(sub(points[1],points[0]),sub(points[2],points[0]))


def encoded_unit(vector):
    values = tuple(float(v) for v in vector)
    length = math.sqrt(dot(values,values))
    require(math.isfinite(length) and length > 0,"Cannot encode zero/cancelled soil normal fan")
    result = tuple(f32(v/length) for v in values)
    require(abs(dot(result,result)-1) <= 2e-6,"Encoded soil normal is not unit length")
    return result


def validate_policy(policy):
    require(type(policy) is dict and set(policy) == set(DEFAULT_POLICY),"Unexpected soil normal policy shape")
    for key in ("schema","fanRule","weighting","hardBoundaryRule","smoothGroups"):
        require(policy[key] == DEFAULT_POLICY[key],"Unsupported soil normal authoring policy: "+key)
    require(type(policy["maximumNormalSpreadDegrees"]) is int and policy["maximumNormalSpreadDegrees"] == 60 and
            type(policy["minimumNormalCosine"]) in (int,float) and policy["minimumNormalCosine"] == .5,
            "Soil normal crease angle must be the explicit 60-degree policy")
    for key,maximum in (("maximumFaces",MAXIMUM_FACES),("maximumCorners",3*MAXIMUM_FACES),
                        ("maximumEdges",MAXIMUM_EDGES),("maximumPairTests",MAXIMUM_PAIR_TESTS)):
        require(type(policy[key]) is int and 1 <= policy[key] <= maximum,"Soil normal capacity exceeds hard limit: "+key)
    require(policy["maximumCorners"] == 3*policy["maximumFaces"] and policy["maximumEdges"] <= 3*policy["maximumFaces"],
            "Inconsistent soil normal capacities")


@dataclass(frozen=True)
class NormalLayout:
    corner_normals: tuple
    evidence: dict


def build(positions,triangles,groups,policy=None,check=lambda:None):
    policy = DEFAULT_POLICY if policy is None else policy
    validate_policy(policy)
    require(type(positions) in (list,tuple) and 0 < len(positions) <= MAXIMUM_VERTICES and
            type(triangles) in (list,tuple) and 0 < len(triangles) <= policy["maximumFaces"] and
            type(groups) in (list,tuple) and len(groups) == len(triangles),"Soil normal geometry capacity/shape exceeded")
    require(all(type(p) in (list,tuple) and len(p) == 3 and
                all(type(v) in (int,float) and math.isfinite(v) and abs(v) <= 1e4 and f32(v) == v for v in p)
                for p in positions),"Soil normal positions must be exact finite float32 metres")
    require(all(type(t) in (list,tuple) and len(t) == 3 and len(set(t)) == 3 and
                all(type(v) is int and 0 <= v < len(positions) for v in t) for t in triangles),"Invalid soil normal triangle indices")
    require(all(type(group) is str and group in GROUPS for group in groups),"Unknown soil normal semantic")
    corners = 3*len(triangles)
    require(corners <= policy["maximumCorners"],"Soil normal corner capacity exceeded")
    reserved = (16384+len(positions)*512+len(triangles)*8192+min(corners,policy["maximumEdges"])*256+
                min(policy["maximumPairTests"],len(triangles)*(len(triangles)-1)//2)*256)
    require(reserved <= MAXIMUM_RESERVED_BYTES,"Soil normal declared storage reservation exceeded")
    geometry_hash = hashlib.sha256(b"forest-contact-closure-geometry-v1\0")
    geometry_hash.update(struct.pack("<II",len(positions),len(triangles)))
    for point in positions: geometry_hash.update(struct.pack("<fff",*point))
    parents = list(range(corners))
    members = {corner:[corner] for corner in range(corners) if groups[corner//3] in SOIL_GROUPS}
    edges,area_vectors,flat = {},[],[]
    for face,(triangle,group) in enumerate(zip(triangles,groups)):
        check()
        geometry_hash.update(struct.pack("<III",*triangle))
        name = group.encode("ascii"); geometry_hash.update(struct.pack("<I",len(name))+name)
        points = tuple(tuple(positions[v]) for v in triangle)
        exact = tuple(tuple(Fraction(v) for v in point) for point in points)
        area = area_vector(exact)
        require(dot(area,area) > 0,"Degenerate soil normal source triangle")
        area_vectors.append(area)
        # Preserve the existing author's flat float arithmetic for all nonsoil semantics.
        flat.append(encoded_unit(area_vector(points)))
        for corner,(a,b) in enumerate(zip(triangle,triangle[1:]+triangle[:1])):
            key = tuple(sorted((a,b)))
            if key not in edges:
                require(len(edges) < policy["maximumEdges"],"Soil normal edge capacity exceeded")
                edges[key] = []
            edges[key].append((face,corner,a,b))
            require(len(edges[key]) <= 2,"Nonmanifold soil normal edge")

    def find(corner):
        while parents[corner] != corner:
            parents[corner] = parents[parents[corner]]; corner = parents[corner]
        return corner

    def join(a,b):
        a,b = find(a),find(b)
        if a != b:
            low,high = min(a,b),max(a,b)
            parents[high] = low
            members[low] = sorted(members[low]+members.pop(high))

    pair_cache = {}
    pair_tests,pair_cache_hits = 0,0
    def compatible(first,second):
        nonlocal pair_tests,pair_cache_hits
        check()
        require(pair_tests < policy["maximumPairTests"],"Soil normal pair-test capacity exceeded")
        pair_tests += 1
        if first == second: return True
        key = tuple(sorted((first,second)))
        if key in pair_cache:
            pair_cache_hits += 1
            return pair_cache[key]
        a,b = area_vectors[first],area_vectors[second]
        cosine_numerator = dot(a,b)
        answer = cosine_numerator > 0 and 4*cosine_numerator*cosine_numerator >= dot(a,a)*dot(b,b)
        pair_cache[key] = answer
        return answer

    def can_join(a,b):
        a,b = find(a),find(b)
        if a == b: return True
        return all(compatible(x//3,y//3) for x in members[a] for y in members[b])

    hard_edges,smooth_edges = [],0
    for edge,incident in sorted(edges.items()):
        check()
        if len(incident) == 1:
            hard_edges.append(dict(vertices=list(edge),faces=[incident[0][0]],reason="original-scan-boundary")); continue
        a,b = incident
        require(a[2] == b[3] and a[3] == b[2],"Inconsistent soil normal source winding")
        if groups[a[0]] == groups[b[0]] and groups[a[0]] in SOIL_GROUPS:
            endpoints = ((3*a[0]+a[1],3*b[0]+(b[1]+1)%3),(3*a[0]+(a[1]+1)%3,3*b[0]+b[1]))
            reason = ("geometric-dihedral-over-60" if not compatible(a[0],b[0]) else
                      "endpoint-fan-spread-over-60" if not all(can_join(x,y) for x,y in endpoints) else None)
            if reason is not None:
                hard_edges.append(dict(vertices=list(edge),faces=[a[0],b[0]],reason=reason))
                continue
            for x,y in endpoints: join(x,y)
            smooth_edges += 1
        else:
            hard_edges.append(dict(vertices=list(edge),faces=[a[0],b[0]],reason="semantic" if groups[a[0]] != groups[b[0]] else "flat-semantic"))
    components = {}
    for corner in range(corners):
        if groups[corner//3] in SOIL_GROUPS: components.setdefault(find(corner),[]).append(corner)
    normals = [[value]*3 for value in flat]
    fans,changed,minimum_cosine = [],0,1.0
    for root,members in sorted(components.items()):
        check()
        vector = tuple(sum((area_vectors[corner//3][axis] for corner in members),Fraction(0)) for axis in range(3))
        normal = encoded_unit(vector)
        exact_normal = tuple(Fraction(v) for v in normal)
        for corner in members:
            face,local = divmod(corner,3)
            facing = dot(exact_normal,area_vectors[face])
            require(facing > 0,"Soil normal fan points inward/tangent at corner "+str(corner)+"; fan="+str(members))
            minimum_cosine = min(minimum_cosine,float(facing)/math.sqrt(float(
                dot(area_vectors[face],area_vectors[face])*dot(exact_normal,exact_normal))))
            normals[face][local] = normal
            changed += normal != flat[face]
        fans.append(dict(id=len(fans),vertex=triangles[root//3][root%3],semanticGroup=groups[root//3],
                         corners=members,normal=list(normal)))
    normals = tuple(tuple(face) for face in normals)
    stream_hash = hashlib.sha256(SCHEMA.encode("ascii")+b"\0")
    for face in normals:
        for normal in face: stream_hash.update(struct.pack("<fff",*normal))
    evidence = dict(schema=SCHEMA,policy=policy,policySha256=hashlib.sha256(json.dumps(policy,sort_keys=True,separators=(",",":")).encode()).hexdigest(),
        closureGeometrySha256=geometry_hash.hexdigest(),normalStreamSha256=stream_hash.hexdigest(),
        faceCount=len(triangles),cornerCount=corners,edgeCount=len(edges),smoothEdges=smooth_edges,
        pairTests=pair_tests,pairCacheHits=pair_cache_hits,maximumPairTests=policy["maximumPairTests"],
        pairTestScope="Every attempted face-pair comparison, including cache hits; no uncharged repeated pair work",
        normalSpreadDecision="Exact positive dot and 4*dot^2 >= lengthSquaredA*lengthSquaredB for every face pair in each fan",
        smoothFanCount=len(fans),fans=fans,hardEdges=hard_edges,changedCorners=changed,
        minimumEncodedIncidentNormalCosineMeasured=minimum_cosine,encodedOutwardHemisphereExact=True,
        declaredReservedBytes=reserved,maximumReservedBytes=MAXIMUM_RESERVED_BYTES,
        sourceScanAttributesChanged=False,geometryChanged=False,uvChanged=False,contactsApproved=False,rootsApproved=False,
        scope="Shading-only connected soil fans with explicit 60-degree creases, exact area sums and encoded outward hemispheres; original scan boundary continuity and real terrain contact remain review gates")
    check()
    return NormalLayout(normals,evidence)

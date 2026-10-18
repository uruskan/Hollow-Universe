"""Root-run, geometry-only CPU views of an authenticated refused cliff closure.

One 640px view per fresh destination. No triangulator retry, asset export, source
edit, texture loading or geometry acceptance. The authoring function is stopped
at its annulus call, before any search; its actual source/collar/guide coordinates
are captured in memory. Selected log witnesses are reclassified exactly against
that captured geometry before rendering. Original PBR files remain untouched.

Example (root serializes Blender; repeat with --view top and --view back):
  blender --background --factory-startup --python tools/blender/forest_contact_diagnostic.py -- \
    --package .artifacts/environment-ownership/forest-qualified-prototype-r2 \
    --recipe tools/blender/data/forest_contact_module.json \
    --failure-log .artifacts/environment-ownership/forest-contact-build-r7.stderr.log \
    --receipt-sha256 f8dca592e6590f555290c6a494af840e07d785de9650c9e406a703dd4c3c2219 \
    --view left-end --destination .artifacts/environment-ownership/contact-view-left-NEW

External process memory/time guard remains required. Cycles CPU, 2 threads,
8 samples, at most4 displayed failures,55s shared internal deadline. PNGs are
diagnostic observations, not PBR/gameplay/collision qualifications.
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
import sys

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
spec = importlib.util.spec_from_file_location("_forest_contact_view_module", HERE/"forest_contact_module.py")
module = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = module
spec.loader.exec_module(module)
quality,author,require = module.quality,module.author,module.require


class Captured(Exception):
    pass


def unit(vector):
    length = math.sqrt(sum(v*v for v in vector))
    require(math.isfinite(length) and length > 0,"Unusable diagnostic direction")
    return tuple(v/length for v in vector)


def midpoint(a,b):
    return tuple((a[i]+b[i])/2 for i in range(3))


def load(options):
    paths = [Path(p).resolve() for p in (options.package,options.recipe,options.failure_log,options.destination)]
    package,recipe_path,log_path,destination = paths
    require(all(p.is_relative_to(ROOT) for p in paths),"Diagnostic path escapes this project")
    require(destination.is_relative_to(ROOT/".artifacts") and not destination.exists() and destination.parent.is_dir(),
            "Diagnostic destination must be fresh under an existing .artifacts parent")
    require(log_path.is_relative_to(ROOT/".artifacts") and log_path.stat().st_size <= 256*1024,"Failure log is outside bounded artifacts")
    recipe_sha = author.sha256(recipe_path)
    recipe = module.validate_recipe(author.strict_json(recipe_path,64*1024,recipe_sha))
    require(options.receipt_sha256 == recipe["receiptSha256"],"Caller receipt pin differs from recipe")
    work = quality.Work(replace(quality.Limits(),maximum_topology_steps=3000000,seconds=55))
    source,physical,far = quality.read_package(ROOT,package,options.receipt_sha256,work)
    receipt = author.strict_json(package/"qualification-receipt.json",1024*1024,options.receipt_sha256)
    pins = {row["file"]:row for row in receipt["outputFiles"]}
    require(receipt["source"]["sha256"] == recipe["sourceSha256"] and pins["physical.json"]["sha256"] == recipe["physicalSha256"],
            "Diagnostic recipe source/physical provenance mismatch")
    cycle = module.outer_cycle(source,recipe["expectedOuterEdges"],work)
    arcs = module.pinned_arcs(cycle,physical,recipe["anchors"],work)
    captured = {}
    def intercept(inner,outer,positions,shared_work,clearance=None):
        require(shared_work is work and clearance is not None,"Unexpected authoring capture point")
        captured.update(inner=list(inner),outer=list(outer),positions=positions,clearance=clearance)
        raise Captured()
    original = module.triangulate_projected_annulus
    module.triangulate_projected_annulus = intercept
    try:
        try: module.construct_closure(cycle,arcs,source,recipe,work)
        except Captured: pass
    finally:
        module.triangulate_projected_annulus = original
    require(captured,"Authoring did not reach the expected pre-search geometry stage")
    raw = log_path.read_bytes()
    require(len(raw) <= 256*1024,"Failure log grew beyond its admission bound")
    text = raw.decode("utf-8-sig")
    start = text.find("{")
    require(start >= 0,"Failure log has no diagnostic object")
    def pairs(items):
        result = {}
        for key,value in items:
            require(key not in result,"Duplicate failure-log JSON key")
            result[key] = value
        return result
    def bad(value):
        raise author.QualificationError("Nonfinite failure-log number: "+value)
    failure = json.loads(text[start:],object_pairs_hook=pairs,parse_constant=bad)
    rows = failure.get("blockingTriangleWitnesses")
    require(type(rows) is list and 0 < len(rows) <= 8,"Expected at most8 bounded zipper failure witnesses")
    positions,clearance = captured["positions"],captured["clearance"]
    obstacles = {name:(mesh,exact) for name,mesh,exact in clearance.obstacles}
    witnesses = []
    for ordinal,row in enumerate(rows):
        ids = row["candidateVertices"]
        require(type(ids) is list and len(ids) == 3 and len(set(ids)) == 3 and
                all(type(v) is int and 0 <= v < len(positions) for v in ids),"Invalid witness candidate ids")
        require([list(positions[v]) for v in ids] == row["candidatePositions"],"Logged candidate positions differ from captured authoring geometry")
        require(row["obstacle"] in obstacles,"Unknown witness obstacle")
        obstacle,exact = obstacles[row["obstacle"]]
        face = row["obstacleTriangle"]
        require(type(face) is int and 0 <= face < len(obstacle.triangles),"Invalid witness obstacle face")
        require([list(p) for p in obstacle.points(face)] == row["obstaclePositions"],"Logged obstacle positions differ from authenticated geometry")
        hit = quality.classify_exact(tuple(clearance.exact[v] for v in ids),
            tuple(exact[v] for v in obstacle.triangles[face]),work)
        require(hit.kind == row["classification"] and hit.kind not in quality.ALLOWED,"Logged failure did not reproduce exactly")
        witnesses.append(dict(logWitness=ordinal,candidateVertices=ids,candidatePositions=row["candidatePositions"],
            obstacle=row["obstacle"],obstacleTriangle=face,obstaclePositions=row["obstaclePositions"],classification=hit.kind,
            recomputedIntersectionMetres=[[float(v/clearance.scale) for v in p] for p in hit.points]))
    selected = options.witnesses
    require(0 < len(selected) <= 4 and len(set(selected)) == len(selected) and all(0 <= i < len(witnesses) for i in selected),
            "Select one to four existing witnesses")
    captured.update(source=source,retained=obstacles["retained-closure"][0],cycle=cycle,work=work,witnesses=witnesses)
    evidence = dict(schemaVersion=1,status="diagnostic-only-unadmitted",assetAccepted=False,view=options.view,
        sourceReceiptSha256=options.receipt_sha256,sourceSha256=recipe["sourceSha256"],recipeSha256=recipe_sha,
        failureLogSha256=hashlib.sha256(raw).hexdigest(),authoringToolSha256=author.sha256(HERE/"forest_contact_module.py"),
        diagnosticToolSha256=author.sha256(Path(__file__)),geometryStage="captured before annulus search; no candidate export",
        sourceBoundsMetres=dict(minimum=[min(p[i] for p in source.positions) for i in range(3)],maximum=[max(p[i] for p in source.positions) for i in range(3)]),
        witnesses=witnesses,displayedWitnesses=selected,originalPbrFilesUntouched=True,
        renderer="Cycles CPU,2 threads,8 samples,640x640; flat diagnostic materials; not a PBR or engine capture")
    return destination,captured,evidence


def outward_evidence(captured,evidence):
    source,retained,positions = captured["source"],captured["retained"],captured["positions"]
    inner,cycle = captured["inner"],captured["cycle"]
    source_edges = set(zip(cycle,cycle[1:]+cycle[:1]))
    source_incident,collar_incident = {},{}
    for face,triangle in enumerate(source.triangles):
        captured["work"].take("topology_steps")
        points = source.points(face)
        for a,b in zip(points,points[1:]+points[:1]):
            if (a,b) in source_edges: source_incident[(a,b)] = (face,points)
    collar_edges = set(zip(inner,inner[1:]+inner[:1]))
    for face,triangle in enumerate(retained.triangles):
        captured["work"].take("topology_steps")
        for a,b in zip(triangle,triangle[1:]+triangle[:1]):
            if (a,b) in collar_edges: collar_incident[(a,b)] = (face,retained.points(face))
    chosen = set()
    for ordinal in evidence["displayedWitnesses"]:
        ids = set(evidence["witnesses"][ordinal]["candidateVertices"])
        for i,(a,b) in enumerate(zip(inner,inner[1:]+inner[:1])):
            if a in ids and b in ids: chosen.update(((i-1)%len(inner),i,(i+1)%len(inner)))
    require(0 < len(chosen) <= 12,"Diagnostic collar-edge selection exceeds12")
    rows = []
    for i in sorted(chosen):
        j = (i+1)%len(inner)
        a,b = inner[i],inner[j]
        face,points = collar_incident[(a,b)]
        collar_normal = unit(quality.normal(points))
        outgoing = unit(quality.cross(quality.sub(positions[b],positions[a]),collar_normal))
        original_face,original_points = source_incident[(cycle[i],cycle[j])]
        original_normal = unit(quality.normal(original_points))
        original_out = unit(quality.cross(quality.sub(cycle[j],cycle[i]),original_normal))
        candidates = []
        for ordinal in evidence["displayedWitnesses"]:
            row = evidence["witnesses"][ordinal]
            ids = row["candidateVertices"]
            if a not in ids or b not in ids: continue
            other = next(v for v in ids if v not in (a,b))
            ea,eb,eq = (tuple(Fraction(v) for v in positions[k]) for k in (a,b,other))
            en = quality.normal(tuple(tuple(Fraction(v) for v in p) for p in points))
            sign = quality.dot(quality.cross(quality.sub(eb,ea),quality.sub(eq,ea)),en)
            candidates.append(dict(logWitness=ordinal,guideVertex=other,guidePosition=positions[other],
                guideDisplacementMetres=quality.sub(positions[other],midpoint(positions[a],positions[b])),
                outwardComponentMetres=quality.dot(quality.sub(positions[other],midpoint(positions[a],positions[b])),outgoing),
                exactRetainedFaceSideSign=str(sign),locallyExterior=sign < 0,
                scope="One incident collar-face half-space only; other source/collar faces still require exact intersection checks"))
        rows.append(dict(collarEdge=[a,b],sourceEdge=[cycle[i],cycle[j]],sourceFace=original_face,retainedFace=face,
            sourceMidpoint=midpoint(cycle[i],cycle[j]),sourceOutwardConormal=original_out,
            collarMidpoint=midpoint(positions[a],positions[b]),collarOutwardConormal=outgoing,
            collarGeometricNormal=collar_normal,candidates=candidates))
    evidence["outwardDirections"] = rows


def render(options,destination,captured,evidence):
    import bpy
    from mathutils import Matrix,Vector
    cv = lambda p: Vector((p[0],-p[2],p[1]))
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 8
    scene.cycles.use_denoising = False
    scene.cycles.max_bounces = 2
    scene.cycles.transparent_max_bounces = 8
    scene.render.threads_mode = "FIXED"
    scene.render.threads = 2
    scene.render.resolution_x = scene.render.resolution_y = 640
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.view_settings.view_transform = "Standard"
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs["Color"].default_value = (.16,.18,.22,1)
    scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = .5
    def material(name,color,emission=False,opacity=1.):
        result = bpy.data.materials.new(name)
        result.use_nodes = True
        nodes = result.node_tree.nodes
        nodes.clear()
        output = nodes.new("ShaderNodeOutputMaterial")
        shader = nodes.new("ShaderNodeEmission" if emission else "ShaderNodeBsdfPrincipled")
        shader.inputs["Color" if emission else "Base Color"].default_value = (*color,1)
        if not emission: shader.inputs["Roughness"].default_value = .8
        if opacity < 1:
            transparent,mix = nodes.new("ShaderNodeBsdfTransparent"),nodes.new("ShaderNodeMixShader")
            mix.inputs[0].default_value = opacity
            result.node_tree.links.new(transparent.outputs[0],mix.inputs[1])
            result.node_tree.links.new(shader.outputs[0],mix.inputs[2])
            shader = mix
        result.node_tree.links.new(shader.outputs[0],output.inputs["Surface"])
        return result
    grey = material("Authenticated scan",(.34,.40,.46))
    gold = material("Retained collar",(.74,.42,.10))
    red = material("Exact obstructing face",(.9,.06,.025))
    cyan = material("Outer guide",(.05,.8,1),True)
    orange = material("Collar boundary",(1,.52,.08),True)
    green = material("Original boundary/outward",(.18,1,.28),True)
    blue = material("Collar outgoing co-normal",(.08,.35,1),True)
    magenta = material("Rejected triangle wire",(1,.08,.8),True)
    ghost = material("Rejected triangle surface",(.9,.06,.7),True,.22)
    white = material("Recomputed intersection",(1,1,1),True)
    selected = [evidence["witnesses"][i] for i in evidence["displayedWitnesses"]]
    source_highlights = {r["obstacleTriangle"] for r in selected if r["obstacle"] == "source-near"}
    collar_highlights = {r["obstacleTriangle"] for r in selected if r["obstacle"] == "retained-closure"}
    def mesh_object(name,positions,triangles,base,highlights=()):
        geometry = bpy.data.meshes.new(name)
        geometry.from_pydata([cv(p) for p in positions],[],triangles)
        geometry.materials.append(base)
        geometry.materials.append(red)
        for polygon in geometry.polygons:
            polygon.material_index = 1 if polygon.index in highlights else 0
            polygon.use_smooth = False
        obj = bpy.data.objects.new(name,geometry)
        bpy.context.collection.objects.link(obj)
        return obj
    source,retained = captured["source"],captured["retained"]
    mesh_object("Original source",source.positions,source.triangles,grey,source_highlights)
    collar_faces = [(i,t) for i,t in enumerate(retained.triangles) if all(v < 2*len(captured["cycle"]) for v in t)]
    mesh_object("Original-to-collar strip",captured["positions"],[t for _,t in collar_faces],gold,
        {i for i,(face,_) in enumerate(collar_faces) if face in collar_highlights})
    def line(name,points,color,radius=.007,closed=False):
        curve = bpy.data.curves.new(name,"CURVE")
        curve.dimensions = "3D"
        curve.bevel_depth,curve.bevel_resolution = radius,0
        spline = curve.splines.new("POLY")
        spline.points.add(len(points)-1)
        for point,value in zip(spline.points,points): point.co = (*cv(value),1)
        spline.use_cyclic_u = closed
        curve.materials.append(color)
        obj = bpy.data.objects.new(name,curve)
        bpy.context.collection.objects.link(obj)
    line("Source rim",captured["cycle"],green,.006,True)
    for key,color in (("inner",orange),("outer",cyan)):
        line(key,[captured["positions"][i] for i in captured[key]],color,.008,True)
    for row in selected:
        name = "Rejected witness "+str(row["logWitness"])
        mesh_object(name,row["candidatePositions"],[(0,1,2)],ghost)
        line(name+" edges",row["candidatePositions"],magenta,.009,True)
        points = row["recomputedIntersectionMetres"]
        if len(points) >= 2: line(name+" intersection",points,white,.012,len(points)>2)
    def arrow(name,start,direction,color):
        finish = tuple(start[i]+.24*direction[i] for i in range(3))
        line(name,[start,finish],color,.006)
        vector = cv(direction)
        bpy.ops.mesh.primitive_cone_add(vertices=6,radius1=.021,radius2=0,depth=.055,
            location=cv(finish)-vector*.0275,rotation=vector.to_track_quat("Z","Y").to_euler())
        bpy.context.object.name = name+" head"
        bpy.context.object.data.materials.append(color)
    for row in evidence["outwardDirections"]:
        arrow("Source co-normal",row["sourceMidpoint"],row["sourceOutwardConormal"],green)
        arrow("Collar co-normal",row["collarMidpoint"],row["collarOutwardConormal"],blue)
    framing = [cv(p) for row in selected for key in ("candidatePositions","obstaclePositions") for p in row[key]]
    low = Vector(tuple(min(p[i] for p in framing) for i in range(3)))
    high = Vector(tuple(max(p[i] for p in framing) for i in range(3)))
    focus = (low+high)/2
    view_offset,up = {"left-end":((-1,0,0),(0,1,0)),"top":((0,1,0),(0,0,-1)),"back":((0,0,-1),(0,1,0))}[options.view]
    forward = -cv(view_offset).normalized()
    right = forward.cross(cv(up)).normalized()
    up_vector = right.cross(forward).normalized()
    scale = max(2.4,1.30*max(max(p.dot(axis) for p in framing)-min(p.dot(axis) for p in framing) for axis in (right,up_vector))+.6)
    data = bpy.data.cameras.new("Diagnostic camera")
    camera = bpy.data.objects.new("Diagnostic camera",data)
    bpy.context.collection.objects.link(camera)
    camera.location = focus-forward*12
    camera.rotation_euler = Matrix((right,up_vector,-forward)).transposed().to_euler()
    data.type,data.ortho_scale,data.clip_start,data.clip_end = "ORTHO",scale,.01,100
    scene.camera = camera
    for name,offset,energy in (("Key",Vector((-4,-3,5)),500),("Fill",Vector((2,1,3)),220)):
        light_data = bpy.data.lights.new(name,"AREA")
        light_data.energy,light_data.shape,light_data.size = energy,"DISK",5
        light = bpy.data.objects.new(name,light_data)
        bpy.context.collection.objects.link(light)
        light.location = focus+offset
        light.rotation_euler = (focus-light.location).to_track_quat("-Z","Y").to_euler()
    labels = ["UNADMITTED / "+options.view.upper(),"Grey scan | Gold collar | Cyan guide",
        "Magenta failed | Red obstacle | White crossing","Green source outward | Blue collar outward",
        "Geometry diagnostic only; coordinates and pins in JSON"]
    for i,label in enumerate(labels):
        text = bpy.data.curves.new("Legend "+str(i),"FONT")
        text.body,text.size = label,scale*.021
        text.materials.append(white)
        obj = bpy.data.objects.new("Legend "+str(i),text)
        bpy.context.collection.objects.link(obj)
        obj.parent = camera
        obj.location = (-scale*.47,scale*(.46-.031*i),-.8)
    evidence["camera"] = dict(godotViewOffset=view_offset,
        focusBlender=list(focus),orthographicScaleMetres=scale,view=options.view)
    destination.mkdir()
    png = destination/(options.view+".png")
    scene.render.filepath = str(png)
    captured["work"].check()
    bpy.ops.render.render(write_still=True)
    captured["work"].check()
    evidence["pngSha256"],evidence["workCounts"] = author.sha256(png),dict(captured["work"].counts)
    author.write_new(destination/"diagnostic.json",author.canonical(evidence))
    print("UNADMITTED_CONTACT_DIAGNOSTIC",str(png),flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package",required=True)
    parser.add_argument("--recipe",required=True)
    parser.add_argument("--failure-log",required=True)
    parser.add_argument("--receipt-sha256",required=True)
    parser.add_argument("--view",choices=("left-end","top","back"),required=True)
    parser.add_argument("--destination",required=True)
    parser.add_argument("--witnesses",type=int,nargs="+",default=[0,6])
    args = sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else sys.argv[1:]
    options = parser.parse_args(args)
    destination,captured,evidence = load(options)
    outward_evidence(captured,evidence)
    render(options,destination,captured,evidence)


if __name__ == "__main__":
    main()

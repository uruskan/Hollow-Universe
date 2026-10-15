"""Review pictures of the exported Iron Wake (docs/IRON_WAKE.md §9.1-§9.5 "As built"), rendered from
game/assets/ships/iron-wake.glb as Godot draws it: back faces culled (any hole shows), LOD0, the finishes approximated
in Eevee from their own .tres (textures, tint, scale; the shader's wear and rust layers are Godot's only), the
mechanisms posed from game/data/ground/batteries.json by the loaders' module, the rockets on their slots.

    blender -b --factory-startup --python art/import/iron_wake_review.py -- [out folder] [names...]

Names: hull-bow, hull-stern, siege-breech, defense-breech, steel-paint (all by default), written as fix-<name>.jpg;
close-bow and close-stern (the closures, §9.1 "As built: the bow and the stern closed"), written as <name>.jpg.
"""
import importlib.util
import json
import math
import re
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
GAME = REPO / "game"
GLB = GAME / "assets" / "ships" / "iron-wake.glb"
ROCKETS = ("rocket", "rocket-incendiary", "rocket-hv")


def g2b(v):
    """Game axes (+X port, +Y up, +Z the nose) as Blender's after the glTF import."""
    return Vector((v[0], -v[2], v[1]))


def loaders():
    spec = importlib.util.spec_from_file_location("iron_wake_loaders", REPO / "art" / "ships" / "iron_wake_loaders.py")
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def res(path):
    return GAME / path.replace("res://", "")


def tres(path):
    """A material's .tres as {key: value text} and {id: resource path}."""
    text = Path(path).read_text(encoding="utf-8")
    ext = dict(re.findall(r'\[ext_resource type="\w+" path="([^"]+)" id="(\w+)"\]', text))
    ids = {v: k for k, v in ext.items()}
    values = dict(re.findall(r"^([\w/]+) = (.+)$", text, re.M))
    return values, ids


def colour(text):
    nums = [float(x) for x in re.findall(r"[-\d.]+", text)]
    return [(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4) for c in nums[:3]] + [nums[3] if len(nums) > 3 else 1.0]


def image(path, colour_data):
    img = bpy.data.images.load(str(path), check_existing=True)
    img.colorspace_settings.name = "sRGB" if colour_data else "Non-Color"
    return img


def finish_material(m, tres_path):
    """An Eevee approximation of a finish: albedo (tinted where its paint layer is), ORM, normal, the contact
    occlusion in the vertex colour's red; back faces culled."""
    values, ids = tres(tres_path)
    m.use_nodes = True
    m.use_backface_culling = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    nt.links.new(bsdf.outputs[0], out.inputs["Surface"])
    if "shader" not in values:
        # A standard material: its colour, roughness, metalness and emission.
        c = colour(values.get("albedo_color", "Color(0.5, 0.5, 0.5, 1)"))
        bsdf.inputs["Base Color"].default_value = c
        bsdf.inputs["Roughness"].default_value = float(values.get("roughness", 0.5))
        bsdf.inputs["Metallic"].default_value = float(values.get("metallic", 0.0))
        if "albedo_texture" in values:
            t = nt.nodes.new("ShaderNodeTexImage")
            t.image = image(res(ids[re.search(r'"(\w+)"', values["albedo_texture"]).group(1)]), True)
            nt.links.new(t.outputs["Color"], bsdf.inputs["Base Color"])
        if values.get("transparency") == "1":
            bsdf.inputs["Alpha"].default_value = c[3]
            m.surface_render_method = "BLENDED"
        if values.get("emission_enabled") == "true" and float(values.get("emission_energy_multiplier", "1")) > 0:
            bsdf.inputs["Emission Color"].default_value = colour(values.get("emission", "Color(1, 1, 1, 1)"))
            bsdf.inputs["Emission Strength"].default_value = float(values.get("emission_energy_multiplier", 1)) * 2
        return
    tex = {k.split("/")[1]: res(ids[re.search(r'"(\w+)"', v).group(1)])
           for k, v in values.items() if k.startswith("shader_parameter/") and "ExtResource" in v}
    num = {k.split("/")[1]: v for k, v in values.items() if k.startswith("shader_parameter/")}
    uvmap = nt.nodes.new("ShaderNodeUVMap")
    scale = nt.nodes.new("ShaderNodeVectorMath")
    scale.operation = "SCALE"
    scale.inputs["Scale"].default_value = float(num.get("uv_scale", "1"))
    nt.links.new(uvmap.outputs["UV"], scale.inputs[0])

    def sample(key, colour_data):
        t = nt.nodes.new("ShaderNodeTexImage")
        t.image = image(tex[key], colour_data)
        nt.links.new(scale.outputs["Vector"], t.inputs["Vector"])
        return t
    alb, orm, nrm = sample("albedo_tex", True), sample("orm_tex", False), sample("normal_tex", False)
    tint = colour(num.get("tint", "Color(1, 1, 1, 1)"))
    tinted = nt.nodes.new("ShaderNodeMix")
    tinted.data_type = "RGBA"
    tinted.blend_type = "MULTIPLY"
    tinted.inputs["B"].default_value = tint
    nt.links.new(alb.outputs["Color"], tinted.inputs["A"])
    if float(num.get("painted", "0")) > 0.5:
        nt.links.new(alb.outputs["Alpha"], tinted.inputs["Factor"])
    else:
        tinted.inputs["Factor"].default_value = 1.0
    attr = nt.nodes.new("ShaderNodeAttribute")
    attr.attribute_name = "Color"
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(attr.outputs["Color"], sep.inputs["Color"])
    ao = nt.nodes.new("ShaderNodeMapRange")
    ao.inputs["To Min"].default_value = 1.0 - float(num.get("contact", "0.42"))
    nt.links.new(sep.outputs["Red"], ao.inputs["Value"])
    shade = nt.nodes.new("ShaderNodeMix")
    shade.data_type = "RGBA"
    shade.blend_type = "MULTIPLY"
    shade.inputs["Factor"].default_value = 1.0
    nt.links.new(tinted.outputs["Result"], shade.inputs["A"])
    nt.links.new(ao.outputs["Result"], shade.inputs["B"])
    nt.links.new(shade.outputs["Result"], bsdf.inputs["Base Color"])
    sp = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(orm.outputs["Color"], sp.inputs["Color"])
    nt.links.new(sp.outputs["Green"], bsdf.inputs["Roughness"])
    nt.links.new(sp.outputs["Blue"], bsdf.inputs["Metallic"])
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nm.inputs["Strength"].default_value = float(num.get("normal_strength", "1"))
    nt.links.new(nrm.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])


def setup():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(GLB))
    imp = (GLB.parent / (GLB.name + ".import")).read_text(encoding="utf-8")
    paths = dict(re.findall(r'"(M_IronWake_\w+)": \{\s*"use_external/enabled": true,\s*"use_external/fallback_path": "([^"]+)"', imp))
    for m in bpy.data.materials:
        key = m.name.split(".")[0]
        if key in paths:
            finish_material(m, res(paths[key]))
    for o in bpy.data.objects:
        if o.type == "MESH":
            n = o.name.split(".")[0]
            o.hide_render = not n.endswith("_lod0") or "convcolonly" in n or "occonly" in n
    # The rockets: the catalogue's models on every slot, kinds in turn.
    meshes = []
    for key in ROCKETS:
        before = set(bpy.data.objects)
        bpy.ops.import_scene.gltf(filepath=str(GAME / "assets" / "items" / f"{key}.glb"))
        new = [o for o in bpy.data.objects if o not in before]
        src = next(o for o in new if o.type == "MESH" and o.name.lower().split(".")[0].endswith("_lod0"))
        me = src.data.copy()
        me.transform(src.matrix_world)
        # The catalogue's rocket materials are Godot's: an olive body with its band's colour here, by kind.
        mat = bpy.data.materials.new(f"review_{key}")
        mat.use_nodes = True
        band = {"rocket": (0.55, 0.45, 0.08, 1), "rocket-hv": (0.12, 0.22, 0.45, 1), "rocket-incendiary": (0.5, 0.08, 0.05, 1)}
        mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = band[key]
        mat.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.5
        me.materials.clear()
        me.materials.append(mat)
        meshes.append(me)
        for o in new:
            o.hide_render = True
    slots = sorted((o for o in bpy.data.objects if o.name.startswith("socket_") and "_Slot_" in o.name or
                    re.match(r"socket_Defense_\w+_Round_\d\d$", o.name)), key=lambda o: o.name)
    for i, s in enumerate(slots):
        r = bpy.data.objects.new(f"round_{s.name}", meshes[i % len(meshes)])
        bpy.context.scene.collection.objects.link(r)
        r.parent = s
        r.matrix_parent_inverse = Matrix.Identity(4)
        r.matrix_basis = Matrix.Identity(4)
    scene = bpy.context.scene
    engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items]
    scene.render.engine = "BLENDER_EEVEE" if "BLENDER_EEVEE" in engines else "BLENDER_EEVEE_NEXT"
    for attr, value in (("taa_render_samples", 64), ("use_raytracing", True), ("use_shadows", True), ("use_gtao", True)):
        if hasattr(scene.eevee, attr):
            setattr(scene.eevee, attr, value)
    world = bpy.data.worlds.new("review")
    scene.world = world
    world.use_nodes = True
    nt = world.node_tree
    sky = nt.nodes.new("ShaderNodeTexSky")
    nt.links.new(sky.outputs["Color"], nt.nodes["Background"].inputs["Color"])
    nt.nodes["Background"].inputs["Strength"].default_value = 0.35
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 4.0
    sun.data.angle = math.radians(1.0)
    sun.rotation_euler = (math.radians(52), math.radians(8), math.radians(140))
    scene.collection.objects.link(sun)
    scene.view_settings.view_transform = "AgX"
    scene.view_settings.look = "AgX - Medium High Contrast"
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.clip_start = 0.03
    cam.data.clip_end = 3000
    return cam


def pose(mod, table, station, kind, elevation, t, loader=0):
    """Poses a station's pitch and one loader at cycle time t from the table (the others at rest)."""
    objs = {o.name.split(".")[0]: o for o in bpy.data.objects}
    rest = pose.rest = getattr(pose, "rest", {})
    for name, o in objs.items():
        if o.type == "EMPTY" and name not in rest:
            rest[name] = o.matrix_basis.copy()
    pitch = objs[f"{station}_Pitch"]
    pitch.matrix_basis = rest[pitch.name.split(".")[0]] @ Matrix.Rotation(math.radians(elevation), 4, Vector((-1, 0, 0)))
    bat = table["batteries"][kind]
    data = json.loads((GAME / "data" / "ground" / "ships" / "iron-wake.json").read_text(encoding="utf-8"))
    st = next(s for s in data["stations"] if s["id"] == station)
    side_of = (lambda k: 1 if st["barrels"][k]["mountSide"] == "port" else -1) if kind == "siege" else \
        (lambda k: 1 if st["side"] == "port" else -1)
    values, shown = mod.evaluate(bat, t, station, f"{loader:02d}", side_of(loader))
    for node, v in values.items():
        o = objs.get(node)
        if o is None:
            continue
        tr = Vector((0.0, 0.0, 0.0))
        rot = Matrix.Identity(4)
        for (k, axis), value in v.items():
            if k == "translate":
                tr += g2b(axis) * value
            else:
                rot = rot @ Matrix.Rotation(math.radians(value), 4, g2b(axis))
        o.matrix_basis = rest[node] @ Matrix.Translation(tr) @ rot
    for node, visible in shown.items():
        o = objs.get(node)
        if o is None:
            continue
        for c in o.children_recursive:
            if c.type == "MESH" and (c.name.split(".")[0].endswith("_lod0") or c.name.startswith("round_")):
                c.hide_render = not visible
    bpy.context.view_layer.update()


def aim(cam, at, offset, lens):
    cam.location = at + offset
    cam.rotation_euler = (at - cam.location).normalized().to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = lens


def render(path, size=(1600, 1000)):
    scene = bpy.context.scene
    scene.render.resolution_x, scene.render.resolution_y = size
    scene.render.image_settings.file_format = "JPEG"
    scene.render.image_settings.quality = 90
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
    print("review: wrote", path)


def main():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    kinds = ("hull", "siege", "defense", "steel", "close")
    out = Path(a[0]) if a and not a[0].startswith(kinds) else REPO / "docs" / "images" / "iron-wake"
    names = [n for n in a if n.startswith(kinds)] or \
        ["hull-bow", "hull-stern", "siege-breech", "defense-breech", "steel-paint"]
    cam = setup()
    mod = loaders()
    table = mod.load_table()
    objs = {o.name.split(".")[0]: o for o in bpy.data.objects}

    def world(name):
        return objs[name].matrix_world.translation.copy()
    for name in names:
        if name == "hull-bow":
            aim(cam, Vector((0, 0, 7)), Vector((24, -52, 10)), 40)
        elif name == "hull-stern":
            aim(cam, Vector((0, 4, 6)), Vector((-26, 50, 7)), 40)
        elif name == "siege-breech":
            phases = table["batteries"]["siege"]["loader"]["phases"]          # ... present, ram (index 5)
            t = sum(p["seconds"] for p in phases[:5]) + 0.6 * phases[5]["seconds"]
            pose(mod, table, "Siege_Forward", "siege", 10.0, t, loader=0)
            # From outboard, level with the recoil space: the open breech, the tray across the gap, the round on it.
            at = world("Siege_Forward_BreechRing_00") + Vector((0.0, 0.5, -0.05))
            aim(cam, at, Vector((-2.7, 0.55, 0.75)), 28)
        elif name == "defense-breech":
            phases = table["batteries"]["defense"]["loader"]["phases"]
            t = sum(p["seconds"] for p in phases[:4]) + 0.55 * phases[4]["seconds"]
            pose(mod, table, "Defense_F_P", "defense", 8.0, t)
            at = world("Defense_F_P_Ram") + Vector((0, -0.45, 0.0))
            aim(cam, at, Vector((1.5, 1.7, 1.4)), 30)
        elif name == "close-bow":
            # The keel lance's embrasure under the bow's front plate, from ahead and below.
            aim(cam, Vector((0, -21.0, 2.7)), Vector((2.6, -9.5, -1.6)), 30)
        elif name == "close-stern":
            # The bulkhead under the ramp's hinge, from behind and below.
            aim(cam, Vector((0, 17.5, 2.7)), Vector((-2.4, 8.5, -1.6)), 30)
        elif name == "steel-paint":
            pose(mod, table, "Siege_Forward", "siege", 10.0, None)
            # The forward siege battery's starboard side: painted armour cheek, the gun house's steel, a trunnion cover.
            at = world("Siege_Forward_Yaw") + Vector((-1.6, 0.4, 1.0))
            aim(cam, at, Vector((-2.6, -1.6, 0.7)), 32)
        render(out / (f"{name}.jpg" if name.startswith("close") else f"fix-{name}.jpg"))


main()

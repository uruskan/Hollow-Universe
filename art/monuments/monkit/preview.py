"""Look-dev renders of monuments in Blender (EEVEE): a physical sky and sun, open ground with the monument's pad,
a person and a Tin Finch for scale, materials drawn as `shaders/monument.gdshader` draws them in the game.

    blender -b --factory-startup --python art/monuments/build.py -- grid-substation --preview docs/images/monuments
"""
import math
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector

from . import log
from .surfaces import MATERIALS, texture_paths


def reset_scene():
    """An empty scene; the kit's built pieces go with it."""
    from . import kit
    bpy.ops.wm.read_factory_settings(use_empty=True)
    kit.reset()


def setup_render(width=1600, height=900, samples=48):
    sc = bpy.context.scene
    engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items]
    sc.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in engines else "BLENDER_EEVEE"
    sc.render.resolution_x, sc.render.resolution_y = width, height
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = "PNG"
    sc.render.film_transparent = False
    ee = sc.eevee
    for attr, value in (("taa_render_samples", samples), ("use_raytracing", True), ("use_shadows", True),
                        ("shadow_ray_count", 2), ("shadow_step_count", 8), ("use_fast_gi", True),
                        ("fast_gi_distance", 6.0), ("fast_gi_method", "GLOBAL_ILLUMINATION"),
                        ("shadow_resolution_scale", 1.0)):
        if hasattr(ee, attr):
            try:
                setattr(ee, attr, value)
            except Exception:  # noqa: BLE001 - option names move between EEVEE versions
                pass
    try:
        sc.view_settings.view_transform = "AgX"
        sc.view_settings.look = "AgX - Medium High Contrast"
    except TypeError:
        pass
    sc.view_settings.exposure = -0.7


def setup_sky(sun_elevation=28.0, sun_heading=215.0, strength=0.45, haze=1.0, fog=0.0):
    """A physical sky and a sun at `sun_elevation` degrees above the horizon, coming from `sun_heading` degrees
    (0 = from +Y, 90 = from +X)."""
    sc = bpy.context.scene
    world = bpy.data.worlds.new("sky")
    sc.world = world
    world.use_nodes = True
    nt = world.node_tree
    nt.nodes.clear()
    sky = nt.nodes.new("ShaderNodeTexSky")
    types = [e.identifier for e in sky.bl_rna.properties["sky_type"].enum_items]
    for t in ("MULTIPLE_SCATTERING", "NISHITA", "HOSEK_WILKIE"):
        if t in types:
            sky.sky_type = t
            break
    if hasattr(sky, "sun_elevation"):
        sky.sun_elevation = math.radians(sun_elevation)
        sky.sun_rotation = math.radians(sun_heading)
        if hasattr(sky, "air_density"):
            sky.air_density = 1.0
            if hasattr(sky, "aerosol_density"):
                sky.aerosol_density = 1.0 + haze
            elif hasattr(sky, "dust_density"):
                sky.dust_density = 1.0 + haze
        if hasattr(sky, "sun_disc"):
            sky.sun_disc = False
    bg = nt.nodes.new("ShaderNodeBackground")
    bg.inputs["Strength"].default_value = strength
    out = nt.nodes.new("ShaderNodeOutputWorld")
    nt.links.new(sky.outputs["Color"], bg.inputs["Color"])
    nt.links.new(bg.outputs["Background"], out.inputs["Surface"])
    FOG["density"] = fog
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 4.2
    sun.data.angle = math.radians(0.6)
    sun.data.color = (1.0, 0.95, 0.88)
    el, hd = math.radians(sun_elevation), math.radians(sun_heading)
    d = Vector((math.sin(hd) * math.cos(el), math.cos(hd) * math.cos(el), math.sin(el)))
    sun.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    sc.collection.objects.link(sun)
    return sun


FOG = {"density": 0.0, "colour": (0.60, 0.69, 0.82, 1.0), "strength": 1.0}


def fogged(nt, shader_out, out_node):
    """Mixes a material's surface shader toward the air's colour with the distance from the camera (aerial
    perspective, as the game's shader does with the planet's air), when FOG density is set."""
    if FOG["density"] <= 0:
        nt.links.new(shader_out, out_node.inputs[0])
        return
    cam = nt.nodes.new("ShaderNodeCameraData")
    k = nt.nodes.new("ShaderNodeMath")
    k.operation = "MULTIPLY"
    k.inputs[1].default_value = -FOG["density"]
    nt.links.new(cam.outputs["View Distance"], k.inputs[0])
    e = nt.nodes.new("ShaderNodeMath")
    e.operation = "EXPONENT"
    nt.links.new(k.outputs[0], e.inputs[0])
    inv = nt.nodes.new("ShaderNodeMath")
    inv.operation = "SUBTRACT"
    inv.inputs[0].default_value = 1.0
    nt.links.new(e.outputs[0], inv.inputs[1])
    emit = nt.nodes.new("ShaderNodeEmission")
    emit.inputs["Color"].default_value = FOG["colour"]
    emit.inputs["Strength"].default_value = FOG["strength"]
    mix = nt.nodes.new("ShaderNodeMixShader")
    nt.links.new(inv.outputs[0], mix.inputs[0])
    nt.links.new(shader_out, mix.inputs[1])
    nt.links.new(emit.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], out_node.inputs[0])


def _img(path, colour):
    im = bpy.data.images.load(str(path), check_existing=True)
    im.colorspace_settings.name = "sRGB" if colour else "Non-Color"
    if colour:
        im.alpha_mode = "CHANNEL_PACKED"
    return im


def surface_material(name):
    """Builds Blender material `name` (a monument surface) as the game shader draws it."""
    set_name, tile, opts = MATERIALS[name]
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    fogged(nt, bsdf.outputs[0], out)
    col = nt.nodes.new("ShaderNodeVertexColor")
    col.layer_name = "Col"
    obj = nt.nodes.new("ShaderNodeObjectInfo")
    tint = nt.nodes.new("ShaderNodeMix")
    tint.data_type = "RGBA"
    tint.blend_type = "MULTIPLY"
    tint.inputs[0].default_value = 1.0
    nt.links.new(col.outputs["Color"], tint.inputs[6])
    nt.links.new(obj.outputs["Color"], tint.inputs[7])
    from .builder import WEAR
    texco = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(texco.outputs["Object"], sep.inputs[0])
    # Ground grime: within 1.8 m of the ground, more on worn parts.
    near = nt.nodes.new("ShaderNodeMapRange")
    near.inputs["From Min"].default_value = 0.0
    near.inputs["From Max"].default_value = 1.8
    near.inputs["To Min"].default_value = 1.0
    near.inputs["To Max"].default_value = 0.0
    nt.links.new(sep.outputs["Z"], near.inputs["Value"])
    wear = nt.nodes.new("ShaderNodeMath")
    wear.operation = "MULTIPLY_ADD"
    wear.inputs[0].default_value = WEAR.get(name, 0.4)
    wear.inputs[1].default_value = 0.55
    wear.inputs[2].default_value = 0.2
    grime = nt.nodes.new("ShaderNodeMath")
    grime.operation = "MULTIPLY"
    nt.links.new(near.outputs["Result"], grime.inputs[0])
    nt.links.new(wear.outputs["Value"], grime.inputs[1])
    if opts.get("glass"):
        bsdf.inputs["Base Color"].default_value = (0.03, 0.035, 0.04, 1)
        bsdf.inputs["Roughness"].default_value = 0.08
        bsdf.inputs["Specular IOR Level"].default_value = 0.9
        return mat
    if opts.get("plain"):
        base = nt.nodes.new("ShaderNodeMix")
        base.data_type = "RGBA"
        base.blend_type = "MULTIPLY"
        base.inputs[0].default_value = 1.0
        base.inputs[6].default_value = (0.6, 0.6, 0.6, 1)
        nt.links.new(tint.outputs[2], base.inputs[7])
        aoc = nt.nodes.new("ShaderNodeCombineColor")
        for i in range(3):
            nt.links.new(col.outputs["Alpha"], aoc.inputs[i])
        shade = nt.nodes.new("ShaderNodeMix")
        shade.data_type = "RGBA"
        shade.blend_type = "MULTIPLY"
        shade.inputs[0].default_value = 1.0
        nt.links.new(base.outputs[2], shade.inputs[6])
        nt.links.new(aoc.outputs[0], shade.inputs[7])
        nt.links.new(shade.outputs[2], bsdf.inputs["Base Color"])
        bsdf.inputs["Roughness"].default_value = opts["plain"]
        return mat
    if opts.get("lamp"):
        emit = nt.nodes.new("ShaderNodeEmission")
        emit.inputs["Strength"].default_value = 6.0
        nt.links.new(tint.outputs[2], emit.inputs["Color"])
        nt.links.new(emit.outputs[0], out.inputs[0])
        return mat
    albedo_p, orm_p, normal_p = texture_paths(set_name)
    uv = nt.nodes.new("ShaderNodeUVMap")
    uv.uv_map = "UVMap"
    scale = nt.nodes.new("ShaderNodeVectorMath")
    scale.operation = "SCALE"
    scale.inputs["Scale"].default_value = 1.0 / tile
    nt.links.new(uv.outputs["UV"], scale.inputs[0])
    texs = []
    for p, colour in ((albedo_p, True), (orm_p, False), (normal_p, False)):
        t = nt.nodes.new("ShaderNodeTexImage")
        t.image = _img(p, colour)
        t.interpolation = "Cubic" if colour else "Linear"
        nt.links.new(scale.outputs[0], t.inputs[0])
        texs.append(t)
    a, orm, nrm = texs
    painted = nt.nodes.new("ShaderNodeMix")
    painted.data_type = "RGBA"
    painted.blend_type = "MULTIPLY"
    nt.links.new(a.outputs["Alpha"], painted.inputs[0])
    nt.links.new(a.outputs["Color"], painted.inputs[6])
    nt.links.new(tint.outputs[2], painted.inputs[7])
    if opts.get("alpha"):
        # The grate: its mask is coverage, not paint.
        painted.inputs[0].default_value = 0.0
        nt.links.remove(painted.inputs[0].links[0])
        clip = nt.nodes.new("ShaderNodeMath")
        clip.operation = "GREATER_THAN"
        clip.inputs[1].default_value = 0.5
        nt.links.new(a.outputs["Alpha"], clip.inputs[0])
        nt.links.new(clip.outputs[0], bsdf.inputs["Alpha"])
        if hasattr(mat, "surface_render_method"):
            mat.surface_render_method = "DITHERED"
    dirt = nt.nodes.new("ShaderNodeMix")
    dirt.data_type = "RGBA"
    dirt.blend_type = "MIX"
    dirt.inputs[7].default_value = (0.22, 0.19, 0.15, 1)
    nt.links.new(grime.outputs[0], dirt.inputs[0])
    nt.links.new(painted.outputs[2], dirt.inputs[6])
    sep_orm = nt.nodes.new("ShaderNodeSeparateColor")
    nt.links.new(orm.outputs["Color"], sep_orm.inputs[0])
    ao = nt.nodes.new("ShaderNodeMath")
    ao.operation = "MULTIPLY"
    nt.links.new(col.outputs["Alpha"], ao.inputs[0])
    nt.links.new(sep_orm.outputs[0], ao.inputs[1])
    shade = nt.nodes.new("ShaderNodeMix")
    shade.data_type = "RGBA"
    shade.blend_type = "MULTIPLY"
    shade.inputs[0].default_value = 1.0
    nt.links.new(dirt.outputs[2], shade.inputs[6])
    aoc = nt.nodes.new("ShaderNodeCombineColor")
    for i in range(3):
        nt.links.new(ao.outputs[0], aoc.inputs[i])
    nt.links.new(aoc.outputs[0], shade.inputs[7])
    nt.links.new(shade.outputs[2], bsdf.inputs["Base Color"])
    rough = nt.nodes.new("ShaderNodeMath")
    rough.operation = "ADD"
    nt.links.new(sep_orm.outputs[1], rough.inputs[0])
    gr = nt.nodes.new("ShaderNodeMath")
    gr.operation = "MULTIPLY"
    gr.inputs[1].default_value = 0.25
    nt.links.new(grime.outputs[0], gr.inputs[0])
    nt.links.new(gr.outputs[0], rough.inputs[1])
    rough.use_clamp = True
    nt.links.new(rough.outputs[0], bsdf.inputs["Roughness"])
    nt.links.new(sep_orm.outputs[2], bsdf.inputs["Metallic"])
    nmap = nt.nodes.new("ShaderNodeNormalMap")
    nmap.uv_map = "UVMap"
    nt.links.new(nrm.outputs["Color"], nmap.inputs["Color"])
    nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
    return mat


def ground(size=6000.0, pad=None, pad_colour=(0.42, 0.40, 0.36)):
    """Open meadow ground (a tinted noise), and the monument's pad on it."""
    bpy.ops.mesh.primitive_plane_add(size=size, location=(0, 0, -0.02))
    g = bpy.context.active_object
    g.name = "ground"
    mat = bpy.data.materials.new("preview_ground")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    tc = nt.nodes.new("ShaderNodeTexCoord")
    n1 = nt.nodes.new("ShaderNodeTexNoise")
    n1.inputs["Scale"].default_value = 0.02
    n1.inputs["Detail"].default_value = 8
    nt.links.new(tc.outputs["Object"], n1.inputs["Vector"])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (0.05, 0.09, 0.025, 1)
    ramp.color_ramp.elements[1].color = (0.14, 0.18, 0.05, 1)
    nt.links.new(n1.outputs["Fac"], ramp.inputs["Fac"])
    n2 = nt.nodes.new("ShaderNodeTexNoise")
    n2.inputs["Scale"].default_value = 1.5
    n2.inputs["Detail"].default_value = 6
    nt.links.new(tc.outputs["Object"], n2.inputs["Vector"])
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    mix.blend_type = "MULTIPLY"
    mix.inputs[0].default_value = 0.6
    nt.links.new(ramp.outputs["Color"], mix.inputs[6])
    nt.links.new(n2.outputs["Color"], mix.inputs[7])
    nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.95
    fogged(nt, bsdf.outputs[0], nt.nodes["Material Output"])
    g.data.materials.append(mat)
    if pad:
        _pad_mesh(pad, pad_colour)
    return g


def _pad_mesh(pad, colour):
    shape = pad["shape"]
    if shape[0] == "rect":
        w, d = shape[1], shape[2]
        pts = [(-w / 2, -d / 2), (w / 2, -d / 2), (w / 2, d / 2), (-w / 2, d / 2)]
    elif shape[0] == "circle":
        r = shape[1]
        pts = [(r * math.cos(a), r * math.sin(a)) for a in np.linspace(0, 2 * math.pi, 48, endpoint=False)]
    else:
        pts = shape[1]
    me = bpy.data.meshes.new("pad")
    me.from_pydata([(x, y, -0.005) for x, y in pts], [], [list(range(len(pts)))])
    o = bpy.data.objects.new("pad", me)
    bpy.context.scene.collection.objects.link(o)
    mat = bpy.data.materials.new("preview_pad")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    tc = nt.nodes.new("ShaderNodeTexCoord")
    n = nt.nodes.new("ShaderNodeTexNoise")
    n.inputs["Scale"].default_value = 3.0
    n.inputs["Detail"].default_value = 10
    nt.links.new(tc.outputs["Object"], n.inputs["Vector"])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    c = colour
    ramp.color_ramp.elements[0].color = (c[0] * 0.6, c[1] * 0.6, c[2] * 0.6, 1)
    ramp.color_ramp.elements[1].color = (c[0], c[1], c[2], 1)
    nt.links.new(n.outputs["Fac"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.95
    fogged(nt, bsdf.outputs[0], nt.nodes["Material Output"])
    me.materials.append(mat)


def figure(at=(0, 0, 0), yaw=0.0, colour=(0.85, 0.35, 0.08)):
    """A 1.8 m person for scale."""
    mat = bpy.data.materials.get("preview_figure") or bpy.data.materials.new("preview_figure")
    mat.use_nodes = True
    mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*colour, 1)
    parts = []
    x, y, z = at
    for loc, size in (((0, 0, 1.1), (0.42, 0.24, 0.62)), ((0, 0, 0.42), (0.36, 0.2, 0.84))):
        bpy.ops.mesh.primitive_cube_add(size=1, location=(x + loc[0], y + loc[1], z + loc[2]))
        o = bpy.context.active_object
        o.scale = size
        parts.append(o)
    bpy.ops.mesh.primitive_uv_sphere_add(radius=0.12, location=(x, y, z + 1.62))
    parts.append(bpy.context.active_object)
    for o in parts:
        o.data.materials.append(mat)
        o.rotation_euler.z = math.radians(yaw)
    return parts


def tin_finch(at=(0, 0, 0), yaw=0.0):
    """The starter ship (game/assets/world/tin-finch.glb) for scale; its LOD0 only, plain grey."""
    from hollow import WORLD
    path = WORLD / "tin-finch.glb"
    if not path.exists():
        return None
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=str(path))
    new = [o for o in bpy.data.objects if o not in before]
    mat = bpy.data.materials.new("preview_ship")
    mat.use_nodes = True
    mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.55, 0.57, 0.6, 1)
    mat.node_tree.nodes["Principled BSDF"].inputs["Metallic"].default_value = 0.6
    for o in new:
        if o.type == "MESH" and ("lod1" in o.name.lower() or "lod2" in o.name.lower() or "hull" in o.name.lower()):
            o.hide_render = True
        elif o.type == "MESH":
            o.data.materials.clear()
            o.data.materials.append(mat)
    roots = [o for o in new if o.parent is None]
    for o in roots:
        o.location = Vector(at)
        o.rotation_euler.z = math.radians(yaw)
    return new


def camera(target, distance, yaw, pitch, lens=35.0, name="cam"):
    """A camera looking at `target` from `distance` metres, `yaw` degrees round from the front (-Y), `pitch` degrees
    down."""
    sc = bpy.context.scene
    cam = sc.camera
    if cam is None:
        cam = bpy.data.objects.new(name, bpy.data.cameras.new(name))
        sc.collection.objects.link(cam)
        sc.camera = cam
    cam.data.lens = lens
    cam.data.clip_start = 0.1
    cam.data.clip_end = 60000
    y, p = math.radians(yaw), math.radians(pitch)
    t = Vector(target)
    d = Vector((math.sin(y) * math.cos(p), -math.cos(y) * math.cos(p), math.sin(p)))
    cam.location = t + d * distance
    cam.rotation_euler = (t - cam.location).to_track_quat("-Z", "Y").to_euler()
    return cam


def render(path):
    sc = bpy.context.scene
    sc.render.filepath = str(path)
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.render.render(write_still=True)
    log(f"rendered {path}")


def stage(builder, ao=True, sun=(28.0, 215.0), people=(), ship=None, pad_colour=(0.20, 0.19, 0.17), fast=False,
          fog=0.0, ground_size=6000.0):
    """Puts `builder`'s monument (built after `reset_scene()`) on open ground under the sky with its materials;
    returns its objects."""
    from .builder import occlusion
    from hollow.export import set_export_colour
    if fast:
        setup_render(800, 450, 16)
    else:
        setup_render()
    setup_sky(*sun, fog=fog)
    ground(size=ground_size, pad=builder.pad, pad_colour=pad_colour)
    col = bpy.data.collections.new("monument")
    bpy.context.scene.collection.children.link(col)
    chunks, inst = builder.realise(col, 0)
    values = occlusion(chunks, chunks + inst, 16, 4.0, 0.0) if ao else {o: None for o in chunks}
    for o in chunks:
        me = o.data
        tint = np.empty(len(me.vertices) * 4, np.float32)
        me.color_attributes["Tint"].data.foreach_get("color", tint)
        tint = tint.reshape(-1, 4)
        if values[o] is not None:
            tint[:, 3] = values[o]
        set_export_colour(o, tint, "POINT")
    used = set()
    for o in chunks + inst:
        for m in o.data.materials:
            if m and m.name in MATERIALS:
                used.add(m.name)
    for name in used:
        surface_material(name)
    for p in people:
        figure(*p) if isinstance(p[0], (tuple, list)) else figure(p)
    if ship:
        tin_finch(*ship)
    return chunks, inst

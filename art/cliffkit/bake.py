"""A piece's own textures (docs/WORLD_TERRAIN_PLAN.md §4.3), baked in Cycles from its scan onto LOD0 with a fresh UV
layout: the albedo with the scan's ambient occlusion multiplied in, its alpha 1 where the scan covers the piece and 0
where it doesn't (the fracture surfaces and the skirt: the shader shows the family's detail there), and the
tangent-space normal map (OpenGL, as Godot reads it). 2K for pieces of 30 m or more, 1K below.
"""
import bpy
import numpy as np

from hollow import log
from hollow.textures import write_png

from . import shape as shaping

AO_STRENGTH = 0.85


def _image(name, size):
    img = bpy.data.images.new(name, size, size, alpha=True, float_buffer=True)
    img.colorspace_settings.name = "Non-Color"
    return img


def _pixels(img):
    w, h = img.size
    px = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(px)
    return px.reshape(h, w, 4)[::-1].copy()  # rows top to bottom


def _maps(material):
    """The scan material's albedo, packed occlusion-roughness-metal and normal images, found by what they feed."""
    albedo = orm = normal = None
    for node in material.node_tree.nodes:
        if node.type != "TEX_IMAGE" or node.image is None:
            continue
        targets = {(link.to_node.type, link.to_socket.name) for out in node.outputs for link in out.links}
        if ("BSDF_PRINCIPLED", "Base Color") in targets:
            albedo = node.image
        elif any(t[0] == "NORMAL_MAP" for t in targets):
            normal = node.image
        elif any(t[0] == "SEPARATE_COLOR" for t in targets):
            orm = node.image
    return albedo, orm, normal


def _emission(name, albedo=None, orm=None, white=False):
    """An emission material for baking: the albedo times the occlusion (red of the ORM map), or plain white."""
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    em = nt.nodes.new("ShaderNodeEmission")
    nt.links.new(em.outputs["Emission"], out.inputs["Surface"])
    if white:
        em.inputs["Color"].default_value = (1, 1, 1, 1)
        return mat
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = albedo
    colour = tex.outputs["Color"]
    if orm is not None:
        otex = nt.nodes.new("ShaderNodeTexImage")
        otex.image = orm
        sep = nt.nodes.new("ShaderNodeSeparateColor")
        nt.links.new(otex.outputs["Color"], sep.inputs["Color"])
        ao = nt.nodes.new("ShaderNodeMapRange")
        ao.inputs["To Min"].default_value = 1.0 - AO_STRENGTH
        nt.links.new(sep.outputs["Red"], ao.inputs["Value"])
        mul = nt.nodes.new("ShaderNodeVectorMath")
        mul.operation = "MULTIPLY"
        nt.links.new(colour, mul.inputs[0])
        nt.links.new(ao.outputs["Result"], mul.inputs[1])
        colour = mul.outputs["Vector"]
    nt.links.new(colour, em.inputs["Color"])
    return mat


def _target(lod0, img):
    """LOD0's bake material: one image node, active."""
    mat = bpy.data.materials.new(lod0.name + "_bake")
    mat.use_nodes = True
    node = mat.node_tree.nodes.new("ShaderNodeTexImage")
    node.image = img
    mat.node_tree.nodes.active = node
    lod0.data.materials.clear()
    lod0.data.materials.append(mat)


def _bake(kind, source, lod0, img, margin):
    _target(lod0, img)
    bpy.ops.object.select_all(action="DESELECT")
    source.select_set(True)
    lod0.select_set(True)
    bpy.context.view_layer.objects.active = lod0
    bpy.ops.object.bake(type=kind, use_selected_to_active=True, cage_extrusion=0.5, max_ray_distance=1.6,
                        margin=margin, margin_type="EXTEND", use_clear=True, target="IMAGE_TEXTURES",
                        normal_space="TANGENT", uv_layer="UVMap")
    return _pixels(img)


def _srgb(c):
    c = np.clip(c, 0.0, 1.0)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1 / 2.4) - 0.055)


def bake(shape, lod0, collection, out_dir):
    """Bakes LOD0's albedo (RGBA) and normal map from the scan; writes them as PNG; returns (albedo path, normal path,
    the scanned colour's mean, sRGB)."""
    key = shape.spec["key"]
    size = 2048 if shape.size >= 30.0 else 1024
    margin = 16 if size == 2048 else 8
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 4
    source = shaping.load_scan(shaping.spec_scan(shape.spec))
    for c in list(source.users_collection):
        c.objects.unlink(source)
    collection.objects.link(source)
    shaping.transform(source, shape.frame)
    shape.bend(source)
    original = source.data.materials[0]
    albedo_img, orm_img, _ = _maps(original)

    normal = _bake("NORMAL", source, lod0, _image(key + "_n", size), margin)
    source.data.materials[0] = _emission(key + "_albedo", albedo_img, orm_img)
    colour = _bake("EMIT", source, lod0, _image(key + "_a", size), margin)
    source.data.materials[0] = _emission(key + "_hit", white=True)
    hit = _bake("EMIT", source, lod0, _image(key + "_h", size), margin)[:, :, 0]

    covered = hit > 0.5
    rgb = colour[:, :, :3]
    mean_linear = rgb[covered].mean(axis=0) if covered.any() else np.array([0.3, 0.25, 0.2])
    rgb[~covered] = mean_linear
    albedo = np.empty(rgb.shape[:2] + (4,), np.float32)
    albedo[:, :, :3] = _srgb(rgb)
    albedo[:, :, 3] = np.clip(hit, 0.0, 1.0)
    nmap = normal[:, :, :3]
    nmap[~covered] = (0.5, 0.5, 1.0)
    out_dir.mkdir(parents=True, exist_ok=True)
    pa = write_png(out_dir / f"{key}_albedo.png", albedo, channels=4)
    pn = write_png(out_dir / f"{key}_normal.png", nmap, channels=3)
    bpy.data.objects.remove(source, do_unlink=True)
    lod0.data.materials.clear()
    log(f"{key}: baked {size} px, scan covers {covered.mean() * 100:.0f}% of the texture")
    return pa, pn, [float(v) for v in _srgb(mean_linear)]

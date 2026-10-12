"""Quick look-dev renders in Blender (Eevee), for checking a build before it reaches the client.

Materials are rebuilt from the exported textures by material name, roughly as the client draws them.
"""
import math
import os

import bpy
from mathutils import Vector

TEXTURES = {
    "skin": ("skin_male_albedo.png", None, 0.55),
    "eye": ("eye_albedo.png", None, 0.1),
    "brow": ("brow_albedo.png", None, 0.8),
    "lash": ("lash_albedo.png", None, 0.8),
    "hair": ("hair_albedo.png", "hair_normal.png", 0.6),
}


def _material(mat, folder):
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF") or nt.nodes.new("ShaderNodeBsdfPrincipled")
    out = nt.nodes.get("Material Output") or nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(bsdf.outputs[0], out.inputs[0])
    spec = TEXTURES.get(mat.name)
    colour = mat.get("preview_colour")
    if spec:
        albedo, normal, rough = spec
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = bpy.data.images.load(os.path.join(folder, "textures", albedo), check_existing=True)
        nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        if tex.image.channels == 4 and mat.name in ("brow", "lash", "hair", "eye"):
            nt.links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
            mat.surface_render_method = 'DITHERED'
        bsdf.inputs["Roughness"].default_value = rough
        if mat.name == "skin":
            bsdf.inputs["Subsurface Weight"].default_value = 0.15
            bsdf.inputs["Subsurface Radius"].default_value = (1.0, 0.35, 0.2)
            bsdf.inputs["Subsurface Scale"].default_value = 0.01
    elif colour:
        bsdf.inputs["Base Color"].default_value = tuple(colour) + (1.0,) if len(colour) == 3 else tuple(colour)
        bsdf.inputs["Roughness"].default_value = mat.get("preview_roughness", 0.85)
        bsdf.inputs["Metallic"].default_value = mat.get("preview_metallic", 0.0)
    else:
        bsdf.inputs["Base Color"].default_value = (0.35, 0.36, 0.38, 1)
        bsdf.inputs["Roughness"].default_value = 0.9


def render(path_prefix, objects, folder, views=((0, "front"), (35, "three_quarter"), (180, "back")), resolution=(700, 1000)):
    scene = bpy.context.scene
    for obj in objects:
        if obj.type == 'MESH':
            for mat in obj.data.materials:
                if mat:
                    _material(mat, folder)
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x, scene.render.resolution_y = resolution
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("preview") if scene.world is None else scene.world
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.36, 0.40, 0.46, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.9
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN'))
    sun.data.energy = 3.2
    sun.rotation_euler = (math.radians(50), 0, math.radians(-35))
    scene.collection.objects.link(sun)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    cam.data.lens = 70
    scene.collection.objects.link(cam)
    scene.camera = cam
    target = Vector((0, 0, 0.98))
    for yaw, name in views:
        r = math.radians(yaw)
        cam.location = target + Vector((math.sin(r) * -5.2, -math.cos(r) * 5.2, 0.25))
        direction = target - cam.location
        cam.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = "%s_%s.png" % (path_prefix, name)
        bpy.ops.render.render(write_still=True)
        print("[hollow] preview", scene.render.filepath, flush=True)

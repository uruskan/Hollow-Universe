"""Shared helpers for converting the PSG kits into the game's GLB files (GROUND.md §9).

Run by building_kit.py and door_kit.py inside Blender 5.2 in background mode. The kits are FBX in centimetres with
Blender's own axes (+Z up, a wall's outside toward -Y); everything here works in metres in those axes, and the glTF
exporter turns them into Godot's (+Y up, the outside toward +Z).
"""
import math
import os
import pathlib

import bpy
import mathutils

REPO = pathlib.Path(__file__).resolve().parents[2]
SCRATCH = pathlib.Path(os.environ.get("HOLLOW_ART_TEMP", REPO / ".tmp" / "art"))

# Godot's import suffix for convex collision that is not drawn.
COLLISION_SUFFIX = "-convcolonly"


def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1.0


def import_fbx(path):
    """Imports a kit FBX and returns its new objects with the centimetre scale applied (vertices in metres)."""
    before = {o.name for o in bpy.data.objects}
    bpy.ops.import_scene.fbx(filepath=str(path), axis_forward='-Y', axis_up='Z')
    new = [o.name for o in bpy.data.objects if o.name not in before]
    for name in new:
        o = bpy.data.objects[name]
        if o.type == 'EMPTY':
            bpy.data.objects.remove(o, do_unlink=True)
    objects = [bpy.data.objects[name] for name in new if name in bpy.data.objects]
    meshes = [o for o in objects if o.type == 'MESH' and not (o.parent and o.parent.type == 'ARMATURE')]
    for o in meshes:
        o.parent = None
        bake_transform(o)
    return objects


def bake_transform(o):
    """Applies an object's transform to its mesh, so its vertices are in metres about the object's origin."""
    o.data.transform(o.matrix_world)
    o.matrix_world = mathutils.Matrix.Identity(4)


def drop_lightmap_uvs(o):
    """The kits carry a second UV set for baked lighting; the game lights in real time, so it goes."""
    for uv in list(o.data.uv_layers)[1:]:
        o.data.uv_layers.remove(uv)


def bounds(o):
    corners = [o.matrix_world @ mathutils.Vector(c) for c in o.bound_box]
    return ([min(c[i] for c in corners) for i in range(3)], [max(c[i] for c in corners) for i in range(3)])


TEXTURES = REPO / "game" / "assets" / "building" / "textures"


def texture(source, name, size=2048, quality=90, colour=True):
    """A kit texture shrunk to at most `size` and saved as JPEG in game/assets/building/textures. The GLBs carry no
    images: the game builds its materials from these files, so each texture is stored once."""
    TEXTURES.mkdir(parents=True, exist_ok=True)
    out = TEXTURES / (name + ".jpg")
    if not out.exists():
        image = bpy.data.images.load(str(source), check_existing=False)
        if image.size[0] > size or image.size[1] > size:
            image.scale(min(size, image.size[0]), min(size, image.size[1]))
        scene = bpy.context.scene
        settings = scene.render.image_settings
        settings.file_format = 'JPEG'
        settings.color_mode = 'RGB'
        settings.quality = quality
        image.save_render(str(out), scene=scene)
        bpy.data.images.remove(image)
    image = bpy.data.images.load(str(out), check_existing=True)
    image.colorspace_settings.name = 'sRGB' if colour else 'Non-Color'
    return image


def gltf_output_group():
    """The node group Blender's glTF exporter reads ambient occlusion from."""
    group = bpy.data.node_groups.get("glTF Material Output")
    if group is None:
        group = bpy.data.node_groups.new("glTF Material Output", 'ShaderNodeTree')
        group.interface.new_socket(name="Occlusion", in_out='INPUT', socket_type='NodeSocketFloat')
    return group


def pbr_material(name, base_color, normal_gl, orm, normal_strength=0.85, prefix=None, normal_name=None):
    """A PBR material from a kit texture set: base colour, OpenGL normal map, ORM (occlusion, roughness, metallic).
    Its textures are saved as <prefix>_BaseColor, _NormalGL and _ORM (a shared normal map can be named instead)."""
    prefix = prefix or name
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    shader = nodes.get("Principled BSDF")
    base = nodes.new("ShaderNodeTexImage")
    base.image = texture(base_color, prefix + "_BaseColor", quality=90, colour=True)
    links.new(base.outputs["Color"], shader.inputs["Base Color"])
    normal_image = nodes.new("ShaderNodeTexImage")
    normal_image.image = texture(normal_gl, normal_name or prefix + "_NormalGL", quality=95, colour=False)
    normal = nodes.new("ShaderNodeNormalMap")
    normal.inputs["Strength"].default_value = normal_strength
    links.new(normal_image.outputs["Color"], normal.inputs["Color"])
    links.new(normal.outputs["Normal"], shader.inputs["Normal"])
    if orm is not None:
        orm_image = nodes.new("ShaderNodeTexImage")
        orm_image.image = texture(orm, prefix + "_ORM", quality=92, colour=False)
        split = nodes.new("ShaderNodeSeparateColor")
        links.new(orm_image.outputs["Color"], split.inputs["Color"])
        links.new(split.outputs["Green"], shader.inputs["Roughness"])
        links.new(split.outputs["Blue"], shader.inputs["Metallic"])
        occlusion = nodes.new("ShaderNodeGroup")
        occlusion.node_tree = gltf_output_group()
        links.new(split.outputs["Red"], occlusion.inputs["Occlusion"])
    else:
        shader.inputs["Roughness"].default_value = 0.8
    return material


def set_material(o, material):
    o.data.materials.clear()
    o.data.materials.append(material)


def collision(o, name):
    """Turns a kit's UCX hull into Godot's convex collision-only node."""
    o.name = name + COLLISION_SUFFIX
    o.data.name = o.name
    o.data.materials.clear()
    for uv in list(o.data.uv_layers):
        o.data.uv_layers.remove(uv)


def export_glb(path, animations=False):
    """Exports the whole scene as one GLB: meshes, collision and material slots (no images: see texture()), Y up."""
    path = pathlib.Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=str(path),
        export_format='GLB',
        export_yup=True,
        export_apply=True,
        export_texcoords=True,
        export_normals=True,
        export_tangents=False,
        export_materials='EXPORT',
        export_image_format='NONE',
        export_cameras=False,
        export_lights=False,
        export_extras=False,
        export_animations=animations,
        export_skins=animations,
        export_morph=False,
    )
    return path


def triangles(o):
    o.data.calc_loop_triangles()
    return len(o.data.loop_triangles)


def report(lines, path):
    pathlib.Path(path).parent.mkdir(parents=True, exist_ok=True)
    pathlib.Path(path).write_text("\n".join(lines) + "\n", encoding="utf-8")
    for line in lines:
        print(line, flush=True)


def rad(degrees):
    return math.radians(degrees)

"""Look-dev renders of a road vehicle built in this Blender session (not shipped): its atlas on Principled shaders with the
ORM and normal maps, the perspex, the lamps lit, a sun and a sky, a ground plane, and the views asked for (camera and target
in the vehicle's frame: +X left, +Y up, +Z forward). Used by art/models/togg.py and zamazingo.py's `--preview`; the pictures
go to .artifacts/itemart/<key>/preview-<view>.png."""
import math

import bpy
from mathutils import Vector


def _b(p):
    return Vector((p[0], -p[2], p[1]))


def _engine(scene):
    for name in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE', 'CYCLES'):
        try:
            scene.render.engine = name
            return name
        except TypeError:
            continue
    return scene.render.engine


def _principled(material):
    material.use_nodes = True
    nodes = material.node_tree.nodes
    return next(n for n in nodes if n.type == 'BSDF_PRINCIPLED'), nodes, material.node_tree.links


def _textured(material, albedo, orm=None, normal=None, emission=0.0):
    bsdf, nodes, links = _principled(material)
    uv = None
    tex = nodes.new('ShaderNodeTexImage')
    tex.image = albedo
    colour = tex.outputs['Color']
    # Vertex occlusion (the export colour) darkens the albedo, as the game's material does.
    attr = nodes.new('ShaderNodeVertexColor')
    attr.layer_name = 'Col'
    mix = nodes.new('ShaderNodeMix')
    mix.data_type = 'RGBA'
    mix.blend_type = 'MULTIPLY'
    mix.inputs['Factor'].default_value = 1.0
    links.new(colour, mix.inputs['A'])
    links.new(attr.outputs['Color'], mix.inputs['B'])
    links.new(mix.outputs['Result'], bsdf.inputs['Base Color'])
    if orm is not None:
        t = nodes.new('ShaderNodeTexImage')
        t.image = orm
        t.image.colorspace_settings.name = 'Non-Color'
        sep = nodes.new('ShaderNodeSeparateColor')
        links.new(t.outputs['Color'], sep.inputs['Color'])
        links.new(sep.outputs['Green'], bsdf.inputs['Roughness'])
        links.new(sep.outputs['Blue'], bsdf.inputs['Metallic'])
    if normal is not None:
        t = nodes.new('ShaderNodeTexImage')
        t.image = normal
        t.image.colorspace_settings.name = 'Non-Color'
        nm = nodes.new('ShaderNodeNormalMap')
        nm.inputs['Strength'].default_value = .6
        links.new(t.outputs['Color'], nm.inputs['Color'])
        links.new(nm.outputs['Normal'], bsdf.inputs['Normal'])
    if emission > 0:
        links.new(colour, bsdf.inputs['Emission Color'])
        bsdf.inputs['Emission Strength'].default_value = emission


def render(key, out, albedo_png, shown, views, size=(1600, 900), samples=48):
    scene = bpy.context.scene
    engine = _engine(scene)
    scene.render.resolution_x, scene.render.resolution_y = size
    scene.render.film_transparent = False
    if engine == 'CYCLES':
        scene.cycles.samples = samples
    try:
        scene.view_settings.view_transform = 'AgX'
    except TypeError:
        pass
    albedo = bpy.data.images.load(str(albedo_png))
    orm = bpy.data.images.load(str(albedo_png).replace('_albedo', '_orm'))
    normal = bpy.data.images.load(str(albedo_png).replace('_albedo', '_normal'))
    for material in bpy.data.materials:
        if material.name.endswith('_Lamps'):
            _textured(material, albedo, emission=2.0)
        elif material.name.endswith('_Glass'):
            bsdf, _, _ = _principled(material)
            bsdf.inputs['Base Color'].default_value = (.70, .78, .76, 1)
            bsdf.inputs['Alpha'].default_value = .26
            bsdf.inputs['Roughness'].default_value = .08
            try:
                material.surface_render_method = 'BLENDED'
            except (AttributeError, TypeError):
                pass
        elif material.name.startswith('M_Hollow_'):
            _textured(material, albedo, orm, normal)
    names = set(o.name for o in shown)
    for o in bpy.data.objects:
        if o.type == 'MESH':
            o.hide_render = o.name not in names
    bpy.ops.mesh.primitive_plane_add(size=80, location=(0, 0, 0))
    ground = bpy.context.object
    gm = bpy.data.materials.new('PreviewGround')
    bsdf, _, _ = _principled(gm)
    bsdf.inputs['Base Color'].default_value = (.22, .21, .17, 1)
    bsdf.inputs['Roughness'].default_value = .95
    ground.data.materials.append(gm)
    world = bpy.data.worlds.new('PreviewSky')
    scene.world = world
    world.use_nodes = True
    bg = next(n for n in world.node_tree.nodes if n.type == 'BACKGROUND')
    bg.inputs['Color'].default_value = (.55, .65, .80, 1)
    bg.inputs['Strength'].default_value = 1.0
    sun_data = bpy.data.lights.new('Sun', 'SUN')
    sun_data.energy = 4.0
    sun_data.angle = math.radians(2)
    sun = bpy.data.objects.new('Sun', sun_data)
    scene.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(48), math.radians(12), math.radians(-140))
    cam_data = bpy.data.cameras.new('Camera')
    cam_data.lens = 35
    cam_data.clip_start = .02
    cam = bpy.data.objects.new('Camera', cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    for name, (eye, target) in views.items():
        e, t = _b(eye), _b(target)
        cam.location = e
        cam.rotation_euler = (t - e).to_track_quat('-Z', 'Y').to_euler()
        ground.hide_render = name == 'under'
        scene.render.filepath = str(out / f'preview-{name}.png')
        bpy.ops.render.render(write_still=True)
        print(f'[preview] {scene.render.filepath}', flush=True)

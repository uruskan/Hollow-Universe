"""Look-dev renders of a vehicle model built in this Blender session (not shipped): the atlas on Principled shaders, a sun
and sky, a ground plane, three views of the craft and a lineup of its other modules. Used by art/models/ka_fa_1500.py's
`--preview`; the pictures go to .artifacts/itemart/<key>/."""
import math

import bpy
from mathutils import Vector


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


def _textured(material, image, alpha=False, emission=0.0):
    bsdf, nodes, links = _principled(material)
    tex = nodes.new('ShaderNodeTexImage')
    tex.image = image
    links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
    bsdf.inputs['Roughness'].default_value = .6
    if alpha:
        links.new(tex.outputs['Alpha'], bsdf.inputs['Alpha'])
        try:
            material.surface_render_method = 'BLENDED'
        except (AttributeError, TypeError):
            pass
    if emission > 0:
        links.new(tex.outputs['Color'], bsdf.inputs['Emission Color'])
        bsdf.inputs['Emission Strength'].default_value = emission


def render(key, out, albedo_png, blur_png, lod0, extras, sockets):
    scene = bpy.context.scene
    engine = _engine(scene)
    scene.render.resolution_x, scene.render.resolution_y = 1600, 900
    scene.render.film_transparent = False
    if engine == 'CYCLES':
        scene.cycles.samples = 64
    albedo = bpy.data.images.load(str(albedo_png))
    blur = bpy.data.images.load(str(blur_png))
    for material in bpy.data.materials:
        if material.name == 'M_Hollow_KaFa':
            _textured(material, albedo)
        elif material.name == 'M_Hollow_KaFa_Lamps':
            _textured(material, albedo, emission=2.0)
        elif material.name == 'M_Hollow_KaFa_Blur':
            _textured(material, blur, alpha=True)
        elif material.name == 'M_Hollow_KaFa_Glass':
            bsdf, _, _ = _principled(material)
            bsdf.inputs['Base Color'].default_value = (.78, .86, .88, 1)
            bsdf.inputs['Alpha'].default_value = .22
            bsdf.inputs['Roughness'].default_value = .05
            try:
                material.surface_render_method = 'BLENDED'
            except (AttributeError, TypeError):
                pass
    shown = set(o.name for o in lod0)
    for o in bpy.data.objects:
        if o.type == 'MESH':
            o.hide_render = o.name not in shown
    # The blades at rest for the craft's pictures: the blurs hidden.
    for o in bpy.data.objects:
        if 'Blur' in o.name:
            o.hide_render = True
    # Ground and sky.
    bpy.ops.mesh.primitive_plane_add(size=60, location=(0, 0, 0))
    ground = bpy.context.object
    gm = bpy.data.materials.new('PreviewGround')
    bsdf, _, _ = _principled(gm)
    bsdf.inputs['Base Color'].default_value = (.20, .22, .16, 1)
    bsdf.inputs['Roughness'].default_value = .95
    ground.data.materials.append(gm)
    world = bpy.data.worlds.new('PreviewSky')
    scene.world = world
    world.use_nodes = True
    bg = next(n for n in world.node_tree.nodes if n.type == 'BACKGROUND')
    bg.inputs['Color'].default_value = (.55, .65, .78, 1)
    bg.inputs['Strength'].default_value = .9
    sun_data = bpy.data.lights.new('Sun', 'SUN')
    sun_data.energy = 3.5
    sun_data.angle = math.radians(3)
    sun = bpy.data.objects.new('Sun', sun_data)
    scene.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(-35))
    cam_data = bpy.data.cameras.new('Camera')
    cam_data.lens = 40
    cam = bpy.data.objects.new('Camera', cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam

    def shoot(name, eye, target):
        cam.location = eye
        cam.rotation_euler = (Vector(target) - Vector(eye)).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = str(out / f'preview-{name}.png')
        bpy.ops.render.render(write_still=True)
        print(f'[preview] {scene.render.filepath}', flush=True)

    shoot('front', (-4.6, -6.2, 2.6), (0, .6, 1.0))
    shoot('rear', (5.2, 7.4, 3.2), (0, .9, 1.0))
    shoot('side', (-9.5, 1.2, 1.6), (0, 1.3, 1.1))
    shoot('cockpit', (0, -.30, 1.37), (0, -2.6, .85))
    shoot('top', (-.01, .6, 14), (0, .6, 0))
    # The other modules in a row beside the craft.
    for i, (name, obj) in enumerate(extras.items()):
        obj.location = (3.2 + 1.3 * i, -1.0, .6 if name.endswith('tank') else 0)
        if name == 'plating':
            obj.location = (2.6 + 1.3 * i, -1.0 + .6, 0)
        obj.hide_render = False
    shoot('modules', (6.0, -6.5, 3.2), (5.0, -.5, .5))

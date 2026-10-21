"""Original, isolated character-audition props. No imported game geometry.

build_props(rig) expects prop_rock, prop_hatchet and prop_torch deform bones.
Coordinates below are in the corresponding bone's REST basis: grip at origin,
shaft along local +Z, hatchet edge toward local -Y. Geometry is transformed to
armature space before binding; all construction modifiers are applied first.
Procedural shading is for Blender review. Principled base/roughness/metal values
are the simple glTF fallback; no unsupported shader is required by the geometry.
"""
import math

import bpy
from mathutils import Matrix, Vector


def _material(name, color, roughness, metallic=0.0, texture_scale=30.0,
              relief=0.0003, stretch=(1.0, 1.0, 1.0), emission=None):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    mat.diffuse_color = (*color, 1.0)
    mat.roughness = roughness
    mat.metallic = metallic
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    shader = nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = (*color, 1.0)
    shader.inputs['Roughness'].default_value = roughness
    shader.inputs['Metallic'].default_value = metallic
    if relief:
        coords = nodes.new('ShaderNodeTexCoord')
        mapping = nodes.new('ShaderNodeVectorMath')
        mapping.operation = 'MULTIPLY'
        mapping.inputs[1].default_value = stretch
        links.new(coords.outputs['Generated'], mapping.inputs[0])
        noise = nodes.new('ShaderNodeTexNoise')
        noise.inputs['Scale'].default_value = texture_scale
        noise.inputs['Detail'].default_value = 3.0
        noise.inputs['Roughness'].default_value = 0.68
        links.new(mapping.outputs['Vector'], noise.inputs['Vector'])
        bump = nodes.new('ShaderNodeBump')
        bump.inputs['Strength'].default_value = 0.25
        bump.inputs['Distance'].default_value = relief
        links.new(noise.outputs['Fac'], bump.inputs['Height'])
        links.new(bump.outputs['Normal'], shader.inputs['Normal'])
    if emission:
        shader.inputs['Emission Color'].default_value = (*emission, 1.0)
        shader.inputs['Emission Strength'].default_value = 1.8
    return mat


def _mesh(name, vertices, faces, material, smooth=False):
    data = bpy.data.meshes.new(name + '_Mesh')
    data.from_pydata(vertices, [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    data.materials.append(material)
    for face in data.polygons:
        face.use_smooth = smooth
    return obj


def _apply_bevel(obj, width, segments=2):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bevel = obj.modifiers.new('Construction_Bevel', 'BEVEL')
    bevel.width = width
    bevel.segments = segments
    bevel.affect = 'EDGES'
    bpy.ops.object.modifier_apply(modifier=bevel.name)


def _shaft(name, rings, material, segments=16):
    """Each ring is (z, x centre, y centre, x radius, y radius)."""
    verts = []
    for z, cx, cy, rx, ry in rings:
        verts.extend((cx + rx * math.cos(2 * math.pi * k / segments),
                      cy + ry * math.sin(2 * math.pi * k / segments), z)
                     for k in range(segments))
    faces = []
    for j in range(len(rings) - 1):
        for k in range(segments):
            kn = (k + 1) % segments
            faces.append((j * segments + k, j * segments + kn,
                          (j + 1) * segments + kn, (j + 1) * segments + k))
    faces.extend((tuple(reversed(range(segments))),
                  tuple((len(rings) - 1) * segments + k for k in range(segments))))
    return _mesh(name, verts, faces, material, smooth=True)


def _spiral(name, start, end, turns, radius, thickness, material,
            center_x=0.0, center_y=0.0, vertical_width=1.0):
    """Small closed tube used for bindings, not a displacement modifier."""
    samples = max(16, int(turns * 20))
    sides = 6
    verts = []
    for i in range(samples + 1):
        t = i / samples
        a = 2.0 * math.pi * turns * t
        radial = Vector((math.cos(a), math.sin(a), 0.0))
        center = Vector((center_x, center_y, start + (end - start) * t)) + radial * radius
        for k in range(sides):
            b = 2.0 * math.pi * k / sides
            verts.append(center + radial * (thickness * math.cos(b))
                         + Vector((0.0, 0.0, thickness * vertical_width * math.sin(b))))
    faces = []
    for i in range(samples):
        for k in range(sides):
            faces.append((i * sides + k, (i + 1) * sides + k,
                          (i + 1) * sides + (k + 1) % sides, i * sides + (k + 1) % sides))
    faces.extend((tuple(range(sides)),
                  tuple(samples * sides + k for k in reversed(range(sides)))))
    return _mesh(name, verts, faces, material, smooth=True)


def _blade(name, polygon, widths, material, bevel=0.001):
    """Closed polygon in Y/Z, with varying thickness toward cutting edge."""
    verts = [(sign * width, y, z) for sign in (-1, 1)
             for (y, z), width in zip(polygon, widths)]
    count = len(polygon)
    faces = [tuple(reversed(range(count))), tuple(range(count, count * 2))]
    faces += [(k, (k + 1) % count, (k + 1) % count + count, k + count)
              for k in range(count)]
    obj = _mesh(name, verts, faces, material)
    if bevel:
        _apply_bevel(obj, bevel)
    return obj


def _rock(material):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=3, radius=1.0)
    obj = bpy.context.object
    obj.name = 'Trial_Rock'
    # Weathered fracture planes are deliberately oblique and unequal. None cuts
    # through the two +/-X hand-contact poles; those remain exactly 82 mm out.
    fractures = [(Vector(n).normalized(), depth) for n, depth in (
        ((.16, .36, .92), .86), ((-.12, -.81, .57), .89),
        ((.28, .81, .51), .91), ((.35, -.70, -.61), .86),
        ((-.26, .49, -.83), .88), ((.43, -.20, .88), .91),
        ((-.33, -.90, -.25), .94), ((.12, .98, -.17), .93))]
    for vertex in obj.data.vertices:
        p = vertex.co.copy()
        grip_fade = max(0.0, 1 - abs(p.x))
        weathering = (.040 * math.sin(p.x * 4.2 + p.y * 3.1 + .7)
                      * math.cos(p.z * 5.3 - .4)
                      + .018 * math.sin(p.y * 12.1 + p.z * 8.3))
        p *= 1 + grip_fade * weathering
        for normal, depth in fractures:
            over = normal.dot(p) - depth
            if over > 0:
                p -= normal * over
        vertex.co = (max(-.082, min(.082, p.x * .082)),
                     max(-.10, min(.10, p.y * .10)),
                     max(-.105, min(.105, p.z * .105)))
    obj.data.materials.append(material)
    for face in obj.data.polygons:
        face.use_smooth = True
    obj.data.update()

    # Original granite/basalt mineral mottling is baked into vertex colours,
    # so the actual mesh retains it through GLB export without image files.
    def noise(p, frequency):
        p = p * frequency
        cell = [math.floor(v) for v in p]
        blend = [v - math.floor(v) for v in p]
        blend = [v * v * (3 - 2 * v) for v in blend]
        value = 0.0
        for x in (0, 1):
            for y in (0, 1):
                for z in (0, 1):
                    k = ((cell[0] + x) * 73856093 ^ (cell[1] + y) * 19349663
                         ^ (cell[2] + z) * 83492791) & 0xffffffff
                    k = ((k ^ (k >> 13)) * 1274126177) & 0xffffffff
                    weight = ((blend[0] if x else 1 - blend[0])
                              * (blend[1] if y else 1 - blend[1])
                              * (blend[2] if z else 1 - blend[2]))
                    value += weight * ((k & 65535) / 65535)
        return value

    colors = obj.data.color_attributes.new(name='StoneMinerals', type='BYTE_COLOR', domain='CORNER')
    for loop in obj.data.loops:
        co = obj.data.vertices[loop.vertex_index].co
        p = Vector((co.x / .082, co.y / .10, co.z / .105))
        broad = noise(p, 3.2)
        grain = noise(p, 16.0)
        vein = math.exp(-abs(p.z + .42 * p.y - .22 * p.x - .10) * 55)
        # Restrained dark mineral islands, warm weathering and a broken pale vein.
        light = .105 + .13 * broad + .075 * grain + .075 * vein * noise(p, 8)
        warm = max(0.0, broad - .52) * .10
        colors.data[loop.index].color = (light + warm, light + .009,
                                        light + .012 - warm * .6, 1.0)
    obj.data.color_attributes.active_color_index = 0
    shader = material.node_tree.nodes.get('Principled BSDF')
    vertex_color = material.node_tree.nodes.new('ShaderNodeVertexColor')
    vertex_color.layer_name = colors.name
    material.node_tree.links.new(vertex_color.outputs['Color'], shader.inputs['Base Color'])
    return [obj]


def _hatchet(materials):
    wood, leather, forged, edge = (materials[k] for k in ('wood', 'leather', 'forged', 'edge'))
    objs = [_shaft('Trial_Hatchet', [
        (-.120, -.006, .006, .018, .024), (-.113, -.006, .006, .021, .025),
        (-.095, -.005, .005, .020, .022), (-.050, -.002, .002, .0175, .020),
        (.000, .000, .000, .017, .019), (.070, .002, -.002, .016, .019),
        (.150, .004, -.003, .015, .018), (.220, .003, -.001, .014, .018),
        (.285, .000, .002, .013, .017), (.325, .000, .002, .012, .016)], wood)]
    objs.append(_spiral('Trial_Hatchet_GripBinding', -.088, .035, 8,
                        .019, .0018, leather, vertical_width=2.0))
    # Narrow poll/eye, flaring blade, and a distinct honed strip.
    polygon = [(.048, .244), (.048, .307), (.006, .322), (-.044, .320),
               (-.131, .349), (-.160, .333), (-.160, .224), (-.130, .207),
               (-.043, .255)]
    widths = [.024, .024, .025, .024, .012, .004, .004, .012, .024]
    objs.append(_blade('Trial_Hatchet_ForgedHead', polygon, widths, forged, .0022))
    objs.append(_blade('Trial_Hatchet_HonedEdge',
                       [(-.133, .345), (-.164, .333), (-.164, .224), (-.133, .211)],
                       [.009, .0014, .0014, .009], edge, .0006))
    objs.append(_shaft('Trial_Hatchet_EyeCollar',
                       [(.237, 0, .003, .019, .024), (.244, 0, .003, .020, .025)], forged))
    return objs


def _torch(materials):
    wood, cloth, char, ember = (materials[k] for k in ('torchwood', 'cloth', 'char', 'ember'))
    objs = [_shaft('Trial_Torch', [
        (-.130, -.002, .000, .016, .018), (-.124, -.002, .000, .018, .020),
        (-.070, .000, .001, .019, .020), (.000, .000, .000, .019, .020),
        (.100, -.002, .001, .018, .020), (.200, -.004, .001, .016, .018),
        (.285, -.004, .002, .017, .019), (.340, -.002, .002, .017, .019)], wood)]
    objs.append(_shaft('Trial_Torch_ClothHead', [
        (.238, -.004, .002, .022, .023), (.255, -.004, .002, .028, .029),
        (.275, -.004, .002, .031, .032), (.310, -.003, .002, .034, .033),
        (.345, -.002, .002, .032, .034), (.375, -.002, .002, .028, .029),
        (.390, -.001, .002, .023, .024)], cloth, segments=16))
    objs.append(_spiral('Trial_Torch_ClothWrap', .253, .371, 6, .031,
                        .0035, cloth, center_x=-.003, center_y=.002, vertical_width=1.9))
    objs.append(_shaft('Trial_Torch_CharredTip', [
        (.374, -.002, .002, .028, .029), (.390, -.001, .002, .024, .025),
        (.399, -.002, .002, .018, .020)], char, segments=12))
    # Tiny visible coals provide a readable live tip without a simulated flame.
    for i, (x, y, z) in enumerate(((-.012, -.015, .389), (.010, -.012, .392),
                                 (.013, .010, .389), (-.006, .010, .399))):
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=1)
        obj = bpy.context.object
        obj.name = 'Trial_Torch_Ember_%02d' % i
        for vertex in obj.data.vertices:
            p = vertex.co
            vertex.co = (x + p.x * .005, y + p.y * .004, z + p.z * .0045)
        obj.data.materials.append(ember)
        objs.append(obj)
    return objs


def build_props(rig, scale=1.0):
    """Build new meshes; return them all. Does not alter the supplied armature."""
    if scale <= 0:
        raise ValueError('Prop scale must be positive')
    bone_names = ('prop_rock', 'prop_hatchet', 'prop_torch')
    missing = [name for name in bone_names if name not in rig.data.bones]
    if missing:
        raise ValueError('Missing prop bones: ' + ', '.join(missing))
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    mats = {
        'rock': _material('Trial_Stone_MatteBasalt', (.20, .225, .235), .88,
                          texture_scale=31, relief=.0011),
        'wood': _material('Trial_Hatchet_OiledAsh', (.24, .115, .047), .52,
                          texture_scale=19, relief=.00035, stretch=(5, 5, .25)),
        'leather': _material('Trial_Grip_DarkLeather', (.052, .027, .016), .72,
                             texture_scale=55, relief=.00025),
        'forged': _material('Trial_Head_DarkForgedSteel', (.12, .135, .145), .39, .82,
                            texture_scale=24, relief=.00045),
        'edge': _material('Trial_Head_HonedSteel', (.43, .46, .48), .26, .9,
                          texture_scale=85, relief=.00008, stretch=(1, 1, .1)),
        'torchwood': _material('Trial_Torch_SplitWood', (.16, .075, .029), .81,
                               texture_scale=22, relief=.00055, stretch=(5, 5, .2)),
        'cloth': _material('Trial_Torch_OilSoakedLinen', (.24, .18, .105), .94,
                           texture_scale=85, relief=.0006),
        'char': _material('Trial_Torch_Charcoal', (.018, .012, .008), .98,
                          texture_scale=14, relief=.0009),
        'ember': _material('Trial_Torch_LiveEmber', (.23, .027, .004), .88,
                           relief=0, emission=(.5, .055, .005)),
    }
    sets = {'prop_rock': _rock(mats['rock']), 'prop_hatchet': _hatchet(mats),
            'prop_torch': _torch(mats)}
    result = []
    for bone_name, objects in sets.items():
        rest = rig.data.bones[bone_name].matrix_local.copy()
        for obj in objects:
            for vertex in obj.data.vertices:
                vertex.co = rest @ (vertex.co * scale)
            obj.data.update()
            obj.parent = rig
            obj.matrix_parent_inverse = Matrix.Identity(4)
            obj.matrix_basis = Matrix.Identity(4)
            group = obj.vertex_groups.new(name=bone_name)
            group.add(list(range(len(obj.data.vertices))), 1.0, 'REPLACE')
            armature = obj.modifiers.new('Prop_Deform', 'ARMATURE')
            armature.object = rig
            obj['trial_prop'] = bone_name.removeprefix('prop_')
            obj['source'] = 'Original mesh: art/character_trial/props.py'
            result.append(obj)
    triangles = sum(sum(len(p.vertices) - 2 for p in obj.data.polygons) for obj in result)
    if triangles >= 10000:
        raise RuntimeError('Prop topology budget exceeded: %d triangles' % triangles)
    return result

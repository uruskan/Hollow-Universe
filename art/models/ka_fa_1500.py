"""The KA-FA 1500 (docs/MINICOPTER.md §10): our minicopter, a two-seat rotorcraft welded from scrap.

The frame (welded tube in oxide-red primer, skids of bent pipe, a bucket seat, a bent clear windscreen, the stick, the
lever and two gauges, the mast with its swashplate, a truss tail boom with its tail rotor behind a taped guard and a road
sign for a fin) and its modules, each its own model mounted at a socket of the frame:
- the flat twin and the tuned twin (air-cooled, a vertical shaft belted to the mast);
- the rotor (a teetering two-blade rotor, and its blurred disc for when it spins);
- the jerry tank and the long tank (under the floor);
- the rear seat (looking out to the right) and the cargo box (on the rear deck);
- the plating.

Blender's axes: +X the craft's left, -Y its nose (Godot +Z), +Z up; the pivot on the ground between the skids. Each
module is built in its socket's frame. `ka-fa-1500-frame.glb` is the frame alone, what the view mounts modules on; `ka-fa-1500.glb` is the
craft as its default build makes it (the frame with the twin, the rotor, the jerry tank and the rear seat), the item's model.
No downloaded meshes or pixels: one 2048 px atlas (art/models/ka_fa_1500_paint.py), a glass, the blades' blur and the
lamps' lenses; three hand-built LODs; vertex occlusion.

    blender -b --factory-startup --python art/models/ka_fa_1500.py [-- --preview]
"""
from pathlib import Path
import json
import math
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
sys.path.insert(0, str(Path(__file__).resolve().parent))
from hollow import WORLD, TEXTURES, REPO, script_args
from hollow.export import collection, export_glb, convex_hull, vertex_ao, set_export_colour, select_only
from hollow.godot import write_material
from hollow.textures import write_png, write_texture_import, normal_from_height
from hollow.component_mesh import ComponentMesh
import ka_fa_1500_paint as paint

KEY = 'ka-fa-1500'
OUT = REPO / '.artifacts/itemart' / KEY
OUT.mkdir(parents=True, exist_ok=True)
ARGS = script_args()
PREVIEW = '--preview' in ARGS

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.unit_settings.system = 'METRIC'
col = collection('KaFa')

# ---------------------------------------------------------------- sockets (Blender frame; docs/MINICOPTER.md §4.1)

SOCKETS = {
    'engine': (0, .55, .465),       # on the floor behind the seat, under the mast
    'rotor': (0, .15, 2.30),        # the mast's top
    'tank': (0, -.20, .42),         # under the floor rails
    'rear': (0, 1.30, .465),        # the rear deck
    'plating': (0, 0, 0),           # the frame's own frame
}
EXTRA_SOCKETS = {
    'engine_audio': (0, .55, .80),
    'seat_pilot': (0, -.25, .57),
    'eye_pilot': (0, -.30, 1.37),
    'seat_rear': (-.12, 1.28, .84),
    'fuel': (.40, -.37, .60),
    'tail_rotor': (-.21, 4.08, 1.10),
    'wash': (0, .15, 0),
}
MAST_PULLEY = Vector((0, .15, 1.12))

# ---------------------------------------------------------------- textures and materials

albedo, orm, height = paint.paint()
paths = {}
for key, pixels in [('albedo', albedo), ('orm', orm), ('normal', normal_from_height(height, 2.0))]:
    paths[key] = write_png(TEXTURES / KEY / f'{KEY}_{key}.png', pixels)
    write_texture_import(paths[key], normal_map=key == 'normal')
blur_png = write_png(TEXTURES / KEY / f'{KEY}_blur.png', paint.blur_disc(), channels=4)
write_texture_import(blur_png)
MATERIALS = {
    'M_Hollow_KaFa': write_material('ka-fa-1500', 'M_Hollow_KaFa', albedo=paths['albedo'], orm=paths['orm'], normal=paths['normal'],
                                    normal_scale=.5, vertex_colour=True, vertex_colour_srgb=False),
    'M_Hollow_KaFa_Glass': write_material('ka-fa-1500_glass', 'M_Hollow_KaFa_Glass', colour=(.78, .86, .88, .22), roughness=.08,
                                          metallic=0, vertex_colour=False, transparent=True, extra=['cull_mode = 2']),
    'M_Hollow_KaFa_Blur': write_material('ka-fa-1500_blur', 'M_Hollow_KaFa_Blur', albedo=blur_png, roughness=.7, vertex_colour=False,
                                         transparent=True, extra=['cull_mode = 2', 'shading_mode = 0']),
    'M_Hollow_KaFa_Lamps': write_material('ka-fa-1500_lamps', 'M_Hollow_KaFa_Lamps', albedo=paths['albedo'], emission=paths['albedo'],
                                          emission_energy=2.0, roughness=.15, vertex_colour=False),
}
mat = bpy.data.materials.new('M_Hollow_KaFa')
glass_mat = bpy.data.materials.new('M_Hollow_KaFa_Glass')
blur_mat = bpy.data.materials.new('M_Hollow_KaFa_Blur')
lamp_mat = bpy.data.materials.new('M_Hollow_KaFa_Lamps')
REGIONS = paint.REGIONS
REGIONS['clear'] = (.0, .0, 1.0, 1.0)  # the glass and the blur use their whole texture

# ---------------------------------------------------------------- shape helpers (research_table.py's, and bent pipe)


def place(obj, matrix):
    for vertex in obj.data.vertices:
        vertex.co = matrix @ vertex.co
    obj.data.update()
    return obj


def toward(a, b):
    """The matrix taking +X from the origin onto the segment a-b, its middle at the origin's image."""
    a, b = Vector(a), Vector(b)
    q = (b - a).normalized().to_track_quat('X', 'Z')
    return Matrix.Translation((a + b) / 2) @ q.to_matrix().to_4x4()


def beam(mesh, name, a, b, size, region, bevel=.003):
    """A square or rectangular tube from a to b: size (across, up) in its own frame."""
    length = (Vector(b) - Vector(a)).length
    return place(mesh.box(name, (0, 0, 0), (length, size[0], size[1]), region, bevel if mesh.lod < 2 else 0), toward(a, b))


def rod(mesh, name, a, b, radius, region, segments=None, end=None):
    length = (Vector(b) - Vector(a)).length
    r2 = end if end is not None else radius
    rings = [(-length / 2, 0), (-length / 2, radius), (length / 2, r2), (length / 2, 0)]
    obj = mesh.lathe_x(name, rings, (0, 0), region, segments=segments or (14, 9, 6)[mesh.lod])
    return place(obj, toward(a, b))


def block(mesh, name, centre, size, region, rotation=None, bevel=.004):
    obj = mesh.box(name, (0, 0, 0), size, region, bevel if mesh.lod < 2 else 0)
    return place(obj, Matrix.Translation(centre) @ (rotation or Matrix.Identity(4)))


def disc(mesh, name, centre, radius, thick, region, axis='Z', segments=None):
    """A short cylinder about an axis ('X', 'Y', 'Z' or a vector)."""
    obj = mesh.lathe_x(name, [(-thick / 2, 0), (-thick / 2, radius), (thick / 2, radius), (thick / 2, 0)], (0, 0), region,
                       segments=segments or (24, 14, 8)[mesh.lod])
    direction = {'X': Vector((1, 0, 0)), 'Y': Vector((0, 1, 0)), 'Z': Vector((0, 0, 1))}.get(axis) if isinstance(axis, str) else Vector(axis)
    q = direction.normalized().to_track_quat('X', 'Z' if abs(direction.normalized().z) < .99 else 'Y')
    return place(obj, Matrix.Translation(centre) @ q.to_matrix().to_4x4())


def face(mesh, name, radius, region, steps=None):
    """A flat round face in the XZ plane looking along -Y, its region's picture laid on it (a gauge's dial, a lens)."""
    steps = steps or (24, 14, 8)[mesh.lod]
    verts, faces, uvs = [(0, 0, 0)], [], [(.5, .5)]
    for k in range(steps):
        a = math.tau * k / steps
        verts.append((radius * math.cos(a), 0, radius * math.sin(a)))
        uvs.append((.5 + .5 * math.cos(a), .5 - .5 * math.sin(a)))
    for k in range(steps):
        faces.append((0, 1 + (k + 1) % steps, 1 + k))
    return mesh.mesh(name, verts, faces, region, False, uvs)


def smooth(points, per):
    """Catmull-Rom points through the control points, `per` to a span."""
    pts = [Vector(p) for p in points]
    if len(pts) < 3 or per <= 1:
        return pts
    out = []
    for i in range(len(pts) - 1):
        p0, p1, p2, p3 = pts[max(i - 1, 0)], pts[i], pts[i + 1], pts[min(i + 2, len(pts) - 1)]
        for k in range(per):
            t = k / per
            out.append(.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3))
    out.append(pts[-1])
    return out


def pipe(mesh, name, points, radius, region, caps=True, per=None, sides=None):
    """Bent tube through control points, capped at its ends."""
    pts = smooth(points, per or (4, 2, 1)[mesh.lod])
    obj = mesh.cable(name, [tuple(p) for p in pts], radius, region, closed=False, sides=sides or (10, 7, 5)[mesh.lod])
    if caps and mesh.lod < 2:
        for end, before in ((pts[0], pts[1]), (pts[-1], pts[-2])):
            d = (end - before).normalized()
            disc(mesh, name + 'Cap', end - d * .001, radius, .002, region, axis=d, segments=(10, 7, 5)[mesh.lod])
    return obj


def loop(mesh, name, a, ra, b, rb, z, radius, region):
    """A belt round two pulleys in a horizontal plane at height z (centres a and b in x, y)."""
    a, b = Vector((a[0], a[1], z)), Vector((b[0], b[1], z))
    along = (b - a).normalized()
    side = Vector((-along.y, along.x, 0))
    steps = (14, 8, 5)[mesh.lod]
    pts = []
    for centre, r, start in ((b, rb, -math.pi / 2), (a, ra, math.pi / 2)):
        for k in range(steps + 1):
            t = start + math.pi * k / steps
            pts.append(centre + (along * math.cos(t) + side * math.sin(t)) * r)
    return mesh.cable(name, [tuple(p) for p in pts], radius, region, closed=True, sides=(6, 5, 4)[mesh.lod])


def rotation(axis, degrees):
    return Matrix.Rotation(math.radians(degrees), 4, axis)


# ---------------------------------------------------------------- the frame

TUBE = .021


def frame(mesh):
    lod = mesh.lod
    # Skids: bent pipe turned up at the nose, rubber pads where they meet the ground, clamps at the struts.
    for sx in (1, -1):
        x = .80 * sx
        pipe(mesh, 'Skid', [(x, -1.31, .22), (x, -1.27, .12), (x, -1.18, .06), (x, -1.02, .04), (x, 1.06, .04)], .032, 'primer')
        for y in (-.85, .75):
            block(mesh, 'SkidPad', (x, y, .011), (.07, .17, .022), 'rubber', bevel=.006)
        for y in (-.62, .58):
            block(mesh, 'SkidClamp', (x, y, .05), (.085, .075, .075), 'steel', bevel=.006)
            rod(mesh, 'ClampBolt', (x - .055, y, .02), (x + .055, y, .02), .007, 'steel')
    # The cross tubes from the skids up under the floor.
    for y in (-.62, .58):
        pipe(mesh, 'SkidStrut', [(.80, y, .06), (.79, y, .2), (.70, y, .34), (.52, y, .425), (.40, y, .44), (-.40, y, .44), (-.52, y, .425),
                                  (-.70, y, .34), (-.79, y, .2), (-.80, y, .06)], .028, 'primer')
    # Floor: two rails of square tube, cross members, diamond plate.
    for sx in (1, -1):
        beam(mesh, 'FloorRail', (.36 * sx, -1.17, .44), (.36 * sx, 1.64, .44), (.04, .04), 'primer')
    for y in (-1.15, -.62, .02, .58, 1.0, 1.62):
        beam(mesh, 'FloorCross', (-.38, y, .44), (.38, y, .44), (.035, .035), 'primer')
    block(mesh, 'CockpitFloor', (0, -.58, .464), (.70, 1.12, .008), 'tread', bevel=.002)
    block(mesh, 'RearDeck', (0, 1.31, .464), (.70, .60, .008), 'tread', bevel=.002)
    # The nose hoop the windscreen sits on, and its bottom brace.
    nose = [(.36, -1.15, .44), (.34, -1.28, .50), (.19, -1.39, .56), (0, -1.42, .58), (-.19, -1.39, .56), (-.34, -1.28, .50), (-.36, -1.15, .44)]
    pipe(mesh, 'NoseHoop', nose, TUBE, 'primer')
    pipe(mesh, 'NoseKeel', [(0, -1.42, .58), (0, -1.36, .47), (0, -1.15, .44)], .016, 'primer')
    # Roll hoops over the pilot down to the pylon, braced.
    for sx in (1, -1):
        hoop = [(.36 * sx, -1.12, .45), (.37 * sx, -1.04, .95), (.34 * sx, -.88, 1.36), (.25 * sx, -.60, 1.64), (.15 * sx, -.25, 1.85),
                (.11 * sx, .06, 1.97)]
        pipe(mesh, 'RollHoop', hoop, TUBE, 'primer')
        pipe(mesh, 'PylonFront', [(.36 * sx, .02, .45), (.25 * sx, .05, 1.2), (.12 * sx, .08, 1.95)], TUBE, 'primer')
        pipe(mesh, 'PylonRear', [(.36 * sx, .58, .45), (.25 * sx, .42, 1.2), (.12 * sx, .24, 1.95)], TUBE, 'primer')
        rod(mesh, 'HoopBrace', (.34 * sx, -.88, 1.36), (.36 * sx, -.20, .46), .013, 'primer')
        rod(mesh, 'PylonBrace', (.25 * sx, .05, 1.2), (.25 * sx, .42, 1.2), .013, 'primer')
        rod(mesh, 'SideBar', (.37 * sx, -1.04, .95), (.36 * sx, .02, .90), .015, 'primer')
        if lod == 0:
            # Tack welds where the tubes meet: grey beads, a hand's work.
            for p in ((.36 * sx, -1.12, .45), (.36 * sx, .02, .45), (.36 * sx, .58, .45), (.12 * sx, .08, 1.95), (.12 * sx, .24, 1.95)):
                disc(mesh, 'Weld', p, .03, .03, 'weld', axis='Z', segments=10)
    rod(mesh, 'HoopCross', (.25, -.60, 1.64), (-.25, -.60, 1.64), TUBE, 'primer')
    rod(mesh, 'ScreenTop', (.30, -.82, 1.44), (-.30, -.82, 1.44), .012, 'primer')
    for sx in (1, -1):
        rod(mesh, 'ScreenStay', (.30 * sx, -.82, 1.44), (.29 * sx, -.70, 1.55), .010, 'primer')
    # The mast's bearing block where the pylons meet, the mast, its pulley and the swashplate with its push rods.
    block(mesh, 'MastBearing', (0, .16, 1.98), (.30, .26, .12), 'dark', bevel=.012)
    disc(mesh, 'BearingFlange', (0, .15, 2.06), .085, .03, 'steel')
    rod(mesh, 'Mast', (0, .15, .98), (0, .15, 2.30), .042, 'steel')
    disc(mesh, 'MastPulley', MAST_PULLEY, .165, .05, 'steel')
    disc(mesh, 'MastPulleyHub', MAST_PULLEY, .06, .09, 'dark')
    block(mesh, 'LowerBearing', (0, .15, .97), (.16, .16, .08), 'dark', bevel=.008)
    disc(mesh, 'Swashplate', (0, .15, 2.15), .12, .022, 'steel')
    disc(mesh, 'SwashRing', (0, .15, 2.13), .10, .02, 'dark')
    for sx in (1, -1):
        rod(mesh, 'PushRod', (.10 * sx, .02, 1.05), (.09 * sx, .13, 2.13), .007, 'steel')
        block(mesh, 'Bellcrank', (.10 * sx, .0, 1.02), (.03, .08, .05), 'steel', bevel=.004)
    # The pilot's seat: a salvaged bucket seat on two rails, a harness.
    block(mesh, 'SeatRails', (0, -.25, .485), (.34, .44, .03), 'steel', bevel=.004)
    block(mesh, 'SeatPan', (0, -.25, .535), (.46, .46, .07), 'seat', bevel=.02)
    back = Matrix.Translation((0, .01, .90)) @ rotation('X', -12)
    place(mesh.box('SeatBack', (0, 0, 0), (.46, .08, .70), 'seat', .02 if lod < 2 else 0), back)
    for sx in (1, -1):
        block(mesh, 'SeatBolster', (.215 * sx, -.24, .60), (.05, .42, .11), 'seat', bevel=.015)
        place(mesh.box('BackBolster', (.215 * sx, -.04, 0), (.05, .07, .55), 'seat', .015 if lod < 2 else 0), back)
        if lod < 2:
            place(mesh.box('Harness', (.11 * sx, -.05, .05), (.045, .012, .62), 'rubber', 0), back)
    place(mesh.box('Headrest', (0, .01, .43), (.26, .08, .16), 'seat', .015 if lod < 2 else 0), back)
    # The controls: the stick, the lever, pedals, a strap-iron panel with two gauges and a switch.
    rod(mesh, 'Cyclic', (0, -.66, .47), (0, -.63, .85), .013, 'steel')
    rod(mesh, 'CyclicGrip', (0, -.63, .85), (0, -.62, .97), .019, 'rubber', end=.017)
    disc(mesh, 'CyclicBoot', (0, -.66, .49), .045, .04, 'rubber')
    rod(mesh, 'Collective', (.29, -.14, .50), (.30, -.52, .66), .012, 'steel')
    rod(mesh, 'CollectiveGrip', (.30, -.52, .66), (.30, -.62, .70), .018, 'rubber')
    beam(mesh, 'PedalBar', (-.22, -1.05, .50), (.22, -1.05, .50), (.02, .02), 'steel')
    for sx in (1, -1):
        block(mesh, 'Pedal', (.12 * sx, -1.07, .54), (.08, .03, .12), 'tread', rotation('X', 20), .004)
    rod(mesh, 'PanelPost', (0, -1.40, .58), (0, -1.20, .86), .011, 'primer')
    panel = Matrix.Translation((0, -1.16, .92)) @ rotation('X', 25)
    place(mesh.box('Panel', (0, 0, 0), (.36, .012, .16), 'dark', .003 if lod < 2 else 0), panel)
    for sx in (1, -1):
        place(disc(mesh, 'GaugeBody', (0, 0, 0), .047, .02, 'dark', axis='Y'), panel @ Matrix.Translation((.085 * sx, .012, .01)))
        # The dial faces the pilot (+Y, the seat's way).
        place(face(mesh, 'GaugeDial', .041, 'gauge'), panel @ Matrix.Translation((.085 * sx, .0225, .01)) @ rotation('Z', 180))
    if lod < 2:
        place(rod(mesh, 'Switch', (0, 0, 0), (0, .03, .02), .005, 'steel'), panel @ Matrix.Translation((0, .006, -.04)))
        pipe(mesh, 'Wiring', [(.0, -1.15, .86), (.20, -1.10, .86), (.36, -1.0, .90), (.37, -.6, .92), (.36, .0, .9), (.30, .3, .75)],
             .006, 'cable', caps=False)
    # The tail boom: a triangular truss of three longerons, braced, the tail rotor's drive shaft inside.
    stations = (9, 6, 4)[lod]

    def at(start, end, t):
        return Vector(start).lerp(Vector(end), t)
    A, B, C = ((.17, 1.05, .56), (.04, 4.06, 1.03)), ((-.17, 1.05, .56), (-.04, 4.06, 1.03)), ((0, 1.05, .98), (0, 4.06, 1.17))
    for longeron in (A, B, C):
        rod(mesh, 'Longeron', longeron[0], longeron[1], .018, 'primer')
    sides = (8, 6, 4)[lod]
    for i in range(stations + 1):
        t = i / stations
        a, b, c = at(*A, t), at(*B, t), at(*C, t)
        for p, q in ((a, b), (a, c), (b, c)):
            rod(mesh, 'TrussRung', p, q, .010, 'primer', segments=sides)
        if i < stations and lod < 2:
            # Zigzag diagonals on each face, turning the other way at every bay.
            t2 = (i + 1) / stations
            a2, b2, c2 = at(*A, t2), at(*B, t2), at(*C, t2)
            for (p, q), (p2, q2) in (((a, c), (a2, c2)), ((b, c), (b2, c2)), ((a, b), (a2, b2))):
                start, end = (p, q2) if i % 2 == 0 else (q, p2)
                rod(mesh, 'TrussBrace', start, end, .008, 'primer', segments=(6, 5, 4)[lod])
    for sx in (1, -1):
        block(mesh, 'BoomRoot', (.17 * sx, 1.05, .50), (.05, .08, .08), 'steel', bevel=.004)
    rod(mesh, 'TailShaft', (0, .85, .80), (0, 4.0, 1.10), .011, 'steel')
    for t in (.3, .55, .8):
        p = Vector((0, .85, .80)).lerp(Vector((0, 4.0, 1.10)), t)
        block(mesh, 'ShaftBearing', p, (.05, .05, .05), 'dark', bevel=.004)
    # The tail: gearbox, the rotor's shaft, a horizontal stabiliser, the guard and the road sign for a fin.
    block(mesh, 'TailGearbox', (0, 4.09, 1.10), (.11, .17, .14), 'dark', bevel=.012)
    rod(mesh, 'TailRotorShaft', (0, 4.08, 1.10), (-.19, 4.08, 1.10), .014, 'steel')
    block(mesh, 'Stabiliser', (0, 3.45, .985), (.82, .20, .008), 'olive', bevel=.002)
    for sx in (1, -1):
        block(mesh, 'StabiliserEnd', (.41 * sx, 3.45, 1.02), (.008, .18, .09), 'olive', bevel=.002)
    # A taped hoop round the tail rotor's lower half, in its plane, on two stays to the boom and the gearbox.
    hub = Vector(EXTRA_SOCKETS['tail_rotor'])
    arc = [hub + Vector((0, .56 * math.cos(math.radians(a)), .56 * math.sin(math.radians(a)))) for a in range(160, 381, (20, 30, 55)[lod])]
    pipe(mesh, 'TailGuard', arc, .018, 'tape', per=1)
    rod(mesh, 'GuardStay', tuple(arc[0]), (-.03, 3.70, 1.06), .012, 'primer')
    rod(mesh, 'GuardStay', tuple(arc[-1]), (-.05, 4.12, 1.04), .012, 'primer')
    fin = Matrix.Translation((.03, 3.98, 1.43)) @ rotation('X', 45)
    place(mesh.box('Fin', (0, 0, 0), (.006, .52, .52), 'sign', 0), fin)
    if lod < 2:
        for dy in (-.08, .08):
            # U-bolts holding the sign to the top longeron.
            pipe(mesh, 'FinBolt', [(.035, 3.98 + dy, 1.20), (.03, 3.98 + dy, 1.13), (-.02, 3.98 + dy, 1.13), (-.025, 3.98 + dy, 1.20)], .005,
                 'steel', caps=False)
    # Lamp housings (the lenses are the lamps' own mesh).
    rod(mesh, 'HeadlampHousing', (0, -1.33, .50), (0, -1.44, .50), .066, 'dark', end=.07)
    rod(mesh, 'HeadlampMount', (0, -1.38, .50), (0, -1.41, .57), .012, 'steel')
    rod(mesh, 'TailLampHousing', (0, 4.13, 1.24), (0, 4.17, 1.24), .03, 'dark')
    # A step on the left strut, a grab handle on the hoop.
    block(mesh, 'Step', (.64, -.62, .31), (.16, .07, .01), 'tread', bevel=.002)
    # The windscreen's bolts at its corners and sides.
    screen_bolts(mesh)
    if lod < 2:
        pipe(mesh, 'GrabHandle', [(.36, -1.04, 1.0), (.42, -1.0, 1.1), (.42, -.92, 1.2), (.35, -.88, 1.3)], .011, 'rubber')


def windscreen(mesh):
    """A clear sheet bent over the nose: a grid between the nose hoop and the screen's top tube, bulging forward."""
    nu, nv = (12, 8, 4)[mesh.lod], (8, 5, 3)[mesh.lod]
    verts, faces, uvs = [], [], []
    for j in range(nv + 1):
        v = j / nv
        for i in range(nu + 1):
            u = i / nu * 2 - 1
            bottom = Vector((.33 * u, -1.38 + .07 * u * u, .575))
            top = Vector((.29 * u, -.83 + .03 * u * u, 1.43))
            p = bottom.lerp(top, v) + Vector((0, -.09 * math.sin(math.pi * v) * (1 - .3 * u * u), .02 * math.sin(math.pi * v)))
            verts.append(tuple(p))
            uvs.append((i / nu, v))
    for j in range(nv):
        for i in range(nu):
            a = j * (nu + 1) + i
            faces.append((a, a + 1, a + nu + 2, a + nu + 1))
    return mesh.mesh('Windscreen', verts, faces, 'clear', True, uvs)


def screen_bolts(mesh):
    for p in ((.33, -1.37, .58), (-.33, -1.37, .58), (.29, -.82, 1.43), (-.29, -.82, 1.43), (.31, -1.12, 1.0), (-.31, -1.12, 1.0)):
        disc(mesh, 'ScreenBolt', p, .012, .012, 'steel', axis=(0, -.6, .4), segments=(8, 6, 4)[mesh.lod])


def lamps(mesh):
    disc(mesh, 'HeadlampLens', (0, -1.445, .50), .055, .008, 'lamp', axis='Y')
    disc(mesh, 'TailLampLens', (0, 4.172, 1.24), .026, .006, 'lamp_red', axis='Y')


def tail_rotor(mesh):
    """The tail rotor about its pivot (its axis X): hub, two blades, taped tips."""
    disc(mesh, 'TailHub', (0, 0, 0), .04, .05, 'dark', axis='X')
    for s in (1, -1):
        block(mesh, 'TailBlade', (-.01, 0, .24 * s), (.012, .07, .40), 'blade', bevel=.002)
        block(mesh, 'TailTip', (-.01, 0, .425 * s), (.014, .072, .05), 'tape', bevel=.001)


def blur(mesh, name, radius, axis):
    """A blurred disc for spinning blades (the blur material's own texture covers it)."""
    steps = (32, 24, 16)[mesh.lod]
    verts, faces, uvs = [(0, 0, 0)], [], [(.5, .5)]
    for k in range(steps):
        a = math.tau * k / steps
        verts.append((radius * math.cos(a), radius * math.sin(a), 0))
        uvs.append((.5 + .5 * math.cos(a), .5 + .5 * math.sin(a)))
    for k in range(steps):
        faces.append((0, 1 + k, 1 + (k + 1) % steps))
    obj = mesh.mesh(name, verts, faces, 'clear', False, uvs)
    if axis == 'X':
        place(obj, rotation('Y', 90))
    return obj


# ---------------------------------------------------------------- the modules, each in its socket's frame


def twin(mesh, tuned=False):
    """The flat twin: a crankcase on a plate, two finned cylinders out to the sides, a vertical shaft and pulley belted to the
    mast's, a carburettor and filter, the exhaust out to the right in heat wrap. The tuned one: red rocker covers, two
    carburettors with velocity stacks, an oil cooler, a bare steel exhaust."""
    lod = mesh.lod
    block(mesh, 'EnginePlate', (0, 0, .01), (.46, .42, .02), 'dark', bevel=.003)
    for sx in (1, -1):
        for sy in (1, -1):
            disc(mesh, 'MountBolt', (.2 * sx, .18 * sy, .025), .012, .012, 'steel', segments=8)
    block(mesh, 'Crankcase', (0, 0, .21), (.34, .32, .34), 'alu', bevel=.03)
    for sx in (1, -1):
        rod(mesh, 'Cylinder', (.17 * sx, -.02, .22), (.36 * sx, -.02, .22), .072, 'alu')
        fins = (8, 4, 0)[lod]
        for k in range(fins):
            x = (.19 + k * .022) * sx
            disc(mesh, 'Fin', (x, -.02, .22), .095, .006, 'alu', axis='X', segments=(16, 10, 6)[lod])
        block(mesh, 'Head', (.40 * sx, -.02, .22), (.06, .17, .17), 'red' if tuned else 'dark', bevel=.01)
        if tuned:
            block(mesh, 'RockerCover', (.435 * sx, -.02, .22), (.02, .13, .13), 'red', bevel=.008)
        rod(mesh, 'SparkPlug', (.40 * sx, -.02, .31), (.42 * sx, -.02, .35), .008, 'steel')
        if lod < 2:
            pipe(mesh, 'PlugLead', [(.42 * sx, -.02, .35), (.30 * sx, -.08, .42), (.08 * sx, -.17, .40)], .005, 'cable', caps=False)
    block(mesh, 'Magneto', (0, -.17, .38), (.10, .06, .08), 'red', bevel=.008)
    rod(mesh, 'Shaft', (0, 0, .38), (0, 0, .63), .024, 'steel')
    pulley_z = MAST_PULLEY.z - SOCKETS['engine'][2]
    disc(mesh, 'Pulley', (0, 0, pulley_z), .11, .05, 'steel')
    disc(mesh, 'PulleyHub', (0, 0, pulley_z), .04, .08, 'dark')
    mast = MAST_PULLEY - Vector(SOCKETS['engine'])
    loop(mesh, 'Belt', (0, 0), .115, (mast.x, mast.y), .17, pulley_z, .012, 'rubber')
    carbs = (-.08, .08) if tuned else (0,)
    for cx in carbs:
        block(mesh, 'Carburettor', (cx, .19, .26), (.07, .08, .08), 'alu', bevel=.008)
        if tuned:
            rod(mesh, 'VelocityStack', (cx, .23, .26), (cx, .31, .27), .028, 'steel', end=.04)
        else:
            disc(mesh, 'AirFilter', (cx, .26, .26), .075, .06, 'red', axis='Y')
    if tuned:
        block(mesh, 'OilCooler', (.0, -.24, .17), (.24, .03, .14), 'dark', bevel=.004)
        if lod == 0:
            for k in range(7):
                block(mesh, 'CoolerFin', (-.10 + k * .033, -.258, .17), (.006, .012, .13), 'steel', bevel=0)
    wrap = 'steel' if tuned else 'heat'
    for sx in (1, -1):
        pipe(mesh, 'Header', [(.40 * sx, .05, .14), (.36 * sx, .14, .08), (.15 * sx, .24, .06), (-.30, .26, .08), (-.46, .20, .10)],
             .02, wrap, caps=False)
    rod(mesh, 'Muffler', (-.48, .18, .10), (-.48, .66, .14), .05 if not tuned else .042, wrap)
    rod(mesh, 'Tailpipe', (-.48, .66, .14), (-.50, .78, .16), .022, 'dark')
    if tuned:
        rod(mesh, 'Tailpipe', (-.46, .66, .17), (-.47, .78, .20), .018, 'dark')
    for k, y in enumerate((.28, .55)):
        block(mesh, 'MufflerStrap', (-.48, y, .10), (.11, .02, .11), 'steel', bevel=.002)
        beam(mesh, 'MufflerStay', (-.48, y, .05), (-.36, y, .0), (.015, .015), 'steel')


def rotor(mesh):
    """The main rotor about its hub (the mast's top at the origin): a teetering hub, two long blades, taped tips."""
    lod = mesh.lod
    block(mesh, 'RotorHub', (0, 0, .03), (.22, .11, .08), 'dark', bevel=.01)
    rod(mesh, 'TeeterBolt', (0, -.08, .03), (0, .08, .03), .012, 'steel')
    disc(mesh, 'HubCap', (0, 0, .085), .05, .03, 'steel')
    for s in (1, -1):
        block(mesh, 'BladeGrip', (.17 * s, 0, .03), (.12, .13, .05), 'steel', bevel=.006)
        rod(mesh, 'PitchHorn', (.14 * s, .07 * s, .03), (.14 * s, .12 * s, -.03), .008, 'steel')
        block(mesh, 'Blade', (1.83 * s, 0, .03), (3.30, .19, .026), 'blade', bevel=.006)
        block(mesh, 'BladeTip', (3.41 * s, 0, .03), (.14, .195, .028), 'tape', bevel=.004)
        if lod < 2:
            for x in (1.1, 2.4):
                block(mesh, 'BladeTape', (x * s, 0, .03), (.06, .196, .029), 'cable', bevel=.002)


def jerry_tank(mesh):
    """A welded tank hung under the floor on two straps, its filler neck out to the left."""
    block(mesh, 'Tank', (0, 0, -.13), (.50, .44, .22), 'red', bevel=.025)
    if mesh.lod < 2:
        for sx in (1, -1):
            block(mesh, 'TankRib', (.12 * sx, 0, -.015), (.03, .40, .01), 'red', bevel=.004)
        beam(mesh, 'TankSeam', (-.25, 0, -.13), (.25, 0, -.13), (.012, .45), 'weld', bevel=0)
    for y in (-.12, .12):
        block(mesh, 'StrapBottom', (0, y, -.245), (.53, .04, .008), 'rubber', bevel=.002)
        for sx in (1, -1):
            block(mesh, 'StrapSide', (.258 * sx, y, -.13), (.008, .04, .24), 'rubber', bevel=.002)
    filler(mesh)


def long_tank(mesh):
    """A drum along the craft under the floor, strapped, the same filler neck."""
    rod(mesh, 'Drum', (0, -.47, -.18), (0, .47, -.18), .15, 'olive')
    for y in (-.47, .47):
        disc(mesh, 'DrumEnd', (0, y, -.18), .152, .02, 'olive', axis='Y')
    for y in (-.33, 0, .33):
        disc(mesh, 'DrumHoop', (0, y, -.18), .156, .02, 'olive', axis='Y')
    for y in (-.25, .25):
        block(mesh, 'StrapBottom', (0, y, -.335), (.32, .04, .008), 'rubber', bevel=.002)
        for sx in (1, -1):
            block(mesh, 'StrapSide', (.16 * sx, y, -.17), (.008, .04, .33), 'rubber', bevel=.002)
    filler(mesh)


def filler(mesh):
    cap = Vector(EXTRA_SOCKETS['fuel']) - Vector(SOCKETS['tank'])
    pipe(mesh, 'FillerNeck', [(.18, cap.y + .07, -.03), (.30, cap.y + .02, .05), (cap.x, cap.y, cap.z - .03)], .024, 'steel')
    disc(mesh, 'FuelCap', tuple(cap), .036, .03, 'red')


def rear_seat(mesh):
    """A seat on the rear deck looking out to the right, a footrest hung outside, a grab bar behind."""
    for sx in (1, -1):
        for sy in (1, -1):
            beam(mesh, 'SeatLeg', (.06 + .10 * sx, .17 * sy, .0), (-.04 + .10 * sx, .17 * sy, .30), (.025, .025), 'primer')
    block(mesh, 'RearSeatPan', (-.10, 0, .34), (.42, .44, .07), 'seat', bevel=.02)
    back = Matrix.Translation((.13, 0, .66)) @ rotation('Y', 12)
    place(mesh.box('RearSeatBack', (0, 0, 0), (.07, .44, .62), 'seat', .02 if mesh.lod < 2 else 0), back)
    pipe(mesh, 'GrabBar', [(.16, -.22, .90), (.18, -.22, 1.04), (.18, .22, 1.04), (.16, .22, .90)], .012, 'primer')
    pipe(mesh, 'FootrestHanger', [(-.30, -.20, .30), (-.40, -.20, .20), (-.58, -.20, -.20)], .014, 'primer')
    pipe(mesh, 'FootrestHanger', [(-.30, .20, .30), (-.40, .20, .20), (-.58, .20, -.20)], .014, 'primer')
    rod(mesh, 'Footrest', (-.58, -.26, -.20), (-.58, .26, -.20), .016, 'tread')
    if mesh.lod < 2:
        block(mesh, 'LapBelt', (-.12, 0, .385), (.05, .44, .01), 'rubber', bevel=0)


def cargo_box(mesh):
    """A lockable steel box strapped to the rear deck: its lid's hinges, two latches and a hasp."""
    block(mesh, 'CargoBox', (0, 0, .22), (.62, .56, .42), 'olive', bevel=.012)
    block(mesh, 'CargoLid', (0, 0, .45), (.64, .58, .045), 'olive', bevel=.01)
    for sy in (-.18, .18):
        rod(mesh, 'Hinge', (.32, sy - .05, .43), (.32, sy + .05, .43), .012, 'steel')
        block(mesh, 'Latch', (-.322, sy, .38), (.012, .06, .08), 'steel', bevel=.003)
    block(mesh, 'Hasp', (-.326, 0, .40), (.01, .05, .07), 'steel', bevel=.002)
    for sy in (-.2, .2):
        block(mesh, 'TieStrap', (0, sy, .475), (.66, .04, .006), 'tape', bevel=0)
        for sx in (1, -1):
            block(mesh, 'TieStrapSide', (.33 * sx, sy, .24), (.006, .04, .47), 'tape', bevel=0)


def plating(mesh):
    """Riveted plates: the cockpit's sides, a belly plate, shields either side of the engine."""
    lod = mesh.lod
    for sx in (1, -1):
        block(mesh, 'SidePlate', (.39 * sx, -.56, .66), (.008, 1.06, .42), 'olive', rotation('Y', -6 * sx), .002)
        block(mesh, 'EngineShield', (.41 * sx, .56, .74), (.008, .52, .46), 'rust', bevel=.002)
        if lod == 0:
            for y in np.linspace(-1.05, -.07, 8):
                for z in (.47, .85):
                    disc(mesh, 'Rivet', (.396 * sx, y, z), .008, .006, 'steel', axis='X', segments=8)
    block(mesh, 'BellyPlate', (0, -.55, .412), (.74, 1.16, .008), 'rust', bevel=.002)


MODULES = {
    # name: (builder, socket, in the craft as M1 flies it)
    'twin': (lambda m: twin(m, False), 'engine', True),
    'tuned-twin': (lambda m: twin(m, True), 'engine', False),
    'rotor': (rotor, 'rotor', True),
    'jerry-tank': (jerry_tank, 'tank', True),
    'long-tank': (long_tank, 'tank', False),
    'rear-seat': (rear_seat, 'rear', True),
    'cargo-box': (cargo_box, 'rear', False),
    'plating': (plating, 'plating', False),
}

# ---------------------------------------------------------------- building every level


def built(builder, name, material, lod):
    mesh = ComponentMesh(col, material, REGIONS, lod)
    builder(mesh)
    return mesh.finish(f'{name}_lod{lod}')


def shade(objects, samples=8, distance=.35):
    """Vertex occlusion over a set of objects seen together, written as the export colour."""
    occlusion = vertex_ao(objects, samples=samples, distance=distance, floor=True)
    for obj, ao in occlusion.items():
        colours = np.ones((len(ao), 4), np.float32)
        colours[:, :3] = (.5 + .5 * ao)[:, None]
        set_export_colour(obj, colours)


def empty(name, at=(0, 0, 0), parent=None):
    e = bpy.data.objects.new(name, None)
    col.objects.link(e)
    e.location = at
    if parent is not None:
        e.parent = parent
    return e


def parent_keep(child, parent, offset=(0, 0, 0)):
    """Parents a mesh built in the parent's frame to an empty placed at `offset`."""
    child.parent = parent
    child.matrix_parent_inverse = Matrix.Identity(4)
    return child


report = {'asset': KEY, 'sockets': {**{f'socket_{k}': list(v) for k, v in SOCKETS.items()}, **{f'socket_{k}': list(v) for k, v in EXTRA_SOCKETS.items()}},
          'files': {}}

# The frame and its moving tail rotor.
body = [built(frame, 'Body', mat, lod) for lod in range(3)]
glass = [built(windscreen, 'Glass', glass_mat, lod) for lod in range(3)]
lens = built(lamps, 'Lamps', lamp_mat, 0)
lens.name = lens.data.name = 'Lamps'
tail_meshes = [built(tail_rotor, 'TailRotorBlades', mat, lod) for lod in range(3)]
tail_blur = built(lambda m: blur(m, 'TailRotorBlur', .45, 'X'), 'TailRotorBlur', blur_mat, 0)
tail_blur.name = tail_blur.data.name = 'TailRotorBlur'

# Each module at every level.
module_objects = {}
for name, (builder, socket, _) in MODULES.items():
    title = ''.join(part.capitalize() for part in name.split('-'))
    module_objects[name] = [built(builder, title, mat, lod) for lod in range(3)]
rotor_blur = built(lambda m: blur(m, 'RotorBlur', 3.5, 'Z'), 'RotorBlur', blur_mat, 0)
rotor_blur.name = rotor_blur.data.name = 'RotorBlur'
for o in [rotor_blur] + [m for objs in module_objects.values() for m in objs]:
    o.location = (0, 0, 0)

# Occlusion: the craft as M1 flies it, seen together at LOD0 (modules moved to their sockets for the rays, then back).
together = [body[0]]
for name, (builder, socket, default) in MODULES.items():
    if default:
        o = module_objects[name][0]
        o.location = SOCKETS[socket]
        together.append(o)
tail_meshes[0].location = EXTRA_SOCKETS['tail_rotor']
together.append(tail_meshes[0])
bpy.context.view_layer.update()
shade(together)
for name in MODULES:
    module_objects[name][0].location = (0, 0, 0)
tail_meshes[0].location = (0, 0, 0)
tail = empty('TailRotor', EXTRA_SOCKETS['tail_rotor'])
for o in tail_meshes + [tail_blur]:
    parent_keep(o, tail)
for name, (builder, socket, default) in MODULES.items():
    if not default:
        shade([module_objects[name][0]], samples=6)
    for o in module_objects[name][1:]:
        shade([o], samples=4, distance=.25)
for o in body[1:] + tail_meshes[1:]:
    shade([o], samples=4, distance=.25)
for o in glass + [lens, rotor_blur, tail_blur]:
    set_export_colour(o, np.ones((len(o.data.vertices), 4), np.float32))

hull = convex_hull([body[0]], 'Body_hull0-convcolonly', col, max_faces=64)


def sockets(parent=None):
    out = []
    for k, v in {**SOCKETS, **EXTRA_SOCKETS}.items():
        out.append(empty(f'socket_{k}', v, parent))
    return out


def tris(objects):
    return {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objects if o.type == 'MESH'}


# The modules' own files: each in its socket's frame (an item's model and, from M4, what the craft mounts).
for name, (builder, socket, default) in MODULES.items():
    objects = module_objects[name]
    extra = []
    if name == 'rotor':
        hub = empty('Rotor')
        for o in objects:
            o.parent = hub
        rotor_blur.parent = hub
        extra = [hub, rotor_blur]
    # Its collision as a dropped item: one convex hull round its first level.
    title = ''.join(part.capitalize() for part in name.split('-'))
    module_hull = convex_hull([objects[0]], f'{title}_hull0-convcolonly', col, max_faces=48)
    path = export_glb(WORLD / f'{KEY}-{name}.glb', objects + extra + [module_hull], MATERIALS)
    bpy.data.objects.remove(module_hull)
    report['files'][path.name] = tris(objects)
    if name == 'rotor':
        for o in objects + [rotor_blur]:
            o.parent = None
        bpy.data.objects.remove(hub)

# The frame alone (ka-fa-1500-frame.glb, ships.json's model): its body, glass, lamps and tail rotor. The view mounts each
# installed module's own model at its socket (ShipView.Modules.cs, docs/MINICOPTER.md §8).
frame_nodes = list(body) + glass + [lens, tail] + tail_meshes + [tail_blur, hull]
frame_sockets = sockets()
path = export_glb(WORLD / f'{KEY}-frame.glb', frame_nodes + frame_sockets, MATERIALS)
report['files'][path.name] = tris(body + glass + tail_meshes + [hull])
for o in frame_sockets:
    bpy.data.objects.remove(o)

# The craft as its default build makes it (ka-fa-1500.glb, the item's model: its icon and the model viewer): the frame
# with the twin, the rotor, the jerry tank and the rear seat under Module_<socket> nodes.
craft = list(frame_nodes)
for name, (builder, socket, default) in MODULES.items():
    if not default:
        continue
    node = empty(f'Module_{socket}', SOCKETS[socket])
    craft.append(node)
    holder = node
    if name == 'rotor':
        holder = empty('Rotor', parent=node)
        craft.append(holder)
        rotor_blur.parent = holder
        rotor_blur.matrix_parent_inverse = Matrix.Identity(4)
        craft.append(rotor_blur)
    for o in module_objects[name]:
        o.parent = holder
        o.matrix_parent_inverse = Matrix.Identity(4)
        craft.append(o)
craft += sockets()
path = export_glb(WORLD / f'{KEY}.glb', craft, MATERIALS)
report['files'][path.name] = tris([o for o in craft if o.type == 'MESH'])
bpy.context.view_layer.update()
lo = [min((o.matrix_world @ v.co)[i] for o in body[:1] for v in o.data.vertices) for i in range(3)]
hi = [max((o.matrix_world @ v.co)[i] for o in body[:1] for v in o.data.vertices) for i in range(3)]
report['frame_bounds_blender'] = {'min': lo, 'max': hi}
bpy.ops.wm.save_as_mainfile(filepath=str(OUT / f'{KEY}-source.blend'))
(OUT / 'geometry.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
print(json.dumps(report['files']), flush=True)

# ---------------------------------------------------------------- preview renders (look-dev only, not shipped)

if PREVIEW:
    import preview_scene
    shown = [o for o in craft if o.type == 'MESH' and (o.name.endswith('_lod0') or '_lod' not in o.name) and 'hull' not in o.name]
    preview_scene.render(KEY, OUT, paths['albedo'], blur_png, lod0=shown,
                         extras={n: module_objects[n][0] for n in MODULES if not MODULES[n][2]}, sockets=SOCKETS)

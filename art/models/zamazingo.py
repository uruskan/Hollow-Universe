"""The Zamazingo (docs/VEHICLES.md §4, §7): our motorbike, a scrap-built enduro on knobbed tyres.

One model, zamazingo.glb (ships.json's kind draws it; the item's model and icon too): a black steel cradle frame, an
air-cooled single with its finned barrel and an exhaust curling up to a high silencer, a mustard tank between shrouds with
the name stencilled across a black band, a long ribbed seat, cream plastics (the high front fender, the number board with
its lamp, the tail), a box-section swingarm on a red mono-shock, the chain, and wire-spoked wheels on knobbed tyres.

The rig the view poses (ShipView.Rig.cs):
- `Fork_front`: what turns with the bars about the raked steering axis (its own up along the axis): the bars, the clamps,
  the stanchions, the number board, the high fender; under it `Wheel_front` at the hub, which slides up the fork (its
  `Steer` the sliders and the caliper, its `Spin` the wheel);
- `Wheel_rear` at the rear hub (its `Spin` the wheel and the sprocket), carried by `Swing_rear`, the swingarm at its pivot
  with its `Tip` at the axle, the chain and the lower shock mount `ShockLow_rear` on it;
- `Shock_rear`: the mono-shock from its top mount, its `Rod` and `Spring`.

Written in the vehicle's frame (vehicle_kit.py: +X left, +Y up, +Z forward; the origin on the ground under the rear axle).
No downloaded meshes or pixels: one 2048 px atlas (vehicle_paint.zamazingo) and the lamps' lenses; three LODs; vertex
occlusion.

    blender -b --factory-startup --python art/models/zamazingo.py [-- --preview]
"""
from pathlib import Path
import json
import math
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
sys.path.insert(0, str(Path(__file__).resolve().parent))
from hollow import WORLD, TEXTURES, REPO, script_args
from hollow.export import collection, export_glb, convex_hull
from hollow.godot import write_material
from hollow.textures import write_png, write_texture_import, normal_from_height
import vehicle_paint as paint
from vehicle_kit import (b, place, rotation, beam, rod, block, disc, lathe, flat_disc, grid_panel, orient, pipe, bolt, bolts, weld,
                         spring, tyre, spoked_rim, built as kit_built, shade, plain, empty, adopt, tris)

KEY = 'zamazingo'
OUT = REPO / '.artifacts/itemart' / KEY
OUT.mkdir(parents=True, exist_ok=True)
ARGS = script_args()
PREVIEW = '--preview' in ARGS

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.unit_settings.system = 'METRIC'
col = collection('Zamazingo')

# ---------------------------------------------------------------- the data's numbers (ships.json) and the bike's geometry

FRONT_HUB, FRONT_R, FRONT_W, FRONT_RIM = (0, .40, 1.52), .40, .12, .265
REAR_HUB, REAR_R, REAR_W, REAR_RIM = (0, .38, 0.0), .38, .16, .23
SEAT, FUEL_CAP = (0, .95, .45), (0, 1.02, .98)
RAKE = math.radians(26)
AXIS_UP = Vector((0, math.cos(RAKE), -math.sin(RAKE)))   # up the steering axis
AXIS_FWD = Vector((0, math.sin(RAKE), math.cos(RAKE)))   # square to it, forward
AXIS_BASE = Vector((0, .40, 1.468))                       # where the axis meets the hub's height
OFFSET = .05                                              # the fork's legs ahead of the axis
LEG_X = .085


def on_axis(t, fwd=0.0, x=0.0):
    """A point up the steering axis by t metres from the hub's height, `fwd` ahead of it, `x` to the left."""
    p = AXIS_BASE + AXIS_UP * t + AXIS_FWD * fwd
    return (x, p.y, p.z)


HEAD_LO, HEAD_HI = on_axis(.60), on_axis(.82)
CLAMP_LO_T, CLAMP_HI_T = .585, .86
PIVOT = (0, .45, .56)            # the swingarm's pivot
SHOCK_TOP, SHOCK_LOW = (0, .87, .50), (0, .475, .43)
SPRING_TOP, SPRING_LOW = .06, .07

# ---------------------------------------------------------------- textures and materials

albedo, orm, height = paint.zamazingo()
paths = {}
for key, pixels in [('albedo', albedo), ('orm', orm), ('normal', normal_from_height(height, 2.0))]:
    paths[key] = write_png(TEXTURES / KEY / f'{KEY}_{key}.png', pixels)
    write_texture_import(paths[key], normal_map=key == 'normal')
MATERIALS = {
    'M_Hollow_Zamazingo': write_material('zamazingo', 'M_Hollow_Zamazingo', albedo=paths['albedo'], orm=paths['orm'], normal=paths['normal'],
                                         normal_scale=.6, vertex_colour=True, vertex_colour_srgb=False),
    'M_Hollow_Zamazingo_Lamps': write_material('zamazingo_lamps', 'M_Hollow_Zamazingo_Lamps', albedo=paths['albedo'], emission=paths['albedo'],
                                               emission_energy=2.0, roughness=.12, vertex_colour=False),
}
mat = bpy.data.materials.new('M_Hollow_Zamazingo')
lamp_mat = bpy.data.materials.new('M_Hollow_Zamazingo_Lamps')
REGIONS = dict(paint.ZAMAZINGO_REGIONS)


def loft(mesh, name, sections, region, exponent=3.0, steps=None, smooth_=True, caps=True):
    """A closed body through cross-sections across the bike: each (z, centre y, half width, half height[, centre x]); a
    superellipse of `exponent` round each (2 an ellipse, higher a rounded box)."""
    n = steps or (24, 14, 8)[mesh.lod]
    verts, faces, uvs = [], [], []
    for i, sec in enumerate(sections):
        z, cy, hw, hh = sec[:4]
        cx = sec[4] if len(sec) > 4 else 0.0
        for k in range(n):
            a = math.tau * k / n
            c, s_ = math.cos(a), math.sin(a)
            x = cx + hw * math.copysign(abs(c) ** (2 / exponent), c)
            y = cy + hh * math.copysign(abs(s_) ** (2 / exponent), s_)
            verts.append((x, y, z))
            uvs.append((k / n, i / max(len(sections) - 1, 1)))
    rows = len(sections)
    for i in range(rows - 1):
        for k in range(n):
            a0, a1 = i * n + k, i * n + (k + 1) % n
            faces.append((a0, a1, a1 + n, a0 + n))
    if caps:
        for i, flip in ((0, True), (rows - 1, False)):
            ring = [i * n + k for k in range(n)]
            faces.append(tuple(reversed(ring)) if flip else tuple(ring))
    return mesh.mesh(name, verts, faces, region, smooth_, None)


# ---------------------------------------------------------------- the frame and what stands still on it


def frame(mesh):
    lod = mesh.lod
    T = .019
    # The head tube, its gussets.
    rod(mesh, 'HeadTube', HEAD_LO, HEAD_HI, .027, 'frame')
    for p in (HEAD_LO, HEAD_HI):
        weld(mesh, p, .034, axis=tuple(AXIS_UP))
    # Twin spars from the head over the engine to the pivot plates; the down tube splitting into the cradle.
    for s in (1, -1):
        spar = [HEAD_HI, (.07 * s, 1.05, 1.0), (.115 * s, .93, .72), (.12 * s, .70, .58), (.12 * s, .50, .56)]
        pipe(mesh, 'Spar', spar, .021, 'frame', per=(3, 2, 1)[lod])
        cradle = [(0, .62, 1.10), (.06 * s, .40, 1.04), (.08 * s, .22, .94), (.08 * s, .19, .72), (.09 * s, .24, .60), (.11 * s, .40, .56)]
        pipe(mesh, 'Cradle', cradle, T, 'frame', per=(3, 2, 1)[lod])
        # Pivot plates either side of the swingarm's pivot.
        block(mesh, 'PivotPlate', (.135 * s, .47, .56), (.012, .16, .14), 'frame', bevel=.004)
        # Seat rails back to the tail, struts down to the pivot plates.
        pipe(mesh, 'SeatRail', [(.115 * s, .92, .70), (.10 * s, .91, .30), (.085 * s, .93, -.05), (.07 * s, .96, -.30)], .014, 'frame',
             per=(2, 1, 1)[lod])
        rod(mesh, 'SeatStrut', (.12 * s, .56, .56), (.09 * s, .90, .05), .012, 'frame')
        # Footpegs: serrated, folding, on their brackets.
        block(mesh, 'PegBracket', (.14 * s, .40, .53), (.03, .06, .06), 'frame', bevel=.004)
        block(mesh, 'Footpeg', (.22 * s, .41, .53), (.13, .025, .055), 'steel', bevel=.004)
        if lod < 2:
            for k in range(4):
                block(mesh, 'PegTooth', (.22 * s, .425, .51 + k * .014), (.13, .008, .005), 'steel', bevel=0)
    rod(mesh, 'DownTube', HEAD_LO, (0, .62, 1.10), .024, 'frame')
    rod(mesh, 'PivotShaft', (-.15, .45, .56), (.15, .45, .56), .012, 'steel')
    for s in (1, -1):
        disc(mesh, 'PivotNut', (.152 * s, .45, .56), .016, .012, 'steel', axis='X', segments=6)
    rod(mesh, 'ShockCross', (-.10, .88, .50), (.10, .88, .50), .016, 'frame')
    block(mesh, 'ShockTopMount', (0, .875, .50), (.06, .05, .05), 'frame', bevel=.004)
    for z in (.85, .30, -.10):
        rod(mesh, 'RailCross', (-.10, .91, z), (.10, .91, z), .01, 'frame')
    # Skid plate under the cradle.
    block(mesh, 'SkidPlate', (0, .165, .80), (.20, .008, .40), 'alu', bevel=.003)
    if lod < 2:
        bolts(mesh, [(.08 * s, .158, z) for s in (1, -1) for z in (.65, .95)], (0, -1, 0), .007)


def engine(mesh):
    """The air-cooled single: crankcase and its covers, the finned barrel leaning forward, the finned head, carburettor and
    airbox, the header curling down and round to the silencer, the levers and the side stand."""
    lod = mesh.lod
    block(mesh, 'Crankcase', (0, .36, .78), (.20, .24, .34), 'alu', bevel=.04)
    for s in (1, -1):
        lathe(mesh, 'SideCover', [(0, 0), (0, .10), (.03, .095), (.045, .07), (.045, 0)], (.10 * s, .36, .76), 'alu', axis=(s, 0, 0),
              segments=(22, 12, 8)[lod])
        if lod < 2:
            for k in range(8):
                a = math.tau * k / 8
                bolt(mesh, (.146 * s, .36 + .09 * math.sin(a), .76 + .09 * math.cos(a)), (s, 0, 0), .006)
    # The barrel leaning forward 15 degrees, its fins, the head and its fins, the spark plug.
    lean = Vector((0, math.cos(math.radians(15)), math.sin(math.radians(15))))
    base = Vector((0, .47, .83))
    rod(mesh, 'Barrel', tuple(base), tuple(base + lean * .22), .052, 'block')
    fins = (9, 5, 0)[lod]
    for k in range(fins):
        c = base + lean * (.03 + k * .022)
        place(mesh.box('Fin', (0, 0, 0), (.15, .006, .13), 'block', 0), Matrix.Translation(c) @ rotation('X', 15))
    head = base + lean * .26
    place(mesh.box('Head', (0, 0, 0), (.16, .08, .15), 'block', .012 if lod < 2 else 0), Matrix.Translation(head) @ rotation('X', 15))
    if lod < 2:
        for k in range(3):
            place(mesh.box('HeadFin', (0, .05 + k * .018, 0), (.17, .006, .15), 'block', 0), Matrix.Translation(head) @ rotation('X', 15))
    rod(mesh, 'SparkPlug', tuple(head + Vector((.07, .02, .0))), tuple(head + Vector((.11, .04, .0))), .009, 'steel')
    if lod < 2:
        pipe(mesh, 'PlugLead', [tuple(head + Vector((.11, .04, 0))), (.12, .82, .85), (.08, .90, .70)], .005, 'cable', caps=False)
    # Carburettor behind the barrel, the rubber boot to the airbox under the seat.
    block(mesh, 'Carburettor', (0, .70, .72), (.07, .08, .09), 'alu', bevel=.01)
    rod(mesh, 'CarbBoot', (0, .72, .67), (0, .76, .55), .035, 'rubber')
    block(mesh, 'Airbox', (0, .80, .30), (.17, .14, .30), 'dark', bevel=.03)
    # The header out of the head's front, down past the cradle and round the right to the silencer, a heat shield.
    port = head + Vector((0, -.02, .07))
    header = [tuple(port), (-.02, .68, 1.08), (-.06, .46, 1.07), (-.105, .30, .96), (-.125, .29, .74), (-.135, .40, .52), (-.14, .54, .34),
              (-.14, .63, .21)]
    pipe(mesh, 'Header', header, .018, 'heat', caps=False, per=(4, 2, 1)[lod])
    rod(mesh, 'Silencer', (-.14, .62, .22), (-.15, .78, -.30), .056, 'silencer')
    disc(mesh, 'SilencerEnd', (-.150, .783, -.31), .058, .02, 'alu', axis=(0, .3, -1))
    rod(mesh, 'Tailpipe', (-.15, .785, -.31), (-.15, .79, -.36), .016, 'dark')
    block(mesh, 'HeatShield', (-.15, .37, .86), (.012, .10, .30), 'steel', turn=rotation('X', -12), bevel=.004)
    for y, z in ((.67, .12), (.74, -.12)):
        block(mesh, 'SilencerStrap', (-.145, y, z), (.12, .02, .03), 'steel', turn=rotation('X', 18), bevel=.003)
    beam(mesh, 'SilencerStay', (-.14, .72, -.05), (-.09, .92, -.05), (.015, .015), 'frame')
    # The front sprocket at the pivot, its cover; gear lever (left), brake pedal and kickstart (right).
    disc(mesh, 'SprocketCover', (.115, .45, .60), .06, .02, 'dark', axis='X')
    pipe(mesh, 'GearLever', [(.12, .44, .62), (.18, .40, .60), (.22, .42, .70)], .008, 'steel', caps=False)
    pipe(mesh, 'BrakePedal', [(-.13, .44, .52), (-.19, .38, .60), (-.22, .40, .70)], .009, 'steel', caps=False)
    pipe(mesh, 'Kickstart', [(-.16, .46, .66), (-.20, .52, .55), (-.21, .60, .44)], .01, 'steel', caps=False)
    # The side stand, folded up along the left.
    pipe(mesh, 'SideStand', [(.14, .37, .50), (.16, .30, .32), (.17, .27, .18)], .011, 'frame', caps=False)
    block(mesh, 'StandFoot', (.17, .265, .17), (.04, .01, .06), 'frame', bevel=.002)
    rod(mesh, 'StandSpring', (.13, .38, .52), (.15, .30, .40), .006, 'red')


def bodywork(mesh):
    """The tank, its shrouds with the name, the seat, the side panels and the tail."""
    lod = mesh.lod
    # Tank: from behind the head down to the seat, its cap.
    loft(mesh, 'Tank', [(1.11, 1.00, .07, .05), (1.07, .995, .14, .075), (.97, .965, .165, .085), (.83, .935, .16, .08), (.71, .915, .13, .06),
                        (.66, .91, .08, .04)], 'tank', exponent=3.2)
    lathe(mesh, 'FuelCap', [(0, 0), (0, .035), (.012, .032), (.02, .02), (.02, 0)], (0, 1.035, .98), 'alu', axis='Y', segments=(16, 10, 6)[lod])
    if lod < 2:
        rod(mesh, 'BreatherHose', (0, 1.055, .98), (.02, 1.06, .95), .003, 'cable')
    # Shrouds either side, the name across them.
    for s in (1, -1):
        grid_panel(mesh, 'Shroud', lambda u, v, s=s: ((.152 + .022 * math.sin(math.pi * v)) * s, .82 + .15 * v - .03 * u, 1.05 - .36 * u - .05 * v),
                   (6, 4, 2)[lod], (3, 2, 1)[lod], 'tank_l' if s > 0 else 'tank_r', smooth=True, want=lambda c, s=s: Vector((s, 0, 0)))
        grid_panel(mesh, 'ShroudBack', lambda u, v, s=s: ((.149 + .022 * math.sin(math.pi * v)) * s, .82 + .15 * v - .03 * u, 1.05 - .36 * u - .05 * v),
                   (6, 4, 2)[lod], (3, 2, 1)[lod], 'plastic', smooth=True, want=lambda c, s=s: Vector((-s, 0, 0)))
        if lod < 2:
            bolts(mesh, [(.168 * s, .90, 1.02), (.168 * s, .86, .70)], (s, 0, 0), .007, 'alu')
        # Side panels under the seat.
        grid_panel(mesh, 'SidePanel', lambda u, v, s=s: ((.112 + .012 * math.sin(math.pi * u)) * s, .72 + .16 * v + .02 * u, .40 - .42 * u + .06 * v * (1 - u)),
                   (5, 3, 2)[lod], 2, 'plastic', want=lambda c, s=s: Vector((s, 0, 0)))
        grid_panel(mesh, 'SidePanelBack', lambda u, v, s=s: ((.109 + .012 * math.sin(math.pi * u)) * s, .72 + .16 * v + .02 * u, .40 - .42 * u + .06 * v * (1 - u)),
                   (5, 3, 2)[lod], 2, 'dark', want=lambda c, s=s: Vector((-s, 0, 0)))
    # The seat: long, flat, ribbed, on a black base.
    loft(mesh, 'Seat', [(.86, .93, .07, .02), (.80, .955, .12, .03), (.60, .965, .125, .035), (.30, .965, .115, .035), (0, .97, .105, .035),
                        (-.22, .975, .09, .03), (-.28, .965, .06, .02)], 'seat', exponent=3.0)
    loft(mesh, 'SeatBase', [(.84, .905, .08, .015), (.60, .92, .12, .02), (0, .93, .10, .02), (-.27, .935, .07, .015)], 'dark', exponent=3.0)
    # Tail: the rear fender rising behind the seat, its lamp housing.
    loft(mesh, 'Tail', [(-.20, .92, .09, .025), (-.35, .945, .085, .02), (-.55, .99, .07, .015), (-.66, 1.01, .055, .012)], 'plastic', exponent=3.5)
    block(mesh, 'TailLampShell', (0, .975, -.62), (.08, .04, .03), 'dark', bevel=.006)


def fork(mesh):
    """What turns with the bars (built where it stands; the caller puts it under the fork's node): the clamps, the
    stanchions, the bars, levers and grips, the number board and its lamp, the high fender."""
    lod = mesh.lod
    for t, name in ((CLAMP_LO_T, 'ClampLow'), (CLAMP_HI_T, 'ClampTop')):
        c = Vector(on_axis(t, OFFSET * .5))
        place(mesh.box(name, (0, 0, 0), (.26, .035, .10), 'alu', .006 if lod < 2 else 0), Matrix.Translation(c) @ rotation('X', -math.degrees(RAKE)))
    rod(mesh, 'Steerer', on_axis(CLAMP_LO_T - .02), on_axis(CLAMP_HI_T + .03), .016, 'steel')
    disc(mesh, 'StemNut', on_axis(CLAMP_HI_T + .03), .02, .015, 'steel', axis=tuple(AXIS_UP), segments=6)
    for s in (1, -1):
        rod(mesh, 'Stanchion', on_axis(.30, OFFSET, LEG_X * s), on_axis(CLAMP_HI_T + .02, OFFSET, LEG_X * s), .022, 'chrome')
        disc(mesh, 'ForkCap', on_axis(CLAMP_HI_T + .025, OFFSET, LEG_X * s), .024, .02, 'alu', axis=tuple(AXIS_UP))
        # Bar risers on the top clamp.
        top = Vector(on_axis(CLAMP_HI_T + .02, -.01, .05 * s))
        rod(mesh, 'Riser', tuple(top), tuple(top + Vector((0, .05, -.01))), .016, 'alu')
    bar_y, bar_z = on_axis(CLAMP_HI_T)[1] + .07, on_axis(CLAMP_HI_T)[2] - .03
    bar = [(-.40, bar_y + .05, bar_z - .07), (-.30, bar_y + .03, bar_z - .03), (-.16, bar_y, bar_z), (0, bar_y, bar_z + .005),
           (.16, bar_y, bar_z), (.30, bar_y + .03, bar_z - .03), (.40, bar_y + .05, bar_z - .07)]
    pipe(mesh, 'Handlebar', bar, .011, 'chrome', per=(3, 2, 1)[lod])
    rod(mesh, 'BarPad', (-.12, bar_y + .02, bar_z + .005), (.12, bar_y + .02, bar_z + .005), .022, 'dark')
    for s in (1, -1):
        rod(mesh, 'Grip', (.29 * s, bar_y + .03, bar_z - .03), (.40 * s, bar_y + .05, bar_z - .07), .017, 'rubber')
        # The lever (clutch on the left, brake on the right) and its perch; the brake's reservoir.
        block(mesh, 'Perch', (.24 * s, bar_y + .02, bar_z - .015), (.04, .035, .035), 'dark', bevel=.004)
        pipe(mesh, 'Lever', [(.24 * s, bar_y + .02, bar_z + .0), (.30 * s, bar_y + .03, bar_z + .04), (.37 * s, bar_y + .045, bar_z + .01)],
             .006, 'alu', caps=False)
        if lod < 2:
            pipe(mesh, 'Cable', [(.24 * s, bar_y + .02, bar_z - .02), (.20 * s, bar_y - .06, bar_z + .06), (.10 * s, bar_y - .20, bar_z + .06),
                                 (.08 * s, bar_y - .34, bar_z - .02)], .004, 'cable', caps=False)
    block(mesh, 'Reservoir', (-.21, bar_y + .055, bar_z - .015), (.04, .03, .03), 'dark', bevel=.004)
    # The number board with its lamp, held off the lower clamp, square to the axis.
    plate_c = Vector(on_axis(.70, .13))
    m = Matrix.Translation(plate_c) @ rotation('X', -math.degrees(RAKE))

    def plate(u, v):
        return tuple(m @ Vector(((u - .5) * .26, (v - .5) * .30, .03 * math.sin(math.pi * u))))
    grid_panel(mesh, 'NumberBoard', plate, (6, 4, 2)[lod], 2, 'number', smooth=True, want=lambda c: b(tuple(AXIS_FWD)))
    grid_panel(mesh, 'NumberBoardBack', lambda u, v: tuple(Vector(plate(u, v)) - AXIS_FWD * .004), (6, 4, 2)[lod], 2, 'plastic', smooth=True,
               want=lambda c: -b(tuple(AXIS_FWD)))
    place(rod(mesh, 'LampShell', (0, 0, -.01), (0, 0, .05), .05, 'dark', end=.054), m @ Matrix.Translation((0, -.06, .03)))
    for s in (1, -1):
        beam(mesh, 'BoardStay', tuple(m @ Vector((.08 * s, .10, -.005))), on_axis(CLAMP_HI_T - .01, OFFSET * .5 + .04, .07 * s), (.012, .012), 'frame')
        beam(mesh, 'BoardStay', tuple(m @ Vector((.08 * s, -.12, -.005))), on_axis(CLAMP_LO_T, OFFSET * .5 + .04, .07 * s), (.012, .012), 'frame')
    # The high front fender off the lower clamp, clear of the tyre at full travel.
    fc = Vector(on_axis(CLAMP_LO_T - .03, OFFSET))

    def fender(u, v, lift=0.0):
        z = -.05 + .80 * u
        y = .06 + lift - .45 * ((z - .32) / .55) ** 2
        return (.075 * (2 * v - 1) * (1 - .25 * (2 * u - 1) ** 2), fc.y + y, fc.z + z)
    grid_panel(mesh, 'FrontFender', fender, (14, 8, 4)[lod], 2, 'plastic', want=lambda c: Vector((0, 0, 1)))
    grid_panel(mesh, 'FrontFenderUnder', lambda u, v: fender(u, v, -.004), (14, 8, 4)[lod], 2, 'plastic', want=lambda c: Vector((0, 0, -1)))
    for s in (1, -1):
        bolt(mesh, (.03 * s, fc.y + .025, fc.z), (0, 1, 0), .006, 'alu')


def front_wheel_parts(mesh):
    """Under the fork's slide (the hub's frame): the sliders, the axle clamps, the caliper on the left."""
    lod = mesh.lod
    hub = Vector(FRONT_HUB)
    for s in (1, -1):
        lo = Vector(on_axis(.02, OFFSET, LEG_X * s)) - hub
        hi = Vector(on_axis(.40, OFFSET, LEG_X * s)) - hub
        rod(mesh, 'Slider', tuple(lo), tuple(hi), .033, 'dark')
        disc(mesh, 'DustSeal', tuple(hi), .03, .02, 'rubber', axis=tuple(AXIS_UP))
        block(mesh, 'AxleClamp', (LEG_X * s, 0, 0), (.045, .07, .07), 'dark', bevel=.008)
        if lod < 2:
            bolts(mesh, [(LEG_X * s + .024 * s, -.02, z) for z in (-.02, .02)], (s, 0, 0), .006)
    block(mesh, 'Caliper', (.07, .10, -.12), (.035, .08, .06), 'red', turn=rotation('X', 35), bevel=.008)
    if lod < 2:
        leg = Vector(on_axis(.42, OFFSET, LEG_X + .03)) - Vector(FRONT_HUB)
        pipe(mesh, 'BrakeHose', [(.07, .14, -.14), (.11, .25, -.10), tuple(leg)], .005, 'hose', caps=False)


def front_wheel(mesh):
    tyre(mesh, FRONT_R, FRONT_W, FRONT_RIM, knob=.018, side=1)
    spoked_rim(mesh, FRONT_RIM, FRONT_W * .82, .042, .12, disc_r=.135, side=1)


def rear_wheel(mesh):
    tyre(mesh, REAR_R, REAR_W, REAR_RIM, knob=.022, side=1)
    spoked_rim(mesh, REAR_RIM, REAR_W * .8, .05, .14, disc_r=.11, side=-1)
    # The rear sprocket on the left, bolted to the hub.
    lod = mesh.lod
    disc(mesh, 'Sprocket', (.09, 0, 0), .105, .006, 'steel', axis='X', inner=.05, segments=(48, 24, 12)[lod])
    if lod == 0:
        for k in range(44):
            a = math.tau * k / 44
            place(mesh.box('Tooth', (0, 0, 0), (.006, .014, .008), 'steel', 0), rotation('X', -math.degrees(a)) @ Matrix.Translation((.09, .108, 0)))
    for k in range(6):
        a = math.tau * k / 6
        bolt(mesh, (.094, .065 * math.cos(a), .065 * math.sin(a)), (1, 0, 0), .006)


def swingarm(mesh):
    """The box-section swingarm from its pivot to the axle, its brace over the tyre, the chain and its guide, the axle
    blocks, the rear caliper's hanger, the shock's lower mount (built where it stands)."""
    lod = mesh.lod
    px, py, pz = PIVOT
    hy, hz = REAR_HUB[1], REAR_HUB[2]
    for s in (1, -1):
        x = .105 * s
        loft(mesh, 'Arm', [(pz + .02, py, .02, .03, x), (pz - .10, py - .01, .022, .036, x), (.20, hy + .02, .02, .03, x), (.02, hy, .018, .022, x),
                           (-.05, hy, .016, .02, x)], 'alu', exponent=4.0, steps=(12, 8, 4)[lod])
        block(mesh, 'AxleBlock', (x + .012 * s, hy, hz), (.02, .04, .05), 'dark', bevel=.004)
        disc(mesh, 'PivotBoss', (x, py, pz), .03, .05, 'alu', axis='X')
    rod(mesh, 'Brace', (-.10, py + .015, .46), (.10, py + .015, .46), .022, 'alu')
    block(mesh, 'ShockMount', (0, SHOCK_LOW[1], SHOCK_LOW[2]), (.05, .04, .04), 'alu', bevel=.004)
    rod(mesh, 'ShockPin', (-.03, SHOCK_LOW[1], SHOCK_LOW[2]), (.03, SHOCK_LOW[1], SHOCK_LOW[2]), .008, 'steel')
    # The chain on the left, front sprocket at the pivot to the rear one, its guide and slider.
    front_c, front_r = Vector((.09, py, pz + .02)), .045
    rear_c, rear_r = Vector((.09, hy, hz)), .108
    along = (front_c - rear_c).normalized()
    up = Vector((0, along.z, -along.y))
    pts = []
    steps = (16, 9, 5)[lod]
    for c, r, start in ((front_c, front_r, -math.pi / 2), (rear_c, rear_r, math.pi / 2)):
        for k in range(steps + 1):
            t = start + math.pi * k / steps
            pts.append(c + (along * math.cos(t) + up * math.sin(t)) * r)
    chain = mesh.cable('Chain', [tuple(p) for p in pts], .006, 'chain', closed=True, sides=(6, 4, 3)[lod])
    block(mesh, 'ChainSlider', (.105, py - .03, .35), (.03, .02, .30), 'dark', bevel=.006)
    block(mesh, 'ChainGuide', (.09, hy - .12, .14), (.035, .06, .10), 'dark', bevel=.006)
    # The rear caliper's hanger and caliper, on the right at the axle.
    block(mesh, 'CaliperHanger', (-.10, hy + .05, hz + .03), (.01, .10, .08), 'alu', bevel=.004)
    block(mesh, 'RearCaliper', (-.085, hy + .085, hz + .05), (.03, .07, .05), 'red', bevel=.006)


def shock_body(mesh):
    lod = mesh.lod
    disc(mesh, 'TopEye', (0, 0, 0), .022, .04, 'zinc', axis='X')
    rod(mesh, 'Body', (0, -.02, 0), (0, -.22, 0), .028, 'zinc')
    rod(mesh, 'Reservoir', (.045, -.03, 0), (.045, -.16, 0), .02, 'alu')
    rod(mesh, 'ReservoirHose', (.02, -.03, 0), (.045, -.035, 0), .006, 'hose')
    disc(mesh, 'TopSeat', (0, -.06, 0), .05, .012, 'alu', axis='Y')
    disc(mesh, 'Preload', (0, -.075, 0), .036, .012, 'red', axis='Y')


def shock_rod(mesh, rest):
    disc(mesh, 'LowEye', (0, 0, 0), .02, .04, 'zinc', axis='X')
    rod(mesh, 'Shaft', (0, .02, 0), (0, rest - .16, 0), .009, 'chrome')
    disc(mesh, 'LowSeat', (0, .07, 0), .047, .012, 'alu', axis='Y')


def coil(mesh, length):
    spring(mesh, 'Spring', (0, 0, 0), (0, -length, 0), .042, .009, 7, 'red')


def lenses(mesh):
    m = Matrix.Translation(Vector(on_axis(.70, .13))) @ rotation('X', -math.degrees(RAKE))
    centre = m @ Vector((0, -.06, .085))
    flat_disc(mesh, 'Headlamp', tuple(centre), .046, tuple(AXIS_FWD), 'lamp')
    block(mesh, 'TailLamp', (0, .975, -.637), (.07, .03, .006), 'lamp_red', bevel=0)


# ---------------------------------------------------------------- building every level


def lods(builder, name, material=mat):
    return [kit_built(col, builder, name, material, REGIONS, lod) for lod in range(3)]


body = lods(lambda m: (frame(m), engine(m), bodywork(m)), 'Body')
lens = kit_built(col, lenses, 'Lamps', lamp_mat, REGIONS, 0)
lens.name = lens.data.name = 'Lamps'
fork_meshes = lods(fork, 'Fork')
front_parts = lods(front_wheel_parts, 'Sliders')
front_wheel_meshes = lods(front_wheel, 'WheelFront')
rear_wheel_meshes = lods(rear_wheel, 'WheelRear')
arm_meshes = lods(swingarm, 'Swingarm')
rest = (Vector(SHOCK_TOP) - Vector(SHOCK_LOW)).length
shock_meshes = lods(shock_body, 'ShockBody')
rod_meshes = lods(lambda m: shock_rod(m, rest), 'ShockRod')
spring_meshes = lods(lambda m: coil(m, rest - SPRING_TOP - SPRING_LOW), 'Spring')

# Occlusion at LOD0, seen together.
for o in (front_parts[0], front_wheel_meshes[0]):
    o.location = b(FRONT_HUB)
rear_wheel_meshes[0].location = b(REAR_HUB)
together = [body[0], fork_meshes[0], front_parts[0], front_wheel_meshes[0], rear_wheel_meshes[0], arm_meshes[0]]
bpy.context.view_layer.update()
shade(together, samples=10, distance=.25)
for o in together:
    o.location = (0, 0, 0)
for objs in (body, fork_meshes, front_parts, front_wheel_meshes, rear_wheel_meshes, arm_meshes):
    for o in objs[1:]:
        shade([o], samples=4, distance=.18)
for objs in (shock_meshes, rod_meshes, spring_meshes):
    for o in objs:
        shade([o], samples=3, distance=.1)
plain([lens])

# ---------------------------------------------------------------- the rig's nodes and the file

root = empty(col, 'Zamazingo')
for o in body + [lens]:
    o.parent, o.matrix_parent_inverse = root, Matrix.Identity(4)
nodes = [root] + body + [lens]

fork_node = empty(col, 'Fork_front', on_axis(.71), root, axis_up=tuple(AXIS_UP))
for o in fork_meshes:
    adopt(o, fork_node)
front = empty(col, 'Wheel_front', FRONT_HUB, fork_node)
front_steer = empty(col, 'Steer', FRONT_HUB, front)
front_spin = empty(col, 'Spin', FRONT_HUB, front_steer)
for o in front_parts:
    o.parent, o.matrix_parent_inverse, o.matrix_basis = front_steer, Matrix.Identity(4), Matrix.Identity(4)
for o in front_wheel_meshes:
    o.parent, o.matrix_parent_inverse, o.matrix_basis = front_spin, Matrix.Identity(4), Matrix.Identity(4)
nodes += [fork_node, front, front_steer, front_spin] + fork_meshes + front_parts + front_wheel_meshes

swing = empty(col, 'Swing_rear', PIVOT, root)
tip = empty(col, 'Tip', REAR_HUB, swing)
for o in arm_meshes:
    adopt(o, swing)
rear = empty(col, 'Wheel_rear', REAR_HUB, root)
rear_steer = empty(col, 'Steer', REAR_HUB, rear)
rear_spin = empty(col, 'Spin', REAR_HUB, rear_steer)
for o in rear_wheel_meshes:
    o.parent, o.matrix_parent_inverse, o.matrix_basis = rear_spin, Matrix.Identity(4), Matrix.Identity(4)
nodes += [swing, tip, rear, rear_steer, rear_spin] + arm_meshes + rear_wheel_meshes

up = tuple(Vector(SHOCK_TOP) - Vector(SHOCK_LOW))
low = empty(col, 'ShockLow_rear', SHOCK_LOW, swing, axis_up=up)
shock = empty(col, 'Shock_rear', SHOCK_TOP, root, axis_up=up)
rod_node = empty(col, 'Rod', SHOCK_LOW, shock, axis_up=up)
seat = empty(col, 'SeatLow', tuple(Vector(SHOCK_LOW) + Vector(up).normalized() * SPRING_LOW), rod_node, axis_up=up)
spring_node = empty(col, 'Spring', tuple(Vector(SHOCK_TOP) - Vector(up).normalized() * SPRING_TOP), shock, axis_up=up)
for objs, parent in ((shock_meshes, shock), (rod_meshes, rod_node), (spring_meshes, spring_node)):
    for o in objs:
        o.parent, o.matrix_parent_inverse, o.matrix_basis = parent, Matrix.Identity(4), Matrix.Identity(4)
nodes += [low, shock, rod_node, seat, spring_node] + shock_meshes + rod_meshes + spring_meshes
for name, at in (('seat_rider', SEAT), ('fuel', FUEL_CAP), ('exhaust', (-.15, .79, -.36))):
    nodes.append(empty(col, f'socket_{name}', at, root))

hull = convex_hull([body[0]], 'Body_hull0-convcolonly', col, max_faces=48)
path = export_glb(WORLD / f'{KEY}.glb', nodes + [hull], MATERIALS)
report = {'asset': KEY, 'files': {path.name: tris([o for o in nodes if o.type == 'MESH'])}}
bpy.context.view_layer.update()
lod0 = [o for o in nodes if o.type == 'MESH' and o.name.endswith('_lod0')]
pts = [o.matrix_world @ v.co for o in lod0 for v in o.data.vertices]
report['bounds_vehicle_frame'] = {'min': [min(p.x for p in pts), min(p.z for p in pts), min(-p.y for p in pts)],
                                  'max': [max(p.x for p in pts), max(p.z for p in pts), max(-p.y for p in pts)]}
report['lod0_triangles'] = sum(tris(lod0).values())
bpy.ops.wm.save_as_mainfile(filepath=str(OUT / f'{KEY}-source.blend'))
(OUT / 'geometry.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
print(json.dumps({k: sum(v.values()) for k, v in report['files'].items()}), report['lod0_triangles'], flush=True)

if PREVIEW:
    import vehicle_preview
    vehicle_preview.render(KEY, OUT, paths['albedo'], lod0 + [lens], {
        'left': ((3.2, 1.1, 1.0), (0, .7, .75)), 'right': ((-3.0, 1.2, .2), (0, .7, .75)), 'front': ((1.6, 1.3, 3.6), (0, .75, .9)),
        'rear': ((-1.2, 1.4, -2.4), (0, .7, .5)), 'rider': ((0, 1.62, .55), (0, .9, 2.4)), 'detail': ((.9, .55, .9), (0, .45, .55)),
        'top': ((.01, 4.5, .75), (0, 0, .75)),
    })

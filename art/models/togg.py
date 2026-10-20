"""The TOGG (docs/VEHICLES.md §3, §7): our modular car, a scrap-built 4x4 on big knobbed tyres.

The chassis (togg-frame.glb, ships.json's model): a welded ladder frame in black chassis paint, its bays' bolt rails, four
corners on long-travel double wishbones with red coil-overs, half-shafts to two differentials and a transfer case, a fuel
drum, bumpers with a winch and a grille guard, round lamps, tube fenders over the rear wheels and pressed ones over the
front. Each of its modules is a model of its own, mounted by the view at its socket (ShipView.Modules.cs):
- the one-seat cab (togg-cab-one.glb): square tube and sea-green sheet, perspex in bolted strips, the name stencilled on
  its doors, a roof rack and a spot lamp; inside, a bucket seat, the wheel, the dash and its gauges; its left door the
  `Hatch` that swings open on its front hinges (ShipView.ShowHatch);
- the engine (togg-engine.glb): a cream inline six, open, with its radiator, fan and belts, the air-filter can, and two
  short stacks.
togg.glb is the car as its default build makes it (the chassis, the cab in bay 3 and the engine in the nose): the item's
model, its icon and the placement ghost.

`-- --monster` builds the Monster TOGG instead (docs/VEHICLES.md §9.9): the same chassis lifted 0.75 m on long-travel arms
over 1.7 m tyres on a 2.84 m track, no fenders, a step ladder each side, and its blown six (monster-togg-engine.glb: the
TOGG's six with a supercharger, a scoop and taller stacks); it wears the TOGG's cab and the TOGG's atlas and materials.
monster-togg-frame.glb and monster-togg.glb are its chassis and its default build. Each engine has an `Exhaust_<n>` empty
at the top of each stack, where the view puts its smoke.

The rig the view poses (ShipView.Rig.cs): each wheel a `Wheel_<key>` at its hub (what turns with the steering under its
`Steer`, what rolls under `Spin`); its wishbones and half-shaft `Arm_<key>_<n>` at their inner pivots, each with a `Tip`;
its coil-over `Shock_<key>` at the top mount, pointing at `ShockLow_<key>` on the lower arm, its `Rod` sliding and its
`Spring` squeezed.

Written in the vehicle's frame (vehicle_kit.py: +X left, +Y up, +Z forward; the rear axle's centre on the ground is the
origin, the data's own numbers). No downloaded meshes or pixels: one 2048 px atlas (vehicle_paint.togg), a perspex and the
lamps' lenses; three hand-built LODs; vertex occlusion.

    blender -b --factory-startup --python art/models/togg.py [-- --preview] [-- --monster]
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
from vehicle_kit import (b, place, rotation, toward, beam, rod, block, disc, lathe, flat_disc, quad, grid_panel, orient, pipe, bolt,
                         bolts, weld, spring, tyre, steel_rim, built as kit_built, shade, plain, empty, adopt, tris)

ARGS = script_args()
PREVIEW = '--preview' in ARGS
MONSTER = '--monster' in ARGS
KEY = 'togg'                                   # the atlas's and the materials' name: the monster wears the TOGG's
NAME = 'monster-togg' if MONSTER else KEY      # the files'
OUT = REPO / '.artifacts/itemart' / NAME
OUT.mkdir(parents=True, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.unit_settings.system = 'METRIC'
col = collection('Togg')

# ---------------------------------------------------------------- the data's numbers (ships.json, modules.json)

LIFT = .75 if MONSTER else 0.0   # the monster's chassis and everything on it over the TOGG's
if MONSTER:
    WHEEL_R, WHEEL_W, RIM_R, TRAVEL = .85, .62, .40, .60
    HUB_X, HUB_Y = 1.42, .85
else:
    WHEEL_R, WHEEL_W, RIM_R, TRAVEL = .48, .32, .23, .35
    HUB_X, HUB_Y = .975, .48
THICK = 1.45 if MONSTER else 1.0  # its arms', shafts' and coil-overs' thickness over the TOGG's
CORNERS = {  # key: (side, axle z, steers)
    'front-left': (1, 3.6, True), 'front-right': (-1, 3.6, True),
    'rear-left': (1, 0.0, False), 'rear-right': (-1, 0.0, False),
}
SOCKETS = {'bay1': (0, .98 + LIFT, -1.2), 'bay2': (0, .98 + LIFT, .2), 'bay3': (0, .98 + LIFT, 1.6), 'nose': (0, .98 + LIFT, 3.0)}
DEFAULT_BUILD = [('bay3', 'cab-one'), ('nose', 'engine')]
FUEL_CAP = (.9, .85, -1.3)                     # on the chassis as it is built (the monster's lifted with it)

RAIL_X, RAIL_LO, RAIL_HI = .50, .78, .94   # the ladder's rails: their centre line, bottom and top
DECK = .98                                 # the bays' bolt rails' top: where modules stand
REAR_END, FRONT_END = -1.40, 4.22
# A corner's geometry, the left side (mirrored by x for the right), at its axle's z.
LOWER_PIVOT, LOWER_TIP = (.36, .36), (.835, .325)   # (x, y)
UPPER_PIVOT, UPPER_TIP = (.47, .70), (.825, .655)
LOWER_SPREAD, UPPER_SPREAD = .21, .16             # the wishbones' pivots fore and aft of the axle line
SHOCK_TOP, SHOCK_LOW = (.575, .955), (.68, .375)
SHAFT_IN = (.17, .43)                               # the differential's output


def lifted(p):
    """A point of the chassis as built (x, y) where it ends up: the monster's 0.75 m higher."""
    return (p[0], p[1] + LIFT)


# The moving parts' ends: their pivots on the chassis (lifted with it), their tips at the hub; the coil-over's lower eye on
# the lower arm. The monster's long arms reach down and out from the lifted frame to its hubs.
ARM_LOWER, ARM_UPPER, ARM_SHOCK_TOP, ARM_SHAFT_IN = lifted(LOWER_PIVOT), lifted(UPPER_PIVOT), lifted(SHOCK_TOP), lifted(SHAFT_IN)
if MONSTER:
    LOWER_TIP, UPPER_TIP = (HUB_X - .14, HUB_Y - .155), (HUB_X - .15, HUB_Y + .175)
    SHOCK_LOW = (ARM_LOWER[0] + .7 * (LOWER_TIP[0] - ARM_LOWER[0]), ARM_LOWER[1] + .7 * (LOWER_TIP[1] - ARM_LOWER[1]) + .04)
STACK_TOP = 1.62 if MONSTER else 1.18   # the engine's stacks' tops over its socket, under their tips and caps

# ---------------------------------------------------------------- textures and materials

paths = {key: TEXTURES / KEY / f'{KEY}_{key}.png' for key in ('albedo', 'orm', 'normal')}
if not (MONSTER and all(p.exists() for p in paths.values())):
    albedo, orm, height = paint.togg()
    for key, pixels in [('albedo', albedo), ('orm', orm), ('normal', normal_from_height(height, 2.0))]:
        paths[key] = write_png(TEXTURES / KEY / f'{KEY}_{key}.png', pixels)
        write_texture_import(paths[key], normal_map=key == 'normal')
MATERIALS = {
    'M_Hollow_Togg': write_material('togg', 'M_Hollow_Togg', albedo=paths['albedo'], orm=paths['orm'], normal=paths['normal'],
                                    normal_scale=.6, vertex_colour=True, vertex_colour_srgb=False),
    'M_Hollow_Togg_Glass': write_material('togg_glass', 'M_Hollow_Togg_Glass', colour=(.70, .78, .76, .26), roughness=.12,
                                          metallic=0, vertex_colour=False, transparent=True, extra=['cull_mode = 2']),
    'M_Hollow_Togg_Lamps': write_material('togg_lamps', 'M_Hollow_Togg_Lamps', albedo=paths['albedo'], emission=paths['albedo'],
                                          emission_energy=2.0, roughness=.12, vertex_colour=False),
}
mat = bpy.data.materials.new('M_Hollow_Togg')
glass_mat = bpy.data.materials.new('M_Hollow_Togg_Glass')
lamp_mat = bpy.data.materials.new('M_Hollow_Togg_Lamps')
REGIONS = dict(paint.TOGG_REGIONS)
REGIONS['clear'] = (.0, .0, 1.0, 1.0)


def mirror_x(p, s):
    return (p[0] * s, p[1], p[2])


# ---------------------------------------------------------------- the chassis: what stands still


def rails(mesh):
    lod = mesh.lod
    length = FRONT_END - REAR_END
    mid = (FRONT_END + REAR_END) / 2
    for s in (1, -1):
        x = RAIL_X * s
        if lod < 2:
            # C-channel, open inward: the web outside, two flanges.
            block(mesh, 'RailWeb', (x + .036 * s, (RAIL_LO + RAIL_HI) / 2, mid), (.008, RAIL_HI - RAIL_LO, length), 'frame', bevel=.002)
            for y in (RAIL_LO + .004, RAIL_HI - .004):
                block(mesh, 'RailFlange', (x, y, mid), (.08, .008, length), 'frame', bevel=.002)
        else:
            block(mesh, 'Rail', (x, (RAIL_LO + RAIL_HI) / 2, mid), (.08, RAIL_HI - RAIL_LO, length), 'frame', bevel=0)
        # The bays' bolt rail on top, from the rear bay to the nose: flat bar, its bolts along the outside.
        block(mesh, 'BayRail', (x + .01 * s, (RAIL_HI + DECK) / 2, .9), (.09, DECK - RAIL_HI, 4.25), 'steel', bevel=.002)
        if lod < 2:
            step = (.175, .35)[lod]
            n = int(4.2 / step)
            for k in range(n + 1):
                z = -1.2 + k * step
                bolt(mesh, (x + .057 * s, DECK - .02, z), (s, 0, 0), .008)
        # The rail's ends: plates welded on.
        for z in (REAR_END, FRONT_END):
            block(mesh, 'RailEnd', (x, (RAIL_LO + RAIL_HI) / 2, z), (.09, .17, .01), 'frame', bevel=.002)
    # Cross members: round tube between the rails, welded at each end.
    for z in (-1.32, -.62, .62, 1.58, 2.32, 3.04, 4.12):
        rod(mesh, 'CrossTube', (-RAIL_X + .03, .86, z), (RAIL_X - .03, .86, z), .045, 'frame')
        for s in (1, -1):
            weld(mesh, (RAIL_X * s - .03 * s, .86, z), .05, axis='X')
    # Outriggers to the deck's edges where the bays are, the side rails along them (not over the rear wheels).
    for z in (-1.2, -.66, .66, 1.6, 2.3, 3.0):
        for s in (1, -1):
            beam(mesh, 'Outrigger', (RAIL_X * s + .04 * s, .90, z), (.985 * s, .93, z), (.05, .06), 'frame')
            if lod < 2:
                block(mesh, 'Gusset', (RAIL_X * s + .07 * s, .84, z), (.06, .1, .006), 'frame', bevel=0)
    for s in (1, -1):
        for z0, z1 in ((-1.22, -.64), (.64, 3.02)):
            beam(mesh, 'SideRail', (.985 * s, .955, z0), (.985 * s, .955, z1), (.05, .05), 'frame')
    # The deck: tread plate laid in each bay between the bolt rails and out to clear the rear wheels, bolted at its edges.
    for z0, z1 in ((-1.20, .18), (.22, 1.58), (1.62, 2.98)):
        block(mesh, 'Deck', (0, .962, (z0 + z1) / 2), (1.60, .012, z1 - z0), 'tread', bevel=.002)
        if lod == 0:
            bolts(mesh, [(x, .969, z) for x in (-.76, -.25, .25, .76) for z in (z0 + .05, z1 - .05)], (0, 1, 0), .007)


def corner_static(mesh, s, z, front):
    """What a corner hangs from: the wishbones' pivot brackets, the coil-over's top mount, a bump stop."""
    lod = mesh.lod
    for dz in (-LOWER_SPREAD, LOWER_SPREAD):
        x, y = LOWER_PIVOT
        # A hanger plate from the rail down to the lower pivot, a bolt through.
        block(mesh, 'LowerHanger', (x * s, (y + RAIL_LO) / 2, z + dz), (.06, RAIL_LO - y + .06, .008), 'frame', bevel=.002)
        block(mesh, 'LowerHanger', (x * s, (y + RAIL_LO) / 2, z + dz + .05 * (1 if dz > 0 else -1)), (.06, RAIL_LO - y + .06, .008), 'frame', bevel=.002)
        rod(mesh, 'PivotBolt', (x * s, y, z + dz - .05), (x * s, y, z + dz + .05), .011, 'steel')
        if lod < 2:
            beam(mesh, 'HangerBrace', (x * s, y + .05, z + dz), (RAIL_X * s - .03 * s, RAIL_LO, z + dz), (.03, .03), 'frame')
    for dz in (-UPPER_SPREAD, UPPER_SPREAD):
        x, y = UPPER_PIVOT
        block(mesh, 'UpperBracket', (x * s, (y + RAIL_LO) / 2 + .01, z + dz), (.07, RAIL_LO - y + .06, .008), 'frame', bevel=.002)
        rod(mesh, 'PivotBolt', (x * s, y, z + dz - .04), (x * s, y, z + dz + .04), .010, 'steel')
    # The coil-over's top mount: a tab on the rail's outside, a cross bolt.
    tx, ty = SHOCK_TOP
    block(mesh, 'ShockTower', (tx * s - .01 * s, ty, z), (.10, .07, .09), 'frame', bevel=.004)
    rod(mesh, 'ShockBolt', (tx * s, ty - .01, z - .06), (tx * s, ty - .01, z + .06), .010, 'steel')
    # A bump stop under the rail, over the lower arm.
    lathe(mesh, 'BumpStop', [(0, 0), (0, .04), (.06, .035), (.09, .02), (.09, 0)], (.60 * s, RAIL_LO, z + .12), 'rubber', axis=(0, -1, 0),
          segments=(12, 8, 6)[lod])


def diff(mesh, z, front):
    """A differential: the pumpkin, its cover's bolts, the axle tubes stub out to the shafts, hung from two tubes."""
    lod = mesh.lod
    c = (0, .43, z)
    lathe(mesh, 'DiffHousing', [(-.14, 0), (-.14, .06), (-.12, .13), (-.04, .16), (.06, .15), (.12, .10), (.14, .05), (.14, 0)],
          c, 'dark', axis=(0, 0, -1 if front else 1), segments=(20, 12, 8)[lod])
    disc(mesh, 'DiffCover', (0, .43, z + (-.145 if front else .145)), .12, .02, 'alu', axis='Z')
    if lod == 0:
        for k in range(10):
            a = math.tau * k / 10
            bolt(mesh, (.10 * math.cos(a), .43 + .10 * math.sin(a), z + (-.157 if front else .157)), (0, 0, -1 if front else 1), .007)
    for s in (1, -1):
        rod(mesh, 'DiffStub', (.10 * s, .43, z), (SHAFT_IN[0] * s, SHAFT_IN[1], z), .05, 'dark')
        disc(mesh, 'DiffFlange', (SHAFT_IN[0] * s, SHAFT_IN[1], z), .055, .02, 'steel', axis='X')
    # Pinion flange toward the transfer case; hangers to the cross tubes.
    pz = z + (-.17 if front else .17)
    disc(mesh, 'Pinion', (0, .45, pz), .05, .04, 'steel', axis='Z')
    for s in (1, -1):
        beam(mesh, 'DiffHanger', (.08 * s, .52, z), (.30 * s, RAIL_LO, z + (.25 if front else -.25)), (.035, .035), 'frame')


def drivetrain(mesh):
    """The transfer case under the cab, prop shafts to each differential with their joints, the gear linkage."""
    lod = mesh.lod
    block(mesh, 'TransferCase', (0, .52, 2.25), (.30, .24, .34), 'dark', bevel=.03)
    if lod < 2:
        block(mesh, 'TransferCover', (0, .52, 2.43), (.22, .18, .03), 'dark', bevel=.008)
        bolts(mesh, [(.09 * sx, .52 + .07 * sy, 2.446) for sx in (1, -1) for sy in (1, -1)], (0, 0, 1), .007)
    for a, b_ in (((0, .47, 2.07), (0, .45, .17)), ((0, .47, 2.43), (0, .45, 3.43))):
        rod(mesh, 'PropShaft', a, b_, .032, 'steel')
        for p in (a, b_):
            block(mesh, 'UJoint', p, (.07, .07, .06), 'dark', bevel=.008)
        if lod < 2:
            m = Vector(a).lerp(Vector(b_), .5)
            disc(mesh, 'Balance', tuple(m), .036, .03, 'steel', axis='Z')
    # Mounts to the cross tubes.
    for z in (2.12, 2.38):
        beam(mesh, 'CaseMount', (-.18, .60, z), (-RAIL_X + .04, RAIL_LO, z), (.03, .03), 'frame')
        beam(mesh, 'CaseMount', (.18, .60, z), (RAIL_X - .04, RAIL_LO, z), (.03, .03), 'frame')


def bumpers(mesh):
    lod = mesh.lod
    # Front: a box beam, its ends cut back, a winch in its middle, tow hooks under it.
    block(mesh, 'FrontBeam', (0, .80, 4.33), (1.70, .16, .14), 'frame', bevel=.008)
    for s in (1, -1):
        beam(mesh, 'BeamEnd', (.85 * s, .80, 4.33), (1.0 * s, .82, 4.24), (.14, .16), 'frame')
        # The grille guard: two uprights, hoops over the radiator, a screen of expanded mesh.
        pipe(mesh, 'GuardUpright', [(.42 * s, .88, 4.36), (.43 * s, 1.20, 4.37), (.42 * s, 1.50, 4.34), (.30 * s, 1.58, 4.30)], .028, 'frame')
        weld(mesh, (.42 * s, .88, 4.36), .04)
        # Tow hooks: red U-hooks under the beam.
        pipe(mesh, 'TowHook', [(.62 * s - .04, .72, 4.36), (.62 * s - .04, .64, 4.40), (.62 * s, .60, 4.44), (.62 * s + .04, .64, 4.40), (.62 * s + .04, .72, 4.36)],
             .016, 'red', per=(3, 2, 1)[lod])
        block(mesh, 'HookPlate', (.62 * s, .73, 4.36), (.12, .03, .06), 'frame', bevel=.002)
        # The headlamps' brackets and housings on the uprights' outsides.
        beam(mesh, 'LampBracket', (.44 * s, 1.30, 4.37), (.53 * s, 1.30, 4.37), (.03, .04), 'frame')
        rod(mesh, 'HeadlampShell', (.58 * s, 1.30, 4.32), (.58 * s, 1.30, 4.44), .085, 'dark', end=.092)
        disc(mesh, 'HeadlampRim', (.58 * s, 1.30, 4.445), .095, .014, 'steel', axis='Z', inner=.08)
        # The amber indicator on the beam's end.
        block(mesh, 'IndicatorShell', (.88 * s, .80, 4.41), (.10, .06, .03), 'dark', bevel=.006)
    pipe(mesh, 'GuardTop', [(.30, 1.58, 4.30), (0, 1.60, 4.29), (-.30, 1.58, 4.30)], .028, 'frame')
    rod(mesh, 'GuardBar', (.43, 1.20, 4.37), (-.43, 1.20, 4.37), .022, 'frame')
    for x in (-.30, -.15, 0, .15, .30):
        beam(mesh, 'GuardSlat', (x, .90, 4.362), (x, 1.47, 4.352), (.045, .008), 'frame')
    rod(mesh, 'GuardLow', (.43, .92, 4.36), (-.43, .92, 4.36), .02, 'frame')
    # The winch: drum between its end plates, the motor, the cable wound on and out through the fairlead to the hook.
    for x in (-.17, .17):
        block(mesh, 'WinchPlate', (x, .80, 4.47), (.02, .17, .15), 'frame', bevel=.003)
    rod(mesh, 'WinchDrum', (-.16, .80, 4.47), (.16, .80, 4.47), .048, 'steel')
    rod(mesh, 'WinchCable', (-.155, .80, 4.47), (.155, .80, 4.47), .062, 'cable', segments=(16, 10, 6)[lod])
    rod(mesh, 'WinchMotor', (.18, .80, 4.47), (.38, .80, 4.47), .062, 'dark')
    disc(mesh, 'WinchMotorEnd', (.385, .80, 4.47), .055, .02, 'steel', axis='X')
    rod(mesh, 'WinchGearbox', (-.18, .80, 4.47), (-.27, .80, 4.47), .058, 'red')
    block(mesh, 'Fairlead', (0, .80, 4.565), (.24, .12, .03), 'alu', bevel=.006)
    pipe(mesh, 'WinchHook', [(0, .80, 4.58), (0, .78, 4.62), (0, .72, 4.63), (.02, .69, 4.60), (.03, .71, 4.57)], .012, 'red', per=(3, 2, 1)[lod])
    # Rear: a box beam with a tread-plate step, the hitch receiver and ball, tail lamps on its ends.
    block(mesh, 'RearBeam', (0, .82, -1.45), (1.96, .16, .14), 'frame', bevel=.008)
    block(mesh, 'RearStep', (0, .905, -1.45), (1.2, .012, .16), 'tread', bevel=.002)
    block(mesh, 'Receiver', (0, .70, -1.56), (.07, .07, .26), 'frame', bevel=.004)
    rod(mesh, 'HitchShank', (0, .70, -1.60), (0, .70, -1.70), .028, 'steel')
    rod(mesh, 'HitchStem', (0, .70, -1.70), (0, .76, -1.70), .018, 'steel')
    lathe(mesh, 'HitchBall', [(-.025, 0), (-.025, .016), (-.016, .025), (0, .028), (.016, .025), (.025, .016), (.025, 0)],
          (0, .79, -1.70), 'steel', axis='Y', segments=(14, 9, 6)[lod])
    for s in (1, -1):
        beam(mesh, 'RearBrace', (RAIL_X * s, RAIL_LO, -1.30), (.70 * s, .78, -1.40), (.04, .04), 'frame')
        block(mesh, 'TailShell', (.86 * s, .84, -1.535), (.20, .09, .03), 'dark', bevel=.006)


def tank(mesh):
    """The chassis's fuel drum across the rear between the rails, strapped, its neck out to the cap on the left."""
    lod = mesh.lod
    rod(mesh, 'FuelDrum', (-.42, .62, -1.0), (.42, .62, -1.0), .17, 'red')
    for x in (-.42, .42):
        disc(mesh, 'DrumEnd', (x, .62, -1.0), .172, .02, 'red', axis='X')
    for x in (-.28, 0, .28):
        disc(mesh, 'DrumHoop', (x, .62, -1.0), .176, .02, 'red', axis='X')
    for x in (-.2, .2):
        pipe(mesh, 'DrumStrap', [(x, RAIL_LO, -1.21), (x, .62, -1.19), (x, .45, -1.12), (x, .43, -1.0), (x, .45, -.88), (x, .62, -.81),
                                 (x, RAIL_LO, -.79)], .008, 'cable', caps=False, per=(2, 1, 1)[lod])
    cx, cy, cz = FUEL_CAP
    pipe(mesh, 'FillerNeck', [(.40, .70, -1.02), (.62, .72, -1.10), (.84, .74, -1.22), (cx, cy - .06, cz)], .028, 'steel')
    disc(mesh, 'FuelCap', (cx, cy - .01, cz), .04, .03, 'red', axis='Y')
    block(mesh, 'FillerBracket', (cx, cy - .07, cz + .05), (.06, .02, .12), 'frame', bevel=.002)
    beam(mesh, 'FillerStay', (cx, cy - .07, cz + .1), (.985, .93, -1.2), (.02, .02), 'frame')


def fenders(mesh):
    lod = mesh.lod
    steps = (14, 9, 6)[lod]
    for s in (1, -1):
        # Rear: a sheet fender over the wheel in chassis black, its outer edge rolled over a tube, a mud flap behind.
        r = .70

        def rear(u, v, s=s, rr=r):
            a = math.radians(16 + 148 * u)
            return ((.80 + .38 * v) * s, HUB_Y + rr * math.sin(a), rr * math.cos(a))
        grid_panel(mesh, 'RearFender', rear, steps, 1, 'frame', want=lambda c: Vector((0, c.y, c.z - HUB_Y)))
        grid_panel(mesh, 'RearFenderUnder', lambda u, v, s=s: rear(u, v, s, r - .006), steps, 1, 'oil',
                   want=lambda c: Vector((0, -c.y, -(c.z - HUB_Y))))
        hoop = [rear(u / steps, 1, s, r + .004) for u in range(steps + 1)]
        pipe(mesh, 'FenderHoop', hoop, .016, 'frame', per=1)
        for a in (16, 164):
            p = (1.17 * s, HUB_Y + r * math.sin(math.radians(a)), r * math.cos(math.radians(a)))
            beam(mesh, 'FenderStay', p, (.985 * s, .93, p[2] * .92), (.025, .025), 'frame')
        block(mesh, 'MudFlap', (HUB_X * s, .47, -.70), (.32, .40, .008), 'rubber', bevel=.002)
        if lod < 2:
            for u in (.15, .38, .62, .85):
                bolt(mesh, rear(u, .5, s, r + .004), (0, math.sin(math.radians(16 + 148 * u)), math.cos(math.radians(16 + 148 * u))), .007)
        # Front: a pressed sheet fender over the wheel, its lip rolled, braced to the frame.
        zf = 3.6
        rf = .70

        def front(u, v, s=s):
            a = math.radians(20 + 140 * u)
            return ((.78 + .40 * v) * s, HUB_Y + rf * math.sin(a), zf + rf * math.cos(a))
        grid_panel(mesh, 'FrontFender', front, steps + 2, 2, 'paint', want=lambda c: Vector((0, c.y + zf, c.z - HUB_Y)))
        grid_panel(mesh, 'FrontFenderUnder', lambda u, v, s=s: ((.78 + .40 * v) * s, HUB_Y + (rf - .006) * math.sin(math.radians(20 + 140 * u)),
                                                               zf + (rf - .006) * math.cos(math.radians(20 + 140 * u))),
                   steps + 2, 2, 'oil', want=lambda c: Vector((0, -(c.y + zf), -(c.z - HUB_Y))))
        lip = [((1.18) * s, HUB_Y + (rf + .005) * math.sin(math.radians(a)), zf + (rf + .005) * math.cos(math.radians(a))) for a in range(20, 161, max(1, 140 // steps))]
        pipe(mesh, 'FenderLip', lip, .014, 'paint', per=1)
        inner = [((.78) * s, HUB_Y + (rf + .002) * math.sin(math.radians(a)), zf + (rf + .002) * math.cos(math.radians(a))) for a in range(20, 161, max(1, 140 // steps))]
        pipe(mesh, 'FenderInnerLip', inner, .01, 'paint', per=1)
        for a in (30, 150):
            p = ((.80) * s, HUB_Y + rf * math.sin(math.radians(a)), zf + rf * math.cos(math.radians(a)))
            beam(mesh, 'FenderBrace', p, (RAIL_X * s + .04 * s, .90, p[2] * .97 + .1), (.025, .025), 'frame')
        if lod < 2:
            for a in range(20, 170, 25):
                bolt(mesh, ((1.185) * s, HUB_Y + (rf + .005) * math.sin(math.radians(a)), zf + (rf + .005) * math.cos(math.radians(a))), (s, 0, 0), .007)


def details(mesh):
    """Steps under the cab's doors, the battery box, brake lines and wiring along the rails, the jerry can on the rear."""
    lod = mesh.lod
    for s in (1, -1):
        block(mesh, 'Step', (1.03 * s, .52, 2.30), (.20, .014, .80), 'tread', bevel=.002)
        for z in (2.0, 2.6):
            beam(mesh, 'StepHanger', (1.03 * s - .06 * s, .52, z), (RAIL_X * s + .04 * s, RAIL_LO + .02, z), (.03, .03), 'frame')
        if lod < 2:
            pipe(mesh, 'BrakeLine', [(RAIL_X * s - .04 * s, .80, 3.4), (RAIL_X * s - .045 * s, .80, 1.6), (RAIL_X * s - .045 * s, .80, .30),
                                     (RAIL_X * s - .05 * s, .78, .15)], .005, 'steel', caps=False, per=1)
            pipe(mesh, 'Loom', [(RAIL_X * s - .04 * s, .90, 4.1), (RAIL_X * s - .045 * s, .90, 1.0), (RAIL_X * s - .045 * s, .90, -1.3),
                                (.86 * s, .84, -1.50)], .009, 'cable', caps=False, per=1)
            for z in (-.9, .3, 1.5, 2.7, 3.9):
                block(mesh, 'Clip', (RAIL_X * s - .04 * s, .89, z), (.012, .03, .02), 'steel', bevel=0)
    # The battery box on the left rail's outside under the cab, its lid strapped.
    block(mesh, 'BatteryBox', (.66, .78, 2.65), (.20, .18, .34), 'frame', bevel=.008)
    block(mesh, 'BatteryLid', (.66, .875, 2.65), (.21, .014, .35), 'tread', bevel=.003)
    block(mesh, 'BatteryStrap', (.66, .80, 2.65), (.212, .19, .03), 'cable', bevel=0)
    # A jerry can stood on the rear beam's right end in its bracket.
    jx, jy, jz = -.78, .90, -1.47
    block(mesh, 'JerryCan', (jx, jy + .23, jz), (.17, .46, .34), 'red', bevel=.02)
    if lod < 2:
        for s in (1, -1):
            # The can's pressed X on each face.
            for d in (1, -1):
                beam(mesh, 'JerryX', (jx + .086 * s, jy + .06, jz - .13 * d), (jx + .086 * s, jy + .40, jz + .13 * d), (.012, .012), 'red', bevel=0)
        for z in (-.07, 0, .07):
            rod(mesh, 'JerryHandle', (jx, jy + .47, jz + z - .02), (jx, jy + .47, jz + z + .02), .012, 'red')
        rod(mesh, 'JerrySpout', (jx, jy + .44, jz + .14), (jx, jy + .49, jz + .15), .02, 'red')
    block(mesh, 'JerryCage', (jx, jy + .10, jz), (.19, .20, .36), 'frame', bevel=.004)
    block(mesh, 'JerryStrap', (jx, jy + .36, jz), (.19, .025, .36), 'cable', bevel=0)


def chassis(mesh):
    rails(mesh)
    for key, (s, z, front) in CORNERS.items():
        corner_static(mesh, s, z, front)
    diff(mesh, 0.0, False)
    diff(mesh, 3.6, True)
    drivetrain(mesh)
    bumpers(mesh)
    tank(mesh)
    if not MONSTER:
        fenders(mesh)
    details(mesh)


def ladders(mesh):
    """The monster's step ladders under its cab doors (where it stands, not lifted): two rails from the step down to a
    knee's height, three treads, a grab handle up the side."""
    lod = mesh.lod
    top = .52 + LIFT
    for s in (1, -1):
        for z in (2.0, 2.6):
            beam(mesh, 'LadderRail', (1.05 * s, top, z), (1.08 * s, .38, z), (.035, .035), 'frame')
            weld(mesh, (1.05 * s, top - .02, z), .04, axis='X')
        for y in (.42, .72, 1.0):
            block(mesh, 'LadderTread', (1.075 * s, y, 2.3), (.16, .02, .62), 'tread', bevel=.003)
            if lod < 2:
                bolts(mesh, [(1.075 * s, y + .012, z) for z in (2.03, 2.57)], (0, 1, 0), .008)


def lenses(mesh):
    for s in (1, -1):
        flat_disc(mesh, 'Headlamp', (.58 * s, 1.30, 4.448), .082, (0, 0, 1), 'lamp')
        block(mesh, 'Indicator', (.88 * s, .80, 4.428), (.085, .045, .006), 'lamp_amber', bevel=0)
        block(mesh, 'TailLamp', (.91 * s, .84, -1.553), (.08, .06, .006), 'lamp_red', bevel=0)
        block(mesh, 'TailIndicator', (.81 * s, .84, -1.553), (.08, .06, .006), 'lamp_amber', bevel=0)


# ---------------------------------------------------------------- a corner's moving parts (each in its own node's frame)


def lower_arm(mesh, s, z):
    lod = mesh.lod
    (px, py), (tx, ty) = ARM_LOWER, LOWER_TIP
    tip = (tx * s, ty, z)
    for dz in (-LOWER_SPREAD, LOWER_SPREAD):
        rod(mesh, 'LowerTube', (px * s, py, z + dz), tip, .024 * THICK, 'frame')
        disc(mesh, 'Bushing', (px * s, py, z + dz), .034 * THICK, .07, 'frame', axis='Z')
        if lod < 2:
            disc(mesh, 'BushingRubber', (px * s, py, z + dz), .022 * THICK, .074, 'rubber', axis='Z')
    rod(mesh, 'LowerBrace', ((px + .12) * s, py - .006, z - LOWER_SPREAD * .74), ((px + .12) * s, py - .006, z + LOWER_SPREAD * .74), .018 * THICK, 'frame')
    # The coil-over's lower tab, a skid plate under the arm.
    lx, ly = SHOCK_LOW
    block(mesh, 'ShockTab', (lx * s, ly - .01, z - .04), (.06, .06, .008), 'frame', bevel=.002)
    block(mesh, 'ShockTab', (lx * s, ly - .01, z + .04), (.06, .06, .008), 'frame', bevel=.002)
    rod(mesh, 'ShockPin', (lx * s, ly, z - .05), (lx * s, ly, z + .05), .009, 'steel')
    if lod < 2:
        grid_panel(mesh, 'ArmSkid', lambda u, v: (((px + .1) + (tx - px - .14) * u) * s, py - .03 - .02 * u, z + (LOWER_SPREAD * (1 - u) * .9 + .02) * (2 * v - 1)),
                   2, 1, 'frame', smooth=False, want=lambda c: Vector((0, 0, -1)))
    # The ball joint at the tip.
    disc(mesh, 'BallJoint', tip, .04 * THICK, .05, 'dark', axis='Y')


def upper_arm(mesh, s, z):
    (px, py), (tx, ty) = ARM_UPPER, UPPER_TIP
    tip = (tx * s, ty, z)
    for dz in (-UPPER_SPREAD, UPPER_SPREAD):
        rod(mesh, 'UpperTube', (px * s, py, z + dz), tip, .02 * THICK, 'frame')
        disc(mesh, 'Bushing', (px * s, py, z + dz), .03 * THICK, .06, 'frame', axis='Z')
    rod(mesh, 'UpperBrace', ((px + .1) * s, py - .01, z - UPPER_SPREAD * .7), ((px + .1) * s, py - .01, z + UPPER_SPREAD * .7), .015 * THICK, 'frame')
    disc(mesh, 'BallJoint', tip, .034 * THICK, .045, 'dark', axis='Y')


def half_shaft(mesh, s, z):
    lod = mesh.lod
    a = (ARM_SHAFT_IN[0] * s + .03 * s, ARM_SHAFT_IN[1], z)
    t = ((HUB_X - .19) * s, HUB_Y - .005, z)
    rod(mesh, 'HalfShaft', a, t, .022 * THICK, 'steel')
    for p, toward_ in ((a, t), (t, a)):
        d = (Vector(toward_) - Vector(p)).normalized()
        q = Vector(p) + d * .09
        # A CV boot: a pleated rubber cone.
        rings = [(0, .028)]
        folds = (5, 3, 2)[lod]
        for k in range(folds * 2 + 1):
            rings.append((.012 + k * .07 / (folds * 2), .055 - .022 * k / (folds * 2) + (.008 if k % 2 == 0 else 0)))
        rings += [(.09, .024), (.09, 0)]
        rings = [(0, 0)] + rings
        lathe(mesh, 'CVBoot', rings, p, 'rubber', axis=d, segments=(14, 9, 6)[lod])


def shock_body(mesh, length=.30):
    """The coil-over's body hung from the top mount (its frame: the top eye at the origin, the shock along -Y): the eye,
    the reservoir-less body in zinc, the top spring seat."""
    lod = mesh.lod
    t = THICK if MONSTER else 1
    disc(mesh, 'TopEye', (0, 0, 0), .026 * t, .04, 'zinc', axis='Z')
    rod(mesh, 'Body', (0, -.02, 0), (0, -length, 0), .033 * t, 'zinc')
    disc(mesh, 'TopSeat', (0, -.05, 0), .062 * t, .014, 'dark', axis='Y')
    disc(mesh, 'Collar', (0, -.07, 0), .04 * t, .02, 'steel', axis='Y')
    if lod < 2:
        disc(mesh, 'BodyCap', (0, -length, 0), .035 * t, .012, 'dark', axis='Y')
    if MONSTER:
        # A remote reservoir clamped to the body, its hose to the top.
        rod(mesh, 'Reservoir', (.075, -.10, 0), (.075, -.10 - length * .55, 0), .026, 'zinc')
        if lod < 2:
            pipe(mesh, 'ReservoirHose', [(.075, -.09, 0), (.06, -.03, 0), (.03, -.01, 0)], .008, 'hose', caps=False)


def shock_rod(mesh, rest, length=.30):
    """The shaft and lower eye (its frame: the lower eye at the origin, the shaft up along +Y into the body), the lower
    spring seat on it."""
    t = THICK if MONSTER else 1
    disc(mesh, 'LowEye', (0, 0, 0), .024 * t, .04, 'zinc', axis='Z')
    rod(mesh, 'Shaft', (0, .02, 0), (0, rest - (length - .1), 0), .011 * t, 'steel')
    disc(mesh, 'LowSeat', (0, .06, 0), .058 * t, .014, 'dark', axis='Y')
    block(mesh, 'BumpRubber', (0, rest - (length + .03), 0), (.03 * t, .03, .03 * t), 'rubber', bevel=.006)


SPRING_TOP, SPRING_LOW = .057, .067   # the seats' distances from the top eye and from the lower eye


def coil(mesh, length):
    if MONSTER:
        spring(mesh, 'Spring', (0, 0, 0), (0, -length, 0), .072, .015, 8, 'red')
    else:
        spring(mesh, 'Spring', (0, 0, 0), (0, -length, 0), .052, .011, 6, 'red')


def knuckle(mesh, s, front):
    """The upright round the hub bearing (its frame: the hub; turns with the steering on the front corners): the ball
    joints' bosses, the brake caliper, a steering arm on the front ones."""
    lod = mesh.lod
    ix = -s * (.135 if not MONSTER else .20)
    block(mesh, 'Upright', (ix, .01, 0), (.05 * THICK, .30, .09 * THICK), 'frame', bevel=.01)
    for y in (LOWER_TIP[1] - HUB_Y, UPPER_TIP[1] - HUB_Y):
        block(mesh, 'UprightBoss', (ix, y + (.02 if y < 0 else -.02), 0), (.06, .05, .07), 'frame', bevel=.008)
    disc(mesh, 'Bearing', (-s * .10, 0, 0), .075, .06, 'dark', axis='X')
    disc(mesh, 'BearingCap', (-s * .065, 0, 0), .06, .02, 'steel', axis='X')
    # The caliper astride the disc, behind the axle, its bleed screw and hose.
    block(mesh, 'Caliper', (-s * .055, .08, -.13), (.055, .09, .07), 'red', turn=rotation('X', -30), bevel=.01)
    if lod < 2:
        pipe(mesh, 'BrakeHose', [(-s * .07, .12, -.16), (-s * .14, .18, -.12), (-s * .25, .22, -.05)], .006, 'hose', caps=False)
    if front:
        beam(mesh, 'SteeringArm', (ix, -.05, 0), (ix - s * .05, -.07, -.17), (.035, .03), 'frame')
        disc(mesh, 'TieRodEnd', (ix - s * .05, -.07, -.18), .024, .03, 'dark', axis='Y')
        rod(mesh, 'TieRod', (ix - s * .05, -.07, -.18), (ix - s * .28, -.06, -.19), .013, 'steel')


def wheel(mesh, s):
    """A whole wheel about its hub (axle along X): the knobbed tyre, the cream beadlock rim, the brake disc."""
    if MONSTER:
        # A flotation tyre's tall sidewall and deep lugs; an eight-stud beadlock; a big vented disc.
        tyre(mesh, WHEEL_R, WHEEL_W, RIM_R, knob=.05, side=s)
        steel_rim(mesh, RIM_R, WHEEL_W - .10, dish=.06, region='cream', side=s, bolt_count=8)
        disc(mesh, 'BrakeDisc', (-s * .09, 0, 0), .28, .032, 'steel', axis='X', inner=.10)
        if mesh.lod < 2:
            disc(mesh, 'DiscHat', (-s * .06, 0, 0), .15, .04, 'dark', axis='X')
        return
    tyre(mesh, WHEEL_R, WHEEL_W, RIM_R, knob=.026, side=s)
    steel_rim(mesh, RIM_R, WHEEL_W - .06, dish=.035, region='cream', side=s)
    disc(mesh, 'BrakeDisc', (-s * .055, 0, 0), .165, .022, 'steel', axis='X', inner=.06)
    if mesh.lod < 2:
        disc(mesh, 'DiscHat', (-s * .035, 0, 0), .09, .03, 'dark', axis='X')


# ---------------------------------------------------------------- the one-seat cab (bay frame: rear centre on the deck)

CAB_W, CAB_H, CAB_Z0, CAB_Z1 = .95, 1.60, .03, 1.37
BELT = .82
WIND_LO, WIND_HI = (1.34, .84), (1.08, 1.54)   # (z, y) of the windscreen's bottom and top
DOOR_Z0, DOOR_Z1 = .34, 1.22
HINGE = (.955, .45, 1.24)


def cab_body(mesh):
    lod = mesh.lod
    T = .04
    # The floor: tread plate on two base rails that sit on the bay's bolt rails.
    for s in (1, -1):
        block(mesh, 'BaseRail', (.51 * s, .02, .70), (.09, .04, 1.34), 'frame', bevel=.003)
        bolts(mesh, [(.51 * s, .043, z) for z in (.12, .45, .95, 1.28)], (0, 1, 0), .009)
    block(mesh, 'Floor', (0, .05, .70), (1.88, .02, 1.34), 'tread', bevel=.002)
    # The cage: square tube at the corners, the A-pillars raked with the screen, the roof frame, the belt rail.
    (zl, yl), (zh, yh) = WIND_LO, WIND_HI
    for s in (1, -1):
        x = (CAB_W - T / 2) * s
        beam(mesh, 'RearPost', (x, .06, CAB_Z0 + T / 2), (x, CAB_H - .04, CAB_Z0 + T / 2), (T, T), 'paint')
        beam(mesh, 'FrontPost', (x, .06, CAB_Z1 - T / 2), (x, yl, CAB_Z1 - T / 2), (T, T), 'paint')
        beam(mesh, 'APillar', (x, yl, zl), (x * .97, yh, zh), (T, T), 'paint')
        beam(mesh, 'RoofRail', (x * .97, yh + .02, zh), (x, CAB_H - .02, CAB_Z0 + T / 2), (T, T), 'paint')
        beam(mesh, 'BeltRail', (x, BELT, CAB_Z0 + T), (x, BELT, DOOR_Z0), (T, T * .8), 'paint')
        beam(mesh, 'SillRail', (x, .07, CAB_Z0 + T), (x, .07, CAB_Z1 - T), (T, T), 'paint')
        # The fixed side panels: the rear quarter (with its window), the kick panel ahead of the door.
        block(mesh, 'QuarterPanel', (x + .002 * s, (BELT + .09) / 2, (CAB_Z0 + DOOR_Z0) / 2), (.012, BELT - .09, DOOR_Z0 - CAB_Z0 - .02), 'paint', bevel=.002)
        block(mesh, 'KickPanel', (x + .002 * s, (BELT + .09) / 2, (DOOR_Z1 + CAB_Z1) / 2 + .01), (.012, BELT - .09, CAB_Z1 - DOOR_Z1 - .02), 'paint', bevel=.002)
        beam(mesh, 'QuarterBar', (x, BELT, CAB_Z0 + T), (x, CAB_H - .05, CAB_Z0 + T), (T * .7, T * .7), 'paint')
        beam(mesh, 'DoorPost', (x, .07, DOOR_Z0 - .015), (x, CAB_H - .04, DOOR_Z0 - .015), (T, T), 'paint')
        if lod < 2:
            # The rear quarter window's bolted strip.
            for y in (BELT + .06, CAB_H - .10):
                block(mesh, 'QuarterStrip', (x + .02 * s, y, (CAB_Z0 + DOOR_Z0) / 2), (.008, .025, DOOR_Z0 - CAB_Z0 - .06), 'steel', bevel=0)
            bolts(mesh, [(x + .026 * s, y, z) for y in (BELT + .06, CAB_H - .10) for z in (.10, .27)], (s, 0, 0), .007)
            # The door's hinge knuckles (their halves on the post) and the striker on the door post.
            for y in (.25, .70):
                rod(mesh, 'HingeFixed', (HINGE[0] * s, y - .04, HINGE[2]), (HINGE[0] * s, y - .005, HINGE[2]), .012, 'steel')
            block(mesh, 'Striker', (x + .02 * s, .52, DOOR_Z0 - .015), (.015, .04, .02), 'steel', bevel=0)
            # A grab handle on the door post.
            pipe(mesh, 'GrabHandle', [(x + .02 * s, .60, DOOR_Z0 - .03), (x + .07 * s, .64, DOOR_Z0 - .03), (x + .07 * s, 1.20, DOOR_Z0 - .03),
                                      (x + .02 * s, 1.24, DOOR_Z0 - .03)], .013, 'rubber')
        # A mirror on its arm off the A-pillar.
        pipe(mesh, 'MirrorArm', [(x, .95, 1.30), (x + .10 * s, .97, 1.32), (x + .16 * s, 1.02, 1.33)], .01, 'frame')
        block(mesh, 'Mirror', (x + .18 * s, 1.08, 1.33), (.13, .17, .03), 'dark', turn=rotation('Y', 15 * s), bevel=.01)
    # Cross members: the cowl under the screen, the screen's top, the rear header, the floor's front.
    beam(mesh, 'Cowl', (-CAB_W + T, yl, zl), (CAB_W - T, yl, zl), (T, T * 1.4), 'paint')
    beam(mesh, 'ScreenHeader', (-CAB_W * .97 + T, yh + .02, zh), (CAB_W * .97 - T, yh + .02, zh), (T, T), 'paint')
    beam(mesh, 'RearHeader', (-CAB_W + T, CAB_H - .04, CAB_Z0 + T / 2), (CAB_W - T, CAB_H - .04, CAB_Z0 + T / 2), (T, T), 'paint')
    beam(mesh, 'RearBelt', (-CAB_W + T, BELT, CAB_Z0 + T / 2), (CAB_W - T, BELT, CAB_Z0 + T / 2), (T, T * .8), 'paint')
    # The front panel under the screen, the rear wall with its small window, the roof.
    block(mesh, 'FrontPanel', (0, (BELT + .07) / 2, CAB_Z1 - .006), (1.86, BELT - .07, .012), 'paint', bevel=.002)
    for y0, y1 in ((.07, .95), (1.35, CAB_H - .06)):
        block(mesh, 'RearWall', (0, (y0 + y1) / 2, CAB_Z0 + .006), (1.86, y1 - y0, .012), 'paint', bevel=.002)
    for x0, x1 in ((-.93, -.45), (.45, .93)):
        block(mesh, 'RearWall', ((x0 + x1) / 2, 1.15, CAB_Z0 + .006), (x1 - x0, .40, .012), 'paint', bevel=.002)
    for y in (.40, .62):
        block(mesh, 'RearBead', (0, y, CAB_Z0 - .004), (1.80, .025, .012), 'paint', bevel=.004)
    # A shovel strapped to the rear wall, its blade down.
    block(mesh, 'ShovelBlade', (.55, .38, CAB_Z0 - .02), (.26, .30, .012), 'steel', bevel=.004)
    rod(mesh, 'ShovelShaft', (.55, .53, CAB_Z0 - .025), (.55, 1.25, CAB_Z0 - .025), .016, 'block')
    block(mesh, 'ShovelGrip', (.55, 1.30, CAB_Z0 - .025), (.14, .03, .03), 'dark', bevel=.006)
    for y in (.65, 1.10):
        block(mesh, 'ShovelClip', (.55, y, CAB_Z0 - .02), (.06, .04, .03), 'frame', bevel=.004)
    if lod < 2:
        for y in (.96, 1.34):
            block(mesh, 'RearStrip', (0, y, CAB_Z0 - .004), (.94, .025, .008), 'steel', bevel=0)
        bolts(mesh, [(x, y, CAB_Z0 - .01) for y in (.96, 1.34) for x in (-.4, -.2, 0, .2, .4)], (0, 0, -1), .007)
    # The roof: cream sheet with pressed ribs, its drip rails.
    block(mesh, 'Roof', (0, CAB_H - .012, (CAB_Z0 + zh) / 2 + .01), (1.92, .016, zh - CAB_Z0 + .04), 'cream', bevel=.003)
    if lod < 2:
        for x in (-.6, -.3, 0, .3, .6):
            block(mesh, 'RoofRib', (x, CAB_H + .002, (CAB_Z0 + zh) / 2 + .01), (.05, .008, zh - CAB_Z0 - .06), 'cream', bevel=.003)
    # The roof rack: a tube rail round, cross bars, feet, and a spot lamp at its front.
    rack_y = CAB_H + .12
    ring = [(.84, rack_y, .10), (.84, rack_y, .98), (.80, rack_y, 1.04), (-.80, rack_y, 1.04), (-.84, rack_y, .98), (-.84, rack_y, .10),
            (-.80, rack_y, .05), (.80, rack_y, .05), (.84, rack_y, .10)]
    pipe(mesh, 'RackRail', ring, .016, 'frame', caps=False, per=(2, 1, 1)[lod])
    for z in (.30, .56, .82):
        rod(mesh, 'RackBar', (-.84, rack_y, z), (.84, rack_y, z), .014, 'frame')
    for x in (-.82, .82):
        for z in (.12, .96):
            rod(mesh, 'RackFoot', (x, CAB_H, z), (x, rack_y, z), .014, 'frame')
            block(mesh, 'RackPad', (x, CAB_H + .006, z), (.05, .012, .05), 'rubber', bevel=.002)
    block(mesh, 'SpotBracket', (0, rack_y + .03, 1.04), (.10, .05, .03), 'frame', bevel=.004)
    rod(mesh, 'SpotShell', (0, rack_y + .10, 1.0), (0, rack_y + .10, 1.12), .07, 'dark', end=.078)
    disc(mesh, 'SpotRim', (0, rack_y + .10, 1.125), .08, .012, 'steel', axis='Z', inner=.066)
    # The windscreen's bolted strips and wipers, parked.
    if lod < 2:
        for t in (0, 1):
            z = zl + (zh - zl) * t
            y = yl + (yh - yl) * t
            block(mesh, 'ScreenStrip', (0, y + (.02 if t == 0 else -.02), z + .02), (1.80, .025, .008), 'steel', bevel=0)
        for s in (1, -1):
            beam(mesh, 'ScreenSideStrip', (.86 * s, yl + .03, zl + .022), (.84 * s, yh - .03, zh + .022), (.025, .008), 'steel')
            for t in (.15, .5, .85):
                bolt(mesh, (.86 * s * (1 - t * .03), yl + (yh - yl) * t, zl + (zh - zl) * t + .028), (0, .3, 1), .007)
        for x in (-.45, .45):
            pipe(mesh, 'Wiper', [(x, yl + .05, zl + .02), (x - .30, yl + .07, zl + .01)], .006, 'dark', caps=False)
            disc(mesh, 'WiperPivot', (x, yl + .045, zl + .02), .015, .02, 'dark', axis=(0, .3, 1))
    # The interior: the bucket seat on its riser, the column and wheel, the dash and its gauges, pedals, levers.
    block(mesh, 'SeatRiser', (0, .17, .66), (.46, .22, .40), 'frame', bevel=.006)
    block(mesh, 'SeatPan', (0, .345, .66), (.50, .09, .48), 'seat', bevel=.025)
    back = Matrix.Translation((0, .78, .40)) @ rotation('X', -12)
    place(mesh.box('SeatBack', (0, 0, 0), (.50, .72, .09), 'seat', .025 if lod < 2 else 0), back)
    place(mesh.box('Headrest', (0, .44, 0), (.28, .17, .09), 'seat', .02 if lod < 2 else 0), back)
    for s in (1, -1):
        block(mesh, 'SeatBolster', (.235 * s, .40, .66), (.06, .10, .44), 'seat', bevel=.02)
        place(mesh.box('BackBolster', (.235 * s, 0, .03), (.06, .62, .08), 'seat', .02 if lod < 2 else 0), back)
        if lod < 2:
            place(mesh.box('Harness', (.11 * s, .05, .05), (.05, .66, .012), 'canvas', 0), back)
    # Dash: a strap-iron panel across, three gauges, switches, a glovebox lid.
    dash = Matrix.Translation((0, .80, 1.27)) @ rotation('X', 18)
    place(mesh.box('Dash', (0, 0, 0), (1.80, .16, .03), 'dark', .004 if lod < 2 else 0), dash)
    place(mesh.box('DashTop', (0, .09, -.05), (1.80, .02, .14), 'canvas', .004 if lod < 2 else 0), dash)
    for gx in (-.12, 0, .12):
        place(disc(mesh, 'GaugeBody', (0, 0, 0), .045, .02, 'dark', axis='Z'), dash @ Matrix.Translation((gx, .01, -.022)))
        place(flat_disc(mesh, 'GaugeDial', (0, 0, 0), .039, (0, 0, -1), 'gauge'), dash @ Matrix.Translation((gx, .01, -.033)))
    if lod < 2:
        for sx in (-.35, -.30, -.25, .28, .33):
            place(rod(mesh, 'Switch', (0, 0, 0), (0, 0, -.03), .005, 'steel'), dash @ Matrix.Translation((sx, -.02, -.016)))
        place(mesh.box('Glovebox', (.55, -.01, -.018), (.30, .10, .008), 'paint', .002), dash)
    # The column and the wheel: three spokes, a thick rim, a horn button.
    col_a, col_b = Vector((0, .70, 1.27)), Vector((0, .92, 1.04))
    rod(mesh, 'Column', tuple(col_a), tuple(col_b), .022, 'dark')
    rod(mesh, 'ColumnShroud', tuple(col_a.lerp(col_b, .1)), tuple(col_a.lerp(col_b, .45)), .04, 'dark')
    axis = (col_b - col_a).normalized()
    wheel_m = Matrix.Translation(col_b) @ axis.to_track_quat('Z', 'Y').to_matrix().to_4x4()
    rim_pts = [wheel_m @ Vector((.19 * math.cos(math.tau * k / 28), .19 * math.sin(math.tau * k / 28), 0)) for k in range((28, 18, 10)[lod])]
    mesh.cable('WheelRim', [tuple(p) for p in rim_pts], .016, 'rubber', closed=True, sides=(8, 6, 4)[lod])
    for k in range(3):
        a = math.tau * (k / 3) + math.pi / 2 + math.pi
        p = wheel_m @ Vector((.18 * math.cos(a), .18 * math.sin(a), 0))
        rod(mesh, 'WheelSpoke', tuple(col_b), tuple(p), .009, 'steel')
    disc(mesh, 'Horn', tuple(col_b + axis * .01), .045, .03, 'dark', axis=axis)
    for x, z in ((-.12, 1.16), (0, 1.16), (.12, 1.16)):
        block(mesh, 'Pedal', (x, .16, z), (.07, .10, .015), 'tread', turn=rotation('X', 25), bevel=.003)
        rod(mesh, 'PedalArm', (x, .20, z + .01), (x, .55, 1.26), .008, 'steel')
    rod(mesh, 'GearLever', (-.26, .07, .92), (-.24, .62, .88), .011, 'steel')
    lathe(mesh, 'GearKnob', [(0, 0), (0, .02), (.02, .03), (.05, .025), (.06, 0)], (-.24, .62, .88), 'dark', axis='Y', segments=(10, 7, 5)[lod])
    disc(mesh, 'GearBoot', (-.26, .09, .92), .06, .05, 'rubber', axis='Y')
    rod(mesh, 'Handbrake', (.26, .10, .78), (.26, .34, .98), .011, 'steel')
    rod(mesh, 'HandbrakeGrip', (.26, .34, .98), (.26, .39, 1.02), .016, 'rubber')
    # A fire extinguisher strapped behind the seat.
    rod(mesh, 'Extinguisher', (-.55, .08, .12), (-.55, .55, .12), .06, 'red')
    rod(mesh, 'ExtinguisherHead', (-.55, .55, .12), (-.55, .63, .12), .025, 'dark')
    block(mesh, 'ExtinguisherStrap', (-.55, .35, .12), (.13, .03, .13), 'cable', bevel=0)


def cab_door(mesh, s):
    """A door (the cab's frame; the left's built about its hinge by the caller): lower sheet with the stencilled name, a
    tube window frame raked with the screen, an inner card, a handle, the hinges' moving halves."""
    lod = mesh.lod
    x = (CAB_W + .005) * s
    (zl, yl), (zh, yh) = WIND_LO, WIND_HI
    region = 'door_l' if s > 0 else 'door_r'
    block(mesh, 'DoorSkin', (x, (BELT + .10) / 2, (DOOR_Z0 + DOOR_Z1) / 2), (.014, BELT - .10, DOOR_Z1 - DOOR_Z0), region, bevel=.003)
    block(mesh, 'DoorCard', (x - .02 * s, (BELT + .12) / 2, (DOOR_Z0 + DOOR_Z1) / 2), (.012, BELT - .14, DOOR_Z1 - DOOR_Z0 - .04), 'seat', bevel=.003)
    # The window frame: the rear upright, the top, the raked front following the A-pillar.
    top = CAB_H - .07
    front_top_z = zl + (zh - zl) * ((top - yl) / (yh - yl)) - .03
    beam(mesh, 'DoorFrameRear', (x, BELT - .01, DOOR_Z0 + .02), (x, top, DOOR_Z0 + .02), (.03, .03), 'paint')
    beam(mesh, 'DoorFrameTop', (x, top, DOOR_Z0 + .02), (x, top, front_top_z), (.03, .03), 'paint')
    beam(mesh, 'DoorFrameFront', (x, BELT - .01, DOOR_Z1 - .02), (x, top, front_top_z), (.03, .03), 'paint')
    beam(mesh, 'DoorBelt', (x, BELT, DOOR_Z0), (x, BELT, DOOR_Z1), (.035, .03), 'paint')
    if lod < 2:
        for y in (BELT + .03, top - .025):
            block(mesh, 'DoorStrip', (x + .017 * s, y, (DOOR_Z0 + DOOR_Z1) / 2 - .02), (.006, .02, DOOR_Z1 - DOOR_Z0 - .12), 'steel', bevel=0)
        bolts(mesh, [(x + .021 * s, y, z) for y in (BELT + .03, top - .025) for z in (.45, .70, .95)], (s, 0, 0), .006)
    # The handle and lock at the rear edge, the hinges' moving knuckles at the front.
    block(mesh, 'HandleBase', (x + .012 * s, .66, DOOR_Z0 + .10), (.012, .04, .14), 'dark', bevel=.003)
    pipe(mesh, 'Handle', [(x + .015 * s, .66, DOOR_Z0 + .05), (x + .04 * s, .66, DOOR_Z0 + .07), (x + .04 * s, .66, DOOR_Z0 + .14),
                          (x + .015 * s, .66, DOOR_Z0 + .16)], .009, 'steel')
    for y in (.25, .70):
        rod(mesh, 'HingeMoving', (HINGE[0] * s, y, HINGE[2]), (HINGE[0] * s, y + .05, HINGE[2]), .012, 'steel')
        block(mesh, 'HingeLeaf', (x + .006 * s, y + .025, HINGE[2] - .04), (.006, .05, .08), 'steel', bevel=0)


def cab_glass(mesh):
    """The fixed perspex: the windscreen's two halves, the rear window, the rear quarters."""
    (zl, yl), (zh, yh) = WIND_LO, WIND_HI
    quad(mesh, 'Windscreen', [(-.88, yl + .02, zl + .004), (.88, yl + .02, zl + .004), (.86, yh - .01, zh + .004), (-.86, yh - .01, zh + .004)],
         'clear', normal=(0, .3, 1))
    for s in (1, -1):
        quad(mesh, 'Quarter', [((CAB_W - .01) * s, BELT + .02, CAB_Z0 + .05), ((CAB_W - .01) * s, BELT + .02, DOOR_Z0 - .04),
                               ((CAB_W - .01) * s, CAB_H - .07, DOOR_Z0 - .04), ((CAB_W - .01) * s, CAB_H - .07, CAB_Z0 + .05)], 'clear', normal=(s, 0, 0))
    quad(mesh, 'RearWindow', [(-.46, .95, CAB_Z0 + .002), (.46, .95, CAB_Z0 + .002), (.46, 1.35, CAB_Z0 + .002), (-.46, 1.35, CAB_Z0 + .002)],
         'clear', normal=(0, 0, -1))


def door_glass(mesh, s):
    (zl, yl), (zh, yh) = WIND_LO, WIND_HI
    x = (CAB_W + .004) * s
    top = CAB_H - .085
    front_top_z = zl + (zh - zl) * ((top - yl) / (yh - yl)) - .045
    quad(mesh, 'DoorWindow', [(x, BELT + .015, DOOR_Z0 + .03), (x, BELT + .015, DOOR_Z1 - .035), (x, top, front_top_z), (x, top, DOOR_Z0 + .03)],
         'clear', normal=(s, 0, 0))


def cab_lens(mesh):
    flat_disc(mesh, 'SpotLens', (0, CAB_H + .22, 1.127), .066, (0, 0, 1), 'lamp')


# ---------------------------------------------------------------- the engine (nose frame: its socket on the deck)


def engine(mesh):
    lod = mesh.lod
    # Mounts: two cross bearers on the bolt rails, rubber blocks, the block's feet.
    for z in (.22, .86):
        block(mesh, 'Bearer', (0, .02, z), (1.10, .04, .07), 'frame', bevel=.003)
        bolts(mesh, [(.51 * s, .043, z) for s in (1, -1)], (0, 1, 0), .009)
        for s in (1, -1):
            block(mesh, 'EngineMount', (.17 * s, .07, z), (.08, .06, .07), 'rubber', bevel=.008)
    # The block: oil pan, the cast block with its core plugs, the head, the red rocker cover and its filler.
    block(mesh, 'OilPan', (0, .14, .56), (.30, .10, .66), 'dark', bevel=.02)
    block(mesh, 'Block', (0, .32, .56), (.36, .28, .70), 'block', bevel=.025)
    if lod < 2:
        for z in (.33, .56, .79):
            disc(mesh, 'CorePlug', (.182, .30, z), .022, .01, 'steel', axis='X')
        for z in np_range(.24, .88, 6 if lod == 0 else 3):
            block(mesh, 'Rib', (-.183, .30, z), (.012, .22, .02), 'block', bevel=0)
    block(mesh, 'Head', (0, .51, .56), (.32, .10, .70), 'block', bevel=.015)
    block(mesh, 'RockerCover', (0, .60, .56), (.26, .08, .64), 'red', bevel=.025)
    if lod < 2:
        for z in (.30, .48, .64, .82):
            bolt(mesh, (0, .645, z), (0, 1, 0), .009)
        for k in range(6 if lod == 0 else 3):
            block(mesh, 'CoverFin', (0, .642, .30 + k * .1), (.20, .008, .012), 'red', bevel=0)
    rod(mesh, 'OilFiller', (.06, .64, .80), (.06, .70, .80), .022, 'dark')
    # Six plugs on the right, their leads to the distributor; the coil.
    for k in range(6):
        z = .28 + k * .112
        rod(mesh, 'SparkPlug', (-.16, .52, z), (-.20, .55, z), .009, 'steel')
        if lod < 2:
            pipe(mesh, 'PlugLead', [(-.20, .55, z), (-.24, .62, z), (-.20, .70, .90 - (.90 - z) * .3), (-.08, .66, .96)], .005, 'cable', caps=False, per=1)
    rod(mesh, 'Distributor', (-.08, .40, .96), (-.08, .66, .96), .03, 'dark')
    disc(mesh, 'DistributorCap', (-.08, .67, .96), .045, .04, 'red', axis='Y')
    rod(mesh, 'Coil', (-.30, .22, .92), (-.30, .40, .92), .03, 'dark')
    # Intake on the left: the manifold, the carburettor, the round air-filter can on top.
    for k in range(6):
        z = .28 + k * .112
        pipe(mesh, 'IntakeRunner', [(.16, .48, z), (.24, .52, z), (.27, .58, (z + .56) / 2), (.20, .68, .56)], .022, 'alu', caps=False, per=1)
    block(mesh, 'Plenum', (.20, .68, .56), (.12, .06, .22), 'alu', bevel=.012)
    block(mesh, 'Carburettor', (.20, .76, .56), (.10, .10, .10), 'alu', bevel=.01)
    if MONSTER:
        blower(mesh)
    else:
        lathe(mesh, 'AirFilter', [(0, 0), (0, .21), (.012, .22), (.10, .22), (.112, .21), (.112, 0)], (.12, .82, .56), 'dark', axis='Y',
              segments=(28, 16, 10)[lod])
        disc(mesh, 'AirFilterLid', (.12, .935, .56), .19, .012, 'cream', axis='Y')
        lathe(mesh, 'WingNut', [(0, 0), (0, .02), (.02, .015), (.035, 0)], (.12, .94, .56), 'steel', axis='Y', segments=6)
        if lod < 2:
            block(mesh, 'WingNutEars', (.12, .965, .56), (.07, .02, .012), 'steel', bevel=.003)
    # Exhaust on the right: the manifold into two short stacks standing up, their rain caps.
    for k in range(6):
        z = .28 + k * .112
        pipe(mesh, 'Header', [(-.16, .42, z), (-.24, .44, z), (-.30, .46, (z + (.36 if k < 3 else .76)) / 2), (-.34, .50, .36 if k < 3 else .76)],
             .022, 'rust', caps=False, per=1)
    top = STACK_TOP
    for z in (.36, .76):
        pipe(mesh, 'Stack', [(-.34, .48, z), (-.40, .60, z), (-.42, .80, z), (-.42, top, z)], .038 if not MONSTER else .046, 'heat', caps=False)
        rod(mesh, 'StackTip', (-.42, top, z), (-.42, top + .12, z), .04 if not MONSTER else .05, 'rust')
        # A flapper cap on its hinge, a little open.
        place(disc(mesh, 'RainCap', (0, 0, 0), .046 if not MONSTER else .056, .006, 'rust', axis='Y'),
              Matrix.Translation((-.42, top + .13, z)) @ rotation('X', 18) @ Matrix.Translation((0, 0, .02)))
        rod(mesh, 'CapHinge', (-.42, top + .12, z - .045), (-.42, top + .13, z - .03), .008, 'steel')
        for y in ((.75, 1.0) if not MONSTER else (.75, 1.05, 1.35)):
            block(mesh, 'StackClamp', (-.42, y, z), (.10, .02, .10), 'steel', bevel=.004)
        beam(mesh, 'StackStay', (-.42, .90, z), (-.60, .02, z), (.02, .02), 'frame')
    # The front of the block: crank pulley, water pump and fan, the alternator, the belt.
    disc(mesh, 'CrankPulley', (0, .18, .93), .08, .03, 'steel', axis='Z')
    disc(mesh, 'PumpPulley', (0, .44, .95), .07, .03, 'steel', axis='Z')
    disc(mesh, 'AltPulley', (.17, .36, .95), .035, .03, 'steel', axis='Z')
    rod(mesh, 'Alternator', (.17, .36, .78), (.17, .36, .95), .07, 'alu')
    belt = [(0, .18 - .085, .935), (.20, .34, .955), (.16, .40, .955), (.06, .51, .955), (-.07, .44, .955), (-.08, .20, .935), (0, .18 - .085, .935)]
    pipe(mesh, 'Belt', belt, .01, 'rubber', caps=False, per=(3, 2, 1)[lod])
    # The fan in its shroud, the radiator, its hoses and cap.
    disc(mesh, 'FanHub', (0, .44, 1.0), .05, .05, 'dark', axis='Z')
    for k in range(6):
        a = math.tau * k / 6
        blade = Matrix.Translation((0, .44, 1.01)) @ rotation('Z', math.degrees(a)) @ Matrix.Translation((.12, 0, 0)) @ rotation('X', 25)
        place(mesh.box('FanBlade', (0, 0, 0), (.16, .07, .006), 'cream', .002 if lod < 2 else 0), blade)
    lathe(mesh, 'Shroud', [(0, .22), (0, .25), (.06, .25), (.08, .30), (.08, .28), (.06, .23), (.0, .22)], (0, .44, .99), 'frame', axis='Z',
          segments=(28, 16, 10)[lod])
    block(mesh, 'RadiatorCore', (0, .42, 1.09), (.78, .58, .05), 'radiator', bevel=.002)
    block(mesh, 'TankTop', (0, .74, 1.09), (.82, .07, .08), 'dark', bevel=.01)
    block(mesh, 'TankBottom', (0, .10, 1.09), (.82, .07, .08), 'dark', bevel=.01)
    for s in (1, -1):
        block(mesh, 'RadiatorSide', (.41 * s, .42, 1.09), (.025, .62, .07), 'frame', bevel=.004)
        beam(mesh, 'RadiatorStay', (.41 * s, .70, 1.09), (.30 * s, .62, .80), (.02, .02), 'frame')
        block(mesh, 'RadiatorFoot', (.41 * s, .05, 1.09), (.06, .06, .10), 'rubber', bevel=.006)
    rod(mesh, 'RadiatorCap', (0, .775, 1.09), (0, .80, 1.09), .03, 'steel')
    pipe(mesh, 'UpperHose', [(0, .52, .93), (.02, .60, 1.0), (.10, .70, 1.06)], .028, 'hose', caps=False)
    pipe(mesh, 'LowerHose', [(-.12, .22, .90), (-.22, .14, .98), (-.26, .10, 1.06)], .028, 'hose', caps=False)
    if lod < 2:
        for p in ((0, .54, .945), (.08, .69, 1.05), (-.13, .21, .91), (-.25, .11, 1.05)):
            disc(mesh, 'HoseClamp', p, .033, .012, 'steel', axis=(0, .3, 1))
    # The battery on its tray beside the block, its terminals and cables; the overflow bottle.
    block(mesh, 'BatteryTray', (.52, .04, .40), (.30, .02, .24), 'frame', bevel=.002)
    block(mesh, 'Battery', (.52, .15, .40), (.26, .20, .18), 'dark', bevel=.008)
    disc(mesh, 'TerminalPos', (.44, .26, .44), .014, .03, 'red', axis='Y')
    disc(mesh, 'TerminalNeg', (.60, .26, .44), .014, .03, 'dark', axis='Y')
    if lod < 2:
        pipe(mesh, 'BatteryCable', [(.44, .27, .44), (.38, .32, .50), (.28, .30, .60), (.19, .26, .70)], .009, 'red', caps=False)
        pipe(mesh, 'GroundCable', [(.60, .27, .44), (.66, .20, .52), (.62, .05, .60)], .009, 'cable', caps=False)
    rod(mesh, 'Overflow', (-.55, .02, .95), (-.55, .26, .95), .06, 'canvas')
    rod(mesh, 'OverflowCap', (-.55, .26, .95), (-.55, .30, .95), .025, 'dark')


def blower(mesh):
    """The monster's supercharger on the intake: a ribbed alloy case on an adaptor plate, its rotors' snout and toothed
    belt down to the crank, two carburettors on top under a black scoop with its butterfly slats."""
    lod = mesh.lod
    block(mesh, 'BlowerPlate', (.18, .74, .56), (.20, .03, .56), 'alu', bevel=.006)
    block(mesh, 'Blower', (.18, .86, .56), (.24, .20, .50), 'alu', bevel=.03)
    if lod < 2:
        for z in np_range(.36, .76, 9 if lod == 0 else 5):
            block(mesh, 'BlowerRib', (.18, .86, z), (.255, .17, .012), 'alu', bevel=.002)
        bolts(mesh, [(.18 + .10 * sx, .965, .56 + .22 * sz) for sx in (1, -1) for sz in (1, -1)], (0, 1, 0), .008)
    # The snout forward, its pulley on the rotors' shaft, the toothed belt to the crank pulley.
    rod(mesh, 'BlowerSnout', (.18, .86, .81), (.18, .86, .90), .055, 'alu')
    disc(mesh, 'BlowerPulley', (.18, .86, .93), .075, .045, 'steel', axis='Z')
    pipe(mesh, 'BlowerBelt', [(.18 + .075, .86, .935), (.08, .18, .935), (-.08, .18, .935), (.18 - .075, .86, .935), (.18 + .075, .86, .935)],
         .012, 'rubber', caps=False, per=(3, 2, 1)[lod])
    # Two carburettors on its top, the scoop over them open to the front.
    for z in (.44, .68):
        block(mesh, 'TopCarb', (.18, 1.01, z), (.12, .10, .10), 'alu', bevel=.01)
    block(mesh, 'ScoopBase', (.18, 1.08, .58), (.26, .04, .42), 'dark', bevel=.01)
    for s in (1, -1):
        block(mesh, 'ScoopSide', (.18 + .125 * s, 1.18, .58), (.02, .18, .42), 'dark', bevel=.006)
    block(mesh, 'ScoopTop', (.18, 1.275, .56), (.27, .025, .46), 'dark', bevel=.01)
    block(mesh, 'ScoopBack', (.18, 1.18, .37), (.27, .2, .02), 'dark', bevel=.006)
    for k in range(5 if lod < 2 else 2):
        y = 1.115 + k * (.13 / 4 if lod < 2 else .13)
        block(mesh, 'Butterfly', (.18, y, .785), (.23, .012, .02), 'steel', turn=rotation('X', -25), bevel=0)


def np_range(a, b_, n):
    return [a + (b_ - a) * k / max(n - 1, 1) for k in range(n)]


# ---------------------------------------------------------------- building every level

report = {'asset': NAME, 'files': {}}


def lods(builder, name, material=mat):
    return [kit_built(col, builder, name, material, REGIONS, lod) for lod in range(3)]


# The chassis and its lamps (the monster's lifted with all that is on them).
body = lods(chassis, 'Body')
lens = kit_built(col, lenses, 'Lamps', lamp_mat, REGIONS, 0)
lens.name = lens.data.name = 'Lamps'
if MONSTER:
    for o in body + [lens]:
        o.data.transform(Matrix.Translation(b((0, LIFT, 0))))
    steps = lods(ladders, 'Ladders')
else:
    steps = []

# Each corner: its wheel (meshes shared by the two corners of a side), knuckle, arms, half-shaft and coil-over.
wheel_meshes = {s: lods(lambda m, s=s: wheel(m, s), 'Wheel' + ('L' if s > 0 else 'R')) for s in (1, -1)}
corner_nodes = {}
for key, (s, z, front) in CORNERS.items():
    title = ''.join(w.capitalize() for w in key.split('-'))
    hub = (HUB_X * s, HUB_Y, z)
    k = {
        'knuckle': lods(lambda m, s=s, front=front: knuckle(m, s, front), f'Knuckle{title}'),
        'lower': lods(lambda m, s=s, z=z: lower_arm(m, s, z), f'LowerArm{title}'),
        'upper': lods(lambda m, s=s, z=z: upper_arm(m, s, z), f'UpperArm{title}'),
        'shaft': lods(lambda m, s=s, z=z: half_shaft(m, s, z), f'HalfShaft{title}'),
    }
    top = Vector((ARM_SHOCK_TOP[0] * s, ARM_SHOCK_TOP[1], z))
    low = Vector((SHOCK_LOW[0] * s, SHOCK_LOW[1], z))
    rest = (top - low).length
    k['rest'] = rest
    body_length = round(rest * .5, 3) if MONSTER else .30
    k['shock_body'] = lods(lambda m, length=body_length: shock_body(m, length), f'ShockBody{title}')
    k['shock_rod'] = lods(lambda m, rest=rest, length=body_length: shock_rod(m, rest, length), f'ShockRod{title}')
    k['spring'] = lods(lambda m, rest=rest: coil(m, rest - SPRING_TOP - SPRING_LOW), f'Spring{title}')
    if key.startswith('rear'):
        # The same side's wheel meshes again, as linked copies (one mesh, two nodes).
        k['wheel'] = []
        for o in wheel_meshes[s]:
            c = o.copy()
            col.objects.link(c)
            c.name = f'Wheel{title}_lod{o.name.split("_lod")[1]}'
            k['wheel'].append(c)
    else:
        k['wheel'] = wheel_meshes[s]
        for o in k['wheel']:
            o.name = f'Wheel{title}_lod{o.name.split("_lod")[1]}'
    corner_nodes[key] = k

# The modules, each in its socket's frame.
cab_parts = {
    'body': lods(cab_body, 'Cab'),
    'door': lods(lambda m: cab_door(m, 1), 'CabDoor'),
    'door_r': lods(lambda m: cab_door(m, -1), 'CabDoorRight'),
    'glass': lods(cab_glass, 'CabGlass', glass_mat),
    'door_glass': lods(lambda m: door_glass(m, 1), 'CabDoorGlass', glass_mat),
    'door_glass_r': lods(lambda m: door_glass(m, -1), 'CabDoorGlassRight', glass_mat),
}
cab_spot = kit_built(col, cab_lens, 'CabLamps', lamp_mat, REGIONS, 0)
cab_spot.name = cab_spot.data.name = 'CabLamps'
engine_parts = {'body': lods(engine, 'Engine')}

# ---------------------------------------------------------------- occlusion: the car as its default build makes it, at LOD0


def placed_copy(objs, offset):
    for o in objs:
        o.location = b(offset)


together = [body[0]] + steps[:1]
for key, k in corner_nodes.items():
    s, z, front = CORNERS[key]
    for part in ('knuckle', 'wheel'):
        k[part][0].location = b((HUB_X * s, HUB_Y, z))
    together += [k['knuckle'][0], k['lower'][0], k['upper'][0], k['shaft'][0]]
    if key in ('front-left', 'front-right'):
        together.append(k['wheel'][0])
cab_at, engine_at = SOCKETS['bay3'], SOCKETS['nose']
for objs in cab_parts.values():
    objs[0].location = b(cab_at)
together += [cab_parts['body'][0], cab_parts['door'][0], cab_parts['door_r'][0]]
engine_parts['body'][0].location = b(engine_at)
together.append(engine_parts['body'][0])
bpy.context.view_layer.update()
shade(together)
for o in together + [objs[0] for objs in cab_parts.values()]:
    o.location = (0, 0, 0)
for k in corner_nodes.values():
    k['wheel'][0].location = (0, 0, 0)
bpy.context.view_layer.update()
for o in body[1:] + steps[1:] + cab_parts['body'][1:] + cab_parts['door'][1:] + cab_parts['door_r'][1:] + engine_parts['body'][1:]:
    shade([o], samples=4, distance=.25)
for k in corner_nodes.values():
    for part in ('knuckle', 'lower', 'upper', 'shaft'):
        for o in k[part][1:]:
            shade([o], samples=3, distance=.2)
    for part in ('shock_body', 'shock_rod', 'spring'):
        for o in k[part]:
            shade([o], samples=3, distance=.12)
for s in (1, -1):
    for o in wheel_meshes[s][1:]:
        shade([o], samples=3, distance=.2)
glass_objects = [o for objs in (cab_parts['glass'], cab_parts['door_glass'], cab_parts['door_glass_r']) for o in objs]
plain(glass_objects + [lens, cab_spot])

# ---------------------------------------------------------------- the rig's nodes


def rig(parent=None):
    """Every corner's nodes under `parent` (the chassis's root), its meshes adopted into them. Returns the nodes."""
    nodes = []
    for key, k in corner_nodes.items():
        s, z, front = CORNERS[key]
        hub = (HUB_X * s, HUB_Y, z)
        w = empty(col, f'Wheel_{key}', hub, parent)
        steer = empty(col, 'Steer', hub, w)
        spin = empty(col, 'Spin', hub, steer)
        for o in k['knuckle']:
            o.parent, o.matrix_parent_inverse = steer, Matrix.Identity(4)
            o.matrix_basis = Matrix.Identity(4)
        for o in k['wheel']:
            o.parent, o.matrix_parent_inverse = spin, Matrix.Identity(4)
            o.matrix_basis = Matrix.Identity(4)
        nodes += [w, steer, spin] + k['knuckle'] + k['wheel']
        # The arms at their inner pivots, each with its tip.
        arms = {
            'lower': ((ARM_LOWER[0] * s, ARM_LOWER[1], z), (LOWER_TIP[0] * s, LOWER_TIP[1], z)),
            'upper': ((ARM_UPPER[0] * s, ARM_UPPER[1], z), (UPPER_TIP[0] * s, UPPER_TIP[1], z)),
            'shaft': ((ARM_SHAFT_IN[0] * s, ARM_SHAFT_IN[1], z), ((HUB_X - .19) * s, HUB_Y - .005, z)),
        }
        arm_nodes = {}
        for name, (pivot, tip) in arms.items():
            a = empty(col, f'Arm_{key}_{name}', pivot, parent)
            t = empty(col, 'Tip', tip, a)
            for o in k[name]:
                adopt(o, a)
            arm_nodes[name] = a
            nodes += [a, t] + k[name]
        # The coil-over: its top mount pointing at its lower eye on the lower arm.
        top = (ARM_SHOCK_TOP[0] * s, ARM_SHOCK_TOP[1], z)
        low = (SHOCK_LOW[0] * s, SHOCK_LOW[1], z)
        up = tuple(Vector(top) - Vector(low))
        target = empty(col, f'ShockLow_{key}', low, arm_nodes['lower'], axis_up=up)
        shock = empty(col, f'Shock_{key}', top, parent, axis_up=up)
        rod_node = empty(col, 'Rod', low, shock, axis_up=up)
        seat = empty(col, 'SeatLow', tuple(Vector(low) + Vector(up).normalized() * SPRING_LOW), rod_node, axis_up=up)
        spring_node = empty(col, 'Spring', tuple(Vector(top) - Vector(up).normalized() * SPRING_TOP), shock, axis_up=up)
        for o in k['shock_body']:
            o.parent, o.matrix_parent_inverse, o.matrix_basis = shock, Matrix.Identity(4), Matrix.Identity(4)
        for o in k['shock_rod']:
            o.parent, o.matrix_parent_inverse, o.matrix_basis = rod_node, Matrix.Identity(4), Matrix.Identity(4)
        for o in k['spring']:
            o.parent, o.matrix_parent_inverse, o.matrix_basis = spring_node, Matrix.Identity(4), Matrix.Identity(4)
        nodes += [target, shock, rod_node, seat, spring_node] + k['shock_body'] + k['shock_rod'] + k['spring']
    return nodes


def sockets(parent=None):
    out = []
    for k, v in SOCKETS.items():
        out.append(empty(col, f'socket_{k}', v, parent))
    out.append(empty(col, 'socket_fuel', (FUEL_CAP[0], FUEL_CAP[1] + LIFT, FUEL_CAP[2]), parent))
    return out


# Hulls (as a dropped or placed item: the item's collision).
hull = convex_hull([body[0]], 'Body_hull0-convcolonly', col, max_faces=64)

# The shock's parts are built in its own frame: turn them from the vehicle's axes into the shock's (local -Y down its axis).
# (They were built along -Y already; the empties carry the tilt.)

# ---------------------------------------------------------------- the files

# The modules' own files, each in its socket's frame.
cab_root = empty(col, 'CabRoot')
hatch = empty(col, 'Hatch', HINGE, cab_root)
hatch.rotation_euler = (math.radians(-90), 0, 0)  # its own up (Godot's local +Z, Blender's -Y) along the vehicle's up
hatch.location = b(HINGE)
bpy.context.view_layer.update()
for o in cab_parts['door'] + cab_parts['door_glass']:
    adopt(o, hatch)
for o in cab_parts['body'] + cab_parts['door_r'] + cab_parts['glass'] + cab_parts['door_glass_r'] + [cab_spot]:
    o.parent, o.matrix_parent_inverse = cab_root, Matrix.Identity(4)
cab_hull = convex_hull([cab_parts['body'][0]], 'Cab_hull0-convcolonly', col, max_faces=48)
cab_nodes = [cab_root, hatch] + [o for objs in cab_parts.values() for o in objs] + [cab_spot]
if not MONSTER:
    path = export_glb(WORLD / f'{KEY}-cab-one.glb', cab_nodes + [cab_hull], MATERIALS)
    report['files'][path.name] = tris([o for o in cab_nodes if o.type == 'MESH'])

engine_root = empty(col, 'EngineRoot')
for o in engine_parts['body']:
    o.parent, o.matrix_parent_inverse = engine_root, Matrix.Identity(4)
# Where its smoke comes out: over each stack's rain cap (the view's exhaust, ShipView.Exhaust.cs).
exhausts = [empty(col, f'Exhaust_{n}', (-.42, STACK_TOP + .17, z), engine_root, axis_up=(0, 1, 0)) for n, z in enumerate((.36, .76))]
engine_hull = convex_hull([engine_parts['body'][0]], 'Engine_hull0-convcolonly', col, max_faces=48)
path = export_glb(WORLD / f'{NAME}-engine.glb', [engine_root] + exhausts + engine_parts['body'] + [engine_hull], MATERIALS)
report['files'][path.name] = tris(engine_parts['body'])

# The chassis alone (togg-frame.glb, ships.json's model), its rig and sockets.
frame_root = empty(col, 'Chassis')
for o in body + steps + [lens]:
    o.parent, o.matrix_parent_inverse = frame_root, Matrix.Identity(4)
rig_nodes = rig(frame_root)
frame_sockets = sockets(frame_root)
frame_nodes = [frame_root] + body + steps + [lens] + rig_nodes + frame_sockets
path = export_glb(WORLD / f'{NAME}-frame.glb', frame_nodes + [hull], MATERIALS)
report['files'][path.name] = tris([o for o in frame_nodes if o.type == 'MESH'])

# The car as its default build makes it (togg.glb): the chassis with its modules under Module_<socket>_<level> nodes.
car = list(frame_nodes) + [hull]
for socket, module in DEFAULT_BUILD:
    node = empty(col, f'Module_{socket}_1', SOCKETS[socket], frame_root)
    root = cab_root if module == 'cab-one' else engine_root
    root.parent, root.matrix_parent_inverse = node, Matrix.Identity(4)
    root.matrix_basis = Matrix.Identity(4)
    car.append(node)
    car += cab_nodes if module == 'cab-one' else [engine_root] + exhausts + engine_parts['body']
path = export_glb(WORLD / f'{NAME}.glb', car, MATERIALS)
report['files'][path.name] = tris([o for o in car if o.type == 'MESH'])

bpy.context.view_layer.update()
pts = [o.matrix_world @ v.co for o in [o for o in car if o.type == 'MESH' and o.name.endswith('_lod0')] for v in o.data.vertices]
report['bounds_vehicle_frame'] = {'min': [min(p.x for p in pts), min(p.z for p in pts), min(-p.y for p in pts)],
                                  'max': [max(p.x for p in pts), max(p.z for p in pts), max(-p.y for p in pts)]}
report['lod0_triangles'] = sum(v for name, v in tris(car).items() if name.endswith('_lod0'))
bpy.ops.wm.save_as_mainfile(filepath=str(OUT / f'{NAME}-source.blend'))
(OUT / 'geometry.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
print(json.dumps({k: sum(v.values()) for k, v in report['files'].items()}), report['lod0_triangles'], flush=True)

# ---------------------------------------------------------------- preview renders (look-dev only, not shipped)

if PREVIEW:
    import vehicle_preview
    shown = [o for o in car if o.type == 'MESH' and o.name.endswith('_lod0') or o in (lens, cab_spot)]
    k = 1.45 if MONSTER else 1.0
    vehicle_preview.render(NAME, OUT, paths['albedo'], shown, {
        'front': ((4.6 * k, 1.9 * k, 8.6 * k), (0, .9 + LIFT, 1.6)), 'rear': ((-4.2 * k, 2.6 * k, -5.4 * k), (0, 1.0 + LIFT, 1.2)),
        'side': ((8.8 * k, 1.3 * k, 1.6), (0, 1.0 + LIFT * .6, 1.5)), 'low': ((2.6 * k, .45, 5.6 * k), (0, .7, 3.0)),
        'corner': ((1.9 * k, .8 * k, 4.6 * k), (HUB_X - .05, HUB_Y, 3.6)), 'cab': ((0, 1.24 + .98 + LIFT, .75 + 1.6), (0, 1.5 + LIFT, 6.0)),
        'top': ((.01, 12 * k, 1.4), (0, 0, 1.4)), 'under': ((3.2 * k, -.9, 1.4), (0, .6 + LIFT, 1.6)),
    })

"""Grid Substation (MONUMENTS.md §5.1, roadside tier): a fenced switchyard left on an old power line: two
transformers behind a blast wall, breakers and a gantry taking the line in from a lattice pylon, a control house,
lamps, cable trenches. Spawns along the old lines; during a Surge (§6.3) it powers up: lamps on, arcing, its locker
opens.
"""
import math

from monkit.kit import (CONCRETE, CONCRETE_T, DARK, FORMWORK, GREY, LIGHTGREY, PAINT, YELLOW, BLACK, block_building,
                        cable, fence, insulator, lattice_tower, rubble, srgb, stripes)

KEY = "grid-substation"
TITLE = "Grid Substation"
SEED = 11
BUILDER = {"chunk": 32.0}

W, D = 40.0, 28.0


def pylon(b, at, height=24.0, rot=(0, 0, 0)):
    """A lattice pylon with two cross arms and hanging insulator strings; returns the arm tips (line ends)."""
    lattice_tower(b, at, height, 5.0, 1.6, rot=rot, sections=6, member=0.14, tint=srgb(0.55, 0.57, 0.58))
    tips = []
    with b.frame(at, rot):
        for z, span in ((height - 4.0, 6.0), (height - 0.5, 4.2)):
            for y in (-0.7, 0.7):
                b.beam((-span, y * 0.4, z), (span, y * 0.4, z), 0.12, 0.12, tint=srgb(0.55, 0.57, 0.58))
            for s in (-1, 1):
                b.beam((s * span, 0, z), (s * 0.8, 0, z + 1.6), 0.1, 0.1, tint=srgb(0.55, 0.57, 0.58))
                insulator(b, (s * (span - 0.3), 0, z - 1.6), 1.6, tint=srgb(0.82, 0.82, 0.8))
                tips.append(b.world((s * (span - 0.3), 0, z - 1.6)))
        b.box((0.3, 0.3, 1.2), (0, 0, height + 0.6), tint=srgb(0.55, 0.57, 0.58), solid=False)
    return tips


def build(b):
    b.set_pad(("rect", W + 10, D + 10), ground="gravel", blend=10.0, clear=4.0)
    # Yard floor: gravel inside, a concrete apron along the front, cable trench covers between the equipment.
    b.box((W, D, 0.08), (0, 0, 0.04), mat=CONCRETE, tint=srgb(0.62, 0.6, 0.56), wear=0.6)
    for y in (-3.0, 7.5):
        for i in range(int((W - 6) / 1.5)):
            x = -W / 2 + 3 + i * 1.5
            b.box((1.45, 0.8, 0.12), (x, y, 0.1), mat=CONCRETE, tint=CONCRETE_T, bevel=0.02, solid=False)
    for i in range(7):
        b.box((0.8, 1.45, 0.12), (-1.0, -2.0 + i * 1.5, 0.1), mat=CONCRETE, tint=CONCRETE_T, bevel=0.02, solid=False)

    # The fence, with a gate gap at the front right.
    hw, hd = W / 2, D / 2
    gate = (9.0, 14.0)
    fence(b, [(gate[1], -hd, 0), (hw, -hd, 0), (hw, hd, 0), (-hw, hd, 0), (-hw, -hd, 0), (gate[0], -hd, 0)])
    for x in gate:
        b.box((0.25, 0.25, 2.8), (x, -hd, 1.4), tint=GREY, bevel=0.02)
    # One gate leaf hanging open, one fallen in the gravel.
    with b.frame((gate[0], -hd, 0), (0, 0, -65)):
        for z in (0.2, 1.2, 2.2):
            b.box((2.4, 0.05, 0.05), (1.25, 0, z), tint=GREY, solid=False)
        b.box((2.4, 0.02, 2.1), (1.25, 0, 1.2), mat="M_Mon_Grate", tint=GREY, solid=False)
    with b.frame((gate[1] - 2.0, -hd - 2.2, 0.06), (88, 0, 12)):
        for z in (0.1, 1.1, 2.1):
            b.box((2.4, 0.05, 0.05), (0, 0, z), tint=GREY, solid=False)
        b.box((2.4, 0.02, 2.1), (0, 0, 1.1), mat="M_Mon_Grate", tint=GREY, solid=False)
    # Danger plates on the fence.
    for x in (-15.0, -5.0, 5.0, 17.0):
        b.box((0.5, 0.02, 0.4), (x, -hd - 0.04, 1.6), tint=YELLOW, solid=False, vary=0)
        b.box((0.2, 0.025, 0.2), (x, -hd - 0.05, 1.62), (0, 45, 0), tint=BLACK, solid=False, vary=0)

    # Transformers behind a blast wall, the first one burnt out.
    b.piece("transformer", (-4.0, 2.5, 0), (0, 0, 0))
    b.piece("transformer", (6.0, 2.5, 0), (0, 0, 0), tint=srgb(0.32, 0.30, 0.28))
    b.box((0.45, 4.5, 4.6), (1.0, 2.5, 2.3), mat=FORMWORK, tint=CONCRETE_T)
    b.box((0.6, 4.7, 0.2), (1.0, 2.5, 4.7), mat=CONCRETE, tint=CONCRETE_T, solid=False)
    # Soot on the wall and gravel beside the burnt one, a puddle of old oil.
    b.box((3.6, 3.0, 0.02), (6.0, 2.5, 0.09), mat=PAINT, tint=srgb(0.08, 0.075, 0.07), solid=False, vary=0, wear=0.9)
    rubble(b, (8.2, 5.0, 0.08), 1.6, count=6, tint=srgb(0.3, 0.28, 0.26), mat=PAINT, max_size=0.6)

    # Breakers and the gantry that takes the line in from the pylon at the back.
    for x in (-6.0, 0.0, 6.0):
        b.piece("breaker", (x, -6.5, 0))
    b.piece("gantry", (0.0, 10.5, 0))
    tips = pylon(b, (0.0, hd + 16.0, 0), 24.0)
    for i, x in enumerate((-3.5, 0.0, 3.5)):
        start = (x, 10.5, 8.0 - 1.5)
        tip = tips[[0, 2, 1][i] if i < 3 else 0]
        cable(b, start, tuple(tip), sag=0.03, radius=0.025)
        cable(b, (x * 0.9, -6.5, 4.6), start, sag=0.08, radius=0.02)
    # The line running on beyond the pylon, out of the yard.
    for t in tips[:3]:
        cable(b, tuple(t), (t.x * 1.1, t.y + 60.0, t.z - 1.0), sag=0.03, radius=0.025)

    # Control house in the back left corner, a locker on its wall.
    block_building(b, (-13.5, 8.0, 0), (0, 0, 0), 10.0, 6.5, 3.6, floors=1, broken=0.5)
    b.box((0.9, 0.4, 1.6), (-10.5, 4.55, 0.95), tint=srgb(0.46, 0.5, 0.46), bevel=0.02)
    b.box((0.85, 0.02, 0.3), (-10.5, 4.34, 1.5), tint=YELLOW, solid=False, vary=0)
    b.marker("locker", (-10.5, 4.2, 0), puzzle="surge", table="substation-locker")
    b.marker("fuse-box", (-12.2, 4.6, 1.3), takes="power-cell")
    b.cyl(0.04, 4.0, (-16.5, 10.0, 5.9), tint=GREY, sides=6, solid=False)
    b.box((0.9, 0.05, 0.05), (-16.5, 10.0, 7.2), tint=GREY, solid=False)
    stripes(b, (-18.4, 4.68, 0), (-15.6, 4.68, 0), 0.5, width=0.25, at_z=0.25)

    # Lamps at two corners.
    b.piece("lamp-post", (-hw + 1.5, -hd + 1.5, 0), (0, 0, 135))
    b.piece("lamp-post", (hw - 1.5, hd - 1.5, 0), (0, 0, -45))

    # Supplies left behind: a cable drum, drums of oil, barrels and crates.
    b.piece("cable-reel", (12.0, -10.0, 0), (0, 0, 20))
    b.piece("drum-stack", (15.5, 6.0, 0), (0, 0, 10))
    b.piece("jersey-barrier", (11.5, -hd - 3.5, 0), (0, 0, 8))
    b.piece("jersey-barrier", (16.0, -hd - 3.0, 0), (0, 0, -6))
    for x, y in ((14.0, 9.5), (16.8, -9.0), (-6.5, -11.0)):
        b.marker("loot", (x, y, 0.1), table="barrel")
    b.marker("loot", (-15.0, 8.5, 0.12), table="crate")
    b.marker("loot", (13.0, -9.5, 0.1), table="crate")
    # A fallen insulator string in the gravel.
    insulator(b, (2.5, -9.5, 0.2), 1.6, rot=(90, 0, 35), tint=srgb(0.82, 0.82, 0.8))

    b.zone("arc", (1.0, 0.0, 2.0), radius=9.0, active="surge", damage=12)
    # The burnt transformer arcs while the Surge lasts.
    b.marker("light", (6.0, 3.0, 4.2), colour=[0.55, 0.72, 1.0], range=14.0, active="surge", flicker=True)
    b.zone("no-build", (0, 0, 0), size=(W + 30, D + 30, 30))
    b.meta.update({"tier": "roadside", "landmark": False, "spawn": "line"})


SUN = (24.0, 205.0)
PEOPLE = [((10.5, -16.5, 0), 20.0)]
VIEWS = [
    {"name": "yard", "target": (0, 0, 3), "distance": 58, "yaw": 28, "pitch": 22, "lens": 32},
    {"name": "gate", "target": (-2, 2, 2.6), "distance": 24, "yaw": 12, "pitch": 3, "lens": 26,
     "people": [((10.5, -16.5, 0), 20.0), ((-9.0, -1.0, 0.08), 160.0)]},
    {"name": "line", "target": (0, 18, 10), "distance": 95, "yaw": 145, "pitch": 12, "lens": 35},
    {"name": "surge", "target": (0, 0, 3), "distance": 46, "yaw": 34, "pitch": 14, "lens": 30, "sun": (-4.0, 250.0),
     "lights": True},
]

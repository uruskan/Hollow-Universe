"""Relay Mast (MONUMENTS.md §5.1 no. 3, roadside tier): one of the old survey relays on a hilltop, a 60 m guyed lattice
mast of triangular section in the Seedward civil livery (orange and off-white aviation bands), stayed at three levels
to three anchor blocks. Microwave links and panel antennas near the top, rest platforms on the way up, a red lamp still
blinking at the tip. At its foot a fenced compound: the equipment hut with its door hanging open, a generator and gas
tank, an ice bridge carrying the feeders across, and one of the dishes that fell. Climbing the mast reveals the zone's
sites on the map.
"""
import math

from mathutils import Vector

from monkit import parts_roadside as P
from monkit.kit import (DARK, GREY, LIGHTGREY, OFFWHITE, ORANGE, RED_LAMP, TEAL, YELLOW, cable, fence, railing,
                        srgb)
from monkit.surfaces import GRATE, LAMP

KEY = "relay-mast"
TITLE = "Relay Mast"
SEED = 303
BUILDER = {"chunk": 40.0, "max_edge": 4.0}

H = 60.0
FACE = 1.8
RC = FACE / math.sqrt(3)
LEG_ANGLES = (90.0, 210.0, 330.0)       # legs: back, front-left, front-right; the ladder face looks to -Y
GUY_LEVELS = (18.0, 36.0, 54.0)
ANCHOR_R = 16.0


def leg(z, i):
    a = math.radians(LEG_ANGLES[i])
    return Vector((RC * math.cos(a), RC * math.sin(a), z))


def platform(b, z, gap_face=1, reach=1.0):
    """A rest platform round the mast at height z: a grated deck outside each face with a rail; on the ladder's face
    the deck leaves a gap for the climber."""
    for i in range(3):
        a, c = leg(z, i), leg(z, (i + 1) % 3)
        mid = (a + c) / 2
        n = Vector((mid.x, mid.y, 0)).normalized()
        yaw = math.degrees(math.atan2(c.y - a.y, c.x - a.x))
        length = FACE + 1.3
        centre = mid + n * (reach / 2 + 0.05)
        if i == gap_face:
            for s in (-1, 1):
                along = Vector((c.x - a.x, c.y - a.y, 0)).normalized()
                cc = centre + along * s * (length / 4 + 0.2)
                b.box((length / 2 - 0.4, reach, 0.05), (cc.x, cc.y, z - 0.025), (0, 0, yaw), mat=GRATE, tint=GREY,
                      solid=True, vary=0)
        else:
            b.box((length, reach, 0.05), (centre.x, centre.y, z - 0.025), (0, 0, yaw), mat=GRATE, tint=GREY,
                  solid=True, vary=0)
        b.box((length, 0.08, 0.12), (centre.x + n.x * reach / 2, centre.y + n.y * reach / 2, z - 0.08), (0, 0, yaw),
              mat="M_Mon_Paint", tint=DARK, solid=False)
        e = centre + n * (reach / 2)
        along = Vector((c.x - a.x, c.y - a.y, 0)).normalized()
        p0, p1 = e - along * (length / 2), e + along * (length / 2)
        railing(b, (p0.x, p0.y, z), (p1.x, p1.y, z), 1.1, spacing=1.4, tint=YELLOW, toe=True)
        # Brackets back to the legs.
        for p in (p0, p1):
            b.beam((p.x, p.y, z - 0.1), (p.x - n.x * reach, p.y - n.y * reach, z - 0.9), 0.06, 0.06,
                   mat="M_Mon_Paint", tint=DARK)


def build(b):
    b.set_pad(("circle", 18.5), ground="gravel", blend=10.0, clear=3.0)

    # Foot of the mast: a concrete pier and a pinned base plate.
    b.box((2.2, 2.2, 0.9), (0, 0, 0.2), mat="M_Mon_Concrete", tint=srgb(0.86, 0.85, 0.82), bevel=0.04)
    b.cyl(0.5, 0.12, (0, 0, 0.71), mat="M_Mon_Paint", tint=GREY, sides=16)
    b.cyl(0.18, 0.4, (0, 0, 0.9), mat="M_Mon_Paint", tint=DARK, sides=12)
    P.tri_mast(b, (0, 0, 0.75), H - 0.75, face=FACE, bay=2.0, orient=180.0, leg_r=0.07, member=0.055)

    # Ladder up the front face, rest platforms at 20 and 40 m, the antenna deck at the top.
    P.rungs(b, (0, -RC * 0.5 - 0.16, 0.75), H - 1.6, width=0.42, rail_tint=srgb(0.8, 0.8, 0.78))
    for z in (20.0, 40.0):
        platform(b, z)
    platform(b, H - 1.0, reach=1.2)
    b.marker("reveal", (0, -1.2, H - 0.9), scope="zone", radius=2500.0)

    # Guys: three levels to three anchors.
    for i, a in enumerate(LEG_ANGLES):
        ang = math.radians(a)
        at = (ANCHOR_R * math.cos(ang), ANCHOR_R * math.sin(ang), 0)
        attach = [leg(z, i) for z in GUY_LEVELS]
        top = P.guy_anchor(b, at, attach)
        for p in attach:
            cable(b, tuple(top), tuple(p), sag=0.004, radius=0.016, tint=srgb(0.5, 0.5, 0.5), segments=6)
            b.box((0.3, 0.3, 0.06), tuple(p), (0, 0, a), mat="M_Mon_Paint", tint=GREY, solid=False)

    # Antennas: three microwave links, panel antennas on the top deck, a whip, the lamp.
    for z, i, r, off in ((46.0, 1, 1.2, 25.0), (38.0, 0, 0.8, -15.0), (30.5, 2, 1.0, 10.0)):
        a = math.radians(LEG_ANGLES[i])
        base = leg(z, i) + Vector((math.cos(a), math.sin(a), 0)) * (r * 0.6 + 0.35)
        P.microwave_dish(b, tuple(base), yaw=LEG_ANGLES[i] + 90.0 + off, radius=r, arm=0.3)
        b.cyl(0.05, 2.4, tuple(leg(z, i) + Vector((math.cos(a), math.sin(a), 0)) * 0.15), mat="M_Mon_Paint",
              tint=GREY, sides=6, solid=False)
    for i in range(3):
        a, c = leg(H - 0.5, i), leg(H - 0.5, (i + 1) % 3)
        mid = (a + c) / 2
        n = Vector((mid.x, mid.y, 0)).normalized()
        yaw = math.degrees(math.atan2(n.y, n.x)) + 90.0
        for s in (-0.45, 0.45):
            along = Vector((c.x - a.x, c.y - a.y, 0)).normalized()
            p = mid + n * 0.55 + along * s
            b.box((0.34, 0.12, 2.5), (p.x, p.y, H + 0.5), (0, 0, yaw), mat="M_Mon_Panel", tint=LIGHTGREY, bevel=0.03,
                  solid=False)
            b.cyl(0.04, 3.2, (p.x - n.x * 0.15, p.y - n.y * 0.15, H + 0.4), mat="M_Mon_Paint", tint=GREY, sides=6,
                  solid=False)
    b.cyl(0.035, 5.0, (0, 0, H + 2.5), mat="M_Mon_Paint", tint=GREY, sides=6, solid=False)
    b.box((0.26, 0.26, 0.3), (0, 0, H + 5.1), mat=LAMP, tint=RED_LAMP, solid=False)
    b.marker("light", (0, 0, H + 5.3), colour=[1.0, 0.12, 0.05], range=600.0, blink=1.2)
    for i in (1, 2):
        p = leg(30.0, i)
        b.box((0.18, 0.18, 0.22), (p.x * 1.15, p.y * 1.15, 30.0), mat=LAMP, tint=RED_LAMP, solid=False)
        b.marker("light", (p.x * 1.3, p.y * 1.3, 30.0), colour=[1.0, 0.12, 0.05], range=300.0)
    b.marker("landmark", (0, 0, H + 5.0), kind_of="relay-mast")
    # Feeders down the mast to the ice bridge.
    for k in range(4):
        x = 0.62 + 0.05 * k
        b.tube([(x, -0.2 + 0.03 * k, 2.7), (x, -0.2 + 0.03 * k, 30.0 + 8.0 * (k % 3))], 0.022, tint=DARK, sides=5)

    # The compound: fence and gate, the hut, generator and gas tank, lamp post, the ice bridge.
    fx0, fx1, fy0, fy1 = -8.0, 8.5, -7.0, 8.0
    gate = (-1.6, 1.6)
    fence(b, [(gate[1], fy0, 0), (fx1, fy0, 0), (fx1, fy1, 0), (fx0, fy1, 0), (fx0, fy0, 0), (gate[0], fy0, 0)],
          height=2.2, tint=P.GALV)
    for x in gate:
        b.box((0.2, 0.2, 2.6), (x, fy0, 1.3), mat="M_Mon_Paint", tint=GREY, bevel=0.02)
    with b.frame((gate[1], fy0, 0), (0, 0, 118.0)):       # one leaf swung wide, one gone
        for z in (0.2, 1.1, 2.0):
            b.box((3.0, 0.05, 0.05), (-1.55, 0, z), mat="M_Mon_Paint", tint=GREY, solid=False)
        b.box((3.0, 0.02, 1.9), (-1.55, 0, 1.1), mat="M_Mon_Chainlink", tint=GREY, solid=False)
    for x in (-5.5, 4.5):
        P.plate(b, (x, fy0 - 0.05, 1.5), size=(0.5, 0.36), tint=YELLOW, text="~", text_h=0.22)
    P.plate(b, (0.0, -RC * 0.5 - 0.3, 2.2), size=(0.6, 0.3), tint=OFFWHITE, border=ORANGE, text="RL-31", text_h=0.13)

    hut = P.prefab_hut(b, (4.6, 3.6, 0), (0, 0, 0), w=3.8, d=2.7, h=2.6, code="RL-31", door_open=78.0)
    # Racks inside: two cabinets, one toppled; the chip may be on the shelf.
    for x, tilt in ((3.4, 0.0), (4.4, 0.0)):
        b.box((0.6, 0.7, 2.0), (x, 4.4, 1.3), (0, 0, 0), mat="M_Mon_Paint", tint=srgb(0.3, 0.32, 0.34), bevel=0.02)
        for k in range(6):
            b.box((0.5, 0.02, 0.05), (x, 4.04, 0.6 + 0.25 * k), mat="M_Mon_Gloss", tint=srgb(0.15, 0.4, 0.3),
                  solid=False, vary=0)
    with b.frame((5.7, 3.4, 0.3), (0, 82, 20)):
        b.box((0.6, 0.7, 1.9), (0, 0, 0.3), mat="M_Mon_Paint", tint=srgb(0.3, 0.32, 0.34))
    b.marker("loot", (3.9, 3.0, 0.32), table="crate")
    b.marker("loot", (3.4, 4.0, 1.25), table="chip-amber", chance=0.3)
    # Ice bridge from the mast to the hut's back corner, feeders lying in the tray.
    for y in (0.9, 2.2):
        x = 1.2 + (y - 0.9) * 1.1
        b.box((0.1, 0.1, 2.6), (x, y, 1.3), mat="M_Mon_Paint", tint=GREY)
    b.box((0.5, 3.9, 0.06), (1.9, 1.9, 2.65), (0, 0, -40.0), mat=GRATE, tint=GREY, solid=False, vary=0)
    for k in range(4):
        b.tube([(0.6 + 0.05 * k, -0.15, 2.72), (1.2 + 0.05 * k, 0.6, 2.72), (2.6 + 0.05 * k, 2.6, 2.72),
                (2.75, 3.4 + 0.05 * k, 2.4)], 0.02, tint=DARK, sides=5)
    # Generator in a weatherproof enclosure, gas tank on saddles, the fuel line.
    with b.frame((-4.6, 4.5, 0), (0, 0, 90.0)):
        b.box((2.4, 1.3, 0.2), (0, 0, 0.1), mat="M_Mon_Concrete", tint=srgb(0.8, 0.79, 0.76))
        b.box((2.2, 1.1, 1.5), (0, 0, 0.95), mat="M_Mon_Panel", tint=OFFWHITE, bevel=0.04)
        b.box((2.21, 1.11, 0.16), (0, 0, 1.25), mat="M_Mon_Paint", tint=TEAL, solid=False, vary=0)
        for k in range(5):
            b.box((0.02, 0.5, 0.06), (1.11, 0.0, 0.5 + k * 0.12), mat="M_Mon_Paint", tint=DARK, solid=False, vary=0)
        b.cyl(0.07, 1.0, (-0.8, 0.3, 2.1), mat="M_Mon_Rust", tint=(1, 1, 1), sides=8, solid=False)
        P.stencil(b, "GEN 2", (-0.9, -0.56, 0.6), (0, 0, 0), 0.18, DARK)
    with b.frame((-4.8, 1.6, 0), (0, 0, 90.0)):
        for x in (-0.6, 0.6):
            b.box((0.2, 0.9, 0.5), (x, 0, 0.25), mat="M_Mon_Concrete", tint=srgb(0.8, 0.79, 0.76))
        b.cyl(0.55, 1.6, (0, 0, 1.0), (0, 90, 0), mat="M_Mon_Paint", tint=OFFWHITE, sides=18)
        for s in (-1, 1):
            b.lathe([(0.55, 0.0), (0.45, 0.2), (0.25, 0.3), (0.0, 0.33)], (s * 0.8, 0, 1.0), (0, 90 * s, 0),
                    mat="M_Mon_Paint", tint=OFFWHITE, sides=18)
        b.cyl(0.08, 0.2, (0, 0, 1.6), mat="M_Mon_Paint", tint=GREY, sides=8, solid=False)
        b.box((0.4, 0.02, 0.3), (0, -0.56, 1.0), mat="M_Mon_Paint", tint=ORANGE, solid=False, vary=0)
    b.tube([(-4.8, 1.6, 1.65), (-4.8, 1.6, 1.9), (-4.8, 3.2, 1.9), (-4.6, 3.6, 1.0)], 0.02, tint=YELLOW, sides=5)
    b.piece("lamp-post", (fx1 - 0.8, fy0 + 0.8, 0), (0, 0, 135.0))
    b.piece("drum-stack", (6.6, -4.6, 0), (0, 0, 12.0))
    b.marker("loot", (6.0, -2.8, 0.05), table="barrel")

    # One of the dishes came down: bent mount, cracked reflector, panels and bolts in the gravel.
    P.microwave_dish(b, (-4.6, -3.2, 0.75), yaw=35.0, radius=1.1, pitch=-62.0, cover=False)
    b.beam((-3.6, -2.4, 0.1), (-4.3, -3.0, 0.9), 0.08, 0.08, mat="M_Mon_Paint", tint=GREY)
    P.clods(b, (-4.8, -3.6, 0), 2.4, count=7, tint=srgb(0.5, 0.47, 0.42), size=0.35, inner=1.2)
    P.debris(b, (-4.0, -4.6, 0), 2.6, count=6, size=0.7, tint=P.SIGN_WHITE, mat="M_Mon_Matte")
    b.marker("loot", (-4.0, -2.0, 0.1), table="tech-scrap")

    b.zone("no-build", (0, 0, 0), radius=26.0)
    b.meta.update({"tier": "roadside", "landmark": True, "spawn": "hilltop"})


SUN = (34.0, 215.0)
PEOPLE = [((1.2, -9.5, 0), 25.0)]
VIEWS = [
    {"name": "mast", "target": (0, 0, 30), "distance": 118, "yaw": 28, "pitch": 9, "lens": 30},
    {"name": "compound", "target": (1.5, 2.5, 3.2), "distance": 11.0, "yaw": -8, "pitch": 4, "lens": 22},
    {"name": "top", "target": (0, 0, 52.5), "distance": 40, "yaw": -35, "pitch": -2, "lens": 32},
]

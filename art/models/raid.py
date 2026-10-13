"""G5 raid items: bag charge, tin grenade, rocket launcher, rockets (GROUND.md §9.1). Our own designs.

Held items follow the character package's frame: authored pointing +Y with the right palm at the origin (the launcher)
or with a palm socket at their middle (the thrown charges, like the timed charge), exported pointing +X. Rockets are
carried items that lie on the ground as authored, along Y.

    blender -b --factory-startup --python art/models/raid.py -- [keys...]
"""
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import bmesh  # noqa: E402
from mathutils import Matrix, Vector, noise  # noqa: E402

from hollow import script_args  # noqa: E402
from hollow.build import (CANVAS, GLOSSY, GUNMETAL, IVORY, LEATHER, PARCHMENT, RUBBER, RUST, STEEL, TEAL,  # noqa: E402
                          Model)

OLIVE = (0.6, 0.66, 0.42)
KHAKI = (0.78, 0.7, 0.52)
TAPE = (0.36, 0.37, 0.38)
YELLOW = (0.95, 0.74, 0.12)
BLUE = (0.16, 0.34, 0.78)


def ring(m, radius, width, at, rot=(0, 0, 0), depth=0.003, **kw):
    """A flat band around an axis (local Z), `width` long and standing `depth` proud of `radius`."""
    return m.lathe([(radius - 0.0005, -width / 2), (radius + depth, -width / 2 + depth * 0.4),
                    (radius + depth, width / 2 - depth * 0.4), (radius - 0.0005, width / 2)], at, rot, close=False, **kw)


def strip(m, x0, x1, path, thickness, **kw):
    """A strip of sheet (canvas, leather) from x0 to x1 along a path of (y, z) points, `thickness` thick along the
    path's normal in the Y-Z plane."""
    bm = bmesh.new()
    rows = []
    n = len(path)
    for i, (y, z) in enumerate(path):
        ya, za = path[max(i - 1, 0)]
        yb, zb = path[min(i + 1, n - 1)]
        ty, tz = yb - ya, zb - za
        length = math.hypot(ty, tz) or 1.0
        ny, nz = -tz / length * thickness / 2, ty / length * thickness / 2
        rows.append([bm.verts.new((x0, y - ny, z - nz)), bm.verts.new((x1, y - ny, z - nz)),
                     bm.verts.new((x1, y + ny, z + nz)), bm.verts.new((x0, y + ny, z + nz))])
    for a, b in zip(rows, rows[1:]):
        for j in range(4):
            k = (j + 1) % 4
            bm.faces.new((a[j], a[k], b[k], b[j]))
    bm.faces.new(rows[0])
    bm.faces.new(list(reversed(rows[-1])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return m.add(bm, **kw)


def hollow_tube(m, outer, inner, at=(0, 0, 0), rot=(0, 0, 0), sides=28, **kw):
    """A tube with wall thickness from an outer profile (radius, z) and an inner bore radius: the profile runs out
    along the outside and back along the bore, so both ends show the wall."""
    z0, z1 = outer[0][1], outer[-1][1]
    profile = list(outer) + [(inner, z1), (inner, z0), outer[0]]
    return m.lathe(profile, at, rot, sides=sides, close=False, **kw)


# ---------------------------------------------------------------- bag charge


def bag_charge(seed=131):
    """A canvas satchel stuffed with powder: lumpy body, a flap buckled down, two belts, a carry loop, and an igniter
    with its fuse cord taped to the side. The palm holds it at its middle (thrown like the timed charge is placed)."""
    m = Model("bag-charge", seed=seed, held=True, forward=False)
    w, d, h = 0.25, 0.12, 0.17
    body = m.box((w, d, h), (0, 0, h / 2), bevel=0.035, segments=4, tile=CANVAS, tint=KHAKI, metres=0.3, vary=0.08,
                 vary_scale=12, sharp=0)
    # Stuffed: bulging sides and lumps from what is packed inside, sagging onto the ground.
    off = Vector((3.1, 7.7, 1.3))
    for v in body.data.vertices:
        c = v.co
        n = noise.noise(c * 14 + off, noise_basis="PERLIN_ORIGINAL")
        bulge = 1 + 0.12 * (1 - (2 * (c.z - h / 2) / h) ** 2)
        c.x *= 1 + 0.04 * (1 - (2 * (c.z - h / 2) / h) ** 2)
        c.y *= bulge
        c.z += 0.006 * n
        c.x += 0.005 * n
        if c.z < 0.012:
            c.z = 0.012 * (c.z / 0.012) ** 0.5 if c.z > 0 else 0.0
    body.data.update()
    # The flap from the back of the top, over it and down the front.
    flap = [(0.058, h + 0.001), (0.03, h + 0.011), (0.0, h + 0.015), (-0.03, h + 0.012), (-0.054, h + 0.002), (-0.067, h - 0.02),
            (-0.071, h - 0.045), (-0.072, h - 0.078)]
    strip(m, -w / 2 - 0.005, w / 2 + 0.005, flap, 0.006, tile=CANVAS, tint=(0.7, 0.62, 0.45), metres=0.3, vary=0.08,
          vary_scale=12, sharp=40)
    # Two belts around the body, buckled at the front: a rounded-square loop just outside the stuffed canvas.
    for x in (-0.07, 0.07):
        loop = []
        for k in range(33):
            a = 2 * math.pi * k / 32
            s, c = math.sin(a), math.cos(a)
            loop.append((math.copysign(abs(s) ** 0.5, s) * (d / 2 * 1.12 + 0.006),
                         h / 2 + math.copysign(abs(c) ** 0.5, c) * (h / 2 + 0.008)))
        strip(m, x - 0.017, x + 0.017, loop, 0.004, tile=LEATHER, tint=(0.55, 0.42, 0.3), metres=0.3, sharp=60)
        m.box((0.036, 0.008, 0.028), (x, -d / 2 * 1.12 - 0.008, h * 0.45), bevel=0.002, tile=STEEL, tint=(0.85, 0.85, 0.85),
              metres=0.05)
        m.box((0.026, 0.006, 0.018), (x, -d / 2 * 1.12 - 0.012, h * 0.45), bevel=0.0, tile=RUBBER, tint=(0.2, 0.2, 0.2),
              metres=0.05)
    # A carry loop on top.
    loop = [(-0.06, 0.0, h + 0.004), (-0.05, 0.0, h + 0.04), (0.0, 0.0, h + 0.055), (0.05, 0.0, h + 0.04), (0.06, 0.0, h + 0.004)]
    strap = m.tube(loop, 0.005, sides=6, tile=LEATHER, tint=(0.5, 0.38, 0.27), metres=0.2)
    for v in strap.data.vertices:
        v.co.y *= 3.0
    strap.data.update()
    for x in (-0.06, 0.06):
        m.box((0.02, 0.03, 0.012), (x, 0.0, h + 0.004), bevel=0.002, tile=STEEL, tint=(0.8, 0.8, 0.8), metres=0.05)
    # The igniter: a steel tube through the flap, a pull cap, and the fuse cord down the right side under tape.
    ig = (w / 2 - 0.035, -0.01, h + 0.01)
    m.cylinder(0.009, 0.06, (ig[0], ig[1], ig[2] + 0.02), (12, 0, 0), sides=12, bevel=0.001, tile=GUNMETAL,
               tint=(0.85, 0.85, 0.85), metres=0.1)
    m.cylinder(0.012, 0.016, (ig[0], ig[1] - 0.006, ig[2] + 0.052), (12, 0, 0), sides=12, bevel=0.002, tile=IVORY,
               tint=(0.75, 0.12, 0.08), metres=0.05)
    cord = [(ig[0] + 0.004, ig[1] + 0.01, ig[2] + 0.005), (w / 2 + 0.01, 0.02, h - 0.01), (w / 2 + 0.02, 0.03, h * 0.6),
            (w / 2 + 0.015, 0.0, h * 0.3), (w / 2 + 0.03, -0.035, 0.04), (w / 2 + 0.06, -0.05, 0.006)]
    m.tube(cord, 0.0028, sides=6, tile=CANVAS, tint=(0.25, 0.22, 0.2), metres=0.2)
    m.box((0.012, 0.05, 0.03), (w / 2 + 0.017, 0.012, h * 0.55), (0, 0, 0), bevel=0.002, tile=RUBBER, tint=TAPE, metres=0.1)
    # Held at its middle: the right palm against the back, +X out of the front.
    m.socket("grip_r", (0, d / 2 + 0.02, h / 2), (0, 0, -90))
    m.socket("fuse", (ig[0], ig[1] - 0.008, ig[2] + 0.06))
    m.export(floor=True)


# ---------------------------------------------------------------- tin grenade


def tin_grenade(seed=133):
    """A food tin packed with powder: ribbed sides, crimped rims, a faded label, nails bound round it with wire and
    tape, a fuse through the lid. Held in the palm at its middle."""
    m = Model("tin-grenade", seed=seed, held=True, forward=False)
    r, h = 0.036, 0.1
    prof = [(0.0, 0.0), (r * 0.94, 0.0), (r * 1.03, 0.003), (r * 1.03, 0.007), (r, 0.009)]
    for k in range(25):
        z = 0.009 + (h - 0.018) * k / 24
        prof.append((r * (1 - 0.018 * max(0.0, math.sin(z * 2 * math.pi / 0.013)) ** 2), z))
    prof += [(r, h - 0.009), (r * 1.03, h - 0.007), (r * 1.03, h - 0.002), (r * 0.94, h), (r * 0.9, h - 0.003), (0.0, h - 0.003)]

    def dented(co, tint):
        k = 1 + 0.12 * noise.noise(co * 60 + Vector((2, 5, 1)), noise_basis="PERLIN_ORIGINAL")
        return [t * k for t in tint]

    m.lathe(prof, sides=32, tile=STEEL, tint=(0.82, 0.83, 0.85), metres=0.12, sharp=40, tint_fn=dented)
    # The label: faded red paper with a pale stripe, torn short of a full turn.
    lab = [(r + 0.0006, 0.028), (r + 0.0012, 0.03), (r + 0.0012, 0.072), (r + 0.0006, 0.074)]
    bm = bmesh.new()
    sides, cover = 32, 0.78
    rings_ = []
    for rr, z in lab:
        rings_.append([bm.verts.new((rr * math.cos(a), rr * math.sin(a), z))
                       for a in (2 * math.pi * cover * i / sides + 0.4 for i in range(sides + 1))])
    for a, b in zip(rings_, rings_[1:]):
        for i in range(sides):
            bm.faces.new((a[i], a[i + 1], b[i + 1], b[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

    def label(co, tint):
        if 0.047 < co.z < 0.054:
            return (0.92, 0.86, 0.7)
        n = noise.noise(co * 80 + Vector((1, 1, 3)), noise_basis="PERLIN_ORIGINAL")
        return [t * (0.9 + 0.2 * n) for t in tint]

    m.add(bm, tile=PARCHMENT, tint=(0.62, 0.16, 0.1), metres=0.15, sharp=0, tint_fn=label)
    # Nails bound round it, under two turns of wire and two bands of tape.
    for i in range(7):
        a = 2 * math.pi * i / 7 + 0.2
        x, y = math.cos(a) * (r + 0.0035), math.sin(a) * (r + 0.0035)
        m.cylinder(0.0017, 0.068, (x, y, 0.05), sides=6, tile=STEEL, tint=(0.55, 0.5, 0.46), metres=0.08)
        m.cylinder(0.0038, 0.0016, (x, y, 0.0848), sides=8, tile=STEEL, tint=(0.6, 0.55, 0.5), metres=0.03)
    for z in (0.024, 0.077):
        ring(m, r + 0.0052, 0.012, (0, 0, z), depth=0.0015, sides=32, tile=RUBBER, tint=TAPE, metres=0.1)
    turns = []
    for k in range(65):
        t = k / 64
        a = t * 2 * 2 * math.pi
        turns.append((math.cos(a) * (r + 0.0058), math.sin(a) * (r + 0.0058), 0.04 + 0.022 * t))
    m.tube(turns, 0.0011, sides=5, tile=STEEL, tint=(0.75, 0.7, 0.62), metres=0.1)
    m.tube([(r + 0.0058, 0.0, 0.04), (r + 0.012, 0.002, 0.037), (r + 0.016, -0.002, 0.039)], 0.0012, sides=5, tile=STEEL,
           tint=(0.75, 0.7, 0.62), metres=0.05)
    # The fuse: a short cord out of the lid, a striker cap on its end.
    fuse = [(0.012, 0.004, h - 0.004), (0.014, 0.004, h + 0.015), (0.02, 0.0, h + 0.03), (0.03, -0.006, h + 0.038)]
    m.tube(fuse, 0.0024, sides=6, tile=CANVAS, tint=(0.3, 0.26, 0.22), metres=0.1)
    m.cylinder(0.004, 0.012, (0.034, -0.008, h + 0.04), (0, 70, -30), sides=8, bevel=0.001, tile=IVORY,
               tint=(0.85, 0.75, 0.3), metres=0.03)
    m.cylinder(0.006, 0.004, (0.012, 0.004, h - 0.001), sides=10, tile=GUNMETAL, tint=(0.6, 0.6, 0.6), metres=0.03)
    m.socket("grip_r", (0, 0, h * 0.5), (0, 0, -90))
    m.socket("fuse", (0.034, -0.008, h + 0.044))
    m.export(floor=True)


# ---------------------------------------------------------------- rocket launcher


def rocket_launcher(seed=135):
    """A shoulder-fired tube: flared venturi at the back, muzzle ring, painted tube with steel bands, a canvas heat
    sleeve where it rests on the shoulder, a pistol grip with trigger guard (the right palm, the pivot), a vertical
    fore grip (the left palm), a folding frame sight on the left. Authored pointing +Y, the tube's axis 11 cm above
    the right palm."""
    m = Model("rocket-launcher", seed=seed, held=True)
    axis = 0.11
    back, front = -0.6, 0.5
    rot = (-90, 0, 0)  # lathe Z along +Y
    at = (0, 0, axis)
    outer = [(0.066, back), (0.064, back + 0.012), (0.056, back + 0.06), (0.05, back + 0.12), (0.05, front - 0.065),
             (0.0545, front - 0.06), (0.0545, front - 0.012), (0.051, front)]
    hollow_tube(m, outer, 0.043, at, rot, tile=TEAL, tint=OLIVE, metres=0.6, vary=0.06, vary_scale=8, sharp=35)
    # The venturi bell is bare dark metal, scorched inside.
    m.lathe([(0.0665, back - 0.001), (0.0645, back + 0.013), (0.0575, back + 0.058), (0.0505, back + 0.115)], at, rot, sides=28,
            close=False, tile=GUNMETAL, tint=(0.55, 0.53, 0.5), metres=0.3)
    m.lathe([(0.043, back + 0.002), (0.043, back + 0.2)], at, rot, sides=24, close=False, tile=GUNMETAL, tint=(0.12, 0.11, 0.1),
            metres=0.3)
    for y in (back + 0.17, -0.02, front - 0.13):
        ring(m, 0.05, 0.022, (0, y, axis), rot, depth=0.0035, sides=28, tile=GUNMETAL, tint=(0.8, 0.8, 0.8), metres=0.2)
    # Heat sleeve over the shoulder: quilted canvas, laced.
    m.lathe([(0.0505, -0.36), (0.0575, -0.352), (0.0585, -0.3), (0.0585, -0.12), (0.0575, -0.07), (0.0505, -0.062)],
            (0, 0, axis), rot, sides=28, close=False, tile=CANVAS, tint=(0.62, 0.56, 0.42), metres=0.3, vary=0.06, vary_scale=30)
    for y in (-0.33, -0.21, -0.09):
        ring(m, 0.0585, 0.01, (0, y, axis), rot, depth=0.002, sides=28, tile=LEATHER, tint=(0.45, 0.34, 0.25), metres=0.1)
    # Firing mechanism on the right of the tube above the grip, a cocking lever, the mount down to the grip.
    m.box((0.03, 0.12, 0.05), (0.052, -0.005, axis - 0.012), bevel=0.004, tile=GUNMETAL, tint=(0.75, 0.75, 0.75), metres=0.2)
    m.cylinder(0.006, 0.05, (0.07, -0.02, axis + 0.005), (0, 90, 0), sides=10, tile=STEEL, tint=(0.85, 0.85, 0.85), metres=0.05)
    m.box((0.012, 0.03, 0.008), (0.098, -0.02, axis + 0.005), bevel=0.002, tile=RUBBER, tint=(0.25, 0.25, 0.25), metres=0.05)
    m.box((0.034, 0.1, 0.03), (0.0, -0.008, axis - 0.058), bevel=0.004, tile=GUNMETAL, tint=(0.7, 0.7, 0.7), metres=0.2)
    # Pistol grip: a side profile (y forward, z up) raked back, thickness across X; the palm at the origin.
    grip = [(-0.036, 0.058), (0.026, 0.058), (0.02, 0.0), (0.006, -0.062), (-0.008, -0.07), (-0.048, -0.068), (-0.04, -0.01)]
    g = m.extrude(grip, 0.03, bevel=0.005, segments=2, tile=RUBBER, tint=(0.32, 0.3, 0.28), metres=0.15, uv="box")
    g.data.transform(Matrix.Rotation(math.radians(90), 4, "Z"))
    for v in g.data.vertices:
        # Finger grooves along the front.
        if v.co.y > 0.004 and v.co.z < 0.03:
            v.co.y -= 0.0025 * max(0.0, math.sin((v.co.z + 0.06) / 0.09 * 3 * math.pi))
    g.data.update()
    guard = [(0.0, 0.026, 0.058), (0.0, 0.06, 0.05), (0.0, 0.074, 0.02), (0.0, 0.07, -0.006), (0.0, 0.046, -0.01), (0.0, 0.022, -0.004)]
    m.tube(guard, 0.0035, sides=6, tile=GUNMETAL, tint=(0.75, 0.75, 0.75), metres=0.1)
    m.box((0.006, 0.01, 0.026), (0.0, 0.036, 0.034), (12, 0, 0), bevel=0.002, tile=STEEL, tint=(0.9, 0.9, 0.9), metres=0.03)
    # Fore grip: a vertical post under the tube for the left palm.
    gy = 0.29
    m.box((0.03, 0.05, 0.03), (0.0, gy, axis - 0.062), bevel=0.004, tile=GUNMETAL, tint=(0.7, 0.7, 0.7), metres=0.1)
    m.lathe([(0.0, -0.065), (0.016, -0.065), (0.019, -0.058), (0.0175, -0.03), (0.019, 0.0), (0.0175, 0.03), (0.016, 0.05),
             (0.0, 0.05)], (0.0, gy, 0.0), (0, 0, 0), sides=14, tile=RUBBER, tint=(0.32, 0.3, 0.28), metres=0.15, sharp=50)
    # Folding frame sight on the left: a base on the tube, a front frame with a post, and a rear aperture.
    sx, sz = -0.058, axis + 0.06
    m.box((0.026, 0.05, 0.022), (sx + 0.012, 0.07, axis + 0.04), bevel=0.003, tile=GUNMETAL, tint=(0.7, 0.7, 0.7), metres=0.1)
    for dx in (-0.016, 0.016):
        m.box((0.004, 0.006, 0.05), (sx + dx, 0.07, sz + 0.015), bevel=0.001, tile=GUNMETAL, tint=(0.75, 0.75, 0.75), metres=0.05)
    for dz in (-0.008, 0.038):
        m.box((0.036, 0.006, 0.004), (sx, 0.07, sz + dz), bevel=0.001, tile=GUNMETAL, tint=(0.75, 0.75, 0.75), metres=0.05)
    m.box((0.002, 0.004, 0.022), (sx, 0.07, sz + 0.002), bevel=0.0, tile=IVORY, tint=(0.95, 0.9, 0.6), metres=0.02)
    m.box((0.024, 0.04, 0.02), (sx + 0.012, -0.2, axis + 0.04), bevel=0.003, tile=GUNMETAL, tint=(0.7, 0.7, 0.7), metres=0.1)
    m.lathe([(0.003, -0.002), (0.011, -0.002), (0.011, 0.002), (0.003, 0.002)], (sx, -0.2, sz + 0.004), (-90, 0, 0), sides=14,
            close=False, tile=GUNMETAL, tint=(0.75, 0.75, 0.75), metres=0.03)
    m.box((0.004, 0.006, 0.022), (sx, -0.2, sz - 0.017), bevel=0.001, tile=GUNMETAL, tint=(0.75, 0.75, 0.75), metres=0.03)
    # Sling swivels.
    for y in (-0.42, 0.36):
        m.lathe([(0.006, -0.002), (0.011, -0.002), (0.011, 0.002), (0.006, 0.002)], (-0.05, y, axis - 0.03), (0, 90, 0), sides=10,
                close=False, tile=STEEL, tint=(0.8, 0.8, 0.8), metres=0.03)
    m.socket("grip_l", (0.0, gy, -0.008))
    m.socket("sight", (sx, 0.07, sz + 0.004))
    m.socket("eye", (sx, -0.2 - 0.18, sz + 0.004))
    m.socket("muzzle", (0.0, front, axis))
    # The back blast leaves the venturi: +X of this socket points backwards.
    m.socket("exhaust", (0.0, back, axis), (0, 0, 180))
    # Hulls are placed after the turn to +X forward.
    m.export(floor=False, hulls=[((-0.05, 0, axis), (1.12, 0.135, 0.135), (0, 0, 0)),
                                 ((-0.005, 0, -0.005), (0.09, 0.04, 0.14), (0, 0, 0)),
                                 ((gy, 0, -0.01), (0.04, 0.04, 0.13), (0, 0, 0))])


# ---------------------------------------------------------------- rockets


def rocket(key, seed, hv=False):
    """A rocket lying on the ground along Y, nose forward: a motor tube with folding fins and a nozzle, and the
    warhead with its fuze; the high-velocity one slimmer and longer with a pointed warhead and a blue band."""
    m = Model(key, seed=seed)
    rm = 0.029 if hv else 0.032  # motor radius
    rw = 0.034 if hv else 0.045  # warhead radius
    fin_r = rm + (0.042 if hv else 0.038)  # how far the fins reach from the axis
    z = max(rw, fin_r * 0.7071) + 0.001  # the axis height: resting on the warhead and two fins
    rot = (-90, 0, 0)
    tail, joint, nose = (-0.34, 0.1, 0.33) if hv else (-0.3, 0.1, 0.3)
    # Nozzle and motor.
    m.lathe([(0.0, tail - 0.005), (rm * 0.55, tail - 0.005), (rm * 0.8, tail), (rm * 0.75, tail + 0.03), (rm, tail + 0.045),
             (rm, joint - 0.012), (rm * 1.05, joint - 0.008), (rm * 1.05, joint), (0.0, joint)], (0, 0, z), rot, sides=24,
            tile=TEAL, tint=(0.5, 0.53, 0.47), metres=0.4, vary=0.05, sharp=40)
    m.lathe([(0.0, tail - 0.004), (rm * 0.5, tail - 0.004), (rm * 0.5, tail + 0.02), (0.0, tail + 0.02)], (0, 0, z), rot, sides=16,
            tile=GUNMETAL, tint=(0.1, 0.09, 0.08), metres=0.1)
    # Warhead: an ogive (pointed for the high-velocity round) with a band, the fuze on its nose.
    pts = [(0.0, joint), (rm * 1.06, joint)]
    length = nose - joint
    for k in range(13):
        t = k / 12
        if hv:
            r = rw * (1 - t ** 1.6) if t > 0.35 else rw * min(1.0, 0.85 + t)
        else:
            r = rw * math.sqrt(max(0.0, 1 - max(0.0, (t - 0.25) / 0.75) ** 2)) * min(1.0, 0.82 + t * 1.5)
        pts.append((max(r, 0.007), joint + 0.006 + length * t))
    pts.append((0.0, nose))
    m.lathe(pts, (0, 0, z), rot, sides=28, tile=TEAL, tint=OLIVE if not hv else (0.5, 0.52, 0.48), metres=0.3, vary=0.05, sharp=30)
    band_y = joint + length * (0.22 if hv else 0.2)
    ring(m, rw * (0.98 if not hv else 0.99), 0.016, (0, band_y, z), rot, depth=0.0012, sides=28, tile=IVORY,
         tint=BLUE if hv else YELLOW, metres=0.05)
    m.cylinder(0.006, 0.02, (0, nose + 0.006, z), (-90, 0, 0), sides=12, bevel=0.0015, tile=STEEL, tint=(0.85, 0.82, 0.75),
               metres=0.03)
    # Four folding fins at 45 degrees, so it rests on two of them.
    root = 0.09 if hv else 0.075
    fin = [(0.0, 0.0), (root, 0.0), (root * 0.8, fin_r - rm), (-0.012, fin_r - rm)]
    for k in range(4):
        a = 45 + 90 * k
        f = m.extrude([(y, r) for y, r in fin], 0.0032, bevel=0.0007, tile=STEEL, tint=(0.78, 0.8, 0.76), metres=0.1, uv="box")
        f.data.transform(Matrix.Rotation(math.radians(90), 4, "Z"))  # outline (x, z) -> (y, z), thickness across X
        f.data.transform(Matrix.Translation((0, tail + 0.016, rm - 0.001)))
        # Around the rocket's axis, then up to where the axis lies.
        f.data.transform(Matrix.Translation((0, 0, z)) @ Matrix.Rotation(math.radians(a), 4, "Y"))
        f.data.update()
    m.export(floor=True)


# ---------------------------------------------------------------- catalogue

ALL = {"bag-charge": bag_charge, "tin-grenade": tin_grenade, "rocket-launcher": rocket_launcher,
       "rocket": lambda: rocket("rocket", 137), "rocket-hv": lambda: rocket("rocket-hv", 139, hv=True)}

for key in script_args() or list(ALL):
    ALL[key]()

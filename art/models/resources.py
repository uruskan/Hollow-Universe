"""Resource items as small piles on the ground (their dropped look and icons), and the bigger world pickups.

    blender -b --factory-startup --python art/models/resources.py -- [keys...]
"""
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import bmesh  # noqa: E402
from mathutils import Vector, noise  # noqa: E402

from hollow import ITEMS, WORLD, script_args  # noqa: E402
from hollow.build import (BASALT, CANVAS, GLOSSY, GUNMETAL, LEATHER, OCHRE, PARCHMENT, ROCK, RUST, STEEL,  # noqa: E402
                          TIMBER, Model)

def log(m, at, rot, length=0.4, radius=0.055, seed=0.0):
    """A round of branch wood along local Y: bark around, pale growth rings on both ends."""
    sides = 20
    bm = bmesh.new()
    rings = []
    for k in range(13):
        y = -length / 2 + length * k / 12
        ring = []
        for i in range(sides):
            a = 2 * math.pi * i / sides
            # Bark ridges along the log, a little crooked, over a gently uneven round.
            ridge = 0.05 * abs(math.sin(a * 4.5 + seed + y * 7)) + 0.02 * math.sin(a * 11 + y * 31 + seed)
            rr = radius * (0.97 + ridge + 0.03 * math.sin(k * 1.3 + seed))
            ring.append((rr * math.cos(a), y, rr * math.sin(a)))
        rings.append(ring)
    verts = [[bm.verts.new(c) for c in ring] for ring in rings]
    for r0, r1 in zip(verts, verts[1:]):
        for i in range(sides):
            j = (i + 1) % sides
            bm.faces.new((r0[i], r0[j], r1[j], r1[i]))
    bm.normal_update()
    bm.faces.ensure_lookup_table()
    f = bm.faces[0]
    if f.normal.dot(Vector((f.calc_center_median().x, 0, f.calc_center_median().z))) < 0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
    m.add(bm, at, rot, material=ROCK, tint=(0.36, 0.27, 0.2), metres=0.22, vary=0.18, sharp=0)

    def growth(co, tint):
        r = math.hypot(co.x, co.z) / radius
        band = (0.88 + 0.12 * math.cos(r * 30 + seed)) * (1 - 0.3 * r ** 4)
        return [t * band for t in tint]

    for sign, ring in ((-1, rings[0]), (1, rings[-1])):
        bm = bmesh.new()
        layers = [[bm.verts.new((x * k, y + sign * 0.003 * (1 - k), z * k)) for x, y, z in ring] for k in (1.0, 0.75, 0.5, 0.25)]
        centre = bm.verts.new((0, ring[0][1] + sign * 0.003, 0))
        for l0, l1 in zip(layers, layers[1:]):
            for i in range(sides):
                j = (i + 1) % sides
                bm.faces.new((l0[i], l0[j], l1[j], l1[i]))
        for i in range(sides):
            bm.faces.new((layers[-1][i], layers[-1][(i + 1) % sides], centre))
        bm.normal_update()
        bm.faces.ensure_lookup_table()
        if bm.faces[0].normal.y * sign < 0:
            bmesh.ops.reverse_faces(bm, faces=bm.faces)
        m.add(bm, at, rot, tile=PARCHMENT, tint=(0.98, 0.8, 0.56), metres=0.25, sharp=0, uv="box", tint_fn=growth)


def wood(key="wood", count=5, scale=1.0, seed=3, folder=None):
    """A few rounds of branch wood, bark on, stacked."""
    m = Model(key, folder or ITEMS, seed=seed)
    r = 0.055 * scale
    rows = [[-1.0, 0.0, 1.0, -2.0, 2.0], [-0.5, 0.5, -1.5, 1.5], [0.0, -1.0, 1.0]]
    order = [(0, 0), (0, 1), (0, 2), (1, 0), (1, 1), (2, 0), (0, 3), (0, 4), (1, 2), (1, 3), (2, 1), (2, 2)]
    for n, (level, k) in enumerate(order[:count]):
        radius = r * m.rng.uniform(0.86, 1.1)
        x = rows[level][k] * r * 2.05
        z = r + level * r * 1.76
        length = 0.4 * scale * m.rng.uniform(0.88, 1.06)
        log(m, (x, m.rng.uniform(-0.02, 0.02) * scale, z), (0, 0, m.rng.uniform(-5, 5)), length, radius, seed=n * 2.3 + seed)
    m.export()


def pile(m, count, size, spread, top=None):
    """Positions for a natural pile: stones spread on the ground without overlapping, a few resting on top.
    Returns (x, y, z, size) per stone, z at the stone's centre."""
    top = max(1, count // 4) if top is None else top
    ground = []
    tries = 0
    while len(ground) < count - top and tries < 400:
        tries += 1
        s = size * m.rng.uniform(0.7, 1.15)
        a = m.rng.uniform(0, 2 * math.pi)
        d = spread * math.sqrt(m.rng.random())
        x, y = math.cos(a) * d, math.sin(a) * d
        if all(math.hypot(px - x, py - y) > (ps + s) * 0.42 for px, py, _, ps in ground):
            ground.append((x, y, s * 0.3, s))
    placed = list(ground)
    for _ in range(top):
        # In the hollow between the two or three nearest ground stones, close to the middle.
        cx = sum(p[0] for p in ground) / len(ground) + m.rng.uniform(-1, 1) * spread * 0.3
        cy = sum(p[1] for p in ground) / len(ground) + m.rng.uniform(-1, 1) * spread * 0.3
        near = sorted(ground, key=lambda p: math.hypot(p[0] - cx, p[1] - cy))[:3]
        s = size * m.rng.uniform(0.7, 1.0)
        z = max(p[2] + p[3] * 0.32 for p in near) + s * 0.22
        placed.append((sum(p[0] for p in near) / len(near), sum(p[1] for p in near) / len(near), z, s))
    return placed


def stones(m, count, size, spread, material=ROCK, tile=BASALT, tint=(0.72, 0.7, 0.66), facets=0.35, metres=0.35):
    placed = pile(m, count, size, spread)
    for x, y, z, s in placed:
        dims = (s * m.rng.uniform(0.95, 1.25), s * m.rng.uniform(0.8, 1.05), s * m.rng.uniform(0.55, 0.75))
        m.blob(dims, (x, y, z), (m.rng.uniform(-15, 15), m.rng.uniform(-15, 15), m.rng.uniform(0, 360)),
               subdivisions=3, rough=0.16, facets=facets, material=material, tile=tile, tint=tint, metres=metres,
               vary=0.12, flatten=-0.6)
    return placed


def stone(key="stone", count=6, size=0.13, spread=0.11, seed=5, folder=None):
    m = Model(key, folder or ITEMS, seed=seed)
    stones(m, count, size, spread)
    m.export()


def metal_ore(key="metal-ore", count=4, size=0.16, spread=0.1, seed=7, folder=None):
    m = Model(key, folder or ITEMS, seed=seed)
    lumps = stones(m, count, size, spread, tint=(0.5, 0.33, 0.25), facets=0.3)
    for (x, y, z, s) in lumps:
        # Dark metal showing in specks: flakes lying along the surface.
        for _ in range(12):
            d = Vector((m.rng.uniform(-1, 1), m.rng.uniform(-1, 1), m.rng.uniform(0.15, 1))).normalized()
            p = Vector((x, y, z)) + Vector((d.x * s * 0.41, d.y * s * 0.33, d.z * s * 0.235))
            rot = d.to_track_quat("Z", "Y").to_euler()
            k = s * m.rng.uniform(0.12, 0.2)
            m.blob((k, k * 0.7, k * 0.1), p, tuple(math.degrees(a) for a in rot), subdivisions=2, rough=0.35, facets=0.3,
                   material=GLOSSY, tint=(0.34, 0.33, 0.34), metres=0.15)
    m.export()


def crystals(m, centre, normal, count, length, radius, tint, spread):
    n = Vector(normal).normalized()
    for _ in range(count):
        d = (n + Vector((m.rng.uniform(-spread, spread), m.rng.uniform(-spread, spread), m.rng.uniform(-spread, spread)))).normalized()
        h = length * m.rng.uniform(0.5, 1.0)
        r = radius * m.rng.uniform(0.6, 1.1)
        base = Vector(centre) + Vector((m.rng.uniform(-1, 1), m.rng.uniform(-1, 1), 0)) * radius * 1.5
        rot = d.to_track_quat("Z", "Y").to_euler()
        m.lathe([(r, -h * 0.15), (r, h * 0.62), (r * 0.25, h)], base + d * h * 0.1,
                tuple(math.degrees(a) for a in rot), sides=6, material=GLOSSY, tint=tint, sharp=20)


def sulphur_ore(key="sulphur-ore", count=4, size=0.16, spread=0.1, seed=9, folder=None):
    m = Model(key, folder or ITEMS, seed=seed)
    lumps = stones(m, count, size, spread, tint=(0.8, 0.76, 0.56), facets=0.3)
    for (x, y, z, s) in lumps:
        for _ in range(2):
            d = Vector((m.rng.uniform(-1, 1), m.rng.uniform(-1, 1), m.rng.uniform(0.3, 1))).normalized()
            centre = Vector((x, y, z)) + Vector((d.x * s * 0.36, d.y * s * 0.3, d.z * s * 0.2))
            crystals(m, centre, d, 9, s * 0.22, s * 0.045, (0.98, 0.8, 0.16), 0.45)
    m.export()


def high_grade_ore(key="high-grade-ore", count=3, size=0.15, spread=0.08, seed=11, folder=None):
    m = Model(key, folder or ITEMS, seed=seed)
    lumps = stones(m, count, size, spread, tint=(0.34, 0.35, 0.38), facets=0.45)
    for (x, y, z, s) in lumps:
        # Bright metal showing through: flat crystalline flakes just proud of the surface.
        for _ in range(7):
            d = Vector((m.rng.uniform(-1, 1), m.rng.uniform(-1, 1), m.rng.uniform(0.2, 1))).normalized()
            p = Vector((x, y, z)) + Vector((d.x * s * 0.44, d.y * s * 0.36, d.z * s * 0.26))
            rot = d.to_track_quat("Z", "Y").to_euler()
            k = s * m.rng.uniform(0.16, 0.26)
            m.blob((k, k * 0.7, k * 0.22), p, tuple(math.degrees(a) for a in rot), subdivisions=1, rough=0.25, facets=0.5,
                   material=GLOSSY, tint=(0.75, 0.77, 0.8), metres=0.15)
    m.export()


def metal_fragments(key="metal-fragments", seed=13):
    m = Model(key, seed=seed)
    for i in range(14):
        a = m.rng.uniform(0, 2 * math.pi)
        d = 0.09 * math.sqrt(m.rng.random())
        at = (math.cos(a) * d, math.sin(a) * d, 0.012 + m.rng.uniform(0, 0.05) * (1 - d / 0.1))
        rot = (m.rng.uniform(-35, 35), m.rng.uniform(-35, 35), m.rng.uniform(0, 360))
        kind = i % 4
        tile = STEEL if m.rng.random() < 0.55 else RUST
        tint = (0.95, 0.93, 0.9)
        if kind == 0:
            m.box((m.rng.uniform(0.05, 0.09), m.rng.uniform(0.03, 0.06), 0.004), at, rot, bevel=0.001, tile=tile, tint=tint, metres=0.2)
        elif kind == 1:
            m.cylinder(0.011, 0.009, at, rot, sides=6, bevel=0.0015, tile=STEEL, tint=tint, metres=0.1)
        elif kind == 2:
            m.cylinder(0.005, m.rng.uniform(0.05, 0.08), at, (90 + rot[0], 0, rot[2]), sides=8, tile=tile, tint=tint, metres=0.2)
        else:
            m.extrude([(-0.03, 0), (0.03, 0), (0.03, 0.008), (0.006, 0.008), (0.006, 0.035), (-0.002, 0.035), (-0.002, 0.008), (-0.03, 0.008)],
                      0.025, at, rot, bevel=0.001, tile=tile, tint=tint, metres=0.2)
    m.export()


def sulphur(key="sulphur", seed=15):
    m = Model(key, seed=seed)
    heap = m.blob((0.22, 0.2, 0.15), (0, 0, 0.0), subdivisions=4, rough=0.06, freq=3.0, flatten=0.0, tile=PARCHMENT,
                  tint=(0.96, 0.82, 0.3), metres=0.25, vary=0.18, vary_scale=40)
    for v in heap.data.vertices:
        v.co.z *= 0.55 + 0.45 * (1 - min(1, math.hypot(v.co.x / 0.11, v.co.y / 0.1)) ** 2)
    # Granules on and around the heap.
    for i in range(46):
        a = m.rng.uniform(0, 2 * math.pi)
        d = 0.115 * math.sqrt(m.rng.random()) * (1.25 if i > 30 else 0.9)
        ground = 0.075 * max(0.0, 1 - (d / 0.115) ** 2) * 0.9
        s_ = m.rng.uniform(0.008, 0.016)
        m.blob((s_, s_, s_ * 0.8), (math.cos(a) * d, math.sin(a) * d, ground + s_ * 0.2), subdivisions=1, rough=0.3,
               facets=0.3, tile=PARCHMENT, tint=(0.98, 0.84, 0.28) if i % 3 else (0.85, 0.7, 0.2), metres=0.1)
    m.export()


def refined_metal(key="refined-metal", seed=17):
    m = Model(key, seed=seed)
    bar = [(-0.085, 0.0), (0.085, 0.0), (0.075, 0.038), (-0.075, 0.038)]
    for i, (x, y, z, rz) in enumerate([(-0.035, 0, 0, 2), (0.035, 0.005, 0, -3), (0.0, 0.0, 0.0385, 88)]):
        m.extrude(bar, 0.06, (x, y, z), (0, 0, rz), bevel=0.004, segments=2, tile=STEEL, tint=(1.18, 1.2, 1.26), metres=0.25)
    m.export()


def charcoal(key="charcoal", seed=19):
    m = Model(key, seed=seed)
    stones(m, 8, 0.085, 0.085, material="M_PSG_Surface", tile=BASALT, tint=(0.3, 0.29, 0.28), facets=0.6, metres=0.2)
    m.export()


def fibre(key="fibre", seed=21):
    m = Model(key, seed=seed)
    length = 0.38
    for i in range(26):
        a = m.rng.uniform(0, 2 * math.pi)
        d = 0.022 * math.sqrt(m.rng.random())
        x, z = math.cos(a) * d, 0.026 + math.sin(a) * d * 0.7
        bend = m.rng.uniform(-0.03, 0.03)
        flare = m.rng.uniform(0.02, 0.05)
        pts = []
        for k in range(7):
            t = k / 6 - 0.5
            spread = 1 + abs(t) * flare * 18
            pts.append((x * spread + bend * t * t * 4, t * length * m.rng.uniform(0.92, 1.0), z * (1 + abs(t) * 0.3) + bend * t))
        green = m.rng.uniform(0.0, 0.18)
        m.tube(pts, 0.0028, sides=4, tile=CANVAS, tint=(0.66 - green, 0.74, 0.42 - green), metres=0.3, radii=None)
    # The twist that ties the bundle.
    ring = [(0.03 * math.cos(a), 0.0, 0.026 + 0.022 * math.sin(a)) for a in (2 * math.pi * i / 12 for i in range(13))]
    m.tube(ring, 0.006, sides=6, tile=CANVAS, tint=(0.55, 0.47, 0.32), metres=0.2, cap=False)
    m.export()


def lamp_oil(key="lamp-oil", seed=23):
    m = Model(key, seed=seed)
    body = [(0.0, 0.0), (0.072, 0.0), (0.078, 0.006), (0.078, 0.15), (0.072, 0.158), (0.03, 0.19), (0.0, 0.19)]
    m.lathe(body, sides=28, tile=OCHRE, tint=(1.0, 0.96, 0.9), metres=0.35)
    for z in (0.012, 0.145):
        m.lathe([(0.0795, z - 0.004), (0.082, z), (0.0795, z + 0.004)], sides=28, close=False, tile=STEEL, tint=(0.85, 0.85, 0.85), metres=0.3)
    m.cylinder(0.018, 0.03, (0.0, 0.0, 0.2), sides=16, bevel=0.002, tile=STEEL, tint=(0.8, 0.8, 0.82), metres=0.2)
    m.tube([(0.03, 0.0, 0.17), (0.07, 0.0, 0.21), (0.1, 0.0, 0.235)], 0.007, sides=10, tile=STEEL, tint=(0.8, 0.8, 0.8), metres=0.2)
    m.tube([(-0.05, -0.0, 0.16), (-0.05, 0.0, 0.215), (-0.0, 0.0, 0.235), (0.035, 0.0, 0.21)], 0.0045, sides=8, tile=STEEL,
           tint=(0.75, 0.75, 0.75), metres=0.2)
    m.export()


def gunpowder(key="gunpowder", seed=25):
    """A heap of black powder on a sheet of brown paper opened out, its corners still turned up, grains spilt around."""
    m = Model(key, seed=seed)
    half, n = 0.105, 14
    bm = bmesh.new()
    grid = {}
    for j in range(n + 1):
        for i in range(n + 1):
            x, y = -half + 2 * half * i / n, -half + 2 * half * j / n
            corner = max(0.0, abs(x) / half + abs(y) / half - 1.15)
            crumple = 0.0016 * noise.noise(Vector((x * 30, y * 30, 1.7)), noise_basis="PERLIN_ORIGINAL")
            # Turned diagonally so the folded corners point at the viewer and to the sides.
            c, s = math.cos(0.5), math.sin(0.5)
            grid[i, j] = bm.verts.new((x * c - y * s, x * s + y * c, 0.0012 + 0.05 * corner ** 1.3 + crumple * (1 + 6 * corner)))
    for j in range(n):
        for i in range(n):
            bm.faces.new((grid[i, j], grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1]))
    bmesh.ops.solidify(bm, geom=list(bm.faces), thickness=0.0009)
    m.add(bm, tile=PARCHMENT, tint=(0.6, 0.45, 0.31), metres=0.3, vary=0.08, vary_scale=30, sharp=0)
    heap = m.blob((0.14, 0.12, 0.09), (0.0, 0.0, 0.0012), subdivisions=4, rough=0.05, freq=3.0, flatten=0.0, tile=BASALT,
                  tint=(0.2, 0.2, 0.21), metres=0.08, vary=0.3, vary_scale=160)
    for v in heap.data.vertices:
        # A cone of poured powder: highest in the middle, a soft foot.
        r = min(1.0, math.hypot(v.co.x / 0.07, v.co.y / 0.06))
        v.co.z = 0.0012 + max(0.0, v.co.z) * (0.62 * (1 - r ** 1.6) + 0.06)
    heap.data.update()
    for i in range(70):
        a = m.rng.uniform(0, 2 * math.pi)
        d = m.rng.uniform(0.06, 0.1) * (0.6 + 0.4 * abs(math.cos(a * 1.5)))
        s_ = m.rng.uniform(0.0025, 0.0055)
        m.blob((s_, s_, s_ * 0.7), (math.cos(a) * d, math.sin(a) * d, 0.0015 + s_ * 0.25), subdivisions=1, rough=0.3, facets=0.3,
               tile=BASALT, tint=(0.16, 0.16, 0.17) if i % 4 else (0.28, 0.28, 0.29), metres=0.05)
    m.export()


ALL = {
    "wood": lambda: wood(),
    "stone": lambda: stone(),
    "metal-ore": lambda: metal_ore(),
    "sulphur-ore": lambda: sulphur_ore(),
    "high-grade-ore": lambda: high_grade_ore(),
    "metal-fragments": metal_fragments,
    "sulphur": sulphur,
    "refined-metal": refined_metal,
    "charcoal": charcoal,
    "fibre": fibre,
    "lamp-oil": lamp_oil,
    "gunpowder": gunpowder,
    # World pickups (gather.json): the same piles, bigger.
    "wood-pile": lambda: wood("wood-pile", count=12, scale=1.3, seed=31, folder=WORLD),
    "stone-pile": lambda: stone("stone-pile", count=11, size=0.2, spread=0.26, seed=33, folder=WORLD),
    "metal-pile": lambda: metal_ore("metal-pile", count=6, size=0.22, spread=0.2, seed=35, folder=WORLD),
    "sulphur-pile": lambda: sulphur_ore("sulphur-pile", count=6, size=0.22, spread=0.2, seed=37, folder=WORLD),
}

for key in script_args() or list(ALL):
    ALL[key]()

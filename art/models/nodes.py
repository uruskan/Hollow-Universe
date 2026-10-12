"""Resource outcrops (GROUND.md §9, gather.json): stone, metal, sulphur and high-grade, three depletion stages each.

Our own design, not the kit's stratified reconstructions of Rust's nodes: a cluster of two or three chipped, angular
boulders bedded in the ground, with the mineral showing in its own way (dark metal patches and rust streaks, yellow
crystal clusters and crusts, bright metal veins). Stage 2 has lost its top along an inclined break, stage 3 is the
low remnant; the breaks are pale, fresh rock. About 2 m across and 1.4 m high (gather.json: radius 1, height 1.4).

    blender -b --factory-startup --python art/models/nodes.py -- [kinds...]
"""
import math
import random
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import bmesh  # noqa: E402
from mathutils import Matrix, Vector, noise  # noqa: E402

from hollow import WORLD, script_args  # noqa: E402
from hollow.build import GLOSSY, ROCK, STEEL, GUNMETAL, Model  # noqa: E402

KINDS = {
    # rock tint, fresh-break tint
    "stone-node": ((0.58, 0.56, 0.53), (0.78, 0.76, 0.72)),
    "metal-node": ((0.46, 0.31, 0.23), (0.62, 0.5, 0.44)),
    "sulphur-node": ((0.72, 0.68, 0.5), (0.86, 0.83, 0.68)),
    "high-grade-node": ((0.3, 0.31, 0.34), (0.5, 0.51, 0.54)),
}

# Where each stage breaks: (point, normal); everything on the normal's side goes.
BREAKS = {2: ((0.0, 0.0, 0.8), (0.32, -0.22, 1.0)), 3: ((0.0, 0.0, 0.42), (-0.18, 0.26, 1.0))}


def boulder_mesh(rng, size, seed):
    """An angular boulder: a lumpy sphere cut by random planes (chipped facets), refined and roughened."""
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=3, radius=1.0)
    off = Vector((seed * 3.1, seed * 1.7, seed * 2.3))
    for v in bm.verts:
        n = v.co.normalized()
        v.co = n * (1 + 0.22 * noise.noise(n * 1.3 + off, noise_basis="PERLIN_ORIGINAL"))
    planes = []
    for _ in range(14):
        d = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-0.6, 1))).normalized()
        planes.append((d, rng.uniform(0.62, 0.86)))
    for v in bm.verts:
        for d, k in planes:
            h = v.co.dot(d)
            if h > k:
                v.co -= d * (h - k)
    bmesh.ops.subdivide_edges(bm, edges=list(bm.edges), cuts=2, use_grid_fill=True)
    for v in bm.verts:
        n = v.co.normalized()
        v.co += n * 0.035 * noise.fractal(v.co * 4.0 + off, 0.55, 2.0, 4, noise_basis="PERLIN_ORIGINAL")
    for v in bm.verts:
        if v.co.z < -0.45:
            v.co.z = -0.45 + (v.co.z + 0.45) * 0.15
    bmesh.ops.scale(bm, vec=Vector(size) * 0.5, verts=bm.verts)
    return bm


def mineral_tint(kind, base):
    """Per-vertex colour: the rock's own variation plus the mineral's marks."""

    def fn(co, tint):
        n = noise.noise(co * 1.7, noise_basis="PERLIN_ORIGINAL")
        c = [t * (0.92 + 0.12 * n) for t in base]
        if kind == "metal-node":
            streak = noise.noise(Vector((co.x * 7, co.y * 7, co.z * 1.2)), noise_basis="PERLIN_ORIGINAL")
            if streak > 0.25:
                k = min(1.0, (streak - 0.25) * 3)
                c = [c[0] * (1 - 0.15 * k) + 0.22 * k, c[1] * (1 - 0.35 * k) + 0.06 * k, c[2] * (1 - 0.55 * k)]
        elif kind == "sulphur-node":
            crust = noise.noise(co * 3.3 + Vector((5, 1, 2)), noise_basis="PERLIN_ORIGINAL")
            if crust > 0.3 and co.z > 0.1:
                k = min(1.0, (crust - 0.3) * 4)
                c = [c[0] * (1 - k) + 0.95 * k, c[1] * (1 - k) + 0.8 * k, c[2] * (1 - k) + 0.2 * k]
        elif kind == "high-grade-node":
            vein = abs(math.sin(co.dot(Vector((0.6, 0.3, 0.75))) * 9 + noise.noise(co * 2) * 2))
            if vein < 0.12:
                k = 1 - vein / 0.12
                c = [x * (1 - k) + 0.85 * k for x in c]
        return c

    return fn


def cut(bm, point, normal, fresh_tint=None):
    """Removes what lies on the normal's side of the plane and caps the break with a rough, pale fracture."""
    point, normal = Vector(point), Vector(normal).normalized()
    geom = list(bm.verts) + list(bm.edges) + list(bm.faces)
    res = bmesh.ops.bisect_plane(bm, geom=geom, plane_co=point, plane_no=normal, clear_outer=True)
    edges = [e for e in res["geom_cut"] if isinstance(e, bmesh.types.BMEdge)]
    if not edges:
        return []
    filled = bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=0)["faces"]
    cap = list(filled)
    rings = set()
    for _ in range(3):
        if not cap:
            break
        res = bmesh.ops.inset_region(bm, faces=cap, thickness=0.06, depth=0.0)
        rings |= {v for f in res["faces"] for v in f.verts}
    cap_verts = {v for f in cap for v in f.verts} | rings
    poked = bmesh.ops.poke(bm, faces=cap)
    cap_verts |= set(poked["verts"])
    # Roughen the break: interior vertices of the cap only.
    rim = {v for e in bm.edges if e.is_boundary for v in e.verts}
    for v in cap_verts:
        if v in rim:
            continue
        v.co += normal * 0.04 * noise.noise(v.co * 6.0, noise_basis="PERLIN_ORIGINAL")
    return list(cap_verts)


def outcrop(kind, stage, seed=200):
    rng = random.Random(seed + sum(map(ord, kind)))
    base, fresh = KINDS[kind]
    m = Model(f"{kind}-{stage}", WORLD, seed=seed + stage)
    m.rng = random.Random(seed)  # the same pieces at every stage
    layout = [((0.05, 0.0, 0.48), (1.55, 1.3, 1.25), (0, 0, 15)),
              ((-0.62, 0.28, 0.3), (0.95, 0.85, 0.75), (0, 8, -20)),
              ((0.55, 0.42, 0.22), (0.75, 0.7, 0.55), (6, 0, 40))]
    tint = mineral_tint(kind, base)
    rocks = []
    for i, (at, size, rot) in enumerate(layout):
        bm = boulder_mesh(rng, size, seed + i * 7.3)
        frame = Matrix.LocRotScale(Vector(at), Matrix.Rotation(math.radians(rot[2]), 3, "Z").to_quaternion()
                                   @ Matrix.Rotation(math.radians(rot[1]), 3, "Y").to_quaternion(), None)
        bm.transform(frame)
        fresh_verts = set()
        for s in (2, 3):
            if stage >= s:
                p, n = BREAKS[s]
                fresh_verts = set(cut(bm, p, n))
        if not bm.faces:
            bm.free()
            continue
        fresh_cos = {v.co.copy().freeze() for v in fresh_verts}

        def colour(co, t, fresh_cos=fresh_cos):
            c = tint(co, t)
            if co.copy().freeze() in fresh_cos:
                return [x * 0.4 + y * 0.6 for x, y in zip(c, fresh)]
            return c

        rocks.append(m.add(bm, material=ROCK, tint=base, metres=1.1, sharp=38, tint_fn=colour, vary=0.0))
    # Minerals on the surviving surface.
    pieces = []
    surface = []
    for o in rocks:
        me = o.data
        for v in me.vertices:
            if v.co.z > 0.12 and v.normal.z > -0.2:
                surface.append((v.co.copy(), v.normal.copy()))
    rng2 = random.Random(seed * 3 + len(kind))
    rng2.shuffle(surface)
    if kind == "metal-node":
        for co, n in surface[:44]:
            k = rng2.uniform(0.06, 0.12)
            rot = n.to_track_quat("Z", "Y").to_euler()
            pieces.append(m.blob((k, k * 0.7, k * 0.12), co - n * 0.012, tuple(math.degrees(a) for a in rot), subdivisions=2, rough=0.3,
                                 facets=0.3, material=GLOSSY, tint=(0.34, 0.33, 0.34), metres=0.2))
    elif kind == "sulphur-node":
        for co, n in [s for s in surface if s[1].z > 0.25][:14]:
            for _ in range(rng2.randint(5, 9)):
                d = (n + Vector((rng2.uniform(-0.5, 0.5), rng2.uniform(-0.5, 0.5), rng2.uniform(-0.3, 0.5)))).normalized()
                h = rng2.uniform(0.05, 0.12)
                r = rng2.uniform(0.012, 0.022)
                base_at = co + Vector((rng2.uniform(-1, 1), rng2.uniform(-1, 1), 0)) * 0.04 - n * 0.02
                rot = d.to_track_quat("Z", "Y").to_euler()
                pieces.append(m.lathe([(r, -h * 0.2), (r, h * 0.6), (r * 0.3, h)], base_at, tuple(math.degrees(a) for a in rot),
                                      sides=6, material=GLOSSY, tint=(0.95, 0.76, 0.14), sharp=20))
    elif kind == "high-grade-node":
        for co, n in surface[:30]:
            k = rng2.uniform(0.06, 0.12)
            rot = n.to_track_quat("Z", "Y").to_euler()
            pieces.append(m.blob((k, k * 0.6, k * 0.1), co - n * 0.008, tuple(math.degrees(a) for a in rot), subdivisions=1, rough=0.3,
                                 facets=0.5, material=GLOSSY, tint=(0.75, 0.77, 0.8), metres=0.12))
    m.export(ao=0.6, ao_distance=0.6)


ALL = list(KINDS)

for kind in script_args() or ALL:
    for stage in (1, 2, 3):
        outcrop(kind, stage)

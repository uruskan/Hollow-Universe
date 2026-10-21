"""Shape helpers for the road vehicles (art/models/togg.py, art/models/zamazingo.py; docs/VEHICLES.md §7).

Everything here is written in the **vehicle's frame**, the game's: +X the vehicle's left, +Y up, +Z forward (ships.json's
numbers, so the hubs, sockets and seats are the data's own). `VMesh` turns each piece into Blender's frame as it is made
(Blender (x, -z, y): +Z up, -Y the nose, as the KA-FA's and every world model's), so its UVs are projected as the other
models' are, and glTF's Y-up export brings the vehicle frame back exactly.

The pieces: tube, square tube and bar between points, blocks, discs and bent pipe (the KA-FA's helpers, in this frame);
knobbed tyres with their moulded sidewalls, beadlock steel rims and spoked rims; coil springs; and the rig's empties: a
wheel's hub, its steering, what spins, the arms that carry it and the dampers between them (ShipView.Rig.cs poses them).
"""
import math

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

from hollow.component_mesh import ComponentMesh
from hollow.export import vertex_ao, set_export_colour

# The vehicle frame into Blender's: (x, y, z) -> (x, -z, y).
C = Matrix(((1, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))
CI = C.inverted()


def b(p):
    """A point of the vehicle frame in Blender's."""
    return Vector((p[0], -p[2], p[1]))


class VMesh(ComponentMesh):
    """A ComponentMesh whose pieces are given in the vehicle's frame."""

    def mesh(self, name, vertices, faces, region, smooth=False, uvs=None):
        return super().mesh(name, [(v[0], -v[2], v[1]) for v in vertices], faces, region, smooth, uvs)


def place(obj, matrix):
    """Moves a piece made at the origin by `matrix`, a transform of the vehicle's frame."""
    m = C @ matrix @ CI
    for vertex in obj.data.vertices:
        vertex.co = m @ vertex.co
    obj.data.update()
    return obj


def rotation(axis, degrees):
    return Matrix.Rotation(math.radians(degrees), 4, axis)


def toward(a, b_):
    """The transform taking +X from the origin onto the segment a-b, its middle at the origin's image (+Y kept up)."""
    a, b_ = Vector(a), Vector(b_)
    d = (b_ - a).normalized()
    q = d.to_track_quat('X', 'Y' if abs(d.y) < .99 else 'Z')
    return Matrix.Translation((a + b_) / 2) @ q.to_matrix().to_4x4()


def beam(mesh, name, a, b_, size, region, bevel=.003):
    """A square or rectangular tube from a to b: size (across, up) in its own frame."""
    length = (Vector(b_) - Vector(a)).length
    return place(mesh.box(name, (0, 0, 0), (length, size[1], size[0]), region, bevel if mesh.lod < 2 else 0), toward(a, b_))


def rod(mesh, name, a, b_, radius, region, segments=None, end=None):
    length = (Vector(b_) - Vector(a)).length
    r2 = end if end is not None else radius
    rings = [(-length / 2, 0), (-length / 2, radius), (length / 2, r2), (length / 2, 0)]
    obj = mesh.lathe_x(name, rings, (0, 0), region, segments=segments or (14, 9, 6)[mesh.lod])
    return place(obj, toward(a, b_))


def block(mesh, name, centre, size, region, turn=None, bevel=.004):
    """A box: size (x, y, z) in the vehicle's axes, turned by `turn` (a vehicle-frame matrix) about its centre."""
    obj = mesh.box(name, (0, 0, 0), size, region, bevel if mesh.lod < 2 else 0)
    return place(obj, Matrix.Translation(centre) @ (turn or Matrix.Identity(4)))


def axis_matrix(axis):
    direction = {'X': Vector((1, 0, 0)), 'Y': Vector((0, 1, 0)), 'Z': Vector((0, 0, 1))}.get(axis) if isinstance(axis, str) else Vector(axis)
    direction = direction.normalized()
    q = direction.to_track_quat('X', 'Y' if abs(direction.y) < .99 else 'Z')
    return q.to_matrix().to_4x4()


def disc(mesh, name, centre, radius, thick, region, axis='Y', segments=None, inner=0.0):
    """A short cylinder (a ring when `inner` > 0) about an axis ('X', 'Y', 'Z' or a vector of the vehicle's frame)."""
    if inner > 0:
        rings = [(-thick / 2, inner), (-thick / 2, radius), (thick / 2, radius), (thick / 2, inner), (-thick / 2, inner)]
    else:
        rings = [(-thick / 2, 0), (-thick / 2, radius), (thick / 2, radius), (thick / 2, 0)]
    obj = mesh.lathe_x(name, rings, (0, 0), region, segments=segments or (24, 14, 8)[mesh.lod])
    return place(obj, Matrix.Translation(centre) @ axis_matrix(axis))


def lathe(mesh, name, rings, centre, region, axis='X', segments=None):
    """A solid of revolution: rings (along, radius) about `axis` through `centre`."""
    obj = mesh.lathe_x(name, rings, (0, 0), region, segments=segments)
    return place(obj, Matrix.Translation(centre) @ axis_matrix(axis))


def flat_disc(mesh, name, centre, radius, normal, region, steps=None):
    """A flat round face looking along `normal`, its region's picture laid on it (a dial, a lens)."""
    steps = steps or (24, 14, 8)[mesh.lod]
    verts, faces, uvs = [(0, 0, 0)], [], [(.5, .5)]
    for k in range(steps):
        a = math.tau * k / steps
        verts.append((radius * math.cos(a), radius * math.sin(a), 0))
        uvs.append((.5 + .5 * math.cos(a), .5 - .5 * math.sin(a)))
    for k in range(steps):
        faces.append((0, 1 + k, 1 + (k + 1) % steps))
    obj = mesh.mesh(name, verts, faces, region, False, uvs)
    n = Vector(normal).normalized()
    q = n.to_track_quat('Z', 'Y' if abs(n.y) < .99 else 'X')
    place(obj, Matrix.Translation(centre) @ q.to_matrix().to_4x4())
    orient(obj, lambda c: b(n))
    return obj


def quad(mesh, name, corners, region, normal=None, uvs=((0, 1), (1, 1), (1, 0), (0, 0))):
    """A one-sided panel through four corners (counter-clockwise seen from where it faces), its region laid on it."""
    obj = mesh.mesh(name, [tuple(c) for c in corners], [(0, 1, 2, 3)], region, False, list(uvs))
    if normal is not None:
        n = b(normal)
        orient(obj, lambda c: n)
    return obj


def grid_panel(mesh, name, at, nu, nv, region, smooth=True, want=None):
    """A panel through a grid of points `at(u, v)` (u, v in 0..1), its region stretched over it."""
    verts, faces, uvs = [], [], []
    for j in range(nv + 1):
        for i in range(nu + 1):
            verts.append(tuple(at(i / nu, j / nv)))
            uvs.append((i / nu, 1 - j / nv))
    for j in range(nv):
        for i in range(nu):
            a = j * (nu + 1) + i
            faces.append((a, a + 1, a + nu + 2, a + nu + 1))
    obj = mesh.mesh(name, verts, faces, region, smooth, uvs)
    if want is not None:
        orient(obj, want)
    return obj


def orient(obj, want):
    """Turns each face of a one-sided panel toward `want(centre)` (Blender), the way it is seen from (the game culls back
    faces; ComponentMesh recalculates normals as for a closed solid)."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    for face in bm.faces:
        if face.normal.dot(want(face.calc_center_median())) < 0:
            face.normal_flip()
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    return obj


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


def bolt(mesh, centre, axis, radius=.009, region='steel'):
    """A hex bolt head (six sides) on a face looking along `axis`."""
    return disc(mesh, 'Bolt', centre, radius, radius * .8, region, axis=axis, segments=6 if mesh.lod < 2 else 4)


def bolts(mesh, points, axis, radius=.009, region='steel'):
    if mesh.lod == 2:
        return
    for p in points:
        bolt(mesh, p, axis, radius, region)


def weld(mesh, centre, radius=.026, axis='Y'):
    if mesh.lod == 0:
        disc(mesh, 'Weld', centre, radius, radius, 'weld', axis=axis, segments=10)


def spring(mesh, name, a, b_, radius, wire, coils, region):
    """A coil spring between two points (its ends ground flat: a closed turn at each)."""
    a, b_ = Vector(a), Vector(b_)
    length = (b_ - a).length
    per = (18, 11, 7)[mesh.lod]
    turns = coils + 1
    pts = []
    for k in range(turns * per + 1):
        t = k / per
        # Closed turns at the ends: the pitch goes to one wire across at each end.
        f = t / turns
        end = wire * 1.05
        live = length - 2 * end
        if t < 1:
            h = end * t
        elif t > turns - 1:
            h = length - end * (turns - t)
        else:
            h = end + live * (t - 1) / (turns - 2)
        ang = math.tau * t
        pts.append(Vector((radius * math.cos(ang), radius * math.sin(ang), h)))
    m = Matrix.Translation(a) @ (b_ - a).normalized().to_track_quat('Z', 'Y' if abs((b_ - a).normalized().y) < .99 else 'X').to_matrix().to_4x4()
    pts = [m @ p for p in pts]
    return mesh.cable(name, [tuple(p) for p in pts], wire, region, closed=False, sides=(7, 5, 4)[mesh.lod])


# ---------------------------------------------------------------- tyres and rims (axle along X, centre at the origin)


def tyre(mesh, radius, width, rim, knob=.024, side=1, shoulder_rows=True, centre_rows=2, pitch=None, region='rubber'):
    """A knobbed off-road tyre about the X axis: a carcass with a bulging sidewall and a rounded crown, its moulded sidewall
    (the 'sidewall' strip laid round each face), and its knobs modelled, staggered blocks across the crown and shoulder
    lugs running down the sidewall (at LOD0 and LOD1; LOD2 keeps the carcass at the knobs' height). `side` (1 or -1) is
    which face of it looks out (+X or -X)."""
    lod = mesh.lod
    crown = radius - (knob if lod < 2 else 0)
    hw = width / 2
    segments = (56, 36, 20)[lod]
    # The carcass in (x along the axle, r): from the bead, out along the sidewall's bulge, round the shoulder and over the crown.
    bead = rim + .012
    belly = hw * 1.06
    prof = [(-hw * .80, bead - .006), (-hw * .88, bead + .012)]
    for k in range(1, 6):
        t = k / 6
        prof.append((-(hw * .88 + (belly - hw * .88) * math.sin(math.pi * t)), bead + (crown - .05 - bead) * t))
    for k in range(5):
        a = math.pi / 2 * k / 4
        prof.append((-(hw - .028) - .028 * math.cos(a) + 0.0, crown - .05 + .05 * math.sin(a)))
    mirror = [(-x, r) for x, r in reversed(prof)]
    rings = [(x, r) for x, r in prof] + [(0, crown + .004)] + mirror
    rings = [(x, r) for x, r in rings]
    # Close it at the bead (it meets the rim's flange there).
    rings = [(rings[0][0], rim - .01)] + rings + [(rings[-1][0], rim - .01)]
    carcass = lathe(mesh, 'Carcass', rings, (0, 0, 0), region, axis='X', segments=segments)
    # Open at the bead (inside the rim): turn its faces out from the tube's core, as the game culls back faces.
    core = (bead + crown) / 2

    def outward(c):
        r = math.hypot(c.y, c.z) or 1e-6
        return Vector((c.x, (r - core) * c.y / r, (r - core) * c.z / r))
    orient(carcass, outward)
    # The moulded sidewalls: a ring of the strip each side, a hair proud of the carcass.
    for s in (1, -1):
        sidewall_ring(mesh, s * (belly * .99 + .002), bead + .03, crown - .07, segments, s)
    if lod == 2:
        return
    # Knobs: a pitch of about four knob heights round the crown; per pitch a centre block (alternating sides), a shoulder
    # block each side (long and short in turn) and, on every other pitch, a lug down one sidewall.
    circumference = math.tau * crown
    pitch = pitch or knob * 4.0
    count = max(8, int(circumference / pitch / 2) * 2)
    cw, sw = width * .30, width * .24
    for k in range(count):
        a = math.tau * k / count
        alt = 1 if k % 2 == 0 else -1
        knob_block(mesh, alt * width * .11, a, crown, (cw, pitch * .46, knob), (.86, .82))
        if shoulder_rows:
            for s in (1, -1):
                long = (k + (s > 0)) % 2 == 0
                x = s * (hw - sw * .52)
                knob_block(mesh, x, a + (pitch * .25 / crown) * alt, crown - .006, (sw, pitch * (.56 if long else .40), knob + .004), (.84, .8),
                           tilt=s * 14)
                if lod == 0 and long:
                    side_lug(mesh, s * hw * .98, a + (pitch * .25 / crown) * alt, crown - .04, (knob * .7, pitch * .42, .045), s)


def knob_block(mesh, x, angle, crown, size, taper, tilt=0.0):
    """A tread block standing on the crown at `angle` round the X axis: base size (across, along, height), its top
    narrowed by `taper`; sunk 6 mm into the carcass so no seam shows."""
    w, d, h = size
    tw, td = w * taper[0], d * taper[1]
    sink = .006
    base = [(-w / 2, -d / 2), (w / 2, -d / 2), (w / 2, d / 2), (-w / 2, d / 2)]
    top = [(-tw / 2, -td / 2), (tw / 2, -td / 2), (tw / 2, td / 2), (-tw / 2, td / 2)]
    verts = [(px, -sink, pz) for px, pz in base] + [(px, h, pz) for px, pz in top]
    faces = [(3, 2, 1, 0), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    obj = mesh.mesh('Knob', verts, faces, 'rubber')
    # Local +Y (up off the crown) to the radial direction at `angle`; local Z along the rolling direction.
    m = rotation('X', -math.degrees(angle)) @ Matrix.Translation((x, crown, 0)) @ rotation('Z', tilt)
    return place(obj, m)


def side_lug(mesh, x, angle, r, size, s):
    """A lug moulded on the sidewall's shoulder, standing out along the axle."""
    h, d, run = size
    obj = mesh.box('SideLug', (0, 0, 0), (h, run, d), 'rubber', 0)
    m = rotation('X', -math.degrees(angle)) @ Matrix.Translation((x + s * h * .2, r - run * .45, 0))
    return place(obj, m)


def sidewall_ring(mesh, x, r0, r1, segments, s):
    """The sidewall's moulded strip: a flat ring at `x` from r0 to r1, facing out along s·X, the strip repeated twice round."""
    verts, faces, uvs = [], [], []
    for k in range(segments + 1):
        a = math.tau * k / segments
        for j, r in enumerate((r0, (r0 + r1) / 2, r1)):
            # Slightly domed: the middle stands out, as the sidewall's bulge.
            verts.append((x + s * (.004 if j == 1 else 0), r * math.cos(a), r * math.sin(a)))
            uvs.append(((2 * k / segments) % 1.0001 if k < segments else 1.0, 1 - j / 2))
    for k in range(segments):
        for j in range(2):
            a0 = k * 3 + j
            faces.append((a0, a0 + 1, a0 + 4, a0 + 3))
    obj = mesh.mesh('Sidewall', verts, faces, 'sidewall', True, uvs)
    orient(obj, lambda c: b((s, 0, 0)))
    return obj


def steel_rim(mesh, rim, width, dish=.04, region='cream', bolt_count=6, beadlock=True, side=1):
    """A pressed steel wheel about the X axis: flanged barrel, a dished centre with round vent holes (dark recesses), a hub
    with lug nuts and a cap, and an outer beadlock ring with its bolts. `side` is the face that looks out."""
    lod = mesh.lod
    hw = width / 2
    seg = (40, 24, 14)[lod]
    s = side
    # Barrel: flanges at each edge, a well in the middle (seen through the vent holes).
    barrel = [(-hw, rim - .02), (-hw, rim + .018), (-hw + .012, rim + .02), (-hw + .02, rim), (-hw * .2, rim - .03), (hw * .2, rim - .03),
              (hw - .02, rim), (hw - .012, rim + .02), (hw, rim + .018), (hw, rim - .02), (-hw, rim - .02)]
    lathe(mesh, 'Barrel', barrel, (0, 0, 0), region, segments=seg)
    # The centre disc, dished outward, its face toward s·X.
    face_x = s * dish
    centre = [(face_x - s * .006, rim - .028), (face_x - s * .006, .12), (face_x + s * .01, .085), (face_x + s * .01, .07),
              (face_x + s * .002, .07), (face_x - s * .004, .085), (face_x - s * .014, .12), (face_x - s * .014, rim - .028)]
    centre.append(centre[0])
    lathe(mesh, 'RimDisc', centre, (0, 0, 0), region, segments=seg)
    # Vent holes: dark recesses round the disc.
    if lod < 2:
        holes = 8
        for k in range(holes):
            a = math.tau * (k + .5) / holes
            r = (rim - .028 + .12) / 2
            disc(mesh, 'Vent', (face_x + s * .003, r * math.cos(a), r * math.sin(a)), .028, .004, 'dark', axis='X', segments=(12, 8, 6)[lod])
    # Hub, lug nuts, cap.
    disc(mesh, 'HubBoss', (face_x + s * .02, 0, 0), .075, .03, 'dark', axis='X')
    for k in range(bolt_count):
        a = math.tau * k / bolt_count
        disc(mesh, 'LugNut', (face_x + s * .038, .052 * math.cos(a), .052 * math.sin(a)), .011, .02, 'steel', axis='X', segments=6)
    disc(mesh, 'HubCap', (face_x + s * .045, 0, 0), .032, .03, 'steel', axis='X')
    if beadlock:
        ring_x = s * (hw - .002)
        disc(mesh, 'Beadlock', (ring_x, 0, 0), rim + .036, .014, 'dark', axis='X', inner=rim - .012)
        if lod < 2:
            n = (24, 12, 0)[lod]
            for k in range(n):
                a = math.tau * (k + .5) / n
                r = rim + .012
                disc(mesh, 'BeadBolt', (ring_x + s * .009, r * math.cos(a), r * math.sin(a)), .007, .008, 'steel', axis='X', segments=6)


def spoked_rim(mesh, rim, width, hub_r, hub_w, spokes=36, region='alu', disc_r=0.0, side=1):
    """A wire-spoked motorcycle wheel about the X axis: a channel rim, the spokes laced from each hub flange to the rim
    crossing, a hub, its axle nut; a brake disc on the left when `disc_r` > 0."""
    lod = mesh.lod
    hw = width / 2
    seg = (44, 28, 16)[lod]
    rim_prof = [(-hw, rim - .014), (-hw, rim + .012), (-hw + .006, rim + .014), (-hw + .01, rim + .002), (hw - .01, rim + .002),
                (hw - .006, rim + .014), (hw, rim + .012), (hw, rim - .014), (hw * .3, rim - .024), (-hw * .3, rim - .024), (-hw, rim - .014)]
    lathe(mesh, 'Rim', rim_prof, (0, 0, 0), region, segments=seg)
    hub = [(-hub_w / 2, 0), (-hub_w / 2, .022), (-hub_w / 2 + .004, hub_r), (-hub_w / 2 + .012, hub_r), (-hub_w / 2 + .016, hub_r * .6),
           (hub_w / 2 - .016, hub_r * .6), (hub_w / 2 - .012, hub_r), (hub_w / 2 - .004, hub_r), (hub_w / 2, .022), (hub_w / 2, 0)]
    lathe(mesh, 'Hub', hub, (0, 0, 0), 'alu', segments=(24, 14, 8)[lod])
    rod(mesh, 'Axle', (-hub_w / 2 - .03, 0, 0), (hub_w / 2 + .03, 0, 0), .009, 'steel')
    for sx in (1, -1):
        disc(mesh, 'AxleNut', (sx * (hub_w / 2 + .026), 0, 0), .014, .012, 'steel', axis='X', segments=6)
    if lod < 2:
        count = spokes if lod == 0 else spokes // 2
        for k in range(count):
            a = math.tau * k / count
            flange = 1 if k % 2 == 0 else -1
            hx = flange * (hub_w / 2 - .008)
            # Tangential lacing: each spoke leaves its flange ahead of or behind the radial line.
            lead = (1 if (k // 2) % 2 == 0 else -1) * .5
            p0 = Vector((hx, hub_r * .92 * math.cos(a), hub_r * .92 * math.sin(a)))
            ar = a + lead * .55
            p1 = Vector((flange * hw * .25, (rim - .02) * math.cos(ar), (rim - .02) * math.sin(ar)))
            rod(mesh, 'Spoke', p0, p1, .0022, 'steel', segments=(5, 4, 3)[lod])
            if lod == 0:
                rod(mesh, 'Nipple', p1, p1 + (p1 - p0).normalized() * .012, .0034, 'steel', segments=5)
    else:
        # Far away: the spokes as a faint disc each side.
        for sx in (1, -1):
            disc(mesh, 'SpokeDisc', (sx * hw * .12, 0, 0), rim - .02, .001, 'dark', axis='X', segments=12, inner=hub_r)
    if disc_r > 0:
        x = side * (hub_w / 2 + .012)
        disc(mesh, 'BrakeDisc', (x, 0, 0), disc_r, .004, 'disc' if 'disc' in mesh.regions else 'steel', axis='X', inner=disc_r * .55)
        if lod == 0:
            for k in range(18):
                a = math.tau * k / 18
                r = disc_r * .78
                disc(mesh, 'DiscHole', (x + side * .0025, r * math.cos(a), r * math.sin(a)), .0045, .001, 'dark', axis='X', segments=6)
        disc(mesh, 'DiscCarrier', (x, 0, 0), disc_r * .58, .006, 'alu', axis='X', inner=hub_r * .5, segments=(18, 10, 6)[lod])


# ---------------------------------------------------------------- building, shading, the rig's empties


def built(col, builder, name, material, regions, lod):
    mesh = VMesh(col, material, regions, lod)
    builder(mesh)
    obj = mesh.finish(f'{name}_lod{lod}')
    return obj


def shade(objects, samples=8, distance=.35):
    """Vertex occlusion over a set of objects seen together, written as the export colour."""
    occlusion = vertex_ao(objects, samples=samples, distance=distance, floor=True)
    for obj, ao in occlusion.items():
        colours = np.ones((len(ao), 4), np.float32)
        colours[:, :3] = (.45 + .55 * ao)[:, None]
        set_export_colour(obj, colours)


def plain(objects):
    for o in objects:
        set_export_colour(o, np.ones((len(o.data.vertices), 4), np.float32))


def empty(col, name, at=(0, 0, 0), parent=None, axis_up=None):
    """An empty at a vehicle-frame point. `axis_up` (a vehicle-frame direction) turns it so its own up (Godot's local +Y,
    Blender's local +Z) lies along that direction, its forward kept in the vehicle's middle plane."""
    e = bpy.data.objects.new(name, None)
    col.objects.link(e)
    if parent is not None:
        e.parent = parent
        e.matrix_parent_inverse = Matrix.Identity(4)
    world = Matrix.Translation(b(at))
    if axis_up is not None:
        up = b(axis_up).normalized()
        world = world @ up.to_track_quat('Z', 'Y').to_matrix().to_4x4()
    if parent is not None:
        bpy.context.view_layer.update()
        e.matrix_world = world
    else:
        e.matrix_world = world
    bpy.context.view_layer.update()
    return e


def adopt(obj, parent):
    """Puts a mesh built in the vehicle's frame under `parent`, keeping where it is (its vertices moved into the parent's
    frame, so its own transform stays the identity)."""
    bpy.context.view_layer.update()
    inv = parent.matrix_world.inverted()
    obj.data.transform(inv @ obj.matrix_world)
    obj.matrix_world = Matrix.Identity(4)
    obj.parent = parent
    obj.matrix_parent_inverse = Matrix.Identity(4)
    obj.matrix_basis = Matrix.Identity(4)
    return obj


def tris(objects):
    return {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objects if o.type == 'MESH'}

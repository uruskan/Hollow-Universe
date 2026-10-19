"""Rounded boxes and thin sleeves around them, for the original cosmetic item assets (art/models/<item>.py).

Vertices come from a grid on a box's faces pushed onto its rounded corners, so the flat faces stay flat and the
corners take an even arc at any level of detail. Each builder returns (vertices, faces) for `ComponentMesh.mesh`,
which box-projects the UVs per face; `weld` joins any coincident vertices left between parts.
"""
import math

import bmesh


def axis_samples(half, radius, corner, flat):
    """Coordinates across one axis: `corner` steps over each rounded edge (even angles), `flat` across the middle."""
    inner = half - radius
    edge = [inner + radius * math.tan(math.pi / 4 * k / corner) for k in range(1, corner + 1)]
    middle = [-inner + 2 * inner * k / flat for k in range(flat + 1)] if inner > 0 else [0.0]
    return [-e for e in reversed(edge)] + middle + edge


def rounded_point(c, half, radius):
    """The box-surface point `c` moved onto the rounded box; also returns its outward normal."""
    q = [max(-(h - radius), min(h - radius, x)) for x, h in zip(c, half)]
    d = [x - y for x, y in zip(c, q)]
    length = math.sqrt(sum(x * x for x in d)) or 1.0
    n = [x / length for x in d]
    return [y + radius * x for y, x in zip(q, n)], n


def rounded_box(half, radius, corner, flat, displace=None):
    """A closed rounded box of half extents `half` centred on the origin. `flat`: segments across each face's middle
    per axis (a number or three); `displace(point, normal)` returns metres along the normal (dents, sag)."""
    flats = flat if isinstance(flat, (list, tuple)) else (flat, flat, flat)
    samples = [axis_samples(h, radius, corner, f) for h, f in zip(half, flats)]
    vertices, faces, index = [], [], {}

    def vertex(c):
        # Face grids share their edges: one vertex per box-surface point, so the mesh is closed.
        key = tuple(round(x, 7) for x in c)
        if key not in index:
            p, n = rounded_point(c, half, radius)
            if displace:
                k = displace(p, n)
                p = [x + y * k for x, y in zip(p, n)]
            index[key] = len(vertices)
            vertices.append(tuple(p))
        return index[key]

    for axis in range(3):
        a, b = [(1, 2), (0, 2), (0, 1)][axis]
        for sign in (-1, 1):
            grid = []
            for u in samples[a]:
                row = []
                for v in samples[b]:
                    c = [0.0, 0.0, 0.0]
                    c[axis], c[a], c[b] = sign * half[axis], u, v
                    row.append(vertex(c))
                grid.append(row)
            for i in range(len(samples[a]) - 1):
                for j in range(len(samples[b]) - 1):
                    q = (grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1])
                    faces.append(q if sign > 0 else q[::-1])
    return vertices, faces


def profile_loop(half_y, half_z, radius, corner):
    """A rounded rectangle across Y and Z, counter-clockwise from +Y: [(y, z, ny, nz)]; straight runs join corners."""
    loop = []
    for cy, cz, start in ((1, 1, 0), (-1, 1, 90), (-1, -1, 180), (1, -1, 270)):
        oy, oz = cy * (half_y - radius), cz * (half_z - radius)
        for k in range(corner + 1):
            a = math.radians(start + 90 * k / corner)
            loop.append((oy + radius * math.cos(a), oz + radius * math.sin(a), math.cos(a), math.sin(a)))
    return loop


def sleeve(x0, x1, half_y, half_z, radius, corner, thickness, gap=0.0004, slices=2):
    """A thin band (paper, tape) around a rounded box's middle from x0 to x1: inner and outer walls and their edges."""
    loop = profile_loop(half_y, half_z, radius, corner)
    n = len(loop)
    vertices, faces = [], []
    xs = [x0 + (x1 - x0) * k / slices for k in range(slices + 1)]
    for offset in (gap, gap + thickness):
        for x in xs:
            for y, z, ny, nz in loop:
                vertices.append((x, y + ny * offset, z + nz * offset))
    ring = n
    layer = (slices + 1) * n
    for side in (0, 1):
        for s in range(slices):
            for j in range(n):
                a = side * layer + s * ring + j
                b = side * layer + s * ring + (j + 1) % n
                q = (a, b, b + ring, a + ring)
                faces.append(q if side else q[::-1])
    for s in (0, slices):
        for j in range(n):
            a = s * ring + j
            b = s * ring + (j + 1) % n
            q = (a, b, b + layer, a + layer)
            faces.append(q if s == slices else q[::-1])
    return vertices, faces


def weld(obj, distance=1e-6):
    """Joins coincident vertices (a rounded box's face seams) after UVs are set, then fixes the normals."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=distance)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    return obj

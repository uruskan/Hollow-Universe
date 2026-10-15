"""The Iron Wake's closures (IRON_WAKE.md §9.1 "As built: the bow and the stern closed"). The asset leaves the space
under Deck A, between the hull's bottom plate (its top at z 2.21) and the deck's floor (its underside at z 2.98), open
at both ends, and a few more places open to the hull's inside, so from outside one saw the deck's underside, the hold's
floor edge and linings, and the bow's void. The import closes them with pieces of its own, in the asset's language
(chamfered plates, raised panels, bolt heads) and finishes, merged into the Body at every level:

- At the bow, a recessed armoured housing round the keel lance: two oxide side cheeks fitted into the hull's slanted
  oxide cheeks (cut along their mid-plane), a dark steel roof under the brown front plate and a back wall the lance's
  barrels pass through, with an oxide mantlet strip above them. The muzzles (y -20.5) sit in a closed embrasure.
- At the stern, a dark steel bulkhead under the ramp's hinge: two wings from the hull's oxide cheeks to the ramp's
  width, up under the stern frame, and between them a plate set back under the ramp (its face at y 17.48, ahead of
  the hinge barrel's front at 17.52), so the ramp swings clear of it; raised oxide panels and bolts on their faces.
- Beside the bridge, tread plates over the two slots (0.5 m wide, 8.5 m long) between the bridge floor's ledge and
  the hull's coaming, which were open from the sky to the bow's bottom plate and Deck A's floor, and a cap over Deck
  A's forward bulkhead where it crosses them.
- At the bow's top, a floor for the shelf between the front plate and the bow deck (a 9 cm slit into the bow's void).
- Along Deck A, a dark steel backing behind the side panels, whose 4-5 cm seams every 3 m showed the linings; strips
  under two narrow slots along the top; blocks behind the joints of the bow section's side panels (y -15.0, -18.0,
  -20.5), through which one saw across the bow's void.

Coordinates are the export's Blender frame (the model turned: the nose at -Y, +Z up), metres, measured on the asset
with rays (the bow cheeks' inner faces: x = 2.305 + 0.6 (y + 21.5) + 0.6 (z - 2.8) between z 2.2 and 3.2; their
plates are 0.18 m thick). build() returns the pieces as mesh objects in the asset's native frame, so the import merges
them as it merges the asset's own groups; air_boxes() and allowed_openings() are what the import's check calls the
hull's inside and its openings, and collision_pieces() what the data file gains.
"""
import math

import bmesh
import bpy
from mathutils import Matrix, Vector

BEVEL = 0.012          # the asset's plates are chamfered about 1 cm
BOW_IN, BOW_OUT = 1.74, 1.90        # the housing's cheeks, |x| (the lance's barrels reach 1.64)
BOW_BACK = (-19.16, -19.00)         # the back wall, y
BOW_FLOOR, BOW_TOP = 2.12, 3.40     # into the bottom plate (2.03-2.21) and the front plate (from 3.30)
BOW_ROOF = 3.26                     # the roof's underside
BOW_FRONT = -22.95                  # inside the front plate's thickness (-23.12 .. -22.88)
STERN_FACE, STERN_BACK = 17.72, 17.30   # the wings' faces (the stern frame's plates span 17.56-17.74)
STERN_RAMP = 2.78                   # |x| of the plate under the ramp (the ramp is 2.725 wide each side)
STERN_SIDE = 5.45                   # into the hull's oxide cheeks (inner faces 4.95-5.35, outer past 6)
STERN_FLOOR, STERN_TOP = 2.12, 3.215    # into the bottom plate; under the stern frame (from 3.20)
STERN_SET = 17.48                   # the plate under the ramp: its face, 4 cm ahead of the hinge barrel (17.52)
STERN_SET_TOP = 3.19                # up into the ramp's threshold plate (its underside at 3.02)


def cheek_mid_y(x, z):
    """The y at which the mid-plane of the hull's slanted bow cheek (0.10 m outside its inner face) passes x, at z."""
    return -21.5 + (x - 0.10 - 2.305 - 0.6 * (z - 2.8)) / 0.6


def _solid(bm, points, material, bevel=BEVEL):
    """A convex solid (the hull of `points`) with chamfered edges, its faces given `material` (slot index)."""
    new = bmesh.new()
    vs = [new.verts.new(p) for p in points]
    bmesh.ops.convex_hull(new, input=vs)
    bmesh.ops.dissolve_limit(new, angle_limit=0.01, verts=new.verts, edges=new.edges)
    if bevel > 0:
        bmesh.ops.bevel(new, geom=list(new.edges), offset=bevel, segments=1, affect="EDGES", clamp_overlap=True)
    bmesh.ops.recalc_face_normals(new, faces=new.faces)
    me = bpy.data.meshes.new("closure_piece")
    new.to_mesh(me)
    new.free()
    base = len(bm.verts)
    for v in me.vertices:
        bm.verts.new(v.co)
    bm.verts.ensure_lookup_table()
    for p in me.polygons:
        f = bm.faces.new([bm.verts[base + i] for i in p.vertices])
        f.material_index = material
    bpy.data.meshes.remove(me)


def _box(bm, lo, hi, material, bevel=BEVEL):
    pts = [(x, y, z) for x in (lo[0], hi[0]) for y in (lo[1], hi[1]) for z in (lo[2], hi[2])]
    _solid(bm, pts, material, bevel)


def _bolts(bm, centres, axis, material, size=0.035, height=0.03):
    """Bolt heads (small chamfered blocks) standing `height` out of a face along `axis` (+/-1 on x, y or z: 'y+')."""
    a, sign = axis[0], 1 if axis[1] == "+" else -1
    i = "xyz".index(a)
    for c in centres:
        lo, hi = list(c), list(c)
        for k in range(3):
            if k == i:
                lo[k], hi[k] = (c[k] - 0.01, c[k] + height) if sign > 0 else (c[k] - height, c[k] + 0.01)
            else:
                lo[k], hi[k] = c[k] - size / 2, c[k] + size / 2
        _box(bm, lo, hi, material, bevel=0.006)


def bow(bm, steel, oxide, detail):
    for s in (-1, 1):
        def x(v):
            return s * v
        # The side cheek: from the hull's cheek (cut along its mid-plane, never in front of the front plate's face)
        # back into the back wall.
        pts = []
        for xv in (BOW_IN, BOW_OUT):
            for z in (BOW_FLOOR, BOW_TOP):
                pts.append((x(xv), max(cheek_mid_y(xv, z), BOW_FRONT), z))
                pts.append((x(xv), BOW_BACK[1], z))
        _solid(bm, pts, oxide)
    # The roof, under the front plate, and the back wall, through which the barrels pass.
    _box(bm, (-BOW_OUT, BOW_FRONT, BOW_ROOF), (BOW_OUT, BOW_BACK[1], BOW_TOP), steel)
    _box(bm, (-BOW_OUT, BOW_BACK[0], BOW_FLOOR), (BOW_OUT, BOW_BACK[1], BOW_TOP), steel)
    # The mantlet strip above the barrels (their tops at z 2.69) and the frame strip under the roof.
    _box(bm, (-BOW_IN, BOW_BACK[0] - 0.08, 2.76), (BOW_IN, BOW_BACK[0] + 0.01, 3.00), oxide)
    if detail:
        _box(bm, (-BOW_IN, BOW_FRONT + 0.15, BOW_ROOF - 0.06), (BOW_IN, BOW_FRONT + 0.30, BOW_ROOF + 0.01), oxide)
        xs = [-1.55, -1.0, -0.45, 0.0, 0.45, 1.0, 1.55]
        _bolts(bm, [(v, BOW_BACK[0] - 0.08, 2.88) for v in xs], "y-", steel)
        _bolts(bm, [(v, BOW_BACK[0], 3.13) for v in xs], "y-", steel)


def stern(bm, steel, oxide, detail):
    for s in (-1, 1):
        def x(v):
            return s * v
        lo_x, hi_x = sorted((x(STERN_RAMP), x(STERN_SIDE)))
        _box(bm, (lo_x, STERN_BACK, STERN_FLOOR), (hi_x, STERN_FACE, STERN_TOP), steel)
        # A raised oxide panel on each wing, bolted round its edge.
        plo, phi = sorted((x(STERN_RAMP + 0.22), x(4.85)))
        _box(bm, (plo, STERN_FACE - 0.01, 2.36), (phi, STERN_FACE + 0.035, 3.02), oxide)
        if detail:
            bx = [x(v) for v in (3.15, 3.7, 4.25, 4.7)]
            _bolts(bm, [(v, STERN_FACE + 0.035, z) for v in bx for z in (2.44, 2.94)], "y+", steel)
    # The plate under the ramp, set back of the hinge barrel's sweep, into the threshold plate above.
    _box(bm, (-STERN_RAMP, STERN_BACK, STERN_FLOOR), (STERN_RAMP, STERN_SET, STERN_SET_TOP), steel)
    _box(bm, (-2.45, STERN_SET - 0.01, 2.36), (2.45, STERN_SET + 0.025, 2.86), oxide)
    if detail:
        bx = [-2.25, -1.5, -0.75, 0.0, 0.75, 1.5, 2.25]
        _bolts(bm, [(v, STERN_SET + 0.025, z) for v in bx for z in (2.44, 2.78)], "y+", steel)


# The slots beside the bridge, open from the sky to the bow's bottom plate and Deck A's floor: between the bridge
# floor's ledge (top z 7.395-7.40) and the hull's sloped coaming (top 7.55-7.74), measured with rays from above:
# (y, the slot's inner edge |x|, its outer edge |x|), the same on both sides to y -10.0; the port one runs on to -9.8.
SLOT = [(-19.05, 2.70, 3.62), (-18.8, 2.82, 3.56), (-18.4, 3.22, 3.80), (-18.0, 3.34, 4.04), (-17.0, 3.70, 4.22),
        (-16.0, 4.04, 4.42), (-15.0, 4.18, 4.60), (-14.0, 4.30, 4.78), (-13.0, 4.44, 4.96), (-12.0, 4.58, 5.14),
        (-11.0, 4.72, 5.14), (-10.2, 4.84, 5.14), (-9.85, 4.84, 5.14)]
SLOT_PORT_END = -9.55
# Under the bridge floor's ledge (its edge lies 0.1-0.25 m inside the bridge eave's, which hides it from above),
# and under the coaming.
SLOT_IN, SLOT_OUT = 0.40, 0.22
SLOT_TOP, SLOT_BOTTOM = 7.38, 7.20    # 1.5 cm under the ledge's top, so the two never fight
# The shelf at the bow's top, between the front plate's top (z 7.10) and the bow deck above (its underside 7.19, its
# front face at y -19.0): no floor, so a 9 cm slit showed the bow's void from ahead and the void lay open from above
# and in front. Its side walls' inner faces at z 7.15: |x| = 1.78 + 0.6 (y + 22.8).
SHELF_FRONT, SHELF_BACK = -22.95, -18.85
SHELF_BOTTOM, SHELF_TOP = 7.04, 7.18


def shelf_wall(y):
    return 1.78 + 0.6 * (y + 22.8)


def bridge_slots(bm, tread):
    for s in (-1, 1):
        rows = list(SLOT)
        if s < 0:
            rows.append((SLOT_PORT_END, rows[-1][1], rows[-1][2]))
        for (y0, a0, b0), (y1, a1, b1) in zip(rows, rows[1:]):
            pts = []
            for y, a, b in ((y0, a0, b0), (y1, a1, b1)):
                for xv in (a - SLOT_IN, b + SLOT_OUT):
                    for z in (SLOT_BOTTOM, SLOT_TOP):
                        pts.append((s * xv, y, z))
            # Each piece a little longer than its stretch, so neighbours overlap rather than meet.
            pts = [(x, y + (-0.03 if y == y0 else 0.03), z) for x, y, z in pts]
            _solid(bm, pts, tread, bevel=0.006)


def bow_shelf(bm, steel):
    pts = []
    for y in (SHELF_FRONT, SHELF_BACK):
        w = shelf_wall(y) + 0.15
        for x in (-w, w):
            for z in (SHELF_BOTTOM, SHELF_TOP):
                pts.append((x, y, z))
    _solid(bm, pts, steel)


# The hull's side panels along Deck A stand 4-5 cm apart every 3 m (y -12, -9, ... 9), and between them and Deck A's
# linings (outer faces at |x| 5.565) is an empty cavity, so each seam showed the linings' interior paint from outside.
# A dark steel backing in the cavity (the panels' inner faces are at |x| 5.75-6.28): the seams read as recessed joints.
# On the port side it stops round the airlock's passage (y 0.8-3.2).
BACKING = (5.59, 5.69)
BACKING_Y = (-12.95, 17.40)
BACKING_Z = (3.25, 7.32)
AIRLOCK_GAP = (0.70, 3.40)
# Two narrow slots along the top between Deck B's wall and the coaming (|x| 5.26-5.34 over y -8.9 .. -5.85; |x|
# 5.50-5.54 over y -1.85 .. 0.05; down to Deck A's floor), each under a strip below the tops round it (7.68-7.77).
TOP_STRIPS = [((5.18, -8.95, 7.52), (5.44, -5.80, 7.66)), ((5.44, -1.95, 7.52), (5.62, 0.15, 7.63))]
# Cracks between the bow section's side panels, through which rays crossed the bow's void from side to side: a 4.6 cm
# seam at y -15.0 (z 3.4 to 7.2; the panels at |x| 5.24-5.40), a 4 mm one where the tapered panels meet the straight
# ones at y -18.0 (|x| 4.66-4.89), and the step at y -20.5 on the taper (|x| 3.21-3.39). A block behind each joint,
# its outer face inside both panels' thickness, its back in the void.
JOINTS = [((4.95, -15.15, 3.30), (5.30, -14.85, 7.20)), ((4.50, -18.12, 3.32), (4.74, -17.88, 7.22)),
          ((2.95, -20.56, 3.70), (3.20, -20.44, 7.00))]
# Deck A's forward bulkhead pokes up through the slot beside the bridge (its top at z 7.40): a cap over it.
BULKHEAD_CAP = ((4.00, -13.42, 7.30), (5.36, -12.98, 7.43))


def mirrored(bm, lo, hi, material, bevel=BEVEL):
    for s in (-1, 1):
        a = (s * lo[0], lo[1], lo[2])
        b = (s * hi[0], hi[1], hi[2])
        _box(bm, (min(a[0], b[0]), lo[1], lo[2]), (max(a[0], b[0]), hi[1], hi[2]), material, bevel)


def seams(bm, steel):
    for s in (-1, 1):
        spans = [BACKING_Y] if s > 0 else [(BACKING_Y[0], AIRLOCK_GAP[0]), (AIRLOCK_GAP[1], BACKING_Y[1])]
        xs = sorted((s * BACKING[0], s * BACKING[1]))
        for y0, y1 in spans:
            _box(bm, (xs[0], y0, BACKING_Z[0]), (xs[1], y1, BACKING_Z[1]), steel, bevel=0.0)
    for lo, hi in TOP_STRIPS + JOINTS + [BULKHEAD_CAP]:
        mirrored(bm, lo, hi, steel)


def air_boxes():
    """The spaces inside the hull, for the import's check (art/hollow/large.py inside_seen): (name, lo, hi) in the
    export's Blender frame, each kept clear of the hull's skin: the space under Deck A (narrower at the bow's taper),
    the bow's void above it (three boxes following the taper), and Deck A within its linings."""
    from mathutils import Vector as V
    return [("under deck A", V((-4.85, -13.0, 2.25)), V((4.85, 17.25, 2.95))),
            ("under the bow", V((-3.3, -18.9, 2.25)), V((3.3, -13.0, 2.95))),
            ("bow void", V((-1.4, -22.7, 3.5)), V((1.4, -13.4, 7.0))),
            ("bow void", V((-3.3, -20.0, 3.5)), V((3.3, -13.4, 7.0))),
            ("bow void", V((-4.4, -17.5, 3.5)), V((4.4, -13.4, 7.0))),
            ("deck A", V((-4.95, -12.95, 3.205)), V((4.95, 16.9, 7.15)))]


def allowed_openings():
    """The openings that show the inside on purpose, export frame: the airlock (its door and its sill) and the ramp's
    doorway. The windows are their glass."""
    from mathutils import Vector as V
    return [("airlock", V((-7.9, -2.0, 2.4)), V((-5.9, 3.5, 6.3))),
            ("ramp", V((-2.8, 17.3, 3.0)), V((2.8, 18.6, 7.2))),
            # The asset's upper port weather door, authored open (asset_manifest.json gunner_access_openings).
            ("weather door", V((-5.6, -9.9, 7.35)), V((-4.3, -8.1, 10.25)))]


def collision_pieces():
    """Pieces for the data file where a player could otherwise fall through a closure: the slots beside the bridge
    from export y -12.1 to -9.9 (-9.5 on the port side), which the asset's convex pieces leave open behind the side
    weather plates (its "Shoulder" pieces cover the rest). Game axes (x, the export's z, minus the export's y)."""
    out = []
    for s in (-1, 1):
        y0 = -9.50 if s < 0 else -9.85
        xs = sorted((s * 4.35, s * 5.30))
        pts = sorted({(round(x, 3), round(z, 3), round(-y, 3)) for x in xs for y in (-12.15, y0) for z in (7.18, 7.40)})
        out.append({"part": "body", "name": "Closure_BridgeSlot", "points": [list(p) for p in pts]})
    return out


def build(materials, out, turn, uv_metres=None, levels=4):
    """{level: mesh object} of the closures in the asset's native frame (`turn` is the import's TURN_WORLD); LOD0 and
    LOD1 with bolts and strips, LOD2 and LOD3 without. `materials`: {finish: bpy material}."""
    made = {}
    for level in range(levels):
        me = bpy.data.meshes.new(f"Closures_LOD{level}")
        me.materials.append(materials["DarkSteel"])
        me.materials.append(materials["OxidePaint"])
        me.materials.append(materials["TreadSteel"])
        bm = bmesh.new()
        bow(bm, 0, 1, level < 2)
        stern(bm, 0, 1, level < 2)
        bridge_slots(bm, 2)
        bow_shelf(bm, 0)
        seams(bm, 0)
        bm.to_mesh(me)
        bm.free()
        project_uv(me, uv_metres or {})
        me.transform(turn.inverted())
        o = bpy.data.objects.new(f"Closures_LOD{level}", me)
        out.objects.link(o)
        made[level] = o
    return made


def project_uv(me, uv_metres):
    """Box-projected UV0 ("UV0_Material", as the asset's), each finish's repeat as the asset bakes it."""
    uv = me.uv_layers.new(name="UV0_Material")
    names = [m.name if m else "" for m in me.materials]
    for p in me.polygons:
        n = p.normal
        axis = max(range(3), key=lambda i: abs(n[i]))
        a, b = [i for i in range(3) if i != axis]
        scale = uv_metres.get(names[p.material_index], 2.0)
        for li in p.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            uv.data[li].uv = (co[a] / scale, co[b] / scale)


def ramp_clear(closures, ramp_parts, hinge_axis_world, angles):
    """The ramp's leaves never touch the closures as they swing: for each pose in `angles` ([(main deg, leaf deg)]),
    no triangle of the posed leaves overlaps a closure triangle. `ramp_parts`: [(object, pivot world point, parent
    index or None)] with the main leaf first; the hinge axis in world space. Returns the poses that touch."""
    from mathutils.bvhtree import BVHTree

    def tree(objs, mats):
        verts, tris = [], []
        for o, m in zip(objs, mats):
            me = o.data
            me.calc_loop_triangles()
            base = len(verts)
            mw = m @ o.matrix_world
            verts += [mw @ v.co for v in me.vertices]
            tris += [[base + i for i in t.vertices] for t in me.loop_triangles]
        return BVHTree.FromPolygons(verts, tris, epsilon=0.0)
    closed = tree([closures], [Matrix.Identity(4)])
    touching = []
    axis = Vector(hinge_axis_world).normalized()
    for main_deg, leaf_deg in angles:
        mats = []
        main_m = None
        for o, pivot, parent in ramp_parts:
            deg = main_deg if parent is None else leaf_deg
            p = Vector(pivot) if parent is None else main_m @ Vector(pivot)
            m = Matrix.Translation(p) @ Matrix.Rotation(math.radians(deg), 4, axis) @ \
                Matrix.Translation(-p)
            if parent is not None:
                m = m @ main_m
            else:
                main_m = m
            mats.append(m)
        if closed.overlap(tree([o for o, _, _ in ramp_parts], mats)):
            touching.append((main_deg, leaf_deg))
    return touching

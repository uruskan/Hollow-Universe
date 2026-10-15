"""The Iron Wake's loading mechanisms (docs/IRON_WAKE.md §6.6 and §9.5 "As built"), made by the import's hook.

    blender -b --factory-startup --python art/build.py -- iron_wake        # the import calls build(ctx), then clips()
    blender -b --factory-startup --python art/ships/review_loaders.py -- <out folder> [what...]   # review renders

build(ctx) runs inside the asset's scene right after it is loaded, in the asset's native frame (+X forward, +Z up,
metres), before any conversion. Geometry is authored here in each station's pitch frame at rest in GAME axes (+X port,
+Y up, +Z the nose, metres from the pitch pivot) and placed into the native frame by G2N. Every moving piece is an
empty at its pivot, turned by NODE_ROT so that the exported node carries no rotation: its axes are the model's game
axes, the axes the motions of game/data/ground/batteries.json are written in.

Why it is built this way (IRON_WAKE.md §9.5):
- The asset's barrels end inside closed cast receivers, just in front of the elevation axle and the gun house: there is
  no room behind a breech for a rammer, and nothing of a breech can be seen. So each battery feeds through its
  trunnion, as naval turrets do (the round turns to the gun's elevation on the trunnion axis, so any elevation works),
  and the breech is brought out where it can be seen.
- Siege: a ready rack of three rounds per barrel stands in the open cockpit beside the gunner, a fourth waiting in its
  hood; a two-stage feed rammer drives the lowest round forward into a port in the gun house; inside, the transfer
  cradle on the trunnion axis swings it to the barrel's line; a chain rammer drives it out of the receiver's ram port,
  across the breech (exposed between the receiver and the shortened barrel sleeve) into the chamber; a sliding wedge
  breech block closes it. The barrel's hidden rear end is moved up to the new chamber mouth.
- The 48 cm bore takes the 9 cm rocket in a launch carrier: a discarding sabot of three steel petals on a pusher base
  (one per round, a part of the mechanism); the muzzle's 48 cm hollow bore stays as it is.
- Defense: the 10.4 cm tubes take the rockets bare, three to a cassette. The cassette sits in an open bay between the
  receiver and a breech face plate, on a chain conveyor; two latches on pins hold it against the breech face. A feed
  hood on the mount's outboard flank, a closed box, holds the next filled cassette above its slot (brought up from the
  mount's base through the trunnion) and lowers it to the slot; the conveyor carries it into the bay and the spent one
  out through the slot of the spent bin, the hood's twin on the inboard flank, whose lift stows it above the slot. Every
  cassette appears and disappears inside a box. The tubes' hidden rear ends move up behind a guide yoke, and their
  sleeves give way to the bay.
- The ports into the gun house and the receiver are short, with black back walls: a round passing into them passes
  into darkness, and the space behind them stays free for what turns inside.
"""
import json
import math
import time
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
TABLE = REPO / "game" / "data" / "ground" / "batteries.json"

G2N = Matrix(((0.0, 0.0, 1.0), (1.0, 0.0, 0.0), (0.0, 1.0, 0.0)))    # game axes -> native axes
N2G = G2N.transposed()
NODE_ROT = Matrix.Rotation(math.radians(90.0), 4, "Z")    # the import's TURN_WORLD undoes it: exported nodes are unrotated
SHARP = math.radians(32.0)
LEVELS = 3        # LOD0-2 made here; the import decimates a static addition's LOD3, and a moving piece has none

# ------------------------------------------------------------------------------------------------ numbers (pitch frame)

# The siege battery, per barrel (x the barrel's axis, s its outboard sign).
SIEGE = {
    "L": 0.78, "R": 0.241,          # a round: the sabot carrier with its rocket, base at its z, nose forward
    "rack_front": -1.27,            # the rack's rounds' front faces; the gun house's rear face is at -1.223
    "rack_y0": -0.04, "pitch": 0.50,  # the delivery slot axis height (above the grips, seen from the seat), the spacing
    "feed": 1.22,                   # the feed rammer's stroke: the round ends inside the gun house
    "port_r": 0.27,                 # the feed and ram ports' radius
    "feed_port": (-1.40, -1.00),    # the feed port in the gun house: (outside, back wall)
    "ram_port": (1.32, 1.05),       # the ram port in the receiver: (outside, back wall)
    "ram_rest": 0.0,                # the ram carrier's base at rest, inside the receiver, at the transfer cradle
    "mouth": 2.05,                  # the chamber mouth, where the barrel's rear end is moved
    "ring": (1.78, 2.35),           # the breech ring
    "slot": (1.875, 2.045),         # the wedge block, closed
    "block": 0.58,                  # its stroke outboard
    "sleeve_cut": 2.37,             # the barrel sleeves keep their front from here
    "chain": 0.81,                  # the rammer's chain seen behind its head
}
SIEGE["ram"] = SIEGE["mouth"] - SIEGE["ram_rest"]          # the chain rammer's stroke (2.05)
SOCKET_DZ, SOCKET_DY = 0.38, -0.0505  # a rocket model's origin: 0.38 ahead of a carrier's base, under its axis by the
                                      # height the rocket and the incendiary one lie at (raid.py: on warhead and fins)

# The defense mount: three tubes at x -0.2752, 0, 0.2752 on y 0; o is the side of the feed hood (the station's side).
DEFENSE = {
    "L": 0.73, "W": 0.40, "H": 0.085,   # a cassette: length, half width, half height
    "z0": 0.512,                        # its rear face in the bay before it seats (the receiver's face is at 0.507)
    "seat": 0.015,                      # it closes up against the breech face by this much
    "socket": 0.368,                    # the rockets' model origin from the cassette's rear face
    "lugs": (0.082, 0.096),             # the latches' lugs from the cassette's rear and front faces
    "latch": (0.535, 1.235),            # the latches' pins
    "plate": (1.26, 1.29),              # the breech face plate
    "face": 0.507,                      # the receiver's front face
    "receiver": (0.79, 0.305),          # it covers the boxes' rear inboard of this x and below this y
    "tube_rear": 1.50,                  # where the tubes' hidden rear ends are moved
    "yoke": (1.58, 1.68),               # the tubes' guide yoke
    "shuttle": 0.885,                   # the conveyor's stroke: hood to bay, bay to bin
    "lift": 0.195,                      # the filled cassette waits this far above the slot in the hood, and is lowered
    "stow": 0.195,                      # the bin's lift stows the spent one as far above the slot
    "window": (-0.095, 0.095),          # the hood's and the bin's slots at the bay's level
    "box_x": (0.425, 1.40), "box_y": (-0.125, 0.33), "box_z": (0.488, 1.27),   # the hood and the bin, closed boxes
    "chamfer": 0.10,                    # their outboard top edges, out of the gunner's view at full depression
}
TUBES = (-0.2752, 0.0, 0.2752)


def load_table():
    return json.loads(TABLE.read_text(encoding="utf-8"))


# ------------------------------------------------------------------------------------------------ the mesh kit


class Kit:
    """Geometry being built in a local frame (game axes, metres); faces are tagged with the asset's finishes. emit()
    writes one mesh object in the native frame: UV0_Material in metres (the asset's metric UVs, one unit a metre),
    smooth shading with sharp edges by angle."""

    def __init__(self, ctx, d=0):
        self.ctx = ctx
        self.d = d
        self.bm = bmesh.new()
        self.mats = []

    def _m(self, finish):
        if finish not in self.mats:
            self.mats.append(finish)
        return self.mats.index(finish)

    def poly(self, coords, faces, finish):
        vs = [self.bm.verts.new(Vector(c)) for c in coords]
        idx = self._m(finish)
        made = []
        for f in faces:
            try:
                face = self.bm.faces.new([vs[i] for i in f])
            except ValueError:
                continue
            face.material_index = idx
            made.append(face)
        return vs, made

    def n(self, n=None):
        return n if n is not None else (24, 12, 8)[self.d]

    def _bevel(self, faces, bevel):
        if bevel > 0 and self.d < 2 and faces:
            edges = list({e for face in faces for e in face.edges})
            bmesh.ops.bevel(self.bm, geom=edges, offset=bevel, segments=2 if self.d == 0 and bevel >= 0.012 else 1,
                            profile=0.5, affect="EDGES", clamp_overlap=True)

    def box(self, lo, hi, finish, bevel=0.0):
        lo, hi = Vector(lo), Vector(hi)
        lo, hi = Vector([min(lo[i], hi[i]) for i in range(3)]), Vector([max(lo[i], hi[i]) for i in range(3)])
        c = [(lo.x, lo.y, lo.z), (hi.x, lo.y, lo.z), (hi.x, hi.y, lo.z), (lo.x, hi.y, lo.z),
             (lo.x, lo.y, hi.z), (hi.x, lo.y, hi.z), (hi.x, hi.y, hi.z), (lo.x, hi.y, hi.z)]
        f = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
        vs, faces = self.poly(c, f, finish)
        self._bevel(faces, min(bevel, min(hi - lo) * 0.45))
        return vs

    def cbox(self, centre, size, finish, bevel=0.0):
        c, s = Vector(centre), Vector(size) * 0.5
        return self.box(c - s, c + s, finish, bevel)

    @staticmethod
    def basis(axis):
        axis = Vector(axis).normalized()
        ref = Vector((0, 1, 0)) if abs(axis.y) < 0.9 else Vector((1, 0, 0))
        u = (ref - axis * ref.dot(axis)).normalized()
        return axis, u, axis.cross(u)

    def lathe(self, a, axis, prof, finish, n=None, closed=False, phase=0.0, arc=None):
        """Revolves (t, r) points about the axis through a; r 0 is a pole. closed: the last ring joins the first (a
        section with a wall). arc: (start, end) radians for an open sector, angles from u (see basis) toward w."""
        n = self.n(n)
        axis, u, w = self.basis(axis)
        a = Vector(a)
        coords, rings = [], []
        for t, r in prof:
            c = a + axis * t
            if r <= 1e-7:
                rings.append([len(coords)])
                coords.append(c)
                continue
            if arc is None:
                angles = [phase + 2 * math.pi * i / n for i in range(n)]
            else:
                angles = [arc[0] + (arc[1] - arc[0]) * i / n for i in range(n + 1)]
            rings.append(list(range(len(coords), len(coords) + len(angles))))
            coords += [c + r * (math.cos(g) * u + math.sin(g) * w) for g in angles]
        faces = []
        open_ = arc is not None
        pairs = list(zip(rings, rings[1:])) + ([(rings[-1], rings[0])] if closed else [])
        for r0, r1 in pairs:
            if len(r0) == 1 and len(r1) == 1:
                continue
            if len(r0) == 1:
                m = len(r1)
                faces += [(r0[0], r1[(i + 1) % m], r1[i]) for i in range(m - open_)]
            elif len(r1) == 1:
                m = len(r0)
                faces += [(r0[i], r0[(i + 1) % m], r1[0]) for i in range(m - open_)]
            else:
                m = len(r0)
                faces += [(r0[i], r0[(i + 1) % m], r1[(i + 1) % m], r1[i]) for i in range(m - open_)]
        if open_ and closed and all(len(r) > 1 for r in rings):
            # The sector's two end faces.
            faces.append(tuple(r[0] for r in reversed(rings)))
            faces.append(tuple(r[-1] for r in rings))
        return self.poly(coords, faces, finish)

    def cyl(self, a, b, r, finish, n=None, caps=True, r2=None, phase=0.0):
        a, b = Vector(a), Vector(b)
        L = (b - a).length
        r2 = r if r2 is None else r2
        prof = ([(0, 0)] if caps else []) + [(0, r), (L, r2)] + ([(L, 0)] if caps else [])
        return self.lathe(a, b - a, prof, finish, n, phase=phase)

    def pipe(self, a, b, r_in, r_out, finish, n=None):
        """A tube with a wall: both ends show it."""
        a, b = Vector(a), Vector(b)
        L = (b - a).length
        return self.lathe(a, b - a, [(0, r_out), (L, r_out), (L, r_in), (0, r_in)], finish, n, closed=True)

    def tube(self, pts, r, finish, n=None, caps=True):
        """A round section swept along a polyline (hoses, looms)."""
        n = self.n(n)
        pts = [Vector(p) for p in pts]
        coords, rings = [], []
        prev = None
        for i, p in enumerate(pts):
            t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
            if prev is None:
                _, u, _ = self.basis(t)
            else:
                u = (prev - t * prev.dot(t)).normalized()
            prev = u
            w = t.cross(u)
            rings.append(list(range(len(coords), len(coords) + n)))
            coords += [p + r * (math.cos(2 * math.pi * j / n) * u + math.sin(2 * math.pi * j / n) * w) for j in range(n)]
        faces = []
        for r0, r1 in zip(rings, rings[1:]):
            faces += [(r0[j], r0[(j + 1) % n], r1[(j + 1) % n], r1[j]) for j in range(n)]
        if caps:
            faces += [tuple(reversed(rings[0])), tuple(rings[-1])]
        return self.poly(coords, faces, finish)

    def prism(self, outline, origin, u, v, depth, finish, bevel=0.0):
        """A plate: an outline [(a, b)] in the plane origin + a*u + b*v, extruded `depth` along u x v."""
        o, u, v = Vector(origin), Vector(u).normalized(), Vector(v).normalized()
        nrm = u.cross(v).normalized()
        m = len(outline)
        coords = [o + a * u + b * v for a, b in outline] + [o + a * u + b * v + nrm * depth for a, b in outline]
        faces = [tuple(range(m - 1, -1, -1)), tuple(range(m, 2 * m))]
        faces += [(i, (i + 1) % m, m + (i + 1) % m, m + i) for i in range(m)]
        vs, made = self.poly(coords, faces, finish)
        self._bevel(made, bevel)
        return vs

    def bolt(self, c, nrm, r=0.018, h=0.012, finish="BrushedSteel"):
        """A hex head standing on a surface: c on the surface, nrm out of it (LOD0 only)."""
        if self.d > 0:
            return
        self.lathe(Vector(c), Vector(nrm), [(0, 0), (0, r), (h * 0.7, r), (h, r * 0.82), (h, 0)], finish, 6,
                   phase=math.pi / 6)

    def text(self, body, origin, u, v, size, depth, finish):
        """Stencilled lettering (LOD0 only): Blender's built-in font as geometry, `depth` proud of the plane."""
        if self.d > 0:
            return
        cu = bpy.data.curves.new("stencil", "FONT")
        cu.body = body
        cu.size = size
        cu.extrude = depth * 0.5
        cu.align_x = "CENTER"
        cu.align_y = "CENTER"
        ob = bpy.data.objects.new("stencil", cu)
        bpy.context.scene.collection.objects.link(ob)
        dg = bpy.context.evaluated_depsgraph_get()
        me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
        o, u, v = Vector(origin), Vector(u).normalized(), Vector(v).normalized()
        nrm = u.cross(v)
        coords = [o + p.co.x * u + p.co.y * v + (p.co.z + depth * 0.5) * nrm for p in me.vertices]
        self.poly(coords, [tuple(p.vertices) for p in me.polygons], finish)
        bpy.data.objects.remove(ob)
        bpy.data.curves.remove(cu)
        bpy.data.meshes.remove(me)

    def triangles(self):
        return sum(len(f.verts) - 2 for f in self.bm.faces)

    def emit(self, name, to_native, collection, parent=None, weld=False):
        bm = self.bm
        if weld:
            bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
        bm.normal_update()
        uv = bm.loops.layers.uv.new("UV0_Material")
        for f in bm.faces:
            nr = f.normal
            a = max(range(3), key=lambda i: abs(nr[i]))
            iu, iv = ((2, 1), (0, 2), (0, 1))[a]
            for lp in f.loops:
                lp[uv].uv = (lp.vert.co[iu], lp.vert.co[iv])
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        self.bm = None
        for f in self.mats:
            me.materials.append(self.ctx.materials[f])
        me.transform(to_native)
        for p in me.polygons:
            p.use_smooth = True
        me.set_sharp_from_angle(angle=SHARP)
        me.validate(verbose=False)
        o = bpy.data.objects.new(name, me)
        collection.objects.link(o)
        if parent is not None:
            o.parent = parent
            o.matrix_parent_inverse = parent.matrix_world.inverted()
        return o


# ------------------------------------------------------------------------------------------------ the asset's meshes


def scratch():
    col = bpy.data.collections.get("LOADERS_SCRATCH")
    if col is None:
        col = bpy.data.collections.new("LOADERS_SCRATCH")
        bpy.context.scene.collection.children.link(col)
    return col


def select_only(objects, active=None):
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in objects:
        o.hide_set(False)
        o.select_set(True)
    bpy.context.view_layer.objects.active = active or objects[0]


def components(o):
    """Connected pieces of a mesh object: [(sorted face indices, lo, hi, {finishes})], lo/hi in native coordinates."""
    me = o.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    mw = o.matrix_world
    names = [m.name.replace("M_IronWake_", "") if m else "" for m in me.materials]
    seen = np.zeros(len(bm.faces), bool)
    out = []
    for f in bm.faces:
        if seen[f.index]:
            continue
        stack, comp = [f], []
        seen[f.index] = True
        while stack:
            x = stack.pop()
            comp.append(x.index)
            for e in x.edges:
                for y in e.link_faces:
                    if not seen[y.index]:
                        seen[y.index] = True
                        stack.append(y)
        vs = {v for i in comp for v in bm.faces[i].verts}
        pts = np.array([tuple(mw @ v.co) for v in vs])
        out.append((sorted(comp), Vector(pts.min(0)), Vector(pts.max(0)),
                    {names[bm.faces[i].material_index] for i in comp}))
    bm.free()
    return out


class Station:
    """One gunner station: its pitch frame (game axes from the pitch pivot at rest) and its asset objects."""

    def __init__(self, ctx, sid):
        self.ctx = ctx
        self.id = sid
        st = ctx.stations[sid]
        self.st = st
        self.kind = st["weapon_class"]
        groups = ctx.manifest["groups"]
        self.P = Vector(groups[st["pitch"]]["origin"])
        self.Y = Vector(groups[st["yaw"]]["origin"])
        self.M = Matrix.Translation(self.P) @ G2N.to_4x4()
        self.yaw = ctx.objects[st["yaw"]]
        self.pitch = ctx.objects[st["pitch"]]
        self.recoils = [ctx.objects[r] for r in st["recoil_nodes"]]
        self.barrels = [N2G @ (Vector(groups[r]["origin"]) - self.P) for r in st["recoil_nodes"]]
        # The station's side of the ship (game +X is port): a defense mount's feed hood faces it.
        gx = (N2G @ self.Y).x
        self.side = 1 if gx > 0.05 else -1 if gx < -0.05 else 0

    def loaders(self):
        """The station's loaders: one per barrel on the siege battery, one per mount on the defense."""
        return len(self.barrels) if self.kind == "siege" else 1

    def loader_side(self, k):
        """+1 for a loader on the port side (its barrel's side of the mount, or the defense mount's side), else -1."""
        if self.kind == "siege":
            return 1 if self.barrels[k].x > 0 else -1
        return self.side or 1

    def native(self, p):
        """A pitch-frame point (game axes) in the native frame."""
        return self.M @ Vector(p)

    def local(self, p):
        return N2G @ (Vector(p) - self.P)

    def lods(self, group):
        return [o for o in self.ctx.lods[group] if o is not None]

    def pieces(self, o, test):
        """Components of `o` for which test(lo, hi, finishes) holds, lo/hi their box in the pitch frame."""
        out = []
        for faces, a, b, fin in components(o):
            la, lb = self.local(a), self.local(b)
            lo = Vector([min(la[i], lb[i]) for i in range(3)])
            hi = Vector([max(la[i], lb[i]) for i in range(3)])
            if test(lo, hi, fin):
                out.append((faces, lo, hi, fin))
        return out


def inside(lo, hi, box_lo, box_hi, tol=0.06):
    return all(box_lo[i] - tol <= lo[i] and hi[i] <= box_hi[i] + tol for i in range(3))


def delete_faces(o, faces):
    if not faces:
        return
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.faces[i] for i in faces], context="FACES")
    loose = [v for v in bm.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bm.to_mesh(o.data)
    bm.free()
    o.data.update()


def split_off(o, faces, name):
    """Moves `faces` of `o` into a new object (same transform, a copy of the mesh with the other faces removed)."""
    piece = o.copy()
    piece.data = o.data.copy()
    piece.name = name
    piece.parent = None
    piece.matrix_world = o.matrix_world.copy()
    for m in list(piece.modifiers):
        piece.modifiers.remove(m)
    scratch().objects.link(piece)
    keep = set(faces)
    delete_faces(piece, [i for i in range(len(piece.data.polygons)) if i not in keep])
    delete_faces(o, faces)
    return piece


def join_into(o, piece):
    select_only([o, piece], active=o)
    bpy.ops.object.join()


def boolean(piece, cut):
    """Subtracts the closed mesh `cut` from `piece`; the new faces take the cutter's finishes and UVs."""
    mod = piece.modifiers.new("cut", "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.solver = "EXACT"
    mod.object = cut
    if hasattr(mod, "material_mode"):
        mod.material_mode = "TRANSFER"
    select_only([piece])
    bpy.ops.object.modifier_apply(modifier=mod.name)


def cut(ctx, st, o, test, make, label):
    """Boolean cuts with a cutter made in the pitch frame by make(kit), on the components of `o` passing test."""
    hits = st.pieces(o, test)
    faces = sorted({i for h in hits for i in h[0]})
    if not faces:
        ctx.log(f"{st.id}: {label}: nothing to cut in {o.name}")
        return 0
    kit = Kit(ctx, 0)
    make(kit)
    cutter = kit.emit(f"cutter_{label}", st.M, scratch(), weld=True)
    piece = split_off(o, faces, f"{o.name}.cut")
    boolean(piece, cutter)
    join_into(o, piece)
    bpy.data.objects.remove(cutter)
    return len(hits)


def remove(st, o, test):
    hits = st.pieces(o, test)
    delete_faces(o, sorted({i for h in hits for i in h[0]}))
    return len(hits)


def move_rear(st, o, barrel, upto, mapping):
    """Moves the vertices of `o`'s main tube (the component spanning the barrel's length) that lie within `upto` of
    its rear end (along native +X) to mapping(dx): the hidden rear end moved forward, its rings kept in order."""
    origin = st.native(barrel)
    best = max(components(o), key=lambda c: c[2].x - c[1].x)
    me = o.data
    mw = o.matrix_world
    inv = mw.inverted()
    verts = {vi for p in best[0] for vi in me.polygons[p].vertices}
    moved = 0
    for vi in verts:
        w = mw @ me.vertices[vi].co
        dx = w.x - origin.x
        if dx <= upto + 1e-4:
            w.x = origin.x + mapping(dx)
            me.vertices[vi].co = inv @ w
            moved += 1
    me.update()
    return moved


def port_cutter(kit, x, y, z_out, z_back, r):
    """A port's cutter: a closed cylinder whose back cap is black (the port's back wall reads as darkness)."""
    d = 1.0 if z_back > z_out else -1.0
    a, b = Vector((x, y, z_out)), Vector((x, y, z_back))
    kit.lathe(a, b - a, [(0, 0), (0, r), ((b - a).length, r)], "DarkSteel", 40)
    kit.lathe(b, b - a, [(0, r), (0, 0)], "BlackPaint", 40)
    return d


# ------------------------------------------------------------------------------------------------ nodes


class Builder:
    """Collects the loaders' nodes, their meshes per level, their sockets and what the data file records."""

    def __init__(self, ctx):
        self.ctx = ctx
        self.col = ctx.collection
        self.nodes = {}          # name -> {"empty", "station", "rest", "parent", "pivot", "frame"}
        self.tris = {}           # level -> triangles of everything added
        self.parts = {}          # mesh base name -> LOD0 triangles
        self.sockets = {}
        self.meshes = []         # {"name", "station", "object" (LOD0), "frame", "moves"}

    def frame_of(self, st, parent):
        """'yaw' for what rides on the gun house, 'pitch' for what elevates (the receiver, the barrels, their parts)."""
        if parent is st.yaw:
            return "yaw"
        if parent.name in self.nodes:
            return self.nodes[parent.name]["frame"]
        return "pitch"

    def node(self, st, name, pivot, parent):
        """An empty at a pitch-frame pivot under `parent` (an asset object or another node), axes the game's."""
        e = bpy.data.objects.new(name, None)
        e.empty_display_size = 0.15
        self.col.objects.link(e)
        e.parent = parent
        e.matrix_parent_inverse = parent.matrix_world.inverted()
        e.matrix_basis = Matrix.Translation(st.native(pivot)) @ NODE_ROT
        bpy.context.view_layer.update()
        self.nodes[name] = {"empty": e, "station": st.id, "rest": e.matrix_basis.copy(), "parent": parent.name,
                            "pivot": tuple(round(c, 4) for c in pivot), "frame": self.frame_of(st, parent)}
        return e

    def socket(self, st, name, holder, point):
        """An attach point for a rocket model: its origin; the model's nose (-Z of the model) points down the barrel."""
        e = bpy.data.objects.new("SOCKET_" + name, None)
        e.empty_display_size = 0.05
        self.col.objects.link(e)
        e.parent = holder
        e.matrix_parent_inverse = holder.matrix_world.inverted()
        e.matrix_basis = Matrix.Translation(st.native(point)) @ NODE_ROT @ Matrix.Rotation(math.pi, 4, "Z")
        self.sockets[name] = {"holder": holder.name, "station": st.id, "point": tuple(round(c, 4) for c in point)}
        return e

    def mesh(self, st, name, make, parent, levels=LEVELS):
        """Meshes `<name>_LOD<n>` for n < levels from make(kit) at each detail, parented to `parent`."""
        made = []
        for d in range(levels):
            kit = Kit(self.ctx, d)
            make(kit)
            n = kit.triangles()
            self.tris[d] = self.tris.get(d, 0) + n
            if d == 0:
                self.parts[name] = n
            made.append(kit.emit(f"{name}_LOD{d}", st.M, self.col, parent))
        # A mesh under a node moves (its node, or the barrel its node rides on); one on the gun house or the receiver
        # stands still in its frame.
        moves = parent.name in self.nodes or parent in st.recoils
        self.meshes.append({"name": name, "station": st.id, "object": made[0], "frame": self.frame_of(st, parent),
                            "moves": moves})
        return made


# ------------------------------------------------------------------------------------------------ the sabot carrier


def sabot(kit, base, x, y):
    """A siege round's launch carrier: base at z `base`, axis (x, y), along +Z. Three steel petals on a pusher base with
    a driving band, open windows so the rocket's colour band shows, saddles holding the rocket, scoops at the front."""
    L, R = SIEGE["L"], SIEGE["R"]
    d = kit.d
    a = Vector((x, y, base))
    ax = Vector((0, 0, 1))
    n = (24, 14, 10)[d]
    # The pusher base: a dished cup carrying the driving band.
    kit.lathe(a, ax, [(0.0, 0.0), (0.0, R - 0.01), (0.008, R), (0.013, R + 0.0015), (0.026, R + 0.0015), (0.03, R),
                      (0.040, R - 0.012), (0.040, 0.0)], "Gunmetal", n)
    if d == 2:
        # Far away: a plain sleeve round the rocket, open at the front.
        kit.lathe(a, ax, [(0.04, R - 0.004), (L, R - 0.004), (L, R - 0.016), (0.04, R - 0.016)], "Gunmetal", n,
                  closed=True)
        return
    gap = math.radians(9.0)
    seg = (6, 4)[d]
    _, u0, w0 = Kit.basis(ax)
    for k in range(3):
        c = math.radians(90.0 + 120.0 * k)
        lo, hi = c - math.radians(60.0) + gap / 2, c + math.radians(60.0) - gap / 2
        # Bands: the rear joint, the middle band and the front bourrelet with the scoop turned in at its edge, so air
        # peels the petal off at the muzzle.
        for z0, z1, r0 in ((0.040, 0.085, R - 0.001), (0.37, 0.43, R - 0.003)):
            kit.lathe(a + ax * z0, ax, [(0.0, r0 - 0.014), (0.0, r0), (z1 - z0, r0), (z1 - z0, r0 - 0.014)],
                      "Gunmetal", seg, closed=True, arc=(lo, hi))
        kit.lathe(a + ax * (L - 0.075), ax, [(0.0, R - 0.015), (0.0, R - 0.001), (0.069, R - 0.001), (0.075, R - 0.012),
                                              (0.075, R - 0.03), (0.06, R - 0.015)], "Gunmetal", seg, closed=True,
                  arc=(lo, hi))
        # The petal's edge rails from base to bourrelet.
        for e in (lo, hi - math.radians(9.0)):
            kit.lathe(a + ax * 0.04, ax, [(0.0, R - 0.016), (0.0, R - 0.002), (L - 0.046, R - 0.002),
                                          (L - 0.046, R - 0.016)], "Gunmetal", 1, closed=True,
                      arc=(e, e + math.radians(9.0)))
        # Saddles: ribs from the petal's middle down to the rocket's motor and warhead.
        u = math.cos(c) * u0 + math.sin(c) * w0
        for zc, r_in in ((0.20, 0.035), (0.53, 0.049)):
            p0 = a + ax * zc
            kit.prism([(r_in, -0.022), (R - 0.01, -0.03), (R - 0.01, 0.03), (r_in, 0.022)], p0 - u.cross(ax) * 0.005, u,
                      ax, 0.010, "Gunmetal")


# ------------------------------------------------------------------------------------------------ siege battery


def siege(ctx, B, st):
    S = SIEGE
    log = ctx.log
    yaw_g, pitch_g = st.st["yaw"], st.st["pitch"]
    for k, bx in enumerate(st.barrels):
        x = bx.x
        # The gun house (the yaw's cast receiver): a feed port behind the bore line, from its rear face in.
        for o in st.lods(yaw_g):
            n = cut(ctx, st, o, lambda lo, hi, f: "DarkSteel" in f and inside(lo, hi, (-2.2, -0.95, -1.30), (2.2, 0.55, 0.75))
                    and hi.z - lo.z > 1.5,
                    lambda kit, x=x: port_cutter(kit, x, S["rack_y0"], *S["feed_port"], S["port_r"]), f"feed{k}")
            log(f"{st.id}: feed port {k} in {o.name}: {n}")
        # The receiver: the ram port on the bore line.
        for o in st.lods(pitch_g):
            n = cut(ctx, st, o, lambda lo, hi, f: "Gunmetal" in f and inside(lo, hi, (-1.95, -0.62, -0.35), (1.95, 0.78, 1.26)),
                    lambda kit, x=x: port_cutter(kit, x, 0.0, *S["ram_port"], S["port_r"]), f"ram{k}")
            log(f"{st.id}: ram port {k} in {o.name}: {n}")
            # The barrel's sleeve keeps only its front, in front of the breech.
            n = cut(ctx, st, o, lambda lo, hi, f, x=x: "DarkSteel" in f and hi.z - lo.z > 2.5 and abs((lo.x + hi.x) / 2 - x) < 0.06,
                    lambda kit, x=x: kit.box((x - 0.95, -0.95, 0.4), (x + 0.95, 0.95, S["sleeve_cut"]), "DarkSteel"),
                    f"sleeve{k}")
            log(f"{st.id}: sleeve {k} in {o.name}: {n}")
    # The barrels' hidden rear ends move up to the chamber mouth.
    for k, (bx, rec) in enumerate(zip(st.barrels, st.st["recoil_nodes"])):
        for o in st.lods(rec):
            n = move_rear(st, o, bx, 0.01, lambda dx, bx=bx: S["mouth"] - bx.z)
            log(f"{st.id}: barrel {k} rear end in {o.name}: {n} vertices")
    # The cockpit's two side magazines make room for the ready racks.
    for o in st.lods(yaw_g):
        n = remove(st, o, lambda lo, hi, f: f == {"OxidePaint"} and 1.1 < abs((lo.x + hi.x) / 2) < 1.8
                   and -2.15 < lo.z and hi.z < -1.2)
        log(f"{st.id}: side magazines in {o.name}: {n} removed")
    nodes = {}
    for k, (bx, rec) in enumerate(zip(st.barrels, st.st["recoil_nodes"])):
        nodes.update(siege_barrel(ctx, B, st, k, bx, ctx.objects[rec]))
    return nodes


def siege_barrel(ctx, B, st, k, bx, recoil):
    S = SIEGE
    x = bx.x
    s = 1 if x > 0 else -1
    sid, kk = st.id, f"{k:02d}"
    L, R = S["L"], S["R"]
    zf = S["rack_front"]
    zb = zf - L                                   # the rack's rounds' bases
    y0, pitch = S["rack_y0"], S["pitch"]
    xi, xo = x - s * 0.268, x + s * 0.268         # the rack's inboard and outboard side plates' inner faces
    ztop = y0 + 2 * pitch + R                     # the hood's lip
    zhood = y0 + 3 * pitch + R + 0.05             # the hood's roof
    z0, z1 = zb - 0.05, zf + 0.01
    FL = -0.397                                   # the cockpit's floor is at -0.399
    names = {}

    # ---- the ready rack (static, on the yaw)
    def rack(kit):
        d = kit.d
        t = 0.018
        # The outboard side is a plate against the armour cheek; the inboard side, facing the gunner, an open frame
        # of posts and rails so the rounds and their rockets' bands show.
        kit.box((xo, FL, z0), (xo + s * t, ztop, z1), "OxidePaint", 0.005)
        for yc in (0.15, 0.65):
            kit.box((xo + s * t, yc - 0.015, z0), (xo + s * 0.05, yc + 0.015, z1), "OxidePaint", 0.004)
        for zc in (z0, z1):
            kit.box((xo + s * t, FL, zc - 0.012), (xo + s * 0.06, ztop, zc + 0.012), "DarkSteel", 0.004)
            kit.box((xi - s * 0.05, FL, zc - 0.03 if zc == z0 else zc - 0.02),
                    (xi, ztop, zc + 0.02 if zc == z0 else zc + 0.03), "OxidePaint", 0.006)
        for j in range(3):
            yc = y0 + (j + 0.5) * pitch
            kit.box((xi - s * 0.04, yc - 0.02, z0), (xi - s * 0.005, yc + 0.02, z1), "OxidePaint", 0.005)
        kit.box((xi - s * 0.04, FL, z0), (xi - s * 0.005, -0.36, z1), "OxidePaint", 0.005)
        # The guide strips the carriers' bands run on, both sides.
        for xp, out in ((xi, -s), (xo, s)):
            for zc in (zb + 0.06, zf - 0.04):
                kit.box((xp - out * 0.013, -0.30, zc - 0.022), (xp, ztop - 0.01, zc + 0.022), "BrushedSteel", 0.003)
        # The delivery cradle under the lowest round, the base plate on the deck.
        for zc in (zb + 0.14, zf - 0.14):
            # A saddle 10 mm under the round's line, so the driving band clears it.
            saddle = [(a, y0 - math.sqrt(R * R - a * a) - (FL + 0.005) - 0.010)
                      for a in (0.20, 0.15, 0.10, 0.05, 0.0, -0.05, -0.10, -0.15, -0.20)]
            kit.prism(([(-0.20, 0.0), (0.20, 0.0)] + saddle)[::-1], (x, FL + 0.005, zc - 0.03), Vector((1, 0, 0)),
                      Vector((0, 1, 0)), 0.06, "DarkSteel", 0.004)
        kit.box((x - 0.30, FL, z0 - 0.03), (x + 0.30, -0.378, z1 + 0.01), "DarkSteel", 0.005)
        for dx in (-0.26, 0.26):
            for zc in (z0 + 0.03, z1 - 0.03):
                kit.bolt((x + dx, -0.378, zc), (0, 1, 0))
        # Escapement pawl housings at each slot, outside the outboard plate, and the index cylinder.
        for j in range(3):
            yc = y0 + j * pitch - R + 0.03
            for zc in (zb + 0.30, zf - 0.30):
                kit.cbox((xo + s * 0.045, yc, zc), (0.05, 0.07, 0.08), "Gunmetal", 0.006)
                kit.cyl((xo + s * 0.02, yc, zc), (xo + s * 0.075, yc, zc), 0.014, "BrushedSteel", 10)
        cx = xo + s * 0.07
        kit.cyl((cx, -0.30, zb + 0.43), (cx, ztop - 0.30, zb + 0.43), 0.035, "DarkSteel")
        kit.cyl((cx, ztop - 0.30, zb + 0.43), (cx, ztop - 0.06, zb + 0.43), 0.017, "BrushedSteel")
        kit.cbox((cx - s * 0.02, ztop - 0.06, zb + 0.43), (0.07, 0.04, 0.08), "DarkSteel", 0.004)
        kit.cbox((cx - s * 0.02, -0.33, zb + 0.43), (0.07, 0.06, 0.09), "DarkSteel", 0.004)
        if d < 2:
            kit.tube([(cx + s * 0.03, -0.27, zb + 0.45), (cx + s * 0.07, -0.25, zb + 0.56), (cx + s * 0.07, -0.385, zb + 0.70)],
                     0.012, "Rubber", 8)
        # The hood over the fourth round, the supply's mouth: an armoured box open below, a hazard band at its lip.
        tt = 0.022
        for xp, out in ((x - s * 0.29, -s), (x + s * 0.29, s)):
            kit.box((xp, ztop, z0 - 0.01), (xp + out * tt, zhood, z1 + 0.01), "OxidePaint", 0.006)
        for zc, sgn in ((z0 - 0.01, -1), (z1 + 0.01, 1)):
            kit.box((x - 0.29 - tt, ztop, zc), (x + 0.29 + tt, zhood, zc + sgn * tt), "OxidePaint", 0.006)
        kit.box((x - 0.29 - tt, zhood, z0 - 0.01 - tt), (x + 0.29 + tt, zhood + tt, z1 + 0.01 + tt), "OxidePaint", 0.008)
        yb0, yb1 = ztop - 0.003, ztop + 0.04
        zr_, zf_ = z0 - 0.01 - tt - 0.003, z1 + 0.01 + tt + 0.003
        for side in (-1, 1):
            kit.box((x + side * 0.29, yb0, zr_), (x + side * (0.29 + tt + 0.003), yb1, zf_), "WarningYellow", 0.003)
        kit.box((x - 0.29, yb0, zr_), (x + 0.29, yb1, z0 - 0.01), "WarningYellow", 0.003)
        kit.box((x - 0.29, yb0, z1 + 0.01), (x + 0.29, yb1, zf_), "WarningYellow", 0.003)
        kit.box((x - 0.20, zhood + tt, zb + 0.10), (x + 0.20, zhood + tt + 0.022, zf - 0.10), "DarkSteel", 0.006)
        for dx in (-0.16, 0.16):
            for zc in (zb + 0.14, zf - 0.14):
                kit.bolt((x + dx, zhood + tt + 0.022, zc), (0, 1, 0))
        face_z = z0 - 0.01 - tt          # the hood's rear face, towards the gunner
        kit.text("READY", (x, (ztop + zhood) / 2 + 0.06, face_z), Vector((-1, 0, 0)), Vector((0, 1, 0)), 0.075, 0.002,
                 "IvoryPaint")
        kit.text("PORT" if s > 0 else "STBD", (x, (ztop + zhood) / 2 - 0.06, face_z), Vector((-1, 0, 0)),
                 Vector((0, 1, 0)), 0.06, 0.002, "IvoryPaint")
        # The feed rammer's cylinder behind the lowest round, on its brackets: a barrel capped behind, a gland in
        # front that its two stages slide out through.
        fb0, fb1 = zb - 0.75, zb - 0.06
        kit.pipe((x, y0, fb0), (x, y0, fb1), 0.062, 0.075, "DarkSteel")
        kit.cyl((x, y0, fb0 - 0.03), (x, y0, fb0 + 0.002), 0.082, "BrushedSteel")
        kit.pipe((x, y0, fb1), (x, y0, fb1 + 0.025), 0.053, 0.082, "BrushedSteel")
        for za in (fb0 + 0.05, fb1 - 0.15):
            kit.box((x - 0.06, FL, za), (x + 0.06, y0 - 0.065, za + 0.10), "DarkSteel", 0.006)
        if d < 2:
            kit.tube([(x + s * 0.06, y0 + 0.03, fb0 + 0.02), (x + s * 0.12, y0 - 0.05, fb0 - 0.05),
                      (x + s * 0.14, -0.385, fb0 + 0.10)], 0.013, "Rubber", 8)
            kit.tube([(x + s * 0.06, y0 + 0.03, fb1 - 0.02), (x + s * 0.13, y0 - 0.05, fb1 - 0.10),
                      (x + s * 0.15, -0.385, fb1 - 0.20)], 0.013, "Rubber", 8)
        # The feed port's collar on the gun house's rear face.
        kit.pipe((x, y0, -1.2495), (x, y0, -1.2245), S["port_r"] - 0.004, S["port_r"] + 0.055, "BrushedSteel", 40)
        for j in range(8):
            a = 2 * math.pi * (j + 0.5) / 8
            kit.bolt((x + math.cos(a) * (S["port_r"] + 0.03), y0 + math.sin(a) * (S["port_r"] + 0.03), -1.2495),
                     (0, 0, -1), r=0.012, h=0.006)
    B.mesh(st, f"{sid}_ReadyRack_{kk}", rack, st.yaw)

    # ---- the rack's carriers (moving): slot j's round on y0 + j * pitch; slot 3 waits in the hood
    for j in range(4):
        name = f"{sid}_Carrier_{kk}_{j:02d}"
        yc = y0 + j * pitch
        e = B.node(st, name, (x, yc, zb), st.yaw)
        B.mesh(st, name, lambda kit, yc=yc: sabot(kit, zb, x, yc), e)
        B.socket(st, f"{sid}_Rack_{kk}_Slot_{j:02d}", e, (x, yc + SOCKET_DY, zb + SOCKET_DZ))
        names[name] = e

    # ---- the feed rammer's two telescopic stages (moving)
    # Stage A: a chromed tube with its piston at its rear end, inside the cylinder's barrel.
    fa = B.node(st, f"{sid}_Feed_{kk}_A", (x, y0, zb - 0.06), st.yaw)
    B.mesh(st, f"{sid}_Feed_{kk}_A", lambda kit: (kit.pipe((x, y0, zb - 0.70), (x, y0, zb - 0.04), 0.038, 0.05, "BrushedSteel"),
                                                  kit.pipe((x, y0, zb - 0.70), (x, y0, zb - 0.675), 0.038, 0.058, "BrushedSteel")),
           fa)
    fb = B.node(st, f"{sid}_Feed_{kk}_B", (x, y0, zb), st.yaw)

    def head(kit):
        # Stage B: a rod with its piston in stage A's bore, and the pusher head with its rubber face.
        kit.cyl((x, y0, zb - 0.68), (x, y0, zb - 0.025), 0.034, "BrushedSteel")
        kit.cyl((x, y0, zb - 0.68), (x, y0, zb - 0.665), 0.0365, "Gunmetal")
        kit.cyl((x, y0, zb - 0.030), (x, y0, zb - 0.003), 0.15, "Gunmetal", r2=0.13)
        kit.cyl((x, y0, zb - 0.003), (x, y0, zb - 0.0005), 0.10, "Rubber")
    B.mesh(st, f"{sid}_Feed_{kk}_B", head, fb)
    names[fa.name] = fa
    names[fb.name] = fb

    # ---- the chain rammer and the round it rams (moving, on the receiver)
    zr = S["ram_rest"]
    rm = B.node(st, f"{sid}_Rammer_{kk}", (x, 0.0, zr), st.pitch)

    def rammer(kit):
        d = kit.d
        kit.cyl((x, 0.0, zr - 0.055), (x, 0.0, zr - 0.004), 0.20, "Gunmetal", r2=0.17)
        kit.cyl((x, 0.0, zr - 0.004), (x, 0.0, zr - 0.0005), 0.13, "DarkSteel")
        kit.cyl((x, 0.0, zr - 0.11), (x, 0.0, zr - 0.05), 0.075, "Gunmetal")
        # The rigid chain: links that lock straight when pushed, pins across.
        count = (9, 9, 3)[d]
        step = (S["chain"] - 0.10) / count
        for i in range(count):
            za1 = zr - 0.10 - i * step
            za0 = za1 - step + 0.006
            if d < 2:
                for side in (-1, 1):
                    kit.box((x + side * 0.058 - 0.008, -0.055, za0), (x + side * 0.058 + 0.008, 0.055, za1),
                            "DarkSteel", 0.003)
                kit.box((x - 0.05, -0.04, za0 + 0.01), (x + 0.05, 0.04, za1 - 0.01), "DarkSteel", 0.004)
                kit.cyl((x - 0.072, 0.0, za1 - 0.012), (x + 0.072, 0.0, za1 - 0.012), 0.014, "BrushedSteel", 10)
            else:
                kit.box((x - 0.066, -0.055, za0), (x + 0.066, 0.055, za1), "DarkSteel")
    B.mesh(st, f"{sid}_Rammer_{kk}", rammer, rm)
    rc = B.node(st, f"{sid}_RamCarrier_{kk}", (x, 0.0, zr), st.pitch)
    B.mesh(st, f"{sid}_RamCarrier_{kk}", lambda kit: sabot(kit, zr, x, 0.0), rc)
    B.socket(st, f"{sid}_Ram_{kk}_Slot_00", rc, (x, SOCKET_DY, zr + SOCKET_DZ))
    names[rm.name] = rm
    names[rc.name] = rc

    # ---- the ram port's collar on the receiver (static, pitch)
    def collar(kit):
        kit.pipe((x, 0.0, 1.225), (x, 0.0, 1.268), S["port_r"] - 0.004, S["port_r"] + 0.06, "BrushedSteel", 40)
        for j in range(8):
            a = 2 * math.pi * (j + 0.5) / 8
            kit.bolt((x + math.cos(a) * (S["port_r"] + 0.035), math.sin(a) * (S["port_r"] + 0.035), 1.268), (0, 0, 1),
                     r=0.012, h=0.008)
    B.mesh(st, f"{sid}_RamPortCollar_{kk}", collar, st.pitch)

    # ---- the breech ring (a node of its own on the barrel, never moving) and its sliding wedge block
    r0, r1 = S["ring"]
    sz0, sz1 = S["slot"]
    br = B.node(st, f"{sid}_BreechRing_{kk}", (x, 0.0, S["mouth"]), recoil)

    def ring(kit):
        d = kit.d
        hw, hh = 0.53, 0.53
        # The rear bar (the round passes its bore when the block is open) and the front block round the chamber mouth.
        for za, zz in ((r0, sz0), (sz1, r1)):
            kit.lathe((x, 0.0, za), (0, 0, 1), [(0, 0.25), (0, 0.47), (zz - za, 0.47), (zz - za, 0.25)], "Gunmetal",
                      40, closed=True)
            kit.box((x - hw, -hh, za), (x + hw, -0.36, zz), "Gunmetal", 0.02)
            kit.box((x - hw, 0.36, za), (x + hw, hh, zz), "Gunmetal", 0.02)
            kit.box((x - hw, -0.36, za), (x - 0.40, 0.36, zz), "Gunmetal", 0.01)
            kit.box((x + 0.40, -0.36, za), (x + hw, 0.36, zz), "Gunmetal", 0.01)
        # The slot is closed inboard; outboard it opens into the guide the block runs out on.
        kit.box((x - s * hw, -hh, sz0), (x - s * 0.44, hh, sz1), "Gunmetal", 0.01)
        ge = x + s * (hw + 0.42)
        for ya, yb in ((-0.36, -0.32), (0.32, 0.36)):
            kit.box((x + s * hw, ya, r0 + 0.02), (ge, yb, sz1 + 0.06), "DarkSteel", 0.008)
        # The guide's end plate, with a hole for the block's piston rod, and the block's hydraulic cylinder beyond it.
        zm = (sz0 + sz1) / 2
        hole = 0.04
        e0, e1 = ge, ge + s * 0.04
        kit.box((e0, -0.36, r0 + 0.02), (e1, -hole, sz1 + 0.06), "DarkSteel", 0.006)
        kit.box((e0, hole, r0 + 0.02), (e1, 0.36, sz1 + 0.06), "DarkSteel", 0.006)
        kit.box((e0, -hole, r0 + 0.02), (e1, hole, zm - hole), "DarkSteel", 0.004)
        kit.box((e0, -hole, zm + hole), (e1, hole, sz1 + 0.06), "DarkSteel", 0.004)
        c0, c1 = x + s * (hw + 0.47), x + s * (hw + 1.12)
        f1 = e1 + s * 0.025
        kit.box((e1, -0.09, zm - 0.09), (f1, -hole, zm + 0.09), "DarkSteel", 0.004)
        kit.box((e1, hole, zm - 0.09), (f1, 0.09, zm + 0.09), "DarkSteel", 0.004)
        kit.box((e1, -hole, zm - 0.09), (f1, hole, zm - hole), "DarkSteel", 0.003)
        kit.box((e1, -hole, zm + hole), (f1, hole, zm + 0.09), "DarkSteel", 0.003)
        kit.pipe((c0, 0.0, zm), (c0 + s * 0.04, 0.0, zm), 0.034, 0.068, "BrushedSteel")
        kit.pipe((c0, 0.0, zm), (c1, 0.0, zm), 0.034, 0.06, "DarkSteel")
        kit.cyl((c1, 0.0, zm), (c1 + s * 0.03, 0.0, zm), 0.066, "BrushedSteel")
        for dy, dz in ((-0.065, -0.065), (-0.065, 0.065), (0.065, -0.065), (0.065, 0.065)):
            kit.bolt((e1 + s * 0.025, dy, zm + dz), (s, 0, 0), r=0.012, h=0.008)
        # The machined chamber mouth and the forcing cone, seen when the block is open.
        kit.lathe((x, 0.0, S["mouth"] - 0.004), (0, 0, 1), [(0, 0.46), (0.0, 0.244), (0.06, 0.243)], "BrushedSteel", 40)
        if d < 2:
            # Its two hoses over the guide to the ring.
            for dz, xc in ((-0.035, c0 + s * 0.06), (0.035, c1 - s * 0.05)):
                kit.tube([(xc, 0.055, zm + dz), (xc - s * 0.02, 0.25, zm + dz * 2), (x + s * 0.90, 0.40, zm + dz * 2),
                          (x + s * 0.525, 0.42, zm + dz * 2)], 0.012, "Rubber", 8)
        for zc in (r0 + 0.045, r1 - 0.045):
            for dx in (-0.42, 0.42):
                for yc in (-hh, hh):
                    kit.bolt((x + dx, yc, zc), (0, 1 if yc > 0 else -1, 0), r=0.016)
        kit.text("RAM", (x - s * 0.14, hh + 0.001, (r0 + r1) / 2), Vector((-1, 0, 0)), Vector((0, 0, 1)), 0.075, 0.002,
                 "WarningYellow")
    B.mesh(st, f"{sid}_BreechRing_{kk}", ring, br)
    B.socket(st, f"{sid}_Breech_{kk}", br, (x, -SOCKET_DY, S["mouth"] - 0.02))
    blk = B.node(st, f"{sid}_Breech_{kk}", (x, 0.0, sz0), br)

    def block(kit):
        zm = (sz0 + sz1) / 2
        kit.box((x - 0.31, -0.31, sz0 + 0.003), (x + 0.31, 0.31, sz1 - 0.003), "BrushedSteel", 0.012)
        # The piston rod's eye on the block's outboard face, and the chromed rod into the cylinder.
        kit.box((x + s * 0.31, -0.05, zm - 0.05), (x + s * 0.34, 0.05, zm + 0.05), "BrushedSteel", 0.005)
        kit.cyl((x + s * 0.34, 0.0, zm), (x + s * 1.00, 0.0, zm), 0.03, "BrushedSteel", 16)
        kit.lathe((x, 0.0, sz0 + 0.002), (0, 0, -1), [(0, 0.0), (0, 0.10), (0.004, 0.08), (0.004, 0.0)], "BrushedSteel", 24)
    B.mesh(st, f"{sid}_Breech_{kk}", block, blk)
    names[br.name] = br
    names[blk.name] = blk
    return names


# ------------------------------------------------------------------------------------------------ defense mount


def cassette(kit, x0, y0, z0):
    """A defense cassette: a welded steel frame holding three rockets in open chambers, the latches' lugs on top near
    both ends, lifting lugs on its sides."""
    D = DEFENSE
    L, W, H = D["L"], D["W"], D["H"]
    d = kit.d
    z1 = z0 + L
    for za, zz in ((z0, z0 + 0.02), (z1 - 0.02, z1)):
        kit.box((x0 - W, y0 - H, za), (x0 + W, y0 + H, zz), "DarkSteel", 0.006)
    for sx in (-1, 1):
        xa = x0 + sx * W
        kit.box((xa, y0 - H, z0 + 0.02), (xa - sx * 0.02, y0 - H + 0.03, z1 - 0.02), "DarkSteel", 0.004)
        kit.box((xa, y0 + H - 0.03, z0 + 0.02), (xa - sx * 0.02, y0 + H, z1 - 0.02), "DarkSteel", 0.004)
        kit.box((xa - sx * 0.005, y0 - 0.03, z0 + 0.30), (xa + sx * 0.012, y0 + 0.03, z0 + 0.43), "Gunmetal", 0.004)
    for xm in (-0.1376, 0.1376):
        kit.box((x0 + xm - 0.01, y0 - H, z0 + 0.02), (x0 + xm + 0.01, y0 - H + 0.02, z1 - 0.02), "DarkSteel", 0.003)
        kit.box((x0 + xm - 0.01, y0 + H - 0.02, z0 + 0.02), (x0 + xm + 0.01, y0 + H, z1 - 0.02), "DarkSteel", 0.003)
    nr = (14, 10, 6)[d]
    for tx in TUBES:
        for zc, r in ((z0 + 0.06, 0.074), (z0 + 0.38, 0.06), (z1 - 0.05, 0.056)):
            kit.pipe((x0 + tx, y0, zc - 0.012), (x0 + tx, y0, zc + 0.012), r, r + 0.008, "Gunmetal", nr)
    # The lugs the latches' tips come down on (DEFENSE["lugs"] from each end).
    for zc in (z0 + D["lugs"][0], z1 - D["lugs"][1]):
        kit.box((x0 - 0.05, y0 + H, zc - 0.015), (x0 + 0.05, y0 + H + 0.012, zc + 0.015), "Gunmetal", 0.004)
    # A painted band across the top rails: which way up and which end goes forward.
    kit.box((x0 - W + 0.005, y0 + H, z1 - 0.13), (x0 + W - 0.005, y0 + H + 0.002, z1 - 0.115), "WarningYellow")


def defense(ctx, B, st):
    D = DEFENSE
    log = ctx.log
    o = st.side if st.side != 0 else 1
    pitch_g = st.st["pitch"]
    # The sleeves and their front collars give way to the bay and the yoke.
    for ob in st.lods(pitch_g):
        n = remove(st, ob, lambda lo, hi, f: abs((lo.y + hi.y) / 2) < 0.03 and lo.z > 0.25 and hi.z < 1.50 and (
            ("DarkSteel" in f and hi.z - lo.z > 1.0) or ("BrushedSteel" in f and lo.z > 1.30 and hi.z < 1.40)))
        log(f"{st.id}: sleeves in {ob.name}: {n} removed")
    # The tubes' hidden rear ends move up behind the breech face: their thick rear section kept, shorter.
    for k, (bx, rec) in enumerate(zip(st.barrels, st.st["recoil_nodes"])):
        new = D["tube_rear"] - bx.z
        for ob in st.lods(rec):
            n = move_rear(st, ob, bx, 1.0, lambda dx, new=new: new + dx * 0.057)
            log(f"{st.id}: tube {k} rear in {ob.name}: {n} vertices")
    sid = st.id
    z0 = D["z0"]
    zs = z0 + D["seat"]
    L = D["L"]
    xa, xb = D["box_x"]
    h0, h1 = D["box_y"]
    bz0, bz1 = D["box_z"]
    p0, p1 = D["plate"]
    face = D["face"]
    rx, ry = D["receiver"]

    def frame(kit):
        d = kit.d
        nr = (24, 12, 8)[d]
        t = 0.02
        # The breech face: a plate across the three tubes with their bores.
        kit.box((-0.47, -0.10, p0), (0.47, 0.11, p1), "Gunmetal", 0.01)
        for tx in TUBES:
            kit.pipe((tx, 0.0, p1 - 0.001), (tx, 0.0, p1 + 0.015), 0.055, 0.075, "BrushedSteel", nr)
        # The guide yoke: collars open below for the recoil pistons' lugs, a top bar, side frames to the breech face.
        y0_, y1_ = D["yoke"]
        kit.box((-0.47, 0.105, y0_), (0.47, 0.15, y1_), "Gunmetal", 0.008)
        for tx in TUBES:
            kit.lathe((tx, 0.0, y0_), (0, 0, 1), [(0, 0.105), (0, 0.128), (y1_ - y0_, 0.128), (y1_ - y0_, 0.105)],
                      "Gunmetal", nr, closed=True, arc=(math.radians(215), math.radians(505)))
        for sx in (-1, 1):
            kit.box((sx * 0.47, -0.12, p0), (sx * 0.50, 0.15, y1_), "DarkSteel", 0.008)
        # The conveyor across the bay's floor, from the hood into the bin: two rails the cassettes ride on, and between
        # them the chain's channel with its dogs' chain, its sprockets' housings at both ends inside the boxes.
        xr = xb - t
        for zc in (z0 + 0.012, z0 + L - 0.03):
            kit.box((-xr, -0.105, zc), (xr, -0.088, zc + 0.03), "BrushedSteel", 0.004)
        zc = z0 + L / 2
        kit.box((-xr, -0.105, zc - 0.03), (xr, -0.093, zc + 0.03), "DarkSteel", 0.003)
        if d == 0:
            n = int(2 * xr / 0.05)
            for i in range(n):
                xc = -xr + (i + 0.5) * 2 * xr / n
                kit.box((xc - 0.019, -0.093, zc - 0.012), (xc + 0.019, -0.0895, zc + 0.012), "Gunmetal")
        for side in (-1, 1):
            kit.box((side * (xr - 0.075), -0.105, zc - 0.06), (side * xr, -0.06, zc + 0.06), "Gunmetal", 0.006)
        # The latches' cheeks and pins: on the receiver's face and on the breech face.
        for zp, za, zz in ((D["latch"][0], face + 0.001, D["latch"][0] + 0.027), (D["latch"][1], D["latch"][1] - 0.027, p0)):
            for sx in (-1, 1):
                kit.box((sx * 0.056, 0.095, za), (sx * 0.07, 0.165, zz), "DarkSteel", 0.003)
            kit.cyl((-0.074, 0.135, zp), (0.074, 0.135, zp), 0.010, "BrushedSteel", 12)
        # The feed hood and the spent bin: closed boxes either side of the bay, slotted at the bay's level on their
        # inboard walls. The filled cassette waits in the hood above its slot and is lowered to it; the conveyor carries
        # it through into the bay and the spent one out into the bin, whose lift stows it above the slot. Every
        # hand-over happens inside a box. Beside the receiver their rear walls close them; in front of it, its face does.
        win = D["window"]
        c = D["chamfer"]
        tc = t / math.sqrt(2.0)
        for side, finish in ((o, "OxidePaint"), (-o, "DarkSteel")):
            xi_, xo_, xr_ = side * xa, side * xb, side * rx

            def ccw(points):
                return points if side > 0 else points[::-1]

            def wall(x0_, z_):
                # A wall across the box (front or rear), its outboard top corner chamfered.
                kit.prism(ccw([(x0_, h0), (xo_, h0), (xo_, h1 - c), (xo_ - side * c, h1), (x0_, h1)]),
                          (0.0, 0.0, z_), Vector((1, 0, 0)), Vector((0, 1, 0)), t, finish, 0.004)
            kit.box((xi_, h0, face + 0.001), (xr_, h0 + t, bz1), finish, 0.006)
            kit.box((xr_, h0, bz0), (xo_, h0 + t, bz1), finish, 0.006)
            kit.box((xi_, h1 - t, bz0), (xo_ - side * c, h1, bz1), finish, 0.006)
            kit.prism(ccw([(xo_ - side * (c + tc), h1 - tc), (xo_ - side * tc, h1 - c - tc), (xo_, h1 - c),
                           (xo_ - side * c, h1)][::-1]), (0.0, 0.0, bz0), Vector((1, 0, 0)), Vector((0, 1, 0)),
                      bz1 - bz0, finish, 0.004)
            wall(xi_, bz1 - t)
            wall(xr_, bz0)
            kit.box((xi_, ry, bz0), (xr_, h1, bz0 + t), finish, 0.006)
            kit.box((xo_, h0, bz0), (xo_ - side * t, h1 - c, bz1), finish, 0.006)
            kit.box((xi_, h0, face + 0.001), (xi_ + side * t, win[0], bz1), finish, 0.004)
            kit.box((xi_, win[1], face + 0.001), (xi_ + side * t, h1, bz1), finish, 0.004)
            kit.box((xi_ - side * 0.004, win[0] - 0.012, face + 0.001), (xi_ + side * (t + 0.004), win[0] + 0.004, bz1),
                    "WarningYellow", 0.002)
            for zc_ in (bz0 + 0.06, bz1 - 0.06):
                for yc in (h0 + 0.05, h1 - c - 0.04):
                    kit.bolt((xo_, yc, zc_), (side, 0, 0))
        xo_ = o * xb
        # The hoist's motor and chain case on the hood's outer wall, its hose to the receiver.
        ym, zm = (h0 + 0.02 + h1 - c - 0.01) / 2, (bz0 + bz1) / 2
        kit.cbox((xo_ + o * 0.035, ym, zm), (0.05, h1 - c - h0 - 0.03, 0.22), "DarkSteel", 0.01)
        kit.cyl((xo_ + o * 0.06, ym + 0.08, zm), (xo_ + o * 0.11, ym + 0.08, zm), 0.05, "Gunmetal")
        if kit.d < 2:
            kit.tube([(xo_ + o * 0.06, ym - 0.18, zm + 0.06), (xo_ + o * 0.07, h0 - 0.03, zm - 0.10),
                      (o * (rx + 0.03), h0 - 0.03, face + 0.04), (o * (rx - 0.03), h0 - 0.025, face - 0.012)],
                     0.014, "Rubber", 8)
        kit.text("FEED", ((xa + xb) / 2 * o, h1 + 0.0005, zm), Vector((-1, 0, 0)), Vector((0, 0, 1)), 0.07, 0.0015,
                 "IvoryPaint")
        kit.text("SPENT", (-o * (xb + 0.0005), ym, zm), Vector((0, 0, o)), Vector((0, 1, 0)), 0.06, 0.0015, "IvoryPaint")
        # The bin's unloading hatch on its outer wall.
        kit.box((-o * (xb + 0.001), h0 + 0.05, zm - 0.20), (-o * (xb + 0.008), h1 - c - 0.02, zm + 0.20), "DarkSteel", 0.003)
    B.mesh(st, f"{sid}_Breech", frame, st.pitch)

    nodes = {}
    bay = B.node(st, f"{sid}_BayCassette", (0.0, 0.0, zs), st.pitch)
    B.mesh(st, f"{sid}_BayCassette", lambda kit: cassette(kit, 0.0, 0.0, zs), bay)
    for k, tx in enumerate(TUBES):
        B.socket(st, f"{sid}_Bay_Slot_{k:02d}", bay, (tx, SOCKET_DY, zs + D["socket"]))
    fx, fy = o * D["shuttle"], D["lift"]
    feed = B.node(st, f"{sid}_FeedCassette", (fx, fy, z0), st.pitch)
    B.mesh(st, f"{sid}_FeedCassette", lambda kit: cassette(kit, fx, fy, z0), feed)
    for k, tx in enumerate(TUBES):
        B.socket(st, f"{sid}_Feed_Slot_{k:02d}", feed, (fx + tx, fy + SOCKET_DY, z0 + D["socket"]))
    for tag, zp, dirz in (("A", D["latch"][0], 1), ("B", D["latch"][1], -1)):
        e = B.node(st, f"{sid}_Latch_{tag}", (0.0, 0.135, zp), st.pitch)

        def latch(kit, zp=zp, dirz=dirz):
            # A hub turning on the pin, an arm reaching over the cassette's end and a tip down onto its lug.
            kit.pipe((-0.05, 0.135, zp), (0.05, 0.135, zp), 0.0125, 0.022, "Gunmetal", 16)
            kit.box((-0.045, 0.122, zp + dirz * 0.018), (0.045, 0.145, zp + dirz * 0.085), "Gunmetal", 0.005)
            tip = zp + dirz * 0.085
            kit.box((-0.045, D["H"] + 0.014, tip), (0.045, 0.145, tip - dirz * 0.022), "Gunmetal", 0.004)
        B.mesh(st, f"{sid}_Latch_{tag}", latch, e)
        nodes[e.name] = e
    nodes.update({bay.name: bay, feed.name: feed})
    return nodes


# ------------------------------------------------------------------------------------------------ the table


def ease(name, t):
    t = min(1.0, max(0.0, t))
    if name == "in":
        return t ** 3
    if name == "out":
        return 1 - (1 - t) ** 3
    if name == "inOut":
        return t * t * (3 - 2 * t)
    return t


def recoil_value(bat, t):
    rc = bat["recoil"]
    times, fr = rc["times"], rc["fractions"]
    if t <= times[0]:
        return 0.0
    for i in range(1, len(times)):
        if t <= times[i]:
            f = (t - times[i - 1]) / (times[i] - times[i - 1])
            return rc["travel"] * (fr[i - 1] + f * (fr[i] - fr[i - 1]))
    return rc["travel"] * fr[-1]


def phases(bat):
    out, t = [], 0.0
    for ph in bat["loader"]["phases"]:
        out.append((ph, t, t + ph["seconds"]))
        t += ph["seconds"]
    return out


def cycle_seconds(bat):
    return phases(bat)[-1][2]


def phase_at(bat, t):
    for ph, t0, t1 in phases(bat):
        if t <= t1 + 1e-9:
            return ph["name"]
    return phases(bat)[-1][0]["name"]


def evaluate(bat, t, station, barrel, side):
    """The loader's pose at time t of a cycle (None: outside a cycle, at rest): {node: {(kind, axis): value}} in game
    axes (degrees for rotate) and {node: shown} for the holders. side: +1 port, -1 starboard (mirror, 'outboard')."""
    fmt = {"station": station, "barrel": barrel}
    out = {}
    for ph, t0, t1 in phases(bat) if t is not None else []:
        for m in ph.get("motions", []):
            node = m["node"].format(**fmt)
            axis = m["axis"]
            if axis == "outboard":
                axis = [side, 0, 0]
            axis = tuple(float(c) for c in axis)
            a, b = m.get("span", [0.0, 1.0])
            ta, tb = t0 + a * (t1 - t0), t0 + b * (t1 - t0)
            if t < ta:
                continue
            if "curve" in m:
                v = recoil_value(bat, t - t0) if t <= t1 else 0.0
            else:
                p = 1.0 if t >= tb else (t - ta) / max(tb - ta, 1e-9)
                v = m["from"] + (m["to"] - m["from"]) * ease(m.get("ease", "linear"), p)
            if m.get("mirror") and side < 0:
                axis = (-axis[0], axis[1], axis[2]) if m["kind"] == "translate" else (axis[0], -axis[1], -axis[2])
            out.setdefault(node, {})[(m["kind"], axis)] = v
    shown = {}
    starts = {ph["name"]: (t0, t1) for ph, t0, t1 in phases(bat)}
    tt = 0.0 if t is None else t
    for h in bat["loader"]["holders"]:
        node = h["node"].format(**fmt)
        a = starts[h["from"]][0] if "from" in h else 0.0
        b = starts[h["until"]][1] if "until" in h else cycle_seconds(bat)
        shown[node] = a - 1e-9 <= tt <= b + 1e-9
    return out, shown


def game_delta(values):
    """A node's pose from its motion values: a translation (game axes) and a rotation, as a 4x4 in GAME axes."""
    tr = Vector((0.0, 0.0, 0.0))
    rot = Matrix.Identity(4)
    for (kind, axis), v in values.items():
        if kind == "translate":
            tr += Vector(axis) * v
        else:
            rot = rot @ Matrix.Rotation(math.radians(v), 4, Vector(axis))
    return Matrix.Translation(tr) @ rot


def ripple(bat, station, t, lead):
    """The defense mount's other tubes in a review or a check: tube 00 fired `lead` seconds before the cycle's start
    (the third tube's shot), tube 01 half that; {node: values} at cycle time t."""
    out = {}
    if bat["fire"].get("mode") != "ripple":
        return out
    step = bat["fire"]["interval"]
    for k in (0, 1):
        tk = t + (2 - k) * step if lead else t
        if 0.0 <= tk <= bat["recoil"]["seconds"]:
            out[f"{station}_Recoil_{k:02d}"] = {("translate", (0.0, 0.0, -1.0)): recoil_value(bat, tk)}
    return out


# ------------------------------------------------------------------------------------------------ posing

C_LOCAL = Matrix(((1.0, 0.0, 0.0), (0.0, 0.0, -1.0), (0.0, 1.0, 0.0)))   # game axes -> a loader node's (and export's)


def to_local(dg):
    """A pose in game axes as a loader node's local matrix: its axes are NODE_ROT's, which are the export's."""
    m = (C_LOCAL @ dg.to_3x3() @ C_LOCAL.transposed()).to_4x4()
    m.translation = C_LOCAL @ dg.translation
    return m


def to_native(dg):
    m = (G2N @ dg.to_3x3() @ N2G).to_4x4()
    m.translation = G2N @ dg.translation
    return m


class Poser:
    """Poses stations in the hook's scene straight from the table: the pitch at an elevation and each loader at a time
    of its cycle (None: at rest). Only the LOD0 objects of the asset are moved; rest() puts everything back."""

    def __init__(self, ctx, B, table):
        self.ctx, self.B, self.table = ctx, B, table
        self.nodes = {n: (r["empty"], r["rest"].copy()) for n, r in B.nodes.items()}
        self.asset = {o: o.matrix_basis.copy() for o in ctx.objects.values() if o is not None}

    def rest(self):
        for o, m in self.asset.items():
            o.matrix_basis = m
        for e, m in self.nodes.values():
            e.matrix_basis = m

    def pose(self, st, elevation, times, lead=False):
        """times: a cycle time (or None) per loader of the station. Returns {holder node: shown}. Every recoil node of
        a defense mount follows the cycle's recoil (lead: as the ripple fired them, else all at once)."""
        bat = self.table["batteries"][st.kind]
        for o in [st.pitch] + st.recoils:
            o.matrix_basis = self.asset[o]
        st.pitch.matrix_basis = self.asset[st.pitch] @ to_native(
            Matrix.Rotation(math.radians(elevation), 4, Vector((-1.0, 0.0, 0.0))))
        for name, (e, rest) in self.nodes.items():
            if self.B.nodes[name]["station"] == st.id:
                e.matrix_basis = rest
        shown = {}
        for k, t in enumerate(times):
            values, vis = evaluate(bat, t, st.id, f"{k:02d}", st.loader_side(k))
            if st.kind == "defense" and t is not None:
                values.update(ripple(bat, st.id, t, lead))
            for node, v in values.items():
                d = game_delta(v)
                if node in self.nodes:
                    e, rest = self.nodes[node]
                    e.matrix_basis = rest @ to_local(d)
                else:
                    o = self.ctx.objects[node]
                    o.matrix_basis = self.asset[o] @ to_native(d)
            shown.update(vis)
        return shown


def world_of(o, cache):
    """An object's world matrix from its parents' bases (the scene isn't re-evaluated between a check's poses)."""
    m = cache.get(o.name)
    if m is None:
        m = o.matrix_basis.copy() if o.parent is None else world_of(o.parent, cache) @ o.matrix_parent_inverse @ o.matrix_basis
        cache[o.name] = m
    return m


# ------------------------------------------------------------------------------------------------ the clearance check


class Shape:
    """A mesh for the check: its vertices (pulled `shrink` metres in along their normals, so parts that only touch
    don't count as crossing), its faces' finishes and its world BVH trees by pose."""

    def __init__(self, name, o=None, kind="loader", frame="yaw", moves=False, shrink=0.0, box=None):
        from mathutils.bvhtree import BVHTree
        self.BVHTree = BVHTree
        self.name, self.o, self.kind, self.frame, self.moves = name, o, kind, frame, moves
        if box is not None:
            lo, hi = box
            co = np.array([(x, y, z) for z in (lo[2], hi[2]) for y in (lo[1], hi[1]) for x in (lo[0], hi[0])], float)
            self.polys = [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]
            self.finish = ["envelope"] * 6
        else:
            me = o.data
            n = len(me.vertices)
            co = np.empty(n * 3)
            me.vertices.foreach_get("co", co)
            co = co.reshape(n, 3)
            if shrink:
                nr = np.empty(n * 3)
                me.vertices.foreach_get("normal", nr)
                co = co - nr.reshape(n, 3) * shrink
            self.polys = [tuple(p.vertices) for p in me.polygons]
            names = [m.name.replace("M_IronWake_", "") if m else "" for m in me.materials]
            idx = np.empty(len(me.polygons), int)
            me.polygons.foreach_get("material_index", idx)
            self.finish = [names[i] if names else "" for i in idx]
        self.co = np.hstack([co, np.ones((len(co), 1))])
        lo, hi = co.min(0), co.max(0)
        self.corners = np.array([(x, y, z, 1.0) for x in (lo[0], hi[0]) for y in (lo[1], hi[1]) for z in (lo[2], hi[2])])
        self.trees = {}

    def centroid(self, i, mw):
        w = (self.co[list(self.polys[i])] @ np.array(mw).T)[:, :3]
        return Vector(w.mean(0))

    def matrix(self, cache):
        return Matrix.Identity(4) if self.o is None else world_of(self.o, cache)

    def aabb(self, mw):
        w = (self.corners @ np.array(mw).T)[:, :3]
        return w.min(0), w.max(0)

    def tree(self, mw):
        key = tuple(round(v, 5) for row in mw for v in row)
        t = self.trees.get(key)
        if t is None:
            w = (self.co @ np.array(mw).T)[:, :3]
            t = self.BVHTree.FromPolygons(w.tolist(), self.polys)
            if len(self.trees) > 64:
                self.trees.clear()
            self.trees[key] = t
        return t


DIRECTIONS = [Vector(d).normalized() for d in ((1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1),
                                               (1, 1, 1), (1, 1, -1), (1, -1, 1), (1, -1, -1), (-1, 1, 1), (-1, 1, -1),
                                               (-1, -1, 1), (-1, -1, -1))]

# Parts joined by design: a pair listed here may touch or pass into each other ({station}, {barrel}).
JOINTS = [
    ("{station}_BreechRing_{barrel}", "{station}_Recoil_{barrel}", "the breech ring is screwed onto the barrel's rear end"),
    ("{station}_RamPortCollar_{barrel}", "{station}_Pitch", "the ram port's collar is set into the receiver's face"),
]


def enclosed(p, others, reach=4.0):
    """True when every ray from p meets something within `reach`: p is shut in by housings, out of anyone's sight."""
    for d in DIRECTIONS:
        start = p + d * 0.003
        if not any(tree.ray_cast(start, d, reach)[0] is not None for _, tree in others):
            return False
    return True


def sample_times(bat, per_phase=4):
    """Times through a cycle: each phase's start and `per_phase` steps through it, and the recoil's furthest point."""
    ts = {0.0}
    for ph, t0, t1 in phases(bat):
        for i in range(1, per_phase + 1):
            ts.add(round(t0 + (t1 - t0) * i / per_phase, 4))
    rc = bat["recoil"]
    ts.add(rc["times"][rc["fractions"].index(max(rc["fractions"]))])
    return sorted(ts)


def clearance(ctx, B, table, step=5.0):
    """The clearance check (IRON_WAKE.md §9.5): every station posed at `step`-degree elevations across its range and
    through every phase of its cycle (each loader of a siege battery at the same time, all three tubes of a defense
    mount recoiling at once). It reports every crossing of a mechanism part with the asset (housings, armour cheeks,
    receivers, barrels, the ship around it), with the gunner's occupied volumes, with the barrels in recoil and with
    the station's other mechanism parts, and whether the mechanism comes into the seated gunner's sightline."""
    t_start = time.time()
    poser = Poser(ctx, B, table)
    layout = {s["id"]: s for s in ctx.layout["stations"]}
    moving_groups = set()
    for s in ctx.stations.values():
        moving_groups.update([s["yaw"], s["pitch"]] + list(s["recoil_nodes"]))
    report = {}
    for st in STATE["stations"]:
        bat = table["batteries"][st.kind]
        lim = bat["mount"]["pitch"]
        elevations = [lim["min"] + step * i for i in range(int((lim["max"] - lim["min"]) / step) + 1)]
        if elevations[-1] < lim["max"]:
            elevations.append(lim["max"])
        times = sample_times(bat)
        # The shapes: this station's mechanism, its asset parts, the ship around it, the gunner's volumes.
        shapes = []
        for m in B.meshes:
            if m["station"] == st.id:
                shapes.append(Shape(m["name"], m["object"], "loader", m["frame"], m["moves"], shrink=0.002))
        nodes_of = {s.name: n for n, r in B.nodes.items() if r["station"] == st.id
                    for s in r["empty"].children if s.type == "MESH"}
        owner = {sh.name: nodes_of.get(sh.o.name) for sh in shapes}
        shapes.append(Shape(st.st["yaw"], st.yaw, "asset", "yaw"))
        shapes.append(Shape(st.st["pitch"], st.pitch, "asset", "pitch"))
        for r in st.st["recoil_nodes"]:
            shapes.append(Shape(r, ctx.objects[r], "asset", "pitch", moves=True))
        cache = {}
        lo = np.array([1e9] * 3)
        hi = -lo
        for sh in shapes:
            if sh.kind == "loader":
                a, b = sh.aabb(sh.matrix(cache))
                lo, hi = np.minimum(lo, a), np.maximum(hi, b)
        lo, hi = lo - 2.5, hi + 2.5
        for g, o in ctx.objects.items():
            if o is None or g in moving_groups or o.type != "MESH":
                continue
            sh = Shape(g, o, "asset", "yaw")
            a, b = sh.aabb(sh.matrix(cache))
            if np.all(a <= hi) and np.all(b >= lo):
                shapes.append(sh)
        lay = layout.get(st.id, {})
        for env in lay.get("occupied_envelopes", []):
            c, s = Vector(env["center_m"]), Vector(env["size_m"]) * 0.5
            shapes.append(Shape(f"gunner {env['id']}", None, "gunner", "yaw", box=(c - s, c + s)))
        joints = [(a.format(station=st.id, barrel=f"{k:02d}"), b.format(station=st.id, barrel=f"{k:02d}"), why)
                  for k in range(len(st.barrels)) for a, b, why in JOINTS]
        records = {}
        rest_pitch = world_of(st.pitch, {})
        checks = poses = 0
        for ie, e in enumerate(elevations):
            for it, t in enumerate(times):
                shown = poser.pose(st, e, [t] * st.loaders())
                poses += 1
                cache = {}
                live = [sh for sh in shapes if shown.get(owner.get(sh.name), True)]
                mats = {sh.name: sh.matrix(cache) for sh in live}
                boxes = {sh.name: sh.aabb(mats[sh.name]) for sh in live}
                to_rest = rest_pitch @ world_of(st.pitch, cache).inverted()
                for i, a in enumerate(live):
                    for b in live[i + 1:]:
                        if a.kind != "loader" and b.kind != "loader":
                            continue
                        same = a.frame == b.frame
                        if ie > 0 and same:
                            continue
                        if it > 0 and not (a.moves or b.moves):
                            continue
                        (la, ha), (lb, hb) = boxes[a.name], boxes[b.name]
                        if np.any(la > hb) or np.any(lb > ha):
                            continue
                        checks += 1
                        pairs = a.tree(mats[a.name]).overlap(b.tree(mats[b.name]))
                        if not pairs:
                            continue
                        fa = {a.finish[i] for i, _ in pairs}
                        fb = {b.finish[j] for _, j in pairs}
                        rec = records.setdefault((a.name, b.name), {
                            "a": a.name, "b": b.name, "count": 0, "faces": 0, "elevations": set(), "phases": set(),
                            "finishes": set(), "lo": Vector((1e9,) * 3), "hi": Vector((-1e9,) * 3), "tested": 0,
                            "hidden": 0, "dark": 0})
                        rec["count"] += 1
                        rec["faces"] = max(rec["faces"], len(pairs))
                        rec["elevations"].add(e)
                        rec["phases"].add(phase_at(bat, t))
                        rec["finishes"].update(fa | fb)
                        # Where it is (the station's frame at rest), and whether it is out of sight: a round passing
                        # into a port's dark back wall, or a crossing enclosed by housings on every side.
                        # A round passing into a port's dark back wall is how it disappears: those faces apart.
                        lit = [(i_, j_) for i_, j_ in pairs
                               if not (b.kind == "asset" and b.finish[j_] == "BlackPaint")
                               and not (a.kind == "asset" and a.finish[i_] == "BlackPaint")]
                        step_ = max(1, len(lit) // 4)
                        pts = [(a.centroid(i_, mats[a.name]) + b.centroid(j_, mats[b.name])) * 0.5
                               for i_, j_ in lit[::step_][:4]]
                        every = max(1, len(pairs) // 4)
                        for i_, j_ in pairs[::every][:4]:
                            pt = (a.centroid(i_, mats[a.name]) + b.centroid(j_, mats[b.name])) * 0.5
                            q = st.local(to_rest @ pt if a.frame == "pitch" else pt)
                            rec["lo"] = Vector([min(rec["lo"][c], q[c]) for c in range(3)])
                            rec["hi"] = Vector([max(rec["hi"][c], q[c]) for c in range(3)])
                        if not lit:
                            rec["dark"] += 1
                        elif rec["tested"] < 10:
                            rec["tested"] += 1
                            others = [(sh, sh.tree(mats[sh.name])) for sh in live
                                      if sh.kind != "gunner" and not (sh.kind == "loader" and sh.name in (a.name, b.name))]
                            if all(enclosed(pt, others) for pt in pts):
                                rec["hidden"] += 1
                            elif len(rec.setdefault("seen", [])) < 2:
                                qa = [st.local(to_rest @ a.centroid(i_, mats[a.name]) if a.frame == "pitch"
                                               else a.centroid(i_, mats[a.name])) for i_, _ in lit[::step_][:3]]
                                rec["seen"].append({"elevation": e, "t": round(t, 3), "phase": phase_at(bat, t),
                                                    "at": [[round(v, 3) for v in q] for q in qa]})
        sight = sightline(ctx, poser, st, shapes, owner, elevations, times, lay)
        rack = rack_view(poser, st, shapes, lay) if st.kind == "siege" else None
        poser.rest()

        def tidy(r):
            out = {"a": r["a"], "b": r["b"], "poses": r["count"], "faces": r["faces"],
                   "elevations": [min(r["elevations"]), max(r["elevations"])],
                   "phases": sorted(r["phases"]), "finishes": sorted(r["finishes"]),
                   "where": [[round(v, 2) for v in r["lo"]], [round(v, 2) for v in r["hi"]]]}
            if r.get("why"):
                out["why"] = r["why"]
            if r.get("seen"):
                out["seen"] = r["seen"]
            return out
        groups = {"crossings": [], "hidden": [], "intoDarkPorts": [], "joints": []}
        for r in records.values():
            why = next((w for ja, jb, w in joints if {ja, jb} == {r["a"], r["b"]}), None)
            if why:
                r["why"] = why
                groups["joints"].append(r)
            elif r["dark"] and not r["tested"]:
                groups["intoDarkPorts"].append(r)
            elif r["tested"] and r["hidden"] == r["tested"]:
                groups["hidden"].append(r)
            else:
                groups["crossings"].append(r)
        report[st.id] = {"elevations": [elevations[0], elevations[-1], step], "times": len(times), "poses": poses,
                         "pairsTested": checks, **{k: [tidy(r) for r in v] for k, v in groups.items()},
                         "sightline": sight}
        if rack is not None:
            report[st.id]["rackInView"] = rack
        ctx.log(f"clearance {st.id}: {poses} poses, {checks} pair tests; " +
                ", ".join(f"{k} {len(v)}" for k, v in groups.items()) + f"; sightline {sight}" +
                (f"; rack {rack}" if rack else ""))
        for k in ("crossings", "hidden", "joints", "intoDarkPorts"):
            for r in groups[k]:
                t_ = tidy(r)
                ctx.log(f"  {k} {r['a']} x {r['b']}: {r['count']} poses ({r['tested']} tested, {r['hidden']} hidden), "
                        f"up to {r['faces']} faces, elevations {t_['elevations']}, phases {t_['phases']}, "
                        f"finishes {t_['finishes']}, at {t_['where']}" + (f", seen {r['seen']}" if r.get("seen") else ""))
    ctx.log(f"clearance checked in {time.time() - t_start:.0f} s")
    return report


def sightline(ctx, poser, st, shapes, owner, elevations, times, lay):
    """Rays from the seated gunner's eye through the view ahead (40 degrees wide, from 10 below the aim to 30 above):
    how many of those the ship leaves open (nothing within 60 m) the mechanism would close, at any pose."""
    eye = Vector(lay["eye_position_m"])
    rays = closed = seen = 0
    first = None
    for e in elevations[::2] + ([elevations[-1]] if len(elevations) % 2 == 0 else []):
        for t in times[::3]:
            shown = poser.pose(st, e, [t] * st.loaders())
            cache = {}
            live = [sh for sh in shapes if sh.kind in ("loader", "asset") and shown.get(owner.get(sh.name), True)]
            trees = [(sh, sh.tree(sh.matrix(cache))) for sh in live]
            for h in range(-20, 21, 4):
                for v in range(-10, 31, 4):
                    a, b = math.radians(h), math.radians(e + v)
                    d = Vector((math.cos(b) * math.cos(a), math.cos(b) * math.sin(a), math.sin(b)))
                    near = {"asset": 60.0, "loader": 60.0}
                    part = None
                    for sh, tree in trees:
                        hit = tree.ray_cast(eye, d, near[sh.kind])
                        if hit[0] is not None and hit[3] < near[sh.kind]:
                            near[sh.kind] = hit[3]
                            if sh.kind == "loader":
                                part = sh.name
                    rays += 1
                    if part is not None and near["loader"] < near["asset"]:
                        seen += 1
                        if near["asset"] >= 60.0:
                            closed += 1
                            first = first or {"part": part, "elevation": e, "phase": phase_at(
                                poser.table["batteries"][st.kind], t), "ray": [h, v]}
    out = {"rays": rays, "mechanismInView": seen, "openViewClosed": closed}
    if first:
        out["first"] = first
    return out


def rack_view(poser, st, shapes, lay):
    """Which of the ready racks' rounds the seated gunner sees: rays from the eye to nine points on each round's
    upper surface on the gunner's side (its top, and 30 and 60 degrees down towards the gunner), at rest."""
    eye = Vector(lay["eye_position_m"])
    poser.pose(st, 0.0, [None] * st.loaders())
    cache = {}
    trees = [(sh, sh.tree(sh.matrix(cache))) for sh in shapes if sh.kind in ("loader", "asset")]
    seen = {}
    for k in range(st.loaders()):
        inboard = Vector((0.0, -1.0 if st.barrels[k].x > 0 else 1.0, 0.0))     # native +Y is game +X
        for j in range(4):
            name = f"{st.id}_Carrier_{k:02d}_{j:02d}"
            sock = bpy.data.objects[f"SOCKET_{st.id}_Rack_{k:02d}_Slot_{j:02d}"]
            centre = world_of(sock, cache).translation + Vector((0.0, 0.0, -SOCKET_DY))
            hits = 0
            for dz in (-0.25, 0.0, 0.25):
                for ang in (90.0, 120.0, 150.0):
                    a = math.radians(ang)
                    p = centre + Vector((dz, 0.0, 0.0)) + (inboard * -math.cos(a) + Vector((0, 0, 1)) * math.sin(a)) * (
                        SIEGE["R"] + 0.004)
                    d = p - eye
                    dist = d.length
                    d.normalize()
                    first, near = None, dist
                    for sh, tree in trees:
                        hit = tree.ray_cast(eye, d, near)
                        if hit[0] is not None and hit[3] < near:
                            first, near = sh, hit[3]
                    if first is None or first.name == name or near > dist - 0.02:
                        hits += 1
            seen[f"{k:02d}/{j:02d}"] = f"{hits}/9"
    return seen


# ------------------------------------------------------------------------------------------------ the review clips


def clips(parts, fps):
    """The review clips (IRON_WAKE.md §9.5), baked from the table for the model viewer: one per mechanism, a whole
    cycle with the recoil that starts it: `<station>_Load_<barrel>` on the siege batteries, `<station>_Load` on the
    defense mounts (the ripple's three shots, then the cassette changed). parts: {part name: the export's empty},
    whose axes are the game's turned into Blender's (x, -z, y), as a loader node's are. The runtime poses from the
    table; these clips only show it."""
    table = load_table()
    made = []
    for st in STATE["stations"]:
        bat = table["batteries"][st.kind]
        total = cycle_seconds(bat)
        if st.kind == "siege":
            for k in range(st.loaders()):
                name = f"{st.id}_Load_{k:02d}"
                bake(name, parts, fps, total, 0.0,
                     lambda t, k=k: evaluate(bat, t, st.id, f"{k:02d}", st.loader_side(k)))
                made.append(name)
        else:
            lead = 2 * bat["fire"]["interval"]
            name = f"{st.id}_Load"

            def sample(t, st=st, bat=bat, lead=lead):
                values, shown = evaluate(bat, max(t, 0.0) if t >= 0 else None, st.id, "00", st.loader_side(0))
                values.update(ripple(bat, st.id, t, lead))
                if t < 0:
                    values[f"{st.id}_Recoil_02"] = {}
                return values, shown
            bake(name, parts, fps, total, lead, sample)
            made.append(name)
    STATE["clips"] = made
    return made


def bake(name, parts, fps, total, lead, sample):
    """One action with a slot per animated node, keyed every frame from -lead to the cycle's end: location (game
    translations), rotation (game rotations) and, for the holders, scale (0.001 while hidden; every hand-over happens
    inside a housing). One NLA strip per node, as the import's own clips."""
    frames = int(math.ceil((total + lead) * fps - 1e-6))
    samples = []
    nodes, holders = set(), set()
    for f in range(frames + 1):
        t = f / fps - lead
        values, shown = sample(t)
        samples.append((f, values, shown))
        nodes.update(values)
        holders.update(shown)
    tracks = {}
    for node in sorted(nodes | holders):
        obj = parts.get(node)
        if obj is None:
            continue
        rot = any(k[0] == "rotate" for _, values, _ in samples for k in values.get(node, {}))
        keys = {"location": [], "rotation_euler": [], "scale": []}
        for f, values, shown in samples:
            d = to_local(game_delta(values.get(node, {})))
            keys["location"].append((f, d.translation.copy()))
            if rot:
                keys["rotation_euler"].append((f, d.to_euler("XYZ")))
            if node in holders:
                s = 1.0 if shown[node] else 0.001
                keys["scale"].append((f, Vector((s, s, s))))
        if node not in nodes:
            keys["location"] = []
        tracks[obj] = {p: k for p, k in keys.items() if k}
    action = bpy.data.actions.new(name)
    for obj, paths in tracks.items():
        if "location" in paths and obj.delta_location.length == 0 and obj.location.length > 0:
            obj.delta_location = obj.location.copy()
            obj.location = (0.0, 0.0, 0.0)
        obj.animation_data_create()
        obj.animation_data.action = action
        slot = obj.animation_data.action_slot
        if slot is None:
            slot = action.slots.new(id_type="OBJECT", name=obj.name)
            obj.animation_data.action_slot = slot
        rest = {p: getattr(obj, p).copy() for p in paths}
        for path, keys in paths.items():
            for f, v in keys:
                setattr(obj, path, v)
                obj.keyframe_insert(path, frame=1 + f)
            setattr(obj, path, rest[path])
        track = obj.animation_data.nla_tracks.new()
        track.name = name
        strip = track.strips.new(name, 1, action)
        strip.action_slot = slot
        strip.extrapolation = "NOTHING"
        obj.animation_data.action = None
    for fc in fcurves(action):
        for k in fc.keyframe_points:
            k.interpolation = "LINEAR"


def fcurves(action):
    try:
        for layer in action.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    yield from bag.fcurves
    except AttributeError:
        yield from action.fcurves


# ------------------------------------------------------------------------------------------------ the hook

STATE = {}


def build(ctx):
    """The hook (art/import/iron_wake.py, IRON_WAKE.md §9.5): makes every station's mechanism in ctx.collection,
    checks its clearances and returns the data file's `loaders` block."""
    t0 = time.time()
    B = Builder(ctx)
    STATE.clear()
    STATE.update({"ctx": ctx, "builder": B, "stations": []})
    for sid in sorted(ctx.stations):
        st = Station(ctx, sid)
        STATE["stations"].append(st)
        if st.kind == "siege":
            siege(ctx, B, st)
        else:
            defense(ctx, B, st)
    for o in list(scratch().objects):
        bpy.data.objects.remove(o)
    bpy.context.view_layer.update()
    ctx.log(f"mechanisms built in {time.time() - t0:.0f} s; triangles added by level: {B.tris}")
    top = sorted(B.parts.items(), key=lambda kv: -kv[1])[:24]
    ctx.log("heaviest parts at LOD0: " + ", ".join(f"{k} {v}" for k, v in top))
    ctx.builder = B
    table = load_table()
    check_table(table)
    check = clearance(ctx, B, table) if not getattr(ctx, "skip_clearance", False) else None
    bpy.context.view_layer.update()
    return record(B, table, check)


def check_table(table):
    """The table's travels are the geometry's: a stroke changed in one place and not the other stops the import."""
    want = {
        ("siege", "{station}_Breech_{barrel}", "breechOpen"): SIEGE["block"],
        ("siege", "{station}_Carrier_{barrel}_00", "fetch"): SIEGE["feed"],
        ("siege", "{station}_Feed_{barrel}_B", "fetch"): SIEGE["feed"],
        ("siege", "{station}_Feed_{barrel}_A", "fetch"): SIEGE["feed"] / 2,
        ("siege", "{station}_Carrier_{barrel}_01", "present"): -SIEGE["pitch"],
        ("siege", "{station}_Rammer_{barrel}", "ram"): SIEGE["ram"],
        ("siege", "{station}_RamCarrier_{barrel}", "ram"): SIEGE["ram"],
        ("defense", "{station}_FeedCassette", "lower"): -DEFENSE["lift"],
        ("defense", "{station}_FeedCassette", "eject"): -DEFENSE["shuttle"],
        ("defense", "{station}_BayCassette", "eject"): -DEFENSE["shuttle"],
        ("defense", "{station}_BayCassette", "seat"): DEFENSE["stow"],
        ("defense", "{station}_FeedCassette", "seat"): DEFENSE["seat"],
    }
    for (kind, node, phase), value in want.items():
        ph = next(p for p in table["batteries"][kind]["loader"]["phases"] if p["name"] == phase)
        m = next(m for m in ph["motions"] if m["node"] == node)
        if abs(m["to"] - value) > 1e-6:
            raise ValueError(f"batteries.json: {kind} {phase} moves {node} to {m['to']}, the mechanism is built for {value}")


def record(B, table, check):
    """The data file's `loaders` block: what the rules and the view need besides the table (IRON_WAKE.md §9.5)."""
    stations = {}
    for st in STATE["stations"]:
        bat = table["batteries"][st.kind]
        loaders = []
        for k in range(st.loaders()):
            barrel = f"{k:02d}"
            names = sorted({m["node"].format(station=st.id, barrel=barrel) for ph in bat["loader"]["phases"]
                            for m in ph.get("motions", [])})
            holders = []
            for h in bat["loader"]["holders"]:
                holders.append({"node": h["node"].format(station=st.id, barrel=barrel),
                                "slots": [s.format(station=st.id, barrel=barrel) for s in h["slots"]]})
            loader = {"side": "port" if st.loader_side(k) > 0 else "starboard", "nodes": names, "holders": holders}
            if st.kind == "siege":
                loader["barrel"] = k
                loader["clip"] = f"{st.id}_Load_{barrel}"
                loader["breechSocket"] = f"{st.id}_Breech_{barrel}"
            else:
                loader["clip"] = f"{st.id}_Load"
            loaders.append(loader)
        stations[st.id] = {"kind": st.kind, "loaders": loaders}
    out = {
        "table": "game/data/ground/batteries.json",
        "module": "art/ships/iron_wake_loaders.py",
        "bore": "siege: a discarding sabot per round, three steel petals on a pusher base sized to the 48 cm bore "
                "(the muzzle's hollow bore stays as modelled); defense: the rockets bare in the 10.4 cm tubes",
        "stations": stations,
        "triangles": {f"lod{k}": v for k, v in sorted(B.tris.items())},
    }
    if check is not None:
        out["clearance"] = check
    return out

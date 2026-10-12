"""Building our own models (GROUND.md §9): primitives mapped onto the shared PSG atlas (or the tiling rock), tinted
per vertex, with vertex ambient occlusion, decimated LODs, a convex hull and attach points, exported in Hollow's
layout. Models are authored in their final pose: held items point +Y with the grip at the origin; world objects face
-Y with their footprint centred on the origin at ground level.
"""
import math
import random

import bmesh
import bpy
import numpy as np
from mathutils import Euler, Matrix, Vector, noise

from . import ITEMS, MATERIALS, TEXTURES, WORLD, log
from .export import (EXPORT_COLOUR, collection, convex_hull, decimated, export_glb, reset, select_only,
                     set_export_colour, vertex_ao, box_hull)
from .godot import write_material
from .textures import downscale, normal_from_height, read, write_png, write_texture_import

# Tiles of the shared PSG atlas (4 x 4, tile 0 at the bottom left).
TIMBER, RUST, GUNMETAL, IVORY, OCHRE, TEAL, RUBBER, STEEL = range(8)
CANVAS, QUILT, BASALT, BLUEPRINT, SOLAR, LEATHER, GLOW, PARCHMENT = range(8, 16)

ATLAS = "M_PSG_Surface"
ROCK = "M_Hollow_Rock"
GLOSSY = "M_Hollow_Glossy"

WHITE = (1.0, 1.0, 1.0)


def materials():
    """Blender material name -> Godot material; writes the rock texture set and Hollow's plain materials once."""
    rock = TEXTURES / "rock"
    albedo, orm, normal = rock / "T_Hollow_Rock_BaseColor.png", rock / "T_Hollow_Rock_ORM.png", rock / "T_Hollow_Rock_NormalGL.png"
    if not albedo.exists():
        from . import SOURCES
        src = SOURCES / "PSG_ResourceNodes_R01" / "ThirdParty" / "PolyHaven" / "rock_08"
        colour = downscale(read(src / "rock_08_diff_4k.jpg"), 1024)
        # Neutral, bright rock detail: the vertex colour gives each rock its own colour.
        lum = colour[..., 0] * 0.3 + colour[..., 1] * 0.59 + colour[..., 2] * 0.11
        lum = 0.6 + (lum - lum.mean()) * 1.7
        grey = colour.copy()
        for c in range(3):
            grey[..., c] = np.clip(lum + (colour[..., c] - colour[..., :3].mean(axis=2)) * 0.35, 0, 1)
        write_png(albedo, grey)
        rough = downscale(read(src / "rock_08_rough_4k.jpg"), 1024)
        packed = np.ones_like(rough)
        packed[..., 1] = np.clip(0.7 + (rough[..., 0] - rough[..., 0].mean()) * 0.8, 0.45, 1)
        packed[..., 2] = 0
        write_png(orm, packed)
        height = downscale(read(src / "rock_08_disp_4k.jpg"), 1024)[..., 0]
        write_png(normal, normal_from_height(height, 30.0))
    write_texture_import(albedo)
    write_texture_import(orm)
    write_texture_import(normal, normal_map=True)
    return {
        ATLAS: MATERIALS / "psg_surface.tres",
        ROCK: write_material("hollow_rock", ROCK, albedo=albedo, orm=orm, normal=normal),
        GLOSSY: write_material("hollow_glossy", GLOSSY, roughness=0.32, metallic=0.0),
    }


def _material(name):
    m = bpy.data.materials.get(name)
    if m is None:
        m = bpy.data.materials.new(name)
    return m


class Model:
    """One model under construction. Call primitives, then `export()`."""

    def __init__(self, key, folder=ITEMS, seed=1):
        reset()
        self.key = key
        self.folder = folder
        self.work = collection("HOLLOW_PARTS")
        self.out = collection("HOLLOW_EXPORT")
        self.parts = {"Body": {"pivot": Vector((0, 0, 0)), "objects": []}}
        self.current = "Body"
        self.sockets = []
        self.rng = random.Random(seed)
        self.count = 0

    # ---------------------------------------------------------------- parts and sockets

    def part(self, name, pivot=(0, 0, 0)):
        """Following primitives belong to part `name`, which moves about `pivot`."""
        self.parts.setdefault(name, {"pivot": Vector(pivot), "objects": []})
        self.current = name
        return self

    def socket(self, name, at, rot=(0, 0, 0)):
        e = bpy.data.objects.new(f"socket_{name}", None)
        e.empty_display_size = 0.03
        self.out.objects.link(e)
        e.matrix_world = Matrix.LocRotScale(Vector(at), Euler([math.radians(a) for a in rot]), None)
        self.sockets.append(e)
        return e

    # ---------------------------------------------------------------- primitives

    def add(self, bm, at=(0, 0, 0), rot=(0, 0, 0), tile=TIMBER, tint=WHITE, material=ATLAS, metres=0.5, uv="box",
            sharp=35.0, vary=0.0, vary_scale=6.0, smooth=True, scale=None, tint_fn=None):
        """Turns a bmesh built in local space into a model object: UVs (in local space, so grain follows the part),
        material, tint (`tint_fn(local position, tint)` varies it), shading, then moves it into place."""
        self.count += 1
        me = bpy.data.meshes.new(f"{self.key}_{self.count}")
        bm.normal_update()
        bm.to_mesh(me)
        bm.free()
        o = bpy.data.objects.new(me.name, me)
        self.work.objects.link(o)
        if scale is not None:
            me.transform(Matrix.Diagonal(Vector(scale)).to_4x4())
        if material == ROCK:
            self._uv_world(me, metres)
        elif uv == "cyl":
            self._uv_cylinder(me, tile, metres)
        elif uv == "path":
            self._uv_path(me, tile, metres)
        else:
            self._uv_box(me, tile, metres)
        me.materials.append(_material(material))
        for p in me.polygons:
            p.use_smooth = smooth
        if smooth and sharp:
            me.set_sharp_from_angle(angle=math.radians(sharp))
        self._tint(me, tint, vary, vary_scale, tint_fn)
        m = Matrix.LocRotScale(Vector(at), Euler([math.radians(a) for a in rot]), None)
        me.transform(m)
        me.update()
        o["part"] = self.current
        self.parts[self.current]["objects"].append(o)
        return o

    def box(self, size, at=(0, 0, 0), rot=(0, 0, 0), bevel=0.004, segments=2, **kw):
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1.0)
        bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
        if bevel > 0:
            bevel = min(bevel, min(size) * 0.3)
            bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=segments, profile=0.5, affect="EDGES",
                            clamp_overlap=True)
        return self.add(bm, at, rot, **kw)

    def cylinder(self, radius, depth, at=(0, 0, 0), rot=(0, 0, 0), sides=16, bevel=0.0, radius2=None, **kw):
        """Along local Z, centred. `radius2` makes a cone/frustum (top radius)."""
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=sides, radius1=radius,
                              radius2=radius if radius2 is None else radius2, depth=depth)
        if bevel > 0:
            bm.normal_update()
            rim = [e for e in bm.edges if len(e.link_faces) == 2
                   and e.link_faces[0].normal.angle(e.link_faces[1].normal, 0) > math.radians(60)]
            bmesh.ops.bevel(bm, geom=rim, offset=min(bevel, radius * 0.4, depth * 0.3), segments=2, profile=0.5,
                            affect="EDGES", clamp_overlap=True)
        kw.setdefault("uv", "cyl")
        return self.add(bm, at, rot, **kw)

    def lathe(self, profile, at=(0, 0, 0), rot=(0, 0, 0), sides=24, close=True, **kw):
        """Revolves (radius, z) points about local Z; zero radius ends close to a point."""
        bm = bmesh.new()
        rings = []
        for r, z in profile:
            if r <= 1e-6:
                rings.append([bm.verts.new((0, 0, z))])
            else:
                rings.append([bm.verts.new((r * math.cos(a), r * math.sin(a), z))
                              for a in (2 * math.pi * i / sides for i in range(sides))])
        for a, b in zip(rings, rings[1:]):
            if len(a) == 1 and len(b) == 1:
                continue
            if len(a) == 1:
                for i in range(sides):
                    bm.faces.new((a[0], b[i], b[(i + 1) % sides]))
            elif len(b) == 1:
                for i in range(sides):
                    bm.faces.new((a[i], b[0], a[(i + 1) % sides]))
            else:
                for i in range(sides):
                    bm.faces.new((a[i], a[(i + 1) % sides], b[(i + 1) % sides], b[i]))
        if close:
            if len(rings[0]) > 1:
                bm.faces.new(list(reversed(rings[0])))
            if len(rings[-1]) > 1:
                bm.faces.new(rings[-1])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        kw.setdefault("uv", "cyl")
        return self.add(bm, at, rot, **kw)

    def tube(self, points, radius, sides=8, cap=True, radii=None, **kw):
        """A tube along model-space points (wires, bindings, handles); `radii` per point."""
        pts = [Vector(p) for p in points]
        bm = bmesh.new()
        rings = []
        prev_u = None
        for i, p in enumerate(pts):
            t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
            if prev_u is None:
                ref = Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0))
                u = t.cross(ref).normalized()
            else:
                u = (prev_u - t * prev_u.dot(t)).normalized()
            w = t.cross(u)
            prev_u = u
            r = radii[i] if radii else radius
            rings.append([bm.verts.new(p + r * (math.cos(a) * u + math.sin(a) * w))
                          for a in (2 * math.pi * j / sides for j in range(sides))])
        for a, b in zip(rings, rings[1:]):
            for j in range(sides):
                bm.faces.new((a[j], a[(j + 1) % sides], b[(j + 1) % sides], b[j]))
        if cap:
            bm.faces.new(list(reversed(rings[0])))
            bm.faces.new(rings[-1])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        kw.setdefault("uv", "path")
        return self.add(bm, **kw)

    def blob(self, size, at=(0, 0, 0), rot=(0, 0, 0), subdivisions=3, rough=0.18, freq=1.6, facets=0.0, seed=None,
             flatten=None, **kw):
        """A lumpy closed shape: an icosphere of radius 1 displaced by noise (and Voronoi facets for chipped stone),
        then scaled to `size` (x, y, z diameters). `flatten` cuts it flat below that z (as a fraction of the radius)."""
        seed = self.rng.random() * 100 if seed is None else seed
        bm = bmesh.new()
        bmesh.ops.create_icosphere(bm, subdivisions=subdivisions, radius=1.0)
        off = Vector((seed * 1.37, seed * 2.11, seed * 0.73))
        for v in bm.verts:
            n = v.co.normalized()
            d = noise.fractal(n * freq + off, 0.6, 2.0, 4, noise_basis="PERLIN_ORIGINAL") * rough
            if facets:
                d -= noise.voronoi(n * (freq * 1.3) + off, distance_metric="DISTANCE", exponent=2.5)[0][0] * facets
            v.co = n * (1 + d)
        if flatten is not None:
            for v in bm.verts:
                if v.co.z < flatten:
                    v.co.z = flatten + (v.co.z - flatten) * 0.08
        bmesh.ops.scale(bm, vec=Vector(size) * 0.5, verts=bm.verts)
        kw.setdefault("sharp", 0)
        return self.add(bm, at, rot, **kw)

    def extrude(self, outline, width, at=(0, 0, 0), rot=(0, 0, 0), bevel=0.0, segments=1, **kw):
        """A flat shape: `outline` (x, z) points, counter-clockwise, extruded `width` along local Y, centred."""
        bm = bmesh.new()
        front = [bm.verts.new((x, -width / 2, z)) for x, z in outline]
        back = [bm.verts.new((x, width / 2, z)) for x, z in outline]
        n = len(outline)
        bm.faces.new(front)
        bm.faces.new(list(reversed(back)))
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((front[j], front[i], back[i], back[j]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        if bevel > 0:
            bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=segments, profile=0.5, affect="EDGES",
                            clamp_overlap=True)
        return self.add(bm, at, rot, **kw)

    def from_mesh(self, verts, faces, at=(0, 0, 0), rot=(0, 0, 0), **kw):
        bm = bmesh.new()
        vs = [bm.verts.new(v) for v in verts]
        for f in faces:
            bm.faces.new([vs[i] for i in f])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        return self.add(bm, at, rot, **kw)

    # ---------------------------------------------------------------- UVs and colour

    def _place(self, me, tile, raw, extent_u, extent_v, metres):
        """Maps raw (u, v) metres into a tile at `metres` per tile, shrunk to fit, at a deterministic offset."""
        scale = 1.0 / metres
        span = max(extent_u, extent_v) * scale
        if span > 0.94:
            scale *= 0.94 / span
        ou = 0.03 + self.rng.random() * max(0.0, 0.94 - extent_u * scale)
        ov = 0.03 + self.rng.random() * max(0.0, 0.94 - extent_v * scale)
        uv = me.uv_layers.new(name="UVMap")
        for li, (u, v) in enumerate(raw):
            uv.data[li].uv = ((tile % 4 + ou + u * scale) / 4, (tile // 4 + ov + v * scale) / 4)

    def _uv_box(self, me, tile, metres):
        vs = [v.co for v in me.vertices]
        lo = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs)))
        hi = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
        ext = hi - lo
        raw = [None] * len(me.loops)
        for p in me.polygons:
            axis = max(range(3), key=lambda a: abs(p.normal[a]))
            axes = [a for a in range(3) if a != axis]
            # Grain (the tiles' v) runs along the longer side.
            if ext[axes[0]] > ext[axes[1]]:
                axes.reverse()
            for li in p.loop_indices:
                c = me.vertices[me.loops[li].vertex_index].co
                raw[li] = (c[axes[0]] - lo[axes[0]], c[axes[1]] - lo[axes[1]])
        eu = max(r[0] for r in raw)
        ev = max(r[1] for r in raw)
        self._place(me, tile, raw, eu, ev, metres)

    def _uv_cylinder(self, me, tile, metres):
        vs = [v.co for v in me.vertices]
        zlo = min(v.z for v in vs)
        rmax = max(math.hypot(v.x, v.y) for v in vs) or 0.01
        raw = [None] * len(me.loops)
        for p in me.polygons:
            cap = abs(p.normal.z) > 0.85
            centre_angle = math.atan2(p.center.y, p.center.x)
            for li in p.loop_indices:
                c = me.vertices[me.loops[li].vertex_index].co
                if cap:
                    raw[li] = (c.x + rmax, c.y + rmax)
                else:
                    a = math.atan2(c.y, c.x)
                    # Keep each face on one side of the seam.
                    if a - centre_angle > math.pi:
                        a -= 2 * math.pi
                    elif centre_angle - a > math.pi:
                        a += 2 * math.pi
                    raw[li] = ((a + math.pi) * rmax, c.z - zlo)
        eu = max(r[0] for r in raw)
        ev = max(r[1] for r in raw)
        self._place(me, tile, raw, eu, ev, metres)

    def _uv_path(self, me, tile, metres):
        # Tubes: around and along, from the vertex order (rings of equal size).
        self._uv_box(me, tile, metres)

    def _uv_world(self, me, metres):
        """Tiling rock: box projection in model space, `metres` per repeat (continuous across parts)."""
        uv = me.uv_layers.new(name="UVMap")
        for p in me.polygons:
            axis = max(range(3), key=lambda a: abs(p.normal[a]))
            axes = [a for a in range(3) if a != axis]
            for li in p.loop_indices:
                c = me.vertices[me.loops[li].vertex_index].co
                uv.data[li].uv = (c[axes[0]] / metres + axis * 0.37, c[axes[1]] / metres + axis * 0.61)

    def _tint(self, me, tint, vary, vary_scale, tint_fn=None):
        col = me.color_attributes.new(name="Tint", type="FLOAT_COLOR", domain="POINT")
        values = np.empty((len(me.vertices), 4), np.float32)
        seed = Vector((self.rng.random() * 50, self.rng.random() * 50, self.rng.random() * 50))
        for i, v in enumerate(me.vertices):
            k = 1.0
            if vary:
                k = 1.0 + vary * noise.noise(v.co * vary_scale + seed, noise_basis="PERLIN_ORIGINAL")
            t = tint_fn(v.co, tint) if tint_fn else tint
            values[i] = (t[0] * k, t[1] * k, t[2] * k, 1.0)
        col.data.foreach_set("color", values.ravel())

    def recolour(self, obj, fn):
        """Per-vertex tint by a function of the model-space position (end grain rings, scorch marks...)."""
        col = obj.data.color_attributes["Tint"]
        values = np.empty(len(obj.data.vertices) * 4, np.float32)
        col.data.foreach_get("color", values)
        values = values.reshape(-1, 4)
        for i, v in enumerate(obj.data.vertices):
            values[i, :3] = fn(v.co, values[i, :3])
        col.data.foreach_set("color", values.ravel())

    # ---------------------------------------------------------------- finishing

    def export(self, hulls="auto", ao=0.55, lods=(0.5, 0.22), name=None, ao_distance=None, floor=True):
        """Joins each part into its LOD0 mesh, bakes vertex AO into the tint, decimates LOD1 and LOD2, adds the
        convex hull(s) and writes the GLB. `hulls`: "auto" (one hull around everything), "none", or a list of
        (centre, size, rotation) boxes."""
        mats = materials()
        objects = []
        lod0 = {}
        for part, record in self.parts.items():
            objs = record["objects"]
            if not objs:
                continue
            select_only(objs)
            if len(objs) > 1:
                bpy.ops.object.join()
            o = bpy.context.view_layer.objects.active
            o.name = f"{part}_LOD0"
            o.data.name = o.name
            for c in list(o.users_collection):
                c.objects.unlink(o)
            self.out.objects.link(o)
            lod0[part] = o
        occlusion = vertex_ao(list(lod0.values()), samples=40, distance=ao_distance, floor=floor)
        for part, o in lod0.items():
            tint = np.empty(len(o.data.vertices) * 4, np.float32)
            o.data.color_attributes["Tint"].data.foreach_get("color", tint)
            tint = tint.reshape(-1, 4)
            shade = 1 - ao + ao * occlusion[o]
            tint[:, :3] *= shade[:, None]
            tint[:, 3] = 1
            set_export_colour(o, tint, "POINT")
        hull_objects = []
        if hulls == "auto":
            hull_objects.append(convex_hull(list(lod0.values()), "Body_hull0-convcolonly", self.out))
        elif hulls and hulls != "none":
            for i, (centre, size, rotation) in enumerate(hulls):
                hull_objects.append(box_hull(f"Body_hull{i}-convcolonly", centre, size, self.out,
                                             [math.radians(a) for a in rotation]))
        for part, o in lod0.items():
            chain = [o]
            for i, ratio in enumerate(lods):
                chain.append(decimated(o, f"{part}_LOD{i + 1}", ratio, self.out))
            objects += chain
            record = self.parts[part]
            pivot = record["pivot"]
            if part != "Body" or pivot.length > 0:
                empty = bpy.data.objects.new(part, None)
                self.out.objects.link(empty)
                empty.location = pivot
                for c in chain:
                    c.data.transform(Matrix.Translation(-pivot))
                    c.parent = empty
                objects.append(empty)
        objects += hull_objects + self.sockets
        return export_glb((self.folder / f"{name or self.key}.glb"), objects, mats)

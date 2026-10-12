"""Converting PSG kit assets (.blend sources in metres) into Hollow's GLB layout."""
import re

import bpy
from mathutils import Matrix, Vector

from . import log
from .export import (apply_matrix, collection, copy_object, export_colour_from_ao, export_glb, keep_uv,
                     select_only)

# Kit forward is +X. Held items keep it (Godot +X forward, +Y up, as the character package holds them); world
# objects turn to face Blender -Y (Godot +Z, the model front). Sockets keep the kit's frame: +X along the kit's forward.
TURN_HELD = Matrix.Identity(4)
TURN_WORLD = Matrix.Rotation(-1.5707963267948966, 4, "Z")


def snake(name):
    """HandR -> hand_r, PowerIn -> power_in, CodeLock -> code_lock."""
    s = re.sub(r"(?<=[a-z0-9])([A-Z])", r"_\1", name)
    return re.sub(r"_+", "_", s).lower()


class Conversion:
    """Collects one asset's export objects: LOD meshes per part, hulls, sockets."""

    def __init__(self, key):
        self.key = key
        self.out = collection("HOLLOW_EXPORT")
        self.parts = {}      # part name -> {"pivot": Vector, "lods": [objects], "hulls": [objects]}
        self.sockets = []    # empties
        self.extra = []      # other nodes (part empties)

    def add_part(self, part, pivot, lods, hulls):
        record = self.parts.setdefault(part, {"pivot": Vector(pivot), "lods": [], "hulls": []})
        for i, source in enumerate(lods):
            record["lods"].append(copy_object(source, f"{part}_lod{i}", self.out))
        for j, source in enumerate(hulls):
            hull = copy_object(source, f"{part}_hull{j}-convcolonly", self.out)
            hull.data.materials.clear()
            for uv in list(hull.data.uv_layers):
                hull.data.uv_layers.remove(uv)
            for a in list(hull.data.color_attributes):
                hull.data.color_attributes.remove(a)
            record["hulls"].append(hull)

    def add_socket(self, name, matrix):
        """An attach point at a world matrix (its +X forward, +Z up)."""
        e = bpy.data.objects.new(f"socket_{name}", None)
        e.empty_display_size = 0.03
        self.out.objects.link(e)
        e.matrix_world = matrix
        self.sockets.append(e)
        return e

    def objects(self):
        result = []
        for record in self.parts.values():
            result += record["lods"] + record["hulls"]
        return result + self.sockets + self.extra

    def finish(self, matrix, main="Body"):
        """Applies `matrix` (turn, scale, pivot move) to everything, bakes the main part to the origin and puts every
        other part under an empty at its pivot."""
        meshes = []
        for record in self.parts.values():
            meshes += record["lods"] + record["hulls"]
        apply_matrix(meshes + self.sockets, matrix)
        for part, record in self.parts.items():
            pivot = matrix @ record["pivot"]
            if part == main:
                select_only(record["lods"] + record["hulls"])
                bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)
                continue
            empty = bpy.data.objects.new(part, None)
            empty.empty_display_size = 0.05
            self.out.objects.link(empty)
            empty.location = pivot
            bpy.context.view_layer.update()
            for o in record["lods"] + record["hulls"]:
                world = o.matrix_world.copy()
                o.parent = empty
                o.matrix_parent_inverse = Matrix.Identity(4)
                o.matrix_world = world
            self.extra.append(empty)
        for record in self.parts.values():
            for o in record["lods"]:
                keep_uv(o, "UV0")
                export_colour_from_ao(o, "AO", 0.42)

    def export(self, path, materials, animations=False):
        return export_glb(path, self.objects(), materials, animations)


def world_bounds(objects):
    pts = [o.matrix_world @ Vector(c) for o in objects for c in o.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi


def objects_in(collection_name):
    col = bpy.data.collections.get(collection_name)
    return list(col.all_objects) if col else []


def log_part(key, part, lods, hulls):
    log(f"{key}: part {part}: LODs {[o.name for o in lods]}, {len(hulls)} hulls")

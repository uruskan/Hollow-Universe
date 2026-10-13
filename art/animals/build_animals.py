"""The animals of the ground game (GROUND.md §6.9): deer, boar, wolf and bear, our own, made from text in Blender 5.2.

    blender -b --factory-startup --python art/animals/build_animals.py -- [deer boar wolf bear] [--preview <png>]

Each species is an anatomy of metaball ellipsoids and capsules (the body, neck, head, legs, tail) polygonised into one
skin, smoothed and decimated into three levels of detail, with rigid parts beside it (eyes, ears, nose, hooves or claws,
antlers, tusks), skinned to a quadruped skeleton by distance to its bones, coloured by its coat (vertex colours over a
shared tiling fur texture), and given procedural clips: Idle, Graze, Alert, Threaten, Eat, Walk, Trot, Run, Attack,
Hurt and Death. Each locomotion clip is made for its species' speed (animals.json), so a view plays it at
speed / native speed and the feet keep up with the ground.

Writes game/assets/animals/<species>.glb (+ .import), <species>.json (clip facts) and the shared fur textures and
materials. The model faces Blender +Y (Godot -Z: an animal at yaw 0 faces north, as characters do), its feet on Z = 0.
"""
import json
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import bmesh  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Euler, Matrix, Quaternion, Vector, noise  # noqa: E402

from hollow import ASSETS, MATERIALS, REPO, log, script_args  # noqa: E402
from hollow.export import collection, export_glb, reset, select_only, set_export_colour  # noqa: E402
from hollow.godot import write_material  # noqa: E402
from hollow.textures import normal_from_height, write_png, write_texture_import  # noqa: E402

OUT = ASSETS / "animals"
TEXTURES = OUT / "textures"
FPS = 30
# A metaball element of radius r (threshold 0.6, stiffness 2) has its surface at 0.574 r.
SURFACE = 0.574
GLOSSY = "M_Animal_Glossy"

V = Vector


def mirror(p):
    return V((-p.x, p.y, p.z))


# ---------------------------------------------------------------- anatomy

class Anatomy:
    """One species: its joints, its metaball body, its rigid parts, its coat and its gaits."""

    def __init__(self, key):
        self.key = key
        self.joints = {}
        self.balls = []       # (kind, centre, size, rotation, surface radius)
        self.parts = []       # (bone, bmesh builder, material, colour)
        self.resolution = 0.015
        self.smooth = 4
        self.lods = (5200, 1900, 650)
        self.gaits = {}
        self.attack = "bite"
        self.speeds = {}

    def joint(self, name, x, y, z, side=None):
        p = V((x, y, z))
        if side is None:
            self.joints[name] = p
        else:
            self.joints[name + ".l"] = V((-abs(x), y, z))
            self.joints[name + ".r"] = V((abs(x), y, z))

    # Surface sizes in metres (what the skin will measure), turned into metaball radii.
    def ball(self, c, rho):
        self.balls.append(("BALL", V(c), None, None, rho))

    def ellipsoid(self, c, half, rot=(0, 0, 0)):
        m = max(half)
        self.balls.append(("ELLIPSOID", V(c), V(half) / m, Euler([math.radians(a) for a in rot]).to_quaternion(), m))

    def capsule(self, a, b, rho):
        a, b = V(a), V(b)
        d = b - a
        q = V((1, 0, 0)).rotation_difference(d.normalized()) if d.length > 1e-6 else Quaternion()
        self.balls.append(("CAPSULE", (a + b) / 2, d.length / 2, q, rho))

    def limb(self, names, radii, both=True):
        """Capsules along a chain of joints (a leg), on both sides."""
        for side in (".l", ".r") if both else ("",):
            for (a, b), r in zip(zip(names, names[1:]), radii):
                self.capsule(self.joints[a + side], self.joints[b + side], r)


# Shapes for rigid parts (in the animal's frame), each a bmesh.

def cone(base, tip, r0, r1=0.0, sides=10, flat=1.0):
    bm = bmesh.new()
    d = V(tip) - V(base)
    q = V((0, 0, 1)).rotation_difference(d.normalized())
    rings = []
    for z, r in ((0.0, r0), (1.0, r1)):
        if r <= 1e-5:
            rings.append([bm.verts.new(V(base) + d * z)])
            continue
        ring = []
        for i in range(sides):
            a = 2 * math.pi * i / sides
            local = V((r * math.cos(a), r * math.sin(a) * flat, 0))
            ring.append(bm.verts.new(V(base) + d * z + q @ local))
        rings.append(ring)
    a, b = rings
    if len(a) > 1 and len(b) > 1:
        for i in range(sides):
            bm.faces.new((a[i], a[(i + 1) % sides], b[(i + 1) % sides], b[i]))
        bm.faces.new(list(reversed(a)))
        bm.faces.new(b)
    elif len(b) == 1:
        for i in range(sides):
            bm.faces.new((a[i], a[(i + 1) % sides], b[0]))
        bm.faces.new(list(reversed(a)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def sphere(c, r, squash=(1, 1, 1), subdivisions=2):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdivisions, radius=1.0)
    for v in bm.verts:
        v.co = V((v.co.x * r * squash[0], v.co.y * r * squash[1], v.co.z * r * squash[2])) + V(c)
    return bm


def tube(points, radii, sides=8):
    bm = bmesh.new()
    pts = [V(p) for p in points]
    rings = []
    prev = None
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        if prev is None:
            ref = V((0, 0, 1)) if abs(t.z) < 0.9 else V((1, 0, 0))
            u = t.cross(ref).normalized()
        else:
            u = (prev - t * prev.dot(t)).normalized()
        w = t.cross(u)
        prev = u
        rings.append([bm.verts.new(p + radii[i] * (math.cos(a) * u + math.sin(a) * w))
                      for a in (2 * math.pi * j / sides for j in range(sides))])
    for a, b in zip(rings, rings[1:]):
        for j in range(sides):
            bm.faces.new((a[j], a[(j + 1) % sides], b[(j + 1) % sides], b[j]))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def ear(base, tip, width, cup=0.35):
    """A leaf-shaped ear: a thin cupped blade from base to tip."""
    bm = bmesh.new()
    base, tip = V(base), V(tip)
    d = tip - base
    side = d.cross(V((0, 1, 0)))
    if side.length < 1e-4:
        side = V((1, 0, 0))
    side.normalize()
    normal = side.cross(d).normalized()
    rows = []
    for k in range(6):
        t = k / 5
        w = width * math.sin(math.pi * min(1.0, t * 1.05 + 0.08)) * (1 - 0.15 * t)
        row = []
        for s in (-1, 0, 1):
            p = base + d * t + side * (s * w * 0.5) + normal * ((1 - abs(s)) * -cup * w * 0.4)
            row.append(bm.verts.new(p))
        rows.append(row)
    back = []
    for row in rows:
        back.append([bm.verts.new(v.co + normal * 0.006) for v in row])
    for a, b in zip(rows, rows[1:]):
        bm.faces.new((a[0], a[1], b[1], b[0]))
        bm.faces.new((a[1], a[2], b[2], b[1]))
    for a, b in zip(back, back[1:]):
        bm.faces.new((b[0], b[1], a[1], a[0]))
        bm.faces.new((b[1], b[2], a[2], a[1]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


# ---------------------------------------------------------------- the species

def deer():
    a = Anatomy("deer")
    a.speeds = {"Walk": 1.4, "Trot": 3.8, "Run": 10.5}
    a.gaits = {"Walk": (1.05, 0.62), "Trot": (0.62, 0.42), "Run": (0.52, 0.3)}
    J = a.joint
    J("hip", 0, -0.55, 1.0)
    J("spine", 0, -0.14, 1.02)
    J("chest", 0, 0.25, 1.03)
    J("neck", 0, 0.42, 1.12)
    J("head", 0, 0.6, 1.47)
    J("snout", 0, 0.86, 1.36)
    J("jaw", 0, 0.62, 1.4)
    J("chin", 0, 0.84, 1.31)
    J("tail0", 0, -0.8, 0.98)
    J("tail1", 0, -0.84, 0.93)
    J("tail2", 0, -0.86, 0.88)
    J("tail3", 0, -0.87, 0.84)
    J("fs", 0.11, 0.3, 0.95, side=True)
    J("fe", 0.11, 0.24, 0.66, side=True)
    J("fw", 0.105, 0.27, 0.36, side=True)
    J("ff", 0.1, 0.29, 0.09, side=True)
    J("ft", 0.1, 0.34, 0.0, side=True)
    J("hh", 0.11, -0.55, 0.93, side=True)
    J("hk", 0.115, -0.43, 0.63, side=True)
    J("ha", 0.105, -0.62, 0.42, side=True)
    J("hf", 0.1, -0.58, 0.09, side=True)
    J("ht", 0.1, -0.54, 0.0, side=True)
    J("ear0", 0.055, 0.58, 1.56, side=True)
    J("ear1", 0.19, 0.53, 1.63, side=True)
    # The body: haunch, barrel, chest; a long neck; a narrow head and muzzle.
    a.ellipsoid((0, -0.5, 0.93), (0.15, 0.22, 0.19))
    a.ellipsoid((0, -0.08, 0.88), (0.17, 0.36, 0.2))
    a.ellipsoid((0, 0.27, 0.9), (0.15, 0.2, 0.23))
    a.capsule((0, -0.58, 1.0), (0, 0.24, 1.04), 0.11)
    a.capsule((0, 0.32, 1.02), (0, 0.55, 1.42), 0.085)
    a.ball((0, 0.36, 1.03), 0.13)
    a.ellipsoid((0, 0.63, 1.46), (0.07, 0.1, 0.08), rot=(-25, 0, 0))
    a.capsule((0, 0.68, 1.42), (0, 0.85, 1.35), 0.047)
    a.capsule((0, 0.66, 1.37), (0, 0.82, 1.31), 0.035)
    # Legs: muscle up top, slender cannons below.
    a.limb(["fs", "fe", "fw", "ff", "ft"], [0.07, 0.042, 0.026, 0.022])
    a.limb(["hh", "hk", "ha", "hf", "ht"], [0.1, 0.055, 0.028, 0.023])
    a.capsule((0, -0.8, 0.98), (0, -0.86, 0.88), 0.032)
    # Rigid parts.
    for s, sx in ((".l", -1), (".r", 1)):
        a.parts.append(("ear" + s, ear(a.joints["ear0" + s], a.joints["ear1" + s], 0.085), None, "ear"))
        a.parts.append(("head", sphere((sx * 0.062, 0.68, 1.47), 0.016), GLOSSY, "eye"))
        a.parts.append(("front_toe" + s, cone(a.joints["ff" + s] + V((0, 0.0, 0.03)), a.joints["ft" + s] + V((0, 0.01, 0.0)), 0.03, 0.022, 8), None, "hoof"))
        a.parts.append(("hind_toe" + s, cone(a.joints["hf" + s] + V((0, 0.0, 0.03)), a.joints["ht" + s] + V((0, 0.01, 0.0)), 0.03, 0.022, 8), None, "hoof"))
        # Antlers: a main beam curving up, out and forward, with tines.
        root = V((sx * 0.045, 0.6, 1.55))
        beam = [root, root + V((sx * 0.06, -0.05, 0.12)), root + V((sx * 0.14, -0.08, 0.27)), root + V((sx * 0.2, -0.02, 0.42)),
                root + V((sx * 0.21, 0.08, 0.53)), root + V((sx * 0.18, 0.17, 0.58))]
        a.parts.append(("head", tube(beam, [0.022, 0.019, 0.016, 0.013, 0.01, 0.005], 7), None, "antler"))
        for k, (i, d) in enumerate(((1, V((0, 0.13, 0.05))), (2, V((sx * 0.02, 0.14, 0.1))), (3, V((-sx * 0.02, 0.1, 0.13))))):
            p = beam[i]
            a.parts.append(("head", tube([p, p + d * 0.5 + V((0, 0, 0.02)), p + d], [0.012, 0.009, 0.003], 6), None, "antler"))
    a.parts.append(("head", sphere((0, 0.865, 1.355), 0.024, (1.1, 0.6, 0.8)), GLOSSY, "nose"))
    return a


def wolf():
    a = Anatomy("wolf")
    a.speeds = {"Walk": 1.5, "Trot": 3.8, "Run": 9.2}
    a.gaits = {"Walk": (0.9, 0.6), "Trot": (0.5, 0.42), "Run": (0.42, 0.3)}
    a.resolution = 0.012
    J = a.joint
    J("hip", 0, -0.47, 0.74)
    J("spine", 0, -0.12, 0.76)
    J("chest", 0, 0.22, 0.77)
    J("neck", 0, 0.34, 0.8)
    J("head", 0, 0.54, 0.88)
    J("snout", 0, 0.77, 0.83)
    J("jaw", 0, 0.55, 0.84)
    J("chin", 0, 0.73, 0.79)
    J("tail0", 0, -0.6, 0.73)
    J("tail1", 0, -0.75, 0.63)
    J("tail2", 0, -0.86, 0.5)
    J("tail3", 0, -0.91, 0.38)
    J("fs", 0.085, 0.26, 0.66, side=True)
    J("fe", 0.085, 0.19, 0.44, side=True)
    J("fw", 0.08, 0.21, 0.19, side=True)
    J("ff", 0.075, 0.23, 0.05, side=True)
    J("ft", 0.075, 0.3, 0.0, side=True)
    J("hh", 0.085, -0.47, 0.68, side=True)
    J("hk", 0.09, -0.37, 0.45, side=True)
    J("ha", 0.08, -0.53, 0.24, side=True)
    J("hf", 0.075, -0.5, 0.05, side=True)
    J("ht", 0.075, -0.44, 0.0, side=True)
    J("ear0", 0.045, 0.53, 0.97, side=True)
    J("ear1", 0.065, 0.5, 1.08, side=True)
    a.ellipsoid((0, -0.44, 0.69), (0.11, 0.16, 0.14))
    a.ellipsoid((0, -0.1, 0.66), (0.115, 0.28, 0.12))
    a.ellipsoid((0, 0.21, 0.63), (0.14, 0.2, 0.21))
    a.capsule((0, -0.5, 0.74), (0, 0.22, 0.77), 0.1)
    a.ellipsoid((0, 0.36, 0.79), (0.15, 0.14, 0.17))      # the ruff
    a.capsule((0, 0.3, 0.78), (0, 0.5, 0.88), 0.11)
    a.ellipsoid((0, 0.55, 0.9), (0.1, 0.12, 0.1))
    a.capsule((0, 0.62, 0.88), (0, 0.76, 0.83), 0.046)
    a.capsule((0, 0.6, 0.84), (0, 0.73, 0.8), 0.034)
    a.limb(["fs", "fe", "fw", "ff", "ft"], [0.058, 0.037, 0.027, 0.024])
    a.limb(["hh", "hk", "ha", "hf", "ht"], [0.085, 0.046, 0.029, 0.025])
    # A bushy tail hanging behind.
    a.capsule((0, -0.58, 0.73), (0, -0.74, 0.63), 0.045)
    a.capsule((0, -0.74, 0.63), (0, -0.86, 0.5), 0.06)
    a.capsule((0, -0.86, 0.5), (0, -0.91, 0.38), 0.05)
    for s, sx in ((".l", -1), (".r", 1)):
        a.parts.append(("ear" + s, ear(a.joints["ear0" + s], a.joints["ear1" + s], 0.07, cup=0.5), None, "ear"))
        a.parts.append(("head", sphere((sx * 0.058, 0.62, 0.92), 0.013), GLOSSY, "eye"))
        for leg, joints in (("front_toe", ("ff", "ft")), ("hind_toe", ("hf", "ht"))):
            a.parts.append((leg + s, sphere(a.joints[joints[0] + s] * 0.4 + a.joints[joints[1] + s] * 0.6 + V((0, 0, 0.018)), 0.034, (0.85, 1.25, 0.55)),
                            None, "paw"))
    a.parts.append(("head", sphere((0, 0.775, 0.835), 0.022, (1.2, 0.8, 0.8)), GLOSSY, "nose"))
    return a


def boar():
    a = Anatomy("boar")
    a.speeds = {"Walk": 1.2, "Trot": 3.2, "Run": 6.6}
    a.gaits = {"Walk": (0.8, 0.62), "Trot": (0.46, 0.42), "Run": (0.38, 0.32)}
    a.attack = "gore"
    J = a.joint
    J("hip", 0, -0.48, 0.74)
    J("spine", 0, -0.1, 0.82)
    J("chest", 0, 0.26, 0.86)
    J("neck", 0, 0.4, 0.74)
    J("head", 0, 0.58, 0.58)
    J("snout", 0, 0.86, 0.37)
    J("jaw", 0, 0.6, 0.5)
    J("chin", 0, 0.8, 0.35)
    J("tail0", 0, -0.68, 0.72)
    J("tail1", 0, -0.74, 0.62)
    J("tail2", 0, -0.76, 0.52)
    J("tail3", 0, -0.77, 0.44)
    J("fs", 0.12, 0.28, 0.5, side=True)
    J("fe", 0.12, 0.24, 0.33, side=True)
    J("fw", 0.115, 0.27, 0.15, side=True)
    J("ff", 0.11, 0.28, 0.04, side=True)
    J("ft", 0.11, 0.32, 0.0, side=True)
    J("hh", 0.12, -0.48, 0.56, side=True)
    J("hk", 0.12, -0.4, 0.36, side=True)
    J("ha", 0.115, -0.5, 0.18, side=True)
    J("hf", 0.11, -0.48, 0.04, side=True)
    J("ht", 0.11, -0.44, 0.0, side=True)
    J("ear0", 0.07, 0.55, 0.69, side=True)
    J("ear1", 0.14, 0.49, 0.79, side=True)
    # Heavy in front, a sloping back, short legs; a wedge of a head ending in a flat snout.
    a.ellipsoid((0, 0.22, 0.6), (0.22, 0.29, 0.29))
    a.ellipsoid((0, -0.12, 0.56), (0.23, 0.34, 0.25))
    a.ellipsoid((0, -0.45, 0.6), (0.19, 0.21, 0.22))
    a.ellipsoid((0, 0.05, 0.8), (0.05, 0.4, 0.06), rot=(-6, 0, 0))   # the bristle ridge
    a.capsule((0, 0.36, 0.68), (0, 0.56, 0.58), 0.19)
    a.ellipsoid((0, 0.6, 0.55), (0.12, 0.16, 0.14), rot=(-30, 0, 0))
    a.capsule((0, 0.66, 0.5), (0, 0.84, 0.38), 0.06)
    a.limb(["fs", "fe", "fw", "ff", "ft"], [0.07, 0.045, 0.03, 0.026])
    a.limb(["hh", "hk", "ha", "hf", "ht"], [0.1, 0.05, 0.032, 0.026])
    a.capsule((0, -0.66, 0.72), (0, -0.76, 0.5), 0.016)
    for s, sx in ((".l", -1), (".r", 1)):
        a.parts.append(("ear" + s, ear(a.joints["ear0" + s], a.joints["ear1" + s], 0.08, cup=0.3), None, "ear"))
        a.parts.append(("head", sphere((sx * 0.085, 0.64, 0.6), 0.013), GLOSSY, "eye"))
        a.parts.append(("front_toe" + s, cone(a.joints["ff" + s] + V((0, 0, 0.025)), a.joints["ft" + s], 0.03, 0.024, 8), None, "hoof"))
        a.parts.append(("hind_toe" + s, cone(a.joints["hf" + s] + V((0, 0, 0.025)), a.joints["ht" + s], 0.03, 0.024, 8), None, "hoof"))
        # Tusks curving up and out of the lower jaw.
        t0 = V((sx * 0.045, 0.79, 0.37))
        a.parts.append(("jaw", tube([t0, t0 + V((sx * 0.025, 0.02, 0.05)), t0 + V((sx * 0.05, 0.0, 0.09))], [0.011, 0.008, 0.002], 6), None, "tusk"))
    a.parts.append(("head", cone((0, 0.83, 0.39), (0, 0.875, 0.365), 0.058, 0.052, 14, flat=0.8), None, "snout"))
    a.parts.append(("hips", tube([V((0, -0.76, 0.46)), V((0, -0.765, 0.4))], [0.02, 0.006], 6), None, "tuft"))
    return a


def bear():
    a = Anatomy("bear")
    a.speeds = {"Walk": 1.3, "Trot": 3.2, "Run": 8.2}
    a.gaits = {"Walk": (1.25, 0.64), "Trot": (0.72, 0.44), "Run": (0.56, 0.32)}
    a.resolution = 0.02
    a.attack = "swipe"
    J = a.joint
    J("hip", 0, -0.7, 0.98)
    J("spine", 0, -0.2, 1.06)
    J("chest", 0, 0.32, 1.12)
    J("neck", 0, 0.55, 1.04)
    J("head", 0, 0.8, 0.96)
    J("snout", 0, 1.08, 0.85)
    J("jaw", 0, 0.82, 0.88)
    J("chin", 0, 1.04, 0.79)
    J("tail0", 0, -1.0, 0.95)
    J("tail1", 0, -1.04, 0.92)
    J("tail2", 0, -1.06, 0.88)
    J("tail3", 0, -1.07, 0.85)
    J("fs", 0.2, 0.38, 0.8, side=True)
    J("fe", 0.2, 0.3, 0.5, side=True)
    J("fw", 0.19, 0.35, 0.16, side=True)
    J("ff", 0.19, 0.4, 0.05, side=True)
    J("ft", 0.19, 0.52, 0.0, side=True)
    J("hh", 0.2, -0.7, 0.84, side=True)
    J("hk", 0.2, -0.55, 0.5, side=True)
    J("ha", 0.19, -0.74, 0.17, side=True)
    J("hf", 0.19, -0.64, 0.05, side=True)
    J("ht", 0.19, -0.5, 0.0, side=True)
    J("ear0", 0.12, 0.76, 1.1, side=True)
    J("ear1", 0.16, 0.74, 1.17, side=True)
    # Massive: a shoulder hump, a deep barrel, heavy haunches, a broad head with a long muzzle.
    a.ellipsoid((0, 0.3, 0.86), (0.33, 0.4, 0.38))
    a.ellipsoid((0, -0.15, 0.83), (0.35, 0.48, 0.36))
    a.ellipsoid((0, -0.62, 0.86), (0.3, 0.3, 0.34))
    a.ball((0, 0.28, 1.12), 0.23)
    a.capsule((0, 0.48, 1.0), (0, 0.76, 0.96), 0.22)
    a.ellipsoid((0, 0.82, 0.95), (0.17, 0.19, 0.17))
    a.capsule((0, 0.92, 0.9), (0, 1.05, 0.85), 0.085)
    a.limb(["fs", "fe", "fw", "ff", "ft"], [0.15, 0.115, 0.095, 0.085])
    a.limb(["hh", "hk", "ha", "hf", "ht"], [0.19, 0.12, 0.09, 0.085])
    a.capsule((0, -0.98, 0.95), (0, -1.06, 0.87), 0.045)
    for s, sx in ((".l", -1), (".r", 1)):
        a.parts.append(("ear" + s, sphere(a.joints["ear0" + s] * 0.4 + a.joints["ear1" + s] * 0.6, 0.05, (0.9, 0.45, 0.85)), None, "ear"))
        a.parts.append(("head", sphere((sx * 0.1, 0.92, 0.99), 0.015), GLOSSY, "eye"))
        for leg, joints in (("front_toe", ("ff", "ft")), ("hind_toe", ("hf", "ht"))):
            tip = a.joints[joints[1] + s]
            for k in (-2, -1, 0, 1, 2):
                c0 = tip + V((sx * 0.0 + k * 0.03, -0.03, 0.035))
                a.parts.append((leg + s, cone(c0, c0 + V((0, 0.06, -0.03)), 0.012, 0.0, 6), None, "claw"))
    a.parts.append(("head", sphere((0, 1.085, 0.86), 0.035, (1.2, 0.7, 0.8)), GLOSSY, "nose"))
    return a


SPECIES = {"deer": deer, "boar": boar, "wolf": wolf, "bear": bear}

# ---------------------------------------------------------------- coats

COATS = {
    "deer": dict(back=(0.42, 0.24, 0.12), side=(0.55, 0.33, 0.17), belly=(0.86, 0.8, 0.7), leg=(0.36, 0.24, 0.14),
                 muzzle=(0.18, 0.13, 0.1), rump=(0.9, 0.86, 0.78), mottle=0.12),
    "wolf": dict(back=(0.24, 0.22, 0.2), side=(0.52, 0.47, 0.4), belly=(0.82, 0.78, 0.7), leg=(0.62, 0.55, 0.45),
                 muzzle=(0.78, 0.72, 0.62), rump=(0.45, 0.4, 0.34), mottle=0.18),
    "boar": dict(back=(0.12, 0.1, 0.085), side=(0.25, 0.2, 0.16), belly=(0.33, 0.27, 0.22), leg=(0.12, 0.1, 0.085),
                 muzzle=(0.2, 0.15, 0.12), rump=(0.22, 0.18, 0.14), mottle=0.22),
    "bear": dict(back=(0.2, 0.12, 0.07), side=(0.3, 0.19, 0.11), belly=(0.26, 0.17, 0.1), leg=(0.14, 0.09, 0.06),
                 muzzle=(0.42, 0.32, 0.22), rump=(0.27, 0.17, 0.1), mottle=0.15),
}

PART_COLOURS = {
    "eye": (0.02, 0.015, 0.012), "nose": (0.03, 0.025, 0.022), "hoof": (0.07, 0.06, 0.05), "paw": None, "claw": (0.1, 0.09, 0.08),
    "antler": (0.62, 0.52, 0.38), "tusk": (0.9, 0.86, 0.74), "snout": (0.55, 0.36, 0.33), "ear": None, "tuft": (0.06, 0.05, 0.045),
}


def coat(a, p, n):
    """The coat's colour at a rest-pose point: darker along the back, paler beneath, the legs, muzzle and rump patches."""
    c = COATS[a.key]
    j = a.joints
    height = (p.z - 0.0) / max(j["chest"].z, 0.1)
    up = n.z
    # Back to belly by the normal's tilt, with a soft edge.
    t = max(0.0, min(1.0, (up + 0.15) / 0.9))
    col = [c["side"][i] + (c["back"][i] - c["side"][i]) * t * t for i in range(3)]
    belly = max(0.0, min(1.0, (-up - 0.35) / 0.4))
    col = [col[i] + (c["belly"][i] - col[i]) * belly for i in range(3)]
    # Legs below the body.
    leg = max(0.0, min(1.0, (j["fw.l"].z + 0.25 - p.z) / 0.25)) if abs(p.x) > 0.04 else 0.0
    col = [col[i] + (c["leg"][i] - col[i]) * leg for i in range(3)]
    # Muzzle beyond the eyes.
    muzzle = max(0.0, min(1.0, (p.y - (j["head"].y + 0.05)) / 0.08)) if p.z > j["hip"].z * 0.6 else 0.0
    col = [col[i] + (c["muzzle"][i] - col[i]) * muzzle for i in range(3)]
    # Rump patch / tail.
    rump = max(0.0, min(1.0, (j["hip"].y - 0.18 - p.y) / 0.12)) * max(0.0, min(1.0, (p.z - j["hip"].z * 0.7) / 0.2))
    col = [col[i] + (c["rump"][i] - col[i]) * rump for i in range(3)]
    k = 1 + c["mottle"] * noise.noise(p * 9 + V((3.1, 7.7, 1.3)), noise_basis="PERLIN_ORIGINAL")
    k *= 1 + 0.06 * noise.noise(p * 40, noise_basis="PERLIN_ORIGINAL")
    return [max(0.0, x * k) for x in col]


# ---------------------------------------------------------------- the skin

def polygonise(a):
    mb = bpy.data.metaballs.new(a.key + "_body")
    mb.resolution = a.resolution
    mb.render_resolution = a.resolution
    mb.threshold = 0.6
    for kind, c, size, rot, rho in a.balls:
        el = mb.elements.new(type=kind)
        el.co = c
        el.stiffness = 2.0
        if kind == "BALL":
            el.radius = rho / SURFACE
        elif kind == "ELLIPSOID":
            el.radius = rho / SURFACE
            el.size_x, el.size_y, el.size_z = size
            el.rotation = rot
        else:
            el.radius = rho / SURFACE
            el.size_x = size
            el.rotation = rot
    o = bpy.data.objects.new(a.key + "_body", mb)
    bpy.context.scene.collection.objects.link(o)
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(o.evaluated_get(dg))
    bpy.data.objects.remove(o)
    body = bpy.data.objects.new(a.key + "_skin", me)
    collection("HOLLOW_WORK").objects.link(body)
    # Smooth the polygoniser's facets, merge slivers.
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=a.resolution * 0.3)
    for _ in range(a.smooth):
        bmesh.ops.smooth_vert(bm, verts=bm.verts, factor=0.5, use_axis_x=True, use_axis_y=True, use_axis_z=True)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = True
    log(f"{a.key}: skin {len(me.vertices)} vertices")
    return body


def add_parts(a, body):
    """The rigid parts, joined into the skin, each in its own vertex group (its bone) and colour."""
    objs = []
    for i, (bone, bm, material, kind) in enumerate(a.parts):
        me = bpy.data.meshes.new(f"{a.key}_part{i}")
        bm.to_mesh(me)
        bm.free()
        for p in me.polygons:
            p.use_smooth = kind not in ("hoof", "claw")
        o = bpy.data.objects.new(me.name, me)
        collection("HOLLOW_WORK").objects.link(o)
        o["bone"] = bone
        o["kind"] = kind
        o["glossy"] = material == GLOSSY
        objs.append(o)
    return objs


# ---------------------------------------------------------------- skeleton

def bones_of(a):
    """(name, head, tail, parent, roll towards) for every bone."""
    j = a.joints
    out = [("root", V((0, 0, 0)), V((0, 0.15, 0)), None, "up"),
           ("hips", j["hip"], j["spine"], "root", "up"),
           ("spine", j["spine"], j["chest"], "hips", "up"),
           ("chest", j["chest"], j["neck"], "spine", "up"),
           ("neck", j["neck"], j["head"], "chest", "up"),
           ("head", j["head"], j["snout"], "neck", "up"),
           ("jaw", j["jaw"], j["chin"], "head", "up"),
           ("tail.1", j["tail0"], j["tail1"], "hips", "up"),
           ("tail.2", j["tail1"], j["tail2"], "tail.1", "up"),
           ("tail.3", j["tail2"], j["tail3"], "tail.2", "up")]
    for s in (".l", ".r"):
        out += [("ear" + s, j["ear0" + s], j["ear1" + s], "head", "forward"),
                ("front_upper" + s, j["fs" + s], j["fe" + s], "chest", "forward"),
                ("front_lower" + s, j["fe" + s], j["fw" + s], "front_upper" + s, "forward"),
                ("front_foot" + s, j["fw" + s], j["ff" + s], "front_lower" + s, "forward"),
                ("front_toe" + s, j["ff" + s], j["ft" + s], "front_foot" + s, "forward"),
                ("hind_upper" + s, j["hh" + s], j["hk" + s], "hips", "forward"),
                ("hind_lower" + s, j["hk" + s], j["ha" + s], "hind_upper" + s, "forward"),
                ("hind_foot" + s, j["ha" + s], j["hf" + s], "hind_lower" + s, "forward"),
                ("hind_toe" + s, j["hf" + s], j["ht" + s], "hind_foot" + s, "forward")]
    return out


def build_rig(a):
    arm = bpy.data.armatures.new(a.key + "_rig")
    rig = bpy.data.objects.new("Armature", arm)
    collection("HOLLOW_EXPORT").objects.link(rig)
    select_only([rig])
    bpy.ops.object.mode_set(mode="EDIT")
    made = {}
    for name, head, tail, parent, roll in bones_of(a):
        b = arm.edit_bones.new(name)
        b.head = head
        b.tail = tail
        # Every bone's local X is the animal's left-right axis, so a turn about it bends in the body's plane.
        b.align_roll(V((0, 0, 1)) if roll == "up" else V((0, 1, 0)))
        b.use_deform = name != "root"
        if parent:
            b.parent = made[parent]
        made[name] = b
    bpy.ops.object.mode_set(mode="OBJECT")
    return rig


def deform_bones(a):
    return [(n, h, t) for n, h, t, _, _ in bones_of(a) if n != "root"]


def skin_weights(a, body):
    """Weights by distance to the bones (inverse fourth power, the nearest four), the legs kept to their own side."""
    bones = deform_bones(a)
    names = [b[0] for b in bones]
    groups = {n: body.vertex_groups.new(name=n) for n in names}
    heads = np.array([list(h) for _, h, _ in bones])
    tails = np.array([list(t) for _, _, t in bones])
    seg = tails - heads
    seg_len2 = np.maximum((seg ** 2).sum(axis=1), 1e-9)
    side = np.array([1.0 if n.endswith(".r") else -1.0 if n.endswith(".l") else 0.0 for n in names])
    is_leg = np.array([("front_" in n or "hind_" in n) for n in names])
    is_ear = np.array([n.startswith("ear") for n in names])
    is_jaw = np.array([n == "jaw" for n in names])
    verts = np.array([list(v.co) for v in body.data.vertices])
    for vi, p in enumerate(verts):
        t = np.clip(((p - heads) * seg).sum(axis=1) / seg_len2, 0, 1)
        closest = heads + seg * t[:, None]
        d = np.sqrt(((p - closest) ** 2).sum(axis=1))
        # Legs only take vertices on their own side, below the body's middle.
        d = np.where(is_leg & (side * p[0] < -0.01), 1e3, d)
        d = np.where(is_ear | is_jaw, d * 1.6 + 0.02, d)
        order = np.argsort(d)[:4]
        nearest = d[order[0]]
        w = 1.0 / np.maximum(d[order], 1e-4) ** 4
        w = np.where(d[order] < nearest * 1.8 + 0.015, w, 0.0)
        w /= w.sum()
        for k, bi in enumerate(order):
            if w[k] > 0.01:
                groups[names[bi]].add([vi], float(w[k]), "REPLACE")


def rigid_weights(part):
    group = part.vertex_groups.new(name=part["bone"])
    group.add(list(range(len(part.data.vertices))), 1.0, "REPLACE")


# ---------------------------------------------------------------- UVs and colour

def uv_box(me, metres=0.35):
    """Box-projected UVs (the tiling fur), grain along the animal's length."""
    uv = me.uv_layers.new(name="UVMap")
    for poly in me.polygons:
        axis = max(range(3), key=lambda k: abs(poly.normal[k]))
        for li in poly.loop_indices:
            c = me.vertices[me.loops[li].vertex_index].co
            if axis == 0:
                u, v = c.y, c.z
            elif axis == 1:
                u, v = c.x, c.z
            else:
                u, v = c.x, c.y
            uv.data[li].uv = (u / metres, v / metres)


def colour_skin(a, body):
    me = body.data
    rgba = np.ones((len(me.vertices), 4), np.float32)
    for i, v in enumerate(me.vertices):
        rgba[i, :3] = coat(a, v.co, v.normal)
    set_export_colour(body, rgba, "POINT")


def colour_part(a, part):
    me = part.data
    kind = part["kind"]
    fixed = PART_COLOURS.get(kind)
    rgba = np.ones((len(me.vertices), 4), np.float32)
    for i, v in enumerate(me.vertices):
        rgba[i, :3] = fixed if fixed else coat(a, v.co, v.normal)
        if kind == "ear":
            # Darker rims and tips.
            rgba[i, :3] *= 0.75
    set_export_colour(part, rgba, "POINT")


# ---------------------------------------------------------------- textures and materials

def fur_textures():
    """A tiling fur detail (strands running along v) as albedo and normal map, shared by every species."""
    albedo, normal = TEXTURES / "T_Fur_Detail.png", TEXTURES / "T_Fur_NormalGL.png"
    size = 512
    rng = np.random.default_rng(1)
    h = rng.random((size, size)).astype(np.float32)
    # Strands: blurred along v (rows), barely across.
    for _ in range(4):
        h = (np.roll(h, 1, 0) + np.roll(h, -1, 0) + np.roll(h, 2, 0) + np.roll(h, -2, 0) + h) / 5
    h = (h * 2 + np.roll(h, 1, 1) * 0.5 + np.roll(h, -1, 1) * 0.5) / 3
    h = (h - h.min()) / (h.max() - h.min())
    tone = 0.78 + 0.32 * h
    pixels = np.ones((size, size, 3), np.float32)
    pixels[...] = tone[..., None]
    write_png(albedo, pixels)
    write_png(normal, normal_from_height(h, 6.0))
    write_texture_import(albedo)
    write_texture_import(normal, normal_map=True)
    return albedo, normal


def materials(albedo, normal):
    fur = write_material("animal_fur", "M_Animal_Fur", albedo=albedo, normal=normal, roughness=0.88, metallic=0.0,
                         extra=["rim_enabled = true", "rim = 0.35", "rim_tint = 0.65", "normal_scale = 0.6"])
    glossy = write_material("animal_glossy", GLOSSY, roughness=0.18, metallic=0.0)
    return {"M_Animal_Fur": fur, GLOSSY: glossy}


# ---------------------------------------------------------------- animation

def smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


class Clip:
    def __init__(self, name, seconds, loop=True, native=None):
        self.name = name
        self.seconds = seconds
        self.loop = loop
        self.native = native
        self.frames = max(2, int(round(seconds * FPS)))


def leg_cycle(phase, duty, sweep, lift):
    """Swing (radians, + forward) and flex (0 … 1) of a leg at a phase of its stride."""
    phase %= 1.0
    if phase < duty:
        s = phase / duty
        return sweep * (1 - 2 * s), 0.0
    s = (phase - duty) / (1 - duty)
    return sweep * (-1 + 2 * smooth(s)), lift * math.sin(math.pi * s)


def gait_pose(a, clip, t, kind):
    """Rotations (radians about each bone's local X, Y, Z) and the hips' offset at time t of a gait."""
    period, duty = a.gaits[clip]
    speed = a.speeds[clip]
    phase = t / period
    j = a.joints
    leg_len = (j["fs.l"] - j["ft.l"]).length
    stance = speed * period * duty
    sweep = min(0.75, math.atan2(stance / 2, leg_len))
    lift = {"Walk": 0.55, "Trot": 0.8, "Run": 1.0}[clip]
    if clip == "Walk":
        offsets = {"hind.l": 0.0, "front.l": 0.25, "hind.r": 0.5, "front.r": 0.75}
    elif clip == "Trot":
        offsets = {"hind.l": 0.0, "front.r": 0.02, "hind.r": 0.5, "front.l": 0.52}
    else:
        offsets = {"hind.l": 0.0, "hind.r": 0.1, "front.l": 0.5, "front.r": 0.6}
    pose = {}
    for leg, off in offsets.items():
        swing, flex = leg_cycle(phase + off, duty, sweep, lift)
        end, s = leg.split(".")
        s = "." + s
        if end == "front":
            pose["front_upper" + s] = (swing + 0.15 * flex, 0, 0)
            pose["front_lower" + s] = (-0.9 * flex, 0, 0)
            pose["front_foot" + s] = (-0.9 * flex, 0, 0)
            pose["front_toe" + s] = (0.35 * flex, 0, 0)
        else:
            pose["hind_upper" + s] = (swing + 0.45 * flex, 0, 0)
            pose["hind_lower" + s] = (-1.0 * flex, 0, 0)
            pose["hind_foot" + s] = (0.85 * flex, 0, 0)
            pose["hind_toe" + s] = (-0.3 * flex, 0, 0)
    w = 2 * math.pi * phase
    if clip == "Run":
        flexion = math.sin(w + 0.6)
        pose["hips"] = (0.08 * flexion, 0, 0)
        pose["spine"] = (-0.1 * flexion, 0, 0)
        pose["chest"] = (0.06 * flexion, 0, 0)
        pose["neck"] = (-0.12 - 0.08 * math.sin(w + 1.4), 0, 0)
        pose["head"] = (0.1 + 0.06 * math.sin(w + 1.4), 0, 0)
        lift_body = 0.05 * (0.5 + 0.5 * math.cos(w))
        pose["tail.1"] = (0.15 + 0.1 * math.sin(w), 0, 0)
    else:
        bob = {"Walk": 0.012, "Trot": 0.025}[clip]
        lift_body = bob * math.cos(2 * w)
        pose["spine"] = (0, 0, 0.03 * math.sin(w))
        pose["neck"] = ({"Walk": -0.05, "Trot": -0.12}[clip] + 0.03 * math.sin(2 * w), 0, 0.02 * math.sin(w))
        pose["head"] = (0.04 * math.sin(2 * w + 0.5), 0, 0)
        pose["tail.1"] = (0.05, 0, 0.12 * math.sin(w))
    pose["tail.2"] = (0.05 * math.sin(w), 0, 0.1 * math.sin(w - 0.5))
    pose["_hips_offset"] = V((0, 0, lift_body - (0.02 if clip == "Run" else 0)))
    return pose


def idle_pose(a, clip, t):
    w = 2 * math.pi * t / clip.seconds
    breathe = math.sin(w * 2)
    pose = {"spine": (0.01 * breathe, 0, 0), "chest": (0.008 * breathe, 0, 0)}
    if clip.name == "Idle":
        look = math.sin(w) * 0.35
        pose["neck"] = (0.02 * math.sin(w * 2), 0, 0.15 * look)
        pose["head"] = (0.03 * math.sin(w * 3), 0, 0.2 * look)
        pose["ear.l"] = (0, 0, 0.3 * max(0.0, math.sin(w * 4 + 1)) ** 8)
        pose["ear.r"] = (0, 0, -0.3 * max(0.0, math.sin(w * 3 + 2)) ** 8)
        pose["tail.1"] = (0.05, 0, 0.25 * math.sin(w * 2))
        pose["tail.2"] = (0, 0, 0.2 * math.sin(w * 2 - 0.6))
    elif clip.name in ("Graze", "Eat"):
        # Head down to the ground; a deer reaches, a wolf or bear tears at a kill.
        reach = {"deer": 1.15, "boar": 0.45, "wolf": 0.8, "bear": 0.55}[a.key]
        chew = math.sin(w * (6 if clip.name == "Graze" else 4))
        pose["neck"] = (-reach, 0, 0.08 * math.sin(w))
        pose["head"] = (-0.25 + 0.06 * chew, 0, 0.05 * math.sin(w * 2))
        pose["jaw"] = (-0.12 * max(0.0, chew), 0, 0)
        pose["chest"] = (-0.06, 0, 0)
        pose["front_upper.l"] = (0.06, 0, 0)
        pose["front_upper.r"] = (0.06, 0, 0)
        if clip.name == "Eat":
            pose["neck"] = (-reach + 0.12 * max(0.0, math.sin(w * 2)), 0, 0)
            pose["head"] = (-0.3 + 0.15 * max(0.0, math.sin(w * 2 + 1)), 0, 0.15 * math.sin(w * 2))
        pose["tail.1"] = (0.02, 0, 0.2 * math.sin(w * 2))
    elif clip.name == "Alert":
        # Head up and still, ears forward, a twitch.
        pose["neck"] = (0.18, 0, 0.03 * math.sin(w))
        pose["head"] = (-0.12, 0, 0.04 * math.sin(w * 2))
        pose["ear.l"] = (-0.25, 0, 0.1)
        pose["ear.r"] = (-0.25, 0, -0.1)
        pose["tail.1"] = (0.3, 0, 0)
    elif clip.name == "Threaten":
        # A fighter's warning: head low and forward, jaw open (a growl, a snort, a roar), weight on the front.
        pose["neck"] = (-0.25 + 0.04 * math.sin(w * 3), 0, 0)
        pose["head"] = (0.1, 0, 0.05 * math.sin(w * 2))
        pose["jaw"] = (-0.35 - 0.1 * math.sin(w * 6), 0, 0)
        pose["chest"] = (-0.05, 0, 0)
        pose["ear.l"] = (0.4, 0, 0.3)
        pose["ear.r"] = (0.4, 0, -0.3)
        pose["front_upper.l"] = (0.12, 0, 0)
        pose["front_upper.r"] = (-0.05, 0, 0)
        pose["tail.1"] = (-0.1, 0, 0)
        pose["_hips_offset"] = V((0, -0.02, -0.03))
        if a.key == "bear":
            # Rears up on its hind legs and roars.
            up = smooth(min(1.0, t / 0.5)) * (1 - smooth((t - clip.seconds + 0.5) / 0.5))
            pose["hips"] = (0.8 * up, 0, 0)
            pose["hind_upper.l"] = (-0.6 * up, 0, 0)
            pose["hind_upper.r"] = (-0.6 * up, 0, 0)
            pose["hind_lower.l"] = (0.3 * up, 0, 0)
            pose["hind_lower.r"] = (0.3 * up, 0, 0)
            pose["front_upper.l"] = (0.5 * up, 0, 0)
            pose["front_upper.r"] = (0.5 * up, 0, 0)
            pose["neck"] = (-0.3 * up, 0, 0)
            pose["jaw"] = (-0.45 * up, 0, 0)
            pose["_hips_offset"] = V((0, -0.15 * up, 0.12 * up))
    return pose


def action_pose(a, clip, t):
    """One-shots: the attack (its hit at the species' wind-up), a flinch, a death onto the side."""
    u = t / clip.seconds
    pose = {}
    if clip.name == "Attack":
        hit = clip.hit / clip.seconds
        if a.attack == "bite":
            # Crouch, lunge, snap at the hit, recover.
            crouch = smooth(u / (hit * 0.5)) * (1 - smooth((u - hit * 0.5) / (hit * 0.5)))
            lunge = smooth((u - hit * 0.45) / (hit * 0.55)) * (1 - smooth((u - hit) / (1 - hit)))
            jaw = -0.55 * smooth((u - hit * 0.5) / (hit * 0.4)) * (1 - smooth((u - hit) / 0.06))
            pose["neck"] = (-0.15 * crouch + 0.1 * lunge, 0, 0)
            pose["head"] = (0.15 * lunge, 0, 0)
            pose["jaw"] = (jaw, 0, 0)
            pose["hind_upper.l"] = (-0.3 * crouch + 0.25 * lunge, 0, 0)
            pose["hind_upper.r"] = (-0.3 * crouch + 0.25 * lunge, 0, 0)
            pose["hind_lower.l"] = (0.4 * crouch, 0, 0)
            pose["hind_lower.r"] = (0.4 * crouch, 0, 0)
            pose["front_upper.l"] = (0.35 * lunge, 0, 0)
            pose["front_upper.r"] = (0.25 * lunge, 0, 0)
            pose["_hips_offset"] = V((0, -0.08 * crouch + 0.3 * lunge, -0.06 * crouch + 0.04 * lunge))
            pose["ear.l"] = (0.5, 0, 0.2)
            pose["ear.r"] = (0.5, 0, -0.2)
        elif a.attack == "gore":
            # Head dips low, then hooks up and to the side with the tusks at the hit.
            dip = smooth(u / (hit * 0.7)) * (1 - smooth((u - hit * 0.7) / (hit * 0.3)))
            hook = smooth((u - hit * 0.7) / (hit * 0.3)) * (1 - smooth((u - hit) / (1 - hit)))
            pose["neck"] = (-0.35 * dip + 0.25 * hook, 0, 0.35 * hook)
            pose["head"] = (-0.3 * dip + 0.35 * hook, 0.2 * hook, 0.2 * hook)
            pose["chest"] = (-0.08 * dip, 0, 0)
            pose["front_upper.l"] = (0.2 * dip, 0, 0)
            pose["front_upper.r"] = (0.2 * dip, 0, 0)
            pose["_hips_offset"] = V((0, 0.15 * hook, -0.03 * dip))
        else:
            # A bear's swipe: weight back, the right forepaw rises and sweeps across at the hit.
            raise_ = smooth(u / (hit * 0.75)) * (1 - smooth((u - hit) / (1 - hit)))
            sweep = smooth((u - hit * 0.7) / (hit * 0.3)) * (1 - smooth((u - hit - 0.1) / (1 - hit - 0.1)))
            pose["hips"] = (0.18 * raise_, 0, 0)
            pose["chest"] = (0.12 * raise_, 0, 0.1 * sweep)
            pose["front_upper.r"] = (1.2 * raise_ - 0.6 * sweep, 0.4 * sweep, -0.5 * sweep)
            pose["front_lower.r"] = (-0.9 * raise_ + 0.5 * sweep, 0, 0)
            pose["front_foot.r"] = (-0.5 * raise_, 0, 0)
            pose["neck"] = (-0.1 * raise_, 0, -0.15 * sweep)
            pose["jaw"] = (-0.4 * raise_, 0, 0)
            pose["_hips_offset"] = V((0, -0.05 * raise_, 0.05 * raise_))
    elif clip.name == "Hurt":
        k = math.sin(math.pi * min(1.0, u * 1.4)) * (1 - u)
        pose["spine"] = (0.08 * k, 0, 0.12 * k)
        pose["neck"] = (0.25 * k, 0, -0.2 * k)
        pose["head"] = (0.15 * k, 0, 0)
        pose["hind_upper.l"] = (-0.12 * k, 0, 0)
        pose["hind_upper.r"] = (-0.12 * k, 0, 0)
        pose["ear.l"] = (0.6 * k, 0, 0)
        pose["ear.r"] = (0.6 * k, 0, 0)
        pose["_hips_offset"] = V((0.04 * k, -0.03 * k, -0.02 * k))
    elif clip.name == "Death":
        # The legs give, it rolls onto its right side and lies still, head down, legs loose.
        fall = smooth(u / 0.7)
        roll = smooth((u - 0.15) / 0.65)
        lie = a.joints["hip"].z - max(0.18, COAT_RADIUS[a.key])
        pose["hips"] = (0, -1.5 * roll, 0)
        pose["neck"] = (-0.35 * fall, 0, -0.3 * roll)
        pose["head"] = (-0.2 * fall, 0, -0.2 * roll)
        pose["jaw"] = (-0.12 * roll, 0, 0)
        for s, k in ((".l", 1), (".r", 0.8)):
            pose["front_upper" + s] = (0.35 * roll * k, 0, 0)
            pose["front_lower" + s] = (-0.5 * roll * k, 0, 0)
            pose["hind_upper" + s] = (0.3 * roll * k, 0, 0)
            pose["hind_lower" + s] = (-0.4 * roll * k, 0, 0)
            pose["hind_foot" + s] = (0.3 * roll * k, 0, 0)
        pose["tail.1"] = (-0.4 * roll, 0, 0)
        pose["_hips_offset"] = V((0.15 * roll, 0, -lie * fall))
    return pose


COAT_RADIUS = {"deer": 0.2, "boar": 0.27, "wolf": 0.14, "bear": 0.36}


def make_clips(a):
    attack = {"bite": (0.9, 0.45), "gore": (0.75, 0.35), "swipe": (1.2, 0.6)}[a.attack]
    clips = [Clip("Idle", 4.0), Clip("Graze", 3.0), Clip("Alert", 2.0), Clip("Threaten", 2.0), Clip("Eat", 2.4)]
    for name in ("Walk", "Trot", "Run"):
        clips.append(Clip(name, a.gaits[name][0], native=a.speeds[name]))
    att = Clip("Attack", attack[0], loop=False)
    att.hit = attack[1]
    clips += [att, Clip("Hurt", 0.4, loop=False), Clip("Death", 1.3, loop=False)]
    return clips


def pose_at(a, clip, t):
    if clip.name in ("Walk", "Trot", "Run"):
        return gait_pose(a, clip.name, t, clip.name)
    if clip.name in ("Attack", "Hurt", "Death"):
        return action_pose(a, clip, t)
    return idle_pose(a, clip, t)


def key_clips(a, rig, clips):
    from bpy_extras import anim_utils
    names = [b.name for b in rig.pose.bones]
    for clip in clips:
        action = bpy.data.actions.new(clip.name)
        action.use_fake_user = True
        if hasattr(clip, "hit"):
            action["hit"] = clip.hit
        slot = action.slots.new(id_type="OBJECT", name=rig.name)
        channelbag = anim_utils.action_ensure_channelbag_for_slot(action, slot)
        frames = list(range(clip.frames + (1 if clip.loop else 0)))
        rot = {n: [] for n in names}
        hips = []
        for f in frames:
            t = (f / clip.frames) * clip.seconds if clip.loop else min(clip.seconds, f / FPS)
            pose = pose_at(a, clip, t)
            for n in names:
                x, y, z = pose.get(n, (0, 0, 0))
                rot[n].append(Euler((x, y, z), "XYZ").to_quaternion())
            hips.append(pose.get("_hips_offset", V((0, 0, 0))))
        # Every bone in every clip (a view switching clips never keeps a bone from the last one).
        for n in names:
            qs = rot[n]
            for i in range(1, len(qs)):
                if qs[i].dot(qs[i - 1]) < 0:
                    qs[i] = -qs[i]
            path = f'pose.bones["{n}"].rotation_quaternion'
            for axis in range(4):
                fc = channelbag.fcurves.new(path, index=axis, group_name=n)
                fc.keyframe_points.add(len(frames))
                co = []
                for f, q in zip(frames, qs):
                    co += [f, q[axis]]
                fc.keyframe_points.foreach_set("co", co)
                fc.update()
        # The hips' offset is in the animal's frame; the hips bone's rest frame has Y along the spine, Z up.
        hb = rig.data.bones["hips"]
        to_local = hb.matrix_local.to_3x3().inverted()
        path = 'pose.bones["hips"].location'
        locals_ = [to_local @ h for h in hips]
        for axis in range(3):
            fc = channelbag.fcurves.new(path, index=axis, group_name="hips")
            fc.keyframe_points.add(len(frames))
            co = []
            for f, l in zip(frames, locals_):
                co += [f, l[axis]]
            fc.keyframe_points.foreach_set("co", co)
            fc.update()
        for fc in channelbag.fcurves:
            for kp in fc.keyframe_points:
                kp.interpolation = "LINEAR"
    return clips


# ---------------------------------------------------------------- building one species

def build(key, mats):
    a = SPECIES[key]()
    body = polygonise(a)
    parts = add_parts(a, body)
    rig = build_rig(a)
    skin_weights(a, body)
    colour_skin(a, body)
    for p in parts:
        rigid_weights(p)
        colour_part(a, p)
    # Materials: fur on the skin and furry parts; glossy eyes and noses.
    fur = bpy.data.materials.get("M_Animal_Fur") or bpy.data.materials.new("M_Animal_Fur")
    glossy = bpy.data.materials.get(GLOSSY) or bpy.data.materials.new(GLOSSY)
    body.data.materials.append(fur)
    for p in parts:
        p.data.materials.append(glossy if p["glossy"] else fur)
    select_only([body] + parts)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.join()
    body = bpy.context.view_layer.objects.active
    uv_box(body.data)
    # Levels of detail: the full skin decimated to each budget, all skinned to the same rig.
    out = collection("HOLLOW_EXPORT")
    lods = []
    tris = sum(len(p.vertices) - 2 for p in body.data.polygons)
    for i, target in enumerate(a.lods):
        o = body.copy()
        o.data = body.data.copy()
        o.name = f"Body_lod{i}"
        o.data.name = o.name
        for c in list(o.users_collection):
            c.objects.unlink(o)
        out.objects.link(o)
        ratio = min(1.0, target / max(1, tris))
        if ratio < 0.999:
            mod = o.modifiers.new("lod", "DECIMATE")
            mod.decimate_type = "COLLAPSE"
            mod.ratio = ratio
            mod.use_collapse_triangulate = True
            select_only([o])
            bpy.ops.object.modifier_apply(modifier=mod.name)
        o.parent = rig
        arm = o.modifiers.new("rig", "ARMATURE")
        arm.object = rig
        normalise_weights(o)
        lods.append(o)
    clips = key_clips(a, rig, make_clips(a))
    OUT.mkdir(parents=True, exist_ok=True)
    export_glb(OUT / f"{key}.glb", [rig] + lods, mats, animations=True)
    facts = {
        "species": key,
        "source": "Hollow (art/animals/build_animals.py): our own, metaball anatomy, procedural clips",
        "clips": {c.name: {"seconds": round(c.seconds, 4), "loop": c.loop, **({"speed": c.native} if c.native else {}),
                           **({"hit": c.hit} if hasattr(c, "hit") else {})} for c in clips},
        "lods": [len(o.data.polygons) for o in lods],
        "length": round(max(v.y for v in a.joints.values()) - min(v.y for v in a.joints.values()), 3),
        "height": round(a.joints["chest"].z, 3),
        "head": [round(a.joints["head"].y, 3), round(a.joints["head"].z, 3)],
    }
    (OUT / f"{key}.json").write_text(json.dumps(facts, indent=2) + "\n", encoding="utf-8", newline="\n")
    log(f"{key}: LODs {facts['lods']} triangles, clips {', '.join(c.name for c in clips)}")
    return rig, lods


def normalise_weights(o):
    groups = {g.index: g for g in o.vertex_groups}
    for v in o.data.vertices:
        entries = sorted(((g.group, g.weight) for g in v.groups if g.group in groups), key=lambda e: -e[1])
        keep = entries[:4]
        total = sum(w for _, w in keep) or 1.0
        for gi, _ in entries[4:]:
            groups[gi].remove([v.index])
        for gi, w in keep:
            groups[gi].add([v.index], w / total, "REPLACE")


# ---------------------------------------------------------------- previews

def preview_tiles(key, rig, lod):
    """Tiles of one species, Workbench-rendered: the side and three-quarter views at rest, then a run, the attack's hit,
    the death and grazing from the side."""
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "VERTEX"
    scene.render.resolution_x, scene.render.resolution_y = 480, 360
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    cam.data.type = "ORTHO"
    scene.collection.objects.link(cam)
    scene.camera = cam
    for o in bpy.data.objects:
        if o.type == "MESH":
            o.hide_render = o != lod
    size = max(lod.dimensions.y, lod.dimensions.z) * 1.25
    cam.data.ortho_scale = size
    tiles = []
    rig.animation_data_create()
    for view, clip, frame in (("side", None, 0), ("three", None, 0), ("side", "Run", 0.25), ("three", "Attack", None),
                              ("side", "Death", 1.0), ("three", "Graze", 0.3)):
        if clip:
            act = bpy.data.actions.get(clip)
            rig.animation_data.action = act
            if act.slots:
                rig.animation_data.action_slot = act.slots[0]
            first, last = act.frame_range
            at = act.get("hit", 0) * FPS if frame is None else first + (last - first) * frame
            scene.frame_set(int(round(at)))
        else:
            rig.animation_data.action = None
            for pb in rig.pose.bones:
                pb.rotation_quaternion = Quaternion()
                pb.location = V((0, 0, 0))
            scene.frame_set(0)
        h = 0.5 * lod.dimensions.z
        if view == "side":
            cam.location = V((size * 2, 0, h))
            cam.rotation_euler = Euler((math.radians(90), 0, math.radians(90)))
        else:
            cam.location = V((size * 1.3, size * 1.5, h + size * 0.45))
            cam.rotation_euler = (V((0, 0, h * 0.7)) - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = str(OUT / f".preview.{key}.{len(tiles)}.png")
        bpy.ops.render.render(write_still=True)
        img = bpy.data.images.load(scene.render.filepath)
        w, hh = img.size
        tiles.append(np.array(img.pixels[:], dtype=np.float32).reshape(hh, w, 4))
        bpy.data.images.remove(img)
        Path(scene.render.filepath).unlink()
    return tiles


def save_sheet(path, rows_of_tiles):
    rows = [r for r in rows_of_tiles if r]
    if not rows:
        return
    th, tw = rows[0][0].shape[:2]
    cols = max(len(r) for r in rows)
    sheet = np.ones((len(rows) * th, cols * tw, 4), np.float32)
    for ri, row in enumerate(rows):
        for ci, im in enumerate(row):
            sheet[(len(rows) - 1 - ri) * th:(len(rows) - ri) * th, ci * tw:(ci + 1) * tw] = im
    out = bpy.data.images.new("sheet", cols * tw, len(rows) * th, alpha=True)
    out.pixels.foreach_set(sheet.ravel())
    out.filepath_raw = str(path)
    out.file_format = "PNG"
    out.save()
    log(f"preview {path}")


def main():
    args = script_args()
    shot = None
    if "--preview" in args:
        i = args.index("--preview")
        shot = Path(args[i + 1])
        args = args[:i] + args[i + 2:]
    keys = [k for k in args if k in SPECIES] or list(SPECIES)
    sheet = []
    for key in keys:
        # Each species from a clean file, so no clip of another is exported with it.
        reset()
        albedo, normal = fur_textures()
        mats = materials(albedo, normal)
        rig, lods = build(key, mats)
        if shot:
            sheet.append(preview_tiles(key, rig, lods[0]))
    if shot:
        save_sheet(shot, sheet)


if __name__ == "__main__":
    main()

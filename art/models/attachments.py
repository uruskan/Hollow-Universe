"""Weapon attachments (G4): holographic sight, scope, silencer, weapon light, muzzle brake.

Each pivot is its mount point and each points +X, like the guns, so an attachment sits exactly on the gun's socket
for its slot (models.json: the attachment's `slot`, the gun's `sockets`): a sight's clamp on the rail (`sight`), a
silencer's or brake's rear face on the muzzle (`muzzle`), a light's clamp under the barrel (`under`). Authored
pointing +Y like the other held things, turned to +X on export.

    blender -b --factory-startup --python art/models/attachments.py -- [keys...]
"""
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from hollow import script_args  # noqa: E402
from hollow.build import GLOSSY, GUNMETAL, RUBBER, STEEL, Model  # noqa: E402

BLACK = (0.55, 0.55, 0.57)
DARK = (0.32, 0.32, 0.34)


def clamp(m, y, length=0.045, width=0.03, height=0.012, z=0.0, under=False):
    """A rail clamp: the block that grips the gun's rail, with a cross bolt; its contact face at z (top when under)."""
    zc = z - height / 2 if under else z + height / 2
    m.box((width, length, height), (0, y, zc), bevel=0.002, tile=GUNMETAL, tint=BLACK, metres=0.6)
    m.cylinder(0.0045, width + 0.008, (0, y, zc), (0, 90, 0), sides=10, bevel=0.0008, tile=STEEL, tint=(0.8, 0.8, 0.8), metres=0.18)


def holo_sight(seed=301):
    m = Model("holo-sight", seed=seed, forward=True)
    clamp(m, 0.0, length=0.05)
    # The body: a low base, two side walls and a roof framing the window, the emitter behind.
    m.box((0.034, 0.07, 0.012), (0, 0.004, 0.018), bevel=0.003, tile=GUNMETAL, tint=BLACK, metres=0.72)
    for x in (-0.0155, 0.0155):
        m.box((0.004, 0.034, 0.042), (x, 0.016, 0.045), bevel=0.0012, tile=GUNMETAL, tint=BLACK, metres=0.6)
    m.box((0.035, 0.036, 0.005), (0, 0.016, 0.068), bevel=0.0015, tile=GUNMETAL, tint=BLACK, metres=0.6)
    # The window: tinted glass with an anti-glare coating that reads blue-green from outside.
    m.box((0.028, 0.002, 0.036), (0, 0.03, 0.045), bevel=0.0, material=GLOSSY, tint=(0.22, 0.48, 0.55), metres=0.3)
    m.box((0.026, 0.024, 0.022), (0, -0.022, 0.035), bevel=0.003, tile=GUNMETAL, tint=DARK, metres=0.48)
    m.cylinder(0.006, 0.008, (0.021, -0.022, 0.038), (0, 90, 0), sides=14, bevel=0.001, tile=RUBBER, tint=(0.6, 0.6, 0.6), metres=0.12)
    m.cylinder(0.0035, 0.004, (0, -0.035, 0.04), (90, 0, 0), sides=10, tile=GUNMETAL, tint=(0.55, 0.15, 0.12), metres=0.12)
    m.socket("eye", (0, -0.12, 0.045))
    m.export(floor=False)


def scope(seed=303):
    m = Model("scope", seed=seed, forward=True)
    axis = 0.046
    for y in (-0.035, 0.045):
        clamp(m, y, length=0.022, width=0.03)
        m.lathe([(0.0158, -0.009), (0.0158, 0.009)], (0, y, axis), (-90, 0, 0), sides=20, close=False, tile=GUNMETAL, tint=BLACK, metres=0.3)
        m.box((0.012, 0.018, axis - 0.012 - 0.012), (0, y, (axis + 0.012) / 2), bevel=0.002, tile=GUNMETAL, tint=BLACK, metres=0.3)
    # The tube, along +Y here: eyepiece at the back, objective bell at the front.
    profile = [(0.0, -0.16), (0.0175, -0.16), (0.019, -0.15), (0.019, -0.11), (0.0135, -0.085), (0.0127, -0.07), (0.0127, 0.08),
               (0.0135, 0.09), (0.0225, 0.125), (0.0235, 0.15), (0.022, 0.16), (0.0, 0.16)]
    m.lathe(profile, (0, 0, axis), (-90, 0, 0), sides=28, tile=GUNMETAL, tint=BLACK, metres=1.8)
    for y, r in ((-0.1605, 0.0165), (0.1605, 0.0205)):
        m.cylinder(r, 0.002, (0, y, axis), (90, 0, 0), sides=24, material=GLOSSY, tint=(0.1, 0.16, 0.22), metres=0.3)
    m.cylinder(0.011, 0.016, (0, 0.005, axis + 0.019), sides=16, bevel=0.0015, tile=GUNMETAL, tint=DARK, metres=0.3)
    m.cylinder(0.011, 0.016, (0.019, 0.005, axis), (0, 90, 0), sides=16, bevel=0.0015, tile=GUNMETAL, tint=DARK, metres=0.3)
    m.socket("eye", (0, -0.24, axis))
    m.export(floor=False)


def silencer(seed=305):
    m = Model("silencer", seed=seed, forward=True)
    # From the muzzle (origin) forward: a threaded collar, the can, a front cap with the bore.
    profile = [(0.0, 0.0), (0.0105, 0.0), (0.0105, 0.016), (0.0175, 0.02), (0.0185, 0.026), (0.0185, 0.172), (0.0175, 0.18),
               (0.008, 0.181), (0.0, 0.181)]
    m.lathe(profile, (0, 0, 0), (-90, 0, 0), sides=28, tile=GUNMETAL, tint=BLACK, metres=1.5)
    m.cylinder(0.0045, 0.002, (0, 0.1815, 0), (90, 0, 0), sides=12, tile=RUBBER, tint=(0.15, 0.15, 0.15), metres=0.12)
    for y in (0.04, 0.05, 0.155, 0.165):
        m.lathe([(0.0186, y - 0.0025), (0.0192, y), (0.0186, y + 0.0025)], (0, 0, 0), (-90, 0, 0), sides=28, close=False, tile=STEEL,
                tint=(0.55, 0.55, 0.57), metres=0.3)
    m.export(floor=False)


def weapon_light(seed=307):
    m = Model("weapon-light", seed=seed, forward=True)
    clamp(m, 0.0, length=0.04, under=True)
    m.box((0.016, 0.03, 0.01), (0, 0.0, -0.016), bevel=0.002, tile=GUNMETAL, tint=BLACK, metres=0.3)
    body = [(0.0, -0.055), (0.012, -0.055), (0.0135, -0.05), (0.0135, 0.03), (0.018, 0.04), (0.019, 0.06), (0.0175, 0.062),
            (0.0, 0.062)]
    m.lathe(body, (0, 0.0, -0.034), (-90, 0, 0), sides=24, tile=GUNMETAL, tint=BLACK, metres=0.9)
    m.cylinder(0.0152, 0.002, (0, 0.0625, -0.034), (90, 0, 0), sides=24, material=GLOSSY, tint=(0.95, 0.93, 0.82), metres=0.18)
    for k in range(6):
        y = -0.03 + k * 0.009
        m.lathe([(0.0136, y - 0.002), (0.0141, y), (0.0136, y + 0.002)], (0, 0, -0.034), (-90, 0, 0), sides=24, close=False,
                tile=STEEL, tint=(0.45, 0.45, 0.47), metres=0.18)
    m.box((0.012, 0.014, 0.008), (0, -0.06, -0.034), bevel=0.002, tile=RUBBER, tint=(0.3, 0.3, 0.3), metres=0.18)
    m.socket("beam", (0, 0.064, -0.034))
    m.export(floor=False)


def muzzle_brake(seed=309):
    m = Model("muzzle-brake", seed=seed, forward=True)
    profile = [(0.0, 0.0), (0.0095, 0.0), (0.0095, 0.012), (0.0145, 0.015), (0.0145, 0.062), (0.0135, 0.066), (0.006, 0.067),
               (0.0, 0.067)]
    m.lathe(profile, (0, 0, 0), (-90, 0, 0), sides=24, tile=STEEL, tint=(0.5, 0.5, 0.52), metres=0.6)
    # Three ports on each side and one on top, dark in the bore.
    for k in range(3):
        y = 0.024 + k * 0.013
        for x in (-1, 1):
            m.box((0.004, 0.007, 0.012), (x * 0.0128, y, 0), bevel=0.0008, tile=RUBBER, tint=(0.12, 0.12, 0.12), metres=0.12)
        m.box((0.009, 0.007, 0.004), (0, y, 0.0128), bevel=0.0008, tile=RUBBER, tint=(0.12, 0.12, 0.12), metres=0.12)
    m.cylinder(0.0042, 0.002, (0, 0.0675, 0), (90, 0, 0), sides=12, tile=RUBBER, tint=(0.12, 0.12, 0.12), metres=0.12)
    m.export(floor=False)


ALL = {"holo-sight": holo_sight, "scope": scope, "silencer": silencer, "weapon-light": weapon_light,
       "muzzle-brake": muzzle_brake}

for key in script_args() or list(ALL):
    ALL[key]()

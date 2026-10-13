"""G7 animal items (GROUND.md §6.9): raw, cooked and burnt meat of the four kinds, hide, bone fragments, animal fat, the
bone knife and the bone club. Our own designs, built from primitives on the shared atlas like the other supplies.

    blender -b --factory-startup --python art/models/animal_items.py -- [keys...]
"""
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from mathutils import Matrix, Vector, noise  # noqa: E402

from hollow import script_args  # noqa: E402
from hollow.build import BASALT, CANVAS, GLOSSY, IVORY, LEATHER, OCHRE, PARCHMENT, Model  # noqa: E402

# Each kind's cut: size (x, y, z diameters), muscle colour, fat (thickness of the cap, its colour), a bone, marbling.
MEATS = {
    "venison": dict(size=(0.15, 0.1, 0.045), muscle=(0.52, 0.09, 0.08), fat=0.006, fat_colour=(0.92, 0.86, 0.74), bone=True,
                    marbling=0.1, seed=171),
    "pork": dict(size=(0.15, 0.11, 0.045), muscle=(0.8, 0.36, 0.33), fat=0.018, fat_colour=(0.97, 0.93, 0.84), bone=True,
                 marbling=0.25, seed=173),
    "wolf-meat": dict(size=(0.13, 0.085, 0.04), muscle=(0.38, 0.06, 0.06), fat=0.0, fat_colour=(0.85, 0.78, 0.66), bone=False,
                      marbling=0.05, seed=175),
    "bear-meat": dict(size=(0.2, 0.13, 0.06), muscle=(0.42, 0.07, 0.06), fat=0.012, fat_colour=(0.95, 0.86, 0.6), bone=True,
                      marbling=0.35, seed=177),
}

COOKED = (0.7, 0.42, 0.28)   # on the ochre tile: a roasted brown
BURNT = (0.06, 0.05, 0.045)


def meat(kind, state):
    spec = MEATS[kind]
    key = f"{state}-{kind}"
    m = Model(key, seed=spec["seed"] + {"raw": 0, "cooked": 1, "burnt": 2}[state], held=True, forward=False)
    sx, sy, sz = spec["size"]
    shrink = {"raw": 1.0, "cooked": 0.9, "burnt": 0.84}[state]
    sx, sy, sz = sx * shrink, sy * shrink, sz * (shrink + 0.05 if state != "raw" else 1.0)
    muscle = spec["muscle"]
    off = Vector((spec["seed"] * 0.37, spec["seed"] * 0.11, 0.3))
    fat_colour = spec["fat_colour"]

    def flesh(co, tint):
        # Marbling: pale streaks through the muscle; a seared crust with grill bars; charring with glowing cracks.
        streak = noise.noise(Vector((co.x * 70, co.y * 22, co.z * 50)) + off, noise_basis="PERLIN_ORIGINAL")
        grain = noise.noise(co * 160 + off, noise_basis="PERLIN_ORIGINAL")
        marbled = streak > 0.72 - 0.6 * spec["marbling"]
        if state == "raw":
            k = 1.0 + 0.16 * grain
            c = [t * k for t in muscle]
            if marbled:
                c = [c[i] * 0.6 + fat_colour[i] * 0.4 for i in range(3)]
            return c
        if state == "cooked":
            bars = math.sin(co.x * 110 + co.y * 35)
            k = 0.45 if bars > 0.8 and co.z > sz * 0.1 else 1.0
            crust = 0.85 + 0.25 * grain
            return [COOKED[i] * k * crust * (1.12 if marbled else 1.0) for i in range(3)]
        crack = noise.noise(co * 70 + off, noise_basis="VORONOI_F2F1")
        ember = 1.0 if crack < 0.04 else 0.0
        return [BURNT[0] * (1 + 0.5 * grain) + 0.12 * ember, BURNT[1] * (1 + 0.5 * grain) + 0.025 * ember, BURNT[2] * (1 + 0.5 * grain)]

    def style(tint):
        if state == "raw":
            return dict(material=GLOSSY, tint=tint)
        if state == "cooked":
            return dict(tile=OCHRE, tint=COOKED)
        return dict(tile=BASALT, tint=BURNT)

    m.blob((sx, sy, sz), (0, 0, sz * 0.42), (0, 0, 8), subdivisions=4, rough=0.12, freq=2.4, flatten=-0.55, metres=0.12,
           tint_fn=flesh, **style(muscle))
    if spec["fat"] > 0:
        # The fat cap: a rim along one long side, half sunk into the cut.
        fat = fat_colour if state == "raw" else ((1.0, 0.8, 0.6) if state == "cooked" else (0.09, 0.07, 0.06))

        def rim(co, tint):
            n = noise.noise(co * 120 + off, noise_basis="PERLIN_ORIGINAL")
            return [t * (0.92 + 0.12 * n) for t in tint]

        fkw = dict(material=GLOSSY, tint=fat) if state == "raw" else dict(tile=PARCHMENT if state == "cooked" else BASALT, tint=fat)
        m.blob((sx * 0.86, spec["fat"] * 2 + 0.008, sz * 0.8), (0, -sy * 0.4, sz * 0.4), (0, 0, 8), subdivisions=3, rough=0.08,
               freq=2.0, flatten=-0.55, metres=0.1, tint_fn=rim, **fkw)
    if spec["bone"]:
        # A bone end sticking out of the cut.
        bone = (0.93, 0.89, 0.8) if state != "burnt" else (0.25, 0.22, 0.19)
        length = sx * 0.45
        m.cylinder(sz * 0.17, length, (sx * 0.5, sy * 0.06, sz * 0.38), (0, 90, 8), sides=10, tile=IVORY, tint=bone, metres=0.08)
        m.blob((sz * 0.48, sz * 0.48, sz * 0.46), (sx * 0.5 + length * 0.5, sy * 0.06, sz * 0.38), subdivisions=2, rough=0.12,
               tile=IVORY, tint=bone, metres=0.06)
    m.export(floor=True)


def hide(seed=181):
    """A pelt folded over once: brown fur on top with a darker stripe down the spine, the pale leather side underneath
    and showing at the fold, ragged edges and four stubs where the legs were."""
    m = Model("hide", seed=seed, held=True, forward=False)
    fur = (0.78, 0.55, 0.36)
    leather = (0.92, 0.78, 0.6)

    def lobes(x, y):
        a = math.atan2(y, x)
        ragged = 1 + 0.1 * noise.noise(Vector((math.cos(a) * 2.5, math.sin(a) * 2.5, seed)), noise_basis="PERLIN_ORIGINAL")
        legs = 1 + 0.22 * max(0.0, math.cos(2 * (a - 0.15))) ** 8
        return ragged * legs

    def furred(co, tint):
        if co.z < 0:
            return [leather[i] * (0.95 + 0.08 * noise.noise(co * 90, noise_basis="PERLIN_ORIGINAL")) for i in range(3)]
        n = noise.noise(Vector((co.x * 40, co.y * 150, co.z * 40)) + Vector((seed, 0, 0)), noise_basis="PERLIN_ORIGINAL")
        spine = math.exp(-((co.y / 0.03) ** 2))
        k = 0.82 + 0.3 * n - 0.4 * spine
        return [t * k for t in fur]

    for layer, (w, d, z, turn) in enumerate([(0.36, 0.27, 0.006, 0), (0.3, 0.2, 0.019, 7)]):
        sheet = m.blob((w, d, 0.012), (0, 0, z), (0, 0, turn), subdivisions=5, rough=0.0, tile=CANVAS, tint=fur, metres=0.4,
                       tint_fn=furred, sharp=0)
        for v in sheet.data.vertices:
            k = lobes(v.co.x, v.co.y)
            v.co.x *= k
            v.co.y *= k
            v.co.z += 0.005 * noise.noise(Vector((v.co.x * 14, v.co.y * 14, layer)), noise_basis="PERLIN_ORIGINAL")
        sheet.data.update()
    # The fold: a soft roll along one edge, leather out.
    m.cylinder(0.012, 0.25, (0, 0.105, 0.016), (0, 90, 7), sides=12, tile=CANVAS, tint=leather, metres=0.3)
    m.export(floor=True)


def bone_fragments(seed=183):
    m = Model("bone-fragments", seed=seed, held=True, forward=False)
    for i in range(8):
        length = m.rng.uniform(0.035, 0.075)
        width = m.rng.uniform(0.012, 0.022)
        a = m.rng.uniform(0, 360)
        r = m.rng.uniform(0.0, 0.035)
        at = (r * math.cos(math.radians(a * 1.7)), r * math.sin(math.radians(a * 1.7)), width * 0.4 + (0.01 if i > 5 else 0))
        tone = m.rng.uniform(0.86, 1.0)

        def porous(co, tint, tone=tone):
            n = noise.noise(co * 220, noise_basis="VORONOI_F1")
            return [t * tone * (0.82 if n < 0.08 else 1.0) for t in tint]

        m.blob((length, width, width * 0.8), at, (m.rng.uniform(-20, 20), m.rng.uniform(-15, 15), a), subdivisions=2, rough=0.16,
               freq=2.8, facets=0.22, tile=IVORY, tint=(0.93, 0.89, 0.79), metres=0.06, tint_fn=porous, sharp=30)
    m.export(floor=True)


def animal_fat(seed=185):
    m = Model("animal-fat", seed=seed, held=True, forward=False)
    for at, size in [((0.0, 0.0, 0.022), (0.075, 0.06, 0.045)), ((0.045, 0.018, 0.016), (0.05, 0.045, 0.032)),
                     ((-0.036, 0.026, 0.014), (0.045, 0.04, 0.028))]:
        m.blob(size, at, (0, 0, m.rng.uniform(0, 90)), subdivisions=3, rough=0.22, freq=2.2, flatten=-0.45, material=GLOSSY,
               tint=(0.96, 0.9, 0.68), metres=0.08, vary=0.08, vary_scale=40)
    # A scrap of hide it lies on.
    m.extrude([(-0.07, -0.05), (0.075, -0.055), (0.08, 0.05), (-0.065, 0.058)], 0.003, (0, 0, 0.0015), (90, 0, 0), tile=LEATHER,
              tint=(0.62, 0.45, 0.3), metres=0.2)
    m.export(floor=True)


def bone_knife(seed=187):
    """A shard of bone ground to an edge and a point, its knuckle end bound in rawhide for a grip. Like the machete: the
    length along +Z (up in the game), the edge towards +X."""
    m = Model("bone-knife", seed=seed, held=True, forward=False)
    bone = (0.92, 0.88, 0.78)
    # The blade: spine at -X, edge at +X, tapering to a point.
    outline = [(-0.008, 0.045), (0.012, 0.045), (0.016, 0.1), (0.014, 0.16), (0.006, 0.21), (0.0, 0.232), (-0.006, 0.2),
               (-0.01, 0.14), (-0.011, 0.09)]
    blade = m.extrude(outline, 0.009, tile=IVORY, tint=bone, metres=0.2, sharp=25)
    for v in blade.data.vertices:
        # Thin to an edge on the +X side.
        t = max(0.0, min(1.0, (v.co.x + 0.002) / 0.014))
        v.co.y *= 1 - 0.8 * t
    blade.data.update()
    m.recolour(blade, lambda co, t: [c * (1.06 if co.x > 0.008 else 0.95) for c in t])
    # The grip: the bone's knuckle, wrapped.
    m.lathe([(0.0, -0.075), (0.016, -0.072), (0.019, -0.06), (0.014, -0.04), (0.012, 0.0), (0.013, 0.05), (0.0, 0.052)], (0, 0, 0),
            (0, 0, 0), sides=12, tile=IVORY, tint=(0.86, 0.8, 0.68), metres=0.15)
    for k in range(9):
        z = -0.035 + k * 0.0085
        m.cylinder(0.0148, 0.007, (0, 0, z), (0, 0, 0), sides=12, tile=LEATHER, tint=(0.5, 0.36, 0.24), metres=0.05)
    m.socket("impact", (0.006, 0, 0.15))
    m.export(floor=False)


def bone_club(seed=189):
    """A thigh bone: a heavy knuckle at the head, the shaft bound with hide at the grip. Like the hatchets: the length
    along +Z (up in the game), the striking side towards +X."""
    m = Model("bone-club", seed=seed, held=True, forward=False)
    bone = (0.9, 0.85, 0.74)
    profile = [(0.0, -0.11), (0.022, -0.105), (0.03, -0.09), (0.024, -0.06), (0.019, 0.0), (0.017, 0.2), (0.019, 0.33), (0.026, 0.4),
               (0.036, 0.44), (0.0, 0.46)]
    shaft = m.lathe(profile, (0, 0, 0), (0, 0, 0), sides=14, tile=IVORY, tint=bone, metres=0.4)
    for v in shaft.data.vertices:
        v.co.x += 0.006 * math.sin(v.co.z * 9)
    shaft.data.update()
    # The head: two condyles.
    for side in (-1, 1):
        m.blob((0.05, 0.06, 0.07), (0.004, side * 0.022, 0.47), (side * 15, 0, 0), subdivisions=3, rough=0.08, tile=IVORY,
               tint=(0.88, 0.82, 0.7), metres=0.1)
    for k in range(14):
        z = -0.05 + k * 0.011
        m.cylinder(0.0215, 0.009, (0, 0, z), (0, 0, 0), sides=12, tile=LEATHER, tint=(0.45, 0.32, 0.21), metres=0.05)
    m.socket("impact", (0.03, 0, 0.46))
    m.export(floor=False)


ALL = {"hide": hide, "bone-fragments": bone_fragments, "animal-fat": animal_fat, "bone-knife": bone_knife, "bone-club": bone_club}
for _kind in MEATS:
    for _state in ("raw", "cooked", "burnt"):
        ALL[f"{_state}-{_kind}"] = (lambda k=_kind, s=_state: meat(k, s))

for key in script_args() or list(ALL):
    ALL[key]()

"""The ground's grass clumps (GROUND.md §4.6, docs/WALKING_GROUND.md §18): our own, painted and built from text in
Blender 5.2.

    blender -b --factory-startup --python art/grass/build_grass.py

Five species of clump and the forest floor's litter, each a bundle of cards that carry painted blades:

- field: the meadow's leafy grass, twelve cards of six to ten blades;
- seeding: tall grass in seed, blade cards under stems with spikes or open panicles;
- dry: straw-coloured, thinner, bent and broken blades;
- flower: a rosette of leaves under flower stalks (petals take the instance's colour);
- herb: heather sprigs with bells (variant A) or a leafy herb with small flowers (variant B);
- litter: dead leaves, needles and twigs lying flat.

Each species has three levels of detail and a billboard: LOD1 and LOD2 are the first cards of LOD0 (the cards are
ordered so that any first few keep the clump's outline) with fewer segments, so the shader can drop cards one by one
with distance and a level change only removes segments; the billboard is two crossed quads with a painted side view.
Cards carry their real normals (bent with the card, rounded across it); the second UV set carries the height fraction
(x) and the card's rank (y) for the shader's wind and stable thinning. The meshes have no vertex colour: Godot multiplies
a MultiMesh's instance colour into it, and the instance colour is the place's grass colour.

Writes game/assets/surface/grass/: grass_albedo.png (pale blade colour, alpha the cutout), grass_detail.png (normal x
and y across and along the blade, accent mask for petals and seed heads, translucency), grass.glb (<species>_lod0,
_lod1, _lod2, _far) and grass.json (the catalogue the game reads), with their Godot import settings. The atlas is
8 columns of 256 px; a species' variant B lies one column right of its variant A.
"""
import json
import math
import random
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector  # noqa: E402

from hollow import ASSETS, log, res_path  # noqa: E402
from hollow.export import reset, select_only  # noqa: E402
from hollow.godot import write_scene_import  # noqa: E402
from hollow.textures import write_png, write_texture_import  # noqa: E402

OUT = ASSETS / "surface" / "grass"
SIZE = 2048
COL = 256
ROW = 512
VARIANT_OFFSET = COL / SIZE  # u from a variant A cell to its B cell


# ---------------------------------------------------------------- colour

def srgb(linear):
    linear = np.clip(linear, 0, 1)
    return np.where(linear <= 0.0031308, linear * 12.92, 1.055 * np.power(linear, 1 / 2.4) - 0.055)


def lerp(a, b, t):
    return a + (b - a) * t


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


# ---------------------------------------------------------------- the atlas

class Cell:
    """A rectangle of the atlas in pixels (x, y from the top left); u runs left to right and v from the cell's foot (0)
    to its top (1)."""

    def __init__(self, col, row, rows=1.0, top=0.0):
        self.x = col * COL
        self.y = int(row * ROW + top * ROW)
        self.w = COL
        self.h = int(rows * ROW)

    def uv(self):
        """(u0, v_top, u1, v_bottom) in Godot's texture coordinates (origin top left)."""
        return (self.x / SIZE, self.y / SIZE, (self.x + self.w) / SIZE, (self.y + self.h) / SIZE)


class Atlas:
    """Layers painted back to front with 'over': linear colour, coverage, the blade's normal tilt (across, along), the
    accent mask and the translucency."""

    def __init__(self):
        self.rgb = np.zeros((SIZE, SIZE, 3), np.float32)
        self.a = np.zeros((SIZE, SIZE), np.float32)
        self.n = np.zeros((SIZE, SIZE, 2), np.float32)
        self.m = np.zeros((SIZE, SIZE), np.float32)
        self.t = np.zeros((SIZE, SIZE), np.float32)

    def over(self, y0, x0, cov, rgb, nxy, mask, thick, cell):
        """Composites a patch whose top-left pixel is (x0, y0), clipped to its cell."""
        h, w = cov.shape
        ya, yb = max(y0, cell.y), min(y0 + h, cell.y + cell.h)
        xa, xb = max(x0, cell.x), min(x0 + w, cell.x + cell.w)
        if ya >= yb or xa >= xb:
            return
        sy, sx = slice(ya - y0, yb - y0), slice(xa - x0, xb - x0)
        dy, dx = slice(ya, yb), slice(xa, xb)
        a = cov[sy, sx]
        inv = 1 - a
        self.rgb[dy, dx] = self.rgb[dy, dx] * inv[..., None] + rgb[sy, sx] * a[..., None]
        self.n[dy, dx] = self.n[dy, dx] * inv[..., None] + nxy[sy, sx] * a[..., None]
        self.m[dy, dx] = self.m[dy, dx] * inv + mask[sy, sx] * a
        self.t[dy, dx] = self.t[dy, dx] * inv + thick[sy, sx] * a
        self.a[dy, dx] = self.a[dy, dx] * inv + a

    def mean(self, cell, accent=False):
        """Coverage-weighted mean linear colour of a cell's blades (without its accents), as the shader's
        normalisation reads it."""
        sl = (slice(cell.y, cell.y + cell.h), slice(cell.x, cell.x + cell.w))
        w = self.a[sl] * (self.a[sl] >= 0.5)
        if not accent:
            w = w * (1 - self.m[sl])
        total = max(float(w.sum()), 1e-6)
        rgb = self.rgb[sl]
        return [round(float((rgb[..., c] * w).sum() / total), 4) for c in range(3)]

    def coverage(self, cell):
        sl = (slice(cell.y, cell.y + cell.h), slice(cell.x, cell.x + cell.w))
        return round(float((self.a[sl] >= 0.5).mean()), 4)


def stroke(atlas, cell, root_u, root_v, height, lean, power, width, colour, rng, profile="blade", fold=0.55,
           accent=0.0, thick=(0.55, 0.95), veins=0.06, midrib=0.10, droop=0.0):
    """One blade, stem or leaf as seen from the side: a centreline from (root_u, root_v) rising `height` (cell heights)
    and leaning `lean` (cell widths) by s^power, `width` cell widths wide at its widest; drawn row by row, its
    width measured across the line. `colour(s)` gives the linear colour along it (s 0 at the root, 1 at the tip)."""
    y_root = cell.y + cell.h * (1 - root_v)
    y_tip = cell.y + cell.h * (1 - root_v - height)
    rows = np.arange(int(math.floor(y_tip)) - 1, int(math.ceil(y_root)) + 1)
    vrow = (cell.y + cell.h - (rows + 0.5)) / cell.h
    s = (vrow - root_v) / height
    keep = (s >= -0.01) & (s <= 1.0)
    rows, s = rows[keep], np.clip(s[keep], 0, 1)
    if rows.size == 0:
        return
    bend = lean * np.power(s, power) - droop * np.power(s, 3.0)
    xc = cell.x + cell.w * (root_u + bend)
    dxds = cell.w * (lean * power * np.power(np.maximum(s, 1e-4), power - 1) - 3 * droop * s * s)
    dyds = cell.h * height
    corr = np.sqrt(1 + (dxds / dyds) ** 2)
    if profile == "blade":
        shape = (0.82 + 0.18 * smooth(0.0, 0.12, s)) * np.power(np.clip(1 - s, 0, 1), 0.62)
    elif profile == "leaf":
        shape = np.power(np.clip(np.sin(np.pi * np.clip(s, 0, 1)), 0, 1), 0.75) * (0.35 + 0.65 * smooth(0.0, 0.25, s))
    elif profile == "stem":
        shape = 1 - 0.35 * s
    elif profile == "broken":
        shape = (0.82 + 0.18 * smooth(0.0, 0.12, s)) * (1 - 0.45 * s)
    else:
        raise ValueError(profile)
    r = 0.5 * width * cell.w * shape * corr
    x_lo = int(math.floor(float((xc - r).min()))) - 1
    x_hi = int(math.ceil(float((xc + r).max()))) + 2
    X = np.arange(x_lo, x_hi) + 0.5
    off = X[None, :] - xc[:, None]
    rr = r[:, None]
    cov = np.clip(rr - np.abs(off) + 0.5, 0, 1).astype(np.float32)
    if profile in ("blade", "leaf"):
        # The tip's last pixels fade rather than end in a stair.
        cov *= np.clip((1 - s[:, None]) * cell.h * height * 0.6 + 0.3, 0, 1)
    q = np.clip(off / np.maximum(rr, 0.6), -1, 1)
    base = np.stack([colour(si) for si in s]).astype(np.float32)
    phase = rng.random() * 6.283
    vein = 1 + veins * np.sin(q * np.pi * 2.5 + phase) + veins * 0.5 * np.sin(s[:, None] * 37 + phase * 2)
    rib = 1 + midrib * (1 - smooth(0.0, 0.22, np.abs(q)))
    edge = 1 - 0.12 * smooth(0.65, 1.0, np.abs(q))
    rgb = base[:, None, :] * (vein * rib * edge)[..., None]
    nxy = np.stack([q * fold, np.full_like(q, 0.08) * (1 - np.abs(q))], axis=-1).astype(np.float32)
    mask = np.full_like(cov, accent)
    th = (thick[0] + (thick[1] - thick[0]) * s)[:, None] * (1 - 0.25 * q * q)
    atlas.over(int(rows[0]), x_lo, cov, rgb.astype(np.float32), nxy, mask.astype(np.float32), th.astype(np.float32), cell)


def blob(atlas, cell, cu, cv, ru, rv, angle, colour, accent=0.0, thick=0.7, dome=0.55, texture=0.0, rng=None,
         pointed=False):
    """A filled ellipse at (cu, cv) with radii ru, rv (cell widths), turned by `angle`: a petal, a seed, a bell, a
    berry or a leaf blade seen flat (`pointed`: narrowing to a point at both ends of its long axis)."""
    cx, cy = cell.x + cu * cell.w, cell.y + cell.h * (1 - cv)
    rx, ry = max(ru * cell.w, 0.4), max(rv * cell.w, 0.4)
    ext = max(rx, ry) + 2
    x0, y0 = int(math.floor(cx - ext)), int(math.floor(cy - ext))
    X = np.arange(x0, int(math.ceil(cx + ext)) + 1) + 0.5
    Y = np.arange(y0, int(math.ceil(cy + ext)) + 1) + 0.5
    dx, dy = X[None, :] - cx, Y[:, None] - cy
    ca, sa = math.cos(angle), math.sin(angle)
    lx, ly = (dx * ca + dy * sa) / rx, (-dx * sa + dy * ca) / ry
    if pointed:
        ly = ly / np.maximum(np.power(np.clip(1 - lx * lx, 0, 1), 0.7), 0.05)
    rho = np.sqrt(lx * lx + ly * ly)
    scale = min(rx, ry)
    cov = np.clip((1 - rho) * scale + 0.5, 0, 1).astype(np.float32)
    shade = 1 - 0.18 * rho * rho
    if texture > 0 and rng is not None:
        shade = shade * (1 + texture * np.sin(lx * 9 + rng.random() * 6) * np.sin(ly * 7 + rng.random() * 6))
    rgb = np.asarray(colour, np.float32)[None, None, :] * shade[..., None]
    nxy = np.stack([np.clip(lx, -1, 1) * dome, np.clip(-ly, -1, 1) * dome], axis=-1).astype(np.float32)
    mask = np.full_like(cov, accent)
    th = np.full_like(cov, thick)
    atlas.over(y0, x0, cov, rgb.astype(np.float32), nxy, mask, th, cell)


def gradient(root, mid, tip, mid_at=0.35):
    """Colour along a blade: dark at the root, its own colour by `mid_at`, paler toward the tip."""
    root, mid, tip = (np.asarray(c, np.float32) for c in (root, mid, tip))

    def colour(s):
        return lerp(lerp(root, mid, smooth(0.0, mid_at, s)), tip, smooth(mid_at, 1.0, s))
    return colour


def varied(colour, rng, spread=0.12, warm=0.0):
    """A blade's own shade of a palette: brighter or darker, a little warmer or cooler."""
    k = 1 + spread * (rng.random() * 2 - 1)
    w = warm * (rng.random() * 2 - 1)
    tilt = np.asarray([1 + w, 1 + w * 0.3, 1 - w], np.float32)

    def c(s):
        return colour(s) * k * tilt
    return c


# Pale, natural blade colours (linear): the shader divides by a cell's mean and multiplies by the place's grass colour,
# so what counts is the variation within a blade and between blades, not the hue.
# Roots only a little darker than the blade: the shader shades the clump's foot itself.
GREEN = gradient((0.15, 0.22, 0.09), (0.30, 0.42, 0.17), (0.50, 0.58, 0.26))
GREEN_DEEP = gradient((0.12, 0.19, 0.08), (0.22, 0.34, 0.14), (0.38, 0.50, 0.22))
GREEN_YELLOW = gradient((0.17, 0.23, 0.09), (0.36, 0.45, 0.17), (0.62, 0.62, 0.28))
STRAW = gradient((0.22, 0.19, 0.10), (0.45, 0.38, 0.20), (0.62, 0.55, 0.34))
STRAW_PALE = gradient((0.26, 0.23, 0.13), (0.52, 0.46, 0.27), (0.70, 0.64, 0.44))
STEM = gradient((0.08, 0.10, 0.04), (0.34, 0.40, 0.18), (0.48, 0.48, 0.25))
WOOD = gradient((0.06, 0.04, 0.025), (0.14, 0.09, 0.05), (0.22, 0.15, 0.08))
LEAF = gradient((0.045, 0.08, 0.03), (0.20, 0.36, 0.12), (0.28, 0.44, 0.15), mid_at=0.4)


def paint_field(atlas, cell, seed):
    """Leafy meadow grass: short blades thickening the foot behind eleven to fourteen tall ones leaning both ways,
    some bent over at the tip."""
    rng = random.Random(seed)
    for i in range(rng.randint(6, 8)):
        u = rng.uniform(0.1, 0.9)
        stroke(atlas, cell, u, rng.uniform(0.0, 0.04), rng.uniform(0.22, 0.42), rng.uniform(-0.15, 0.15),
               rng.uniform(1.4, 2.2), rng.uniform(0.03, 0.05), varied(GREEN_DEEP, rng, 0.14, 0.05), rng)
    n = rng.randint(11, 14)
    for i in range(n):
        u = 0.1 + 0.8 * (i + rng.random() * 0.8) / n
        h = rng.uniform(0.55, 0.97)
        lean = rng.uniform(-0.22, 0.22) + (0.5 - u) * rng.uniform(-0.2, 0.35)
        palette = rng.choice((GREEN, GREEN, GREEN_DEEP, GREEN_YELLOW))
        stroke(atlas, cell, u, rng.uniform(0.0, 0.04), h, lean, rng.uniform(1.6, 2.6), rng.uniform(0.032, 0.062),
               varied(palette, rng, 0.14, 0.06), rng, droop=rng.uniform(0, 0.12) if h > 0.8 else 0)


def paint_dry(atlas, cell, seed):
    """Dry grass: thinner straw blades, more of them bent over or broken off."""
    rng = random.Random(seed)
    n = rng.randint(10, 13)
    for i in range(n):
        u = 0.08 + 0.84 * (i + rng.random() * 0.8) / n
        broken = rng.random() < 0.3
        h = rng.uniform(0.35, 0.65) if broken else rng.uniform(0.5, 0.95)
        lean = rng.uniform(-0.3, 0.3)
        palette = rng.choice((STRAW, STRAW, STRAW_PALE, GREEN_YELLOW if rng.random() < 0.25 else STRAW))
        stroke(atlas, cell, u, rng.uniform(0.0, 0.04), h, lean, rng.uniform(1.4, 2.8), rng.uniform(0.022, 0.042),
               varied(palette, rng, 0.16, 0.08), rng, profile="broken" if broken else "blade",
               droop=0 if broken else rng.uniform(0.0, 0.22), fold=0.45, thick=(0.35, 0.6), veins=0.1)


def paint_seeding_blades(atlas, cell, seed):
    """The leaves of grass in seed: a few long blades, arching."""
    rng = random.Random(seed)
    n = rng.randint(6, 8)
    for i in range(n):
        u = 0.12 + 0.76 * (i + rng.random() * 0.8) / n
        h = rng.uniform(0.45, 0.8)
        lean = rng.uniform(-0.3, 0.3)
        stroke(atlas, cell, u, rng.uniform(0.0, 0.04), h, lean, rng.uniform(1.8, 2.8), rng.uniform(0.03, 0.05),
               varied(rng.choice((GREEN, GREEN_YELLOW)), rng, 0.14, 0.06), rng, droop=rng.uniform(0.04, 0.2))


def paint_seed_stems(atlas, cell, seed, kind):
    """Stems in seed: 'spike' (a dense cylinder of grains, as timothy's) or 'panicle' (branches of hanging spikelets,
    as oats' or bent grass')."""
    rng = random.Random(seed)
    n = rng.randint(3, 4)
    head = gradient((0.30, 0.30, 0.16), (0.52, 0.48, 0.28), (0.64, 0.58, 0.36))
    for i in range(n):
        u = 0.22 + 0.56 * (i + rng.random() * 0.7) / n
        h = rng.uniform(0.8, 0.96)
        lean = rng.uniform(-0.12, 0.12)
        power = rng.uniform(1.5, 2.2)
        stroke(atlas, cell, u, 0.0, h, lean, power, rng.uniform(0.012, 0.018), varied(STEM, rng, 0.1), rng,
               profile="stem", fold=0.3, thick=(0.45, 0.6), veins=0.02, midrib=0.0)
        tip_u = u + lean
        if kind == "spike":
            length = rng.uniform(0.14, 0.22)
            for g in range(26):
                s = g / 25
                v = h - length * (1 - s) - 0.005
                du = lean * power * (v / h) ** (power - 1) * 0.0
                blob(atlas, cell, tip_u + du + rng.uniform(-0.012, 0.012), v, rng.uniform(0.016, 0.022),
                     rng.uniform(0.009, 0.012), rng.uniform(-0.4, 0.4), head(0.4 + 0.6 * s) * rng.uniform(0.85, 1.1),
                     accent=1.0, thick=0.5, dome=0.6)
        else:
            # Branches from the top third, each with spikelets hanging along it.
            for b in range(rng.randint(5, 7)):
                v0 = h - rng.uniform(0.02, 0.3)
                side = -1 if b % 2 else 1
                bl = rng.uniform(0.12, 0.2)
                bh = rng.uniform(0.05, 0.12)
                stroke(atlas, cell, tip_u - lean * 0.15, v0, bh, side * bl * 0.6, 1.3, 0.008, varied(STEM, rng, 0.1),
                       rng, profile="stem", fold=0.2, veins=0.0, midrib=0.0)
                for k in range(4):
                    f = (k + 1) / 4
                    blob(atlas, cell, tip_u - lean * 0.15 + side * bl * 0.6 * f ** 1.3, v0 + bh * f - 0.015,
                         0.012, 0.022, rng.uniform(-0.3, 0.3), head(0.7) * rng.uniform(0.85, 1.15), accent=1.0,
                         thick=0.6, dome=0.5)


PETAL = (0.82, 0.82, 0.78)
DISC = (0.55, 0.38, 0.05)


def paint_flower_stalks(atlas, cell, seed, kind):
    """Flower stalks: 'daisy' heads (rays of petals round a disc, seen from the side) or 'cluster' heads (a ball of
    small florets, as clover's or knapweed's) on thin stems with a leaf or two."""
    rng = random.Random(seed)
    n = rng.randint(3, 4)
    for i in range(n):
        u = 0.15 + 0.7 * (i + rng.random() * 0.8) / n
        h = rng.uniform(0.55, 0.9)
        lean = rng.uniform(-0.1, 0.1)
        stroke(atlas, cell, u, 0.0, h, lean, 1.8, rng.uniform(0.012, 0.016), varied(STEM, rng, 0.1), rng,
               profile="stem", fold=0.3, veins=0.02, midrib=0.0, thick=(0.5, 0.6))
        for leaf in range(rng.randint(1, 2)):
            v0 = rng.uniform(0.12, 0.45) * h
            side = rng.choice((-1, 1))
            stroke(atlas, cell, u + lean * (v0 / h) ** 1.8, v0, rng.uniform(0.08, 0.14), side * rng.uniform(0.08, 0.14),
                   1.2, rng.uniform(0.035, 0.05), varied(LEAF, rng, 0.1), rng, profile="leaf")
        cu, cv = u + lean, h
        if kind == "daisy":
            r = rng.uniform(0.05, 0.072)
            for p in range(12):
                a = p / 12 * math.tau + rng.random() * 0.2
                # A flat disc seen nearly edge on: petals fan out sideways, squashed in height.
                blob(atlas, cell, cu + math.cos(a) * r * 0.55, cv + math.sin(a) * r * 0.16, r * 0.5, r * 0.11,
                     -math.atan2(math.sin(a) * 0.3, math.cos(a)), PETAL, accent=1.0, thick=0.8, dome=0.25)
            blob(atlas, cell, cu, cv + r * 0.05, r * 0.32, r * 0.2, 0, DISC, accent=0.0, thick=0.4, dome=0.7,
                 texture=0.2, rng=rng)
        else:
            r = rng.uniform(0.034, 0.046)
            for f in range(16):
                a = rng.random() * math.tau
                d = r * math.sqrt(rng.random())
                blob(atlas, cell, cu + math.cos(a) * d, cv + r * 0.6 + math.sin(a) * d, r * 0.33, r * 0.45,
                     rng.uniform(-0.6, 0.6), np.asarray(PETAL) * rng.uniform(0.8, 1.05), accent=1.0, thick=0.75,
                     dome=0.4)


def paint_flower_leaves(atlas, cell, seed):
    """The flower's rosette: broad leaves arching out from the foot, low."""
    rng = random.Random(seed)
    n = rng.randint(6, 8)
    for i in range(n):
        u = 0.15 + 0.7 * (i + rng.random() * 0.8) / n
        h = rng.uniform(0.25, 0.5)
        lean = (u - 0.5) * rng.uniform(0.5, 0.9) + rng.uniform(-0.1, 0.1)
        stroke(atlas, cell, u, 0.0, h, lean, rng.uniform(1.3, 2.0), rng.uniform(0.08, 0.13), varied(LEAF, rng, 0.12),
               rng, profile="leaf", fold=0.6, midrib=0.16, veins=0.05, droop=rng.uniform(0.0, 0.08))


def paint_heather(atlas, cell, seed):
    """Heather: woody stems branching up, crowded with tiny dark scale leaves, spikes of pale bells (the accent: the
    shader colours them pink-purple) along their tops."""
    rng = random.Random(seed)
    foliage = np.asarray((0.10, 0.17, 0.07), np.float32)
    shoots = []
    for i in range(rng.randint(7, 9)):
        u = 0.12 + 0.76 * (i + rng.random() * 0.8) / 8
        h = rng.uniform(0.45, 0.85)
        lean = rng.uniform(-0.2, 0.2)
        power = rng.uniform(1.3, 1.8)
        shoots.append((u, 0.0, h, lean, power))
        for b in range(rng.randint(1, 3)):
            s0 = rng.uniform(0.2, 0.6)
            shoots.append((u + lean * s0 ** power, h * s0, h * (1 - s0) * rng.uniform(0.5, 0.9),
                           rng.choice((-1, 1)) * rng.uniform(0.06, 0.16), 1.4))
    for (u, v0, h, lean, power) in shoots:
        stroke(atlas, cell, u, v0, h, lean, power, 0.016, varied(WOOD, rng, 0.15), rng, profile="stem", fold=0.3,
               veins=0.03, midrib=0.0, thick=(0.2, 0.3))
    for (u, v0, h, lean, power) in shoots:
        for k in range(int(70 * h / 0.6)):
            s = rng.uniform(0.08, 1.0)
            cu = u + lean * s ** power + rng.uniform(-0.022, 0.022)
            blob(atlas, cell, cu, v0 + h * s, rng.uniform(0.008, 0.014), rng.uniform(0.004, 0.007),
                 rng.uniform(-1.2, 1.2), foliage * rng.uniform(0.7, 1.35), thick=0.5, dome=0.35)
        for k in range(int(26 * h / 0.6)):
            s = rng.uniform(0.6, 1.0)
            cu = u + lean * s ** power + rng.uniform(-0.016, 0.016)
            blob(atlas, cell, cu, v0 + h * s, 0.009, 0.011, rng.uniform(-0.4, 0.4),
                 np.asarray(PETAL) * rng.uniform(0.8, 1.05), accent=1.0, thick=0.7, dome=0.5)


def paint_herb(atlas, cell, seed):
    """A leafy herb: rounded leaves on short stalks and a few small white flowers above them."""
    rng = random.Random(seed)
    for i in range(rng.randint(6, 8)):
        u = 0.15 + 0.7 * (i + rng.random() * 0.8) / 7
        h = rng.uniform(0.2, 0.45)
        lean = (u - 0.5) * rng.uniform(0.3, 0.7)
        stroke(atlas, cell, u, 0.0, h * 0.6, lean * 0.6, 1.5, 0.014, varied(STEM, rng, 0.1), rng, profile="stem",
               fold=0.3, veins=0.0, midrib=0.0)
        stroke(atlas, cell, u + lean * 0.6, h * 0.45, h * 0.55, lean * 0.4, 1.2, rng.uniform(0.11, 0.16),
               varied(LEAF, rng, 0.14), rng, profile="leaf", fold=0.7, midrib=0.2, veins=0.08)
    for f in range(rng.randint(3, 5)):
        u = rng.uniform(0.25, 0.75)
        h = rng.uniform(0.5, 0.7)
        stroke(atlas, cell, u, 0.0, h, rng.uniform(-0.06, 0.06), 1.5, 0.01, varied(STEM, rng, 0.1), rng,
               profile="stem", fold=0.2, veins=0.0, midrib=0.0)
        for p in range(5):
            a = p / 5 * math.tau
            blob(atlas, cell, u + math.cos(a) * 0.018, h + math.sin(a) * 0.012, 0.016, 0.01, a, PETAL, accent=1.0,
                 thick=0.8, dome=0.3)


def paint_litter(atlas, cell, seed, kind):
    """Litter seen from above: 'leaves' (broad dead leaves, brown to orange) or 'needles' (needle clusters and twigs)."""
    rng = random.Random(seed)
    if kind == "leaves":
        colours = [(0.30, 0.17, 0.07), (0.36, 0.22, 0.09), (0.22, 0.13, 0.06), (0.42, 0.30, 0.12), (0.30, 0.20, 0.10)]
        for k in range(rng.randint(9, 12)):
            cu, cv = rng.uniform(0.15, 0.85), rng.uniform(0.15, 0.85)
            a = rng.uniform(0, math.pi)
            length = rng.uniform(0.12, 0.2)
            c = np.asarray(rng.choice(colours), np.float32) * rng.uniform(0.8, 1.15)
            blob(atlas, cell, cu, cv, length, length * rng.uniform(0.45, 0.6), a, c, thick=0.25, dome=0.35,
                 texture=0.12, rng=rng, pointed=True)
            # The midrib, darker.
            blob(atlas, cell, cu, cv, length * 0.9, 0.006, a, c * 0.65, thick=0.2, dome=0.0)
    else:
        for k in range(rng.randint(3, 4)):
            u = rng.uniform(0.15, 0.85)
            stroke(atlas, cell, u, rng.uniform(0.0, 0.2), rng.uniform(0.5, 0.75), rng.uniform(-0.3, 0.3), 1.2,
                   rng.uniform(0.015, 0.025), varied(WOOD, rng, 0.2), rng, profile="stem", fold=0.3, veins=0.05,
                   midrib=0.0, thick=(0.2, 0.2))
        for k in range(rng.randint(60, 80)):
            cu, cv = rng.uniform(0.08, 0.92), rng.uniform(0.08, 0.92)
            a = rng.uniform(0, math.pi)
            c = np.asarray((0.32, 0.20, 0.08), np.float32) * rng.uniform(0.6, 1.2)
            blob(atlas, cell, cu, cv, rng.uniform(0.03, 0.05), 0.0045, a, c, thick=0.2, dome=0.0)


def paint_billboard(atlas, cell, seed, species):
    """A species' clump seen from the side, as its billboard shows it from afar: the blades of all its cards in one
    picture, roots in the middle, the outline as the cards lean out."""
    rng = random.Random(seed)
    if species in ("field", "dry", "seeding"):
        n = 34 if species != "dry" else 38
        for i in range(n):
            u = 0.5 + rng.gauss(0, 0.13)
            h = rng.uniform(0.45, 0.95) * (0.8 if species == "seeding" else 1.0)
            lean = (u - 0.5) * rng.uniform(0.6, 1.4) + rng.uniform(-0.12, 0.12)
            if species == "dry":
                palette = rng.choice((STRAW, STRAW_PALE, STRAW))
            else:
                palette = rng.choice((GREEN, GREEN, GREEN_DEEP, GREEN_YELLOW))
            stroke(atlas, cell, min(max(u, 0.12), 0.88), 0.0, h, lean, rng.uniform(1.5, 2.5),
                   rng.uniform(0.03, 0.05), varied(palette, rng, 0.14, 0.06), rng, droop=rng.uniform(0, 0.1))
        if species == "seeding":
            head = gradient((0.30, 0.30, 0.16), (0.52, 0.48, 0.28), (0.64, 0.58, 0.36))
            for i in range(5):
                u = 0.5 + rng.gauss(0, 0.1)
                h = rng.uniform(0.85, 0.97)
                stroke(atlas, cell, u, 0.0, h, rng.uniform(-0.08, 0.08), 1.8, 0.014, varied(STEM, rng, 0.1), rng,
                       profile="stem", veins=0.0, midrib=0.0)
                for g in range(12):
                    blob(atlas, cell, u + rng.uniform(-0.012, 0.012), h - 0.16 * g / 11, 0.02, 0.012, 0,
                         head(0.7), accent=1.0, thick=0.5)
    elif species == "flower":
        for i in range(10):
            u = 0.5 + rng.gauss(0, 0.13)
            stroke(atlas, cell, min(max(u, 0.12), 0.88), 0.0, rng.uniform(0.2, 0.42), (u - 0.5) * 0.8, 1.5,
                   rng.uniform(0.07, 0.11), varied(LEAF, rng, 0.12), rng, profile="leaf")
        for i in range(6):
            u = 0.5 + rng.gauss(0, 0.12)
            h = rng.uniform(0.55, 0.9)
            stroke(atlas, cell, u, 0.0, h, rng.uniform(-0.06, 0.06), 1.8, 0.014, varied(STEM, rng, 0.1), rng,
                   profile="stem", veins=0.0, midrib=0.0)
            blob(atlas, cell, u, h, 0.06, 0.03, 0, PETAL, accent=1.0, thick=0.8)
            blob(atlas, cell, u, h + 0.004, 0.022, 0.016, 0, DISC, thick=0.4)
    elif species in ("heather", "herb"):
        paint = paint_heather if species == "heather" else paint_herb
        paint(atlas, cell, seed)
    else:
        raise ValueError(species)


# ---------------------------------------------------------------- the cards

class Species:
    def __init__(self, key, height, radius, lods, cards, translucency, roughness, accent=None, far=True):
        self.key = key
        self.height = height      # nominal height of the clump at scale 1, metres
        self.radius = radius      # where the cards stand, metres from the clump's centre
        self.lods = lods          # [(cards, segments)] for LOD0, LOD1, LOD2
        self.cards = cards        # the cards, most important first
        self.translucency = translucency
        self.roughness = roughness
        self.accent = accent
        self.far = far
        self.cells = []           # the cells the species paints (variant A), for the catalogue
        self.billboard = None


def make_cards(rng, specs):
    """Cards from (count, cell, generator) groups, interleaved so that every prefix of the list holds a share of each
    group and of every direction round the clump."""
    groups = []
    for count, cell, gen in specs:
        group = []
        for i in range(count):
            card = gen(rng, i, count)
            card["cell"] = cell
            group.append(card)
        # Spread each group's directions: sort by angle, then take them in a bit-reversed order.
        group.sort(key=lambda c: c["angle"])
        order = sorted(range(len(group)), key=lambda i: int(format(i, "08b")[::-1], 2))
        groups.append([group[i] for i in order])
    out = []
    total = sum(len(g) for g in groups)
    taken = [0] * len(groups)
    for k in range(total):
        # Next from the group furthest behind its share.
        best = max(range(len(groups)), key=lambda g: (len(groups[g]) * (k + 1) / total - taken[g], -g)
                   if taken[g] < len(groups[g]) else (-1e9, 0))
        out.append(groups[best][taken[best]])
        taken[best] += 1
    return out


def card_ring(radius, height, width, lean, curve=(1.6, 2.4), up=False, height_spread=(0.78, 1.08)):
    """A generator of cards standing round a clump: each card's foot within `radius`, turned across the clump, leaning
    out by `lean` (fraction of its height), its height `height` times a draw from `height_spread`."""
    def gen(rng, i, n):
        angle = (i + rng.random() * 0.7) / n * math.tau
        r = radius * math.sqrt(rng.uniform(0.05, 1.0)) * (0.3 if up else 1.0)
        return {
            "angle": angle,
            "base": (math.cos(angle) * r, math.sin(angle) * r),
            "yaw": angle + math.pi / 2 + rng.uniform(-0.6, 0.6),
            "height": height * rng.uniform(*height_spread),
            "width": width * rng.uniform(0.85, 1.15),
            "lean": lean * rng.uniform(0.5, 1.2),
            "lean_dir": angle + rng.uniform(-0.5, 0.5),
            "curve": rng.uniform(*curve),
            "u": rng.uniform(0.0, 0.2),
            "span": rng.uniform(0.7, 0.8),
            "shade": rng.uniform(0.0, 1.0),
            "stiff": rng.uniform(0.6, 1.0),
            "flip": rng.random() < 0.5,
        }
    return gen


def flat_pieces(radius, length, count):
    def gen(rng, i, n):
        angle = rng.uniform(0, math.tau)
        r = radius * math.sqrt(rng.random())
        return {"angle": angle, "base": (math.cos(angle) * r, math.sin(angle) * r), "yaw": rng.uniform(0, math.tau),
                "length": length * rng.uniform(0.8, 1.2), "width": length * rng.uniform(0.8, 1.1), "curl": rng.uniform(0.01, 0.035),
                "shade": rng.random(), "stiff": 1.0, "flip": rng.random() < 0.5, "u": 0.0, "span": 1.0}
    return gen


class MeshData:
    def __init__(self):
        self.verts, self.faces, self.normals, self.uvs, self.colours = [], [], [], [], []

    def add(self, p, n, uv, data):
        """A vertex: position, normal, atlas UV (Godot's convention, v from the top) and its (height fraction, rank)."""
        self.verts.append(tuple(p))
        self.normals.append(tuple(n))
        self.uvs.append(uv)
        self.colours.append(data)
        return len(self.verts) - 1


def build_card(mesh, card, segments, rank, cell, taper=0.82, rounding=0.35):
    """One upright card: a strip of `segments` rows rising along its lean curve, its UV across the cell's chosen span."""
    bx, by = card["base"]
    yaw = card["yaw"]
    across = Vector((math.cos(yaw), math.sin(yaw), 0))
    ld = Vector((math.cos(card["lean_dir"]), math.sin(card["lean_dir"]), 0))
    h, w = card["height"], card["width"]
    u0, u1 = cell.uv()[0], cell.uv()[2]
    du = u1 - u0
    ua = u0 + du * card["u"]
    ub = ua + du * card["span"]
    if card["flip"]:
        ua, ub = ub, ua
    v_top, v_bottom = cell.uv()[1], cell.uv()[3]
    base = Vector((bx, by, 0))
    rows = []
    for k in range(segments + 1):
        t = k / segments
        c = base + Vector((0, 0, h * t)) + ld * (h * card["lean"] * t ** card["curve"])
        dc = Vector((0, 0, h)) + ld * (h * card["lean"] * card["curve"] * max(t, 1e-3) ** (card["curve"] - 1))
        n = across.cross(dc).normalized()
        half = 0.5 * w * (1 - (1 - taper) * t)
        row = []
        for side in (-1, 1):
            p = c + across * (half * side)
            nn = (n + across * (rounding * side)).normalized()
            uv = (ua if side < 0 else ub, v_bottom + (v_top - v_bottom) * t)
            row.append(mesh.add(p, nn, uv, (t, rank)))
        rows.append(row)
    for k in range(segments):
        a, b = rows[k]
        c, d = rows[k + 1]
        mesh.faces.append((a, b, d, c))


def build_flat(mesh, card, rank, cell, segments=3):
    """A litter piece lying on the ground, its ends curling up a little."""
    bx, by = card["base"]
    yaw = card["yaw"]
    along = Vector((math.cos(yaw), math.sin(yaw), 0))
    across = Vector((-along.y, along.x, 0))
    u0, v_top, u1, v_bottom = cell.uv()
    if card["flip"]:
        u0, u1 = u1, u0
    rows = []
    for k in range(segments + 1):
        t = k / segments
        x = (t - 0.5) * card["length"]
        z = card["curl"] * (2 * t - 1) ** 2 + 0.004 * rank
        c = Vector((bx, by, 0)) + along * x + Vector((0, 0, z))
        row = []
        for side in (-1, 1):
            p = c + across * (0.5 * card["width"] * side)
            uv = (u0 if side < 0 else u1, v_bottom + (v_top - v_bottom) * t)
            row.append(mesh.add(p, (0, 0, 1), uv, (0.0, rank)))
        rows.append(row)
    for k in range(segments):
        a, b = rows[k]
        c, d = rows[k + 1]
        mesh.faces.append((a, b, d, c))


def build_far(mesh, species, cell):
    """The billboard: two quads crossed at right angles, each the width of the clump, with the side view."""
    w = species.radius * 2 + species.height * 0.35
    h = species.height * 1.02
    u0, v_top, u1, v_bottom = cell.uv()
    for q, angle in enumerate((0.3, 0.3 + math.pi / 2)):
        across = Vector((math.cos(angle), math.sin(angle), 0))
        n = across.cross(Vector((0, 0, 1))).normalized()
        rows = []
        for k in range(2):
            row = []
            for side in (-1, 1):
                p = across * (0.5 * w * side) + Vector((0, 0, h * k))
                nn = (n * 0.45 + Vector((0, 0, 1))).normalized()
                row.append(mesh.add(p, nn, (u0 if side < 0 else u1, v_bottom + (v_top - v_bottom) * k),
                                    (float(k), q * 0.5)))
            rows.append(row)
        a, b = rows[0]
        c, d = rows[1]
        mesh.faces.append((a, b, d, c))


def to_object(mesh, name):
    me = bpy.data.meshes.new(name)
    me.from_pydata(mesh.verts, [], mesh.faces)
    me.update()
    uv = me.uv_layers.new(name="UVMap")
    for poly in me.polygons:
        for li in poly.loop_indices:
            vi = me.loops[li].vertex_index
            u, v = mesh.uvs[vi]
            # Blender's v runs up from the image's foot; glTF (and Godot) read it from the top.
            uv.data[li].uv = (u, 1 - v)
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    data = me.uv_layers.new(name="UV2")
    for poly in me.polygons:
        for li in poly.loop_indices:
            t, rank = mesh.colours[me.loops[li].vertex_index]
            # Stored as Blender reads UVs, so glTF's flip gives the shader rank itself.
            data.data[li].uv = (t, 1 - rank)
    me.uv_layers.active = uv
    uv.active_render = True
    me.shade_smooth()
    me.normals_split_custom_set_from_vertices([Vector(n) for n in mesh.normals])
    return o


def triangles(mesh):
    return 2 * len(mesh.faces)


# ---------------------------------------------------------------- the species

def species_table():
    """The five clumps and the litter: sizes, levels of detail (cards, segments) and their cards."""
    table = []
    rng = random.Random(4011)

    field = Species("field", 0.46, 0.17, [(12, 4), (7, 2), (4, 1)], None, translucency=0.9, roughness=0.74)
    field.cells = [Cell(0, 0)]
    field.cards = make_cards(rng, [(12, field.cells[0], card_ring(0.27, 0.46, 0.22, 0.36, height_spread=(0.6, 1.15)))])
    field.billboard = Cell(0, 2)
    table.append(field)

    seeding = Species("seeding", 0.85, 0.15, [(10, 5), (6, 3), (3, 1)], None, translucency=0.85, roughness=0.72,
                      accent="seed")
    seeding.cells = [Cell(2, 0), Cell(6, 0)]
    seeding.cards = make_cards(rng, [(5, seeding.cells[0], card_ring(0.24, 0.5, 0.22, 0.36)),
                                     (5, seeding.cells[1], card_ring(0.08, 0.85, 0.16, 0.12, curve=(1.4, 2.0), up=True))])
    seeding.billboard = Cell(2, 2)
    table.append(seeding)

    dry = Species("dry", 0.42, 0.17, [(11, 4), (6, 2), (4, 1)], None, translucency=0.45, roughness=0.8)
    dry.cells = [Cell(4, 0)]
    dry.cards = make_cards(rng, [(11, dry.cells[0], card_ring(0.26, 0.42, 0.22, 0.4, height_spread=(0.6, 1.1)))])
    dry.billboard = Cell(4, 2)
    table.append(dry)

    flower = Species("flower", 0.5, 0.15, [(10, 4), (6, 2), (3, 1)], None, translucency=0.75, roughness=0.7,
                     accent="petal")
    flower.cells = [Cell(2, 1), Cell(0, 1)]
    flower.cards = make_cards(rng, [(5, flower.cells[0], card_ring(0.15, 0.24, 0.24, 0.5, curve=(1.3, 1.8))),
                                    (5, flower.cells[1], card_ring(0.07, 0.5, 0.18, 0.08, up=True))])
    flower.billboard = Cell(6, 2)
    table.append(flower)

    herb = Species("herb", 0.34, 0.16, [(9, 3), (6, 2), (3, 1)], None, translucency=0.5, roughness=0.74,
                   accent="bell")
    herb.cells = [Cell(4, 1)]
    herb.cards = make_cards(rng, [(9, herb.cells[0], card_ring(0.16, 0.34, 0.22, 0.28, curve=(1.2, 1.7)))])
    herb.billboard = Cell(0, 3)
    table.append(herb)

    litter = Species("litter", 0.04, 0.2, [(3, 3)], None, translucency=0.2, roughness=0.8, far=False)
    litter.cells = [Cell(6, 1, rows=0.5)]
    litter.cards = make_cards(rng, [(3, litter.cells[0], flat_pieces(0.2, 0.42, 3))])
    table.append(litter)
    return table


def paint_atlas():
    atlas = Atlas()
    for variant, col in ((0, 0), (1, 1)):
        paint_field(atlas, Cell(col, 0), 100 + variant)
        paint_seeding_blades(atlas, Cell(col + 2, 0), 200 + variant)
        paint_dry(atlas, Cell(col + 4, 0), 300 + variant)
        paint_seed_stems(atlas, Cell(col + 6, 0), 400 + variant, ("spike", "panicle")[variant])
        paint_flower_stalks(atlas, Cell(col, 1), 500 + variant, ("daisy", "cluster")[variant])
        paint_flower_leaves(atlas, Cell(col + 2, 1), 600 + variant)
    paint_heather(atlas, Cell(4, 1), 700)
    paint_herb(atlas, Cell(5, 1), 701)
    paint_litter(atlas, Cell(6, 1, rows=0.5), 800, "leaves")
    paint_litter(atlas, Cell(7, 1, rows=0.5), 801, "leaves")
    paint_litter(atlas, Cell(6, 1, rows=0.5, top=0.5), 802, "needles")
    paint_litter(atlas, Cell(7, 1, rows=0.5, top=0.5), 803, "needles")
    for variant in (0, 1):
        paint_billboard(atlas, Cell(0 + variant, 2), 900 + variant, "field")
        paint_billboard(atlas, Cell(2 + variant, 2), 910 + variant, "seeding")
        paint_billboard(atlas, Cell(4 + variant, 2), 920 + variant, "dry")
        paint_billboard(atlas, Cell(6 + variant, 2), 930 + variant, "flower")
    paint_billboard(atlas, Cell(0, 3), 940, "heather")
    paint_billboard(atlas, Cell(1, 3), 941, "herb")
    return atlas


def write_textures(atlas):
    OUT.mkdir(parents=True, exist_ok=True)
    a = np.clip(atlas.a, 0, 1)
    straight = atlas.rgb / np.maximum(a, 1e-4)[..., None]
    albedo = np.concatenate([srgb(straight), a[..., None]], axis=2)
    # Godot bleeds colour into the cut-out pixels (fix_alpha_border); the detail map's empty pixels stay flat.
    n = atlas.n / np.maximum(a, 1e-4)[..., None]
    m = atlas.m / np.maximum(a, 1e-4)
    t = atlas.t / np.maximum(a, 1e-4)
    covered = a > 1e-3
    detail = np.zeros((SIZE, SIZE, 4), np.float32)
    detail[..., 0] = np.where(covered, np.clip(n[..., 0] * 0.5 + 0.5, 0, 1), 0.5)
    detail[..., 1] = np.where(covered, np.clip(n[..., 1] * 0.5 + 0.5, 0, 1), 0.5)
    detail[..., 2] = np.where(covered, np.clip(m, 0, 1), 0)
    detail[..., 3] = np.where(covered, np.clip(t, 0, 1), 0)
    albedo_path = write_png(OUT / "grass_albedo.png", albedo, channels=4)
    detail_path = write_png(OUT / "grass_detail.png", detail, channels=4)
    write_texture_import(albedo_path)
    write_texture_import(detail_path)
    return albedo_path, detail_path


def build_meshes(table):
    objects = []
    facts = []
    for sp in table:
        lods = []
        for level, (count, segments) in enumerate(sp.lods):
            mesh = MeshData()
            for i, card in enumerate(sp.cards[:count]):
                rank = i / len(sp.cards)
                if sp.key == "litter":
                    build_flat(mesh, card, rank, card["cell"])
                else:
                    build_card(mesh, card, segments, rank, card["cell"])
            name = f"{sp.key}_lod{level}"
            objects.append(to_object(mesh, name))
            lods.append({"node": name, "cards": count, "segments": segments, "triangles": triangles(mesh),
                         "vertices": len(mesh.verts)})
        far = None
        if sp.far:
            mesh = MeshData()
            build_far(mesh, sp, sp.billboard)
            name = f"{sp.key}_far"
            objects.append(to_object(mesh, name))
            far = {"node": name, "triangles": triangles(mesh), "vertices": len(mesh.verts)}
        verts = [v for o in objects if o.name.startswith(sp.key + "_lod0") for v in o.data.vertices]
        height = max(v.co.z for v in verts)
        reach = max(math.hypot(v.co.x, v.co.y) for v in verts)
        facts.append((sp, lods, far, height, reach))
        log(f"{sp.key}: LODs {[l['triangles'] for l in lods]} triangles, far {far['triangles'] if far else '-'}, "
            f"height {height:.2f} m, reach {reach:.2f} m")
    return objects, facts


def export(objects):
    path = OUT / "grass.glb"
    select_only(objects)
    bpy.ops.export_scene.gltf(
        filepath=str(path), export_format="GLB", use_selection=True, export_image_format="NONE",
        export_materials="NONE", export_vertex_color="ACTIVE", export_all_vertex_colors=False,
        export_yup=True, export_apply=False, export_texcoords=True, export_normals=True, export_tangents=False,
        export_animations=False, export_extras=False, export_cameras=False, export_lights=False)
    write_scene_import(path, {}, False, {"meshes/create_shadow_meshes": "false", "meshes/ensure_tangents": "true",
                                         "meshes/light_baking": "0"})
    log(f"exported {path.name}: {len(objects)} meshes, {path.stat().st_size / 1e6:.2f} MB")
    return path


def write_catalogue(atlas, facts, albedo, detail, glb):
    species = []
    for sp, lods, far, height, reach in facts:
        means = [atlas.mean(c) for c in sp.cells]
        species.append({
            "key": sp.key,
            "height": round(height, 4),
            "reach": round(reach, 4),
            "lods": lods,
            "far": far,
            "translucency": sp.translucency,
            "roughness": sp.roughness,
            "accent": sp.accent,
            # The coverage-weighted mean linear colour of the blades the species' cards carry (variant A and B
            # together), which the shader divides by so that the clump's mean colour is the place's grass colour.
            "mean": [round(sum(m[c] for m in means) / len(means), 4) for c in range(3)],
            "coverage": [atlas.coverage(c) for c in sp.cells],
            "cells": [list(c.uv()) for c in sp.cells],
            "billboard": list(sp.billboard.uv()) if sp.billboard else None,
        })
    catalogue = {
        "source": "Hollow (art/grass/build_grass.py): our own, painted and built from text",
        "albedo": res_path(albedo),
        "detail": res_path(detail),
        "model": res_path(glb),
        "variantOffset": VARIANT_OFFSET,
        "species": species,
    }
    path = OUT / "grass.json"
    path.write_text(json.dumps(catalogue, indent=2) + "\n", encoding="utf-8", newline="\n")
    log(f"wrote {path.name}")


def main():
    reset()
    atlas = paint_atlas()
    albedo, detail = write_textures(atlas)
    table = species_table()
    objects, facts = build_meshes(table)
    glb = export(objects)
    write_catalogue(atlas, facts, albedo, detail, glb)


if __name__ == "__main__":
    main()

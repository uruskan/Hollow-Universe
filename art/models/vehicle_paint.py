"""The TOGG's and the Zamazingo's texture atlases (docs/VEHICLES.md §7): painted here from noise, no downloaded pixels.

Each vehicle has one 2048 px atlas for every part of it. The painter is the KA-FA's (`ka_fa_1500_paint.Painter`: value
noise, worn paint chipped to bare steel, rust blooming where it is thin, scratches), given each vehicle's own regions; this
file adds what a road vehicle needs: a stencil font for the names on the doors and the tank, a radiator's core, the tyres'
moulded sidewalls, zinc-plated dampers, a drive chain and a gripper seat. Regions are (x, y, w, h) in top-down image
coordinates, as `hollow.component_mesh.ComponentMesh` maps them. `togg()` and `zamazingo()` return albedo, ORM and height.
"""
import math

import numpy as np

import ka_fa_1500_paint as base

N = 2048

TOGG_REGIONS = {
    'paint': (.005, .005, .32, .19), 'cream': (.335, .005, .32, .19), 'frame': (.665, .005, .33, .19),
    'steel': (.005, .205, .19, .14), 'dark': (.205, .205, .19, .14), 'alu': (.405, .205, .19, .14),
    'rubber': (.605, .205, .19, .14), 'zinc': (.805, .205, .19, .14),
    'seat': (.005, .355, .19, .14), 'tread': (.205, .355, .19, .14), 'red': (.405, .355, .19, .14),
    'rust': (.605, .355, .19, .14), 'heat': (.805, .355, .19, .14),
    'door_l': (.005, .505, .32, .19), 'door_r': (.335, .505, .32, .19), 'radiator': (.665, .505, .16, .19),
    'block': (.835, .505, .16, .19),
    'sidewall': (.005, .705, .49, .10), 'cable': (.505, .705, .24, .10), 'hose': (.755, .705, .24, .10),
    'weld': (.005, .815, .19, .18), 'gauge': (.205, .815, .09, .09), 'lamp': (.305, .815, .06, .06),
    'lamp_red': (.375, .815, .06, .06), 'lamp_amber': (.445, .815, .06, .06), 'tape': (.205, .91, .19, .085),
    'canvas': (.405, .905, .19, .09), 'oil': (.605, .815, .19, .18), 'mesh': (.805, .815, .19, .18),
}

ZAMAZINGO_REGIONS = {
    'tank': (.005, .005, .32, .19), 'plastic': (.335, .005, .32, .19), 'frame': (.665, .005, .33, .19),
    'steel': (.005, .205, .19, .14), 'dark': (.205, .205, .19, .14), 'alu': (.405, .205, .19, .14),
    'rubber': (.605, .205, .19, .14), 'chrome': (.805, .205, .19, .14),
    'seat': (.005, .355, .19, .14), 'chain': (.205, .355, .19, .14), 'red': (.405, .355, .19, .14),
    'rust': (.605, .355, .19, .14), 'heat': (.805, .355, .19, .14),
    'tank_l': (.005, .505, .32, .19), 'tank_r': (.335, .505, .32, .19), 'number': (.665, .505, .16, .19),
    'block': (.835, .505, .16, .19),
    'sidewall': (.005, .705, .49, .10), 'cable': (.505, .705, .24, .10), 'hose': (.755, .705, .24, .10),
    'weld': (.005, .815, .19, .18), 'gauge': (.205, .815, .09, .09), 'lamp': (.305, .815, .06, .06),
    'lamp_red': (.375, .815, .06, .06), 'lamp_amber': (.445, .815, .06, .06), 'tape': (.205, .91, .19, .085),
    'zinc': (.405, .905, .19, .09), 'silencer': (.605, .815, .19, .18), 'disc': (.805, .815, .19, .18),
}


class Painter(base.Painter):
    """The KA-FA's painter on a vehicle's own regions."""

    def __init__(self, regions, seed):
        super().__init__(seed)
        self.regions = regions

    def window(self, name):
        x, y, w, h = self.regions[name]
        return slice(int(y * N), int((y + h) * N)), slice(int(x * N), int((x + w) * N))

    def size(self, name):
        ys, xs = self.window(name)
        return ys.stop - ys.start, xs.stop - xs.start


# ---------------------------------------------------------------- a stencil font (only the letters the names need)


def _arc(cx, cy, rx, ry, a0, a1, steps=14):
    return [(cx + rx * math.cos(math.radians(a0 + (a1 - a0) * k / steps)), cy + ry * math.sin(math.radians(a0 + (a1 - a0) * k / steps)))
            for k in range(steps + 1)]


# Strokes in a unit box (x right, y up), with the gaps a stencil's bridges leave.
GLYPHS = {
    'T': ([(0, 1), (1, 1)], [(.5, .86), (.5, 0)]),
    'O': (_arc(.5, .5, .5, .5, 104, 256), _arc(.5, .5, .5, .5, -76, 76)),
    'G': (_arc(.5, .5, .5, .5, 104, 256), _arc(.5, .5, .5, .5, 284, 330), _arc(.5, .5, .5, .5, 40, 76), [(.56, .44), (.98, .44), (.98, .18)]),
    'Z': ([(0, 1), (.9, 1)], [(.97, .9), (.03, .1)], [(.1, 0), (1, 0)]),
    'A': ([(0, 0), (.45, 1)], [(.55, 1), (1, 0)], [(.27, .36), (.73, .36)]),
    'M': ([(0, 0), (0, 1)], [(.09, .97), (.46, .38)], [(.54, .38), (.91, .97)], [(1, 1), (1, 0)]),
    'I': ([(.5, 0), (.5, 1)],),
    'N': ([(0, 0), (0, 1)], [(.1, .97), (.9, .03)], [(1, 0), (1, 1)]),
    'H': ([(0, 0), (0, 1)], [(1, 0), (1, 1)], [(.14, .5), (.86, .5)]),
    'L': ([(0, 1), (0, .12)], [(.12, 0), (1, 0)]),
    'W': ([(0, 1), (.22, 0)], [(.3, .04), (.5, .62)], [(.5, .62), (.7, .04)], [(.78, 0), (1, 1)]),
    '7': ([(0, 1), (1, 1)], [(.96, .9), (.42, 0)]),
    ' ': (),
}
WIDTH = {'I': .3, ' ': .55}


def text_mask(shape, text, box, stroke, mirror=False, slant=0.0):
    """A mask (0..1) of `text` set in the stencil font inside `box` = (u0, v0, u1, v1) of a region (v down), its strokes
    `stroke` of the letters' height thick; `mirror` sets it backwards (for a panel seen from the other side)."""
    h, w = shape
    vv, uu = np.mgrid[0:h, 0:w].astype(np.float32)
    uu, vv = (uu + .5) / w, (vv + .5) / h
    u0, v0, u1, v1 = box
    height = v1 - v0
    advance = [WIDTH.get(c, .78) for c in text]
    gap = .22
    total = sum(advance) + gap * (len(text) - 1)
    # Letters as tall as the box allows, narrowed (in u) to fit its width; aspect is the region's pixels.
    letter_h = min(height, (u1 - u0) / total * w / h)
    scale_u = letter_h * h / w
    start = u0 + ((u1 - u0) - scale_u * total) / 2
    mask = np.zeros(shape, np.float32)
    x = start
    half = stroke * letter_h / 2
    for c, a in zip(text, advance):
        for line in GLYPHS.get(c, ()):
            base_v = v1 - (height - letter_h) / 2
            pts = [(x + px * a * scale_u + slant * py * letter_h, base_v - py * letter_h) for px, py in line]
            for (ax, ay), (bx, by) in zip(pts, pts[1:]):
                lo_u, hi_u = min(ax, bx) - half * 3, max(ax, bx) + half * 3
                lo_v, hi_v = min(ay, by) - half * 3, max(ay, by) + half * 3
                i0, i1 = max(0, int(lo_v * h)), min(h, int(hi_v * h) + 2)
                j0, j1 = max(0, int(lo_u * w)), min(w, int(hi_u * w) + 2)
                if i0 >= i1 or j0 >= j1:
                    continue
                su, sv = uu[i0:i1, j0:j1], vv[i0:i1, j0:j1]
                # Distance in the letters' own proportions (v scaled to u by the region's aspect).
                dx, dy = bx - ax, (by - ay) * h / w
                px, py = su - ax, (sv - ay) * h / w
                t = np.clip((px * dx + py * dy) / max(dx * dx + dy * dy, 1e-9), 0, 1)
                d = np.hypot(px - t * dx, py - t * dy)
                edge = half * h / w
                mask[i0:i1, j0:j1] = np.maximum(mask[i0:i1, j0:j1], np.clip((edge - d) / (edge * .18) + .5, 0, 1))
        x += (a + gap) * scale_u
    return mask[:, ::-1].copy() if mirror else mask


def stencil(p, name, text, box, colour, stroke=.17, mirror=False, wear=.7):
    """Sprays `text` through a stencil onto a painted region: overspray at the edges, flaked where the paint is worn."""
    ys, xs = p.window(name)
    shape = (ys.stop - ys.start, xs.stop - xs.start)
    mask = text_mask(shape, text, box, stroke, mirror)
    over = text_mask(shape, text, box, stroke * 1.5, mirror) * .18
    flake = np.clip(p.field(shape, [(40, 1), (120, .5)]) * 2 + wear, 0, 1)
    p.blend(ys, xs, over * flake, colour, .62, 0)
    p.blend(ys, xs, mask * flake, colour, .58, 0, .002)


# ---------------------------------------------------------------- shared surfaces


def _common(p, rubber_tint=(.045, .045, .047)):
    rng = p.rng
    ys, xs, cloud = p.base('steel', (.52, .52, .51), .36, 1, [(7, .5), (29, .2)])
    uu, vv = p.uv(ys, xs)
    p.albedo[ys, xs, :3] *= (1 + np.sin(vv * 1500) * .02)[:, :, None]
    p.scratches(ys, xs, 80, .08, .12)
    ys, xs, cloud = p.base('dark', (.055, .055, .06), .55, .2, [(6, .5), (23, .25)])
    oil = np.clip(p.field(cloud.shape, [(9, 1), (31, .4)]) - .3, 0, 1)
    p.blend(ys, xs, oil * .8, (.02, .02, .018), .25, .3)
    p.scratches(ys, xs, 50, .14, .15, .9)
    ys, xs, cloud = p.base('alu', (.56, .56, .54), .55, .9, [(4, .4), (15, .2)])
    p.albedo[ys, xs, :3] *= (1 + rng.uniform(-.05, .05, cloud.shape).astype(np.float32))[:, :, None]
    p.height[ys, xs] += rng.uniform(-.008, .008, cloud.shape).astype(np.float32)
    soot = np.clip(p.field(cloud.shape, [(5, 1)]) - .2, 0, 1)
    p.blend(ys, xs, soot * .4, (.14, .12, .10), .7, .3)
    # Rubber: dusty at the edges of the region's noise, a faint mould-release sheen.
    ys, xs, cloud = p.base('rubber', rubber_tint, .86, 0, [(8, .5), (31, .2)])
    dust = np.clip(p.field(cloud.shape, [(4, 1), (13, .5)]) * .8 + .1, 0, 1)
    p.blend(ys, xs, dust * .35, (.20, .17, .13), .95, 0)
    # Moulded sidewall: a strip (u round the tyre, v from the rim outward): rings, the brand raised twice round, lugs.
    ys, xs, cloud = p.base('sidewall', rubber_tint, .84, 0, [(9, .3)])
    uu, vv = p.uv(ys, xs)
    for v, width in ((.10, .012), (.16, .006), (.80, .008)):
        ring = np.exp(-((vv - v) / width) ** 2)
        p.blend(ys, xs, ring * .5, (.07, .07, .07), .7, 0, .03)
    letters = np.zeros(cloud.shape, np.float32)
    for u0 in (.06, .56):
        letters = np.maximum(letters, text_mask(cloud.shape, 'HOLLOW', (u0, .30, u0 + .34, .62), .2))
    p.blend(ys, xs, letters, (.085, .085, .085), .62, 0, .06)
    ticks = (np.abs(((uu * 120) % 1) - .5) < .12) & (vv > .86)
    p.blend(ys, xs, ticks * 1.0, (.06, .06, .06), .8, 0, .04)
    p.blend(ys, xs, np.clip(p.field(cloud.shape, [(6, 1)]) + .2, 0, 1) * .3, (.22, .19, .15), .95, 0)
    # Red: the Tin Finch's fuel-can red, faded (springs, the jerry can, the tow hooks).
    p.worn_paint('red', (.52, .08, .05), chip_level=.70, rust=.72, scratch=30)
    ys, xs, cloud = p.base('rust', (.30, .16, .08), .85, .2, [(5, .6), (18, .3), (47, .16)], variation=.3)
    pits = np.clip(p.field(cloud.shape, [(120, 1), (300, .5)]) - .55, 0, 1) * 4
    p.blend(ys, xs, pits, (.18, .09, .05), .95, .1, -.03)
    ys, xs, cloud = p.base('heat', (.55, .47, .33), .9, 0, [(6, .3)])
    uu, vv = p.uv(ys, xs)
    weave = np.sin(uu * 400) * np.sin(vv * 80 + uu * 40)
    p.albedo[ys, xs, :3] *= (1 + weave * .08)[:, :, None]
    p.height[ys, xs] += weave * .02
    p.blend(ys, xs, np.clip(p.field(cloud.shape, [(5, 1)]) + .1, 0, 1) * .7, (.12, .10, .08), .9, 0)
    p.base('cable', (.03, .03, .035), .55, 0, [(9, .4)])
    ys, xs, cloud = p.base('hose', (.05, .05, .055), .7, 0, [(9, .4)])
    uu, vv = p.uv(ys, xs)
    p.height[ys, xs] += (np.sin(uu * 300) > .6) * .01
    ys, xs, cloud = p.base('weld', (.33, .33, .35), .5, .9, [(30, .6), (90, .4)], variation=.3)
    uu, vv = p.uv(ys, xs)
    p.height[ys, xs] += np.sin(uu * 300) * .03
    p.blend(ys, xs, np.clip(np.sin(uu * 7) * .5 + .2, 0, 1) * .4, (.20, .22, .40), .5, .9)
    # Gauges: a cream dial, ticks and a needle.
    ys, xs, cloud = p.base('gauge', (.80, .77, .66), .25, 0, [(3, .2)])
    uu, vv = p.uv(ys, xs)
    r = np.hypot(uu - .5, vv - .5)
    ang = np.arctan2(vv - .5, uu - .5)
    ticks = (r > .34) & (r < .42) & (np.abs(((ang / math.tau * 24) % 1) - .5) > .42) & (ang < 2.4) | \
            ((r > .30) & (r < .42) & (np.abs(((ang / math.tau * 6) % 1) - .5) > .46))
    needle = (np.abs((uu - .5) * math.sin(-2.2) - (vv - .5) * math.cos(-2.2)) < .012) & (r < .36) & \
             ((uu - .5) * math.cos(-2.2) + (vv - .5) * math.sin(-2.2) > 0)
    p.blend(ys, xs, ticks * 1.0, (.05, .05, .05))
    p.blend(ys, xs, needle * 1.0, (.55, .08, .04))
    p.blend(ys, xs, (r > .46) * 1.0, (.08, .08, .08), .4, .8)
    # Lenses: fluted glass over a reflector.
    for name, colour in (('lamp', (.95, .86, .62)), ('lamp_red', (.78, .06, .04)), ('lamp_amber', (.92, .48, .06))):
        ys, xs, cloud = p.base(name, colour, .12, 0, [(3, .2)])
        uu, vv = p.uv(ys, xs)
        r = np.hypot(uu - .5, vv - .5)
        p.blend(ys, xs, ((np.sin(uu * 70) > .6) & (r < .45)) * .25, tuple(min(1, c * 1.2 + .1) for c in colour))
        p.blend(ys, xs, (np.abs(r - .2) < .02) * .3, tuple(c * .7 for c in colour))
    # Hazard tape.
    ys, xs, cloud = p.base('tape', (.80, .62, .08), .55, 0, [(6, .3)])
    uu, vv = p.uv(ys, xs)
    p.blend(ys, xs, (((uu * 8 + vv * 4) % 1) < .5) * 1.0, (.04, .04, .04), .5, 0)
    p.blend(ys, xs, np.clip(p.field(cloud.shape, [(40, 1), (120, .5)]) - .55, 0, 1) * 3, (.35, .32, .25), .8, 0, -.005)
    # Bolt-on blocks (cast iron painted, an engine's or a hub's): see each vehicle.


def _zinc(p, name):
    """Yellow zinc plating: gold to green-blue where it was thin, dull with road film."""
    ys, xs, cloud = p.base(name, (.62, .55, .32), .32, 1, [(5, .5), (17, .25)], variation=.18)
    tint = p.field(cloud.shape, [(3, 1), (9, .5)])
    p.blend(ys, xs, np.clip(tint, 0, 1) * .25, (.48, .54, .42), .3, 1)
    p.blend(ys, xs, np.clip(-tint, 0, 1) * .2, (.68, .48, .28), .3, 1)
    p.blend(ys, xs, np.clip(p.field(cloud.shape, [(8, 1)]) + .1, 0, 1) * .4, (.18, .16, .12), .7, .4)


def _seat(p, colour, ribbed=False):
    ys, xs, cloud = p.base('seat', colour, .55, 0, [(6, .4), (20, .2)])
    uu, vv = p.uv(ys, xs)
    if ribbed:
        # A gripper seat: ribs across, a welt round its edge.
        ribs = (np.sin(vv * 90) > .55).astype(np.float32)
        p.blend(ys, xs, ribs * .6, tuple(c * .6 for c in colour), .7, 0, .03)
        welt = np.minimum(np.minimum(uu, 1 - uu), np.minimum(vv, 1 - vv)) < .03
        p.blend(ys, xs, welt * 1.0, tuple(c * 1.5 for c in colour), .5, 0, .02)
    else:
        seams = (np.abs(((uu * 4) % 1) - .5) > .485) | (np.abs(((vv * 3) % 1) - .5) > .485)
        stitches = seams & (np.sin((uu + vv) * 900) > .3)
        p.blend(ys, xs, seams * .6, tuple(c * .55 for c in colour), .7, 0, -.03)
        p.blend(ys, xs, stitches * 1.0, (.55, .50, .40), .7, 0, .01)
        patch = ((uu > .58) & (uu < .86) & (vv > .55) & (vv < .80)).astype(np.float32)
        p.blend(ys, xs, patch, (.42, .43, .44), .45, .4, .01)
    cracks = np.clip(p.field(cloud.shape, [(60, 1), (150, .4)]) - .75, 0, 1) * 5
    p.blend(ys, xs, cracks, tuple(c * .45 for c in colour), .8, 0, -.02)


def _block(p, colour):
    """A cast part painted (an engine block, a hub): sand-cast speckle under the paint, oil weeping down, grime."""
    ys, xs, cloud = p.worn_paint('block', colour, cells=((4, .5), (13, .3), (41, .1)), chip_level=.72, rust=.75, scratch=20)
    p.height[ys, xs] += p.rng.uniform(-.012, .012, cloud.shape).astype(np.float32)
    uu, vv = p.uv(ys, xs)
    streaks = np.clip(p.field(cloud.shape, [(30, 1)]) * np.clip(vv * 1.4, 0, 1) - .1, 0, 1) * \
        np.clip(np.sin(uu * 60 + p.field(cloud.shape, [(5, 3)])) * 2, 0, 1)
    p.blend(ys, xs, streaks * .7, (.08, .06, .04), .25, .2)


def togg():
    p = Painter(TOGG_REGIONS, 46071)
    _common(p)
    # The TOGG's own paint: a deep sea green, sun-faded on top (low v), chipped to steel and rusting at the edges.
    ys, xs, cloud = p.worn_paint('paint', (.14, .32, .33), chip_level=.66, rust=.58, scratch=60)
    uu, vv = p.uv(ys, xs)
    p.blend(ys, xs, np.clip(.5 - vv, 0, 1) * .5, (.30, .44, .43), .7, 0)
    for name, mirror in (('door_l', False), ('door_r', True)):
        ys, xs, cloud = p.worn_paint(name, (.14, .32, .33), chip_level=.66, rust=.58, scratch=60)
        uu, vv = p.uv(ys, xs)
        p.blend(ys, xs, np.clip(.4 - vv, 0, 1) * .5, (.30, .44, .43), .7, 0)
        # A pressed bead along the panel, then the name sprayed through a stencil, a little skew.
        bead = np.exp(-((vv - .78) / .012) ** 2)
        p.blend(ys, xs, bead * .3, (.10, .24, .25), .6, 0, .05)
        stencil(p, name, 'TOGG', (.14, .34, .86, .66), (.86, .83, .72), mirror=mirror)
        # Rust weeping from the lower edge, dents' scuffs.
        weep = np.clip(p.field(cloud.shape, [(25, 1)]) + (vv - .82) * 4, 0, 1) * np.clip(np.sin(uu * 80) + .4, 0, 1)
        p.blend(ys, xs, weep * .6, (.32, .14, .06), .85, .1, .004)
    # Cream: the engine's and the rims' (the Tin Finch's tanks).
    ys, xs, cloud = p.worn_paint('cream', (.80, .74, .60), chip_level=.70, rust=.66, scratch=50)
    grime = np.clip(p.field(cloud.shape, [(4, 1), (12, .5)]) + .1, 0, 1)
    p.blend(ys, xs, grime * .35, (.38, .32, .22), .75, 0)
    # The frame and the arms: black chassis paint over steel, chipped where stones hit, rust in the chips.
    ys, xs, cloud = p.worn_paint('frame', (.055, .057, .06), chip_level=.60, rust=.50, scratch=90)
    p.blend(ys, xs, np.clip(p.field(cloud.shape, [(5, 1), (19, .4)]) + .2, 0, 1) * .45, (.24, .20, .15), .9, 0)
    _zinc(p, 'zinc')
    _seat(p, (.20, .15, .10))
    _block(p, (.80, .74, .60))
    # Diamond plate.
    ys, xs, cloud = p.base('tread', (.46, .46, .45), .42, 1, [(5, .4), (19, .2)])
    uu, vv = p.uv(ys, xs)
    a, b = (uu * 30 + vv * 18) % 1, (uu * 30 - vv * 18) % 1
    lozenge = ((np.abs(a - .5) < .08) & (np.abs(b - .5) < .35)) | ((np.abs(b - .5) < .08) & (np.abs(a - .5) < .35))
    p.height[ys, xs] += lozenge * .06
    p.blend(ys, xs, np.clip(cloud * 1.5 - .5, 0, 1) * .6, (.28, .16, .09), .8, .2)
    # The radiator's core: fins on edge, flat tubes across, a few bent fins and bugs, a dark gap behind.
    ys, xs, cloud = p.base('radiator', (.20, .20, .19), .5, .8, [(6, .4)])
    uu, vv = p.uv(ys, xs)
    fins = (np.abs(((uu * 140) % 1) - .5) < .22).astype(np.float32)
    tubes = (np.abs(((vv * 22) % 1) - .5) < .14).astype(np.float32)
    p.blend(ys, xs, fins * .7, (.42, .40, .36), .45, .9, .02)
    p.blend(ys, xs, tubes, (.30, .28, .25), .5, .9, .04)
    bent = np.clip(p.field(cloud.shape, [(30, 1)]) - .6, 0, 1) * 4
    p.blend(ys, xs, bent, (.12, .11, .10), .7, .5, -.02)
    # Mud and oil grime (the underside's blend surface).
    ys, xs, cloud = p.base('oil', (.16, .13, .10), .8, .1, [(5, .6), (17, .4), (60, .2)], variation=.3)
    p.blend(ys, xs, np.clip(p.field(cloud.shape, [(9, 1)]) - .2, 0, 1), (.05, .045, .04), .35, .3)
    # Canvas (the seat's cushion straps, a tarp over a spare): olive duck, sun-bleached.
    ys, xs, cloud = p.base('canvas', (.30, .30, .20), .9, 0, [(6, .3)])
    uu, vv = p.uv(ys, xs)
    p.albedo[ys, xs, :3] *= (1 + np.sin(uu * 600) * np.sin(vv * 600) * .06)[:, :, None]
    # Expanded mesh (the grille guard's screen): a lozenge lattice over a dark gap.
    ys, xs, cloud = p.base('mesh', (.03, .03, .03), .8, 0, [(4, .2)])
    uu, vv = p.uv(ys, xs)
    a, b = (uu * 14 + vv * 7) % 1, (uu * 14 - vv * 7) % 1
    strand = (np.minimum(a, 1 - a) < .09) | (np.minimum(b, 1 - b) < .09)
    p.blend(ys, xs, strand * 1.0, (.07, .07, .075), .5, .5, .05)
    return np.clip(p.albedo, 0, 1), np.clip(p.orm, 0, 1), p.height


def zamazingo():
    p = Painter(ZAMAZINGO_REGIONS, 46093)
    _common(p)
    # The tank and its side panels: a faded mustard over primer, chipped, sun-bleached on top.
    for name, mirror in (('tank', None), ('tank_l', False), ('tank_r', True)):
        ys, xs, cloud = p.worn_paint(name, (.66, .48, .10), chip_level=.68, rust=.66, scratch=60)
        uu, vv = p.uv(ys, xs)
        p.blend(ys, xs, np.clip(.45 - vv, 0, 1) * .6, (.80, .66, .30), .7, 0)
        if mirror is not None:
            # A black band along the tank, the name sprayed across it in cream.
            band = ((vv > .30) & (vv < .70)).astype(np.float32)
            p.blend(ys, xs, band * .92, (.04, .04, .04), .5, 0, .002)
            stencil(p, name, 'ZAMAZINGO', (.09, .37, .91, .63), (.86, .80, .62), stroke=.16, mirror=mirror, wear=.8)
    # Plastics: cream fenders and plates, scuffed grey where boots and branches rubbed, mud splashed from below.
    ys, xs, cloud = p.base('plastic', (.80, .76, .66), .5, 0, [(4, .3), (13, .2)])
    uu, vv = p.uv(ys, xs)
    p.scratches(ys, xs, 160, -.10, -.15, None, (.01, .05))
    mud = np.clip(p.field(cloud.shape, [(7, 1), (23, .4)]) * .7 + (vv - .72) * 3, 0, 1)
    p.blend(ys, xs, mud * .6, (.26, .21, .15), .9, 0, .005)
    # The number board: cream plastic, a black seven painted by hand.
    ys, xs, cloud = p.base('number', (.82, .78, .68), .5, 0, [(4, .3)])
    seven = text_mask(cloud.shape, '7', (.34, .06, .66, .44), .16)
    p.blend(ys, xs, seven, (.05, .05, .05), .45, 0)
    p.blend(ys, xs, np.clip(p.field(cloud.shape, [(9, 1)]) - .1, 0, 1) * .3, (.30, .25, .18), .9, 0)
    # The frame: gloss black powder-coat, chipped to grey primer where boots rub, mud below.
    ys, xs, cloud = p.worn_paint('frame', (.04, .042, .045), chip_level=.64, rust=.62, scratch=80)
    p.orm[ys, xs, 1] -= .15
    p.blend(ys, xs, np.clip(p.field(cloud.shape, [(5, 1), (19, .4)]) + .1, 0, 1) * .35, (.24, .20, .15), .9, 0)
    # Chrome: the fork's stanchions and the bars, pitted where the chrome lifted.
    ys, xs, cloud = p.base('chrome', (.78, .78, .78), .1, 1, [(6, .2)])
    pits = np.clip(p.field(cloud.shape, [(160, 1), (400, .5)]) - .7, 0, 1) * 4
    p.blend(ys, xs, pits, (.30, .22, .16), .7, .4, -.01)
    # A drive chain seen from its side: plates and rollers, oily.
    ys, xs, cloud = p.base('chain', (.10, .10, .10), .45, .9, [(6, .3)])
    uu, vv = p.uv(ys, xs)
    link = (np.abs(((uu * 40) % 1) - .5) < .38) & (np.abs(vv - .5) < .3)
    roller = np.hypot(((uu * 40) % 1) - .0, (vv - .5) * 3) < .2
    p.blend(ys, xs, link * 1.0, (.24, .23, .22), .35, 1, .03)
    p.blend(ys, xs, roller * 1.0, (.40, .38, .34), .3, 1, .05)
    _zinc(p, 'zinc')
    _seat(p, (.05, .05, .055), ribbed=True)
    _block(p, (.18, .18, .19))
    # The silencer: brushed aluminium blued and sooted toward its end.
    ys, xs, cloud = p.base('silencer', (.64, .64, .62), .3, 1, [(5, .3)])
    uu, vv = p.uv(ys, xs)
    p.albedo[ys, xs, :3] *= (1 + np.sin(vv * 900) * .03)[:, :, None]
    p.blend(ys, xs, np.clip(uu - .6, 0, 1) * 2 * .6, (.30, .26, .30), .4, 1)
    p.blend(ys, xs, np.clip(uu - .85, 0, 1) * 6, (.06, .05, .05), .8, .2)
    # The brake disc: ground steel rings, drilled, a blued heat band.
    ys, xs, cloud = p.base('disc', (.56, .56, .55), .3, 1, [(5, .2)])
    uu, vv = p.uv(ys, xs)
    p.albedo[ys, xs, :3] *= (1 + np.sin(np.hypot(uu - .5, vv - .5) * 900) * .04)[:, :, None]
    p.blend(ys, xs, np.clip(1 - np.abs(np.hypot(uu - .5, vv - .5) - .38) * 12, 0, 1) * .4, (.32, .30, .42), .3, 1)
    return np.clip(p.albedo, 0, 1), np.clip(p.orm, 0, 1), p.height

"""The Kestrel's texture atlas (docs/KESTREL.md §10): painted here from noise, no downloaded pixels.

One 2048 px atlas for the frame and every module: primer-red tube, faded olive and jerry red paint, bare and dark steel,
cast aluminium, rubber, a worn vinyl seat, diamond plate, the blades' grey, hazard tape, a road sign for the fin, the
gauges' faces, lamp lenses, heavy rust and exhaust wrap. Regions are (x, y, w, h) in top-down image coordinates, as
`hollow.component_mesh.ComponentMesh` maps them. `paint()` returns albedo, ORM and height arrays; the rotor's blur disc
has its own small RGBA texture (`blur_disc()`).
"""
import math

import numpy as np

N = 2048
REGIONS = {
    'primer': (.005, .005, .49, .24), 'olive': (.505, .005, .49, .24),
    'steel': (.005, .255, .24, .14), 'dark': (.255, .255, .24, .14), 'alu': (.505, .255, .24, .14),
    'rubber': (.755, .255, .24, .14),
    'seat': (.005, .405, .24, .14), 'tread': (.255, .405, .24, .14), 'blade': (.505, .405, .24, .14),
    'tape': (.755, .405, .24, .14),
    'sign': (.005, .555, .24, .24), 'gauge': (.255, .555, .115, .115), 'lamp': (.38, .555, .055, .055),
    'lamp_red': (.44, .555, .055, .055), 'heat': (.255, .68, .24, .115),
    'red': (.505, .555, .24, .24), 'rust': (.755, .555, .24, .24),
    'cable': (.005, .805, .24, .19), 'weld': (.255, .805, .24, .19),
}


class Painter:
    def __init__(self, seed=46011):
        self.rng = np.random.default_rng(seed)
        self.albedo = np.ones((N, N, 4), np.float32)
        self.orm = np.ones_like(self.albedo)
        self.orm[:, :, 2] = 0
        self.height = np.zeros((N, N), np.float32)

    def window(self, name):
        x, y, w, h = REGIONS[name]
        return slice(int(y * N), int((y + h) * N)), slice(int(x * N), int((x + w) * N))

    def field(self, shape, cells_weights):
        """Smooth value noise: a sum of bicubic-blended grids, each (cells, weight)."""
        h, w = shape
        yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
        xx /= w
        yy /= h
        out = np.zeros(shape, np.float32)
        for cells, weight in cells_weights:
            grid = self.rng.uniform(-1, 1, (cells + 1, cells + 1)).astype(np.float32)
            gx, gy = xx * cells, yy * cells
            ix, iy = gx.astype(int), gy.astype(int)
            fx, fy = gx - ix, gy - iy
            fx = fx * fx * (3 - 2 * fx)
            fy = fy * fy * (3 - 2 * fy)
            out += ((grid[iy, ix] * (1 - fx) + grid[iy, ix + 1] * fx) * (1 - fy)
                    + (grid[iy + 1, ix] * (1 - fx) + grid[iy + 1, ix + 1] * fx) * fy) * weight
        return out

    def base(self, name, colour, rough, metal, cells, variation=.08):
        ys, xs = self.window(name)
        shape = (ys.stop - ys.start, xs.stop - xs.start)
        cloud = self.field(shape, cells)
        noise = self.rng.uniform(-1, 1, shape).astype(np.float32)
        self.albedo[ys, xs, :3] = np.array(colour, np.float32) * (1 + cloud * variation + noise * .02)[:, :, None]
        self.orm[ys, xs, 1] = rough + cloud * .05
        self.orm[ys, xs, 2] = metal
        self.height[ys, xs] = cloud * .02 + noise * .004
        return ys, xs, cloud

    def uv(self, ys, xs):
        h, w = ys.stop - ys.start, xs.stop - xs.start
        vv, uu = np.mgrid[0:h, 0:w].astype(np.float32)
        return uu / w, vv / h

    def blend(self, ys, xs, mask, colour, rough=None, metal=None, lift=0.0):
        """Lays `colour` over a region where `mask` (0..1) says, with its roughness, metalness and relief."""
        m = np.clip(mask, 0, 1)[:, :, None]
        tile = self.albedo[ys, xs, :3]
        tile[:] = tile * (1 - m) + np.array(colour, np.float32) * m
        if rough is not None:
            self.orm[ys, xs, 1] = self.orm[ys, xs, 1] * (1 - m[:, :, 0]) + rough * m[:, :, 0]
        if metal is not None:
            self.orm[ys, xs, 2] = self.orm[ys, xs, 2] * (1 - m[:, :, 0]) + metal * m[:, :, 0]
        self.height[ys, xs] += m[:, :, 0] * lift

    def scratches(self, ys, xs, count, bright, rough_drop, metal_to=None, length=(.01, .06)):
        h, w = ys.stop - ys.start, xs.stop - xs.start
        uu, vv = self.uv(ys, xs)
        for _ in range(count):
            x, y = self.rng.uniform(.02, .98), self.rng.uniform(.02, .98)
            width, run, slope = self.rng.uniform(.0006, .0016), self.rng.uniform(*length), self.rng.uniform(-.6, .6)
            y0, y1 = max(0, int((y - run) * h)), min(h, int((y + run) * h) + 1)
            x0, x1 = max(0, int((x - run) * w)), min(w, int((x + run) * w) + 1)
            su, sv = uu[y0:y1, x0:x1], vv[y0:y1, x0:x1]
            stroke = np.exp(-((sv - y - (su - x) * slope) / width) ** 2) * np.clip(1 - np.abs(su - x) / run, 0, 1)
            sy, sx = slice(ys.start + y0, ys.start + y1), slice(xs.start + x0, xs.start + x1)
            self.albedo[sy, sx, :3] += stroke[:, :, None] * bright
            self.orm[sy, sx, 1] -= stroke * rough_drop
            if metal_to is not None:
                self.orm[sy, sx, 2] = np.maximum(self.orm[sy, sx, 2], stroke * metal_to)

    def worn_paint(self, name, colour, cells=((5, .6), (17, .3), (61, .12)), chip_level=.62, rust=.55, scratch=60):
        """Paint over steel: chipped to bare metal at its edges' noise, rust blooming where it is thin."""
        ys, xs, cloud = self.base(name, colour, .6, 0, list(cells))
        chips = np.clip((self.field(cloud.shape, [(90, 1), (230, .5)]) + cloud * .3 - chip_level) * 4, 0, 1)
        rusty = np.clip(cloud * 1.3 - rust, 0, 1)
        self.blend(ys, xs, chips, (.42, .41, .39), .45, .85, -.02)
        self.blend(ys, xs, rusty * .7, (.30, .15, .07), .85, .1, .004)
        self.scratches(ys, xs, scratch, .10, .1, .8)
        return ys, xs, cloud


def paint():
    p = Painter()
    rng = p.rng
    # Tube frame: oxide-red primer, sprayed by hand: runs, chips at the joints, rust where it was thin.
    ys, xs, cloud = p.worn_paint('primer', (.40, .12, .07), chip_level=.68, rust=.50, scratch=35)
    uu, vv = p.uv(ys, xs)
    runs = np.clip(p.field(cloud.shape, [(40, 1)]) - .7, 0, 1) * np.clip(np.sin(uu * 220) * 4, 0, 1)
    p.blend(ys, xs, runs * .6, (.33, .09, .05), .5, 0, .006)
    # Faded olive panel paint (the plating, the cargo box, the long tank).
    p.worn_paint('olive', (.22, .25, .15), chip_level=.64, rust=.62, scratch=70)
    # Jerry red (the jerry tank, the tuned engine's covers).
    ys, xs, cloud = p.worn_paint('red', (.50, .07, .05), chip_level=.68, rust=.70, scratch=40)
    # Bare steel: brushed, scratched.
    ys, xs, cloud = p.base('steel', (.50, .50, .49), .38, 1, [(7, .5), (29, .2)])
    uu, vv = p.uv(ys, xs)
    p.albedo[ys, xs, :3] *= (1 + np.sin(vv * 1500) * .02)[:, :, None]
    p.scratches(ys, xs, 80, .08, .12)
    # Dark steel: painted black, oily, worn bright at the edges.
    ys, xs, cloud = p.base('dark', (.055, .055, .06), .55, .2, [(6, .5), (23, .25)])
    oil = np.clip(p.field(cloud.shape, [(9, 1), (31, .4)]) - .3, 0, 1)
    p.blend(ys, xs, oil * .8, (.02, .02, .018), .25, .3)
    p.scratches(ys, xs, 50, .14, .15, .9)
    # Cast aluminium: rough, grey, sand-cast speckle.
    ys, xs, cloud = p.base('alu', (.55, .55, .53), .62, .9, [(4, .4), (15, .2)])
    p.albedo[ys, xs, :3] *= (1 + rng.uniform(-.06, .06, cloud.shape).astype(np.float32))[:, :, None]
    p.height[ys, xs] += rng.uniform(-.01, .01, cloud.shape).astype(np.float32)
    soot = np.clip(p.field(cloud.shape, [(5, 1)]) - .2, 0, 1)
    p.blend(ys, xs, soot * .5, (.12, .10, .08), .7, .3)
    # Rubber.
    p.base('rubber', (.05, .05, .05), .88, 0, [(8, .5)])
    # Seat: worn tan vinyl, stitched in panels, a duct-tape patch, cracks.
    ys, xs, cloud = p.base('seat', (.36, .27, .17), .55, 0, [(6, .4), (20, .2)])
    uu, vv = p.uv(ys, xs)
    seams = (np.abs(((uu * 4) % 1) - .5) > .485) | (np.abs(((vv * 3) % 1) - .5) > .485)
    stitches = seams & (np.sin((uu + vv) * 900) > .3)
    p.blend(ys, xs, seams * .6, (.20, .15, .09), .7, 0, -.03)
    p.blend(ys, xs, stitches * 1.0, (.55, .50, .40), .7, 0, .01)
    cracks = np.clip(p.field(cloud.shape, [(60, 1), (150, .4)]) - .75, 0, 1) * 5
    p.blend(ys, xs, cracks, (.16, .12, .08), .8, 0, -.02)
    patch = ((uu > .58) & (uu < .86) & (vv > .55) & (vv < .80)).astype(np.float32)
    p.blend(ys, xs, patch, (.42, .43, .44), .45, .4, .01)
    # Diamond plate.
    ys, xs, cloud = p.base('tread', (.46, .46, .45), .42, 1, [(5, .4), (19, .2)])
    uu, vv = p.uv(ys, xs)
    a, b = (uu * 30 + vv * 18) % 1, (uu * 30 - vv * 18) % 1
    lozenge = ((np.abs(a - .5) < .08) & (np.abs(b - .5) < .35)) | ((np.abs(b - .5) < .08) & (np.abs(a - .5) < .35))
    p.height[ys, xs] += lozenge * .06
    p.blend(ys, xs, np.clip(cloud * 1.5 - .5, 0, 1) * .6, (.28, .16, .09), .8, .2)
    # Rotor blades: light grey, the leading edge (low v) worn to bare metal, tape repairs.
    ys, xs, cloud = p.base('blade', (.55, .57, .56), .5, 0, [(4, .3), (13, .15)])
    uu, vv = p.uv(ys, xs)
    edge = np.clip(1 - vv * 10, 0, 1) * np.clip(p.field(cloud.shape, [(50, 1)]) + .6, 0, 1)
    p.blend(ys, xs, edge, (.62, .62, .60), .35, .9)
    p.scratches(ys, xs, 40, .08, .1, .6, (.02, .1))
    # Hazard tape: yellow and black diagonal stripes, scuffed.
    ys, xs, cloud = p.base('tape', (.80, .62, .08), .55, 0, [(6, .3)])
    uu, vv = p.uv(ys, xs)
    stripes = ((uu * 8 + vv * 4) % 1) < .5
    p.blend(ys, xs, stripes * 1.0, (.04, .04, .04), .5, 0)
    scuff = np.clip(p.field(cloud.shape, [(40, 1), (120, .5)]) - .55, 0, 1) * 3
    p.blend(ys, xs, scuff, (.35, .32, .25), .8, 0, -.005)
    # The fin's road sign: a yellow square (a diamond once mounted at 45°), a black border and a bold chevron, faded,
    # holed where it was bolted to its post.
    ys, xs, cloud = p.base('sign', (.78, .60, .10), .5, .1, [(4, .4), (11, .2)], variation=.15)
    uu, vv = p.uv(ys, xs)
    border = (np.minimum(np.minimum(uu, 1 - uu), np.minimum(vv, 1 - vv)) < .07) & \
             (np.minimum(np.minimum(uu, 1 - uu), np.minimum(vv, 1 - vv)) > .025)
    # A chevron along the square's diagonal: it points along a diagonal, so it reads as an arrow on the diamond.
    d1, d2 = (uu + vv) - 1.0, (uu - vv)
    chevron = (np.abs(np.abs(d2) * .9 - (d1 + .25)) < .09) & (np.abs(d2) < .38)
    p.blend(ys, xs, border * 1.0, (.04, .04, .04), .5, .1)
    p.blend(ys, xs, chevron * 1.0, (.04, .04, .04), .5, .1)
    fade = np.clip(p.field(cloud.shape, [(5, 1), (19, .4)]) * .5 + .25, 0, 1)
    p.blend(ys, xs, fade * .35, (.70, .68, .62), .7, .1)
    p.blend(ys, xs, np.clip(cloud * 1.6 - .9, 0, 1), (.32, .17, .08), .85, .3)
    for cx, cy in ((.5, .12), (.5, .88)):
        hole = ((uu - cx) ** 2 + (vv - cy) ** 2) < .0009
        p.blend(ys, xs, hole * 1.0, (.02, .02, .02), .9, 0, -.05)
    # Gauges: a cream dial, black ticks and needle under scratched glass.
    ys, xs, cloud = p.base('gauge', (.80, .77, .66), .25, 0, [(3, .2)])
    uu, vv = p.uv(ys, xs)
    r = np.hypot(uu - .5, vv - .5)
    ang = np.arctan2(vv - .5, uu - .5)
    ticks = (r > .34) & (r < .42) & (np.abs(((ang / math.tau * 24) % 1) - .5) > .42) & (ang < 2.4) | \
            ((r > .30) & (r < .42) & (np.abs(((ang / math.tau * 6) % 1) - .5) > .46))
    needle = (np.abs((uu - .5) * math.sin(-2.2) - (vv - .5) * math.cos(-2.2)) < .012) & (r < .36) & \
             ((uu - .5) * math.cos(-2.2) + (vv - .5) * math.sin(-2.2) > 0)
    rim = r > .46
    p.blend(ys, xs, ticks * 1.0, (.05, .05, .05))
    p.blend(ys, xs, needle * 1.0, (.55, .08, .04))
    p.blend(ys, xs, rim * 1.0, (.08, .08, .08), .4, .8)
    # Lenses.
    ys, xs, cloud = p.base('lamp', (.92, .80, .55), .15, 0, [(3, .2)])
    uu, vv = p.uv(ys, xs)
    p.blend(ys, xs, (np.sin(uu * 60) > .7) * .3, (1, .95, .8))
    p.base('lamp_red', (.75, .06, .04), .15, 0, [(3, .2)])
    # Exhaust wrap: tan woven fibre, sooted.
    ys, xs, cloud = p.base('heat', (.55, .47, .33), .9, 0, [(6, .3)])
    uu, vv = p.uv(ys, xs)
    weave = (np.sin(uu * 400) * np.sin(vv * 80 + uu * 40))
    p.albedo[ys, xs, :3] *= (1 + weave * .08)[:, :, None]
    p.height[ys, xs] += weave * .02
    p.blend(ys, xs, np.clip(p.field(cloud.shape, [(5, 1)]) + .1, 0, 1) * .7, (.12, .10, .08), .9, 0)
    # Heavy rust: pitted plate.
    ys, xs, cloud = p.base('rust', (.30, .16, .08), .85, .2, [(5, .6), (18, .3), (47, .16)], variation=.3)
    pits = np.clip(p.field(cloud.shape, [(120, 1), (300, .5)]) - .55, 0, 1) * 4
    p.blend(ys, xs, pits, (.18, .09, .05), .95, .1, -.03)
    p.scratches(ys, xs, 30, .12, .15, .7)
    # Cables and wiring: black sheathing.
    p.base('cable', (.03, .03, .035), .55, 0, [(9, .4)])
    # Weld beads: grey-blue heat tint over steel.
    ys, xs, cloud = p.base('weld', (.33, .33, .35), .5, .9, [(30, .6), (90, .4)], variation=.3)
    uu, vv = p.uv(ys, xs)
    p.height[ys, xs] += np.sin(uu * 300) * .03
    p.blend(ys, xs, np.clip(np.sin(uu * 7) * .5 + .2, 0, 1) * .4, (.20, .22, .40), .5, .9)
    return np.clip(p.albedo, 0, 1), np.clip(p.orm, 0, 1), p.height


def blur_disc(size=512):
    """The rotor's blur: a translucent disc, darker at the blades' path and with the tape's ring at the tips."""
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    u, v = (xx + .5) / size * 2 - 1, (yy + .5) / size * 2 - 1
    r = np.hypot(u, v)
    a = np.arctan2(v, u)
    out = np.zeros((size, size, 4), np.float32)
    out[..., :3] = .38
    alpha = np.clip((r - .04) * 4, 0, 1) * .16 * (1 + .25 * np.cos(a * 2)) * (r < .995)
    tape = (r > .93) & (r < .99)
    out[tape, 0], out[tape, 1], out[tape, 2] = .55, .45, .12
    alpha = np.where(tape, .24, alpha)
    hub = r < .05
    alpha = np.where(hub, 0, alpha)
    out[..., 3] = alpha
    return out

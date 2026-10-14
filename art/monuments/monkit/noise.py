"""Tileable noise for the monuments' surface textures (MONUMENTS.md §8.2): numpy only, every field periodic on the
texture square so the textures repeat without seams. Gradients come from a fixed table (no trigonometry), so a build
writes the same files on every machine.

Arrays are (rows, columns) = (v, u), rows top to bottom, values float32.
"""
import numpy as np

# Sixteen unit gradients round the circle (cos/sin of k * 22.5 degrees, written out).
_G = np.array([
    (1.0, 0.0), (0.92388, 0.38268), (0.70711, 0.70711), (0.38268, 0.92388),
    (0.0, 1.0), (-0.38268, 0.92388), (-0.70711, 0.70711), (-0.92388, 0.38268),
    (-1.0, 0.0), (-0.92388, -0.38268), (-0.70711, -0.70711), (-0.38268, -0.92388),
    (0.0, -1.0), (0.38268, -0.92388), (0.70711, -0.70711), (0.92388, -0.38268)], np.float64)


def rng(seed):
    return np.random.default_rng(seed)


def _fade(t):
    return t * t * t * (t * (t * 6 - 15) + 10)


def perlin(size, px, py, r):
    """Gradient noise in about [0, 1], `px` cells across and `py` down (both divide nothing: any period works)."""
    k = r.integers(0, 16, (py, px))
    gx, gy = _G[k, 0], _G[k, 1]
    x = np.arange(size) * (px / size)
    y = np.arange(size) * (py / size)
    xi = np.floor(x).astype(np.int64)
    yi = np.floor(y).astype(np.int64)
    xf = (x - xi)[None, :]
    yf = (y - yi)[:, None]
    x0, x1 = xi % px, (xi + 1) % px
    y0, y1 = (yi % py)[:, None], ((yi + 1) % py)[:, None]
    x0, x1 = x0[None, :], x1[None, :]

    def dot(yy, xx, dx, dy):
        return gx[yy, xx] * dx + gy[yy, xx] * dy

    n00 = dot(y0, x0, xf, yf)
    n10 = dot(y0, x1, xf - 1, yf)
    n01 = dot(y1, x0, xf, yf - 1)
    n11 = dot(y1, x1, xf - 1, yf - 1)
    u, v = _fade(xf), _fade(yf)
    a = n00 + u * (n10 - n00)
    b = n01 + u * (n11 - n01)
    return ((a + v * (b - a)) * 0.75 + 0.5).astype(np.float32)


def fbm(size, period, octaves, r, gain=0.5, stretch=1.0):
    """Fractal sum of `octaves` gradient noises, `period` cells across at the first (doubling each octave),
    normalised to about [0, 1]. `stretch` > 1 makes features longer down the texture (streaks)."""
    total = np.zeros((size, size), np.float32)
    amp, norm = 1.0, 0.0
    p = period
    for _ in range(octaves):
        if p > size // 2:
            break
        py = max(1, int(round(p / stretch)))
        total += amp * (perlin(size, p, py, r) - 0.5)
        norm += amp
        amp *= gain
        p *= 2
    return total / max(norm, 1e-6) + 0.5


def worley(size, period, r, jitter=0.9):
    """Cellular noise: distance to the nearest and second nearest feature point (in cells) and the nearest cell's
    random value in [0, 1). One point per cell of a `period` x `period` grid."""
    pts = r.random((period, period, 2)) * jitter + (1 - jitter) * 0.5
    val = r.random((period, period))
    c = (np.arange(size) + 0.5) * (period / size)
    X, Y = c[None, :], c[:, None]
    cx, cy = np.floor(X).astype(np.int64), np.floor(Y).astype(np.int64)
    f1 = np.full((size, size), 9.0)
    f2 = np.full((size, size), 9.0)
    ident = np.zeros((size, size))
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            nx, ny = cx + dx, cy + dy
            p = pts[ny % period, nx % period]
            d = np.sqrt((X - (nx + p[..., 0])) ** 2 + (Y - (ny + p[..., 1])) ** 2)
            closer = d < f1
            f2 = np.where(closer, f1, np.minimum(f2, d))
            ident = np.where(closer, val[ny % period, nx % period], ident)
            f1 = np.where(closer, d, f1)
    return f1.astype(np.float32), f2.astype(np.float32), ident.astype(np.float32)


def blur(a, radius):
    """Box blur with wrap-around, applied twice (close to a Gaussian)."""
    if radius < 1:
        return a
    out = a.astype(np.float32)
    for _ in range(2):
        for axis in (0, 1):
            acc = out.copy()
            for k in range(1, radius + 1):
                acc += np.roll(out, k, axis=axis) + np.roll(out, -k, axis=axis)
            out = acc / (2 * radius + 1)
    return out


def drip(a, length, decay=0.93, axis=0):
    """Smears a mask downward (rows increase down the texture) with a fading tail: rust and water running down
    from where `a` is set. Wraps, so the result still tiles."""
    out = a.astype(np.float32).copy()
    w = 1.0
    for k in range(1, length + 1):
        w *= decay
        out = np.maximum(out, np.roll(a, k, axis=axis) * w)
    return out


def lines(size, count, r, length=(0.02, 0.12), width=1, horizontal_bias=0.0, strength=(0.4, 1.0)):
    """Thin scratches: `count` straight segments splatted into a field (max of overlaps), wrapping at the edges.
    `length` as fractions of the texture; `horizontal_bias` 0..1 pulls their direction toward the u axis."""
    out = np.zeros((size, size), np.float32)
    for _ in range(count):
        x0, y0 = r.random() * size, r.random() * size
        # A random direction, squashed toward u by the bias.
        dx, dy = r.normal(), r.normal() * (1 - horizontal_bias * 0.85)
        n = max(np.hypot(dx, dy), 1e-6)
        dx, dy = dx / n, dy / n
        ln = (length[0] + r.random() * (length[1] - length[0])) * size
        steps = max(2, int(ln))
        t = np.linspace(0, ln, steps)
        s = strength[0] + r.random() * (strength[1] - strength[0])
        fade = np.minimum(1.0, np.minimum(t, ln - t) / max(ln * 0.2, 1))
        xs = (x0 + dx * t).astype(np.int64)
        ys = (y0 + dy * t).astype(np.int64)
        for o in range(width):
            out[(ys + o) % size, xs % size] = np.maximum(out[(ys + o) % size, xs % size], s * fade)
    return out


def remap(a, lo, hi):
    """Linear step: 0 below `lo`, 1 above `hi`."""
    return np.clip((a - lo) / max(hi - lo, 1e-6), 0, 1).astype(np.float32)


def smooth(a, lo, hi):
    t = remap(a, lo, hi)
    return t * t * (3 - 2 * t)

"""Procedural, tileable surface textures (height, normal and colour) built with numpy inside Blender.

All functions return float32 arrays shaped (size, size) or (size, size, channels), rows top to bottom. The output is
deterministic: every generator takes a seed.
"""
import numpy as np


def _rng(seed):
    return np.random.default_rng(seed)


def value_noise(size, cells, seed, octaves=1, persistence=0.5):
    """Tileable value noise in [0, 1]: a random lattice of `cells` per side, smoothly interpolated, with octaves."""
    total = np.zeros((size, size), np.float32)
    amplitude, norm = 1.0, 0.0
    rng = _rng(seed)
    for o in range(octaves):
        n = max(1, cells * (2 ** o))
        lattice = rng.random((n, n)).astype(np.float32)
        coords = np.arange(size, dtype=np.float32) * n / size
        i0 = np.floor(coords).astype(np.int32) % n
        i1 = (i0 + 1) % n
        f = coords - np.floor(coords)
        f = f * f * (3 - 2 * f)
        rows0 = lattice[i0][:, i0] * (1 - f)[None, :] + lattice[i0][:, i1] * f[None, :]
        rows1 = lattice[i1][:, i0] * (1 - f)[None, :] + lattice[i1][:, i1] * f[None, :]
        layer = rows0 * (1 - f)[:, None] + rows1 * f[:, None]
        total += amplitude * layer
        norm += amplitude
        amplitude *= persistence
    return total / norm


def plain_weave(size, threads, seed, slub=0.25):
    """Height of a plain weave: warp and weft threads passing over and under each other."""
    rng = _rng(seed)
    y, x = np.mgrid[0:size, 0:size].astype(np.float32) / size * threads
    fx, fy = x - np.floor(x), y - np.floor(y)
    ix, iy = np.floor(x).astype(np.int32) % threads, np.floor(y).astype(np.int32) % threads
    thick_x = 1.0 + slub * (rng.random(threads).astype(np.float32) - 0.5)
    thick_y = 1.0 + slub * (rng.random(threads).astype(np.float32) - 0.5)
    prof_x = np.sqrt(np.clip(1 - (2 * fx - 1) ** 2, 0, 1)) * thick_x[ix]
    prof_y = np.sqrt(np.clip(1 - (2 * fy - 1) ** 2, 0, 1)) * thick_y[iy]
    over = 0.5 + 0.5 * np.cos(np.pi * (y + ix))
    warp = prof_x * (0.55 + 0.45 * over)
    weft = prof_y * (0.55 + 0.45 * (1 - (0.5 + 0.5 * np.cos(np.pi * (x + iy)))))
    h = np.maximum(warp, weft)
    fibre = value_noise(size, threads * 4, seed + 1, octaves=2)
    return (h * 0.85 + fibre * 0.15).astype(np.float32)


def twill(size, threads, seed):
    """Height of a 2/1 twill (denim-like diagonal ribs)."""
    y, x = np.mgrid[0:size, 0:size].astype(np.float32) / size * threads
    d = (x + y) * 1.0
    rib = 0.5 + 0.5 * np.cos(2 * np.pi * d / 1.5)
    across = np.sqrt(np.clip(1 - (2 * (x - np.floor(x)) - 1) ** 2, 0, 1))
    fibre = value_noise(size, threads * 3, seed + 2, octaves=2)
    return (0.55 * rib + 0.3 * across + 0.15 * fibre).astype(np.float32)


def knit(size, columns, seed):
    """Height of a jersey knit: columns of small V-shaped loops."""
    y, x = np.mgrid[0:size, 0:size].astype(np.float32) / size * columns
    fx = x - np.floor(x)
    fy = (y * 1.4) - np.floor(y * 1.4)
    leg = np.abs(fx - 0.5) * 2
    v = np.abs(leg - (1 - fy))
    h = np.clip(1 - v * 2.2, 0, 1) ** 0.7
    fibre = value_noise(size, columns * 3, seed + 3, octaves=2)
    return (h * 0.8 + fibre * 0.2).astype(np.float32)


def leather(size, seed):
    """Height of a leather grain: cell-like wrinkles."""
    a = value_noise(size, 48, seed, octaves=3, persistence=0.55)
    b = value_noise(size, 96, seed + 7, octaves=2)
    ridges = 1 - np.abs(a - 0.5) * 2
    return (0.65 * ridges ** 3 + 0.35 * b).astype(np.float32)


def height_to_normal(h, strength):
    """Tangent-space normal map (OpenGL convention, green up) from a tileable height field."""
    dx = (np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)) * 0.5
    dy = (np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)) * 0.5
    nx, ny, nz = -dx * strength, dy * strength, np.ones_like(h)
    length = np.sqrt(nx * nx + ny * ny + nz * nz)
    n = np.stack([nx / length, ny / length, nz / length], axis=-1)
    return (n * 0.5 + 0.5).astype(np.float32)


def to_blender_pixels(rgb_or_rgba):
    """Rows top-to-bottom (numpy) into Blender's flat bottom-to-top RGBA float buffer."""
    a = rgb_or_rgba
    if a.ndim == 2:
        a = np.stack([a, a, a], axis=-1)
    if a.shape[2] == 3:
        a = np.concatenate([a, np.ones(a.shape[:2] + (1,), np.float32)], axis=-1)
    return np.ascontiguousarray(np.flipud(np.clip(a, 0, 1)).astype(np.float32)).ravel()

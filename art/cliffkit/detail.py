"""The families' shared tiling detail (docs/WORLD_TERRAIN_PLAN.md §4.3): each family's Megascans surface brought to 2K
and packed into two maps that `cliffkit.gdshader` projects in world space:

- `<family>_detail_albedo.png`: RGB the surface's colour (sRGB), A its height (0-1);
- `<family>_detail_normal.png`: RG the normal's x and y (OpenGL: +Y up the image), B roughness, A occlusion.

Megascans surfaces cover 1 m at 4K; the shader spreads a repeat over a few metres (700-1,000 px/m at 2K).
"""
import numpy as np

from hollow import SOURCES, log
from hollow.textures import downscale, read, write_png

from .kit import FAMILIES

SIZE = 2048


def _stretch(channel):
    lo, hi = np.percentile(channel, 0.5), np.percentile(channel, 99.5)
    return np.clip((channel - lo) / max(hi - lo, 1e-6), 0.0, 1.0)


def _opengl(normal, height):
    """Whether the normal's green runs up the image (OpenGL) against its own height (rows top to bottom)."""
    gy, gx = np.gradient(height)
    return float(np.mean((normal[:, :, 1] - 0.5) * gy)) > 0


def build(family, out):
    """Writes the family's two detail maps into `out`; returns their paths and the mean colour (sRGB)."""
    spec = FAMILIES[family]
    folder = SOURCES / spec["surface"]
    stem = spec["stem"]

    def load(kind, fallback=None):
        path = folder / f"{stem}_{kind}.jpg"
        if not path.exists() and fallback:
            path = folder / f"{stem}_{fallback}.jpg"
        return downscale(read(path), SIZE)

    albedo = load("BaseColor")
    height = _stretch(load("Displacement", "Bump")[:, :, 0])
    normal = load("Normal")
    rough = load("Roughness")[:, :, 0]
    ao = load("AO")[:, :, 0]
    n = normal[:, :, :3] * 2 - 1
    if not _opengl(normal, height):
        n[:, :, 1] *= -1
        log(f"{family}: detail normal flipped to OpenGL")
    n /= np.maximum(np.linalg.norm(n, axis=2, keepdims=True), 1e-6)
    a = np.empty(albedo.shape[:2] + (4,), np.float32)
    a[:, :, :3] = albedo[:, :, :3]
    a[:, :, 3] = height
    b = np.empty_like(a)
    b[:, :, 0] = n[:, :, 0] * 0.5 + 0.5
    b[:, :, 1] = n[:, :, 1] * 0.5 + 0.5
    b[:, :, 2] = rough
    b[:, :, 3] = ao
    out.mkdir(parents=True, exist_ok=True)
    pa = write_png(out / f"{family}_detail_albedo.png", a, channels=4)
    pb = write_png(out / f"{family}_detail_normal.png", b, channels=4)
    mean = [float(v) for v in albedo[::8, ::8, :3].reshape(-1, 3).mean(axis=0)]
    return pa, pb, mean

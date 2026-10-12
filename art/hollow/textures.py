"""Texture conversion: read, downscale, renormalise, derive normal maps, write PNG (GROUND.md §9: 2K at most)."""
import struct
import zlib
from pathlib import Path

import bpy
import numpy as np

from . import log


def read(path):
    """An image as a float32 array (rows top to bottom, RGBA, 0..1), raw values with no colour conversion."""
    image = bpy.data.images.load(str(path), check_existing=False)
    image.colorspace_settings.name = "Non-Color"
    image.reload()
    w, h = image.size
    pixels = np.empty(w * h * 4, np.float32)
    image.pixels.foreach_get(pixels)
    bpy.data.images.remove(image)
    return pixels.reshape(h, w, 4)[::-1].copy()


def downscale(pixels, size):
    """Box-filters a square power-of-two image down to `size` pixels (no change when already that small)."""
    h, w = pixels.shape[:2]
    if w <= size and h <= size:
        return pixels
    fy, fx = h // min(h, size), w // min(w, size)
    return pixels.reshape(h // fy, fy, w // fx, fx, pixels.shape[2]).mean(axis=(1, 3))


def renormalise(pixels):
    """Unit-length tangent-space normals after filtering."""
    n = pixels[..., :3] * 2 - 1
    n /= np.maximum(np.linalg.norm(n, axis=2, keepdims=True), 1e-6)
    out = pixels.copy()
    out[..., :3] = n * 0.5 + 0.5
    return out


def flip_green(pixels):
    """DirectX to OpenGL normal map convention."""
    out = pixels.copy()
    out[..., 1] = 1 - out[..., 1]
    return out


def normal_from_height(height, strength):
    """An OpenGL (Y up) normal map from a tileable height map (2D float array, rows top to bottom)."""
    gx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5
    gy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5
    n = np.stack((-gx * strength, gy * strength, np.ones_like(height)), axis=2)
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    out = np.ones(height.shape + (4,), np.float32)
    out[..., :3] = n * 0.5 + 0.5
    return out


def _filtered_rows(a):
    """PNG row filtering: per row the cheapest of Sub, Up and Paeth (minimum sum of absolute differences)."""
    h, w, c = a.shape
    x = a.reshape(h, w * c).astype(np.int16)
    left = np.zeros_like(x)
    left[:, c:] = x[:, :-c]
    up = np.zeros_like(x)
    up[1:] = x[:-1]
    upleft = np.zeros_like(x)
    upleft[1:, c:] = x[:-1, :-c]
    p = left + up - upleft
    pa, pb, pc = np.abs(p - left), np.abs(p - up), np.abs(p - upleft)
    paeth = np.where((pa <= pb) & (pa <= pc), left, np.where(pb <= pc, up, upleft))
    candidates = [(1, (x - left) % 256), (2, (x - up) % 256), (4, (x - paeth) % 256)]
    costs = np.stack([np.minimum(f, 256 - f).sum(axis=1) for _, f in candidates])
    best = costs.argmin(axis=0)
    out = np.empty((h, w * c + 1), np.uint8)
    for i, (kind, f) in enumerate(candidates):
        rows = best == i
        out[rows, 0] = kind
        out[rows, 1:] = f[rows].astype(np.uint8)
    return out


def write_png(path, pixels, channels=3):
    """Writes 8-bit PNG (RGB or RGBA) with adaptive row filters and maximum compression; returns the path."""
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    a = (np.clip(pixels[..., :channels], 0, 1) * 255 + 0.5).astype(np.uint8)
    h, w, c = a.shape
    raw = _filtered_rows(a)

    def chunk(kind, data):
        return struct.pack("!I", len(data)) + kind + data + struct.pack("!I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    colour_type = 2 if c == 3 else 6
    png = (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack("!2I5B", w, h, 8, colour_type, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(raw.tobytes(), 9)) + chunk(b"IEND", b""))
    path.write_bytes(png)
    log(f"wrote {path.name} {w}x{h} ({len(png) / 1e6:.1f} MB)")
    return path


def convert(source, target, size=2048, normal=False, directx=False, channels=3):
    """Reads `source`, downscales it to `size`, fixes normals, writes `target` as PNG."""
    pixels = downscale(read(source), size)
    if directx:
        pixels = flip_green(pixels)
    if normal:
        pixels = renormalise(pixels)
    return write_png(target, pixels, channels)


def write_texture_import(png, normal_map=False, srgb_colour=True):
    """A Godot `.import` beside `png`: VRAM-compressed with mipmaps for 3D use (normal maps as RGTC)."""
    from .godot import merge_import
    png = Path(png)
    merge_import(png.with_name(png.name + ".import"), ['importer="texture"', 'type="CompressedTexture2D"'], {
        "compress/mode": "2",
        "compress/high_quality": "false",
        "compress/normal_map": "1" if normal_map else "2",
        "mipmaps/generate": "true",
        "process/fix_alpha_border": "true",
        "detect_3d/compress_to": "0",
    })


def _existing_uid(path):
    path = Path(path)
    if not path.exists():
        return None
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.startswith("uid="):
            return line.split('"')[1]
    return None

"""The effects library's assets (GROUND.md §9.3), rebuilt from text by Blender 5.2 in background mode:

    blender -b --factory-startup --python art/fx/build.py              # everything
    blender -b --factory-startup --python art/fx/build.py -- c4        # one part: c4, textures, decals, chunks

- c4: the PSG C4 VFX kit (D:/GameDev/Assets/PSG_C4VFX_R01, made for this project) converted to game/assets/fx/c4/:
  the smoke flipbook (8 x 8) with its normals, the fire flipbook (4 x 4, HDR), flash, pressure ring, heat distortion,
  dust puff, spark, scorch, and the concrete texture of its debris. 2K at most; normals turned to OpenGL's convention.
- textures: our own, procedural (numpy): muzzle flash atlas, tileable noise, soft dot, droplet streak.
- decals: our own bullet holes (wood, rock, metal), blood, dirt scuff, with normal maps.
- chunks: debris meshes for the particle batches, several shapes merged per mesh with the shape's index in UV2.x:
  stone (the kit's four concrete shards and our own chips and clods), wood (splinters and bark), metal (the kit's two
  fragments and our own tin shards). game/assets/fx/chunks.glb, no materials (the game draws them with its own shader).

Every output is written the same way on every run (fixed seeds, canonical face order).
"""
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import bmesh  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

from hollow import ASSETS, SOURCES, log, script_args  # noqa: E402
from hollow.export import collection, reset, select_only  # noqa: E402
from hollow.godot import merge_import, write_scene_import  # noqa: E402
from hollow.textures import downscale, flip_green, read, renormalise, write_png  # noqa: E402

KIT = SOURCES / "PSG_C4VFX_R01"
FX = ASSETS / "fx"
C4 = FX / "c4"
TEX = FX / "textures"
DECALS = FX / "decals"


def texture_import(png, normal=False, quality=False, mipmaps=True, lossless=False):
    """Godot import settings: VRAM-compressed (BC7 with `quality`, RGTC for normals) with mipmaps, or lossless."""
    png = Path(png)
    merge_import(png.with_name(png.name + ".import"), ['importer="texture"', 'type="CompressedTexture2D"'], {
        "compress/mode": "0" if lossless else "2",
        "compress/high_quality": "true" if quality else "false",
        "compress/normal_map": "1" if normal else "2",
        "mipmaps/generate": "true" if mipmaps else "false",
        "process/fix_alpha_border": "true",
        "detect_3d/compress_to": "0",
    })


def srgb(linear):
    a = np.clip(linear, 0, 1)
    return np.where(a <= 0.0031308, a * 12.92, 1.055 * np.power(a, 1 / 2.4) - 0.055)


def noise_field(size, seed, cells):
    """Tileable noise of the given cell count, for the kit's derived maps."""
    return tile_noise(size, cells, seed, 4)


# ---------------------------------------------------------------- the PSG C4 kit


def c4():
    src = KIT / "Textures"
    C4.mkdir(parents=True, exist_ok=True)
    # Smoke: straight alpha, 8 x 8 frames of 512 -> 256 px.
    smoke = downscale(read(src / "T_C4_Smoke_SubUV_8x8.png"), 2048)
    write_png(C4 / "smoke_8x8.png", smoke, 4)
    texture_import(C4 / "smoke_8x8.png", quality=True)
    normal = downscale(read(src / "T_C4_Smoke_Normal_SubUV_8x8.png"), 2048)
    write_png(C4 / "smoke_normal_8x8.png", renormalise(flip_green(normal)), 3)
    texture_import(C4 / "smoke_normal_8x8.png", normal=True)
    # Fire: premultiplied HDR on black, 4 x 4 frames of 1024 -> 512 px; stored sRGB-encoded at 1/1.25 of its value
    # (the shader multiplies back), so the dark red tails keep their steps.
    fire = downscale(read(src / "T_C4_Fire_HDR_SubUV_4x4.exr"), 2048)
    peak = float(fire[..., :3].max())
    log(f"fire peak {peak:.3f}")
    out = np.ones_like(fire)
    out[..., :3] = srgb(fire[..., :3] / 1.25)
    write_png(C4 / "fire_4x4.png", out, 3)
    texture_import(C4 / "fire_4x4.png", quality=True)
    for name, target, size in (("T_C4_Flash", "flash", 512), ("T_C4_PressureRing", "pressure_ring", 512),
                               ("T_C4_Spark", "spark", 256), ("T_C4_DustPuff", "dust_puff", 512)):
        img = downscale(read(src / f"{name}.png"), size)
        write_png(C4 / f"{target}.png", img, 4)
        texture_import(C4 / f"{target}.png", quality=True)
    # Scorch: the kit's mask as soot (a decal: dark brown-black, denser in the middle) and embers glowing in it.
    mask = downscale(read(src / "T_C4_Scorch.png"), 512)[..., 3]
    size = mask.shape[0]
    n = noise_field(size, 9, 3)
    soot = np.ones((size, size, 4))
    tone = 0.035 + 0.05 * n
    soot[..., 0], soot[..., 1], soot[..., 2] = tone * 1.15, tone, tone * 0.85
    soot[..., :3] = srgb(soot[..., :3])
    soot[..., 3] = np.clip(mask * (0.75 + 0.35 * n), 0, 1)
    write_png(C4 / "scorch.png", soot, 4)
    texture_import(C4 / "scorch.png")
    speck = noise_field(size, 31, 2)
    glow = np.zeros((size, size, 4))
    hot = np.clip((speck - 0.62) * 5, 0, 1) * np.clip((mask - 0.55) * 3, 0, 1)
    glow[..., 0], glow[..., 1], glow[..., 2] = hot, hot * 0.36, hot * 0.08
    glow[..., 3] = 1
    write_png(C4 / "scorch_glow.png", glow, 3)
    texture_import(C4 / "scorch_glow.png")
    heat = downscale(read(src / "T_C4_HeatDistortion.png"), 512)
    write_png(C4 / "heat_distortion.png", heat, 4)
    texture_import(C4 / "heat_distortion.png", quality=True)
    concrete = downscale(read(src / "T_C4_Concrete_BaseColor.png"), 256)
    write_png(C4 / "concrete_albedo.png", concrete, 3)
    texture_import(C4 / "concrete_albedo.png")


# ---------------------------------------------------------------- procedural helpers


def tile_noise(size, cells, seed, octaves=4, persistence=0.5):
    """Tileable fractal value noise in [0, 1]: random lattices that wrap, smoothly interpolated, summed by octave."""
    rng = np.random.default_rng(seed)
    total = np.zeros((size, size), np.float64)
    amp, norm = 1.0, 0.0
    for o in range(octaves):
        n = cells * (2 ** o)
        lattice = rng.random((n, n))
        coords = np.arange(size) * n / size
        i0 = np.floor(coords).astype(int) % n
        i1 = (i0 + 1) % n
        f = coords - np.floor(coords)
        f = f * f * f * (f * (f * 6 - 15) + 10)
        rows0 = lattice[i0][:, i0] * (1 - f)[None, :] + lattice[i0][:, i1] * f[None, :]
        rows1 = lattice[i1][:, i0] * (1 - f)[None, :] + lattice[i1][:, i1] * f[None, :]
        total += amp * (rows0 * (1 - f)[:, None] + rows1 * f[:, None])
        norm += amp
        amp *= persistence
    total /= norm
    return (total - total.min()) / (total.max() - total.min())


def noise2(h, w, scale, seed, octaves=4):
    """Non-tiling fractal noise of any shape (bilinear lattices), in [0, 1]."""
    rng = np.random.default_rng(seed)
    out = np.zeros((h, w))
    amp, norm = 1.0, 0.0
    for o in range(octaves):
        sy, sx = max(2, int(h / scale * 2 ** o) + 2), max(2, int(w / scale * 2 ** o) + 2)
        lat = rng.random((sy, sx))
        y = np.linspace(0, sy - 1.001, h)
        x = np.linspace(0, sx - 1.001, w)
        y0, x0 = np.floor(y).astype(int), np.floor(x).astype(int)
        fy, fx = y - y0, x - x0
        fy, fx = fy * fy * (3 - 2 * fy), fx * fx * (3 - 2 * fx)
        a = lat[y0][:, x0] * (1 - fx) + lat[y0][:, x0 + 1] * fx
        b = lat[y0 + 1][:, x0] * (1 - fx) + lat[y0 + 1][:, x0 + 1] * fx
        out += amp * (a * (1 - fy)[:, None] + b * fy[:, None])
        norm += amp
        amp *= 0.5
    out /= norm
    return (out - out.min()) / (out.max() - out.min() + 1e-9)


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def fire_ramp(heat):
    """Heat 0..1 to colour: deep red, orange, yellow, near-white."""
    stops = np.array([0.0, 0.25, 0.5, 0.75, 1.0])
    cols = np.array([[0.35, 0.03, 0.0], [0.9, 0.22, 0.02], [1.0, 0.55, 0.12], [1.0, 0.82, 0.45], [1.0, 0.97, 0.88]])
    out = np.zeros(heat.shape + (3,))
    for c in range(3):
        out[..., c] = np.interp(heat, stops, cols[:, c])
    return out


# ---------------------------------------------------------------- our own textures


def muzzle_flash_atlas():
    """1024 x 512: the top row four side views of a flame leaving the muzzle (the barrel along +U from the left edge),
    the bottom row four views down the barrel (a star of tongues). RGB colour, A coverage; drawn additively."""
    cell = 256
    atlas = np.zeros((cell * 2, cell * 4, 4))
    yy, xx = np.mgrid[0:cell, 0:cell] / (cell - 1)
    for k in range(4):
        rng = np.random.default_rng(100 + k)
        # Side: the flame along u (0 at the muzzle), w across (-1..1).
        u, w = xx, (yy - 0.5) * 2
        n1 = noise2(cell, cell, 40, 200 + k, 4)
        n2 = noise2(cell, cell, 14, 300 + k, 3)
        reach = rng.uniform(0.78, 0.95)
        prof = np.sin(np.pi * np.clip(u / reach, 0, 1) ** 0.62) * (1 - np.clip(u / reach, 0, 1)) ** 0.35
        half = 0.06 + 0.62 * prof * (0.75 + 0.5 * n1)
        # A few tongues: narrow lobes leaving at small angles.
        tongues = np.zeros_like(u)
        for t in range(rng.integers(3, 6)):
            ang = rng.uniform(-0.45, 0.45)
            length = rng.uniform(0.45, 0.95)
            along = u * np.cos(ang) + w * 0.5 * np.sin(ang)
            across = -u * np.sin(ang) + w * 0.5 * np.cos(ang)
            width = 0.035 + 0.05 * np.clip(along / length, 0, 1) * (1 - np.clip(along / length, 0, 1))
            tongues = np.maximum(tongues, np.exp(-(across / width) ** 2) * smoothstep(length, length * 0.55, along) * (along > 0))
        body = smoothstep(half, half * 0.35, np.abs(w)) * smoothstep(0.0, 0.03, u)
        cov = np.clip(np.maximum(body * (0.45 + 0.75 * n2), tongues * (0.6 + 0.6 * n2)), 0, 1)
        core = np.exp(-(w / 0.16) ** 2) * np.exp(-u * 3.2) * smoothstep(0.0, 0.04, u)
        heat = np.clip(cov * 0.75 + core * 0.9, 0, 1)
        atlas[0:cell, k * cell:(k + 1) * cell, :3] = fire_ramp(heat)
        atlas[0:cell, k * cell:(k + 1) * cell, 3] = np.clip(cov + core, 0, 1) * smoothstep(1.0, 0.9, u)
        # Front: tongues around the centre.
        x, y = (xx - 0.5) * 2, (yy - 0.5) * 2
        r = np.hypot(x, y)
        th = np.arctan2(y, x)
        # A few broad, uneven tongues round a soft core: gas bursting out of the muzzle, not a drawn star.
        petals = rng.integers(4, 7)
        reach_th = np.full_like(r, 0.3)
        base = rng.uniform(0, 2 * np.pi)
        for p in range(petals):
            a0 = base + 2 * np.pi * p / petals + rng.uniform(-0.35, 0.35)
            length = rng.uniform(0.5, 0.85)
            width = rng.uniform(0.22, 0.38)
            d = np.angle(np.exp(1j * (th - a0)))
            reach_th = np.maximum(reach_th, 0.3 + (length - 0.3) * np.exp(-(d / width) ** 2))
        n3 = noise2(cell, cell, 30, 400 + k, 4)
        n4 = noise2(cell, cell, 10, 450 + k, 3)
        wobble = reach_th * (0.82 + 0.3 * n4)
        star = smoothstep(wobble, wobble * 0.3, r) * (0.5 + 0.65 * n3)
        core = np.exp(-(r / 0.24) ** 2)
        heat = np.clip(star * 0.7 + core, 0, 1)
        atlas[cell:, k * cell:(k + 1) * cell, :3] = fire_ramp(heat)
        atlas[cell:, k * cell:(k + 1) * cell, 3] = np.clip(star + core, 0, 1) * smoothstep(1.0, 0.92, r)
    # Colour where there is coverage; black elsewhere (additive).
    atlas[..., :3] *= atlas[..., 3:4] > 0.002
    out = atlas.copy()
    out[..., :3] = srgb(atlas[..., :3])
    write_png(TEX / "muzzle_flash.png", out, 4)
    texture_import(TEX / "muzzle_flash.png", quality=True)


def noise_texture():
    """256 x 256 tileable fractal noise in three channels (flames, haze, embers)."""
    size = 256
    img = np.ones((size, size, 4))
    img[..., 0] = tile_noise(size, 4, 11, 5)
    img[..., 1] = tile_noise(size, 8, 12, 4)
    img[..., 2] = tile_noise(size, 16, 13, 3)
    write_png(TEX / "noise.png", img, 3)
    texture_import(TEX / "noise.png", lossless=True)


def soft_dot():
    size = 64
    yy, xx = np.mgrid[0:size, 0:size] / (size - 1)
    r = np.hypot(xx - 0.5, yy - 0.5) * 2
    img = np.ones((size, size, 4))
    img[..., 3] = np.clip(np.exp(-(r / 0.42) ** 2) * smoothstep(1.0, 0.8, r) * 1.05, 0, 1)
    write_png(TEX / "soft_dot.png", img, 4)
    texture_import(TEX / "soft_dot.png")


def droplet():
    """32 x 128: a soft elongated drop, the head at the top (the direction of travel)."""
    h, w = 128, 32
    yy, xx = np.mgrid[0:h, 0:w]
    v = 1 - yy / (h - 1)  # 1 at the head
    x = (xx / (w - 1) - 0.5) * 2
    width = 0.25 + 0.75 * smoothstep(0.0, 0.85, v) * smoothstep(1.0, 0.8, v)
    img = np.ones((h, w, 4))
    img[..., 3] = smoothstep(width, width * 0.3, np.abs(x)) * smoothstep(0.0, 0.25, v) * smoothstep(1.0, 0.93, v)
    write_png(TEX / "droplet.png", img, 4)
    texture_import(TEX / "droplet.png")


# ---------------------------------------------------------------- decals


def height_normal(height, strength):
    gx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5
    gy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5
    n = np.stack((-gx * strength, gy * strength, np.ones_like(height)), axis=2)
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    out = np.ones(height.shape + (4,))
    out[..., :3] = n * 0.5 + 0.5
    return out


def decal(name, albedo, alpha, height, strength=6.0):
    img = np.ones(albedo.shape[:2] + (4,))
    img[..., :3] = srgb(albedo)
    img[..., 3] = np.clip(alpha, 0, 1)
    write_png(DECALS / f"{name}.png", img, 4)
    texture_import(DECALS / f"{name}.png")
    write_png(DECALS / f"{name}_normal.png", height_normal(height, strength), 3)
    texture_import(DECALS / f"{name}_normal.png", normal=True)


def bullet_holes():
    size = 256
    yy, xx = np.mgrid[0:size, 0:size] / (size - 1)
    x, y = (xx - 0.5) * 2, (yy - 0.5) * 2
    r = np.hypot(x, y)
    th = np.arctan2(y, x)
    for v in range(2):
        seed = 500 + v * 10
        n = noise2(size, size, 18, seed, 4)
        fine = noise2(size, size, 5, seed + 1, 3)
        # Wood: a dark hole, raw wood torn out along the grain (X) in fibres, splinters running off it.
        rng = np.random.default_rng(seed + 5)
        hole = smoothstep(0.11, 0.07, r * (0.8 + 0.4 * n))
        fibres = np.repeat(noise2(size, size // 8, 3, seed + 3, 3), 8, axis=1)[:, :size]
        ell = np.hypot(x / 1.0, y / 0.42)
        torn = smoothstep(0.42 + 0.22 * (n - 0.5), 0.2, ell) * (0.55 + 0.45 * fibres)
        streaks = np.zeros_like(r)
        for c in range(6):
            yc = rng.uniform(-0.12, 0.12)
            side = 1 if c % 2 else -1
            length = rng.uniform(0.35, 0.75)
            width = rng.uniform(0.008, 0.018)
            along = x * side
            streaks = np.maximum(streaks, smoothstep(width, 0.0, np.abs(y - yc - 0.03 * np.sin(along * 9)))
                                 * smoothstep(length, length * 0.6, along) * (along > 0.05))
        raw = np.array([0.74, 0.58, 0.39])
        wood = raw * (0.72 + 0.4 * fibres[..., None])
        wood = wood * (1 - hole[..., None]) + np.array([0.035, 0.025, 0.02]) * hole[..., None]
        alpha = np.clip(np.maximum(torn * 0.9, streaks * 0.75) + hole, 0, 1)
        h = -hole * 1.0 + torn * 0.3 * fibres + streaks * 0.2 - smoothstep(0.3, 0.08, r) * 0.25
        decal(f"hole_wood_{v}", wood, alpha, h, 5.0)
        # Rock: a crushed pale crater with radial cracks.
        hole = smoothstep(0.1, 0.06, r * (0.85 + 0.3 * n))
        spall = 0.3 + 0.16 * (n - 0.5) + 0.06 * np.sin(th * 7 + v)
        crater = smoothstep(spall, spall * 0.7, r)
        cracks = np.zeros_like(r)
        rng = np.random.default_rng(seed + 2)
        for c in range(7):
            a = rng.uniform(0, 2 * np.pi)
            length = rng.uniform(0.35, 0.8)
            d = np.abs(np.angle(np.exp(1j * (th - a - 0.15 * (n - 0.5) * r * 4))))
            cracks = np.maximum(cracks, smoothstep(0.035, 0.0, d * r) * smoothstep(length, length * 0.5, r) * (r > 0.08))
        rock = np.ones((size, size, 3)) * (0.5 + 0.22 * fine[..., None])
        rock = rock * (1 - 0.65 * cracks[..., None])
        rock = rock * (1 - hole[..., None]) + 0.04 * hole[..., None]
        alpha = np.clip(crater * 0.85 + cracks * 0.8 + hole, 0, 1)
        h = -hole - crater * 0.35 * (1 - r / max(spall.max(), 0.01)) - cracks * 0.15
        decal(f"hole_rock_{v}", rock, alpha, h, 6.0)
        # Metal: a punched hole, its edge torn into bright petals pushed out, paint chipped off round it.
        th_n = noise2(size, size, 9, seed + 7, 3)
        hole = smoothstep(0.085, 0.065, r * (0.95 + 0.12 * n))
        lattice = np.random.default_rng(seed + 8).random(11)
        at = (th + np.pi) / (2 * np.pi) * 11
        i0 = np.floor(at).astype(int) % 11
        f = at - np.floor(at)
        around = lattice[i0] * (1 - f) + lattice[(i0 + 1) % 11] * f
        petals = 0.09 + 0.045 * around ** 2 + 0.015 * (th_n - 0.5)
        rim = smoothstep(petals, petals - 0.025, r) * (1 - hole) * (0.7 + 0.3 * th_n)
        chips = smoothstep(0.24 + 0.14 * (n - 0.5), 0.12, r)
        metal = np.ones((size, size, 3)) * 0.2 * (0.8 + 0.4 * fine[..., None])
        metal = metal * (1 - rim[..., None]) + np.array([0.78, 0.76, 0.72]) * (0.85 + 0.3 * th_n[..., None]) * rim[..., None]
        metal = metal * (1 - hole[..., None]) + 0.015 * hole[..., None]
        alpha = np.clip(chips * 0.85 + rim + hole, 0, 1)
        h = -hole * 1.2 + rim * 0.7 - smoothstep(0.3, 0.0, r) * 0.2
        decal(f"hole_metal_{v}", metal, alpha, h, 7.0)


def blood_and_scuffs():
    size = 256
    yy, xx = np.mgrid[0:size, 0:size] / (size - 1)
    x, y = (xx - 0.5) * 2, (yy - 0.5) * 2
    r = np.hypot(x, y)
    for v in range(2):
        rng = np.random.default_rng(600 + v)
        n = noise2(size, size, 26, 610 + v, 4)
        blob = smoothstep(0.42 + 0.25 * (n - 0.5), 0.3, r * (1 + 0.25 * np.sin(np.arctan2(y, x) * 3 + v)))
        drops = np.zeros_like(r)
        for d in range(9):
            a = rng.uniform(0, 2 * np.pi)
            dist = rng.uniform(0.45, 0.85)
            rad = rng.uniform(0.025, 0.07)
            cx, cy = np.cos(a) * dist, np.sin(a) * dist
            drops = np.maximum(drops, smoothstep(rad, rad * 0.6, np.hypot(x - cx, y - cy)))
        cov = np.clip(blob + drops, 0, 1)
        col = np.ones((size, size, 3)) * np.array([0.085, 0.006, 0.005])
        col *= (0.7 + 0.45 * n)[..., None]
        decal(f"blood_{v}", col, cov * 0.92, cov * 0.4 + n * 0.05, 3.0)
    # A scuff: dark disturbed soil with flecks, soft edged.
    n = noise2(size, size, 12, 640, 4)
    cov = smoothstep(0.75, 0.2, r * (0.8 + 0.4 * n)) * (0.5 + 0.5 * n)
    col = np.ones((size, size, 3)) * 0.16 * (0.7 + 0.5 * noise2(size, size, 3, 641, 2))[..., None]
    decal("scuff", col, cov * 0.8, -cov * 0.3 + n * 0.1, 3.0)


# ---------------------------------------------------------------- chunk meshes


def _canonical(bm):
    """Builds a mesh from a bmesh with faces in a fixed order (every build writes the same file)."""
    verts = [v.co.copy() for v in bm.verts]
    bm.verts.index_update()
    faces = []
    for f in bm.faces:
        vs = [v.index for v in f.verts]
        k = vs.index(min(vs))
        faces.append(vs[k:] + vs[:k])
    faces.sort()
    return verts, faces


def _normalised(bm, size=1.0):
    """Centres a shape and scales it so its largest side is `size`."""
    lo = Vector((min(v.co.x for v in bm.verts), min(v.co.y for v in bm.verts), min(v.co.z for v in bm.verts)))
    hi = Vector((max(v.co.x for v in bm.verts), max(v.co.y for v in bm.verts), max(v.co.z for v in bm.verts)))
    centre = (lo + hi) / 2
    scale = size / max(hi - lo)
    for v in bm.verts:
        v.co = (v.co - centre) * scale


def _object(name, bm, variant, flag=0.0, smooth=False):
    """A mesh object with box-projected UVs and the variant in UV2 = (variant, flag)."""
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    verts, faces = _canonical(bm)
    bm.free()
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    uv = me.uv_layers.new(name="UVMap")
    var = me.uv_layers.new(name="Variant")
    for p in me.polygons:
        p.use_smooth = smooth
        axis = max(range(3), key=lambda a: abs(p.normal[a]))
        axes = [a for a in range(3) if a != axis]
        for li in p.loop_indices:
            c = me.vertices[me.loops[li].vertex_index].co
            uv.data[li].uv = (c[axes[0]] * 0.5 + 0.5 + variant * 0.17, c[axes[1]] * 0.5 + 0.5 + variant * 0.29)
            var.data[li].uv = (variant + 0.5, flag)
    me.uv_layers.active = uv
    o = bpy.data.objects.new(name, me)
    return o


def _chip(rng, elong=(1.0, 1.0, 1.0), jitter=0.22, subdiv=0):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * elong[0], v.co.y * elong[1], v.co.z * elong[2])) * (1 + rng.uniform(-jitter, jitter))
    # Knock off a corner or two: flatten vertices beyond a random plane.
    for _ in range(2):
        n = Vector(rng.normal(size=3)).normalized()
        d = rng.uniform(0.35, 0.7)
        for v in bm.verts:
            k = v.co.dot(n)
            if k > d:
                v.co -= n * (k - d)
    _normalised(bm)
    return bm


def _splinter(rng):
    """A sliver of wood: a thin twisted prism, pointed at both ends, along X."""
    bm = bmesh.new()
    segs = 5
    w, t = rng.uniform(0.12, 0.2), rng.uniform(0.05, 0.08)
    rings = []
    for i in range(segs + 1):
        s = i / segs
        x = s - 0.5
        taper = math.sin(math.pi * s) ** 0.6 + 0.08
        twist = rng.uniform(-0.15, 0.15) * s
        ring = []
        for a, b in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
            y, z = a * w / 2 * taper, b * t / 2 * taper
            ring.append(bm.verts.new((x, y * math.cos(twist) - z * math.sin(twist), y * math.sin(twist) + z * math.cos(twist)
                                       + rng.uniform(-0.01, 0.01))))
        rings.append(ring)
    for r0, r1 in zip(rings, rings[1:]):
        for j in range(4):
            k = (j + 1) % 4
            bm.faces.new((r0[j], r0[k], r1[k], r1[j]))
    bm.faces.new(rings[0])
    bm.faces.new(list(reversed(rings[-1])))
    _normalised(bm)
    return bm


def _plate(rng, curve=0.25, thickness=0.06, jag=0.2, n=6):
    """A curved flake (bark, tin): a jagged outline on a bent sheet, with some thickness."""
    bm = bmesh.new()
    outline = []
    for i in range(n):
        a = 2 * math.pi * i / n + rng.uniform(-0.3, 0.3)
        r = 0.5 * (1 + rng.uniform(-jag, jag))
        outline.append((math.cos(a) * r, math.sin(a) * r * rng.uniform(0.55, 0.9)))
    top = [bm.verts.new((x, y, curve * x * x + thickness / 2)) for x, y in outline]
    bottom = [bm.verts.new((x, y, curve * x * x - thickness / 2)) for x, y in outline]
    bm.faces.new(top)
    bm.faces.new(list(reversed(bottom)))
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((bottom[i], bottom[j], top[j], top[i]))
    _normalised(bm)
    return bm


def _kit_shard(blend_objects, name):
    src = blend_objects[name]
    bm = bmesh.new()
    bm.from_mesh(src.data)
    bmesh.ops.transform(bm, matrix=src.matrix_world, verts=bm.verts)
    _normalised(bm)
    return bm


def chunks():
    # The kit's debris, read from its .blend (metres), before our scene is set up.
    bpy.ops.wm.open_mainfile(filepath=str(KIT / "Blender" / "PSG_C4_Debris.blend"))
    kit = {o.name: o for o in bpy.data.objects if o.type == "MESH"}
    shards = [_kit_shard(kit, f"SM_C4_ConcreteShard_{i}") for i in range(1, 5)]
    fragments = [_kit_shard(kit, f"SM_C4_MetalFragment_{i}") for i in range(1, 3)]
    # bmesh copies outlive the file: keep them as data before resetting.
    shard_data = []
    for bm in shards + fragments:
        verts, faces = _canonical(bm)
        shard_data.append((verts, faces))
        bm.free()
    reset()
    out = collection("HOLLOW_EXPORT")
    rng = np.random.default_rng(77)

    def from_data(verts, faces):
        bm = bmesh.new()
        vs = [bm.verts.new(v) for v in verts]
        for f in faces:
            bm.faces.new([vs[i] for i in f])
        return bm

    groups = {
        # Stone: the kit's four shards, four chips of our own and two rounder clods (for dirt).
        "chunk_stone": [(from_data(*shard_data[i]), 0.0, False) for i in range(4)]
        + [(_chip(rng, (1.0, rng.uniform(0.55, 0.8), rng.uniform(0.35, 0.6))), 0.0, False) for _ in range(4)]
        + [(_chip(rng, (1.0, 0.85, 0.7), jitter=0.12, subdiv=1), 1.0, True) for _ in range(2)],
        # Wood: five splinters (raw wood) and three flakes of bark (UV2.y = 1).
        "chunk_wood": [(_splinter(rng), 0.0, False) for _ in range(5)]
        + [(_plate(rng, curve=0.35, thickness=0.12, jag=0.25), 1.0, False) for _ in range(3)],
        # Metal: the kit's two fragments and two torn tin shards.
        "chunk_metal": [(from_data(*shard_data[4 + i]), 0.0, False) for i in range(2)]
        + [(_plate(rng, curve=0.2, thickness=0.03, jag=0.35, n=7), 1.0, False) for _ in range(2)],
    }
    objects = []
    for name, parts in groups.items():
        objs = []
        for i, (bm, flag, smooth) in enumerate(parts):
            o = _object(f"{name}_{i}", bm, i, flag, smooth)
            out.objects.link(o)
            objs.append(o)
        select_only(objs)
        bpy.ops.object.join()
        joined = bpy.context.view_layer.objects.active
        joined.name = name
        joined.data.name = name
        objects.append(joined)
        log(f"{name}: {len(parts)} shapes, {len(joined.data.vertices)} vertices")
    path = FX / "chunks.glb"
    select_only(objects)
    bpy.ops.export_scene.gltf(
        filepath=str(path), export_format="GLB", use_selection=True, export_image_format="NONE",
        export_materials="NONE", export_vertex_color="NONE", export_yup=True, export_apply=False,
        export_texcoords=True, export_normals=True, export_tangents=False, export_animations=False,
        export_extras=False, export_cameras=False, export_lights=False)
    write_scene_import(path, {})
    log(f"exported {path.name}")


def textures():
    TEX.mkdir(parents=True, exist_ok=True)
    muzzle_flash_atlas()
    noise_texture()
    soft_dot()
    droplet()


def decals():
    DECALS.mkdir(parents=True, exist_ok=True)
    bullet_holes()
    blood_and_scuffs()


PARTS = {"c4": c4, "textures": textures, "decals": decals, "chunks": chunks}

for part in script_args() or list(PARTS):
    PARTS[part]()

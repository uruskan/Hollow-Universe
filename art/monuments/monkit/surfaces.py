"""The monuments' surfaces (MONUMENTS.md §8.2): tileable texture sets made here from noise (no scans, so nothing to
credit), the Godot materials that draw them with `shaders/monument.gdshader`, and Blender materials that draw them
the same way for look-dev renders.

Every set is an albedo (RGB, and in A the paint mask: where the vertex tint applies), an ORM (occlusion, roughness,
metal) and an OpenGL normal map, square and seamless, `TILE` metres per repeat. Meshes carry UVs in metres (the
builder projects every face onto its main axis), so texel density is the same on every monument.

    blender -b --factory-startup --python art/monuments/build.py -- --surfaces      # rebuild the textures
"""
from pathlib import Path

import numpy as np

from . import noise as N

SIZE = 1024

# Material name in Blender -> (texture set, metres per repeat, Godot options).
MATERIALS = {
    "M_Mon_Concrete": ("concrete", 4.0, {}),
    "M_Mon_Formwork": ("formwork", 4.8, {}),
    "M_Mon_Paint": ("paint", 2.0, {}),
    "M_Mon_Rust": ("rust", 2.5, {}),
    "M_Mon_Corrugated": ("corrugated", 2.0, {}),
    "M_Mon_Asphalt": ("asphalt", 6.0, {}),
    "M_Mon_Grate": ("grate", 0.6, {"alpha": True}),
    "M_Mon_Hull": ("hull", 8.0, {}),
    "M_Mon_Panel": ("panel", 2.4, {}),
    "M_Mon_Chainlink": ("chainlink", 0.5, {"alpha": True}),
    "M_Mon_Glass": (None, 1.0, {"glass": True}),
    "M_Mon_Lamp": (None, 1.0, {"lamp": True}),
    "M_Mon_Gloss": (None, 1.0, {"plain": 0.28}),
    "M_Mon_Matte": (None, 1.0, {"plain": 0.85}),
}
(CONCRETE, FORMWORK, PAINT, RUST, CORRUGATED, ASPHALT, GRATE, HULL, PANEL, CHAINLINK, GLASS, LAMP, GLOSS,
 MATTE) = MATERIALS


def _rgb(lum, tint):
    return np.stack([lum * tint[0], lum * tint[1], lum * tint[2]], axis=2)


def _set(albedo, mask, ao, rough, metal, height, strength):
    """Packs a texture set: albedo RGBA (A = paint mask), ORM, normal map from `height` (metres-ish units scaled by
    `strength`)."""
    a = np.empty(albedo.shape[:2] + (4,), np.float32)
    a[..., :3] = np.clip(albedo, 0, 1)
    a[..., 3] = np.clip(mask, 0, 1)
    orm = np.stack([np.clip(ao, 0, 1), np.clip(rough, 0.04, 1), np.clip(metal, 0, 1)], axis=2).astype(np.float32)
    from hollow.textures import normal_from_height
    return {"albedo": a, "orm": orm, "normal": normal_from_height(height.astype(np.float32), strength)}


# ---------------------------------------------------------------------------------------------------- the sets


def concrete(size, r, form=False):
    """Cast concrete: mottled grey, fine grain, sparse pits and lighter aggregate, faint rain streaks. `form` adds
    the board-formed look of poured walls: panel joints and tie holes on a 2.4 x 1.2 m grid (the tile is 4.8 m)."""
    grain = N.fbm(size, 48, 5, r)
    mott = N.fbm(size, 4, 4, r)
    mid = N.fbm(size, 12, 3, r)
    hue = N.fbm(size, 3, 3, r)
    f1, _, cell = N.worley(size, 110, r)
    pits = N.smooth(0.2 - f1, 0.0, 0.12) * (cell > 0.62)
    f1b, _, cellb = N.worley(size, 70, r)
    aggregate = N.smooth(0.16 - f1b, 0.0, 0.08) * (cellb > 0.75)
    streak = N.fbm(size, 48, 3, r, stretch=20.0)
    cluster = N.smooth(N.fbm(size, 3, 3, r), 0.5, 0.7)
    streaks = N.smooth(streak, 0.62, 0.8) * cluster
    stain = N.smooth(N.fbm(size, 4, 5, r), 0.6, 0.82)
    pits = pits * N.smooth(N.fbm(size, 5, 3, r), 0.35, 0.65)
    lum = 0.66 + (mott - 0.5) * 0.16 + (mid - 0.5) * 0.08 + (grain - 0.5) * 0.07
    lum = lum - pits * 0.2 + aggregate * 0.05 - streaks * 0.06 - stain * 0.07
    height = grain * 0.35 + mid * 0.25 - pits * 1.0 + aggregate * 0.25
    ao = 1 - pits * 0.5
    if form:
        u = (np.arange(size) + 0.5) / size
        uu, vv = u[None, :], u[:, None]
        # Joints: vertical every 2.4 m (half the tile), horizontal every 1.2 m.
        jx = np.abs(((uu * 2) % 1.0) - 0.5) * 2 * 2.4  # metres to the nearest vertical joint, from the joint's centre
        jy = np.abs(((vv * 4) % 1.0) - 0.5) * 2 * 1.2
        dx = 2.4 / 2 - jx / 2
        dy = 1.2 / 2 - jy / 2
        joint = np.maximum(N.smooth(-np.abs(dx) + 0.012, 0.0, 0.008), N.smooth(-np.abs(dy) + 0.008, 0.0, 0.006))
        # Tie holes: two rows per panel, three across, 32 mm across.
        tx = ((uu * 2 * 3) % 1.0) - 0.5
        ty = ((vv * 4 * 2) % 1.0) - 0.5
        hole = N.smooth(0.018 - np.sqrt((tx * 0.8) ** 2 + (ty * 0.6) ** 2), 0.0, 0.006)
        board = N.fbm(size, 4, 2, r, stretch=0.1)
        lum = lum - joint * 0.12 - hole * 0.35 + (board - 0.5) * 0.04
        height = height - joint * 0.6 - hole * 1.5
        ao = ao - hole * 0.5 - joint * 0.2
    if not form:
        # Saw-cut joints along the tile's edges (every 4 m), sealant dark, edges a little chipped.
        u = (np.arange(size) + 0.5) / size
        edge = np.minimum(np.minimum(u, 1 - u)[None, :].repeat(size, axis=0),
                          np.minimum(u, 1 - u)[:, None].repeat(size, axis=1))
        chip = N.fbm(size, 32, 3, r)
        joint = N.smooth(0.0022 + (chip - 0.5) * 0.002 - edge, 0.0, 0.0012)
        lum = lum - joint * 0.16
        height = height - joint * 1.2
        ao = ao - joint * 0.35
    warm = (hue - 0.5) * 0.05
    albedo = _rgb(lum, (1.0, 0.985, 0.955))
    albedo[..., 0] += warm - stain * 0.012
    albedo[..., 1] += stain * 0.006
    albedo[..., 2] -= warm + stain * 0.01
    rough = 0.9 + (grain - 0.5) * 0.08 + pits * 0.05 - streaks * 0.06
    return _set(albedo, np.ones_like(lum), ao, rough, np.zeros_like(lum), height, 2.2)


def paint(size, r):
    """Painted steel: a near-white coat (the vertex tint gives its colour) with orange peel, chips down to dark primer
    and bare metal, scratches, and rust running down from the worst of it."""
    peel = N.fbm(size, 96, 3, r)
    blot = N.fbm(size, 6, 5, r)
    # Chips: islands where a broken-up field crosses a threshold, more where the blots are.
    field = N.fbm(size, 24, 5, r) + (blot - 0.5) * 0.6
    chips = N.smooth(field, 0.70, 0.73)
    primer = N.smooth(field, 0.64, 0.68) * (1 - chips)
    scratches = N.lines(size, 150, r, length=(0.01, 0.06), width=1, horizontal_bias=0.6, strength=(0.25, 0.6))
    scratches = np.maximum(scratches, N.blur(scratches, 1) * 0.8)
    bare = np.maximum(chips, scratches * 0.9)
    run = N.drip(N.blur(chips, 2), 90, 0.965) * (0.5 + 0.5 * N.fbm(size, 32, 3, r, stretch=8.0))
    rust = np.clip(run * 1.25 - bare * 0.5, 0, 1) * 0.85
    grime = N.fbm(size, 4, 5, r)
    lum = 0.8 + (peel - 0.5) * 0.04 + (blot - 0.5) * 0.06 + (grime - 0.5) * 0.08
    albedo = _rgb(lum, (1.0, 1.0, 1.0))
    primer_col = np.array([0.45, 0.26, 0.2])
    metal_col = np.array([0.33, 0.33, 0.34])
    rust_col = np.stack([0.46 + 0.1 * blot, 0.24 + 0.05 * blot, 0.12 + 0.02 * blot], axis=2)
    albedo = albedo * (1 - primer[..., None]) + primer_col * primer[..., None]
    albedo = albedo * (1 - bare[..., None]) + metal_col * bare[..., None]
    albedo = albedo * (1 - rust[..., None]) + rust_col * rust[..., None]
    mask = (1 - np.maximum(bare, primer)) * (1 - rust)
    height = 1 - bare * 0.8 - primer * 0.3 + (peel - 0.5) * 0.15
    rough = 0.48 + (peel - 0.5) * 0.08 + primer * 0.25 - bare * 0.12 + rust * 0.4
    metal = bare * 0.85
    ao = 1 - bare * 0.15
    return _set(albedo, mask, ao, rough, metal, height, 1.4)


def rust(size, r):
    """Steel left to the weather: layered orange and brown rust, dark pitting, flakes, streaks."""
    a = N.fbm(size, 6, 6, r)
    b = N.fbm(size, 20, 5, r)
    c = N.fbm(size, 64, 3, r)
    f1, f2, cell = N.worley(size, 60, r)
    pits = N.smooth(0.16 - f1, 0.0, 0.1) * (cell > 0.72) * 0.6
    flakes = N.smooth(f2 - f1, 0.0, 0.04)
    streak = N.smooth(N.fbm(size, 48, 4, r, stretch=10.0), 0.5, 0.75)
    dark = np.array([0.25, 0.15, 0.10])
    orange = np.array([0.52, 0.27, 0.12])
    ochre = np.array([0.60, 0.42, 0.22])
    t1 = N.smooth(a, 0.38, 0.62)[..., None]
    t2 = N.smooth(b, 0.6, 0.75)[..., None]
    albedo = dark * (1 - t1) + orange * t1
    albedo = albedo * (1 - t2 * 0.6) + ochre * t2 * 0.6
    albedo = albedo * (0.85 + 0.3 * c[..., None]) * (1 - pits[..., None] * 0.5) * (1 - streak[..., None] * 0.2)
    height = a * 0.4 + b * 0.3 + c * 0.2 - pits * 0.8 + (1 - flakes) * 0.15
    rough = 0.88 + (c - 0.5) * 0.1
    metal = 0.15 * (1 - t1[..., 0])
    return _set(albedo, np.zeros_like(a), 1 - pits * 0.4, rough, metal, height, 2.6)


def corrugated(size, r):
    """Corrugated sheet: 76 mm waves across the tile (26 to 2 m), a galvanised or painted face (paint mask), rust
    creeping along the troughs and at the bottom of streaks."""
    u = (np.arange(size) + 0.5) / size
    waves = 26
    # A smooth wave without trigonometry: a periodic quartic that looks like a sine.
    t = (u * waves) % 1.0
    s = 4 * t * (1 - t)  # 0 at the joints, 1 mid-wave
    wave = (s * s * (3 - 2 * s))[None, :].repeat(size, axis=0)
    blot = N.fbm(size, 5, 5, r)
    streak = N.fbm(size, 40, 4, r, stretch=12.0)
    trough = 1 - wave
    rustness = N.smooth(blot + streak * 0.5 + trough * 0.15, 0.95, 1.15)
    scratches = N.lines(size, 150, r, length=(0.01, 0.05), horizontal_bias=0.3, strength=(0.3, 0.7))
    lum = 0.78 + (blot - 0.5) * 0.08 + (wave - 0.5) * 0.03
    albedo = _rgb(lum, (0.98, 0.99, 1.0))
    rust_col = np.stack([0.48 + 0.08 * blot, 0.26 + 0.0 * blot, 0.13 + 0.0 * blot], axis=2)
    albedo = albedo * (1 - rustness[..., None]) + rust_col * rustness[..., None]
    albedo = albedo * (1 - scratches[..., None] * 0.3)
    mask = (1 - rustness) * (1 - scratches * 0.6)
    height = wave * 1.0 + (blot - 0.5) * 0.02
    rough = 0.5 + rustness * 0.4 + (blot - 0.5) * 0.1
    metal = (1 - rustness) * 0.25
    ao = 0.82 + 0.18 * wave
    return _set(albedo, mask, ao, rough, metal, height, 9.0)


def asphalt(size, r):
    """Old asphalt: dark binder, light aggregate, patched and cracked, oil stains."""
    grain = N.fbm(size, 128, 3, r)
    f1, f2, cell = N.worley(size, 180, r)
    stones = N.smooth(0.25 - f1, 0.0, 0.12) * (cell > 0.4)
    big = N.fbm(size, 4, 5, r)
    cf1, cf2, _ = N.worley(size, 7, r, jitter=1.0)
    crack_field = N.fbm(size, 10, 4, r)
    cracks = N.smooth(0.035 - (cf2 - cf1), 0.0, 0.025) * N.smooth(crack_field, 0.55, 0.7)
    stains = N.smooth(N.fbm(size, 8, 5, r), 0.66, 0.8)
    patch = N.smooth(N.fbm(size, 3, 3, r), 0.7, 0.72)
    lum = 0.27 + (grain - 0.5) * 0.08 + stones * 0.14 + (big - 0.5) * 0.08 - cracks * 0.15 - stains * 0.07
    lum = lum * (1 - patch * 0.25)
    albedo = _rgb(lum, (1.0, 1.0, 1.02))
    height = grain * 0.3 + stones * 0.5 - cracks * 1.2
    rough = 0.86 + (grain - 0.5) * 0.08 - stains * 0.25
    return _set(albedo, np.ones_like(lum), 1 - cracks * 0.6, rough, np.zeros_like(lum), height, 2.0)


def grate(size, r):
    """Bar grating for walkways: bearing bars every 30 mm, cross bars every 100 mm (0.6 m tile), alpha in A
    (the paint mask channel; the grate's material is alpha-tested and untinted)."""
    u = (np.arange(size) + 0.5) / size
    bars_u = 20   # 30 mm
    bars_v = 6    # 100 mm
    bu = np.abs(((u * bars_u) % 1.0) - 0.5)[None, :]
    bv = np.abs(((u * bars_v) % 1.0) - 0.5)[:, None]
    bearing = (bu > 0.5 - 0.1).astype(np.float32).repeat(size, axis=0)  # 3 mm of 30
    cross = (bv > 0.5 - 0.04).astype(np.float32).repeat(size, axis=1)   # 4 mm of 100
    solid = np.maximum(bearing, cross)
    blot = N.fbm(size, 6, 4, r)
    rustness = N.smooth(blot, 0.6, 0.8)
    lum = 0.55 + (blot - 0.5) * 0.15
    albedo = _rgb(lum, (0.95, 0.96, 1.0))
    rust_col = np.stack([0.42 + 0.06 * blot, 0.24 + 0.0 * blot, 0.13 + 0.0 * blot], axis=2)
    albedo = albedo * (1 - rustness[..., None]) + rust_col * rustness[..., None]
    height = solid * 1.0 + bearing * 0.3
    rough = 0.55 + rustness * 0.35
    return _set(albedo, solid, np.ones_like(lum), rough, (1 - rustness) * 0.6, height, 3.0)


def hull(size, r):
    """Ship hull plating, 8 m tile: plates of several sizes on a 0.5 m grid, recessed seams, rows of fasteners, a
    shade per plate, wear along seams, scorch and streaks. The paint mask is the plates' coat."""
    cells = 16  # 0.5 m
    owner = -np.ones((cells, cells), np.int64)
    rects = []
    idx = 0
    for y in range(cells):
        for x in range(cells):
            if owner[y, x] >= 0:
                continue
            w = int(r.choice([1, 2, 2, 3, 4, 4, 6, 8]))
            h = int(r.choice([1, 2, 2, 3, 4, 4]))
            w = min(w, cells - x)
            h = min(h, cells - y)
            # Shrink until free.
            while w > 1 and (owner[y, x:x + w] >= 0).any():
                w -= 1
            while h > 1 and (owner[y:y + h, x:x + w] >= 0).any():
                h -= 1
            owner[y:y + h, x:x + w] = idx
            rects.append((x, y, w, h))
            idx += 1
    px = size // cells
    up = np.kron(owner, np.ones((px, px), np.int64))
    # Seams: where the owner changes to the right or below (wrapping).
    seam = ((up != np.roll(up, 1, axis=1)) | (up != np.roll(up, 1, axis=0))).astype(np.float32)
    seam = np.maximum(seam, np.roll(seam, -1, axis=1) * 0.6)
    seam = np.maximum(seam, np.roll(seam, -1, axis=0) * 0.6)
    near = N.blur(seam, 3)
    shade = r.random(idx)[up] * 0.08 - 0.04
    # Fasteners: dots along each plate's edges every 8 cm, 1 cm in.
    bolts = np.zeros((size, size), np.float32)
    step = max(4, int(round(size / 8.0 * 0.08 / 1.0)))
    inset = max(2, size // 800 + 3)
    for (x, y, w, h) in rects:
        x0, y0, x1, y1 = x * px + inset, y * px + inset, (x + w) * px - inset, (y + h) * px - inset
        if r.random() < 0.35:
            continue
        for xx in range(x0, x1, step):
            bolts[y0 % size, xx % size] = 1
            bolts[y1 % size, xx % size] = 1
        for yy in range(y0, y1, step):
            bolts[yy % size, x0 % size] = 1
            bolts[yy % size, x1 % size] = 1
    bolts = np.clip(N.blur(bolts, 1) * 4, 0, 1)
    blot = N.fbm(size, 6, 5, r)
    wear = N.smooth(near * 1.4 + (blot - 0.5) * 0.8, 0.55, 0.85)
    streak = N.smooth(N.fbm(size, 48, 4, r, stretch=10.0), 0.55, 0.8)
    lum = 0.8 + shade + (blot - 0.5) * 0.06 - seam * 0.35 - streak * 0.06
    albedo = _rgb(lum, (1.0, 1.0, 1.0))
    metal_col = np.array([0.4, 0.41, 0.43])
    albedo = albedo * (1 - wear[..., None]) + metal_col * wear[..., None]
    albedo = albedo * (1 - bolts[..., None] * 0.25)
    mask = (1 - wear) * (1 - seam * 0.8)
    height = 1 - seam * 1.0 + bolts * 0.6 + shade * 2
    rough = 0.45 + wear * -0.1 + streak * 0.2 + (blot - 0.5) * 0.08
    metal = wear * 0.8
    return _set(albedo, mask, 1 - seam * 0.4, rough, metal, height, 2.5)


def panel(size, r):
    """Clean composite wall panels for laboratories and habitats, 2.4 m tile: a 1.2 x 1.2 m grid of panels with
    seams and corner screws, faint smudges."""
    u = (np.arange(size) + 0.5) / size
    d = np.abs(((u * 2) % 1.0) - 0.5)          # half-panel units across
    seam_u = N.smooth(d - 0.5 + 0.006, 0.0, 0.004)[None, :].repeat(size, axis=0)
    seam_v = N.smooth(d - 0.5 + 0.006, 0.0, 0.004)[:, None].repeat(size, axis=1)
    seam = np.maximum(seam_u, seam_v)
    cu = ((u * 2) % 1.0)[None, :]
    cv = ((u * 2) % 1.0)[:, None]
    screws = np.zeros((size, size), np.float32)
    for sx in (0.04, 0.96):
        for sy in (0.04, 0.96):
            screws = np.maximum(screws, N.smooth(0.012 - np.sqrt((cu - sx) ** 2 + (cv - sy) ** 2), 0, 0.004))
    smudge = N.smooth(N.fbm(size, 5, 5, r), 0.6, 0.85)
    grain = N.fbm(size, 64, 3, r)
    lum = 0.86 + (grain - 0.5) * 0.03 - smudge * 0.08 - seam * 0.4 - screws * 0.3
    albedo = _rgb(lum, (1.0, 1.0, 1.0))
    mask = 1 - seam * 0.9 - screws
    height = 1 - seam * 1.0 + screws * 0.4
    rough = 0.38 + smudge * 0.2 + (grain - 0.5) * 0.05
    return _set(albedo, mask, 1 - seam * 0.3, rough, screws * 0.8, height, 1.6)


def chainlink(size, r):
    """Chain-link fence mesh, 0.5 m tile: wires on the two diagonals every 50 mm (a diamond mesh), galvanised with
    rust in places; coverage in A."""
    u = (np.arange(size) + 0.5) / size
    uu, vv = u[None, :], u[:, None]
    n = 10  # diamonds across the tile
    a = np.abs((((uu + vv) * n) % 1.0) - 0.5)
    b = np.abs((((uu - vv) * n) % 1.0) - 0.5)
    w = 0.5 - 0.07
    wire = np.maximum(N.smooth(a, w - 0.02, w), N.smooth(b, w - 0.02, w))
    blot = N.fbm(size, 4, 4, r)
    rustness = N.smooth(blot, 0.62, 0.8)
    lum = 0.6 + (blot - 0.5) * 0.1
    albedo = _rgb(lum, (0.97, 0.98, 1.0))
    rust_col = np.stack([0.45 + 0.0 * blot, 0.26 + 0.0 * blot, 0.14 + 0.0 * blot], axis=2)
    albedo = albedo * (1 - rustness[..., None]) + rust_col * rustness[..., None]
    rough = 0.5 + rustness * 0.4
    return _set(albedo, wire, np.ones_like(lum), rough, (1 - rustness) * 0.7, wire, 2.0)


SETS = {
    "concrete": lambda s, r: concrete(s, r),
    "formwork": lambda s, r: concrete(s, r, form=True),
    "paint": paint,
    "rust": rust,
    "corrugated": corrugated,
    "asphalt": asphalt,
    "grate": grate,
    "hull": hull,
    "panel": panel,
    "chainlink": chainlink,
}


def folder():
    from hollow import TEXTURES
    return TEXTURES / "monuments"


def build_textures(names=None, size=SIZE, force=False):
    """Writes every texture set (or `names`) to game/assets/textures/monuments with their Godot import files."""
    from hollow import log
    from hollow.textures import write_png, write_texture_import
    out = folder()
    for i, (name, make) in enumerate(SETS.items()):
        if names and name not in names:
            continue
        paths = [out / f"{name}_albedo.png", out / f"{name}_orm.png", out / f"{name}_normal.png"]
        if not force and all(p.exists() for p in paths):
            continue
        log(f"surface {name}")
        tex = make(size, N.rng(7001 + i * 31))
        write_png(paths[0], tex["albedo"], channels=4)
        write_png(paths[1], tex["orm"])
        write_png(paths[2], tex["normal"])
        write_texture_import(paths[0])
        write_texture_import(paths[1])
        write_texture_import(paths[2], normal_map=True)
    return out


def texture_paths(name):
    out = folder()
    return out / f"{name}_albedo.png", out / f"{name}_orm.png", out / f"{name}_normal.png"

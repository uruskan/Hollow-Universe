"""The Iron Wake's finishes (docs/IRON_WAKE.md §9.2 "As built"): the textures derived for them, the curvature masks baked
into the vertex colour, and the ShaderMaterials on game/shaders/iron_wake_surface.gdshader.

After the owner's look on 15 October 2026 ("blotchy, square-patched rust" on every steel finish: the asset derived them
all from Poly Haven's Rusty Metal Sheet):
- gunmetal and dark steel: a clean, scratched base re-derived from Blue Metal Plate (the asset's own CC0 source), its
  plate seams painted out, its large blotches flattened, greyed and darkened, metallic;
- every paint: Green Metal Rust (Poly Haven, CC0), painted ship steel, its paint recoloured by each finish's tint and
  its chips and rust kept where the source has them (the albedo's alpha is the paint layer);
- deck tread: Metal Plate (Poly Haven, CC0), diamond plate at a real plate's pitch (about 3 cm);
- rust only in cavities, at the contact shadow and in some chips, and edge wear where the surface turns fast, all in
  the one shader (Godot's own lighting) from the baked occlusion and curvature in the vertex colour.
The derived 2K maps are written once into game/assets/textures/iron_wake (the house's 2K rule); a later run keeps them,
so the sources are needed only to make them again.
"""
import hashlib
import json
import math
from pathlib import Path

import numpy as np

from hollow import GAME, MATERIALS, SOURCES, log, res_path
from hollow.textures import downscale, read, renormalise, write_png, write_texture_import

HERE = Path(__file__).resolve().parent
FINISHES = HERE / "iron_wake_finishes.json"
OUT = GAME / "assets" / "textures" / "iron_wake"
SHADER = GAME / "shaders" / "iron_wake_surface.gdshader"

# Where the sources are, each checked against the hash its provider published or the asset's provenance recorded.
POLYHAVEN = SOURCES / "Downloads" / "polyhaven"
ASSET_SOURCES = [SOURCES / "IronWake_Buildable_R01" / "Textures" / "Source_PolyHaven",
                 SOURCES / "IronWake_Benchmark_20261015" / "Asset" / "Textures" / "Source_PolyHaven"]
SHA256 = {
    "blue_metal_plate_diff_4k.png": "cd7dfc69866c6b67af3e1fe8a17b700fdf24954cebd9fa46d52877ae8af05d64",
    "blue_metal_plate_arm_4k.png": "845785e231b3f502ee1a1e9f0242285247b9b4fd44fa5e9c4838634a338f7e41",
    "blue_metal_plate_nor_gl_4k.png": "fa0e94e8632abc57c098a27f18bcf93dd2018f78b94d88465f0ae9b42761a475",
    "rusty_metal_sheet_diff_4k.png": "b6cb2479d9036626acad12cb897c4f4f48d8d358dd3b91ea9572ee42ea150620",
}
MD5 = {
    "green_metal_rust_diff_2k.png": "d6b7e4ba43193c1a526bd943f52dc11c",
    "green_metal_rust_nor_gl_2k.png": "7e6eb0c41507137b07c84a4f8956dcb4",
    "green_metal_rust_arm_2k.png": "580bb5dcfec0c1aa91058b4ad0a40885",
    "metal_plate_diff_2k.png": "d3b4ffca928122f2428a627867b202fd",
    "metal_plate_nor_gl_2k.png": "f73df0b0ecd4fc29c3620a5e448b1432",
    "metal_plate_arm_2k.png": "9b137ea97781adce129899d168f2fc9d",
}

# What each finish becomes: its surface (the derived set), the physical repeat of that set's texture in metres, and
# the shader's wear. A finish's tint and normal strength stay the asset's (iron_wake_finishes.json).
SURFACES = {
    "gunmetal": {"albedo": "T_IW_Gunmetal_BaseColor_2K.png", "orm": "T_IW_Gunmetal_ORM_2K.png",
                 "normal": "T_IW_Gunmetal_NormalGL_2K.png", "metres": 2.0},
    "paint": {"albedo": "T_IW_PaintChipped_BaseColor_2K.png", "orm": "T_IW_PaintChipped_ORM_2K.png",
              "normal": "T_IW_PaintChipped_NormalGL_2K.png", "metres": 2.0},
    "tread": {"albedo": "T_IW_TreadPlate_BaseColor_2K.png", "orm": "T_IW_TreadPlate_ORM_2K.png",
              "normal": "T_IW_TreadPlate_NormalGL_2K.png", "metres": 0.8},
    "machined": {"albedo": "T_IW_PBR_Machined_BaseColor_2K.png", "orm": "T_IW_PBR_Machined_ORM_2K.png",
                 "normal": "T_IW_PBR_Steel_NormalGL_2K.png", "metres": 2.0},
    "rubber": {"albedo": "T_IW_PBR_Machined_BaseColor_2K.png", "orm": "T_IW_PBR_Rubber_ORM_2K.png",
               "normal": "T_IW_PBR_Steel_NormalGL_2K.png", "metres": 2.0},
    "fabric": {"albedo": "T_IW_PBR_Fabric_BaseColor_2K.png", "orm": "T_IW_PBR_Fabric_ORM_2K.png",
               "normal": "T_IW_PBR_Fabric_NormalGL_2K.png", "metres": 0.3},
}
RUST = "T_IW_Rust_1K.png"
NOISE = "T_IW_WearNoise_512.png"
# Finish -> (surface, normal map instead of the surface's, painted, chip, rust, grime).
FINISH = {
    "TealPaint": ("paint", None, 1, 0.75, 0.55, 0.45),
    "OxidePaint": ("paint", None, 1, 0.75, 0.55, 0.45),
    "WarningYellow": ("paint", None, 1, 0.9, 0.45, 0.45),
    "IvoryPaint": ("paint", None, 1, 0.8, 0.45, 0.45),
    "BrownPaint": ("paint", None, 1, 0.75, 0.55, 0.45),
    "OlivePaint": ("paint", None, 1, 0.75, 0.55, 0.45),
    "BlackPaint": ("paint", None, 1, 0.6, 0.4, 0.35),
    "InteriorPaint": ("paint", None, 1, 0.55, 0.3, 0.5),
    "DarkSteel": ("gunmetal", None, 0, 0.55, 0.45, 0.45),
    "Gunmetal": ("gunmetal", None, 0, 0.6, 0.35, 0.4),
    "TreadSteel": ("tread", None, 0, 0.5, 0.35, 0.55),
    "LouvredSteel": ("gunmetal", "T_IW_PBR_Louvre_NormalGL_2K.png", 0, 0.45, 0.45, 0.5),
    "GrateSteel": ("gunmetal", "T_IW_PBR_Grate_NormalGL_2K.png", 0, 0.45, 0.45, 0.5),
    "BrushedSteel": ("machined", None, 0, 0.35, 0.15, 0.3),
    "Rubber": ("rubber", None, 0, 0.0, 0.0, 0.25),
    "SeatFabric": ("fabric", None, 0, 0.0, 0.0, 0.3),
}


def finishes():
    """The asset's sixteen surface definitions and its display tiles, as the import keeps them (iron_wake_finishes.json)."""
    return json.loads(FINISHES.read_text(encoding="utf-8"))


# ------------------------------------------------------------------------------------------------ sources


def _digest(path, algorithm):
    h = hashlib.new(algorithm)
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def source(name):
    """A checked source file: the asset's Poly Haven originals (wherever a copy with the recorded hash is) or the
    Poly Haven downloads the owner approved (by the provider's MD5)."""
    if name in MD5:
        path = POLYHAVEN / ("green_metal_rust" if name.startswith("green_metal_rust") else "metal_plate") / name
        if not path.exists() or _digest(path, "md5") != MD5[name]:
            raise RuntimeError(f"iron_wake: {name} missing or not Poly Haven's file (MD5 {MD5[name]}) at {path}")
        return path
    for folder in ASSET_SOURCES:
        path = folder / name
        if path.exists() and _digest(path, "sha256") == SHA256[name]:
            return path
    raise RuntimeError(f"iron_wake: no copy of {name} with SHA-256 {SHA256[name][:16]}... in {ASSET_SOURCES}")


def srgb_to_linear(a):
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)


def linear_to_srgb(a):
    a = np.clip(a, 0.0, 1.0)
    return np.where(a <= 0.0031308, a * 12.92, 1.055 * a ** (1 / 2.4) - 0.055)


def luminance(lin):
    return lin[..., 0] * 0.2126 + lin[..., 1] * 0.7152 + lin[..., 2] * 0.0722


def blur(a, radius, passes=3):
    """A wrapped box blur (tileable), `passes` times along both axes."""
    out = a.astype(np.float64)
    for _ in range(passes):
        for axis in (0, 1):
            c = np.cumsum(np.concatenate([np.take(out, range(-radius - 1, 0), axis=axis), out,
                                          np.take(out, range(0, radius), axis=axis)], axis=axis), axis=axis)
            n = out.shape[axis]
            hi = np.take(c, range(2 * radius + 1, 2 * radius + 1 + n), axis=axis)
            lo = np.take(c, range(0, n), axis=axis)
            out = (hi - lo) / (2 * radius + 1)
    return out


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def fill_band(a, lo, hi, axis):
    """Paints a band [lo, hi) along `axis` out: the neighbours on either side mirrored into it and cross-faded."""
    w = hi - lo
    n = a.shape[axis]
    before = np.take(a, [(lo - 1 - k) % n for k in range(w)], axis=axis)
    after = np.take(a, [(hi + w - 1 - k) % n for k in range(w)], axis=axis)
    shape = [1] * a.ndim
    shape[axis] = w
    t = smoothstep(0.0, 1.0, np.linspace(0.0, 1.0, w)).reshape(shape)
    fill = before * (1 - t) + after * t
    index = [slice(None)] * a.ndim
    index[axis] = slice(lo, hi)
    a[tuple(index)] = fill
    return a


# The plate seams in Blue Metal Plate at 4K (measured: columns 26.6-30.7 % and 76.8-81.3 %, rows 73 % and 97 %).
SEAMS = [(1080, 1268, 1), (3136, 3344, 1), (2980, 3036, 0), (3960, 4012, 0)]


def _write(name, pixels, channels=3, normal=False):
    path = OUT / name
    write_png(path, pixels, channels)
    write_texture_import(path, normal_map=normal)
    return path


def make_gunmetal():
    """Clean worn gunmetal from Blue Metal Plate: its seams painted out, its big blotches flattened, grey and dark, a
    metal; its light flecks (where its paint chipped) become bright scratches."""
    diff = read(source("blue_metal_plate_diff_4k.png"))[..., :3]
    arm = read(source("blue_metal_plate_arm_4k.png"))[..., :3]
    nor = read(source("blue_metal_plate_nor_gl_4k.png"))[..., :3]
    for lo, hi, axis in SEAMS:
        for a in (diff, arm, nor):
            fill_band(a, lo, hi, axis)
    diff, arm, nor = (downscale(a, 2048) for a in (diff, arm, nor))
    lum = luminance(srgb_to_linear(diff))
    mean = float(lum.mean())
    flat = lum / np.maximum(blur(lum, 160), 1e-4) * mean
    v = (0.25 * lum + 0.75 * flat) / mean
    v = 1.0 + (v - 1.0) * 1.3
    fleck = smoothstep(1.7, 2.6, v)
    base = np.clip(0.24 * v, 0.04, 0.6)
    rgb = np.stack([base * 0.965, base * 0.985, base * 1.0], axis=2)
    _write(SURFACES["gunmetal"]["albedo"], linear_to_srgb(rgb))
    g = arm[..., 1]
    p5, p95 = np.percentile(g, 5), np.percentile(g, 95)
    rough = 0.38 + np.clip((g - p5) / max(p95 - p5, 1e-4), 0, 1) * 0.24
    rough = rough * (1 - 0.35 * fleck)
    orm = np.stack([arm[..., 0], rough, 0.88 + 0.12 * fleck], axis=2)
    _write(SURFACES["gunmetal"]["orm"], orm)
    _write(SURFACES["gunmetal"]["normal"], renormalise(np.dstack([nor, np.ones(nor.shape[:2])]))[..., :3], normal=True)


def make_paint():
    """Chipped paint from Green Metal Rust: its green paint made a neutral layer (the alpha) that each finish's tint
    colours, its chips, scratches and rust kept as they are."""
    diff = read(source("green_metal_rust_diff_2k.png"))[..., :3]
    arm = read(source("green_metal_rust_arm_2k.png"))[..., :3]
    nor = read(source("green_metal_rust_nor_gl_2k.png"))[..., :3]
    lin = srgb_to_linear(diff)
    lum = np.maximum(luminance(lin), 1e-4)
    chroma = lin / lum[..., None]
    ref = np.median(chroma.reshape(-1, 3), axis=0)
    d = np.linalg.norm(chroma - ref, axis=2) / np.linalg.norm(ref)
    mask = 1.0 - smoothstep(0.12, 0.42, d)
    mask *= smoothstep(0.25, 0.6, lum / np.median(lum))          # dark pits and rust holes are not paint
    paint_lum = lum / float(np.median(lum[mask > 0.9])) * 0.214   # the old paint's mean (sRGB 0.5)
    layer = np.repeat(paint_lum[..., None], 3, axis=2)
    rgb = layer * mask[..., None] + lin * (1 - mask[..., None])
    out = np.dstack([linear_to_srgb(rgb), mask])
    _write(SURFACES["paint"]["albedo"], out, channels=4)
    _write(SURFACES["paint"]["orm"], arm)
    _write(SURFACES["paint"]["normal"], renormalise(np.dstack([nor, np.ones(nor.shape[:2])]))[..., :3], normal=True)
    log(f"paint layer: {float(mask.mean()) * 100:.0f} % of the source is paint")


def make_tread():
    """Diamond tread plate from Metal Plate, its colour half greyed so the deck sits in the ship's palette."""
    diff = read(source("metal_plate_diff_2k.png"))[..., :3]
    arm = read(source("metal_plate_arm_2k.png"))[..., :3]
    nor = read(source("metal_plate_nor_gl_2k.png"))[..., :3]
    lin = srgb_to_linear(diff)
    grey = luminance(lin)[..., None]
    rgb = (lin * 0.5 + grey * 0.5) * 1.1
    _write(SURFACES["tread"]["albedo"], linear_to_srgb(rgb))
    _write(SURFACES["tread"]["orm"], arm)
    _write(SURFACES["tread"]["normal"], renormalise(np.dstack([nor, np.ones(nor.shape[:2])]))[..., :3], normal=True)


def make_rust():
    rust = downscale(read(source("rusty_metal_sheet_diff_4k.png"))[..., :3], 1024)
    _write(RUST, rust)


def make_noise(size=512, seed=7):
    """A tileable grey noise (fractal value noise on wrapped lattices) that breaks up the wear masks."""
    rng = np.random.RandomState(seed)
    total = np.zeros((size, size))
    amp, norm = 1.0, 0.0
    for cells in (4, 8, 16, 32, 64, 128):
        lattice = rng.rand(cells, cells)
        x = np.arange(size) * cells / size
        i = np.floor(x).astype(int)
        f = x - i
        f = f * f * (3 - 2 * f)
        a = lattice[np.ix_(i % cells, i % cells)]
        b = lattice[np.ix_(i % cells, (i + 1) % cells)]
        c = lattice[np.ix_((i + 1) % cells, i % cells)]
        d = lattice[np.ix_((i + 1) % cells, (i + 1) % cells)]
        fy, fx = f[:, None], f[None, :]
        total += amp * (a * (1 - fx) * (1 - fy) + b * fx * (1 - fy) + c * (1 - fx) * fy + d * fx * fy)
        norm += amp
        amp *= 0.55
    total /= norm
    total = (total - total.min()) / (total.max() - total.min())
    _write(NOISE, np.repeat(total[..., None], 3, axis=2))


def textures():
    """Makes whichever derived maps are missing; returns {file name: path} of every map the finishes use."""
    makers = [("gunmetal", make_gunmetal), ("paint", make_paint), ("tread", make_tread)]
    for key, make in makers:
        if not all((OUT / SURFACES[key][k]).exists() for k in ("albedo", "orm", "normal")):
            make()
    if not (OUT / RUST).exists():
        make_rust()
    if not (OUT / NOISE).exists():
        make_noise()
    names = {RUST, NOISE}
    for s in SURFACES.values():
        names.update(s[k] for k in ("albedo", "orm", "normal"))
    names.update(n for _, n, *_ in FINISH.values() if n)
    for name in sorted(names):
        if not (OUT / name).exists():
            raise RuntimeError(f"iron_wake: {name} is missing from {OUT}")
        write_texture_import(OUT / name, normal_map="Normal" in name)
    return {n: OUT / n for n in names}


# ------------------------------------------------------------------------------------------------ the materials


def uv_metres(objects):
    """{material name: metres of surface per UV unit} over the asset's own faces: the median over faces of an edge's
    length in the world over its length in UV0 (the asset bakes each finish's repeat into UV0)."""
    import bpy  # noqa: F401
    samples = {}
    for o in objects:
        me = o.data
        uv = me.uv_layers.get("UV0_Material") or (me.uv_layers[0] if me.uv_layers else None)
        if uv is None:
            continue
        mw = o.matrix_world
        names = [m.name if m else "" for m in me.materials]
        for p in me.polygons[: min(len(me.polygons), 4000)]:
            li = list(p.loop_indices)
            a, b = li[0], li[1]
            va, vb = me.loops[a].vertex_index, me.loops[b].vertex_index
            w = (mw @ me.vertices[va].co - mw @ me.vertices[vb].co).length
            u = (uv.data[a].uv - uv.data[b].uv).length
            if w > 0.02 and u > 1e-5:
                samples.setdefault(names[p.material_index], []).append(w / u)
    return {m: float(np.median(v)) for m, v in sorted(samples.items()) if len(v) >= 20}


def write_shader_material(name, resource_name, params, textures):
    """game/assets/materials/<name>.tres: a ShaderMaterial on the Iron Wake's surface shader."""
    order = ["albedo_tex", "orm_tex", "normal_tex", "steel_tex", "rust_tex", "noise_tex"]
    files = []
    for key in order:
        if textures.get(key) and textures[key] not in files:
            files.append(textures[key])
    ids = {f: str(i + 2) for i, f in enumerate(files)}
    out = [f'[gd_resource type="ShaderMaterial" load_steps={len(files) + 2} format=3]', "",
           f'[ext_resource type="Shader" path="{res_path(SHADER)}" id="1"]']
    for f in files:
        out.append(f'[ext_resource type="Texture2D" path="{res_path(f)}" id="{ids[f]}"]')
    out += ["", "[resource]", f'resource_name = "{resource_name}"', 'shader = ExtResource("1")']
    for key in order:
        if textures.get(key):
            out.append(f'shader_parameter/{key} = ExtResource("{ids[textures[key]]}")')
    for key, value in params.items():
        if isinstance(value, tuple):
            out.append(f"shader_parameter/{key} = Color(" + ", ".join(f"{c:g}" for c in value) + ")")
        else:
            out.append(f"shader_parameter/{key} = {value:g}")
    path = MATERIALS / f"{name}.tres"
    path.write_text("\n".join(out) + "\n", encoding="utf-8", newline="\n")
    log(f"wrote material {path.name}")
    return path


def materials(revision, maps, metres_per_uv):
    """The sixteen finishes as ShaderMaterials. Returns ({Blender material: .tres}, {material: (mean linear albedo,
    roughness, metallic)} for LOD4's colours)."""
    from hollow.kit import snake
    from hollow.textures import read as read_png
    tex, info = {}, {}
    cache = {}

    def mean_of(name, alpha_tint=None):
        key = (name, alpha_tint)
        if key not in cache:
            px = downscale(read_png(maps[name]), 256)
            rgb = srgb_to_linear(px[..., :3])
            if alpha_tint is not None:
                a = px[..., 3:4]
                rgb = rgb * (1 - a) + rgb * np.array(alpha_tint) * a
            cache[key] = rgb.reshape(-1, 3).mean(axis=0)
        return cache[key]
    for m in revision["materials"]:
        finish = m["name"]
        surface, normal, painted, chip, rust, grime = FINISH[finish]
        s = SURFACES[surface]
        name = f"M_IronWake_{finish}"
        metres = metres_per_uv.get(name, m.get("repeat_metres", 2.0))
        tint = tuple(float(c) for c in m["tint"])
        params = {
            "tint": tuple(float(_srgb(c)) for c in tint) + (1.0,),
            "uv_scale": round(metres / s["metres"], 4),
            "normal_strength": float(m["normal_strength"]),
            "painted": float(painted), "chip": chip, "rust": rust, "grime": grime,
        }
        textures = {"albedo_tex": maps[s["albedo"]], "orm_tex": maps[s["orm"]], "normal_tex": maps[normal or s["normal"]],
                    "steel_tex": maps[SURFACES["gunmetal"]["albedo"]], "rust_tex": maps[RUST], "noise_tex": maps[NOISE]}
        tex[name] = write_shader_material(f"iron_wake_{snake(finish)}", name, params, textures)
        a = mean_of(s["albedo"], tint if painted else None)
        if not painted:
            a = a * np.array(tint)
        o = mean_of(s["orm"])
        info[name] = (a, float(o[1]), float(o[2]))
    return tex, info


def _srgb(c):
    c = max(0.0, float(c))
    return 12.92 * c if c <= 0.0031308 else 1.055 * c ** (1 / 2.4) - 0.055


# ------------------------------------------------------------------------------------------------ curvature masks


def curvature(o):
    """Per vertex of a mesh object: how convex (0..1) and how concave (0..1) the surface is round it, from where its
    neighbours lie against its normal (the vertex normal, so a sharp corner counts), smoothed twice over the edges."""
    me = o.data
    n = len(me.vertices)
    if n == 0:
        return np.zeros(0, np.float32), np.zeros(0, np.float32)
    co = np.empty(n * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    nr = np.empty(n * 3)
    me.vertex_normals.foreach_get("vector", nr)
    nr = nr.reshape(-1, 3)
    ev = np.empty(len(me.edges) * 2, np.int64)
    me.edges.foreach_get("vertices", ev)
    ev = ev.reshape(-1, 2)
    d = co[ev[:, 1]] - co[ev[:, 0]]
    length = np.maximum(np.linalg.norm(d, axis=1), 1e-9)
    d /= length[:, None]
    acc = np.zeros(n)
    cnt = np.zeros(n)
    np.add.at(acc, ev[:, 0], -np.einsum("ij,ij->i", nr[ev[:, 0]], d))
    np.add.at(acc, ev[:, 1], np.einsum("ij,ij->i", nr[ev[:, 1]], d))
    np.add.at(cnt, ev[:, 0], 1)
    np.add.at(cnt, ev[:, 1], 1)
    c = acc / np.maximum(cnt, 1)
    for _ in range(2):
        s = np.zeros(n)
        np.add.at(s, ev[:, 0], c[ev[:, 1]])
        np.add.at(s, ev[:, 1], c[ev[:, 0]])
        c = 0.5 * c + 0.5 * s / np.maximum(cnt, 1)
    convex = smoothstep(0.02, 0.32, c).astype(np.float32)
    concave = smoothstep(0.02, 0.28, -c).astype(np.float32)
    return convex, concave


def carry(source, values, targets):
    """Per-vertex values of other levels of detail: each target vertex takes the value at the nearest point of the
    `source` Triangles, interpolated across that triangle. `values`: {source object: (n, k) array}."""
    from hollow.large import mesh_arrays
    k = next(iter(values.values())).shape[1]
    allv = np.zeros((len(source.verts), k), np.float32)
    for o, (base, n) in source.ranges.items():
        if o in values:
            allv[base:base + n] = values[o]
    nearest = source.tree.find_nearest
    out = {}
    for o in targets:
        co, _, _, _ = mesh_arrays(o)
        res = np.zeros((len(co), k), np.float32)
        for i, p in enumerate(co.tolist()):
            loc, _, idx, _ = nearest(p)
            if loc is None:
                continue
            a, b, c = source.tris[idx]
            pa, pb, pc = source.verts[a], source.verts[b], source.verts[c]
            v0, v1, v2 = pb - pa, pc - pa, np.array(loc) - pa
            d00, d01, d11 = v0 @ v0, v0 @ v1, v1 @ v1
            d20, d21 = v2 @ v0, v2 @ v1
            den = d00 * d11 - d01 * d01
            if abs(den) < 1e-18:
                res[i] = (allv[a] + allv[b] + allv[c]) / 3
                continue
            w1 = (d11 * d20 - d01 * d21) / den
            w2 = (d00 * d21 - d01 * d20) / den
            res[i] = (1 - w1 - w2) * allv[a] + w1 * allv[b] + w2 * allv[c]
        out[o] = np.clip(res, 0, 1)
    return out

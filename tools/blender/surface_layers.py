# The ground's texture layers (GROUND.md §9, docs/WALKING_GROUND.md §8), built from the owned scans so the build is
# reproducible. The first sixteen textures are the sixteen ground materials' own scans, in the order of
# Hollow.Surface.GroundMaterial; the textures after them are the alternatives a material switches to by place (a
# scan of pebbly sand in patches of a beach, desert sand where it is hot and dry, icy grass along the snow line,
# an ice wall where ice stands steep), chosen in the shader by each slot's selector (surface_ground_look.gdshaderinc).
# Every file is one texture of the arrays:
#   <nn>_<key>_albedo.jpg|.webp   the scan's base colour at 4K: the source JPEG copied byte for byte, or lossless WebP
#                                 where the source is a PNG or EXR, or not 4K
#   <nn>_<key>_normal.jpg|.webp   its normal map, the same way (each texture's own convention, measured: layers.json)
#   <nn>_<key>_rh.webp            2K, lossless: r = roughness, g = height (displacement, or bump where a set has none,
#                                 stretched to 0-1)
#   layers.json                   "textures": every texture: key, metres a repeat, normal axes against the first
#                                 texture's, mean linear colour, the files; "layers": the sixteen materials, each its
#                                 texture, its colour gain, and its alternatives (texture, selector, gain)
#   *.import                      Godot's import settings: VRAM-compressed (BPTC), mipmaps; the game assembles the
#                                 textures into texture arrays when it loads (game/scripts/Surface/GroundLayers.cs)
# Nothing lossy is written: Godot's VRAM compression is the only step after the scans' own files. A file that is
# already in the output folder is kept (the scans' JPEGs never change; lossless WebP is deterministic), so a run only
# adds what is missing and rewrites layers.json.
# Run with Blender 5.2:
#   blender -b --factory-startup --python tools/blender/surface_layers.py -- <assets folder> <output folder> <scratch folder>
# where the assets folder is D:\GameDev\Assets (its Megascans sets unpacked; the Poly Haven sets are read from their
# scene packs' zips into the scratch folder).
import json
import os
import shutil
import sys
import zipfile

import bpy
import numpy as np

SIZE = 4096
RH_SIZE = 2048


def ms(folder, prefix, height="Displacement"):
    """A Megascans set: base colour, normal, roughness and height maps (JPEG, 4K) in its unpacked folder."""
    return {"albedo": "{0}/{1}_BaseColor.jpg".format(folder, prefix), "normal": "{0}/{1}_Normal.jpg".format(folder, prefix),
            "rough": "{0}/{1}_Roughness.jpg".format(folder, prefix), "height": "{0}/{1}_{2}.jpg".format(folder, prefix, height)}


def ph(archive, name, suffix="", ext=None):
    """A Poly Haven set inside a scene pack's zip (CC0): diffuse, OpenGL normal, roughness and displacement, PNG unless
    the set's own file kinds are given (the Namaqualand pack keeps JPEG colour, EXR normals and roughness)."""
    ext = ext or {}
    return {kind: "zip:{0}:textures/{1}_{2}{3}.{4}".format(archive, name, tag, suffix, ext.get(kind, "png"))
            for kind, tag in (("albedo", "diff"), ("normal", "nor_gl"), ("rough", "rough"), ("height", "disp"))}


BWM = "Bare-World-Manual/"
NAMAQUALAND = {"albedo": "jpg", "normal": "exr", "rough": "exr", "height": "png"}
# (key, metres a repeat, sources). The first sixteen are GroundMaterial's, in its order; the rest the alternatives.
# Append only: the arrays' indices are the shaders' and the catalogue's.
TEXTURES = [
    ("grass", 4.0, ms(BWM + "dry_grassy_ground_vbmkebz_4k", "Dry_Grassy_Ground_vbmkebz_4K")),
    ("forest_floor", 3.0, ms(BWM + "forest_floor_vmcjadofw_4k", "Forest_Floor_vmcjadofw_4K")),
    ("soil", 2.0, ph("verdant_trail.zip", "dirt_floor", "_4k")),
    ("rock", 2.4, ms(BWM + "granite_rock_wgpqfg3_4k", "Granite_Rock_wgpqfg3_4K")),
    ("gravel", 4.0, ms(BWM + "gravel_ground_vixscfddw_4k", "Gravel_Ground_vixscfddw_4K", "Bump")),
    ("sand", 4.0, ms(BWM + "beach_sand_ukviejtlw_4k", "Beach_Sand_ukviejtlw_4K", "Bump")),
    ("snow", 2.0, ms("clean_snow_ueqmfe3fw_4k", "Clean_Snow_ueqmfe3fw_4K")),
    ("moss", 4.0, ph("pine_forest.zip", "forest_ground_04")),
    ("wet_sand", 1.5, ms("wet_plain_sand_rldas3p0_4k", "Wet_Plain_Sand_rldas3p0_4K")),
    ("shingle", 2.0, ms("beach_gravel_with_pebbles_tbvqegas_4k", "Beach_Gravel_With_Pebbles_tbvqegas_4K")),
    ("wet_grass", 3.0, ms("wet_grass_ground_sjzkdjba_4k", "Wet_Grass_Ground_sjzkdjba_4K")),
    ("mossy_floor", 4.0, ms("mossy_ground_tj4ocdjr_4k", "Mossy_Ground_tj4ocdjr_4K")),
    ("tundra", 4.0, ms("tundra_mossy_ground_vjulbjjr_4k", "Tundra_Mossy_Ground_vjulbjjr_4K", "Bump")),
    ("sandstone", 2.5, ms("canyon_sandstone_rock_vimldgeg_4k", "Canyon_Sandstone_Rock_vimldgeg_4K")),
    ("dry_earth", 2.5, ms("desert_western_ground_dirt_coarse_07_xiqebhm_4k", "Desert_Western_Ground_Dirt_Coarse_07_xiqebhm_4K")),
    ("ice", 3.0, ms("frozen_lake_tcbkdcfr_4k", "Frozen_Lake_tcbkdcfr_4K")),
    # The alternatives (16 on). A rock's and sandstone's alternatives repeat at their slot's size: the triplanar
    # projection on cliffs uses one set of body-space cycles a slot.
    ("forest_floor_2", 3.0, ms("forest_floor_wgyhdgy_4k", "Forest_Floor_wgyhdgy_4K")),
    ("coastal_rock", 2.4, ms("cracked_nordic_coastal_rock_ukohaj0mw_4k", "Cracked_Nordic_Coastal_Rock_ukohaj0mw_4K", "Bump")),
    ("desert_gravel", 3.0, ms("desert_western_ground_gravel_coarse_02_xglmbct_4k", "Desert_Western_Ground_Gravel_Coarse_02_xglmbct_4K")),
    ("tundra_gravel", 3.0, ms("tundra_gravel_ground_vjepchymw_4k", "Tundra_Gravel_Ground_vjepchymw_4K", "Bump")),
    ("pebbly_sand", 2.5, ms("beach_sand_with_pebbles_vl0fdilo_4k", "Beach_Sand_With_Pebbles_vl0fdilo_4K")),
    ("desert_sand", 4.0, ms("desert_sand_wkjgche_4k", "Desert_Sand_wkjgche_4K")),
    ("icy_grass", 3.0, ms("icy_snow_on_dry_grass_sjziebea_4k", "Icy_Snow_On_Dry_Grass_sjziebea_4K")),
    ("melting_snow", 2.5, ms("melting_snow_uexjbdudy_4k", "Melting_Snow_uexjbdudy_4K")),
    ("snow_cliff", 2.0, ms("snow_cliff_uhqncfpdy_4k", "Snow_Cliff_uhqncfpdy_4K")),
    ("damp_sand", 3.0, ph("namaqualand.zip", "damp_sand", "_4k", NAMAQUALAND)),
    ("gravelly_sand", 2.5, ph("namaqualand.zip", "gravelly_sand", "_4k", NAMAQUALAND)),
    ("sleet_gravel", 2.5, ms("sleet_covered_gravel_tb5oaf2s_4k", "Sleet_Covered_Gravel_tb5oaf2s_4K")),
    ("tundra_rocky", 3.0, ms("tundra_rocky_ground_vi2mahnmw_4k", "Tundra_Rocky_Ground_vi2mahnmw_4K", "Bump")),
    ("lichened_rock", 2.5, ms("tundra_lichened_rock_vjbode1mw_4k", "Tundra_Lichened_Rock_vjbode1mw_4K")),
    ("snowy_stones", 2.5, ms("windswept_snowy_stones_tdloaf1r_4k", "Windswept_Snowy_Stones_tdloaf1r_4K")),
    ("outback_rocky", 3.0, ms("desert_outback_ground_rocky_06_xiqgafr_4k", "Desert_Outback_Ground_Rocky_06_xiqgafr_4K")),
    ("desert_sand_fine", 3.0, ms("desert_western_ground_sand_fine_01_xgmpdek_4k", "Desert_Western_Ground_Sand_Fine_01_xgmpdek_4K")),
    ("ice_wall", 3.0, ms("ice_wall_uebjabcr_4k", "Ice_Wall_uebjabcr_4K")),
]

# The sixteen materials: (texture, colour gain, alternatives: (texture, selector, colour gain)). The selectors are the
# shader's (surface_ground_look.gdshaderinc): noise (patches of about fifty metres), dryness and lush (the grass's
# dryness), lush-patch (lush ground in patches), heat (hot and dry), cold (freezing), steep (slopes), snowline (snow
# shared with grass), thaw (around freezing). Gains lift a scan taken wet or in shade to the place it stands for.
ONE = [1.0, 1.0, 1.0]
LAYERS = [
    ("grass", "grass", ONE, [("wet_grass", "lush", [2.0, 2.7, 1.6]), ("mossy_floor", "lush-patch", [1.15, 1.15, 1.05])]),
    ("forest_floor", "forest_floor", ONE, [("forest_floor_2", "noise", ONE)]),
    ("soil", "soil", ONE, []),
    ("rock", "rock", ONE, [("coastal_rock", "noise", ONE)]),
    ("gravel", "gravel", ONE, [("desert_gravel", "heat", ONE), ("tundra_gravel", "cold", ONE)]),
    ("sand", "sand", ONE, [("pebbly_sand", "noise", ONE), ("desert_sand", "heat", ONE)]),
    ("snow", "snow", ONE, [("icy_grass", "snowline", ONE), ("melting_snow", "thaw", ONE), ("snow_cliff", "steep", ONE)]),
    ("moss", "moss", ONE, []),
    ("wet_sand", "wet_sand", ONE, [("damp_sand", "noise", ONE)]),
    ("shingle", "shingle", ONE, [("gravelly_sand", "noise", ONE), ("sleet_gravel", "cold", ONE)]),
    ("wet_grass", "wet_grass", ONE, []),
    ("mossy_floor", "mossy_floor", ONE, []),
    ("tundra", "tundra", ONE, [("tundra_rocky", "noise", ONE), ("lichened_rock", "steep", ONE), ("snowy_stones", "cold", ONE)]),
    ("sandstone", "sandstone", ONE, []),
    ("dry_earth", "dry_earth", ONE, [("outback_rocky", "noise", ONE), ("desert_sand_fine", "noise", ONE)]),
    ("ice", "ice", ONE, [("ice_wall", "steep", ONE)]),
]

IMPORT = """[remap]

importer="texture"
type="CompressedTexture2D"

[params]

compress/mode=2
compress/high_quality=true
compress/lossy_quality=0.7
compress/uastc_level=0
compress/rdo_quality_loss=0.0
compress/hdr_compression=1
compress/normal_map=2
compress/channel_pack=0
mipmaps/generate=true
mipmaps/limit=-1
roughness/mode=0
roughness/src_normal=""
process/channel_remap/red=0
process/channel_remap/green=1
process/channel_remap/blue=2
process/channel_remap/alpha=3
process/fix_alpha_border=false
process/premult_alpha=false
process/normal_map_invert_y=false
process/hdr_as_srgb=false
process/hdr_clamp_exposure=false
process/size_limit=0
detect_3d/compress_to=0
"""


def resolve(spec, root, scratch):
    if not spec.startswith("zip:"):
        return os.path.join(root, spec)
    _, archive, member = spec.split(":", 2)
    target = os.path.join(scratch, os.path.basename(member))
    if not os.path.exists(target):
        with zipfile.ZipFile(os.path.join(root, archive)) as z, z.open(member) as src, open(target, "wb") as dst:
            shutil.copyfileobj(src, dst)
    return target


def load(path, size=None):
    """A map's pixels (rows, columns, RGBA, 0-1), made square and brought to size when one is given: a scan twice as
    wide as high (some Megascans surfaces) repeats along its short side, which keeps its metres, and a map larger than
    the size is averaged down by whole texels. Returns the pixels and the source's width (0 when it wasn't square)."""
    image = bpy.data.images.load(path)
    image.colorspace_settings.name = "Non-Color"  # raw values: no colour conversion on the way through
    w, h = image.size[0], image.size[1]
    width = w
    pixels = np.empty(w * h * 4, dtype=np.float32)
    image.pixels.foreach_get(pixels)
    bpy.data.images.remove(image)
    data = np.clip(pixels.reshape(h, w, 4), 0.0, 1.0)
    square = h == w
    if h != w:
        if h < w and w % h == 0:
            data = np.concatenate([data] * (w // h), axis=0)
        elif w < h and h % w == 0:
            data = np.concatenate([data] * (h // w), axis=1)
        else:
            raise ValueError("%s is %d x %d: not square, and not a whole number of squares" % (path, w, h))
    if size:
        side = data.shape[0]
        if side % size == 0 and side > size:
            f = side // size
            data = data.reshape(size, f, size, f, 4).mean(axis=(1, 3))
        elif side != size:
            raise ValueError("%s is %d square: not a whole multiple of %d" % (path, side, size))
    return data, (width if square else 0)


def save_webp(path, data):
    """Lossless WebP (Blender writes lossless at quality 100), RGB."""
    height, width = data.shape[0], data.shape[1]
    image = bpy.data.images.new("layer", width=width, height=height, alpha=True, float_buffer=False)
    image.colorspace_settings.name = "Non-Color"
    image.pixels.foreach_set(data.astype(np.float32).ravel())
    settings = bpy.context.scene.render.image_settings
    settings.file_format = "WEBP"
    settings.color_mode = "RGB"
    settings.quality = 100
    image.save_render(path, scene=bpy.context.scene)
    bpy.data.images.remove(image)


def stretch(channel):
    lo, hi = np.percentile(channel, 0.5), np.percentile(channel, 99.5)
    return np.clip((channel - lo) / max(hi - lo, 1e-6), 0.0, 1.0)


def convention(nrm, height):
    """Signs of the normal's x and y against the height's slopes (rows run bottom to top in Blender)."""
    gy, gx = np.gradient(height)
    return (float(np.sign(np.mean((nrm[:, :, 0] - 0.5) * -gx))), float(np.sign(np.mean((nrm[:, :, 1] - 0.5) * -gy))))


def write_import(out, name):
    """Godot's import settings for a new file; an existing one is Godot's own by now (it adds the file's uid and the
    imported path), and is kept."""
    path = os.path.join(out, name + ".import")
    if os.path.exists(path):
        return
    with open(path, "w", newline="\n") as f:
        f.write(IMPORT)


def colour_map(kind, source, out, stem):
    """The 4K map: the source JPEG itself when it already is a square one at 4K, else lossless WebP at 4K. Kept when present."""
    data, width = load(source, SIZE)
    if source.lower().endswith(".jpg") and width == SIZE:
        name = stem + "_" + kind + ".jpg"
        if not os.path.exists(os.path.join(out, name)):
            shutil.copyfile(source, os.path.join(out, name))
    else:
        name = stem + "_" + kind + ".webp"
        if not os.path.exists(os.path.join(out, name)):
            save_webp(os.path.join(out, name), data)
    write_import(out, name)
    return name, data


def linear(c):
    """sRGB-encoded to linear."""
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def main():
    args = sys.argv[sys.argv.index("--") + 1:]
    root, out, scratch = args[0], args[1], args[2]
    os.makedirs(out, exist_ok=True)
    os.makedirs(scratch, exist_ok=True)
    textures = []
    reference = None
    for index, (key, metres, files) in enumerate(TEXTURES):
        stem = "%02d_%s" % (index, key)
        albedo_name, albedo = colour_map("albedo", resolve(files["albedo"], root, scratch), out, stem)
        normal_name, normal = colour_map("normal", resolve(files["normal"], root, scratch), out, stem)
        rough, _ = load(resolve(files["rough"], root, scratch), RH_SIZE)
        height, _ = load(resolve(files["height"], root, scratch), RH_SIZE)
        h = stretch(height[:, :, 0])
        rh_name = stem + "_rh.webp"
        if not os.path.exists(os.path.join(out, rh_name)):
            rh = np.zeros((RH_SIZE, RH_SIZE, 4), dtype=np.float32)
            rh[:, :, 0] = rough[:, :, 0]
            rh[:, :, 1] = h
            rh[:, :, 3] = 1
            save_webp(os.path.join(out, rh_name), rh)
        write_import(out, rh_name)
        # The normal's convention, from its own height at 1K (a quarter of the 4K normal's resolution).
        small = normal[::4, ::4]
        signs = convention(small, stretch(height[::2, ::2, 0]))
        if reference is None:
            reference = signs
        flip = [1.0 if signs[0] == reference[0] else -1.0, 1.0 if signs[1] == reference[1] else -1.0]
        mean = albedo[::8, ::8, 0:3].reshape(-1, 3).mean(axis=0)
        mean_linear = linear(albedo[::8, ::8, 0:3]).reshape(-1, 3).mean(axis=0)
        textures.append({"index": index, "key": key, "metres": metres, "flip": flip, "albedo": albedo_name, "normal": normal_name,
                         "roughHeight": rh_name, "meanColour": [round(float(c), 4) for c in mean],
                         "meanLinear": [round(float(c), 4) for c in mean_linear],
                         "roughness": round(float(rough[:, :, 0].mean()), 3)})
        print("TEXTURE", index, key, albedo_name, normal_name, rh_name, "flip", flip, "mean %.3f %.3f %.3f" % tuple(mean))
    by_key = {t["key"]: t["index"] for t in textures}
    layers = []
    for index, (key, texture, gain, variants) in enumerate(LAYERS):
        layers.append({"index": index, "key": key, "texture": by_key[texture], "gain": gain,
                       "variants": [{"texture": by_key[t], "select": s, "gain": g} for t, s, g in variants]})
    with open(os.path.join(out, "layers.json"), "w", newline="\n") as f:
        json.dump({"size": SIZE, "roughHeightSize": RH_SIZE, "textures": textures, "layers": layers}, f, indent=2)
        f.write("\n")
    print("WROTE", os.path.join(out, "layers.json"), len(textures), "textures")


main()

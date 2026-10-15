# The ground's sixteen texture layers (GROUND.md §9, docs/WALKING_GROUND.md §8), in the order of
# Hollow.Surface.GroundMaterial, built from the owned scans so the build is reproducible:
#   <nn>_<key>_albedo.jpg|.webp   the scan's base colour at 4K: the source JPEG copied byte for byte, or lossless WebP
#                                 where the source is a PNG or not 4K
#   <nn>_<key>_normal.jpg|.webp   its normal map, the same way (each layer's own convention, measured: layers.json)
#   <nn>_<key>_rh.webp            2K, lossless: r = roughness, g = height (displacement, or bump where a set has none,
#                                 stretched to 0-1)
#   layers.json                   per layer: key, metres a repeat, normal axes against the first layer's, the files
#   *.import                      Godot's import settings: VRAM-compressed (BPTC), mipmaps; the game assembles the
#                                 layers into texture arrays when it loads (game/scripts/Surface/GroundLayers.cs)
# Nothing lossy is written: Godot's VRAM compression is the only step after the scans' own files.
# Run with Blender 5.2:
#   blender -b --factory-startup --python tools/blender/surface_layers.py -- <assets folder> <output folder> <scratch folder>
# where the assets folder is D:\GameDev\Assets (its Megascans sets unpacked; the two Poly Haven sets are read from their
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


def ph(archive, name, suffix=""):
    """A Poly Haven set inside a scene pack's zip (CC0): diffuse, OpenGL normal, roughness and displacement PNGs."""
    return {kind: "zip:{0}:textures/{1}_{2}{3}.png".format(archive, name, tag, suffix)
            for kind, tag in (("albedo", "diff"), ("normal", "nor_gl"), ("rough", "rough"), ("height", "disp"))}


BWM = "Bare-World-Manual/"
# (key, metres a repeat, sources). The order is GroundMaterial's; append only.
LAYERS = [
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
    image = bpy.data.images.load(path)
    image.colorspace_settings.name = "Non-Color"  # raw values: no colour conversion on the way through
    width = image.size[0]
    if size and (image.size[0] != size or image.size[1] != size):
        image.scale(size, size)
    w, h = image.size[0], image.size[1]
    pixels = np.empty(w * h * 4, dtype=np.float32)
    image.pixels.foreach_get(pixels)
    bpy.data.images.remove(image)
    return pixels.reshape(h, w, 4), width


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


def colour_map(kind, source, out, stem):
    """The 4K map: the source JPEG itself when it already is one at 4K, else lossless WebP at 4K."""
    data, width = load(source)
    if source.lower().endswith(".jpg") and width == SIZE:
        name = stem + "_" + kind + ".jpg"
        shutil.copyfile(source, os.path.join(out, name))
    else:
        if width != SIZE:
            data, _ = load(source, SIZE)
        name = stem + "_" + kind + ".webp"
        save_webp(os.path.join(out, name), data)
    with open(os.path.join(out, name + ".import"), "w", newline="\n") as f:
        f.write(IMPORT)
    return name, data


def main():
    args = sys.argv[sys.argv.index("--") + 1:]
    root, out, scratch = args[0], args[1], args[2]
    os.makedirs(out, exist_ok=True)
    os.makedirs(scratch, exist_ok=True)
    table = []
    reference = None
    for index, (key, metres, files) in enumerate(LAYERS):
        stem = "%02d_%s" % (index, key)
        albedo_name, albedo = colour_map("albedo", resolve(files["albedo"], root, scratch), out, stem)
        normal_name, normal = colour_map("normal", resolve(files["normal"], root, scratch), out, stem)
        rough, _ = load(resolve(files["rough"], root, scratch), RH_SIZE)
        height, _ = load(resolve(files["height"], root, scratch), RH_SIZE)
        h = stretch(height[:, :, 0])
        rh = np.zeros((RH_SIZE, RH_SIZE, 4), dtype=np.float32)
        rh[:, :, 0] = rough[:, :, 0]
        rh[:, :, 1] = h
        rh[:, :, 3] = 1
        rh_name = stem + "_rh.webp"
        save_webp(os.path.join(out, rh_name), rh)
        with open(os.path.join(out, rh_name + ".import"), "w", newline="\n") as f:
            f.write(IMPORT)
        # The normal's convention, from its own height at 1K (a quarter of the 4K normal's resolution).
        small = normal[::4, ::4]
        signs = convention(small, stretch(height[::2, ::2, 0]))
        if reference is None:
            reference = signs
        flip = [1.0 if signs[0] == reference[0] else -1.0, 1.0 if signs[1] == reference[1] else -1.0]
        mean = albedo[::8, ::8, 0:3].reshape(-1, 3).mean(axis=0)
        table.append({"index": index, "key": key, "metres": metres, "flip": flip, "albedo": albedo_name, "normal": normal_name,
                      "roughHeight": rh_name, "meanColour": [round(float(c), 4) for c in mean],
                      "roughness": round(float(rough[:, :, 0].mean()), 3)})
        print("LAYER", index, key, albedo_name, normal_name, rh_name, "flip", flip, "mean %.3f %.3f %.3f" % tuple(mean))
    with open(os.path.join(out, "layers.json"), "w", newline="\n") as f:
        json.dump({"size": SIZE, "roughHeightSize": RH_SIZE, "layers": table}, f, indent=2)
        f.write("\n")
    print("WROTE", os.path.join(out, "layers.json"))


main()

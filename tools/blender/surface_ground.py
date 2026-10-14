# Prepares the ground textures of the surface view (GROUND.md §9): eight material layers in the order of
# Hollow.Surface.GroundMaterial, downscaled to 2048 px and packed into two atlases of 2 x 4 slices that Godot imports
# as texture arrays:
#   ground_albedo.webp  rgb = base colour
#   ground_normal.webp  rg = normal (as stored by the source), b = height (displacement, or bump where a set has
#                       none); the shader derives occlusion from the height, and roughness is per layer
# Lossy WebP without alpha keeps the repository small (the alpha channel would be stored losslessly).
# Run with Blender 5.2 (it reads the JPEG sources and writes WebP):
#   blender -b --factory-startup --python tools/blender/surface_ground.py -- <assets folder> <poly haven folder> <output folder>
# where the assets folder holds the Megascans sets as unpacked (Bare-World-Manual/..., clean_snow_...) and the Poly Haven
# folder the eight Poly Haven files named below. Normal maps are brought to one convention (the first layer's), measured
# from each set's own height map, so sets authored for OpenGL and DirectX light the same way.
import os
import sys

import bpy
import numpy as np

SIZE = 2048
COLUMNS, ROWS = 2, 4

# (layer, base colour, height map, normal, roughness), relative to the sources folder given on the command line.
# Megascans sets come from Fab (listings in game/assets/THIRD_PARTY.md); the two Poly Haven sets (CC0) are taken from
# Poly Haven's "Pine Forest" and "Verdant Trail" scene packs and unpacked into a folder "polyhaven" first.
MS = "Bare-World-Manual/{0}/{1}_{2}.jpg"


def megascans(folder, prefix, height):
    return (MS.format(folder, prefix, "BaseColor"), MS.format(folder, prefix, height),
            MS.format(folder, prefix, "Normal"), MS.format(folder, prefix, "Roughness"))


def polyhaven(name, suffix=""):
    return tuple("{{polyhaven}}/{0}_{1}{2}.png".format(name, kind, suffix) for kind in ("diff", "disp", "nor_gl", "rough"))


LAYERS = [
    ("grass", megascans("dry_grassy_ground_vbmkebz_4k", "Dry_Grassy_Ground_vbmkebz_4K", "Displacement")),
    ("forest_floor", megascans("forest_floor_vmcjadofw_4k", "Forest_Floor_vmcjadofw_4K", "Displacement")),
    ("soil", polyhaven("dirt_floor", "_4k")),
    ("rock", megascans("granite_rock_wgpqfg3_4k", "Granite_Rock_wgpqfg3_4K", "Displacement")),
    ("gravel", megascans("gravel_ground_vixscfddw_4k", "Gravel_Ground_vixscfddw_4K", "Bump")),
    ("sand", megascans("beach_sand_ukviejtlw_4k", "Beach_Sand_ukviejtlw_4K", "Bump")),
    ("snow", ("clean_snow_ueqmfe3fw_4k/Clean_Snow_ueqmfe3fw_4K_BaseColor.jpg", "clean_snow_ueqmfe3fw_4k/Clean_Snow_ueqmfe3fw_4K_Displacement.jpg",
              "clean_snow_ueqmfe3fw_4k/Clean_Snow_ueqmfe3fw_4K_Normal.jpg", "clean_snow_ueqmfe3fw_4k/Clean_Snow_ueqmfe3fw_4K_Roughness.jpg")),
    ("moss", polyhaven("forest_ground_04")),
]


def load(path):
    image = bpy.data.images.load(path)
    image.colorspace_settings.name = "Non-Color"  # raw values: no colour conversion on the way through
    if image.size[0] != SIZE or image.size[1] != SIZE:
        image.scale(SIZE, SIZE)
    pixels = np.empty(SIZE * SIZE * 4, dtype=np.float32)
    image.pixels.foreach_get(pixels)
    bpy.data.images.remove(image)
    return pixels.reshape(SIZE, SIZE, 4)


def convention(nrm, height):
    """Signs of the normal's x and y against the height's slopes (rows run bottom to top in Blender)."""
    gy, gx = np.gradient(height)
    sx = np.sign(np.mean((nrm[:, :, 0] - 0.5) * -gx))
    sy = np.sign(np.mean((nrm[:, :, 1] - 0.5) * -gy))
    return sx, sy


def normalise(channel):
    lo, hi = np.percentile(channel, 0.5), np.percentile(channel, 99.5)
    return np.clip((channel - lo) / max(hi - lo, 1e-6), 0.0, 1.0)


def save(name, data, folder):
    height, width = data.shape[0], data.shape[1]
    image = bpy.data.images.new(name, width=width, height=height, alpha=True, float_buffer=False)
    image.colorspace_settings.name = "Non-Color"
    image.pixels.foreach_set(data.astype(np.float32).ravel())
    scene = bpy.context.scene
    scene.render.image_settings.file_format = "WEBP"
    scene.render.image_settings.color_mode = "RGB"
    scene.render.image_settings.quality = 82
    path = os.path.join(folder, name + ".webp")
    image.save_render(path, scene=scene)
    print("WROTE", path)


def main():
    args = sys.argv[sys.argv.index("--") + 1:]
    root, haven, out = args[0], args[1], args[2]
    reference = None
    os.makedirs(out, exist_ok=True)
    albedo = np.zeros((ROWS * SIZE, COLUMNS * SIZE, 4), dtype=np.float32)
    normal = np.zeros((ROWS * SIZE, COLUMNS * SIZE, 4), dtype=np.float32)
    for index, (layer, files) in enumerate(LAYERS):
        colour_file, height_file, normal_file, rough_file = (
            f.replace("{polyhaven}", haven) if f.startswith("{polyhaven}") else os.path.join(root, f) for f in files)
        colour = load(colour_file)
        height = normalise(load(height_file)[:, :, 0])
        nrm = load(normal_file)
        rough = load(rough_file)[:, :, 0]
        signs = convention(nrm, height)
        if reference is None:
            reference = signs
        for axis in (0, 1):
            if signs[axis] != reference[axis]:
                nrm[:, :, axis] = 1.0 - nrm[:, :, axis]
                print("LAYER", index, layer, "normal axis", axis, "flipped to match")
        # Slice `index` sits in row index // 2 from the top; Blender's rows run from the bottom.
        row, column = index // COLUMNS, index % COLUMNS
        y0 = (ROWS - 1 - row) * SIZE
        x0 = column * SIZE
        albedo[y0:y0 + SIZE, x0:x0 + SIZE, 0:3] = colour[:, :, 0:3]
        albedo[y0:y0 + SIZE, x0:x0 + SIZE, 3] = 1
        normal[y0:y0 + SIZE, x0:x0 + SIZE, 0] = nrm[:, :, 0]
        normal[y0:y0 + SIZE, x0:x0 + SIZE, 1] = nrm[:, :, 1]
        normal[y0:y0 + SIZE, x0:x0 + SIZE, 2] = height
        normal[y0:y0 + SIZE, x0:x0 + SIZE, 3] = 1
        mean = colour[:, :, 0:3].reshape(-1, 3).mean(axis=0)
        print("LAYER", index, layer, "mean colour %.3f %.3f %.3f" % tuple(mean), "roughness %.2f" % float(rough.mean()))
    save("ground_albedo", albedo, out)
    save("ground_normal", normal, out)


main()

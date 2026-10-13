"""Skin maps for the body (GROUND.md §4.8), made from its own texture and its hands' bones:

- skin_male_normal.png: the texture's fine relief, found from its light and dark (creases in the palms, pores, lines
  round the eyes), plus fine wrinkles over the finger joints and a thin plate for each nail;
- the nails' gloss, kept in the albedo's alpha (1 on skin, lower on the nails; the skin shader scales its roughness
  by it).

build_body.py runs this after painting the stubble. On its own it reads art/.cache/human_male.blend and rewrites
the maps in place (only the albedo's alpha changes there):

    blender -b --factory-startup --python art/characters/skin_maps.py
"""
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "lib"))

import bpy  # noqa: E402
import numpy as np  # noqa: E402

import garment as G  # noqa: E402
import hollow_art as H  # noqa: E402
import surfaces as S  # noqa: E402

FINGERS = ("thumb", "index", "middle", "ring", "pinky")
# Half the width of each nail (metres).
NAIL_HALF_WIDTH = {"thumb": 0.0072, "index": 0.0058, "middle": 0.006, "ring": 0.0056, "pinky": 0.0047}


def main():
    body = H.args().get("body", "male")
    bpy.ops.wm.open_mainfile(filepath=os.path.join(H.CACHE, "human_%s.blend" % body))
    paint(bpy.data.objects["body_source"], os.path.join(H.CHARACTERS, "textures"), "skin_" + body)


def paint(body, folder, stem="skin_male"):
    path = os.path.join(folder, stem + "_albedo.png")
    px, w, h = H.load_image_pixels(path)
    img = np.flipud(px).copy()
    mask, pos, nor, _ = G.rasterise(body, w)
    rig = body.parent
    # Relief from the texture: darker than its surroundings is lower (creases, pores), lighter is higher. Faded out
    # at the edges of the texture's islands, so the seams carry none.
    lum = img[..., :3] @ np.array((0.3, 0.59, 0.11), np.float32)
    lum = G.bleed(lum, mask, steps=24)
    edge = np.clip(G.edge_distance(mask, 6) / 6.0, 0, 1)
    height = np.clip(lum - blur(lum, 5), -0.08, 0.08) * edge
    # A nail is a thin plate: a low step at its edges and a groove along the cuticle.
    nail, cuticle = nails(rig, pos, nor)
    nail = blur(nail * mask, 1, passes=2)
    height += nail * 0.022 - cuticle * 0.03 + knuckles(rig, pos, nor)
    height *= mask
    normal = S.height_to_normal(height, 5.0)
    H.save_image(S.to_blender_pixels(G.bleed(normal, mask, 8)), w, h, os.path.join(folder, stem + "_normal.png"))
    img[..., 3] = 1.0 - 0.55 * np.clip(nail, 0, 1)
    H.save_image(S.to_blender_pixels(img), w, h, path)
    H.log("skin maps painted (%d nail texels)" % int((nail > 0.5).sum()))


def blur(img, radius, passes=3):
    """Box blur, repeated (three passes come close to a Gaussian); edges clamped."""
    out = img.astype(np.float32)
    n = 2 * radius + 1
    for _ in range(passes):
        for axis in (0, 1):
            pad = [(0, 0)] * out.ndim
            pad[axis] = (radius + 1, radius)
            c = np.cumsum(np.pad(out, pad, mode='edge'), axis=axis)
            size = out.shape[axis]
            out = (np.take(c, np.arange(n, n + size), axis=axis) - np.take(c, np.arange(size), axis=axis)) / n
    return out


def hand_frames(rig):
    """Per hand: its side, the bones, the direction across the knuckles towards the thumb, and the back of the hand."""
    bones = {b.name: (rig.matrix_world @ b.head_local, rig.matrix_world @ b.tail_local) for b in rig.data.bones}
    for side in ("_l", "_r"):
        wrist = bones["hand" + side][0]
        middle, index, pinky = (bones[f + "_01" + side][0] for f in ("middle", "index", "pinky"))
        f = (middle - wrist).normalized()
        t = index - pinky
        t = (t - f * t.dot(f)).normalized()
        z = f.cross(t).normalized()
        yield side, bones, t, (z if side == "_r" else -z)


def finger_coords(head, tail, back, pos, nor):
    """Each texel's place along a finger bone (0 at its head, 1 at its tail), its distance sideways from the bone's
    line (metres), and how much its normal faces `back` (made square to the bone)."""
    axis = tail - head
    length = axis.length
    axis = axis / length
    d = (back - axis * back.dot(axis)).normalized()
    across = axis.cross(d)
    rel = pos - np.array(head[:], np.float32)
    return (rel @ np.array(axis[:], np.float32) / length, rel @ np.array(across[:], np.float32),
            nor @ np.array(d[:], np.float32), length)


def nails(rig, pos, nor):
    """Where the nails lie (1 inside) and the line of each cuticle: on the back of each finger's last bone, from a
    third of the way along to just past its end, as wide as the nail."""
    out = np.zeros(pos.shape[:2], np.float32)
    cuticle = np.zeros(pos.shape[:2], np.float32)
    for side, bones, t, back in hand_frames(rig):
        for name in FINGERS:
            head, tail = bones[name + "_03" + side]
            d = (back * 0.6 + t * 0.8).normalized() if name == "thumb" else back
            along, sideways, facing, _length = finger_coords(head, tail, d, pos, nor)
            width = NAIL_HALF_WIDTH[name]
            inside = (along > 0.34) & (along < 1.2) & (np.abs(sideways) < width) & (facing > 0.4)
            out = np.maximum(out, inside.astype(np.float32))
            line = (np.abs(along - 0.34) < 0.05) & (np.abs(sideways) < width * 1.1) & (facing > 0.4)
            cuticle = np.maximum(cuticle, line.astype(np.float32))
    return out, cuticle


def knuckles(rig, pos, nor):
    """Wrinkles over the knuckles: a few fine grooves across the back of each finger at its middle and end joints."""
    out = np.zeros(pos.shape[:2], np.float32)
    for side, bones, _, back in hand_frames(rig):
        for name in FINGERS[1:]:
            for bone, count, spacing in ((name + "_02" + side, 3, 0.0026), (name + "_03" + side, 2, 0.0022)):
                head, tail = bones[bone]
                along, sideways, facing, length = finger_coords(head, tail, back, pos, nor)
                d = along * length
                near = (np.abs(d) < spacing * count * 0.5 + 0.001) & (np.abs(sideways) < 0.0075) & (facing > 0.35)
                fade = np.clip(1 - np.abs(sideways) / 0.0075, 0, 1) * np.clip((facing - 0.35) / 0.3, 0, 1)
                grooves = np.clip(np.cos(d / spacing * 2 * np.pi), 0, 1) ** 3
                out -= np.where(near, grooves * fade * 0.03, 0.0)
    return out


if __name__ == "__main__":
    main()

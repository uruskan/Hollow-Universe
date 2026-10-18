"""Validate the isolated character audition's GLBs using the standard library.

This checks exported data, not appearance, grip contact, foot sliding, camera
framing, rigging quality, or runtime import. Those require visual review.
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import math
from pathlib import Path
import struct
import sys


ROOT = Path(__file__).resolve().parents[2]
DEFAULT_DIR = ROOT / ".artifacts" / "character-trial-v1"
EXPECTED = ("idle", "walk", "sprint", "crouch_walk", "jump_land",
            "rock_strike", "hatchet_chop", "torch_equip")
LOOPS = frozenset(EXPECTED[:4])
COMPONENTS = {5120: ("b", 1), 5121: ("B", 1), 5122: ("h", 2),
              5123: ("H", 2), 5125: ("I", 4), 5126: ("f", 4)}
SHAPES = {"SCALAR": (1, 1), "VEC2": (1, 2), "VEC3": (1, 3),
          "VEC4": (1, 4), "MAT2": (2, 2), "MAT3": (3, 3), "MAT4": (4, 4)}
BOUNDARY = ("Static exported-data checks only. Passing does not establish visual quality, "
            "anatomy, deformation quality, grip/contact accuracy, foot planting, camera "
            "framing, or correct behavior in Blender/Godot. Review renders and playback separately.")


def finite(values):
    return all(math.isfinite(float(v)) for v in values)


def normalized_name(name):
    for prefix in ("TPS_", "FPS_"):
        if name.upper().startswith(prefix):
            return name[len(prefix):].lower()
    return name.lower()


def quat_distance(a, b):
    # q and -q encode the same orientation.
    return min(max(abs(x - y) for x, y in zip(a, b)),
               max(abs(x + y) for x, y in zip(a, b)))


def material_textures(value):
    """Yield texture-info dictionaries, including extension material textures."""
    if isinstance(value, dict):
        for key, child in value.items():
            if key.endswith("Texture") and isinstance(child, dict) and "index" in child:
                yield child
            else:
                yield from material_textures(child)
    elif isinstance(value, list):
        for child in value:
            yield from material_textures(child)


class InvalidGLB(ValueError):
    pass


class GLB:
    def __init__(self, path):
        self.path = Path(path)
        raw = self.path.read_bytes()
        if len(raw) < 12:
            raise InvalidGLB("GLB header is truncated")
        magic, version, length = struct.unpack_from("<4sII", raw)
        if magic != b"glTF" or version != 2 or length != len(raw):
            raise InvalidGLB("Invalid GLB magic, version, or declared file length")
        chunks = []
        offset = 12
        while offset < len(raw):
            if offset + 8 > len(raw):
                raise InvalidGLB("Truncated chunk header")
            size, kind = struct.unpack_from("<II", raw, offset)
            offset += 8
            if size % 4 or offset + size > len(raw):
                raise InvalidGLB("Invalid GLB chunk length")
            chunks.append((kind, raw[offset:offset + size]))
            offset += size
        if not chunks or chunks[0][0] != 0x4E4F534A:
            raise InvalidGLB("First GLB chunk is not JSON")
        if sum(kind == 0x4E4F534A for kind, _ in chunks) != 1:
            raise InvalidGLB("GLB must contain exactly one JSON chunk")
        binary = [data for kind, data in chunks if kind == 0x004E4942]
        if len(binary) > 1:
            raise InvalidGLB("GLB has more than one BIN chunk")
        self.doc = json.loads(chunks[0][1].decode("utf-8").rstrip(" \t\r\n\0"))
        if self.doc.get("asset", {}).get("version") != "2.0":
            raise InvalidGLB("glTF asset version must be 2.0")
        self.buffers = []
        for i, entry in enumerate(self.doc.get("buffers", [])):
            uri = entry.get("uri")
            if uri is None:
                if i != 0 or not binary:
                    raise InvalidGLB(f"Buffer {i} has no available BIN chunk")
                data = binary[0]
            elif uri.startswith("data:") and ";base64," in uri:
                data = base64.b64decode(uri.split(",", 1)[1], validate=True)
            else:
                raise InvalidGLB(f"Buffer {i} references an external URI; audition GLBs must be self-contained")
            declared = entry.get("byteLength", -1)
            if not isinstance(declared, int) or declared < 0 or len(data) < declared:
                raise InvalidGLB(f"Buffer {i} has an invalid byteLength")
            self.buffers.append(data[:declared])
        self.cache = {}

    def entries(self, key):
        return self.doc.get(key, [])

    def entry(self, key, index):
        values = self.entries(key)
        if not isinstance(index, int) or isinstance(index, bool) or not 0 <= index < len(values):
            raise InvalidGLB(f"Invalid {key} index: {index!r}")
        return values[index]

    def view(self, index):
        view = self.entry("bufferViews", index)
        i = view["buffer"]
        if not isinstance(i, int) or not 0 <= i < len(self.buffers):
            raise InvalidGLB(f"bufferView {index}: invalid buffer")
        start, size = view.get("byteOffset", 0), view["byteLength"]
        if not isinstance(start, int) or not isinstance(size, int) or start < 0 or size < 0:
            raise InvalidGLB(f"bufferView {index}: invalid byte range")
        data = self.buffers[i]
        if start + size > len(data):
            raise InvalidGLB(f"bufferView {index}: byte range exceeds buffer")
        return data[start:start + size], view

    def unpack(self, view_index, offset, count, component, shape, stride=None):
        if component not in COMPONENTS or shape not in SHAPES:
            raise InvalidGLB(f"Unsupported accessor component/type {component}/{shape}")
        fmt, size = COMPONENTS[component]
        columns, rows = SHAPES[shape]
        # Matrix columns with 8/16-bit components require four-byte padding.
        column_size = rows * size
        if columns > 1:
            column_size = (column_size + 3) & ~3
        element_size = columns * column_size
        stride = element_size if stride is None else stride
        if (not isinstance(count, int) or count < 0 or not isinstance(offset, int) or offset < 0
                or not isinstance(stride, int) or stride < element_size or stride % size):
            raise InvalidGLB("Invalid accessor count, offset, or stride")
        data, _ = self.view(view_index)
        end = offset if count == 0 else offset + (count - 1) * stride + element_size
        if end > len(data):
            raise InvalidGLB("Accessor byte range exceeds its bufferView")
        return [tuple(struct.unpack_from("<" + fmt, data,
                     offset + i * stride + c * column_size + r * size)[0]
                      for c in range(columns) for r in range(rows)) for i in range(count)]

    def accessor(self, index):
        if index in self.cache:
            return self.cache[index]
        acc = self.entry("accessors", index)
        component, shape, count = acc["componentType"], acc["type"], acc["count"]
        if component not in COMPONENTS or shape not in SHAPES:
            raise InvalidGLB(f"Accessor {index}: invalid component/type")
        if not isinstance(count, int) or count < 1:
            raise InvalidGLB(f"Accessor {index}: count must be positive")
        if "bufferView" in acc:
            _, view = self.view(acc["bufferView"])
            values = self.unpack(acc["bufferView"], acc.get("byteOffset", 0), count,
                                 component, shape, view.get("byteStride"))
        else:
            columns, rows = SHAPES[shape]
            values = [(0,) * (columns * rows) for _ in range(count)]
        if "sparse" in acc:
            sparse = acc["sparse"]
            amount, ix, val = sparse["count"], sparse["indices"], sparse["values"]
            if not isinstance(amount, int) or not 1 <= amount <= count:
                raise InvalidGLB(f"Accessor {index}: invalid sparse count")
            if ix["componentType"] not in (5121, 5123, 5125):
                raise InvalidGLB(f"Accessor {index}: invalid sparse index type")
            indices = self.unpack(ix["bufferView"], ix.get("byteOffset", 0), amount,
                                  ix["componentType"], "SCALAR")
            replacements = self.unpack(val["bufferView"], val.get("byteOffset", 0), amount, component, shape)
            last = -1
            for (dest,), value in zip(indices, replacements):
                if dest <= last or dest >= count:
                    raise InvalidGLB(f"Accessor {index}: sparse indices are not increasing/in range")
                values[dest] = value
                last = dest
        if acc.get("normalized"):
            if component == 5126:
                raise InvalidGLB(f"Accessor {index}: float accessor cannot be normalized")
            bits = COMPONENTS[component][1] * 8
            signed = component in (5120, 5122)
            divisor = (1 << (bits - int(signed))) - 1
            values = [tuple(max(-1.0, v / divisor) if signed else v / divisor for v in row)
                      for row in values]
        if not all(finite(row) for row in values):
            raise InvalidGLB(f"Accessor {index}: contains NaN or infinity")
        self.cache[index] = values
        return values


class Validator:
    def __init__(self, path, weight_tolerance=.002, loop_tolerance=.0001):
        self.path = Path(path)
        self.weight_tolerance = weight_tolerance
        self.loop_tolerance = loop_tolerance
        self.errors = []
        self.warnings = []
        self.summary = {"path": str(self.path.resolve()), "errors": self.errors, "warnings": self.warnings}

    def check(self, condition, message):
        if not condition:
            self.errors.append(message)
        return condition

    def run(self):
        try:
            self.glb = GLB(self.path)
            g = self.glb
            self.summary["file_bytes"] = self.path.stat().st_size
            self.summary["sha256"] = hashlib.sha256(self.path.read_bytes()).hexdigest()
            for i in range(len(g.entries("accessors"))):
                g.accessor(i)
            self.check(bool(g.entries("meshes")), "No meshes exported")
            self.check(bool(g.entries("skins")), "No skins exported")
            self.nodes_and_skins()
            self.geometry()
            self.textures()
            self.animations()
        except (OSError, ValueError, KeyError, TypeError, IndexError, struct.error) as exc:
            self.errors.append(f"Cannot validate GLB: {type(exc).__name__}: {exc}")
        self.summary["passed"] = not self.errors
        return self.summary

    def nodes_and_skins(self):
        g = self.glb
        nodes = g.entries("nodes")
        for i, node in enumerate(nodes):
            for field, size in (("matrix", 16), ("translation", 3), ("rotation", 4), ("scale", 3)):
                if field in node:
                    value = node[field]
                    self.check(len(value) == size and finite(value), f"Node {i}: invalid {field}")
            if "matrix" in node:
                self.check(not any(k in node for k in ("translation", "rotation", "scale")),
                           f"Node {i}: matrix and TRS coexist")
            if "rotation" in node:
                self.check(abs(sum(v * v for v in node["rotation"]) - 1) <= .002,
                           f"Node {i}: rotation is not a unit quaternion")
            for child in node.get("children", []):
                g.entry("nodes", child)
            if "mesh" in node:
                g.entry("meshes", node["mesh"])
            if "skin" in node:
                g.entry("skins", node["skin"])
                self.check("mesh" in node, f"Node {i}: skin assigned without a mesh")
        skin_summaries = []
        for i, skin in enumerate(g.entries("skins")):
            joints = skin.get("joints", [])
            self.check(bool(joints), f"Skin {i}: no joints")
            self.check(len(set(joints)) == len(joints), f"Skin {i}: duplicate joints")
            for joint in joints:
                g.entry("nodes", joint)
            if "skeleton" in skin:
                g.entry("nodes", skin["skeleton"])
            if "inverseBindMatrices" in skin:
                acc = g.entry("accessors", skin["inverseBindMatrices"])
                rows = g.accessor(skin["inverseBindMatrices"])
                self.check(acc["type"] == "MAT4" and acc["componentType"] == 5126,
                           f"Skin {i}: inverse bind accessor is not float MAT4")
                self.check(len(rows) == len(joints), f"Skin {i}: inverse bind count differs from joint count")
            else:
                self.warnings.append(f"Skin {i}: inverse bind matrices omitted; glTF identity defaults apply")
            skin_summaries.append({"name": skin.get("name", str(i)), "joint_count": len(joints),
                                   "inverse_bind_matrices": "inverseBindMatrices" in skin})
        self.summary["skins"] = skin_summaries

    def geometry(self):
        g = self.glb
        primitive_count = vertices = triangles = uv_vertices = normal_vertices = skinned_vertices = 0
        max_weight_error = 0.0
        mesh_skins = {}
        for node in g.entries("nodes"):
            if "mesh" in node and "skin" in node:
                mesh_skins.setdefault(node["mesh"], set()).add(node["skin"])
        for mi, mesh in enumerate(g.entries("meshes")):
            for pi, primitive in enumerate(mesh.get("primitives", [])):
                tag = f"Mesh {mi} primitive {pi}"
                primitive_count += 1
                attrs = primitive.get("attributes", {})
                if not self.check("POSITION" in attrs, f"{tag}: missing POSITION"):
                    continue
                count = len(g.accessor(attrs["POSITION"]))
                self.check(g.entry("accessors", attrs["POSITION"])["type"] == "VEC3", f"{tag}: POSITION is not VEC3")
                vertices += count
                self.check("NORMAL" in attrs, f"{tag}: missing NORMAL")
                normal_vertices += count if "NORMAL" in attrs else 0
                uv_vertices += count if "TEXCOORD_0" in attrs else 0
                for name, ai in attrs.items():
                    values = g.accessor(ai)
                    self.check(len(values) == count, f"{tag}: {name} count differs from POSITION")
                    if name == "NORMAL":
                        self.check(g.entry("accessors", ai)["type"] == "VEC3", f"{tag}: NORMAL is not VEC3")
                    if name.startswith("TEXCOORD_"):
                        self.check(g.entry("accessors", ai)["type"] == "VEC2", f"{tag}: {name} is not VEC2")
                if "indices" in primitive:
                    acc = g.entry("accessors", primitive["indices"])
                    index_rows = g.accessor(primitive["indices"])
                    self.check(acc["type"] == "SCALAR" and acc["componentType"] in (5121, 5123, 5125),
                               f"{tag}: invalid index accessor")
                    self.check(all(0 <= row[0] < count for row in index_rows), f"{tag}: index outside vertex range")
                    index_count = len(index_rows)
                else:
                    index_count = count
                if primitive.get("mode", 4) == 4:
                    self.check(index_count % 3 == 0, f"{tag}: triangle index count is not divisible by three")
                    triangles += index_count // 3
                if "material" in primitive:
                    material = g.entry("materials", primitive["material"])
                    for texture in material_textures(material):
                        g.entry("textures", texture["index"])
                        uv = texture.get("extensions", {}).get("KHR_texture_transform", {}).get(
                            "texCoord", texture.get("texCoord", 0))
                        self.check(f"TEXCOORD_{uv}" in attrs,
                                   f"{tag}: textured material requires missing TEXCOORD_{uv}")
                if mi not in mesh_skins:
                    continue
                skinned_vertices += count
                weight_sets = sorted(name for name in attrs if name.startswith("WEIGHTS_"))
                joint_sets = sorted(name for name in attrs if name.startswith("JOINTS_"))
                self.check(bool(weight_sets) and "WEIGHTS_0" in attrs, f"{tag}: skinned mesh has no WEIGHTS_0")
                self.check({n[8:] for n in weight_sets} == {n[7:] for n in joint_sets},
                           f"{tag}: joint/weight attribute sets differ")
                sums = [0.0] * count
                for weight_name in weight_sets:
                    suffix = weight_name[8:]
                    joint_name = "JOINTS_" + suffix
                    if joint_name not in attrs:
                        continue
                    wa, ja = g.entry("accessors", attrs[weight_name]), g.entry("accessors", attrs[joint_name])
                    self.check(wa["type"] == "VEC4" and ja["type"] == "VEC4", f"{tag}: joint/weight attributes must be VEC4")
                    self.check(ja["componentType"] in (5121, 5123) and not ja.get("normalized"),
                               f"{tag}: invalid JOINTS component type")
                    weights, joints = g.accessor(attrs[weight_name]), g.accessor(attrs[joint_name])
                    limit = min(len(g.entry("skins", si)["joints"]) for si in mesh_skins[mi])
                    self.check(all(all(0 <= j < limit for j in row) for row in joints), f"{tag}: joint index exceeds attached skin")
                    self.check(all(all(0 <= w <= 1 for w in row) for row in weights), f"{tag}: weight outside [0,1]")
                    for vi, row in enumerate(weights[:count]):
                        sums[vi] += sum(row)
                error = max((abs(total - 1) for total in sums), default=1)
                max_weight_error = max(max_weight_error, error)
                self.check(error <= self.weight_tolerance,
                           f"{tag}: maximum skin weight sum error {error:.7g} exceeds {self.weight_tolerance}")
        self.check(skinned_vertices > 0, "No skinned mesh vertices exported")
        self.summary["geometry"] = {"mesh_count": len(g.entries("meshes")), "primitive_count": primitive_count,
                                    "vertex_count": vertices, "triangle_count": triangles,
                                    "normal_count": normal_vertices, "uv0_count": uv_vertices,
                                    "skinned_vertex_count": skinned_vertices,
                                    "maximum_weight_sum_error": max_weight_error}

    def textures(self):
        g = self.glb
        for i, texture in enumerate(g.entries("textures")):
            source = texture.get("source")
            if source is None:
                source = texture.get("extensions", {}).get("KHR_texture_basisu", {}).get("source")
            self.check(source is not None, f"Texture {i}: no image source")
            if source is not None:
                g.entry("images", source)
        for i, im in enumerate(g.entries("images")):
            if "bufferView" in im:
                data, _ = g.view(im["bufferView"])
                self.check(bool(data), f"Image {i}: empty data")
            elif "uri" in im:
                self.check(im["uri"].startswith("data:"), f"Image {i}: external URI; audition GLBs must be self-contained")
            else:
                self.errors.append(f"Image {i}: missing data")
        self.summary["materials"] = {"material_count": len(g.entries("materials")),
                                     "texture_count": len(g.entries("textures")), "image_count": len(g.entries("images"))}

    def animations(self):
        g = self.glb
        skinned_joints = {j for skin in g.entries("skins") for j in skin.get("joints", [])}
        names = [normalized_name(a.get("name", "")) for a in g.entries("animations")]
        self.check(len(names) == len(EXPECTED), f"Expected 8 clips, found {len(names)}")
        self.check(len(set(names)) == len(names), "Duplicate animation names after prefix normalization")
        self.check(set(names) == set(EXPECTED),
                   f"Animation names differ: missing={sorted(set(EXPECTED) - set(names))}, extra={sorted(set(names) - set(EXPECTED))}")
        summaries = []
        for ai, anim in enumerate(g.entries("animations")):
            name = names[ai]
            tag = f"Animation {anim.get('name', ai)}"
            samplers = anim.get("samplers", [])
            minimum, maximum = math.inf, -math.inf
            moving = moving_joints = keys = 0
            loop_error = 0.0
            targets = set()
            for ci, channel in enumerate(anim.get("channels", [])):
                target = channel.get("target", {})
                node, path = target.get("node"), target.get("path")
                g.entry("nodes", node)
                self.check(path in ("translation", "rotation", "scale", "weights"), f"{tag}: unsupported target path {path}")
                self.check((node, path) not in targets, f"{tag}: duplicate channel for node {node}/{path}")
                targets.add((node, path))
                si = channel["sampler"]
                if not isinstance(si, int) or not 0 <= si < len(samplers):
                    raise InvalidGLB(f"{tag}: invalid sampler index")
                sampler = samplers[si]
                ia, oa = g.entry("accessors", sampler["input"]), g.entry("accessors", sampler["output"])
                times = g.accessor(sampler["input"])
                values = g.accessor(sampler["output"])
                self.check(ia["type"] == "SCALAR" and ia["componentType"] == 5126, f"{tag}: time accessor must be float SCALAR")
                t = [row[0] for row in times]
                self.check(t[0] >= 0 and all(b > a for a, b in zip(t, t[1:])), f"{tag}: times are negative or not strictly increasing")
                minimum, maximum = min(minimum, t[0]), max(maximum, t[-1])
                keys += len(t)
                interpolation = sampler.get("interpolation", "LINEAR")
                self.check(interpolation in ("LINEAR", "STEP", "CUBICSPLINE"), f"{tag}: unsupported interpolation")
                factor = 3 if interpolation == "CUBICSPLINE" else 1
                expected_type = {"translation": "VEC3", "rotation": "VEC4", "scale": "VEC3", "weights": "SCALAR"}.get(path)
                self.check(oa["type"] == expected_type and oa["componentType"] == 5126,
                           f"{tag}: {path} output must be float {expected_type}")
                if path == "weights":
                    # Morph outputs flatten each frame's weights into scalar elements.
                    if not self.check(len(values) % (len(t) * factor) == 0, f"{tag}: morph output count mismatch"):
                        continue
                    width = len(values) // (len(t) * factor)
                    grouped = [tuple(v[0] for v in values[i:i + width]) for i in range(0, len(values), width)]
                else:
                    if not self.check(len(values) == len(t) * factor, f"{tag}: sampler input/output count mismatch"):
                        continue
                    grouped = values
                samples = grouped[1::3] if factor == 3 else grouped
                if path == "rotation":
                    self.check(all(abs(sum(v * v for v in q) - 1) <= .002 for q in samples),
                               f"{tag}: non-unit quaternion keyframe")
                    distance = quat_distance
                else:
                    distance = lambda a, b: max(abs(x - y) for x, y in zip(a, b))
                if any(distance(samples[0], sample) > 1e-6 for sample in samples[1:]):
                    moving += 1
                    if node in skinned_joints:
                        moving_joints += 1
                if name in LOOPS:
                    error = distance(samples[0], samples[-1])
                    loop_error = max(loop_error, error)
                    self.check(error <= self.loop_tolerance,
                               f"{tag}: node {node}/{path} loop endpoint error {error:.7g} exceeds {self.loop_tolerance}")
            duration = maximum - minimum if minimum != math.inf else 0.0
            self.check(duration > 0 and math.isfinite(duration), f"{tag}: clip has no positive finite duration")
            self.check(moving > 0, f"{tag}: all channels are constant/bind-only")
            self.check(moving_joints > 0, f"{tag}: no animated skin-joint channels")
            if duration > 60:
                self.warnings.append(f"{tag}: duration exceeds 60 seconds ({duration:g}s)")
            summaries.append({"name": anim.get("name", ""), "normalized_name": name,
                              "duration_seconds": duration, "channel_count": len(targets),
                              "moving_channels": moving, "moving_skin_joint_channels": moving_joints,
                              "total_channel_keyframes": keys,
                              "loop": name in LOOPS, "maximum_loop_endpoint_error": loop_error if name in LOOPS else None})
        self.summary["animations"] = summaries


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tps", type=Path, default=DEFAULT_DIR / "survivor-tps.glb")
    parser.add_argument("--fps", type=Path, default=DEFAULT_DIR / "survivor-fps.glb")
    parser.add_argument("--out", type=Path, default=DEFAULT_DIR / "validation.json")
    args = parser.parse_args()
    results = {key: Validator(path).run() for key, path in (("tps", args.tps), ("fps", args.fps))}
    passed = all(result["passed"] for result in results.values())
    report = {"schema_version": 1, "passed": passed, "validation_boundary": BOUNDARY,
              "expected_animations": list(EXPECTED),
              "tolerances": {"weight_sum_absolute": .002, "quaternion_squared_norm_absolute": .002,
                             "loop_component_absolute": .0001, "motion_component_absolute": .000001},
              "assets": results}
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    print(json.dumps({"passed": passed, "report": str(args.out.resolve()),
                      "errors": {key: result["errors"] for key, result in results.items()}}, indent=2))
    return 0 if passed else 1


if __name__ == "__main__":
    sys.exit(main())

"""Review renders of the Iron Wake's loading mechanisms (docs/IRON_WAKE.md §9.5), in Blender's Eevee with the asset's
own materials: contact sheets of every phase of each kind's loader at three elevations, the seated gunner's occupied
volumes drawn faintly for scale (the siege battery's two, its ready rack and its breech, which the gun house stands
between), and one cycle of each kind with its views side by side (PNG sequences turned into MP4 and GIF).

    blender -b --factory-startup --python art/ships/review_loaders.py -- <out folder> [sheets] [cycles] [--workbench]

The mechanism is posed straight from game/data/ground/batteries.json by the loaders' module, as the import's clips
are. The rockets in the racks, the rammer's carrier and the cassettes are the catalogue's models
(game/assets/items/rocket*.glb) on the slots' sockets, as the battery view will attach them. ffmpeg is HOLLOW_FFMPEG,
or ShareX's.
"""
import importlib.util
import math
import os
import shutil
import subprocess
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
sys.path.insert(0, str(REPO / "art"))
sys.path.insert(0, str(REPO / "art" / "import"))
FFMPEG = Path(os.environ.get("HOLLOW_FFMPEG", r"D:/GameDev/Tools/ScreenCapture/ShareX/ffmpeg.exe"))
ROCKETS = ("rocket", "rocket-incendiary", "rocket-hv")

# One station of each kind, its loader, and the review's views: cameras in game axes from the pitch pivot, fixed to
# the gun house so the elevation shows (from, at, lens). The siege loader's two ends are 4 m apart with the gun house
# between them, so it has two views, the ready rack from the gunner's side and the breech from outboard; a sheet each,
# side by side in the cycle. The elevations of the sheets, the cycle's elevation.
REVIEW = {
    "siege": {"station": "Siege_Forward", "loader": 1, "elevations": (-8, 16, 40), "cycle": 12, "views": {
        "rack": {"from": (-0.75, 1.75, -2.75), "at": (1.0, 0.45, -1.62), "lens": 19},
        "breech": {"from": (2.75, 1.95, 0.6), "at": (1.0, 0.3, 1.85), "lens": 20}}},
    "defense": {"station": "Defense_F_P", "loader": 0, "elevations": (-15, 25, 65), "cycle": 20, "views": {
        "bay": {"from": (0.95, 2.45, 0.15), "at": (-0.1, -0.02, 0.9), "lens": 24}}},
}


def module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out = Path(a[0]) if a else REPO / "docs" / "images" / "iron-wake"
    what = {w for w in a[1:] if not w.startswith("--")} or {"sheets", "cycles"}
    return out, what, "--workbench" in a


def setup(asset, mod, workbench):
    """LOD0 only, rockets on every slot, the gunners' volumes as faint boxes, studio light."""
    scene = bpy.context.scene
    lod0 = set(bpy.data.collections["01_GAME_LOD0"].all_objects)
    added = set(asset.collection.all_objects)
    for o in bpy.data.objects:
        if o.type == "MESH":
            o.hide_render = not (o in lod0 or (o in added and o.name.endswith("_LOD0")))
    # The rockets: the catalogue's GLBs, their LOD0 baked into meshes put on the slots' sockets.
    meshes = {}
    for key in ROCKETS:
        before = set(bpy.data.objects)
        bpy.ops.import_scene.gltf(filepath=str(REPO / "game" / "assets" / "items" / f"{key}.glb"))
        new = [o for o in bpy.data.objects if o not in before]
        bpy.context.view_layer.update()
        src = next(o for o in new if o.type == "MESH" and o.name.lower().split(".")[0].endswith("_lod0"))
        me = src.data.copy()
        me.transform(src.matrix_world)
        meshes[key] = me
        for o in new:
            o.hide_render = True
            o.hide_viewport = True
    sockets = sorted((o for o in asset.collection.all_objects if o.name.startswith("SOCKET_") and "_Slot_" in o.name),
                     key=lambda o: o.name)
    for i, s in enumerate(sockets):
        r = bpy.data.objects.new(f"round_{s.name}", meshes[ROCKETS[i % len(ROCKETS)]])
        asset.collection.objects.link(r)
        r.parent = s
        r.matrix_parent_inverse = Matrix.Identity(4)
        r.matrix_basis = Matrix.Identity(4)
    # The seated gunners' occupied volumes (gunner_station_layout.json), faint.
    ghost = bpy.data.materials.new("review_gunner")
    ghost.use_nodes = True
    bsdf = ghost.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (0.35, 0.6, 1.0, 1.0)
    bsdf.inputs["Alpha"].default_value = 0.16
    for attr, value in (("surface_render_method", "BLENDED"), ("blend_method", "BLEND")):
        if hasattr(ghost, attr):
            try:
                setattr(ghost, attr, value)
            except TypeError:
                pass
    ghost.diffuse_color = (0.35, 0.6, 1.0, 0.16)
    for s in asset.layout["stations"]:
        for env in s["occupied_envelopes"]:
            me = bpy.data.meshes.new("gunner")
            c, h = Vector(env["center_m"]), Vector(env["size_m"]) * 0.5
            v = [c + Vector((x * h.x, y * h.y, z * h.z)) for z in (-1, 1) for y in (-1, 1) for x in (-1, 1)]
            me.from_pydata(v, [], [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)])
            me.materials.append(ghost)
            o = bpy.data.objects.new("gunner", me)
            asset.collection.objects.link(o)
    # Light: a studio sky for the metals' reflections and a low sun for shadows.
    world = bpy.data.worlds.new("review")
    scene.world = world
    if hasattr(world, "use_nodes") and not world.use_nodes:
        world.use_nodes = True
    nt = world.node_tree
    env = nt.nodes.new("ShaderNodeTexEnvironment")
    hdri = Path(bpy.utils.resource_path("LOCAL")) / "datafiles" / "studiolights" / "world" / "forest.exr"
    env.image = bpy.data.images.load(str(hdri))
    sky = nt.nodes["Background"]
    nt.links.new(env.outputs["Color"], sky.inputs["Color"])
    sky.inputs["Strength"].default_value = 0.9
    # The sky lights the scene; what the camera sees behind the ship is a plain studio grey.
    flat = nt.nodes.new("ShaderNodeBackground")
    flat.inputs["Color"].default_value = (0.30, 0.31, 0.32, 1.0)
    path = nt.nodes.new("ShaderNodeLightPath")
    mix = nt.nodes.new("ShaderNodeMixShader")
    nt.links.new(path.outputs["Is Camera Ray"], mix.inputs["Fac"])
    nt.links.new(sky.outputs["Background"], mix.inputs[1])
    nt.links.new(flat.outputs["Background"], mix.inputs[2])
    nt.links.new(mix.outputs["Shader"], nt.nodes["World Output"].inputs["Surface"])
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 3.2
    sun.data.angle = math.radians(1.5)
    sun.rotation_euler = (math.radians(50), math.radians(-12), math.radians(205))
    scene.collection.objects.link(sun)
    if workbench:
        scene.render.engine = "BLENDER_WORKBENCH"
        sh = scene.display.shading
        sh.light, sh.color_type, sh.show_cavity = "STUDIO", "MATERIAL", True
    else:
        engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items]
        scene.render.engine = "BLENDER_EEVEE" if "BLENDER_EEVEE" in engines else "BLENDER_EEVEE_NEXT"
        ee = scene.eevee
        for attr, value in (("taa_render_samples", 48), ("use_raytracing", True), ("use_shadows", True),
                            ("shadow_resolution_scale", 1.0), ("use_gtao", True)):
            if hasattr(ee, attr):
                setattr(ee, attr, value)
        scene.view_settings.view_transform = "AgX"
        scene.view_settings.look = "AgX - Medium High Contrast"
    cam = bpy.data.objects.new("review_cam", bpy.data.cameras.new("review_cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.clip_start = 0.05
    return cam


def aim(cam, st, review):
    p, t = st.native(review["from"]), st.native(review["at"])
    cam.location = p
    d = (t - p).normalized()
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = review["lens"]


def show(poser, st, mod, e, t, lead=False):
    """Poses the station: its review loader at cycle time t (None: at rest), the others at rest."""
    times = [None] * st.loaders()
    times[REVIEW[st.kind]["loader"]] = t
    shown = poser.pose(st, e, times, lead=lead)
    for n, rec in poser.B.nodes.items():
        if rec["station"] != st.id:
            continue
        for c in rec["empty"].children:
            if c.type == "MESH" and c.name.endswith("_LOD0"):
                c.hide_render = not shown.get(n, True)
    bpy.context.view_layer.update()


def render(path, size):
    scene = bpy.context.scene
    scene.render.resolution_x, scene.render.resolution_y = size
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


def tile(out, cells, cols, size, font):
    """A sheet of labelled cells (ffmpeg's xstack and drawtext)."""
    w, h = size
    cmd = [str(FFMPEG), "-y", "-loglevel", "error"]
    for path, _ in cells:
        cmd += ["-i", str(path)]
    parts = []
    for i, (_, label) in enumerate(cells):
        parts.append(f"[{i}]drawtext=fontfile={font}:text='{label}':x=10:y=8:fontsize=20:fontcolor=white:"
                     f"box=1:boxcolor=black@0.55:boxborderw=6[v{i}]")
    layout = "|".join(f"{(i % cols) * w}_{(i // cols) * h}" for i in range(len(cells)))
    parts.append("".join(f"[v{i}]" for i in range(len(cells))) + f"xstack=inputs={len(cells)}:layout={layout}[o]")
    cmd += ["-filter_complex", ";".join(parts), "-map", "[o]", "-frames:v", "1", "-q:v", "3", str(out)]
    subprocess.run(cmd, check=True, cwd=str(Path(font).parent))


def main():
    out, what, workbench = args()
    out.mkdir(parents=True, exist_ok=True)
    iw = module("iron_wake", REPO / "art" / "import" / "iron_wake.py")
    iw.load()
    asset = iw.Asset()
    asset.skip_clearance = True
    mod, _ = iw.run_loaders(asset)
    table = mod.load_table()
    poser = mod.Poser(asset, asset.builder, table)
    cam = setup(asset, mod, workbench)
    work = out / "_review"
    work.mkdir(exist_ok=True)
    font = work / "font.ttf"
    shutil.copy(Path(os.environ.get("WINDIR", r"C:\Windows")) / "Fonts" / "arial.ttf", font)
    stations = {st.id: st for st in mod.STATE["stations"]}
    for kind, review in REVIEW.items():
        st = stations[review["station"]]
        bat = table["batteries"][kind]
        views = review["views"]
        if "sheets" in what:
            for view, camera in views.items():
                aim(cam, st, camera)
                cells = []
                for e in review["elevations"]:
                    for ph, t0, t1 in mod.phases(bat):
                        if ph["name"] == "recover":
                            rc = bat["recoil"]
                            t = rc["times"][rc["fractions"].index(max(rc["fractions"]))]
                        else:
                            t = t0 + 0.6 * (t1 - t0)
                        show(poser, st, mod, e, t)
                        path = work / f"{kind}_{view}_{ph['name']}_{e}.png"
                        render(path, (640, 400))
                        cells.append((path, f"{ph['name']}  {e} deg  t {t:.2f} s"))
                name = f"loader-{kind}-sheet.jpg" if len(views) == 1 else f"loader-{kind}-{view}-sheet.jpg"
                tile(out / name, cells, len(mod.phases(bat)), (640, 400), font.name)
        if "cycles" in what:
            fps = 24
            lead = 2 * bat["fire"]["interval"] if kind == "defense" else 0.0
            total = mod.cycle_seconds(bat) + lead
            folders = []
            for view in views:
                folder = work / f"{kind}_{view}_cycle"
                if folder.exists():
                    shutil.rmtree(folder)
                folder.mkdir()
                folders.append(folder)
            for f in range(int(total * fps) + 1):
                t = f / fps - lead
                show(poser, st, mod, review["cycle"], t if t >= 0 else None, lead=bool(lead))
                if t < 0:
                    # Before the third shot the first two tubes of the ripple recoil.
                    for node, v in mod.ripple(bat, st.id, t, lead).items():
                        o = asset.objects[node]
                        o.matrix_basis = poser.asset[o] @ mod.to_native(mod.game_delta(v))
                    bpy.context.view_layer.update()
                for camera, folder in zip(views.values(), folders):
                    aim(cam, st, camera)
                    render(folder / f"f{f:04d}.png", (960, 600))
            inputs = []
            for folder in folders:
                inputs += ["-framerate", str(fps), "-i", str(folder / "f%04d.png")]
            n = len(folders)
            join = "".join(f"[{i}]" for i in range(n)) + (f"hstack=inputs={n}" if n > 1 else "null")
            mp4 = out / f"loader-{kind}-cycle.mp4"
            gif = out / f"loader-{kind}-cycle.gif"
            subprocess.run([str(FFMPEG), "-y", "-loglevel", "error"] + inputs +
                           ["-filter_complex", join + "[v]", "-map", "[v]", "-c:v", "libx264", "-pix_fmt", "yuv420p",
                            "-crf", "20", "-movflags", "+faststart", str(mp4)], check=True)
            subprocess.run([str(FFMPEG), "-y", "-loglevel", "error"] + inputs +
                           ["-filter_complex", join + f",fps=12,scale={480 * n}:-1:flags=lanczos,split[a][b];"
                            "[a]palettegen=max_colors=128[p];[b][p]paletteuse=dither=bayer:bayer_scale=4[v]",
                            "-map", "[v]", str(gif)], check=True)
    shutil.rmtree(work)
    poser.rest()
    print("review written to", out)


main()

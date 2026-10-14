"""The Iron Wake's data file, game/data/ground/ships/iron-wake.json (docs/IRON_WAKE.md §4.3, §5.4, §8, §9.1, §9.4).

Measured from the asset's own objects in its native frame (+X forward, +Z up, metres) and written in the model's game
axes: +Z the nose, +Y up, +X the ship's left (port), metres, from the model's origin. A native point (x, y, z) is
(y, z, x) here. Everything is at the parked pose (ramp and airlock shut, turrets at yaw 0, pitch 0, barrels run out);
a moving part's pieces, sockets and boxes are given at that pose and move with the part about its pivot.

The asset's side labels are mirrored: it puts what it calls Port (Engine_Port, Outrigger_Port, the Defense_*_P
mounts, the airlock, the pilot's seat) at native -Y, which is the ship's right in a right-handed frame with +X forward
and +Z up, and its HandGrip_L at each gunner's right hand. Node and socket names stay the asset's, so they can be
traced; every side recorded here (`side`, the hull's boxes, the grips) is the true one, measured in game axes.
"""
import hashlib
import math

import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

from hollow import log
from hollow.large import mesh_arrays, number, vec

HEADROOM = 2.2       # the boarding volume above a walkable top (a standing character, with margin)
LANE = 1.2           # circulation lanes kept clear of building (IRON_WAKE.md §8)
LANDING = 1.0        # landings kept clear at each end of a stair


def game(v):
    """A native point or direction in game axes."""
    return (v[1], v[2], v[0])


def gp(v, places=3):
    return vec(game(v), places)


def side(x_game):
    return "port" if x_game > 0.05 else "starboard" if x_game < -0.05 else "centre"


def box(lo, hi, places=3, **extra):
    """A native axis-aligned box in game axes."""
    a, b = game(lo), game(hi)
    out = {k: v for k, v in extra.items()}
    out["min"] = vec([min(a[i], b[i]) for i in range(3)], places)
    out["max"] = vec([max(a[i], b[i]) for i in range(3)], places)
    return out


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def world_points(o):
    mw = o.matrix_world
    return [mw @ v.co for v in o.data.vertices]


def aabb(points):
    xs = [p[0] for p in points]
    ys = [p[1] for p in points]
    zs = [p[2] for p in points]
    return (min(xs), min(ys), min(zs)), (max(xs), max(ys), max(zs))


def merge(a, b):
    return (tuple(min(a[0][i], b[0][i]) for i in range(3)), tuple(max(a[1][i], b[1][i]) for i in range(3)))


def tree_of(objects, skip=()):
    verts, tris = [], []
    base = 0
    for o in objects:
        co, _, tv, mats = mesh_arrays(o)
        keep = [i for i, m in enumerate(mats) if m not in skip]
        verts.append(co)
        if keep:
            tris.append(tv[keep] + base)
        base += len(co)
    v = np.concatenate(verts)
    t = np.concatenate(tris) if tris else np.zeros((0, 3), np.int32)
    return BVHTree.FromPolygons(v.tolist(), t.tolist(), epsilon=0.0)


def rot_y(deg):
    return Matrix.Rotation(math.radians(deg), 3, "Y")


def rot_z(deg):
    return Matrix.Rotation(math.radians(deg), 3, "Z")


class Ship:
    """What the data file is measured from: the asset's documents and objects (native frame, parked pose)."""

    def __init__(self, asset):
        self.a = asset
        self.m = asset.manifest
        self.groups = self.m["groups"]
        self.turrets = {t["gunner_station"]: t for t in self.m["features"]["weapons"]["articulated_turrets"]}
        self.stations = {s["id"]: s for s in self.m["features"]["gunner_stations"]}
        self.sockets = {s["name"]: s for s in self.m["sockets"]}

    def lod(self, group, level):
        return self.a.lods[group][level]

    def origin(self, group):
        return Vector(self.groups[group]["origin"])

    def socket_matrix(self, name):
        s = self.sockets[name]
        parent = s["parent"] if s["parent"] in self.groups else "Hull_Underbody"
        return bpy.data.objects[f"SOCKET_SM_IW_{parent}_{name}"].matrix_world.copy()


# ------------------------------------------------------------------------------------------------ parts and sockets


def parts(ship, moving, loaders, clearance):
    out = [{"name": "body", "kind": "static"}]
    motion = ship.a.motion
    ramp = ship.m["features"]["ramp"]
    for name, parent in moving.items():
        o = Vector(ship.groups[name]["origin"])
        rec = {"name": name, "parent": parent, "pivot": gp(o, 4)}
        if name == "Ramp_Main":
            rec.update(kind="hinge", axis=[1, 0, 0], closed=0, open=ramp["open_deg"][0], seconds=3.0)
        elif name == "Ramp_Leaf":
            rec.update(kind="hinge", axis=[1, 0, 0], closed=0, open=ramp["open_deg"][1], seconds=3.0)
        elif name == "Airlock_Door":
            rec.update(kind="slide", axis=gp((1, 0, 0), 4), closed=0, open=2.5, seconds=1.4)
        else:
            station = next(s for s, t in ship.turrets.items() if name in (t["yaw"], t["pitch"]) or name in t["recoil_nodes"])
            t = ship.turrets[station]
            if name == t["yaw"]:
                # Positive turns the guns to port (the ship's left), as the right-hand rule about +Y gives.
                rec.update(kind="yaw", axis=[0, 1, 0], station=station)
                # Where the guns clear the hull firing level (degrees; an arc from a to b with a > b runs through 180).
                rec["levelArcs"] = clearance.get(station, {}).get("arcs", [])
            elif name == t["pitch"]:
                # Positive raises the barrels: about -X (native -Y).
                rec.update(kind="pitch", axis=gp((0, -1, 0), 4), station=station)
            else:
                profile = motion["profiles"][t["weapon_class"]]
                rec.update(kind="recoil", axis=gp((-1, 0, 0), 4), station=station, barrel=t["recoil_nodes"].index(name),
                           travel=profile["travel_m"], seconds=profile["times_seconds"][-1])
        rec["side"] = side(game(o)[0])
        out.append(rec)
    for item in loaders:
        out.append(item)
    return out


def sockets(ship, moving):
    out = {}
    for name, s in ship.sockets.items():
        mw = ship.socket_matrix(name)
        rec = {}
        if s["parent"] in moving:
            rec["part"] = s["parent"]
        rec["position"] = gp(mw.translation, 4)
        rec["forward"] = gp(mw.col[0].xyz.normalized(), 4)
        rec["up"] = gp(mw.col[2].xyz.normalized(), 4)
        out[name] = rec
    return out


def pieces(ship, ucx, moving):
    out = []
    for u in sorted(ucx, key=lambda o: o.name):
        group = u.parent.name[len("SM_IW_"):]
        pts = sorted({tuple(gp(p, 3)) for p in world_points(u)})
        rec = {"part": group if group in moving else "body", "name": u.get("proxy_for", u.name)}
        if "Glass" in rec["name"]:
            rec["glass"] = True
        rec["points"] = [list(p) for p in pts]
        out.append(rec)
    return out


# ------------------------------------------------------------------------------------------------ spaces


ROOM_IDS = {"Deck A": "deck-a", "Double-height": "bay", "Deck B": "deck-b", "Bridge": "bridge"}
ZONE_IDS = {"Tall fabrication bay": "bay", "Lower forward construction deck": "forward",
            "Lower aft storage deck": "aft", "Upper mezzanine": "mezzanine"}


def rooms(ship):
    out = []
    for r in ship.m["rooms"]:
        rid = next(v for k, v in ROOM_IDS.items() if r["name"].startswith(k))
        out.append(box(r["bounds"][0], r["bounds"][1], id=rid, name=r["name"], floor=number(r["floor_z"], 3)))
    return out


def ramp_pose(ship, t):
    """World matrices of the ramp's main leaf and fold leaf at clip time t (seconds, 0 shut .. 3 open)."""
    times = [0, .36, 2.64, 3.0]
    main_a = [0, -3, -113, -114]
    leaf_a = [0, 4, 176, 180]

    def at(values):
        if t <= times[0]:
            return values[0]
        for i in range(1, len(times)):
            if t <= times[i]:
                f = (t - times[i - 1]) / (times[i] - times[i - 1])
                return values[i - 1] + f * (values[i] - values[i - 1])
        return values[-1]
    hinge = ship.origin("Ramp_Main")
    leaf_rest = ship.origin("Ramp_Leaf") - hinge
    main = Matrix.Translation(hinge) @ Matrix.Rotation(math.radians(at(main_a)), 4, "Y")
    leaf = main @ Matrix.Translation(leaf_rest) @ Matrix.Rotation(math.radians(at(leaf_a)), 4, "Y")
    return main, leaf


def ramp_geometry(ship, t):
    """The ramp's collision points at clip time t, native."""
    main, leaf = ramp_pose(ship, t)
    pts = []
    for group, m in (("Ramp_Main", main), ("Ramp_Leaf", leaf)):
        o = ship.lod(group, 0)
        inv = o.matrix_world.inverted()
        for u in [c for c in o.children if c.name.startswith("UCX_")]:
            for v in u.data.vertices:
                local = inv @ (u.matrix_world @ v.co)
                pts.append(m @ local)
    return pts


def walkable_tops(ship, ucx, moving):
    """Up-facing tops of the collision pieces (native): (group, name, lo, hi, top)."""
    out = []
    for u in sorted(ucx, key=lambda o: o.name):
        name = u.get("proxy_for", u.name)
        if "Glass" in name:
            continue
        group = u.parent.name[len("SM_IW_"):]
        mw = u.matrix_world
        nm = mw.to_3x3().inverted().transposed()
        tops = []
        for p in u.data.polygons:
            n = (nm @ p.normal).normalized()
            if n.z > 0.7 and p.area > 0.04:
                tops.append([mw @ u.data.vertices[i].co for i in p.vertices])
        if not tops:
            continue
        pts = [q for f in tops for q in f]
        lo, hi = aabb(pts)
        # Somewhere to stand: not a wall's or a rail's top, nothing under the deck.
        if min(hi[0] - lo[0], hi[1] - lo[1]) < 0.3 or hi[2] < 3.0:
            continue
        out.append((group, name, lo, hi, hi[2]))
    return out


def merged_boxes(items):
    """Greedily merges boxes of the same part and top height (within 5 cm) whose union stays nearly full."""
    boxes = [dict(i) for i in items]
    changed = True
    while changed:
        changed = False
        for i in range(len(boxes)):
            for j in range(i + 1, len(boxes)):
                a, b = boxes[i], boxes[j]
                if a.get("part") != b.get("part") or abs(a["top"] - b["top"]) > 0.05:
                    continue
                lo = [min(a["lo"][k], b["lo"][k]) for k in range(3)]
                hi = [max(a["hi"][k], b["hi"][k]) for k in range(3)]
                ua = (hi[0] - lo[0]) * (hi[1] - lo[1])
                sa = (a["hi"][0] - a["lo"][0]) * (a["hi"][1] - a["lo"][1]) + \
                     (b["hi"][0] - b["lo"][0]) * (b["hi"][1] - b["lo"][1])
                if ua <= sa * 1.08 + 0.05:
                    a.update(lo=lo, hi=hi, top=max(a["top"], b["top"]), names=sorted(set(a["names"] + b["names"])))
                    del boxes[j]
                    changed = True
                    break
            if changed:
                break
    return boxes


def boarding(ship, ucx, moving, room_list):
    out = []
    for r in ship.m["rooms"]:
        rid = next(v for k, v in ROOM_IDS.items() if r["name"].startswith(k))
        out.append(box(r["bounds"][0], r["bounds"][1], name=rid))
    items = []
    for group, name, lo, hi, top in walkable_tops(ship, ucx, moving):
        part = group if group in moving else None
        if part in ("Ramp_Main", "Ramp_Leaf"):
            continue
        items.append({"part": part, "names": [name], "lo": [lo[0], lo[1], top], "hi": [hi[0], hi[1], top + HEADROOM],
                      "top": top})
    for b in merged_boxes(items):
        rec = {"name": "+".join(b["names"][:3]) + ("+..." if len(b["names"]) > 3 else "")}
        if b["part"]:
            rec["part"] = b["part"]
        out.append(box(b["lo"], b["hi"], **rec))
    # The ramp open: its walking surface and the headroom over it.
    pts = ramp_geometry(ship, 3.0)
    lo, hi = aabb(pts)
    out.append(box((lo[0], lo[1], lo[2]), (hi[0], hi[1], hi[2] + HEADROOM), name="ramp", when="ramp-open"))
    return out


def build_zones(ship):
    out = []
    for z in ship.m["features"]["build_zones"]:
        rec = box(z["bounds"][0], z["bounds"][1], id=ZONE_IDS[z["name"]], name=z["name"], floor=number(z["floor_z"], 3),
                  headroom=number(z.get("headroom_m", z.get("nominal_headroom_m", 0)), 3))
        out.append(rec)
    return out


def exclusions(ship, ucx):
    out = []

    def add(name, lo, hi, why):
        out.append(box(lo, hi, name=name, why=why))
    ramp = ship.m["features"]["ramp"]
    h = Vector(ramp["hinge"])
    w = ramp["width_m"] / 2 + 0.1
    add("ramp-top", (h.x, -w, h.z), (h.x + 3.0, w, 7.0), "the ramp's top: the way in and out")
    swing = []
    for k in range(31):
        swing += ramp_geometry(ship, 3.0 * k / 30)
    lo, hi = aabb(swing)
    add("ramp-swing", lo, hi, "the space the ramp sweeps")
    door = ship.lod("Airlock_Door", 0)
    dlo, dhi = aabb(world_points(door))
    # The inner face of the deck's lining on the airlock's side.
    lining = max(aabb(world_points(u))[1][1] for u in ucx
                 if u.get("proxy_for") == "A_InnerWall" and aabb(world_points(u))[1][1] < 0)
    add("airlock-approach", (dlo[0] - 0.25, -6.55, 3.2), (dhi[0] + 0.25, lining + 1.5, dhi[2] + 0.15),
        "the airlock's door and the deck inside it")
    stair = next(u for u in ucx if u.get("proxy_for") == "Stair_WalkRamp")
    slo, shi = aabb(world_points(stair))
    add("stairs", (slo[0] - LANDING, slo[1] - 0.14, 3.2), (shi[0], shi[1] + 0.16, 7.4),
        "the stair and the landing at its foot")
    add("stairs-top", (shi[0], slo[1] - 0.14, 7.4), (shi[0] + 0.44 + LANDING, shi[1] + 0.06, 10.7),
        "the landing at the stair's head")
    for z in ship.m["features"]["build_zones"]:
        for k, ex in enumerate(z.get("excluded_bounds", [])):
            add("stair-void", ex[0], ex[1], "the open stairwell through the mezzanine")
    bridge = next(r for r in ship.m["rooms"] if r["name"].startswith("Bridge"))
    add("bridge", bridge["bounds"][0], bridge["bounds"][1], "the bridge: pilot stations, dais and its steps")
    for route in ship.m["features"]["gunner_access_routes"]:
        lanes(out, route["id"], route["waypoints_m"])
    # Added by the import: the asset's routes start on the upper deck, so the ways from the ramp and the airlock to
    # the stair's foot are kept clear too (along the port wall from the ramp; straight across from the airlock).
    lanes(out, "ramp-to-stairs", [(-14.7, 0.0, 3.2), (-14.7, 4.75, 3.2), (slo[0] - LANDING, 4.75, 3.2)])
    mid = (dlo[0] + dhi[0]) / 2
    lanes(out, "airlock-to-stairs", [(mid, lining + 1.5, 3.2), (mid, slo[1] - 0.14, 3.2)])
    for sid, s in ship.stations.items():
        feet = [Vector(ship.sockets[s["entry_socket"]]["position"])]
        feet += [Vector(p) for p in s["stationary_access_points_m"]]
        for k, p in enumerate(feet):
            add(f"station-{sid}-{k}", (p.x - 0.5, p.y - 0.5, p.z), (p.x + 0.5, p.y + 0.5, p.z + 2.0),
                "a gunner station's access and exit")
    return out


def lanes(out, name, points):
    """1.2 m lanes along a route's segments, from the feet up to headroom."""
    for k in range(len(points) - 1):
        a, b = Vector(points[k]), Vector(points[k + 1])
        lo = [min(a[i], b[i]) for i in range(3)]
        hi = [max(a[i], b[i]) for i in range(3)]
        along = 0 if abs(b.x - a.x) >= abs(b.y - a.y) else 1
        across = 1 - along
        lo[across] -= LANE / 2
        hi[across] += LANE / 2
        lo[along] -= LANE / 2
        hi[along] += LANE / 2
        hi[2] += HEADROOM
        out.append(box(lo, hi, name=f"lane-{name}-{k}", why="a way kept clear"))


def routes(ship):
    return [{"id": r["id"], "station": r["station_id"], "width": LANE, "points": [gp(p, 3) for p in r["waypoints_m"]]}
            for r in ship.m["features"]["gunner_access_routes"]]


# ------------------------------------------------------------------------------------------------ stations


def barrel_radius(ship, recoil):
    """The barrel's radius: the largest distance from its axis over the front half (the tube and its collar)."""
    o = ship.lod(recoil, 0)
    origin = o.matrix_world.translation
    pts = world_points(o)
    length = max(p.x for p in pts) - origin.x
    r = 0.0
    for p in pts:
        if p.x - origin.x > length * 0.55:
            r = max(r, math.hypot(p.y - origin.y, p.z - origin.z))
    return r, length


def clearance(ship, moving):
    """For each station: per degree of yaw (-180..179), the lowest elevation from -20 to 70 degrees from which every
    higher one lets the barrels clear the ship (from the breech to 6 m past the muzzle on the axis, and the tube's
    radius to the muzzle), or None; and the arcs of yaw where the guns clear the ship firing level (that floor at or
    below 0)."""
    result = {}
    stat = [g for g in ship.groups if g not in moving]
    for sid, t in ship.turrets.items():
        others = [g for g in moving if not g.startswith(sid + "_")]
        tree = tree_of([ship.lod(g, 1) for g in stat + others if ship.a.lods[g][1] is not None])
        yaw_o = ship.origin(t["yaw"])
        pitch_o = ship.origin(t["pitch"])
        lines = []
        for k, recoil in enumerate(t["recoil_nodes"]):
            b = ship.origin(recoil)
            muzzle = Vector(ship.sockets[f"{sid}_Muzzle_{k:02d}"]["position"])
            r, _ = barrel_radius(ship, recoil)
            lines.append((b, muzzle + Vector((6.0, 0, 0))))
            for dy, dz in ((r, 0), (-r, 0), (0, r), (0, -r)):
                off = Vector((0, dy, dz))
                lines.append((b + off, muzzle + off))
        floor = []
        for yaw in range(-180, 180):
            ry = rot_z(yaw)
            lowest = None
            for e in range(70, -21, -1):
                rp = rot_y(-e)
                blocked = False
                for a, b in lines:
                    pa = yaw_o + ry @ (pitch_o - yaw_o + rp @ (a - pitch_o))
                    pb = yaw_o + ry @ (pitch_o - yaw_o + rp @ (b - pitch_o))
                    d = pb - pa
                    if tree.ray_cast(pa, d.normalized(), d.length)[0] is not None:
                        blocked = True
                        break
                if blocked:
                    break
                lowest = e
            floor.append(lowest)
        level = [f is not None and f <= 0 for f in floor]
        arcs = []
        if all(level):
            arcs = [[-180, 180]]
        elif any(level):
            # Runs of level yaw on the circle, starting the walk at a blocked degree so none is cut in two.
            start = level.index(False)
            run = None
            for k in range(1, 361):
                i = (start + k) % 360
                if level[i] and run is None:
                    run = [i - 180, i - 180]
                elif level[i]:
                    run[1] = i - 180
                elif run is not None:
                    arcs.append(run)
                    run = None
            if run is not None:
                arcs.append(run)
            arcs.sort()
        result[sid] = {"floor": floor, "arcs": arcs}
        log(f"clearance {sid}: level {arcs}, floor at 0: {floor[180]}")
    return result


def receivers(ship):
    """Each battery's breech end, for the loading mechanisms (IRON_WAKE.md §9.5), in the pitch node's frame at rest
    (game axes, from the pitch pivot): the receiver housing's box (the pitch part), and each barrel's rear end on its
    axis (the recoil node's origin), length and radius (its tube, heat shield and collar over the front half)."""
    out = {}
    for sid, t in ship.turrets.items():
        p0 = ship.origin(t["pitch"])
        lo, hi = aabb(world_points(ship.lod(t["pitch"], 0)))
        barrels = []
        for k, recoil in enumerate(t["recoil_nodes"]):
            r, length = barrel_radius(ship, recoil)
            barrels.append({"recoil": recoil, "breech": gp(ship.origin(recoil) - p0, 4), "length": number(length, 3),
                            "radius": number(r, 3)})
        out[sid] = {"pitch": t["pitch"], "housing": box(Vector(lo) - p0, Vector(hi) - p0, 3), "barrels": barrels}
    return out


def stations(ship, moving, clear, recv):
    out = []
    for sid, s in ship.stations.items():
        t = ship.turrets[sid]
        yaw_o = ship.origin(t["yaw"])
        seat = Vector(ship.sockets[s["seat_socket"]]["position"])
        grips = {}
        for name in s["handgrip_sockets"]:
            p = Vector(ship.sockets[name]["position"])
            # Which hand: the gunner faces native +X, so native +Y is the left hand.
            grips["left" if p.y > seat.y else "right"] = name
        barrels = []
        for k, recoil in enumerate(t["recoil_nodes"]):
            o = ship.origin(recoil)
            # Its side of the mount, as the gunner sees it (a part's "side" is the ship's, at the parked pose).
            barrels.append({"recoil": recoil, "muzzle": f"{sid}_Muzzle_{k:02d}", "breech": gp(o, 4),
                            "mountSide": side(game(o - yaw_o)[0])})
        rec = {
            "id": sid, "kind": t["weapon_class"], "side": side(game(yaw_o)[0]),
            "yaw": t["yaw"], "pitch": t["pitch"], "mount": t["mount"], "access": s["access_group"],
            "barrels": barrels,
            "seat": s["seat_socket"], "eye": s["camera_socket"], "camera": s["camera_socket"],
            "grips": {"left": grips.get("left"), "right": grips.get("right")},
            "entry": s["entry_socket"], "exit": s["exit_socket"], "interaction": s["interaction_socket"],
            "route": next((r["id"] for r in ship.m["features"]["gunner_access_routes"] if r["station_id"] == sid), None),
            "accessPoints": [gp(p, 3) for p in s["stationary_access_points_m"]],
            "occupied": [box([c - h / 2 for c, h in zip(e["center_m"], e["size_m"])],
                             [c + h / 2 for c, h in zip(e["center_m"], e["size_m"])], name=e["id"])
                         for e in s["occupied_envelopes"]],
            "receiver": recv[sid],
            "clearance": {"yawFrom": -180, "pitchFrom": -20, "pitchTo": 70, "levelArcs": clear[sid]["arcs"],
                          "floor": clear[sid]["floor"]},
        }
        out.append(rec)
    return out


# ------------------------------------------------------------------------------------------------ hull and lights


def hull(ship, moving):
    """Ten boxes fitted to the model for the world's sweeps (IRON_WAKE.md §5.4) and the landing pads' feet."""
    def pts_of(groups, keep=lambda p: True):
        out = []
        for g in groups:
            out += [p for p in world_points(ship.lod(g, 0)) if keep(p)]
        return out
    hull_groups = ["Hull_Underbody", "Hull_Port", "Hull_Starboard", "Hull_Stern", "Hull_Bow", "Aft_WeatherDeck",
                   "Side_WeatherDecks", "Airlock_Frame", "Exterior_Fittings", "DeckA_Floor", "KeelLance_Mount",
                   "KeelLance_Pitch", "Lift_-7_-1", "Lift_-7_1", "Lift_9_-1", "Lift_9_1"]
    split = 18.0
    boxes = []

    def add(name, pts, source, part=None, origin=None):
        lo, hi = aabb(pts)
        rec = {"name": name}
        if part:
            rec["part"] = part
        rec["source"] = source
        boxes.append(box(lo, hi, **rec))
    add("body", pts_of(hull_groups, lambda p: p.x <= split), "the hull aft of the bow's taper")
    add("bow", pts_of(hull_groups, lambda p: p.x >= split), "the bow forward of x 18 m (native)")
    add("superstructure", pts_of(["DeckB_PortWall", "DeckB_StarboardWall", "DeckB_Roof", "Launch_Bank",
                                  "Engineering_Backbone", "Service_Radiators"]), "the upper deck, its roof and fittings")
    add("bridge", pts_of(["Bridge_Shell", "Bridge_Floor"]), "the bridge")
    for group in ("Engine_Port", "Engine_Starboard"):
        pts = pts_of([group])
        add("drive-" + side(game(aabb(pts)[0])[0] + game(aabb(pts)[1])[0]), pts, group)
    for sign, group in ((-1, "Outrigger_Port"), (1, "Outrigger_Starboard")):
        # The catwalk along that side up to its rails' tops (not the stair to the roof, a thin frame above them).
        pts = pts_of([group, "Gunner_ServiceCirculation"], lambda p, s=sign: p.y * s > 5.2 and p.z <= 9.0)
        for g in ship.groups:
            if g.startswith("Defense_") and ("_P" in g[9:11] if sign < 0 else "_S" in g[9:11]) and g not in moving:
                pts += pts_of([g])
        for sid, t in ship.turrets.items():
            pivot = ship.origin(t["yaw"])
            if t["weapon_class"] != "defense" or pivot.y * sign < 0:
                continue
            reach = 0.0
            for g in [t["yaw"], t["pitch"]] + t["recoil_nodes"]:
                for p in world_points(ship.lod(g, 0)):
                    reach = max(reach, math.hypot(p.x - pivot.x, p.y - pivot.y))
            top = max(p.z for g in [t["yaw"], t["pitch"]] + t["recoil_nodes"] for p in world_points(ship.lod(g, 0)))
            pts += [pivot + Vector((reach, reach, top - pivot.z)), pivot + Vector((-reach, -reach, 0))]
        lo, hi = aabb(pts)
        name = "outrigger-" + side(game(lo)[0] + game(hi)[0])
        add(name, pts, f"{group}, the catwalk, the sponsons and the defense mounts' turning circles")
    for sid in ("Siege_Forward", "Siege_Dorsal"):
        t = ship.turrets[sid]
        pts = pts_of([t["yaw"], t["pitch"]] + t["recoil_nodes"])
        add("battery-" + ("forward" if "Forward" in sid else "dorsal"), pts, f"{sid} at the parked pose", part=t["yaw"])
    pads = []
    for g in sorted(g for g in ship.groups if g.startswith("Landing_")):
        o = ship.lod(g, 0)
        shoe = next(u for u in o.children if u.name.startswith("UCX_"))
        lo, hi = aabb(world_points(shoe))
        foot = ((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2, lo[2])
        x = game(foot)[0]
        pads.append({"name": ("fore" if foot[0] > 0 else "aft") + "-" + side(x), "source": g, "foot": gp(foot, 3),
                     "size": vec([hi[1] - lo[1], hi[0] - lo[0]], 3)})
    return {"boxes": boxes, "pads": pads}


def lights(ship, moving, room_list):
    """Every light fixture: each separate piece of the lamp material in LOD0, where it is, which way it shines and
    whether it is an omni or a spot by its shape. Each glowing face counts by its area times how much of its
    hemisphere is open within a metre (a lens's face against its housing, a ceiling or inside the solid it is set in
    counts for nothing, the face looking into the room fully); the fixture is a spot along the mean of those
    weighted normals when they agree (a lens in a housing, a strip under a ceiling, a glow at the back of a nozzle)
    and an omni when they cancel out (a beacon open all round)."""
    import bmesh
    from hollow.large import hemisphere
    out = []
    groups = sorted(g for g in ship.groups if ship.a.lods[g][0] is not None)
    tree = tree_of([ship.lod(g, 0) for g in groups], skip=("M_IronWake_Lamps",))
    hemi = hemisphere(24).tolist()
    for g in groups:
        o = ship.lod(g, 0)
        names = [m.name if m else "" for m in o.data.materials]
        if "M_IronWake_Lamps" not in names:
            continue
        idx = names.index("M_IronWake_Lamps")
        bm = bmesh.new()
        bm.from_mesh(o.data)
        bm.faces.ensure_lookup_table()
        mw = o.matrix_world
        nm = mw.to_3x3().inverted().transposed()
        seen = set()
        fixtures = []
        for f in bm.faces:
            if f.material_index != idx or f.index in seen:
                continue
            stack, comp = [f], []
            seen.add(f.index)
            while stack:
                x = stack.pop()
                comp.append(x)
                for e in x.edges:
                    for y in e.link_faces:
                        if y.material_index == idx and y.index not in seen:
                            seen.add(y.index)
                            stack.append(y)
            pts = [mw @ v.co for x in comp for v in x.verts]
            glow, area = Vector(), 0.0
            for x in comp:
                n = (nm @ x.normal).normalized()
                c = mw @ x.calc_center_median() + n * 0.002
                hit = tree.ray_cast(c, n, 100.0)
                if hit[0] is not None and hit[1].dot(n) > 0:
                    continue  # the face lies inside the solid it is set in
                t = n.orthogonal().normalized()
                b = n.cross(t)
                open_ = sum(1 for dx, dy, dz in hemi if tree.ray_cast(c, t * dx + b * dy + n * dz, 1.0)[0] is None)
                weight = x.calc_area() * open_ / len(hemi)
                glow += n * weight
                area += weight
            fixtures.append((aabb(pts), glow, area))
        bm.free()
        for (lo, hi), glow, area in sorted(fixtures, key=lambda f: f[0]):
            c = (Vector(lo) + Vector(hi)) / 2
            rec = {"name": f"{g}_{len([x for x in out if x['group'] == g]):02d}", "group": g}
            rec["position"] = gp(c, 3)
            rec["size"] = vec([abs(v) for v in game(Vector(hi) - Vector(lo))], 3)
            if area > 0 and glow.length / area >= 0.3:
                rec["kind"] = "spot"
                rec["direction"] = gp(glow.normalized(), 3)
            else:
                rec["kind"] = "omni"
            rec["room"] = next((r["id"] for r in room_list if all(r["min"][i] - 0.3 <= game(c)[i] <= r["max"][i] + 0.3
                                                                  for i in range(3))), "outside")
            rec["side"] = side(game(c)[0])
            if g in moving:
                rec["part"] = g
            if g.startswith("Engine_"):
                rec["use"] = "exhaust"
            out.append(rec)
    for r in out:
        del r["group"]
    return out


# ------------------------------------------------------------------------------------------------ the file


def build(asset, moving, loaders, materials):
    ship = Ship(asset)
    ucx = list(bpy.data.collections["03_CONVEX_COLLISION"].objects)
    log("data: clearance sweeps")
    clear = clearance(ship, moving)
    recv = receivers(ship)
    room_list = rooms(ship)
    allpts = []
    for g in ship.groups:
        if asset.lods[g][0] is not None:
            allpts += world_points(asset.lods[g][0])
    lo, hi = aabb(allpts)
    sides = {"_P": "starboard", "_S": "port", "Port": "starboard", "Starboard": "port",
             "HandGrip_L": "right hand", "HandGrip_R": "left hand"}
    data = {
        "format": 1,
        "ship": "iron-wake",
        "model": "res://assets/ships/iron-wake.glb",
        "axes": "game axes: +Z the nose, +Y up, +X the ship's left (port); metres from the model's origin; parked pose",
        "source": {"asset": "IronWake_Buildable_R01", "blend": "IronWake_Buildable_R01.blend",
                   "sha256": sha256(asset.blend), "manifest": "Documentation/asset_manifest.json",
                   "manifestSha256": sha256(asset.source / "Documentation" / "asset_manifest.json"),
                   "import": "art/import/iron_wake.py"},
        "assetSides": sides,
        "bounds": box(lo, hi, 3),
        "centreOfMass": gp(ship.sockets["CentreOfMassHint"]["position"], 3),
        "materials": materials,
        "parts": parts(ship, moving, loaders, clear),
        "sockets": sockets(ship, moving),
        "stations": stations(ship, moving, clear, recv),
        "rooms": room_list,
        "boarding": boarding(ship, ucx, moving, room_list),
        "buildZones": build_zones(ship),
        "exclusions": exclusions(ship, ucx),
        "routes": routes(ship),
        "hull": hull(ship, moving),
        "lights": lights(ship, moving, room_list),
        "pieces": pieces(ship, ucx, moving),
    }
    return data

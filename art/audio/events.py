"""The ground game's sound events (GROUND.md §4.9) and the recipe of every file they play.

`everything()` gives (outs, events): the files build.py renders and the catalogue entries it writes into
game/data/ground/sounds.json. A catalogue entry's playback fields (volume, pitch, attenuation, bus, priority...)
are only defaults here: build.py keeps whatever sounds.json already says for them.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import audiolib  # noqa: E402
import sources  # noqa: E402
from build import Out, Seg  # noqa: E402
from sources import bsb, pack  # noqa: E402

OUTS = []
EVENTS = []

# Playback defaults per bus: unitSize and maxDistance in metres, priority 0-100 (higher wins a voice), the most
# voices of one event at once, random pitch range and volume jitter (± dB).
BUS_DEFAULTS = {
    "Footsteps": dict(unitSize=2.0, maxDistance=40.0, priority=30, limit=8, pitch=[0.94, 1.06], jitter=1.5),
    "SFX": dict(unitSize=4.0, maxDistance=60.0, priority=50, limit=6, pitch=[0.96, 1.04], jitter=1.0),
    "UI": dict(space="2d", priority=90, limit=3, pitch=[0.98, 1.02], jitter=0.5, occlude=False),
    "Ambience": dict(space="2d", priority=20, limit=2, pitch=[1.0, 1.0], jitter=0.0, occlude=False),
}


def res(rel):
    return "res://assets/audio/" + rel


def event(key, outs, bus, volume=0.0, **fields):
    """A catalogue entry playing `outs` (its variants)."""
    for o in outs:
        OUTS.append(o)
    e = {"key": key, "category": outs[0].category if outs else fields.pop("category"), "bus": bus,
         "volume": volume}
    e.update(BUS_DEFAULTS[bus])
    e.update(fields)
    if any(o.loop for o in outs):
        e["loop"] = True
    e["files"] = [res(o.rel) for o in outs]
    EVENTS.append(e)
    return e


def layered(key, layers, bus="SFX", volume=0.0, **fields):
    """A distance-layered event: layers = [(name, outs, from, to, fade, volume, unitSize, maxDistance)]."""
    e = {"key": key, "category": layers[0][1][0].category, "bus": bus, "volume": volume}
    e.update(BUS_DEFAULTS[bus])
    e.update(fields)
    e["layers"] = []
    for name, outs, start, end, fade, vol, unit, maxd in layers:
        for o in outs:
            OUTS.append(o)
        e["layers"].append({"name": name, "from": start, "to": end, "fade": fade, "volume": vol, "unitSize": unit,
                            "maxDistance": maxd, "category": outs[0].category,
                            "files": [res(o.rel) for o in outs]})
    EVENTS.append(e)
    return e


# ---------------------------------------------------------------- picking parts of recordings

def pick(events, n, min_len=0.05, max_len=10.0, within_db=9.0):
    """n events of steady level from a list: the loudest ones within `within_db` of the loudest, spread in time."""
    ok = [e for e in events if min_len <= e.length]
    if len(ok) < n:
        raise SystemExit("only %d usable events of %d wanted" % (len(ok), n))
    peaks = sorted(e.peak for e in ok)
    ref = peaks[int(0.85 * (len(peaks) - 1))]
    # Steady level: not much quieter than the strong ones, and no outliers (bumps, knocks) above them.
    good = [e for e in ok if ref - within_db <= e.peak <= ref + 3.0]
    if len(good) < n:
        good = sorted(ok, key=lambda e: abs(e.peak - ref))[:n]
    good.sort(key=lambda e: e.start)
    if len(good) <= n:
        chosen = good
    else:
        step = len(good) / float(n)
        chosen = [good[int(i * step + step / 2)] for i in range(n)]
    for e in chosen:
        e.end = min(e.end, e.start + max_len)
    return chosen


def steps(src, n, min_sep=0.28, start=0.0, duration=None, min_len=0.08, max_len=0.6, within_db=8.0, **kw):
    return pick(audiolib.find_steps(src, start, duration, min_sep=min_sep, max_len=max_len, **kw), n, min_len,
                max_len, within_db)


def hits(src, n, min_gap=0.25, max_len=1.0, within_db=9.0, min_len=0.03, **kw):
    return pick(audiolib.find_events(src, min_gap=min_gap, max_len=max_len, **kw), n, min_len, max_len, within_db)


def seg_of(src, ev, extra=0.06, **kw):
    """A part covering an event, with a little room after it for its tail."""
    return Seg(src, ev.start, ev.length + extra, **kw)


def numbered(prefix, i):
    return "%s_%02d.ogg" % (prefix, i + 1)


# ---------------------------------------------------------------- footsteps

STEP_FX = "highpass=f=70:poles=2,lowpass=f=15000"
SOFT_FX = "highpass=f=90:poles=2,lowpass=f=4200,acompressor=threshold=-24dB:ratio=2:attack=8:release=80"
THUMP = "lowpass=f=220:poles=2,highpass=f=50:poles=2"


def kenney(name):
    return pack("kenney-impact-sounds", "Audio/" + name)


def fantozzi(name):
    return pack("oga-fantozzi-footsteps", "Fantozzi-footsteps/flac/Fantozzi-%s.flac" % name)


def corsica(kind, i):
    return pack("oga-corsica-snow-gravel", "Corsica_S-Walking_in_Snow/Corsica_S-Walking_on_%s_%02d.flac" % (kind, i))


def footstep_set(surface, walk, run, crouch_from, land):
    """walk/run: lists of Seg lists (one per variant); crouch_from: Seg lists to soften; land: Seg lists."""
    base = "footstep/%s/" % surface
    sets = {
        "walk": [Out(base + numbered("walk", i), "footstep", segs, fx=STEP_FX) for i, segs in enumerate(walk)],
        "run": [Out(base + numbered("run", i), "footstep", segs, fx=STEP_FX) for i, segs in enumerate(run)],
        "crouch": [Out(base + numbered("crouch", i), "footstep", segs, fx=SOFT_FX, fade_in=0.012)
                   for i, segs in enumerate(crouch_from)],
        "land": [Out(base + numbered("land", i), "footstep", segs, fx=STEP_FX) for i, segs in enumerate(land)],
    }
    volume = {"walk": 0.0, "run": 3.0, "crouch": -7.0, "land": 4.0}
    reach = {"walk": 30.0, "run": 45.0, "crouch": 14.0, "land": 45.0}
    for stance, outs in sets.items():
        event("footstep.%s.%s" % (stance, surface), outs, "Footsteps", volume[stance], maxDistance=reach[stance])


def thump(gain=-8.0, at=0.0, i=0):
    return Seg(kenney("impactSoft_heavy_%03d.ogg" % (i % 5)), gain=gain, fx=THUMP, at=at)


def footsteps():
    # Grass: slow steps through short grass for walking, a run through tall grass.
    g1, g2, grun = bsb("1253"), bsb("0854"), bsb("1843")
    walk_ev = [(g1, e) for e in steps(g1, 4, min_sep=0.6, max_len=0.55)] + \
              [(g2, e) for e in steps(g2, 3, min_sep=0.6, max_len=0.5)]
    run_ev = [(grun, e) for e in steps(grun, 7, min_sep=0.25, max_len=0.32)]
    soft = "highshelf=f=7000:g=-6"
    footstep_set(
        "grass",
        walk=[[seg_of(s, e, fx=soft)] for s, e in walk_ev[:6]],
        run=[[seg_of(s, e, fx=soft)] for s, e in run_ev[:6]],
        crouch_from=[[seg_of(s, e, pitch=0.97)] for s, e in walk_ev[1:5]],
        land=[[seg_of(grun, run_ev[k][1]), seg_of(grun, run_ev[k + 1][1], at=0.03, gain=-3), thump(-9, i=k)]
              for k in range(3)])

    # Dirt: a mountain path recorded in first person (BigSoundBank 3216); runs add a swish of motion.
    d = bsb("3216")
    dirt = steps(d, 9, min_sep=0.4, max_len=0.45)
    footstep_set(
        "dirt",
        walk=[[seg_of(d, e)] for e in dirt[:6]],
        run=[[seg_of(d, dirt[k], pitch=0.95), seg_of(grun, run_ev[k][1], gain=-10), thump(-12, i=k)]
             for k in range(6)],
        crouch_from=[[seg_of(d, e, pitch=0.98)] for e in dirt[2:6]],
        land=[[seg_of(d, dirt[k]), seg_of(d, dirt[k + 3], at=0.035, gain=-3), thump(-8, i=k)] for k in range(3)])

    # Sand: Fantozzi's sand steps.
    sand = [fantozzi("Sand%s%d" % (side, i)) for side in "LR" for i in (1, 2, 3)]
    footstep_set(
        "sand",
        walk=[[Seg(s)] for s in sand],
        run=[[Seg(s, pitch=0.93, gain=1), thump(-12, i=k)] for k, s in enumerate(sand)],
        crouch_from=[[Seg(s, pitch=1.02)] for s in sand[:4]],
        land=[[Seg(sand[k]), Seg(sand[k + 3], at=0.03, gain=-2), thump(-8, i=k)] for k in range(3)])

    # Rock: Fantozzi's stone steps; runs from fast steps on concrete.
    stone = [fantozzi("Stone%s%d" % (side, i)) for side in "LR" for i in (1, 2, 3)]
    c = bsb("1318")
    crun = steps(c, 6, min_sep=0.22, max_len=0.25)
    footstep_set(
        "rock",
        walk=[[Seg(s)] for s in stone],
        run=[[seg_of(c, e, extra=0.04)] for e in crun],
        crouch_from=[[Seg(s, pitch=1.02)] for s in stone[:4]],
        land=[[seg_of(c, crun[k], extra=0.04), Seg(stone[k], at=0.03, gain=-2), thump(-9, i=k)] for k in range(3)])

    # Snow: Corsica_S's steps on snow-covered gravel; runs from quick steps in snow.
    snow = [corsica("snow_covered_gravel", i) for i in (1, 3, 5, 7, 9, 11)]
    sq = bsb("3363")
    srun = steps(sq, 6, min_sep=0.25, max_len=0.35)
    footstep_set(
        "snow",
        walk=[[Seg(s)] for s in snow],
        run=[[seg_of(sq, e)] for e in srun],
        crouch_from=[[Seg(s, pitch=1.02)] for s in snow[:4]],
        land=[[seg_of(sq, srun[k]), Seg(snow[k], at=0.03, gain=-2), thump(-9, i=k)] for k in range(3)])

    # Wood (building floors): shoes on a wooden floor; runs from a second floor recording, heavier, and TinyWorlds'
    # wood steps; landings from jumps onto a wooden pontoon.
    w = bsb("0165")
    wood = steps(w, 6, min_sep=0.45, max_len=0.4)
    w2 = bsb("1516")
    wrun = steps(w2, 3, min_sep=0.45, max_len=0.4)
    footstep_set(
        "wood",
        walk=[[seg_of(w, e)] for e in wood],
        run=[[seg_of(w2, e, pitch=0.95), thump(-14, i=k)] for k, e in enumerate(wrun)] +
            [[Seg(pack("oga-tinyworlds-different-steps", "wood0%d.ogg" % i), pitch=0.97)] for i in (1, 2, 3)],
        crouch_from=[[seg_of(w, e, pitch=1.02)] for e in wood[:4]],
        land=[[Seg(bsb("1841"))], [Seg(bsb("1842"))], [seg_of(w, wood[0]), seg_of(w2, wrun[1], at=0.03), thump(-8)]])

    # Metal (sheet floors): our layering of a hard shoe step and a thin metal plate's ring.
    plate = [kenney("impactPlate_light_%03d.ogg" % i) for i in range(5)]
    ring = "highpass=f=180,lowpass=f=5200"
    footstep_set(
        "metal",
        walk=[[Seg(stone[k]), Seg(plate[k % 5], gain=-9, fx=ring, pitch=0.9)] for k in range(6)],
        run=[[seg_of(c, crun[k], extra=0.04), Seg(plate[k % 5], gain=-6, fx=ring, pitch=0.85)] for k in range(6)],
        crouch_from=[[Seg(stone[k]), Seg(plate[k], gain=-12, fx=ring, pitch=0.9)] for k in range(4)],
        land=[[seg_of(c, crun[k], extra=0.04), Seg(kenney("impactMetal_heavy_%03d.ogg" % k), gain=-8, fx=ring),
               thump(-9, i=k)] for k in range(3)])

    # Shallow water: shoes in water; runs add a small splash; landings are big splashes.
    wa = bsb("0690")
    water = steps(wa, 10, min_sep=0.6, max_len=0.75, within_db=7)
    small = [bsb(s) for s in ("1529", "1530", "1531", "1532", "1533", "1534")]
    footstep_set(
        "water",
        walk=[[seg_of(wa, e)] for e in water[:6]],
        run=[[seg_of(wa, water[k]), Seg(small[k], dur=0.45, gain=-8, fade_out=0.2)] for k in range(6)],
        crouch_from=[[seg_of(wa, e, pitch=1.02)] for e in water[6:10]],
        land=[[Seg(bsb(s), dur=1.2, fade_out=0.5)] for s in ("1519", "1520", "1521")])

    # Swimming: strokes of a board through water (BigSoundBank 1514).
    sw = bsb("1514")
    strokes = steps(sw, 4, min_sep=1.0, max_len=1.1, within_db=6)
    event("swim.stroke", [Out(numbered("footstep/swim/stroke", i), "footstep", [seg_of(sw, e, extra=0.15)],
                              fx="highpass=f=90,lowpass=f=12000") for i, e in enumerate(strokes)],
          "Footsteps", 0.0, maxDistance=30.0)


# ---------------------------------------------------------------- gathering

def rubber(name):
    return pack("oga-rubberduck-metal-wood", name) if not name.startswith("bfh1") else \
        pack("oga-rubberduck-breaking", name)


def rpg(name):
    return pack("kenney-rpg-audio", "Audio/" + name)


def gathering():
    # Chops: an axe into a log (BigSoundBank 0536, bright with body; 0705, a drier knock) with weight in the low mids.
    a1, a2 = bsb("0536"), bsb("0705")
    chops = [(a1, e) for e in hits(a1, 4, min_gap=1.0, max_len=0.6)] + \
            [(a2, e) for e in hits(a2, 3, min_gap=1.0, max_len=0.5)]
    body = "highpass=f=60,equalizer=f=170:t=q:w=1:g=4,equalizer=f=2600:t=q:w=1.5:g=-2"
    event("gather.wood.hit", [Out(numbered("gather/wood_hit", i), "gather", [seg_of(s, e, extra=0.25, fx=body)])
                              for i, (s, e) in enumerate(chops[:6])], "SFX", 0.0, maxDistance=70.0)

    # A tree coming down (our layering): a deep crack of splitting wood, the trunk creaking over, the crash.
    twig = bsb("1299")
    snaps = hits(twig, 4, min_gap=0.3, max_len=0.4)
    chair = bsb("0516")
    thuds = hits(chair, 6, min_gap=1.0, max_len=0.6)
    fall = []
    for k in range(3):
        fall.append(Out(numbered("gather/wood_break", k), "gather", [
            seg_of(twig, snaps[k], extra=0.1, pitch=0.5, gain=8),
            seg_of(twig, snaps[k + 1], extra=0.1, pitch=0.62, gain=4, at=0.18),
            Seg(rubber("wood_cracking_0%d.ogg" % (k + 1)), pitch=0.7, gain=4, at=0.35, fx="lowpass=f=5000"),
            Seg(rubber("wood_falling_0%d.ogg" % (k + 1)), pitch=0.75, gain=-4, at=1.3, fx="lowpass=f=6000"),
            seg_of(chair, thuds[k], extra=0.2, pitch=0.55, gain=-2, at=1.45, fx="lowpass=f=2500"),
            thump(-4, at=1.45, i=k),
        ], fx="highpass=f=40", tail_db=-50))
    event("gather.wood.break", fall, "SFX", 2.0, unitSize=8.0, maxDistance=120.0, priority=60, limit=2)

    # Stone: a pick into rock (Kenney's mining impacts) with grit from falling stones.
    mining = [kenney("impactMining_%03d.ogg" % i) for i in range(5)]
    stone_fall = bsb("1022")
    grit = hits(stone_fall, 8, min_gap=0.8, max_len=0.8, within_db=10)
    event("gather.stone.hit", [Out(numbered("gather/stone_hit", k), "gather", [
        Seg(mining[k % 5], pitch=(0.92, 1.0, 1.06, 0.96, 1.03, 0.9)[k]),
        seg_of(stone_fall, grit[k], extra=0.1, gain=-12, fx="highpass=f=400")], fx="highpass=f=50")
        for k in range(6)], "SFX", 0.0, maxDistance=70.0)
    # Ore: the same pick with the ring of metal in the rock.
    plate = [kenney("impactPlate_light_%03d.ogg" % i) for i in range(5)]
    event("gather.ore.hit", [Out(numbered("gather/ore_hit", k), "gather", [
        Seg(mining[(k + 2) % 5], pitch=(0.95, 1.02, 0.98, 1.05)[k]),
        Seg(plate[k], gain=-11, pitch=0.8, fx="highpass=f=600,lowpass=f=7000")], fx="highpass=f=50")
        for k in range(4)], "SFX", 0.0, maxDistance=70.0)

    # An outcrop breaking: to its broken stage, to its remnant, and gone.
    stages = []
    for stage in (1, 2, 3):
        outs = []
        for k in range(2):
            j = (stage - 1) * 2 + k
            segs = [Seg(mining[j % 5], pitch=0.72 - 0.06 * stage),
                    seg_of(stone_fall, grit[j], extra=0.3, gain=-2, at=0.04),
                    Seg(rubber("bfh1_rock_breaking_0%d.ogg" % (1 + j % 3)), gain=-4, at=0.02),
                    thump(-6, at=0.0, i=j)]
            if stage == 3:
                segs.append(Seg(rubber("bfh1_rock_falling_0%d.ogg" % (2 + k)), gain=-3, at=0.25))
                segs.append(seg_of(stone_fall, grit[(j + 3) % 8], extra=0.3, gain=-5, at=0.45))
            outs.append(Out(numbered("gather/node_break%d" % stage, k), "gather", segs, fx="highpass=f=40"))
        stages.append(outs)
        event("gather.node.break.%d" % stage, outs, "SFX", 1.0 + stage, unitSize=6.0, maxDistance=90.0, priority=55)

    # The weak spot: a distinct bright tick over the hit.
    event("gather.weakspot", [Out(numbered("gather/weakspot", k), "foley", [
        Seg(plate[k + 1], pitch=1.7, fx="highpass=f=1800"),
        Seg(mining[k], pitch=1.4, gain=-14, fx="highpass=f=2500")], length=0.35, max_fade=0.2)
        for k in range(3)], "SFX", -3.0, maxDistance=40.0, priority=60, pitch=[0.98, 1.02])

    # Picking berries: leaves rustling and a small snap; plants: grass pulled, roots coming out of soil.
    leaves = bsb("2889")
    rustle = steps(leaves, 6, min_sep=0.5, max_len=0.45)
    event("gather.berry", [Out(numbered("gather/berry", k), "foley", [
        seg_of(leaves, rustle[k], fade_in=0.02, fx="highpass=f=200,lowpass=f=8000,highshelf=f=5000:g=-6,"
                                                   "acompressor=threshold=-30dB:ratio=3:attack=5:release=80:makeup=6"),
        seg_of(twig, snaps[k % 4], extra=0.05, pitch=1.6, gain=-10, at=0.12)]) for k in range(4)],
        "SFX", 0.0, maxDistance=25.0)
    grass, mud = bsb("1253"), bsb("0495")
    pulls = steps(grass, 8, min_sep=0.6, max_len=0.5)
    squelch = steps(mud, 4, min_sep=0.5, max_len=0.35)
    event("gather.plant", [Out(numbered("gather/plant", k), "foley", [
        seg_of(grass, pulls[k + 2], pitch=1.1, fx="highpass=f=250,highshelf=f=7000:g=-5"),
        seg_of(mud, squelch[k], pitch=1.2, gain=-8, at=0.15, fx="highpass=f=200")]) for k in range(4)],
        "SFX", 0.0, maxDistance=25.0)

    # Picking an item up: cloth and leather handled.
    event("gather.pickup", [Out(numbered("gather/pickup", k), "foley", [Seg(rpg(n))], fx="highpass=f=120")
                            for k, n in enumerate(("cloth1.ogg", "cloth3.ogg", "handleSmallLeather.ogg",
                                                   "handleSmallLeather2.ogg"))],
          "SFX", -2.0, maxDistance=20.0)


# ---------------------------------------------------------------- survival

def grunt(name):
    return pack("oga-haeldb-male-grunts", "yelling sounds/" + name)


def survival():
    bites = [bsb(s) for s in ("1113", "1114", "3081", "1594")]
    eats = []
    for k, b in enumerate(bites):
        ev = hits(b, 2, min_gap=0.8, max_len=1.2, within_db=10)
        eats.append(Out(numbered("survival/eat", k), "foley", [Seg(b, ev[0].start, 1.3, fade_out=0.3)],
                        fx="highpass=f=100,lowpass=f=12000", length=1.3))
    event("survival.eat", eats, "SFX", 0.0, maxDistance=15.0, priority=60)

    drinks = []
    for k, s in enumerate(("3247", "3248", "3249")):
        g = bsb(s)
        ev = hits(g, 1, min_gap=1.0, max_len=2.0, within_db=12)
        drinks.append(Out(numbered("survival/drink", k), "foley", [Seg(g, max(0.0, ev[0].start - 0.05), 1.6,
                                                                       fade_out=0.4)],
                          fx="highpass=f=90,lowpass=f=12000", length=1.6))
    event("survival.drink", drinks, "SFX", 0.0, maxDistance=15.0, priority=60)

    tape = [bsb(s) for s in ("0297", "0298", "0299")]
    cloth = bsb("0013")
    wraps = []
    for k, t in enumerate(tape):
        ev = hits(t, 1, min_gap=0.5, max_len=1.2, within_db=12)
        wraps.append(Out(numbered("survival/bandage", k), "foley", [
            Seg(t, ev[0].start, 1.0, fade_out=0.25),
            Seg(cloth, 1.0 + 2.5 * k, 1.1, gain=-6, at=0.35, fade_in=0.05, fade_out=0.3)],
            fx="highpass=f=120,lowpass=f=12000", length=1.5))
    event("survival.bandage", wraps, "SFX", 0.0, maxDistance=15.0, priority=60)

    # One voice for the player (HaelDB's third vocalist): short grunts when hurt; a cry and the body falling at death.
    event("survival.hurt", [Out(numbered("survival/hurt", k), "voice", [Seg(grunt("3grunt%d.wav" % (k + 1)))],
                                fx="highpass=f=90,lowpass=f=11000", length=0.7, max_fade=0.25) for k in range(6)],
          "SFX", 0.0, maxDistance=35.0, priority=70, limit=2, pitch=[0.97, 1.03])
    falls = [bsb(s) for s in ("2452", "2453", "2454")]
    event("survival.death", [Out(numbered("survival/death", k), "voice", [
        Seg(grunt("3yell%d.wav" % (k + 3)), fx="highpass=f=90,lowpass=f=11000"),
        Seg(falls[k], gain=-4, at=0.55, fx="highpass=f=50")]) for k in range(3)],
        "SFX", 2.0, maxDistance=45.0, priority=80, limit=2, pitch=[0.98, 1.02])

    # Cold: shivering breaths out (a man breathing; our tremolo for the shiver).
    br = bsb("2195")
    breaths = steps(br, 4, min_sep=1.5, max_len=1.4, within_db=8, smooth_ms=120)
    event("survival.cold", [Out(numbered("survival/cold", k), "voice", [
        seg_of(br, e, extra=0.2, fx="tremolo=f=9:d=0.35,highpass=f=150,lowpass=f=9000", fade_in=0.08)])
        for k, e in enumerate(breaths)], "SFX", -4.0, maxDistance=10.0, priority=40, limit=1)


# ---------------------------------------------------------------- deployables

def loop(rel, category, src, start, length, overlap=2.0, fx="", channels=1, gain=0.0, quality=5):
    """A seamless loop of `length` seconds taken from `src` at `start`."""
    return Out(rel, category, [Seg(src, start, length + overlap, gain=gain)], fx=fx, channels=channels,
               loop=(length, overlap), fade_in=0.0, quality=quality)


def deployables():
    event("campfire.loop", [loop("deploy/campfire_loop.ogg", "loop", bsb("2857"), 4.0, 24.0,
                                 fx="highpass=f=60,lowpass=f=14000")],
          "SFX", 0.0, unitSize=3.0, maxDistance=35.0, priority=40, limit=8)
    event("furnace.loop", [loop("deploy/furnace_loop.ogg", "loop", bsb("1127"), 6.0, 20.0,
                                fx="highpass=f=45,equalizer=f=120:t=q:w=1:g=3,lowpass=f=9000")],
          "SFX", 0.0, unitSize=3.0, maxDistance=40.0, priority=40, limit=8)

    match, match2, gas = bsb("0004"), bsb("1271"), bsb("1340")
    strikes = hits(match, 2, min_gap=2.0, max_len=1.0, within_db=10) + hits(match2, 1, min_gap=2.0, max_len=1.0)
    whoosh = hits(gas, 2, min_gap=1.0, max_len=2.0, within_db=12)
    event("campfire.ignite", [Out(numbered("deploy/campfire_ignite", k), "foley", [
        seg_of(match if k < 2 else match2, strikes[k], extra=0.3),
        Seg(gas, whoosh[k % 2].start, 1.6, gain=-8, at=0.25, fade_out=0.6, fx="lowpass=f=6000")])
        for k in range(2)], "SFX", 0.0, maxDistance=30.0)
    big = [bsb("1341"), bsb("1342")]
    roars = [hits(b, 1, min_gap=1.0, max_len=2.5, within_db=12)[0] for b in big]
    event("furnace.ignite", [Out(numbered("deploy/furnace_ignite", k), "foley", [
        Seg(big[k], roars[k].start, 2.0, fade_out=0.8, fx="lowpass=f=8000"),
        Seg(kenney("impactMetal_light_00%d.ogg" % k), gain=-10, fx="highpass=f=200")]) for k in range(2)],
        "SFX", 0.0, maxDistance=35.0)
    event("fire.extinguish", [Out(numbered("deploy/extinguish", k), "foley", [Seg(bsb(s))],
                                  fx="highpass=f=150,lowpass=f=12000")
                              for k, s in enumerate(("1694", "1695", "1696"))], "SFX", -2.0, maxDistance=25.0)

    # Boxes: a wooden lid creaking open, shut with a knock.
    event("box.open", [Out(numbered("deploy/box_open", k), "foley", [Seg(rpg(n))], fx="highpass=f=80")
                       for k, n in enumerate(("creak1.ogg", "creak2.ogg", "doorOpen_1.ogg"))],
          "SFX", 0.0, maxDistance=20.0)
    event("box.close", [Out(numbered("deploy/box_close", k), "foley", [Seg(rpg(n))], fx="highpass=f=60")
                        for k, n in enumerate(("doorClose_1.ogg", "doorClose_2.ogg", "doorClose_4.ogg"))],
          "SFX", 0.0, maxDistance=20.0)
    chair = bsb("0516")
    thuds = hits(chair, 6, min_gap=1.0, max_len=0.6)
    event("deploy.place", [Out(numbered("deploy/place", k), "foley", [seg_of(chair, e, extra=0.2, pitch=0.85),
                                                                      thump(-10, i=k)], fx="highpass=f=45")
                           for k, e in enumerate(thuds[:4])], "SFX", 0.0, maxDistance=30.0)


# ---------------------------------------------------------------- building

def building():
    twig, chair, stone_fall = bsb("1299"), bsb("0516"), bsb("1022")
    snaps = hits(twig, 6, min_gap=0.3, max_len=0.4, within_db=12)
    thuds = hits(chair, 6, min_gap=1.0, max_len=0.6)
    grit = hits(stone_fall, 8, min_gap=0.8, max_len=0.8, within_db=10)
    mining = [kenney("impactMining_%03d.ogg" % i) for i in range(5)]
    sheets = [bsb(s) for s in ("1771", "1772", "1773", "1774", "1775")]
    two_sheets = [bsb(s) for s in ("1776", "1777", "1778")]
    bars = [bsb("0234"), bsb("0235")]
    anvil = [bsb("3589"), bsb("3590")]
    anvil_hits = [hits(a, 3, min_gap=0.6, max_len=1.5, within_db=8) for a in anvil]
    hammer = [bsb("0005"), bsb("0006")]
    nails = [hits(h, 4, min_gap=0.25, max_len=0.5, within_db=8) for h in hammer]
    wood_hit = [kenney("impactWood_heavy_%03d.ogg" % i) for i in range(5)]
    plank = [kenney("impactPlank_medium_%03d.ogg" % i) for i in range(5)]

    place = {
        "twig": [[seg_of(twig, snaps[k], extra=0.1), seg_of(twig, snaps[k + 3], extra=0.1, at=0.05, gain=-4),
                  Seg(kenney("impactWood_light_%03d.ogg" % k), gain=-6)] for k in range(3)],
        "wood": [[Seg(wood_hit[k]), Seg(plank[k], gain=-4, at=0.02), seg_of(chair, thuds[k], extra=0.2, gain=-6)]
                 for k in range(3)],
        "stone": [[Seg(mining[k], pitch=0.7), seg_of(stone_fall, grit[k], extra=0.3, gain=-3), thump(-6, i=k)]
                  for k in range(3)],
        "metal": [[Seg(sheets[k], fx="highpass=f=80"), thump(-8, i=k)] for k in range(3)],
        "armoured": [[Seg(bars[k % 2], pitch=0.8), seg_of(anvil[k % 2], anvil_hits[k % 2][k], extra=0.4, pitch=0.7,
                                                         gain=-8), thump(-4, i=k)] for k in range(3)],
    }
    for grade, variants in place.items():
        event("build.place.%s" % grade, [Out(numbered("build/place_%s" % grade, k), "gather", segs,
                                             fx="highpass=f=40") for k, segs in enumerate(variants)],
              "SFX", 0.0, maxDistance=60.0)

    upgrade = {
        "wood": [[seg_of(hammer[k % 2], nails[k % 2][0], extra=0.1), seg_of(hammer[k % 2], nails[k % 2][1],
                                                                           extra=0.1, at=0.28),
                  Seg(plank[k + 1], gain=-3, at=0.5)] for k in range(3)],
        "stone": [[Seg(mining[k], pitch=0.8), Seg(mining[k + 1], pitch=0.75, at=0.3),
                   seg_of(stone_fall, grit[k + 3], extra=0.3, gain=-4, at=0.45)] for k in range(3)],
        "metal": [[seg_of(anvil[k % 2], anvil_hits[k % 2][k], extra=0.4), Seg(two_sheets[k], gain=-4, at=0.25,
                                                                               fx="highpass=f=80")]
                  for k in range(3)],
        "armoured": [[seg_of(anvil[k % 2], anvil_hits[k % 2][(k + 1) % 3], extra=0.5, pitch=0.75),
                      Seg(bars[k % 2], pitch=0.7, gain=-2, at=0.2), thump(-3, at=0.2, i=k)] for k in range(3)],
    }
    for grade, variants in upgrade.items():
        event("build.upgrade.%s" % grade, [Out(numbered("build/upgrade_%s" % grade, k), "gather", segs,
                                               fx="highpass=f=40") for k, segs in enumerate(variants)],
              "SFX", 0.0, maxDistance=60.0)

    event("build.repair", [Out(numbered("build/repair", k), "gather", [
        Seg(rubber(n)), seg_of(hammer[k % 2], nails[k % 2][k + 1], extra=0.15, gain=-6)], fx="highpass=f=60")
        for k, n in enumerate(("wood_hammer_01.ogg", "wood_hammer_02.ogg", "hammer_01.ogg"))],
        "SFX", -2.0, maxDistance=50.0)
    event("build.demolish", [Out(numbered("build/demolish", k), "gather", [
        Seg(rubber("wood_breaking_0%d.ogg" % (k % 2 + 1))),
        Seg(rubber("bfh1_wood_breaking_0%d.ogg" % (k + 1)), gain=-3, at=0.1),
        seg_of(chair, thuds[k + 2], extra=0.2, pitch=0.7, gain=-2, at=0.3), thump(-4, at=0.3, i=k)],
        fx="highpass=f=40") for k in range(3)], "SFX", 2.0, maxDistance=80.0, priority=60)
    event("build.collapse", [Out(numbered("build/collapse", k), "explosion", [
        Seg(rubber("wood_falling_0%d.ogg" % (k + 3)), pitch=0.8),
        Seg(rubber("bfh1_rock_falling_0%d.ogg" % (k + 4)), gain=-2, at=0.15),
        Seg(two_sheets[k], pitch=0.7, gain=-4, at=0.3, fx="lowpass=f=5000"),
        seg_of(stone_fall, grit[k + 4], extra=0.4, gain=-3, at=0.5),
        seg_of(chair, thuds[k], extra=0.2, pitch=0.5, at=0.2, fx="lowpass=f=1500"),
        thump(0, at=0.2, i=k), thump(-3, at=0.65, i=k + 1)], fx="highpass=f=35", tail_db=-50)
        for k in range(3)], "SFX", 0.0, unitSize=10.0, maxDistance=200.0, priority=75, limit=3)

    # Doors by material: open and close.
    creaks = [bsb(s) for s in ("3205", "3211", "0302")]
    closes = [bsb(s) for s in ("2431", "2432", "1704")]
    door = {
        "wood": ([[Seg(c, 0.0, 2.2, fade_out=0.5, fx="highpass=f=70")] for c in creaks],
                 [[Seg(c, 0.0, 1.6, fade_out=0.4, fx="highpass=f=50")] for c in closes]),
        # Sheet metal: a latch, the hinge's grind, and a clanging slam shut.
        "metal": ([[Seg(rubber("metal_open_01.ogg"), pitch=p, fx="highpass=f=80"),
                    Seg(bsb("0616"), 1.0 + 4 * k, 1.2, gain=-8, fade_in=0.1, fade_out=0.4, fx="highpass=f=200")]
                   for k, p in enumerate((1.0, 0.94))],
                  [[Seg(rubber("metal_close_01.ogg"), pitch=p, fx="highpass=f=60"),
                    Seg(rubber("metal_slam_01.ogg"), pitch=p * 0.9, gain=-3, at=0.05, fx="highpass=f=60"),
                    thump(-10, at=0.05, i=k)] for k, p in enumerate((1.0, 0.93))]),
        # Armoured: the same, heavier and lower, with a bolt's thud.
        "armoured": ([[Seg(rubber("metal_open_01.ogg"), pitch=p, fx="highpass=f=60"),
                       Seg(bars[k % 2], pitch=0.6, gain=-12, at=0.05)] for k, p in enumerate((0.72, 0.68))],
                     [[Seg(rubber("metal_slam_01.ogg"), pitch=p, fx="highpass=f=45"),
                       Seg(bars[k % 2], pitch=0.55, gain=-6, at=0.02), thump(-4, i=k)]
                      for k, p in enumerate((0.7, 0.65))]),
        "garage": ([[Seg(bsb("0683"), 0.5, 3.0, fade_in=0.1, fade_out=0.8, fx="highpass=f=60")],
                    [Seg(bsb("0513"), 1.0, 3.0, fade_in=0.1, fade_out=0.8, fx="highpass=f=60")]],
                   [[Seg(bsb("0601"), 0.0, 3.0, fade_out=0.6, fx="highpass=f=50")],
                    [Seg(bsb("0683"), 6.0, 3.0, fade_in=0.1, fade_out=0.6, fx="highpass=f=50")]]),
    }
    for material, (opens, shuts) in door.items():
        event("door.open.%s" % material, [Out(numbered("build/door_%s_open" % material, k), "foley", segs)
                                          for k, segs in enumerate(opens)], "SFX", 0.0, maxDistance=45.0)
        event("door.close.%s" % material, [Out(numbered("build/door_%s_close" % material, k), "foley", segs)
                                           for k, segs in enumerate(shuts)], "SFX", 0.0, maxDistance=50.0)

    # A key lock turning (BigSoundBank's lock recordings).
    event("lock.key", [Out(numbered("build/key_lock", k), "click", [Seg(bsb(s), 0.0, 1.2, fade_out=0.3)],
                           fx="highpass=f=200") for k, s in enumerate(("0191", "0037"))],
          "SFX", 0.0, maxDistance=15.0)

    # The code lock: a key press, accepted (two rising tones), refused (a low double buzz).
    keys = bsb("0758")
    presses = hits(keys, 4, min_gap=0.25, max_len=0.25, within_db=6)
    event("codelock.beep", [Out(numbered("build/codelock_beep", k), "click", [seg_of(keys, e, extra=0.02,
                                                                                    pitch=(1.0, 1.06, 0.95)[k])],
                                fx="highpass=f=300", length=0.22) for k, e in enumerate(presses[:3])],
          "SFX", -4.0, maxDistance=12.0, priority=60, pitch=[1.0, 1.0], jitter=0.0)
    # Accepted and refused are the lock's own piezo tones (made in code: a beep is electronic anyway).
    synth = __import__("synth")
    accept = synth.tones("lock_accept", [(1760.0, 0.0, 0.09), (2349.0, 0.12, 0.16)], 0.32)
    deny = synth.tones("lock_deny", [(392.0, 0.0, 0.14), (392.0, 0.2, 0.22)], 0.46, square=1.0)
    event("codelock.accept", [Out("build/codelock_accept.ogg", "foley", [Seg(accept)], fx="highpass=f=300")],
          "SFX", -2.0, maxDistance=15.0, priority=65, pitch=[1.0, 1.0], jitter=0.0)
    event("codelock.deny", [Out("build/codelock_deny.ogg", "foley", [Seg(deny)], fx="highpass=f=150,lowpass=f=6000")],
          "SFX", -4.0, maxDistance=15.0, priority=65, pitch=[1.0, 1.0], jitter=0.0)


# ---------------------------------------------------------------- weapons

GUNS = "oga-free-firearm-sound-library"


def gun(name):
    return pack(GUNS, "Prepared SFX Library/" + name)


def shots(path, n, length, gap=0.5):
    ev = audiolib.find_events(path, min_gap=gap, max_len=3.0, end_db=40)
    top = max(e.peak for e in ev)
    main = [e for e in ev if e.peak >= top - 6]
    return [(e.start, length) for e in main[:n]]


NEAR_FX = "highpass=f=45,equalizer=f=95:t=q:w=1:g=2.5"
MID_FX = "highpass=f=60,lowpass=f=9000"


def gunshots(kind, near, mid, ir):
    """near and mid: [(path, start, length)]; far is the mid recordings sent through distance and terrain."""
    near_outs = [Out(numbered("weapon/%s_near" % kind, k), "gunshot", [Seg(p, max(0.0, s - 0.01), l)], fx=NEAR_FX,
                     tail_db=-55, max_fade=0.4) for k, (p, s, l) in enumerate(near)]
    mid_outs = [Out(numbered("weapon/%s_mid" % kind, k), "distant", [Seg(p, max(0.0, s - 0.01), l)], fx=MID_FX,
                    tail_db=-55, max_fade=0.6) for k, (p, s, l) in enumerate(mid)]
    far_outs = [Out(numbered("weapon/%s_far" % kind, k), "distant", [
        Seg(p, max(0.0, s - 0.01), l, fx="lowpass=f=1300,highpass=f=50,afade=t=in:d=0.008,"
                                          "acompressor=threshold=-38dB:ratio=5:attack=0.5:release=500:makeup=12",
            ir=ir[k % 2], wet=0.3, dry=1.0, post="lowpass=f=1600")], tail_db=-55, max_fade=1.0)
        for k, (p, s, l) in enumerate(mid[:3])]
    # Rust's guns carry far: near to 60 m, then the mid recording, then a dull far report with its echoes.
    layered("%s.shot" % kind, [
        ("near", near_outs, 0.0, 60.0, 30.0, 0.0, 12.0, 160.0),
        ("mid", mid_outs, 60.0, 350.0, 80.0, -3.0, 40.0, 600.0),
        ("far", far_outs, 350.0, 1500.0, 150.0, -6.0, 120.0, 1600.0),
    ], "SFX", 0.0, priority=85, limit=10, delay=True, pitch=[0.97, 1.03], jitter=1.0)
    # Silenced: the report muffled to a thud with the action's clack; it carries a short way only.
    slide = bsb("1985")
    quiet = [Out(numbered("weapon/%s_silenced" % kind, k), "impact", [
        Seg(p, max(0.0, s - 0.01), 0.5, fx="lowpass=f=1500,highpass=f=90,acompressor=threshold=-20dB:ratio=4:"
                                           "attack=2:release=60", fade_out=0.25),
        Seg(slide, 0.25, 0.14, gain=-4, at=0.04, fx="highpass=f=800")], tail_db=-50)
        for k, (p, s, l) in enumerate(near[:3])]
    layered("%s.shot.silenced" % kind, [
        ("near", quiet, 0.0, 40.0, 20.0, 0.0, 4.0, 70.0),
    ], "SFX", 0.0, priority=80, limit=8, delay=True, pitch=[0.97, 1.03], jitter=1.0)


def weapons():
    whoosh = [bsb(s) for s in ("1795", "1796", "1797", "1798", "1799", "1800")]
    event("melee.swing", [Out(numbered("weapon/swing", k), "foley", [Seg(w, fx="highpass=f=200")]) for k, w in
                          enumerate(whoosh[:4])], "SFX", -2.0, maxDistance=20.0)
    event("melee.swing.heavy", [Out(numbered("weapon/swing_heavy", k), "foley", [Seg(w, pitch=0.78,
                                                                                     fx="highpass=f=120")])
                                for k, w in enumerate(whoosh[2:6])], "SFX", 0.0, maxDistance=25.0)
    punches = [bsb(s) for s in ("2459", "2460", "2461", "2462", "2463", "2464")]
    body = [kenney("impactPunch_%s_%03d.ogg" % ("heavy" if k % 2 else "medium", k % 5)) for k in range(6)]
    event("melee.hit.flesh", [Out(numbered("weapon/hit_flesh", k), "impact", [
        Seg(p, fx="highpass=f=90"), Seg(body[k], gain=-3, fx="highpass=f=70,lowpass=f=4000")])
        for k, p in enumerate(punches)], "SFX", 0.0, maxDistance=35.0, priority=65)
    chair = bsb("0516")
    thuds = hits(chair, 6, min_gap=1.0, max_len=0.6)
    event("melee.hit.hard", [Out(numbered("weapon/hit_hard", k), "impact", [
        Seg(kenney("impactWood_medium_%03d.ogg" % k)), seg_of(chair, thuds[k], extra=0.15, gain=-6)],
        fx="highpass=f=60") for k in range(4)], "SFX", 0.0, maxDistance=45.0)

    rope = bsb("0554")
    creak = steps(rope, 4, min_sep=1.0, max_len=1.0, smooth_ms=80)
    event("bow.draw", [Out(numbered("weapon/bow_draw", k), "foley", [
        seg_of(rope, creak[k], extra=0.1, fx="highpass=f=250", fade_in=0.08),
        Seg(rpg("creak%d.ogg" % (k + 1)), gain=-8, pitch=1.2, fx="highpass=f=400")], fade_in=0.05)
        for k in range(2)], "SFX", -2.0, maxDistance=15.0)
    arch = bsb("0549")
    twangs = hits(arch, 2, min_gap=0.3, max_len=0.5, within_db=6)
    event("bow.release", [Out(numbered("weapon/bow_release", k), "foley", [seg_of(arch, e, extra=0.15)],
                              fx="highpass=f=120") for k, e in enumerate(twangs)], "SFX", 0.0, maxDistance=30.0)
    event("arrow.flight", [Out(numbered("weapon/arrow_flight", k), "foley", [Seg(w, pitch=1.6, fx="highpass=f=900")])
                           for k, w in enumerate(whoosh[:3])], "SFX", -6.0, maxDistance=20.0, priority=40)
    target = bsb("0548")
    thunks = hits(target, 6, min_gap=0.4, max_len=0.6, within_db=10)
    event("arrow.impact", [Out(numbered("weapon/arrow_impact", k), "impact", [
        seg_of(target, thunks[k], extra=0.15), Seg(kenney("impactWood_light_%03d.ogg" % k), gain=-8)],
        fx="highpass=f=80") for k in range(4)], "SFX", 0.0, maxDistance=35.0)
    event("arrow.impact.flesh", [Out(numbered("weapon/arrow_flesh", k), "impact", [
        seg_of(target, thunks[k + 2], extra=0.1, gain=-4), Seg(punches[k], gain=-2, pitch=1.15)],
        fx="highpass=f=100") for k in range(3)], "SFX", 0.0, maxDistance=35.0, priority=65)

    synth = __import__("synth")
    ir = [synth.outdoor_ir("ir_outdoor_a", seed=1),
          synth.outdoor_ir("ir_outdoor_b", seed=5, t60=2.2, reflections=((0.13, 0.4), (0.29, 0.28), (0.52, 0.18),
                                                                         (0.81, 0.12), (1.24, 0.08)))]
    ak_near, ak_mid = gun("AK-47/C_28P.wav"), gun("AK-47/C_31P.wav")
    sks_near, sks_mid = gun("SKS/U_14P.wav"), gun("SKS/U_19P.wav")
    rifle_near = [(ak_near, s, 1.8) for s, _ in shots(ak_near, 4, 1.8)] + \
                 [(sks_near, s, 1.8) for s, _ in shots(sks_near, 2, 1.8)]
    rifle_mid = [(ak_mid, s, 2.6) for s, _ in shots(ak_mid, 2, 2.6)] + \
                [(sks_mid, s, 2.6) for s, _ in shots(sks_mid, 2, 2.6)]
    gunshots("rifle", rifle_near, rifle_mid, ir)
    ppq_near, ppq_mid = gun("Walther PPQ/X_39P.wav"), gun("Walther PPQ/X_31P.wav")
    m1911_near, m1911_mid = gun("1911/A_42P.wav"), gun("1911/A_34P.wav")
    pistol_near = [(ppq_near, s, 1.4) for s, _ in shots(ppq_near, 3, 1.4)] + \
                  [(m1911_near, s, 1.4) for s, _ in shots(m1911_near, 2, 1.4)]
    pistol_mid = [(ppq_mid, s, 2.2) for s, _ in shots(ppq_mid, 2, 2.2)] + \
                 [(m1911_mid, s, 2.2) for s, _ in shots(m1911_mid, 2, 2.2)]
    gunshots("pistol", pistol_near, pistol_mid, ir)

    # Reloads, from a pistol's slide and magazine; the rifle's are the same, heavier. Each part is the loudest
    # moment of its recording.
    def main_part(path, before=0.04, length=0.45):
        e = max(audiolib.find_events(path, min_gap=0.15, max_len=1.0, end_db=40), key=lambda x: x.peak)
        return Seg(path, max(0.0, e.start - before), length)

    racks = [bsb(s) for s in ("1984", "1986", "1987")]
    loads = [bsb(s) for s in ("1988", "1990", "1991")]
    for kind, pitch in (("pistol", 1.0), ("rifle", 0.84)):
        event("reload.%s.magout" % kind, [Out(numbered("weapon/%s_magout" % kind, k), "click", [
            Seg(rpg("metalLatch.ogg"), pitch=pitch * (1.0 + 0.05 * k)),
            Seg(rubber("metal_open_01.ogg"), pitch=pitch * 1.2, gain=-6, at=0.06, fx="highpass=f=300")],
            fx="highpass=f=150") for k in range(2)], "SFX", 0.0, maxDistance=20.0)
        event("reload.%s.magin" % kind, [Out(numbered("weapon/%s_magin" % kind, k), "click", [
            main_part(l, length=0.4)], fx="highpass=f=150") for k, l in enumerate(loads)], "SFX", 0.0,
            maxDistance=20.0)
        for k, r in enumerate(racks):
            seg = main_part(r, before=0.12, length=0.5)
            seg.pitch = pitch
            racks[k] = (r, seg)
        part = "slide" if kind == "pistol" else "bolt"
        event("reload.%s.%s" % (kind, part), [Out(numbered("weapon/%s_%s" % (kind, part), k), "click", [seg],
                                                  fx="highpass=f=150") for k, (_, seg) in enumerate(racks)],
              "SFX", 0.0, maxDistance=20.0)
        racks = [r for r, _ in racks]
    event("gun.dryfire", [Out(numbered("weapon/dryfire", k), "click", [main_part(bsb(s), length=0.25)],
                              fx="highpass=f=300") for k, s in enumerate(("1994", "1995"))],
          "SFX", -2.0, maxDistance=12.0)
    shells = [bsb(s) for s in ("1355", "1356", "1361", "1367")]
    event("gun.shell", [Out(numbered("weapon/shell", k), "click", [Seg(s, fx="highpass=f=400,lowpass=f=11000")])
                        for k, s in enumerate(shells)], "SFX", -8.0, maxDistance=10.0, priority=20, limit=6)

    # Bullet impacts on materials.
    target = bsb("0548")
    small = [bsb(s) for s in ("1529", "1530", "1531", "1532")]
    impacts = {
        "flesh": [[Seg(punches[k], pitch=1.25, fx="highpass=f=160"),
                   Seg(kenney("impactPunch_medium_%03d.ogg" % k), gain=-4, pitch=1.1, fx="lowpass=f=5000")]
                  for k in range(4)],
        "wood": [[Seg(kenney("impactWood_light_%03d.ogg" % k), pitch=1.1),
                  Seg(plank_k(k), gain=-6, pitch=1.3)] for k in range(4)],
        "stone": [[Seg(kenney("impactMining_%03d.ogg" % k), pitch=1.35, fx="highpass=f=300")] for k in range(4)],
        "metal": [[Seg(kenney("impactMetal_light_%03d.ogg" % k), pitch=1.1),
                   Seg(kenney("impactPlate_light_%03d.ogg" % k), gain=-6, pitch=1.3)] for k in range(4)],
        "dirt": [[Seg(kenney("impactSoft_medium_%03d.ogg" % k), pitch=1.2),
                  Seg(bsb("1022"), 1.70 + 0.0, 0.25, gain=-10, pitch=1.3, fx="highpass=f=600")] for k in range(4)],
        "water": [[Seg(small[k], 0.0, 0.6, fade_out=0.3, pitch=1.15, fx="highpass=f=200")] for k in range(4)],
    }
    for material, variants in impacts.items():
        event("impact.%s" % material, [Out(numbered("weapon/impact_%s" % material, k), "impact", segs,
                                           fx="highpass=f=60", max_fade=0.15) for k, segs in enumerate(variants)],
              "SFX", 0.0, maxDistance=50.0, priority=55, limit=8)

    # Hit markers (2D, for the shooter): a dry tick, a bright clink for a head shot, a lower knock for a kill.
    event("hit.marker", [Out(numbered("ui/hit_marker", k), "marker", [
        Seg(kenney("impactPlate_light_%03d.ogg" % (k + 2)), pitch=1.9, fx="highpass=f=1200")], length=0.12,
        max_fade=0.08) for k in range(2)], "UI", 0.0, pitch=[0.99, 1.01])
    event("hit.headshot", [Out(numbered("ui/hit_headshot", k), "marker", [
        Seg(kenney("impactGlass_light_%03d.ogg" % k), pitch=1.1, fx="highpass=f=1500"),
        Seg(kenney("impactPlate_light_%03d.ogg" % k), pitch=2.2, gain=-6, fx="highpass=f=1500")], length=0.3,
        max_fade=0.2) for k in range(2)], "UI", 0.0, pitch=[1.0, 1.0])
    event("hit.kill", [Out("ui/hit_kill_01.ogg", "marker", [
        Seg(kenney("impactMetal_light_002.ogg"), pitch=0.8, fx="highpass=f=300"),
        Seg(kenney("impactPlate_light_001.ogg"), pitch=1.5, gain=-6, at=0.07, fx="highpass=f=1200")],
        length=0.45, max_fade=0.3)], "UI", 0.0, pitch=[1.0, 1.0])


def plank_k(k):
    return kenney("impactPlank_medium_%03d.ogg" % (k % 5))


# ---------------------------------------------------------------- explosives

def explosives():
    event("explosive.fuse", [loop("weapon/fuse_loop.ogg", "loop", bsb("1306"), 2.0, 6.0, overlap=1.0,
                                  fx="highpass=f=200,lowpass=f=12000")],
          "SFX", -2.0, unitSize=2.0, maxDistance=20.0, priority=70)
    # The charge's timer: a piezo beep (made in code; it is electronic anyway).
    beep = __import__("synth").tones("timer_beep", [(3150.0, 0.0, 0.11)], 0.2)
    event("explosive.beep", [Out("weapon/timer_beep_01.ogg", "click", [Seg(beep)], fx="highpass=f=500")],
          "SFX", -2.0, maxDistance=25.0, priority=70, pitch=[1.0, 1.0], jitter=0.0)
    sub = [pack("kenney-sci-fi-sounds", "Audio/lowFrequency_explosion_00%d.ogg" % i) for i in (0, 1)]
    crunch = [pack("kenney-sci-fi-sounds", "Audio/explosionCrunch_00%d.ogg" % i) for i in range(5)]
    near, mid, far, away = bsb("1808"), bsb("1807"), bsb("1806"), bsb("1023")
    # The far-away recording's rolling echo (after its first report) is the tail of the nearer layers.
    echo = Seg(away, 0.35, 4.0, fade_in=0.3, fx="lowpass=f=2500")
    near_outs = [Out(numbered("weapon/explosion_near", k), "explosion", [
        Seg(near, fx="highpass=f=40"),
        Seg(sub[k % 2], gain=-1, fx="lowpass=f=180,highpass=f=30"),
        Seg(crunch[k], gain=-6, pitch=0.75, fx="lowpass=f=6000"),
        Seg(echo.src, echo.start, echo.dur, fade_in=echo.fade_in, fx=echo.fx, gain=-6, at=0.15),
        Seg(bsb("1022"), 1.70, 0.8, gain=-12, at=0.12, fx="highpass=f=300")], tail_db=-55, max_fade=1.2)
        for k in range(3)]
    mid_outs = [Out(numbered("weapon/explosion_mid", k), "distant", [
        Seg(mid, fx="highpass=f=40,lowpass=f=5000"),
        Seg(sub[k % 2], gain=-6, fx="lowpass=f=140,highpass=f=30"),
        Seg(echo.src, echo.start, echo.dur, fade_in=echo.fade_in, fx="lowpass=f=1800", gain=-2, at=0.25)],
        tail_db=-55, max_fade=1.2) for k in range(2)]
    far_outs = [Out(numbered("weapon/explosion_far", k), "distant", [
        Seg(far, fx="highpass=f=35,lowpass=f=1500"),
        Seg(away, 0.0, 4.5, fx="highpass=f=35,lowpass=f=1300", gain=-1 if k else -4, at=0.0 if k else 0.3),
        Seg(sub[k % 2], gain=-10, fx="lowpass=f=110,highpass=f=30")], tail_db=-55, max_fade=1.5)
        for k in range(2)]
    layered("explosion", [
        ("near", near_outs, 0.0, 80.0, 40.0, 0.0, 20.0, 250.0),
        ("mid", mid_outs, 80.0, 500.0, 120.0, -2.0, 60.0, 900.0),
        ("far", far_outs, 500.0, 2500.0, 250.0, -4.0, 200.0, 2600.0),
    ], "SFX", 0.0, priority=95, limit=6, delay=True, pitch=[0.95, 1.05], jitter=1.0)
    stone_fall = bsb("1022")
    grit = hits(stone_fall, 8, min_gap=0.8, max_len=0.8, within_db=10)
    event("explosion.debris", [Out(numbered("weapon/debris", k), "gather", [
        seg_of(stone_fall, grit[k], extra=0.4), seg_of(stone_fall, grit[k + 3], extra=0.4, at=0.2, gain=-3),
        Seg(rubber("bfh1_falling_0%d.ogg" % (k + 1)), gain=-4, at=0.1),
        Seg(bsb("1771"), gain=-10, at=0.35, fx="highpass=f=200")], fx="highpass=f=60") for k in range(3)],
        "SFX", 0.0, unitSize=6.0, maxDistance=120.0, priority=60, limit=3)


# ---------------------------------------------------------------- interface

def iface(name):
    return pack("kenney-interface-sounds", "Audio/" + name)


def uia(name):
    return pack("kenney-ui-audio", "Audio/" + name)


def interface():
    def ui(key, rel, segs_list, volume=0.0, wav=True, **kw):
        ext = ".wav" if wav else ".ogg"
        outs = [Out("ui/%s_%02d%s" % (rel, k + 1, ext), "ui", segs, fx="highpass=f=150", max_fade=0.1)
                for k, segs in enumerate(segs_list)]
        event(key, outs, "UI", volume, **kw)

    ui("ui.click", "click", [[Seg(uia("click%d.ogg" % i))] for i in (1, 3, 5)])
    ui("ui.hover", "hover", [[Seg(uia("rollover%d.ogg" % i))] for i in (2, 4)], -6.0, cooldown=0.05)
    ui("ui.drag", "drag", [[Seg(rpg("handleSmallLeather.ogg"), 0.0, 0.25, fade_out=0.08)],
                           [Seg(rpg("handleSmallLeather2.ogg"), 0.0, 0.25, fade_out=0.08)]], -3.0)
    ui("ui.drop", "drop", [[Seg(rpg("dropLeather.ogg"))], [Seg(iface("drop_002.ogg"), gain=-6),
                                                           Seg(rpg("dropLeather.ogg"), pitch=1.1)]], -2.0)
    ui("ui.craft.start", "craft_start", [[Seg(iface("switch_002.ogg")), Seg(rpg("cloth2.ogg"), gain=-8)],
                                         [Seg(iface("switch_004.ogg")), Seg(rpg("cloth4.ogg"), gain=-8)]], wav=False)
    ui("ui.craft.done", "craft_done", [[Seg(iface("confirmation_001.ogg"))], [Seg(iface("confirmation_002.ogg"))]],
       wav=False)
    ui("ui.error", "error", [[Seg(iface("error_006.ogg"))], [Seg(iface("error_008.ogg"))]], -2.0, wav=False,
       cooldown=0.2)
    ui("ui.inventory.open", "inventory_open", [[Seg(rpg("cloth1.ogg")), Seg(rpg("beltHandle1.ogg"), gain=-6)],
                                               [Seg(rpg("clothBelt.ogg"))]], wav=False)
    ui("ui.inventory.close", "inventory_close", [[Seg(rpg("cloth3.ogg"))], [Seg(rpg("clothBelt2.ogg"))]], wav=False)
    ui("ui.notify", "notify", [[Seg(iface("bong_001.ogg"))], [Seg(iface("question_002.ogg"))]], -2.0, wav=False,
       cooldown=0.5)


# ---------------------------------------------------------------- ambience

def ambience():
    # Each bed is the steadiest stretch of its recording (the least spread of short-term loudness), so no single
    # call or gust stands out each time the loop comes round.
    synth = __import__("synth")
    calm = synth.wind("wind_calm", seconds=44.0, seed=7)
    high = synth.wind("wind_high", seconds=44.0, seed=19, body=(0.35, 0.18, 0.1, 0.06), whistle=(0.5, 0.3, 0.15),
                      body_band=(90.0, 700.0), whistle_band=(900.0, 2200.0))

    def bed(key, rel, src, start, length=40.0, fx="", channels=2, overlap=3.0, **kw):
        event(key, [loop("ambience/" + rel, "ambience", src, start, length, overlap=overlap, fx=fx,
                         channels=channels)], "Ambience", 0.0, **kw)

    bed("amb.wind.calm", "wind_calm.ogg", calm, 0.0, 40.0, fx="highpass=f=40", overlap=4.0)
    # A mono recording: the right channel 23 ms later, so the bed is wide rather than in the middle of the head.
    bed("amb.wind.strong", "wind_strong.ogg", bsb("0146"), 10.0, 40.0,
        fx="highpass=f=110,lowpass=f=9000,equalizer=f=400:t=q:w=1:g=2,adelay=delays=0|23", overlap=4.0)
    bed("amb.wind.high", "wind_high.ogg", high, 0.0, 40.0, fx="highpass=f=60", overlap=4.0)
    bed("amb.birds.meadow", "birds_meadow.ogg", bsb("0898"), 13.0, 50.0, fx="highpass=f=320,lowpass=f=14000")
    bed("amb.birds.forest", "birds_forest.ogg", bsb("2715"), 108.0, 48.0, fx="highpass=f=250,lowpass=f=15000")
    bed("amb.insects.night", "insects_night.ogg", bsb("1880"), 71.0, 50.0, fx="highpass=f=300,lowpass=f=14000")
    bed("amb.surf.near", "surf_near.ogg", bsb("0698"), 20.0, 44.0, fx="highpass=f=45,lowpass=f=13000")
    bed("amb.surf.far", "surf_far.ogg", bsb("0698"), 80.0, 44.0, fx="highpass=f=40,lowpass=f=900")
    bed("amb.leaves", "leaves.ogg", bsb("0904"), 50.0, 40.0, fx="highpass=f=250,lowpass=f=13000")

    # Rare accents, placed far around the listener: distant thunder, owls at night, crows by day.
    thunder = [bsb(s) for s in ("3113", "3114", "3179", "2718")]
    event("amb.thunder", [Out(numbered("ambience/thunder", k), "accent", [
        Seg(t, fx="lowpass=f=900,highpass=f=30")], tail_db=-50, max_fade=2.0) for k, t in enumerate(thunder)],
        "Ambience", 0.0, space="3d", unitSize=400.0, maxDistance=6000.0, priority=25, limit=1, pitch=[0.85, 1.0])
    owl = [bsb("1763"), bsb("1764")]
    event("amb.owl", [Out(numbered("ambience/owl", k), "accent", [Seg(o, fx="highpass=f=200,lowpass=f=6000")],
                          tail_db=-45) for k, o in enumerate(owl)],
          "Ambience", 0.0, space="3d", unitSize=30.0, maxDistance=400.0, priority=20, limit=1)
    crows = [bsb("0956"), bsb("3463")]
    event("amb.crow", [Out(numbered("ambience/crow", k), "accent", [Seg(c, fx="highpass=f=300,lowpass=f=9000")],
                           tail_db=-45) for k, c in enumerate(crows)],
          "Ambience", 0.0, space="3d", unitSize=30.0, maxDistance=400.0, priority=20, limit=1)


# ---------------------------------------------------------------- what each item sounds like

MELEE = {"swing": "melee.swing", "hitFlesh": "melee.hit.flesh", "hitHard": "melee.hit.hard",
         "hitWood": "gather.wood.hit", "hitStone": "gather.stone.hit", "hitOre": "gather.ore.hit",
         "weakSpot": "gather.weakspot"}
HEAVY = dict(MELEE, swing="melee.swing.heavy")


def gun_roles(kind):
    return {"shot": "%s.shot" % kind, "shotSilenced": "%s.shot.silenced" % kind,
            "magOut": "reload.%s.magout" % kind, "magIn": "reload.%s.magin" % kind,
            "rack": "reload.%s.%s" % (kind, "slide" if kind == "pistol" else "bolt"),
            "dryFire": "gun.dryfire", "shell": "gun.shell", "hitMarker": "hit.marker", "headshot": "hit.headshot",
            "kill": "hit.kill"}


PLACE = {"place": "deploy.place"}
DOOR = {"wood": "wood", "metal": "metal", "armoured": "armoured", "garage": "garage"}


def door_roles(material):
    return {"open": "door.open.%s" % material, "close": "door.close.%s" % material, "place": "build.place.wood"}


# Item keys (items.json, models.json) and door keys (building.json) → role → event. The ground mode asks
# SoundCatalogData.ItemEvent(item, role); the tests check every item that needs a sound has its roles.
ITEMS = {
    "rock": MELEE, "torch": MELEE, "stone-hatchet": HEAVY, "stone-pickaxe": HEAVY, "metal-hatchet": HEAVY,
    "metal-pickaxe": HEAVY, "wooden-spear": HEAVY, "stone-spear": HEAVY, "machete": MELEE,
    "bow": {"draw": "bow.draw", "release": "bow.release", "flight": "arrow.flight", "impact": "arrow.impact",
            "impactFlesh": "arrow.impact.flesh", "hitMarker": "hit.marker", "headshot": "hit.headshot",
            "kill": "hit.kill"},
    "arrow": {"flight": "arrow.flight", "impact": "arrow.impact", "impactFlesh": "arrow.impact.flesh",
              "pickup": "gather.pickup"},
    "pistol": gun_roles("pistol"), "rifle": gun_roles("rifle"),
    "silencer": {"shotPistol": "pistol.shot.silenced", "shotRifle": "rifle.shot.silenced"},
    "bandage": {"use": "survival.bandage"}, "medical-injector": {"use": "survival.bandage"},
    "red-berries": {"use": "survival.eat"}, "blue-berries": {"use": "survival.eat"},
    "wild-tuber": {"use": "survival.eat"}, "roasted-tuber": {"use": "survival.eat"},
    "sleeping-bag": PLACE, "bed": PLACE, "workbench-1": PLACE, "workbench-2": PLACE, "workbench-3": PLACE,
    "vehicle-workbench": PLACE, "engineering-workbench": PLACE, "claim-device": PLACE, "ladder": PLACE,
    "campfire": {"place": "deploy.place", "loop": "campfire.loop", "ignite": "campfire.ignite",
                 "extinguish": "fire.extinguish", "open": "box.open", "close": "box.close"},
    "furnace": {"place": "deploy.place", "loop": "furnace.loop", "ignite": "furnace.ignite",
                "extinguish": "fire.extinguish", "open": "box.open", "close": "box.close"},
    "small-box": {"place": "deploy.place", "open": "box.open", "close": "box.close"},
    "large-box": {"place": "deploy.place", "open": "box.open", "close": "box.close"},
    # G7: the research table, the blueprint and the world's props, on stand-ins from the events above (GROUND.md §11 W).
    "research-table": {"place": "deploy.place", "open": "box.open", "close": "box.close", "start": "ui.craft.start", "done": "ui.craft.done"},
    "blueprint": {"learn": "ui.craft.done"},
    "loot-barrel-1": {"hit": "impact.metal", "break": "impact.metal"},
    "loot-barrel-2": {"hit": "impact.metal", "break": "impact.metal"},
    "loot-crate": {"open": "box.open", "close": "box.close"},
    "military-crate": {"open": "box.open", "close": "box.close"},
    "recycler": {"open": "box.open", "close": "box.close", "start": "furnace.ignite", "loop": "furnace.loop", "stop": "fire.extinguish"},
    "building-plan": {"placeTwig": "build.place.twig", "placeWood": "build.place.wood",
                      "placeStone": "build.place.stone", "placeMetal": "build.place.metal",
                      "placeArmoured": "build.place.armoured"},
    "hammer": {"swing": "melee.swing", "upgradeWood": "build.upgrade.wood", "upgradeStone": "build.upgrade.stone",
               "upgradeMetal": "build.upgrade.metal", "upgradeArmoured": "build.upgrade.armoured",
               "repair": "build.repair", "demolish": "build.demolish", "collapse": "build.collapse"},
    "code-lock": {"press": "codelock.beep", "accept": "codelock.accept", "deny": "codelock.deny",
                  "place": "lock.key"},
    "key-lock": {"turn": "lock.key", "place": "lock.key"},
    "timed-charge": {"fuse": "explosive.fuse", "beep": "explosive.beep", "explode": "explosion",
                     "debris": "explosion.debris", "place": "deploy.place", "throw": "melee.swing"},
    # G5's other raiding items: stand-ins from the events above until they have their own (GROUND.md §11 R15).
    "bag-charge": {"fuse": "explosive.fuse", "explode": "explosion", "debris": "explosion.debris",
                   "place": "deploy.place", "throw": "melee.swing", "fizzle": "fire.extinguish",
                   "relight": "campfire.ignite"},
    "tin-grenade": {"fuse": "explosive.fuse", "explode": "explosion", "debris": "explosion.debris",
                    "place": "deploy.place", "throw": "melee.swing", "bounce": "impact.metal",
                    "fizzle": "fire.extinguish", "relight": "campfire.ignite"},
    "rocket-launcher": {"shot": "rifle.shot", "magOut": "reload.rifle.magout", "magIn": "reload.rifle.magin",
                        "rack": "reload.rifle.bolt", "dryFire": "gun.dryfire", "hitMarker": "hit.marker",
                        "kill": "hit.kill"},
    "rocket": {"flight": "arrow.flight", "explode": "explosion", "debris": "explosion.debris"},
    "rocket-hv": {"flight": "arrow.flight", "explode": "explosion", "debris": "explosion.debris"},
    "rifle-ammo-explosive": {"explode": "impact.metal"},
    # Door items (models.json) and door kinds (building.json).
    "wooden-door": door_roles("wood"), "metal-door": door_roles("metal"), "armoured-door": door_roles("armoured"),
    "double-door": door_roles("wood"), "garage-door": door_roles("garage"),
    "sheet-metal-door": door_roles("metal"), "wooden-double-door": door_roles("wood"),
    "sheet-metal-double-door": door_roles("metal"), "armoured-double-door": door_roles("armoured"),
}


def everything():
    OUTS.clear()
    EVENTS.clear()
    footsteps()
    gathering()
    survival()
    deployables()
    building()
    weapons()
    explosives()
    interface()
    ambience()
    return list(OUTS), list(EVENTS)

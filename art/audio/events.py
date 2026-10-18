"""The ground game's sound events (GROUND.md §4.9) and the recipe of every file they play.

`everything()` gives (outs, events): the files build.py renders and the catalogue entries it writes into
game/data/ground/sounds.json. A catalogue entry's playback fields (volume, pitch, attenuation, bus, priority...)
are normally defaults: build.py keeps sounds.json edits. Footwear volume and distance are owned by footsteps.json.
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


def footstep_set(surface, walk, run, crouch_from, land, contact_fx=None):
    """walk/run: lists of Seg lists (one per variant); crouch_from: Seg lists to soften; land: Seg lists."""
    base = "footstep/%s/" % surface
    contact = dict(fx=contact_fx, length=0.32, max_fade=0.07) if contact_fx else dict(fx=STEP_FX)
    crouch = dict(contact, fade_in=0.012) if contact_fx else dict(fx=SOFT_FX, fade_in=0.012)
    sets = {
        "walk": [Out(base + numbered("walk", i), "footstep", segs, **contact) for i, segs in enumerate(walk)],
        "run": [Out(base + numbered("run", i), "footstep", segs, **contact) for i, segs in enumerate(run)],
        "crouch": [Out(base + numbered("crouch", i), "footstep", segs, **crouch)
                   for i, segs in enumerate(crouch_from)],
        "land": [Out(base + numbered("land", i), "footstep", segs, **contact) for i, segs in enumerate(land)],
    }
    volume = {"walk": 0.0, "run": 3.0, "crouch": -7.0, "land": 4.0}
    reach = {"walk": 30.0, "run": 45.0, "crouch": 14.0, "land": 45.0}
    for stance, outs in sets.items():
        variation = dict(pitch=[0.98, 1.02], jitter=0.8) if contact_fx else {}
        event("footstep.%s.%s" % (stance, surface), outs, "Footsteps", volume[stance], maxDistance=reach[stance], **variation)


def thump(gain=-8.0, at=0.0, i=0):
    return Seg(kenney("impactSoft_heavy_%03d.ogg" % (i % 5)), gain=gain, fx=THUMP, at=at)


def footsteps():
    # Surface/gait recordings and footwear treatment are declared in data.
    from footstep_banks import generate
    generate(event)

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


# ---------------------------------------------------------------- ships (G6)

def scifi(name):
    return pack("kenney-sci-fi-sounds", "Audio/" + name)


def ships():
    # The Tin Finch's engines (GROUND.md §6.7): a low hum that runs with them and the thrusters' burn, each a seamless
    # loop the game turns up with the thrust; a boost's roar; the hatch; a touchdown on the skids and a crash.
    event("ship.engine", [loop("ship/engine_loop.ogg", "loop", scifi("spaceEngineLow_000.ogg"), 0.5, 3.0, overlap=1.0,
                               fx="highpass=f=35,lowpass=f=8000")],
          "SFX", 0.0, unitSize=8.0, maxDistance=300.0, priority=65, limit=8)
    event("ship.thrust", [loop("ship/thrust_loop.ogg", "loop", scifi("thrusterFire_001.ogg"), 0.5, 3.0, overlap=1.0,
                               fx="highpass=f=60,lowpass=f=12000")],
          "SFX", 0.0, unitSize=10.0, maxDistance=400.0, priority=65, limit=8)
    event("ship.boost", [Out(numbered("ship/boost", k), "foley", [
        Seg(scifi("spaceEngineLarge_00%d.ogg" % k), 0.0, 1.8, fade_out=0.9, fx="highpass=f=40"),
        Seg(scifi("thrusterFire_00%d.ogg" % (k + 2)), 0.0, 1.6, gain=-4, fade_out=0.8)]) for k in range(2)],
          "SFX", 0.0, unitSize=10.0, maxDistance=400.0, priority=70, limit=4)
    event("ship.hatch.open", [Out(numbered("ship/hatch_open", k), "foley", [Seg(scifi("doorOpen_00%d.ogg" % k))])
                              for k in range(2)], "SFX", -2.0, unitSize=3.0, maxDistance=30.0, priority=50)
    event("ship.hatch.close", [Out(numbered("ship/hatch_close", k), "foley", [Seg(scifi("doorClose_00%d.ogg" % k))])
                               for k in range(2)], "SFX", -2.0, unitSize=3.0, maxDistance=30.0, priority=50)
    event("ship.touchdown", [Out(numbered("ship/touchdown", k), "impact", [
        Seg(scifi("impactMetal_00%d.ogg" % k), fx="lowpass=f=2500"),
        Seg(kenney("impactSoft_heavy_00%d.ogg" % k), gain=-3, fx=THUMP)]) for k in range(2)],
          "SFX", 0.0, unitSize=6.0, maxDistance=80.0, priority=60)
    event("ship.crash", [Out(numbered("ship/crash", k), "impact", [
        Seg(scifi("impactMetal_00%d.ogg" % (k + 2))),
        Seg(scifi("explosionCrunch_00%d.ogg" % k), gain=-4, pitch=0.8, fx="lowpass=f=5000"),
        Seg(kenney("impactMetal_heavy_00%d.ogg" % k), gain=-2)]) for k in range(2)],
          "SFX", 0.0, unitSize=8.0, maxDistance=150.0, priority=75)


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


# ---------------------------------------------------------------- animals (G7, GROUND.md §6.9)

def animals():
    """The animals' voices (made in code: animal_voices.py) and footsteps (the grass steps, faster and lighter for
    hooves, softer for paws, heavier for the bear), one event per species and role: animal.<species>.<role>."""
    voices = __import__("animal_voices")
    made = voices.everything()
    hooves = voices.hooves()
    falls = [bsb(s) for s in ("2452", "2453", "2454", "2455")]
    vfx = "highpass=f=60,lowpass=f=12000"
    # How far each role carries (metres: full loudness within unitSize, silent past maxDistance), its loudness and
    # how many may sound at once.
    reach = {"call": (8.0, 140.0, -3.0, 3), "alarm": (10.0, 160.0, 0.0, 3), "threat": (8.0, 110.0, 0.0, 3),
             "attack": (6.0, 80.0, 0.0, 4), "hurt": (8.0, 110.0, 0.0, 4), "death": (8.0, 120.0, 0.0, 3)}
    far = {("wolf", "call"): (40.0, 900.0, 2.0, 2), ("bear", "threat"): (14.0, 240.0, 2.0, 2),
           ("boar", "call"): (4.0, 45.0, -6.0, 3), ("bear", "call"): (6.0, 70.0, -3.0, 2)}
    ir = __import__("synth").outdoor_ir("ir_outdoor")
    for key, roles in made.items():
        for role in ("call", "alarm", "threat", "attack", "hurt", "death"):
            if role not in roles:
                continue
            outs = []
            for k, wav in enumerate(roles[role]):
                segs = [Seg(wav, fx=vfx)]
                if role == "call" and key == "wolf":
                    # A howl carries: the open air's tail under it.
                    segs = [Seg(wav, fx=vfx, ir=ir, wet=0.35, dry=1.0)]
                if role == "attack" and key == "wolf":
                    segs.append(Seg(roles["snap"][k % len(roles["snap"])], gain=-4, at=0.3))
                if role == "threat" and key == "bear":
                    segs.append(Seg(roles["pop"][k % len(roles["pop"])], gain=-8, at=0.05))
                    segs.append(Seg(roles["pop"][(k + 1) % len(roles["pop"])], gain=-10, at=0.22))
                if role == "death":
                    # The body going down at the end of the cry, heavier for the bigger animals.
                    pitch = {"deer": 0.95, "boar": 0.9, "wolf": 1.0, "bear": 0.7}[key]
                    segs.append(Seg(falls[k % len(falls)], gain=-5, pitch=pitch, at=0.55, fx="highpass=f=40"))
                outs.append(Out(numbered("animals/%s_%s" % (key, role), k), "voice", segs, tail_db=-46, max_fade=0.3))
            unit, distance, volume, limit = far.get((key, role), reach[role])
            event("animal.%s.%s" % (key, role), outs, "SFX", volume, unitSize=unit, maxDistance=distance,
                  priority=55 if role in ("call",) else 65, limit=limit, pitch=[0.95, 1.05], jitter=1.5)

    # Footsteps: the grass recordings sped up for the lighter animals (a hoof's tock on top), slowed for the bear.
    g1, g2 = bsb("1253"), bsb("0854")
    grass = [(g1, e) for e in steps(g1, 4, min_sep=0.6, max_len=0.55)] +             [(g2, e) for e in steps(g2, 3, min_sep=0.6, max_len=0.5)]
    soft = "highshelf=f=7000:g=-6"
    paws = "lowpass=f=3200,highpass=f=80"
    sets = {
        "deer": [[seg_of(s, e, pitch=1.35, fx=soft), Seg(hooves["deer"][k % 4], gain=-14, fx="highpass=f=400")]
                 for k, (s, e) in enumerate(grass[:6])],
        "boar": [[seg_of(s, e, pitch=1.5, fx=soft), Seg(hooves["boar"][k % 4], gain=-16, fx="highpass=f=400")]
                 for k, (s, e) in enumerate(grass[:6])],
        "wolf": [[seg_of(s, e, pitch=1.2, fx=paws)] for s, e in grass[:6]],
        "bear": [[seg_of(s, e, pitch=0.78, fx=soft), thump(-6, i=k)] for k, (s, e) in enumerate(grass[:6])],
    }
    volume = {"deer": -2.0, "boar": -3.0, "wolf": -7.0, "bear": 2.0}
    reach_steps = {"deer": 30.0, "boar": 28.0, "wolf": 20.0, "bear": 40.0}
    for key, variants in sets.items():
        event("animal.%s.step" % key, [Out(numbered("animals/%s_step" % key, i), "footstep", segs, fx=STEP_FX)
                                       for i, segs in enumerate(variants)],
              "Footsteps", volume[key], maxDistance=reach_steps[key])


# ---------------------------------------------------------------- the Iron Wake (IRON_WAKE.md §10)

IW = "iron-wake/"
LOOP_S, LOOP_PRE = 16.0, 4.0     # the drives' loops: 16 s periodic, after 4 s for the filters to settle


def iron_wake():
    """The Iron Wake's sounds (IRON_WAKE.md §10, SHIP_FEEL.md), all in iron-wake/: the drives, the hull, the doors,
    the alarms, the siege batteries and their loaders, the defense mounts, the turrets, rockets and fire.

    What the libraries have no recording of is made in code (machines.py: shafts, turbines, pumps, contactors,
    servos, chains, ratchets, steel's modes, klaxons and bells) and layered with recordings of real mechanisms: locks
    and latches (BigSoundBank 0191, 1539, 1540), drawers sliding on steel rails (0773, 0792), creaking and grinding
    steel (0616, 0683), steam venting (1694-1696), steel bars and anvils, gunfire (the free firearm library's
    shotguns and rifles), explosions (1806-1808, 1023), gasoline fires (1340-1344) and burning wood (0987-0989).
    Loops are periodic by construction and cut without a crossfade (build.make_loop)."""
    m = __import__("machines")
    synth = __import__("synth")
    hull_ir = m.steel_ir("hull_ir", seconds=1.8, t60=1.25)
    ir = [synth.outdoor_ir("ir_outdoor_a", seed=1),
          synth.outdoor_ir("ir_outdoor_b", seed=5, t60=2.2, reflections=((0.13, 0.4), (0.29, 0.28), (0.52, 0.18),
                                                                         (0.81, 0.12), (1.24, 0.08)))]
    _iw_drives(m)
    _iw_hull(m, hull_ir)
    _iw_doors(m, hull_ir)
    _iw_alarms(m)
    _iw_siege(m, hull_ir, ir)
    _iw_loader(m)
    _iw_defense(m, ir)
    _iw_turret(m)
    _iw_rockets(m, ir)
    _iw_fire(m)


# Recorded parts the Iron Wake's mechanisms share: (BigSoundBank id, start, seconds).
CLACKS = [("1540", 1.25, 0.4), ("1540", 0.87, 0.32), ("0191", 2.46, 0.38), ("0191", 7.02, 0.36), ("0191", 1.32, 0.42),
          ("0191", 8.6, 0.3), ("0191", 3.51, 0.36), ("0191", 2.9, 0.22)]      # heavy latches and bolts
SLIDES = [("0773", 0.15, 1.25), ("0773", 2.5, 1.15), ("0773", 5.05, 1.1), ("0792", 2.24, 0.72), ("0792", 11.79, 0.9),
          ("0792", 21.41, 0.7)]   # steel sliding on rails, then its stop (65-83 % in): parts up to 0.6 are only sliding
CREAKS = [(0.04, 1.9), (12.63, 0.86), (15.09, 0.88), (40.81, 1.6), (44.87, 2.6), (56.89, 1.85), (68.64, 1.5),
          (5.24, 0.6), (26.08, 0.5), (28.24, 0.7), (51.32, 0.8), (75.17, 1.0), (65.3, 0.5), (60.3, 0.57)]  # in 0616
SQUEALS = [(9.33, 0.9), (11.10, 0.85), (14.9, 0.8), (17.04, 1.5), (18.93, 1.5), (20.87, 1.25)]   # in 0683


_ONSETS = {}


def _onset(path, start=0.0, dur=0.5, pre=0.002, floor_db=-24.0):
    """Where a part of a recording really starts: the first millisecond within `floor_db` of its loudest (a shot's
    or a clack's attack), less `pre`; layers recorded with different leads then land together."""
    key = (path, start, dur, pre, floor_db)
    if key not in _ONSETS:
        x = audiolib.decode(path, 48000, start, dur)
        hop = 48
        env = [sum(v * v for v in x[i:i + hop]) for i in range(0, max(0, len(x) - hop), hop)]
        top = max(env) if env else 0.0
        k = next((i for i, v in enumerate(env) if v >= top * 10 ** (floor_db / 10.0)), 0)
        _ONSETS[key] = max(0.0, start + k * 0.001 - pre)
    return _ONSETS[key]


def _clack(k, **kw):
    """A heavy latch or bolt, from its attack."""
    sid, start, dur = CLACKS[k % len(CLACKS)]
    path = bsb(sid)
    t = _onset(path, start, dur, 0.003)
    return Seg(path, t, dur - (t - start), **kw)


def _slide(k, part=None, **kw):
    """A slide on rails and its stop; `part` (from, to) in fractions of it."""
    sid, start, dur = SLIDES[k % len(SLIDES)]
    if part:
        start, dur = start + dur * part[0], dur * (part[1] - part[0])
    return Seg(bsb(sid), start, dur, **kw)


def _creak(k, longest=None, **kw):
    start, dur = CREAKS[k % len(CREAKS)]
    return Seg(bsb("0616"), start, min(dur, longest or dur), **kw)


def _iw_drives(m):
    loop = (LOOP_S, 0.0, LOOP_PRE)
    total = LOOP_PRE + LOOP_S + 0.01
    # Real flame under the made combustion: a big gasoline fire's steady roar, slowed (BigSoundBank 1342).
    roar = [m.looped(bsb("1342"), 12.0 + 9.0 * k, LOOP_S, 3.0, total, pitch=p) for k, p in enumerate((0.5, 0.56))]
    # The drive at four points, crossfaded by the game: ground idle, flight idle, part thrust, full thrust.
    for key, speed, load in (("idle.slow", 0.62, 0.0), ("idle.fast", 0.68, 0.06), ("load.slow", 0.84, 0.55),
                             ("load.fast", 1.0, 1.0)):
        segs = [Seg(m.drive_loop("drive_" + key.replace(".", "_"), speed=speed, load=load, seconds=LOOP_S,
                                 pre=LOOP_PRE, seed=11))]
        if load > 0.2:
            segs.append(Seg(roar[0 if load < 0.8 else 1], gain=-16 + 9 * load, fx="lowpass=f=2400,highpass=f=40"))
        event("iw.drive." + key, [Out(IW + "drive_%s.ogg" % key.replace(".", "_"), "engine", segs,
                                      fx="highpass=f=25", loop=loop)],
              "SFX", 0.0, unitSize=16.0, maxDistance=900.0 if key.startswith("load") else 400.0, priority=72, limit=4,
              pitch=[1.0, 1.0], jitter=0.0)

    # Start: the main contactor, the pumps' contactor, the pumps running up, the igniter, light-off, the spool to
    # ground idle through the hull's resonance (a creak as it passes), a bleed valve closing.
    outs = []
    for k, t in enumerate((dict(contactors=(0.0, 0.34), sparks=(1.15, 1.85), light=1.85),
                           dict(contactors=(0.0, 0.41), sparks=(1.2, 1.98), light=1.98),
                           dict(contactors=(0.0, 0.29), sparks=(1.1, 1.78), light=1.78))):
        c0, c1 = t["contactors"]
        outs.append(Out(IW + numbered("drive_start", k), "machine", [
            Seg(m.drive_start("drive_start_%d" % k, seconds=7.6, seed=21 + k, **t)),
            _clack(0, pitch=0.78, gain=-17, at=c0, fx="highpass=f=120"),
            _clack(2 + k, pitch=0.85, gain=-19, at=c1, fx="highpass=f=150"),
            Seg(bsb("1340"), 0.0, 1.6, pitch=0.6, gain=-14, at=t["light"] - 0.02, fade_out=0.6, fx="lowpass=f=1800"),
            _creak(1 + k, 0.8, pitch=0.5, gain=-18, at=t["light"] + 1.8, fade_in=0.08, fade_out=0.25,
                   fx="lowpass=f=2500"),
            Seg(rpg("metalLatch.ogg"), pitch=0.7, gain=-14, at=6.3 + 0.1 * k)],
            fx="highpass=f=25", tail_db=-55, max_fade=0.8, length=7.6))
    event("iw.drive.start", outs, "SFX", 0.0, unitSize=16.0, maxDistance=500.0, priority=75, limit=2,
          pitch=[0.99, 1.01], jitter=0.5)

    # Shutdown: the fuel valve shuts, the flame dies, the turbine runs down (the hull creaks as it passes its
    # resonance), the pumps' contactor drops, the pumps stop, the main contactor drops, the metal ticks as it cools.
    outs = []
    for k, t in enumerate((dict(pumps_off=1.0, mains_off=5.6, ticks=9), dict(pumps_off=1.25, mains_off=5.9, ticks=8),
                           dict(pumps_off=0.85, mains_off=5.3, ticks=10))):
        outs.append(Out(IW + numbered("drive_shutdown", k), "machine", [
            Seg(m.drive_shutdown("drive_shutdown_%d" % k, seconds=8.6, seed=31 + k, **t)),
            _clack(1, pitch=0.72, gain=-15, fx="highpass=f=100"),
            _clack(3 + k, pitch=0.82, gain=-19, at=t["pumps_off"], fx="highpass=f=150"),
            _clack(0, pitch=0.74, gain=-18, at=t["mains_off"], fx="highpass=f=120"),
            _creak(3 + k, 1.3, pitch=0.48, gain=-17, at=1.0, fade_in=0.2, fade_out=0.4, fx="lowpass=f=2200")],
            fx="highpass=f=25", tail_db=-55, max_fade=0.6))
    event("iw.drive.shutdown", outs, "SFX", -3.0, unitSize=16.0, maxDistance=500.0, priority=75, limit=2,
          pitch=[0.99, 1.01], jitter=0.5)

    # Release: thrust falling. The load lets go, the blow-off vents, the whine falls back (the loops carry on).
    outs = []
    for k in range(4):
        h = ("1695", "1694", "1696", "1695")[k]
        outs.append(Out(IW + numbered("drive_release", k), "machine", [
            Seg(m.drive_release("drive_release_%d" % k, seed=41 + k, top=(0.92, 0.95, 0.88, 0.97)[k],
                                blow=(1.0, 0.8, 1.2, 0.9)[k])),
            Seg(rpg("metalLatch.ogg"), pitch=(0.6, 0.66, 0.58, 0.63)[k], gain=-12, at=0.01),
            Seg(bsb(h), (0.0, 0.1, 0.0, 0.5)[k], 1.0, pitch=0.85, gain=-14, at=0.03, fade_out=0.5,
                fx="highpass=f=600"),
            _creak(7 + k, 0.6, pitch=0.6, gain=-18, at=0.6, fade_out=0.2, fx="lowpass=f=3000")],
            fx="highpass=f=25", tail_db=-50, max_fade=0.6))
    event("iw.drive.release", outs, "SFX", -2.0, unitSize=16.0, maxDistance=600.0, priority=70, limit=2,
          pitch=[0.97, 1.03], jitter=1.0)

    # The lift engines: four ducted fans beating against each other, the downwash (a strong wind's real gusts in it).
    wind = m.looped(bsb("0146"), 20.0, LOOP_S, 3.0, total, pitch=0.85)
    for key, speed in (("slow", 0.4), ("fast", 1.0)):
        event("iw.lift.loop." + key, [Out(IW + "lift_loop_%s.ogg" % key, "engine", [
            Seg(m.lift_loop("lift_" + key, speed=speed, seconds=LOOP_S, pre=LOOP_PRE, seed=51)),
            Seg(wind, gain=-14 + 4 * speed, fx="highpass=f=60,lowpass=f=5000")], fx="highpass=f=25", loop=loop)],
            "SFX", 0.0, unitSize=16.0, maxDistance=800.0, priority=72, limit=4, pitch=[1.0, 1.0], jitter=0.0)

    # Manoeuvring puffs: a solenoid's snap and a hard burst of gas, short taps and longer pushes.
    outs = []
    for k in range(6):
        size = (0.6, 1.0, 1.5, 0.8, 1.2, 0.5)[k]
        h, s0 = (("1694", 0.13), ("1695", 0.05), ("1696", 0.03))[k % 3]
        outs.append(Out(IW + numbered("rcs_puff", k), "machine", [
            Seg(m.puff("rcs_%d" % k, seconds=0.25 + 0.3 * size, seed=61 + k, size=size)),
            Seg(bsb(h), s0, 0.12 + 0.25 * size, pitch=1.35, gain=-10, fade_out=0.1, fx="highpass=f=900")],
            fx="highpass=f=40", tail_db=-50, max_fade=0.15))
    event("iw.rcs.puff", outs, "SFX", -4.0, unitSize=6.0, maxDistance=180.0, priority=55, limit=8,
          pitch=[0.94, 1.06], jitter=1.5)

    # The boost: its strain as a layer over the load (slow as it begins, fast at its height), and its release.
    roar_b = m.looped(bsb("1342"), 40.0, 12.0, 2.5, 16.01, pitch=0.62)
    for key, level in (("slow", 0.0), ("fast", 1.0)):
        event("iw.boost.strain." + key, [Out(IW + "boost_strain_%s.ogg" % key, "engine", [
            Seg(m.boost_loop("boost_" + key, level=level, seconds=12.0, pre=4.0, seed=71)),
            Seg(roar_b, gain=-10 + 3 * level, fx="lowpass=f=3500,highpass=f=40")], fx="highpass=f=25",
            loop=(12.0, 0.0, 4.0))],
            "SFX", 0.0, unitSize=18.0, maxDistance=900.0, priority=74, limit=2, pitch=[1.0, 1.0], jitter=0.0)
    outs = []
    for k in range(4):
        h = ("1695", "1694", "1695", "1696")[k]
        start, dur = SQUEALS[k]
        outs.append(Out(IW + numbered("boost_release", k), "machine", [
            Seg(m.boost_release("boost_release_%d" % k, seed=81 + k)),
            Seg(bsb(h), 0.0, 1.2, pitch=0.75, gain=-12, at=0.02, fade_out=0.6, fx="highpass=f=500"),
            Seg(bsb("0683"), start, min(dur, 1.0), pitch=0.36, gain=-14, at=0.35, fade_in=0.1, fade_out=0.4,
                fx="lowpass=f=1800"),
            Seg(kenney("impactMetal_heavy_%03d.ogg" % k), pitch=0.5, gain=-12, at=0.9, fx="lowpass=f=4000")],
            fx="highpass=f=25", tail_db=-50, max_fade=0.7))
    event("iw.boost.release", outs, "SFX", 0.0, unitSize=18.0, maxDistance=700.0, priority=72, limit=2,
          pitch=[0.97, 1.03], jitter=1.0)


def _iw_hull(m, hull_ir):
    # Strain by intensity: the hull's steel creaking (BigSoundBank 0616 slowed), groaning (made: stick-slip through the
    # plates' modes; 0683's squeals slowed far down) and popping through (oil-canning), ringing in the structure.
    door = bsb("0616")
    light = []
    for k in range(6):
        start, dur = CREAKS[7 + k]
        light.append(Out(IW + numbered("hull_strain_light", k), "machine", [
            Seg(door, start, min(dur, 0.9), pitch=(0.72, 0.8, 0.66, 0.76, 0.7, 0.84)[k], fade_in=0.03,
                fade_out=0.15, fx="highpass=f=90", ir=hull_ir, wet=0.35, dry=1.0),
            Seg(m.strike("ping_%d" % k, seconds=0.3, seed=101 + k, f0=(2600.0, 3100.0, 2300.0, 3500.0, 2800.0,
                                                                        3300.0)[k],
                         ratios=m.PLATE[:5], decay_s=0.09, hardness=0.9), gain=-16, at=(0.1, 0.3, 0.05, 0.4, 0.2,
                                                                                          0.15)[k])],
            fx="highpass=f=40", tail_db=-50, max_fade=0.4))
    event("iw.hull.strain.light", light, "SFX", -4.0, unitSize=10.0, maxDistance=90.0, priority=50, limit=3,
          pitch=[0.95, 1.05], jitter=1.5, occlude=False)
    medium = []
    for k in range(6):
        start, dur = CREAKS[k]
        medium.append(Out(IW + numbered("hull_strain_medium", k), "machine", [
            Seg(door, start, min(dur, 1.4), pitch=(0.44, 0.47, 0.41, 0.5, 0.4, 0.45)[k], fade_in=0.05, fade_out=0.3,
                fx="highpass=f=60", ir=hull_ir, wet=0.4, dry=1.0),
            Seg(m.groan("groan_m%d" % k, seconds=1.5, seed=111 + k, f0=(72.0, 84.0, 66.0, 90.0, 78.0, 70.0)[k],
                        glide=(0.85, 0.9, 0.8, 0.92, 0.86, 0.82)[k]), gain=-10, ir=hull_ir, wet=0.3, dry=1.0),
            Seg(m.oilcan("pop_m%d" % k, seed=121 + k, f0=(170.0, 210.0, 150.0, 240.0, 190.0, 160.0)[k]), gain=-9,
                at=(0.45, 0.6, 0.3, 0.7, 0.5, 0.35)[k], ir=hull_ir, wet=0.4, dry=1.0)],
            fx="highpass=f=35", tail_db=-50, max_fade=0.6))
    event("iw.hull.strain.medium", medium, "SFX", -2.0, unitSize=12.0, maxDistance=120.0, priority=55, limit=3,
          pitch=[0.96, 1.04], jitter=1.0, occlude=False)
    heavy = []
    for k in range(5):
        start, dur = SQUEALS[k]
        heavy.append(Out(IW + numbered("hull_strain_heavy", k), "machine", [
            Seg(bsb("0683"), start, min(dur, 1.4), pitch=(0.25, 0.28, 0.23, 0.27, 0.22)[k], fade_in=0.15,
                fade_out=0.5, fx="lowpass=f=1600,highpass=f=40", ir=hull_ir, wet=0.45, dry=1.0),
            Seg(m.groan("groan_h%d" % k, seconds=2.2, seed=131 + k, f0=(48.0, 55.0, 44.0, 58.0, 51.0)[k], glide=0.8,
                        rough=0.9, bright=0.8), gain=-6, ir=hull_ir, wet=0.35, dry=1.0),
            Seg(m.oilcan("pop_h%d" % k, seed=141 + k, f0=(130.0, 150.0, 120.0, 160.0, 140.0)[k], size=1.4), gain=-6,
                at=(0.5, 0.7, 0.4, 0.9, 0.6)[k], ir=hull_ir, wet=0.45, dry=1.0),
            Seg(m.oilcan("pop_h2%d" % k, seed=151 + k, f0=(210.0, 190.0, 230.0, 200.0, 180.0)[k]), gain=-10,
                at=(1.2, 1.4, 1.1, 1.5, 1.3)[k]),
            Seg(m.boom("boom_h%d" % k, seconds=1.4, seed=161 + k, f_start=54.0, f_end=41.0, decay_s=1.0), gain=-8,
                at=0.08),
            _creak(k + 3, 0.9, pitch=0.62, gain=-14, at=1.0, fade_out=0.3, fx="highpass=f=80"),
            Seg(m.rattle_burst("rattle_h%d" % k, seconds=1.0, seed=171 + k, rate=30.0, f0=0.9), gain=-16, at=0.6)],
            fx="highpass=f=30", tail_db=-52, max_fade=0.8))
    event("iw.hull.strain.heavy", heavy, "SFX", 0.0, unitSize=16.0, maxDistance=160.0, priority=60, limit=2,
          pitch=[0.97, 1.03], jitter=1.0, occlude=False)

    # Touchdown: the legs' oleo struts compressing (the steam of a vent for their hydraulics), 420 t landing in a deep
    # thud, the gear's steel clanking, a rebound, things rattling, and the hull settling with a creak.
    falls = [("2455", 0.09, 0.5), ("2453", 0.41, 0.4), ("2454", 0.26, 0.4), ("2456", 0.7, 0.55)]
    outs = []
    for k in range(4):
        sid, s0, d = falls[k]
        outs.append(Out(IW + numbered("touchdown", k), "machine", [
            Seg(bsb(sid), s0, d, pitch=0.5, gain=-2, fx="lowpass=f=1200"),
            Seg(kenney("impactSoft_heavy_%03d.ogg" % k), pitch=0.55, gain=-4, fx="lowpass=f=900"),
            Seg(m.boom("boom_td%d" % k, seconds=1.4, seed=181 + k, f_start=68.0, f_end=42.0, decay_s=0.9), gain=-1),
            Seg(bsb("3590"), (1.18, 2.22, 5.62, 9.82)[k], 0.5, pitch=0.42, gain=-16, fade_out=0.3,
                fx="lowpass=f=3000"),
            Seg(rubber("metal_slam_01.ogg"), pitch=(0.6, 0.55, 0.65, 0.58)[k], gain=-10, at=0.02),
            Seg(bsb(("1695", "1694", "1696", "1695")[k]), 0.0, 0.9, pitch=0.6, gain=-14, at=0.03, fade_out=0.4,
                fx="highpass=f=300"),
            Seg(m.boom("boom_tr%d" % k, seconds=0.8, seed=191 + k, f_start=58.0, f_end=40.0, decay_s=0.5), gain=-9,
                at=0.32),
            Seg(m.rattle_burst("rattle_td%d" % k, seconds=1.1, seed=201 + k, rate=40.0), gain=-14, at=0.05),
            _creak(k * 2, 0.9, pitch=0.55, gain=-14, at=0.7, fade_in=0.05, fade_out=0.3, fx="highpass=f=60")],
            fx="highpass=f=28", tail_db=-52, max_fade=0.7))
    event("iw.touchdown", outs, "SFX", 0.0, unitSize=14.0, maxDistance=250.0, priority=70, limit=2,
          pitch=[0.97, 1.03], jitter=1.0)

    # Inside: the ventilation and the machinery, long seamless beds (stereo), played by room.
    event("iw.interior.ventilation", [Out(IW + "interior_ventilation.ogg", "ambience", [
        Seg(m.ventilation("ventilation", seconds=48.0, pre=4.0, seed=211))], fx="highpass=f=30", channels=2,
        loop=(48.0, 0.0, 4.0))], "Ambience", 0.0)
    event("iw.interior.machinery", [Out(IW + "interior_machinery.ogg", "ambience", [
        Seg(m.machinery("machinery", seconds=56.0, pre=4.0, seed=221))], fx="highpass=f=30", channels=2,
        loop=(56.0, 0.0, 4.0))], "Ambience", 0.0)


def _iw_doors(m, hull_ir):
    # The cargo ramp: release (the hydraulics' valve, two locking pins drawn, the weight taken), the actuators'
    # travel (a loop: the power pack's motor and pump, the oil's rush, the hinges' rumble), contact (the lip landing)
    # and lock (pins driven home, pressure let go).
    outs = []
    for k in range(3):
        outs.append(Out(IW + numbered("ramp_release", k), "machine", [
            Seg(rpg("metalClick.ogg"), pitch=0.8, gain=-10),
            Seg(m.hiss("ramp_hiss%d" % k, seconds=1.4, seed=231 + k, attack=0.05, hold=0.5, decay_s=0.8, lo=700.0,
                       hi=7000.0, sweep=(2600.0, 1500.0)), gain=-16, at=0.03),
            Seg(bsb("1539"), _onset(bsb("1539"), 1.5, 0.35), 0.3, pitch=(0.6, 0.64, 0.57)[k], gain=-3, at=0.15),
            Seg(bsb("1539"), _onset(bsb("1539"), 1.85, 0.4), 0.38, pitch=(0.62, 0.58, 0.66)[k], gain=-4,
                at=0.42 + 0.04 * k),
            Seg(kenney("impactSoft_heavy_%03d.ogg" % (k + 1)), pitch=0.55, gain=-6, at=0.6),
            Seg(m.boom("boom_rr%d" % k, seconds=0.9, seed=241 + k, f_start=72.0, f_end=45.0, decay_s=0.6), gain=-6,
                at=0.6),
            _creak(10 + k, 0.7, pitch=0.6, gain=-16, at=0.7, fade_out=0.25, fx="highpass=f=80")],
            fx="highpass=f=30", tail_db=-50, max_fade=0.5))
    event("iw.ramp.release", outs, "SFX", 0.0, unitSize=7.0, maxDistance=140.0, priority=62, limit=2,
          pitch=[0.98, 1.02], jitter=1.0)
    event("iw.ramp.actuator", [Out(IW + "ramp_actuator.ogg", "engine", [
        Seg(m.motor_loop("ramp_pack", rot_hz=24.6, seconds=10.0, pre=3.0, seed=251, gear=9, poles=4, brush=0.3,
                         ring_rumble=1.4, ticks=1.0)),
        Seg(m.looped(bsb("0513"), 2.0, 10.0, 2.0, 13.01, pitch=0.8), gain=-6, fx="highpass=f=50,lowpass=f=6000"),
        Seg(m.looped(bsb("1342"), 60.0, 10.0, 2.0, 13.01, pitch=1.6), gain=-24, fx="highpass=f=1500,lowpass=f=7000")],
        fx="highpass=f=40", loop=(10.0, 0.0, 3.0))],
        "SFX", 0.0, unitSize=7.0, maxDistance=140.0, priority=60, limit=2, pitch=[1.0, 1.0], jitter=0.0)
    outs = []
    for k in range(4):
        outs.append(Out(IW + numbered("ramp_contact", k), "machine", [
            Seg(rubber("metal_slam_01.ogg"), pitch=(0.5, 0.55, 0.47, 0.52)[k], gain=-2),
            Seg(kenney("impactPlate_heavy_%03d.ogg" % k), pitch=0.55, gain=-4),
            Seg(bsb("3590"), (0.15, 1.2, 3.23, 5.64)[k], 0.4, pitch=0.33, gain=-18, fade_out=0.3, fx="lowpass=f=2500"),
            Seg(bsb(("2455", "2453", "2454", "2456")[k]), (0.09, 0.41, 0.26, 0.7)[k], 0.5, pitch=0.55, gain=-4,
                fx="lowpass=f=1500"),
            Seg(m.boom("boom_rc%d" % k, seconds=1.2, seed=261 + k, f_start=62.0, f_end=42.0, decay_s=0.7), gain=-3),
            Seg(m.strike("plate_rc%d" % k, seconds=2.0, seed=271 + k, f0=(82.0, 95.0, 76.0, 101.0)[k],
                         ratios=m.PLATE, decay_s=1.3, hardness=0.4), gain=-10),
            Seg(bsb(("1772", "1775", "1777", "1773")[k]), 0.0, 1.2, pitch=0.7, gain=-14, at=0.06, fade_out=0.4,
                fx="lowpass=f=5000")],
            fx="highpass=f=28", tail_db=-52, max_fade=0.7))
    event("iw.ramp.contact", outs, "SFX", 0.0, unitSize=9.0, maxDistance=220.0, priority=66, limit=2,
          pitch=[0.97, 1.03], jitter=1.0)
    outs = []
    for k in range(3):
        outs.append(Out(IW + numbered("ramp_lock", k), "mechanism", [
            Seg(rpg("metalLatch.ogg"), pitch=(0.65, 0.6, 0.7)[k], gain=-6),
            _clack(0, pitch=(0.72, 0.68, 0.76)[k], gain=0, at=0.12),
            Seg(m.strike("lock_rl%d" % k, seconds=0.7, seed=281 + k, f0=(380.0, 350.0, 410.0)[k], decay_s=0.4,
                         hardness=0.7), gain=-8, at=0.12),
            Seg(m.boom("boom_rl%d" % k, seconds=0.5, seed=291 + k, f_start=95.0, f_end=60.0, decay_s=0.25), gain=-10,
                at=0.12),
            Seg(bsb("1696"), 0.0, 0.6, pitch=1.0, gain=-16, at=0.22, fade_out=0.3, fx="highpass=f=900")],
            fx="highpass=f=40", tail_db=-50, max_fade=0.4))
    event("iw.ramp.lock", outs, "SFX", 0.0, unitSize=6.0, maxDistance=90.0, priority=60, limit=2,
          pitch=[0.98, 1.02], jitter=1.0)

    # The side airlock: the door's motor running its 1.5 s along the rail to its stop; the seal's gasket, dogs and
    # the pressure equalising.
    outs = []
    for k in range(3):
        outs.append(Out(IW + numbered("airlock_slide", k), "machine", [
            Seg(m.servo("airlock_motor%d" % k, seconds=1.36, seed=301 + k, top_hz=(105.0, 112.0, 98.0)[k],
                        gear=13, up=0.22, down=0.28, grit=1.4, brake=0.6)),
            _slide(k, (0.05, 0.6), pitch=0.7, gain=-3, fade_in=0.08, fade_out=0.15, fx="highpass=f=80"),
            _slide(k + 3, (0.15, 0.6), pitch=0.75, gain=-4, at=0.62, fade_in=0.12, fade_out=0.1, fx="highpass=f=80"),
            Seg(rubber("metal_close_01.ogg"), pitch=(0.7, 0.66, 0.74)[k], gain=-5, at=1.36),
            Seg(kenney("impactSoft_heavy_%03d.ogg" % (k + 2)), pitch=0.7, gain=-9, at=1.36),
            Seg(m.boom("boom_as%d" % k, seconds=0.5, seed=311 + k, f_start=110.0, f_end=75.0, decay_s=0.2), gain=-10,
                at=1.36)],
            fx="highpass=f=40", tail_db=-50, max_fade=0.35))
    event("iw.airlock.slide", outs, "SFX", -2.0, unitSize=4.0, maxDistance=70.0, priority=55, limit=2,
          pitch=[0.98, 1.02], jitter=1.0)
    outs = []
    for k in range(3):
        outs.append(Out(IW + numbered("airlock_seal", k), "mechanism", [
            Seg(kenney("impactSoft_heavy_%03d.ogg" % k), pitch=0.7, gain=-2, fx="lowpass=f=1500"),
            Seg(m.boom("boom_ae%d" % k, seconds=0.5, seed=321 + k, f_start=85.0, f_end=58.0, decay_s=0.25), gain=-6),
            Seg(rpg("metalLatch.ogg"), pitch=(0.7, 0.74, 0.68)[k], gain=-4, at=0.12),
            Seg(rpg("metalLatch.ogg"), pitch=(0.66, 0.7, 0.72)[k], gain=-5, at=0.21),
            _clack(2 + k, pitch=0.8, gain=-6, at=0.3),
            Seg(bsb(("1696", "1694", "1695")[k]), 0.0, 0.7, pitch=1.2, gain=-14, at=0.36, fade_in=0.04,
                fade_out=0.3, fx="highpass=f=1200")],
            fx="highpass=f=40", tail_db=-50, max_fade=0.35))
    event("iw.airlock.seal", outs, "SFX", 0.0, unitSize=4.0, maxDistance=60.0, priority=55, limit=2,
          pitch=[0.98, 1.02], jitter=1.0)


def _iw_alarms(m):
    # Annunciators, sparse (the game repeats them): an electro-mechanical klaxon for hull critical, an electric bell
    # for fire aboard. Made in code: a motor-driven klaxon's rasp and a trembler bell's gong; no voice, no beeps.
    outs = []
    for k, (blasts, low, high, dia) in enumerate((
            (((0.0, 0.95), (1.3, 0.95)), 290.0, 470.0, 430.0),
            (((0.0, 1.1), (1.45, 0.8)), 270.0, 440.0, 400.0),
            (((0.0, 0.85), (1.15, 0.85), (2.3, 0.6)), 300.0, 490.0, 455.0))):
        outs.append(Out(IW + numbered("alarm_hull", k), "alarm", [
            Seg(m.klaxon("klaxon_%d" % k, blasts=blasts, seconds=blasts[-1][0] + blasts[-1][1] + 0.4, seed=331 + k,
                         low=low, high=high, diaphragm=dia))], fx="highpass=f=150,lowpass=f=9000", tail_db=-50,
            max_fade=0.3))
    event("iw.alarm.hull", outs, "SFX", 0.0, unitSize=14.0, maxDistance=160.0, priority=92, limit=2,
          pitch=[1.0, 1.0], jitter=0.0, occlude=False)
    outs = []
    for k, (secs, rate, f0) in enumerate(((2.4, 19.0, 1150.0), (2.2, 20.5, 1060.0), (2.6, 18.0, 1240.0))):
        outs.append(Out(IW + numbered("alarm_fire", k), "alarm", [
            Seg(m.bell("bell_%d" % k, seconds=secs, seed=341 + k, strike_hz=rate, f0=f0))],
            fx="highpass=f=200", tail_db=-50, max_fade=0.6))
    event("iw.alarm.fire", outs, "SFX", 0.0, unitSize=14.0, maxDistance=160.0, priority=92, limit=2,
          pitch=[1.0, 1.0], jitter=0.0, occlude=False)


# The firearm library's heavy guns (GUNS): shotguns for a cannon's body, rifles for its crack; near and mid.
SHOTGUNS_NEAR = ["CD/H_21P.wav", "Model 12/K_22P.wav", "Mossberg/N_30P.wav", "Nova/O_21P.wav"]
SHOTGUNS_MID = ["CD/H_16P.wav", "Model 12/K_17P.wav", "Mossberg/N_26P.wav", "Nova/O_17P.wav"]
RIFLES_NEAR = ["1917/B_24P.wav", "Mosin Nagant/M_21P.wav", "Arisaka/E_25P.wav", "Tikka/W_29P.wav",
               "Savage 10 .300 Blackout/T_27P.wav"]
RIFLES_MID = ["1917/B_16P.wav", "Mosin Nagant/M_26P.wav", "Tikka/W_24P.wav", "Savage 10 .300 Blackout/T_17P.wav"]


def _first_shot(name, which=0, length=1.5):
    """A shot of the firearm library, from its attack (so a crack and a body recorded apart start together)."""
    p = gun(name)
    found = shots(p, which + 1, length)
    s, _ = found[min(which, len(found) - 1)]
    return p, _onset(p, max(0.0, s - 0.05), 0.3)


def _iw_siege(m, hull_ir, ir):
    # The mechanism at the mount: the sear's sharp release, the barrel's 0.48 m of recoil in 80 ms (steel sliding
    # fast), the buffer's thud with the cradle ringing, the oil's hiss, and a short rattle.
    outs = []
    hammers = [("1994", 0.0, 0.2), ("1995", 0.04, 0.25), ("1992", 0.0, 0.2), ("1993", 0.04, 0.2), ("1995", 0.21, 0.2)]
    for k in range(5):
        sid, s0, d = hammers[k]
        outs.append(Out(IW + numbered("siege_mechanism", k), "impact", [
            Seg(bsb(sid), s0, d, pitch=(0.55, 0.6, 0.52, 0.58, 0.56)[k], gain=-2, fx="highpass=f=200"),
            Seg(rpg("metalLatch.ogg"), pitch=(0.75, 0.7, 0.8, 0.72, 0.78)[k], gain=-6),
            Seg(m.strike("sear_%d" % k, seconds=0.4, seed=351 + k, f0=(560.0, 600.0, 520.0, 640.0, 580.0)[k],
                         decay_s=0.22, hardness=0.9), gain=-6),
            Seg(m.hiss("recoil_%d" % k, seconds=0.16, seed=361 + k, attack=0.004, hold=0.05, decay_s=0.06, lo=500.0,
                       hi=9000.0, rough=0.5, sweep=(3800.0, 900.0), q=1.5), gain=-6, at=0.005),
            _slide(k, (0.5, 0.6), pitch=1.4, gain=-12, at=0.0, fade_in=0.01, fade_out=0.02, fx="highpass=f=300"),
            Seg(kenney("impactSoft_heavy_%03d.ogg" % k), pitch=0.5, gain=-2, at=0.08, fx="lowpass=f=1200"),
            Seg(m.boom("buffer_%d" % k, seconds=0.6, seed=371 + k, f_start=82.0, f_end=52.0, decay_s=0.3), gain=-4,
                at=0.08),
            Seg(bsb("3590"), (1.18, 2.22, 5.62, 9.82, 12.5)[k], 0.45, pitch=0.45, gain=-20, at=0.08, fade_out=0.3,
                fx="lowpass=f=4000"),
            Seg(m.strike("cradle_%d" % k, seconds=1.2, seed=381 + k, f0=(178.0, 196.0, 165.0, 205.0, 185.0)[k],
                         decay_s=0.9, hardness=0.5), gain=-8, at=0.08),
            Seg(m.hiss("buffer_oil_%d" % k, seconds=0.5, seed=391 + k, attack=0.01, hold=0.05, decay_s=0.3, lo=1200.0,
                       hi=6000.0), gain=-14, at=0.09),
            Seg(m.rattle_burst("rattle_sm%d" % k, seconds=0.6, seed=401 + k, rate=50.0, f0=1.1, decay_s=0.35),
                gain=-12, at=0.1),
            Seg(rubber("keys_0%d.ogg" % (k + 1)), pitch=0.8, gain=-18, at=0.12)],
            fx="highpass=f=40", tail_db=-50, max_fade=0.4))
    event("siege.fire.mechanism", outs, "SFX", 0.0, unitSize=6.0, maxDistance=120.0, priority=82, limit=4,
          pitch=[0.97, 1.03], jitter=1.0)

    # The report, outside: a cannon's heavy body (shotguns slowed), a rifle's crack on top, an explosion's blast,
    # a low push of air and the open air's rolling echo; the middle and far layers as heard across the world.
    near = []
    for k in range(5):
        sg, sgs = _first_shot(SHOTGUNS_NEAR[k % 4], k // 4)
        rf, rfs = _first_shot(RIFLES_NEAR[k])
        near.append(Out(IW + numbered("siege_report_near", k), "explosion", [
            Seg(sg, sgs, 1.6, pitch=(0.55, 0.52, 0.58, 0.5, 0.56)[k], ir=ir[k % 2], wet=0.3, dry=1.0,
                post="lowpass=f=7000"),
            Seg(rf, rfs, 1.0, pitch=0.8, gain=-8, fx="highpass=f=200"),
            Seg(bsb(("1808", "1807")[k % 2]), _onset(bsb(("1808", "1807")[k % 2]), 0.0, 0.2),
                pitch=(0.65, 0.7, 0.62, 0.68, 0.66)[k], gain=-5, fx="highpass=f=35"),
            Seg(m.boom("siege_sub_%d" % k, seconds=1.4, seed=411 + k, f_start=66.0, f_end=41.0, decay_s=0.9),
                gain=-3),
            Seg(bsb("1023"), 0.35, 4.0, fade_in=0.3, gain=-10, at=0.12, fx="lowpass=f=2500")],
            fx="highpass=f=30,equalizer=f=70:t=q:w=1:g=2", tail_db=-55, max_fade=1.2))
    mid = []
    for k in range(4):
        sg, sgs = _first_shot(SHOTGUNS_MID[k], 0, 2.5)
        mid.append(Out(IW + numbered("siege_report_mid", k), "distant", [
            Seg(sg, sgs, 2.5, pitch=(0.55, 0.52, 0.58, 0.5)[k], fx="lowpass=f=4500", ir=ir[k % 2], wet=0.4, dry=1.0,
                post="lowpass=f=4500"),
            Seg(bsb("1806"), pitch=0.7, gain=-6, fx="lowpass=f=3000"),
            Seg(m.boom("siege_sub_mid_%d" % k, seconds=1.4, seed=421 + k, f_start=56.0, f_end=41.0, decay_s=1.0),
                gain=-8),
            Seg(bsb("1023"), 0.35, 4.0, fade_in=0.3, gain=-6, at=0.2, fx="lowpass=f=1800")],
            fx="highpass=f=30", tail_db=-55, max_fade=1.2))
    far = []
    for k in range(3):
        sg, sgs = _first_shot(SHOTGUNS_MID[k + 1], 0, 2.5)
        far.append(Out(IW + numbered("siege_report_far", k), "distant", [
            Seg(sg, sgs, 2.5, pitch=0.52, fx="lowpass=f=1100,highpass=f=40,afade=t=in:d=0.008,"
                                             "acompressor=threshold=-38dB:ratio=5:attack=0.5:release=500:makeup=12",
                ir=ir[k % 2], wet=0.45, dry=1.0, post="lowpass=f=1300"),
            Seg(bsb("1023"), 0.0, 4.5, gain=-3, fx="highpass=f=30,lowpass=f=1200"),
            Seg(m.boom("siege_sub_far_%d" % k, seconds=1.6, seed=431 + k, f_start=50.0, f_end=40.0, decay_s=1.2),
                gain=-10)],
            fx="highpass=f=28", tail_db=-55, max_fade=1.5))
    layered("siege.fire.report", [
        ("near", near, 0.0, 120.0, 50.0, 0.0, 25.0, 300.0),
        ("mid", mid, 120.0, 800.0, 150.0, -2.0, 80.0, 1200.0),
        ("far", far, 800.0, 3000.0, 300.0, -4.0, 250.0, 3200.0),
    ], "SFX", 0.0, priority=96, limit=6, delay=True, pitch=[0.97, 1.03], jitter=1.0)

    # Inside the ship: the shot transmitted through the structure, a muffled thump with the hull's steel answering,
    # then small rattles overhead and along the rails.
    outs = []
    for k in range(4):
        sg, sgs = _first_shot(SHOTGUNS_NEAR[k], 0)
        outs.append(Out(IW + numbered("siege_interior", k), "impact", [
            Seg(sg, sgs, 1.2, pitch=0.4, fx="lowpass=f=220,lowpass=f=220", ir=hull_ir, wet=0.4, dry=1.0),
            Seg(m.boom("siege_in_sub_%d" % k, seconds=1.2, seed=441 + k, f_start=60.0, f_end=42.0, decay_s=0.7),
                gain=-1),
            Seg(m.strike("hull_bong_%d" % k, seconds=1.6, seed=451 + k, f0=(96.0, 108.0, 88.0, 116.0)[k],
                         ratios=m.PLATE, decay_s=1.1, hardness=0.3), gain=-12, ir=hull_ir, wet=0.5, dry=1.0),
            Seg(m.rattle_burst("rattle_si%d" % k, seconds=1.2, seed=461 + k, rate=55.0, f0=(0.9, 1.0, 0.85, 1.1)[k],
                               decay_s=0.7), gain=-8, at=0.03),
            Seg(rubber("keys_0%d.ogg" % (k + 2)), pitch=0.9, gain=-16, at=0.06, fx="lowpass=f=6000"),
            Seg(bsb(("1771", "1773", "1775", "1772")[k]), 0.0, 1.0, pitch=0.8, gain=-18, at=0.09, fade_out=0.4,
                fx="lowpass=f=3500")],
            fx="highpass=f=28,lowpass=f=6000", tail_db=-52, max_fade=0.7))
    event("siege.fire.interior", outs, "SFX", 0.0, unitSize=30.0, maxDistance=120.0, priority=86, limit=3,
          pitch=[0.96, 1.04], jitter=1.0, occlude=False)

    # Recovery: the recuperator's oil pushing the barrel back to battery over 0.9 s, the slide, the firm stop.
    outs = []
    for k in range(4):
        outs.append(Out(IW + numbered("siege_recover", k), "mechanism", [
            Seg(m.hiss("recuperator_%d" % k, seconds=0.9, seed=471 + k, attack=0.12, hold=0.4, decay_s=0.3, lo=500.0,
                       hi=5000.0, rough=0.35, sweep=(700.0, 1500.0), q=1.6), gain=-6),
            _slide(k + 2, (0.05, 0.6), pitch=0.72, gain=-6, fade_in=0.1, fade_out=0.1, fx="highpass=f=120"),
            Seg(rubber("metal_close_01.ogg"), pitch=(0.6, 0.64, 0.57, 0.62)[k], gain=-3, at=0.84),
            Seg(kenney("impactSoft_heavy_%03d.ogg" % (k + 1)), pitch=0.65, gain=-9, at=0.84),
            Seg(m.strike("battery_%d" % k, seconds=0.6, seed=481 + k, f0=(300.0, 330.0, 285.0, 315.0)[k],
                         decay_s=0.3, hardness=0.65), gain=-6, at=0.84),
            Seg(bsb("1696"), 0.0, 0.3, pitch=1.4, gain=-18, at=0.87, fade_out=0.15, fx="highpass=f=1500")],
            fx="highpass=f=45", tail_db=-50, max_fade=0.3, length=1.25))
    event("siege.recover", outs, "SFX", -2.0, unitSize=5.0, maxDistance=80.0, priority=70, limit=4,
          pitch=[0.98, 1.02], jitter=1.0)

    # The latch: the barrel caught in battery.
    outs = []
    for k in range(4):
        outs.append(Out(IW + numbered("siege_latch", k), "mechanism", [
            _clack(k % 2, pitch=(0.7, 0.78, 0.74, 0.72)[k], gain=0),
            Seg(rpg("metalLatch.ogg"), pitch=(0.55, 0.58, 0.52, 0.6)[k], gain=-5),
            Seg(m.strike("latch_%d" % k, seconds=0.5, seed=491 + k, f0=(450.0, 520.0, 480.0, 500.0)[k], decay_s=0.3,
                         hardness=0.8), gain=-6),
            Seg(m.boom("latch_th%d" % k, seconds=0.3, seed=501 + k, f_start=110.0, f_end=75.0, decay_s=0.12),
                gain=-10)],
            fx="highpass=f=50", tail_db=-50, max_fade=0.25))
    event("siege.latch", outs, "SFX", 0.0, unitSize=4.0, maxDistance=60.0, priority=66, limit=4,
          pitch=[0.97, 1.03], jitter=1.0)


def _iw_loader(m):
    # Each sound fits its phase (IRON_WAKE.md §6.6); several turrets run them at once, so each has variants. All at
    # the loader's scale: breech blocks and trays of steel, servos, a chain rammer, a ratchet drum, cassettes.
    def loader(key, rel, n, make, phase, volume=0.0, cat="mechanism"):
        outs = [Out(IW + numbered(rel, k), cat, make(k), fx="highpass=f=60", tail_db=-48, max_fade=0.12,
                    length=phase + 0.15) for k in range(n)]
        event(key, outs, "SFX", volume, unitSize=3.0, maxDistance=45.0, priority=62, limit=8, pitch=[0.97, 1.03],
              jitter=1.0)

    impm = ["impactMetal_medium_%03d.ogg" % i for i in range(5)]
    impp = ["impactPlate_medium_%03d.ogg" % i for i in range(5)]
    # Breech open (0.45 s): unlocked, the hydraulics push, the block slides down to its stop.
    loader("loader.breech.open", "loader_breech_open", 4, lambda k: [
        _clack(2 + k, pitch=0.72, gain=-2),
        Seg(m.hiss("breech_o%d" % k, seconds=0.4, seed=511 + k, attack=0.02, hold=0.15, decay_s=0.2, lo=900.0,
                   hi=6000.0, sweep=(2600.0, 1300.0)), gain=-10, at=0.02),
        _slide(k, (0.3, 0.6), pitch=0.85, gain=-6, at=0.04, fade_in=0.05, fade_out=0.05, fx="highpass=f=150"),
        Seg(kenney(impm[k]), pitch=0.6, gain=-3, at=0.37),
        Seg(m.boom("breech_ob%d" % k, seconds=0.25, seed=521 + k, f_start=125.0, f_end=85.0, decay_s=0.12), gain=-12,
            at=0.37)], 0.45)
    # The arm's swing (0.80 s, and a shorter 0.55 s): its servo, gears and brake.
    loader("loader.arm.swing", "loader_arm_swing", 4, lambda k: [
        Seg(m.servo("arm_%d" % k, seconds=0.78, seed=531 + k, top_hz=(132.0, 144.0, 126.0, 150.0)[k],
                    gear=(11, 12, 11, 13)[k])),
        _creak(8 + k, 0.4, pitch=1.4, gain=-26, at=0.2, fade_in=0.1, fade_out=0.1, fx="highpass=f=400")], 0.80, -6.0)
    loader("loader.arm.swing.short", "loader_arm_swing_short", 3, lambda k: [
        Seg(m.servo("arm_short_%d" % k, seconds=0.53, seed=541 + k, top_hz=(128.0, 140.0, 134.0)[k], gear=11,
                    up=0.14, down=0.17))], 0.55, -6.0)
    # The rack's index (in the fetch's 0.80 s): solenoid, the pawl over the ratchet, the detent.
    loader("loader.rack.index", "loader_rack_index", 4, lambda k: [
        Seg(m.ratchet_run("rack_%d" % k, clicks=(5, 4, 6, 5)[k], spacing=(0.045, 0.05, 0.04, 0.048)[k],
                          seed=551 + k)),
        Seg(bsb(("1986", "1987", "1986", "1987")[k]), 0.04, 0.42, pitch=(0.8, 0.85, 0.78, 0.82)[k], gain=-12,
            at=0.03)], 0.60)
    # Transfer (0.40 s): the pusher's breath of air, the rocket sliding onto the tray, landing in it.
    loader("loader.transfer", "loader_transfer", 4, lambda k: [
        Seg(m.puff("pusher_%d" % k, seconds=0.25, seed=561 + k, valve=0.6, size=0.35), gain=-12),
        _slide(k + 3, (0.25, 0.58), pitch=1.0, gain=-5, at=0.02, fade_in=0.04, fade_out=0.05, fx="highpass=f=150"),
        Seg(kenney(impp[k]), pitch=0.7, gain=-3, at=0.3),
        Seg(kenney("impactSoft_medium_%03d.ogg" % k), pitch=0.8, gain=-8, at=0.3)], 0.40)
    # Ram (0.50 s): the chain rammer runs out and drives the rocket home; it ends as the rocket seats.
    loader("loader.ram", "loader_ram", 4, lambda k: [
        Seg(m.chain_run("ram_%d" % k, seconds=0.46, seed=571 + k, rate=(48.0, 72.0), size=(1.0, 1.05, 0.95, 1.1)[k],
                        seat=1.0)),
        _clack(k, pitch=0.62, gain=-8, at=0.46),
        Seg(kenney("impactMetal_heavy_%03d.ogg" % k), pitch=0.55, gain=-8, at=0.46)], 0.50 + 0.2)
    # Retract (0.40 s): the rammer folds back, lighter; the tray swings clear and clicks.
    loader("loader.retract", "loader_retract", 4, lambda k: [
        Seg(m.chain_run("retract_%d" % k, seconds=0.34, seed=581 + k, rate=(62.0, 36.0), size=0.9, motor=0.8,
                        seat=0.0)),
        Seg(rpg("metalClick.ogg"), 0.2, 0.2, pitch=0.8, gain=-6, at=0.35)], 0.40, -4.0)
    # Breech close (0.35 s): pushed up, locked with a clank.
    loader("loader.breech.close", "loader_breech_close", 4, lambda k: [
        Seg(m.hiss("breech_c%d" % k, seconds=0.3, seed=591 + k, attack=0.015, hold=0.12, decay_s=0.12, lo=900.0,
                   hi=6000.0, sweep=(1300.0, 2600.0)), gain=-10),
        _slide(k + 1, (0.35, 0.6), pitch=1.1, gain=-7, fade_in=0.03, fade_out=0.04, fx="highpass=f=150"),
        _clack(k % 2, pitch=0.82, gain=0, at=0.26),
        Seg(m.strike("breech_cl%d" % k, seconds=0.45, seed=601 + k, f0=(560.0, 600.0, 530.0, 580.0)[k], decay_s=0.3,
                     hardness=0.8), gain=-5, at=0.26),
        Seg(m.boom("breech_cb%d" % k, seconds=0.3, seed=611 + k, f_start=120.0, f_end=80.0, decay_s=0.12), gain=-10,
            at=0.26)], 0.35 + 0.25)

    # The defense mount's cassette.
    loader("loader.cassette.release", "loader_cassette_release", 4, lambda k: [
        Seg(rpg("metalLatch.ogg"), pitch=(0.68, 0.72, 0.65, 0.7)[k], gain=-2),
        Seg(rpg("metalLatch.ogg"), pitch=(0.64, 0.67, 0.7, 0.62)[k], gain=-4, at=0.07),
        Seg(m.strike("cas_rel_%d" % k, seconds=0.3, seed=921 + k, f0=(820.0, 870.0, 790.0, 850.0)[k], decay_s=0.12,
                     hardness=0.8), gain=-8),
        Seg(m.boom("cas_rel_th%d" % k, seconds=0.2, seed=925 + k, f_start=150.0, f_end=110.0, decay_s=0.06), gain=-12,
            at=0.07),
        Seg(rubber("metal_spring_0%d.ogg" % (k % 2 + 1)), pitch=0.8, gain=-12, at=0.1)], 0.30)
    loader("loader.cassette.eject", "loader_cassette_eject", 4, lambda k: [
        _slide(k + 3, (0.35, 0.6), pitch=1.2, gain=-6, fade_in=0.03, fade_out=0.04, fx="highpass=f=150"),
        Seg(rubber("bfh1_metal_falling_0%d.ogg" % (k % 3 + 2)), pitch=0.75, gain=-2, at=0.18),
        Seg(kenney("impactMetal_heavy_%03d.ogg" % (k + 1)), pitch=0.65, gain=-2, at=0.34),
        Seg(m.strike("chute_%d" % k, seconds=0.6, seed=621 + k, f0=(240.0, 260.0, 225.0, 250.0)[k], ratios=m.PLATE,
                     decay_s=0.4, hardness=0.6), gain=-9, at=0.34)], 0.45 + 0.2)
    loader("loader.cassette.raise", "loader_cassette_raise", 3, lambda k: [
        Seg(m.chain_run("hoist_%d" % k, seconds=0.62, seed=631 + k, rate=(30.0, 40.0), size=1.2, motor=1.5)),
        Seg(m.servo("hoist_motor_%d" % k, seconds=0.62, seed=641 + k, top_hz=(95.0, 102.0, 90.0)[k], gear=7,
                    brake=0.0), gain=-4),
        Seg(kenney(impp[k + 1]), pitch=0.7, gain=-5, at=0.63)], 0.70, -3.0)
    loader("loader.cassette.seat", "loader_cassette_seat", 4, lambda k: [
        _slide(k, (0.3, 0.6), pitch=1.05, gain=-5, fade_in=0.03, fade_out=0.04, fx="highpass=f=150")] + [
        Seg(m.strike("tube_%d_%d" % (k, j), seconds=0.25, seed=651 + 10 * k + j, f0=(700.0, 760.0, 730.0)[j],
                     decay_s=0.12, hardness=0.5), gain=-9, at=0.2 + 0.05 * j) for j in range(3)] + [
        Seg(kenney(impp[(k + 2) % 5]), pitch=0.75, gain=-2, at=0.36)], 0.45)
    loader("loader.cassette.lock", "loader_cassette_lock", 4, lambda k: [
        Seg(rpg("metalLatch.ogg"), pitch=(0.74, 0.7, 0.78, 0.72)[k], gain=-3, at=0.02),
        Seg(rpg("metalLatch.ogg"), pitch=(0.7, 0.73, 0.68, 0.76)[k], gain=-3, at=0.07),
        Seg(m.strike("cas_lock_%d" % k, seconds=0.3, seed=931 + k, f0=(760.0, 800.0, 735.0, 780.0)[k], decay_s=0.15,
                     hardness=0.8), gain=-6, at=0.16),
        Seg(m.boom("cas_lock_th%d" % k, seconds=0.2, seed=935 + k, f_start=140.0, f_end=100.0, decay_s=0.07), gain=-10,
            at=0.16),
        Seg(bsb("1988"), 0.27, 0.2, pitch=0.7, gain=-6, at=0.16)], 0.30)


def _iw_defense(m, ir):
    # The defense mount's report: sharper and smaller than the siege gun's and its own: a rifle's hard crack, the
    # launch tube's steel ring and the rocket's hiss tearing out of it ("tchang-fsss"), a small blast and a short
    # push of air. Rippled three at a time a quarter second apart.
    near = []
    for k in range(6):
        rf, rfs = _first_shot(RIFLES_NEAR[(k + 1) % 5], k // 5)
        near.append(Out(IW + numbered("defense_report_near", k), "gunshot", [
            Seg(rf, rfs, 0.9, pitch=(0.92, 0.95, 0.9, 0.97, 0.93, 0.9)[k], gain=-1),
            Seg(m.strike("tube_ring_%d" % k, seconds=0.7, seed=661 + k, f0=(420.0, 455.0, 395.0, 470.0, 440.0,
                                                                            410.0)[k],
                         ratios=m.BAR, decay_s=0.45, hardness=0.85), gain=-10),
            Seg(m.hiss("launch_hiss_%d" % k, seconds=0.45, seed=671 + k, attack=0.003, hold=0.04, decay_s=0.22,
                       lo=1200.0, hi=9000.0, rough=0.5, sweep=(6000.0, 2200.0), q=1.3), gain=-7),
            Seg(bsb("1340"), 0.02, 0.4, pitch=1.6, gain=-12, fade_out=0.15, fx="highpass=f=800"),
            Seg(bsb("1807"), _onset(bsb("1807"), 0.0, 0.2), pitch=0.9, gain=-10, fx="highpass=f=60"),
            Seg(m.boom("def_sub_%d" % k, seconds=0.4, seed=681 + k, f_start=96.0, f_end=60.0, decay_s=0.18),
                gain=-7)],
            fx="highpass=f=45", tail_db=-55, max_fade=0.5))
    mid = []
    for k in range(4):
        rf, rfs = _first_shot(RIFLES_MID[k], 0, 2.0)
        mid.append(Out(IW + numbered("defense_report_mid", k), "distant", [
            Seg(rf, rfs, 2.0, pitch=0.92, fx="lowpass=f=6000", ir=ir[k % 2], wet=0.25, dry=1.0),
            Seg(m.hiss("launch_hiss_mid_%d" % k, seconds=0.5, seed=691 + k, attack=0.004, hold=0.03, decay_s=0.25,
                       lo=900.0, hi=5000.0, rough=0.4), gain=-12)],
            fx="highpass=f=50", tail_db=-55, max_fade=0.8))
    far = []
    for k in range(3):
        rf, rfs = _first_shot(RIFLES_MID[k + 1], 0, 2.2)
        far.append(Out(IW + numbered("defense_report_far", k), "distant", [
            Seg(rf, rfs, 2.2, pitch=0.9, fx="lowpass=f=1300,highpass=f=50,afade=t=in:d=0.008,"
                                           "acompressor=threshold=-38dB:ratio=5:attack=0.5:release=500:makeup=12",
                ir=ir[k % 2], wet=0.4, dry=1.0, post="lowpass=f=1600")],
            fx="highpass=f=45", tail_db=-55, max_fade=1.0))
    layered("defense.fire.report", [
        ("near", near, 0.0, 70.0, 30.0, 0.0, 14.0, 180.0),
        ("mid", mid, 70.0, 450.0, 100.0, -3.0, 45.0, 800.0),
        ("far", far, 450.0, 1800.0, 200.0, -6.0, 140.0, 1900.0),
    ], "SFX", 2.0, priority=92, limit=9, delay=True, pitch=[0.96, 1.04], jitter=1.0)
    # Its mechanism, per barrel: the firing solenoid's snap, the short recoil's knock, a ping of the tube.
    outs = []
    for k in range(5):
        outs.append(Out(IW + numbered("defense_mechanism", k), "mechanism", [
            Seg(m.strike("def_sol_%d" % k, seconds=0.2, seed=701 + k, f0=(1200.0, 1300.0, 1150.0, 1250.0, 1350.0)[k],
                         decay_s=0.06, hardness=0.9), gain=-4),
            Seg(bsb(("1992", "1993", "1994", "1992", "1995")[k]), (0.0, 0.04, 0.0, 0.0, 0.04)[k], 0.18,
                pitch=0.8, gain=-4, fx="highpass=f=300"),
            Seg(kenney("impactMetal_light_%03d.ogg" % k), pitch=0.7, gain=-5, at=0.02),
            Seg(m.boom("def_kn%d" % k, seconds=0.3, seed=711 + k, f_start=140.0, f_end=90.0, decay_s=0.1), gain=-9,
                at=0.02),
            Seg(m.strike("def_ping_%d" % k, seconds=0.45, seed=721 + k, f0=(760.0, 820.0, 730.0, 790.0, 845.0)[k],
                         ratios=m.BAR, decay_s=0.25, hardness=0.7), gain=-12, at=0.02)],
            fx="highpass=f=60", tail_db=-50, max_fade=0.2))
    event("defense.fire.mechanism", outs, "SFX", 0.0, unitSize=4.0, maxDistance=80.0, priority=72, limit=6,
          pitch=[0.97, 1.03], jitter=1.0)


def _iw_turret(m):
    # Traverse: electric drives on a slewing ring, slow (the siege battery's 24°/s) and fast (the defense mount's
    # 80°/s), crossfaded by slew rate; the stop at a limit.
    for key, rot, gear, brush, rumble in (("slow", 38.0, 23, 0.8, 1.0), ("fast", 62.0, 23, 1.4, 1.5)):
        event("turret.traverse." + key, [Out(IW + "turret_traverse_%s.ogg" % key, "loop", [
            Seg(m.motor_loop("traverse_" + key, rot_hz=rot, seconds=8.0, pre=3.0, seed=731, gear=gear, poles=4,
                             brush=brush, ring_rumble=rumble, ticks=3.0 if key == "fast" else 1.5))],
            fx="highpass=f=40", loop=(8.0, 0.0, 3.0))],
            "SFX", 0.0, unitSize=5.0, maxDistance=90.0, priority=55, limit=8, pitch=[1.0, 1.0], jitter=0.0)
    outs = []
    for k in range(4):
        outs.append(Out(IW + numbered("turret_limit", k), "mechanism", [
            Seg(kenney("impactMetal_heavy_%03d.ogg" % k), pitch=0.5, gain=-2),
            _clack(k % 2, pitch=0.65, gain=-5),
            Seg(m.strike("limit_%d" % k, seconds=0.9, seed=741 + k, f0=(210.0, 230.0, 195.0, 220.0)[k], decay_s=0.6,
                         hardness=0.6), gain=-6),
            Seg(m.boom("limit_th%d" % k, seconds=0.5, seed=751 + k, f_start=88.0, f_end=55.0, decay_s=0.25), gain=-6),
            Seg(m.rattle_burst("rattle_tl%d" % k, seconds=0.6, seed=761 + k, rate=30.0, decay_s=0.3), gain=-16,
                at=0.04)],
            fx="highpass=f=45", tail_db=-50, max_fade=0.35))
    event("turret.limit", outs, "SFX", 0.0, unitSize=5.0, maxDistance=90.0, priority=60, limit=6,
          pitch=[0.97, 1.03], jitter=1.0)


def _iw_rockets(m, ir):
    # The motor lighting a few metres out, a proper flight loop (a rocket motor's roar and crackle), and the
    # incendiary's burst: a small blast and the fuel catching with a whoomp, splashing and crackling.
    outs = []
    for k in range(4):
        outs.append(Out(IW + numbered("rocket_ignite", k), "impact", [
            Seg(m.rocket_ignite("ignite_%d" % k, seconds=1.1, seed=771 + k, size=(1.0, 0.85, 1.15, 0.95)[k])),
            Seg(bsb("1340"), 0.0, 0.9, pitch=(1.25, 1.35, 1.2, 1.3)[k], gain=-10, fade_out=0.4,
                fx="highpass=f=300")],
            fx="highpass=f=40", tail_db=-50, max_fade=0.4))
    event("rocket.ignite", outs, "SFX", 0.0, unitSize=10.0, maxDistance=400.0, priority=80, limit=8,
          pitch=[0.95, 1.05], jitter=1.0, delay=True)
    roar = [m.looped(bsb("1342"), 70.0 + 7.0 * k, 6.0, 1.5, 8.01, pitch=1.5) for k in range(2)]
    event("rocket.flight", [Out(IW + "rocket_flight_%02d.ogg" % (k + 1), "engine", [
        Seg(m.rocket_loop("rocket_%d" % k, seconds=6.0, pre=2.0, seed=781 + k, body=(1.0, 0.85)[k])),
        Seg(roar[k], gain=-5, fx="highpass=f=150,lowpass=f=9000")], fx="highpass=f=40", loop=(6.0, 0.0, 2.0))
        for k in range(2)],
        "SFX", 0.0, unitSize=12.0, maxDistance=500.0, priority=75, limit=12, pitch=[1.0, 1.0], jitter=0.0)
    whoosh = [("1341", 0.07, 1.4), ("1342", 0.29, 1.4), ("1344", 0.05, 1.3), ("1340", 0.0, 1.5)]
    near = []
    for k in range(4):
        sid, s0, d = whoosh[k]
        near.append(Out(IW + numbered("incendiary_near", k), "explosion", [
            Seg(bsb(("1807", "1808")[k % 2]), pitch=(0.95, 0.9, 1.0, 0.92)[k], gain=-2),
            Seg(scifi("explosionCrunch_%03d.ogg" % k), pitch=0.85, gain=-10, fx="lowpass=f=6000"),
            Seg(m.whump("inc_whump_%d" % k, seconds=1.3, seed=791 + k, size=(1.0, 1.1, 0.9, 1.05)[k]), gain=-1),
            Seg(bsb(sid), s0, d, pitch=0.8, gain=-3, fade_out=0.5),
            Seg(bsb(("1529", "1531", "1533", "1530")[k]), 0.0, 0.5, pitch=0.6, gain=-14, at=0.05, fade_out=0.2),
            Seg(bsb("0987"), 3.4 + 2.0 * k, 1.6, gain=-12, at=0.35, fade_in=0.2, fade_out=0.6)],
            fx="highpass=f=35", tail_db=-55, max_fade=1.0))
    mid = []
    for k in range(3):
        sid, s0, d = whoosh[k + 1]
        mid.append(Out(IW + numbered("incendiary_mid", k), "distant", [
            Seg(bsb(("1806", "1807", "1808")[k]), pitch=0.85, gain=-3, fx="lowpass=f=3500"),
            Seg(m.whump("inc_whump_mid_%d" % k, seconds=1.3, seed=801 + k), gain=-2, fx="lowpass=f=1500"),
            Seg(bsb(sid), s0, d, pitch=0.8, gain=-6, fade_out=0.5, fx="lowpass=f=3000", ir=ir[k % 2], wet=0.35,
                dry=1.0),
            Seg(bsb("1023"), 0.35, 3.0, fade_in=0.3, gain=-10, at=0.15, fx="lowpass=f=2000")],
            fx="highpass=f=35", tail_db=-55, max_fade=1.0))
    far = []
    for k in range(2):
        far.append(Out(IW + numbered("incendiary_far", k), "distant", [
            Seg(bsb("1806"), pitch=(0.8, 0.85)[k], gain=-2, fx="lowpass=f=1200"),
            Seg(m.whump("inc_whump_far_%d" % k, seconds=1.5, seed=811 + k, size=1.2), gain=-4, fx="lowpass=f=900",
                ir=ir[k], wet=0.4, dry=1.0),
            Seg(bsb("1023"), 0.0, 3.5, gain=-4, fx="highpass=f=30,lowpass=f=1100")],
            fx="highpass=f=30", tail_db=-55, max_fade=1.2))
    layered("rocket.incendiary.burst", [
        ("near", near, 0.0, 60.0, 30.0, 0.0, 10.0, 150.0),
        ("mid", mid, 60.0, 400.0, 100.0, -2.0, 40.0, 600.0),
        ("far", far, 400.0, 1500.0, 150.0, -4.0, 120.0, 1600.0),
    ], "SFX", -4.0, priority=90, limit=6, delay=True, pitch=[0.95, 1.05], jitter=1.0)


def _iw_fire(m):
    # Fire (IRON_WAKE.md §7): catching with a whoomp, then burning, small (crackle) or large (roar), as loops.
    starts = [("1340", 0.0, 1.6), ("1341", 0.0, 1.5), ("1342", 0.25, 1.4), ("1344", 0.0, 1.4)]
    outs = []
    for k in range(4):
        sid, s0, d = starts[k]
        outs.append(Out(IW + numbered("fire_ignite", k), "impact", [
            Seg(m.whump("fire_whump_%d" % k, seconds=1.2, seed=821 + k, size=(0.9, 1.0, 1.1, 0.95)[k]), gain=-2),
            Seg(bsb(sid), s0, d, gain=-1, fade_out=0.5),
            Seg(bsb("0988"), 6.4 + 3.1 * k, 1.4, gain=-12, at=0.3, fade_in=0.15, fade_out=0.5)],
            fx="highpass=f=35", tail_db=-50, max_fade=0.6))
    event("fire.ignite", outs, "SFX", 0.0, unitSize=8.0, maxDistance=150.0, priority=70, limit=6,
          pitch=[0.95, 1.05], jitter=1.0)
    event("fire.patch.small", [Out(IW + "fire_patch_small.ogg", "loop", [
        Seg(m.looped(bsb("1341"), 2.0, 20.0, 3.0, 23.01, pitch=1.05), gain=0.0, fx="highpass=f=80"),
        Seg(m.looped(bsb("2857"), 8.0, 20.0, 3.0, 23.01), gain=-4, fx="highpass=f=200"),
        Seg(m.fire_roar("fire_small_roar", seconds=20.0, pre=3.0, seed=831, low=420.0), gain=-12)],
        fx="highpass=f=45", loop=(20.0, 0.0, 3.0))],
        "SFX", 0.0, unitSize=4.0, maxDistance=60.0, priority=45, limit=12, pitch=[1.0, 1.0], jitter=0.0)
    event("fire.patch.large", [Out(IW + "fire_patch_large.ogg", "engine", [
        Seg(m.looped(bsb("1342"), 10.0, 24.0, 3.0, 27.01), gain=0.0, fx="highpass=f=40"),
        Seg(m.looped(bsb("0989"), 30.0, 24.0, 3.0, 27.01), gain=-6, fx="highpass=f=150"),
        Seg(m.fire_roar("fire_large_roar", seconds=24.0, pre=3.0, seed=841, low=280.0), gain=-6)],
        fx="highpass=f=30", loop=(24.0, 0.0, 3.0))],
        "SFX", 0.0, unitSize=6.0, maxDistance=90.0, priority=50, limit=12, pitch=[1.0, 1.0], jitter=0.0)


# The listener's spaces (IRON_WAKE.md §10): reverb presets for AudioBuses.SetSpace, in the terms of Godot's
# AudioEffectReverb, written into sounds.json (a preset tuned by hand there is kept). `structureDb` is how loud
# structure-borne sounds (siege.fire.interior, the hull's strain) are heard in the space.
SPACES = {
    "outside": {"roomSize": 0.2, "damping": 0.7, "spread": 0.8, "hipass": 0.15, "dry": 1.0, "wet": 0.02,
                "predelayMs": 12.0, "predelayFeedback": 0.2, "structureDb": -80.0,
                "note": "Open air: no tail of its own (today's SetSpace(0))."},
    "deck": {"roomSize": 0.78, "damping": 0.28, "spread": 1.0, "hipass": 0.08, "dry": 0.92, "wet": 0.34,
             "predelayMs": 22.0, "predelayFeedback": 0.32, "structureDb": 0.0, "rooms": ["deck-a", "bay", "deck-b"],
             "note": "Deck A, the bay and the mezzanine as one connected steel room: long, bright, with flutter "
                     "between parallel plates."},
    "bridge": {"roomSize": 0.22, "damping": 0.85, "spread": 0.5, "hipass": 0.3, "dry": 1.0, "wet": 0.07,
               "predelayMs": 3.0, "predelayFeedback": 0.05, "structureDb": -6.0, "rooms": ["bridge"],
               "note": "The glazed bridge, insulated and padded: small and dry."},
}


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
    "metal-pickaxe": HEAVY, "salvaged-axe": HEAVY, "wooden-spear": HEAVY, "stone-spear": HEAVY, "machete": MELEE,
    "bow": {"draw": "bow.draw", "release": "bow.release", "flight": "arrow.flight", "impact": "arrow.impact",
            "impactFlesh": "arrow.impact.flesh", "hitMarker": "hit.marker", "headshot": "hit.headshot",
            "kill": "hit.kill"},
    "arrow": {"flight": "arrow.flight", "impact": "arrow.impact", "impactFlesh": "arrow.impact.flesh",
              "pickup": "gather.pickup"},
    "pistol": gun_roles("pistol"), "rifle": gun_roles("rifle"),
    "silencer": {"shotPistol": "pistol.shot.silenced", "shotRifle": "rifle.shot.silenced"},
    "bandage": {"use": "survival.bandage"}, "medical-injector": {"use": "survival.inject"},
    "red-berries": {"use": "survival.eat"}, "blue-berries": {"use": "survival.eat"},
    "wild-tuber": {"use": "survival.eat"}, "roasted-tuber": {"use": "survival.eat"},
    # G7 animals (GROUND.md §6.9): meat is eaten like any food; the bone knife and club are swung like the other blades.
    **{"%s-%s" % (state, meat): {"use": "survival.eat"} for state in ("raw", "cooked", "burnt")
       for meat in ("venison", "pork", "wolf-meat", "bear-meat")},
    "bone-knife": MELEE, "bone-club": HEAVY,
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
    "recycler": {"open": "box.open", "close": "box.close", "start": "recycler.start", "loop": "recycler.loop", "stop": "recycler.stop"},
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
    "rocket-launcher": {"shot": "launcher.shot", "magOut": "launcher.open", "magIn": "launcher.load",
                        "rack": "launcher.lock", "dryFire": "gun.dryfire", "hitMarker": "hit.marker",
                        "kill": "hit.kill"},
    # IW4 (IRON_WAKE.md §10.1): every rocket's motor catching where it leaves and its roar on it until it stops (the
    # rockets' view loops "flight" on the rocket); the incendiary rocket's burst, its fuel catching and its fire (a
    # 3.5 m pool: the large loop).
    "rocket": {"ignite": "rocket.ignite", "flight": "rocket.flight", "explode": "explosion", "debris": "explosion.debris"},
    "rocket-hv": {"ignite": "rocket.ignite", "flight": "rocket.flight", "explode": "explosion", "debris": "explosion.debris"},
    "rocket-incendiary": {"ignite": "rocket.ignite", "flight": "rocket.flight", "explode": "rocket.incendiary.burst",
                          "fireStart": "fire.ignite", "fire": "fire.patch.large"},
    "rifle-ammo-explosive": {"explode": "impact.metal"},
    # Door items (models.json) and door kinds (building.json).
    # Ships (G6): the Tin Finch's engines, boost, hatch, touchdown, crash and wreck.
    "tin-finch": {"engine": "ship.engine", "load": "ship.thrust", "boost": "ship.boost", "hatchOpen": "ship.hatch.open",
                  "hatchClose": "ship.hatch.close", "touchdown": "ship.touchdown", "crash": "ship.crash",
                  "destroyed": "explosion"},
    # The Iron Wake (IW6, IRON_WAKE.md §10.1): ShipsView drives each role from the replicated state.
    "iron-wake": {
        "start": "iw.drive.start", "idle": "iw.drive.idle.slow", "idleFast": "iw.drive.idle.fast",
        "load": "iw.drive.load.slow", "loadFast": "iw.drive.load.fast", "release": "iw.drive.release",
        "shutdown": "iw.drive.shutdown", "lift": "iw.lift.loop.slow", "liftFast": "iw.lift.loop.fast",
        "rcs": "iw.rcs.puff", "boostLoop": "iw.boost.strain.slow", "boostLoopFast": "iw.boost.strain.fast",
        "boostRelease": "iw.boost.release", "strainLight": "iw.hull.strain.light", "strainMedium": "iw.hull.strain.medium",
        "strainHeavy": "iw.hull.strain.heavy", "touchdown": "iw.touchdown", "crash": "ship.crash",
        "destroyed": "explosion", "ventilation": "iw.interior.ventilation", "machinery": "iw.interior.machinery",
        "alarmHull": "iw.alarm.hull", "rampOpen": "iw.ramp.release", "rampClose": "iw.ramp.release",
        "rampTravel": "iw.ramp.actuator", "rampOpened": "iw.ramp.contact", "rampShut": "iw.ramp.lock",
        "airlockOpen": "iw.airlock.slide", "airlockClose": "iw.airlock.slide", "airlockShut": "iw.airlock.seal"},
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
    ships()
    interface()
    ambience()
    animals()
    iron_wake()
    __import__("repair_actions").recipes()
    return list(OUTS), list(EVENTS)

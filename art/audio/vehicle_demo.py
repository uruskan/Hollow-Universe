"""A ground vehicle's drive rendered to a WAV for listening (docs/VEHICLES.md §7.1): idle, a pull away through the gears to
top speed, off the throttle and down through the box, a stop, idle; mixed from the built files the way the game mixes them
(the gearbox of Hollow.Client.Vehicles.VehicleRevs, each engine loop pitched to the revs and crossfaded with its neighbours
at equal power, the throttle's loops against the overrun's, the turbo by boost, a clunk at each change, the tyres on one
ground swelled and pitched by speed). It hears no distance or room: it is the engine as the rider hears it.

    python -I art/audio/vehicle_demo.py <kind> [surface] [out.wav]     (default: .artifacts/vehicle-demo/<kind>-<surface>.wav)
"""
import json
import math
import os
import sys
from array import array

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import audiolib  # noqa: E402
import synth  # noqa: E402

RATE = 48000
REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
GAME = os.path.join(REPO, "game")


def db(x):
    return 10.0 ** (x / 20.0)


class Revs:
    """VehicleRevs, step for step (keep the two alike)."""

    def __init__(self, e):
        self.e = e
        self.rpm, self.gear, self.load, self.over, self.boost = 0.0, 0, 0.0, 0.0, 0.0
        self.running = False
        self.shift_left = self.cut_left = self.neutral_for = self.gas_was = self.release_cd = 0.0
        self.shifted = self.released = False

    def wheel(self, gear, s):
        gears = self.e["gears"]
        top = self.e.get("reverse", 4.0) if gear < 0 else gears[gear - 1] if 1 <= gear <= len(gears) else 0.0
        return abs(s) / top * self.e["redline"] if top > 0 else 0.0

    @staticmethod
    def approach(v, t, dt, tau):
        return v + (t - v) * (1.0 - math.exp(-dt / max(tau, 1e-3)))

    def step(self, speed, throttle, engines, grounded, dt):
        e = self.e
        idle, red = e["idle"], e["redline"]
        limiter = e.get("limiter", red * 1.03)
        self.shifted = self.released = False
        self.release_cd = max(0.0, self.release_cd - dt)
        if not engines:
            self.running, self.gear, self.load, self.over = False, 0, 0.0, 0.0
            self.rpm = self.approach(self.rpm, 0.0, dt, 0.3)
            self.rpm = 0.0 if self.rpm < 60 else self.rpm
            self.boost = self.approach(self.boost, 0.0, dt, 0.8)
            return
        if not self.running:
            self.running, self.rpm, self.gear = True, max(self.rpm, idle), 0
        s = abs(speed)
        braking = (throttle < -0.05 and speed > 0.5) or (throttle > 0.05 and speed < -0.5)
        gas = 0.0 if braking else abs(throttle)
        backing = not braking and (throttle < -0.05 or (speed < -0.5 and throttle <= 0.05))
        self.choose(s, gas, backing, grounded, dt)
        engaged = False
        if self.gear == 0:
            target = idle + gas * (red * 0.9 - idle)
        else:
            wheels = self.wheel(self.gear, s)
            if not grounded:
                wheels = max(wheels, idle + gas * (red - idle))
            slip = idle + gas * (e.get("launch", 0.4) * red - idle) if self.gear in (1, -1) else idle
            engaged = grounded and wheels >= slip and self.shift_left <= 0
            target = max(wheels, slip)
        if self.cut_left > 0:
            self.cut_left -= dt
        elif gas > 0.05 and self.rpm >= limiter - 1:
            self.cut_left = 0.07
        target = min(target, red * 0.95 if self.cut_left > 0 else limiter)
        resp = e.get("response", 0.1)
        tau = 0.05 if engaged else (resp if target > self.rpm else resp * 1.8)
        self.rpm = min(max(self.approach(self.rpm, target, dt, tau), idle * 0.85), limiter)
        load = 0.0 if self.shift_left > 0 or self.cut_left > 0 else (gas * 0.6 if self.gear == 0 else gas)
        self.load = self.approach(self.load, load, dt, 0.06)
        drag = 0.0
        if gas < 0.05 and self.gear != 0:
            drag = min(max((self.rpm - idle * 1.25) / max(red * 0.55 - idle, 1), 0.0), 1.0) * (1.0 if braking else 0.8)
        self.over = self.approach(self.over, drag, dt, 0.12)
        spool = e.get("turboSpool", 0.0)
        if spool > 0:
            want = self.load * min(max((self.rpm - red * 0.3) / (red * 0.45), 0.0), 1.0)
            self.boost = self.approach(self.boost, want, dt, spool if want > self.boost else spool * 0.5)
            if self.gas_was > 0.6 and gas < 0.2 and self.boost > 0.35 and self.release_cd <= 0:
                self.released, self.release_cd = True, 1.5
        self.gas_was = gas

    def choose(self, s, gas, backing, grounded, dt):
        e = self.e
        if backing:
            self.gear, self.neutral_for = -1, 0.0
            return
        if self.gear <= 0 and (gas > 0.05 or s > 1.0):
            self.gear = 1
        if self.gear != 0 and gas < 0.05 and s < 0.4:
            self.neutral_for += dt
            if self.neutral_for > 0.6:
                self.gear = 0
        else:
            self.neutral_for = 0.0
        if self.shift_left > 0:
            self.shift_left -= dt
            return
        if self.gear < 1 or not grounded:
            return
        revs, red, top = self.wheel(self.gear, s), e["redline"], len(e["gears"])
        if self.gear < top and ((gas > 0.05 and revs > e.get("upshift", 0.9) * red) or revs > red * 0.98):
            self.change(self.gear + 1)
        elif self.gear > 1 and (revs < e.get("downshift", 0.45) * red
                                or (gas > 0.7 and revs < 0.5 * red and self.wheel(self.gear - 1, s) < 0.8 * red)):
            self.change(self.gear - 1)

    def change(self, gear):
        self.gear, self.shift_left, self.shifted = gear, self.e.get("shiftTime", 0.25), True


class Loop:
    """A loop of samples read at any rate (linear interpolation), its gain moving toward a target."""

    def __init__(self, samples):
        self.x = samples
        self.pos = 0.0
        self.gain = 0.0

    def block(self, n, pitch, gain):
        out = [0.0] * n
        x, size = self.x, len(self.x)
        g0, g1 = self.gain, gain
        pos = self.pos
        for i in range(n):
            k = int(pos)
            f = pos - k
            a = x[k % size]
            b = x[(k + 1) % size]
            out[i] = (a + f * (b - a)) * (g0 + (g1 - g0) * i / n)
            pos += pitch
        self.pos = pos % size
        self.gain = gain
        return out


def main(argv):
    kind = argv[1] if len(argv) > 1 else "zamazingo"
    surface = argv[2] if len(argv) > 2 else "gravel"
    out = argv[3] if len(argv) > 3 else os.path.join(REPO, ".artifacts", "vehicle-demo", "%s-%s.wav" % (kind, surface))
    with open(os.path.join(GAME, "data", "ground", "sounds.json"), encoding="utf-8") as f:
        cat = json.load(f)
    e = cat["engines"][kind]
    events = {ev["key"]: ev for ev in cat["events"]}
    cache = {}

    def samples(key, k=0):
        ev = events[key]
        path = os.path.join(GAME, ev["files"][k % len(ev["files"])].replace("res://", ""))
        if path not in cache:
            cache[path] = array("f", audiolib.decode(path, RATE))
        return cache[path], db(ev.get("volume", 0.0))

    loops = e["loops"]
    on = [Loop(samples(l["on"])[0]) for l in loops]
    off = [Loop(samples(l["off"])[0]) if l["off"] != l["on"] else None for l in loops]
    tyre = Loop(samples(e["tyres"].get(surface, e["tyres"]["hard"]))[0])
    turbo = Loop(samples(e["turbo"])[0]) if e.get("turbo") else None
    revs = Revs(e)
    gears = e["gears"]
    # The drive: idle 2.5 s, full throttle to top speed, 3 s cruising at a little throttle, off the throttle and braking to a
    # stop, idle 2 s. Speed follows a simple pull that falls as it nears the top.
    top = gears[-1] * 0.97
    block = 480
    dt = block / RATE
    t, speed = 0.0, 0.0
    phase, phase_t = "idle", 0.0
    mix = []
    shots = []
    while True:
        phase_t += dt
        if phase == "idle" and phase_t > 2.5:
            phase, phase_t = "pull", 0.0
        elif phase == "pull" and speed >= top * 0.98:
            phase, phase_t = "cruise", 0.0
        elif phase == "cruise" and phase_t > 3.0:
            phase, phase_t = "brake", 0.0
        elif phase == "brake" and speed <= 0.0 and phase_t > 0.5:
            phase, phase_t = "end", 0.0
        elif phase == "end" and phase_t > 2.5:
            break
        throttle = {"idle": 0.0, "pull": 1.0, "cruise": 0.35, "brake": -0.6 if speed > 3 else 0.0, "end": 0.0}[phase]
        if phase == "pull":
            speed += dt * (5.5 if e["redline"] > 5000 else 2.4) * (1.0 - (speed / gears[-1]) ** 2) * (0.2 if revs.shift_left > 0 else 1.0)
        elif phase == "brake":
            speed = max(0.0, speed - dt * (4.0 if speed > 3 else 1.5))
        revs.step(speed, throttle, True, True, dt)
        rpm = max(revs.rpm, 1.0)
        a = (min(max(revs.load, 0.0), 1.0) ** 0.75) * math.pi / 2
        on_g, off_g = math.sin(a), math.cos(a) * db(2.0 * revs.over)
        acc = [0.0] * block
        for i, l in enumerate(loops):
            w = 0.0
            if rpm <= loops[0]["rpm"]:
                w = 1.0 if i == 0 else 0.0
            elif rpm >= loops[-1]["rpm"]:
                w = 1.0 if i == len(loops) - 1 else 0.0
            elif i + 1 < len(loops) and l["rpm"] <= rpm <= loops[i + 1]["rpm"]:
                w = math.cos(math.log(rpm / l["rpm"]) / math.log(loops[i + 1]["rpm"] / l["rpm"]) * math.pi / 2)
            elif i > 0 and loops[i - 1]["rpm"] <= rpm <= l["rpm"]:
                w = math.sin(math.log(rpm / loops[i - 1]["rpm"]) / math.log(l["rpm"] / loops[i - 1]["rpm"]) * math.pi / 2)
            pitch = rpm / l["rpm"] * e.get("pitch", 1.0)
            vol = db(e.get("volume", 0.0))
            if off[i] is None:
                part = on[i].block(block, pitch, vol * db(l["onDb"]) * w)
            else:
                p1 = on[i].block(block, pitch, vol * db(l["onDb"]) * w * on_g)
                p2 = off[i].block(block, pitch, vol * db(l["offDb"]) * w * off_g)
                part = [u + v for u, v in zip(p1, p2)]
            acc = [u + v for u, v in zip(acc, part)]
        if turbo:
            g = revs.boost ** 1.4
            acc = [u + v for u, v in zip(acc, turbo.block(block, (0.55 + 0.6 * revs.boost) * e.get("pitch", 1.0), g * db(-6.0)))]
        ratio = speed / e.get("tyreSpeed", 10.0)
        tg = db(e.get("tyreDb", 0.0)) * (max(ratio, 0.01) ** 0.9) * min(speed / 1.5, 1.0) if speed > 0.3 else 0.0
        acc = [u + v for u, v in zip(acc, tyre.block(block, e.get("tyrePitch", 1.0) * min(max(ratio ** 0.4, 0.5), 1.6), tg))]
        if revs.shifted and e.get("shift"):
            shots.append((len(mix) * block, e["shift"]))
        if revs.released and e.get("blowoff"):
            shots.append((len(mix) * block, e["blowoff"]))
        mix.append(acc)
        t += dt
    flat = [v for b in mix for v in b]
    for k, (at, key) in enumerate(shots):
        x, g = samples(key, k)
        for j, v in enumerate(x):
            if at + j < len(flat):
                flat[at + j] += v * g * db(-4.0)
    peak = max(1e-6, max(abs(v) for v in flat))
    flat = [v * 0.89 / peak for v in flat]
    os.makedirs(os.path.dirname(out), exist_ok=True)
    synth.write_wav(out, [flat])
    print(out, "%.1f s" % (len(flat) / RATE))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))

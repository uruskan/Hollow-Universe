"""Draws docs/images/vehicles/proportions.svg: the proportion sheet of VEHICLES.md (side elevations, metres to pixels)."""
import math
import sys

PX = 80.0  # pixels per metre
out = []

INK = "#2a2c2e"
TYRE = "#2f3133"
TREAD = "#1d1f20"
RIM = "#b8b1a2"
STEEL = "#5d6469"
BLUE = "#9fbfcc"      # the Tin Finch's pale blue
CREAM = "#e8dfc8"
RED = "#b9573f"
RUST = "#8d5233"
DECK = "#c9b48d"
GLASS = "#cfe0e6"
NOTE = "#5a5f63"


def m(v):
    return v * PX


def text(x, y, s, size=13, anchor="start", weight="normal", fill=INK):
    out.append(f'<text x="{x:.1f}" y="{y:.1f}" font-size="{size}" text-anchor="{anchor}" '
               f'font-weight="{weight}" fill="{fill}">{s}</text>')


def rect(x, y, w, h, fill, stroke=INK, sw=1.5, rx=0, dash=None, opacity=1.0):
    d = f' stroke-dasharray="{dash}"' if dash else ""
    out.append(f'<rect x="{x:.1f}" y="{y:.1f}" width="{w:.1f}" height="{h:.1f}" rx="{rx}" fill="{fill}" '
               f'stroke="{stroke}" stroke-width="{sw}"{d} opacity="{opacity}"/>')


def line(x1, y1, x2, y2, stroke=INK, sw=1.5, dash=None):
    d = f' stroke-dasharray="{dash}"' if dash else ""
    out.append(f'<line x1="{x1:.1f}" y1="{y1:.1f}" x2="{x2:.1f}" y2="{y2:.1f}" stroke="{stroke}" '
               f'stroke-width="{sw}" stroke-linecap="round"{d}/>')


def circle(cx, cy, r, fill, stroke=INK, sw=1.5, dash=None):
    d = f' stroke-dasharray="{dash}"' if dash else ""
    out.append(f'<circle cx="{cx:.1f}" cy="{cy:.1f}" r="{r:.1f}" fill="{fill}" stroke="{stroke}" stroke-width="{sw}"{d}/>')


def wheel(cx, cy, diameter, rim_diameter, knobs=26, dash=None):
    r = m(diameter) / 2
    rr = m(rim_diameter) / 2
    circle(cx, cy, r, TYRE, dash=dash)
    # Knobbed tread: blocks standing proud of the carcass.
    for i in range(knobs):
        a = 2 * math.pi * i / knobs
        k = 0.055 * PX
        w = 2 * math.pi * r / knobs * 0.55
        x = cx + math.cos(a) * (r - k * 0.35)
        y = cy + math.sin(a) * (r - k * 0.35)
        deg = math.degrees(a) + 90
        out.append(f'<rect x="{x - w / 2:.1f}" y="{y - k / 2:.1f}" width="{w:.1f}" height="{k:.1f}" fill="{TREAD}" '
                   f'transform="rotate({deg:.1f} {x:.1f} {y:.1f})"/>')
    circle(cx, cy, r - 0.06 * PX, TYRE, stroke="#4a4d50", sw=1)
    circle(cx, cy, rr, RIM)
    circle(cx, cy, rr * 0.42, STEEL)
    for i in range(6):
        a = 2 * math.pi * i / 6
        circle(cx + math.cos(a) * rr * 0.68, cy + math.sin(a) * rr * 0.68, 2.2, STEEL, sw=0.8)


def person(x, ground, height=1.8, label=True):
    h = m(height)
    head = h * 0.13
    circle(x, ground - h + head / 2, head / 2, "#d9d4ca")
    rect(x - h * 0.11, ground - h + head, h * 0.22, h * 0.42, "#d9d4ca", rx=4)
    line(x - h * 0.06, ground - h * 0.45, x - h * 0.07, ground, sw=4, stroke="#8f8a80")
    line(x + h * 0.06, ground - h * 0.45, x + h * 0.07, ground, sw=4, stroke="#8f8a80")
    if label:
        text(x, ground + 16, f"{height:.1f} m", 11, "middle", fill=NOTE)


def ground_line(x1, x2, y):
    line(x1, y, x2, y, stroke="#8f8a80", sw=2)
    for x in range(int(x1), int(x2), 14):
        line(x, y + 1, x - 7, y + 8, stroke="#c6c0b4", sw=1)


def dimension(x1, x2, y, label):
    line(x1, y, x2, y, stroke=NOTE, sw=1)
    line(x1, y - 5, x1, y + 5, stroke=NOTE, sw=1)
    line(x2, y - 5, x2, y + 5, stroke=NOTE, sw=1)
    text((x1 + x2) / 2, y - 5, label, 11, "middle", fill=NOTE)


def vdimension(x, y1, y2, label):
    line(x, y1, x, y2, stroke=NOTE, sw=1)
    line(x - 5, y1, x + 5, y1, stroke=NOTE, sw=1)
    line(x - 5, y2, x + 5, y2, stroke=NOTE, sw=1)
    text(x + 7, (y1 + y2) / 2 + 4, label, 11, fill=NOTE)


# --- The car's numbers (VEHICLES.md section 3.1): three bays from 0.35 m, the nose engine mount ahead of them ---
CAR = dict(length=6.0, rear_overhang=1.55, wheelbase=3.6, tyre=0.96, rim=0.46, frame_top=0.98, frame_depth=0.16,
           bay=1.4, bays=3, bay_start=0.35, nose=1.2, cab=1.6)


def nose_engine(f, label="engine", sub="nose mount"):
    """The engine in the nose mount: an open hood over the block, a radiator grille in front, a stack by the cab."""
    x = f["bay0"] + f["bay"] * CAR["bays"]
    w = m(CAR["nose"])
    h = m(0.78)
    top = f["top"]
    rect(x + 2, top - h, w - 4, h, CREAM, rx=5)
    for k in range(6):
        line(x + w - 10, top - h + 8 + k * 9, x + w - 4, top - h + 8 + k * 9, stroke=STEEL, sw=2)
    line(x + 10, top - h, x + 10, top - h - m(0.55), stroke=STEEL, sw=6)
    text(x + w / 2 - 4, top - h / 2 + 2, label, 12, "middle", "bold")
    text(x + w / 2 - 4, top - h / 2 + 16, sub, 9, "middle", fill=NOTE)


def car_frame(x0, ground, tracks=False):
    c = CAR
    rear_axle = x0 + m(c["rear_overhang"])
    front_axle = rear_axle + m(c["wheelbase"])
    front = x0 + m(c["length"])
    top = ground - m(c["frame_top"])
    wheel_y = ground - m(c["tyre"]) / 2
    if tracks:
        # One track unit a side: drive sprocket behind, idler ahead, five road wheels on the frame's hubs.
        h = 0.92
        y_top = ground - m(h)
        x_a = rear_axle - m(0.62)
        x_b = front_axle + m(0.62)
        out.append(f'<path d="M {x_a + m(0.46):.1f} {y_top:.1f} L {x_b - m(0.40):.1f} {y_top:.1f} '
                   f'A {m(0.40):.1f} {m(0.40):.1f} 0 0 1 {x_b - m(0.30):.1f} {ground - m(0.16):.1f} '
                   f'L {x_b - m(0.6):.1f} {ground:.1f} L {x_a + m(0.5):.1f} {ground:.1f} '
                   f'L {x_a + m(0.12):.1f} {ground - m(0.22):.1f} '
                   f'A {m(0.46):.1f} {m(0.46):.1f} 0 0 1 {x_a + m(0.46):.1f} {y_top:.1f} Z" '
                   f'fill="{TREAD}" stroke="{INK}" stroke-width="1.5"/>')
        circle(x_a + m(0.46), y_top + m(0.46), m(0.36), STEEL)
        circle(x_b - m(0.40), y_top + m(0.40), m(0.30), STEEL)
        for i in range(5):
            x = rear_axle + (front_axle - rear_axle) * (i + 0.5) / 5
            circle(x, ground - m(0.22), m(0.17), RIM)
        text((x_a + x_b) / 2, ground + 30, "track units on the same four hubs", 11, "middle", fill=NOTE)
    else:
        for ax in (rear_axle, front_axle):
            # Long-travel arms and a coil-over, drawn behind the tyre.
            line(ax, wheel_y, ax - m(0.35), top + m(0.10), stroke=STEEL, sw=4)
            line(ax + m(0.05), wheel_y - m(0.05), ax + m(0.42), top - m(0.18), stroke=RED, sw=6)
            wheel(ax, wheel_y, c["tyre"], c["rim"])
    # Ladder frame, bumpers, lamps and the chassis tank.
    rect(x0 + m(0.18), top, m(c["length"] - 0.36), m(c["frame_depth"]), STEEL)
    rect(front - m(0.22), top - m(0.30), m(0.22), m(0.62), STEEL, rx=3)
    circle(front - m(0.05), top - m(0.18), m(0.09), "#f2e7b8")
    rect(x0, top - m(0.22), m(0.24), m(0.52), STEEL, rx=3)
    rect(x0 + m(0.08), top + m(0.18), m(0.55), m(0.24), RED, rx=6)
    text(x0 + m(0.35), top + m(0.35), "fuel", 9, "middle", fill="#f6efe2")
    return dict(rear_axle=rear_axle, front_axle=front_axle, front=front, top=top,
                bay0=x0 + m(c["bay_start"]), bay=m(c["bay"]))


def module(x, top, bays, height, fill, label, sub=None, glass=False, dash=None):
    w = m(CAR["bay"]) * bays
    h = m(height)
    rect(x + 2, top - h, w - 4, h, fill, rx=3, dash=dash)
    if glass:
        rect(x + 10, top - h + 10, w - 20, h * 0.36, GLASS, sw=1)
        for k in range(3):
            circle(x + 8 + k * (w - 16) / 2, top - h + 6, 1.8, STEEL, sw=0.6)
    text(x + w / 2, top - h / 2 + (14 if glass else 4), label, 12, "middle", "bold")
    if sub:
        text(x + w / 2, top - h / 2 + (29 if glass else 19), sub, 10, "middle", fill=NOTE)
    return top - h


def bay_ticks(f, ground):
    for i in range(CAR["bays"] + 1):
        x = f["bay0"] + f["bay"] * i
        line(x, f["top"] + 2, x, f["top"] + m(CAR["frame_depth"]) - 2, stroke="#f6efe2", sw=1.2)
    for i in range(CAR["bays"]):
        x = f["bay0"] + f["bay"] * (i + 0.5)
        text(x, ground + 14, f"bay {i + 1}", 10, "middle", fill=NOTE)


def panel(x, y, w, h, title, subtitle):
    rect(x, y, w, h, "#fbf8f2", stroke="#d8d1c3", sw=1, rx=6)
    text(x + 16, y + 26, title, 16, weight="bold")
    text(x + 16, y + 45, subtitle, 12, fill=NOTE)


W, H = 1640, 958
out.append(f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {W} {H}" width="{W}" height="{H}" '
           f'font-family="Segoe UI, Inter, Helvetica, Arial, sans-serif">')
out.append(f'<rect width="{W}" height="{H}" fill="#f3efe7"/>')
text(24, 36, "Ground vehicles: proportion sheet (proposal, VEHICLES.md)", 22, weight="bold")
text(24, 58, "Side elevations to scale, 80 px = 1 m, front to the right. Colours are the Tin Finch's: pale blue, cream, "
             "faded red over steel. Shapes are blocks for proportion only, not art.", 13, fill=NOTE)

# Panel 1: tyres, nose engine, two-seat cab, a second engine, deck with a bed and a box.
panel(16, 74, 800, 520, "1  Car on tyres: nose engine · two-seat cab · second engine · camp deck",
      "0.96 m tyres (0.46 m rims, 0.25 m sidewall), 3.6 m wheelbase, 0.42 m under the frame")
g1 = 470
ground_line(40, 790, g1)
f = car_frame(90, g1)
bay_ticks(f, g1)
x = f["bay0"]
deck_top = f["top"]
rect(x + 2, deck_top - m(0.10), f["bay"] - 4, m(0.10), DECK, rx=2)
line(x + 4, deck_top - m(0.10), x + 4, deck_top - m(0.55), stroke=STEEL, sw=3)
line(x + 4, deck_top - m(0.55), x + f["bay"] - 4, deck_top - m(0.55), stroke=STEEL, sw=3)
rect(x + 8, deck_top - m(0.10) - m(0.32), m(0.62), m(0.32), "#8e9a6c", rx=4)
text(x + 8 + m(0.31), deck_top - m(0.10) - m(0.12), "bed", 10, "middle", fill="#f6efe2")
rect(x + m(0.78), deck_top - m(0.10) - m(0.5), m(0.5), m(0.5), "#a07a4e")
text(x + m(1.03), deck_top - m(0.10) - m(0.22), "box", 10, "middle", fill="#f6efe2")
text(x + f["bay"] / 2, deck_top - m(0.85), "camp deck", 12, "middle", "bold")
text(x + f["bay"] / 2, deck_top - m(0.85) + 15, "a build zone", 10, "middle", fill=NOTE)
module(x + f["bay"], deck_top, 1, 0.95, CREAM, "engine", "a second one")
for k in range(3):
    line(x + f["bay"] + 20 + k * 30, deck_top - m(0.95) - 2, x + f["bay"] + 20 + k * 30, deck_top - m(0.95) - 18,
         stroke=STEEL, sw=5)
module(x + 2 * f["bay"], deck_top, 1, CAR["cab"], BLUE, "two-seat cab", "driver + one", glass=True)
nose_engine(f)
person(760, g1)
dimension(90, 90 + m(CAR["length"]), g1 + 48, f"{CAR['length']:.1f} m")
dimension(f["rear_axle"], f["front_axle"], g1 + 70, f"wheelbase {CAR['wheelbase']:.1f} m")
vdimension(f["front_axle"] + m(0.95), g1, g1 - m(CAR["tyre"]), f"{CAR['tyre']:.2f} m")
vdimension(40 + 8, g1, deck_top - m(CAR["cab"]), f"{CAR['frame_top'] + CAR['cab']:.2f} m")

# Panel 2: tracks, the nose engine, a one-seat cab with another stacked on it, a camper shell over two bays.
panel(824, 74, 800, 520, "2  Car on tracks: nose engine · one-seat cab, another stacked · camper shell",
      "the same chassis; two track units take the four hubs; a second level on roofs that carry load")
g2 = 470
ground_line(848, 1598, g2)
f2 = car_frame(898, g2, tracks=True)
bay_ticks(f2, g2)
x = f2["bay0"]
shell_top = module(x, f2["top"], 2, 1.95, CREAM, "camper shell", "a room: bed, boxes")
rect(x + 18, f2["top"] - m(1.6), m(0.55), m(1.5), "#a9a294", sw=1)
text(x + 18 + m(0.27), f2["top"] - m(0.6), "door", 9, "middle")
roof = module(x + 2 * f2["bay"], f2["top"], 1, CAR["cab"], BLUE, "one-seat cab", "carries load", glass=True)
module(x + 2 * f2["bay"], roof, 1, 1.05, BLUE, "one-seat cab", "level 2: a lookout", glass=False, dash="5 3")
nose_engine(f2)
person(1570, g2)
vdimension(848 + 8, g2, roof - m(1.05), f"{CAR['frame_top'] + CAR['cab'] + 1.05:.2f} m")
text(1224, 545, "two levels at most; the tallest build stays under 3.9 m", 11, "middle", fill=NOTE)

# Panel 3: the motorbike, sidecar dashed, snow kit dashed. Points in metres: x forward of the rear axle, y up.
panel(16, 602, 800, 340, "3  Motorbike: frame · single engine · tyres · seat · rack (sidecar dashed)",
      "0.80 m front and 0.76 m rear tyres, 1.52 m wheelbase, 0.95 m seat, 0.32 m under the frame")
g3 = 860
ground_line(40, 790, g3)
ox = 200


def P(x, y):
    return ox + m(x), g3 - m(y)


def path(points, fill="none", stroke=INK, sw=1.5, close=False, dash=None):
    d = " ".join(("M" if i == 0 else "L") + f" {P(*p)[0]:.1f} {P(*p)[1]:.1f}" for i, p in enumerate(points))
    if close:
        d += " Z"
    dd = f' stroke-dasharray="{dash}"' if dash else ""
    out.append(f'<path d="{d}" fill="{fill}" stroke="{stroke}" stroke-width="{sw}" stroke-linejoin="round"{dd}/>')


# Sidecar first, dashed, as it stands beyond the bike: a tub on its own small wheel.
path([(0.10, 0.34), (0.05, 0.86), (0.95, 0.92), (1.55, 0.70), (1.45, 0.34)], stroke=RUST, close=True, dash="6 4")
circle(*P(0.80, 0.30), m(0.30), "none", stroke=RUST, dash="6 4")
text(*P(1.62, 0.86), "sidecar (beyond)", 10, fill=RUST)
wheel(*P(0.0, 0.38), 0.76, 0.43, knobs=22)
wheel(*P(1.52, 0.40), 0.80, 0.53, knobs=22)
path([(0.0, 0.38), (0.58, 0.47)], stroke=STEEL, sw=7)                       # swingarm
path([(0.30, 0.45), (0.56, 0.86)], stroke=RED, sw=6)                        # rear shock
path([(1.52, 0.40), (1.22, 1.02)], stroke=STEEL, sw=8)                      # fork, 26 degrees of rake
path([(1.22, 1.02), (1.10, 1.18), (0.98, 1.20)], stroke=INK, sw=4)          # bars
path([(1.22, 1.02), (0.98, 0.40), (0.58, 0.47), (0.62, 0.92), (1.22, 1.02)], stroke=STEEL, sw=5)   # frame
path([(0.62, 0.92), (0.02, 0.96)], stroke=STEEL, sw=4)                      # subframe
path([(0.70, 0.40), (0.62, 0.38), (0.62, 0.78), (1.02, 0.80), (1.06, 0.42)], fill=CREAM, close=True)  # engine
text(*P(0.84, 0.56), "engine", 10, "middle")
path([(0.85, 0.62), (0.40, 0.70), (0.05, 0.78)], stroke="#7a7067", sw=5)   # exhaust
path([(0.70, 0.95), (0.78, 1.15), (1.14, 1.12), (1.20, 1.02)], fill=BLUE, close=True)               # tank
text(*P(0.96, 1.20), "tank", 10, "middle")
path([(0.06, 0.95), (0.80, 1.00), (0.80, 1.06), (0.06, 1.04)], fill="#4b3a2c", close=True)          # seat
text(*P(0.40, 1.10), "seat", 10, "middle")
path([(-0.30, 0.94), (0.05, 0.94), (0.05, 1.00), (-0.30, 1.00)], fill=STEEL, close=True)            # rack
text(*P(-0.14, 1.06), "rack", 10, "middle")
circle(*P(1.32, 0.98), m(0.08), "#f2e7b8")
# The snow kit, dashed: a ski for the front, a short track for the rear.
path([(1.05, 0.0), (1.95, 0.0), (2.08, 0.06), (2.12, 0.16)], stroke="#3d6c8a", sw=2, dash="6 4")
out.append(f'<rect x="{P(-0.42, 0)[0]:.1f}" y="{P(0, 0.48)[1]:.1f}" width="{m(1.20):.1f}" height="{m(0.48):.1f}" '
           f'rx="16" fill="none" stroke="#3d6c8a" stroke-width="2" stroke-dasharray="6 4"/>')
text(*P(0.85, -0.40), "snow kit (dashed): a ski for the front tyre, a track for the rear", 11, "middle", fill="#3d6c8a")
person(P(2.55, 0)[0], g3)
dimension(P(0, 0)[0], P(1.52, 0)[0], g3 + 52, "wheelbase 1.52 m")
vdimension(P(1.98, 0)[0], g3, g3 - m(0.80), "0.80 m")
vdimension(P(-0.62, 0)[0], g3, g3 - m(1.0), ""); text(P(-0.68, 0)[0], g3 - m(0.5), "seat 1.0 m", 11, "end", fill=NOTE)

# Panel 4: why the wheels read as small or not.
panel(824, 602, 800, 340, "4  Wheels that read right",
      "the same 1.8 m person; the left is the look the owner rejected, the right is ours")
g4 = 860
ground_line(848, 1598, g4)
# Left: a small, thin wheel tucked under a tall body.
lx = 960
rect(lx - 90, g4 - m(1.85), 330, m(1.45), "#d6d0c4", rx=10)
circle(lx, g4 - m(0.31), m(0.31), TYRE)
circle(lx, g4 - m(0.31), m(0.25), RIM)
circle(lx + 230, g4 - m(0.31), m(0.31), TYRE)
circle(lx + 230, g4 - m(0.31), m(0.25), RIM)
text(lx + 75, g4 - m(1.95), "avoid: 0.62 m wheel, thin sidewall,", 11, "middle", fill=NOTE)
text(lx + 75, g4 - m(1.95) + 14, "body hangs over it, no visible travel", 11, "middle", fill=NOTE)
person(lx + 300, g4)
# Right: ours.
rx_ = 1360
wheel(rx_, g4 - m(0.48), 0.96, 0.46)
line(rx_, g4 - m(0.48), rx_ - m(0.45), g4 - m(1.05), stroke=STEEL, sw=5)
line(rx_ + m(0.05), g4 - m(0.53), rx_ + m(0.40), g4 - m(1.25), stroke=RED, sw=7)
out.append(f'<path d="M {rx_ - m(0.72):.1f} {g4 - m(1.00):.1f} A {m(0.72):.1f} {m(0.72):.1f} 0 0 1 '
           f'{rx_ + m(0.72):.1f} {g4 - m(1.00):.1f}" fill="none" stroke="{STEEL}" stroke-width="4"/>')
text(rx_, g4 - m(1.86), "ours: 0.96 m tyre, half its radius rubber", 11, "middle", fill=NOTE)
text(rx_, g4 - m(1.86) + 14, "knobs proud, open arch, the spring seen", 11, "middle", fill=NOTE)
person(rx_ + 140, g4)
text(1224, g4 + 46, "Rules: tyre ≥ 0.53 × the person's height on a car (0.44 on the bike); sidewall ≥ 0.25 × diameter;",
     11, "middle", fill=INK)
text(1224, g4 + 62, "the arch clears full bump and full steering lock; the wheel's top edge stands above the frame.",
     11, "middle", fill=INK)

out.append("</svg>")
with open(sys.argv[1], "w", encoding="utf-8") as fh:
    fh.write("\n".join(out))
print("wrote", sys.argv[1])

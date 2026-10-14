# Map-UX prototype data

Snapshots of Hollow's real generator for the galaxy-map UX prototypes, plus a clearly marked **mock** gameplay
layer. Each file is a plain script that sets one global. A prototype opened by double-click (`file://`, no
server, no `fetch`) loads them with:

```html
<meta charset="utf-8">
<script src="data/local.js"></script>   <!-- window.HOLLOW_LOCAL -->
<script src="data/galaxy.js"></script>  <!-- window.HOLLOW_GALAXY -->
<script src="data/mock.js"></script>    <!-- window.HOLLOW_MOCK -->
```

The files are ASCII; non-ASCII text such as the en dash in "Sagittarius–Carina" is written as `–` escapes.

| File | Global | Size | What |
|---|---|---|---|
| `local.js` | `HOLLOW_LOCAL` | 4.6 MB | All 37,409 systems within 150 ly of Sol, with planet summaries; body lists within 40 ly |
| `galaxy.js` | `HOLLOW_GALAXY` | 6.6 MB | A 250,000-star galaxy-wide sample, a face-on glow image, structure labels, arm paths and sectors near Sol |
| `mock.js` | `HOLLOW_MOCK` | 0.15 MB | **MOCK** stations, home base, other players, danger regions, resources, ship |

## Axis convention (all files)

Hollow's galactic frame, in light-years, with **Sol at the origin**:

- **+Z points to the Galactic Centre.** Sgr A* is at `(-26.1, -21.4, 26673)`.
- **+Y is "up"**, toward the north galactic pole, out of the plane. The Sun is 67.8 ly above the midplane, so the
  midplane sits at about y = -68 near Sol (the plane is tilted by 0.1°).
- **+X points toward galactic longitude 90°**, the direction the Sun orbits (toward Cygnus).
- The frame is right-handed with Y up, like Godot and three.js. Seen from the north galactic pole (+Y), +X is
  to the right and +Z points up the screen. Positions convert straight into a three.js scene.

## local.js: `HOLLOW_LOCAL`

`systems` is column-wise: index `i` of every array is the same system. Systems are sorted by distance from Sol,
so index 0 is Sol.

| Column | Meaning |
|---|---|
| `addr` | 64-bit system address as hex (`"0x8000000000011720"`). Bit 63 set means a catalogue (HYG 4.1) star. |
| `name` | System name: the real name for catalogue stars, the procedural name otherwise (`"Nolcas Peris CG-HBW 7-1"`) |
| `x`, `y`, `z` | Position in ly, to 0.01 ly |
| `cls` | Spectral class of the primary, e.g. `"G2 V"`, `"M4.5 V"`, `"DA7"`, `"K1 III"` |
| `col` | Display colour `#rrggbb`, using the same logic as the Godot maps (`game/scripts/Common/StarColors.cs`) |
| `mag` | Absolute visual magnitude M_V. Black holes read 99 and neutron stars 30; the maps draw both as 11.5. |
| `teff` | Effective temperature, K |
| `lvl` | Octree level 0–7 by brightness (0 is brightest). The game's map shows levels by camera distance. |
| `cat` | 1 = real catalogue star, 0 = procedural |
| `scoop` | 1 = scoopable: the primary is a main-sequence O, B, A, F, G, K or M star |
| `sum` | Planet summary `[stars, planets, moons, landable, gasGiants, iceGiants, earthLike, waterWorlds, ringed, belts]` (also in `meta.summaryFields`) |

The summary comes from the real system generator, `Galaxy.GetSystem`; Sol uses the hand-written Solar System.
- `stars` includes companions.
- `landable` counts planets and moons together.
- `ringed` counts planets with rings.

`bodies` holds the body list of each system within 40 ly (649 systems), keyed by system index as a string, e.g.
`HOLLOW_LOCAL.bodies["0"]` is Sol. Each body is an array, with fields listed in `meta.bodyFields`:

`[name, kind, type, parent, aAU, radiusKm, gravityG, tempK, atmosphere, landable, rings]`

- `kind` is 0 for a star, 1 a planet, 2 a moon, 3 a belt.
- `type` is a spectral class for stars, or `"Gas giant"`, `"Rocky ice world"`, `"Asteroid belt"` and so on.
- `parent` is the index of the body orbited, or -1 for the primary star.
- `aAU` is the semi-major axis around the parent in AU, to 5 significant digits; it is 0 for the primary.
- `gravityG` is surface gravity in g.
- `tempK` is the effective temperature for stars and the mean surface temperature for planets and moons.
- `atmosphere` is `none`, `thin`, `thick`, `breathable` or `hydrogen-helium`.
- `landable` and `rings` are 0 or 1.

**Counts:**
- 37,409 systems: 7,124 catalogue and 30,285 procedural. 29,699 are scoopable.
- 9 systems within 10 ly and 1,434 within 50 ly; GALAXY.md reports 9 and about 1,430.
- There are no procedural stars within 30 ly, because the catalogue is complete there.

**Known systems** (index: name, position, class):
- 0: Sol (0, 0, 0), G2 V. 33 bodies: 9 planets, 21 moons, 2 belts.
- 1: Proxima Centauri (-3.04, -0.14, 2.93), M5 V. It is a separate system in Hollow.
- 2: Alpha Centauri (-3.02, -0.05, 3.09), G2 V. Its bodies are Alpha Centauri A and B, plus 5 generated planets.
- 3: Barnard's Star (2.97, 1.44, 4.94), M4 V.
- 7: Sirius (-6.24, -1.33, -5.77), A0 V, with Sirius B (DA2).

## galaxy.js: `HOLLOW_GALAXY`

### `stars`: the zoomed-out sample

There are 250,000 points drawn from Hollow's **density model** (`DensityModel`, all six components) and **star-class
table** (`StarClassTable.Pick`/`Roll`).
- They are a statistical sample of the galaxy, not individual generated stars, so they have no addresses or names.
- Sampling is stratified by level, with sample counts in `perLevel`: 70k, 60k, 50k, 20k, 15k, 12k, 12k and 11k for
  levels 0 to 7.
- `levelSystems[L]` is how many real systems of level L the sampled volume holds, so one point of level L stands
  for `levelSystems[L] / perLevel[L]` systems. That is about 20 systems at level 0 and 7.8 million at level 7.
- The total is 1.2×10¹¹.
- Coverage is ±62,000 ly around Sgr A* in the plane, and up to 12,000 ly above and below the midplane.

The arrays are base64, little-endian typed arrays:

| Field | Type | Per star |
|---|---|---|
| `pos` | Float32Array | x, y, z (ly, galactic frame) |
| `col` | Uint8Array | r, g, b (same colour logic as the maps) |
| `mag` | Uint8Array | M_V = `mag / 8 - 12` |
| `lvl` | Uint8Array | level 0–7 |
| `comp` | Uint8Array | density component: 0 young disk, 1 thin disk, 2 thick disk, 3 bulge/bar, 4 nuclear disk, 5 halo |
| `bright` | Uint8Array | 0–255: log10 of the light this point stands for (its level weight × its V luminosity), between `brightLog10Min` and `brightLog10Max` |

```js
const bytes = b64 => Uint8Array.from(atob(b64), c => c.charCodeAt(0));
const S = HOLLOW_GALAXY.stars;
const pos = new Float32Array(bytes(S.pos).buffer);   // 3 per star
const col = bytes(S.col);                             // 3 per star
const bright = bytes(S.bright), lvl = bytes(S.lvl), comp = bytes(S.comp);
```

**Rendering tip.** `bright` is physically weighted, so the old bulge and disk giants dominate. That gives a
V-band look: a strong bar, with soft arms. The spiral arms stand out in the young stars: draw `lvl <= 1` or
`comp == 0` (young disk) brighter or bluer. Level 0 alone (70k points) shows the four arms crisply.

### `glow`: face-on density image

The image is 248 × 248 cells of 500 ly each, centred on the Galactic Centre and projected along Y. It is
row-major: `index = iz * size + ix`.
- Cell centre: `X = x0 + (ix + 0.5) * cellLy`, `Z = z0 + (iz + 0.5) * cellLy`.
- `all` counts every system per column; `young` counts the young disk only, which traces the arms.
- Byte 0 means empty. Otherwise `log10(systems) = log10Min + (byte - 1) / 254 * (log10Max - log10Min)`.

### `structures` and `armPaths`: labels

`structures` lists label anchors (`label: [x, y, z]`), placed where Hollow's density model puts each structure:

| Name | Kind | Note |
|---|---|---|
| Galactic Centre | centre | At Sgr A* |
| Galactic Bar | bar | The label sits on the near half. `ends` gives both ends of the long axis, at e⁻² density, ±10,300 ly. |
| Scutum–Centaurus Arm | arm | Hollow's arm crossing the Sun–centre line at R = 16,140 ly |
| Sagittarius–Carina Arm | arm | R = 22,500 ly, 4,200 ly inside the Sun |
| Perseus Arm | arm | R = 31,300 ly, 4,600 ly outside the Sun |
| Norma Arm | arm | The inner part of the arm at R = 43,700 ly. It is labelled at R = 14,000 ly, toward longitude 328°. |
| Outer Arm | arm | The same arm beyond the Sun, labelled at R = 44,000 ly |
| Orion Spur | spur | Hollow's local spur, R = 27,070 ly, passing 400 ly outside the Sun |

Hollow's code calls these arms "inner arm", "near arm", "far arm", "outer arm" and "local spur". The real names
follow from where each arm crosses the line from the Sun to the Galactic Centre.
- Every label point was checked against `DensityModel.ArmStrength`: 1.0 on the main arms, 0.8 on the weaker
  outer arm, and 0.62 on the spur.
- `armPaths` gives each arm's centre line on the midplane as `[x, y, z, weight]` points. `weight` (0–1) is the
  arm's own strength there, so a line can fade where the arm fades.

### `sectors`

There are 203 sectors of 1,024 ly whose cube comes within 3,000 ly of Sol, each as
`[name, sx, sy, sz, centreX, centreY, centreZ]`.
- They are sorted by centre distance.
- Sol is at the centre of its own sector, "Nolcas Peris" (64, 64, 38), so sector centres fall on multiples of
  1,024 ly.

## mock.js: `HOLLOW_MOCK` (MOCK, not in the game)

All of this is invented for the prototypes. It is deterministic (seeded), and the game has none of it yet.
- Every `sys` field is an index into `HOLLOW_LOCAL.systems` from the same export; check `meta.localCount`.
- `addr`, `system`, `pos` and `distanceLy` are repeated for convenience.

- **`ship`**: "Wayfarer".
  - Jump range 12.5 ly; the tank holds 6 jumps of fuel and is full. Each jump of up to 12.5 ly uses 1.
  - It refills at a scoopable star (`scoop = 1`) or at any system with a station.
  - It starts at the home base.
- **`home`**: the player's home base, on Gliese 54.1 1 (a rocky ice world) in Gliese 54.1, 12.0 ly from Sol.
  It was picked as the system 10–15 ly away with a scoopable star and the most landable bodies.
- **`stations`**: 36 stations with `name`, `type` and `services`.
  - `type` is `orbital` (around a planet), `outpost` (on a landable body) or `depot` (deep space).
  - `services` always includes `refuel`; the others are `repair`, `market` and `shipyard`.
  - One station is at Earth. Placement is weighted by `exp(-d/18 ly)`, so stations are densest near Sol per unit
    of volume: 3 within 20 ly and 21 within 50 ly.
- **`players`**: 5 other players' bases (`handle`, `relation` friendly/neutral/hostile), 15–100 ly from Sol.
- **`danger`**: two spheres (`centre`, `radiusLy`, `effect`), neither covering the home base:
  - "Raider ambush zone", hostile, 12 ly radius;
  - "Ion storm front", hazard, 20 ly radius.
- **`resources`**: resource hints from body type and temperature: iron, nickel, silicates, ice, water,
  volatiles, organics and rare metals, as a bit mask where bit k is `names[k]`.
  - `systemMask[i]` is the OR over system i's landable bodies, for all 37,409 systems.
  - `bodies` lists `[sys, bodyIndex, mask]` for each landable body of the systems that have body lists (2,894).

Station names and player handles are made up from our own syllables. They are not taken from any game.

## Regenerate

The data come from `tools/Hollow.MapExport`. It reads the real generator and `game/data/catalog/hyg41.bin.gz`,
and changes nothing in `src/`. From the repository root:

```bash
dotnet run --project tools/Hollow.MapExport -c Release            # writes prototypes/map-ux/data/
dotnet run --project tools/Hollow.MapExport -c Release -- <dir>   # or somewhere else
```

The export takes about 10 seconds and is deterministic: two runs give byte-identical files. If the generator
changes (new golden values), regenerate so the prototypes match the game.

# Hollow Universe — Galaxy Generation

Status: draft 1, 10 October 2026. Specifies milestone M1: the galaxy, its star systems, and how the maps
read them. Generator version: **`GalaxyVersion = 1` (development, not frozen)**.

The rules in [ARCHITECTURE.md §8](ARCHITECTURE.md#8-determinism-rules) apply to everything here: no
platform maths, no unseeded randomness, one RNG stream per object.

---

## 1. Goals

- A Milky Way at real scale: about 120 billion star systems in a disk about 100,000 light-years across.
- Real stars near Sol, from a public catalogue, with their real names.
- Procedural stars everywhere else, generated on demand, never stored.
- A structure the galaxy map can stream: bright stars visible from far away, faint ones appearing as you
  zoom in.
- Every system has a stable 64-bit address and a name that can be typed back into a search.
- Every system has stars, planets, moons, rings and belts that look physically plausible.

Out of scope for M1: planet surfaces, resources, stations, nebulae as objects.

---

## 2. Frames and constants

**Galactic frame** (ARCHITECTURE.md §6): light-years, origin Sol, +Z toward the Galactic Centre
(l = 0°, b = 0°), +Y toward the north galactic pole (b = +90°), +X toward l = 90°.

A direction given as galactic longitude *l* and latitude *b* is `(cos b · sin l, sin b, cos b · cos l)`.

Converting the catalogue's equatorial coordinates (J2000, +x to the vernal equinox, +z to the north
celestial pole) uses the standard Hipparcos rotation. With equatorial vector `e`:

```
toward GC   (our +Z) = (-0.0548755604162154, -0.8734370902348850, -0.4838350155487132) · e
toward l=90 (our +X) = (+0.4941094278755837, -0.4448296299600112, +0.7469822444972189) · e
toward NGP  (our +Y) = (-0.8676661490190047, -0.1980763734312015, +0.4559837761750669) · e
```

| Constant | Value |
|---|---|
| Light-years per parsec | 3.261563777 |
| Sun to Galactic Centre (R₀) | 8.178 kpc = 26,673 ly |
| Galactic Centre (Sgr A*) | l = 359.944°, b = −0.046° → (−26.1, −21.4, 26,673) ly |
| Sun above the disk midplane | 20.8 pc = 67.8 ly |
| Total systems (target) | ~1.4 × 10¹¹ |
| Local density at the Sun (target) | 0.00233 systems per ly³ (0.081 per pc³: stars, white dwarfs and brown dwarfs, counting multiples once) |

**Model frame**: the density model works in a frame centred on Sgr A* with the disk midplane horizontal.
`p_model = Rx(φ) · (p_galactic − GC)`, where the tiny tilt φ (≈ 0.0997°) is chosen so the Sun lands
67.8 ly above the midplane. In the model frame, R is the distance from the axis (`√(x² + z²)`), h is the
height (`y`), and the azimuth is `θ = atan2(x, −z)`: 0 toward the Sun, increasing in the direction of
rotation.

---

## 3. Density model

The number density of systems is the sum of six components. All lengths in ly.

| Component | Shape | Parameters | Population |
|---|---|---|---|
| Young disk | `exp(−(R−R₀)/h_R) · sech²(h / 2h_z) · (0.15 + arms)` | h_R = 8,480, h_z = 330 | Young |
| Thin disk | `exp(−(R−R₀)/h_R) · sech²(h / 2h_z) · (1 + 0.3 · arms_wide)` | h_R = 8,480, h_z = 980 | Disk |
| Thick disk | `exp(−(R−R₀)/h_R) · sech²(h / 2h_z)` | h_R = 11,740, h_z = 2,935 | Old |
| Bulge / bar | `exp(−r_s² / 2)`, `r_s = (((x'/a)² + (y'/b)²)² + (z'/c)⁴)^¼` | a = 5,150, b = 2,020 (in plane), c = 1,400 (vertical); bar major axis at θ = +27° | Old |
| Nuclear disk | `exp(−√(R² + (h/0.25)²) / 300)` | | Old |
| Stellar halo | `(R² + (h/0.65)² + 3000²)^−1.4` | | Old |

- **Disk hole and edge**: the three disks are multiplied by `1 − 0.8 · exp(−(R/8000)²)` (the bar region is
  dominated by the bulge) and by `exp(−max(0, R − 49,000) / 1,500)` (the disk thins out beyond 15 kpc).
- **Normalisation**: at the Sun, young : thin : thick : halo = 2% : 86% : 11.8% : 0.2% of 0.00233 per ly³.
  The bulge is scaled to 3.5 × 10¹⁰ systems in total, the nuclear disk to 10⁹. Constants are computed once
  at start-up from these targets (deterministically).
- **Density cap**: the total is capped at 1 system per ly³ (only reached in the inner few hundred ly of the
  core).

### 3.1 Spiral arms

Four main arms are logarithmic spirals with pitch angle p = 12°, trailing:
`θ_k(R) = θ_k0 − ln(R / R₀) / tan p`. Each arm is placed by its radius where it crosses θ = 0 (the
Sun's direction):

| Arm (our own names) | Radius at θ = 0 | Strength |
|---|---|---|
| Inner arm | 16,140 ly (4.95 kpc) | 1.0 |
| Near arm (inside the Sun) | 22,500 ly (6.9 kpc) | 1.0 |
| Far arm (outside the Sun) | 31,300 ly (9.6 kpc) | 1.0 |
| Outer arm | 43,700 ly (13.4 kpc) | 0.8 |
| Local spur (passes just outside the Sun, only for −40° < θ < +35°) | 27,070 ly (8.3 kpc), pitch 11° | 0.6 |

For a point at (R, θ), the distance to arm k is `d_k = R · |wrap(θ − θ_k(R))| · sin p`.
`arms = Σ strength_k · exp(−d_k² / 2σ²)` with σ = 1,300 ly for young stars and 2,600 ly for the wide
version. Arms fade in between R = 9,000 and 13,000 ly and fade out between 46,000 and 59,000 ly.

---

## 4. Star classes and populations

Each generated system has a **primary star** drawn from a class table. Every entry belongs to exactly one
**level** (§5), set by its absolute visual magnitude M_V:

| Level | M_V range | Cell size | Typical members |
|---|---|---|---|
| 0 | < −3.0 | 1024 ly | O and B0 main sequence, supergiants, Wolf–Rayet |
| 1 | −3.0 … −0.5 | 512 ly | B1–B6, bright red giants, carbon stars |
| 2 | −0.5 … 1.5 | 256 ly | B7–A2, red clump and K giants, Herbig Ae/Be |
| 3 | 1.5 … 2.8 | 128 ly | A3–F0 |
| 4 | 2.8 … 4.2 | 64 ly | F1–F9, subgiants |
| 5 | 4.2 … 5.8 | 32 ly | G, T Tauri |
| 6 | 5.8 … 9.0 | 16 ly | K0–M0 |
| 7 | ≥ 9.0 | 8 ly | M1–M9, brown dwarfs, white dwarfs, neutron stars, black holes |

The class table (`StarClassTable`) holds, per entry: level, spectral range, luminosity class, and a weight
for each population. Local weights (fraction of systems), before normalising:

| Entry | Level | Young | Disk | Old |
|---|---|---|---|---|
| O main sequence (O3–O9.5) | 0 | 0.000015 | 0 | 0 |
| B0 main sequence | 0 | 0.002 | 0 | 0 |
| Supergiant (B–M, I) | 0 | 0.0015 | 0 | 0 |
| Wolf–Rayet | 0 | 0.000005 | 0 | 0 |
| B1–B6 main sequence | 1 | 0.028 | 0.0001 | 0 |
| Red giant, bright (M III) | 1 | 0.0005 | 0.0007 | 0.0015 |
| Carbon star | 1 | 0 | 0.00005 | 0.0001 |
| B7–A2 main sequence | 2 | 0.035 | 0.0004 | 0 |
| Giant (G8–K5 III, red clump) | 2 | 0.001 | 0.0045 | 0.008 |
| Herbig Ae/Be | 2 | 0.0025 | 0 | 0 |
| A3–F0 main sequence | 3 | 0.06 | 0.004 | 0.0005 |
| F1–F9 main sequence | 4 | 0.06 | 0.030 | 0.015 |
| Subgiant (F–K IV) | 4 | 0 | 0.010 | 0.012 |
| G main sequence | 5 | 0.06 | 0.077 | 0.06 |
| T Tauri | 5 | 0.04 | 0 | 0 |
| K0–M0 main sequence | 6 | 0.10 | 0.18 | 0.18 |
| M1–M9 main sequence | 7 | 0.40 | 0.52 | 0.50 |
| Brown dwarf (L, T, Y) | 7 | 0.15 | 0.15 | 0.14 |
| White dwarf | 7 | 0 | 0.065 | 0.09 |
| Neutron star | 7 | 0.0005 | 0.0003 | 0.0004 |
| Black hole | 7 | 0.00005 | 0.00005 | 0.00008 |

These weights are tunable until the generator is frozen. They give the local mix of the real solar
neighbourhood: about 76% M dwarfs and brown dwarfs, 7.5% G, 0.6% A, 0.1% B, a few O stars per hundred
million systems.

Physical properties come from interpolation tables (main sequence, giants, supergiants, white dwarfs,
brown dwarfs): effective temperature, M_V, mass, radius, luminosity. Each star adds a small seeded scatter
so no two are identical. Colours are blackbody colours of the effective temperature.

---

## 5. Sectors, levels and cells

- A **sector** is a 1024-ly cube. The grid is 128 × 128 × 128 sectors, anchored so that Sol sits at the
  centre of sector (64, 64, 38). Grid origin in the galactic frame: (−66,048, −66,048, −39,424) ly.
  The grid covers the whole disk, the bulge and the inner halo.
- Each sector is an octree. **Level L** splits the sector into 2^L × 2^L × 2^L **cells** of
  1024 / 2^L ly.
- A star lives at the level of its class (§4), in the cell containing its position. **Bright stars live in
  big cells.** To show the sky as seen from far away you only need levels 0–2; zooming in adds finer levels.
  Typical counts near Sol: one level-7 star per 8-ly cell, about 300 level-0 stars per 1024-ly cell in the
  disk.

---

## 6. System addresses

64 bits, most significant first.

**Procedural** (bit 63 = 0):

| Bits | Field |
|---|---|
| 63 | 0 |
| 60–62 | level L (0–7) |
| 39–59 | sector: x (7 bits), y (7), z (7) |
| next 3·L bits | cell inside the sector: x (L bits), y (L), z (L) |
| remaining 39 − 3·L bits | star index inside the cell |

A level-7 cell can hold 262,143 stars; a level-0 cell 549 billion. The density cap keeps real counts far
below both.

**Catalogue** (bit 63 = 1): bits 56–62 = catalogue number (0 = HYG 4.1), bits 0–55 = the catalogue's own ID.

---

## 7. Generating a cell

Input: (galaxy seed, level, sector, cell). Output: the list of stars in that cell. Pure function.

1. **Integrate the density** over the cell by sampling it on a regular grid (4 × 4 × 4 points; 4 × 8 × 4
   for levels 0–2, whose cells are taller than the disk is thick). This gives the expected number of systems
   per population in the cell.
2. **Expected stars at this level** = Σ over populations of (systems in population) × (fraction of that
   population's class weight that belongs to this level).
3. **Count**: `n = Poisson(λ)` using the cell's RNG stream.
4. **For each star i in 0 … n−1**, with an RNG stream seeded from its full address:
   - choose the population (weighted by step 2),
   - choose the class entry within this level (weighted by that population's weights),
   - choose the position: rejection sampling inside the cell against that population's density, at most
     16 tries,
   - **catalogue suppression** (§8.3): if the star falls where the catalogue is complete for this level,
     drop it. Its index stays unused, so other stars keep their addresses. In the fade-in zone the
     keep-or-drop roll comes from a stream of its own (`Streams.Suppression`), so a star that survives is
     identical to the one an unsuppressed run would make,
   - derive temperature, magnitude, mass, radius, luminosity and metallicity.
5. Stars keep their index as their address. The list is sorted by index.

Cost: well under a millisecond for typical cells. The map generates thousands of cells on worker threads.

---

## 8. Real stars

### 8.1 Source

**HYG database v4.1** (David Nash / astronexus, CC BY-SA 4.0): about 120,000 stars from Hipparcos, Yale
Bright Star and Gliese, with IAU proper names. The build tool `tools/Hollow.CatalogBuilder` converts the CSV
into `game/data/catalog/hyg41.bin.gz` (a compact binary). The derived file is published under the same
licence with attribution (`game/data/catalog/LICENSE.md`). AT-HYG (2.5 million stars) is a later upgrade.

### 8.2 Import rules

- Skip stars with missing or dubious distances (HYG marks them with distance ≥ 100,000 pc).
- Keep stars within 1,500 ly, and luminous stars (level 0–1) within 3,000 ly. Beyond that, catalogue
  distances are too uncertain; procedural stars fill in.
- Convert positions to the galactic frame (§2).
- Spectral type: parse the catalogue's type (class, subclass, luminosity class, white dwarfs `D…`). If it's
  missing, estimate the temperature from the colour index.
- Level: from the absolute magnitude, with the thresholds in §4.
- **Multiple stars**: a star whose `comp_primary` points to another star becomes a companion in that star's
  system. The system's address is the primary's.
- **Names**, first available: proper name (Sol, Sirius, Proxima Centauri); Bayer letter with the full
  constellation genitive (Alpha Canis Majoris); Flamsteed number (61 Cygni); Gliese (Gliese 412); HD; HIP;
  `HYG <id>`. All of them are searchable.

### 8.3 Completeness radii

Near Sol, the catalogue already contains the real stars, so procedural stars of the same level are removed
there. For bright levels the radius is where a star of that level's faintest magnitude drops below what the
catalogue reliably lists (apparent magnitude ≈ 8). For the faint levels 6–7 it is where the Gliese catalogue
of nearby stars stops being nearly complete:

| Level | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
|---|---|---|---|---|---|---|---|---|
| Suppression radius (ly) | 3,000 | 1,500 | 650 | 350 | 190 | 90 | 50 | 30 |

Procedural stars are fully removed inside the radius and fade in to normal over the next 25%. The result:
real stars where we know them, procedural stars where we don't, and no doubled neighbourhood.

Suppression happens **whether or not the catalogue is loaded**. A client missing the catalogue file shows an
empty neighbourhood (and logs an error) rather than generating different procedural stars from the server.
A test compares cells near the radii with and without the catalogue.

---

## 9. Names

**Sectors** have two-word names, such as "Vasken Orrimar", built from original syllable tables:
first word 64 × 64 combinations, second word 32 × 16, giving exactly 2²¹ names, one per sector. The sector
index is scrambled with a fixed odd multiplier first, so neighbouring sectors don't get similar names. The
mapping is one-to-one, so a name parses back to its sector. A test checks that every name parses uniquely.

**Procedural systems**: `<Sector> <Cell> <Level>-<Index>`, for example `Vasken Orrimar QX-AB 6-23`.

- `<Cell>` is the cell index inside the sector written in letters (base 26): 1 letter at level 1, up to
  5 at level 7, with a dash after the first two. Level 0 has no cell part: `Vasken Orrimar 0-12`.
- `<Level>` is the level digit, `<Index>` the star index.

**Catalogue systems** keep their real names. **Bodies** add designations: companion stars `B`, `C`;
planets `1`, `2`, …; moons `a`, `b`, …; belts `Belt A`. Example: `Vasken Orrimar QX-AB 6-23 B 3 a`.

---

## 10. Star systems

Generated from the system address. Sol, its planets and its main moons are hand-written from real data.

**Random streams.** The system's key is `Hash64(seed, "SYST", address)`. Each object draws from its own
stream derived from the key: the layout (companion count, orbit slots, which slots hold planets, whether there
is a belt), the primary, each companion, each orbit slot, and each moon. Changing how one moon is generated
never shifts a planet. Bodies have stable paths (ARCHITECTURE.md §7): companions `(i + 1) << 24`, a planet
`(slot + 1) << 16`, its moons add `(m + 1) << 8`, a belt sets bit `0x80`. A planet is identified by its orbit
slot, not by its number.

**Stars**

- Catalogue systems take their companions from the catalogue (separation from their positions, eccentricity
  rolled). Procedural systems roll for companions: probability by primary mass (brown dwarfs 15%, M 27%,
  K–G 45%, F–A 55%, B–O 75%), a second companion in 15% of those, mass ratio uniform 0.1–1 (8% are white
  dwarfs), separation log-normal around `50 AU · M^0.3` (σ = 1.5 dex, at most 20,000 AU), eccentricity
  Rayleigh (σ = 0.3, at most 0.85).
- **Floors**: a companion's periapsis is at least three times the sum of the two stars' radii. Multiple
  stars form a stable hierarchy: each wider companion's periapsis is at least three times the previous
  companion's apoapsis. A rolled companion that the hierarchy would push out beyond 40,000 AU (0.63 ly) at
  apoapsis is dropped instead; real companions are always kept.

**Planets**

- Expected count by primary type (M 3.0, K 3.5, G 4.5, F 4.0, A 2.5, B 1.0, O 0.3, giants 1.5, white dwarfs
  0.5, brown dwarfs 0.8, neutron stars 0.2, black holes 0.1), raised by metallicity, at most 14.
- **Planet zone**: from `max(0.02 AU, 0.1 AU · √L, 3 stellar radii)` out to `max(2 AU, 50 AU · √M)`.
  Companions with an apoapsis under 5 AU make a close pair: planets orbit both, beyond three times the
  companion's apoapsis. Wider companions cut the zone at a third of their periapsis.
- **Orbit slots** from the inner edge outward, each 1.4–2.4 times the previous one; planets take a random
  subset of the slots.
- **Orbits never cross**: each planet's eccentricity is limited so its periapsis stays at least 15% beyond
  the previous body's apoapsis and its apoapsis stays inside the next slot divided by 1.3.
- Snow line `2.7 AU · √L`; equilibrium temperature `278.6 K · L^¼ / √a`.
- Types: metal-rich, rocky, rocky-ice, icy, water world, Earth-like (rare: rocky, 0.5–3 Earth masses,
  temperate; 4% of temperate rocky slots around F–K stars, 0.6% around M dwarfs; in the test sample of
  30,000 systems near Sol, about 1 system in 110 has one), ammonia world, ice giant, gas giant. Gas giants
  are more likely beyond the snow line and around metal-rich stars.
- Each planet has mass, radius, gravity, atmosphere (none, thin, thick, Earth-like, hydrogen-helium) and
  surface pressure, surface temperature, rotation (close-in planets are tidally locked), axial tilt, and
  orbital elements (semi-major axis, eccentricity, inclination, node, argument of periapsis, mean anomaly at
  epoch J2000).
- **Moons**: gas giants about 4 major moons, ice giants 2.5, large rocky planets sometimes 1–2, all inside
  0.4 of the planet's Hill radius (at periapsis).
- **Rings**: gas giants 40%, ice giants 30%, rocky 2%.
- **Belts**: about 30% of systems. A belt takes the first chosen slot beyond half the snow line if there is
  room; otherwise it trails the last planet (1.5–2.5 times its orbit, inside the zone). Its width is 20–40% of
  its radius, trimmed so it never touches its neighbours.
- **Landable** means "has a solid surface". Whether it is pleasant to land on is gameplay, not generation.

**Positions over time**: universe time is real UTC. Each body's mean anomaly advances with its orbital
period; Kepler's equation is solved per frame (Newton's method).

---

## 11. Galaxy map

| Camera distance | What is drawn |
|---|---|
| > 60,000 ly | the whole galaxy as a glow: the sky shader ray-marches a volume texture built from the density model |
| 2,200 – 60,000 ly | the glow, the most luminous stars (level 0) around the focus, the sector grid with sector names |
| 240 – 2,200 ly | levels 1–6 join one by one as the camera comes closer |
| < 240 ly | every level, each out to its own radius; names of the brightest named stars; stalks down to the focus plane |

- **Radius per level**: the view radius (1.5 × camera distance) times a reach (7 for level 0 down to 1 for
  level 7), capped per level (3,500 ly for level 0 down to 200 ly for level 7). Stars fade out towards it.
- **Streaming**: cells are generated in blocks (1–8 cells per edge, one point mesh per block) on worker
  threads, coarse levels and nearest blocks first. Meshes are built on the main thread within a few
  milliseconds per frame. Blocks out of range are freed.
- **Star budget**: at most about 100,000 stars per level. A density profile around the focus (the density
  model sampled on 40 shells × 64 directions, about a millisecond; `StarCountProfile`) predicts how many stars
  of each level lie within any radius. Where a level's full radius would hold more, which happens towards the
  core, its radius shrinks to fit. Its blocks then keep only the stars within 1.25 × that radius, and cells
  outside it are never generated. When the focus moves, such blocks are rebuilt; the old mesh stays until the
  new one is ready, and large generated cells are cached (up to about 1.2 million stars) so a rebuild doesn't
  generate them again. Near Sol the budget only matters at the widest zooms. The profile itself is computed
  on a worker thread; blocks are only planned with a profile from near the current focus.
- **Stars** are GPU points: size and brightness from absolute magnitude and distance to the camera, colour
  from temperature. **Crowding**: where a level's stars overlap on screen, their light is cut, first by
  shrinking the points and then by dimming them. A dense region then reads as fine grain and glow instead of
  a white blob.
- **Glow adaptation**: the sky glow is dimmed by how much brighter the sky around the camera is than at Sol
  (inside the bulge, about 35 times), like an eye adapting.
- **Floating origin**: each block's mesh is stored relative to the block's corner, and the scene is
  re-centred on the focus, so positions stay precise even 60,000 ly from Sol.
- **Interaction**: drag to rotate, right-drag or WASD to pan, wheel to zoom, click a star to select it,
  double-click to fly to it, search by any name, open the system map of the selected star. Each block's stars
  are sorted into an 8 × 8 × 8 grid of buckets, so picking and the stalks only look at the buckets near the
  cursor's ray or the focus (a few milliseconds even with 700,000 stars loaded at the core).

## 12. System map

- An orrery of the selected system: stars, planets, moons, rings and belts on their real orbits, animated by
  universe time (with a time-speed control).
- Distances are drawn on a compressed (logarithmic) scale by default so inner planets stay visible; a linear
  scale can be switched on.
- Selecting a body shows its data: type, mass, radius, gravity, temperature, atmosphere, orbit, day length,
  landable.

---

## 13. Tests

| Test | Checks |
|---|---|
| Determinism | Generating the same cells twice, in any order and on any thread, gives identical stars. |
| Golden | A hash over a fixed set of cells and systems matches the stored value for this generator version. |
| `DMath` | Results within 4 ulp of `Math` over wide ranges (measured: 3), and bit-identical on every platform. |
| Frames | The Galactic Centre's equatorial coordinates convert to (0, 0, 1); the north galactic pole to (0, 1, 0). |
| Catalogue | Sirius is 8.6 ly away, Proxima Centauri 4.24 ly, Vega 25 ly; Sol has address and name "Sol"; Toliman belongs to Alpha Centauri. |
| Catalogue independence | Procedural stars near the completeness radii are identical with and without the catalogue loaded, and none lies inside a radius. |
| Suppression | In the fade-in zone, every star that survives is identical (address, position, class, physics) to the one an unsuppressed generator makes. |
| Untrusted addresses | Catalogue addresses with another catalogue number, an out-of-range ID (including one that would truncate to a real ID) or a companion's ID are not found, without errors. |
| Density | Local density 0.00233 per ly³; 8–14 systems within 10 ly and 900–1,900 within 50 ly (measured: 9 and about 1,430); total 1.1–1.7 × 10¹¹ (measured 1.22 × 10¹¹). |
| Star counts | `StarCountProfile` agrees with a Monte Carlo integral of the density within 25%, near Sol, in the disk and at the core. |
| Addresses and names | Address ↔ fields round-trip; name ↔ address round-trip; every sector name parses uniquely. |
| Systems | Sol matches its hand data; moons stay inside their planet's Hill sphere. |
| Orbits | In thousands of systems: neighbouring planets and belts never overlap; companions keep the radius floor and the hierarchy; planets stay clear of companions. |
| Architecture | Nothing in `src/` references Godot; generation code uses no platform maths, unseeded randomness or clocks. |

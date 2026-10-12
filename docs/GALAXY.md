# Hollow Universe — Galaxy Generation

Status: draft 1, 10 October 2026. Specifies milestone M1: the galaxy, its star systems, and how the maps
read them. Generator versions (§14): **galaxy 1, system 1, surface 1 (development, not frozen)**.

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
| 3 | 1.5 … 2.8 | 128 ly | A3–F0, bright subgiants |
| 4 | 2.8 … 4.2 | 64 ly | F1–F9, subgiants |
| 5 | 4.2 … 5.8 | 32 ly | G, T Tauri |
| 6 | 5.8 … 9.0 | 16 ly | K0–M0 |
| 7 | ≥ 9.0 | 8 ly | M1–M9, brown dwarfs, white dwarfs, neutron stars, black holes |

The class table (`StarClassTable`) holds, per entry: level, spectral range, luminosity class, and a weight
for each population. Local weights (fraction of systems), before normalising:

| Entry | Level | Young | Disk | Old |
|---|---|---|---|---|
| O main sequence (O3–O9.5) | 0 | 0.000005 | 0 | 0 |
| B0 main sequence | 0 | 0.00006 | 0 | 0 |
| Supergiant (B–M, I) | 0 | 0.00038 | 0 | 0 |
| Wolf–Rayet | 0 | 0.000003 | 0 | 0 |
| B1–B6 main sequence | 1 | 0.0041 | 0.000015 | 0 |
| Red giant, bright (M III) | 1 | 0.0001 | 0.00014 | 0.0003 |
| Carbon star | 1 | 0 | 0.000001 | 0.000002 |
| B7–A2 main sequence | 2 | 0.061 | 0.0007 | 0 |
| Giant (G8–K5 III, red clump) | 2 | 0.00047 | 0.0021 | 0.0038 |
| Herbig Ae/Be | 2 | 0.00075 | 0 | 0 |
| A3–F0 main sequence | 3 | 0.06 | 0.004 | 0.0005 |
| Bright subgiant (A8–F8 IV) | 3 | 0 | 0.004 | 0.004 |
| F1–F9 main sequence | 4 | 0.047 | 0.0237 | 0.0119 |
| Subgiant (F–K IV) | 4 | 0 | 0.0047 | 0.0056 |
| G main sequence | 5 | 0.042 | 0.054 | 0.042 |
| T Tauri | 5 | 0.006 | 0 | 0 |
| K0–M0 main sequence | 6 | 0.10 | 0.18 | 0.18 |
| M1–M9 main sequence | 7 | 0.40 | 0.52 | 0.50 |
| Brown dwarf (L, T, Y) | 7 | 0.15 | 0.15 | 0.14 |
| White dwarf | 7 | 0 | 0.065 | 0.09 |
| Neutron star | 7 | 0.0005 | 0.0003 | 0.0004 |
| Black hole | 7 | 0.00005 | 0.00005 | 0.00008 |

These weights are tunable until the generator is frozen. They are **calibrated against the real-star
catalogue** where it holds every star of a level (brighter than V ≈ 7.5: level 0 within 1,000 ly, 1 within 800,
2 within 400, 3 within 200, 4 within 160, 5 within 80, 6 and 7 within 20; `tools/Hollow.GeneratorReport
calibrate`). The density model integrated over those spheres now predicts, per level, 70 / 1,203 / 2,599 / 680 /
1,095 / 261 / 14 / 57 systems against 61 / 1,162 / 2,479 / 664 / 1,066 / 243 / 12 / 63 real ones. (The first
table made 8× too many level-0 stars near Sol, 6× too many at level 1, 1.5–2× too many giants, subgiants and G
dwarfs, and had no evolved stars at level 3, where the real sky has 295 within 200 ly.) The local mix of systems:
51% M dwarfs, 15% brown dwarfs, 18% K0–M0, 6.7% white dwarfs, 5.2% G, 2.3% F, 0.7% A, 0.01% B and about one O
star per ten million systems.

Physical properties come from interpolation tables (main sequence, giants, supergiants, white dwarfs,
brown dwarfs): effective temperature, M_V, mass, radius, luminosity. Each star adds a small seeded scatter
so no two are identical. Colours are blackbody colours of the effective temperature.

### 4.1 Ages and evolution

Every star has an **age** (`GalaxyStar.Age`, Gyr; the whole system shares it), drawn from its component's
star-formation history, spread evenly over:

| Component | Young disk | Thin disk | Thick disk | Bulge | Nuclear disk | Halo |
|---|---|---|---|---|---|---|
| Ages (Gyr) | 0–1 | 0.3–9 | 8–12 | 7.5–12.5 | 0.01–12 | 10–13.2 |

The age decides what evolved stars and remnants are like (`StellarEvolution`, fits to standard stellar evolution):

- **Main-sequence lifetime** `log₁₀ t[yr] = 10 − 3.6 x + x²`, `x = log₁₀ M` (1 M☉: 10 Gyr; 2 M☉: 1 Gyr; 10 M☉:
  25 Myr). A main-sequence star is younger than its lifetime: its age is drawn from the component's range cut at
  the lifetime (a heavy star in an old population formed late; old populations get few F dwarfs for that reason).
- **Giants, bright giants, subgiants, carbon stars**: age from the population (at least 40 Myr), mass = the
  **turnoff mass** of that age ± 5%: old-population giants weigh about 1 M☉, young ones several.
  **Supergiants**: 1.0–1.1 times their main-sequence lifetime. T Tauri stars 0.5–10 Myr, Herbig Ae/Be stars
  0.3–5 Myr, Wolf–Rayet stars 2–7 Myr.
- **White dwarfs**: age from the population (at least 50 Myr); the progenitor was heavier than the turnoff mass
  of that age (initial mass function dN/dM ∝ M^−2.3, up to 8 M☉); mass from the initial–final mass relation
  `0.109 M_i + 0.394`; it has cooled since: `T_eff = 8,000 K · (t_cool / Gyr)^−0.35 · (M / 0.6 M☉)^0.2`
  (3,500–150,000 K), so most are cool.
- **Neutron stars**: progenitor 8–20 M☉ (IMF-weighted); surface temperature
  `10⁶ K · (1 + t_cool / 10⁵ yr)^−0.2`. **Black holes**: older than 6 Myr.
- **Brown dwarfs** never ignite hydrogen and cool for ever: mass 0.013–0.078 M☉ (dN/dM ∝ M^−0.6),
  `T_eff = 1,550 K · (M / 0.05 M☉)^0.83 · (t / Gyr)^−0.32`, and the spectral type follows the temperature: young
  or heavy ones are L dwarfs, old light ones T and Y.
- **Real stars** get an age estimate from their class the same way (seeded by catalogue ID, the same in every
  universe; white dwarfs from their temperature). Sol is 4.57 Gyr.
- **Companions** (§10) were born with the primary: their initial mass is a fraction of the primary's initial mass,
  and the system's age decides what they are now. In 10% of systems the companion started heavier, if a star that
  heavy would be dead by now: it is a white dwarf or neutron star, fainter than the primary, like Sirius B.

The planets of a system share its age, which drives their internal heat, atmosphere loss and cratering (§10).

---

## 5. Sectors, levels and cells

- A **sector** is a 1024-ly cube. The grid is 128 × 128 × 128 sectors, anchored so that Sol sits at the
  centre of sector (64, 64, 38). Grid origin in the galactic frame: (−66,048, −66,048, −39,424) ly.
  The grid covers the whole disk, the bulge and the inner halo.
- Each sector is an octree. **Level L** splits the sector into 2^L × 2^L × 2^L **cells** of
  1024 / 2^L ly.
- A star lives at the level of its class (§4), in the cell containing its position. **Bright stars live in
  big cells.** To show the sky as seen from far away you only need levels 0–2; zooming in adds finer levels.
  Typical counts near Sol: one level-7 star per 8-ly cell, about 30 level-0 stars per 1024-ly cell in the
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

Input: (galaxy seed, level, sector, cell). Output: the list of stars in that cell. Pure function. The galaxy
seed is the universe seed mixed with the galaxy-stage version (§14); every stream below starts from it.

1. **Integrate the density** over the cell by sampling it on a regular grid (4 × 4 × 4 points; 4 × 8 × 4
   for levels 0–2, whose cells are taller than the disk is thick). This gives the expected number of systems
   per population in the cell. Each grid point stands for a sub-cell, and its samples are kept for step 4.
2. **Expected stars at this level** = Σ over populations of (systems in population) × (fraction of that
   population's class weight that belongs to this level).
3. **Count**: `n = Poisson(λ)` using the cell's RNG stream.
4. **For each star i in 0 … n−1**, with an RNG stream seeded from its full address:
   - choose the population (weighted by step 2),
   - choose the class entry within this level (weighted by that population's weights),
   - choose the position: first a sub-cell, with probability proportional to that component's sampled density
     there, then rejection sampling inside the sub-cell against the density itself, with a bound of 1.3 times
     the largest sample of the sub-cell and its 26 neighbours, at most 24 tries. A star that runs out of tries
     keeps its last candidate, which still lies in a sub-cell where the component has stars, so no star can land
     where the density says there are none (the old sampler drew from the whole cell and kept an unweighted
     candidate after 16 failures: 0.2% of level-0 stars, bunched where the arms fade out; now none at level 0
     and 57 of 78 million at level 1),
   - **catalogue suppression** (§8.3): if the star falls where the catalogue is complete for this level,
     drop it. Its index stays unused, so other stars keep their addresses. In the fade-in zone the
     keep-or-drop roll comes from a stream of its own (`Streams.Suppression`), so a star that survives is
     identical to the one an unsuppressed run would make,
   - derive temperature, magnitude, mass, radius, luminosity and metallicity.
5. Stars keep their index as their address. The list is sorted by index.

Cost: well under a millisecond for typical cells. The map generates thousands of cells on worker threads.

### 7.1 Galaxy-wide maps

`tools/Hollow.GeneratorReport maps` generates every level across the whole sector grid and draws three contact
sheets in `docs/images/generator/` (levels 0–3 in the top row, 4–7 below; the Galactic Centre above Sol, as on
the galaxy map). Levels 0–2 are generated in full; for levels 3–7 one column of cells per 256-ly pixel stands for
the pixel.

- `face-on.png`: the generated stars in a slab around the midplane (±1,024 ly for levels 0–2, ±256 ly for 3–4,
  ±64 ly for 5–7), brightness from surface density and colour from the stars' mean colour, so the class mix
  shows: the young arms at levels 0–1, the old bar and bulge in red giants at level 1, A stars at level 3, red
  dwarfs at level 7.
- `ratio.png`: generated stars divided by a fine integral of the density model over the same area, per
  2,048-ly block (grey = 1; red = more, blue = fewer, full colour at 1.5×). It shows steps between cells or a
  biased sampler. Measured χ² per block, generated against the integral: 1.00–1.05 at every level, i.e. Poisson
  noise only. The blue squares at Sol are the catalogue's region (§8.3).
- `edge-on.png`: levels 0–3 in a slice through Sol and the Galactic Centre (|x| under half a cell), ±8,192 ly
  high.

Before the generator is frozen these are looked at for artefacts like the ones Elite found after launch (density
steps between sectors, a cross of suppressed stars). The first look (October 2026) found no steps; the only
pattern was the old sampler's smear where the arms fade out, fixed by the sampler in step 4.

---

## 8. Real stars

### 8.1 Source

**HYG database v4.1** (David Nash / astronexus, CC BY-SA 4.0): about 120,000 stars from Hipparcos, Yale
Bright Star and Gliese, with IAU proper names, plus Hollow's hand-entered **supplement** of the nearest brown
dwarfs and ultracool stars that HYG lacks (§8.4). The build tool `tools/Hollow.CatalogBuilder` converts the CSV
into `game/data/catalog/hyg41.bin.gz` (a compact binary, catalogue number 0), which keeps 99,140 stars (98,695
systems) after the rules below. The derived file is published under the same licence with attribution
(`game/data/catalog/LICENSE.md`). AT-HYG (2.5 million stars, Gaia distances) is the planned upgrade (§8.5).

### 8.2 Import rules

- Skip stars with missing or dubious distances (HYG marks them with distance ≥ 100,000 pc).
- Keep exactly the systems where the catalogue counts for the primary's level and distance: completeness `C`
  above zero (§8.3). That ends at the import limit of 1,500 ly (3,000 ly for the luminous levels 0–1), where
  catalogue distances get too uncertain, or earlier where the catalogue holds under 1% of a level (level 7 beyond
  113 ly, for example). The builder applies this rule to the CSV, and `--update` re-applies it to a built file;
  the current file keeps 98,676 of the 100,761 systems the old distance cuts kept.
- Convert positions to the galactic frame (§2).
- Spectral type: parse the catalogue's type (class, subclass, luminosity class, white dwarfs `D…`). If it's
  missing, estimate the temperature from the colour index.
- **Repairs** (every build, and `--update`): a star with neither type nor colour became a G5 dwarf; one fainter
  than +7.5 (no G dwarf is) gets the main-sequence type of its absolute magnitude instead, and so does a "giant"
  fainter than +6 (a garbled type: Luyten's Star, Gliese 581 and Gliese 876 were "M5 III"). The measured magnitude
  is always kept. Explicit fixes by HYG ID: van Maanen's Star is a white dwarf (DZ8), VB 10 an M8 dwarf, Wolf 424 B
  an M7 dwarf; Capella's type is corrected while reading the CSV.
- Level: from the absolute magnitude, with the thresholds in §4.
- **Multiple stars**: a star whose `comp_primary` points to another star becomes a companion in that star's
  system. The system's address is the primary's. **Bindings** join real companions the source lists as systems
  of their own: Proxima Centauri is Alpha Centauri C. Its address (the old Proxima system's) is now a companion's
  and finds nothing; its name leads to Alpha Centauri.
- **Names**, first available: proper name (Sol, Sirius, Proxima Centauri); Bayer letter with the full
  constellation genitive (Alpha Canis Majoris); Flamsteed number (61 Cygni); Gliese (Gliese 412); HD; HIP;
  `HYG <id>`. All of them are searchable.

### 8.3 Completeness

Near Sol the catalogue already holds the real stars, so it replaces part of the procedural population. How much
is the catalogue's **completeness** `C(level, d)`: the fraction of a level's stars at distance `d` from Sol that
the catalogue holds. A procedural star at that level and distance is left out with probability `C` (the roll has
its own stream, `Streams.Suppression`, so a star that survives is identical to the one an unsuppressed generator
makes), and the builder keeps exactly the real stars where `C > 0` (§8.2). Real plus procedural stars then add
up to the density model, with no doubled neighbourhood and no gap.

`C` is analytic and frozen in code (`CatalogueCompleteness`), so it never depends on whether the catalogue file
loaded. It has a complete core that ends near `d₀` (the Gliese catalogue of nearby stars, for the faint levels)
and a bulk share `p` that falls near `d₅₀` (the survey's magnitude limit):

```
c(d) = (1 − p) / (1 + (d / d₀)⁸)  +  p / (1 + (d / d₅₀)^k)
C(d) = clamp((c − 0.01) / 0.98, 0, 1), and 0 beyond the import limit
```

Fitted per level (least squares on real ÷ model counts in 26 shells around Sol, after the class table's
calibration in §4; `tools/Hollow.GeneratorReport shells`):

| Level | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
|---|---|---|---|---|---|---|---|---|
| p | 0.58 | 0.68 | 0.72 | 0.48 | 0.40 | 0.42 | 0.56 | 0.54 |
| d₀ (ly) | 750 | 750 | 501 | 422 | 299 | 178 | 18.8 | 33.5 |
| d₅₀ (ly) | 2,851 | 1,641 | 955 | 733 | 473 | 288 | 146 | 73.3 |
| k | 2.7 | 3.4 | 4.7 | 6.6 | 6.7 | 6.4 | 6.0 | 9.2 |
| Catalogue only (C = 1) within (ly) | 444 | 423 | 313 | 256 | 178 | 106 | 11.7 | 20.8 |
| Procedural only (C = 0) beyond (ly) | 3,000 | 3,000 | 1,500 | 1,314 | 820 | 516 | 285 | 113 |

At levels 0–1 the catalogue still holds a quarter (level 0) or a tenth (level 1) of the stars at the 3,000-ly
import limit, so `C` drops to 0 there in one step; the density stays continuous, because the real stars inside
and the procedural ones outside both follow the model.

Measured (real + generated ÷ model, per shell, `shells`): with the old hard radii (3,000 / 1,500 / 650 / 350 /
190 / 90 / 50 / 30 ly, fading in over 25%) the neighbourhood was doubled beyond them, up to 1.27× at level 2,
2.2× at level 3, 1.5–1.6× at levels 4–7, and 70% of the catalogue sat on a full procedural population; inside
them levels 0–1 had only 4–36% of the model's stars, because the model made far too many luminous stars. Now
every shell with more than a dozen expected stars is within 0.86–1.07× at every level (a test checks eight
shells at levels 2, 4, 6 and 7 within ±15%).

Suppression happens **whether or not the catalogue is loaded**. A client missing the catalogue file shows an
empty neighbourhood (and logs an error) rather than generating different procedural stars from the server.
A test compares cells across the edge of the complete core with and without the catalogue.

### 8.4 The hand-entered supplement

`tools/Hollow.CatalogBuilder/supplement.csv` lists real objects the source catalogue lacks, from public
measurements (the RECONS and Wikipedia lists of nearest stars and brown dwarfs, and their discovery papers). The
builder adds it to every build, after the repairs and bindings; `--update` replaces an earlier supplement, so
running it twice changes nothing. Supplement IDs are 1,000,000–1,999,999, above every HYG ID and below AT-HYG's
ranges (§8.5), so addresses never collide. Supplement systems are always kept, whatever `C` says.

- A **new system** gives its J2000 right ascension and declination, its parallax, spectral type, temperature and
  mass (radius and absolute magnitude are typical for the type).
- A **companion** (of a source star or of a supplement star) gives its separation in AU from its primary, or from
  the star named in `near` (the partner in a close pair). The builder places it across the line of sight at the
  primary's distance, turned about it by an angle from its ID. A separation of 0 means unknown, and the
  generator rolls one.

Contents (30 objects; 28 within 20 ly, where the procedural generator makes nothing at their level, so nothing
is counted twice, and two farther hosts of famous planets, kept whatever `C` says): the brown dwarfs Luhman 16 A and B (6.5 ly, the nearest), WISE 0855−0714 (Y4, 7.4 ly, the
coldest known), UGPS J0722−0540, WISE 1741+2553, WISE 1639−6847, DENIS 0255−4700, WISE 1506+7027,
DENIS J0817−6155, 2MASS 0939−2448 A and B, WISE 0350−5658, 2MASS J1114−2618, 2MASS J0415−0935, WISE 1541−2250,
2MASS 0937+2931 and SIMP J0136+0933 (a planetary-mass object); the ultracool stars Teegarden's Star,
SCR 1845−6357 A (with its brown dwarf B), DENIS J1048−3956, 2MASS J1540−5101 and LSR J1835+3259; and brown-dwarf
companions of HYG stars: Epsilon Indi Ba and Bb (a pair 2.4 AU apart, 1,459 AU from Epsilon Indi A), Gliese 229 Ba
and Bb (a 12-day pair, 33 AU from Gliese 229) and Gliese 570 D (1,525 AU from Gliese 570 A). Masses that were never
measured are typical for the type (noted in the file). The nearby white dwarfs (Sirius B, Procyon B, 40 Eridani B,
Stein 2051 B, van Maanen's Star, Gliese 440, GJ 1221) were already in HYG. Farther out: TRAPPIST-1 (M8, 40.7 ly)
and Kepler-186 (M1, 579 ly), whose planets the authored layer adds (§15); each adds one star to a place the
generator also fills, which no count can notice.

### 8.5 AT-HYG

The builder also reads **AT-HYG v4.0** (Augmented Tycho + HYG, astronexus, CC BY-SA 4.0): about 2.55 million stars
(all of Tycho-2 and HYG) with Gaia DR3 distances for nearly all, positions `x0, y0, z0` (equatorial, parsecs),
`absmag`, `ci`, `spect` and HYG's names and numbers. It becomes catalogue number 1. Its own `id` is a serial number
in right-ascension order, which shifts between versions, so IDs are rebuilt to stay stable: a star in HYG keeps its
HYG ID (Sol stays 0, so a 0:id address maps to 1:id), a Tycho-2 star gets `2²⁴ + (TYC1 · 2¹⁴ + TYC2) · 4 + TYC3`,
and any other `2³⁰ + its AT-HYG serial` (stable only within one AT-HYG version). AT-HYG lists no multiple stars:
they come from the HYG CSV (`--hyg`) through AT-HYG's `hyg` column, else from a shared Gliese number (Gl 559 A and B),
else from a shared Tycho-2 field and number within an arcminute; the brightest member is the primary.

Switching needs, in this order: the file (not in the repository: `data/athyg_40.csv.gz` from
<https://codeberg.org/astronexus/athyg>, 199,688,001 bytes compressed, stored with Git LFS), optionally HYG's CSV
for the multiple stars; a build with `--keep-all`; a new completeness fit for catalogue 1 (`shells`, then a
second parameter set in `CatalogueCompleteness`, since AT-HYG is complete far deeper than HYG); then a normal
build. The catalogue file grows from 4.6 MB to an estimated 100 MB compressed, so it would likely be streamed by
sky region rather than loaded whole.

---

## 9. Names

**Sectors** have two-word names, such as "Nolcas Peris" (Sol's sector) or "Jorver Irum" (the Galactic
Centre's), built from original syllable tables: first word 64 × 64 combinations, second word 32 × 16, giving
exactly 2²¹ names, one per sector. The sector index is scrambled with fixed odd multipliers and xor-shifts first,
so neighbouring sectors don't get similar names. The mapping is one-to-one, so a name parses back to its sector.
A test checks that every name parses uniquely.

**Procedural systems**: `<Sector> <Cell> <Level>-<Index>`, for example `Lumtum Delis FF-WL 6-0`.

- `<Cell>` is the cell index inside the sector written in letters (base 26): 0, 1, 2, 2, 3, 4, 4, 5 letters at
  levels 0–7, with a dash after the first two when there are three or more (`Belath Alum DA 3-17`,
  `Lumtum Delis BR-HSB 7-0`). The letters must decode to a cell that exists at that level. Level 0 has no cell
  part: `Bratum Loron 0-0`.
- `<Level>` is the level digit, `<Index>` the star index.

**Systems of a named region** (a real cluster or nebula, §15.2) take the region's name in place of the sector's:
`Pleiades LA-YS 6-0`, `Orion Nebula CG-HK 5-12`. That applies to a procedural system inside the region's sphere
(the smallest region, where they nest) and in the sector that holds the region's centre, so the name still
decodes to one address (the region gives the sector, the rest the cell and star); the rare systems of a region
that straddles a sector border keep their sector name. The sector form of such a system's name still finds it.
No region name parses as a sector name (a test checks every one). This is Hollow's own format: the region's real
name followed by Hollow's cell code, with no word in between.

**Catalogue systems** keep their real names, and the authored layer (§15) names stars of famous multiple systems
(Proxima Centauri, Epsilon Indi Ba and Bb). **Bodies** add designations. In a multiple system the stars are
`A`, `B`, `C`, … (A is the primary; letters follow the orbit tree, inner members first) and a barycentre is
named by its stars (`AB`, `ABC`, `BC`). Planets are numbered outward around their host, `1`, `2`, …, after the
host's letters: `<system> B 3` orbits star B, `<system> AB 1` orbits the pair A and B; moons add `a`, `b`, …
(`<system> B 3 a`). A single star's planets have no letter (`Lumtum Delis FF-WL 6-0 2 a`). A host has at most one
belt, `<system> Belt` (or `<system> B Belt`); it takes no number.

---

## 10. Star systems

Generated from the system address. Sol, its planets and its main moons are hand-written from real data.

**Random streams.** The system's key is `Hash64(systemSeed, "SYST", address)`, where the system seed mixes the
universe seed with the system-stage version (§14). Each object draws from its own stream derived from the key:
the layout (companion count), each companion, each star, each pair's orbit, each host's layout (orbit slots,
which slots hold planets, whether there is a belt), each orbit slot and each moon. Changing how one moon is
generated never shifts a planet.

### 10.1 The orbit tree and body paths

A system is a **tree of bodies**. The root (body 0) is the only star or, for multiple stars, the outermost
barycentre, fixed at the origin of the system frame; everything else orbits its parent on a Keplerian orbit.
Multiple stars are **hierarchical**: the primary and its innermost companion orbit their barycentre; that pair and
the next companion orbit theirs, and so on — `(((A B) C) D)`. A wide companion (beyond 50 AU) is itself a close
pair a quarter of the time, `((A (B C))`, its two stars orbiting each other within a fifth of their distance from
the rest. The two members of every pair orbit their barycentre on opposite sides, each at its share of the
separation (`a₁ = a · m₂ / (m₁ + m₂)`, periapsis arguments π apart, one period from the pair's total mass), so the
stars' centre of mass stays at the origin. A system has at most 8 stars and 7 barycentres; the generator makes up
to 5 stars.

**Every star and every barycentre is a planet host** (S-type around one star, P-type around a pair). Each host
draws its own zone, slots, planet count and belt, with its own layout stream. Light from stars outside a host
reaches its planets too (§10.2).

**Body paths** (`BodyPath`, 32 bits, ARCHITECTURE.md §7) name a body for ever, so they never depend on numbering
that could shift (a planet is its host's orbit slot, not its number; a suppressed slot keeps its path unused):

| Bits | Field |
|---|---|
| 28–31 | node: the star (1–8, A–H) or barycentre (9–15, inner pairs first) the body is or orbits |
| 20–27 | orbit slot + 1 around that node (0 = the node itself) |
| 12–19 | moon + 1 (0 = the planet itself) |
| 4–11 | reserved for moons of moons (0) |
| 0–3 | feature: 0 = the body, 1 = a belt in that slot |

Sol's planets are slots 0–8 of node 1 (the Sun), its belts slots 20 and 21.

### 10.2 Stars, planets, moons

**Stars**

- Catalogue systems take their companions from the catalogue, eccentricity rolled. A companion's separation is
  its angle from the primary on the sky times the primary's distance: the catalogue's distances of the stars of
  one system often differ by far more than the stars are apart (parallax errors put Procyon B 3,458 AU from
  Procyon instead of 15, 61 Cygni B 2,375 AU from A instead of about 86). A companion at the primary's very
  position has an unknown separation, which is rolled (log-normal around 40 AU). Two companions much closer to
  each other (under a fifth of their separation from the primary) orbit each other as a close pair: Epsilon Indi
  Ba and Bb, Gliese 229 Ba and Bb. A catalogue star listed **alone** beyond 25 ly rolls companions like a
  procedural star and keeps only those the catalogue can't have seen: too faint at that distance (the
  catalogue holds under half of their level, §8.3) or too close to resolve (under 1″). Within 25 ly the
  neighbourhood's multiple stars are known, so none are added. Procedural systems roll for companions, with a probability by primary
  mass: under 0.08 M☉ (brown dwarfs) 15%, under 0.5 M☉ 27%, under 1.3 M☉ 45%, under 3 M☉ 55%, above that 75%;
  neutron stars, black holes and Wolf–Rayet stars 20%. A second companion comes in 15% of those systems.
  A companion's initial mass is the primary's initial mass times a uniform 0.1–1 (at least 0.013 M☉), or in 10%
  of systems 1–3 times it if such a star would have died by now; the system's age then makes it a brown dwarf, a
  main-sequence star, a giant (only beside an evolved primary), a white dwarf, a neutron star or a black hole
  (§4.1). Separation log-normal around `50 AU · M^0.3` (σ = 1.5 dex, 0.05–20,000 AU), eccentricity Rayleigh
  (σ = 0.3, at most 0.85). A companion wider than 50 AU is a close pair in 25% of cases: the second star's initial
  mass is the first's times 0.2–1, separation log-uniform 0.02 AU to 5% of the wide orbit, eccentricity Rayleigh
  (σ = 0.15, at most 0.5).
- **Floors**: two stars' periapsis is at least three times the sum of their radii. Multiple stars form a stable
  hierarchy: each wider companion's periapsis is at least three times the previous companion's apoapsis, and a
  companion pair's apoapsis is at most a fifth of its own periapsis around the rest. A rolled companion that the
  hierarchy would push out beyond 40,000 AU (0.63 ly) at apoapsis is dropped instead; real companions are always
  kept.

**Planets**

- Expected count per host by its (heaviest) star's type (M 3.0, K 3.5, G 4.5, F 4.0, A 2.5, B 1.0, O 0.3, giants
  1.5, white dwarfs 0.5, brown dwarfs 0.8, neutron stars 0.2, black holes 0.1), raised by metallicity, at most 14.
  A pair (circumbinary planets) gets half. A close companion stunts the disc planets form in: around one star of
  a pair the rate falls smoothly (in log distance) from full at a closest approach of 50 AU to zero at 3 AU.
- **Planet zone** around a star: from `max(0.01 AU, 0.03 AU · √L, 3 stellar radii)` (where the equilibrium
  temperature reaches about 1,600 K, so close-in hot worlds exist: metal-rich worlds, hot giants) out to
  `max(2 AU, 50 AU · √M)`. Around a pair: from `max(0.03 AU · √L, a · c_P)` with the pair's total light and mass.
  Every host's zone ends where planets stop being stable against what orbits outside it: for each enclosing pair,
  at `a · c_S` of that pair. `c_S` and `c_P` are the stability limits for planets in binaries (Holman & Wiegert
  1999), with `μ` the perturbing (or lighter) star's share of the pair's mass and `e` its eccentricity:
  `c_S = 0.464 − 0.380μ − 0.631e + 0.586μe + 0.150e² − 0.198μe²` (0.01–0.5) and
  `c_P = 1.60 + 5.10e − 2.22e² + 4.12μ − 4.27eμ − 5.09μ² + 4.61e²μ²` (1.5–6).
- **Orbit slots** from the inner edge outward, each 1.4–2.4 times the previous one; planets take a weighted
  random subset of the slots. Slots beyond `0.1 AU · √L` (orbits of about ten days around the Sun) all weigh
  1; closer in, real planets get rarer quickly, so a slot weighs `(a / 0.1 AU·√L)³`. In the sample of 30,000
  systems near Sol about 1 system in 120 has a gas giant above 1,000 K, and 2% of planets are metal-rich.
- **Orbits never cross**: each planet's eccentricity is limited so its periapsis stays at least 15% beyond
  the previous body's apoapsis and its apoapsis stays inside the next slot divided by 1.3.
- Snow line `2.7 AU · √L` (the host's own light). **Companion light**: the equilibrium temperature is
  `278.6 K · (L / a² + F)^¼`, where `L` is the host's light (both stars for a pair) and `F` the light of the stars
  outside the host, each at the orbit-averaged distance of the pair that joins it to the host,
  `F = Σ L_out / (a² √(1 − e²))`.
- **How a planet forms**: around a slot hotter than 1,000 K a gas giant with probability `0.05 × g` (at most
  0.3); inside the snow line `0.08 × g × m` (at most 0.5); beyond it a gas giant with `0.3 × g × m` (0.05–0.7),
  an ice giant with 0.25, otherwise a solid planet (`g` grows with metallicity, `m` with the star's mass). Masses
  are log-uniform: gas giants 40–4,000, ice giants 8–40, solid planets 0.02–10 Earth masses.
- **Where it formed** (its *formation zone*, the formation distance over the snow line of the young star,
  `2.7 AU · √L_ZAMS`): a solid planet formed at 0.7–1.4 times its orbit; a giant now inside that snow line
  migrated in from 1–3 times it. Moons share their planet's zone.
- Eccentricity Rayleigh (σ 0.08 for giants, 0.05 otherwise), limited so orbits never cross. A solid planet inside
  `0.1 AU · M^⅓` is **tidally locked**: its day is its year, its eccentricity is a fifth of the rolled one (what
  neighbours force after tides circularised it) and its axial tilt a fiftieth. Other planets spin in 10 hours to
  100 days (10% retrograde), tilt Gaussian (σ 25°).
- Everything else about a planet or moon (what it is made of, its heat, air, water, climate and life) is its
  **physical state** (§10.3). Its type, atmosphere label, surface and hazards are derived from that state.
- **Moons**: a Poisson number with mean 4 for gas giants, 2.5 for ice giants and 0.4 for every other planet
  above 0.5 Earth masses (at most 12), all inside 0.4 of the planet's Hill radius (at periapsis, against its
  host's mass). Masses log-uniform from 10⁻⁵ Earth masses to `min(0.03, 3·10⁻⁴ · M_planet)`. The first orbits at
  1.2–2 times `max(2.5 R_planet, 1.15 × the rings' outer edge)`, each next one 1.3–2.2 times further out. A
  quarter are held in a **resonance** with a neighbour that forces an eccentricity of 0.002–0.012 (Io 0.004,
  Europa 0.009); tides have circularised the rest to 10⁻⁶–10⁻⁴. Moons are tidally locked.
- **Rings**: gas giants 40%, ice giants 30%, gas dwarfs 5%, every other planet 2%; 1.2–1.6 planet radii to
  1.3–2.2 times that; mass 10⁻⁹–10⁻⁶ of the planet's. Made of ice where the planet's equilibrium temperature is
  under 170 K, rock and dust under 700 K, metal above.
- **Belts**: about 30% of hosts want one; at most one per host. A belt takes the first chosen slot beyond
  half the snow line if there is room; otherwise it trails the last planet (1.5–2.5 times its orbit, inside the
  zone). Its width is 20–40% of its radius, trimmed so it never touches its neighbours. Its total mass is
  10⁻⁴–0.05 Earth masses (times the metallicity factor); it is metal inside 0.15 of the young star's snow line,
  metal-rich rock inside 0.5, rock inside 1.3 and ice beyond.
- **Landable** means "has a solid surface": rock or ice (§10.3). Giants, gas dwarfs and global oceans are not.
  How dangerous a surface is lives in its hazards; whether that stops anyone is gameplay.

### 10.3 Physical state

Every planet and moon has a **physical state** (`PlanetState`), built top-down by `WorldBuilder` from the star,
the orbit and the system's age: formation zone → bulk composition → radius → internal and tidal heat → atmosphere
(volatile inventories, escape, stripping) → climate (cold traps, water, ice, albedo, greenhouse; carbon dioxide
regulated by weathering) → life and oxygen → crust, volcanism and magnetic field → labels and hazards. The
planet-surface milestone (M4) builds terrain, biomes, vegetation, animals and resources from it. The model is a set
of approximate fits to planetary science, calibrated on the Solar System (a test feeds it the Solar System's inputs,
§13). Each planet and moon draws it from its own stream (`Hash64(system key, "world" + node·256 + slot)` for a
planet; a moon from its own moon stream), with `DMath` and exactly rounded operations only. Sol's planets and
moons have hand-written states from real data (`SolSystem`), which go through the same hazard rules.

**Inputs.** Mass and how it formed (solid, ice giant, gas giant); formation zone; starlight `S` in Earth units
(L☉/AU², including companions' light, §10.2); the host's **activity** `X`, its high-energy output (ultraviolet,
X-rays, flares, wind) relative to the Sun's, from its heaviest star: white dwarf 0.3, black hole 0.5, F dwarf 0.8,
G dwarf and brown dwarf 1, other giants and subgiants 1, O–A dwarfs 1.5, supergiant 2, K dwarf 2, M0–M4 dwarf 4,
M5 and later 16 (late red dwarfs stay saturated for billions of years), neutron star 5, Herbig Ae/Be and
Wolf–Rayet 10, T Tauri 30, all times `1 + 0.3 / age[Gyr]`; the system's age; the orbit, spin and tidal lock; for
a moon its planet's radius and whether it is a gas giant over 150 Earth masses (radiation belts); and whether the
light comes from a **stable host** (an F–M main-sequence star or subgiant) that gives life time.

**1. Composition** (mass fractions; `Composition`):

| Formation zone | Metal | Ice | Rock |
|---|---|---|---|
| under 0.03 (only metal condensed) | 0.45–0.70 | 0 | the rest |
| 0.03–0.85 | 0.22–0.38; a giant impact stripped the mantle (0.45–0.70, like Mercury) in 15% of cases inside 0.25, 3% beyond | 10⁻⁵–3·10⁻³ (log-uniform: water delivered late) | the rest |
| 0.85–1.3 | 0.15–0.30 | 0.02–0.40 | the rest |
| beyond 1.3 | 0.08–0.18 | 0.30–0.55 | the rest |

A solid planet heavier than 1.5 Earth masses may have captured a hydrogen–helium envelope (probability
`clamp((log₁₀ M − 0.18) / 0.6, 0, 0.9)`, 90% from 5 Earth masses) of 0.2–5% of its mass. It keeps it if its core
holds hydrogen (below) and `S · X < 50 (M / 5 M⊕)²`: a **gas dwarf**, radius `R_core (1 + 3.9 f^0.6)`, albedo 0.3,
no surface, hazards crushing pressure, toxic air and storms.

**2. Radius**: `R = (3M / 4πρ)^⅓ · (1 + 2 M/M⊕)^−0.065`, with ρ the uncompressed density of the mix (iron 7.9,
rock 3.3, ice 1.0 g/cm³). Earth comes out at 1.00 R⊕, the Moon 1,738 km, Ganymede 2,630 km. The metal core's radius
is `√metal` of the body's.

**3. Heat** (`HeatFlow`, W/m²). Radiogenic and primordial: `47 TW · M · (metal + rock) · M^0.3 · e^−(age − 4.57)/2.8`
(M in Earth masses, age in Gyr: Earth 0.09 W/m², Mars 0.02). Tides, for tidally locked planets and moons:
`(21/2)(k₂/Q) G M_host² R⁵ n e² / a⁶` with `k₂/Q` 0.02 for icy bodies (over 10% ice), 0.01 for rock, at most
5 W/m² (Io: 2.5; beyond that the interior melts and the orbit circularises).

**4. Atmosphere.** Inventories as fractions of the body's mass: carbon dioxide 2·10⁻⁵–2·10⁻⁴ (log-uniform; Earth's
carbonates hold about 5·10⁻⁵, Venus's air 10⁻⁴); nitrogen 3·10⁻⁷–5·10⁻⁶ (Earth 7·10⁻⁷), twenty times that for
bodies formed beyond twice the snow line (Titan 7·10⁻⁵); argon `10⁻⁸ · (metal + rock) · min(1, age / 4.57)`;
beyond twice the snow line also methane 10⁻⁷–10⁻⁴ and ammonia (2% of the ice). Water: the ice fraction if it is at
least 5%, otherwise half of it (the rest stays in the mantle).
- **Stripping** (the "cosmic shoreline"): with `I = S · X` and `I_s = 0.6 (v_esc / 5 km/s)⁴`, a fraction
  `exp(−14 (I / I_s)²)` of every gas is kept: Earth and Venus keep theirs, Mars 0.04%, Mercury and the Moon
  nothing. Under 1% kept, the body counts as **airless**.
- **Escape**: a gas also needs an escape speed six times its thermal speed at the exosphere,
  `T_exo = T₀ + 750 K · √(S X)` (about 1,050 K at Earth, 170 K at Titan), where `T₀ = 278.6 K · S^¼`.
- **Runaway greenhouse**: when `0.7 · S · 340 W/m² > 282 W/m²` (more light than a wet atmosphere can radiate,
  S > 1.19) all water is vapour, and after 0.3 Gyr only `max(10⁻⁵, exp(−60 I / I_s))` of it is left (Venus:
  10⁻⁵; a heavy super-Earth keeps a steam atmosphere).
- An airless body loses its surface water if `T₀ > 200 K`, or if it is rocky and heated over 0.3 W/m² (Io).
  A water-rich body (ice ≥ 5%) holds 95% of its carbon dioxide in its deep ocean.

**5. Climate** (`Evaluate`, iterated until the temperature moves less than 0.2 K, at most 12 passes, starting
warm as young planets do, so a world that can keep liquid water does):
- **Cold traps**: carbon dioxide, nitrogen, methane and ammonia condense where their partial pressure exceeds
  the saturation pressure `exp(L/R · (1/T_b − 1/T_c))` at the coldest surface temperature
  `T_c = T · max(1 − max(swing, 0.15), 0.25)` (boiling or sublimation points at 1 atm `T_b` = 194.7, 77.35,
  111.7, 239.8 K; `L/R` = 3,030, 830, 986, 2,800 K).
- **Water vapour**: 60% of saturation, `0.00604 atm · exp(L/R (1/273.16 − 1/T))` (`L/R` 6,140 K over ice, 5,420 K
  over water), at most all the water.
- **Liquids and ice**: magma above 1,300 K (cover `clamp((T − 1300)/700, 0.2, 1)`). Water if some remains on the
  surface, above 0.006 atm, between 273.15 K and the boiling point `1 / (1/373.15 − ln P / 4884)`: ocean cover
  `1 − exp(−w / 1.9·10⁻⁴)` for surface water `w` (a fraction of the mass: Earth's 2.3·10⁻⁴ gives 71%; global from
  0.5%), polar and sea ice `clamp((300 − T)/120, 0, 0.9)`. With ammonia, over 1% water and 176–273 K: an
  ammonia–water sea (cover `clamp(w / 0.05, 0.3, 1)`). Frozen water covers an icy body completely, a rocky one
  `1 − exp(−w / 2·10⁻⁵)`. Methane, over 0.5 atm and 85–115 K: hydrocarbon lakes (`clamp(1000 f_CH₄, 0.01, 0.2)`).
  Nitrogen or carbon dioxide frosts make at least 20% ice.
- **Albedo**: ground (oceans 0.06, lava 0.05, land 0.15, metal-rich land 0.10) under ice (0.55); above 0.01 atm
  pulled towards clouds by `1 − exp(−P / 20 atm)`: 0.75 for a hot (over 400 K) carbon dioxide or sulphur dioxide
  atmosphere (sulphuric acid clouds, Venus), 0.4 otherwise; at least 0.28 over liquid water (water clouds), 0.25
  under methane haze below 150 K.
- **Greenhouse** (grey optical depth from partial pressures in atm; each doubling of carbon dioxide warms Earth
  about 3 K, twice Earth's nitrogen about 5 K):
  `τ = 0.08 ln(1 + p_CO₂/3·10⁻⁴) + B(1.75, p_CO₂, 300) + 0.2 ln(1 + p_H₂O/4·10⁻⁴) + B(2, p_H₂O, 300) + B(2.5, p_CH₄, 50)
  + B(3, p_NH₃, 50) + B(5, p_SO₂, 50) + 0.07 P² / (1 + P/2) / (1 + P/200)`, with saturating bands
  `B(k, p, s) = k p / (1 + p/s)`.
- **Temperature**: `T = min((T_eq⁴ + q/σ)^¼ · (1 + 0.75 τ)^¼, 3,000 K)`, `T_eq = 278.6 K · ((1 − A) S)^¼`.
- **Carbonate–silicate cycle**: on a body with water, exposed rock (ice under 5%), air, no runaway and a warm
  climate with all its carbon dioxide, weathering locks carbon dioxide away until the surface is near 288 K: the
  air keeps the least amount between 3·10⁻⁴ atm and the whole inventory that reaches 288 K (bisection, 6 steps).
  Outer-zone worlds keep bars of it; inner ones a trace.

**6. Life.** A world is habitable with liquid water, 260–330 K, over 0.05 atm, a stable host and 0.8 Gyr of age.
Life starts there with probability 0.3 (microbes); after 2 Gyr it makes oxygen with probability 0.3 (simple
life: oxygen of 12–30% against the nitrogen and argon, methane falls to 1%); after 3.5 Gyr it grows complex with
probability 0.45 (plants and animals). Nobody knows these odds; these make life rare but findable (below).

**7. Crust.**
- **Tectonics**: molten above 1,300 K; dead under 0.01 W/m² (0.003 for icy bodies with over 10% ice); plates for a
  rocky body of 0.3–6 Earth masses with liquid water and over 0.03 W/m²; otherwise a stagnant lid.
- **Volcanism**: activity `clamp(log₁₀(q / q₀) / 2, 0, 1)` with `q₀` 0.04 W/m² for rock (Earth 0.18, Io 0.9) and
  0.01 for ice (Enceladus just over it). Rocky bodies: sulphur volcanoes on a moon heated mostly by tides,
  otherwise lava. Icy bodies: nitrogen geysers below 60 K on bodies formed beyond five times the snow line,
  ammonia–water cryovolcanoes with ammonia, water geysers otherwise. Rocky volcanoes on an airless body leave a
  trace of sulphur dioxide (`10⁻⁹ × activity` atm).
- **Magnetic field** (× Earth's): `metal / 0.32 · √M · spin · min(1, q_int / 0.06)` for bodies over 5% metal,
  0.02 Earth masses and 0.04 W/m² of internal heat; `spin` 1 for days under 30 hours, `√(30 h / day)` beyond.
- **Subsurface ocean**: ice ≥ 5%, colder than 273 K, over 200 km radius, `q > 0.004 W/m² · (1,000 km / R)`, and
  no liquid on the surface.
- **Day and night**: `swing = 0.6 · s / (1 + 5 P^0.7)`, `s` 1 when tidally locked, otherwise
  `clamp(log₁₀(day[h] / 10) / 2, 0.15, 1)`; day `T (1 + 0.55 swing)`, night `T · max(1 − swing, 0.25)`.
- **Cratering**: `clamp(min(1, age / 3 Gyr) · (1 − renewal) / (1 + 10 P), 0, 1)`, renewal the largest of the
  volcanic activity, 0.9 for plates, the ocean cover and 0.5 for a subsurface ocean (1 when molten).

**8. Labels**, derived from the state:
- **Type** (the first that applies): metal-rich world (metal ≥ 45%); lava world (magma on the surface);
  Earth-like world (simple or complex life, oxygen ≥ 10%, liquid water); water world (water covers ≥ 95%);
  ammonia world (ammonia–water seas ≥ 50%); icy world (ice ≥ 30%); rocky ice world (ice ≥ 5% or ice cover ≥ 90%);
  rocky world. Giants are gas giants or ice giants by how they formed; gas dwarfs as above.
- **Atmosphere** (`AtmosphereType`, by mole fraction): none (under 10⁻⁹ atm; rock vapour above 1,500 K);
  hydrogen–helium (over half H₂ + He); oxygen–nitrogen (O₂ ≥ 10%, N₂ > 30%); steam, carbon dioxide, nitrogen
  (nitrogen–methane with over 1% methane), methane, ammonia or sulphur dioxide when that gas is over half;
  otherwise the largest gas. Its words add the pressure: trace under 0.001 atm, thin under 0.5, thick from 2,
  crushing from 30.
- **Surface** (`SurfaceKind`): none (giants, gas dwarfs); ocean (water or ammonia–water covers ≥ 95%); ice (ice
  cover ≥ 50%, or ice ≥ 30% below 250 K); rock.
- **Hazards** (`Hazards`, flags; one rule set, `WorldBuilder.HazardsFor`, also for Sol):

| Hazard | When |
|---|---|
| Vacuum | under 0.001 atm |
| Extreme heat | day above 400 K |
| Extreme cold | night below 150 K |
| High pressure | over 10 atm |
| Toxic atmosphere | over 0.01 atm and not breathable (oxygen 0.16–0.5 atm, CO₂ under 1%, SO₂ under 0.01%) |
| Corrosive | over 0.01 atm with SO₂ over 1%, or over half CO₂ above 10 atm and 500 K (acid clouds) |
| High gravity | over 1.6 g |
| Volcanic | volcanic activity over 0.5 |
| Radiation | inside 10 radii of a gas giant over 150 Earth masses (its belts), or no magnetic field (under 0.1), under 0.1 atm and `S · X > 2` |
| Molten | a molten surface |
| Storms | over 5 atm, or over 0.1 atm with day and night 150 K apart |
| Quakes | tidal heating over 0.1 W/m², or moving plates |

**Giants** keep a simple state: envelope 80–94% of the mass (gas giants) or 8–20% (ice giants), ice 30–60%
(75–85%) of the rest, rock 70% of what remains, metal the rest. Radius: gas giants
`11.2 R⊕ · (M / 318)^−0.04` from 95 Earth masses, `9.45 R⊕ · (M / 95)^0.5` below, up to 20% larger when irradiated
above 1,000 K; ice giants `3.9 R⊕ · (M / 17)^0.3`. Albedo 0.08 above 1,000 K, 0.2 above 350 K, 0.32 below. Their
own heat `5.4 W/m² · (M / 318)^0.5 · (age / 4.6 Gyr)^−1.2` (Jupiter's) adds to the starlight. Gases: 86% hydrogen,
13.5% helium, with methane (0.3%, ice giants 2%) and ammonia below 200 K, water vapour above. Magnetic field
`10 · (M / 318)^0.8` × Earth's (ice giants 0.5). No surface.

**Fields** (`PlanetState`, ranges as generated):

| Field | Unit | Range | Meaning |
|---|---|---|---|
| `FormationZone` | – | 0.01–100 | formation distance over the young star's snow line |
| `Composition` | mass fractions | each 0–1, sum 1 | metal, rock, ice (all volatiles), gas (H₂ + He) |
| `CoreRadius` | fraction of radius | 0–0.85 | the metal core |
| `HeatFlow` | W/m² | 0–10 (Earth 0.09, Io 2.5) | internal plus tidal heat through the surface |
| `TidalHeating` | W/m² | 0–5 | the tidal part |
| `Tectonics` | enum | none, stagnant lid, plates, molten | how the crust moves |
| `Volcanism` | enum | none, silicate, sulphur, water, ammonia–water, nitrogen | what erupts |
| `VolcanicActivity` | – | 0–1 (Earth 0.18, Io 0.9) | how active |
| `MagneticField` | × Earth's | 0–80 (Jupiter 10 by construction) | intrinsic field |
| `Cratering` | – | 0–1 (the Moon 1) | how old and battered the surface is |
| `Albedo` | – | 0.05–0.75 | Bond albedo |
| `EquilibriumTemperature` | K | 3–2,500 | with this albedo, without greenhouse or internal heat |
| `MinTemperature`, `MaxTemperature` | K | 3–3,000 | typical night (or polar) and day temperatures |
| `Gases` | mole fractions | each 0–1, sum 1 (all 0 without air) | H₂, He, N₂, O₂, CO₂, CH₄, H₂O, NH₃, SO₂, Ar |
| `Greenhouse` | optical depth | 0–1,000 (Earth 0.8, Venus 130) | the greenhouse τ |
| `Liquid` | enum | none, water, ammonia–water, hydrocarbons, magma | the surface liquid |
| `OceanCover`, `IceCover` | fraction of surface | 0–1 | liquid and ice cover |
| `SubsurfaceOcean` | bool | | liquid water under an ice shell |
| `Biosphere` | enum | none, microbial, simple, complex | life |
| `Surface` | enum | none, rock, ice, ocean | what you would stand on |
| `Hazards` | flags | | dangers (table above) |

With it on `Body`: the type, atmosphere label, surface pressure (atm; giants nominally 1,000) and surface
temperature (K) derived from the state; `RingMaterial` and `RingMass` (kg); a belt's `BeltMaterial` and total
`Mass` (kg). `Landable` is a surface of rock or ice.

**What it makes** (`Hollow.GeneratorReport stats`; frequencies in a volume-complete sample of 3,551 systems
within 70 ly of a point 570 ly from Sol, so mostly red dwarfs as in the real Galaxy): liquid water on the surface
in 1 system in 9 (G dwarfs 1 in 4, M dwarfs 1 in 10, brown dwarfs 1 in 75), life in 1 in 30, oxygen in 1 in
140, plants and animals in about 1 in 600. In a 43,000-system sample: 19% of moons are volcanically active, nearly
all by tides; 72% of solid planets have some volcanism, most of it as gentle as Earth's; 1 system in 290 has an
Earth-like world; 18% of bodies have a subsurface ocean.

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
- **Named regions** (§15.2): a ring of the region's size, coloured by its kind (emission nebulae red, reflection
  nebulae blue, dark nebulae dusky, planetary nebulae teal, supernova remnants amber, clusters pale), and its name,
  from outside the region out to 60 times its radius (at least 2,500 ly), placed by the Labeller after the
  stars' names. The focus panel names the region the focus is in; a selected star's card gives its region and
  sector. Search finds regions by name or designation (M42, NGC 7293).
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
- Selecting a body shows its data: type, orbit, mass, radius, gravity, temperature (day and night when they
  differ by more than 20 K), atmosphere with its pressure, day length, axial tilt, and its physical state (§10.3):
  surface with its liquids and ice, composition, geology, heat flow, magnetic field, a subsurface ocean, life,
  hazards, ring material and landable; for belts their material and mass; for stars and barycentres, the
  system's age (and a barycentre's members).
- Planets are drawn from their state: oceans, lava seas (glowing), hydrocarbon lakes and ammonia seas fill the
  low ground up to their cover, ice caps cover their share from the poles, and the atmosphere's rim grows with
  pressure and takes the colour of its gases; a deep cloudy atmosphere (over 5 atm of carbon dioxide, sulphur
  dioxide, steam or nitrogen–methane) hides the ground.
- Barycentres are empty points in the orrery (labelled only when selected) and rows in the body list. Around the
  root, distances use the system's (compressed) scale; around a companion or an inner pair, 0.35 of it, so that
  host's planets stay clustered around it.

---

## 13. Tests

| Test | Checks |
|---|---|
| Determinism | Generating the same cells twice, in any order and on any thread, gives identical stars. |
| Golden | Hashes match their stored values for these generator versions: 40 cells (5 places × 8 levels); the first 8 procedural systems of 12 of those cells (levels 4–7); and the real systems Sol, Alpha Centauri, Luhman 16, Epsilon Indi, Barnard's Star, Sirius, Procyon, Tau Ceti, Epsilon Eridani, 61 Cygni, Vega, Capella, Castor, Mizar, Albireo, Polaris, Betelgeuse and Rigel. System hashes cover every stored field of every body (`Golden.Of` walks the fields by reflection, so a new field is pinned without touching the test). |
| Versions | Another galaxy-stage version changes the stars, another system-stage version changes the systems, and the stages have different seeds. |
| `DMath` | Results within 4 ulp of `Math` over wide ranges (measured: 3), and bit-identical on every platform. |
| Frames | The Galactic Centre's equatorial coordinates convert to (0, 0, 1); the north galactic pole to (0, 1, 0). |
| Catalogue | Sirius is 8.6 ly away, Proxima Centauri 4.24 ly (a companion of Alpha Centauri), Vega 25 ly; Sol has address and name "Sol"; Toliman belongs to Alpha Centauri. |
| Nearby dwarfs and companions | Luhman 16 is a pair of brown dwarfs at 6.5 ly and WISE 0855−0714 lies at 7.4 ly (the supplement); Proxima's name leads to Alpha Centauri, and in that system it orbits A and B thousands of AU out; Epsilon Indi Ba and Bb orbit each other; 61 Cygni A and B are 50–200 AU apart (projected), not their 2,375 AU parallax difference; catalogue stars listed alone gain no companions within 25 ly, and beyond it only faint or unresolved ones, which occur. |
| Catalogue independence | Procedural stars across the edge of the catalogue's complete core are identical with and without the catalogue loaded, and none lies inside the core. |
| Suppression | Where the catalogue holds about half of a level, every star that survives is identical (address, position, class, physics) to the one an unsuppressed generator makes. |
| Completeness | Real plus generated stars match the density model within ±15% in eight shells at levels 2, 4, 6 and 7 where the catalogue holds part of the level. |
| Sampler | In 36 level-0 cells around the Galactic Centre, the share of young-disk stars within 165 ly of the midplane matches the density's `sech²` profile within 4σ (and differs from a uniform spread by more than 6σ). |
| Untrusted addresses | Catalogue addresses with another catalogue number, an out-of-range ID (including one that would truncate to a real ID) or a companion's ID are not found, without errors. |
| Density | Local density 0.00233 per ly³; 8–14 systems within 10 ly and 900–1,900 within 50 ly (measured: 9 and about 1,430); total 1.1–1.7 × 10¹¹ (measured 1.22 × 10¹¹). |
| Star counts | `StarCountProfile` agrees with a Monte Carlo integral of the density within 25%, near Sol, in the disk and at the core. |
| Addresses and names | Address ↔ fields round-trip; name ↔ address round-trip; every sector name parses uniquely. |
| Systems | Sol matches its hand data, including its bodies' physical states (Earth's complex life, Io's sulphur volcanoes, Europa's buried ocean, Titan's lakes, Venus's acid); moons stay inside their planet's Hill sphere. |
| Physical state | In thousands of generated systems every planet and moon has a state; compositions and gases add up; only giants and gas dwarfs lack a surface; landable means rock or ice; the mean temperature lies between night and day; vacuum means under 0.001 atm; Earth-like means oxygen from life over liquid water; liquid water, lava, hydrocarbon lakes, subsurface oceans and life all occur. |
| Solar System calibration | The world builder given the inputs of Earth, Venus, Mars, the Moon and Titan, with 20 random inventories each, makes temperate Earths with liquid water, dry hothouse Venuses, cold thin-aired Marses, airless Moons and cold Titans that keep their air (all 20; Titan at least 14). |
| Orbits | In thousands of systems: neighbouring planets and belts of every host never overlap; both members of every pair balance at its barycentre and keep the radius floor or the hierarchy; circumbinary planets stay beyond 1.5 times the pair's separation, planets of one member inside half the pair's closest approach. |
| Orbit tree | Paths are unique, decode to their host, slot and moon, and find their body; the stars' centre of mass stays at the origin at any time; triples, planets around companions, circumbinary planets, planets warmed by a companion and moons with atmospheres all occur. |
| Authored layer | Every authored system's named stars carry their names at the right paths and every authored host has exactly its known planets, at their measured orbits and masses, with a physical state; TRAPPIST-1 e's year comes out at 6.1 days from its star's mass; Proxima b orbits the red dwarf Proxima Centauri; 51 Pegasi b is a hot Jupiter. Every region name is unique, never parses as a sector name, lies in the grid, and its designation finds it; procedural systems inside the Pleiades carry its name and their names find them. |
| Architecture | Nothing in `src/` references Godot; generation code uses no platform maths, unseeded randomness or clocks. |

---

## 14. Versions

Each stage of the pipeline (ARCHITECTURE.md §9) has its own generator version, `GeneratorVersion` in
`Hollow.Galaxy`:

| Stage | Constant | Covers | Value |
|---|---|---|---|
| Galaxy | `GeneratorVersion.Galaxy` | density model, cells, primary stars and their physics | 1 |
| System | `GeneratorVersion.System` | companion stars, planets, moons, rings, belts | 1 |
| Surface | `GeneratorVersion.Surface` | terrain, biomes, resources (M4; reserved, nothing reads it yet) | 1 |
| Authored | `AuthoredLayer.Version` | real planets, star names and named regions (§15) | 1 |

A stage's seed is `Hash64(universe seed, stage tag, version)` (tags `GALV`, `SYSV`, `SURV`), and every stream of
the stage starts from it. So the output of each stage is a function of (universe seed, address, version), as
ARCHITECTURE.md P1 says, and the stages are independent: a new system version rebuilds every system but moves no
star. While the generator is in development the versions stay at 1 and changes update the golden tests instead
(AGENTS.md). Once a shard goes live, its versions are frozen and recorded with it; a change to a stage then ships
as a new version of that stage, for new shards or after a wipe.

The authored layer has a version too, recorded with a shard the same way: it is content rather than a random
stage, so it seeds nothing, but a shard must keep the planets, names and regions it was opened with.

---

## 15. Authored layer

Hand-written content from public facts, **compiled into the engine** (`Hollow.Galaxy.Authored`) so it can never be
a missing or different data file, and **versioned** (`AuthoredLayer.Version`, §14). It is applied on top of
generation and is keyed so nothing else moves: real systems by their **catalogue ID** (the same in HYG and in
AT-HYG, §8.5) and their bodies by **body path** (§10.1); regions by position. An address is untrusted: anything
that isn't a known catalogue ID finds nothing. Sol is hand-written on its own (`SolSystem`, §10).

### 15.1 Real systems

An authored system can name its stars and barycentres by node number, and give a host (by node) its **known
planets**. An authored host has exactly those planets, in slots 0, 1, 2, … (paths `Planet(node, slot)`), named
`<prefix> <letter>`; nothing is invented around them (no extra planets or belts, and no rings, since none have
been seen). What was measured is used as it is: mass (the minimum mass where only that is known, or an estimate
from the radius, as noted in the data), semi-major axis and eccentricity. The period follows from the masses;
everything else is generated as for any planet, from the planet's own slot stream: spin and tidal lock, axial tilt,
moons (the usual rules, which leave close-in planets without any) and the whole physical state (§10.3), from the
real star's light and age.

| System | Host | Planets | Source |
|---|---|---|---|
| Alpha Centauri | node 3, named Proxima Centauri | Proxima Centauri d (0.26 M⊕ min., 0.0288 AU), b (1.06 M⊕ min., 0.0485 AU, habitable zone) | Anglada-Escudé et al. 2016; Faria et al. 2022 |
| Epsilon Indi | nodes 2, 3 and 9 named Ba, Bb and B | (brown dwarfs, §8.4) | Dieterich et al. 2018 |
| Gliese 229 | nodes 2, 3 and 9 named Ba, Bb and B | (brown dwarfs, §8.4) | Xuan et al. 2024; Whitebook et al. 2024 |
| Epsilon Eridani (Ran) | 1 | b, 1.0 Jupiter masses at 3.53 AU, e = 0.06 | Llop-Sayson et al. 2021 |
| 51 Pegasi (Helvetios) | 1 | b (Dimidium), 0.61 Jupiter masses at 0.0523 AU: the first planet found around a Sun-like star | Mayor & Queloz 1995 |
| Teegarden's Star | 1 | b, c, d (1.16, 1.05, 0.82 M⊕ min.; 0.026–0.079 AU) | Zechmeister et al. 2019; Dreizler et al. 2024 |
| TRAPPIST-1 | 1 | b–h, seven Earth-sized planets, 0.012–0.062 AU, masses from transit timing | Agol et al. 2021 |
| Kepler-186 | 1 | b–f, f in the habitable zone at 0.432 AU (masses estimated from radii) | Quintana et al. 2014 |

The star names are tied to node numbers, which follow the orbit tree (§10.1). A test checks that each lands on the
intended star (Proxima Centauri must be the red dwarf hosting Proxima b), so a generator change that renumbers
nodes is caught.

### 15.2 Regions

Real clusters and nebulae as objects of the galaxy (`Region`): a name, a designation, a kind, a centre (from
J2000 coordinates and distance) and a radius (from the apparent size at that distance, rounded up to take in the
halo). They name the procedural systems inside them (§9), appear on the galaxy map (§11) and in search. 39
regions:

| Kind | Regions (designation, distance in ly, radius in ly) |
|---|---|
| Open clusters | Hyades (Melotte 25, 153, 15), Pleiades (M45, 444, 15), Beehive Cluster (M44, 577, 12), Coma Star Cluster (Melotte 111, 280, 10), Alpha Persei Cluster (Melotte 20, 570, 15), Southern Pleiades (IC 2602, 479, 8), Omicron Velorum Cluster (IC 2391, 500, 6), Ptolemy's Cluster (M7, 980, 12), Butterfly Cluster (M6, 1,600, 6), Shoe-Buckle Cluster (M35, 2,800, 12), Golden Eye Cluster (M67, 2,770, 12), Wild Duck Cluster (M11, 6,200, 20), Double Cluster (NGC 869 and 884, 7,500, 40) |
| Globular clusters | Hercules Cluster (M13, 22,200, 75), Sagittarius Cluster (M22, 10,600, 50), Spider Cluster (M4, 7,200, 35), Omega Centauri (NGC 5139, 17,090, 75), 47 Tucanae (NGC 104, 14,700, 60) |
| Emission nebulae | Orion Nebula (M42, 1,344, 12), Lagoon Nebula (M8, 4,100, 40), Eagle Nebula (M16, 5,700, 35), Omega Nebula (M17, 5,500, 9), Trifid Nebula (M20, 5,200, 21), Carina Nebula (NGC 3372, 8,500, 115), Rosette Nebula (NGC 2237, 5,200, 65), North America Nebula (NGC 7000, 2,590, 45), California Nebula (NGC 1499, 1,000, 25), Heart Nebula (IC 1805, 7,500, 80) |
| Reflection and dark nebulae | M78 Nebula (M78, 1,600, 2), Horsehead Nebula (Barnard 33, 1,375, 2), Coalsack (Caldwell 99, 600, 30), Rho Ophiuchi Cloud (460, 18) |
| Planetary nebulae | Dumbbell Nebula (M27, 1,360, 1.5), Ring Nebula (M57, 2,570, 1.3), Owl Nebula (M97, 2,030, 1), Helix Nebula (NGC 7293, 655, 2), Cat's Eye Nebula (NGC 6543, 3,300, 0.5) |
| Supernova remnants | Crab Nebula (M1, 6,500, 5.5), Veil Nebula (Cygnus Loop, 2,400, 55) |

Regions are places, not yet populations: a cluster holds the stars the density model puts there plus its real
catalogue members (the Pleiades' and Hyades' bright stars are in HYG), not an overdensity of generated members,
and a nebula has no gas of its own in the generator. Both are follow-ups.

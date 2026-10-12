# Generator v2: progress notes

Branch `generator-v2`. Implements recommendations 1–9 of the report "Elite galaxy generation vs Hollow"
(`reports/Elite galaxy generation vs Hollow.md` in the main checkout), in the order 1, 3, 4, 2, 5, 7, 6, 8, 9.
Each item is one commit (or a few) that builds and passes the tests. Elite is a reference for how things work;
no Elite tables, names, thresholds or data are used.

Measurements come from `tools/Hollow.GeneratorReport` (Release build, single thread, this PC):

```bash
dotnet run -c Release --project tools/Hollow.GeneratorReport -- bench   # µs per generated system
dotnet run -c Release --project tools/Hollow.GeneratorReport -- stats   # what the systems contain
```

`bench` times `SystemGenerator.Generate` over a fixed sample of 36,110 procedural systems (near Sol and in the
inner disk) and `Galaxy.GetSystem` over the 1,371 real systems within 60 ly. `stats` describes 42,018 procedural
systems.

| | Before (5581c88) | After item 7 | After item 9 |
|---|---|---|---|
| Procedural system | 5.9 µs (8.4 bodies, 0.71 µs per body) | 9.9 µs (10.4 bodies, 0.95 µs per body) | 20.4 µs (10.3 bodies, 1.98 µs per body) |
| Real system (GetSystem) | 4.4 µs | 5.3 µs | 15.5 µs |

Item 7 (multiple stars, planets around companions and pairs) added bodies; item 6 (the physical state, a climate
solve per body with air) doubled the cost per body. 20 µs per system is about 50,000 systems a second on one thread.

---

## 1. Hot worlds reachable

**Changed.** The planet zone's inner edge moved from `max(0.02 AU, 0.1 AU·√L, 3 R★)` to
`max(0.01 AU, 0.03 AU·√L, 3 R★)`: equilibrium temperatures now reach about 1,600 K (before: at most 881 K, so the
generator's metal-rich and hot-giant branches were dead code). A first try that let planets fill the new inner
slots as often as any other pushed the median planet from 262 K to 353 K, far hotter than real planet statistics.
So slot occupancy is now a weighted random subset: slots beyond `0.1 AU·√L` (about ten-day orbits around the Sun)
weigh 1, closer slots `(a / 0.1 AU·√L)³`, following the steep fall of real occurrence rates at short periods.

**Golden.** Systems golden `0xD88E791D58901CB4` → `0x313E81E347F46464` (every system's slots changed). Cells
golden unchanged.

**Measured** (`stats`, 42,018 systems):

| | Before | After |
|---|---|---|
| Median planet temperature | 262 K | 287 K |
| 99th percentile | 1,001 K | 1,451 K |
| Hottest planet | 1,321 K (rocky world, thick atmosphere) | 2,404 K (metal-rich world) |
| Metal-rich worlds | 0 | 2,914 (2.5% of planets) |
| Gas giants above 1,000 K | 0 | 370 (0.9% of systems; real: 0.5–1% of Sun-like stars) |
| Speed | 5.9 µs per system | 5.4 µs (slightly fewer moons) |

The test `GeneratedSystemsArePlausible` now also requires metal-rich worlds and hot giants in its sample.

**Decisions.** The absolute floor went from 0.02 to 0.01 AU so compact M-dwarf systems (like TRAPPIST-1, whose
innermost planet orbits at 0.0115 AU) can exist. Neutron stars and black holes keep their 0.1 AU inner edge.
The classification and greenhouse rules are unchanged here; item 6 replaces them with physical state.

---

## 3. Per-stage versions, wider goldens, GALAXY.md corrections

**Changed.**
- `GeneratorVersion` (new, `Hollow.Galaxy`): `Galaxy = 1`, `System = 1`, `Surface = 1` (reserved for M4). Each
  stage's seed is `Hash64(universe seed, stage tag, version)` (`GALV`, `SYSV`, `SURV`); `CellGenerator` and
  `SystemGenerator` derive their seed from it. The old `Galaxy.GeneratorVersion = 1`, which nothing read, is gone.
- Goldens: `Golden.Of` (tests) hashes every stored field of an object by reflection, recursively and in name
  order (auto-property backing fields included, computed properties skipped), so fields added in items 5–7 are
  pinned automatically. The procedural systems golden uses it; a new golden covers Sol and 16 real systems
  (Alpha Centauri, Proxima Centauri, Barnard's Star, Sirius, Procyon, Tau Ceti, Epsilon Eridani, 61 Cygni, Vega,
  Capella, Castor, Mizar, Albireo, Polaris, Betelgeuse, Rigel). A new test checks that another galaxy or system
  version changes its stage.
- GALAXY.md: sector and system name examples that the tables can't produce ("Vasken Orrimar", cell code "QX-AB"
  at level 6) replaced with real ones printed by `tools/Hollow.GeneratorReport names`; body designations
  corrected (only the primary has planets, so "B 3 a" can't exist yet; the one belt is `<system> Belt`, not
  "Belt A"); multiplicity by primary mass (the code's thresholds, plus 20% for neutron stars, black holes and
  Wolf–Rayet stars); moons and rings per type as the code does them; the catalogue's real size; a new §14 on
  versions. ARCHITECTURE.md §8.4 names the per-stage versions.

**Golden.** Everything changes because every seed changed: cells `0x9EB9D62229A1F6AC` → `0x595FEAC36CFD361D`;
procedural systems `0x313E81E347F46464` → `0xDEEBD959A57739FB` (now over every field); real systems (new)
`0xD0DADDB204F53B3B`.

**Measured.** No change in speed (the bench varies ±20% between runs on this PC; best of several: 6.4 µs per
procedural system, 4.4 µs per real one).

**Decisions.** Versions stay at 1 during development (ARCHITECTURE.md §8.4: goldens change instead). The
universe seed string "Hollow Universe galaxy, version 1" is kept: it names the main universe and is separate
from the stage versions. Golden hashes skip computed properties so a computed NaN can never make the hash differ
between x64 and ARM64.

---

## 4. A sampler that can't leave the density; galaxy-wide maps

**Maps first.** `tools/Hollow.GeneratorReport maps` renders every level over the whole sector grid (levels 0–2
in full, 3–7 one cell column per 256-ly pixel; about 4 minutes on 12 threads): `docs/images/generator/face-on.png`
(surface density and class mix), `ratio.png` (generated ÷ a fine integral of the density, per 2,048-ly block) and
`edge-on.png` (levels 0–3 through Sol and the Galactic Centre). Looked at before and after the change:

- No density steps between cells or sectors at any level: χ² per block of generated counts against the fine
  integral is 1.00–1.05 everywhere (1 = Poisson noise). (A first measurement gave 1.9–2.7 at levels 3–4; that was
  the map's own sampling comparing one cell column against the whole pixel, fixed by integrating the same
  footprint.)
- One real pattern: at level 0, paired red/blue arcs where the spiral arms fade out (R ≈ 50,000 ly). Counting
  showed the 16-try fallback hit 3,030 of 1.38 million level-0 stars (0.2%) and 76,173 of 78 million at level 1,
  bunched in cells where the arm is a narrow peak over a faint background (acceptance ~13%, so ~10% of those
  stars fell back to a uniform spot in a 1,024-ly cell, off the arm).
- Expected features, not artefacts: the catalogue's holes around Sol (item 2), the hole in the young disk inside
  the bar, the old bar in red giants at level 1.

**Changed.** The density samples of the cell's integration grid (4 × 4 × 4, or 4 × 8 × 4 at levels 0–2) are kept;
a star's position now picks a sub-cell by its component's sampled density (binary search over running sums), then
rejection-samples inside the sub-cell with a bound of 1.3 × the largest sample of the sub-cell and its 26
neighbours, at most 24 tries. A candidate is always inside a sub-cell where the component has stars, so even a
star that runs out of tries stays within the density. Measured: fallbacks 0 at level 0 (was 3,030), 57 at level 1
(was 76,173); the arcs are gone. New test `SamplerFollowsTheDensity`: young-disk stars in 36 level-0 cells follow
the `sech²` profile (5,498 within 165 ly of the midplane; the density says 5,498, a uniform spread 4,696).

**Golden.** Cells `0x595FEAC36CFD361D` → `0x22954A90D19DDA8E` (every star's random draws changed); procedural
systems `0xDEEBD959A57739FB` → `0x5F09B25668332B31` (their primaries changed). Real systems unchanged.

**Measured.** Cell generation cost unchanged within noise (the maps took 218 s before and 239 s after, dominated by
level 2's 3 × 10⁸ stars).

---

## 2. Catalogue completeness instead of hard radii

**Measured first** (`tools/Hollow.GeneratorReport shells`: per level, catalogue primaries and actually generated
procedural stars in 26 shells from 0 to 4,000 ly, against the density model's expected count). Two problems, not
one:

1. **Doubled beyond the radii**, as the report said: (real + generated) ÷ model peaked at 1.27 (level 2,
   800–1,000 ly), 2.20 (level 3, 400–500 ly), 1.52 (level 4), 1.60 (level 5), 1.64 (level 6), 1.60 (level 7),
   settling to 1.0 only where the catalogue thins out.
2. **The model itself was wrong where the catalogue is complete.** Inside the radii, levels 0–1 had only 4–36% of
   the model's stars, though the catalogue is certainly complete there (every star brighter than V ≈ 7.5).
   `calibrate` compared the class table entry by entry against the real sky inside those spheres: 58× too many B0
   dwarfs, 9× too many supergiants, 7× too many B1–B6 dwarfs, 5× too many bright red giants, 260× too many carbon
   stars, about 2× too many giants and subgiants, 1.5× too many G dwarfs, and no evolved stars at level 3 (the
   real sky has 295 within 200 ly). A completeness factor can't hide a wrong model: it would only move the step.

**Changed.**
- Class table recalibrated (GALAXY.md §4): luminous young and evolved entries scaled to the real counts, a new
  "Bright subgiant" entry at level 3, T Tauri stars cut to a pre-main-sequence share. Model ÷ real inside the
  complete spheres is now 1.15 / 1.04 / 1.05 / 1.02 / 1.03 / 1.07 / 1.16 / 0.90 for levels 0–7. Level-0 stars near
  Sol: about 30 per 1,024-ly cell instead of 300; the galaxy's luminous stars are correspondingly rarer.
- `CatalogueCompleteness` (new, frozen in code): `C(level, d)`, the share of a level's stars at distance d that the
  catalogue holds. Form: a complete core ending near d₀ plus a bulk share p falling near d₅₀ (fitted per level by
  least squares on real ÷ model; a single logistic left 25–55% gaps at levels 6–7 where the Gliese catalogue has a
  complete core to ~20 ly and then a plateau), clamped to exactly 1 above 0.99 and 0 below 0.01 or beyond the
  import limit. Procedural stars are left out with probability C (same separate roll stream as before, so
  survivors are unchanged); the old radii and ramp are gone.
- Catalogue builder: keeps exactly the systems with C > 0 (both from the CSV and with the new `--update in out`,
  which re-applies the rules to a built file; the HYG CSV is not in the repository or on this PC).
  `hyg41.bin.gz` rebuilt with `--update`: 98,676 systems (was 100,761); dropped are far-tail stars where the
  catalogue holds under 1% of their level.

**After** (`shells`): every shell with more than a dozen expected stars is within 0.86–1.07 × model at every level
(the few outliers are small-number shells: level 0 at 650–800 ly, 12 stars against 16.5; level 7 at 20–25 ly).
Within 4,000 ly: real + generated − model = 0.0%. New test `RealAndGeneratedStarsAddUpToTheModel` checks eight
partial shells at levels 2, 4, 6, 7 (measured 0.99–1.11). Within 10 ly: 9 systems (all real); within 50 ly 1,200
(850 real; was 1,432).

| Level | Catalogue only within | Procedural only beyond | Worst shell before | Worst shell after |
|---|---|---|---|---|
| 0 | 444 ly | 3,000 ly | 0.04 | 0.89 (0.73 in a 12-star shell) |
| 1 | 423 | 3,000 | 0.07 | 0.96 |
| 2 | 313 | 1,500 | 1.27 | 0.97–1.02 |
| 3 | 256 | 1,314 | 2.20 | 0.94–1.02 |
| 4 | 178 | 820 | 1.52 | 0.97–1.01 |
| 5 | 106 | 516 | 1.60 | 0.93–1.01 |
| 6 | 11.7 | 285 | 1.64 (and 0.41 at 25–30 ly) | 0.93–1.20 |
| 7 | 20.8 | 113 | 1.60 | 0.86–1.03 (0.72 at 20–25 ly) |

**Golden.** Cells `0x22954A90D19DDA8E` → `0x5432B95D1D9D85D5` (class table and suppression); procedural systems
`0x5F09B25668332B31` → `0xDB5A9536406D01B2`. Real systems unchanged (their stars are still in the file).

**Decisions.**
- Calibrating the class table is part of this item: without it, no completeness factor can avoid a step where the
  catalogue ends. It changes the galaxy at large (8× fewer O/B0 stars and supergiants, 5–7× fewer B1–B6 stars and
  bright giants), which is what the real neighbourhood says; if the far views look too sparse, that is a visual
  question for the map, not the generator.
- `C` depends on level and distance only (as specified); extinction makes the real catalogue less complete in the
  plane than toward the poles, which the fit averages over.
- Levels 0–1 keep their 3,000-ly import limit: there `C` drops from ~0.26 / ~0.08 to 0 in one step, and the
  density stays continuous (real stars inside, procedural outside).

---

## 5. Stellar ages; evolved stars and remnants from age

**Changed.**
- `StellarEvolution` (new): main-sequence lifetime fit (1 M☉ 10 Gyr, 2 M☉ 1 Gyr, 10 M☉ 25 Myr), turnoff mass,
  age ranges per density component (young disk 0–1 Gyr, thin disk 0.3–9, thick disk 8–12, bulge 7.5–12.5,
  nuclear disk 0.01–12, halo 10–13.2), initial–final mass relation, white-dwarf and neutron-star cooling,
  brown-dwarf cooling.
- `StarClassTable.Roll` now takes the star's component and returns its age (`GalaxyStar.Age`, Gyr). Main-sequence
  stars: age cut at their lifetime. Giants, bright giants, subgiants, carbon stars: mass = turnoff mass of their
  age. Supergiants, T Tauri, Herbig Ae/Be, Wolf–Rayet: ages of their short phases. White dwarfs: progenitor
  (IMF-weighted, above the turnoff), cooling age → temperature. Neutron stars: cooling age → temperature. Brown
  dwarfs: mass from the substellar mass function, temperature from mass and age, spectral type from temperature.
- Catalogue stars: an age estimate from their class, seeded by catalogue ID only (the same in every universe);
  Sol 4.57 Gyr. `StarSystem.Age` is the primary's.
- Companions: initial mass relative to the primary's initial mass, and the system's age decides what they are
  (brown dwarf, main sequence, giant beside an evolved primary, white dwarf, neutron star, black hole). The old
  "8% are white dwarfs" coin flip is gone; instead 10% of companions may have started heavier and died (like
  Sirius B). Companion mix in the `stats` sample: 21,336 main sequence, 3,043 white dwarfs, 53 brown dwarfs (before
  the giant rule: 98 giants beside dwarfs, now only beside evolved primaries).
- Class table: old-population F1–F9 weight 0.0119 → 0.003 (an old population has few F dwarfs), thin disk 0.0237 →
  0.0249 to keep the level-4 calibration (model 1,092 vs 1,066 real within 160 ly).
- System map: the star card shows the system's age.

**Measured** (`stars`, 405,000 generated stars in the disk and bulge): ages per component as designed (young disk
median 70 Myr, thin disk 3.0 Gyr, thick disk 10.0, bulge 10.0, halo 11.6). Old-population giants weigh 0.93–1.40
M☉ (median 1.01), subgiants likewise; white dwarfs median 0.56 M☉ and 4,300 K in the old bulge. Main-sequence stars
younger than their population ("formed late"): 3–6% in old populations (heavy G/F dwarfs). Speed: 8.2 µs per
procedural system (within the bench's noise).

New test `AgesFollowPopulationsAndEvolution`: ages within their population, main-sequence stars younger than their
lifetime, giants at the turnoff mass, white dwarfs in range, no main-sequence companion past its lifetime.

**Golden.** Cells `0x5432B95D1D9D85D5` → `0xF3C42388CE7F958C`; procedural systems `0xDB5A9536406D01B2` →
`0x41FDF66D1B336573`; real systems `0xD0DADDB204F53B3B` → `0xC8BE0B049BAF6BA3` (catalogue stars now carry an age).

**Decisions.** The class table stays the present-day mix (it is calibrated to the real sky); age decides the
properties of evolved stars and remnants, not whether they exist. Making existence age-conditional would change
each level's expected count per star and break the cell budget; the table's population weights carry it instead.
Main-sequence spectral codes are still drawn uniformly within an entry (equal numbers per subclass), as before.

---

## 7. A barycentric orbit tree

**Changed.**
- A system is now a tree: the root (body 0) is the only star or the outermost barycentre, fixed at the origin;
  `BodyKind.Barycentre` is new. Multiple stars are hierarchical, `(((A B) C) D)`, and a companion wider than 50 AU
  is itself a close pair in 25% of cases, `(A (B C))`. Both members of every pair orbit the barycentre on opposite
  sides at their mass share of the separation (one period), so the stars' centre of mass stays at the origin.
- Every star and every barycentre hosts planets: S-type around one star, P-type (circumbinary) around a pair, each
  zone limited by the stability limits for planets in binaries (Holman & Wiegert 1999) against everything outside
  it. A close companion stunts planet formation: around one star of a pair the planet rate falls (in log distance)
  from full at 50 AU closest approach to zero at 3 AU; circumbinary planets get half the single-star rate.
- Companion light: a planet's equilibrium temperature includes the light of every star outside its host, at the
  orbit-averaged distance of the pair that joins them.
- Moons above 0.005 M⊕ can hold atmospheres (cold ones thick, like Titan; others a thin veil). Item 6 replaces
  these rolls with retention physics.
- `BodyPath` (new): 32 bits = node (star 1–8 or barycentre 9–15) · orbit slot · moon · reserved moon-of-moon ·
  feature (belt). Sol's planets are slots 0–8 of node 1, its belts slots 20–21. `StarSystem.Find(path)`,
  `StarSystem.PrimaryStar`. Names: `<system> B 3`, `<system> AB 1`, `<system> B 3 a`, `<system> B Belt`.
- Client: barycentres are empty points (labelled when selected) and rows with their own icon; the star card shows
  members and age; planets around a companion or inner pair are drawn at 0.35 of the system's compressed scale
  around their host; short names drop the system name for stars too ("A", "BC 1").

**Measured** (`stats`, 43,083 procedural systems): 135,277 planets (was 107,069: companions and pairs now host
planets; without the close-binary suppression it was 179,508 with a median of 356 K, from planets squeezed into
the hot inner zones of tight pairs), median planet 262 K, hot giants in 0.7% of systems, 2,073 metal-rich worlds.
Companion stars: 25,297 main sequence, 2,162 white dwarfs (7.8%), 185 brown dwarfs, 46 giants. The test sample
(757 multiple systems near 600 ly) has 212 systems with 3+ stars, 1,335 planets around companions, 462 circumbinary
planets, 134 airless planets measurably warmed by a companion and 576 moons with atmospheres. Speed: 10.4 bodies per
system (was 8.5), 9.9 µs per system, 0.95 µs per body.

New test `MultipleStarsFormABarycentricTree` (paths unique and decodable, centre of mass fixed at three times,
companion light, all the new configurations occur); `OrbitsNeverCross` now checks every host and every pair.

**Golden.** Procedural systems `0x41FDF66D1B336573` → `0xEFAF6FE0D28121BF`; real systems `0xC8BE0B049BAF6BA3` →
`0xF1DF976ED04A7E2A`. Cells unchanged.

**Decisions.** Up to 5 stars (a primary, two companions, either a close pair); the path format allows 8 stars and
7 barycentres. Rings stay on planets only. Moons of moons and binary planets are left out (the path reserves
bits for moons of moons). The system frame's origin is now the root barycentre (ARCHITECTURE.md §6 updated).

---

## 6. Per-body physical state; labels and hazards derived from it

**Changed.**
- PlanetState (new, on every planet and moon): formation zone, bulk composition (metal, rock, ice, gas), core
  radius, heat flow and its tidal part, tectonics, volcanism and its activity, magnetic field, cratering, albedo,
  equilibrium / day / night temperatures, gases (ten mole fractions), greenhouse depth, surface liquid with ocean and
  ice cover, subsurface ocean, biosphere, surface kind (none, rock, ice, ocean) and hazards (twelve flags). Every
  field, unit, range and formula is in GALAXY.md §10.3.
- WorldBuilder (new) builds it top-down: composition by formation zone (metal-rich only where metal alone
  condensed, or after a giant impact), radius from composition, radiogenic and tidal heat, volatile inventories,
  stripping by the "cosmic shoreline" (high-energy light against escape speed) and Jeans escape at an exosphere
  heated by that light, runaway greenhouse with water loss, a climate solver (cold traps for CO₂, N₂, CH₄, NH₃;
  water vapour; oceans, lava, lakes, ice; albedo; a grey greenhouse with log and saturating bands), the
  carbonate–silicate cycle as a bisection for the CO₂ that keeps 288 K, life and oxygen, crust and volcanism,
  magnetic field, subsurface oceans, day/night swing, cratering. The type (new: gas dwarf, lava world), the
  atmosphere label (now by its main gases: carbon dioxide, nitrogen, nitrogen–methane, steam, oxygen–nitrogen, …
  plus trace / thin / thick / crushing) and the surface kind are derived from it. Landable is now "surface of
  rock or ice", not "not a giant". Giants keep a simple state (envelope, own heat, gases, field).
- HazardsFor: one rule set for generated bodies and Sol (vacuum, heat, cold, pressure, toxic air, acid, gravity,
  eruptions, radiation, molten ground, storms, quakes).
- Star activity (high-energy output relative to the Sun, by type and age) feeds stripping and exospheres; late red
  dwarfs 16 (they stay saturated for billions of years), early ones 4.
- Orbits: moons in a resonance (25%) keep a forced eccentricity of 0.002–0.012, the rest are circularised
  (10⁻⁶–10⁻⁴); moons start 1.2–2 times (was 1–1.5) beyond 2.5 planet radii; tidally locked planets get a fifth of the
  rolled eccentricity and a fiftieth of the tilt. Tidal heating is capped at 5 W/m² (twice Io).
- Rings carry a material (ice, rock and dust, metal, by the planet's equilibrium temperature) and a mass; belts a
  material (by formation zone) and a total mass.
- Sol: every planet and moon has a hand-written state from real data (Earth's oceans, plates and life; Venus's
  92 atm of CO₂ and acid; Io's sulphur volcanoes; Europa's and Enceladus's buried oceans; Titan's lakes; …); the
  Main and Kuiper belts and Saturn's, Uranus's and Neptune's rings have their materials and masses.
- Client: the body card shows surface (with oceans, lava, lakes, ice), composition, geology, heat flow, magnetic
  field, buried ocean, life, hazards, day/night temperatures, the atmosphere with its pressure, ring and belt
  materials. Planets are drawn from their state: ocean and lava cover, ice caps, glowing lava, haze coloured by the
  gases, deep cloud decks hiding the ground.
- GeneratorReport: stats reports the state (surface, liquids, life, tectonics, volcanism split into planets and
  moons with heat-flow percentiles, hazards) and a volume-complete frequency table by primary star; new worlds
  prints one example of each notable kind of world with its whole state.

**Calibration.** The new test WorldsMatchTheSolarSystem feeds the builder the Solar System's inputs with 20 random
inventories each. It found two real errors that the statistics had hidden: an exosphere at 4 × T_eq stripped
Titan's nitrogen (0 of 20 kept air; now T₀ + 750 K·√(S X), 170 K at Titan, 20 of 20), and pressure broadening twice
too strong made Earths with 3–4 atm of nitrogen 312–321 K (now twice Earth's nitrogen warms it about 5 K). Now all
20 Earths are temperate with liquid water, all Venuses dry hothouses, all Marses cold thin-aired deserts, all Moons
airless, all Titans cold and with air. Earlier rounds (with stats): the first climate loop started with all CO₂
in the air, so every temperate wet world fell onto the hot branch (liquid water on 534 bodies in 43,083 systems,
Earth-like 1 system in 2,268); free moon eccentricities and moons too close in made 54% of bodies volcanic and
radiation-belt rules 48%; the water-vapour feedback had a loop gain above 1.

**Measured** (stats; the frequency rows from a volume-complete sample of 3,551 systems within 70 ly of a point
570 ly from Sol, mostly red dwarfs as in the real Galaxy; the rest from the usual 43,083 systems):

| | Item 7 | Item 6 |
|---|---|---|
| Speed | 9.9 µs per system, 0.95 µs per body | 20.0 µs per system, 1.95 µs per body (real systems 15.1 µs) |
| Median planet temperature | 262 K | 288 K (now with albedo and greenhouse) |
| Earth-like worlds | rolled, about 1 system in 110 | derived, 1 system in 293 |
| Surface liquid water | – | 1 system in 9 (G dwarfs 1 in 4, M dwarfs 1 in 10) |
| Life / oxygen / plants and animals | – | 1 system in 30 / 140 / 590 |
| Volcanically active | – | moons 19% (almost all by tides), solid planets 72% (mostly as gentle as Earth) |
| Subsurface oceans | – | 18% of planets and moons |
| Landable | every planet and moon but giants | 82% of planets and moons (rock or ice) |
| Moons with atmospheres (test sample) | 576 (rolled) | 921 (retained) |

The climate solver is the cost: airless bodies settle in two passes, bodies with air in up to twelve, and wet
temperate rocky worlds run up to eight climates for the carbonate–silicate bisection.

Screenshots (system map, body card with the state): docs/images/system-earth.jpg (Sol, Earth),
system-complex-life.jpg (a 4.8 M⊕ super-Earth with plants and animals around an M0 dwarf, 1.5 atm
oxygen–nitrogen, high gravity), system-ocean-world.jpg (a global ocean under 4.8 atm of CO₂ with microbes, in a
quadruple system, beside a sulphur-volcano moon), system-lava-world.jpg (an airless, tidally locked lava world
around a T Tauri star, 1,788 K by day, 538 K by night).

New tests: WorldsMatchTheSolarSystem; GeneratedSystemsArePlausible checks every body's state (compositions and
gases add up, only giants and gas dwarfs lack a surface, landable = rock or ice, mean between night and day, vacuum
below 0.001 atm, Earth-like = oxygen from life over water) and that oceans, lava, hydrocarbon lakes, buried oceans
and life occur; SolSystemIsReal checks Sol's states.

**Golden.** Procedural systems  xEFAF6FE0D28121BF →  x3672732AE16E55F1; real systems  xF1DF976ED04A7E2A →
 x6D9ECAF5B04D1C7D (every planet and moon now has a state; moon and locked-planet orbits changed). Cells unchanged.

**Decisions.**
- The odds of life (0.3), of oxygen-making life after 2 Gyr (0.3) and of complex life after 3.5 Gyr (0.45) are
  unknowable; they are constants (WorldBuilder.LifeChance and next) chosen so living worlds are rare but
  findable: about 17 worlds with plants and animals among the 10,000 systems within 100 ly of any point.
- Red-dwarf activity follows the cumulative X-ray/UV record (saturated for Gyr): Earth-sized planets in the
  habitable zones of late red dwarfs are mostly airless, as JWST found for TRAPPIST-1 b and c; super-Earths keep air.
- The climate starts warm (as planets form) and the carbonate–silicate cycle is solved directly, so a world that
  can hold liquid water does; snowball states are not generated.
- Volcanism stays common on large rocky planets (internal heat over Earth's threshold); the eruption hazard needs
  activity over 0.5 (14% of bodies).
- Breathable means 0.16–0.5 atm of oxygen with under 1% CO₂ and 0.01% SO₂; Earth's only hazard is quakes.
- The state is stored on Body (not recomputed), so goldens pin it and M4 reads it without the generator.

**Open for the owner.** Whether these frequencies (water 1 in 9 systems, life 1 in 30, oxygen 1 in 140, plants
and animals 1 in 590) suit the game; they are three constants. Whether 72% of solid planets having some volcanism
is too much for variety.

---

## 8. Nearby brown dwarfs, real companions, Proxima, AT-HYG prepared

**Measured first** (GeneratorReport near 21, every catalogue star within 21 ly with its companions): HYG has all
the nearby white dwarfs (Sirius B, Procyon B, 40 Eridani B, Stein 2051 B, van Maanen's Star, Gliese 440, GJ 1221)
but none of the brown dwarfs and few ultracool stars of the published nearest-star lists. It also showed source
problems that matter for real systems: companions' separations taken from their own noisy parallaxes (Procyon B
3,458 AU from Procyon, real 15; 61 Cygni B 2,375 AU, real 86; Struve 2398 B 23,634 AU, real about 56); stars
without a type turned into G5 dwarfs of absolute magnitude 13–16 (van Maanen's Star, a white dwarf, among them);
red dwarfs typed "M5 III" (Luyten's Star, Gliese 581, 876, 693).

**Changed.**
- Supplement: 	ools/Hollow.CatalogBuilder/supplement.csv (IDs 1,000,000–1,999,999), 28 objects within 20 ly from
  public measurements: Luhman 16 A/B, WISE 0855−0714, UGPS J0722−0540, WISE 1741+2553, WISE 1639−6847,
  DENIS 0255−4700, WISE 1506+7027, DENIS J0817−6155, 2MASS 0939−2448 A/B, WISE 0350−5658, 2MASS J1114−2618,
  2MASS J0415−0935, WISE 1541−2250, 2MASS 0937+2931, SIMP J0136+0933, Teegarden's Star, SCR 1845−6357 A/B,
  DENIS J1048−3956, 2MASS J1540−5101, LSR J1835+3259, and the companions Epsilon Indi Ba/Bb, Gliese 229 Ba/Bb,
  Gliese 570 D. New systems by RA, Dec and parallax; companions by separation (placed across the line of sight).
  Values checked against the Wikipedia list of nearest stars (Gaia/RECONS parallaxes); unmeasured masses are
  typical for the type and noted as such.
- Bindings: Proxima Centauri is Alpha Centauri C (its old system address now names a companion and finds
  nothing; its name leads to Alpha Centauri).
- Repairs (every build): untyped stars fainter than +7.5 and "giants" fainter than +6 get the main-sequence type
  of their measured magnitude; van Maanen's Star → DZ8, VB 10 → M8 V, Wolf 424 B → M7 V.
- The builder applies all of it in Finish, so --update brought the shipped file up to date without the CSV;
  running it twice gives a byte-identical file. 99,140 stars, 98,695 systems (was 99,112 / 98,676).
- Generator: catalogue companions sit at their **projected** separation (angle on the sky × distance); two
  companions closer to each other than a fifth of their distance from the primary orbit each other as a close pair
  (Epsilon Indi Ba/Bb, Gliese 229 Ba/Bb); a catalogue star listed alone beyond 25 ly rolls companions and keeps
  only ones the catalogue can't have seen (where it holds under half of their level at that distance, or closer
  than 1″). In the test sample, 215 of 658 checked catalogue singles gained unseen companions, none within 25 ly.
- **AT-HYG prepared, not downloaded.** The builder detects AT-HYG's header and reads it as catalogue number 1:
  x0, y0, z0 positions, Gaia distances, stable IDs (HYG ID where it has one, so Sol stays 0; Tycho-2 stars
  2²⁴ + packed TYC; Gaia-only stars 2³⁰ + AT-HYG serial), multiple stars from HYG's CSV through the hyg
  column (--hyg), else Gliese numbers, else Tycho-2 components within an arcminute; --keep-all for a
  completeness refit. Checked once on a six-row hand-made sample in the AT-HYG layout (six rows became four
  systems: a Gliese pair and a Tycho-2 double grouped).
- GeneratorReport near [ly] lists the catalogue around Sol.

**What the AT-HYG switch needs from the owner** (a download, so not done):

| | |
|---|---|
| File | data/athyg_40.csv.gz (AT-HYG v4.0, the current version; one gzip file, Git LFS) |
| Source | <https://codeberg.org/astronexus/athyg> (raw: https://codeberg.org/astronexus/athyg/raw/branch/main/data/athyg_40.csv.gz) |
| Size | 199,688,001 bytes (190 MiB) compressed; LFS object sha256 69ad04dd33d7c7bb4f5e1b4682798075811547ea9fb8d0e802e5b319c46818a6 (read from the repository's LFS pointer, 2026-10-12) |
| Contents | about 2.55 million stars, Gaia DR3 distances for nearly all; CC BY-SA 4.0 |
| Optional | HYG's own CSV for multiple stars: data/hyg/CURRENT/hyg_v44.csv.gz (HYG v4.4, about 13.6 MB) from <https://codeberg.org/astronexus/hyg>, or hyg/CURRENT/hygdata_v41.csv (33,932,548 bytes) from <https://github.com/astronexus/HYG-Database>, the version the shipped file came from |

Then: dotnet run --project tools/Hollow.CatalogBuilder -- athyg_40.csv athyg.bin.gz --hyg hygdata_v41.csv --keep-all,
refit completeness for catalogue 1 with GeneratorReport shells (AT-HYG is complete far deeper than HYG, so
CatalogueCompleteness needs a second parameter set), rebuild without --keep-all, and decide how to ship a file of
roughly 100 MB (probably streamed by sky region).

**Measured.** Speed: 18.6 µs per procedural system (unchanged within noise), 13.8 µs per real system. Real systems
within 60 ly: 1,390 (was 1,371). Alpha Centauri: Proxima orbits the AB barycentre at about 9,600 AU; 61 Cygni A–B
now 50–200 AU apart instead of 2,375. Screenshot: docs/images/system-alpha-centauri.jpg (Proxima selected, with
its generated planets until item 9 authors the real ones).

New test NearbyDwarfsAndRealCompanions; CatalogueMatchesRealStars finds Proxima as Alpha Centauri's companion.

**Golden.** Real systems  x6D9ECAF5B04D1C7D →  x3B34458D8352D1D2 (the golden set now has Luhman 16 and Epsilon
Indi instead of Proxima, which is part of Alpha Centauri; every catalogue multiple's separations changed). Cells and
procedural systems unchanged.

**Decisions.** The supplement stops at 20 ly, inside the level-7 complete core (§8.3), so no star is counted twice;
farther famous stars come with item 9. Catalogue stars keep their positions; only the separations used for orbits
are projected. Star bodies are still named by letter (Alpha Centauri C); real star names in system maps come with
the authored layer (item 9). The catalogue keeps its name hyg41.bin.gz and number 0: new IDs only extend it.

**Open for the owner.** The AT-HYG download above (and how to ship a 100 MB catalogue).

---

## 9. The authored layer: real planets, real names, named regions

**Changed.**
- Hollow.Galaxy.Authored (new): hand-written content from public facts, compiled into the engine (never a
  missing file) and versioned (AuthoredLayer.Version = 1, recorded with a shard like the stage versions).
  Systems are keyed by catalogue ID (shared by HYG and AT-HYG) and bodies by body path; addresses are untrusted.
- Real systems: an authored host gets exactly its known planets (measured mass, semi-major axis, eccentricity; the
  period from the masses; spin, moons and the physical state generated as usual, from the real star), named
  <prefix> <letter>; no invented planets, belts or rings around them. Star and barycentre names by node. Content:
  Proxima Centauri b and d (and the name Proxima Centauri for Alpha Centauri's node 3), Epsilon Eridani b,
  51 Pegasi b, Teegarden's Star b–d, TRAPPIST-1 b–h, Kepler-186 b–f, and the names Epsilon Indi Ba/Bb/B and
  Gliese 229 Ba/Bb/B. TRAPPIST-1 and Kepler-186 joined the catalogue supplement (99,142 stars, 98,697 systems).
  Values checked against Wikipedia's planet tables (Agol 2021, Faria 2022, Dreizler 2024, Quintana 2014).
- Regions (Region, Regions): 39 real clusters and nebulae (13 open clusters, 5 globulars, 10 emission nebulae,
  a reflection and 3 dark nebulae, 5 planetary nebulae, 2 supernova remnants) with designation, kind, centre from
  J2000 coordinates and distance (the Messier list and well-known values), and a radius from their apparent size.
- Names: a procedural system inside a region, in the sector holding the region's centre, is named
  <Region> <cell> <level>-<index> (Pleiades LA-YS 6-0): the real name, then Hollow's own cell code, with no
  word between (not Elite's <Region> Sector form, and from no game's table). It parses back to the address; the
  sector form still finds it. No region name parses as a sector name.
- Search: regions by name (from three letters) or designation, as places with their kind; region-named systems.
  SearchResult gained PlaceKind.
- Client: the galaxy map draws each region as a ring of its size in a colour by kind with its name (through the
  Labeller, after star names); the focus panel names the region you're in ("Emission nebula in Xelam Lorire");
  a selected star's card shows its region and sector. Star bodies show authored names in the system map.

**Measured.** Speed unchanged (20.4 µs per procedural system, 15.5 µs per real one; the authored layer is a
dictionary lookup per real system and a sector check per procedural name). 27 generated systems near the Pleiades
carry its name. TRAPPIST-1 e: a 6.1-day year, 225 K, airless around its flaring late red dwarf (as JWST found for
b and c); TRAPPIST-1 g comes out a water world. 51 Pegasi b: a hot Jupiter above 1,000 K.

Screenshots: docs/images/system-trappist-1.jpg (TRAPPIST-1 e selected), docs/images/galaxy-orion-regions.jpg
(the Orion Nebula, the Horsehead and M78 among Orion's real stars).

New test AuthoredSystemsAndRegions.

**Golden.** Real systems  x3B34458D8352D1D2 →  x32A5776B1B463E3B (Alpha Centauri gets Proxima b and d and
Proxima's name; Epsilon Indi its names; Epsilon Eridani its real planet). Cells and procedural systems unchanged.

**Decisions.**
- Authored hosts have only their known planets: inventing planets around real stars would put fiction next to
  facts; generated moons are allowed (real moons are unknown, and the usual rules leave close-in planets bare).
- Minimum masses are used as masses (true masses are usually a little larger); Kepler-186's masses are estimates
  from radii, noted in the data.
- Regions name only systems in their home sector, so names stay unique and parseable without a second lookup
  table; a region straddling a sector border keeps the sector name on its far side.
- Regions are places, not populations yet: no generated cluster overdensity and no nebula gas (follow-ups).
- Star names by node number are checked by the test, so a generator change that renumbers nodes fails loudly.

**Open for the owner.** Which further real systems and regions to author (the format makes each a few lines);
whether clusters should get generated member stars and nebulae a visible volume on the galaxy map.

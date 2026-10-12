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

| | Before (5581c88) |
|---|---|
| Procedural system | 5.9 µs (8.4 bodies, 0.71 µs per body) |
| Real system (GetSystem) | 4.4 µs |

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

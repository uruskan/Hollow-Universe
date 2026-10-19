# Environment performance baseline (T0 item 4)

19 October 2026. The measured hosted baseline of [TERRAIN_SCENERY_PLAN.md](TERRAIN_SCENERY_PLAN.md) T0 item 4,
and the numerical budget T5 compares candidates against. It measures the game as it stood before T2 (source
`8244f984`: Earth layout 5, save revision 5, cliffs generation 2). Unedited reports, frames and logs are in
`.artifacts/t04-baseline/` (not committed).

## Machine and method

- AMD Ryzen 5 3600 (6 cores, 12 threads), 16 GB, NVIDIA GeForce RTX 2070 SUPER 8 GB (driver 616.92),
  Windows 10 Pro 19045, Godot 4.7.2 .NET. Debug game assemblies, Release dedicated server.
- Every run: a hosted game from a fresh scratch save, 1600 × 900 windowed, solar hour 10, the preset passed for
  the run only (`--quality`; the saved preferences are untouched), vsync off and the frame cap lifted while
  measuring. These are benchmark settings, not a promised frame rate.
- **Breakdowns** (`--environment-route <folder> --environment-breakdown`) stand still at a site, sample 8 s, and
  remove one layer at a time: trees and plants, the whole ground-cover node, then its procedural grass, litter
  and authored plants alone (through the field's diagnostic mask), cliffs, monuments, water, sun shadows and
  SDFGI. The baseline is sampled before and after; the two agree within 0.3 ms at High and Low (2.2 ms at
  Maximum, after SDFGI was restored).
- **Route walks** (`--environment-route <folder> --environment-profile 60`) walk the connected benchmark route
  from its beach with the normal hosted movement and record the first 60 s of every leg.
- Another session's test process ran during the two High breakdowns; the other seven runs had the machine
  alone. The High clearing breakdown repeats an earlier pilot within 0.1 ms of GPU time.

## Breakdowns

Frame and GPU times are means in milliseconds; a layer's cost is the GPU time its removal saved.

| | High, clearing | High, woodland | Low, clearing | Low, woodland | Maximum, woodland |
| --- | ---: | ---: | ---: | ---: | ---: |
| Frame (mean / p95) | 16.0 / 16.2 | 15.7 / 16.6 | 6.8 / 7.1 | 8.3 / 11.5 | 33.4 / 44.1 |
| GPU | 15.5 | 15.1 | 6.2 | 6.0 | 32.8 |
| Draw calls | 1,205 | 1,191 | 813 | 815 | 2,267 |
| Primitives | 9.3 M | 8.4 M | 5.1 M | 4.5 M | 16.4 M |
| Ground cover (whole node) | 5.1 | 4.1 | 2.2 | 1.8 | 8.2 |
| – procedural grass | 1.6 | 2.2 | 1.0 | 1.0 | 5.5 |
| – litter | 0.0 | 0.1 | 0.1 | 0.1 | 1.6 |
| – authored plants | 2.9 | 1.6 | 1.0 | 0.7 | 3.1 |
| Trees and plants | 2.5 | 2.8 | 1.0 | 1.0 | 7.6 |
| Sun shadows | 2.4 | 2.8 | 0.7 | 0.7 | 8.7 |
| SDFGI | – | – | – | – | 3.6 |
| Cliffs, monuments, water | ≤ 0.2 each | ≤ 0.2 each | ≤ 0.1 each | ≤ 0.1 each | 1.2–1.6 each |
| Video memory (peak) | 5.7 GiB | 6.1 GiB | 5.5 GiB | 5.6 GiB | 6.8 GiB |
| Client private memory | 10.6 GiB | 10.5 GiB | 10.5 GiB | 10.4 GiB | 11.7 GiB |
| Start to steady play | 41.9 s | 37.6 s | 34.4 s | 34.7 s | 42.8 s |

Submitted ground cover, before the renderer's culling (instances / triangles):

| | Procedural grass | Litter | Authored plants |
| --- | --- | --- | --- |
| High, clearing | 34,225 / 1.03 M | 13,225 / 0.24 M | 6,625 / 2.44 M in 30 batches |
| High, woodland | 34,225 / 1.03 M | 13,225 / 0.24 M | 4,886 / 1.58 M in 33 batches |
| Low | 16,900 / 0.51 M | 6,724 / 0.12 M | 1,638–2,202 / 0.96–1.40 M |
| Maximum, woodland | 66,564 / 2.00 M | 25,921 / 0.47 M | 8,421 / 1.59 M |

Procedural grass and litter are each one batch covering a whole cube face, so the whole face's instances are
submitted wherever the camera looks. The drawn-contact atlas uploaded 2.4 to 4.0 MB per run. At High the frame is
GPU-bound (GPU within 0.6 ms of the frame); at Low in the woodland the frame (8.3 ms) exceeds the GPU (6.0 ms),
so Low is held by the CPU there. The client process commits 10.4 to 11.7 GiB of private memory at every preset,
on a 16 GB machine: a problem of its own, outside the environment budget.

## Route walks

The route is `EarthEnvironmentRoute.Find`'s 868 m walk (beach, woodland, clearing, water, cliff, weather station).
Three High walks and one Low walk, each leg's first 60 s:

| | High (three walks pooled) | Low |
| --- | ---: | ---: |
| Frames | 47,017 | 37,694 |
| Mean / median | 14.11 / 13.96 ms | 5.87 / 5.79 ms |
| p95 / p99 / worst | 16.20 / 16.72 / 33.32 ms | 6.67 / 7.33 / 39.16 ms |
| Spread of the walks' means | 0.10 ms | one walk |

| Leg (High) | Mean | p95 | GPU | Render CPU |
| --- | ---: | ---: | ---: | ---: |
| woodland | 13.93 | 15.19 | 13.42 | 1.24 |
| clearing | 15.06 | 16.21 | 14.55 | 1.34 |
| water | 13.73 | 14.76 | 13.21 | 1.20 |
| cliff | 15.68 | 16.78 | 15.16 | 1.08 |
| monument | 13.90 | 15.09 | 13.39 | 1.28 |

**Completion.** One High walk completed the route (exit 0, 375 s), with no native crash in any run. The other
three stopped on the last leg: the walker pressed against a boulder for 8 s at the same waypoint, 1.6 m short of
it. The route planner checked only the heightfield, and one of its waypoints lay inside a boulder's footprint.
It now keeps waypoints 1 m and its one-metre corridor samples 0.5 m clear of every solid natural object, as the
server builds their collision from the game's natural kinds (`EarthEnvironmentRoute.Find(world, natural)`); the
route test checks that independently. The planned route depends on where cliff groups stand, so it changes while
T2 iterates (877 m after T2's first iteration); the route baseline T4 and T5 compare against is recorded once T2's
cliffs are final.

## Budget for T4 and T5

At High, 1600 × 900, on this machine, a candidate must hold, over three warmed walks of the same route:

- the route's pooled mean and p95 within 2% of the baseline recorded on that route (the walks' means spread by
  0.7%), and no new worst frame above 34 ms;
- the ground-cover node at most its baseline cost (5.1 ms at the clearing, 4.1 ms in the woodland), whatever
  coverage it adds, with the three layers reported separately;
- at Low, the same rules against the Low walk; at Maximum, a stress check only.

Coverage gained by spending more GPU time does not pass; T5's compact tiles are expected to pay for T4.

## T2's cost so far

At High, removing every cliff piece saves 0.64 ms of GPU time at the cliff-heavy mountain site (18.121935,
−168.553855) and 0.44 ms at the 877 m route's clearing, about 4.5 and 3.9 million primitives and 41 and 22 draw
calls: each nearby piece draws its 24,000-triangle near version. The 573 m route after T2's second iteration runs
through denser temperate forest; one High walk there averages 16.92 ms (p95 18.93 ms), which is not comparable
with the 868 m route. Its three-walk baseline is recorded when T2's cliffs are final.

Generation got heavier with T2's third iteration (`earth cliffs-bench`, one thread): all 6,144 cliff cells build
in 7–12 s, 1.2–1.9 ms a cell on average, but the heaviest cell takes 150–210 ms, and a collision tile built cold over
it takes 542 ms (58,092 triangles), against tens of milliseconds before T2. Workers build tiles 128 m ahead of a
player, and arrivals and respawns wait behind the loading screen, but `PhysicsWorld.EnsureTile` builds a tile still
missing on the calling thread, the server's tick: a ship outrunning the prefetch over mountains could stall it.
T5 bounds this: fewer, cheaper fit trials, or cells warmed on workers before any tile needs them.

The far layer (T2) draws every formation beyond the near layer's pieces. From 300 m above the mountain site at High,
its worst kind of view, hiding it saves 0.4 ms of GPU time (11.06 against 10.66 ms with the near layer alone), about
0.4 million primitives and 140 draw calls; casting shadows it would cost about 2.3 ms more, so it casts none (a
piece's shadow is the near layer's, out to 340 m). On the ground there the difference is inside the runs' noise
(±1 ms; the dense grass makes that view 29 ms), and the layer's own work on the main thread is about 20 µs a
frame. Its sweep built every cell in 12.6 s of a hosted start, on two low-priority threads (6,742 pieces in 105
chunks); a capture was ready 0.3–1.3 s later than without it, while the cells within 680 m are built.

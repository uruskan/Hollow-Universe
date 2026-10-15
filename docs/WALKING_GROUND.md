# The ground at walking scale (S4)

Status: **design with look-dev renders and a working generator, for the owner's review (15 October 2026); S4c, the
game drawing it, built the same day (§17).** This is package S4 of [SCALE.md](SCALE.md) §6. The owner asked for it on
15 October 2026 ("finish S3c, S3d and S4 right now"). By the owner's rule for world work, the design and its pictures
come first, then the generator. Here both exist: the generator (`WalkingWorld` in `Hollow.Surface`) made every picture
below, and the hosted game stands on it and draws it behind a switch that is off by default (`--ground walking`, §12),
with the owned 4K scans as its sixteen ground layers (§8). Everything marked **Default** is listed in §2 so it can be
changed in one line. S4a merged into `main` on 15 October 2026; S4c built on the branch `s4`.

---

## 1. What S4 does

The first world is 2.41 km in radius: 73 km², about four 4.2 km Rust maps (SCALE.md §3.2). Today every zone stands on
the stand-in of S3 (WHOLE_WORLD.md §4): continents 20 m high, plain hills, biomes in latitude stripes. The owner's second
playtest (finding 3 of [KNOWN_PROBLEMS.md](KNOWN_PROBLEMS.md)) found a forest everywhere, a tree every metre, and no
biomes, valleys, rivers, lakes, cliffs or difference between flat and mountainous land. S4 makes the ground for a world
of this size:

- **landmasses** from the real sea cover (45 % on the first world), with sandy and rocky coasts and sea cliffs (§3);
- **relief provinces**: plains, hills, tablelands with escarpments, mountains with ridges (§4);
- **water**: streams and rivers from their sources to the sea or a lake, lakes in hollows, valleys, canyons and gorges
  cut by water (§5);
- **climate** at real-equivalent heights, so the highest peaks carry snow, and biomes in patches (§6);
- **woods** in stands, clearings and open plains, about half as dense as today's forest at most (§7);
- **the scatter** at the level by size (WHOLE_WORLD.md §3.2 left this for S4) (§7);
- **a plan for the scans** the owner owns (§8).

What S4 does not do:

| Not in S4 | Where |
|---|---|
| Drawing the ball, its terrain, water and plants in the client | S3c, being built; S4c adds the new ground's materials and water to it (§13) |
| The globe map | S3d, being built; it draws any `ISurface` |
| Air, clouds and sky at the new scale | S5 |
| Monuments in the world | M3, after S4 (WHOLE_WORLD.md decision 12) |

---

## 2. Decisions for the owner

Each row is a **Default** that the generator uses today. The pictures show the defaults.

| # | Question | Default | Alternative |
|---|---|---|---|
| 1 | One generator for small worlds | `WalkingWorld`, a new generator built for a few kilometres; `PlanetSurface` stays for the real-size look-dev views and tools | Retune `PlanetSurface`. Its plates, belts and climate work over 7 to 3,000 km and would all go |
| 2 | Landmasses | A few continents and many islands: the first octave 2.6 km long (§3) | One large continent; or an archipelago of islands a kilometre or two across |
| 3 | The relief mix | Shares of plains 0.30, hills 0.34, tablelands 0.16, mountains 0.20, grouped into ranges by an uplift field: on the first world 28 / 31 / 15 / 27 % of dry land (§4) | Fewer mountains (Rust's maps are mostly hills and plains); or more tablelands |
| 4 | Heights | Hills about 40 m; plateaus 52 m with mesas 30 m on them; mountains up to 400 m (the first world's highest peak is about 400 m); half the land under 23 m | Higher mountains (500 m); lower everything |
| 5 | Coasts | Sandy or rocky by a field about a kilometre across; a rocky coast rises in a sea cliff of at most 26 m (§3) | Higher sea cliffs; all coasts sandy |
| 6 | Streams | Water runs once its catchment reaches 0.26 km²: 32 streams, 14.2 km of them on the first world, 6 to 32 m wide (§5) | More and wider rivers, like Rust's 20 to 50 m: a catchment of 0.15 km² and wider channels |
| 7 | Lakes | Every hollow at least 2.5 m deep and 1.5 ha large holds a lake: 35 lakes, 2.4 km², 6 % of the land, frozen where it is cold (§5) | Fewer lakes (most hollows filled into flat meadows); or more |
| 8 | Canyons and gorges | Water cuts canyons into tablelands (up to 28 m deep, walls of 70°) and gorges into mountains (8 m, 48°) (§5) | Smooth valleys everywhere |
| 9 | Climate | Real-equivalent heights × 12 (SCALE.md default 5): snow on the highest peaks, ice caps beyond about 67°; the real climate, warm at the equator (18 °C mean) | Lean the first world's climate toward a temperate Rust-like world |
| 10 | Woods | Closed stands at 45 % of the biome table's density (135 trees a hectare in temperate forest, 300 today); woods cover 18 % of plains, 55 % of hills, 30 % of tablelands, 60 % of mountains: 41 % of dry land, 28 trees a hectare on average (§7) | Denser (60 %); sparser |
| 11 | Scatter | Tiles at the level nearest 120 m (level 5, 118 m on the first world), candidates spaced in metres as on the planet (§7) | — |
| 12 | Water in the rules | `ISurface.WaterLevelAt` (§9): lakes and streams are water for swimming, as the sea is; built behind the switch | Only the sea is water until S4c |
| 13 | The landing site | The generator picks it: a meadow 5 to 25 m above the sea at a forest's edge, near the coast and a stream, in the temperate belt; on the first world 41.90° S, 21.45° E (§11) | Choose one by hand from the maps |
| 14 | Scans | Sixteen ground layers from the owned 4K scans, built in S4c (§8); the cliff and rock meshes in S4d | More layers (a 17th needs another vertex channel, §17) |
| 15 | Speed | Make the height cheaper before it goes live: a target of 1 µs a height and 5 ms a physics tile (§10) | Go live at today's 1.75 µs and 10.5 ms if S3b's load tests still hold |
| 16 | Going live | Off by default (`--ground walking`) until the owner says go; then the default, and the stand-in stays for tests (§12, §13) | — |

---

## 3. Landmasses and coasts

![The first world: relief, equal area](images/scale/s4/world_relief.png)

*The whole first world on an equal-area map (Mollweide): heights from the deep sea (dark blue) to 330 m (white), lit from
the north-west. 2,048 pixels for 15.1 km round the equator: 7.4 m a pixel.*

- **Landmass field:** two warped noise fields on the sphere, about 2.6 km and 720 m long, and a small third (210 m) that
  breaks the coast into coves, points and skerries. A warp of about 220 m bends coasts and every border below.
- **Sea cover:** the field's level below which 45 % of 8,192 evenly spread directions lie is the coast, so the sea
  covers the real share (45.3 % measured). The sea lies at height 0, so `SeaRadius` is the radius.
- **Sea floor:** a curve from the coast down: 2 m at 15 m out, 9 m at about 60 m, 28 m at 150 m, 160 m in the deepest
  basins. Swimmers find shallows along most shores.
- **Coast kind:** a field about a kilometre across makes each stretch sandy or rocky. On a sandy coast the land's relief
  rises over about 70 m behind a low shore, and the shore within about 2 m of the sea is beach. On a rocky coast the
  relief rises in a sea cliff of at most 26 m within a few metres, and the rest of it further inland.

## 4. Relief provinces

![Relief provinces](images/scale/s4/world_provinces.png)

*Relief provinces: plains (pale green), hills (green), tablelands (ochre), mountains (violet); lakes and streams in blue.*

- **Provinces:** 96 points spread evenly over the sphere (about 0.8 km² each; half of them lie under the sea), each of one
  relief type. An uplift field about 3 km long weighs the choice, so mountains and tablelands gather into ranges and
  plains into lowlands.
- **Blending:** every province near a point weighs in by a Gaussian of its distance, normalised, so heights are smooth
  everywhere and a border is about 300 m wide. A nearest-province rule would leave a crease along every border.
- **Plains:** 4 to 9 m above the shore, gentle swells, and kettle hollows up to 4.5 m deep, which hold small lakes.
- **Hills:** eroded noise about 460 m long and 40 m high: smooth slopes, detail on crests and flats.
- **Tablelands:** a plateau 52 m high whose edge is an escarpment, three quarters cliff over a talus slope, with spurs
  and bays; mesas and buttes 30 m high standing on it. Water cuts their canyons (§5).
- **Mountains:** eroded ridged noise about 1.25 km long, up to 400 m, rising toward the massif's middle, with eroded
  detail on the flanks and gullies cut by the drainage.
- **Metre-scale ground:** 0.3 to 1.5 m of noise about 22 m long, rougher on stony and mountain ground.

On the first world: half the land lies under 23 m, 90 % under 120 m, and the highest peak stands about 400 m.

| Measured on the first world | Plains | Hills | Tablelands | Mountains |
|---|---|---|---|---|
| Share of dry land | 28 % | 31 % | 15 % | 27 % |

## 5. Water: streams, lakes, valleys

![Streams and lakes](images/scale/s4/world_rivers.png)

*Streams and rivers (blue, wider as they gather water), lakes, and dry valleys (faint grey).*

Water is worked out once when the world is made, on a drainage grid of the cube sphere (level 7 on the first world:
98,304 cells of about 30 m):

1. **The ground's height** at every cell, without the metre-scale detail.
2. **A priority flood** from every cell under the sea, lowest first. Each cell reached takes the level it was reached at
   (its own height, or the hollow's spill level if that is higher) and drains to the cell it came from. So every cell
   has a way down to the sea, through hollows at their spill level. The heap is ordered by (level, cell), so the result
   is the same on every machine.
3. **Catchments:** the ground each cell drains, summed down the ways in the flood's reverse order.
4. **Lakes:** cells the flood filled above their own height, joined with neighbours filled to the same level. A hollow at
   least 2.5 m deep and 1.5 ha large holds a lake at its spill level. The ground is not cut for it: the water covers the
   ground under its level inside a smooth field over the lake's cells, so the lake fills the hollow the relief made and
   its shores slope down into the water. Ground lower than the lake just outside the field is raised to a lip 0.4 m over
   its level, so nothing lower than the lake lies beside it. A lake where the mean temperature is under −3 °C is frozen:
   ice at its level, no water (§17).
5. **The network:** a cell with a catchment of 4 ha or more starts a dry valley; from 0.26 km² water runs in it. Each
   piece goes from a cell to the cell it drains to. Two passes of smoothing and a wander across the way it runs turn the
   grid's eight directions into curves.
6. **Levels, downstream first:** a stream's water lies 0.5 to 2.5 m below its banks, never below the water it runs into,
   so it meets the sea at sea level and a lake at the lake's. A stream leaving a lake starts at the lake's level.
7. **Carving:** the ground near a piece is the lowest of its profiles: a channel (a parabola from the bed to the banks,
   6 to 32 m wide), and a valley that eases from the banks to the ground, up to 150 m from a large river. On tablelands
   the valley's sides are canyon walls (rise 2.6 over 1), on mountains gorge walls (1.1), and the water cuts deeper as
   its catchment grows (up to 28 m and 8 m). Outside every channel, ground lower than a stream's banks beside it is
   raised to them, so its water stays in its channel.

The pieces are found by tile (level 5, 118 m) when a height is asked for. Every way down ends in the sea or a lake by
construction; a test walks every stream from its source to check it, and that its water lies in its channel.

On the first world: 32 streams start (21 run to the sea, 11 into lakes), 14.2 km of streams and rivers, 44 km of dry
valleys, 35 lakes covering 2.4 km² (the largest 58 ha and 19.6 m deep). Open water of lakes and streams covers 5.9 % of
the land (frozen lakes are ground).

![A river valley](images/scale/s4/zoom_valley.png)

*A river valley by the sea, 800 m across: two streams join and run to the sea between dry country (top) and grassland;
dry valleys in grey, a gorge's wall in black (top). Trees are drawn where the scatter puts them. The scale bar is
100 m. Rendered again for S4c: the straight-edged shading of the first renders is gone (§17.2).*

![A lake](images/scale/s4/zoom_lake.png)

*The largest lake, 58 ha, in its basin: the shores slope down to the water.*

## 6. Climate and biomes

![Biomes](images/scale/s4/world_biomes.png)

*Biomes, woods darker, snow where it lies.*

- **Temperature:** the planet's real mean (18 °C on the first world), 42 K between the equator and the poles, patches of
  ±3 K a kilometre or two across, and a fall of 6 K a kilometre at **real-equivalent heights**: a game height × 12, so a
  400 m peak is cold like a 4.8 km one (SCALE.md §3.3). The highest peaks carry snow; at the equator they are cold but bare.
- **Ice:** the temperature below which the real ice cover (8 %) lies, on 8,192 evenly spread directions: ice caps from
  about 67° and on high ground.
- **Moisture:** the real latitude bands (wet tropics, dry subtropics, wet mid-latitudes), patches ±0.3 down to a few
  hundred metres, drier deep inland, wetter in stream valleys and on mountains.
- **Biomes:** the biome table of GROUND.md §4.6, unchanged, read at real-equivalent heights (so alpine ground starts high
  on cold mountains), with beaches on sandy coasts within about 2 m of the sea.

On the first world's dry land: tropical forest 18 %, temperate forest 15 %, boreal forest 14 %, savanna 14 %, ice 12 %,
shrubland 9 %, grassland 9 %, beach 4 %, tundra 2 %, alpine 2 %, desert 1 %.

![The globe](images/scale/s4/world_globe.png)

*The first world seen from four sides, 25° north.*

## 7. Woods and the scatter

- **Density:** the biome table's densities times 0.45 in closed stands: about 135 trees a hectare in temperate forest,
  where the playtest had 300 everywhere.
- **Where woods grow:** each relief type has a share of woods (plains 18 %, hills 55 %, tablelands 30 %, mountains 60 %),
  more along streams, in patches about 420 m long with smaller clearings in them, so there are closed stands, edges,
  clearings, meadows and open plains. On the first world 41 % of dry land is wooded, at 28 trees a hectare on average.
- **Open ground for sight lines:** plains are mostly open. The horizon is 90 m on flat ground, so the long views come from
  hills, plateau edges and ridges, which the relief has everywhere.
- **The scatter** is the planet's (`Scatter`), fed by the walking ground (`IScatterField`): tiles at the level nearest
  120 m (level 5, 118 m), candidates spaced in metres as on the planet (trees 3.5 m, bushes 2.4 m, ferns 1.6 m, rocks
  3.2 m, berries 6.4 m, outcrops 12.8 m), so densities per square metre are the planet's. Nothing grows in water. A
  wooded tile has about 800 objects and takes about 5 ms.

![Wooded hills](images/scale/s4/zoom_forest.png)

*Wooded hills by the sea, 600 m across: a stand with its edges and clearings; 70 trees a hectare over the square.*

![Open plains](images/scale/s4/zoom_plain.png)

*Open plains under a mountain, with lakes and a stream, woods at the edges.*

## 8. The scans

Before S4c only 8 of the 34 owned ground sets reached the game (at 2K, from lossy WebP), and none of the 62 scanned
cliffs and rocks. Every set in `D:\GameDev\Assets` is bought for this game or CC0; `game/assets/THIRD_PARTY.md` records
where each came from.

**Built in S4c: sixteen ground layers at 4K** (`GroundMaterial`, appended; chosen per vertex by biome, slope, wetness and
the shore through `ISurface.Cover`, so the stand-in and the real planet use them too):

| # | Layer | Scan | Where the walking ground puts it |
|---|---|---|---|
| 0–7 | grass, forest floor, soil, granite rock, gravel, beach sand, snow, moss | as before, now at 4K | as before |
| 8 | wet sand | Wet Plain Sand (`rldas3p0`) | the swash and the waterline on sandy coasts, lake and stream beds |
| 9 | shingle | Beach Gravel With Pebbles (`tbvqegas`) | the foot of rocky coasts, sea beds by rocky shores, stream beds |
| 10 | wet grass | Wet Grass Ground (`sjzkdjba`) | stream banks, wet valleys, rainy country |
| 11 | mossy forest floor | Mossy Ground (`tj4ocdjr`) | wet and cold woods |
| 12 | tundra | Tundra Mossy Ground (`vjulbjjr`) | the tundra and alpine ground |
| 13 | sandstone | Canyon Sandstone Rock (`vimldgeg`) | cliffs on tablelands and in dry country (granite elsewhere) |
| 14 | dry earth | Desert Western Ground Dirt Coarse 07 (`xiqebhm`) | deserts, savanna, shrubland |
| 15 | ice | Frozen Lake (`tcbkdcfr`) | frozen lakes, bare ice on the ice sheets |

- **Files** (`tools/blender/surface_layers.py`, from the asset folders): each layer's colour and normal map are the
  scan's own 4K files (the Megascans JPEGs copied byte for byte; the Poly Haven PNGs as lossless WebP); a 2K lossless WebP
  holds roughness and height. Nothing lossy is written. Godot compresses each for the GPU (BPTC, mipmaps) when it imports
  it, and `GroundLayers` puts them together into three texture arrays when the game loads (1 to 3.4 s, under the loading
  screen): colour, normals, roughness with height. About 810 MB on the GPU; 912 MB of files in the repository.
- **Shading** (`surface_terrain.gdshader`): the scan at its own size near by, over a turned copy at about three times
  the size (the detail blend), a far turned copy from the air, macro variation of brightness and hue, height-based
  blending between layers by each scan's own height, roughness from each scan's map, triplanar granite and sandstone on
  steep ground. Each layer's normals keep their scan's convention (`layers.json` gives the flips; all agree today).
- **Not yet** (16 is the layers' count for now): the second beach sand and pebbles, coastal rock, the second forest
  floor, three more tundra sets, red rock, desert gravel and three desert sands, the snow line's icy grass, melting and
  windswept snow, the snow cliff and the ice wall. A 17th layer needs a 17th channel in the tile's vertex data (§17).

The table of the original plan, kept for the sets above:

| Where | Ground sets (`D:\GameDev\Assets`) |
|---|---|
| Sandy beaches; the swash | `beach_sand_uinladeo`, `beach_sand_with_pebbles_vl0fdilo`; `wet_plain_sand_rldas3p0` |
| Rocky coves and shingle | `beach_gravel_with_pebbles_tbvqegas`, `cracked_nordic_coastal_rock_ukohaj0mw` |
| Meadows, grassland | Dry Grassy Ground (in the game); `wet_grass_ground_sjzkdjba` in wet valleys and on river banks |
| Forest floor | Forest Floor (in the game), `forest_floor_wgyhdgy`; `mossy_ground_tj4ocdjr` in wet woods |
| Boreal forest and tundra | `tundra_mossy_ground_vjulbjjr`, `tundra_rocky_ground_vi2mahnmw`, `tundra_gravel_ground_vjepchymw`, `tundra_lichened_rock_vjbode1mw` |
| Tablelands and canyons, dry country | Canyon Sandstone Rock (Fab), `RedRock02_4K`, `desert_western_ground_dirt_coarse_07`, `desert_western_ground_gravel_coarse_02`, `desert_outback_ground_rocky_06` |
| Desert and savanna sand | `desert_sand_sfilxqh`, `desert_sand_sfiodwh`, `desert_sand_wkjgche`, `desert_western_ground_sand_fine_01` |
| Mountains, scree | Granite Rock and Gravel Ground (in the game) |
| Snow line and snow | Clean Snow (in the game), `icy_snow_on_dry_grass_sjziebea` (the snow line on grass), `melting_snow_uexjbdudy`, `fresh_windswept_snow_uehnaazn`, `sleet_covered_gravel_tb5oaf2s`, `windswept_snowy_stones_tdloaf1r`; `snow_cliff_uhqncfpdy`, `ice_wall_uebjabcr` on steep ice |
| Frozen lakes | `frozen_lake_tcbkdcfr` |

**Cliff and rock meshes** (Proposal: a dressing pass, S4d, places them along the generator's cliffs: the escarpments,
sea cliffs, canyon and gorge walls, where the slope passes 50°; new scatter kinds, appended; with collision):

| Where | Meshes |
|---|---|
| Rocky coasts and sea cliffs | `huge_nordic_coastal_cliff` (two), `massive_nordic_coastal_cliff`, Large Nordic Coastal Cliff (Fab), `coastal_cliff_04`, `coast_rocks_01/02`, `coast_line_01/02` (Poly Haven), `nordic_beach_boulder` |
| Tableland escarpments and canyons | `desert_western_cliff_layered_large_01`, `_xl_04` (also Fab), `_corner_xl_01`, `desert_western_ledge_rock_large_03`, `_medium_01`, `desert_western_rock_*` (six), `namaqualand_cliff_02` (Poly Haven) |
| Forest cliffs and boulders | `nordic_forest_cliff_large`, `nordic_forest_ledge_rock_large`, `nordic_forest_ledge_dirt_root_medium`, `nordic_forest_rock_large`, Forest Boulder (Fab), `rock_moss_set_02` (Poly Haven) |
| Mountains | `mountainside` (Poly Haven), `large_tundra_rock_formation`, `massive_tundra_rock_formation`, `tundra_rock_formation` |
| Tundra and snow | `tundra_*` boulders and stones (seven), `ice_cliff`, `large_ice_cliff`, `snowy_rocks_pile`, `rocky_snow_pile`, `snow_embankment` (two), `huge_snow_hill` |
| Scree at a cliff's foot | `small_stones_pack`, `rounded_cobble_pack`, `desert_western_pile_dirt_rock_small_01` |

The scanned plants (`*_ue_high`: bilberry, bracken, crowberry, dwarf birch and others) stay out: plants are made in code,
and grass is a shader system.

![A tableland](images/scale/s4/zoom_tableland.png)

*A tableland, 800 m across: canyons with steep walls (black), dry or with a stream in them, woods and a lake.*

![Slopes of the tableland](images/scale/s4/slopes_tableland.png)

*The same ground by slope: under 8° (pale green), 8–20° (yellow), 20–35° (orange), 35–50° (red), cliffs over 50°
(dark red). Cliffs are where the meshes go.*

![A mountain](images/scale/s4/zoom_mountain.png)

*The highest mountain, 420 m, 1 km across: ridges and gullies, snow, a tarn (left), the coast (right). The hill shading
makes the mountains' metre-scale detail look crinkled; on the ground they read as rock and snow (§17.5).*

![Slopes of the mountain](images/scale/s4/slopes_mountain.png)

![A rocky coast](images/scale/s4/zoom_coast.png)

*A rocky coast under a mountain: the sea cliff is the dark band along the shore.*

![A sandy beach](images/scale/s4/zoom_beach.png)

*A sandy bay with its beach; a stream crosses the isthmus.*

## 9. The interface

`WalkingWorld` is an `ISurface` and an `IScatterField`:

| | |
|---|---|
| `Radius`, `SeaRadius` | The game radius (2,410 m on the first world); the sea at height 0 |
| `HeightAt(direction)` | The ground, with channels, valleys, canyons and lakes' bowls cut in |
| `WaterLevelAt(direction)` | **New on `ISurface`** (default: the sea's level wherever there is a sea): the sea, a lake's level over its hollow, a stream's surface in its channel, or −∞. The water probe (`GroundProbe.InWater`) asks it, so lakes and streams are water for swimming and falling |
| `BiomeAt(direction)` | The biome table at real-equivalent heights, with beaches |
| `GroundCollision(frame, tile, margin)` | `ISurface`'s default grid of about a metre (`SurfaceGrid`) |
| `ScatterFor(tile)` | The scatter at its level by size (`ScatterPlan`) |
| `Fields(direction)` | Everything at a point (`WalkingSample`): height, water, landmass, the four relief weights, rocky coast, roughness, nearness to a stream |
| `Climate`, `BiomeWeights`, `Vegetation`, `ForestCover`, `MeanTemperature` | As on `PlanetSurface`, for the client's materials and the tools |
| `Lakes`, `Drainage` | The lakes and the network's pieces, for the map and the tools |
| `LandingSite` | Where players land (§11) |

- **Deterministic** (ARCHITECTURE.md §8): `DMath`, `Hash64`, `Rng` and the surface's gradient noise; `ArchitectureTests`
  scans the folder. The world is a pure function of the body's seed (`WalkingWorld.SeedFor(ZoneSpec.SurfaceSeed)`, with
  `WalkingWorld.Version` in it), its game radius and its real sea cover, ice cover and mean temperature. The drainage is
  worked out with a heap ordered by (level, cell) and sums in a fixed order; its heights are sampled on worker threads,
  each into its own slot.
- **The noise's basis:** Perlin's twelve edge gradients each have a zero component, so about one lattice edge in seven
  carries noise that is exactly zero along its whole length. Ridged noise, creases and thresholds near zero turn those
  into straight lines hundreds of metres long at walking scale (they were the first renders' worst fault). The walking
  ground uses the cube's eight corners as gradients instead (`NoiseBasis.Corners`). The planet keeps Perlin's, so its
  golden values are unchanged.
- **The numbers** are one table, `WalkingParameters`: every length, height, share and threshold of this document (§14).
- **Shared:** `WalkingWorlds` builds one world per body and radius in a process, as `PlanetSurfaces` does: a server, the
  host's client and a hundred bots draw from one.

## 10. Cost

Measured on the first world (one thread, warmed up, the best of three runs; the stand-in for comparison):

| | Walking ground | Stand-in |
|---|---|---|
| A height | 1.75 µs | 0.6 µs |
| A physics tile (level 6, 59 m, a metre grid, 2 m margin) | 10.5 ms | 2.6 ms |
| Building the world (both the server and every client) | 0.4–0.5 s | under 10 ms |
| Memory | about 11 MB (the drainage grid) | none |
| A scatter tile (level 5, a wooded one) | 5 ms, about 800 objects | — |

The real planet's surface costs 3.7 µs a height. The server builds collision tiles all the time on workers (S3b held
arrivals until their ground was built), so a tile at 10.5 ms instead of 2.6 ms is the one cost to watch. A first check ran
10 bots on beaches all round the walking ground for 15 s: the server's tick 0.73 ms on average, 2.1 ms at the 99th
percentile, nobody disconnected, prediction 0.016 mm off on average. It also showed the server's scatter cache (384
tiles, sized for the planet's level) making 4,209 scatter tiles in the run: it needs sizing for level 5.

**Plan (S4b, Default):** a target of 1 µs a height and 5 ms a tile, by
- sampling the slow fields (landmass, warp, provinces, plateaus, massifs: about half the cost) on a fixed lattice of the
  cube sphere, about 15 m, when the world is made, and interpolating them (the same lattice on every machine, so heights
  stay a pure function of the direction);
- keeping the heights of recent collision grids, shared by neighbouring tiles (as `PlanetSurface` does, GROUND.md §5.11);
- the scatter cache by size;

then S3b's load tests with 100 bots on the walking ground, which must hold their numbers (GROUND.md §5.12).

## 11. What changes when it goes live

- **Golden values:** a new one for the walking ground (`WalkingWorldTests`, 0x1D83ABE87CA2D9EA), moved on purpose with
  any change to the generator or its table. The planet's and the stand-in's stay. The movement check stays on the plain
  sphere.
- **Saves:** positions and bases saved on the stand-in mean nothing on the new ground. Going live wipes the world's rows
  once (a schema step, as schema 7 did; accounts and teams stay; characters start a new life).
- **The landing site:** chosen again (SCALE.md §4.8). The generator picks it (`LandingSite`): a meadow 5 to 25 m above
  the sea at a forest's edge, near the coast and a stream, in the temperate belt nearest 45°, off the mountains. On the
  first world it is 41.90° S, 21.45° E, a meadow 6 m above the sea at a forest's edge. `FirstWorld`'s landing
  latitude and longitude move there; until then a server on the walking ground at the default site uses it.
- **Arrivals:** beaches all round the world, as now (WHOLE_WORLD.md decision 8); sandy coasts give them (4 % of dry land is
  beach). Beaches lie only by the sea, but the search (`Beaches`) should also skip ground under `WaterLevelAt` (to do in
  S4e).
- **Nature IDs:** the scatter's (`ScatterNaturalObjects`: tag, face, tile X and Y at level 5, kind, candidate), not the
  stand-in's tag-0x5A IDs. Overrides saved under the old IDs go with the wipe.
- **Loot clusters and recyclers:** found as now (coast, forest edge, open), on the new ground; the six recyclers stand on
  each cube face's best coastal spot. They move with the ground.
- **Animals:** spawn areas read biomes and heights through `ISurface`, as now. The nav grid still reads only the sea
  (`NavGrid`: `SeaRadius`); it moves to `WaterLevelAt` in S4e so animals keep out of lakes and streams.
- **The map:** its picture's cache key names the ground and its version. S3d's globe draws `HeightAt`, `BiomeAt` and
  `WaterLevelAt`.
- **Monuments (M3):** placed on the new ground, on flat or tableland sites.
- **The client (S4c, built, §17):** terrain materials from biome weights, slope and wetness (§8); water surfaces for
  lakes (flat, at their level) and streams (their surface along the channel), drawn on the terrain's tiles as the sea
  is.

## 12. The switch (built)

**15 October 2026: the walking ground is the default** (the owner: the game they play must have everything that is built). The server and the hosted game stand on it unless given `--ground stand-in`; a hosted game keeps a save per ground (`hollow-host-walking.db` beside `hollow-host.db`). S4e's other steps (the landing site, arrivals off lakes, the nav grid's water) remain.

The game can stand on the walking ground today, off by default:

- **Server:** `dotnet run --project src/Hollow.Server -- --ground walking` (or `--ground stand-in`, the default).
- **The game:** `play-ground.cmd --ground walking` hosts a game on it (the option goes to the hosted server); joining
  players get the ground with the zone.
- **Bots:** `dotnet run -c Release --project tools/Hollow.Bots -- loadtest ... --ground walking`.
- **How:** `ZoneSpec.Ground` (`GroundKind`: the stand-in or the walking ground) is the one switch; `CreateSurface`
  picks the surface and `CreateNature` the scatter's natural objects by it. `ZoneInfo.Ground` carries it to clients
  (protocol 24). A server on the walking ground left at the default latitude and longitude lands players at its
  `LandingSite`.
- **The hosted game** draws it (S4c, §17): its sixteen ground layers, lakes' and streams' water with the sea's, and its
  woods at their densities. To stand somewhere for a picture: `--lat <deg> --lon <deg>` (the hosted server's landing
  site, where `--spawn origin` arrives), `--yaw <deg>` (the bearing to face on arrival) and `--pitch`, `--hour` (local
  time there); `walking shots` in the generator report prints the six places below.

## 13. Order of work after approval

| Step | What | Check |
|---|---|---|
| S4a | Built: the generator, its test, the look-dev report and the switch | This document's pictures; `WalkingWorld` tests; 10 bots on the ground |
| S4b | Speed (§10): the slow fields on a lattice, shared collision heights, the scatter cache by size | S3b's load tests with 100 bots on the walking ground |
| S4c | Built (§17): the client draws it: sixteen 4K ground layers from the scans, lakes' and streams' water with the sea's, the plants by the new scatter; the renders' two flaws fixed | The six Godot look-dev shots of §17 and the frame time |
| S4d | Cliffs and rocks: the scanned meshes along cliffs, sea cliffs, canyon and gorge walls, with collision | Shots of each; the load test |
| S4e | Live: the default ground, the landing site, the wipe, the golden values, the docs | The owner's playtest |

## 14. The table of numbers

`WalkingParameters` (`src/Hollow.Surface/Walking/WalkingParameters.cs`) holds every number above. The generator report can
change any of them for one run, to compare pictures: `HOLLOW_WALKING="StreamCatchment=150000;LakeMinArea=30000"`.

| Group | Numbers (defaults) |
|---|---|
| Landmasses | `ContinentWavelength` 2,600 m, `IslandWavelength` 720 m, `IslandShare` 0.32, `CoastDetail` 0.035 at 210 m, `WarpWavelength` 1,500 m, `WarpReach` 0.09 rad |
| Coasts | `CoastKindWavelength` 950 m, `SandyShoreRamp` 0.1, `RockyShoreRamp` 0.012, `SeaCliffHeight` 26 m |
| Provinces | `Provinces` 96, `ProvinceBlend` 300 m, `UpliftWavelength` 3,000 m, shares 0.30 / 0.34 / 0.16 / 0.20 |
| Relief | `PlainsHeight` 7 m, `KettleDepth` 4.5 m, `HillsHeight` 44 m at 460 m, `PlateauHeight` 52 m, `MesaHeight` 30 m, `CliffShare` 0.75, `MountainHeight` 400 m, `RidgeWavelength` 1,250 m, `RidgeWarp` 110 m, `RidgeCrest` 0.14, `MountainDetail` 0.12, `GroundDetail` 0.3 m, `StonyDetail` 1.2 m |
| Water | `DrainageCell` 30 m, `ValleyCatchment` 4 ha, `StreamCatchment` 0.26 km², `LakeMinDepth` 2.5 m, `LakeMinArea` 1.5 ha, `FrozenLakeTemperature` −3 °C, stream half-width 3 m (+2.2 a size), drop 0.8 m, depth 0.9 m, valley 45 m (+22), dry valley 26 m, `Meander` 0.35 cells, `RuggedValleyFade` 0.3, `CanyonDepth` 28 m at 0.25 km², `CanyonWall` 2.6, `GorgeDepth` 8 m, `GorgeWall` 1.1 |
| Climate | `RealHeightFactor` 12, `LapseRate` 6 K/km, `EquatorToPole` 42 K, patches ±3 K and ±0.3 at 1,700 m |
| Plants | `TreeDensityScale` 0.45, woods 0.18 / 0.55 / 0.30 / 0.60, `WoodsWavelength` 420 m, `ScatterTile` 120 m |

## 15. Tools

`dotnet run -c Release --project tools/Hollow.GeneratorReport -- walking <command>`:

| Command | What |
|---|---|
| `stats` | Speed, sea cover, heights, relief and biome shares, woods, streams and lakes, a scatter tile |
| `relief`, `biomes`, `rivers`, `provinces` `[png] [width]` | A whole-world equal-area map |
| `globe [png] [size]` | Four views of the globe |
| `zoom lat lon metres [png] [size]` | A close-up at walking scale, with the scatter's trees, outcrops and berries drawn |
| `slopes lat lon metres [png] [size]` | A close-up by slope class |
| `spots` | Places for close-ups: the highest peak, a river valley, a rocky coast, a beach, a tableland, the largest lake, a plain, the landing site, wooded hills |
| `shots` | Places for the game's ground-level shots (§17): a valley with a stream, a coast, a cliff, a snowy mountain, a forest edge, a plain, as `--lat --lon --yaw --pitch` |
| `cover lat lon` | What the ground looks like at a point (`ISurface.Cover`: biomes and ground materials) and the plants and rocks within 10 m |
| `closeups [folder]` | Every spot's close-up |
| `compare [png]` | Today's stand-in above the walking ground |
| `all [folder]` | The whole set, by default into `docs/images/scale/s4` |
| `probe lat lon metres size x0 row x1` | The fields along a row of a close-up's pixels (a negative `x0` probes a column), for finding faults |

![Today's stand-in and the walking ground](images/scale/s4/compare_standin.png)

*Today's stand-in (top: continents, latitude stripes, no water inland) and the walking ground (bottom), the same
projection.*

![The landing site](images/scale/s4/zoom_landing.png)

*The landing site the generator picks, 800 m across.*

## 16. Found while building

- **Straight lines from the noise.** Ridged noise on Perlin's gradients drew ruler-straight ridgelines along lattice
  edges where the noise is exactly zero (§9). Fixed by a second gradient basis for the walking ground.
- **Creases along province borders.** Weights from the nearest province (a minimum) kink along the borders, and the
  provinces' amplitudes then crease there. Fixed by normalised Gaussian weights.
- **A missed tile.** The drainage's pieces are found by tile; `TileKey.Around` samples a third of a tile apart, so a
  piece's reach could clip a tile no sample fell in, leaving a step. Fixed by one more step of reach; a test walks
  100,000 points 5 cm apart and finds no step over 0.4 m.
- **Perched water.** A lake's outlet valley cut its rim below the lake's level, and a stream crossing a hollow could stand
  above the ground beside its channel. Fixed by lakes' shapes with a lip, outlets at the lake's level, and banks.

## 17. S4c as built (15 October 2026)

Built on the branch `s4` after S4a merged into `main`. With `--ground walking` the hosted game draws the new ground: its
materials, its lakes' and streams' water and its woods. With the switch off nothing changes but the sixteen ground
layers (§8) and the biome table's new columns, which the stand-in and the real planet use too.

### 17.1 What the game draws

- **The ground's look** comes through `ISurface.Cover`, S3c's drawing contract, which `WalkingWorld` overrides: the
  client has no path of its own for the walking ground. The rule: ice on frozen lakes; under water a bed (by the sea, wet
  sand and shingle by the coast's kind; in lakes and streams shingle, wet sand and soil); elsewhere the biome table's
  materials through the shared rule (`BiomeTable.GroundMaterials`), with sandstone for granite on the cliffs of
  tablelands and dry country, wet grass and mossy forest floor where the ground is wet or by a stream (mossy floor in
  the cold too), wet sand and shingle along the sea's edge. `walking cover <lat> <lon>` prints what a point gets, and
  the plants and rocks within 10 m.
- **Sixteen layers** (§8). The biome table's rows spread over the new columns, so every world draws tundra ground,
  mossy boreal woods, dry earth in deserts, savanna and shrubland, and ice on ice sheets. Each new layer belongs to a
  family among the first eight (`BiomeTable.Family`: wet sand is sand, shingle gravel, wet grass grass, mossy floor
  forest floor, tundra moss, sandstone rock, dry earth soil, ice snow); footsteps, impacts and the grass read the
  families, so the sounds and effects need no new rows.
- **The tile's vertex data:** the sixteen weights as bytes, twelve packed three to a float in `CUSTOM2` (exact integers
  under 2^24) and four in `CUSTOM3`. A 17th layer needs another channel.
- **Water, one system, the sea's.** Each terrain tile asks `WaterLevelAt` at every vertex and two vertices beyond its
  edge (`TileGrid.WaterLevels`): a vertex's water lies at its own level where it is wet, else at the highest wet level
  within two vertices, else 2 m under the ground, so the water's edge is where the ground cuts it, as the sea's is. Lakes
  lie flat at their level; streams slope along their channels. The water depth's second channel marks inland water: the
  sea shader drops its swell, surf and swash there and keeps a third of its foam.
- **Woods** at the walking ground's densities: the client's plants come from `ScatterFor` at its level (level 5, 118 m),
  and the server's natural objects (trees to chop, rocks, berries) from the same scatter.
- **Standing somewhere** in a hosted game: `--lat` and `--lon` (the hosted server's landing site), `--yaw` and
  `--pitch`, `--hour` (local time there) and `--clouds` (this client's cloud cover). `walking shots` prints the six places
  below for the player Ada: a player arrives 3 to 8 m from the landing site at a place fixed by the name's hash, so each
  place is the landing site that brings Ada to open ground with no plant or rock within 3 m and no tree within 6 m.

### 17.2 The renders' two flaws, fixed

- **The straight-edged shading across the mountain** in `zoom_valley.png`: a crease of the ridged noise. Folding the
  noise at zero makes a sharp crest, and the first octave's crest ran straight for hundreds of metres. `RidgedNoise`
  turns every octave by its own rotation and rounds the crest (`RidgeCrest` 0.14, halved each octave), so no ridgeline
  runs straight.
- **Lakes inside raised rims:** the small fields of the landmass (islands, coast detail) opened pockets of sea deep inland,
  and the coast's ramp flattened the land round them into rings at sea level; the flood then filled them as lakes
  inside walls. Those fields now cut into the land only near the continents' coast, and the relief rises inland of the
  continents' own shore (`PocketMargin` 0.03). Lakes no longer carve a bowl: the water fills the hollow the relief made,
  so its shores slope down into it, and only ground lower than the lake just outside it is raised to a lip of 0.4 m
  (§5).
- **Frozen lakes:** a lake where the mean temperature at its level is under −3 °C (`FrozenLakeTemperature`) is ice at
  its level, with no water; it is ground to walk on and draws the ice layer.

The views of §3 to §8 and §15 were rendered again with these changes.

### 17.3 The six shots

The hosted game at 1600 × 900 on the RTX 2070 Super, 10 o'clock local time, clear sky (`--clouds 0`). The mountain's
place was picked again after its first shot faced a hillside, and the first plain shot stood in a fern (§17.5):

```
dotnet run -c Release --project tools/Hollow.GeneratorReport -- walking shots    # the places
$GODOT --path game --resolution 1600x900 -- --view ground --name Ada --host --ground walking --spawn origin --hour 10 \
  --clouds 0 --lat <lat> --lon <lon> --yaw <deg> --pitch <deg> --shot ../docs/images/scale/s4/ground/<name>.png
```

![A valley with a stream](images/scale/s4/ground/valley.png)

*A valley with a stream (28.85° S, 117.13° E): the stream in its channel, its bed of shingle and wet sand, savanna
grass and scattered pines and broadleaves; 86 fps.*

![A coast](images/scale/s4/ground/coast.png)

*A sandy coast (41.11° N, 87.07° E): the beach, wet sand at the swash, the sea to the left; 79 fps.*

![A cliff](images/scale/s4/ground/cliff.png)

*A tableland's escarpment, 85 m high, in sandstone (13.37° S, 110.52° W), seen from the wood's edge below it. Its face
is smooth until S4d puts the scanned cliff meshes along it; 69 fps.*

![A snowy mountain](images/scale/s4/ground/mountain.png)

*A peak of 286 m, −10 °C on top, seen from about 720 m away (62.57° S, 115.70° E), looking north into the morning sun:
snow lying on the boreal ground in front with grass through it, a lake to the right. On a ball of 2.4 km the ground
falls away 100 m in 700 m, so a peak shows only from where nothing nearer stands in the line of sight; the place was
picked for that.*

![A forest edge](images/scale/s4/ground/forest.png)

*A meadow at a wood's edge (40.86° N, 150.89° W): firs, pines, birches and broadleaves, the nearest tree 25 m away;
78 fps.*

![A plain](images/scale/s4/ground/plain.png)

*Open plains (42.06° S, 10.43° E), looking toward the highest ground in view: grass, flowers, berry bushes, a wood under
the mountain; 69 fps.*

### 17.4 Costs measured

| | |
|---|---|
| The sixteen layers into texture arrays | 1.0 to 3.4 s once a game, under the loading screen |
| Video memory of the layers | about 810 MB (BPTC, with mipmaps) |
| The layers' files in the repository | 912 MB (`game/assets/surface/ground/layers`) |
| Frame rate in the shots (1600 × 900, the game's own counter) | 69 to 89 fps |
| The hosted server's world props at start (loot spots, recyclers) | 8.7 to 11.3 s on the walking ground; for S4b |

### 17.5 Found while building

- **The game hung on the walking ground:** `ScatterNaturalObjects.Level` gave the planet scatter's level (16), not its
  own (5), so the client's plants asked for tiles at level 16. Fixed: its level, `InTile` and `TileOf` use the
  scatter's own level.
- **Clouds near the ground:** the sky's cloud layer is drawn over the ground a few metres from the camera on the small
  ball (a bank of white over the beach, a haze over a meadow). The shots use `--clouds 0`; S5 (air, clouds and sky at
  the new scale) owns it.
- **A player arriving in a fern** sees its fronds fill the screen: arrivals don't avoid plants (they have no collision).
  The shots' places allow for it; arrivals could skip plants in S4e.
- **Crinkled mountains in the close-ups:** the hill shading of the engine-free renders makes the ridged noise's
  metre-scale octaves on mountains look like crumpled foil (it did before S4c too); from the ground (the mountain shot)
  they read as rock and snow. Softer high octaves are a look-dev choice for the owner (`MountainDetail`, `RidgeCrest`).
- **Dashed lines at the waterline on flat beaches:** the swash's foam where the water is a few centimetres deep over
  the sand draws as short white dashes along the shore (the coast shot). To tune with S5's water.
- **Grass on the edge of ice and snow:** the grass field samples its density every eight lattice nodes and interpolates,
  so blades reach a few metres onto a frozen lake from its shore.

### 17.6 What is left

- **S4b, speed:** the slow fields on a lattice, shared collision heights, the scatter cache by size, the server's world
  props (10 s at start), then S3b's load tests with 100 bots on the walking ground (§10).
- **S4d, cliffs and rocks:** the scanned cliff and rock meshes along escarpments, sea cliffs, canyon and gorge walls,
  with collision (§8).
- **S4e, live:** the default ground, the landing site, the wipe, the golden values, arrivals off lakes and plants, the
  nav grid on `WaterLevelAt` (§11).
- More layers than sixteen (a vertex channel more) and the rest of §8's sets.

### 17.7 Checks

By the owner's rule for this package (the weekly usage limit), the checks were cut to the minimum: the solution builds;
the test filters `Walking` (6 tests), `Architecture` (5), `CollisionMatchesRenderTiles` (the render and collision
tiles agree, 0.003 mm), `Sound` (5), `StandIn`, `Biome` and `Scatter` passed. Skipped: the full suite, the movement
check, the network, console and building tests, the 100-bot load tests on the walking ground, the frame-time
measurement with a crowd, the stand-in and real planet looked at in the game with the sixteen layers.

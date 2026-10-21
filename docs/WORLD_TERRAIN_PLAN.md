# World terrain plan: landforms, water, rock and cliffs, rebuilt

Status 20 October 2026: a proposal, waiting for the owner's approval. A second research pass has added concrete
recipes for erosion (§4.1), the rock kit (§4.3), ground shading (§4.5) and water (§4.6). It replaces the cliff work of
T2 in
[TERRAIN_SCENERY_PLAN.md](TERRAIN_SCENERY_PLAN.md) and changes how the first world's ground is made
([WALKING_GROUND.md](WALKING_GROUND.md) §3–5). Nothing in it is built yet.

## The owner's decisions, to approve or change

1. **Direction.** The ground keeps to runnable slopes (about 45° at most), and every steep rock face is a placed rock
   model, as in Rust, Unreal's PCG demos and every documented game. The world's shape is baked once per planet, with
   erosion and a river network, and detail is added at run time. The noise walls and the wall skins go.
2. **Cliffs from a kit.** Cliffs are chains of modular rock pieces placed on steep ground, with the ground fitted
   under each piece (Rust's way), rather than meshes generated from the ground (Far Cry 5's way). The pieces are built
   in Blender from the scans the owner already has. Far Cry 5's way stays a fallback for long canyon walls.
3. **A new first world.** Its shape changes completely, so dev saves made on the current ground are dropped (a new
   save revision).
4. **Fast iterations checked by the owner.** No tests and no screenshots until the owner approves a step's look. Each
   iteration says how to launch it and where to look. Polish, tests, documentation and optimisation come after.
5. **One step in parallel.** Building the cliff kit in Blender (iteration 4) touches only art files, so it can run in
   its own session while the ground is rebuilt.
6. **The ground's textures move to control maps.** Instead of 16 weights per vertex, each ground tile gets a control
   map (§4.5). This is the terrain-texture session's area, so iteration 3 is done with that session or handed to it.

## 1. Why the cliffs failed

- **The ground made walls.** `Escarpment` (`WalkingWorld.cs:412`) puts three quarters of a 52 m plateau rise into a
  band a few metres wide; rocky coasts climb 26 m within a few metres; canyons have walls of 2.6 over 1. Noise
  thresholds give walls that are vertical, even in height and never cut by water. No shipped game we found draws
  vertical rock with its heightfield.
- **Rock was added afterwards, to cover the walls.** About 6,700 placements of five unrelated scans and a generated
  skin, fitted to walls they were not made for. The coverage measure said 91–96% while most of the cliff showed no
  rock. It counted sight lines that met anything, the smooth skin included, at the steepest points only.
- **The cost was mostly hitches.** A collision tile over cliffs, built cold, takes up to 891 ms (123,522 triangles).
  The client's prediction and the server's tick build it on their own thread when prefetching falls behind
  (`Predictor.cs:255`, `PhysicsWorld.cs:170`). The cosmetic physics then builds it a third time as one concave shape
  on the main thread (`GroundCollision.cs:65`). The first 20 seconds also generate every scan's LODs on the main
  thread and build about 450 skins.
  - On the GPU: the near pieces have no LOD chain, are double-sided and cast into every shadow cascade to 340 m.
  - The far layer draws whole 236 m chunks at one LOD.
- **The work was judged by numbers and pictures I made, not by the owner's eye.**

## 2. What the industry does

The same picture comes from every source. The world is built in layers, large to small: shape → erosion and
drainage → masks → ground materials → placed rock → scatter → roads and monuments → detail. No documented game draws
vertical rock with its terrain heightfield.

- **Rust** (Facepunch; devblogs and two public decompiles):
  - **Order of generation:** height, biome, splat, topology → monuments → roads, rails, rivers → hydraulic erosion →
    cliff splat and topology → PlaceCliffs and decor → dungeons ([order](https://github.com/GirthiusMaximus/a_a2/blob/main/docs/PROCGEN_RESEARCH.md)).
  - **Faces:** the heightmap only supplies slope; every face is a placed model
    ([devblog 54](https://rust.facepunch.com/news/devblog-54)).
  - **Rock splat:** ground of 30–50° gets rock splat, so the ground between pieces reads as rock.
  - **Topology bands:** cliff (spawns nothing), cliffside (ore), riverside, beach and roadside, and others, which
    drive what spawns where ([topology](https://wiki.facepunch.com/rust/Topology)).
  - **PlaceCliffs:**
    - It seeds on cliff topology, one try per 1,000 m², on slopes of at least 10°.
    - Each seed grows a chain of up to about 8 pieces joined at socket markers, turning at most 160° in all, with end
      caps.
    - Anchor and check points drop pieces that would float or bury.
  - **Fitting:** pieces carry terrain stamps (height, splat, topology, holes).
  - **The kit:** 26 cliff models (8 coastal, 10 hills, 8 jutting), plus about 15 coastal rocks, 10 large formations
    and 5 hand-made mountains ([prefab list](https://www.corrosionhour.com/rust-prefab-list/)), all remade in 2024
    ([World Update 2.0](https://rust.facepunch.com/news/world_update_2)).
  - **Look:** a shared tiling material tinted by biome, grass on the tops, and a base blended into the ground with a
    height limit ([devblog 62](https://rust.facepunch.com/news/devblog-62)).
  - **Rivers:** paths with width, padding, fade, depth, splat and topology, widening with length
    ([map data](https://wiki.facepunch.com/rust/Map_Data)).
- **Unreal PCG** (Electric Dreams, Valley of the Ancient):
  - **Assemblies:** artists place assemblies, hand-composed groups of rock pieces kept as data, rather than single
    rocks. The big cliffs are assemblies set along splines
    ([Electric Dreams](https://dev.epicgames.com/documentation/en-us/unreal-engine/procedural-content-generation-in-electric-dreams)).
  - **Placement chain:** sample by slope → filter by density → bounds → prune overlaps from largest to smallest →
    subtract exclusions → spawn
    ([nodes](https://dev.epicgames.com/documentation/en-us/unreal-engine/procedural-content-generation-framework-node-reference-in-unreal-engine)).
  - **Landscape patches:** placed things edit the heights and layers under them
    ([patches](https://dev.epicgames.com/documentation/unreal-engine/landscape-patch-system)).
  - **Blending:** rock bases fade into the ground's colour.
  - **Without Nanite:** 4 LODs at 50% each, HLOD and simple collision.
- **Far Cry 5:**
  - Houdini turns steep terrain into cliff meshes with strata, one mesh with collision per 64 m sector, drawn with the
    terrain's own shader. The cliff colour is written onto the ground beneath for distance
    ([notes on the GDC talk](https://christianjmills.com/posts/procedural-tools-far-cry-5-notes/)).
  - Its predecessors had no cliff technology and avoided large cliffs.
- **Horizon Zero Dawn:** placement is deterministic, on the GPU, from artists' rules. Cliff faces built from thousands
  of rocks became a performance bottleneck and were rebuilt as low-poly shells
  ([80.lv](https://80.lv/articles/horizon-zero-dawn-interview-with-the-team)).
- **Ghost Recon Wildlands:**
  - Terrain eroded in World Machine; rivers routed strictly downhill, roads by a pathfinder costing slope and turns,
    both stamped as heights, materials and masks.
  - Materials from slope, height, flow, deposition and curvature; rocks scattered by cliff and slope detection
    ([80.lv](https://80.lv/articles/procedural-world-building-in-ghost-recon-wildlands/)).
- **Planets and finite worlds:**
  - Rust generates its 4.5 km map once per seed and caches it. Star Citizen and Space Engineers bake eroded
    heightmaps. Elite blends terrain types by planet-scale masks.
  - Minecraft 1.18 maps smooth noises (continentalness, erosion, peaks and valleys) through hand-tuned curves: flat
    stretches make plateaus, steep ones cliffs, the lowest points rivers
    ([world generation](https://minecraft.wiki/w/World_generation)).
- **Erosion and rivers:**
  - **Stream-power erosion** (FastScape) with uplift gives branching valleys that drain
    ([Cordonnier 2016](https://diglib.eg.org/handle/10.1111/cgf12820),
    [code](https://github.com/H-Schott/StreamPowerErosion)).
  - **Thermal erosion** lays talus at the angle of repose.
  - **Lakes:** a priority flood fills them and finds their spills ([Barnes 2014](https://arxiv.org/abs/1511.04463)).
  - **Channels:** width grows with the square root of discharge; meanders are 7–12 widths long. The cut bank is on the
    outside of a bend, the sand and gravel point bar on the inside
    ([USGS](https://pubs.usgs.gov/publication/pp282B)).
  - **Caprock:** hard caprock over soft rock makes mesas, buttes, talus, and waterfalls that retreat into gorges.
  - **Erosion masks:** World Machine, Gaea and Houdini all output flow, wear and deposition masks, which drive
    materials and scatter.
- **Godot without Nanite:**
  - **Detail levels:** made at import, never at run time.
  - **Pieces:** tens of metres across, so each is culled and simplified on its own. Near ones are instanced
    automatically; far ones go in a MultiMesh per mesh per small cell (32–64 m) with visibility ranges.
  - **Horizon culling:** done per cell. From eye height the horizon is about 64 m on Earth, and a 30 m cliff's top
    stays visible to about 330 m.
  - **Shadows:** a tight distance, low-LOD shadow proxies.
  - **Collision:** decimated meshes or hulls, built off the main thread.
  - **Budget:** about 1,000–2,000 draw calls and 3–5 million triangles a frame over all passes, for 60 fps on a
    mid-range GPU (an estimate).

### Rust's cliffs, measured

A public 4.25 km custom Rust map (18.06 km², Earth's area to within a percent), decoded on 3 October from its viewer
export (`D:\GameDev\Assets\RustMapsResearch\2026-10-03`, outside the repository; numbers only). Its terrain grid is
8.3 m, so slopes are smoothed. It is a custom map, so its placement may differ in places from the generator's.

| | Hill cliffs, mid | Hill cliffs, large | Tall cliffs |
|---|---:|---:|---:|
| Distinct placements | 2,879 | 1,655 | 287 |
| Models | 4 | 6 | 4 |
| Scale, 10th–90th percentile (uniform) | 0.78–1.06 | 0.77–1.07 | 0.44–1.00 |
| Origin below the ground, median | 3.0 m | 5.3 m | 44 m |
| Ground slope under them, median | 19° | 22° | 6° |
| Up axis off the ground's normal, median | 5° | 5° | upright |
| Face turned downhill | 93% | 93% | – |
| Nearest other cliff, median | 13 m | 18 m | 19 m |

- Pieces stand about 10–12 m apart along a slope and step down it every 7–10 m.
- Ground of 15–40° holds a cliff piece in 20–36% of its 15 m cells.
- The ground rises about 18 m within 60 m of a typical piece, against our walls' 40 m.
- The map also has about 29,800 placed rock formations on flatter ground.

## 3. The world we want

What a believable world needs, grouped by size, with how the industry makes each and where ours stands. **Made with:**
H heightfield shaping, E erosion and its masks, M placed models, S splines or paths, D decals, Sc scatter.

| Feature | Made with | Ours now |
|---|---|---|
| **Large landforms** | | |
| Mountains: ridges, spurs, peaks, saddles, bowls | H, E | Ridged noise; no erosion |
| Rolling hills | H, E | Eroded-looking noise |
| Plateaus, escarpments, mesas, buttes, layered strata | H with hard caprock, E talus, M cliff bands | Noise walls |
| Valleys: V-shaped, wide U-shaped, floodplains | E downcutting and deposition | Carved profiles along streams |
| Canyons, gorges, ravines, gullies | E, H, M walls | Canyon walls 2.6:1 on tablelands |
| Plains and basins | H | Plains with kettle hollows |
| Coasts: beaches, sea cliffs, headlands, bays, coves, stacks, river mouths, dunes | H, M kits, E | Sandy or rocky; rocky is a 26 m wall |
| Islands | H | From the landmass noise |
| **Water** | | |
| River network: sources, tributaries, widening downstream, meanders | S from a drainage graph, H carving | Drainage on a 30 m grid; curves smoothed |
| Riverbeds: channel below the banks, gravel or sand, pools, rapids, fords | S cross-section, Sc, D | A parabola 6–32 m wide |
| Banks: rocky cut banks, sandy point bars, floodplain, terraces | S, M rocks, splat | Missing |
| Waterfalls and cascades, plunge pools | S steps where rivers cross hard rock, M | Missing |
| Lakes and ponds with beach or reed shores | Priority flood, splat, Sc | Lakes in hollows; shores plain |
| Wetlands: marsh, swamp, bog | Masks, splat, Sc | Missing |
| Desert washes, oases | E, Sc | Missing |
| Estuaries and deltas | H, S | Rivers meet the sea at its level |
| **Rock** | | |
| Cliffs by setting: sea, river bank, plateau rim, mountain face, canyon wall | M kits on runnable ground | Scans and skins; to be replaced |
| Outcrops on grassy hills, tors | M micro-cliffs, Sc | Small outcrops in the scatter |
| Free-standing formations, boulders, hoodoos, arches | M, Sc | Boulders in the scatter |
| Talus, scree, boulder fields | E thermal masks, Sc, D | Missing |
| Caves and overhangs | M interiors, holes in the ground | Missing |
| **Ground surface** | | |
| Materials by slope, height, moisture, flow and curvature | Splat from masks | Slope, climate and biome; no flow or wear |
| Erosion marks: flow streaks, gullies, sediment fans | E masks, D | Missing |
| Loose stones, pebbles | Sc, D | Some in the scatter |
| Small relief: bumps, ruts, sand ripples | Detail noise, normal maps | Metre-scale noise |
| **Plants** | | |
| Biomes with soft edges | Masks, noise | Biome table; soft edges |
| Forest structure: cores, clearings, edges, lone trees | Sc rules | Woods by relief and patches |
| River plants: reeds, bank trees | Sc by distance to water | Missing |
| Ground cover by biome: grass, flowers, ferns, shrubs, cacti, palms | Sc | Grass and flora (the grass session) |
| **Human-made and gameplay** | | |
| Roads and trails following the land, bridges, tunnels | S pathfinder, H stamp, M | Missing |
| Monuments on blended pads | H stamp, M | Flattened pads |
| Flat land for building, beaches for respawning | H, masks | Beaches on sandy coasts |
| Resource nodes and animal habitats by terrain | Sc by masks | By biome and relief |
| **Distance and movement** | | |
| Silhouettes at distance: ridgelines, tree lines, cliffs | LOD, horizon culling | Far layer to be replaced |
| Haze and weather | Renderer | Sky and air exist |
| Water look: shallow colour, foam, flow direction | Shader | Lakes and streams drawn |
| Walkable slopes, barriers, passes onto plateaus, fords, no traps | H rules, checks | Walls trap and block |

## 4. How it will work

### 4.1 The planet's bake (engine-free, deterministic)

Once per planet and generator version, the server bakes the large shape. Clients do the same and compare a hash. The
result is cached beside the save. Earth's bake can also be checked in, compressed, so the game starts at once.

1. **Grid.**
   - One graph over all six faces of the cube sphere: 8 neighbours (7 at the cube's corners), with true distances and
     cell areas, since areas vary about 1.4× across a face (`CubeSphere.cs`).
   - About 7 m to begin with (6 × 256²), then about 4 m (6 × 512²) by upsampling and a short second pass.
   - Each face keeps a two-cell border from its neighbours, so sampling is seamless across the cube's edges.
2. **Fields.**
   - **Sea and coasts:** today's landmass and coast fields.
   - **Uplift**, from today's relief provinces and the uplift field: 0 at the sea, 10–30 m per time unit in hill
     country, 60–100 in mountain ranges. Calibrated so hills reach about 40 m and peaks about 400 m, as now.
   - **Rock hardness:**
     - soft rock erodes at K = 1;
     - on tablelands, caprock bands 10–30 m thick, warped by noise and measured in rock height so they follow the
       strata, erode at K = 0.1–0.2. Erosion then makes mesas, buttes and escarpments that retreat, with talus below.
   - **Critical slope for talus:** about 33° on soft rock (varied ×0.85–1.25 by noise), 70° on caprock.
3. **Erosion: the stream-power law**, solved implicitly in FastScape's manner (Braun & Willett 2013), with m = 0.5 and
   n = 1, so A^m is an exact square root. Each step:
   1. Apply uplift.
   2. A priority flood (+ε) gives each cell its water level; lakes are not eroded.
   3. D8 receivers on that level. Ties among slopes within 10% of the steepest are broken by a hash, so channels do
      not run along the grid's axes.
   4. The stack (each cell after its receiver) and the catchment A, summed in reverse order.
   5. The implicit update h ← (h + U·dt + F·h_r)/(1 + F), with F = K·dt·√A/L.
   6. Four thermal sweeps in gather form (each cell reads its neighbours and writes only itself), each pair moving a
      sixteenth of the excess.
   7. In the last 30% of steps, deposition in the lowlands fills valley floors into floodplains.
4. **Steps and resolution:**
   - **7 m:** dt 0.25, 100–150 steps.
   - **4 m:** upsample, add 0.5–1.5 m of damped noise, then 30–50 steps.
   - Optionally, at 4 m, water droplets (about half a droplet per land cell) cut 1–3-cell gullies and alluvial fans.
     Stream power cannot make those.
   - Long thermal runs at one angle look melted. So the angle varies by rock and noise, channels are skipped, and the
     bake ends on stream-power steps.
5. **Finish.**
   - Channels are clamped to fall downstream, then flooded again; lakes are where the water stands more than 0.2 m
     over the ground.
   - **Rivers** are the cells with a large catchment: a graph with discharge, bed heights that only fall downstream,
     widths from the square root of discharge, and meanders. Waterfalls go where a river crosses caprock.
   - **Masks:**
     - flow (log of catchment, routed to several neighbours at exponent 1.1);
     - wear (rock removed) and deposition;
     - talus; lake; rock;
     - slope, curvature, distances to water and coast;
     - topology bands after Rust's: cliff, cliffside, riverbank, riverside, lakeside, beach, forest edge.

**Cost:**
- About 1.6 million cells at 4 m, and some tens of megabytes before compression.
- An estimated 0.5–1 minute on one thread, 3 minutes at worst; iteration 1 measures it.

**Determinism:**
- Doubles, `DMath`, and `Math.Sqrt`, which is correctly rounded.
- The stack, the flood and the sweeps run sequentially. Parallel passes only read old values and write their own cell.
- The flood's heap is keyed by (height, cell).

### 4.2 The ground at run time

One height function feeds collision and drawing alike: the baked height, smoothly sampled, plus 1–40 m of
eroded-looking detail. Operators, found by a spatial index, then act on it:
- river channels with a bend-aware cross-section (cut bank outside, bar inside);
- lake levels;
- monument pads;
- each cliff piece's stamp.

Nothing that changes where water drains happens at run time; anything that would (a large formation, a monument)
goes into the bake first.

### 4.3 Cliffs and rock

- **The kit.** 18–20 pieces to start, built in Blender from the owner's scans:
  - **Desert Western layered cliffs**, for tablelands, mesas and deserts: two straight XL pieces, a convex corner XL,
    two large pieces, two ledges, and left and right slope ends;
  - **Nordic coastal cliffs**, for coasts: three tall pieces and left and right slope ends;
  - **tundra formations**, for hills and mountains: four jutting pieces;
  - three or four rubble fillers.

  Concave turns come from rotated, overlapping pieces, and biome variants from tint rather than new pieces.
- **Each piece:**
  - **Shape and joints:**
    - cut along diagonal fractures at its ends, so pieces chain in any order and can be mirrored;
    - a pivot at the middle of its base line, with its face toward +X;
    - sockets 1–2 m inside each end, so neighbours overlap by 10–20%;
    - a skirt 3 m below the ground line; everything lower, and backs that are always buried, are deleted.
  - **Detail levels:**
    - LOD0 about 5,000 triangles for pieces up to 16 m, 10–15,000 for 20–40 m, 20,000 for the 49 m corner. A vertex
      group protects the top lip and the ends.
    - LOD1–3 at 50%, 25% and 12.5%, made in Blender: Godot's import LODs keep open scan rims at full density.
    - LOD2 is the shadow proxy; beyond about 250 m, a shell of 300–600 triangles.
  - **Collision:** a closed shell from a 0.25 m voxel remesh, decimated to 800–2,000 triangles (at most 1,000
    vertices), within 0.3 m of LOD0.
  - **Textures:**
    - its own albedo (with AO) and normal map, 2K for pieces of 30 m or more and 1K below, compressed BC7 and RGTC;
    - one shared 2K tiling detail set per family, mapped in world space by biplanar projection (700–1,000 px/m), so
      detail stays sharp at any scale. It blends in within about 30 m.

    Unique textures alone blur up close. A 2K map on a 40 m piece is sharp only beyond about 18 m.
  - **Masks:** vertex colours baked by script. Red is base dirt or wetness, green where moss may grow, blue picks the
    detail material.
- **Its material:**
  - moss, snow or grass where the surface faces the planet's radial up (dot above about 0.7), broken by the detail's
    height;
  - a biome tint per instance;
  - the lowest 0.5–1.5 m takes the ground's material through the rock's height map, with a noisy edge, so the base
    sits in the ground.
- **The pipeline:** one Blender script per family:
  1. Import the scan.
  2. Cut it into pieces.
  3. Set pivots and socket empties.
  4. Decimate the detail levels.
  5. Build the collision shell.
  6. Bake the vertex colours.
  7. Resize the textures.
  8. Export a GLB.

  Godot imports it with shadow meshes and GPU compression.
- **Placement.** Rust's PlaceCliffs, ported to the sphere:
  - seeds on cliff topology, slope measured against the radial up;
  - chains of up to about 8 pieces joined at their sockets, each facing downhill and tilted with the ground;
  - uniform scale from the kit's range;
  - anchor and check points that drop a piece rather than patch it.
- **Smaller rock.** Single micro-cliffs and outcrops on hills; formations and boulders on flatter ground; debris at
  cliff feet from the talus mask.
- **Stamps.** Each piece lowers or raises the ground under its footprint, paints rock and gravel round its base and
  marks topology, so nothing grows inside it. Where it overhangs, the ground beneath is pushed under the mesh.

### 4.4 Drawing and collision

- **Near pieces** are instances sharing mesh and material, which Godot batches. **Farther ones** go in a MultiMesh per
  mesh per LOD per 64 m cell, switching hard at set distances with margins.
- **Cells past the horizon are hidden.**
- **Shadows:** cast to about 200 m from the LOD2 proxies, and none beyond. Past about 250 m the far shells go in a
  MultiMesh per 64 m cell.
- **Collision:**
  - Each piece's collision mesh joins its ground tile's static body, built on workers and never on the main thread or
    the server's tick.
  - When a tile is not ready, movement waits for it under the loading rules rather than building it in line.
  - Cliffs leave the cosmetic physics.
- **Budget:** about 1,000–2,000 draw calls and 3–5 million triangles a frame over all passes at High; no main-thread
  task over 2 ms; no frame over 33 ms for streaming.

### 4.5 Ground materials and scatter

- **Control data per tile, about 1 m a texel:**
  - two material IDs and a blend, in Terrain3D's 32-bit layout;
  - a second map for wetness, tint, flow and AO.

  Today's shader blends 16 weights stored per vertex (`surface_ground_look.gdshaderinc`). The control map's fixed
  cost suits 34 layers better than weights do.
- **Scores from the masks** (each texel keeps the top two):
  - rock above 40° or where wear exceeds 0.5;
  - scree on talus (30–40°);
  - sand or silt where water deposits;
  - gravel along flow, at most 30%;
  - thin soil on convex ground, soil and moss in hollows;
  - beach within 12 m of the coast and under 2 m above the sea;
  - riverside within 4 m of a river, lakeside within 6 m of a lake;
  - snow by height and slope.

  Flow alone looks artificial, so it only adds.
- **Per pixel:**
  - four control taps, at most three materials, merged where IDs repeat;
  - height blending, w = b·(b + blend + h)^k, sharp near (k 24) and softer by 150 m (k 4) so edges don't shimmer;
  - two texture fetches per material (albedo with height; normal, roughness and AO).
- **Breaking the tiling:**
  - hex tiling (Mikkelsen 2022) within 50 m;
  - from 40 m, a second sample four times larger blends in, complete by 120 m;
  - beyond 400 m, a far colour baked per tile;
  - a macro tint at about 200 m and 30 m (±12%).
- **Steep ground, rock only:** from 45° to 65°, one projection plane snapped to eight directions (no extra fetches);
  above 65°, biplanar mapping.
- **Budget:** about 16 texture fetches a pixel, 32 at worst; 8× anisotropy; 2K layers (about 190 MB per array against
  725 MB at 4K); at most 1.5 ms at 1440p.
- **The scatter follows the topology bands:** reeds in river shallows, willows on banks, debris and ore at cliff
  feet, driftwood on beaches, small trees at forest edges.
- **Ownership:** the ground's materials belong to the terrain-texture session and the grass to the grass session.
  This plan gives them the masks; the control map is planned with the texture session.

### 4.6 Water

- **One water surface** for the sea, lakes and rivers. Today's water level per terrain vertex (`TileGrid.WaterLevels`)
  gains a flow direction, a speed and turbulence in a spare vertex channel, so confluences and river mouths blend by
  themselves. Unreal's water is one mesh in the same way.
- **Baked per river node:** surface height, speed from slope, flow direction, turbulence. A tributary or a river mouth
  ends at the level of the water it joins, and its flow fades over one or two river widths.
- **Shading:**
  - **Flowing water:** Valve's flow mapping (Portal 2), two phases half a cycle apart with a noisy phase, with normal
    strength and tiling following speed; lakes get two drifting layers.
  - **Colour and foam:** today's colour by depth, plus refraction offset by the normal. Foam comes from depth
    differences (rocks, legs, hulls) and from turbulence.
  - **Reflections:** sky and reflection probes, with an optional short screen-space march on calm water near the
    camera; no planar reflections.
  - **Cost:** estimated under 0.5 ms at 1080p without the march.
- **Rapids and falls.**
  - Rapids are steps in the bake, with foam from slope.
  - A waterfall is a sheet mesh along the arc from its lip, with two layers scrolling at different speeds. Its pool
    gets radial flow and foam, and its mist is a few large soft cards with spray.
- **Banks and beds:**
  - a wet band above the water level;
  - gravel where water runs fast, sand on inner bends and beaches, silt in lakes;
  - caustics under the water.

  Plants: reeds in 0–0.6 m of slow water, willows on the banks, nothing in rapids.
- **Swimming and boats:** `WaterAt(p)` (level, flow, depth, kind) replaces `WaterLevelAt`.
  - Swimmers drift with the current.
  - Boats float on four Jolt buoyancy points (`ApplyBuoyancyImpulse`, which the vendored JoltPhysicsSharp binds).
  - No water colliders.

## 5. Iterations

Each iteration ends with a build that runs, how to launch it, where to look and what to judge. The owner checks it in
the game; I change it until the owner is satisfied, then the next one starts.

| # | What changes | How the owner checks it |
|---|---|---|
| 1. **The shape** | The bake of §4.1 at 7 m: caprock, stream-power and thermal erosion, deposition, lakes. The noise walls and the current cliffs are off; ground drawn with today's textures. Its run time measured. | Fly over Earth (hosted game, noclip, teleports to listed places): ridges, valleys, plateaus with eroded edges, mesas, talus, coasts. |
| 2. **The water** | §4.6: the river graph, channels with cut banks and point bars, one water surface with flow, rapids, waterfalls, lakes. | Follow a river from its source to the sea; walk its banks; cross at a ford; swim with the current. |
| 3. **The ground's layers** | §4.5: control maps from the masks (rock, scree, gravel beds, sand bars, deposits, snow), hex tiling, far colour. With the texture session. | Near and far at the same places: does the ground read as what it is? |
| 4. **The cliff kit** (own session, in parallel) | §4.3: 18–20 pieces from the scans, with authored LODs, collision shells, sockets, masks and the rock material. | The model viewer, and the pieces in a test row on flat ground. |
| 5. **Cliffs placed** | Chains on cliff topology, micro-cliffs and outcrops, formations, stamps. | The same places: plateau rims, river banks, coasts, hills. |
| 6. **Cost** | Instancing per cell, LOD distances, horizon culling, shadows, collision off the main thread. | Frame rate and hitches at the same places, and walking and flying fast. |
| 7. **Life** | Scatter by topology bands; biome edges. | Riverbanks, cliff feet, beaches, forest edges. |
| 8. **People's marks** | Roads and trails with bridges, monument pads blended, respawn beaches checked. | Drive and walk the roads. |

Later, by need: caves and overhangs, wetlands, desert washes and dunes, roads with tunnels, the other planets
(TERRAIN_SCENERY_PLAN T6).

**After approval of the look (polish):** tests for determinism, drainage and seams; cost measured per pass; captures;
the docs (WALKING_GROUND.md, GROUND.md, CODE_LAYOUT.md); save revisions and protocol bumps.

## 6. What goes and what stays

- **Goes:**
  - the wall skins (`CliffWalls.cs`, `CliffField.Walls.cs`, `cliff_skin.gdshader`);
  - the face fitting in `CliffPlacements`;
  - the far layer as built, and the runtime LOD generation;
  - cliffs in the cosmetic collision;
  - `Escarpment`'s wall band and the rocky coast's wall;
  - the five-scan set and its stray imports;
  - the dormant Formations module (about 8,000 lines), unless its streaming budget is wanted.
- **Stays and is reused:**
  - the cube-sphere tile math, the cell caches and the nearest-first background sweep;
  - the horizon test and per-cell batching;
  - today's priority flood and catchments, moved into the bake;
  - the scatter and its field;
  - monument flattening;
  - the prefetching of collision;
  - the performance toggles of the route, and the survey tools, rewritten for the new masks.

## 7. Working with the other sessions

The grass session owns the grass, and the terrain-texture session owns the ground's materials and `GroundLayers`.
Iteration 3 is planned with the texture session before it starts. Builds and Godot runs take turns as before.
Iteration 4 runs in its own session on art files only.

## 7a. Where it stands (20 October 2026, end of the first session)

- **Done, checked by the owner:**
  - iteration 1, Earth's shape baked and eroded (30fedf03, `TerrainBake`);
  - the cliff kit (f58b0207, `art/cliffkit`, 21 pieces);
  - iteration 5, the kit placed on Earth (6a1199c1: `CliffKitPlacements`, `CliffKitField`, with the ground fitted behind the walls).

  The owner liked the cliffs but not fully. They put that down to Earth's poor layout, and noted that none of the
  project's 57 built monuments (12 medium, 10 large, 8 landmarks in `game/data/monuments`) stand on Earth: only 6
  roadside ones from `src/Hollow.Surface/Data/monuments.json` are placed.
- **The order now, as the owner chose** (Rust's generator places monuments before rivers and cliffs):
  1. **Candidate layouts.** `earth layouts <folder> [--variants 0-11] [--width 1600]`, written on 20 October:
     - Earth with `LayoutVariant` 0–11 (5 is today's), each baked, with the kit placed;
     - an equal-area relief map each (`layout-NN.png`): cliff pieces as dark red dots, flat sites from
       `TerrainBake.FlatSites(0.09, 2)` as circles, magenta from a radius of 150 m and yellow from 75 m, the landing
       site as a white cross;
     - a numbered contact sheet, and `layouts.txt` and `layouts.json`: land share, relief shares, highest ground, lakes,
       cliff pieces, flat sites, the landing site.

     All twelve take 76 s, three at a time (15 s once their bakes are cached). The owner picks one, or describes the
     Earth they want. Layout 6, the one I recommended, went into the game the same day (revision 12) for the owner to
     fly before choosing. `earth bake` lists its places worth a look, flat sites included, as F1 teleports.
  2. **Monument sites.** About 2 large, 5 medium and 10 small, plus roadside ones, on the flattest sites. Their pads
     are flattened in the bake before erosion, so drainage and cliffs respect them.
  3. **Rivers.** The design is ready but no code is written:
     - a river node per bake cell draining 12 ha or more, from D8 receivers on the last flood, with the water level
       falling downstream, half-width 1 + 2.2·√(A/10⁵) m, depth 0.5 + 0.35·√(A/10⁵) m and a signed bend;
     - the network cached in the bake (raise `TerrainBake.Version`);
     - channels carved at run time in `WalkingWorld`'s height: a steep cut bank outside a bend, a gentle point bar
       inside, a lip that keeps the water in;
     - the water given through `WalkingSample.Water`, as lakes are;
     - kit cliffs kept out of rivers.
  4. **Cliffs again** on the final ground. To fix:
     - slivers of bare ground where the coarse far terrain cuts the fitted ground;
     - tiers kept 24 m apart;
     - `KitLeanBack` and the seed slopes.
  5. **Roads** between monuments.
- **Found in the owner's playtest of 20 October, not yet fixed:**
  - **Cliffs cost the most in collision; fixed in part the same day.** At the busiest cliff sites one 64 m collision
    tile carried 280,000–436,000 triangles, 97% of them the kit's shells; the bare ground is about 9,800.
    `CliffKitPlacements.AddCollision` added the whole shell of every piece within the tile's reach plus 60 m: 139–222
    pieces, of which 65–89 reach the tile. The client's cosmetic ground (`GroundCollision.cs`) then made each tile a
    concave shape on the main thread.
    - Now a tile takes only the pieces whose footprint reaches it, and of each only the triangles over the tile (or
      within its 2 m margin): 75,000–100,000 triangles at those sites.
    - The cosmetic ground no longer copies shells into tiles. It makes each piece's shape once and places it on the
      tile its pivot stands over (`IInstancedSolids`), so its tiles hold the bare ground alone.
    - Still to do in step 6: the server's and the prediction's tiles keep the cut shells, which is 7–10 times the
      ground. Fewer pieces (step 4) and instanced shapes in Jolt would bring them down. `earth kit` prints these
      numbers.
  - **The cliffs read as scattered assets.** Chains stand on every slope over 32°, ledges are stacked in rows round
    mountains, and gaps are left between them. Each piece also shows its own scan's textures, so neighbours don't
    match. For step 4.
  - **Beaches are bare:** no driftwood, boulders, pebbles or wrack. The owned scans include a Nordic beach boulder,
    coast rocks and coast lines, and dead logs that could be bleached into driftwood.
  - **The ground's textures tile visibly** on even ground and from the air. The shader blends two or three scales of
    the same scan, so the repeats still show as squares; iteration 3's anti-tiling is not built.
  - **White pans by the beaches.** About 19 ha of sea is under 18 cm deep, and the water shader fills it with its
    shore foam, a texture that repeats every 3.1 m.
  - **The sea's edge never moves.** There is no run-up or backwash, and the shore foam drifts at about 6 cm/s.
- **Uncommitted when the session ended:** nothing. Earth's sea is three times deeper (`SeaDepth` 3, floor at most
  300 m), at the owner's word that it was far too shallow; Earth's bake re-runs once (about 13 s).
- **Way of working:** fast iterations the owner checks by hand; no tests or screenshot runs until they approve a look.
  Every change to Earth's shape, its parameters or its solids raises `WalkingParameters.Earth.TerrainRevision` in the
  same commit. Otherwise the owner's hosted save, which records the parameters' fingerprint, refuses to start. The
  deeper sea of 20 October did exactly that (now revision 11). Then polish:
  - tests, including `TerrainSaveTests` and the cliff tests;
  - docs;
  - save revisions;
  - the protocol bump.

  Commit by explicit path: other sessions share the checkout.

## 8. What the research could not settle

- **Times are estimates:** the bake's run time and the ground shader's cost are not yet measured. Iterations 1 and 3
  measure them.
- **Rust's rock textures:** the claim that they shrank from 1.1 GB to 120 MB has no primary source. The kit's texture
  plan (unique textures for shape and colour, a shared tiling detail) rests instead on documented practice at Naughty
  Dog and in Ghost Recon Wildlands.
- **Unread primary sources:**
  - Cordonnier 2016's paper and Far Cry 5's slides were too large to fetch, so their settings come from notes and
    later papers.
  - The Witcher 3's terrain talk is members-only.
  - Red Dead Redemption 2 and Horizon Forbidden West have published nothing on their terrain.
- **Rust's code:** some Rust facts come from third-party decompiles of its code.

Second-pass sources:
- **Erosion:**
  - [FastScape's stream-power solver](https://github.com/fastscape-lem/fastscapelib-fortran/blob/master/src/StreamPowerLaw.f90);
  - [landlab's lithology layers](https://landlab.readthedocs.io/en/latest/tutorials/lithology/lithology_and_litholayers.html);
  - [Schott 2023](https://github.com/H-Schott/StreamPowerErosion) and
    [2024](https://github.com/H-Schott/MultiScaleErosion);
  - [a 2022 comparison of costs](https://arxiv.org/html/2210.14496v1);
  - [Gaea's erosion](https://docs.quadspinner.com/Reference/Erosion/Erosion.html);
  - [World Machine's thermal erosion](https://help.world-machine.com/topic/device-thermalerosion/).
- **The rock kit:**
  - [Ubisoft's modular photogrammetry](https://80.lv/articles/modular-photogrammetry-in-environment-design/);
  - [Ghost Recon Wildlands' materials](https://80.lv/articles/landscape-and-material-pipeline-of-ghost-recon-wildlands/);
  - [Godot's import LODs](https://docs.godotengine.org/en/latest/tutorials/3d/mesh_lod.html);
  - [detail normal blending](https://blog.selfshadow.com/publications/blending-in-detail/);
  - [biplanar mapping](https://iquilezles.org/articles/biplanar/).
- **Ground shading:**
  - [Call of Duty's terrain (2023)](https://advances.realtimerendering.com/s2023/Etienne(ATVI)-Large%20Scale%20Terrain%20Rendering%20with%20notes%20(Advances%202023).pdf);
  - [notes on Far Cry 5's terrain](https://wolfand11.github.io/blogs/graphics/TerrainRenderingInFarCry5.html);
  - [Terrain3D's control map](https://terrain3d.readthedocs.io/en/stable/docs/controlmap_format.html);
  - [hex tiling](https://jcgt.org/published/0011/03/05/paper-lowres.pdf).
- **Water:**
  - [Valve's flow maps](https://www.advances.realtimerendering.com/s2010/Vlachos-Waterflow%28SIGGRAPH%202010%20Advanced%20RealTime%20Rendering%20Course%29.pdf);
  - [Unreal's water meshing](https://dev.epicgames.com/documentation/en-us/unreal-engine/water-meshing-system-and-surface-rendering-in-unreal-engine);
  - [Horizon's rivers](https://80.lv/articles/horizon-zero-dawn-procedural-rivers-wires/);
  - [waterfall tricks](https://simonschreibt.de/gat/jedi-fallen-order-splishy-splashy/);
  - [wet surfaces](https://seblagarde.wordpress.com/2013/04/14/water-drop-3b-physically-based-wet-surfaces/).

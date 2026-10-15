# One zone, the whole world (S3)

Status: **approved by the owner on 14 October 2026; being built.** This is package S3 of [SCALE.md](SCALE.md) §6. The
owner said go on 14 October 2026, after asking why the map still shows a 3.9 km square without proper zoom (finding 1 of
[KNOWN_PROBLEMS.md](KNOWN_PROBLEMS.md)), and approved the twelve defaults of §2 the same evening, so they are
decisions. The code is built on the branch `whole-world` (worktree `D:\GameDev\Projects\Hollow-wt\whole-world`).
S3a and S3b are built (14 and 15 October 2026; how they differ from this design is at the end of §14); S3c and S3d
remain.

Four read-only surveys of the code (14 October 2026: the simulation, the network and saves, the world and the animals,
the Godot client) found every place that assumes the zone is a flat patch. This design is built on them.

---

## 1. What S3 does

Today a zone is laid out on a plane that touches the ground at the landing site of a 6,435 km planet: +X east, +Y up,
+Z south, origin on the ground. "Up" and gravity are already radial almost everywhere, but the ground under a point,
the physics tiles, every facing, the spatial indexes, the map and everything placed "around the origin" are measured
on that plane. On a 2.41 km world the plane reaches only the near half of the ball and breaks 90° from the landing
site. S3 makes one zone hold a whole world:

- **The zone's frame is the world's own frame:** origin at the centre, the body's axes (+Y towards the north pole,
  longitude counted eastward from +X). Positions are metres from the centre, and up is the direction from the centre.
- **Everything spatial belongs to the ball:** the ground, physics tiles, nature, props, animals, relevance, building,
  ships and the map.
- **A facing is a direction, not an angle,** so nothing has a pole or a seam (§5).
- **The map is the whole world:** a globe that becomes a flat chart as you zoom in (§11).

What S3 does not do:

| Not in S3 | Where |
|---|---|
| The real ground at walking scale | S4, a design with look-dev renders first |
| The air, clouds and sky at the new scale: the thin shell seen from space, clouds seen from above, light at the ball's edge | S5. S3 only does the minimum so the ball is drawn right (§10.2) |
| `GameScale`, the radius from a body's real size | S1. Until it lands S3 uses 2.41 km as a constant |
| Several worlds in one server (the zone host) | Deferred by the owner on 14 October 2026 |

**S3 runs on a stand-in world (§4).** The real `PlanetSurface` cannot come along. It is built at the real 6,435 km
radius, where single-precision positions measured from the centre move in half-metre steps, and its land is taller than
a 2.41 km world (SCALE.md §3.3). So `main` keeps the real first world until S4 gives the ball its ground, and S3 stays
on its branch until the owner says otherwise (§14).

---

## 2. Decisions (approved by the owner, 14 October 2026)

| # | Question | Decision | Alternative not taken |
|---|---|---|---|
| 1 | The development world's saves | Wiped once (schema 7): bases, things, ships, overrides, markers. Accounts and teams stay; characters start a new life | None that works: positions on a 6,435 km plane have no place on a 2.41 km ball |
| 2 | Facing | A view direction, carried along the ground as you move (§5) | A compass heading from the north pole: simpler, but it spins and flips at the poles |
| 3 | Gravity | Towards the centre for everything: ragdolls, gibs, effects and animals too | None |
| 4 | Leaving the world | 2 world radii above the ground, 4.8 km on the first world (SCALE.md default 4) | Another multiple |
| 5 | The map | One map for the G screen and the death screen: a globe that becomes a flat chart as you zoom in, from the whole world down to about 150 m across | Two screens: a globe, and a flat projection of the whole world |
| 6 | The map's grid | 6 faces × 25 × 25 cells of about 150 m, named by face and by Rust's cell: "C K12" | A latitude and longitude graticule, whose cells shrink towards the poles |
| 7 | The stand-in during S3 | A sea, a few continents and climate bands, so beaches, coasts, swimming, biomes and the map have something to work on | The bare stand-in: 12 m hills, no sea, one biome |
| 8 | Where players arrive | A random beach anywhere on the world | Beaches within one region |
| 9 | Recyclers | Six sites, one per cube face, each on its face's best coastal spot | One near each arrival beach |
| 10 | Animals | Today's rule on the ball: herds come out around players anywhere, sleep beyond 340 m and are removed after 30 s with nobody near | A population that lives on without players, as in Rust: later |
| 11 | Time of day | One clock for the world. Each player's hour is the hour where they stand; `time <hour>` makes it that hour where the admin stands | None |
| 12 | Monuments in the world (M3) | Wait for S4's ground | Place them on the stand-in |

---

## 3. The frame and its lengths

### 3.1 The world frame

- `ZoneFrame.World`: origin at the centre, the body's own axes. `ToLocal` and `ToBody` still convert between doubles and
  floats. `UpAt(p)` is p made unit length; it is zero at the centre, where nothing may stand, so noclip stops 500 m
  below the mean radius.
- Today's tangent frame (`ZoneFrame.At(latitude, longitude)`) stays as a tool for the surface look-dev view and the
  generator report on today's big world. The zone never uses it.
- **Precision.** Everything stays within about 7.3 km of the centre (a ship at the leaving height). A float there moves
  in steps of 0.24 mm at the ground and 0.5 mm at the ceiling, as fine as today's ±8.2 km zone. Jolt stays single
  precision, and nothing needs a floating origin.
- **Local axes, for readouts only:** east is the pole axis crossed with up, north is up crossed with east, and within a
  metre of a pole east falls back to −Z, as `ZoneFrame` does today. No rule of the game depends on them; they give the
  compass and the map's north.

### 3.2 Lengths by size, not by level number

Cube-sphere tiles are chosen today by level number: level 16 is 154 m on a 6,435 km world and 6 cm on a 2.41 km one.
One helper, `CubeSphere.LevelFor(radius, metres)`, gives the level whose tile edge (`TileKey.EdgeLength`, π/2 × R ÷
2^level) is nearest a length. It uses only + − × ÷, so the server and every client get the same answer. Every tiled
system asks it:

| Use | Today | On the first world at 2.41 km |
|---|---|---|
| Physics ground tiles | 64 m squares of the plane | Level 6: 59 m, 24,576 tiles, which fit in today's 65,536 tile body IDs |
| Scatter: trees, bushes, rocks | Level 16, 154 m | Level 5, 118 m. Candidates are spaced in metres, not counted per tile edge, so densities don't change |
| The stand-in's nature | 64 m squares of the plane | Level 6, 59 m |
| Nav grids | 64 m squares of 2 m cells | Level 6, 30 × 30 cells of 1.97 m |
| Animal spawn areas | 256 m squares | Level 4, 237 m |
| Loot cells | 130 m cells within 1.6 km of the origin | Level 5, 118 m, over the whole world |
| The terrain's finest tiles (client) | Level 18, 39 m | Level 7, 30 m: 0.9 m between vertices, today's density |
| Grass cells (client) | 0.4 m cells of the plane | Level 13, 0.46 m |
| The map's grid | 150 m cells of a 3.9 km square | 25 × 25 cells a face, 151 m |
| Monument districts (M3, later) | Level 11, 4.9 km | A face, 3.8 km |

An ID that carries tile coordinates checks their range. An untrusted or old ID naming a tile outside its face is "not
found", never an exception; today `TileKey` throws, which a forged scatter ID could reach.

### 3.3 Distances and indexes in three dimensions

Every index keyed by X and Z becomes one 3D grid of 64 m cubes in the body frame, with straight-line distances:

- relevance (300 m in, 320 m out);
- the building's areas;
- natural-object overrides;
- deployables and the pieces' 4 m cells;
- the animals' neighbour grid;
- the server's walk over static things each snapshot.

A straight line is never longer than the way along the ground, and at 300 m the two differ by 0.05 %. Today's X/Z
distance over-includes on a ball: at a pole it takes in both polar caps. The extra spawns would use up the snapshot's
48-thing and 1,200-byte budgets.

---

## 4. The stand-in world (decision 7)

The stand-in is `SphereSurface` at 2,410 m, with three additions so the ball has something to test:

- **Continents:** low-frequency noise on the sphere, features about a kilometre across and ±20 m high, under today's
  12 m hills.
- **A sea:** its level is set so that 45 % of the world is under water, the first world's real cover. Today the
  stand-in has no sea (`SeaRadius` 0), which leaves arrivals, coastal loot, swimming, the nav grid's water and the map's
  sea untested; its map would be black (§12).
- **Climate bands** by latitude and height: Beach within 3 m of the sea, Ice beyond 70°, Tundra from 55°, Boreal forest
  from 40°, Temperate forest and Grassland below that. Trees come from `gather.json`'s stand-in density. Its clock gives
  local solar hours by longitude, so night is a place (SCALE.md §3.2). Today `StandInClimate` has one phase for
  everyone.

It is a test fixture and nothing more. S4 replaces it with the real generator at walking scale.

---

## 5. Facing (decision 2)

Today every facing is an angle with one meaning: 0 is the zone's −Z laid onto the local ground. That meaning lives in
one function, `CharacterMotor.HorizontalBasis`, used about 70 times in 28 files. With the frame at the centre, its
reference vanishes on the equator, and the southern hemisphere turns the wrong way.

**Characters.**
- The input carries a **view direction**, a unit vector in the body frame, instead of yaw and pitch.
  - On the wire it is octahedral, 2 × 16 bits: the same 32 bits as yaw and pitch today, at least as fine as today's
    16-bit angles (0.0055°).
  - Forward is the view laid onto the ground; pitch stays within ±89°, as now.
  - The view needs no reference, so it can't have a pole or a seam. The server and the client agree on it whatever
    their positions.
- **Carried on the client.** Each frame the camera's view is turned with the ground under it: the shortest rotation from
  the last up to the new one. Then the mouse turns it about up and tilts it. Walking straight ahead with the mouse still
  follows a great circle, round the world and over the poles.
- **Input, not state.** The view is part of the input, as yaw is today. `CharacterState` and its hash don't change, and
  prediction replays the inputs with their views.
- **Remote players and animals.** Snapshots carry their view (octahedral), and interpolation turns between views
  (slerp), never between angles. Today it mixes yaws measured in two positions' references.

**Things that stand still** (deployables, bags, sleeping bodies, corpses, natural objects' weak spots, loot props):
- They carry a forward direction, octahedral on the wire and exact in saves.
- Building pieces keep their 30° steps inside their base's frame, which is already exact.

**Ships** send their rotation as a quaternion (smallest three, about 47 bits). Today it is 48 bits of Euler angles about
the zone's +Y, which lock when a ship flies north or south along the equator.

**Shared code.**
- `Basis(forward, up)` replaces `HorizontalBasis(yaw, up)` everywhere: movement, hit shapes, the pose history, the
  boxes of props and deployables, placement, ships, nature and animals.
- The hand-written `Atan2(x, z)` inverses go too: building, rotations, the raid ring, the console.
- Compass readouts (the ship's heading, raid alerts' bearings, the map's arrow) measure from local north (§3.1). Within
  a metre of a pole they show no bearing.

**A base's heading** comes from the placing player's view. A ball has no world grid to snap to, so finding 4's fixed
15° grid goes with it.

---

## 6. The simulation on the ball

### 6.1 The ground

- `GroundProbe.Settle(p)` and `Normal(p)` become radial: the ground is found along the direction of p, and normals are
  taken in the local tangent basis. `HeightAbove`, `Crossing` and `InWater` already work that way. Every
  `GroundAt(x, z)` caller passes a point or a direction instead.
- **Physics ground tiles** are cube-sphere tiles at the level nearest 64 m (§3.2), keyed by `TileKey`, and a tile's body
  ID is its index.
  - Each character holds the tiles within 64 m and prefetches those within 96 m; ships prefetch along their path.
  - Tiles are found by sampling the local tangent plane, which handles face seams and cube corners.
  - Collision triangles overlap their neighbours by 2 m, as today.
- `ISurface.GroundCollision` takes a `TileKey` instead of a square of the plane. It has six implementations, four of
  them in tests.

### 6.2 Characters

- **Already radial:** up, gravity, stair steps, slopes, falls and swimming (`SeaRadius`).
- **Changes:**
  - the view (§5);
  - noclip is bounded by height: from 500 m below the mean radius to the leaving height;
  - a character restored from a save is checked by its height above the ground, not an X/Z box.

### 6.3 Weapons, blasts and the pose history

Bullets, arrows, thrown charges and rockets already fall towards the centre, and blasts already reach in 3D. Changes:
- nature is queried by a sphere instead of an X/Z box;
- a charge that slips under the ground settles radially;
- an explosive's default normal is up at its point (today +Y);
- clients' drawn arcs re-aim gravity as the server does. Today they draw one parabola, but on this ball gravity turns
  2.4° every 100 m.

The pose history keeps a forward with the feet.

### 6.4 Building and placement

A base keeps its first foundation's frame, as now; stability and claims are already local or 3D. Changes: the base's
heading (§5), 3D cells, and `GroundBelow` retries along the ground.

**Curvature shows.** Across a base the ground falls d²/2R below its plane: 0.93 m at 67 m from the first foundation.
Foundations are 1 m deep, so on level ground pieces further out than that are refused as floating. Finding 2's deeper
footings would extend it, and Rust's bases rarely span 60 m. The comment in `Structure.cs` that curvature is "far below
a millimetre" goes.

### 6.5 Ships

- **No edge:** today `KeepInZone` clamps X and Z to ±7,800 m.
- **The ceiling** is the leaving height (decision 4). `ships.json` stores it in world radii, and its reader checks 1 to
  3 radii.
- **The air** they fly through uses real-equivalent heights (Proposal): the climb to the leaving height feels like
  today's climb to 15 km.
- **Orbit and J** work as today.
- **Prefetch** goes by tiles.
- **Caps per world.** Today each zone has 16,384 building bodies, 1,024 parked ships and 1,024 explosives. These now
  hold for a whole world, which is enough for S3; P6 revisits them.

### 6.6 Time and climate

- `PlanetClimate` already works per position. The stand-in's climate gets local hours (§4).
- The console's `time` and `timescale`, the clock line and `ZoneHours` read the hours where the admin stands. At the
  server's console they use the arrival point.

### 6.7 Places

- **Arrivals:** beaches over the whole world (decision 8). They are found per tile and sorted by tile key, so the result
  is deterministic, then picked at random.
  - Sea level comes from `SeaRadius`, not from height 0.
  - A world without a sea gets arrival points on open flat ground.
  - `--lat` and `--lon` name a point on the world, and `--spawn origin` arrives there.
- **The console:**
  - `teleport <latitude> <longitude>`, `teleport <player>` and `teleport <cell>` (a grid name) replace
    `teleport <x> <z>`;
  - `beside` is 1.5 m to the player's right;
  - `whereami` prints latitude, longitude, height and cell.
- **Map markers** are points on the ball. Today they must lie inside a 1,950 m square.
- **The raid ring** (`--raid-bases`, `raidbase`) has a centre point, and its huts face it along the ground.

---

## 7. The world on the ball

### 7.1 Nature

- **Scatter** runs at the level by size (§3.2), with candidates spaced in metres, so density per square metre stays as
  it is. The level is part of an object's ID, and decoding checks the range.
- **The stand-in's nature** sits on level-6 tiles. Its IDs carry a tag, the face, x, y and an index; today they carry
  plane tiles.
- **Queries.** Every query by an X/Z rectangle becomes a query by a sphere (centre and radius): combat, targeting,
  placement, nav grids, the client's `NatureView` and the ambience's tree count.
- **Weak spots** face a direction (§5). Today they are a 16-bit yaw that lands on the wrong side away from the landing
  site.

### 7.2 Props

- **Loot clusters** cover the whole world: one candidate per level-5 tile, 6,144 tiles against about 480 cells today.
  They are classified as today: coast, forest edge or open.
- **Recyclers** follow decision 9.
- **IDs** are a tag, a tile and a member index. They must not reuse today's tag 0xC4 with a new meaning.
- **Start-up** grows with the count. The clusters are generated on workers and measured in the load test.

### 7.3 Animals

Today's rule stays (decision 10):
- spawn areas come out within 380 m of a player;
- animals sleep beyond 340 m from every player;
- an area empties after 30 s with nobody within 560 m;
- there are at most 600 animals.

What changes:
- spawn areas are level-4 tiles (237 m);
- each level-6 tile gets a nav grid in its own frame: heights along up, slopes against the local up, and A* across tile
  edges and face seams. Today the grid reads slope against the frame's +Y;
- positions move along the ground and are put back on it;
- steering and the neighbour grid work in 3D;
- an animal's heading is a direction.

---

## 8. Network (protocol 21)

- **Positions** keep their encoding: steps of 1/256 m, range ±8,388 km. On a ball Y moves as much as X and Z, so a moving
  entity's deltas cost a few more bits.
- **The view.** `InputCommand`, `CharacterNetState`, `FireIntent` and `ThrowIntent` carry a view (octahedral, 2 × 16
  bits) instead of yaw and pitch. Free look stays relative to the view.
- **Facings of things.** `ThingSpawn`, `PoseNet`, `NaturalNet`'s weak spot and `TeammateNet` carry a forward direction.
- **Ships** send a smallest-three quaternion.
- **Points.** `BagNet`, `MarkerIntent` and `DevNav` become 3D points; today they carry X and Z only.
- **The welcome.** `ZoneInfo` carries the world's game radius. It no longer carries the origin's latitude and longitude,
  because the frame is the world's.
- **Relevance** uses the 3D grid (§3.3). Ships keep their 2,000 m.
- **Versions.** `Protocol.Version` goes from 20 to 21; NetGen rewrites the serializers, and the schema hash follows.

---

## 9. Saves (schema 7, decision 1)

- Characters, things, building frames, ships, explosives and corpses are already stored in the body frame as doubles.
  They would survive the new zone frame, but not the new radius: a position 6,435 km from the centre means nothing on a
  2.41 km ball.
- **Migration 6 → 7** deletes the world's rows: things, building, ships, both override tables, zones, notifications and
  markers. It clears characters' positions and inventories, which means a new life. Accounts and teams stay.
- **Formats.** Facings are stored as forward directions. Markers, death points and raid alerts become body-frame points;
  today they are zone-frame floats. Codec versions go up wherever a layout changes.
- **The map's cache key** gets the radius and the surface's version; today it has neither.

---

## 10. The client

### 10.1 One way to draw the ground

The stand-in is drawn the way the real surface is drawn: by `TerrainLod`'s quadtree on the cube faces, which already
works on a sphere. What `--stand-in` uses today goes: `GroundTerrain` (7 × 7 squares of the plane, 448 m across from a
ship), the procedural sky and the fixed light. Changes:

- **The scene:** `RenderFrame.World`, so the scene's coordinates are the body frame in floats.
- **`TerrainLod`:**
  - the finest level is chosen by size;
  - height bounds come from the surface, not a fixed −11 to +9 km;
  - tiles can be built from any `ISurface`, with materials from biome weights and slope. Today only a `PlanetSurface`
    can be drawn.
- **Texturing** (Proposal). Today every ground layer samples the plane's X and Z, so textures stretch away from the
  landing site. Instead, each vertex gets coordinates in metres on its cube face, blended across face seams, with the
  tangent derived from them. Terrain, grass and litter share them. The alternative, full triplanar, costs about three
  times the texture fetches in the most expensive shader.
- **The sea** uses the same per-point frame for its swell, ripples and normals.

### 10.2 Sky and air: the minimum (S5 does the rest)

- A `hu_planet_centre` global gives the ball's centre, separate from the air's real radius. With it, the terrain, water,
  grass and foliage shaders find their up. Today they all take the centre to be 6,435 km below the camera.
- The sky's ground test and horizon use the ball. Its edge lies 2.2° below level at eye height and 70° below from the
  leaving height; today the sky puts it at −0.04°.
- The aerial-perspective table covers 10 km instead of 400 km, and its east comes from §3.1. Today that east is
  undefined 90° east or west of the landing site.
- Clouds are sampled by direction instead of on the plane.
- Left for S5: clouds that show from above (today they are drawn behind all geometry), the thin air seen from space, and
  light computed per pixel at the ball's edge.

### 10.3 Plants and grass

- **Plants:**
  - scatter tiles are found at the level by size;
  - drawing ranges come from the horizon;
  - the near impostors' bounds follow the camera;
  - wind blows along the local ground.
- **Grass:**
  - a local frame at each recentre;
  - stable cells on the cube faces (level 13, 0.46 m) that match the terrain's grass layer;
  - heights at the level by size.

### 10.4 Things that fall

- **Ragdolls** fall in an `Area3D` with point gravity at the centre; today they get a constant force along the up where
  they died. They must start where the body is: the scene's origin is now the planet's centre. Finding 15 suspects they
  already start at the origin.
- **Gibs:** the dust's floor lies along up.
- **Effects:** particles fall towards the centre, which the process shader reads, and floors are stored as heights along
  up. Bounds are ±8 km.
- **Tracers and thrown arcs** re-aim gravity as they fly.

### 10.5 Cameras, lights and tools

- **The far plane** is 15 km: the ball's edge is 6.8 km away from the leaving height.
- **The look-dev, menu and tour cameras** take a local up.
- **The sun and the moon** are hidden below the camera's horizon, not below the render plane.
- **Readouts** use local north (§5), and the console prints metres.
- **Scripted runs and tools:**
  - the tour, the ship sandbox and the animal sandbox find their spots by latitude and longitude or by tile;
  - the F2 panel draws nav grids by tile.

---

## 11. The map (decisions 5 and 6)

- **One map control**, used by the G screen and the death screen. Today they are two painters with two grid rules.
- **The picture:**
  - six square pictures, one per cube face, rendered from the surface by direction;
  - deterministic, rendered on a worker, cached under a key with the radius and the surface's version;
  - 1,024 px a face, 3.7 m a pixel.
- **Projection.** A canvas shader draws it from the six pictures:
  - zoomed out, it is a globe seen from afar;
  - zooming in, it blends into a flat chart centred on the view, so the land under the view is true to scale;
  - zoom runs from the whole world (4.8 km across) down to about 150 m across.
- **Moving it.** Dragging turns the globe. Following keeps the player in the middle with local north up.
- **The overlay.** The interface draws the grid, markers and labels over it, crisp at any scale and placed by
  `Labeller`. It uses the same projection in C#, its inverse for clicks, and a test that hides the far side.
- **The grid** (decision 6): faces A to F, each 25 × 25 cells lettered and numbered as in Rust, so "C K12". The console's
  `whereami` and `teleport <cell>` use the same naming function. Today the console has its own copy.
- **Facing arrows** are measured from local north.

---

## 12. Found on the way

The surveys found these on `main` today. S3 did not cause them.

| What | Where | Fix |
|---|---|---|
| Every animal added on the same tick gets the same seed, so a herd's members start on one spot, face one way and think on the same tick | `AnimalWorld.AddMember`: the counter `_localIds` is never incremented | Increment it, on `main` now |
| In play, the near tree impostors' bounds are ±400 m around the landing site. More than about 400 m from it, they disappear whenever those bounds are out of view. Not yet seen in a run | `Vegetation.Game.RebuildNear` anchors the near set at the frame's origin; the impostors' `CustomAabb` is fixed | Bounds around the camera, on `main` now |
| The map of a surface without a sea is most likely black: heights become infinite and the shading NaN. Not run | `ZoneMapImage` | In S3: the stand-in gets a sea (§4) and the map reads `SeaRadius` safely |
| `world.json` says coastal clusters lie within 40 m of the sea; the code uses 35 m | `LootSpots.Classify` | In S3 |
| The animals' class comment says they think at 8 and 4 Hz; they think at 7.5 and 5 | `AnimalWorld` | In S3 |

---

## 13. Tests and tools

- **Existing tests.** About 100 to 130 of the 225 tests change.
  - Most change through the three rigs (`GameRig`, `LoopbackRig`, `ShipRig`) and the new teleport syntax; 30 to 40
    assertions are tied to plane numbers.
  - The hand-built 6,435 km tangent frames in PhysicsTests, SurfaceTests, BuildingTests and MovementTests move to the
    ball.
- **The movement check** runs on the stand-in at 2.41 km in the world frame.
  - Its new line replaces 0x6E7FDE937D5A46FA in GROUND.md, and Godot's `--physics-check` must print the same.
  - PhysicsTests' "moved more than 40 m" becomes a distance from the start.
- **New: a walk round the world** (SCALE.md §5.4). A character walks straight ahead with the mouse still, through points
  90° and 180° from its start and over a pole. It must come back to its start within a metre, facing the way it set
  off. Then the same with a ship.
- **Bots.** A bot's home is its arrival point, it steers on the local ground, and the build plots and the raid ring are
  laid out around a point on the ball. FlyBrain's home vector today points into the planet.
- **The check for S3** (SCALE.md §6):
  - the load test on the ball: 100 bots surviving, building, fighting, raiding and flying;
  - the walk round it;
  - screenshots of the ground, of the ball from the leaving height, and of the map.
- **Rules that still hold:**
  - `Zone.cs` stays under 450 lines;
  - new code in `Hollow.Surface` uses `DMath`;
  - ARCHITECTURE.md §6 and §10.1 (surfaces on a tangent frame, a zone at most 20 km) change with the code.

---

## 14. Order of work

On the branch `whole-world`, with `main` merged in regularly. Each step ends green.

| Step | What | Check |
|---|---|---|
| S3a | The world frame, lengths by size, the view (input, wire, history, hit shapes), the radial ground, physics tiles on the ball, the stand-in world, building, ships, the console, protocol 21, schema 7, the tests' rigs | The tests, the movement check and the walk round the world |
| S3b | Nature, props, arrivals, animals, the raid ring, the server's 3D grid, bots | The load test on the ball |
| S3c | The client draws the ball: the render frame, terrain from any surface, texturing, the sea, the sky's minimum, plants and grass, ragdolls, gibs and effects, cameras | Screenshots from the ground and from the leaving height; the frame time |
| S3d | The map | Screenshots of the globe, the chart and the death screen; the interface check |

S3a is the largest, and nothing on `main` changes while the branch is built.

**Other work at the same time.** If wave 1 of KNOWN_PROBLEMS.md §4 starts meanwhile, three of its packages touch code
S3 also changes:
- Building: the first foundation's heading, finding 4;
- Interaction and bodies: ragdolls, finding 15;
- Seats, controls and damage: the pilot's keys.

They reach `whole-world` when `main` is merged in, so conflicts stay in those three places.

**Into `main`.** The branch merges when the owner says: together with S4's ground, or earlier if they accept playing on
the stand-in.

**S3a as built (14 October 2026).** The checks: the 227 tests pass; the movement check's line is now
`0xD16D9135CE374BD2` (final position 849.89264, 1428.0099, 1734.6373); the walk round the world brings the character
back within 61 cm and the Tin Finch, 100 m up, within 9 cm and at its height, both facing the way they set off.
Godot's `--physics-check` hasn't been run against the new line (S3c), nor the load test with 100 bots surviving,
building, fighting, raiding and flying (S3b); 20 walking bots ran for 20 seconds at 1.6 ms a tick, their prediction
exact. Where the build differs from the design above:
- **Things that stand still** keep a yaw, measured from local north (§5 has them carry a forward direction). Their point
  never moves, so its north never changes; the wire and the saves for them stay as they were.
- **Ships** (§6.5): the ground turns under a ship as it goes. Every substep its attitude and its velocity turn with the
  ground, and a straight step over the curve doesn't lift it, only its way up does. Without this, the Tin Finch's
  160 m/s at full throttle is faster than an orbit of the first world (about 147 m/s at 0.92 g) and its nose pitches
  against the horizon. So the ball flies as the flat ground did, and a ship flown straight ahead goes round the world
  at its height. A new ship
  stands level at its centre of mass: from its pivot, 0.28 m behind, the up is 0.0064° off, which flight assist held as
  a sink of a metre a minute.
- **Scatter levels by size** (§3.2) wait for S4: zones don't use the real planet's scatter until then.
- **The climate** (§6.6) follows the zone's sky where each player stands on every server; a zone a test makes without a
  sky keeps one compressed day.
- **Schema 7** (§9) gives characters a new life by clearing their place and state, so blueprints go with the inventory
  and markers. The player codec is version 5; markers and the death's point in older states are dropped.
- **The tests:** stand-in servers in tests arrive at the landing site, where the tests lay out their places
  (`LoopbackRig`'s `SpawnAtOrigin`); `GameRig.Clear` fells trees along a test's line of fire, since the stand-in's
  placement moved them; the animals' network test goes to the nearest area its survey gives herds (the stand-in's
  habitat is thin); the real-planet tests read the planet's scatter in a frame at the landing site, and walk, swim and
  find beaches on the stand-in.
- **Found while converting:** the nature changes' replication, rewritten for 3D cubes, sent a fresh hit twice, once
  for its cube coming into range and once as this tick's change (fixed); the X's height test passed by luck (its
  bound now follows where the swing meets the trunk).

**S3b as built (15 October 2026).** The check: every load test of AGENTS.md ran on the ball with 100 bots (20 for
flying), each twice: with every bot at the landing site, and with arrivals on beaches all round the world, each bot
playing round its own. All 16 runs keep every bot to the end and meet the targets; GROUND.md §5.12 has the tables and
the comparison with the flat patch. In short, at the landing site and on beaches (the server's tick, average / 99th
percentile, ms):

| Load test | Landing site | Before S3 | Beaches |
|---|---|---|---|
| 100 walking | 5.60 / 9.3 | 4.25 / 10.2 | 4.40 / 7.6 |
| 100 surviving | 6.57 / 11.4 | 5.90 / 12.3 | 4.50 / 9.5 |
| 100 fighting | 7.48 / 12.4 | 8.64 / 18.5 | 4.68 / 11.1 |
| 100 building | 7.44 / 22.8 | 7.38 / 21.3 | 4.39 / 27.0 |
| 100 raiding | 8.13 / 14.7 | 9.66 / 30.7 | 5.73 / 9.8 |
| 100 living off the loot | 5.60 / 9.1 | 5.75 / 18.6 | 4.52 / 7.9 |
| 20 flying | 1.51 / 3.2 | 1.31 / 4.1 | 1.90 / 3.9 |
| 100 surviving and 200 animals | 8.31 / 13.9 | 9.13 / 27.3 | 5.19 / 17.9 |

Bandwidth down at the landing site is 12–47 % higher than on the flat patch (a position on a ball changes in all three
coordinates, §8), within 256 kbit/s; prediction is exact wherever nothing pushes a player. The engine-free tests pass
(227). What S3b changed, besides measuring:
- **Arrivals:** a client's prediction holds an arrival (the start, a respawn on a far beach, a teleport) until worker
  threads have built its ground, as the server holds a login or a respawn, whose wait went from 1 to 5 s. Without it
  100 bots arriving all round the world lost their connections: their one process built about a thousand tiles itself.
- **The tiles near a point** (§6.1) are kept by the 7.4 m cell it lies in (`NearTiles`): the search went 2.2 → 0.1–0.4
  ms a tick for 100 characters on the server, and in every client's prediction. The nav grid asks the same way.
- **A server that keeps everyone in one place** (`SpawnAtOrigin`) respawns them round the landing site too, or a fight
  scattered its crowd over the world.
- **Animals** (§7.3): a nav lookup finds its face coordinates once instead of twice, and a path search remembers each
  cell's walkability: 6.4 → 2.0–2.7 µs an animal a tick (2.1 on the flat patch).
- **A foundation's collision check** leaves the ground out (it sits in it): bases started at once on beaches spent
  3–90 ms on a box against a tile's mesh.
- **The bots** (§13): a bot's home is where it arrived, renewed after a respawn elsewhere; the build plots are a grid
  round the landing site on dry ground for the crowd there, else the bot's own beach; on beaches the raid arena puts each
  hut beside its raider's beach (`TestBases.Beside`), one hut a bot; the animal test on beaches uses natural spawning,
  held to its population (`ServerOptions.AnimalCap`). Older bot faults the ball showed are in GROUND.md §5.12.
- **Left:** bursts of slow placements when 80 bases start at once on beaches (30 of 3,602 ticks over 33 ms); the
  snapshot's bytes a moving character (about 87 of 99 walkers fit the budget); the animals' cost above the flat
  patch's. KNOWN_PROBLEMS.md lists them.

---

## 15. Docs that change with the code

- GROUND.md: §3, §4.2, §4.4, §4.5, §4.6, §6.3, §6.7, §6.8, §6.9 and §8.
- GROUND_SYSTEMS.md: targeting and queries by sphere.
- ARCHITECTURE.md: §6 and §10.1.
- HOW_IT_WORKS.md and CODE_LAYOUT.md.
- AGENTS.md: the console's teleport, the stand-in and the server's options.
- SCALE.md: §5.

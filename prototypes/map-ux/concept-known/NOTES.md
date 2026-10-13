# Concept Known: the map shows what you know

Round 3: one map that combines what the owner has decided so far (`../LEADS.md`, the space direction in
`docs/SPACE_CONCEPT.md` on branch `space-concept`, and `docs/LORE.md` on branch `ground`). It is a fork of concept AB:
same layout, A's palette, A's search, place card and itinerary, and the two system views.

Open `index.html` by double-click. It needs internet once, to load three.js 0.170.0 and d3-delaunay 6.0.4 from jsDelivr.

## The idea

- **The map shows what you know.** Your home, your outposts, the systems you visited, what your drones scanned and the
  bright stars you can see from home are drawn. Everything else is unknown: a quiet density haze with no points and
  no labels.
- **Every known star gets a shape.** Known stars own Voronoi cells in screen space, bounded a few light-years from
  each star, so the shapes stop where knowledge stops. The colour says what you know: mint for yours, white for
  visited, blue for scanned by a drone, a dashed outline for seen only, lilac for a find and pink for another
  player's base.
- **Tiles keep it readable.** Cells merge into tiles, never more than about 200 on screen.
  - The roots are the wholes you know: Home, each drone survey and each past trip. A star known on its own is its own
    root.
  - Your bases, visited systems, finds, the selection and the route always keep a tile of their own.
  - Any other tile wider than 150 px splits in two, across its longer side on screen. Zooming out merges a survey into
    one tile ("Tureis survey · 204 systems · 4 finds"); zooming in splits it down to single stars.
  - The split is decided when the view settles, so tiles never flicker while the camera moves.
- **Drones explore for you.** Send a drone to any star or any point in unknown space. It flies out (time compressed to
  a few seconds), surveys every system within 20 ly, the region fades in from the centre, and the finds arrive as
  cards: ruins of the vanished civilisation, wrecks, rich resources, other players' bases and fuel stars.
- **Your space is a feeling, not a mechanic.** Your bases glow softly, with a home area around them. There is no
  territory, claim, faction or border.
- **Anywhere stays reachable.** Search finds any star by name; catalogue and generated names are public. Picking an
  unknown star says how little is known about it. Routes run through unknown space as a quiet dashed line, and their
  stops there read "Unknown system". From an arm or a sector card, **Route there** plans past the 150 ly of star data:
  the known part, then a dashed line through unknown space.
- **Hollow's own system map** is the Realistic view: bodies on the left, the clock on top, the body card on the
  right, Compressed / True scale and textured planets. A's calm Simple view stays the default.

## What's mock

Everything about knowledge, drones and finds is invented. It is deterministic: fixed rules, no randomness, the same on
every load.

**`knowledge.js` (MOCK)**
- **Bases:** the home base on Gliese 54.1 (from `../data/mock.js`), plus Outpost Ember (Achird), Outpost Cinder
  (Gliese 783 A) and Outpost Lark (Tabit).
- **Visited:** the stops of eight past trips, laid out by the real route planner.
- **Surveys:**

  | Survey | Size | Drone, date |
  |---|---|---|
  | Chi Herculis | 112 systems | Tern, 2 Oct |
  | GJ 1124 | 138 systems | Moth, 5 Oct |
  | Tureis | 204 systems | Tern, 11 Oct |
- **Seen:** the 40 named stars brighter than magnitude 2.5 seen from home.

**Finds** are properties of a system. A hash of its address decides them, so a drone finds the same thing every time:
- ruins (about 1 % of systems with landable worlds) and wrecks;
- rich rare-metal deposits;
- the other players' bases from `mock.js`;
- one fuel star per survey: the brightest scoopable A, F or G star.

Names follow the lore families (old civil, industrial, military, colony; old ships).

**Drones:**
- **Moth** is surveying, 72 min left in game time.
- **Tern** is returning home with 4 finds.
- **Gnat** is ready.

The background drones run in game time. A drone you send runs time-compressed, and the panel's "skip ahead" button
finishes a leg now.

**The rest is mock too:**
- `data.js` has no stations: fuel stars (scoopable, outside the storm) are the only refuelling points.
- The raider zone is gone. The ion storm stays as the one natural hazard, behind A's Fastest / Safer choice.
- Planet textures are procedural (seeded by the body's name).
- Orbital periods come from Kepler's law, with planet masses from gravity and radius.

**Real:** stars, names, classes, positions, planet summaries and body lists within 40 ly, and the galaxy sample, glow,
arms and sectors (Hollow's generator, `../data`).

## Controls

| Input | Does |
|---|---|
| Left-drag | Turn (sideways) and **tilt freely through the whole range**, under the plane too. Only the poles are avoided. |
| Right-drag or Shift-drag | Pan |
| Scroll | Zoom through the levels. Scroll into a star to open its system; scroll toward unknown space to move there. |
| Hover | Lights the tile under the cursor and its star's own cell. Outside every cell: "Unknown space" with the 20 ly footprint of a drone survey. |
| `Tab` / `Shift+Tab` | Step through the stars of the tile under the cursor |
| Click | Selects the star (exactly one under the cursor), or the unknown point. A group's name selects the group's report. |
| Double-click | Fly there (a group's name: show all of it) |
| `E` | Drones panel |
| `/` | Search |
| `R` `T` `F` `Enter` `Space` `V` `C` `G` `1`–`4` `Esc` | As in concept AB |

**In the Realistic view:**
- `Space` plays or pauses the clock, and `,` `.` change its speed.
- `L` switches between Compressed and True scale.
- `V` switches to the Simple view.

**Filter chips:** Yours · Visited · Scanned · Seen · Finds (also the legend of the tile colours) | Reachable · Fuel ·
Landable · Earth-like. Every chip counts and highlights known stars only.

**Deep links** (as concept AB, plus):
- `drone=<star>` or `drone=x,y,z` sends the ready drone. Add `phase=travel|scan|reveal|done` and `t=<s>` to jump
  ahead.
- `skip=moth|tern` finishes a background drone's leg.
- `drones` opens the panel.
- `group=sv-far` selects a group.
- `scale=true` starts the system map in True scale; `time=<days>` sets its clock.
- `mouse=x,y` places a hover cursor.
- `debug=1` writes the tile counts and the placed labels into `<pre id="dbg">`.

## Files

| File | What |
|---|---|
| `knowledge.js` | **MOCK** knowledge: levels, groups (home, surveys, trips), finds, bases, drones, reports. `addSurvey` and `visit` change it live. |
| `tiles.js` | Every known star gets a shape: the cells, the tiles under the ~200 budget, drawing and picking (d3-delaunay) |
| `stars.js` | The star field: known stars as points, unknown ones as haze, the reveal |
| `data.js` | Concept A's search, intents and route planner. No stations, hazards only; intents answer from what you know. |
| `app.js` | Concept AB's app with knowledge, tiles, drones, reports, the Realistic system map, free tilt and far routes |
| `index.html`, `style.css` | AB's layout, plus the drones button and panel, report cards and the Realistic view's panels |

**Shots:** `../shots/concept-known-1-neighbourhood.png` … `-7-route-unknown.png`

## Rough edges

- Cells are drawn in screen space. A survey above or below your home can overlap it on screen, and its tiles then
  interleave with Home's. Turning the view separates them.
- The route planner knows every star, as the game does. Fuel stops in unknown space are marked "unconfirmed" rather
  than planned around.
- Beyond 150 ly the prototype has no star data. A far route's autopilot stops at the edge of the data and says so.
- Background drones run in game time (Moth needs 72 minutes). Use "skip ahead" in the Drones panel to see them finish.

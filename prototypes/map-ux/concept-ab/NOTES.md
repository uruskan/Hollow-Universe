# Concept AB: zoom levels with concept A inside

This concept merges B and A, with B as the frame.

**From B: layout and zoom.**
- Four zoom levels: Galaxy › Region › Neighbourhood › System.
- Breadcrumb and level caption at the top left, the zoom rail on the left.
- Ship pill and search pill at the top right, camera tools at the bottom right.
- Scale and data note at the bottom left, filter chips along the bottom, inspector on the right.

**From A: look and panels.**
- A's palette: neutral glass with one mint accent; red and amber appear only for danger.
- A's big search panel, opened from the pill or with `/`.
- A's place card and route itinerary inside the inspector.

**System level: two views.** A toggle at the top centre (or `V`) switches between them.
- **Simple** (A's calm row of worlds) is the default.
- **Realistic** is B's orbit map, with moons and labels.

**Added for every fork:**
- **Forgiving star picking** in the shared star module.
- **A mock territory layer:** your space, three factions, a contested border and the frontier.

Open `index.html` by double-click. It needs internet once, to load three.js from jsDelivr.

## Use

| Input | Does |
|---|---|
| Scroll / pinch | Zoom through the levels. Keep scrolling into a star to open its system; scroll out to leave it. |
| Drag | Orbit |
| Right-drag or Shift-drag | Pan |
| `1`–`4` | Galaxy, Region, Neighbourhood, System |
| Breadcrumb, rail | Jump back to a level |
| Hover | Snaps to the likeliest star within about 24 px. A ring, its name pill and a dotted leader from the cursor show what a click will select. |
| `Tab` / `Shift+Tab` | Cycle through the stars near the cursor |
| Click | Selects the star. Where several stars are too close to call, a chooser lists them (name, distance, jumps, what's there); pick one with the mouse or with `↑` `↓` `↵`. |
| Double-click | Fly to a star |
| `/` or `Ctrl+K` | Search. Shows "Ask the map" intents, favourites and recents; results give distance and jumps. Choosing a result flies there and opens the inspector. |
| `R` `T` `F` | Route, Target, Favourite |
| `Enter` / `O` | Open the system |
| `Space` | Start or stop the autopilot |
| `V` | Simple / Realistic in a system; top view elsewhere |
| `C` / `H` | Centre on me |
| `G` | Galaxy ↔ neighbourhood |
| `Esc` | Back out one step: chooser, search, autopilot, body, system, route, card, then a level up |

**Filter chips:** Reachable · Fuel · Stations · Landable · Earth-like · Territory · Bases · Danger. The Territory chip colours every controlled system in its owner's colour and brightens the regions.

**Routes:** concept A's planner (`data.js`), A* over (system, fuel) across the 37,409 real systems.
- If the fastest route passes a danger zone, the itinerary offers **Fastest** and **Safer**, with A's warning card.
- **Start autopilot** stays visible however short the window, because the button is pinned to the bottom of the scrolling inspector.

**Labels:** at most 22 star names at once (`STAR_LABEL_CAP` in `app.js`), chosen by priority and never overlapping each other or the panels. In order:
1. you, your home and your outposts;
2. the selection and target;
3. the route;
4. results and filter matches;
5. bright and named stars.

Stations, bases and favourites get a small nudge. Region names, distance rings and danger zones are annotations and do not count toward the 22.

**Deep links** (used for the screenshots):
- `index.html#level=galaxy|region|neighbourhood|system`, plus any of:
  - `select=<star>` or `route=<star>` (add `safe` or `steps` to a route)
  - `view=simple|realistic` and `body=<body>` (in a system)
  - `filters=reach,fuel,stations,land,earth,terr,bases,danger`, `intent=<id>`
  - `top=1`, `dist=<ly>`, `hover=<star>`
- `search=<query>` opens the search panel (an empty value shows the empty state).

## Files

| File | What |
|---|---|
| `index.html`, `style.css` | B's layout with A's palette and components |
| `app.js` | Camera, levels, galaxy and region layers, realistic system scene, labels, search, cards, routes, autopilot, territory rendering |
| `data.js` | Concept A's engine-free layer (`window.HD`): search, intents, jump counts, route planner. Unchanged copy. |
| `stars.js` | **The star field module** (`window.HollowStars`); see below |
| `territory.js` | **Mock territory data** (`window.HOLLOW_TERRITORY`); see below |
| `tools/make-territory.js` | Regenerates `territory.js`. It is deterministic: two runs give byte-identical files. |

### The star field module (`stars.js`): what forks replace

It owns how the 37,409 local stars are **drawn**, **highlighted**, **picked** and **ranked for labels**. Replace the visuals freely, keep this interface, and `app.js` keeps working:

```js
const field = HollowStars.create({ THREE, scene, dpr, stars: { n, x, y, z, mag, col, cat } });
field.update({ target, dist, opacity });   // every frame before rendering
field.setStyle(i, mode, rgb); field.commit();   // 0 normal, 1 highlighted in rgb, 2 dimmed
field.project(camera, w, h);                // every frame; fills field.visible / sx / sy / app
field.labelCandidates(extra)                // [i, priority, ...]; extra = Map(i -> bonus) the app needs considered
field.setImportance(weights)                // Float32Array: how much each star matters when several are close
field.candidatesAt(x, y, r = 24)            // [{ i, d, score }], best first
field.pick(x, y)                            // { i, list, crowd, crowded }
```

- Picking is pure screen-space math over `project()`, so a fork can draw stars any way it likes and still reuse it.
- The score ranks candidates by:
  - nearness to the cursor;
  - importance (named, stations, bases, your territory, landable worlds, near you), filled by `app.js`;
  - filter highlight and apparent brightness.

  Dimmed stars rank last.
- `crowded` means two or more stars are too close to call. The chooser and Tab cycling, which live in `app.js`, use `crowd`.

### Territory (`territory.js`, MOCK)

```js
HOLLOW_TERRITORY = {
  meta: { mock: true, localCount, ... },
  factions: [{ id, name, relation: 'self' | 'hostile' | 'neutral' | 'friendly', color, capital,
               systems: [{ sys, system, addr, role: 'home' | 'outpost' | 'capital' | 'member', name?, radiusLy }] }],
  contested: [{ sys, system, addr, claimants: ['you', 'karth'], radiusLy }],
  frontier: { what, systems: [sys, ...] },   // unclaimed systems within one jump of your space
}
```

`sys` indexes `HOLLOW_LOCAL.systems` from the same export.

**The factions:**

| Faction | Relation | Systems |
|---|---|---|
| Your space | yours | 5: home base plus 4 outposts within 25 ly |
| Karth Hegemony | hostile | 16 |
| Tessaly Accord | neutral | 13 |
| Orrin Free Holds | friendly | 10 |

There are also 3 contested systems on the border between your space and Karth, and 85 frontier systems. All names are our own.

**How concept-ab draws it:**
- Each faction's region is the smooth union of discs (`radiusLy`) around its systems. It is baked into a signed-distance texture and drawn on the plane through your ship: a soft tint with a crisp border, and the contested overlap hatched in both colours.
- Thin stems tie each controlled system to the plane.
- Region names sit on the regions.
- The ship pill reads "Your space · 5 systems · 3 contested". Cards and hover cards show the owner, contested or frontier status.

A fork can redraw all of this from the same data.

## Real and mock

- **Real**, from Hollow's generator:
  - stars, names, classes and positions;
  - planet summaries and body lists within 40 ly;
  - the galaxy sample, glow, arms and sectors.
- **Mock**, labelled "mock" in the UI:
  - stations, bases, danger zones, resource hints and the ship (`../data/mock.js`);
  - the territory layer (`territory.js`);
  - seeded favourites and recents;
  - itinerary times (about 28 s per jump and 40 s per refuel).
- Systems beyond 40 ly have no body list. Their Simple and Realistic views are a labelled schematic built from the summary counts.

## Known rough edges

- Territory is drawn as a footprint on the galactic plane. A system far above or below the plane can look as if it sits inside a region it does not belong to; the stems and the Territory chip show the real members.
- Galaxy and region views have no territory: it is too small to see at those scales.
- At the default neighbourhood zoom, the ground near Sol is crowded in projection, so the chooser opens often there. Zooming in or pressing Tab avoids it.

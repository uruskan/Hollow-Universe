# Concept A: Search & Go

**The idea.** The map is one search field. You type, or ask in plain words ("nearest fuel", "water worlds",
"ringed within 30 ly"), pick a result, and the map flies there. A place card slides up with what is there:
the star, its worlds as a row of small planets with landable ones marked, stations, resource hints. Route
plans instantly over the real 37,409 systems with the ship's jump range and fuel. The card becomes an
itinerary: jumps, refuel stops, time, any danger on the way with a safer alternative, and one button,
"Start autopilot". Everything else stays quiet. The 3D view is calm and gently tilted, it shows at most
15 labels at a time, ranked by relevance, and they never overlap. Scrolling out turns your neighbourhood into
the whole galaxy, with the arms and the core labelled, and search still works there.

Open `index.html` by double-click. It needs internet once, for three.js from jsDelivr.

## Use

| Action | Mouse / trackpad | Keys |
|---|---|---|
| Search | Click the field | `/` or `Ctrl+K` |
| Choose a result | Click | `↑` `↓` then `Enter` |
| Back out (results, card, route, system view, galaxy) | ✕ or ‹ buttons | `Esc` |
| Select a star | Click it or its label | |
| Orbit | Drag | `Q` `E` |
| Pan | Right-drag or Shift-drag | `W` `A` `S` `D` or arrows |
| Zoom (out far enough becomes the galaxy) | Scroll, pinch, `+` `−` buttons | `+` `−` |
| Neighbourhood or galaxy | Segmented control, top right | `G` |
| Back to the ship | Arrow button, top right | `H` |
| Route, target or favourite the selected star | Card buttons | `R`, `T`, `F` |
| Open the system view | "Open system" | `O` |
| Start or stop the autopilot | Big button in the itinerary | `Space` |
| Next or previous body in the system view | Click a planet | `←` `→` |

**Try first:**
1. Press `/` and click "Nearest station".
2. Type `alkaid` and press `Enter`, then `R`. The fastest route crosses the Raider ambush zone, so pick
   "Safer", then "Start autopilot".
3. Press `G` for the galaxy and type `perseus`.

**URL states** (used for the screenshots, and handy for links):
- `#open` (empty search), `#q=water` (a query)
- `#sel=Sirius`, `#intent=earth`
- `#route=Alkaid&safe&steps`, `#route=Alkaid&auto=0.5` (autopilot half way)
- `#galaxy`, `#struct=Perseus Arm`
- `#system=Sol&body=Earth`, `#zoom=40` (zoom out by a factor)

## Real data, mock data

- **Real** (Hollow's generator, via `../data/`): every star, name, position, class, colour, planet summary
  and body list. Routes and jump counts are computed live on these positions: A* over (system, fuel) with
  12.5 ly jumps and a 6-jump tank. Scooping refuels at main-sequence stars, and stations always refuel.
  The galaxy view is the density-model sample and the arm and structure anchors.
- **Mock**, and labelled "mock" in the UI: the stations, the home base, the player bases, the danger zones,
  the resource hints and the ship.
  - The Ion storm doubles fuel use and blocks scooping, and the planner respects both.
  - Recents and favourites start with a few seeded entries so the empty state shows what they look like.
    After that they are stored per browser.
- Body lists exist within 40 ly. Farther systems show their worlds from the summary counts, and their
  "Open system" button is disabled.
- Routes are limited to the 150 ly bubble. Galaxy structures show the distance and the jump count instead
  of a route.

## Decisions

- The place card docks bottom-left and slides up, rather than sitting in the centre, so the selected star
  and the route stay in the clear part of the map. Under 900 px wide it becomes a centred bottom sheet.
  The camera centres its target in the free area: the view offset follows the card.
- One accent colour, mint (`#5fe3c0`), for you, your route and what you chose. Red and amber appear only
  for danger and hazard. Star colours are desaturated by about 40%.
- Label relevance comes in this order:
  1. ship and home;
  2. selection and target;
  3. route destination, then danger and refuel stops, then the other stops;
  4. search results;
  5. stations and favourites;
  6. the nearest named stars.

  Labels avoid each other, the panels and the route line. Distance rings and the arrow to the Galactic
  Centre are annotations and do not count toward the 15.
- Procedural names drop the shared sector prefix on map labels: "CH-GBY 7-0" instead of "Nolcas Peris
  CH-GBY 7-0". Cards and search show the full name.
- Zooming walks a path between your local anchor and a galaxy framing point. Scrolling out from home ends on
  the whole galaxy, and scrolling back in returns home without any panning.
- Itinerary time is about 28 s per jump plus 40 s per refuel stop, an assumption until the game has real
  timings. The planner ranks routes by fewest jumps, then fewest refuels, then shortest distance, and it
  avoids refuelling inside danger zones. "Safer" heavily penalises any stop inside a danger zone.

## Files

- `index.html`: the page.
- `style.css`: the styles.
- `data.js`: indexes, search, intents and the route planner. It has no rendering and also runs under node.
- `map.js`: three.js scene, camera, picking, labels and markers.
- `app.js`: search panel, cards, itinerary, autopilot, system view, keys and URL states.

My own scripts load as classic scripts, because ES modules do not load from `file://`. Only three.js comes
in as a module.

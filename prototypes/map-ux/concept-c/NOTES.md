# Concept C: Reach map

**The idea.** The map answers one survival question: *where can I go from here, and what's there?* It is
flat, like an atlas page seen from above (the galactic plane, Galactic Centre up), and centred on your ship.
Around you, three rings mark where 1, 2 and 3 jumps take you. They follow the real stars, so they are lumpy, not
circles. A lit area shows how far the fuel in the tank reaches, and everything beyond it is greyed out. A
lettered grid of 12 ly squares lets friends say "meet at G7"; your square is always shown ("You · M13"). Each star
carries its survival facts as small glyphs: a filled dot means you can scoop fuel, a hollow one means you can't. A
square around a star is a station, a small moon means landable worlds, and a red or amber halo means danger. Pins
mark bases, and a tick shows whether a star is above or below you. Routes read like a subway line: a vertical line
with stops, where refuel stops, stations and danger are highlighted, plus one "Start autopilot" button. Zoom out
and the grid gives way to Hollow's sector names, then to the whole galaxy with its arms named. A breadcrumb
(Milky Way › Orion Spur › Nolcas Peris › M13) tells you where you are at every scale. The 3D button tilts the same
map into perspective and back, for the moments when height matters.

## Open it

Double-click `index.html` (it runs from `file://`). It needs internet once, for three.js from jsDelivr.

## Use it

| Do | Mouse / trackpad | Keys |
|---|---|---|
| Pan | drag; two-finger scroll | WASD or arrows |
| Zoom (toward the cursor) | wheel; pinch | `+` `-` |
| Select a star | click it or its name | |
| Plan a route to the selected star | **Route** on the card | `R` or `Enter` |
| Start or stop the autopilot | **Start autopilot** in the route panel | `P` |
| Target or favourite the selected star | **Target**, ☆ | `G`, `F` |
| 3D tilt on or off | **3D** | `T` |
| Turn the 3D view | right-drag | `Q` `E` |
| Back to your ship | ◎, or click the "You" card | `Space` |
| Search | the box at the top left | `/` |
| Legend | **Legend** | `L` |
| Back out (legend, finder, card, route, 3D, zoom) | | `Esc` |

- **Search** finds stars, stations, bases, grid squares (`G7`), sectors and arms.
- Focus the empty search box for **Find nearby**: stations, landable worlds, water & ice, metals, organics, rare
  metals. A finder dims everything else, numbers the six nearest matches on the map (by jumps) and lists them.
- Click a crumb to zoom out to that level.
- In the star card, point at a world in the strip for its details. Systems within 40 ly have full body lists;
  further out the card shows a summary.

URL states (handy for screenshots; combine with `&`): `#select=Sirius`, `#route=Thaldis Landing`,
`#route=Sanor Spindle&autopilot=0.45`, `#tilt=1`, `#zoom=galaxy|arm|sector`, `#w=<ly across>`, `#at=x,z`,
`#finder=water`, `#search=gl`, `#legend=1`. Add `shot=1` to switch off fades for headless captures, and
`#selftest=1` to run a scripted pass over the main interactions (results in the console).

## Decisions

- **Rendering: three.js (WebGL) for everything drawn, HTML for every word.** Stars (37,409 local, 250,000 galaxy
  points), the glow image, the ground plane and the route ribbon are WebGL. Labels, pins, stop markers, panels
  and rulers are HTML, placed by projection each frame with a greedy collision-free placer that avoids panels and
  markers. No Canvas2D was needed. The flat view is a 0.3° lens from far away, which is orthographic to within
  about 1%. The 3D tilt widens it to 40° while pitching from 90° to 50°, so the map opens into perspective without
  a jump.
- **Rings, reach and glyphs are drawn by shaders.** The 1/2/3-jump rings and the tank's edge are the zero contours
  of signed-distance fields: unions of 12.5 ly discs around the systems that can make the last jump, rasterised at
  0.5 ly and lightly smoothed. They stay crisp at any zoom. Star glyphs (fuel, station, moon, danger, height tick)
  are drawn procedurally in the point shader, so all 37k stars cost one draw call.
- **Reach** is graph-based on the real positions: jumps of up to 12.5 ly in 3D, one fuel per jump, two inside the
  ion storm. "Lit" means reachable on the fuel in the tank, without stopping to refuel. The rings count jumps
  regardless of fuel. Because ~80% of stars are scoopable, "reachable with refuelling" would light the whole
  chart, so the map shows the tank instead and leaves refuelling to the route.
- **Routes** use A* over (system, fuel) states. They minimise jumps first, then distance, and avoid hostile space
  and the storm when they can. The tank refills at scoopable stars outside the storm and at stations. The route
  marks a refuel stop only where the next refuel point would be out of range, so a route shows the few stops that
  matter. The autopilot also tops up at the destination when it can. ETA is mock: 40 s per jump and 50 s per
  refuel.
- **The atlas grid** is 25 × 25 squares of 12 ly (columns A–Y from the left, rows 1–25 from the Galactic-Centre
  side), centred on Sol, which sits in M13. It covers the whole 150 ly data bubble with single letters, and a
  square is about one jump across. In the full game, each 1,024 ly sector would carry its own chart, named
  "Nolcas Peris M13".
- **Height**: in the flat view a short tick points up (above your plane) or down (below), on stars within two
  jumps, or on every reachable star up close. Stars far above or below fade. In 3D the ticks become real drop
  lines.
- **Orientation** follows `data/README.md`: +X right, +Z (Galactic Centre) up the screen. That is the conventional
  galaxy-map view, which strictly is the view from the south galactic pole.
- One accent (Hollow's interface cyan) means navigation. Warm means fuel only, red means danger only, and the base
  pins use green, grey and red for friendly, neutral and hostile.

## What's mock

Everything in `HOLLOW_MOCK`: stations, your home base and the other players' bases, the two danger zones, resource
hints, and the ship (Wayfarer, 12.5 ly, 6 jumps of fuel). Route timings (ETA) are invented too. The UI says so in a
line at the bottom and in the legend. Stars, names, positions, classes, planets, sectors and the galaxy are real
generator output. Individual systems exist only within 150 ly in this data, and the sector view says so.

## Rough edges

- Beyond 150 ly the prototype has no individual systems, so the sector view is mostly grid and names, and only
  the galaxy view shows the wider disk.
- Labels are re-placed every frame the view moves. They fade in and out instead of sliding, so fast pans can
  shimmer a little.
- In 3D the drop lines are limited to stars within three jumps; the HTML labels are not depth-sorted.
- The trackpad/mouse-wheel distinction is a heuristic (fractional or horizontal deltas mean trackpad).

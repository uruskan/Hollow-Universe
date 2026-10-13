# Concept Transit: the neighbourhood as a metro map

At the Neighbourhood level the map stops drawing 37,409 stars. It draws the systems that matter as stations: home, outposts, contested systems, faction capitals with a few members, stations, player bases, and fuel stops that split long lanes. That makes 63 stations. Lanes between stations are real chains of 12.5 ly jumps. Each lane keeps its real direction snapped to 0, 45 or 90 degrees, and its length grows with its jumps, so distance reads in jumps: every tick on a line is one jump, and each tick is a real system you can click. Lanes are joined into 27 lines coloured by territory: Y for your space (mint), K, T and O for the factions, and L for open space (grey). Territory is drawn as fare-zone areas, with Zone 1 for your space and Zone K, T and O for the factions. Contested systems are split interchanges in both colours, with a hatched border. A **Map / Transit** toggle (or `M`) morphs every station and lane between its real 3D place and the diagram.

This is a first playable version. The coordinator asked for speed, so it is not polished.

## How it solves

- **Readability:** in Transit only stations and lines are drawn. They sit on one flat plane, seen from straight above, with clean angles, so there is no depth to decode and no star cloud. Map mode is concept AB unchanged, for when you need the real sky.
- **Selection:**
  - Every target is a station at least about 60 px from the next one, with a large hit area (radius plus 18 px) and a label.
  - Labels avoid the lines and each other.
  - Ticks select the real system between stations.
  - Search, the inspector, routes and the system views are concept AB's, unchanged.
- **Territory:**
  - Your space is a mint Zone 1 that hugs your stations and your lines.
  - Faction zones are in their own colours, and the contested border is hatched in both colours.
  - The zone bar along the bottom frames and highlights a zone when clicked (for example "Your space · 5" or "Karth Hegemony · 16"). The lines inside a zone carry its colour, so the network itself shows who controls what.

## Controls (Transit)

| Input | Does |
|---|---|
| `M` or the top toggle | Map ↔ Transit, animated (1.15 s; the camera tilts to top view while the lines straighten) |
| Drag | Pan. The diagram does not orbit. |
| Scroll | Zoom, toward the hovered station |
| Hover or click | Hover shows the station's card with its line badges. Click selects; double-click zooms in, and again opens the system. |
| Zone bar | Click a zone to frame and highlight it; click again to clear |
| `R` | Route: in Transit the journey rides the network |

**Routes in Transit.** The journey rides the network: it boards at your station, rides lines, changes at interchanges, and refuels where the tank needs it. Where the lines go the long way round, it takes a short off-network hop of up to 4 jumps. If the network is still a detour of more than 3 jumps (or 40 %), it falls back to concept A's direct planner and says so. The route comes back in the planner's format, so the itinerary and autopilot are unchanged. A journey strip along the bottom reads like a trip planner: start, then each line with its jump count, each change, each refuel, then arrival.

## Mock, and rough edges

- Mock data:
  - stations, bases, factions, contested systems and danger (`../data/mock.js`, `territory.js`);
  - the network's choice of stations (`transit.js`: everything within 84 ly of home).
- Real data: stars, positions, the 12.5 ly jump graph, the hop counts and every system on a lane.
- Rough edges:
  - The layout is greedy: some lanes that would cross were dropped, so a few journeys need off-network hops.
  - Zone labels may overlap a line.
  - Headless screenshots run at 10–17 fps under software WebGL; check fps on a real GPU.

## Files and deep links

| File | What |
|---|---|
| `transit.js` | New. The network: stations, lanes (real jump chains), the octilinear layout, lines, zones and the journey planner. Engine-free and deterministic; it builds in about 0.3 s. |
| `app.js` | Concept AB's app with a Transit section added: the SVG diagram, the morph, picking, station and zone labels, the toggle, the zone bar and the journey strip. Camera targets follow the diagram. |
| `index.html`, `style.css` | The SVG layer, the toggle, the zone bar and the journey strip |

Deep links:
- `#level=neighbourhood&transit=1|0`
- `zone=you|karth|tessaly|orrin|contested`
- `tfreeze=0.5` freezes the morph for screenshots.
- Concept AB's links (`select`, `route`, `hover`, `dist`, `focus`) still work.

Screenshots: `../shots/concept-transit-1-diagram.png` to `-6-map.png`.

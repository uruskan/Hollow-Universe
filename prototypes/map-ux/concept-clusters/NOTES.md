# Concept Clusters: the star field as pins on a map

Stars that would crowd each other on screen merge into cluster bubbles. Each bubble shows a count and a tiny
summary ("1 station · 112 fuel"), and its ring takes its owner's colour. As you zoom in or fly closer, bubbles split
into smaller bubbles and then into single stars. They fly out of their parent and back in when you zoom out. Places
are always individual glyphs: your space, stations, bases, rare filter matches, results, the selection, the target,
the route and favourites. Every other star is inside a bubble, drawn only as a faint density haze, or is a single
star with room around it. The camera stays calm: below the galaxy level it never tilts lower than about 48°.

Everything else is concept AB unchanged: layout, search, inspector, routes, chooser and both system views. Open
`index.html` by double-click (three.js 0.170.0 loads from jsDelivr).

## How it solves the three problems

**Readability.**
- The default neighbourhood shows about 50 bubbles and a handful of named places, not 37,409 dots.
- Bubbles keep about 130 px apart and never overlap.
- The haze still shows where stars are dense, without competing with anything.
- At region scale the whole 150 ly survey becomes 5 bubbles (15k, 10k, 7.9k, 2k, 1.6k), each with its summary.

**Selection.**
- A bubble is a 26–64 px target. Hover shows a card, for example "249 systems around Chi Draconis · 27–142 ly ·
  nearest 3 jumps · 191 fuel · 197 landable · 1 Earth-like".
- Clicking a bubble flies to it until its children separate.
- When zooming cannot separate the stars (they are under 0.25 ly apart), clicking lists them instead: the chooser
  for up to 6 stars, the inspector list for more.
- A single star only appears once it has about 46 px of room, so concept AB's forgiving picker rarely needs its
  chooser.

**Territory.**
- Each owner is clustered separately, so a cluster never mixes owners. A bubble's ring and tint show its owner;
  where bubbles absorb each other on screen, the ring is split by owner.
- Your space is drawn as one solid shape. A minimum spanning tree of corridors joins the home base and outposts, with
  a strong fill, a crisp border and an inner rampart line.
- Factions get the same treatment, more quietly.
- The Territory chip turns every bubble into a count of controlled systems. Bubbles with none fade to empty rings.

## Controls

These are concept AB's controls (see `../concept-ab/NOTES.md`), plus:
- **Hover a bubble:** its summary card.
- **Click or double-click a bubble:** open it (zoom in), or list its stars when zooming cannot separate them.
- **Click its caption:** the same.
- **Drag:** orbit, with the tilt held between about 48° and straight down, below the galaxy level.

Filters re-count the bubbles in the filter's colour. For example, with Fuel on a bubble shows its fuel stars and the
caption gives the total ("of 214").

**Deep links** (as concept AB, plus):
- `open=<star>` opens the bubble that holds that star.
- `hoverc=<star>` hovers it.

## How it works

The engine is in `stars.js`, which keeps concept AB's interface and adds `clusterAt`, `members`, `boxOf`,
`clusterOf`, `setPlaces`, `setHighlight` and `bubbles`.

1. **Hierarchy, built once at start-up (about 0.3 s):** greedy clustering per owner, with the radius doubling at each
   level (0.35 ly to 358 ly; vertical distances count 62 %). It makes 17,994 clusters.
2. **Cut, every frame:** a cluster opens when its children would sit at least 196 px apart on screen (46 px for single
   stars), or when it covers more than 240 px. Hysteresis prevents flicker.
3. **Screen pass:** the biggest bubbles absorb whatever lies within about 132 px, which handles clusters stacked along
   the view.
4. **Drawing:** bubbles are WebGL discs with a segmented ring; the counts are HTML, so they stay crisp. The headless
   capture ran at 130–144 fps.

## Mock and rough edges

- Stations, bases, danger, resources, the ship and territory are mock, as in concept AB.
- Because the cloud is 3D, a bubble can hold stars at very different depths ("27–142 ly"). Opening it flies to its
  main cluster, not to the whole column.
- With the Territory chip on, faction systems show both as glyphs and as counts in their bubble.
- Not yet polished: region-scale captions can touch the station glyphs around "You are here", and there is no
  keyboard way to step through bubbles.

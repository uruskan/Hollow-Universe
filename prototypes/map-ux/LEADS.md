# Map UX: what the owner's reviews have settled so far

Round 1 (A Search & Go, B Zoom levels, C Reach map) and round 2 (five star-field inventions forked from the A+B base),
reviewed 2026-10-12/13. This file keeps the conclusions so nothing is lost while the space gameplay is being defined.

## Settled

- **Frame**: B's layout (breadcrumb, zoom-level rail, ship + search top right, tools bottom right, filter chips, inspector)
  with A's colours, A's big search panel, A's place card and route itinerary. Two system views: Simple (A) and Realistic
  orbits (B). This is `concept-ab`.
- **C (flat reach map with an atlas grid) is out.** Transit (metro diagram) looks good but doesn't make sense to use; parked.
  Lens is a good idea but unusable as built; parked.
- **Camera**: holding the left mouse button and moving up/down must tilt the view freely through the full range. Every
  prototype so far clamped the tilt, and that "drives him crazy". Fix in any future prototype and in the real map.

## The lead: every star gets a shape

From Domains (where the cells were only meant to make picking easy): the real problem with Hollow's and Elite's maps is
that billions of identical points are always on screen. **Give every star a shape**, so the map reads as a mosaic of
regions, like countries on a map, instead of a cloud of dots. "We're not scientists; it doesn't have to look exactly like
real life." A very good lead, not the whole answer.

Directions that follow from it (not yet built):
- **Hierarchical tiles**: always a readable number of tiles on screen (on the order of 100–300). Zooming out merges stars
  into bigger tiles (clusters → sectors → arms); zooming in splits a tile until it is a single star.
- **Freedom from exact positions**: tiles may be nudged or straightened so they stay readable (the way a cartogram or a
  metro map bends geography), as long as neighbours stay neighbours.
- **Territory falls out for free**: owned tiles are filled with the owner's colour; contested and frontier tiles are marked.

## Floors: promising, with one problem

Floors (10 ly layers with a floor picker) looked "just as promising, even like an implementation of the shapes idea".
Problem: you can't sweep the pointer across every star; stars on other floors are out of reach. Any flattening must keep
every star reachable (e.g. a slidable/thickness-adjustable height band instead of hard floors, or height shown on the
tile rather than hiding it).

## Open: the map follows the game

The owner stepped back: the map can't be finalised before deciding what the player does in space (how many systems a
player really uses, whether controlling space is a mechanic, how travel should feel). Resume map work from those answers.

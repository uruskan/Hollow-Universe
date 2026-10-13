# Concept Domains: one cell per place

The view is divided into domains, like countries on a board game. Every star that has earned a domain owns the part of
the view nearest to it: a Voronoi partition of the projected stars (d3-delaunay 6.0.4 from jsDelivr). Far out, only
places earn a domain: your space, stations, bases, faction systems and bright named stars. So the cells are few and big,
and the rest of the stars are a faint texture. Zooming in refines the partition as more stars earn a domain. Controlled
systems colour their domains in their owner's colour, so territories become countries with borders. The view is top-down
by default, and you can still tilt it.

This is a first playable version, forked from concept AB. Layout, search, inspector, routes and both system views are
unchanged.

## How it solves

**Readability.**
- Only the stars that own a domain are drawn crisp and can be named. The rest dim to texture.
- Who earns a domain is decided by rank, then a zoom gate, then spacing (at least 34–46 px apart), then a budget of
  about 150 on a 1600 × 900 view at 100 ly.
- Filters and "Ask the map" results reshape the board. Only matching stars earn domains, so a filter is also a
  selection aid.

**Selection.**
- There is always exactly one star under the cursor: the one whose domain holds it. There is no snap radius and no
  chooser.
- The hovered domain lights up as a soft polygon, and a dotted leader runs from the cursor to its star.
- `Tab` steps through the other stars inside the hovered domain. The hover card says how many there are; zooming in
  splits the domain.

**Territory.**
- A faction system colours its domain out to its claim radius. Neighbouring claims meet on straight borders, and a claim
  facing the wild ends in an arc.
- Each country has a crisp border just outside its fill, with faint province lines inside.
- Contested systems are hatched in both claimants' colours, with a dashed amber border.
- Frontier systems are outlined with dashed mint lines: land you could claim. The hover card says "Frontier · free to
  claim".

**Frame rate.** Domains are re-dealt only once the view has held still for 150 ms. While the camera moves, the same
stars keep their domains and only the geometry follows them, at 60 fps or more in testing.

## Controls

The controls are concept AB's (see `../concept-ab/NOTES.md`), with these differences:
- **Hover** lights the domain under the cursor.
- **Click** selects its star.
- **`Tab` / `Shift+Tab`** step through the stars inside that domain.
- **`V`** or drag tilts the view. Top-down is the default; the deep link `top=0` starts tilted.
- **Deep link `mouse=x,y`** puts a simulated cursor at that point for screenshots.

## What's mock

- Stations, bases, danger zones, resources, the ship and the whole territory layer (`territory.js`, same data as
  concept AB) are mock.
- The frontier claim radius (4.5 ly) and the domain reach (26 ly) are this concept's own choices.

## Files

- `domains.js` (new): who earns a domain, the Voronoi, drawing domains and territory, and picking.
- `stars.js`: adds site emphasis (`setSites`, `setSiteBlend`), a margin projection (`near`), and labels for domain
  owners only.
- `app.js`: AB's app with these changes:
  - the signed-distance territory plane and stems are replaced by domains;
  - adds `rankStars`;
  - picking and `Tab` go through domains;
  - top-down is the default.
- Shots: `../shots/concept-domains-1-far.png`, `-2-territory-hover.png`, `-3-route.png`.

## Rough edges (first version)

- The mesh is faint at far zoom.
- At far zoom, countries look like clusters of round claims.
- The frontier's dashed outlines are busy next to your space.
- During a long fly-in the domains are those of the previous view until it settles.

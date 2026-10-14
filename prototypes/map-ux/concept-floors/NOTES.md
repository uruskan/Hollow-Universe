# Concept Floors: the neighbourhood as a building

**First playable version.** Forked from concept AB. Layout, search, inspector, routes and both system views are AB's.
Only the star field, the territory and the Neighbourhood overlays changed.

## The idea

The neighbourhood is cut into horizontal floors: slabs 10 ly thick, parallel to the galactic plane. Floor 0 is
centred on your home base. Every star is drawn flat on the middle of its own floor, so the floor you look at is a
clean floor plan seen from a high, calm angle.
- Stars one or two floors away are faint ▲ (above) and ▼ (below) ghosts. Everything further away is hidden.
- A floor picker on the right moves between floors, and the camera rides up or down like a lift.
- Each row of the picker shows what that floor holds: territory, stations, bases and danger.

## How it solves the three problems

- **Readability.** One floor holds about 1,800 of the 37,409 stars, and about 280 at the default zoom. Each sits
  on one flat plane, so nothing floats in front of anything else.
- **Selection.** Only the active floor can be picked. Stars on a floor are about 6 ly (about 70 px) apart, so the
  cursor snaps to the right star and the "too close to call" chooser almost never opens. You still get AB's
  forgiving picking, Tab and the chooser.
- **Territory.** Every claimed system holds a sphere. Each floor draws its cross-section: a crisp 2D border with a
  fill, named per floor ("Your space · 3 systems on this floor"). Danger zones and your jump range are drawn the same
  way, as hatched or dashed cross-sections.
  - The raider zone 40 ly above home now shows only on floors +30 to +50. In AB's 3D view it looked as if it sat on
    top of your home.

## Controls (added to AB's)

| Input | Does |
|---|---|
| Floor picker (right) | Click a floor, scroll over it, or use the arrows. Hover a floor for what it holds. |
| `E` / `Q`, Page Up / Page Down, Shift + scroll | One floor up or down |
| `C` / `H` | Centre on me, which also returns to your floor |
| Ghosts on/off | The picker's footer button shows or hides the ▲ ▼ ghosts |
| Click a route's ▲ +20 lift | Go to that floor |

Selecting anything (search, list, route step, chooser) jumps to its floor. The autopilot rides the floors with the
ship. The place card shows the star's floor.

Deep links add `floor=<n>` (floors above yours), `ghosts=0` and `tip=<n>` (open the picker card for a floor).

## Routes across floors

The route runs through the floor plans and climbs between them. Each floor change gets a mint lift marker, such as
"▲ +20". The picker draws the route as a rail through the floors it visits, with a dot on each floor where it stops.

## Mock and open ends

- **Mock:** the same as AB (territory, stations, bases, danger, ship), plus the floor grid. Floor 0 on the home base
  is a design choice.
- Stars are drawn at their floor's mid-height, up to 5 ly from their true height. Jumps, ranges and the autopilot
  still use the true positions.
- **Not done yet (first version):**
  - dimming the route on other floors;
  - choosing the floor thickness (5, 10 or 20 ly);
  - a picker summary for floors scrolled out of view;
  - the ghost density and size still need tuning.

## Files

| File | What changed |
|---|---|
| `stars.js` | New. Slices into floors (`setFloors`), draws dots and ghosts, and picks and labels on the active floor only. Otherwise AB's interface, see its header. |
| `app.js` | AB plus: the floors state and lift animation, the picker, the floor-plate shader (grid, territory, danger and jump-range cross-sections), labels per floor, route lifts, floor-aware selection, keys and deep links. |
| `index.html`, `style.css` | The picker, its card, and the ghost and lift labels |
| `territory.js` | AB's mock territory, unchanged (regenerate it with `concept-ab/tools/make-territory.js`) |

Screenshots are in `../shots/concept-floors-*.png`: your floor, another floor, the picker card, a selection,
territory, and a route across floors.

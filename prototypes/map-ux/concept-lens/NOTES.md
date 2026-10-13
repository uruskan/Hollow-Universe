# Concept Lens: focus plus context

**First playable version.** Everything except the star field and the territory is concept AB, unchanged: layout,
search, inspector, routes and both system views. Open `index.html` by double-click (three.js comes from jsDelivr).

At the Neighbourhood level the 37,409 stars are no longer drawn one by one. They become a calm, low-contrast
density glow, so you can see where stars are thick or thin. On top of that sit only the key places: your space,
stations, bases, the contested border, the selection, the target and the route. A circular lens (240 px, with a
thin ring) follows the cursor. Inside it the cloud is spread apart by a smooth fisheye. The magnification adapts so
stars sit about 36 px apart, from ×1.6 to ×5. Each star has its name, a height stalk to the plane through your ship
and a big pick target. You read one patch at a time, and outside the lens nothing competes for attention.

## How it solves

- **Readability.** Outside the lens the field is one soft haze plus a handful of crisp marks, and only those marks
  have names (14 at most).
  - Inside the lens, up to 11 names are placed and never cross the rim. Your space, the selection and the route
    are named even when they sit on the rim.
  - Stalks show height: solid when a star is above your plane, dashed when it is below. Only the 34 brightest stars
    in the patch get one, plus the hovered, the selected and owned stars.
  - The compressed rim of the fisheye stays faint. It is context, not something to read.
- **Selection.** The lens moves on a leash. Inside half its radius it holds still, so the spread-apart stars don't
  move while you aim. Push past that and it is pulled along.
  - Each star in the lens has a 30 px target, and its name is clickable too.
  - The hover card sits beside the lens, never over it.
  - Concept AB's chooser and `Tab` cycling remain for the rare stars that still line up.
  - Outside the lens, only the key places can be picked.
- **Territory.** Outside the lens, owned regions are soft tints that fade across the border, with no line. Your
  space is a little stronger. Region names stay on the map.
  - Inside the lens the same regions are crisp, with borders and the contested hatching.
  - Every star carries its owner: a ring in the owner's colour (dashed amber where contested, faint dashes on the
    free frontier), and an owner chip on its name. Your outposts show their names, such as "Outpost Ember".
  - The stalks land on the territory floor.
  - The bezel under the lens says whose space you are over, for example "Your space · held by you · 409 stars ·
    ×3.2".

## Controls (in addition to concept AB's)

| Input | Does |
|---|---|
| Move | The lens follows on a leash and settles when you slow down |
| `L` | Lock the lens on the selection, or on the hovered star; it then follows the selection. `L` again releases it |
| `Alt` + scroll, `[` `]` | Resize the lens (140–520 px) |

**Deep links** (on top of AB's):
- `lens=<star>` or `lens=<x>,<y>` places the lens. `x` and `y` are fractions of the window, or pixels.
- `lensr=<diameter px>` sets its size.
- `lock=1` locks it on the selection.

Example: `#level=neighbourhood&lens=0.4,0.36`, or `#level=neighbourhood&select=Sirius&lock=1`.

**Screenshots:** `../shots/concept-lens-1-calm.png` to `concept-lens-5-route.png`. They show the calm default, the
lens over a dense patch, the lens over your space, the lens locked on Sirius, and a route with the lens locked on its
destination.

## How it is built

- `stars.js` keeps AB's interface and adds the rest:
  - **Glow:** every star is splatted into a float target at one sixth of the screen size, then drawn as one haze.
    The haze saturates softly and its gain is levelled for zoom.
  - **Key places:** a points layer that draws only the stars the app marks.
  - **Lens projection:** which stars lie under the glass, and where the lens shows them.
  - **Picking:** lens-aware.
- `lens.js` draws the lens in three layers:
  - **WebGL glass:** a screen quad that ray-casts every pixel back through the inverse fisheye onto the plane through
    your ship. That gives the crisp territory, distance rings, jump bubble and danger shells, lined up with the stars.
  - **2D canvas:** the stars, stalks, owner rings and the route through the lens.
  - **HTML:** the ring and the bezel.
- `app.js` is AB's with these changes: soft territory outside, lens-aware labels and markers, `L`, resize, the hover
  card beside the lens, and the deep links.

## Mock

- Everything AB marks as mock still is: territory, stations, bases, danger, resources and the ship.
- The "held by you" wording in the bezel is mock too.

## Rough edges (first version)

- Around Sol the lens magnifies about ×3.2, and "2 stars here" can still appear for stars on nearly the same line
  of sight.
- The bezel can overlap a route label just below the lens.
- Galaxy and Region levels are unchanged from AB, and have no lens.

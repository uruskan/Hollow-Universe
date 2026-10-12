# Concept B: Zoom levels

**The idea.** One continuous zoom, four clearly different levels, each showing only what makes sense at its
scale (semantic zoom).
- **Galaxy:** the tilted Milky Way, with the centre, bar and arms named and a "You are here" pin.
- **Region** (about 5,000 ly): Hollow's sectors as soft named tiles, star density as a haze, and the arms as
  ribbons named along their path.
- **Neighbourhood** (about 100 ly): real stars as icons, labels by priority, the jump bubble, distance rings
  around you, filters and routes.
- **System:** a calm orrery with log-scaled orbits and key facts on every body; landable bodies are marked.

You always know where you are: the breadcrumb (`Milky Way › Orion Spur › Nolcas Peris › Sol`) and the zoom rail on
the left both follow the zoom continuously. Clicking a crumb or a rail stop flies back to that level. Layers
crossfade at the thresholds; nothing pops.

Open `index.html` by double-click. It needs internet once, to load three.js from jsDelivr.

## Use

| Input | Does |
|---|---|
| Scroll / pinch | Zoom. At the Neighbourhood level it zooms toward the star under the cursor; keep scrolling into a star to open its system. Scroll out of a system to leave it. |
| Drag | Orbit. Tilt is limited; it stops before edge-on. |
| Right-drag or Shift-drag | Pan (Region and Neighbourhood) |
| Hover | Mini card |
| Click | Inspector on the right: facts, then **Route · Target · Favourite · Open** |
| Double-click | Fly to a star; a second double-click opens its system |
| `Esc` | Back out: closes search, deselects a body, leaves the system, closes the inspector, then goes up a level |
| `1`–`4` | Galaxy, Region, Neighbourhood, System |
| Arrows or WASD | Orbit and tilt |
| `+` / `-` | Zoom |
| `T` | Top view |
| `C` | Centre on me |
| `R` | Plot a route to the selection |
| `F` | Favourite |
| `Enter` | Open the system |
| `Space` | Start or stop the autopilot |
| `/` | Search (secondary) |

Filter chips (Neighbourhood level): Reachable · Fuel · Stations · Landable · Earth-like · Bases · Danger.
- Each chip has its own colour and shows its count.
- Several chips combine as OR: a star takes the colour of the rarest chip it matches. Everything else dims, and
  only matching stars keep their labels.

**Routes** run A* over the real 37,409 systems. The search state is (system, fuel):
- Jump range is 12.5 ly; the tank holds 6 jumps.
- The ship refuels at scoopable stars and at stations, but cannot scoop inside the ion storm, where each jump
  costs 2.
- Routes avoid the raider zone when possible.
- The step list marks only the refuel stops you need.
- **Start autopilot** animates the ship along the route. Fuel, the jump bubble and "Reachable" update at each stop.

**Deep links** (used for the screenshots): `index.html#level=galaxy|region|neighbourhood|system`, plus any of:
- `select=<star>` or `route=<star>`
- `body=<body>` (in a system)
- `filters=reach,fuel,stations,land,earth,bases,danger`
- `top=1`, `hover=<star>`, `sector=<name>`, `struct=<id>`
- `yaw=`, `pitch=` (degrees) and `dist=` (ly)

## Real and mock

- **Real**, from Hollow's generator via `../data`:
  - every star within 150 ly (names, positions, classes, colours, planet summaries);
  - body lists within 40 ly;
  - the 250k-point galaxy sample, its glow image, structure labels and arm paths;
  - the 203 sectors.
- **Mock** (`HOLLOW_MOCK`): stations, the home base, other players' bases, the danger zones, resources and the
  ship. The footer says so, and the inspector tags each of these with a small "mock" chip.

## Decisions and known rough edges

- **The Region haze** is drawn from the density model's glow image. It pictures density; the dots are not
  individual stars.
- **Systems beyond 40 ly** have no body list in the data. Their System view is a schematic built from the summary
  counts and is labelled "schematic".
- **Zooming in from the Galaxy always goes to you.** Star-level data covers only 150 ly around Sol, so the target
  eases from the Galactic Centre to your position as you zoom. You can select a far sector at the Region level,
  but its stars don't exist in this prototype.
- **"Reachable"** means within one jump of the ship right now.
- **The inspector shifts the view** into the free area (the frustum widens, so nothing is stretched).
- **Rough edges:**
  - Labels are placed every frame. During fast orbits a label can occasionally swap sides.
  - The route line may run under labels.
  - Below about 1,100 px wide, the chips and rail compact but are not fully tuned.
  - The jump estimate in the inspector is a lower bound until you press Route.

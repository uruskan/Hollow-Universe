# Playable scale and the Earth start

The authoritative requirements are `D:/GameDev/Projects/x/design.md`: GAL-01, SYS-01, WORLD-01 and LOOP-01.
The older scale proposals describe the reference project and do not override these requirements.

`GameScale` keeps generated physical values intact. Its provisional, configurable radius mapping is
`clamp(earthRadius * (realRadius / realEarthRadius)^(1/3), 250 m, 1354.055000514614 m)`.
The default Earth radius is 1200 m, giving about 18.10 km² for the complete sphere. The upper radius is
the square root of 23,040,000 m² divided by 4π, rounded down: no planet or moon exceeds the 23.04 km²
spherical-area limit. Relief is measured above this mean sphere; the requirement is the specified
spherical area, not the sum of microscopic terrain triangles. Sizes and the 250 m floor are tuning,
not new design commitments. Source bodies keep real radius, mass, gravity, atmosphere and orbits.

Hosted and dedicated games start on Earth from the existing authored `SolSystem`. Its definition is
built in, so a missing optional HYG catalogue cannot produce a different starting planet. The legacy
`FirstWorld` remains available to generator regression fixtures and reference tools.
Its historical 2410 m fixture radius is deliberately retained: old firing-lane and movement regression
scenarios depend on that exact terrain. It is not the hosted/dedicated starting world or part of the
playable registry. New movement and flight checks run separately on scaled Earth.

`PlayableGalaxy` selects a fixed seeded subset from the unchanged source generator and includes Sol.
Its default budget is 1000 actual stars, including companions, rather than 1000 multiple-star systems.
The budget is configurable from 1 to 1000. Only listed addresses are exposed by its search and system
lookup. The live galaxy map uses this registry for all dots, labels, search and opening system maps.
Compact planar map positions are diagram units, not light-years and not flight coordinates. The
generator's physical positions and planetary orbits remain untouched. Map layout and population
selection are versioned by `PlayableGalaxy.LayoutVersion`.
The map uses a separate fixed-screen-size symbol shader, a subdued bounded grid and a tilted overview.
Double-click focuses the map camera; opening a system inspects it and does not transport the player.

The ordinary Godot .NET build is sufficient for these planets. At the maximum surface radius, a float
coordinate has spacing about 0.122 mm; even at four world radii it is about 0.488 mm. `ZoneFrame.World`
uses the planet centre, and `RenderFrame` supports a moving origin for large reference views.
Interplanetary travel must keep authoritative locations in double precision and render/solve physics
near the player. The finite map does not implement travel. Local ascent now crosses the atmospheric
threshold without parking and supports descent; the current four-radius outer simulation guard remains.
Interplanetary flight, streaming and server transfers remain separate work.

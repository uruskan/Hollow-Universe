# Hollow Universe AB Test B

Godot 4.7.2 .NET / C# was selected with the owner on 15 October 2026.
`DESIGN.md` is the authoritative design snapshot. The original Hollow Universe is the source of
reused code and assets; this checkout and its user data are independent.

## Run

Double-click `Play.cmd` for the main menu. It builds the client and server and checks asset imports.
From PowerShell: `./Play.ps1 -Mode Host -PlayerName Ada` or
`./Play.ps1 -Mode Join -Address 127.0.0.1 -PlayerName Bea`.
These launcher sessions use port 27025 to avoid the original project's usual port.
Join addresses accept IPv4 or host names; the current client has IPv6 disabled.
The existing `play-ground.cmd` and development viewers remain available.

## Current implementation wave

| Work | Owner | Status |
| --- | --- | --- |
| Independent copy, save/settings isolation, build and launcher | Integration | Implemented; client and release server build cleanly |
| Earth starting world and 23.04 km² whole-surface cap | World generation | Implemented; hosted Earth radius 1,200 m, area 18.10 km² |
| Finite playable galaxy, at most 1,000 stars including companions | World generation | Implemented; default 1,000 stars in 610 systems, rendered map checked |
| Separate Vehicle/Engineering Workbenches and spacecraft unlocks | Survival/progression | Implemented; progression and network authority checks pass |
| Ground menu startup and sensible graphics defaults | Client quality | Implemented; menu and hosted gameplay captured and inspected |
| Continuous ascent/descent through local orbital altitude | Flight | Implemented inside one world; actual Earth scale tested |

## Quality gates and remaining development

The inherited game is a foundation, not a finished implementation of this design. Compilation,
engine-free tests, multiplayer checks and actual rendered captures establish each milestone.
The first connected milestone targets gathering, crafting, building, ship boarding, flight,
two-player interaction, and saving/rejoining. Interplanetary travel must be tested as real continuous
gameplay before being reported complete.

Further work packages include compressed system layout and travel; server handover between worlds;
planetary streaming; surface/space visual transitions; modular ship upgrades; ship capture;
planet-size-dependent monument placement and loot; weather; shared oxygen equipment; electricity; turret authorization;
autominers; research/rare variants; respawn points across worlds; fauna; sound and impact feedback;
save recovery; multiplayer security; load testing; and visual polish. Proposed solo activities in
PLAY-03 remain proposals until selected. Each package must integrate with shared systems and be
validated before adding its full content catalogue.

## Coordinate decision

Compressed travel distances do not require a realistic astronomical simulation. Existing physics
uses planet-local and ship-local positions. Keep ordinary Godot and preserve that separation.
Double-precision world metadata can remain plain C# data without changing the Godot engine build.
The choice of coordinate frames is an implementation detail, not an extra player-facing travel mode.

## Workbench progression

CRAFT-01 through CRAFT-05 now use the existing shared interaction, crafting, and blueprint systems.
The Vehicle Workbench has a separate, unlevelled tree containing the Tin Finch. Learning the ship costs
salvage; crafting it still requires a Vehicle Workbench. A standard level-3 workbench cannot substitute
for the vehicle station, and its speed bonus does not accelerate specialized assembly. The tech-tree
screen keeps its existing layout and opens the particular bench used, including when several different
benches are nearby. The server validates the actual interaction target before charging salvage.

The Engineering Workbench is craftable and placeable, uses its existing owned mesh and icons, and has
its own empty tree. Its technologies remain undefined in the design; no engineering catalogue has been
invented. Both specialized benches remain level zero. The three standard workbench trees remain available.
Existing item IDs and keys remain stable; Engineering Workbench adds ID 172. Display names now match
Stone Axe, Stone Pickaxe, Research Bench, and Vehicle Workbench in the design.

Provisional balance, subject to playtesting: the Tin Finch unlock costs 500 salvage; an Engineering
Workbench takes 45 seconds at standard workbench level 2 and costs 500 metal fragments, 20 refined metal,
and 200 wood. Its placement half extents are 0.89 × 0.939 × 0.463 metres, measured from the supplied
engineering bench mesh, with 500 health. These gameplay numbers are implementation tuning, not confirmed
design decisions.

Validation added: exact one-rock/one-torch starting and respawn inventories; reconnects do not duplicate
the kit; separate learning, station, and speed requirements; physical blueprint learning; client/server
confirmation and rollback of an unlock sent to the wrong bench. Existing ship assembly coverage now
includes the knowledge gate. Validation results are recorded below after the integration build.

## Validation log — 15 October 2026

- Installed tools: Godot 4.7.2 .NET, .NET SDK 10.0.302. Standard engine build.
- Solution Debug build and dedicated server Release build: zero warnings and zero errors.
  Eight inherited C# warnings were repaired without changing gameplay rules.
- Focused integration/regression run: 17/17 passed, including exact client/server ship prediction,
  Earth-scale movement and ascent/return, legacy scenario compatibility, and content validation.
- Full engine-free suite: **298/298 passed in 358.0 seconds**, including simulation, persistence,
  content, network authority, client prediction and new design-specific checks.
- Godot asset import succeeds. Real rendered captures at 1600 × 900 / High verify the survival menu,
  hosted Earth, finite galaxy map and Earth's system-map details.
- `Play.ps1` was run end to end: both builds, asset import and main-menu startup succeeded.
- Hosted server identifies Earth as body `8000000000000000/10300000`, radius 1,200 m, gravity 1 g.
  Restarting and rejoining the review save wakes the same character with its saved inventory.
- The first gameplay capture exposed an inherited muzzle-smoke warmup leak. Preview effects now have
  their own temporary owner; the fresh gameplay capture confirms the stray puff is gone.
- Known cleanup issue: hosted-game shutdown reports seven Texture RIDs and two ObjectDB instances
  still alive. Gameplay capture exits successfully without runtime exceptions; this is not a claim
  that all renderer resource cleanup is complete.

Review captures are under `docs/images/abtest-b/`; detailed transient build, import, server and test
logs are in ignored `.artifacts/`. These checks establish this development wave, not completion of
the full design or a production load/performance certification.

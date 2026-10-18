# Admin, clan-base and crafting-HUD validation — 16 October 2026

The shared Debug build at 19:49 passed with zero warnings/errors. The coordinated
foundation run at 19:52 passed **64/64**, including all five clan-base tests and
all six camera tests. The repaired network fixture now verifies occupied F1
refusal, successful replication to two clients, and persistence after restart.

At 20:26–20:28 local, the exclusive standalone visual slot completed seven jobs
sequentially. The full UI check passed **67/67**, including the 16 crafting-HUD
checks; the focused UI check was deliberately not duplicated. All six requested
screenshots were opened and inspected. No hosted tour, build, import or Git
operation was run in this slot.

## Evidence and resource guard

All seven processes exited 0. Every preflight had at least 5.5 GiB available;
the lowest start was 5.900 GiB. Across all jobs, free memory stayed at or above
5.029 GiB and commit at or below 59%. Durations were 4.31–8.07 seconds, below the
120-second bound. The final process check found no remaining Godot, Hollow,
dotnet or MSBuild process.

The actual loaded client DLL was
`game/.godot/mono/temp/bin/Debug/HollowUniverse.dll`, SHA-256:

```text
6E9F640EB671E2A84002B3DE500D4DE213141BA5193BD677F86778A4AE7EC407
```

It and the client/simulation dependency DLLs were unchanged across every job.
Each receipt contains their hashes, arguments, exit status, memory samples and
stdout/stderr paths. `graphics.cfg`, `ground-settings.cfg`, `ground-ui.cfg`,
`settings.cfg` and the presence/absence of `console-bindings.cfg` were unchanged;
none needed restoration. Settings contents are not included in this report.

The [summary](../../.artifacts/admin-validation/summary.json) links all seven
receipts. Their run directories are:

| Job | Directory under `.artifacts/admin-validation` | Result |
| --- | --- | --- |
| Full UI check | `20261016-202622-ui-check` | 67/67 |
| Four remaining | `20261016-202639-hud-four` | 1600×900 image inspected |
| Three remaining | `20261016-202700-hud-three` | 1600×900 image inspected |
| Narrow HUD | `20261016-202714-hud-narrow` | 1024×768 image inspected |
| Base exterior | `20261016-202729-clan-exterior` | 1600×900 image inspected |
| Shooting floor | `20261016-202753-clan-floor` | 1600×900 image inspected |
| Roof | `20261016-202814-clan-roof` | 1600×900 image inspected |

## Visual findings

- [Four remaining](../images/crafting-hud/crafting-four.png): the card is left of
  the vitals, clear of the hotbar; bandage icon/name, ×4, inventory 5 and progress
  are readable.
- [Three remaining](../images/crafting-hud/crafting-three.png): ×3 and inventory
  6 are visible together, with the existing +1 pickup feed above the vitals.
- [Narrow HUD](../images/crafting-hud/crafting-narrow.png): the card moves above
  the hotbar/ammo line and remains left of the vitals without overlap or clipped
  text. Counts and icon remain readable at the reduced UI scale.
- [Exterior](../images/admin-clan-base/clan-base-exterior.png): the large layered
  shell, covered window gallery, honeycomb perimeter and sloped roof cover render.
- [Shooting floor](../images/admin-clan-base/clan-base-shooting-floor.png): solid
  standing tiles and covered windows surround an open triangular peek-down recess
  with visible ground below. This agrees with the passing downward-ray test.
- [Roof](../images/admin-clan-base/clan-base-roof.png): roof fighting space,
  parapets, sloped cover and the enclosed stair exit render coherently.

The offline sandbox's placement/tool overlays remain visible in its screenshots;
their “Too far away” or “Something is in the way” messages describe the active
building-plan preview, not a preset spawn failure. Each base run reports 228
pieces, stone grade and zero collapsing pieces.

The three base runs each report **30 ObjectDB instances leaked at exit**. They
still exit 0 and produce the inspected images, but this cleanup warning remains
an open finding. No allocation/texture-decode error, failed assertion or memory
guard stop occurred. The separate hosted F1 visual tour has not run; actual player
navigation through the base is not established by these offline captures.

The heavy slot is released. Production source and existing camera staging were
not changed; commits still require the coordinator's Git slot.

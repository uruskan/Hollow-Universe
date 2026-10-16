# P4 First-person tools and optics

Repair N2 pickaxes, N7 size consistency, N8 holo size, R8 and remaining L16/L18/L22 quality.
Read COMMON.md, REPAIR_BACKLOG.md, HELD_ITEMS.md and screenshots 8/9/10. The owner explicitly
says the axe animations are now great. Preserve that accepted motion; do not restart the axe
animation programme. Pickaxes have wrong grip/angle/apparent reach, and other tools overwhelm
the view. Holo functionality was verified earlier but its size/art is now rejected.

Ownership: `game/scripts/Characters/` presentation/editor code, held-presentations.json,
first-person character/item/optic shaders, `game/scripts/Items/GunAttachmentPresentation.cs`,
`ScopeLensView.cs`, attachment-specific art/assets after measurement, associated diagnostics.
Do not edit `game/scripts/Ground/FirstPersonView.cs`, ThingsView, combat identity/timing/network
paths or weapon physics: active P5 work owns these. Coordinate a narrow event-interface patch
if required. Shared models.json and graphics preference files need reserved ownership.

Suggested agents: pickaxe source motion/grip/contact composition; catalogue-wide projection/
framing and workshop usability; holo mesh/mount/eye relief/ADS framing. Divide exact files
within Characters and held-presentations before edits; one agent owns the JSON integration.

The project already has separate vertical viewmodel FOV (60 default, 40–100 range) and 0.12
depth compression. The metal-hatchet pilot adds a complete-rig offset; other tools did not
inherit it. A professional solution combines consistent projection and authored full-rig pose/
motion. Do not move the world camera or shrink third-person assets to solve first-person size.
Depth compression can preserve apparent size and is not a general resizing control. Sources
and reasoning are in REPAIR_BACKLOG.md. No additional camera pass is presumed necessary.

Acceptance: accepted axe regression first; pickaxe carry/equip/full swing/actual hit/miss,
correct working-tip orientation and hands, close/normal/out-of-range targets; both bodies,
multiple aspect ratios, hip/ADS transitions and all touched action cycles. Keep server reach,
damage and strike timing unchanged. Holo must leave useful screen space while preserving
reticle alignment, readable aperture, mounting, eye relief and hands; verify remote view too.
Measure screen composition and capture actual gameplay. Reproduce workshop drag with real
mouse input where available; injected UI events alone do not close the original usability report.
Request build/import/GPU slot. Write `docs/repair-results/P4-first-person.md` with review media.

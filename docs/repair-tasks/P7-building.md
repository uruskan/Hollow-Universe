# P7 Roofs and remaining building cases

Implement N10 and complete L2/L4–L14/L24 checks under COMMON.md and REPAIR_BACKLOG.md.
Read REPAIR_BUILDING.md and screenshot 11. The owner wants roofs without attached side
panels, able to join and stack side by side. The panels are authored in mesh and collision.

Initial ownership: building-kit geometry exporter, shared PieceShapes, roof assets and
building geometry/placement tests. Reserve placement handlers, catalogue entries, interaction
verbs and UI through coordinator. “Add F1 build grade and free crafting” currently edits
placement/crafting/admin/protocol paths; “Audit building asset library” is surveying assets.
Reuse their work and inspect latest diffs before extending these areas. P8 owns gib runtime
settings, while you own regeneration of fractures matching changed roof meshes.

Suggested agents: roof mesh/collision/LOD changes; remaining fit/snapping/support cases;
live scenario diagnostics and small interaction gaps. Assign exact files first.

Remove the unwanted side panels from matching visible/collision shapes and regenerate roof
gibs. Verify neighbouring/stacked square and triangular roofs, rotations, all grades/LODs,
support and walk/shot collision. Avoid invisible gable walls, gaps or unrelated geometry loss.

Carryover: meaningful positional snapping for deployables near walls/objects (currently absent),
flexible first foundations on uneven terrain, alternate supports, floor joints/twig shimmer,
window interiors/stairs, readable door opening direction, three-triangle/middle-garage code
lock, close campfire/workbench fit and ghost-to-real transition. Naming beds already works;
add the requested optional Give to friend entry without inventing an unapproved ownership
transfer system. Preserve corpse/health-bar and attachment interactions.

Acceptance uses normal hosted placement/input for the exact cases above, plus authority/
network/persistence/support tests. A hut exterior or static mesh contact sheet does not close
all cases. Include owner-visible refusal reasons and shared scalable placement rules.
Request build/import/Blender/Godot slot. Write `docs/repair-results/P7-building.md`.

# Building repair — 15 October 2026

The historical L2/L4–11 report is evidence of symptoms, not a completed-fix checklist.
This batch changes the shared engine-free placement/stability rules and the actual building meshes.

## Placement and support

- **L2:** foundation depth is 3 m rather than 1 m. The first foundation's top follows the aim,
  bounded by the highest and lowest sampled terrain beneath it; corners still need ground support.
  Existing half-storey up/down sockets remain. The footing geometry and collision agree with that depth;
  the walking cap and horizontal footprint are preserved. This permits uneven sites, not floating foundations.
- **L4:** free-foundation heading follows the actual view rather than rounding to 15 degrees.
  R still rotates 90 degrees. The existing wire pose yaw carries the heading at 16-bit precision.
  Occupied sockets are excluded from candidates instead of retaining heavily penalized candidates that
  prevent a free-foundation fallback. Empty nearby sockets still provide deliberate attachment snapping.
- **L5:** a solid square or triangle floor recognizes every wall top bearing on at least 25 cm of it,
  even when it is only part of a wall and not an exact full-edge socket match. A corner, wall side or
  empty floor-frame centre provides no added bearing. Stability still follows the strongest path from
  grounded foundations; removing all supports collapses the floor.
- **L9:** the shared attachment sight check permits the target door's own leaf between the player and
  its lock socket. Another object's obstruction is still rejected. A lock remains limited to one per door.
- **L10/L14:** measured workbench half extents replace inherited oversized boxes: tiers 1/2/3 use
  (0.840,0.947,0.404), (0.925,0.934,0.479), (0.930,0.980,0.481) m respectively.
  Their height now also matches the visible mesh. They use the existing shared support-relative rotation
  rule on building floors (R: quarter turn), and free rotation off a building. Bodyless deployables use the
  same 1 cm contact allowance as physics, so a touching neighbour is not an intersection.
  Arbitrary positional snapping to every wall/object is not implemented by this batch.

Facepunch's [Devblog189](https://rust.facepunch.com/news/devblog-189) documents half-height foundation offsets;
[Devblog85](https://rust.facepunch.com/news/devblog-85) discusses placement on uneven ground.
Our 3 m footing is a deliberate local tuning value, not a claimed Rust constant. Their
[floor support change](https://commits.facepunch.com/428244) also distinguishes wall contact from support;
our added support requires the wall's actual top, not its side.

## Presentation and source meshes

- **L8:** door ghosts show the real hinged leaves partly open, so both hinge and swing side are visible.
  R flips the complete candidate. The garage ghost has a raised arrow on its operated face.
- **L11:** a pending hologram ends when its reliable server result arrives. It no longer remains for
  half a second over the real mesh. The active preview is hidden over its pending copy or an occupied
  door/lock/socket, and stays hidden at the last placed pose until the aim changes (or the server rejects it).
  Under packet delay there can be a short gap between acknowledgement and replication;
  an accepted placement is not drawn twice.
- **L6/L7:** direct geometric audit found overlapping coplanar panels, including roughly 2–3 m² on the
  stone floor meshes, and partial overlaps in every non-twig window. Exact matching vertex triples alone
  did not describe the problem. `tools/repair_building_glbs.py` subtracts prior coplanar polygons from
  later triangles, interpolating the original UVs/normals. Reversed back faces are kept. It compacts
  superseded buffers and preserves material slots, LOD keys, node transforms and collision-node names.
  The normal Blender kit converter now applies the same repair after export. No external game asset is used.

The script stages under `.artifacts/building-repair`; root coordinates installation/import and screenshots.
The source asset is the user's `PSG_BuildingKit_R01`, already vendored in this project.

## Verification

Root's focused run passed 17/17, including all four new `BuildingRepair` regressions, the sloped-foundation
aim test, three building network tests, and unrelated audio/environment tests in that run. The new cases
cover retaining a triangle's second wall support, denying corner/side/frame-hole support, free-heading
wire agreement, garage locks from either side between other doors, and a bench that the old oversized
box rejected. The network test checks that an accepted pending ghost is gone when the real piece arrives.

`BuildingFitTests` additionally checks every foundation LOD reaches the configured footing depth.
Final GLB validation, Godot imports and visual captures are coordinated by root; do not treat the focused
simulation tests as proof of the final rendered appearance.

The five repaired GLBs are installed. Structural validation checked all mesh indices/attributes, finite
positions, unchanged node names/transforms and all 40 foundation mesh/hull depths. All normals are unit
length within 1.53e−7. Across all grades and LODs, clipping raised the triangle count from 1,398,290 to
1,411,605 (+0.95%). Reports are `docs/reports/building-mesh-repair.json` and
`docs/reports/building-mesh-validation.json`.

## Rendered joint review

The coordinated build17 tour completed in 18.1 seconds with exit 0, isolated 1600×900 input,
no runtime errors and no captured runtime-input hash drift. All 73 expected JPEGs decoded at
their expected dimensions: nine joint views across five grades, five grade sheets, five change
maps, sixteen walking frames and two walking sheets. They are preserved unaltered under
`docs/images/abtest-b/repairs/building/`, with two labeled overview sheets,
`building-joints.log` and `building-evidence.json` containing source/image/runtime hashes.

Every grade sheet, change map and both walking sheets was reviewed. Wood, stone, metal and
armoured joints look continuous; the repaired broad interior panels do not exhibit large
changing patches in these views. The identical-camera control changed zero pixels. The
0.3mm camera-offset experiment still counted 134,693 changed pixels across the 45 views;
93,684 belong to twig. Most highlighted pixels follow thin branches, silhouettes, panel
edges and top trims. This is consistent with strong edge aliasing, but the images alone do
not rule out every remaining coplanar overlap. Twig shimmer remains a known visual limit;
this result must not be described as zero flicker.

The tour uses authored LOD0 and covers the listed foundations, walls, floor junctions,
corners, triangle junctions and doors. It does not validate every LOD or window, stair and
roof variant. Capture completion is distinct from functional collision/support tests and
does not establish performance.

The first run timed out because the sandbox terrain renderer requested a square tile set,
then evicted a still-requested corner tile outside its circular retention distance. Keeping
requested tiles fixed the readiness loop. Planet-frame corrections in the capture fixture
also replaced global X/Z height queries and global Y offsets; the captured groups are now
positioned relative to their local planetary up vector. These fixture fixes do not alter
the building assets or authoritative generation.

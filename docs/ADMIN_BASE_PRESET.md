# Admin 3×3 clan-base preset

The owner requested replacing the F1 Tools hut spawn with a large Rust-style base on
16 October 2026. This is an original layout made from Hollow Universe's ordinary
building pieces; no Rust models, textures or other game files are imported.

## Layout and behavior

The preset has a 3×3 square core, two enclosed storeys with triangular honeycomb,
a protected central cupboard room, a triangular two-door entrance airlock, a stairwell,
a covered shooting level, four open recesses for looking down along the walls,
and a roof with parapets, sloped cover and an enclosed stair exit. It is a structural
shell for building/raiding practice, with room to furnish; it does not spawn loot,
turrets or separate external tool cupboards.

F1 Tools offers all five structural grades. The selected grade applies to every
piece, with wooden, sheet-metal or armoured doors appropriate to the tier. Ordinary
ownership, cupboard authorization, collision, damage, stability, replication,
upkeep and persistence apply. This does not claim the raid cost or exploits of any
particular Rust design.

The layout is authored as a socket-linked JSON blueprint. The engine-free loader
uses the shared building geometry and stability rules, validating the complete
layout before insertion. Spawning checks the whole footprint for dry, sufficiently
flat ground and existing obstacles before changing the world. A refusal leaves no
partial base. The small `TestBases` fixture remains available to existing raid tests
and tours.

## Reference research

- [Rust Daddy's El Padre walkthrough and creator transcript](https://www.corrosionhour.com/el-padre-clan-base-design-2020/):
  layered core protection, honeycomb, a covered shooting floor, internal/external
  peek-down positions and sloped roof battlements informed this smaller layout.
- [Rust Daddy's Colossus walkthrough and creator transcript](https://www.corrosionhour.com/rust-colossus-clan-base-design/):
  roof fighting positions and protected access informed the stair exit and cover.
- [Facepunch devblog 35](https://rust.facepunch.com/news/devblog-35): structural
  support is an explicit building constraint. This preset is checked against this
  project's own stability rules rather than assuming Rust's values.

These references describe design principles. Their layouts and assets are not
copied, and their older game balance is not treated as current Hollow Universe behavior.

## Validation

The authored layout has 228 pieces. Four peek-down frames sit between honeycomb
bays, above open ground; the solid gallery tiles beside them provide standing
positions and the exterior windows provide cover. Their open floors need the same
care as any ordinary floor frame.

Five tests cover layout/support at every grade, invalid blueprints, occupied sites
and downward clearance, submerged/steep sites, and admin authorization plus network
replication and persistence. The first four passed in the coordinated 19:29 run.
The initial network/save test was refused because it assumed the default 26 m F1 target
was clear, beyond the stand-in nature's 9 m arrival clearing. Its fixture now
depletes solid nature around that test target, checks an intentional player
overlap refusal through F1, then moves the guest clear before spawning. Production
overlap protection is unchanged. After the shared rebuild, all five base tests
passed in the 19:52 foundation run (64/64 overall), including replication and save
reload. All six camera tests passed in that run too.

The coordinated 19:49 build passed with zero warnings/errors after the fixture
repair. The earlier memory-stopped build remains recorded separately. Source
review and `git diff --check` completed. Exact validation commands are in
[the validation handoff](repair-results/Admin-validation-commands.md).

Offline visual fixtures, captured and inspected during the exclusive 20:26–20:28
slot; the hosted tour remains pending:

```text
--view build --scene clan --grade 2 --shot clan-base.png
--view build --scene clan --from 0,7.6,1.6 --at -1.5,6.3,6.3 --shot clan-floor.png
--view ground --host --name ClanCheck --spawn origin --tour ../docs/images/admin-clan-base --tour-steps clan-base
```

The offline fixture uses the exact server blueprint. The hosted tour finds a dry,
level site, submits the normal F1 spawn command, checks replicated pieces/doors/
cupboard, and captures the exterior, shooting floor, roof and F1 Tools tab. Use an
isolated save for the hosted run.

The exterior, covered shooting gallery/open peek-down recess and roof fighting
space were inspected in three offline images. All show the 228-piece stone
preset with no collapsing pieces. The sandbox logs 30 ObjectDB instances leaked
at exit in each capture; that cleanup warning is retained as an open finding.
See [images, receipts and validation limits](repair-results/Admin-visual-validation.md).

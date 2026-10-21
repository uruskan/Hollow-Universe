# P3 Resource nodes and animals

Repair the resource part of N2/R2 and complete R16 animal validation. Read COMMON.md,
REPAIR_BACKLOG.md, REPAIR_RESOURCE_NODES.md, REPAIR_SURVIVAL.md and screenshot 3.

The node package was found at `D:/GameDev/Assets/PSG_ResourceNodes_R01`. Twelve source and
twelve runtime stage hashes match its receipt. It already supplies Stone/Metal/Sulfur/HQMetal,
four LODs per stage. Do not replace it with the older three-type ResourceNodes package.
Decorative boulders currently have no yield. Identify the actual runtime kind/model/collider
behind screenshot-like objects and make harvestable resources readable and available.
The screenshot alone does not prove it depicts a broken ore node.

Initial ownership: resource-specific nature/spawn/harvest paths and tests, resource-node export
tools and node assets; AnimalSystem and directly related frame/attack tests. Reserve shared
`models.json`, `gather.json`, `surfaces.json`, Scatter and NatureView before edits. P1 owns
decorative geology; P2 owns vegetation presentation; P4 owns pickaxe animation. Any contract
changes to shared attack identity/event processing go to the active P5 owner.

Suggested agents: node source-to-runtime/spawn audit; mining interaction/collision/visual
hotspots and tests; animal frame/obstacle attacks and collider validation. Keep files disjoint.

Acceptance: all four ores appear in recorded normal hosted routes; correct tools harvest them,
stages/hotspots/yields match, exhaustion/respawn/restart/remote observation agree. Show clear
distinction from non-resource rocks without silently making every decorative mesh farmable.
Test pickaxe hits/misses jointly with P4 without increasing authoritative reach to hide poses.
Animals cannot attack a player protected on an unreachable foundation; test reachability,
obstacles, height changes and multiple planet frames in real host/guest play, not only fixtures.
Preserve breathing/immunity regressions. Request runtime slot and write
`docs/repair-results/P3-resources-fauna.md`.

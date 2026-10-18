# Isolated Cascadeur character trial

This experiment owns automation in this directory and output only in
`.artifacts/character-trial-cascadeur/`. It reads the preserved Blender trial;
it does not alter `art/character_trial/`, v1, v2, the backup ZIP, or gameplay assets.

Cascadeur 2026.2.3's built-in script server must be running. The client invokes
the official `run_script` MCP tool on loopback and records code, response, and
event-log changes. Execute one reviewed stage at a time, inspect its results,
and do not retry uncertain mutations blindly. Modification callbacks must return
true; wrapper methods returning None need resulting-state verification.

Native checkpoints currently separate imported bind, the first generated rig
with unwanted auto-pose drift, the clean rig without auto posing, and the
unmodified reference chop. `Scene.save(path)` completed and produced files;
`DataSourceManager.save_scene_as(...)` opened a manual save dialog instead.

The current session reports both export and Pro features unavailable. Native
scene saves and individual PNG captures work. No FBX export or license activation
is attempted. Native image-sequence rendering closed Cascadeur after its first
frame on 18 October 2026; avoid that path pending investigation.

These are review artifacts. File integrity, control counts, and tiny pose drift
do not certify natural motion, wrist deformation, finger contact, or planted feet.
The TPS hatchet candidate must be evaluated before expanding to other motions.

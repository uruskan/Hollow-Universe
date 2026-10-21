# Client startup

The AB Test B client opens the existing survival main menu when started normally from Godot or without a
`--view` argument. PLAY starts a hosted game; CONNECT joins a server. The inherited ground interface is unchanged.
`--host` and `--connect address:port` still start a game directly.

Development viewers remain available through explicit arguments after Godot's `--`, including `--view galaxy`,
`--view system --focus Sol`, `--view surface`, `--view ship`, `--view ui`, and the asset viewers. The galaxy map uses
the new finite playable registry; the legacy surface and ship sandboxes remain reference tooling.

On a fresh profile, integrated graphics use Medium and other adapters use High. Detecting a discrete GPU does not
establish that it can run every expensive effect together. Ultra and Maximum remain selectable, and saved settings
are respected. `--quality low|medium|high|ultra|maximum` applies a named preset for the current run without saving it;
names are case insensitive. Unknown values and numeric enum values leave the saved/default quality in effect.

The entry point records the actual default view in crash reports. Project settings give this copy its own user data
directory, so graphics preferences and hosted saves are independent of the original Hollow Universe project.

Effect previews use a separate director owned by the renderer's temporary warm-up holder. Its particles, tracers,
delayed layers, lights, and decals are freed with that holder before play begins; live fires and torches retain their
own director and handles. Muzzle previews explicitly use the small preview scale because the flash renderer removes
parent scaling from muzzle transforms. Preview shakes do not notify gameplay listeners.

## Verification

Build the solution, then run the client at 1600 × 900 with `-- --hour 10 --shot <absolute-path.png>` to verify the
default main menu. Use `-- --host --spawn origin --hour 10 --quality high --shot <absolute-path.png>` to capture the
playable ground after the loading pipeline settles. Both screenshot commands exit after saving. Explicit
`--view galaxy` opens the finite map. Captures of all three views were inspected on 15 October 2026;
the gameplay capture confirms preview effects do not remain visible after loading.

The existing `--load-trace 45 --load-shot <absolute-path.png>` options record the hosted game's startup stages,
shader work, and frame times. A build alone does not verify runtime visual quality; inspect captures and logs too.

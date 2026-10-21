# Functional optics and weapon light — 15 October 2026

Legacy report L17 includes functional attachments as well as the draggable inventory sockets. The socket UI and
authoritative transfers were repaired separately; this package connects the attached models to aiming and light.
No item transaction, attachment compatibility, damage, ammunition, or protocol format changes are needed.

The first-person ADS pose now resolves the fitted optic's actual `socket_eye` and orientation before falling back
to the base weapon's eye/sight pose. Iron sights retain their existing artist adjustments. Holo and scope retain
their authored 2× and 4× camera magnification; the mouse now scales with handheld magnification as it already did
with a ship station's zoom.

Both original Hollow optic models had opaque glass. The scope's body also ended in solid caps, and the holo's
emitter housing crossed its optical axis. The corrected source has a hollow scope tube, a separate optical lens
at every LOD, and a holo emitter lowered by 3 mm. Native mount points, eye positions and overall dimensions are
preserved. `optic` metadata in `models.json` declares the lens node, eye socket and ring/dot or crosshair style.
The shared shader draws transparent glass and a collimated reticle on that actual surface, bounded by its mesh.
Its first-person variant uses the same projection and depth convention as the viewmodel. It does not place a HUD
reticle over an opaque scope. The holo retains transparent glass. The scope's `magnified` presentation capability
also images the real world onto its rear aperture during local ADS: one 512×512 HDR viewport shares the current
World3D and excludes the first-person layer, so the physical tube and gun front sight cannot obstruct the image.
Its field of view matches the aperture's projected size at the existing 4× camera magnification; zoom is not
multiplied again. The image is linear HDR, with exposure/tone mapping applied once by the main viewport.
Only the local scope in ADS renders this extra view; hip fire, reload, TPS and holographic sights retain their
normal presentation. The viewport is disabled when ADS ends and freed with the attachment. Its material is
duplicated after cached first-person conversion, preventing texture references from entering static caches.
That private material still follows live viewmodel-FOV changes, including at the hip. The optical environment
also mirrors the camera's five immersion-fog properties each active frame, so entering/leaving water cannot
leave the scope with stale fog while the surrounding world changes.

The existing `AttachmentStats.Light` contract means a light shines while the fitted gun is held. The shared
attachment presenter creates a spotlight at its authored `socket_beam`, using data for range, angle and energy.
Local first-person origin/direction follow the weapon's viewmodel projection. Other players' beams follow their
held models and the server's existing `HeldMods` snapshot. Detaching, holstering, seating, death or removing the
model removes/hides its beam. No independent toggle or new saved light state was introduced. Only the local beam
casts shadows; remote beams have a short range and fade with distance. The 25 m/22°/4-energy tuning passed the
coordinated night wall comparison in build17.

The rules call the slot `rail`, while the art catalogue maps that capability to the `under` mount. That existing
mapping is intentional; neither name was blindly replaced. Presentation capabilities are declared per catalogue
entry, and the light is created only when the fitted item's actual shared rules declare `Light`.

Validation is separate for each stage. The staged Blender static audit passed native-eye ray tests through every
LOD of both optics. It found and prevented the holo emitter obstruction as well as the scope's original caps.
Holo LOD0/1/2 totals are 966/484/214 triangles; scope totals are 2,270/1,150/521. Original project source was used;
no external asset or provider generation was introduced. `.artifacts/optics-stage/validation.json` records output
hashes, metre bounds and the explicit absence of runtime visual acceptance. The separate `game-dev` CLI is not
installed, so this authorized local repair uses the repository's Blender exporter and installed Blender 5.2.1.

Two new engine-free tests cover the actual GLB optical-axis/lens contract and two-client attachment replication
through attach, detach, holster and death. Source, both GLBs, the three shader files and the three catalogue entries
are activated; coordinated build 13 passed with zero warnings/errors and both new tests passed (2/2,
`.artifacts/repair-attachments-focused.log`). Held lenses also track the ground camera's exposure through
a per-instance shader parameter, so the reticle remains readable at night without altering catalogue previews.
Actual build15 holo ADS passed with eye error 0.0000 m, axis alignment 1.000000 and an accepted image. Scope ADS
failed at 0.0646 m eye error: the arm reach solver translated the already aligned gun toward the camera, pushing
its eyepiece into the near plane. The failed image was inspected and is not acceptance evidence.

The build16 correction is limited to fitted-optic ADS outside reloads. It transfers the nearest feasible two-arm
translation to the FPS shoulder roots, blended by ADS and capped at 12 cm, retaining limb lengths, grip contact and
the native optical eye. Iron-sight and reload solver paths stay unchanged. Build16's actual tour passed eye,
axis and both-hand checks (scope eye error 0, axis 1, shoulder adjustment 56.4 mm), but its scope image was rejected:
the hollow tube still left only a tiny viewing hole. Build17's rendered rear-aperture image above resolved that
visual failure and was accepted after runtime inspection. The build16 light toggling was real, but its claimed night used
the wrong surface origin and was invalid. The harness now sets midnight at the player's actual planet-frame
position and requires the rendered sky's sun elevation below −6° before capturing light off/on.

The hosted `repair-attachments` scenario presses RMB
through the real input path, checks the final native eye/axis and actual viewmodel material, then ray-tests the
camera centre against lens and opaque optic triangles before checking night light creation/removal and holster.
It also logs shoulder accommodation and requires both hands within 25 mm of their actual grips. It saves the ADS
frame before its geometry assertions so failures remain inspectable. A shared immutable lens
material cache prevents repeated equipping from growing the existing first-person material caches.
The scenario now checks the actual scope viewport dimensions, shared world, cull mask, bounded field of view,
nonblack image readback and render shutdown after ADS. These checks accompany visual review; they do not replace
it. Godot documents the linear HDR viewport texture contract in
[Viewport.UseHdr2D](https://docs.godotengine.org/en/stable/classes/class_viewport.html#class-viewport-property-use-hdr-2d).

## Accepted build17 evidence

The actual hosted tour passed every check in `.artifacts/repair-optics17.log`, and root visually accepted all six
frames below. The scope has a full, clear rear field and readable crosshair with no front-sight obstruction.
Holo, daytime scope and night scope each measured native-eye error 0.0000 m, axis alignment 1.000000 and both hand
errors 0.0000 m. The scope's actual optical image is 512² at 7.36° field of view, matching the main camera's 21.72°
at the authored 4× magnification and the projected aperture. Leaving ADS disabled the extra render.

The real camera's local solar hour was 0.00 and sun elevation −63.98°. The off/on comparison visibly changes the
hut wall, the scope sees the lit target at night, detaching removes the spotlight, and holstering removes the held
model. These are true-night results; the invalid build16 night claim above is superseded.

| Capture | Accepted evidence |
| --- | --- |
| [Holo ADS](images/abtest-b/repairs/attachments/repair-holo-ads.jpg) | Transparent authored aperture and reticle |
| [Scope ADS](images/abtest-b/repairs/attachments/repair-scope-ads.jpg) | Full rear optical field, crosshair, no front-sight obstruction |
| [Night light off](images/abtest-b/repairs/attachments/repair-weapon-light-off.jpg) | Actual midnight baseline |
| [Night light on](images/abtest-b/repairs/attachments/repair-weapon-light-on.jpg) | Beam visibly illuminates the same hut wall |
| [Night scoped light](images/abtest-b/repairs/attachments/repair-scope-night-light.jpg) | Illuminated world through the actual scope image |
| [Light detached](images/abtest-b/repairs/attachments/repair-weapon-light-detached.jpg) | Beam removed after authoritative detachment |

All six are exact copies of `.artifacts/repair-optics17` output, with verified source/destination SHA-256 matches.
[evidence.json](images/abtest-b/repairs/attachments/evidence.json) records hashes, byte counts, source log hashes and
measured values. No image was edited or composited.

Build17 compiled with zero warnings/errors; its final focused run passed 8/8, including both attachment tests
(`.artifacts/repair-final-focused.log`). The preceding full build16 suite passed 384/384 in 429.0 s. After the
shoulder accommodation, actual held-editor base pistol/rifle regressions passed separately for male and female:
12 actions and 200 FPS/TPS samples per body, finite transforms, and no connected hand sample exceeding 25 mm
(`.artifacts/repair-held-regression16-male.log` and `-female.log`). Both retained the known six Jolt-shape and one
ObjectDB exit-leak diagnostics; those unrelated exit diagnostics are not claimed fixed by this optics package.

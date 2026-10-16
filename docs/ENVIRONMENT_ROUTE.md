# Hosted environment route — 16 October 2026

This diagnostic responds to the owner's rejection of isolated scenic captures as proof of ordinary gameplay quality.
It is not an environment art change. Existing uncommitted geology and the separate audio pass are preserved.
The source is integrated in the client and game; the original isolated selector probe remains under
`.artifacts/environment-route-stage`. The coordinated baseline build passed and both route tests passed.
The initial run exposed a spawn-position assumption and was rejected. Final baseline and candidate runs completed
the normal child-host route on High/day and Maximum/night. Original frames, measured costs, rejected candidates
and limitations are recorded in [REPAIR_ENVIRONMENT_2026-10-16.md](REPAIR_ENVIRONMENT_2026-10-16.md).

## Why the old viewers are insufficient

`GeologyReview` forces High, FOV65 and hour16, selects the first qualifying formation and has no player/server.
It is useful asset evidence, not a normal-play route. `WalkingReport.Build` still targets inherited FirstWorld at
radius2410. The new selector requires the current Earth at radius1200 and uses its actual beaches, scatter,
cliffs, monument pads and water. No generated output or player save changes.

Existing measured anchors from `.artifacts/repair-full3.log` include the substation centre at
**-34.8335, -142.0225** and its forest-side exterior at **-35.795848, -142.695460**. These are approximately23m apart.
The old cliff shot at **-30.939053, 149.085783** is in another region and predates the multipart geology port;
joining those screenshots would not establish one walking route. Exact new route coordinates must come from the
compiled `EnvironmentRouteTests` report, not guessed interpolation between the pictures.

## Measured route selection

The isolated probe completed in1.13s on its first region candidate, finding a360.55m connected sampled corridor
around `service-substation/tile 5/3/4/7`. It compiled only the staged selector against explicit DLL references,
with no project references or writes to shared outputs. The full path, inputs and copied-DLL hashes are in
`.artifacts/environment-route-stage/route-plan.json`; original built-DLL hashes are retained in
`built-dll-hashes.json`. Runtime object collision and art remain unverified.

| Stop | Latitude | Longitude | Height | Grade | Trees within18m |
| --- | ---: | ---: | ---: | ---: | ---: |
| Beach | 41.596451 | 106.092227 | 1.43m | 0.148 | 8 |
| Woodland | 40.684345 | 105.386096 | 4.26m | 0.106 | 9 |
| Clearing | 38.400842 | 96.694376 | 15.50m | 0.190 | 1 |
| Water bank | 38.902857 | 97.899158 | 13.65m | 0.545 | 1 |
| Nordic broad cliff front | 34.144385 | 99.162322 | 18.43m | 0.631 | 6 |
| Small substation exterior | 36.047505 | 98.557676 | 19.16m | 0.072 | 9 |

Woodland grass factor is0.244; clearing grass is0.845. These are generated cover inputs, not measurements of
what the player sees. Ground-level standalone camera bearings derived from the same look targets are retained in
`checkpoint-cameras.json`. Woodland uses `--yaw -106.008 --pitch 0.089`; clearing uses
`--yaw 61.542 --pitch -9.580`. Those assume a1.70m standing eye plus0.05m floor clearance; actual camera metadata
must still match between compared captures.

## Source and integration seam

- `src/Hollow.Client/Diagnostics/EarthEnvironmentRoute.cs`: shared engine-free selector for tests and the client.
- `tests/Hollow.Tests/EnvironmentRouteTests.cs`: actual-Earth feature/corridor checks and coordinate report; rejects
  inherited radius and submerged corridors.
- `game/scripts/Ground/EnvironmentRoute.cs`: ordinary hosted-client movement, checkpoint evidence and bounded timings.

The integrated `GroundMode` seam tracks the route node, rejects conflicting options early and cancels/disposes
it before stopping the view/client. No `GroundTour` file is changed. The attachment follows this shape;
combinations with existing shot/tour/measure jobs are rejected so two harnesses never control one client.

```csharp
if (Options.TryGetValue("environment-route", out string? routeFolder) && routeFolder != "true")
{
    if (Options.ContainsKey("shot") || Options.ContainsKey("tour") || Options.ContainsKey("measure"))
        throw new InvalidOperationException("--environment-route cannot share --shot, --tour or --measure");
    AddChild(new EnvironmentRoute
    {
        Name = "EnvironmentRoute", View = _view, Folder = routeFolder,
        ProfileSeconds = Number("environment-profile", 20),
        ObserveSeconds = Number("environment-observe", 0), SolarHour = Number("hour", 10),
    });
}
```

## First coordinated execution

After a coordinated build, run `dotnet run --no-build --project tests/Hollow.Tests -- EnvironmentRouteTests`.
The selector examines at most18 region candidates with a ten-metre grid and two-metre edge samples. It requires
one connected beach → actual woodland → grassy clearing → water bank → scanned cliff front → small monument
exterior, with no segment above0.70 grade or crossing more than0.1m water. Total path is bounded to1200m.
It reports latitude/longitude, height, grade, grass/woodland factors, actual nearby tree counts, feature and distance.
An absent route is a diagnostic failure to investigate, not permission to relax geometric safety or teleport.
Heightfield sampling does not prove avoidance of every trunk or scanned collision; the real walking run remains mandatory.

Use the reported beach latitude/longitude and a **fresh diagnostic database**, not the user's persistent save:

```text
godot --windowed --path game --resolution 1600x900 -- --view ground --host
  --name EnvironmentRoute --port 27047 --db ABSOLUTE_FRESH_DB --spawn origin
  --lat REPORTED_BEACH_LAT --lon REPORTED_BEACH_LON --quality high --hour 10 --clouds 0.25
  --environment-route ABSOLUTE_OUTPUT_FOLDER --environment-profile 20
```

Omit `--in-process` for the ordinary child-server host path. There is no `--shot`, mute, noclip, kit, density override,
camera lift or spawned scenery. The harness drives the same `InputFrame`/prediction/server movement as ordinary play,
with normal survival and starter inventory. It does not claim physical-key validation. Its isolated window prevents
physical mouse/UI interference without making movement neutral; input is cleared on exit. Root must keep other builds,
imports, renderers and heavy scans out of measured intervals and retain a process roster.

The bounded comparison is a complete High/day baseline and Maximum/night baseline, followed by matching candidate
runs; High/day is the primary frame-time comparison. This is not a full quality/time combinatorial matrix.
Both art and lighting agents use the same route, camera, FOV, resolution, clouds and frozen phase. The walk sets
local solar phase directly through the existing ephemeris and records actual sun elevation; hour0 must be below
civil twilight, hour10 must be above10°. Night acceptance cannot come from a command-line label alone.
Capture output includes all six original PNGs, hashes, actual camera basis, player coordinates/ground clearance,
movement flags, server/prediction ticks and corrections, sun/moon illuminance/elevation, exposure, graphics values,
streaming readiness and actual multimesh instance/visibility counts. No failed-readiness screenshot can pass a gate.
The failure image is explicitly named `FAILED-diagnostic.png` and returns nonzero.

Each walking leg records at most20s of raw wall-clock frame intervals plus GPU/render CPU timing, with vsync/cap
temporarily disabled and restored without saving. Screenshots, encoding and readiness waits occur outside those
intervals. The JSON reports sample count/duration, median/P95/P99/worst and raw values; it does not assert a performance
improvement or hardware-independent target. Eight seconds without progress or more than two seconds airborne fails
the route. Failures retain trajectories, rather than silently moving through obstacles.

## First hosted-run repair

`baseline-route-high.log/.err` showed a healthy, grounded character at41.816644°,106.048151°, about4.66m from
the requested beach. `Zone.SpawnPoint` intentionally spreads arrivals on a name-seeded3–8m ring around the
landing site. The harness now verifies the server's landing site within0.25m of the selected beach, accepts
only that documented ring with0.25m numerical allowance, and walks a separately recorded spawn-to-beach
connector. The same dry/grade/monument corridor checks run before it moves. Existing deadline, stuck,
airborne and normal shared collision behavior remain active; unsafe connectors fail without teleporting.
Heightfield samples establish feasibility, while the actual connector walk must prove traversal.

Godot loaded this assembly from bytes, so `Assembly.Location` was empty; hashing that value previously
prevented report saving and process exit after the first failure. Reports now retain the loaded module's MVID,
informational version and a nullable file path/hash. Preserve the actual launch DLL SHA-256 in the external
launch manifest; a missing runtime path is not a replacement hash or evidence of a different binary.
Cleanup/report exceptions are logged and cannot skip the final diagnostic exit. These repairs passed the subsequent
build, route tests and four completed baseline/final route runs; the first run did not traverse any leg.

## Ordinary hosted observation

After the controlled walk, run the normal menu/host path on a copied or fresh diagnostic save and append
`--environment-route ABSOLUTE_FOLDER --environment-observe 30`. No quality/hour override is required for this check.
This mode preserves saved graphics, player location, clock, mouse and keyboard input. Walk through the actual
scene while it records; it saves ready start/end images and movement/network/grounding telemetry. Its frame report
retains ordinary vsync/frame-cap settings and must not be compared directly to the uncapped route measurement.
It quits its own diagnostic session after recording, so never attach it to the user's ongoing session unexpectedly.
The user-visible normal-host result and the controlled checkpoint result must be reviewed separately.

## Acceptance boundary

Inspect the unedited frames and movement evidence for retained grass at ground level, woodland-to-clearing contrast,
ground/rock contact, water transition, readable small monument/loot and missing/popping scenery. Confirm view height
and coverage in normal play. Mesh counts and file presence cannot establish visual quality. Root owns final visual
review, audio-first scheduling and the decision to integrate staged art after baseline evidence exists.

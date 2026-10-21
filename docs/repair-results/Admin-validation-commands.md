# Queued admin/base/crafting validation

Prepared 16 October 2026 after the foundation run found the clan-base network
fixture's occupied-site assumption. The shared rebuild and foundation rerun now
pass. The full UI check (including the 16 crafting checks) and six standalone
screenshots below were completed and inspected. The focused UI check was not
duplicated; the hosted tour remains pending a separate slot. See
[the results](Admin-visual-validation.md). Production placement checks are unchanged.

Run each job separately under an explicit coordinator slot and memory guard:
start with at least 5.5 GiB available, stop the owned job below 2 GiB or above
85% commit. No imports, other game/build processes or parallel captures. These
are individual command lines for the guarded launcher, not a batch to execute.
Use a 120-second bound for UI/build-sandbox jobs and 240 seconds for a hosted tour.
Keep stdout/stderr, process exit, memory samples and images together in the receipt.

PowerShell path setup (no game process):

```powershell
$adminRoot = 'D:\GameDev\Projects\HollowUniverseABTestB'
$adminGodot = 'D:\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'
New-Item -ItemType Directory -Force -Path "$adminRoot\docs\images\crafting-hud","$adminRoot\docs\images\admin-clan-base","$adminRoot\.artifacts\admin-clan-base" | Out-Null
```

Focused engine-free checks after the shared rebuild (or include these filters in
the coordinator's foundation run):

```powershell
dotnet "$adminRoot\tests\Hollow.Tests\bin\Debug\net10.0\Hollow.Tests.dll" ClanBaseTests AdminCameraTests
```

The 16 focused crafting-HUD checks, then the full UI regression in a separate job:

```powershell
& $adminGodot --path "$adminRoot\game" --resolution 1600x900 -- --view ui --crafting-hud-check --ui-scale 1 --quality low --mute
```

```powershell
& $adminGodot --path "$adminRoot\game" --resolution 1600x900 -- --view ui --ui-check --ui-scale 1 --quality low --mute
```

HUD snapshots: four remaining/five owned, three remaining/six owned, then the
narrow fallback. The fixed sample clock prevents the screenshot wait from changing
these counts. Inspect all three for text/icon clarity, progress, and hotbar/vitals
overlap; a successful exit alone is not visual acceptance.

```powershell
& $adminGodot --path "$adminRoot\game" --resolution 1600x900 -- --view ui --screen hud-crafting --ui-scale 1 --quality low --mute --shot "$adminRoot\docs\images\crafting-hud\crafting-four.png"
```

```powershell
& $adminGodot --path "$adminRoot\game" --resolution 1600x900 -- --view ui --screen hud-crafting-next --ui-scale 1 --quality low --mute --shot "$adminRoot\docs\images\crafting-hud\crafting-three.png"
```

```powershell
& $adminGodot --path "$adminRoot\game" --resolution 1024x768 -- --view ui --screen hud-crafting --ui-scale 1 --quality low --mute --shot "$adminRoot\docs\images\crafting-hud\crafting-narrow.png"
```

Offline clan-base captures use the same JSON and ordinary building renderer as the
server. Inspect the complete exterior, gallery standing tiles/window cover and
open peek-down recess, and roof/stair exit separately:

```powershell
& $adminGodot --path "$adminRoot\game" --resolution 1600x900 -- --view build --scene clan --grade 2 --quality medium --mute --shot "$adminRoot\docs\images\admin-clan-base\clan-base-exterior.png"
```

```powershell
& $adminGodot --path "$adminRoot\game" --resolution 1600x900 -- --view build --scene clan --grade 2 --from '0,7.6,1.6' --at '-1.5,6.3,6.3' --quality medium --mute --shot "$adminRoot\docs\images\admin-clan-base\clan-base-shooting-floor.png"
```

```powershell
& $adminGodot --path "$adminRoot\game" --resolution 1600x900 -- --view build --scene clan --grade 2 --from '12,14,14' --at '0,9.8,0' --quality medium --mute --shot "$adminRoot\docs\images\admin-clan-base\clan-base-roof.png"
```

The separately granted hosted tour verifies the F1 path and replication, then
captures four gameplay views. `--in-process` uses the freshly built client/server
assemblies rather than an older Release server executable. Use the isolated save
below only if absent; on a repeat, choose a fresh numbered filename. Do not delete
or reuse a player's save. Keep this GPU run separate from the offline screenshots.

```powershell
& $adminGodot --path "$adminRoot\game" --resolution 1600x900 -- --view ground --host --in-process --port 27143 --name ClanCheck --spawn origin --db "$adminRoot\.artifacts\admin-clan-base\clan-tour-01.db" --hour 10 --quality medium --mute --tour "$adminRoot\docs\images\admin-clan-base\hosted" --tour-steps clan-base
```

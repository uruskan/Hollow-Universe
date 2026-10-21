#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$GodotPath = $env:GODOT,
    [ValidateSet('low', 'medium', 'high', 'ultra', 'maximum')][string]$Quality = 'high',
    [ValidatePattern('^[1-9][0-9]*x[1-9][0-9]*$')][string]$Resolution = '1600x900',
    [ValidateRange(-90, 90)][double]$Latitude = -42.7987,
    [ValidateRange(-180, 180)][double]$Longitude = 7.689,
    [ValidateRange(0, 24)][double]$Hour = 10,
    [double]$Yaw = 0,
    [ValidateRange(-89, 89)][double]$Pitch = 0,
    [ValidateRange(1024, 65535)][int]$Port = 27770,
    [ValidateRange(1, 60)][double]$WaitSeconds = 2,
    [ValidateRange(30, 600)][int]$TimeoutSeconds = 180,
    [ValidateRange(512, 131072)][int]$MinimumStartFreeMiB = 8192,
    [ValidateRange(256, 131072)][int]$MinimumFreeMiB = 2048,
    [ValidateRange(1024, 131072)][int]$MaximumOwnedWorkingSetMiB = 8192,
    [ValidateRange(50, 99)][int]$MaximumCommitPercent = 90,
    [switch]$SpatialGrass,
    [switch]$PreflightOnly
)

# No build/import/retry or renderer overrides. Use an already-built ordinary hosted game and a fresh diagnostic save.
# Guard values are operational limits, not performance budgets. Only this run's verified process tree can be stopped.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$destination = [IO.Path]::GetFullPath($OutputDirectory, $repo)
if (Test-Path -LiteralPath $destination) { throw "Use a new output directory: $destination" }
[IO.Directory]::CreateDirectory($destination) | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
$clock = [Diagnostics.Stopwatch]::StartNew()
$owned = @{}
$child = $null
$reason = $null
$runnerError = $null
$exitCode = $null
$preflightPassed = $false
$launched = $false
$runnerStage = 'preflight'
$minimumObservedFreeMiB = [double]::PositiveInfinity
$peakOwnedWorkingSetMiB = 0.0
$stdout = Join-Path $destination 'stdout.log'
$stderr = Join-Path $destination 'stderr.log'
$image = Join-Path $destination 'ground.png'
$database = Join-Path $destination 'fresh-earth.db'

function Write-Json([string]$Name, $Value) {
    [IO.File]::WriteAllText((Join-Path $destination $Name), ($Value | ConvertTo-Json -Depth 12), $utf8)
}
function Add-JsonLine([string]$Name, $Value) {
    [IO.File]::AppendAllText((Join-Path $destination $Name), (($Value | ConvertTo-Json -Depth 8 -Compress) + "`n"), $utf8)
}
function Get-MemorySnapshot {
    # Query the memory manager directly: WMI formatted counters can time out during resource upload.
    # These are the same available-physical and system commit quantities; all guard thresholds stay unchanged.
    # https://learn.microsoft.com/en-us/windows/win32/api/psapi/ns-psapi-performance_information
    if (-not ('HollowCapture.MemorySnapshot' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
namespace HollowCapture {
    public static class MemorySnapshot {
        [StructLayout(LayoutKind.Sequential)]
        public struct Information {
            public uint Size;
            public UIntPtr CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable;
            public UIntPtr SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
            public uint HandleCount, ProcessCount, ThreadCount;
        }
        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetPerformanceInfo(out Information value, uint size);
        public static Information Read() {
            if (!GetPerformanceInfo(out Information value, (uint)Marshal.SizeOf<Information>()))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (value.PageSize.ToUInt64() == 0 || value.CommitLimit.ToUInt64() == 0)
                throw new InvalidOperationException("System memory counters unavailable.");
            return value;
        }
    }
}
'@
    }
    $m = [HollowCapture.MemorySnapshot]::Read()
    $pageBytes = [double]$m.PageSize.ToUInt64()
    $commitBytes = [double]$m.CommitTotal.ToUInt64() * $pageBytes
    $limitBytes = [double]$m.CommitLimit.ToUInt64() * $pageBytes
    [pscustomobject]@{
        Utc = [DateTime]::UtcNow.ToString('O'); FreeMiB = [double]$m.PhysicalAvailable.ToUInt64() * $pageBytes / 1MB
        CommitMiB = [math]::Round($commitBytes / 1MB, 2)
        CommitLimitMiB = [math]::Round($limitBytes / 1MB, 2)
        CommitPercent = [math]::Round(100.0 * $commitBytes / $limitBytes, 2)
        Source = 'GetPerformanceInfo'
    }
}
function Get-ProcessClassification([string]$Name, [string]$CommandLine) {
    # Reusable build workers can outlive their build for hours. Their presence alone is not active work.
    # A live build/test/run parent is still classified independently and prevents a capture.
    $dotnet = $Name -ieq 'dotnet.exe'
    $msbuild = $Name -ieq 'MSBuild.exe' -or ($dotnet -and $CommandLine -match '(?i)\bMSBuild\.dll\b')
    $node = $msbuild -and $CommandLine -match '(?i)(?:^|\s)[/-]nodemode:1(?:\s|$)'
    $compilerServer = $Name -ieq 'VBCSCompiler.exe' -or ($dotnet -and $CommandLine -match '(?i)\bVBCSCompiler\.dll\b')
    $buildCommand = $dotnet -and $CommandLine -match '(?i)\bdotnet(?:\.exe)?["\s]+(?:build|test|run|msbuild|publish|pack)\b'
    $compiler = $Name -match '^(csc|vbc)\.exe$' -or ($dotnet -and $CommandLine -match '(?i)\b(?:csc|vbc)\.dll\b')
    $runtime = $Name -match '^(Godot.*|Hollow\.(Server|Tests)|blender)\.exe$' -or
        ($dotnet -and $CommandLine -match '(?i)\bHollow\.(Tests|Server)\.(?:dll|exe)\b')
    [pscustomobject]@{
        Competing = $runtime -or $buildCommand -or $compiler -or ($msbuild -and -not $node)
        ReusableBuildServer = $node -or $compilerServer
    }
}
function Get-ProcessInventory {
    @(Get-CimInstance -ClassName Win32_Process -OperationTimeoutSec 3 | ForEach-Object {
        # Do not preserve arbitrary process command lines, which can contain credentials.
        $classification = Get-ProcessClassification $_.Name $_.CommandLine
        [pscustomobject]@{
            ProcessId = [int]$_.ProcessId; ParentProcessId = [int]$_.ParentProcessId; Name = $_.Name
            # CIM creation times have less precision than Process.StartTime; compare both at millisecond precision.
            CreatedUtc = if ($_.CreationDate) { $_.CreationDate.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ') } else { $null }
            WorkingSetMiB = [math]::Round([double]$_.WorkingSetSize / 1MB, 2)
            Competing = $classification.Competing; ReusableBuildServer = $classification.ReusableBuildServer
        }
    })
}
function Update-OwnedProcesses($Inventory) {
    # Only children descended from the exact launched process are eligible; reject a recycled parent PID.
    do {
        $added = $false
        foreach ($item in $Inventory) {
            if ($owned.ContainsKey($item.ProcessId) -or -not $owned.ContainsKey($item.ParentProcessId)) { continue }
            $parent = $owned[$item.ParentProcessId]
            $liveParent = $Inventory | Where-Object { $_.ProcessId -eq $parent.ProcessId -and $_.CreatedUtc -eq $parent.CreatedUtc } | Select-Object -First 1
            if ($null -eq $liveParent -or $null -eq $item.CreatedUtc -or $item.CreatedUtc -lt $parent.CreatedUtc) { continue }
            $owned[$item.ProcessId] = $item
            $added = $true
        }
    } while ($added)
}
function Stop-OwnedProcesses {
    # Retained process identity, never Stop-Process by name. Children normally exit through the game's --parent contract.
    foreach ($item in @($owned.Values | Sort-Object CreatedUtc -Descending)) {
        $p = Get-Process -Id $item.ProcessId -ErrorAction SilentlyContinue
        if ($null -eq $p) { continue }
        try {
            if ($p.StartTime.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ') -ne $item.CreatedUtc) { continue }
            if (-not $p.HasExited) { $p.Kill(); [void]$p.WaitForExit(3000) }
        } catch {
            Add-JsonLine 'cleanup.jsonl' @{ ProcessId = $item.ProcessId; Error = $_.Exception.Message }
        } finally { $p.Dispose() }
    }
}
function Quote-NativeArgument([string]$Value) {
    # Windows command-line quoting; never evaluate a string as PowerShell or invoke a second shell.
    '"' + [regex]::Replace([regex]::Replace($Value, '(\\*)"', '$1$1\"'), '(\\+)$', '$1$1') + '"'
}
function Number([double]$Value) { $Value.ToString('R', [Globalization.CultureInfo]::InvariantCulture) }
function Get-PngValidation([string]$Path, [string]$ExpectedResolution) {
    $stream = $null; $decoded = $null
    try {
        if (-not (Test-Path -LiteralPath $Path)) { throw 'Screenshot was not written.' }
        $stream = [IO.File]::OpenRead($Path)
        # Validate the file itself, not just its name or a nonzero file length. Image data validation forces PNG decoding.
        Add-Type -AssemblyName System.Drawing.Common
        $decoded = [Drawing.Image]::FromStream($stream, $false, $true)
        if ($decoded.RawFormat.Guid -ne [Drawing.Imaging.ImageFormat]::Png.Guid) { throw 'Screenshot is not a PNG.' }
        $actual = "$($decoded.Width)x$($decoded.Height)"
        if ($actual -ne $ExpectedResolution) { throw "Screenshot resolution $actual differs from requested $ExpectedResolution." }
        [pscustomobject]@{ Valid = $true; Width = $decoded.Width; Height = $decoded.Height; Error = $null }
    } catch {
        [pscustomobject]@{ Valid = $false; Width = $null; Height = $null; Error = $_.Exception.Message }
    } finally {
        if ($null -ne $decoded) { $decoded.Dispose() }
        if ($null -ne $stream) { $stream.Dispose() }
    }
}

try {
    if (-not $IsWindows) { throw 'This bounded runner uses Windows process/memory counters.' }
    if ($MinimumStartFreeMiB -lt $MinimumFreeMiB) { throw 'Startup memory floor must be at least the running floor.' }
    if (-not $GodotPath) {
        $GodotPath = 'D:\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe'
    }
    $GodotPath = (Resolve-Path -LiteralPath $GodotPath).Path
    $serverCandidates = @('Release', 'Debug' | ForEach-Object {
        $dll = Join-Path $repo "src/Hollow.Server/bin/$_/net10.0/Hollow.Server.dll"
        if (Test-Path -LiteralPath $dll) { Get-Item -LiteralPath $dll }
    })
    $server = $serverCandidates | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($null -eq $server) { throw 'Build the dedicated server first; in-process fallback is not this scenario.' }
    $gameAssembly = Join-Path $repo 'game/.godot/mono/temp/bin/Debug/HollowUniverse.dll'
    if (-not (Test-Path -LiteralPath $gameAssembly)) { throw 'Build the Debug Godot client first.' }
    $assemblyTime = (Get-Item -LiteralPath $gameAssembly).LastWriteTimeUtc
    $captureSources = @('game/scripts/App/Main.cs', 'game/scripts/Ground/LoadTrace.cs',
        'game/scripts/Ground/RenderWarmup.cs', 'game/scripts/Common/WarmupAssetAdmission.cs',
        'game/scripts/Surface/Flora/ImpostorBaker.cs', 'game/scripts/Surface/GrassField.cs',
        'game/scripts/Surface/GrassField.SpatialSampling.cs', 'game/scripts/Surface/GrassField.Spatial.cs',
        'game/scripts/Surface/GrassField.Contact.cs', 'game/scripts/Surface/TerrainContactAtlas.cs',
        'game/scripts/Surface/TerrainLod.cs', 'game/scripts/Surface/TerrainLod.Contact.cs',
        'game/scripts/Surface/SurfaceView.cs', 'game/scripts/Surface/SurfaceStreamingDiagnostics.cs')
    $staleSources = @($captureSources | Where-Object {
        (Get-Item -LiteralPath (Join-Path $repo $_)).LastWriteTimeUtc -gt $assemblyTime
    })
    $arguments = @('--path', (Join-Path $repo 'game'), '--windowed', '--resolution', $Resolution, '--',
        '--view', 'ground', '--host', '--name', 'EnvironmentCheck', '--port', "$Port", '--db', $database,
        '--spawn', 'origin', '--lat', (Number $Latitude), '--lon', (Number $Longitude), '--hour', (Number $Hour),
        '--yaw', (Number $Yaw), '--pitch', (Number $Pitch), '--quality', $Quality, '--mute',
        '--shot', $image, '--wait', (Number $WaitSeconds), '--capture-trace', '--load-trace', "$($TimeoutSeconds + 60)")
    if ($SpatialGrass) { $arguments += '--spatial-grass' }
    $runnerStage = 'preflight process inventory'
    $inventory = Get-ProcessInventory
    $runnerStage = 'preflight memory counters'
    # A first WMI performance-counter query can need several seconds to initialize. Runtime samples remain bounded to 3s.
    $memory = Get-MemorySnapshot
    $preflight = [ordered]@{
        Scenario = 'ordinary-child-host-ground-shot'; RunnerProcessId = $PID; Utc = [DateTime]::UtcNow.ToString('O')
        Repository = $repo; OutputDirectory = $destination; Executable = $GodotPath; Arguments = $arguments
        Memory = $memory; Processes = $inventory; PreflightOnly = [bool]$PreflightOnly; SourcesNewerThanAssembly = $staleSources
        Guards = @{ TimeoutSeconds = $TimeoutSeconds; MinimumStartFreeMiB = $MinimumStartFreeMiB
            MinimumFreeMiB = $MinimumFreeMiB; MaximumOwnedWorkingSetMiB = $MaximumOwnedWorkingSetMiB; MaximumCommitPercent = $MaximumCommitPercent }
        Inputs = @(@($PSCommandPath, (Join-Path $repo 'game/project.godot'),
            (Join-Path $repo 'game/shaders/surface_grass.gdshader'),
            (Join-Path $repo 'game/shaders/surface_terrain_contact.gdshaderinc'), $gameAssembly, $server.FullName) +
            @($captureSources | ForEach-Object { Join-Path $repo $_ }) | ForEach-Object {
                $f = Get-Item -LiteralPath $_
                @{ Path = $f.FullName; Bytes = $f.Length; LastWriteUtc = $f.LastWriteTimeUtc.ToString('O'); Sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash }
            })
    }
    Write-Json 'preflight.json' $preflight
    $busy = @($inventory | Where-Object { $_.Competing -and $_.ProcessId -ne $PID })
    $network = [Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties()
    $portBusy = @($network.GetActiveUdpListeners() + $network.GetActiveTcpListeners() | Where-Object Port -eq $Port).Count -gt 0
    if ($staleSources.Count -gt 0) { $reason = 'preflight diagnostics need rebuild' }
    elseif ($busy.Count -gt 0) { $reason = 'preflight competing process' }
    elseif ($portBusy) { $reason = 'preflight port occupied' }
    elseif ($memory.FreeMiB -lt $MinimumStartFreeMiB) { $reason = 'preflight free-memory guard' }
    elseif ($memory.CommitPercent -ge $MaximumCommitPercent) { $reason = 'preflight commit guard' }
    else { $preflightPassed = $true }
    if ($preflightPassed -and -not $PreflightOnly) {
        $runnerStage = 'launch'
        $clock.Restart()
        $child = Start-Process -FilePath $GodotPath -ArgumentList (($arguments | ForEach-Object { Quote-NativeArgument $_ }) -join ' ') `
            -WorkingDirectory $repo -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
        $launched = $true
        $owned[$child.Id] = [pscustomobject]@{
            ProcessId = $child.Id; ParentProcessId = $PID; Name = $child.ProcessName
            CreatedUtc = $child.StartTime.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
        }
        Write-Json 'launch.json' @{ RunnerProcessId = $PID; ProcessId = $child.Id; CreatedUtc = $owned[$child.Id].CreatedUtc; Arguments = $arguments }
        do {
            $runnerStage = 'runtime process inventory'
            $inventory = Get-ProcessInventory
            Update-OwnedProcesses $inventory
            $runnerStage = 'runtime memory counters'
            $memory = Get-MemorySnapshot
            $current = @($inventory | Where-Object { $owned.ContainsKey($_.ProcessId) -and $owned[$_.ProcessId].CreatedUtc -eq $_.CreatedUtc })
            $workingSet = [double]($current | Measure-Object WorkingSetMiB -Sum).Sum
            $competitors = @($inventory | Where-Object { $_.Competing -and -not $owned.ContainsKey($_.ProcessId) })
            $minimumObservedFreeMiB = [math]::Min($minimumObservedFreeMiB, $memory.FreeMiB)
            $peakOwnedWorkingSetMiB = [math]::Max($peakOwnedWorkingSetMiB, $workingSet)
            Add-JsonLine 'resources.jsonl' @{ Seconds = $clock.Elapsed.TotalSeconds; Memory = $memory
                OwnedProcesses = $current; OwnedWorkingSetMiB = $workingSet
                CompetingProcesses = $competitors }
            if ($clock.Elapsed.TotalSeconds -ge $TimeoutSeconds) { $reason = 'timeout' }
            elseif ($competitors.Count -gt 0) { $reason = 'competing process started during capture' }
            elseif ($memory.FreeMiB -lt $MinimumFreeMiB) { $reason = 'system free-memory guard' }
            elseif ($memory.CommitPercent -ge $MaximumCommitPercent) { $reason = 'system commit guard' }
            elseif ($workingSet -gt $MaximumOwnedWorkingSetMiB) { $reason = 'owned working-set guard' }
            if ($reason) { break }
        } while (-not $child.WaitForExit(1000))
        if ($child.HasExited) { $exitCode = $child.ExitCode }
    }
} catch {
    $runnerError = $_.Exception.ToString()
    if (-not $reason) { $reason = 'runner error' }
} finally {
    Stop-OwnedProcesses
    if ($null -ne $child) {
        if ($child.WaitForExit(3000)) { $exitCode = $child.ExitCode }
        $child.Dispose()
    }
    $lines = @()
    if (Test-Path -LiteralPath $stdout) {
        try {
            # Redirected output may still be flushing after process termination. Share the writer's handle;
            # a forensic log-read failure must never prevent the cleanup receipt from being written.
            $logStream = [IO.File]::Open($stdout, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
            try {
                if ($logStream.Length -gt 32MB) { throw 'Capture log exceeds 32 MiB inspection limit.' }
                $logReader = [IO.StreamReader]::new($logStream)
                try { $lines = $logReader.ReadToEnd() -split "`r?`n" } finally { $logReader.Dispose() }
            } finally { $logStream.Dispose() }
        } catch {
            $runnerError = "$runnerError`nLog read: $($_.Exception.Message)"
            if (-not $reason) { $reason = 'capture log read failed' }
        }
    }
    $capture = @(); $loading = @()
    foreach ($line in $lines) {
        if ($line.StartsWith('[capture-progress] ')) {
            try { $capture += $line.Substring(19) | ConvertFrom-Json } catch { }
        } elseif ($line.StartsWith('[load-progress] ')) {
            try { $loading += $line.Substring(16) | ConvertFrom-Json } catch { }
        }
    }
    $captureStartedReady = @($capture | Where-Object { $_.stage -eq 'begin' -and $_.ready -eq $true }).Count -gt 0
    $captureSaved = @($capture | Where-Object { $_.stage -eq 'save-complete' }).Count -gt 0
    $groundReady = @($lines | Where-Object { $_ -match '^\[capture\] ground state=Playing .*ready=True loading=False ' }).Count -gt 0
    $imageInfo = if (Test-Path -LiteralPath $image) { Get-Item -LiteralPath $image } else { $null }
    $png = Get-PngValidation $image $Resolution
    $cleanupComplete = $false
    try {
        $after = Get-ProcessInventory
        Write-Json 'processes-after.json' $after
        $remaining = @($after | Where-Object { $owned.ContainsKey($_.ProcessId) -and $owned[$_.ProcessId].CreatedUtc -eq $_.CreatedUtc })
        $cleanupComplete = $remaining.Count -eq 0
    } catch { Add-JsonLine 'cleanup.jsonl' @{ Error = $_.Exception.Message } }
    $acceptanceFailures = @(
        if ($launched) {
            if ($exitCode -ne 0) { 'game did not exit successfully' }
            if (-not $captureStartedReady) { 'capture did not begin ready' }
            if (-not $captureSaved) { 'successful save marker absent' }
            if (-not $groundReady) { 'readback did not show Playing, ready and loading=False' }
            if (-not $png.Valid) { 'PNG decoding or resolution validation failed' }
            if (-not $cleanupComplete) { 'owned-process cleanup incomplete' }
        }
    )
    $success = $launched -and -not $reason -and $acceptanceFailures.Count -eq 0
    $result = [ordered]@{
        Success = $success; PreflightPassed = $preflightPassed; PreflightOnly = [bool]$PreflightOnly; Launched = $launched
        RunnerProcessId = $PID; OwnedProcesses = @($owned.Values); CleanupComplete = $cleanupComplete; ExitCode = $exitCode; Terminated = $reason; Error = $runnerError; LastRunnerStage = $runnerStage
        Seconds = [math]::Round($clock.Elapsed.TotalSeconds, 3)
        MinimumObservedFreeMiB = if ([double]::IsPositiveInfinity($minimumObservedFreeMiB)) { $null } else { $minimumObservedFreeMiB }
        PeakOwnedWorkingSetMiB = $peakOwnedWorkingSetMiB; CaptureStartedReady = $captureStartedReady; GroundReadyAtReadback = $groundReady
        CaptureStages = $capture; ReadinessSamples = $loading; ScreenshotExists = $null -ne $imageInfo
        ScreenshotValidation = $png; AcceptanceFailures = $acceptanceFailures
        ScreenshotBytes = if ($imageInfo) { $imageInfo.Length } else { 0 }; Screenshot = $image
        ScreenshotSha256 = if ($imageInfo) { (Get-FileHash -LiteralPath $image -Algorithm SHA256).Hash } else { $null }
        Stdout = $stdout; Stderr = $stderr
    }
    Write-Json 'result.json' $result
    Write-Output "Capture success=$success launched=$launched preflight=$preflightPassed reason=$reason; $destination"
}
if (($PreflightOnly -and $preflightPassed) -or $success) { exit 0 }
exit 1

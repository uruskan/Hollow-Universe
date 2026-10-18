param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [string]$OutputDirectory = '.artifacts/tests-isolated',
    [int]$ClassTimeoutSeconds = 300,
    [int]$MinimumFreeMiB = 768,
    [string[]]$Classes = @()
)

# Every discovered class runs once in a fresh process, serially. This bounds accumulated test-fixture/native state;
# it does not establish a long-lived server's memory behavior or replace the separate lifecycle/stress checks.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$runner = Join-Path $repo "tests/Hollow.Tests/bin/$Configuration/net10.0/Hollow.Tests.exe"
if (-not (Test-Path -LiteralPath $runner)) { throw "Build the $Configuration test runner first: $runner" }
$destination = [IO.Path]::GetFullPath($OutputDirectory, $repo)
[IO.Directory]::CreateDirectory($destination) | Out-Null
$discovered = @(& $runner --list-classes)
if ($LASTEXITCODE -ne 0 -or $discovered.Count -eq 0) { throw 'Test discovery failed.' }
$selectedClasses = if ($Classes.Count -eq 0) { $discovered } else {
    foreach ($name in $Classes) { if ($name -notin $discovered) { throw "Unknown test class: $name" } }
    $Classes
}
$results = [Collections.Generic.List[object]]::new()
Write-Output "Running $($selectedClasses.Count) test classes serially in fresh processes."
foreach ($testClass in $selectedClasses) {
    if ($testClass -notmatch '^[A-Za-z0-9_]+$') { throw "Unexpected test class: $testClass" }
    $freeBeforeMiB = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1024
    if ($freeBeforeMiB -lt $MinimumFreeMiB) {
        Write-Output "Stopped before $testClass because available memory is below the guard."
        break
    }
    $stdout = Join-Path $destination "$testClass.stdout.log"
    $stderr = Join-Path $destination "$testClass.stderr.log"
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $child = Start-Process -FilePath $runner -ArgumentList @('--class', $testClass) -WorkingDirectory $repo `
        -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    $reason = $null
    while (-not $child.WaitForExit(1000)) {
        $freeMiB = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1024
        if ($timer.Elapsed.TotalSeconds -gt $ClassTimeoutSeconds) { $reason = 'class timeout' }
        elseif ($freeMiB -lt $MinimumFreeMiB) { $reason = 'system memory pressure' }
        if ($reason) {
            # This is the child created above, never another game/editor/test process.
            $child.Kill()
            $child.WaitForExit()
            break
        }
    }
    $child.WaitForExit() # flush redirected streams
    $text = [IO.File]::ReadAllText($stdout)
    $summary = [regex]::Match($text, '(?m)^(\d+)/(\d+) passed in ([0-9.]+) s\.\r?$')
    $passed = if ($summary.Success) { [int]$summary.Groups[1].Value } else { 0 }
    $total = if ($summary.Success) { [int]$summary.Groups[2].Value } else { 0 }
    $result = [pscustomobject]@{
        Class = $testClass; ExitCode = $child.ExitCode; Completed = $summary.Success; Passed = $passed; Total = $total
        Seconds = [math]::Round($timer.Elapsed.TotalSeconds, 3); Terminated = $reason; Stdout = $stdout; Stderr = $stderr
        ProcessId = $child.Id; FreeBeforeMiB = [math]::Round($freeBeforeMiB)
    }
    $results.Add($result)
    $results | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $destination 'results.json') -Encoding utf8
    if ($result.ExitCode -ne 0 -or -not $result.Completed) {
        Write-Output "FAIL $testClass exit=$($result.ExitCode) $reason ($passed/$total)"
    }
    $child.Dispose()
    if ($reason -eq 'system memory pressure') { break }
}
$complete = $results.Count -eq $selectedClasses.Count -and @($results | Where-Object { -not $_.Completed }).Count -eq 0
$success = $complete -and @($results | Where-Object { $_.ExitCode -ne 0 -or $_.Passed -ne $_.Total }).Count -eq 0
$passedTotal = ($results | Measure-Object Passed -Sum).Sum
$testTotal = ($results | Measure-Object Total -Sum).Sum
Write-Output "Isolated suite: $passedTotal/$testTotal tests; $($results.Count)/$($selectedClasses.Count) classes; complete=$complete; passed=$success"
if (-not $success) { exit 1 }

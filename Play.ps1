[CmdletBinding()]
param(
    [ValidateSet('Menu', 'Host', 'Join')][string]$Mode = 'Menu',
    [ValidateScript({
        if ([string]::IsNullOrWhiteSpace($_) -or $_ -match '[:\[\]]') {
            throw 'Use an IPv4 address or host name; this client does not support IPv6.'
        }
        $true
    })][string]$Address = '127.0.0.1',
    [ValidateRange(1024, 65535)][int]$Port = 27025,
    [string]$PlayerName = 'Survivor',
    [ValidateSet('low', 'medium', 'high', 'ultra', 'maximum')][string]$Quality,
    [string]$GodotPath = $env:GODOT
)

$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
if (-not $GodotPath) {
    $GodotPath = 'D:\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe'
}
if (-not (Test-Path -LiteralPath $GodotPath -PathType Leaf)) {
    throw 'Godot .NET was not found. Set GODOT or pass -GodotPath with its executable path.'
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK 10 is required to build Hollow Universe.'
}

Write-Host 'Building Hollow Universe...'
& dotnet build HollowUniverse.sln -nologo -v quiet
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& dotnet build src/Hollow.Server -c Release -nologo -v quiet
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$artifactDirectory = Join-Path $PSScriptRoot '.artifacts'
New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
$importLog = Join-Path $artifactDirectory 'launch-import.log'
$importErrorLog = Join-Path $artifactDirectory 'launch-import-errors.log'
Write-Host 'Checking game assets...'
$importArguments = @('--headless', '--path', ('"{0}"' -f (Join-Path $PSScriptRoot 'game')), '--import')
$importProcess = Start-Process -FilePath $GodotPath -ArgumentList $importArguments -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput $importLog -RedirectStandardError $importErrorLog
if ($importProcess.ExitCode -ne 0 -or (Select-String -LiteralPath $importErrorLog -Pattern '^ERROR:' -Quiet)) {
    Get-Content -LiteralPath $importLog -Tail 40
    Get-Content -LiteralPath $importErrorLog -Tail 40
    throw "Asset import failed. See $importLog"
}

$gameArguments = @('--path', (Join-Path $PSScriptRoot 'game'), '--', '--view', 'ground', '--port', "$Port")
if ($Quality) { $gameArguments += @('--quality', $Quality) }
if ($Mode -eq 'Host') {
    $gameArguments += @('--host', '--name', $PlayerName)
} elseif ($Mode -eq 'Join') {
    $gameArguments += @('--connect', "${Address}:$Port", '--name', $PlayerName)
}
Write-Host 'Starting Hollow Universe...'
& $GodotPath @gameArguments
exit $LASTEXITCODE

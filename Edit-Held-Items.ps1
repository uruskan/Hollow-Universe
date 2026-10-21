[CmdletBinding()]
param(
    [string]$Item = 'metal-hatchet',
    [string]$GodotPath = $env:GODOT,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
if (-not $GodotPath) {
    $GodotPath = 'D:\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe'
}
if (-not (Test-Path -LiteralPath $GodotPath -PathType Leaf)) {
    throw 'Godot .NET was not found. Set GODOT or pass -GodotPath.'
}
if (-not $SkipBuild) {
    & dotnet build HollowUniverse.sln -nologo -v quiet
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
# This is the user-requested interactive editor, so its window is intentionally visible.
& $GodotPath --path (Join-Path $PSScriptRoot 'game') --resolution 1600x900 -- --view held-editor --item $Item
exit $LASTEXITCODE

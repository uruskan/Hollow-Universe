@echo off
rem Hollow Universe: every monument at its real size in one scene (docs/MONUMENTS.md 8.5).
rem
rem   view-monuments.cmd                         build, then open the monument viewer over the whole park
rem   view-monuments.cmd --focus launch-complex  start at one monument (its key, as in game/data/monuments)
rem
rem In the viewer: hold the right mouse button to look, W A S D to fly, Q / E down and up, Shift faster, the wheel
rem sets the speed, Left / Right jump between monuments, Home shows the whole park, L hides the labels.
rem GODOT names the Godot 4.7.2 .NET executable if it isn't in the usual place.
setlocal
cd /d "%~dp0"
if "%GODOT%"=="" set "GODOT=D:\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe"
if not exist "%GODOT%" goto nogodot
echo Building...
dotnet build HollowUniverse.sln -nologo -v quiet -clp:ErrorsOnly
if errorlevel 1 exit /b 1
rem Import the assets when any is newer than the last import (a clone, a merge that brought new ones).
powershell -NoProfile -Command "$s = Get-Item game\.godot\hollow-import.stamp -ErrorAction SilentlyContinue; if ($s -and -not (Get-ChildItem game\assets, game\data -Recurse -File | Where-Object { $_.LastWriteTime -gt $s.LastWriteTime } | Select-Object -First 1)) { exit 0 } else { exit 1 }"
if errorlevel 1 (
  echo Importing assets ^(the first time takes several minutes^)...
  "%GODOT%" --headless --path game --import
  if not exist "game\.godot" mkdir "game\.godot"
  type nul > "game\.godot\hollow-import.stamp"
)
"%GODOT%" --path game -- --view monuments %*
exit /b

:nogodot
echo Godot 4.7.2 .NET was not found at "%GODOT%". Set GODOT to its executable and run this again.
exit /b 1

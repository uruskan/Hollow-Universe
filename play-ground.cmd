@echo off
rem Hollow Universe: the ground game on the first world (docs/GROUND.md 4.3).
rem
rem   play-ground.cmd                  build, then host a game on this computer (others can join on port 27015)
rem   play-ground.cmd join [address]   build, then join a game as another player (default 127.0.0.1)
rem   play-ground.cmd menu             build, then open the main menu (PLAY, CONNECT, name, QUIT)
rem
rem GODOT names the Godot 4.7.2 .NET executable if it isn't in the usual place; PLAYER the player's name
rem (default Host when hosting, Guest when joining). Further arguments go to the game (AGENTS.md lists them).
setlocal
cd /d "%~dp0"
if "%GODOT%"=="" set "GODOT=D:\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe"
if not exist "%GODOT%" goto nogodot
echo Building...
dotnet build HollowUniverse.sln -nologo -v quiet -clp:ErrorsOnly
if errorlevel 1 exit /b 1
if not exist "game\.godot\imported" "%GODOT%" --headless --path game --import
if /i "%~1"=="join" goto join
if /i "%~1"=="menu" goto menu
if "%PLAYER%"=="" set "PLAYER=Host"
"%GODOT%" --path game -- --view ground --host --name %PLAYER% %*
exit /b

:join
set "ADDRESS=%~2"
shift
if "%ADDRESS%"=="" set "ADDRESS=127.0.0.1"
if "%ADDRESS:~0,2%"=="--" (set "ADDRESS=127.0.0.1") else (shift)
if "%PLAYER%"=="" set "PLAYER=Guest"
"%GODOT%" --path game -- --view ground --connect %ADDRESS%:27015 --name %PLAYER% %1 %2 %3 %4 %5 %6 %7 %8 %9
exit /b

:menu
shift
"%GODOT%" --path game -- --view ground %1 %2 %3 %4 %5 %6 %7 %8 %9
exit /b

:nogodot
echo Godot 4.7.2 .NET was not found at "%GODOT%". Set GODOT to its executable.
exit /b 1

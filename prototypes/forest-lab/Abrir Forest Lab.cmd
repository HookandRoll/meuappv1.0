@echo off
setlocal
set "FOREST_GODOT=%~dp0..\..\Godot_v4.7.2-stable_mono_win64.exe"
if not exist "%FOREST_GODOT%" (
    echo Godot nao encontrado. Abra project.godot no Godot 4 .NET ou padrao.
    pause
    exit /b 1
)
start "Forest Lab" "%FOREST_GODOT%" --path "%~dp0."

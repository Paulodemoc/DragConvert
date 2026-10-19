@echo off
rem Starts the Release build with the live log window. Usage: run-spike.cmd [layered|dwm]
rem Double-click it from Explorer (or run from a non-admin terminal): an elevated DragConvert
rem can't receive drops from Explorer, because Windows blocks drag-and-drop into higher-privilege apps.
set RENDERER=%1
if "%RENDERER%"=="" set RENDERER=layered
start "" "%~dp0..\src\DragConvert.App\bin\Release\net10.0-windows\DragConvert.exe" --console --renderer=%RENDERER%

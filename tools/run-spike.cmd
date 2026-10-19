@echo off
rem Starts the Release build with the live log window. Usage: run-spike.cmd [layered|dwm]
rem Without an argument the overlay style comes from Settings.
rem Double-click it from Explorer (or run from a non-admin terminal): an elevated DragConvert
rem can't receive drops from Explorer, because Windows blocks drag-and-drop into higher-privilege apps.
set RENDERER_ARG=
if not "%1"=="" set RENDERER_ARG=--renderer=%1
start "" "%~dp0..\src\DragConvert.App\bin\Release\net10.0-windows10.0.19041.0\DragConvert.exe" --console %RENDERER_ARG%

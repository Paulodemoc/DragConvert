# DragConvert

Windows tray app that converts files from a drag gesture. Offline only. See docs/design.md.

## Stack
C# 14, .NET 10, WPF (Fluent theme), CsWin32. Solution in src/, tests in tests/.

## Rules
- All Win32/COM code lives in DragConvert.Native. Core and Providers must not reference UI or Win32.
- Nullable enabled; warnings are errors. Run `dotnet build` and `dotnet test` before saying a task is done.
- Package versions live in Directory.Packages.props (central package management).
- Win32 hook callbacks must only record state and post to another thread. Never do I/O or allocate heavily in them.
- Never modify the source file. Write output to a temp file and rename atomically.
- No network calls and no telemetry.
- Record non-obvious decisions as short ADRs in docs/decisions/.
- Hooks and overlay can't be unit tested: keep docs/test-matrix.md as the manual checklist the user runs on a real desktop.

## Commands
- Build: dotnet build DragConvert.slnx
- Test: dotnet test DragConvert.slnx
- Smoke run (starts and exits): DragConvert.exe --smoke
- Run app: dotnet run --project src/DragConvert.App -- --console [--renderer=layered|dwm]
- Logs: %LOCALAPPDATA%\DragConvert\logs

## Current milestone
See docs/design.md, Implementation plan. Work on one milestone at a time.
Status: M1 spike code done (hook thread, key polling, overlay with layered/dwm renderers, logging). Waiting on the user to run docs/test-matrix.md; then write docs/decisions/0001-drag-detection.md.

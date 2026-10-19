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
- Licensing: no GPL/AGPL components. FFmpeg must be an LGPL build (MP4 uses h264_mf, not x264). HEIC is input-only.
- Apps launched from an elevated shell are elevated, and Windows then blocks drops from Explorer: launch GUI test builds via tools/run-spike.cmd from Explorer.
- Hooks and overlay can't be unit tested: keep docs/test-matrix.md as the manual checklist the user runs on a real desktop.

## Commands
- Build: dotnet build DragConvert.slnx
- Test: dotnet test DragConvert.slnx
- Smoke run (starts and exits): DragConvert.exe --smoke
- Fetch FFmpeg for dev/tests (LGPL build into tools/bin, git-ignored): pwsh tools/fetch-ffmpeg.ps1
- CLI harness: dotnet run --project src/DragConvert.Cli -- formats <file> | convert <file>... <format> [--out <dir>]
- Run app: dotnet run --project src/DragConvert.App -- --console [--renderer=layered|dwm]
- Logs: %LOCALAPPDATA%\DragConvert\logs

## Current milestone
See docs/design.md, Implementation plan. Work on one milestone at a time.
Status: M3 functional part done (overlay converts via FormatCatalog, toasts, single instance + pipe, Send To, tray window, settings; ADR 0004). Next: user tries docs/test-matrix.md M3 section, then the M3 UI pass (tokens, motion, gallery, snapshots, renderer choice).

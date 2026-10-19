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
- Visual values (sizes, radii, colours, timings) belong in src/DragConvert.App/Theme/Tokens.xaml, not in code.
- After UI changes, render snapshots and look at them before saying the UI is done; also run the --smoke check.
- Hooks and overlay can't be unit tested: keep docs/test-matrix.md as the manual checklist the user runs on a real desktop.

## Commands
- Build: dotnet build DragConvert.slnx
- Test: dotnet test DragConvert.slnx
- Smoke run (starts and exits): DragConvert.exe --smoke
- Fetch FFmpeg for dev/tests (LGPL build into tools/bin, git-ignored): pwsh tools/fetch-ffmpeg.ps1
- UI gallery (every overlay state, live): DragConvert.exe --gallery
- UI snapshots to PNG (review visuals; also regenerates Assets/DragConvert.ico source): DragConvert.exe --snapshot[=dir] (default artifacts/ui)
- Real overlay window for screenshots (DWM effects, glass backdrop): DragConvert.exe --preview-overlay[=glass|layered|dwm]
- CLI harness: dotnet run --project src/DragConvert.Cli -- formats <file> | convert <file>... <format> [--out <dir>]
- Run app: dotnet run --project src/DragConvert.App -- --console [--renderer=layered|dwm]
- Logs: %LOCALAPPDATA%\DragConvert\logs

## Current milestone
See docs/design.md, Implementation plan. Work on one milestone at a time.
Status: M3 done pending the owner's review of the UI pass (ADR 0005). Next: M4 PDF and tools (Alt tiles).

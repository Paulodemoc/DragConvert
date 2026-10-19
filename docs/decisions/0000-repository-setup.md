# 0000. Repository setup (M0)

Date: 2026-10-19
Status: accepted

## Decisions

- **Solution format: `.slnx`.** The .NET 10 SDK creates the XML solution format by default; it is smaller and merges cleanly. The design doc's `DragConvert.sln` references mean this file.
- **Central package management.** All package versions live in `Directory.Packages.props`; project files carry `PackageReference` without versions.
- **Layering enforced by target framework.** `Core` and `Providers` target `net10.0`, not `net10.0-windows`, so WPF and Win32 APIs are not available to them at compile time.
- **Warnings are errors everywhere**, not only in CI, via `Directory.Build.props`, with `AnalysisLevel=latest` and code style enforced in the build.
- **SDK pinned** in `global.json` (10.0.402, `rollForward: latestFeature`); CI installs the same version from it.
- **Tray icon: H.NotifyIcon.Wpf**, created with efficiency mode off because EcoQoS would slow the overlay's show path. The icon is a generated placeholder until M3.
- **CI smoke test:** the app accepts `--smoke`, starts fully (tray icon included), and exits with code 0 once the dispatcher is idle.
- **License: MIT** (needed before the first public commit for SignPath).
- **No FluentAssertions for now.** Version 8 moved to a commercial license; tests use plain xUnit asserts. Revisit (Shouldly or AwesomeAssertions) if assertions get verbose.
- **ShellExt project not created.** It is a native C++/Rust component for post-MVP M7.

# Drag-to-Convert for Windows: Design & Implementation Plan

Oct 18, 2026 · @Paulo Henrique Vieira Neves

## Overview and goals

We are building a tray-resident Windows app that converts or edits any file from a drag gesture: start dragging, hold a modifier key, drop onto a format. Everything runs locally. It is the Windows counterpart of the Mac app Tangerine (Leap Studio), built as an original product with its own name, UI and code. Working name: **DragConvert**.

**Goals (proposed targets)**

- The overlay appears within 150 ms of the modifier key press during a drag.
- Files never leave the machine; no network calls, no telemetry by default.
- Output is always a new file beside the original; the source is never modified.
- MVP covers the common image, audio, video and PDF conversions (around 40 format pairs) with a provider design that makes adding more cheap.
- Works for drags from Explorer, the desktop, browsers and mail clients, not only Explorer.
- Ships for Windows 10 22H2 and Windows 11 (x64 first, ARM64 later) as a signed GitHub release and a Store package, at zero cost to the author.

**Success criteria**

- Convert a HEIC photo to JPEG with one drag and one drop, no app window opened.
- Cold start under 1 s; idle memory under 80 MB; idle CPU effectively zero.
- No false-positive antivirus detections on the signed release build.

## Scope and non-goals

The MVP proves the drag overlay and a small, solid conversion set; breadth and polish come after.

| Area | MVP | Later |
| --- | --- | --- |
| Entry points | Tray drop zone, SendTo, drag overlay (drop-target design) | Win11 modern context menu, keyboard-driven radial menu for Explorer selection |
| Images | JPEG, PNG, WebP, HEIC, TIFF, BMP, GIF, PDF | SVG rasterize, AVIF, resize/rotate tools |
| Audio | MP3, M4A, WAV, FLAC, OGG, Opus | Trim, normalize |
| Video | MP4 (H.264), WebM, GIF, extract audio | HEVC/AV1 presets, trim |
| PDF | Split, merge, reorder pages, compress | OCR, page extraction to images |
| Tools | Strip metadata (EXIF, PDF info) | QR reader, archive create/extract |
| Platform | Windows 11 and Windows 10 22H2, x64 | ARM64 |
| Settings | Hotkey/modifier choice, output folder rule, autostart | Per-type presets, themes |

**Non-goals**

- Office documents (DOCX to PDF and similar) in the MVP; they need Office or LibreOffice and are a separate decision.
- Any cloud processing, accounts or sync.
- A full editor UI; the app converts and applies quick tools, nothing more.
- Cross-platform builds; this is Windows-only by design.
- Copying Tangerine's name, branding, icons or UI assets.

## User experience

The core flow is drag, hold the trigger, drop on a format tile; the app saves a converted copy next to the original and shows a toast.

**Primary flow: drag overlay**

1. The user starts dragging one or more files from any app.
2. While dragging, they hold the trigger (default Ctrl+Shift; Shift alone conflicts with Explorer's move-on-drop).
3. A small topmost overlay appears near the cursor with format tiles chosen for the file type (for example HEIC shows JPEG, PNG, WebP, PDF).
4. Holding Alt as well swaps the tiles for tools (compress PDF, strip metadata, split).
5. The user drops on a tile. Conversion runs in the background.
6. A toast reports the result with Open and Show in folder actions; failures show a one-line reason.
7. Releasing the trigger or pressing Esc dismisses the overlay and the drag proceeds normally.

**Secondary entry points**

- **Tray icon:** a drop zone window and a recent-conversions list; also the home of Settings.
- **SendTo:** a `Convert with DragConvert` entry that opens a small picker.
- **Context menu (post-MVP):** a Win11 modern menu submenu with the same format list.

**Behavior rules**

- Multi-file drags convert each file; mixed types show only formats valid for all, otherwise a per-type picker.
- Output name: `<name>.<newext>`; on collision append `  (1) `, never overwrite.
- Overlay never steals focus and never blocks the underlying drag if the user ignores it.
- Dark and light themes follow the system; overlay respects per-monitor DPI.
- Every tile is reachable by keyboard in the tray picker for accessibility.

## UI and visual design

The goal is a native-feeling Windows 11 look (Fluent materials, rounded corners, smooth motion) that appears instantly and never gets in the way of the drag. We start with WPF on .NET 10 and a hidden, pre-created overlay window, and let M1 measurements decide whether a custom renderer is needed.

**What we are matching.** The Mac app shows a radial menu around the dragged file, adapts the formats to the file type, and lets the user pick with the arrow keys, Enter and Escape ([MacGeneration review](https://www.macg.co/logiciels/2026/10/tangerine-un-convertisseur-de-fichiers-toujours-portee-de-main-sur-mac-311381), [feature description](https://chatgate.ai/post/tangerine)). On Windows the ring appears around the cursor, because that is where the user's attention already is.

**Design principles**

1. Instant: create the overlay hidden at startup, then only move and show it. No layout work, file I/O or heavy allocation on the show path.
2. Native: follow the system light or dark mode and accent colour, use Segoe UI Variable, 8 px corners.
3. Forgiving: a dead zone around the cursor so nothing is dropped by accident, large tiles, clear hover feedback.
4. Quiet: never steal focus, no sound, dismiss cleanly on Esc or when the trigger is released.

**Visual language**

| Element | Windows 11 | Windows 10 fallback |
| --- | --- | --- |
| Surface | Translucent DWM system backdrop (Acrylic or Mica Alt), tinted to the theme | Solid, slightly translucent tint without blur |
| Corners | DWM rounded corners (`DWMWA_WINDOW_CORNER_PREFERENCE`) | Rounded clip region |
| Shadow | DWM shadow on the borderless window | Soft drawn shadow, or none |
| Typography | Segoe UI Variable, 12 to 14 px labels | Segoe UI |
| Colour | System accent for hover and active tile; light and dark from the system setting | Same |
| Icons | One bundled vector icon set for formats and tools, drawn as XAML geometry | Same |
| Motion | Ease-out, 120 to 180 ms; honours the system animation setting | Same |
| High contrast | Opaque surface with system colours | Same |

The system backdrop APIs need a recent Windows 11 build (22H2 or later, to be confirmed in M1); older systems get the fallback column.

**Overlay layout (proposed starting values)**

- A ring of up to 8 tiles, each at least 56 px at 100% scaling, around the cursor with a 48 px dead zone at the centre. Extra formats go to a second ring or a More tile.
- Holding Alt swaps the ring from formats to tools.
- A small caption under the hovered tile states the result, for example "JPEG, same folder".
- Arrow keys, Enter and Esc apply to the tray picker and the later Explorer-selection mode. In the drop-target design the file list is only known once the cursor enters the overlay, so the ring itself is mouse-driven.

**Motion (proposed starting values)**

- Show: fade and scale from 0.92 to 1.0 over 140 ms, tiles staggered by 15 ms.
- Hover: tile scales to 1.06 over 100 ms with an accent outline.
- Drop: the tile pulses once, the ring collapses over 120 ms, a progress ring then a check mark appears at the drop point.
- Hide: fade over 100 ms.
- If the system animation setting is off, skip every transition.

**Renderer options for the overlay**

| Option | Strengths | Weaknesses | Verdict |
| --- | --- | --- | --- |
| WPF with DWM backdrop | Fastest to build; XAML animations; built-in Fluent theme since .NET 9; one toolchain | `AllowsTransparency` (layered window) can hurt performance and DWM effects, so use the DWM backdrop path and test both | Start here |
| Direct2D with DirectComposition | Lowest latency, per-pixel alpha, full control | Most code: text, hit testing and animation are ours | Fallback if M1 misses targets |
| WinUI 3 | Native Fluent controls, Composition animations | Heavier runtime and packaging, less low-level control of the window and its drop target, harder idle-memory goal | Not for the overlay |
| WebView2 (HTML overlay) | Easiest to make beautiful | Extra process, slower first show, fights the idle-memory goal | Rejected |
| Avalonia | Own Skia renderer, flexible styling | Large dependency; cross-platform benefit unused | Rejected |

To keep a renderer swap cheap, the ring geometry, tile ordering and hover state live in plain C# with no WPF types (an `OverlayModel` in `DragConvert.Core`). The WPF views only draw that model.

**Settings, tray and notifications**

- **Settings:** WPF with the built-in Fluent theme (`ThemeMode`), which per one WPF project's [migration notes](https://github.com/mcc1/RoundedTB/releases/tag/v0.2) also applies the Mica backdrop. Use a left navigation list (General, Formats, Output, About). If a control or material is missing, evaluate WPF-UI or [FluentWpfCore](https://www.nuget.org/packages/FluentWpfCore).
- **Tray:** a monochrome icon that follows the taskbar theme; menu with Pause, Settings and Quit. Windows 11 often hides tray icons in overflow, so first run asks the user to pin it.
- **Completion:** native Windows notifications with Open and Show in folder actions, and a progress notification for long jobs. Unpackaged apps need an app identity (a Start-menu shortcut with an AppUserModelID); MSIX provides it. Verify in M3.
- **Fast jobs:** conversions that finish in about a second skip the notification and show the check mark on the tile instead.

**Accessibility and DPI**

- Per-Monitor V2 DPI; test at 100%, 150% and 200%, including mixed-DPI monitors.
- Tile labels exposed through UI Automation; the tray picker is fully keyboard operable.
- Honour high contrast and the system animation setting; text contrast meets WCAG AA in light and dark.

**Building and reviewing the UI with Claude Code**

- Add a UI gallery (a separate project or a `--gallery` flag) that renders the overlay, tray picker, settings and toasts with fake data, so visuals can be iterated without the drag pipeline.
- Keep design tokens (radii, spacing, colours, durations) in one `Theme/Tokens.xaml`, so changes are token edits instead of scattered magic numbers.
- Add a snapshot tool that renders each overlay state to PNG into `artifacts/ui/`. Claude Code can open those images to review its own changes. Real backdrop blur and DWM effects only show on a real desktop, so check those by hand.
- Log the time from hook event to first frame, and fail the manual checklist if it exceeds the 150 ms target.

## Architecture

A single tray process owns the hooks, overlay, UI and engine; Explorer integrations are thin forwarders that pass file paths to it over a named pipe.

> *Architecture diagram (see the source doc): 3 entry points (drag overlay, tray drop zone, SendTo/shell forwarder) feed 1 tray process (Native hooks and drop target, App UI, Core engine), which calls 4 providers (FFmpeg, Image, PDF, Metadata).*

The highlighted native layer is the riskiest piece, which is why milestone M1 builds it first. Core and Providers have no Win32 or UI dependencies, so the whole conversion path can be tested from a plain test project and the CLI harness.

**Process model**

- One long-lived tray process per user session, enforced with a named mutex; later launches forward their paths to it and exit.
- Conversions run on worker threads or in child processes (FFmpeg, ExifTool), never on the UI thread or the hook thread.
- The shell extension (post-MVP) is a separate native component that never loads .NET into `explorer.exe`.

## Drag detection and overlay

Windows gives no global "a drag is in progress" event and no way to read another app's drag data, so we detect the drag ourselves and let the overlay be a real drop target that receives the files at drop time.

**Detecting a drag**

- Install low-level hooks (`SetWindowsHookEx` with `WH_MOUSE_LL` and `WH_KEYBOARD_LL`) in a dedicated thread with its own message loop.
- A drag candidate starts on left-button down, then movement beyond `SM_CXDRAG`/`SM_CYDRAG`. Track the trigger modifier state throughout.
- Optionally corroborate with a WinEvent hook for the transient OLE drag-image window; verify behavior on current Windows 11 builds before relying on it.
- The hook callback only records state and posts a message to the UI thread. It must return in well under the system hook timeout or Windows removes the hook silently.

**Showing the overlay**

- When a drag candidate and the trigger are both active, show a borderless topmost window with `WS_EX_NOACTIVATE`, `WS_EX_TOOLWINDOW` and layered styles, positioned near the cursor and clamped to the monitor work area.
- Register the window with `RegisterDragDrop` as an `IDropTarget`; each tile is a hit region inside it.
- On `DragEnter` read the `IDataObject`, inspect `CF_HDROP` (and `FileGroupDescriptorW` for virtual files such as mail attachments) to learn file types, then filter the tiles.
- On `Drop` copy the paths (materialize virtual files to a temp folder first) and hand a `ConversionJob` to the engine.

Because the real file list is only available once the cursor enters the overlay, the overlay first shows a generic tile set and refines it on `DragEnter`. This is the main UX compromise of the drop-target design.

**Edge cases to handle explicitly**

| Case | Behavior |
| --- | --- |
| Elevated source window (UIPI) | Hooks don't see it; show a tray hint to run elevated or ignore |
| Per-monitor DPI | Per-Monitor V2 manifest; size tiles from the monitor under the cursor |
| Multi-monitor and virtual desktops | Clamp to the cursor's monitor; overlay lives on the current desktop |
| Games and exclusive fullscreen | Disable hooks when a fullscreen app is foreground |
| Remote desktop sessions | Disable by default |
| Drag cancelled | Hide overlay on button-up or Esc; never leave the window orphaned |
| Hook removed by Windows | Heartbeat check and re-install |

Keep a user setting to change or disable the trigger, since Ctrl+Shift can collide with input-language switching on some keyboard layouts.

## Explorer integration

Explorer has no plugin API for drags, so the officially supported hooks are the context menu, SendTo and the tray; the drag overlay is the app's own layer on top.

| Mechanism | What it gives | Cost and caveats | Phase |
| --- | --- | --- | --- |
| Tray drop zone | Always-available target for any file | Trivial; no shell integration | MVP |
| SendTo shortcut | Right-click, Send to, Convert | One `.lnk` in `shell:sendto`; opens a picker | MVP |
| Win11 modern context menu | Native-looking submenu with formats | `IExplorerCommand` COM server, needs package identity (sparse MSIX) | Post-MVP |
| Classic `IContextMenu` or registry verbs | Works on Win10 and under Show more options | Shell extension DLL or simple verbs; simple verbs launch the app per file | Optional |
| Explorer selection query | Radial menu with the real file list at drag start | `IShellWindows` and `IFolderView2` via COM; Explorer and desktop only | Later |

**Design decisions**

- The context-menu handler is a thin native component (C++ or Rust) that only forwards file paths to the main app over a named pipe. We do not load the .NET runtime into `explorer.exe`.
- The main app is a single-instance tray process; the SendTo entry and shell handler launch a tiny forwarder when it isn't running, then hand off paths.
- The shell component is versioned independently, because an updated handler can't replace a DLL Explorer has loaded until Explorer restarts. Declare it in the Store MSIX; use a sparse package only for the unpackaged GitHub build.
- PowerToys' Image Resizer is the closest open-source reference for the `IExplorerCommand` plus sparse-package pattern.

## Conversion engine

The engine is a registry of providers behind one interface, so each library is isolated and a new format means a new provider or a new row in a capability table, not a change to the UI.

**Core abstractions**

```csharp
public interface IConversionProvider
{
    string Id { get; }
    IReadOnlyCollection<ConversionCapability> Capabilities { get; } // (inputExt, outputExt, options)
    Task<ConversionResult> ConvertAsync(ConversionJob job, IProgress<double> progress, CancellationToken ct);
}

public sealed record ConversionJob(string InputPath, string OutputPath, string TargetFormat, IReadOnlyDictionary<string, string> Options);
```

- `FormatCatalog` merges provider capabilities and answers "which targets are valid for these input files"; the overlay and tray picker read from it.
- `JobQueue` runs jobs with bounded parallelism (default: CPU count / 2), supports cancel, and reports progress to the toast.
- Providers that wrap CLI tools run them as child processes with a job object, a timeout and stderr capture; in-process libraries run on worker threads.
- Output goes to a temp file in the destination folder and is renamed on success, so a crash never leaves a half-written file with the final name.

**Provider candidates**

| Domain | Candidate | Notes and licensing |
| --- | --- | --- |
| Audio and video | FFmpeg (bundled `ffmpeg.exe`) | Choose an LGPL build to ship freely; GPL builds (x264, x265) carry copyleft obligations. Process-invoked, so linking is not an issue but attribution is |
| Raster images | Magick.NET or libvips (NetVips) | Magick.NET is Apache-2.0; HEIC needs libheif; check the patent notes on HEVC-based HEIC |
| Image fallback | SkiaSharp or Windows Imaging Component | WIC handles many formats natively; HEIF needs the Microsoft HEIF extension installed |
| PDF | PDFium (PdfiumViewer or PDFtoImage) and PdfPig or QPDF | Split, merge, reorder via QPDF or PdfPig; compression via Ghostscript is AGPL, so avoid or isolate |
| Metadata | MetadataExtractor or ExifTool | ExifTool is Perl, Artistic/GPL; shipping it is fine as a separate process |
| QR codes | ZXing.Net | Apache-2.0 |
| Archives | SharpCompress or 7-Zip | LGPL and BSD terms; 7-Zip is a CLI dependency |

**Decisions needed before shipping**

- Which FFmpeg build and codecs to bundle, and who handles its legal review.
- Whether HEIC output is in scope given patent licensing around HEVC.
- Download-on-demand for large engines (FFmpeg, PDF tools) to keep the installer small, versus bundling everything for fully offline first run.

## Tech stack and solution layout

The app is C# on .NET 10 (LTS) with thin native code only where Windows requires it.

| Concern | Choice | Reason |
| --- | --- | --- |
| Language and runtime | C# 14, .NET 10 (LTS, supported to Nov 2028), self-contained single-file publish | No runtime install for users; trimming is optional |
| Win32 interop | CsWin32 source generator | Typed P/Invoke and COM without hand-written signatures |
| Overlay UI | WPF on the built-in Fluent theme with a DWM backdrop; Direct2D if M1 misses latency targets (see UI and visual design) | WPF is fast to build; the overlay still sets extended styles through `HwndSource` |
| Settings and tray UI | WPF on the built-in Fluent theme; evaluate WPF-UI for extra controls | Same process, one toolchain |
| Tray icon | `H.NotifyIcon` or `System.Windows.Forms.NotifyIcon` | Mature and simple |
| DI, config, logging | `Microsoft.Extensions.*` and Serilog to a rolling file | Familiar, testable |
| Tests | xUnit, FluentAssertions, a fake provider for engine tests | Engine is UI-free and fully testable |
| Shell extension | C++/WinRT or Rust (`windows` crate), post-MVP | Keeps CLR out of `explorer.exe` |
| Installer and updates | MSIX via the Microsoft Store, plus signed GitHub releases and winget | Free signing, hosting and updates; see Distribution and cost |
| CI | GitHub Actions on `windows-latest`: build, test, sign, publish artifacts | Matches the tooling Claude Code works with well |

**Proposed repository layout**

```text
DragConvert/
  CLAUDE.md
  docs/                       design.md, decisions/ (ADRs)
  src/
    DragConvert.Core/         job model, FormatCatalog, JobQueue, settings (no UI, no Win32)
    DragConvert.Providers/    FFmpeg, Image, Pdf, Metadata providers
    DragConvert.Native/       CsWin32 wrappers: hooks, drop target, shell helpers
    DragConvert.App/          tray, overlay, toast, DI composition root
    DragConvert.ShellExt/     native context-menu handler (post-MVP)
  tests/
    DragConvert.Core.Tests/
    DragConvert.Providers.Tests/   uses small fixture files
    DragConvert.Native.Tests/      pure logic only; hook behavior is manual
  tools/                      scripts: fetch ffmpeg, sign, package
  Directory.Build.props       nullable on, warnings as errors, analyzers
```

**Conventions**

- Nullable reference types on; warnings as errors in CI.
- All Win32 and COM usage lives in `DragConvert.Native`; Core and Providers reference no Windows UI APIs.
- Every provider has fixture-based tests; hook and overlay behavior gets a manual test checklist because it can't be automated reliably.
- Architecture decisions are recorded as short ADRs in `docs/decisions/`.

## Distribution and cost

The project is a personal, open-source tool with no budget, and every part of the plan can ship for $0: free signing, free hosting and free CI, in exchange for being public and openly licensed.

| Need | Zero-cost route | Conditions and caveats |
| --- | --- | --- |
| Source, CI, releases | Public GitHub repo, GitHub Actions, GitHub Releases | Actions are free for public repositories; check current limits |
| Signing for GitHub builds | [SignPath Foundation](https://signpath.org/terms) free certificate for OSS | OSI-approved license, public repo, free downloads, no proprietary components, CI-built artifacts, MFA for maintainers; the certificate is OV, so SmartScreen reputation still builds with downloads and early users may see a warning |
| Store distribution | [Free individual Microsoft Store account](https://blogs.windows.com/windowsdeveloper/2025/09/10/free-developer-registration-for-individual-developers-on-microsoft-store/); Microsoft hosts, signs and auto-updates an MSIX | Store certification applies, and a tray app with global input hooks must be checked against its policies in an M6 spike |
| Updates outside the Store | winget manifest for the signed GitHub build | Free; manifests are submitted as pull requests |
| Certificates | Do not buy one | Self-signed certificates are free but each user would have to trust them manually, so use them for development only |

**Licensing decisions**

- Pick an OSI-approved license (MIT, Apache-2.0 or GPL-3.0) before the first public commit; SignPath requires it.
- Bundle FFmpeg as a separate LGPL build with license notices and a source offer; avoid AGPL tools such as Ghostscript.
- Run ExifTool as a separate process; keep every dependency's license in a `THIRD-PARTY-NOTICES.md`.
- Use only openly licensed icons and fonts (system fonts such as Segoe UI Variable are used at runtime, not bundled).

**Release path**

1. From the first milestone, publish unsigned CI builds to GitHub Releases as pre-releases, with a note about the SmartScreen prompt.
2. Once the repo is public with a license and a CI build, apply to the SignPath Foundation and switch releases to signed builds.
3. In M6, submit the MSIX to the Microsoft Store and add the winget manifest.
4. If the Store rejects the app over global hooks, GitHub plus SignPath plus winget remains the distribution channel.

## Risks and mitigations

The biggest risks are the global hooks (reliability and antivirus reputation) and the codec licensing; both should be tested before polishing anything else.

| Risk | Likelihood | Impact | Mitigation |
| --- | --- | --- | --- |
| Antivirus or SmartScreen flags global input hooks | High | High | Free signing (Store for the MSIX, SignPath Foundation for GitHub builds), minimal hook logic, no keystroke storage, publish a plain statement of what hooks are used for, submit builds to vendors for allow-listing |
| Hooks silently removed by Windows on slow callbacks | Medium | High | Do nothing but record state in the callback; heartbeat and re-install; log timing |
| Drag overlay behaves differently across source apps | High | Medium | Manual test matrix (Explorer, desktop, Chrome, Edge, Outlook, Teams, VS Code); keep tray and SendTo as fallbacks |
| Trigger key collides with OS shortcuts | Medium | Medium | Configurable trigger; default tested on common layouts |
| Elevated apps invisible to a normal-integrity process | Medium | Low | Document it; optional elevated helper later |
| FFmpeg or codec licensing and HEVC patents | Medium | High | LGPL build, avoid bundling GPL codecs, ship license notices and a source offer for FFmpeg, make HEIC and HEVC optional |
| Large installer from bundled engines | Medium | Low | Download engines on first use with hash verification, or ship a slim and a full installer |
| Windows 11 menu integration brittle across builds | Medium | Medium | Isolate behind the forwarder; treat as post-MVP; test on Insider builds |
| Name or UI resemblance to Tangerine | Low | High | Original name, assets and design; check trademark before launch |
| Conversion output corrupt on crash | Low | Medium | Temp file then atomic rename; cleanup on startup |

## Implementation plan

The plan retires the riskiest unknown first: a spike proves the drag overlay works before any conversion code exists. Each milestone is sized for one or a few Claude Code sessions and ends with something you can run.

### M0. Repository and CI

- [ ] Create the solution and projects from the layout above, with `Directory.Build.props` (nullable, warnings as errors, analyzers)
- [ ] Add `CLAUDE.md` and `docs/design.md` (this document exported as Markdown)
- [ ] GitHub Actions: restore, build, test on `windows-latest`

**Done when:** CI is green on an empty app that builds and runs.

### M1. Spike: drag detection and overlay

- [ ] `DragConvert.Native`: low-level mouse and keyboard hooks on a dedicated thread, exposing events (`DragStarted`, `TriggerChanged`, `DragEnded`)
- [ ] Overlay window with no-activate, topmost, layered styles, clamped to the cursor's monitor
- [ ] `IDropTarget` implementation that logs `CF_HDROP` paths on `DragEnter` and `Drop`
- [ ] Manual test matrix: Explorer, desktop, Chrome, Edge, Outlook attachment, VS Code

* [ ] Measure overlay show latency, animation smoothness and memory with the WPF renderer; record the numbers and the renderer decision in an ADR

**Done when:** dragging a file with the trigger held shows the overlay and dropping on a tile logs the correct path in every source in the matrix. Write down what failed in `docs/decisions/0001-drag-detection.md`; this result decides whether the overlay design stands.

### M2. Core engine and first providers

- [ ] `Core`: `ConversionJob`, `IConversionProvider`, `FormatCatalog`, `JobQueue` with cancel and progress, atomic output naming
- [ ] FFmpeg provider (bundled `ffmpeg.exe`): audio formats, MP4/WebM, extract audio, video to GIF
- [ ] Image provider: JPEG, PNG, WebP, TIFF, BMP, GIF, HEIC (decision on HEIC output first)
- [ ] A throwaway CLI harness: `dragconvert convert <file> <format>` for fast iteration
- [ ] Fixture-based tests for each provider

**Done when:** the CLI converts the fixture set correctly and tests pass in CI.

### M3. Tray app and integration

- [ ] Single-instance tray process with Settings window (trigger key, output rule, autostart)
- [ ] Wire overlay tiles to `FormatCatalog`; refine tiles on `DragEnter` from real file types
- [ ] Toast with Open and Show in folder; error reporting with a one-line reason
- [ ] Tray drop zone and SendTo entry, with a forwarder for pipe handoff

* [ ] UI pass per the UI and visual design section: `Theme/Tokens.xaml`, ring layout from `OverlayModel`, motion, Win10 fallback, UI gallery and PNG snapshot tool

**Done when:** the headline flow works end to end: drag a HEIC file, hold the trigger, drop on JPEG, get a new file and a toast.

### M4. PDF and tools

- [ ] PDF provider: split, merge, reorder, compress, page extraction to images
- [ ] Metadata strip for images and PDFs
- [ ] Alt-modified tool tiles in the overlay

**Done when:** each tool has fixture tests and appears in the overlay and the tray picker.

### M5. Hardening

- [ ] Per-Monitor V2 DPI, multi-monitor and virtual desktop behavior
- [ ] Disable hooks for fullscreen apps and remote sessions; hook heartbeat and re-install
- [ ] Performance pass against the targets (150 ms overlay, idle memory and CPU)
- [ ] Crash-safe temp files and startup cleanup; log rotation

* [ ] Accessibility and visual QA: high contrast, reduced motion, UI Automation names, 100/150/200% DPI, light and dark themes

**Done when:** the manual checklist in `docs/test-matrix.md` passes on Windows 10 and Windows 11.

### M6. Packaging, signing, release

- [ ] Build the MSIX in CI; register a free Microsoft Store developer account and submit it
- [ ] Apply to the SignPath Foundation and sign GitHub release builds from CI (needs a public repo and an OSI license)
- [ ] Submit signed builds to major antivirus vendors for allow-listing
- [ ] Updates through the Store; winget manifest for GitHub releases; engine download with hash verification if chosen

**Done when:** a clean Windows VM installs, runs and updates the Store package with no warnings, and the signed GitHub build installs with at most a SmartScreen reputation prompt.

### M7. Win11 context menu (post-MVP)

- [ ] Native `IExplorerCommand` handler registered in the MSIX manifest (sparse package only for unpackaged GitHub builds); pipe forwarding to the app
- [ ] Same format list as the overlay, driven from `FormatCatalog`

**Done when:** right-clicking an image on Windows 11 shows a Convert submenu in the modern menu.

## Working with Claude Code

Export this document to Markdown as `docs/design.md`, add the `CLAUDE.md` below, and give Claude Code one milestone at a time with its acceptance criteria.

**Suggested `CLAUDE.md`**

```markdown
# DragConvert

Windows tray app that converts files from a drag gesture. Offline only. See docs/design.md.

## Stack
C# 14, .NET 10, WPF (Fluent theme), CsWin32. Solution in src/, tests in tests/.

## Rules
- All Win32/COM code lives in DragConvert.Native. Core and Providers must not reference UI or Win32.
- Nullable enabled; warnings are errors. Run `dotnet build` and `dotnet test` before saying a task is done.
- Win32 hook callbacks must only record state and post to another thread. Never do I/O or allocate heavily in them.
- Never modify the source file. Write output to a temp file and rename atomically.
- No network calls and no telemetry.
- Record non-obvious decisions as short ADRs in docs/decisions/.

## Commands
- Build: dotnet build DragConvert.sln
- Test: dotnet test DragConvert.sln
- Run app: dotnet run --project src/DragConvert.App

## Current milestone
See docs/design.md, Implementation plan. Work on one milestone at a time.
```

**How to drive it**

- Start in plan mode for M1 and ask for the hook and drop-target design before code; the spike's job is learning, so ask for logging at every step.
- Hooks and overlay can't be unit tested, so have Claude Code write a manual test checklist in `docs/test-matrix.md` and run through it yourself on a real desktop. Claude Code can build and run tests; it cannot watch your screen.
- Commit at the end of each milestone and have Claude Code summarize decisions into an ADR before you clear the session.
- Keep sessions short: one milestone, or half of one for M2 and M4, then start fresh with `CLAUDE.md` and the design doc.
- Ask for tests first for `Core` and each provider; fixtures can be tiny generated files kept in `tests/fixtures`.
- Have it add a CI job that fails on warnings, so quality is enforced without you checking.

**Starter prompts**

1. M0: "Read docs/design.md and CLAUDE.md. Create the solution and projects described in the repository layout, add Directory.Build.props, a GitHub Actions workflow that builds and tests on windows-latest, and an empty tray app that starts and exits cleanly. Don't implement features yet."
2. M1: "Implement milestone M1 from the design. First propose the threading model for the hooks and overlay in a short plan and wait for my approval. Then build the spike with verbose logging and write docs/test-matrix.md for me to run."
3. M2: "Implement the Core engine and the image and FFmpeg providers per the design, test-first. Add the CLI harness. Stop after the provider tests pass and summarize any licensing questions you hit."

## Open questions

- [ ] Decided: personal, open-source tool with no budget (see Distribution and cost). Still open: MIT, Apache-2.0 or GPL-3.0?
- [ ] Default trigger key: Ctrl+Shift, Alt, or hover-and-hold? Decide after the M1 spike on real keyboards.
- [ ] Is the overlay's two-step tile refinement (generic tiles first, filtered on `DragEnter`) acceptable, or is Explorer-selection querying worth building early?
- [ ] FFmpeg: bundle an LGPL build, or download on first use?
- [ ] HEIC and HEVC output: in scope given patent licensing?
- [ ] Overlay renderer: WPF first, or Direct2D and DirectComposition from the start? Decide from the M1 latency, animation and memory measurements.
- [ ] Installer: confirm in the M6 spike that a Store MSIX works for a tray app with global hooks; Velopack or a plain zip is the GitHub fallback.
- [ ] Final product name and trademark check.
- [ ] Minimum supported OS: Windows 10 22H2 plus Windows 11, or Windows 11 only?

# Manual test matrix

Hooks, the overlay and drag-and-drop can't be unit tested reliably, so this checklist is run by hand on a real desktop. Record results in the tables; anything that fails goes into the relevant ADR.

## M1: drag detection and overlay spike

### Setup

1. Build: `dotnet build DragConvert.slnx -c Release`
2. Run with the live log window, once per renderer:
   - `src\DragConvert.App\bin\Release\net10.0-windows10.0.19041.0\DragConvert.exe --console --renderer=layered`
   - `src\DragConvert.App\bin\Release\net10.0-windows10.0.19041.0\DragConvert.exe --console --renderer=dwm`
3. The same log is written to `%LOCALAPPDATA%\DragConvert\logs\` (tray icon → Open log folder).
4. Quit from the tray icon (right-click → Quit) before switching renderer.

Run the app **non-elevated**, like a normal user would. Windows (UIPI) blocks drag-and-drop from normal apps into an elevated process: the cursor shows a crossed circle and no `DragEnter` is logged. An app started from an elevated terminal (including an elevated Claude Code session) inherits elevation, so start it by double-clicking `tools\run-spike.cmd` (layered) or `tools\run-spike-dwm.cmd` in Explorer. If it is elevated anyway, the log shows a warning and the tray shows a notification.

### The gesture

1. Start dragging a file (press, move a few pixels).
2. Hold **Ctrl+Shift** (either before or after starting the drag).
3. The ring appears around the cursor. Move onto a tile: it highlights and the caption says e.g. "JPEG, same folder".
4. Add **Alt** while holding Ctrl+Shift: the tiles switch to tools.
5. Drop on a tile. The log shows a `DROP on tile …` line with the path(s), and the overlay hides.

### Per-source checks

For each source, test both renderers. Mark ✅ / ❌ and note anything odd.

| # | Source | What to drag | Overlay appears | Paths in `DragEnter` log | `DROP` logs correct path | Notes |
|---|---|---|---|---|---|---|
| 1 | Explorer window | A .jpg, then 3 mixed files | | | | |
| 2 | Desktop | A file on the desktop | | | | |
| 3 | Chrome | A file from the downloads page (chrome://downloads) | | | | |
| 4 | Chrome | An image from a web page (likely no path, maybe virtual) | | | | |
| 5 | Edge | Same as 3 and 4 | | | | |
| 6 | Outlook (classic or new) | An attachment from an email (expect virtual file, no path) | | | | |
| 7 | VS Code | A file from the Explorer sidebar | | | | |

### Behaviour checks (either source, both renderers)

| # | Check | Expected | Layered | DWM |
|---|---|---|---|---|
| B1 | Release Ctrl+Shift with the cursor outside the ring | Overlay hides; drag continues normally | | |
| B2 | Press Esc during the drag | Overlay hides and the drag is cancelled | | |
| B3 | Drop in the ring's empty centre | Layered: drop falls through to the window underneath. DWM: drop is rejected (no-op) | | |
| B4 | Drop on a tile | Overlay hides; source file untouched (not moved or deleted) | | |
| B5 | Overlay never takes focus | The source window keeps its active title bar; typing still goes to it | | |
| B6 | Near a screen edge or corner | Ring stays fully on screen (clamped) | | |
| B7 | Second monitor, if you have one (ideally a different scaling) | Ring appears on the cursor's monitor, centred, right size | | |
| B8 | Ctrl+Shift while dragging a window title bar or selecting text | Overlay may appear (known spike limitation); note whether "drag image visible" in the log is true or false | | |
| B9 | Show animation and hover | Smooth fade/scale in, tile grows on hover, no flicker | | |
| B10 | Light and dark Windows theme | Tiles and caption readable in both | | |

### Numbers to record

From the `Overlay shown #N … ms to first frame` log lines (ignore show #1 if it's much slower):

| Renderer | First show (ms) | Typical show (ms) | Over 150 ms? | Working set after a few shows (MB) | Backdrop applied |
|---|---|---|---|---|---|
| Layered | | | | | n/a |
| DWM | | | | | |

The timing is measured from the drag start when Ctrl+Shift is already held, or from when the key poll detects the chord (up to 10 ms after the press).

### Result

Write the outcome, failures and the renderer decision into `docs/decisions/0001-drag-detection.md`.

## M3: tray app and integration

Start with `tools\run-spike.cmd` (non-elevated). Notifications appear bottom-right.

| # | Check | Expected | Result |
|---|---|---|---|
| T1 | Drag a .jpg from Explorer, hold Ctrl+Shift | Ring shows only image targets (PNG, WebP, TIFF, BMP, GIF, PDF), no audio/video | |
| T2 | Drop it on PNG | `<name>.png` appears beside it; "Converted to PNG" notification with Open and Show in folder; both buttons work | |
| T3 | Drag 3 photos and 1 video together | Tiles show counts ("JPEG · 3 of 4"), formats valid for all first | |
| T4 | Drop that mix on an image format | Photos converted, video skipped and named in the notification | |
| T5 | Drag a folder with subfolders | Caption says "N files in 1 folder"; outputs land beside each original, subfolders included | |
| T6 | Convert a long video (over 2 s) | Progress notification with a bar and Cancel; Cancel stops it and leaves no partial file | |
| T7 | Drop the same file twice | Second output is `<name> (1).<ext>`; nothing overwritten | |
| T8 | Right-click a file → Send to → DragConvert | Tray window opens with that file's formats; clicking one converts it | |
| T9 | Left-click the tray icon | Tray window with drop zone and recent conversions; Open/Show work | |
| T10 | Drop files onto the tray window | Formats listed; Tab/Enter work without the mouse | |
| T11 | Settings: change trigger to Ctrl+Alt | Overlay now needs Ctrl+Alt; Ctrl+Shift does nothing | |
| T12 | Settings: fixed output folder | Outputs go to that folder | |
| T13 | Settings: Start with Windows on, sign out and in | DragConvert starts in the tray | |
| T14 | Tray menu: Pause drag overlay | Overlay no longer appears until unpaused | |
| T15 | Drag a .docx with the trigger held | Caption "Nothing here can be converted", no tiles | |
| T16 | Drag an Outlook attachment with the trigger held | Caption "Attachments can't be converted yet" | |
| T17 | Start a second copy of DragConvert.exe | No second tray icon; the existing tray window opens | |

## M4: PDF and tools

| # | Check | Expected | Result |
|---|---|---|---|
| P1 | Drag a PDF, hold Ctrl+Shift, then add Alt | Ring switches to tools (Split pages, Pages to images, Compress, Remove metadata); releasing Alt switches back | |
| P2 | Drag two PDFs, Alt, drop on Merge PDFs | "<first> (merged).pdf" with all pages in drag order | |
| P3 | Split pages on a 3-page PDF | Folder "<name> pages" with 3 PDFs | |
| P4 | Pages to images | Folder "<name> images" with one PNG per page, sharp at 100% zoom | |
| P5 | Compress a scanned or photo-heavy PDF | Smaller "(compressed)" copy that looks the same; a text-only PDF reports "already as small as…" | |
| P6 | Drag 3 photos, Alt, Combine to PDF | One PDF, a page per photo, upright | |
| P7 | Remove metadata on a phone photo | "(clean).jpg" with no camera/GPS info (file Properties → Details), still upright | |
| P8 | Remove metadata on an MP3 with tags | "(clean).mp3" plays; title/artist gone | |
| P9 | Password-protected PDF → Split pages | Notification says the PDF is password-protected | |
| P10 | Tray window with a PDF | Tools section below formats; buttons work with Tab/Enter | |

## M4b: format breadth

| # | Check | Expected | Result |
|---|---|---|---|
| F1 | Drag an iPhone HEIC photo onto AVIF | "<name>.avif" opens in the Photos app | |
| F2 | Drag an SVG logo onto PNG | Sharp PNG at twice the SVG's size, transparent background kept | |
| F3 | Drag an MP4 onto MOV, MKV, AVI and WMV (one at a time) | Each plays in Media Player / VLC with sound | |
| F4 | Drag a Word document onto Markdown and HTML | Headings, bold, links and tables survive; HTML opens in the browser | |
| F5 | Drag a text-based PDF onto TXT | Text page by page; a scanned PDF gives an almost empty file | |
| F6 | Drag a CSV exported from Excel (with accents) onto XLSX, then the XLSX back onto CSV | Both open in Excel with accents intact and numbers as numbers | |
| F7 | Drag an SRT onto VTT | Plays as subtitles in a browser `<track>` or VLC with the same timings | |
| F8 | Alt-tools on a photo, an MP3 and a phone video: Compress | Smaller copies that look/sound fine; overlay shows one Compress tile | |
| F9 | Alt-tools on an MP3: Normalize, Make mono, Waveform | Even loudness; mono file; PNG of the waveform | |
| F10 | Alt-tools on a video: Mute, Snapshot | Silent copy; JPG of a representative frame | |
| F11 | Drag a ZIP, a .tar.gz, a RAR and a 7z, Alt, Extract | Folder beside each archive with its contents; a password-protected one reports it | |
| F12 | Drag a mixed selection (e.g. a PDF, a TXT and an EXE), Alt, Zip | One ZIP keeping the files' relative folders | |

## M5: hardening

| # | Check | Expected | Result |
|---|---|---|---|
| H1 | Start a fullscreen game or press F11 for a fullscreen video, wait 5 s | Log: "Mouse hook removed: fullscreen app in front"; back on the desktop: "Mouse hook installed" | |
| H2 | Connect to this PC over Remote Desktop | Hook removed ("remote desktop session"); overlay doesn't appear | |
| H3 | Tray menu → Pause drag overlay | Log "Mouse hook removed: paused"; overlay doesn't appear; unpausing reinstalls | |
| H4 | Second monitor with different scaling (e.g. 100% + 150%) | Overlay appears centred on the cursor's monitor at the right size; glass backdrop lines up | |
| H5 | Virtual desktops: switch desktop, drag with the trigger | Overlay appears on the current desktop | |
| H6 | Switch Windows between light and dark | Ring style follows within a second or so (glass is neutral) | |
| H7 | Turn on high contrast | Overlay switches to the ring with system colours | |
| H8 | Turn off "Animation effects" in Settings → Accessibility | Overlay appears and disappears without animations | |
| H9 | Kill DragConvert in Task Manager during a long video conversion | ffmpeg.exe disappears too; at next start the log says it removed the leftover temp file | |
| H10 | Leave the app idle for a minute | Task Manager memory (working set) drops well below 50 MB; CPU stays at 0% | |
| H11 | Narrator or Inspect over the tray window | Format and tool buttons are announced with names | |
| H12 | Windows N edition (if available) | MP4 isn't offered; log: "H.264 encoding (h264_mf) unavailable" | |

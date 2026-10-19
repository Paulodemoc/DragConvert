# Manual test matrix

Hooks, the overlay and drag-and-drop can't be unit tested reliably, so this checklist is run by hand on a real desktop. Record results in the tables; anything that fails goes into the relevant ADR.

## M1: drag detection and overlay spike

### Setup

1. Build: `dotnet build DragConvert.slnx -c Release`
2. Run with the live log window, once per renderer:
   - `src\DragConvert.App\bin\Release\net10.0-windows\DragConvert.exe --console --renderer=layered`
   - `src\DragConvert.App\bin\Release\net10.0-windows\DragConvert.exe --console --renderer=dwm`
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

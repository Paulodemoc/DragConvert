# 0004. Tray app and integration (M3, functional part)

Date: 2026-10-19
Status: accepted; the UI pass (tokens, motion, gallery, snapshots, renderer choice) follows separately

## Decisions

- **Notifications: WinRT toasts directly**, with the app targeting `net10.0-windows10.0.19041.0`. The unpackaged build
  registers an AppUserModelID under `HKCU\Software\Classes\AppUserModelId\DragConvert.App` and sets it on the process,
  which is enough for toasts with buttons; no Start-menu shortcut is needed. Button clicks arrive through the toast's
  `Activated` event while the app runs (a tray app always is). Rejected: the archived Community Toolkit notifications
  package, and Shell_NotifyIcon balloons (no buttons, no progress). Cost: the Windows SDK projection assembly ships
  with the app.
- **Notification behaviour** (ADR 0003): one summary per drop with Open / Show in folder / Open log; a progress toast
  with a Cancel button for batches still running after 1.5 s, replaced in place by the result; a "stays until
  answered" confirmation above 200 files.
- **The whole overlay disc is a drop target**, centre included. Windows then reports the dragged files as soon as the
  ring appears under the cursor, so tiles are correct from the start instead of "generic first, refined on
  DragEnter". Dropping in the centre is a no-op (the drag ends) rather than falling through to the window below.
- **Single instance**: a named mutex per user and session; later launches send their paths over a named pipe
  (`PipeOptions.CurrentUserOnly`) and exit. The exe is its own forwarder; no separate forwarder binary. An elevated
  launch can't hand off to a non-elevated instance (pipe owner check), the same boundary as drag-and-drop (ADR 0001).
- **Send To**: `%APPDATA%\Microsoft\Windows\SendTo\DragConvert.lnk` → `DragConvert.exe --send-to`, created or removed
  at startup per the setting (skipped for `--smoke`).
- **Tray window** (left-click the tray icon): drop zone, the picker for Send To or dropped files (union of targets
  with counts, keyboard operable, UI Automation names), and the last 10 conversions.
- **Settings** in `%LOCALAPPDATA%\DragConvert\settings.json`, applied live: trigger chord (or off), output beside the
  original or a fixed folder, start with Windows (HKCU Run key), Send To entry, overlay style (next start).
- **Fluent look for windows** via WPF's `ThemeMode="System"` (experimental API, WPF0001 suppressed).
- **Alt / tools mode is off until M4**, when there are tools to show.
- **Virtual files** (Outlook attachments): detected, but the overlay says "Attachments can't be converted yet";
  materialising `FileContents` needs a custom `IDropTarget` (later).

## Verified

- Send To hand-off at normal integrity, picker shows all targets for a HEIC, clicking JPEG (through UI Automation)
  produced `IMG_0001.jpg` and the "Converted to JPEG" notification.
- A folder with HEIC, WAV, a 20 s MP4 and a .docx: targets listed with counts ("GIF, 2 of 4"); WebM converted the MP4
  into its subfolder in 17 s with the progress notification, and the summary named the 3 skipped files.

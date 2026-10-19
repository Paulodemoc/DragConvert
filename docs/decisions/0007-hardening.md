# 0007. Hardening (M5)

Date: 2026-10-19
Status: accepted; the manual checks in docs/test-matrix.md (M5) remain to be run on Windows 10 and 11

## Decisions

- **Hook supervision.** Every 3 s the overlay controller decides whether the mouse hook should exist: it's removed
  while paused (tray menu), in a Remote Desktop session (`SM_REMOTESESSION`) and while a fullscreen app is in front
  (`SHQueryUserNotificationState`: busy, D3D fullscreen or presentation mode), and reinstalled afterwards. Pause now
  removes the hook instead of ignoring events.
- **Hook watchdog.** Windows silently removes low-level hooks whose callbacks run too long. The hook records the tick
  of its last event; if the cursor moved between two supervisor checks but the hook saw nothing, it's reinstalled
  (at most every 30 s) and a warning is logged.
- **Child processes die with the app.** A job object with `KILL_ON_JOB_CLOSE` holds every ffmpeg.exe
  (`ProcessRunner.ProcessStarted` hook, set by the app so Providers stays free of Win32). Verified by killing
  DragConvert mid-conversion: ffmpeg exited at once.
- **Crash-safe temp files.** `TempJournal` records each temp file or folder while it exists
  (`%LOCALAPPDATA%\DragConvert\temp-journal.txt`); startup deletes leftovers, but only paths carrying the temp marker,
  so a damaged journal can't delete user files. Verified: the half-written WebM from the kill test was removed at the
  next start.
- **Logs** roll daily and at 10 MB, 7 files kept; Information level in Release (Debug with `--console` or in Debug
  builds).
- **Memory and CPU.** Workstation non-concurrent GC. After 20 s of nothing happening the app trims its working set
  (at most once a minute). Spinner animations now run only while a conversion is shown: a forever animation kept WPF
  rendering frames while hidden. Measured idle (Release, glass): working set 17 MB after trim (160 MB before),
  private bytes 79 to 92 MB, CPU about 0.4% of one core (was 1.25%).
- **DPI and monitors.** The overlay is centred twice on show (moving to a monitor with different scaling resizes it),
  and the glass backdrop is captured at the target monitor's DPI (`GetDpiForMonitor`). Tool windows show on the
  current virtual desktop.
- **Themes and accessibility.** High contrast always uses the ring style, whose palette switches to system colours.
  Light/dark/accent/high-contrast changes rebuild the overlay (debounced 1 s). Tiles carry UI Automation names;
  animations follow the Windows animation setting.
- **Windows "N" editions.** At startup a 1-frame `h264_mf` encode probes for Media Foundation; if it fails, MP4 is
  removed from the targets (`FormatCatalog.Refresh`).

## Not done

- The idle private-memory target (80 MB) is met only sometimes; the WPF and Windows SDK projection baseline is
  around 75 MB. Further cuts would mean lazy-loading the notification projection or trimming WPF.
- Fullscreen, remote-session and multi-monitor behaviour can't be exercised from this machine's automation; they are
  in the manual checklist.

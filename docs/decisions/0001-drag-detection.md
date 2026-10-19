# 0001. Drag detection and overlay (M1 spike)

Date: 2026-10-19
Status: accepted (overlay design stands); renderer choice deferred to the UI pass

## Context

M1 had to prove that the drop-target design works: detect a drag globally, show an overlay near the cursor while
the trigger is held, and receive the dragged files when they are dropped on a tile.

## Decisions

- **Mouse: `WH_MOUSE_LL` on a dedicated thread.** The callback only feeds `DragDetector` and posts transitions
  (drag started, drag ended) to the UI thread.
- **Keyboard: no hook.** Ctrl/Shift/Alt/Esc are read with `GetAsyncKeyState` on a 10 ms timer that runs only while a
  drag is active. A global keyboard hook is the classic keylogger signature, and antivirus flagging is rated
  High/High in the risk table.
- **Drop target: WPF's built-in OLE registration** (`AllowDrop`), which calls `RegisterDragDrop` underneath. A
  hand-written `IDropTarget` in `Native` is only needed if virtual-file contents can't be read through WPF (M3).
- **Delayed hide on release.** The hook sees the button-up before the drop is delivered, so on release over the
  overlay we wait up to 500 ms for the drop instead of hiding at once.
- **Drop effect is Copy (or Link), never Move,** so a source can never delete the original.
- **The app must not run elevated.** UIPI blocks drag-and-drop from normal-integrity apps into an elevated process.
  The app now warns (log and tray notification) when it is elevated.

## Results (Windows 11, single monitor at 100%, layered renderer)

| Measure | Result | Target |
| --- | --- | --- |
| Trigger to first frame | 5.1, 6.1, 9.1, 18.7 ms (4 shows) | 150 ms |
| Explorer drop, correct path | 2 of 2 | all |
| Working set after startup | 136 MB (private 89 MB) | idle under 80 MB |
| Working set after 4 shows | 168 MB (private 127 MB) | |

- Explorer offers `FileDrop` plus, for some files, `FileGroupDescriptorW`/`FileContents`; the descriptor parser read
  the virtual file name correctly.
- The shell drag-image window (`SysDragImage`) was visible in 3 of 4 shows, so it can't reliably confirm an OLE drag
  on its own.
- First manual run failed with a crossed-circle cursor and no `DragEnter`: the app had been launched from an
  elevated terminal (see the elevation decision above).

## Open

- Remaining sources in `docs/test-matrix.md` (desktop, Chrome, Edge, Outlook, VS Code) and the behaviour checks are
  not yet run.
- The `dwm` renderer is not yet measured. The renderer decision (layered ring vs DWM panel vs Direct2D) moves to the
  UI pass in M3, together with the memory growth seen above, which needs a look before M5's targets.
- Overlay also appears for non-file drags (window moves, text selection) while the trigger is held; acceptable for
  now, revisit with the Explorer-selection query or by checking the drop data on `DragEnter`.

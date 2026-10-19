# 0003. Mixed-type drops, folders and error reporting

Date: 2026-10-19
Status: accepted (decided by the project owner before M3)

## Context

The design said "mixed types show only formats valid for all, otherwise a per-type picker", but never designed the
picker, and in practice the intersection is often empty (photo + song) or unhelpful (JPEG + MP4 share only GIF). It
didn't cover dropped folders at all. Error reporting was planned (toasts in M3) but only per file.

## Decisions

1. **Mixed types: union with counts.** The ring shows every target that applies to at least one dragged file, labelled
   "JPEG · 3 of 5". Targets valid for all files come first, then by count. Dropping converts the files that can go to
   that target and skips the rest; the summary notification lists what was skipped. `FormatCatalog.TargetOptionsFor`
   implements the ordering and counts.
   - Rejected: intersection only (often leaves nothing to drop on); a per-category second ring (most UI work, slower
     to use).
2. **Folders: expand recursively.** A dropped folder counts as all files inside it, subfolders included; each output
   goes beside its original. `DropExpander` does the walk: name order, a folder's files before its subfolders, folder
   links (junctions, symlinks) not followed so a link back up the tree can't loop, system files and our own temp files
   skipped, capped at 10,000 files (reported as truncated). Above about 200 files the app asks for confirmation in the
   notification before starting. Expansion runs off the UI thread.
3. **Errors reach the user through Windows notifications.**
   - One summary notification per drop: "5 converted, 1 failed: <reason>", with Open / Show in folder for the outputs
     and the skipped and failed files named (or a "details" action that opens the log when the list is long).
   - Problems that stop a conversion before it starts also notify: engine missing, nothing in the drop can be
     converted, running elevated (already done).
   - Fast single-file jobs keep the design's check mark on the tile instead of a notification; failures always notify.
   - Messages come from `ConversionResult.Error`, which providers already keep to one line.
   - Notifications with buttons need an app identity (Start-menu shortcut with an AppUserModelID, or the MSIX); verify
     in M3.

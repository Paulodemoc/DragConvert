# 0005. UI pass (M3)

Date: 2026-10-19
Status: accepted, pending the owner's review of the look

## Decisions

- **Design tokens** live in `src/DragConvert.App/Theme/Tokens.xaml`: overlay geometry, motion timings, fonts, and
  the light and dark palettes. `OverlayTheme` resolves them for the current Windows theme; high contrast switches to
  opaque system colours.
- **Accent colour** comes from `UISettings` the way Windows 11 uses it: `AccentLight2` for filled controls on dark,
  `AccentDark1` on light. Text on the accent is black or white, whichever has the higher WCAG contrast ratio.
- **Icons** are glyphs from the system font Segoe Fluent Icons (Segoe MDL2 Assets on Windows 10), used at runtime and
  never bundled: one per category (image, audio, video, document).
- **Overlay layout**: a round plate, square tiles with icon, label and an optional "3 of 5" line, a centre badge
  for what is being dragged, and a caption pill. `OverlayModel` now computes the smallest ring radius at which square
  tiles keep their gap, so eight tiles never overlap; the plate is sized to the current tile count.
- **Motion** per the design: plate fades and scales in (140 ms), tiles stagger by 15 ms, hover grows a tile to
  1.06 (100 ms), hide fades (100 ms). Everything is skipped when Windows animations are off.
- **After a drop** the overlay becomes a status bubble at the dropped tile (click-through): spinner, then a check mark
  or a cross. Batches that succeed within 1.5 s skip the notification (the check mark confirms them); slower ones fade
  the bubble and hand over to the progress notification; failures always notify.
- **App mark**: original vector artwork (a file, a curved arrow, the converted file) in `Theme/AppIcon.cs`. Coloured
  tile for the .ico and window icons; a monochrome version in the tray that follows the taskbar's light/dark theme.
  The tray library only takes `System.Drawing.Icon` reliably, so the tray icon is built as an in-memory .ico.
- **UI gallery and snapshots**: `DragConvert.exe --gallery` shows every overlay state live; `--snapshot[=dir]`
  renders them (light, dark, high contrast when on) plus the icons to PNG in `artifacts/ui/` and regenerates
  `DragConvert.ico`. DWM backdrop effects only appear on a real desktop.
- **Renderer**: both stay available (Settings, "Overlay style"). The ring (layered) is the default; the acrylic
  panel draws the same tiles without the plate.

## Found by reviewing the snapshots

- Eight tiles overlapped (ring radius ignored tile count) → fixed in `OverlayModel`, with a unit test per tile count.
- White text on a lightened accent had poor contrast in dark mode → `UISettings` accent shades and a contrast check.
- The status bubble lost its background circle → fixed.

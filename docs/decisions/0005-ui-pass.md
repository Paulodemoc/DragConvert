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

## Addendum: glass style, acrylic fix, owner's icons (same day)

- **Acrylic panel showed flat grey**: DWM draws system backdrops only for *active* windows, and the overlay is
  never activated. Fix: send `WM_NCACTIVATE(TRUE)` after showing and swallow `WM_NCACTIVATE(FALSE)`; the frame looks
  active, focus never moves. Verified with a desktop screenshot (`--preview-overlay=dwm`).
- **New "Glass" style (now the default)**, modelled on the owner's reference: a translucent blue disc with a rim
  highlight, a darker inner disc with a large icon and count badge, round glass bubbles with labels, and a bright
  glowing bubble on hover.
- **Round blur**: every Windows blur API blurs the whole window *rectangle*. Tried and rejected, each verified with
  screenshots: DWM system backdrop + `SetWindowRgn` (region applied, backdrop ignores it), accent acrylic
  (`SetWindowCompositionAttribute`) on a frame-extended window and on a layered window (both rectangular).
  Adopted instead: capture the screen area behind the overlay just before it appears (GDI `BitBlt`, in memory only),
  blur it on the CPU (3x downscale + three box-blur passes), and draw it inside the disc. The overlay doesn't move
  while open, so a frozen backdrop is indistinguishable from live blur except over moving content (video). Warm
  cost: about 24 ms capture + blur, 40 ms to show; the warm-up at startup runs one capture to pay JIT costs early.
  When the capture fails, the disc uses its stronger no-blur tint.
- **Icons are the owner's artwork**: `assets/icon-source/app.png` (neon, opaque black surround removed with a
  rounded-rectangle mask) and `assets/icon-source/tray.png` (glossy, already transparent), turned into multi-size
  .ico files with Magick.NET (Lanczos, light sharpening at 32 px and below). The tray uses the full-colour icon at
  Windows' small-icon size; it no longer follows the taskbar theme.
- **`--preview-overlay[=glass|layered|dwm]`** shows the real overlay window for 8 s, so DWM effects and the captured
  backdrop can be screenshotted and reviewed.

## Addendum 2: neutral glass, acrylic panel removed, live style switching

- **Glass is now neutral** (the owner wanted transparency without the blue): the blurred backdrop, then a black
  layer whose opacity follows the backdrop's measured brightness (0.06 over black up to about 0.5 over white, so white
  text stays readable), then a faint white frost and white rims. The hovered bubble and the count badge are white with
  dark content. All values are `Glass.*` tokens.
- **Acrylic panel style removed.** Even when the WM_NCACTIVATE fix works, DWM acrylic is a tinted rectangle that reads
  as flat grey over dark windows, which isn't what the owner wants. Settings holding the old "dwm" value fall back to
  Glass. Removed with it: WindowChrome, the system backdrop call and the fake-active frame.
- **Style changes apply immediately**: the controller closes the overlay window and builds and warms up a new one.
- The owner's report of "glass still shows a rectangle" came from an outdated Debug build (only Release had been
  rebuilt). Both configurations are now rebuilt after changes.

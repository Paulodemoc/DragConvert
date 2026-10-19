# 0002. Conversion engine and first providers (M2)

Date: 2026-10-19
Status: accepted

## Decisions

- **Engine shape.** `IConversionProvider` declares capabilities as (input format, target format) pairs over a fixed
  `MediaFormats` list. `FormatCatalog` merges them (first registered provider wins a route) and answers "which
  targets for these files", including the intersection for multi-file drags. Providers whose engine is missing
  (`IsAvailable == false`) contribute nothing, so the overlay never offers a conversion that can't run.
- **`JobQueue` owns file safety, not providers.** It plans `<name>.<ext>` beside the source (`<name> (1).<ext>` on
  collision), hands the provider a temp path in the destination folder (`.<name>.<random>.dragconvert-tmp.<ext>`),
  renames on success without overwriting, and deletes the temp file on failure or cancel. Providers only write to the
  path they're given. Leftover temp files carry the marker for startup cleanup (M5).
- **Bounded parallelism:** default CPU count / 2, at least 1.
- **Errors are one line.** Providers throw `ConversionException` with a message fit for a toast; the queue turns it,
  and I/O errors, into a `Failed` result.
- **Images: Magick.NET (Q8, AnyCPU), in process.** Applies EXIF rotation to the pixels, flattens transparency onto
  white for JPEG/BMP/PDF, keeps animation for GIF/WebP and pages for TIFF/PDF.
- **HEIC is input only.** Magick.NET can't write HEIC anyway (no encoder delegate), and HEIC output would raise the
  HEVC patent question. The HEIC test fixture (64x48) was made once with Windows' HEIF encoder and is committed.
- **Audio/video: LGPL FFmpeg as a child process.** No GPL components, so MP4 uses `h264_mf` (the H.264 encoder that
  ships with Windows) instead of x264. `h264_mf` rejects frames under 64 px, so tiny inputs are upscaled with aspect
  kept. WebM is VP9 + Opus, GIF uses a two-pass palette at up to 480 px wide and 12 fps.
- **Progress** comes from `ffmpeg -progress pipe:1` (`out_time_us`) against the duration ffmpeg prints on stderr.
- **Cancellation** kills the ffmpeg process tree (`Process.Kill(entireProcessTree: true)`). A Win32 job object, as the
  design suggests, would also cover our own crashes; it needs `Native`, so it's deferred to M5 hardening.
- **Tests:** xUnit v3, so FFmpeg tests are skipped (not failed) when ffmpeg.exe is missing. Image, audio and video
  inputs are generated at test time; CI fetches FFmpeg with `tools/fetch-ffmpeg.ps1` (hash-checked).
- **CLI harness** `dragconvert formats|convert` for manual iteration; throwaway once the tray app covers it.

## Licensing questions found

1. **Magick.Native statically links LGPL libraries** (libheif, libde265 for HEIC decoding, among others). LGPL lets
   users replace the library, which is awkward when it is linked into one native DLL. Options: accept it with notices
   and a source offer (common practice for Magick.NET apps), use a Magick.NET build without HEIC, or decode HEIC
   through another route. Needs a decision before the first public release (M6).
2. **HEVC patents for HEIC decoding** via libde265. Decoding-only exposure is lower than encoding, but it isn't zero.
   Alternative: decode HEIC through Windows (WIC) when the user has the HEIF/HEVC extensions, falling back to
   Magick.NET.
3. **FFmpeg distribution**: bundle the LGPL build with license text and source offer, or download on first use (hash
   verified). Still open (design doc, Open questions).
4. **`h264_mf` quality and availability** depend on the Windows Media Foundation encoder (hardware or software). It's
   present on standard Windows 10/11 but missing on "N" editions without the Media Feature Pack; the MP4 tile should
   be hidden there (M5).

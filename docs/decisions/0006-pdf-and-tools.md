# 0006. PDF and tools (M4)

Date: 2026-10-19
Status: accepted

## Decisions

- **Tools are separate from conversions.** `IFileTool` (Core) covers actions that aren't format changes: one run per
  file, or one run combining all matching files (merge). Outputs are a file ("report (compressed).pdf") or a folder
  ("report pages"). `ToolRunner` gives them the same safety as conversions: temp file or folder beside the first
  input, renamed into place, never overwriting; any unexpected exception becomes a normal failure result.
  `ToolCatalog` lists the tools that apply to a drop, with counts; combining tools need at least two matching files.
- **Overlay**: holding Alt with the trigger swaps the ring from formats to tools (and back on release, while the
  ring stays up). The caption hints "Alt for tools" when tools exist. The tray window lists tools under formats.
- **Tools in M4** (tile order): Merge PDFs, Split pages, Pages to images, Compress, Combine to PDF (images), Remove
  metadata.
- **PDFsharp 6.2.4 (MIT)** for merge, split, compress and PDF metadata. Its dependency
  System.Security.Cryptography.Pkcs 8.0.1 has known vulnerabilities, so it's pinned to 10.0.13 through central
  package management (`CentralPackageTransitivePinningEnabled`).
- **Pages to images uses Windows' own PDF renderer** (`Windows.Data.Pdf`), in a new `DragConvert.Providers.Windows`
  project (Windows 10 2004+ target). No PDF engine to bundle or license. Ghostscript is ruled out (AGPL).
- **Compress** recompresses embedded JPEG photos (quality 70, at most 2000 px on the long side) for RGB, grey and
  ICC-based RGB/grey images, keeping the channel count; skips images with masks, decode arrays or other colour
  spaces; drops page thumbnails; stores content with maximum Flate compression. If the result isn't smaller, it
  fails with "already as small as this tool can make it" instead of writing a useless copy.
- **Remove metadata** is lossless where it matters: JPEG (drops APP1/APP13/COM and other APPn segments, keeps JFIF,
  ICC and Adobe segments, re-inserts a minimal EXIF holding only the orientation), PNG (drops text, eXIf and time
  chunks) and WebP (drops EXIF/XMP chunks, clears their VP8X flags). TIFF and GIF go through Magick.NET; PDFs lose
  their info dictionary and XMP stream; audio and video are stream-copied by FFmpeg without metadata or chapters.
  HEIC isn't offered (it can't be written back). PDFsharp writes its own Producer field on save.
- **Reorder pages is deferred**: it needs a page-picking UI, which doesn't fit a drag gesture.

## Found by end-to-end runs (CLI on real files)

- Compress crashed on PDFs whose image colour space is an array or an indirect name (as Magick.NET writes them):
  names are now read defensively, and the runners turn unexpected exceptions into failures.
- Merge accepted a single file from the CLI; the runner now requires two.

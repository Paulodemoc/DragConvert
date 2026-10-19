# 0009. Format breadth (M4b)

Date: 2026-10-19
Status: accepted

## Decisions

- **Scope is roadmap tiers A and B** (docs/roadmap.md). Tier C (Office documents through installed Word, Excel,
  PowerPoint or LibreOffice) and tier D (tools that need a window) stay on the roadmap.
- **New formats.** Images: AVIF in and out, SVG in (rasterised at 192 DPI, so twice its nominal size). Audio: AIFF
  (PCM 16-bit big-endian) and WMA (wmav2) out. Video: MOV and MKV (h264_mf + AAC), AVI (MPEG-4 Part 2 + MP3) and WMV
  (wmv2 + wmav2) out; all stay inside the LGPL FFmpeg build. Documents: PDF → TXT/Markdown, DOCX → TXT/Markdown/HTML,
  Markdown → HTML/TXT. Subtitles: SRT ↔ VTT, both → TXT. Spreadsheets: CSV ↔ TSV ↔ XLSX.
- **New categories** Text, Subtitle, Spreadsheet and Archive, each with its own Segoe Fluent glyph.
- **New tools** (tile order after the M4 ones): Compress for images, audio and video; Pages to JPG beside Pages to
  PNG; Normalize loudness (EBU R128 `loudnorm`), Make mono, Waveform image; Mute and Snapshot for video; Extract
  (ZIP, TAR, GZIP, TGZ, RAR, 7z) and Zip (any files).
- **`IFileTool` gained `MinimumInputs` and `AcceptsAnyFile`** (default members), so Zip can combine a single file of
  any type, and `OutputExtension` takes the input format (Compress keeps the input's extension).
- **Libraries**, all permissive: PdfPig 0.1.16 (Apache-2.0) for PDF text, DocumentFormat.OpenXml 3.4.1 (MIT) for
  DOCX and XLSX, Markdig 1.3.2 (BSD-2-Clause) for Markdown, SharpCompress 0.50.4 (MIT) for RAR and 7z. ZIP, TAR and
  GZIP use the .NET libraries.
- **Text extraction is plain**: DOCX keeps headings, bold/italic, links, lists and tables; PDF text comes out page by
  page in reading order with no layout. Neither is a faithful document conversion, and the tile names say so
  (TXT, Markdown, HTML).
- **XLSX reads only the first sheet**, values only (formulas as their cached results, dates as serial numbers).
  CSV is written UTF-8 with a BOM so Excel opens non-ASCII text correctly; a first line with more semicolons than
  commas is read as semicolon-separated (European Excel exports).
- **Extract checks every entry's path stays inside the output folder** ("zip slip"): the .NET extractors do this
  themselves, and SharpCompress entries are checked before writing. Password-protected archives fail with a clear
  message; RAR is extract-only (creating RAR needs the proprietary rarlab tools).

# Roadmap

Formats and tools beyond the MVP, sorted by what they cost. Tiers A and B are milestone **M4b (format breadth)**,
done in code (ADR 0009). Tiers C and D are recorded here for later planning.

## Tier A: no new libraries (M4b)

| Area | Formats / tools |
| --- | --- |
| Images | AVIF in and out; SVG input |
| Image tools | Compress (re-encode and downsize) |
| Audio | AIFF, WMA output |
| Audio tools | Compress (lower bitrate), Normalize (loudness), Channels (stereo/mono) |
| Video | MOV, MKV, AVI, WMV output |
| Video tools | Compress, Mute (drop audio), Snapshot (one frame as an image) |
| PDF | Pages to JPG (alongside PNG) |
| Subtitles | SRT ↔ VTT, both to TXT |
| Spreadsheets | CSV ↔ TSV |
| Archives | ZIP / TAR / GZIP extract; create a ZIP from the dragged files |

## Tier B: one small, permissively licensed library each (M4b)

| Feature | Library (license) |
| --- | --- |
| PDF → TXT / Markdown (text extraction) | PdfPig (Apache-2.0) |
| DOCX → TXT / Markdown / HTML | DocumentFormat.OpenXml (MIT) |
| Markdown → HTML / TXT | Markdig (BSD-2-Clause) |
| XLSX ↔ CSV / TSV | DocumentFormat.OpenXml (MIT); ClosedXML avoided because its SixLabors.Fonts dependency uses a split license |
| RAR extract | SharpCompress (MIT) |
| Audio waveform image ("Visualize") | FFmpeg (already bundled) |

## Tier C: Office formats through an installed engine (later)

Faithful layout for Office documents needs a real office engine, which is why the design doc keeps them out of the
MVP. The practical route: detect an installed engine and drive it in the background; offer these only when one is
present.

| Engine | Enables |
| --- | --- |
| Microsoft Word (COM automation) | DOCX / DOC / RTF → PDF; DOCX ↔ RTF |
| Microsoft PowerPoint (COM) | PPT / PPTX → PDF, JPG, PNG (one image per slide) |
| Microsoft Excel (COM) | XLSX / XLS → PDF; XLSX ↔ CSV with full fidelity |
| LibreOffice (`soffice --headless --convert-to`) | All of the above plus ODT / ODS / ODP, EPUB, DOCX ↔ ODT, XLSX ↔ ODS |

Open questions: COM automation needs Office's licensing terms checked for unattended use; LibreOffice adds about 1 s
of start-up per conversion. PDF → DOCX / PPTX / XLSX is skipped: without commercial libraries the results are poor.
Apple Pages isn't available on Windows.

## Tier D: tools that need an editing window (later, design decision first)

The design doc lists "a full editor UI" as a non-goal; each of these needs the user to choose something, so it would
need a small purpose-built window opened from the drop.

| Tool | Needs |
| --- | --- |
| Crop (image, video) | A region picker |
| Trim (audio, video) | A start/end picker with preview |
| Split by time (video) | Cut points |
| Speed (video) | A speed choice (fixed presets could work without a window) |
| Redact (image, video) | Regions to blur or black out |
| Annotate / Edit (image) | Drawing and adjustment tools |
| Organize / reorder pages (PDF) | Page thumbnails to drag around |
| Add background (image) | Background removal needs an ML model (size, licensing) |
| Bleep (audio) | Speech recognition to find words |

## Not planned

- HEIC output (HEVC patents; ADR 0002). AVIF is the alternative.
- Cloud features of any kind (design non-goal).

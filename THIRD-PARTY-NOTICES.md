# Third-party notices

DragConvert uses the following third-party components. Each is listed with its license. Full license texts will be
bundled with release builds.

## Shipped with the app

| Component | Used by | License | Notes |
| --- | --- | --- | --- |
| [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon) | App (tray icon) | MIT | |
| [Serilog](https://github.com/serilog/serilog), Serilog.Sinks.File, Serilog.Sinks.Console | App (logging) | Apache-2.0 | |
| [Magick.NET](https://github.com/dlemstra/Magick.NET) | Providers (images) | Apache-2.0 | Bundles the native Magick.Native library: [ImageMagick](https://imagemagick.org/script/license.php) (ImageMagick License) and its delegates, including libheif and libde265 (LGPL-3.0) for HEIC decoding. See open question in ADR 0002. |
| [FFmpeg](https://ffmpeg.org/legal.html) | Providers (audio and video), run as a separate ffmpeg.exe process | LGPL-2.1-or-later | LGPL build from [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds) (no GPL components, no x264/x265). Includes libmp3lame (LGPL), libvorbis, libopus and libvpx (BSD-style). Releases must ship FFmpeg's license and a source offer. Not yet bundled; fetched by `tools/fetch-ffmpeg.ps1` for development. |

## Artwork

The app and tray icons (`assets/icon-source/`) were provided by the project owner for DragConvert. Format and tool
glyphs come from the Segoe Fluent Icons / Segoe MDL2 Assets system fonts at runtime and are not redistributed.

## Build and test only (not shipped)

| Component | License |
| --- | --- |
| [CsWin32](https://github.com/microsoft/CsWin32) (source generator) | MIT |
| [xUnit.net](https://github.com/xunit/xunit) v3 | Apache-2.0 |
| Microsoft.NET.Test.Sdk, coverlet.collector | MIT |

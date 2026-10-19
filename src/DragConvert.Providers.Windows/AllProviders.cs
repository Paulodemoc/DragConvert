using DragConvert.Core.Conversion;
using DragConvert.Core.Tools;
using DragConvert.Providers.Ffmpeg;
using DragConvert.Providers.Tools;

namespace DragConvert.Providers.Windows;

/// <summary>Everything the app offers: conversion providers and tools, in the order they're listed.</summary>
public static class AllProviders
{
    public static IReadOnlyList<IConversionProvider> Converters(string? ffmpegPath) => DefaultProviders.Create(ffmpegPath);

    /// <summary>
    /// Tools in tile order. Only the ones that apply to a drop are shown, so each file type sees a short list:
    /// compress first, then type-specific tools, then metadata, then archives.
    /// </summary>
    public static IReadOnlyList<IFileTool> Tools(string? ffmpegPath) =>
    [
        new CompressPdfTool(),
        new ImageCompressTool(),
        new CompressAudioTool(ffmpegPath),
        new CompressVideoTool(ffmpegPath),
        new MergePdfTool(),
        new SplitPdfTool(),
        new PdfToImagesTool(PdfToImagesTool.ImageKind.Png),
        new PdfToImagesTool(PdfToImagesTool.ImageKind.Jpeg),
        new ImagesToPdfTool(),
        new NormalizeAudioTool(ffmpegPath),
        new MonoAudioTool(ffmpegPath),
        new WaveformTool(ffmpegPath),
        new MuteVideoTool(ffmpegPath),
        new SnapshotTool(ffmpegPath),
        new RemoveMetadataTool(ffmpegPath),
        new ExtractArchiveTool(),
        new CreateZipTool(),
    ];

    /// <summary>The ffmpeg.exe the app would use (see <see cref="FfmpegLocator"/>).</summary>
    public static string? FindFfmpeg() => FfmpegLocator.Find();
}

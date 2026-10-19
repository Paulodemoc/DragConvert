using DragConvert.Core.Conversion;
using DragConvert.Core.Tools;
using DragConvert.Providers.Ffmpeg;
using DragConvert.Providers.Tools;

namespace DragConvert.Providers.Windows;

/// <summary>Everything the app offers: conversion providers and tools, in the order they're listed.</summary>
public static class AllProviders
{
    public static IReadOnlyList<IConversionProvider> Converters(string? ffmpegPath) => DefaultProviders.Create(ffmpegPath);

    /// <summary>Tools in tile order: PDF tools, then image combining, then metadata removal.</summary>
    public static IReadOnlyList<IFileTool> Tools(string? ffmpegPath) =>
    [
        new MergePdfTool(),
        new SplitPdfTool(),
        new PdfToImagesTool(),
        new CompressPdfTool(),
        new ImagesToPdfTool(),
        new RemoveMetadataTool(ffmpegPath),
    ];

    /// <summary>The ffmpeg.exe the app would use (see <see cref="FfmpegLocator"/>).</summary>
    public static string? FindFfmpeg() => FfmpegLocator.Find();
}

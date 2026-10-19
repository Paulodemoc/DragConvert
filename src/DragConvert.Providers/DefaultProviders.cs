using DragConvert.Core.Conversion;
using DragConvert.Providers.Ffmpeg;
using DragConvert.Providers.Image;

namespace DragConvert.Providers;

public static class DefaultProviders
{
    /// <summary>All built-in providers, in priority order (the first provider for a route wins).</summary>
    public static IReadOnlyList<IConversionProvider> Create(string? ffmpegPath = null) =>
    [
        new ImageProvider(),
        new FfmpegProvider(ffmpegPath ?? FfmpegLocator.Find()),
    ];
}

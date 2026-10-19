namespace DragConvert.Core.Conversion;

/// <summary>
/// Merges provider capabilities and answers "which targets are valid for these files" and "who converts this".
/// The overlay, tray picker and CLI all read from it.
/// </summary>
public sealed class FormatCatalog
{
    private readonly Dictionary<(MediaFormat Input, MediaFormat Target), IConversionProvider> _routes = [];

    public FormatCatalog(IEnumerable<IConversionProvider> providers)
    {
        foreach (IConversionProvider provider in providers.Where(p => p.IsAvailable))
        {
            foreach (ConversionCapability capability in provider.Capabilities)
            {
                // The first provider registered for a route wins, so registration order sets priority.
                _routes.TryAdd((capability.Input, capability.Target), provider);
            }
        }
    }

    /// <summary>Targets for one file, in <see cref="MediaFormats.All"/> order.</summary>
    public IReadOnlyList<MediaFormat> TargetsFor(string inputPath)
    {
        MediaFormat? input = MediaFormats.FromPath(inputPath);
        return input is null ? [] : MediaFormats.All.Where(t => _routes.ContainsKey((input, t))).ToList();
    }

    /// <summary>Targets valid for every file in a multi-file drag; empty if any file is unknown.</summary>
    public IReadOnlyList<MediaFormat> CommonTargetsFor(IEnumerable<string> inputPaths)
    {
        IEnumerable<MediaFormat>? common = null;
        foreach (string path in inputPaths)
        {
            IReadOnlyList<MediaFormat> targets = TargetsFor(path);
            common = common is null ? targets : common.Intersect(targets);
        }

        return common?.ToList() ?? [];
    }

    public IConversionProvider? FindProvider(string inputPath, MediaFormat target)
    {
        MediaFormat? input = MediaFormats.FromPath(inputPath);
        return input is not null && _routes.TryGetValue((input, target), out IConversionProvider? provider) ? provider : null;
    }
}

namespace DragConvert.Core.Conversion;

/// <summary>
/// Merges provider capabilities and answers "which targets are valid for these files" and "who converts this".
/// The overlay, tray picker and CLI all read from it.
/// </summary>
public sealed class FormatCatalog
{
    private readonly IReadOnlyList<IConversionProvider> _providers;
    private Dictionary<(MediaFormat Input, MediaFormat Target), IConversionProvider> _routes = [];

    public FormatCatalog(IEnumerable<IConversionProvider> providers)
    {
        _providers = providers.ToList();
        Refresh();
    }

    /// <summary>Rebuilds the routes, e.g. after a provider found out at runtime that it can't produce a format.</summary>
    public void Refresh()
    {
        var routes = new Dictionary<(MediaFormat Input, MediaFormat Target), IConversionProvider>();
        foreach (IConversionProvider provider in _providers.Where(p => p.IsAvailable))
        {
            foreach (ConversionCapability capability in provider.Capabilities)
            {
                // The first provider registered for a route wins, so registration order sets priority.
                routes.TryAdd((capability.Input, capability.Target), provider);
            }
        }

        _routes = routes;
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

    /// <summary>
    /// Every target that applies to at least one file, with how many files it applies to (the overlay shows
    /// "JPEG · 3 of 5"). Targets valid for all files come first, then by count, then in <see cref="MediaFormats.All"/> order.
    /// Dropping on a target converts the files it applies to and skips the rest.
    /// </summary>
    public IReadOnlyList<TargetOption> TargetOptionsFor(IReadOnlyCollection<string> inputPaths)
    {
        var counts = new Dictionary<MediaFormat, int>();
        foreach (string path in inputPaths)
        {
            foreach (MediaFormat target in TargetsFor(path))
            {
                counts[target] = counts.GetValueOrDefault(target) + 1;
            }
        }

        return MediaFormats.All
            .Where(counts.ContainsKey)
            .Select(t => new TargetOption(t, counts[t], inputPaths.Count))
            .OrderByDescending(o => o.AppliesToAll)
            .ThenByDescending(o => o.FileCount)
            .ToList();
    }

    /// <summary>Splits files into those that can become <paramref name="target"/> and those to skip.</summary>
    public DropPlan Plan(IEnumerable<string> files, MediaFormat target)
    {
        var convert = new List<string>();
        var skipped = new List<string>();
        foreach (string file in files)
        {
            (FindProvider(file, target) is null ? skipped : convert).Add(file);
        }

        return new DropPlan(target, convert, skipped);
    }

    public IConversionProvider? FindProvider(string inputPath, MediaFormat target)
    {
        MediaFormat? input = MediaFormats.FromPath(inputPath);
        return input is not null && _routes.TryGetValue((input, target), out IConversionProvider? provider) ? provider : null;
    }
}

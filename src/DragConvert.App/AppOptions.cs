using DragConvert.App.Overlay;

namespace DragConvert.App;

/// <summary>
/// Command line: file or folder paths (from Send To or a shortcut) plus flags: --smoke (start, then exit; for CI),
/// --console (live log window), --renderer=layered|dwm (overrides the setting), --send-to (marks Send To launches).
/// </summary>
internal sealed record AppOptions(bool Smoke, bool Console, OverlayRenderer? Renderer, IReadOnlyList<string> Paths)
{
    public static AppOptions Parse(IReadOnlyList<string> args)
    {
        bool smoke = false;
        bool console = false;
        OverlayRenderer? renderer = null;
        var paths = new List<string>();

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            if (arg.Equals("--smoke", StringComparison.OrdinalIgnoreCase))
            {
                smoke = true;
            }
            else if (arg.Equals("--console", StringComparison.OrdinalIgnoreCase))
            {
                console = true;
            }
            else if (arg.StartsWith("--renderer", StringComparison.OrdinalIgnoreCase))
            {
                string? value = arg.Contains('=', StringComparison.Ordinal)
                    ? arg[(arg.IndexOf('=', StringComparison.Ordinal) + 1)..]
                    : (i + 1 < args.Count ? args[++i] : null);
                if (Enum.TryParse(value, ignoreCase: true, out OverlayRenderer parsed))
                {
                    renderer = parsed;
                }
            }
            else if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                paths.Add(arg);
            }
        }

        return new AppOptions(smoke, console, renderer, paths);
    }
}

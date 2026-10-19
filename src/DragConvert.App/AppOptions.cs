using DragConvert.App.Overlay;

namespace DragConvert.App;

/// <summary>
/// Command-line flags: --smoke (start, then exit; for CI), --console (live log window),
/// --renderer=layered|dwm (overlay renderer to compare in the M1 spike).
/// </summary>
internal sealed record AppOptions(bool Smoke, bool Console, OverlayRenderer Renderer)
{
    public static AppOptions Parse(IReadOnlyList<string> args)
    {
        bool smoke = false;
        bool console = false;
        var renderer = OverlayRenderer.Layered;

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
        }

        return new AppOptions(smoke, console, renderer);
    }
}

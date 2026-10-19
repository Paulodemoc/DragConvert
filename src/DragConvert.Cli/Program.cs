using System.Diagnostics;
using DragConvert.Core.Conversion;
using DragConvert.Providers;
using DragConvert.Providers.Ffmpeg;

const string Usage = """
    Usage:
      dragconvert formats <file>...                     List the formats each file can be converted to
      dragconvert convert <file>... <format> [--out <dir>]
                                                        Convert files; output goes beside each file (or into <dir>)
    Formats are ids, names or extensions: jpeg, PNG, webp, mp3, mp4, gif, pdf...
    """;

if (args.Length < 2)
{
    Console.WriteLine(Usage);
    return 2;
}

string? ffmpeg = FfmpegLocator.Find();
var catalog = new FormatCatalog(DefaultProviders.Create(ffmpeg));
if (ffmpeg is null)
{
    Console.Error.WriteLine("Note: ffmpeg.exe not found, so audio and video formats are unavailable (run tools/fetch-ffmpeg.ps1).");
}

switch (args[0].ToLowerInvariant())
{
    case "formats":
        foreach (string file in args.Skip(1))
        {
            IReadOnlyList<MediaFormat> targets = catalog.TargetsFor(file);
            Console.WriteLine($"{Path.GetFileName(file)}: {(targets.Count == 0 ? "(no conversions)" : string.Join(", ", targets.Select(t => t.DisplayName)))}");
        }

        if (args.Length > 2)
        {
            IReadOnlyList<MediaFormat> common = catalog.CommonTargetsFor(args.Skip(1));
            Console.WriteLine($"All together: {(common.Count == 0 ? "(none in common)" : string.Join(", ", common.Select(t => t.DisplayName)))}");
        }

        return 0;

    case "convert":
        return await ConvertAsync(args.Skip(1).ToList());

    default:
        Console.WriteLine(Usage);
        return 2;
}

async Task<int> ConvertAsync(List<string> rest)
{
    string? outputDirectory = null;
    int outIndex = rest.FindIndex(a => a is "--out" or "-o");
    if (outIndex >= 0)
    {
        if (outIndex + 1 >= rest.Count)
        {
            Console.Error.WriteLine("--out needs a folder.");
            return 2;
        }

        outputDirectory = Path.GetFullPath(rest[outIndex + 1]);
        Directory.CreateDirectory(outputDirectory);
        rest.RemoveRange(outIndex, 2);
    }

    if (rest.Count < 2)
    {
        Console.WriteLine(Usage);
        return 2;
    }

    MediaFormat? target = MediaFormats.FromName(rest[^1]);
    if (target is null)
    {
        Console.Error.WriteLine($"Unknown format '{rest[^1]}'.");
        return 2;
    }

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
        Console.Error.WriteLine("Cancelling...");
    };

    using var queue = new JobQueue(catalog);
    bool interactive = !Console.IsOutputRedirected;
    int failures = 0;
    foreach (string file in rest[..^1])
    {
        var stopwatch = Stopwatch.StartNew();
        int lastPercent = -1;
        var progress = new Progress<double>(p =>
        {
            int percent = (int)(p * 100);
            if (interactive && percent != lastPercent)
            {
                lastPercent = percent;
                Console.Write($"\r{Path.GetFileName(file)} -> {target.DisplayName}: {percent,3}%");
            }
        });

        ConversionResult result = await queue.ConvertAsync(file, target, progress: progress, outputDirectory: outputDirectory, cancellationToken: cts.Token);
        if (interactive)
        {
            Console.Write("\r" + new string(' ', 79) + "\r");
        }

        switch (result.Status)
        {
            case ConversionStatus.Succeeded:
                Console.WriteLine($"OK   {Path.GetFileName(file)} -> {result.OutputPath} ({stopwatch.Elapsed.TotalSeconds:F1} s)");
                break;
            case ConversionStatus.Cancelled:
                Console.WriteLine($"STOP {Path.GetFileName(file)}: cancelled");
                return 130;
            default:
                failures++;
                Console.WriteLine($"FAIL {Path.GetFileName(file)}: {result.Error}");
                break;
        }
    }

    return failures == 0 ? 0 : 1;
}

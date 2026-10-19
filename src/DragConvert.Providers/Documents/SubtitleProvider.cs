using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DragConvert.Core.Conversion;
using static DragConvert.Core.Conversion.MediaFormats;

namespace DragConvert.Providers.Documents;

/// <summary>One subtitle cue.</summary>
public sealed record SubtitleCue(TimeSpan Start, TimeSpan End, string Text);

/// <summary>SRT ↔ VTT, and either to plain text (the spoken lines without timings or styling tags).</summary>
public sealed partial class SubtitleProvider : IConversionProvider
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public string Id => "subtitles";

    public bool IsAvailable => true;

    public IReadOnlyCollection<ConversionCapability> Capabilities { get; } =
    [
        new(Srt, Vtt), new(Vtt, Srt), new(Srt, Txt), new(Vtt, Txt),
    ];

    public Task ConvertAsync(ConversionJob job, IProgress<double>? progress, CancellationToken cancellationToken) => Task.Run(
        () =>
        {
            IReadOnlyList<SubtitleCue> cues = Parse(File.ReadAllText(job.InputPath));
            if (cues.Count == 0)
            {
                throw new ConversionException($"No subtitles found in {Path.GetFileName(job.InputPath)}.");
            }

            string output = job.Target.Id switch
            {
                "vtt" => WriteVtt(cues),
                "srt" => WriteSrt(cues),
                _ => WriteText(cues),
            };
            File.WriteAllText(job.OutputPath, output, Utf8);
        },
        cancellationToken);

    /// <summary>Reads SRT or WebVTT: blocks separated by blank lines, each with a "start --> end" line.</summary>
    public static IReadOnlyList<SubtitleCue> Parse(string content)
    {
        var cues = new List<SubtitleCue>();
        string[] blocks = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        foreach (string block in blocks)
        {
            string[] lines = block.Split('\n', StringSplitOptions.TrimEntries);
            int timing = Array.FindIndex(lines, l => l.Contains("-->", StringComparison.Ordinal));
            if (timing < 0)
            {
                continue; // WEBVTT header, NOTE, STYLE, REGION blocks
            }

            Match match = TimingPattern().Match(lines[timing]);
            if (!match.Success)
            {
                continue;
            }

            string text = string.Join('\n', lines.Skip(timing + 1).Where(l => l.Length > 0));
            cues.Add(new SubtitleCue(ParseTime(match.Groups[1].Value), ParseTime(match.Groups[2].Value), text));
        }

        return cues;
    }

    public static string WriteSrt(IReadOnlyList<SubtitleCue> cues)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < cues.Count; i++)
        {
            sb.Append(i + 1).Append('\n')
              .Append(FormatTime(cues[i].Start, ',')).Append(" --> ").Append(FormatTime(cues[i].End, ',')).Append('\n')
              .Append(cues[i].Text).Append("\n\n");
        }

        return sb.ToString();
    }

    public static string WriteVtt(IReadOnlyList<SubtitleCue> cues)
    {
        var sb = new StringBuilder("WEBVTT\n\n");
        foreach (SubtitleCue cue in cues)
        {
            sb.Append(FormatTime(cue.Start, '.')).Append(" --> ").Append(FormatTime(cue.End, '.')).Append('\n')
              .Append(cue.Text).Append("\n\n");
        }

        return sb.ToString();
    }

    /// <summary>The spoken lines only: styling tags removed, one cue per line.</summary>
    public static string WriteText(IReadOnlyList<SubtitleCue> cues) =>
        string.Join('\n', cues.Select(c => TagPattern().Replace(c.Text, string.Empty).Replace('\n', ' ').Trim()).Where(t => t.Length > 0)) + "\n";

    private static TimeSpan ParseTime(string value)
    {
        // hh:mm:ss,mmm (SRT), hh:mm:ss.mmm or mm:ss.mmm (VTT)
        string[] parts = value.Replace(',', '.').Split(':');
        double seconds = double.Parse(parts[^1], CultureInfo.InvariantCulture);
        int minutes = int.Parse(parts[^2], CultureInfo.InvariantCulture);
        int hours = parts.Length > 2 ? int.Parse(parts[^3], CultureInfo.InvariantCulture) : 0;
        return new TimeSpan(0, hours, minutes, 0) + TimeSpan.FromSeconds(seconds);
    }

    private static string FormatTime(TimeSpan time, char separator) =>
        $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}{separator}{time.Milliseconds:000}";

    [GeneratedRegex(@"((?:\d+:)?\d{1,2}:\d{2}[,.]\d{1,3})\s*-->\s*((?:\d+:)?\d{1,2}:\d{2}[,.]\d{1,3})")]
    private static partial Regex TimingPattern();

    [GeneratedRegex(@"<[^>]+>|\{\\[^}]*\}")]
    private static partial Regex TagPattern();
}

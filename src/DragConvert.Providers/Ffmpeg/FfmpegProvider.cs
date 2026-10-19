using System.Globalization;
using System.Text.RegularExpressions;
using DragConvert.Core.Conversion;
using DragConvert.Providers.Processes;
using static DragConvert.Core.Conversion.MediaFormats;

namespace DragConvert.Providers.Ffmpeg;

/// <summary>
/// Audio and video conversions through a bundled LGPL ffmpeg.exe run as a child process.
/// MP4 uses h264_mf (the H.264 encoder built into Windows) because LGPL builds have no x264.
/// </summary>
public sealed partial class FfmpegProvider : IConversionProvider
{
    private static readonly MediaFormat[] AudioInputs = [Mp3, M4a, Wav, Flac, Ogg, Opus, Wma];
    private static readonly MediaFormat[] AudioTargets = [Mp3, M4a, Wav, Flac, Ogg, Opus];
    private static readonly MediaFormat[] VideoInputs = [Mp4, WebM, Mov, Mkv, Avi, Wmv];
    private static readonly MediaFormat[] VideoTargets = [Mp4, WebM, Gif, Mp3, M4a];

    private readonly string? _ffmpegPath;
    private readonly TimeSpan _timeout;

    public FfmpegProvider(string? ffmpegPath, TimeSpan? timeout = null)
    {
        _ffmpegPath = ffmpegPath;
        _timeout = timeout ?? TimeSpan.FromHours(2);
        Capabilities =
        [
            .. Routes(AudioInputs, AudioTargets),
            .. Routes(VideoInputs, VideoTargets),
            // Animated GIF to video.
            .. Routes([Gif], [Mp4, WebM]),
        ];
    }

    public string Id => "ffmpeg";

    public bool IsAvailable => _ffmpegPath is not null && File.Exists(_ffmpegPath);

    public IReadOnlyCollection<ConversionCapability> Capabilities { get; private set; }

    /// <summary>
    /// Checks that h264_mf can actually encode (Windows "N" editions lack Media Foundation). When it can't, MP4 is
    /// removed from the capabilities; call <see cref="FormatCatalog.Refresh"/> afterwards.
    /// </summary>
    public async Task<bool> ProbeH264Async(CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return false;
        }

        ProcessResult result = await ProcessRunner.RunAsync(
            _ffmpegPath!,
            ["-hide_banner", "-nostdin", "-v", "error", "-f", "lavfi", "-i", "testsrc=size=64x64:rate=1:duration=1", "-frames:v", "1", "-pix_fmt", "nv12", "-c:v", "h264_mf", "-f", "null", "-"],
            TimeSpan.FromSeconds(20),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            Capabilities = Capabilities.Where(c => c.Target != Mp4).ToArray();
            return false;
        }

        return true;
    }

    public async Task ConvertAsync(ConversionJob job, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            throw new ConversionException("FFmpeg is not installed.");
        }

        List<string> arguments = ["-hide_banner", "-nostdin", "-y", "-i", job.InputPath, .. EncoderArguments(job.Target), "-progress", "pipe:1", "-nostats", job.OutputPath];

        double durationSeconds = 0;
        progress?.Report(0);

        ProcessResult result = await ProcessRunner.RunAsync(
            _ffmpegPath!,
            arguments,
            _timeout,
            onStandardOutputLine: line =>
            {
                // -progress writes key=value lines; out_time_us is the position written so far.
                if (durationSeconds > 0 && line.StartsWith("out_time_us=", StringComparison.Ordinal)
                    && long.TryParse(line.AsSpan(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out long micros))
                {
                    progress?.Report(Math.Clamp(micros / 1_000_000.0 / durationSeconds, 0, 0.99));
                }
            },
            onStandardErrorLine: line =>
            {
                if (durationSeconds == 0 && DurationPattern().Match(line) is { Success: true } match)
                {
                    durationSeconds = TimeSpan.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture).TotalSeconds;
                }
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new ConversionException(Describe(result.StandardErrorTail));
        }
    }

    /// <summary>Codec settings per target. Exposed for tests and the CLI's dry-run output.</summary>
    public static IReadOnlyList<string> EncoderArguments(MediaFormat target) => target.Id switch
    {
        "mp3" => ["-vn", "-c:a", "libmp3lame", "-q:a", "2"],
        "m4a" => ["-vn", "-c:a", "aac", "-b:a", "192k"],
        "wav" => ["-vn", "-c:a", "pcm_s16le"],
        "flac" => ["-vn", "-c:a", "flac"],
        "ogg" => ["-vn", "-c:a", "libvorbis", "-q:a", "5"],
        "opus" => ["-vn", "-c:a", "libopus", "-b:a", "128k"],
        "mp4" =>
        [
            // h264_mf needs NV12, even dimensions and at least 64 px per side: upscale tiny inputs keeping aspect.
            "-vf", "scale='if(lt(iw,ih),max(64,iw),-2)':'if(lt(iw,ih),-2,max(64,ih))',scale=trunc(iw/2)*2:trunc(ih/2)*2",
            "-pix_fmt", "nv12",
            "-c:v", "h264_mf", "-rate_control", "quality", "-quality", "70",
            "-c:a", "aac", "-b:a", "160k", "-movflags", "+faststart",
        ],
        "webm" =>
        [
            "-c:v", "libvpx-vp9", "-crf", "33", "-b:v", "0", "-deadline", "good", "-cpu-used", "4", "-row-mt", "1",
            "-c:a", "libopus", "-b:a", "128k",
        ],
        "gif" =>
        [
            "-an", "-vf", "fps=12,scale='min(480,iw)':-2:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse", "-loop", "0",
        ],
        _ => throw new ConversionException($"FFmpeg can't produce {target.DisplayName}."),
    };

    private static IEnumerable<ConversionCapability> Routes(MediaFormat[] inputs, MediaFormat[] targets) =>
        from input in inputs
        from target in targets
        where input != target
        select new ConversionCapability(input, target);

    /// <summary>The most useful stderr line, shortened to one line for the toast.</summary>
    private static string Describe(IReadOnlyList<string> stderr)
    {
        string? line = stderr.LastOrDefault(l => l.Contains("Error", StringComparison.OrdinalIgnoreCase) || l.Contains("Invalid", StringComparison.OrdinalIgnoreCase))
            ?? stderr.LastOrDefault(l => !string.IsNullOrWhiteSpace(l));
        if (line is null)
        {
            return "FFmpeg failed without an error message.";
        }

        line = line.Trim();
        return line.Length > 200 ? line[..200] + "…" : line;
    }

    [GeneratedRegex(@"Duration:\s*(\d+:\d{2}:\d{2}(?:\.\d+)?)")]
    private static partial Regex DurationPattern();
}

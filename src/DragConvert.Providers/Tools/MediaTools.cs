using DragConvert.Core.Conversion;
using DragConvert.Core.Tools;
using DragConvert.Providers.Ffmpeg;
using DragConvert.Providers.Processes;
using static DragConvert.Core.Conversion.MediaFormats;

namespace DragConvert.Providers.Tools;

/// <summary>A per-file tool that runs FFmpeg once; subclasses supply what it accepts and the arguments.</summary>
public abstract class FfmpegTool(string? ffmpegPath) : IFileTool
{
    protected static readonly MediaFormat[] AudioFormats = [Mp3, M4a, Wav, Flac, Ogg, Opus, Aiff, Wma];
    protected static readonly MediaFormat[] VideoFormats = [Mp4, WebM, Mov, Mkv, Avi, Wmv];

    public abstract string Id { get; }

    public abstract string DisplayName { get; }

    public abstract string Glyph { get; }

    public bool IsAvailable => ffmpegPath is not null && File.Exists(ffmpegPath);

    public bool CombinesInputs => false;

    public ToolOutputKind OutputKind => ToolOutputKind.File;

    public abstract string NameSuffix { get; }

    /// <summary>When true, a result that isn't smaller than the input is discarded with a clear message.</summary>
    protected virtual bool MustShrink => false;

    public abstract bool Accepts(MediaFormat input);

    public virtual string OutputExtension(MediaFormat? input) => input?.Extension ?? string.Empty;

    public async Task RunAsync(ToolJob job, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        string input = job.Inputs[0];
        MediaFormat format = FromPath(input) ?? throw new ConversionException($"{DisplayName} can't open {Path.GetFileName(input)}.");
        List<string> arguments = ["-hide_banner", "-nostdin", "-y", "-v", "error", "-i", input, .. Arguments(format), job.OutputPath];
        ProcessResult result = await ProcessRunner.RunAsync(ffmpegPath!, arguments, TimeSpan.FromHours(2), cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            string reason = result.StandardErrorTail.LastOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "FFmpeg failed.";
            throw new ConversionException(reason.Length > 200 ? reason[..200] + "…" : reason);
        }

        if (MustShrink && File.Exists(job.OutputPath) && new FileInfo(job.OutputPath).Length >= new FileInfo(input).Length)
        {
            File.Delete(job.OutputPath);
            throw new ConversionException($"{Path.GetFileName(input)} is already as small as this tool can make it.");
        }
    }

    /// <summary>Encoder settings that keep the file's own format (for tools that re-encode in place).</summary>
    protected static IEnumerable<string> SameFormat(MediaFormat format) => FfmpegProvider.EncoderArguments(format);

    protected abstract IEnumerable<string> Arguments(MediaFormat format);
}

/// <summary>Smaller audio at a lower bitrate, same format (lossy formats only).</summary>
public sealed class CompressAudioTool(string? ffmpegPath) : FfmpegTool(ffmpegPath)
{
    public override string Id => "compress-audio";

    public override string DisplayName => "Compress";

    public override string Glyph => "";

    public override string NameSuffix => " (compressed)";

    protected override bool MustShrink => true;

    public override bool Accepts(MediaFormat input) => input == Mp3 || input == M4a || input == Ogg || input == Opus || input == Wma;

    protected override IEnumerable<string> Arguments(MediaFormat format) => format.Id switch
    {
        "mp3" => ["-vn", "-c:a", "libmp3lame", "-b:a", "96k"],
        "m4a" => ["-vn", "-c:a", "aac", "-b:a", "96k"],
        "ogg" => ["-vn", "-c:a", "libvorbis", "-q:a", "2"],
        "opus" => ["-vn", "-c:a", "libopus", "-b:a", "64k"],
        _ => ["-vn", "-c:a", "wmav2", "-b:a", "96k"],
    };
}

/// <summary>Evens out loudness to a standard level (EBU R128, -16 LUFS), same format.</summary>
public sealed class NormalizeAudioTool(string? ffmpegPath) : FfmpegTool(ffmpegPath)
{
    public override string Id => "normalize-audio";

    public override string DisplayName => "Normalize";

    public override string Glyph => ""; // Volume

    public override string NameSuffix => " (normalized)";

    public override bool Accepts(MediaFormat input) => AudioFormats.Contains(input);

    protected override IEnumerable<string> Arguments(MediaFormat format) => ["-af", "loudnorm=I=-16:TP=-1.5:LRA=11", .. SameFormat(format)];
}

/// <summary>Mixes stereo (or more) down to one channel, same format.</summary>
public sealed class MonoAudioTool(string? ffmpegPath) : FfmpegTool(ffmpegPath)
{
    public override string Id => "mono-audio";

    public override string DisplayName => "Make mono";

    public override string Glyph => ""; // Speakers

    public override string NameSuffix => " (mono)";

    public override bool Accepts(MediaFormat input) => AudioFormats.Contains(input);

    protected override IEnumerable<string> Arguments(MediaFormat format) => ["-ac", "1", .. SameFormat(format)];
}

/// <summary>A picture of the waveform (PNG), handy for previews and sharing.</summary>
public sealed class WaveformTool(string? ffmpegPath) : FfmpegTool(ffmpegPath)
{
    public override string Id => "waveform";

    public override string DisplayName => "Waveform";

    public override string Glyph => ""; // Diagnostic (a pulse line)

    public override string NameSuffix => " (waveform)";

    public override bool Accepts(MediaFormat input) => AudioFormats.Contains(input);

    public override string OutputExtension(MediaFormat? input) => ".png";

    protected override IEnumerable<string> Arguments(MediaFormat format) =>
        ["-filter_complex", "aformat=channel_layouts=mono,showwavespic=s=1600x400:colors=0x6A74D9", "-frames:v", "1"];
}

/// <summary>Smaller video: at most 1920 px on the long side, re-encoded at a lower quality, same container.</summary>
public sealed class CompressVideoTool(string? ffmpegPath) : FfmpegTool(ffmpegPath)
{
    private const string Downscale = "scale='if(gt(iw,ih),min(1920,iw),-2)':'if(gt(iw,ih),-2,min(1920,ih))',scale=trunc(iw/2)*2:trunc(ih/2)*2";

    public override string Id => "compress-video";

    public override string DisplayName => "Compress";

    public override string Glyph => "";

    public override string NameSuffix => " (compressed)";

    protected override bool MustShrink => true;

    public override bool Accepts(MediaFormat input) => input == Mp4 || input == Mov || input == Mkv || input == WebM;

    protected override IEnumerable<string> Arguments(MediaFormat format) => format == WebM
        ? ["-vf", Downscale, "-c:v", "libvpx-vp9", "-crf", "40", "-b:v", "0", "-deadline", "good", "-cpu-used", "4", "-row-mt", "1", "-c:a", "libopus", "-b:a", "96k"]
        : ["-vf", Downscale, "-pix_fmt", "nv12", "-c:v", "h264_mf", "-rate_control", "quality", "-quality", "45", "-c:a", "aac", "-b:a", "128k", .. (format == Mkv ? Array.Empty<string>() : ["-movflags", "+faststart"])];
}

/// <summary>Removes the audio track without re-encoding the video.</summary>
public sealed class MuteVideoTool(string? ffmpegPath) : FfmpegTool(ffmpegPath)
{
    public override string Id => "mute-video";

    public override string DisplayName => "Mute";

    public override string Glyph => ""; // Mute

    public override string NameSuffix => " (muted)";

    public override bool Accepts(MediaFormat input) => VideoFormats.Contains(input);

    protected override IEnumerable<string> Arguments(MediaFormat format) => ["-map", "0:v", "-c", "copy", "-an"];
}

/// <summary>Saves a representative frame from the start of the video as a JPEG.</summary>
public sealed class SnapshotTool(string? ffmpegPath) : FfmpegTool(ffmpegPath)
{
    public override string Id => "snapshot";

    public override string DisplayName => "Snapshot";

    public override string Glyph => ""; // Camera

    public override string NameSuffix => " (snapshot)";

    public override bool Accepts(MediaFormat input) => VideoFormats.Contains(input) || input == Gif;

    public override string OutputExtension(MediaFormat? input) => ".jpg";

    protected override IEnumerable<string> Arguments(MediaFormat format) => ["-vf", "thumbnail=90", "-frames:v", "1", "-q:v", "2"];
}

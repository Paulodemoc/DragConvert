using DragConvert.Providers.Ffmpeg;
using DragConvert.Providers.Processes;
using ImageMagick;
using ImageMagick.Drawing;

namespace DragConvert.Providers.Tests;

/// <summary>
/// Small input files for provider tests. Images are generated with Magick.NET, audio and video with ffmpeg's test
/// sources; only the HEIC sample is committed (Fixtures/sample.heic, 64x48) because Magick.NET can't write HEIC.
/// </summary>
public sealed class MediaFixtures : IAsyncLifetime
{
    public string Folder { get; } = Path.Combine(Path.GetTempPath(), "dragconvert-fixtures", Guid.NewGuid().ToString("N"));

    public string? Ffmpeg { get; } = FfmpegLocator.Find();

    public string? Ffprobe { get; } = FfmpegLocator.Find("ffprobe.exe");

    public string Heic { get; } = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.heic");

    public string TransparentPng => Path.Combine(Folder, "transparent.png");

    public string RotatedJpeg => Path.Combine(Folder, "rotated.jpg");

    public string AnimatedGif => Path.Combine(Folder, "animated.gif");

    public string Corrupt => Path.Combine(Folder, "corrupt.png");

    public string Wav => Path.Combine(Folder, "tone.wav");

    public string Mp4 => Path.Combine(Folder, "clip.mp4");

    public string LongMp4 => Path.Combine(Folder, "long.mp4");

    public string CorruptMp4 => Path.Combine(Folder, "corrupt.mp4");

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Folder);

        // 64x48, transparent background with an opaque red square: checks alpha handling.
        using (var png = new MagickImage(MagickColors.Transparent, 64, 48))
        {
            new Drawables().FillColor(MagickColors.Red).Rectangle(16, 12, 48, 36).Draw(png);
            await png.WriteAsync(TransparentPng, MagickFormat.Png);
        }

        // Stored 64x48 with EXIF orientation "rotate 90°": displays as 48x64.
        using (var jpeg = new MagickImage(MagickColors.Blue, 64, 48))
        {
            // ImageMagick writes the Orientation property over the profile's tag, so set both.
            jpeg.Orientation = OrientationType.RightTop;
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, (ushort)6); // RightTop: rotate 90° clockwise to display
            jpeg.SetProfile(exif);
            await jpeg.WriteAsync(RotatedJpeg, MagickFormat.Jpeg);
        }

        using (var gif = new MagickImageCollection())
        {
            foreach (MagickColor color in new[] { MagickColors.Red, MagickColors.Green, MagickColors.Blue })
            {
                var frame = new MagickImage(color, 32, 32) { AnimationDelay = 20 };
                gif.Add(frame);
            }

            await gif.WriteAsync(AnimatedGif, MagickFormat.Gif);
        }

        await File.WriteAllTextAsync(Corrupt, "this is not an image");
        await File.WriteAllTextAsync(CorruptMp4, "this is not a video");

        if (Ffmpeg is not null)
        {
            await GenerateAsync(["-f", "lavfi", "-i", "sine=frequency=440:duration=1", "-c:a", "pcm_s16le", Wav]);
            await GenerateAsync(
            [
                "-f", "lavfi", "-i", "testsrc=size=160x120:rate=15:duration=1",
                "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
                "-pix_fmt", "nv12", "-c:v", "h264_mf", "-c:a", "aac", "-shortest", Mp4,
            ]);
            await GenerateAsync(
            [
                "-f", "lavfi", "-i", "testsrc=size=640x480:rate=30:duration=30",
                "-pix_fmt", "nv12", "-c:v", "h264_mf", LongMp4,
            ]);
        }
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
        }

        return ValueTask.CompletedTask;
    }

    public string OutputPath(string name) => Path.Combine(Folder, $"{Guid.NewGuid():N}-{name}");

    /// <summary>Codec names of every stream in a media file, via ffprobe.</summary>
    public async Task<IReadOnlyList<string>> CodecsAsync(string path)
    {
        var codecs = new List<string>();
        ProcessResult result = await ProcessRunner.RunAsync(
            Ffprobe!,
            ["-v", "error", "-show_entries", "stream=codec_name", "-of", "csv=p=0", path],
            TimeSpan.FromMinutes(1),
            onStandardOutputLine: line =>
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    lock (codecs)
                    {
                        codecs.Add(line.Trim().TrimEnd(','));
                    }
                }
            });
        Assert.Equal(0, result.ExitCode);
        return codecs;
    }

    private async Task GenerateAsync(string[] arguments)
    {
        ProcessResult result = await ProcessRunner.RunAsync(Ffmpeg!, ["-hide_banner", "-nostdin", "-y", "-v", "error", .. arguments], TimeSpan.FromMinutes(2));
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException("Fixture generation failed: " + string.Join(Environment.NewLine, result.StandardErrorTail));
        }
    }
}

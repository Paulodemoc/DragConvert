using System.Diagnostics;
using DragConvert.Core.Conversion;
using DragConvert.Providers.Ffmpeg;

namespace DragConvert.Providers.Tests;

/// <summary>Skipped when ffmpeg.exe isn't available; run tools/fetch-ffmpeg.ps1 to enable them.</summary>
public sealed class FfmpegProviderTests(MediaFixtures fixtures) : IClassFixture<MediaFixtures>
{
    private static readonly IReadOnlyDictionary<string, string> NoOptions = new Dictionary<string, string>();

    private FfmpegProvider Provider()
    {
        Assert.SkipWhen(fixtures.Ffmpeg is null || fixtures.Ffprobe is null, "ffmpeg/ffprobe not found; run tools/fetch-ffmpeg.ps1");
        return new FfmpegProvider(fixtures.Ffmpeg);
    }

    private async Task<string> ConvertAsync(string input, MediaFormat target, IProgress<double>? progress = null)
    {
        string output = fixtures.OutputPath("out" + target.Extension);
        await Provider().ConvertAsync(new ConversionJob(input, output, target, NoOptions), progress, TestContext.Current.CancellationToken);
        Assert.True(File.Exists(output), $"No output written for {target}");
        return output;
    }

    [Theory]
    [InlineData("mp3", "mp3")]
    [InlineData("m4a", "aac")]
    [InlineData("flac", "flac")]
    [InlineData("ogg", "vorbis")]
    [InlineData("opus", "opus")]
    public async Task Wav_converts_to_each_audio_format(string targetId, string expectedCodec)
    {
        string output = await ConvertAsync(fixtures.Wav, MediaFormats.FromName(targetId)!);
        Assert.Equal([expectedCodec], await fixtures.CodecsAsync(output));
    }

    [Fact]
    public async Task Mp3_converts_back_to_wav()
    {
        string mp3 = await ConvertAsync(fixtures.Wav, MediaFormats.Mp3);
        string wav = await ConvertAsync(mp3, MediaFormats.Wav);
        Assert.Equal(["pcm_s16le"], await fixtures.CodecsAsync(wav));
    }

    [Fact]
    public async Task Mp4_converts_to_webm_with_vp9_and_opus()
    {
        string output = await ConvertAsync(fixtures.Mp4, MediaFormats.WebM);
        Assert.Equal(["vp9", "opus"], await fixtures.CodecsAsync(output));
    }

    [Fact]
    public async Task Webm_converts_to_mp4_with_h264_and_aac()
    {
        string webm = await ConvertAsync(fixtures.Mp4, MediaFormats.WebM);
        string mp4 = await ConvertAsync(webm, MediaFormats.Mp4);
        Assert.Equal(["h264", "aac"], await fixtures.CodecsAsync(mp4));
    }

    [Fact]
    public async Task Video_converts_to_gif()
    {
        string output = await ConvertAsync(fixtures.Mp4, MediaFormats.Gif);
        Assert.Equal(["gif"], await fixtures.CodecsAsync(output));
    }

    [Theory]
    [InlineData("mp3", "mp3")]
    [InlineData("m4a", "aac")]
    public async Task Audio_is_extracted_from_video(string targetId, string expectedCodec)
    {
        string output = await ConvertAsync(fixtures.Mp4, MediaFormats.FromName(targetId)!);
        Assert.Equal([expectedCodec], await fixtures.CodecsAsync(output));
    }

    [Fact]
    public async Task Animated_gif_converts_to_mp4()
    {
        string output = await ConvertAsync(fixtures.AnimatedGif, MediaFormats.Mp4);
        Assert.Equal(["h264"], await fixtures.CodecsAsync(output));
    }

    [Fact]
    public async Task Progress_is_reported_from_ffmpeg_output()
    {
        var values = new List<double>();
        var progress = new SynchronousProgress(v =>
        {
            lock (values)
            {
                values.Add(v);
            }
        });

        await ConvertAsync(fixtures.LongMp4, MediaFormats.WebM, progress);

        Assert.Contains(values, v => v > 0 && v < 1);
        Assert.All(values, v => Assert.InRange(v, 0, 1));
    }

    [Fact]
    public async Task Cancellation_stops_ffmpeg_promptly()
    {
        FfmpegProvider provider = Provider();
        string output = fixtures.OutputPath("cancelled.webm");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.ConvertAsync(new ConversionJob(fixtures.LongMp4, output, MediaFormats.WebM, NoOptions), null, cts.Token));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Took {stopwatch.Elapsed} to cancel");
    }

    [Fact]
    public async Task Corrupt_input_throws_a_one_line_reason()
    {
        FfmpegProvider provider = Provider();
        var job = new ConversionJob(fixtures.CorruptMp4, fixtures.OutputPath("out.webm"), MediaFormats.WebM, NoOptions);

        var ex = await Assert.ThrowsAsync<ConversionException>(() => provider.ConvertAsync(job, null, TestContext.Current.CancellationToken));

        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
        Assert.DoesNotContain('\n', ex.Message);
    }

    [Fact]
    public void Missing_ffmpeg_makes_the_provider_unavailable()
    {
        var provider = new FfmpegProvider(@"C:\nowhere\ffmpeg.exe");
        Assert.False(provider.IsAvailable);
        Assert.Empty(new FormatCatalog([provider]).TargetsFor("song.wav"));
    }

    [Fact]
    public void Capabilities_never_convert_a_format_to_itself() =>
        Assert.DoesNotContain(new FfmpegProvider(null).Capabilities, c => c.Input == c.Target);

    /// <summary>Progress&lt;T&gt; posts to a sync context; tests need the callbacks inline.</summary>
    private sealed class SynchronousProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}

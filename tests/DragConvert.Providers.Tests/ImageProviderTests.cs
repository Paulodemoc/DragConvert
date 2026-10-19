using DragConvert.Core.Conversion;
using DragConvert.Providers.Image;
using ImageMagick;

namespace DragConvert.Providers.Tests;

public sealed class ImageProviderTests(MediaFixtures fixtures) : IClassFixture<MediaFixtures>
{
    private static readonly IReadOnlyDictionary<string, string> NoOptions = new Dictionary<string, string>();
    private readonly ImageProvider _provider = new();

    private async Task<string> ConvertAsync(string input, MediaFormat target)
    {
        string output = fixtures.OutputPath("out" + target.Extension);
        await _provider.ConvertAsync(new ConversionJob(input, output, target, NoOptions), null, TestContext.Current.CancellationToken);
        Assert.True(File.Exists(output), $"No output written for {target}");
        return output;
    }

    [Theory]
    [InlineData("jpeg", MagickFormat.Jpeg)]
    [InlineData("webp", MagickFormat.WebP)]
    [InlineData("tiff", MagickFormat.Tiff)]
    [InlineData("bmp", MagickFormat.Bmp)]
    [InlineData("gif", MagickFormat.Gif)]
    public async Task Png_converts_to_each_raster_target_with_the_same_size(string targetId, MagickFormat expected)
    {
        string output = await ConvertAsync(fixtures.TransparentPng, MediaFormats.FromName(targetId)!);

        var info = new MagickImageInfo(output);
        Assert.Equal(expected, info.Format);
        Assert.Equal(64u, info.Width);
        Assert.Equal(48u, info.Height);
    }

    [Fact]
    public async Task Png_converts_to_pdf()
    {
        string output = await ConvertAsync(fixtures.TransparentPng, MediaFormats.Pdf);
        byte[] header = new byte[5];
        await using (FileStream stream = File.OpenRead(output))
        {
            await stream.ReadExactlyAsync(header, TestContext.Current.CancellationToken);
        }

        Assert.Equal("%PDF-"u8.ToArray(), header);
    }

    [Fact]
    public async Task Heic_converts_to_jpeg()
    {
        string output = await ConvertAsync(fixtures.Heic, MediaFormats.Jpeg);

        var info = new MagickImageInfo(output);
        Assert.Equal(MagickFormat.Jpeg, info.Format);
        Assert.Equal(64u, info.Width);
        Assert.Equal(48u, info.Height);
    }

    [Fact]
    public async Task Transparency_becomes_white_in_jpeg()
    {
        string output = await ConvertAsync(fixtures.TransparentPng, MediaFormats.Jpeg);

        using var image = new MagickImage(output);
        IMagickColor<byte>? corner = image.GetPixels().GetPixel(0, 0).ToColor();
        Assert.NotNull(corner);
        Assert.True(corner.R > 245 && corner.G > 245 && corner.B > 245, $"Corner was {corner}, expected white");
    }

    [Fact]
    public async Task Exif_rotation_is_applied_to_the_pixels()
    {
        string output = await ConvertAsync(fixtures.RotatedJpeg, MediaFormats.Png);

        var info = new MagickImageInfo(output);
        Assert.Equal(48u, info.Width);
        Assert.Equal(64u, info.Height);
    }

    [Fact]
    public async Task Animated_gif_keeps_its_frames_in_webp()
    {
        string output = await ConvertAsync(fixtures.AnimatedGif, MediaFormats.WebP);

        using var frames = new MagickImageCollection(output);
        Assert.Equal(3, frames.Count);
    }

    [Fact]
    public async Task Animated_gif_to_png_uses_the_first_frame()
    {
        string output = await ConvertAsync(fixtures.AnimatedGif, MediaFormats.Png);
        using var frames = new MagickImageCollection(output);
        Assert.Single(frames);
    }

    [Fact]
    public async Task Source_file_is_not_modified()
    {
        byte[] before = await File.ReadAllBytesAsync(fixtures.TransparentPng, TestContext.Current.CancellationToken);
        await ConvertAsync(fixtures.TransparentPng, MediaFormats.WebP);
        Assert.Equal(before, await File.ReadAllBytesAsync(fixtures.TransparentPng, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Corrupt_input_throws_a_conversion_exception()
    {
        string output = fixtures.OutputPath("out.jpg");
        var job = new ConversionJob(fixtures.Corrupt, output, MediaFormats.Jpeg, NoOptions);
        var ex = await Assert.ThrowsAsync<ConversionException>(() => _provider.ConvertAsync(job, null, TestContext.Current.CancellationToken));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
        Assert.DoesNotContain('\n', ex.Message);
    }

    [Fact]
    public void Capabilities_never_target_heic_or_the_same_format()
    {
        Assert.DoesNotContain(_provider.Capabilities, c => c.Target == MediaFormats.Heic);
        Assert.DoesNotContain(_provider.Capabilities, c => c.Input == c.Target);
        Assert.Contains(_provider.Capabilities, c => c.Input == MediaFormats.Heic && c.Target == MediaFormats.Jpeg);
    }
}

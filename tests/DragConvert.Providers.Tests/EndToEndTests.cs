using DragConvert.Core.Conversion;
using ImageMagick;

namespace DragConvert.Providers.Tests;

/// <summary>The real providers behind the catalog and job queue, as the app and CLI use them.</summary>
public sealed class EndToEndTests(MediaFixtures fixtures) : IClassFixture<MediaFixtures>
{
    [Fact]
    public async Task Heic_to_jpeg_lands_beside_the_original_with_no_temp_files_left()
    {
        string folder = Path.Combine(fixtures.Folder, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string input = Path.Combine(folder, "IMG_0001.heic");
        File.Copy(fixtures.Heic, input);

        using var queue = new JobQueue(new FormatCatalog(DefaultProviders.Create()));
        ConversionResult result = await queue.ConvertAsync(input, MediaFormats.Jpeg, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        Assert.Equal(Path.Combine(folder, "IMG_0001.jpg"), result.OutputPath);
        Assert.Equal(MagickFormat.Jpeg, new MagickImageInfo(result.OutputPath!).Format);
        Assert.Equal(["IMG_0001.heic", "IMG_0001.jpg"], Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Catalog_offers_image_targets_for_heic_and_audio_targets_for_wav()
    {
        var catalog = new FormatCatalog(DefaultProviders.Create(fixtures.Ffmpeg));

        Assert.Equal(
            [MediaFormats.Jpeg, MediaFormats.Png, MediaFormats.WebP, MediaFormats.Tiff, MediaFormats.Bmp, MediaFormats.Gif, MediaFormats.Pdf],
            catalog.TargetsFor("photo.heic"));

        Assert.SkipWhen(fixtures.Ffmpeg is null, "ffmpeg not found");
        Assert.Equal(
            [MediaFormats.Mp3, MediaFormats.M4a, MediaFormats.Flac, MediaFormats.Ogg, MediaFormats.Opus],
            catalog.TargetsFor("voice.wav"));
    }
}

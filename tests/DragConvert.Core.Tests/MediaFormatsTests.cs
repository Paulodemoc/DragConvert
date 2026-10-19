using DragConvert.Core.Conversion;

namespace DragConvert.Core.Tests;

public class MediaFormatsTests
{
    [Theory]
    [InlineData(@"C:\photos\IMG_001.JPG", "jpeg")]
    [InlineData("scan.tiff", "tiff")]
    [InlineData("clip.m4v", "mp4")]
    [InlineData(".heif", "heic")]
    [InlineData("flac", "flac")]
    public void FromPath_maps_every_extension_alias(string path, string expectedId) =>
        Assert.Equal(expectedId, MediaFormats.FromPath(path)?.Id);

    [Fact]
    public void FromPath_returns_null_for_unknown_types() => Assert.Null(MediaFormats.FromPath("notes.xyz"));

    [Theory]
    [InlineData("jpeg", "jpeg")]
    [InlineData("JPG", "jpeg")]
    [InlineData("WebP", "webp")]
    public void FromName_accepts_ids_names_and_extensions(string name, string expectedId) =>
        Assert.Equal(expectedId, MediaFormats.FromName(name)?.Id);

    [Fact]
    public void Output_extension_is_the_first_listed() => Assert.Equal(".jpg", MediaFormats.Jpeg.Extension);

    [Fact]
    public void Extensions_are_unique_across_formats()
    {
        var all = MediaFormats.All.SelectMany(f => f.Extensions).ToList();
        Assert.Equal(all.Count, all.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}

using DragConvert.Core.Conversion;
using static DragConvert.Core.Conversion.MediaFormats;

namespace DragConvert.Core.Tests;

public class FormatCatalogTests
{
    private static readonly FakeProvider Images = new(
        "image",
        new ConversionCapability(Heic, Jpeg),
        new ConversionCapability(Heic, Png),
        new ConversionCapability(Png, Jpeg),
        new ConversionCapability(Png, WebP));

    private static readonly FakeProvider Audio = new("audio", new ConversionCapability(Wav, Mp3));

    [Fact]
    public void Targets_depend_on_the_input_type()
    {
        var catalog = new FormatCatalog([Images, Audio]);
        Assert.Equal([Jpeg, Png], catalog.TargetsFor("photo.HEIC"));
        Assert.Equal([Mp3], catalog.TargetsFor("voice.wav"));
    }

    [Fact]
    public void Unknown_types_have_no_targets() => Assert.Empty(new FormatCatalog([Images]).TargetsFor("readme.txt"));

    [Fact]
    public void Multi_file_drags_get_only_the_targets_valid_for_all()
    {
        var catalog = new FormatCatalog([Images, Audio]);
        Assert.Equal([Jpeg], catalog.CommonTargetsFor(["a.heic", "b.png"]));
        Assert.Empty(catalog.CommonTargetsFor(["a.heic", "b.wav"]));
    }

    [Fact]
    public void Target_options_list_every_target_with_counts_and_all_file_targets_first()
    {
        var video = new FakeProvider("video", new ConversionCapability(Mp4, Gif), new ConversionCapability(Mp4, Mp3));
        var images = new FakeProvider("img", new ConversionCapability(Png, Jpeg), new ConversionCapability(Png, Gif));
        var catalog = new FormatCatalog([images, video]);

        IReadOnlyList<TargetOption> options = catalog.TargetOptionsFor(["a.png", "b.png", "c.png", "d.mp4", "e.mp4"]);

        Assert.Equal(
            [new TargetOption(Gif, 5, 5), new TargetOption(Jpeg, 3, 5), new TargetOption(Mp3, 2, 5)],
            options);
        Assert.True(options[0].AppliesToAll);
        Assert.False(options[1].AppliesToAll);
    }

    [Fact]
    public void Files_with_no_conversions_still_count_toward_the_total()
    {
        var catalog = new FormatCatalog([Images]);
        TargetOption only = Assert.Single(catalog.TargetOptionsFor(["a.png", "notes.docx"]), o => o.Target == Jpeg);
        Assert.Equal(1, only.FileCount);
        Assert.Equal(2, only.TotalFiles);
    }

    [Fact]
    public void Unavailable_providers_are_ignored()
    {
        var missingEngine = new FakeProvider("ffmpeg", new ConversionCapability(Wav, Mp3)) { IsAvailable = false };
        var catalog = new FormatCatalog([missingEngine]);
        Assert.Empty(catalog.TargetsFor("voice.wav"));
        Assert.Null(catalog.FindProvider("voice.wav", Mp3));
    }

    [Fact]
    public void First_registered_provider_wins_a_shared_route()
    {
        var other = new FakeProvider("other", new ConversionCapability(Png, Jpeg));
        Assert.Same(Images, new FormatCatalog([Images, other]).FindProvider("x.png", Jpeg));
    }
}

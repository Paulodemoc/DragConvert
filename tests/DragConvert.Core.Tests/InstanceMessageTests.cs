using DragConvert.Core.Conversion;
using DragConvert.Core.Instance;
using static DragConvert.Core.Conversion.MediaFormats;

namespace DragConvert.Core.Tests;

public class InstanceMessageTests
{
    [Fact]
    public void Open_message_round_trips_paths_with_spaces_and_unicode()
    {
        var message = InstanceMessage.Open([@"C:\My Photos\Férias 2026.heic", @"D:\a.mp4"]);
        InstanceMessage? parsed = InstanceMessage.Parse(message.Serialize());
        Assert.NotNull(parsed);
        Assert.Equal(InstanceMessage.OpenCommand, parsed.Command);
        Assert.Equal(message.Paths, parsed.Paths);
    }

    [Fact]
    public void Serialized_message_is_a_single_line() => Assert.DoesNotContain('\n', InstanceMessage.Open(["a", "b"]).Serialize());

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("{\"Command\":\"\"}")]
    public void Invalid_lines_parse_to_null(string? line) => Assert.Null(InstanceMessage.Parse(line));

    [Fact]
    public void Plan_splits_files_by_whether_they_can_become_the_target()
    {
        var catalog = new FormatCatalog([new FakeProvider("img", new ConversionCapability(Png, Jpeg))]);
        DropPlan plan = catalog.Plan(["a.png", "b.mp4", "c.PNG", "d.docx"], Jpeg);
        Assert.Equal(["a.png", "c.PNG"], plan.Convert);
        Assert.Equal(["b.mp4", "d.docx"], plan.Skipped);
    }
}
